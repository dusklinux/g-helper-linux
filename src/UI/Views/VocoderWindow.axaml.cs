using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Per-effect mini-window for the vocoder. Sliders cover carrier pitch,
/// detune, envelope attack/release and wet/dry mix. Pattern matches the
/// delay/reverb mini-windows: hydrate from <see cref="AudioState"/>,
/// push every change through <see cref="AudioHelper"/> (which also
/// writes through to AudioState + AppConfig).
///
/// The "Follow voice pitch" toggle reshapes the carrier slider in place:
/// when off it's the fixed carrier frequency 50..440 Hz, when on it
/// becomes a -24..+24 semitone transposition relative to the tracked
/// voice pitch (0 = robot at your pitch).
/// </summary>
public partial class VocoderWindow : Window
{
    private bool _loading = true;

    /// <summary>
    /// One canned voice. A preset stamps the entire voice pipeline -
    /// vocoder bands, pitch shifter, autotune snap, bit-crusher - so the
    /// listener actually hears the named character instead of just the
    /// raw vocoder timbre. Three DSP layers cooperate:
    ///
    ///   - vocoder (Mix &gt; 0): formant carrier voices (Daft Punk,
    ///     Kraftwerk, Matrix, Cylon, Hawking)
    ///   - pitch shifter / autotune: time-domain re-pitching of the dry
    ///     voice (Vader = -5 st, Chipmunk = +12 st, Cher = chromatic snap)
    ///   - bit-crusher: low-rate ADC and amplitude quantise for lo-fi
    ///     radio characters (Robot Phone)
    ///
    /// Each preset turns the layers it needs on and leaves the rest in
    /// bypass, so the output is the character the name promises.
    /// </summary>
    private readonly record struct VocoderPreset(
        string Name,
        int Mix,                       // 0..1000 per-mille (vocoder dry/wet)
        int CarrierHz,                 // 50..440 (only used when Follow=false)
        int Detune,                    // 0..200 per-mille
        int AttackMs,                  // 1..100
        int ReleaseMs,                 // 5..500
        bool Follow,
        int VocoderPitchShift,         // -24..+24 (only used when Follow=true)
        int PitchShiftCentisemis,      // -2400..+2400 dry-voice pitch shift
        bool Autotune,                 // pitch-shifter driven by autotune
        int AutotuneTargetHz,          // 0 = chromatic snap, >0 = monotone
        int BitcrushBits,              // 0 = bypass, 1..15 levels
        int BitcrushDownsample,        // 1 = no downsampling
        int MatrixIntensity,           // 0 = clean carrier .. 1000 = full Sentinel
        int BandpassHpfHz,             // 0 = bypass; e.g. 300 for telephone
        int BandpassLpfHz,             // 0 = bypass; e.g. 3000 for muffled
        int StutterHz                  // 0 = bypass; 4..8 for Cylon cadence
    );

