namespace GHelper.Linux.Helpers;

/// <summary>
/// Temperature display formatting. Celsius by default, Fahrenheit when the
/// "fahrenheit" config is set (toggle in Extra Settings).
/// </summary>
public static class TempHelper
{
    public static bool IsFahrenheit => AppConfig.Is("fahrenheit");

    public static double CelsiusToFahrenheit(double c) => c * 9.0 / 5.0 + 32.0;

    public static string FormatTemp(double celsius)
    {
        return IsFahrenheit
            ? Math.Round(CelsiusToFahrenheit(celsius)) + "°F"
            : Math.Round(celsius) + "°C";
    }
}
