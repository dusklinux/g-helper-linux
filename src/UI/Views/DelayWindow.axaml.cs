using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Views;

public partial class DelayWindow : Window
{
    private bool _loading = true;

    public DelayWindow()
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
        Title = Labels.Get("audio_delay_title");
        labelHeader.Text = Labels.Get("audio_delay_title");
        labelTimeHeader.Text = Labels.Get("audio_delay_time");
        labelFbHeader.Text = Labels.Get("audio_delay_feedback");
        labelMixHeader.Text = Labels.Get("audio_mix");
        buttonReset.Content = Labels.Get("audio_reset");
    }

    private void HydrateFromState()
    {
        _loading = true;
        var s = AudioState.Instance;
        sliderTime.Value = s.DelayMs;
        sliderFb.Value = s.DelayFeedback;
        sliderMix.Value = s.DelayMix;
        labelTime.Text = $"{s.DelayMs} ms";
        labelFb.Text = $"{s.DelayFeedback / 10}%";
        labelMix.Text = $"{s.DelayMix / 10}%";
        _loading = false;
    }

    private void OnChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || labelTime == null)
            return;
        int ms = (int)sliderTime.Value;
        int fb = (int)sliderFb.Value;
        int mix = (int)sliderMix.Value;
        labelTime.Text = $"{ms} ms";
        labelFb.Text = $"{fb / 10}%";
        labelMix.Text = $"{mix / 10}%";
        // SetDelay writes through to AudioState + AppConfig (persistence).
        AudioHelper.Instance.SetDelay(ms, fb, mix);
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        AudioState.Instance.ResetDelay();
        HydrateFromState();
        var s = AudioState.Instance;
        AudioHelper.Instance.SetDelay(s.DelayMs, s.DelayFeedback, s.DelayMix);
    }
}
