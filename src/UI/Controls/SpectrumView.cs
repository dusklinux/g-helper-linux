using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// Live spectrum visualizer rendering log-magnitude dB bins as vertical bars.
/// Input range is expected to be -80..0 dB. Bars span the full width.
/// </summary>
public sealed class SpectrumView : Control
{
    public float[]? Bins { get; set; }
    public IBrush Fill { get; set; } = new SolidColorBrush(Color.Parse("#50C878"));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        context.FillRectangle(new SolidColorBrush(Color.Parse("#101418")),
                              new Rect(0, 0, w, h));

        var bins = Bins;
        if (bins == null || bins.Length == 0)
            return;

        double barW = w / bins.Length;
        for (int i = 0; i < bins.Length; i++)
        {
            // map -80..0 dB -> 0..h
            float db = bins[i];
            if (db < -80f)
                db = -80f;
            if (db > 0f)
                db = 0f;
            double t = (db + 80.0) / 80.0;
            double barH = h * t;
            double x = i * barW;
            context.FillRectangle(Fill,
                new Rect(x + 0.5, h - barH, barW - 1.0, barH));
        }
    }
}
