using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace GHelper.Linux.Helpers;

/// <summary>Color math utilities using Avalonia's <see cref="Color"/>.</summary>
public class ColorUtils
{
    /// <summary>Linear interpolation between two colors. Weight 0 = color1, 1 = color2.</summary>
    public static Color GetWeightedAverage(Color color1, Color color2, float weight)
    {
        int red = (int)Math.Round(color1.R * (1 - weight) + color2.R * weight);
        int green = (int)Math.Round(color1.G * (1 - weight) + color2.G * weight);
        int blue = (int)Math.Round(color1.B * (1 - weight) + color2.B * weight);

        red = Math.Clamp(red, 0, 255);
        green = Math.Clamp(green, 0, 255);
        blue = Math.Clamp(blue, 0, 255);

        return Color.FromRgb((byte)red, (byte)green, (byte)blue);
    }

    /// <summary>Midpoint average of two colors.</summary>
    public static Color GetMidColor(Color color1, Color color2)
    {
        return Color.FromRgb(
            (byte)((color1.R + color2.R) / 2),
            (byte)((color1.G + color2.G) / 2),
            (byte)((color1.B + color2.B) / 2));
    }

    /// <summary>HSV color model with RGB conversions. H, S, V are all in [0,1].</summary>
    public class HSV
    {
        public double Hue { get; set; }
        public double Saturation { get; set; }
        public double Value { get; set; }

        /// <summary>Convert to RGB.</summary>
        public Color ToRGB()
        {
            var hue = Hue * 6;
            var saturation = Saturation;
            var value = Value;

            double red, green, blue;

            if (saturation == 0)
            {
                red = green = blue = value;
            }
            else
            {
                var i = Convert.ToInt32(Math.Floor(hue));
                var f = hue - i;
                var p = value * (1 - saturation);
                var q = value * (1 - saturation * f);
                var t = value * (1 - saturation * (1 - f));
                int mod = i % 6;

                red = new[] { value, q, p, p, t, value }[mod];
                green = new[] { t, value, value, q, p, p }[mod];
                blue = new[] { p, p, t, value, value, q }[mod];
            }

            return Color.FromRgb(
                (byte)Convert.ToInt32(red * 255),
                (byte)Convert.ToInt32(green * 255),
                (byte)Convert.ToInt32(blue * 255));
        }

        /// <summary>Convert RGB to HSV.</summary>
        public static HSV ToHSV(Color rgb)
        {
            double red = rgb.R / 255.0;
            double green = rgb.G / 255.0;
            double blue = rgb.B / 255.0;
            var min = Math.Min(red, Math.Min(green, blue));
            var max = Math.Max(red, Math.Max(green, blue));
            var delta = max - min;
            double hue;
            double saturation = 0;
            var value = max;

            if (max != 0)
                saturation = delta / max;

            if (delta == 0)
                hue = 0;
            else
            {
                if (red == max)
                    hue = (green - blue) / delta + (green < blue ? 6 : 0);
                else if (green == max)
                    hue = 2 + (blue - red) / delta;
                else
                    hue = 4 + (red - green) / delta;

                hue /= 6;
            }

            return new HSV { Hue = hue, Saturation = saturation, Value = value };
        }

        /// <summary>Increase saturation of a color. No-op for grey (R==G==B).</summary>
        public static Color UpSaturation(Color rgb, float increase = 0.2f)
        {
            if (rgb.R == rgb.G && rgb.G == rgb.B)
                return rgb;
            var hsv = ToHSV(rgb);
            hsv.Saturation = Math.Min(hsv.Saturation + increase, 1.0);
            return hsv.ToRGB();
        }
    }

    /// <summary>Exponentially smoothed color for smooth transitions (e.g. ambient lighting).</summary>
    public class SmoothColor
    {
        public Color RGB
        {
            get { return Interpolate(); }
            set { _target = value; }
        }

        private Color Interpolate()
        {
            _current = ColorInterpolator.InterpolateBetween(_target, _current, _smooth);
            return _current;
        }

        private float _smooth = 0.65f;
        private Color _target = Colors.Black;
        private Color _current = Colors.Black;

        internal static class ColorInterpolator
        {
            delegate byte ComponentSelector(Color color);
            static readonly ComponentSelector _redSelector = color => color.R;
            static readonly ComponentSelector _greenSelector = color => color.G;
            static readonly ComponentSelector _blueSelector = color => color.B;

            public static Color InterpolateBetween(Color endPoint1, Color endPoint2, double lambda)
            {
                if (lambda < 0 || lambda > 1)
                    throw new ArgumentOutOfRangeException(nameof(lambda));

                if (endPoint1 != endPoint2)
                {
                    return Color.FromRgb(
                        InterpolateComponent(endPoint1, endPoint2, lambda, _redSelector),
                        InterpolateComponent(endPoint1, endPoint2, lambda, _greenSelector),
                        InterpolateComponent(endPoint1, endPoint2, lambda, _blueSelector));
                }

                return endPoint1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static byte InterpolateComponent(Color end1, Color end2, double lambda, ComponentSelector selector)
            {
                return (byte)(selector(end1) + (selector(end2) - selector(end1)) * lambda);
            }
        }
    }
}