    private static readonly VocoderPreset[] Presets =
    [
        // Daft Punk - "Around the World" / "Get Lucky": carrier vocoder
        // following the singer's melody. Clean two-osc carrier (no ring
        // mod), tight detune, fast envelopes - the EMS-Vocoder-tracking-
        // a-melodic-line sound. No band-pass; the vocoder bands themselves
        // shape the spectrum.
        //                     mix car  det atk rel  follow vshift psh   AT  ATtgt bcb bcds  matrix  hpf  lpf  stut
        new("Daft Punk",       900, 110,  50,  2,  12, true,    0,     0, false,    0,  0,    1,    150,    0,    0,   0),

        // Chipmunk - tape sped up 2x = +12 st pitch with formants riding
        // along. Pure granular shifter (no formant preservation), no
        // vocoder. Light HPF trims the chest rumble that doesn't survive
        // an octave-up shift sensibly.
        new("Chipmunk",          0, 110,   0,  2,  15, false,   0,  1200, false,    0,  0,    1,      0,  150,    0,   0),

        // Cylon - Battlestar Galactica machine voice. The signature isn't
        // just the vocoder; it's the staccato "By Your Command" chopping.
        // Stutter gate at 6 Hz (slightly fast for menace) plus medium
        // Matrix intensity gives the rhythmic robot speech the name
        // promises. Fixed deep carrier, slow envelope so each chop sustains.
        new("Cylon",           900,  90, 160, 10,  70, false,   0,     0, false,    0,  0,    1,    450,    0,    0,   6),

        // Darth Vader - James Earl Jones pitched down + telephone helmet
        // muffle + breathing track. We can't add breathing in real-time,
        // but pitch shift -5 st combined with a 2500 Hz LPF reproduces
        // the "speaking through a helmet" character. Vocoder OFF - Vader
        // is the speaker, not a robot. Slight HPF (80 Hz) trims the
        // overshift rumble.
        new("Darth Vader",       0, 110,   0,  5,  30, false,   0,  -500, false,    0,  0,    1,      0,   80, 2500,   0),

        // Kraftwerk - "Trans-Europe Express" pure-analog EMS Vocoder. Clean
        // saw+square carrier (Matrix = 0 - no ring mod, no tanh drive),
        // fixed mid carrier at 140 Hz, very tight detune, snappy envelope.
        // Mostly wet so the formants dominate.
        new("Kraftwerk",       950, 140,  10,  3,  15, false,   0,     0, false,    0,  0,    1,      0,    0,    0,   0),

        // Matrix Agent - Sentinel comm-rig. Full Matrix-intensity stack
        // (ring mod + sub-octave + tanh drive + tremolo) on a deep 70 Hz
        // carrier with dry-voice pitched down -2 st for menace. Telephony
        // band-pass (300-3400 Hz) makes it sound like it is coming over
        // a comm channel.
        new("Matrix Agent",    900,  70,  90, 10,  55, false,   0,  -200, false,    0,  0,    1,   1000,  300, 3400,   0),

        // Robot Phone - the canonical "speaking through a real telephone":
        // 300-3400 Hz telephone band + 8-bit / 2x downsample crush for the
        // µ-law-ish quantisation noise. No vocoder, no shift - this is a
        // signal-chain effect, not a synthesised voice.
        new("Robot Phone",       0, 110,   0,  5,  25, false,   0,     0, false,    0,  8,    2,      0,  300, 3400,   0),

        // Sci-Fi Alien - "almost forming words". Pitch shift +7 st (perfect
        // fifth up) + heavy Matrix vocoder + tinny-radio band-pass
        // (700-3200 Hz) so vowels resemble articulated buzz rather than
        // human speech.
        new("Sci-Fi Alien",    500, 110, 130,  2,  10, false,   0,   700, false,    0,  0,    1,    700,  700, 3200,   0),
    ];

