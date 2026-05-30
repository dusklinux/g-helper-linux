using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// Lightweight rolling waveform visualizer. Set <see cref="Samples"/> and call
/// <see cref="InvalidateVisual"/> to redraw. The control draws a centered
/// zero line and a polyline of the supplied -1..+1 samples.
/// </summary>
public sealed class WaveformView : Control
{
    public float[]? Samples { get; set; }
    public IBrush Stroke { get; set; } = new SolidColorBrush(Color.Parse("#4CC2FF"));
    public IBrush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(40, 76, 194, 255));
    public IBrush ZeroLine { get; set; } = new SolidColorBrush(Color.FromArgb(60, 160, 160, 160));
    public double StrokeThickness { get; set; } = 1.4;

    public WaveformView()
    {
        // Hot signals (post-EQ, post-vocoder) can exceed |1.0| and the
        // polyline would otherwise paint outside the control bounds, bleeding
        // into the spectrum panel below. Clipping at the edge is honest:
        // the user sees the signal hit the ceiling but it stays put.
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        // background panel
        context.FillRectangle(new SolidColorBrush(Color.Parse("#101418")),
                              new Rect(0, 0, w, h));

        // zero line
        var midY = h * 0.5;
        var pen = new Pen(ZeroLine, 1);
        context.DrawLine(pen, new Point(0, midY), new Point(w, midY));

        var s = Samples;
        if (s == null || s.Length < 2)
            return;

        // polyline
        var strokePen = new Pen(Stroke, StrokeThickness);
        var prev = new Point(0, midY - s[0] * (h * 0.48));
        for (int i = 1; i < s.Length; i++)
        {
            var x = (double)i / (s.Length - 1) * w;
            var y = midY - s[i] * (h * 0.48);
            context.DrawLine(strokePen, prev, new Point(x, y));
            prev = new Point(x, y);
        }
    }
}
