using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Views;

public partial class ReverbWindow : Window
{
    private bool _loading = true;

    public ReverbWindow()
    {
        InitializeComponent();
        ApplyLabels();
        HydrateFromState();
        Labels.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Labels.LanguageChanged -= OnLanguageChanged;
        _loading = false;
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(ApplyLabels);

    private void ApplyLabels()
    {
        Title = Labels.Get("audio_reverb_title");
        labelHeader.Text = Labels.Get("audio_reverb_header");
        labelRoomHeader.Text = Labels.Get("audio_reverb_room");
        labelDampHeader.Text = Labels.Get("audio_reverb_damp");
        labelWidthHeader.Text = Labels.Get("audio_reverb_width");
        labelMixHeader.Text = Labels.Get("audio_mix");
        buttonReset.Content = Labels.Get("audio_reset");
    }

    private void HydrateFromState()
    {
        _loading = true;
        var s = AudioState.Instance;
        sliderRoom.Value = s.ReverbRoom;
        sliderDamp.Value = s.ReverbDamp;
        sliderWidth.Value = s.ReverbWidth;
        sliderMix.Value = s.ReverbMix;
        labelRoom.Text = $"{s.ReverbRoom / 10}%";
        labelDamp.Text = $"{s.ReverbDamp / 10}%";
        labelWidth.Text = $"{s.ReverbWidth / 10}%";
        labelMix.Text = $"{s.ReverbMix / 10}%";
        _loading = false;
    }

    private void OnChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || labelRoom == null)
            return;
        int rm = (int)sliderRoom.Value;
        int dp = (int)sliderDamp.Value;
        int wd = (int)sliderWidth.Value;
        int mx = (int)sliderMix.Value;
        labelRoom.Text = $"{rm / 10}%";
        labelDamp.Text = $"{dp / 10}%";
        labelWidth.Text = $"{wd / 10}%";
        labelMix.Text = $"{mx / 10}%";
        AudioHelper.Instance.SetReverb(rm, dp, wd, mx);
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        AudioState.Instance.ResetReverb();
        HydrateFromState();
        var s = AudioState.Instance;
        AudioHelper.Instance.SetReverb(s.ReverbRoom, s.ReverbDamp, s.ReverbWidth, s.ReverbMix);
    }
}