    public VocoderWindow()
    {
        InitializeComponent();
        ApplyLabels();
        comboPreset.ItemsSource = Presets.Select(p => p.Name).ToList();
        comboPreset.SelectedIndex = -1;
        HydrateFromState();
        Labels.LanguageChanged += OnLanguageChanged;
        AudioHelper.Instance.FrameReceived += OnFrame;
        Closed += (_, _) =>
        {
            Labels.LanguageChanged -= OnLanguageChanged;
            AudioHelper.Instance.FrameReceived -= OnFrame;
        };
        _loading = false;
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(ApplyLabels);

    private void ApplyLabels()
    {
        Title = Labels.Get("audio_vocoder_title");
        labelHeader.Text = Labels.Get("audio_vocoder_title");
        labelDescription.Text = Labels.Get("audio_vocoder_description");
        labelPresetHeader.Text = Labels.Get("audio_vocoder_preset");
        labelDryWet.Text = Labels.Get("audio_vocoder_drywet");
        labelDetuneHeader.Text = Labels.Get("audio_vocoder_detune");
        labelAttackHeader.Text = Labels.Get("audio_vocoder_attack");
        labelReleaseHeader.Text = Labels.Get("audio_vocoder_release");
        buttonReset.Content = Labels.Get("audio_reset");
        checkFollow.Content = Labels.Get("audio_vocoder_follow");
        ToolTip.SetTip(checkFollow, Labels.Get("audio_vocoder_follow_tooltip"));
        // Carrier-slider header depends on whether follow is on. Defer
        // to ConfigureCarrierSlider which is also called when the toggle
        // flips, so the label tracks the slider mode.
        ConfigureCarrierSlider(AudioState.Instance.VocoderFollow);
    }

    private void HydrateFromState()
    {
        _loading = true;
        var s = AudioState.Instance;
        sliderDetune.Value = s.VocoderDetune;
        sliderAttack.Value = s.VocoderAttackMs;
        sliderRelease.Value = s.VocoderReleaseMs;
        sliderMix.Value = s.VocoderMix;
        checkFollow.IsChecked = s.VocoderFollow;
        // ConfigureCarrierSlider sets Min/Max + initial slider value to
        // either VocoderCarrierHz or VocoderPitchShift depending on mode.
        ConfigureCarrierSlider(s.VocoderFollow);
        UpdateLabels(s.VocoderDetune, s.VocoderAttackMs, s.VocoderReleaseMs, s.VocoderMix);
        _loading = false;
    }

    /// <summary>
    /// Reconfigure the carrier slider in place to either Hz mode (50..440)
    /// or semitone mode (-24..+24). Suppresses ValueChanged callbacks
    /// during the swap so we don't accidentally push a wrong value to the
    /// helper.
    /// </summary>
    private void ConfigureCarrierSlider(bool follow)
    {
        bool wasLoading = _loading;
        _loading = true;
        var s = AudioState.Instance;
        if (follow)
        {
            sliderCarrier.Minimum = -24;
            sliderCarrier.Maximum = 24;
            sliderCarrier.TickFrequency = 1;
            sliderCarrier.Value = s.VocoderPitchShift;
            labelCarrierHeader.Text = Labels.Get("audio_vocoder_transpose");
            labelCarrier.Text = FormatSemitones(s.VocoderPitchShift);
        }
        else
        {
            sliderCarrier.Minimum = 50;
            sliderCarrier.Maximum = 440;
            sliderCarrier.TickFrequency = 10;
            sliderCarrier.Value = s.VocoderCarrierHz;
            labelCarrierHeader.Text = Labels.Get("audio_vocoder_carrier");
            labelCarrier.Text = $"{s.VocoderCarrierHz} Hz";
        }
        _loading = wasLoading;
    }

    private static string FormatSemitones(int s)
    {
        if (s == 0)
            return "0 st";
        return (s > 0 ? "+" : "") + s + " st";
    }

    private void UpdateLabels(int det, int atk, int rel, int mix)
    {
        labelDetune.Text = $"{det / 10}%";
        labelAttack.Text = $"{atk} ms";
        labelRelease.Text = $"{rel} ms";
        labelMix.Text = $"{mix / 10}%";
    }

    /// <summary>
    /// Handler for every slider except the carrier slider (which has its
    /// own handler because it changes semantic with follow mode). Also
    /// clears any active preset selection so the dropdown does not lie
    /// about the user's tweaked state.
    /// </summary>
    private void OnChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || labelCarrier == null)
            return;
        ClearPresetSelection();
        PushFullVocoderState();
    }

    /// <summary>
    /// Carrier slider: interpret the current value either as Hz (when
    /// follow is off) or as a semitone shift (when follow is on), then
    /// push the full param set.
    /// </summary>
    private void SliderCarrier_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || labelCarrier == null)
            return;
        var s = AudioState.Instance;
        if (s.VocoderFollow)
        {
            int shift = (int)sliderCarrier.Value;
            s.VocoderPitchShift = shift;
            labelCarrier.Text = FormatSemitones(shift);
        }
        else
        {
            int hz = (int)sliderCarrier.Value;
            s.VocoderCarrierHz = hz;
            labelCarrier.Text = $"{hz} Hz";
        }
        ClearPresetSelection();
        PushFullVocoderState();
    }

    private void CheckFollow_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        bool follow = checkFollow.IsChecked == true;
        AudioState.Instance.VocoderFollow = follow;
        // Reconfigure the slider so the user sees Hz <-> semitones swap.
        ConfigureCarrierSlider(follow);
        ClearPresetSelection();
        PushFullVocoderState();
    }

    /// <summary>
    /// Preset combo selection. Pulls the canned values into AudioState,
    /// reflects them in the sliders, and pushes the full parameter set
    /// to the helper in one go. Suppresses the slider ValueChanged
    /// callbacks via <see cref="_loading"/> so they do not clobber the
    /// selection mid-apply.
    /// </summary>
    private void ComboPreset_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        int idx = comboPreset.SelectedIndex;
        if (idx < 0 || idx >= Presets.Length)
            return;
        ApplyPreset(Presets[idx]);
    }

    private void ApplyPreset(VocoderPreset p)
    {
        _loading = true;
        var s = AudioState.Instance;
        s.VocoderMix = p.Mix;
        s.VocoderCarrierHz = p.CarrierHz;
        s.VocoderDetune = p.Detune;
        s.VocoderAttackMs = p.AttackMs;
        s.VocoderReleaseMs = p.ReleaseMs;
        s.VocoderFollow = p.Follow;
        s.VocoderPitchShift = p.VocoderPitchShift;
        // Voice-effects layer (pitch shifter / autotune / bitcrush /
        // band-pass / stutter) - each preset turns on only the layers it
        // actually needs. The defaults (0 / false) bypass each layer.
        s.VoicePitchShiftCentisemis = p.PitchShiftCentisemis;
        s.VoiceAutotuneOn = p.Autotune;
        s.VoiceAutotuneTargetHz = p.AutotuneTargetHz;
        s.VoiceBitcrushBits = p.BitcrushBits;
        s.VoiceBitcrushDownsample = p.BitcrushDownsample;
        s.VocoderMatrixIntensity = p.MatrixIntensity;
        s.VoiceBandpassHpfHz = p.BandpassHpfHz;
        s.VoiceBandpassLpfHz = p.BandpassLpfHz;
        s.VoiceStutterHz = p.StutterHz;
        s.VoiceStutterDutyMille = 500; // default duty; presets are square

        sliderMix.Value = p.Mix;
        sliderDetune.Value = p.Detune;
        sliderAttack.Value = p.AttackMs;
        sliderRelease.Value = p.ReleaseMs;
        checkFollow.IsChecked = p.Follow;
        // ConfigureCarrierSlider rebinds the carrier slider's min/max
        // and reads the right field (Hz vs semitones) from AudioState.
        ConfigureCarrierSlider(p.Follow);
        UpdateLabels(p.Detune, p.AttackMs, p.ReleaseMs, p.Mix);
        _loading = false;

        // Push the full state in one batch. Vocoder + voice-effects layers
        // are independent on the wire (different helper commands), so the
        // order does not matter; the helper applies them atomically per
        // sample on the next callback.
        var helper = AudioHelper.Instance;
        helper.SetVocoder(p.Mix, p.CarrierHz, p.AttackMs, p.ReleaseMs,
                          p.Detune, p.Follow, p.VocoderPitchShift);
        helper.SetPitchShift(p.PitchShiftCentisemis);
        helper.SetAutotune(p.Autotune);
        helper.SetVoiceAutotuneTarget(p.AutotuneTargetHz);
        helper.SetBitcrush(p.BitcrushBits, p.BitcrushDownsample);
        helper.SetVocoderMatrix(p.MatrixIntensity);
        helper.SetVoiceBandpass(p.BandpassHpfHz, p.BandpassLpfHz);
        helper.SetVoiceStutter(p.StutterHz, 500);
    }

    /// <summary>
    /// Drop the selected preset back to "no selection" without re-firing
    /// the SelectionChanged handler. Called whenever the user adjusts
    /// a slider or toggles follow so the dropdown does not claim a
    /// preset that no longer matches the live values.
    /// </summary>
    private void ClearPresetSelection()
    {
        if (comboPreset.SelectedIndex < 0)
            return;
        bool wasLoading = _loading;
        _loading = true;
        comboPreset.SelectedIndex = -1;
        _loading = wasLoading;
    }

    private void PushFullVocoderState()
    {
        var s = AudioState.Instance;
        // Read the non-carrier sliders here; carrier values are already
        // committed to AudioState by SliderCarrier_Changed / ConfigureCarrierSlider.
        int det = (int)sliderDetune.Value;
        int atk = (int)sliderAttack.Value;
        int rel = (int)sliderRelease.Value;
        int mix = (int)sliderMix.Value;
        UpdateLabels(det, atk, rel, mix);
        AudioHelper.Instance.SetVocoder(mix, s.VocoderCarrierHz, atk, rel, det,
                                        s.VocoderFollow, s.VocoderPitchShift);
    }

    private void OnFrame(AudioFrame f)
    {
        // Show the live tracked pitch next to the follow toggle so the
        // user can see whether the tracker has a lock. Hz only; helps
        // diagnose "robot sings wrong note" issues.
        Dispatcher.UIThread.Post(() =>
        {
            if (f.TrackedPitchHz > 1.0f)
                labelTrackedPitch.Text = $"~{f.TrackedPitchHz:F0} Hz";
            else
                labelTrackedPitch.Text = "-";
        });
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        AudioState.Instance.ResetVocoder();
        HydrateFromState();
        ClearPresetSelection();
        var s = AudioState.Instance;
        var helper = AudioHelper.Instance;
        helper.SetVocoder(s.VocoderMix, s.VocoderCarrierHz,
                          s.VocoderAttackMs, s.VocoderReleaseMs,
                          s.VocoderDetune,
                          s.VocoderFollow, s.VocoderPitchShift);
        // Voice-effects layer is reset by ResetVocoder; mirror that to the
        // helper so the user is not left in (say) Chipmunk-shifted state
        // after pressing the big Reset button.
        helper.SetPitchShift(s.VoicePitchShiftCentisemis);
        helper.SetAutotune(s.VoiceAutotuneOn);
        helper.SetVoiceAutotuneTarget(s.VoiceAutotuneTargetHz);
        helper.SetBitcrush(s.VoiceBitcrushBits, s.VoiceBitcrushDownsample);
        helper.SetVocoderMatrix(s.VocoderMatrixIntensity);
        helper.SetVoiceBandpass(s.VoiceBandpassHpfHz, s.VoiceBandpassLpfHz);
        helper.SetVoiceStutter(s.VoiceStutterHz, s.VoiceStutterDutyMille);
    }
}
