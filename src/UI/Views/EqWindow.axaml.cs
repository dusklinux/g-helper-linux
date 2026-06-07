using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Views;

public partial class EqWindow : Window
{
    private readonly DispatcherTimer _redraw;

    public EqWindow()
    {
        InitializeComponent();
        ApplyLabels();

        HydrateFromState();
        eqView.BandChanged += OnBandChanged;
        eqView.BandQChanged += OnBandChanged;
        eqView.PostGainChanged += OnPostGainChanged;
        AudioHelper.Instance.FrameReceived += OnFrame;
        Labels.LanguageChanged += OnLanguageChanged;

        _redraw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _redraw.Tick += (_, _) => eqView.InvalidateVisual();
        _redraw.Start();

        Closed += (_, _) =>
        {
            _redraw.Stop();
            AudioHelper.Instance.FrameReceived -= OnFrame;
            Labels.LanguageChanged -= OnLanguageChanged;
        };
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(ApplyLabels);

    private void ApplyLabels()
    {
        Title = Labels.Get("audio_eq_title");
        labelHint.Text = Labels.Get("audio_eq_hint");
        buttonReset.Content = Labels.Get("audio_reset");
        buttonBypass.Content = Labels.Get("audio_bypass");
    }

    private void HydrateFromState()
    {
        var st = AudioState.Instance;
        for (int i = 0; i < eqView.Bands.Length && i < st.EqBands.Length; i++)
        {
            var sb = st.EqBands[i];
            eqView.Bands[i] = (sb.Type, sb.FreqHz, sb.QMille, sb.GainCentiDb);
        }
        eqView.PostGainCentiDb = st.EqGainCentiDb;
    }

    private void OnFrame(AudioFrame f)
    {
        // Provide BOTH pre- and post-chain spectra so the response view can
        // overlay them with transparency, making the EQ's effect visible at
        // a glance.
        eqView.SpectrumIn = f.SpectrumIn;
        eqView.SpectrumOut = f.SpectrumOut;
    }

    private void OnBandChanged(int idx)
    {
        // Both drag and Q-scroll funnel through here: SetEqBand persists to
        // AudioState (which writes to AppConfig) and pushes to the helper.
        var b = eqView.Bands[idx];
        AudioHelper.Instance.SetEqBand(idx, b.type, b.freqHz, b.qMille, b.gainCentiDb);
    }

    private void OnPostGainChanged()
    {

        AudioHelper.Instance.SetEqGain(eqView.PostGainCentiDb);
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        AudioState.Instance.ResetEqToDefaults();
        HydrateFromState();
        // Push every band so the helper matches AudioState.
        for (int i = 0; i < eqView.Bands.Length; i++)
            OnBandChanged(i);

        AudioHelper.Instance.SetEqGain(0);
        eqView.InvalidateVisual();
    }

    private void ButtonBypass_Click(object? sender, RoutedEventArgs e)
    {
        // Flatten gains; leave types/frequencies intact so the user can
        // restore by dragging handles back up.
        for (int i = 0; i < eqView.Bands.Length; i++)
        {
            var b = eqView.Bands[i];
            b.gainCentiDb = 0;
            eqView.Bands[i] = b;
            OnBandChanged(i);
        }
        eqView.PostGainCentiDb = 0;
        AudioHelper.Instance.SetEqGain(0);
        eqView.InvalidateVisual();
    }
}
