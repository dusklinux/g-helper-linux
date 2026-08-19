using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Dynamic theming support for Matugen (Material You color generator).
/// Reads generated colors from ~/.config/matugen/generated/hyprland-colors.lua
/// without hardcoding any user paths.
/// Falls back cleanly to standard G-Helper dark palette if the file is missing or unreadable.
/// Also watches for file changes at runtime to hot-reload colors when wallpaper/palette changes.
/// </summary>
public static class MatugenTheme
{
    private static FileSystemWatcher? _watcher;
    private static DateTime _lastReload = DateTime.MinValue;

    public static string GetConfigPath()
    {
        string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig))
        {
            string customPath = Path.Combine(xdgConfig, "matugen", "generated", "hyprland-colors.lua");
            if (File.Exists(customPath))
                return customPath;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "matugen", "generated", "hyprland-colors.lua");
    }

    public static void Initialize()
    {
        ApplyTheme();
        WatchConfigFile();
    }

    public static event Action? ThemeChanged;

    public static IBrush GetAccentBrush() =>
        Application.Current?.Resources["AccentColor"] as IBrush ?? new SolidColorBrush(Color.Parse("#4CC2FF"));

    public static Color GetAccentColor() =>
        Application.Current?.Resources["AccentColor"] is SolidColorBrush b ? b.Color : Color.Parse("#4CC2FF");

    public static IBrush GetWindowBackgroundBrush() =>
        Application.Current?.Resources["WindowBackground"] as IBrush ?? new SolidColorBrush(Color.Parse("#1C1C1C"));

    public static IBrush GetPanelBackgroundBrush() =>
        Application.Current?.Resources["PanelBackground"] as IBrush ?? new SolidColorBrush(Color.Parse("#262626"));

    public static IBrush GetTextForegroundBrush() =>
        Application.Current?.Resources["TextForeground"] as IBrush ?? new SolidColorBrush(Color.Parse("#F0F0F0"));

    public static IBrush GetTextDimBrush() =>
        Application.Current?.Resources["TextDim"] as IBrush ?? new SolidColorBrush(Color.Parse("#A0A0A0"));

    public static IBrush GetAccentForegroundBrush() =>
        Application.Current?.Resources["AccentForeground"] as IBrush ?? Brushes.Black;

    public static string GetAccentHex()
    {
        var c = GetAccentColor();
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static bool ApplyTheme()
    {
        var app = Application.Current;
        if (app == null)
            return false;

        string path = GetConfigPath();
        if (!File.Exists(path))
        {
            Logger.WriteLine($"Matugen: config not found at '{path}' - using default colors");
            ApplyFallback(app);
            ThemeChanged?.Invoke();
            return false;
        }

        try
        {
            var colors = ParseColorsFile(path);
            if (colors.Count == 0)
            {
                Logger.WriteLine("Matugen: no valid color entries found - using default colors");
                ApplyFallback(app);
                ThemeChanged?.Invoke();
                return false;
            }

            // Map Material Design 3 tokens to G-Helper UI elements
            var windowBg = colors.GetValueOrDefault("background", Color.Parse("#1C1C1C"));
            var panelBg = colors.GetValueOrDefault("surface_container",
                colors.GetValueOrDefault("surface", Color.Parse("#262626")));
            var buttonBg = colors.GetValueOrDefault("surface_container_high",
                colors.GetValueOrDefault("surface_variant", Color.Parse("#373737")));
            var buttonHover = colors.GetValueOrDefault("surface_container_highest",
                colors.GetValueOrDefault("surface_bright", Color.Parse("#454545")));
            var textFg = colors.GetValueOrDefault("on_surface",
                colors.GetValueOrDefault("on_background", Color.Parse("#F0F0F0")));
            var textDim = colors.GetValueOrDefault("on_surface_variant",
                colors.GetValueOrDefault("outline", Color.Parse("#A0A0A0")));
            var accent = colors.GetValueOrDefault("primary", Color.Parse("#4CC2FF"));
            var accentHover = colors.GetValueOrDefault("primary_fixed",
                colors.GetValueOrDefault("primary_fixed_dim", Color.Parse("#5FCDFF")));
            var accentFg = colors.GetValueOrDefault("on_primary",
                (accent.R * 0.299 + accent.G * 0.587 + accent.B * 0.114) > 150
                    ? Color.Parse("#101010") : Color.Parse("#FFFFFF"));
            var separator = colors.GetValueOrDefault("outline_variant", Color.Parse("#333333"));

            app.Resources["WindowBackground"] = new SolidColorBrush(windowBg);
            app.Resources["PanelBackground"] = new SolidColorBrush(panelBg);
            app.Resources["ButtonBackground"] = new SolidColorBrush(buttonBg);
            app.Resources["ButtonHover"] = new SolidColorBrush(buttonHover);
            app.Resources["TextForeground"] = new SolidColorBrush(textFg);
            app.Resources["TextDim"] = new SolidColorBrush(textDim);
            app.Resources["AccentColor"] = new SolidColorBrush(accent);
            app.Resources["AccentHover"] = new SolidColorBrush(accentHover);
            app.Resources["AccentForeground"] = new SolidColorBrush(accentFg);
            app.Resources["SeparatorColor"] = new SolidColorBrush(separator);

            // Fluent theme system resource keys
            app.Resources["SystemAccentColor"] = accent;
            app.Resources["SystemAccentColorLight1"] = accentHover;
            app.Resources["SystemAccentColorLight2"] = accentHover;
            app.Resources["SystemAccentColorLight3"] = accentHover;
            app.Resources["SystemAccentColorDark1"] = accent;
            app.Resources["SystemAccentColorDark2"] = accent;
            app.Resources["SystemAccentColorDark3"] = accent;

            app.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(accent);
            app.Resources["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
            app.Resources["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(Color.FromArgb(140, accent.R, accent.G, accent.B));
            app.Resources["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(accent);
            app.Resources["SystemControlHighlightAltAccentHighBrush"] = new SolidColorBrush(accentHover);

            app.Resources["ComboBoxDropDownBackground"] = new SolidColorBrush(panelBg);
            app.Resources["ComboBoxPopupBackground"] = new SolidColorBrush(panelBg);
            app.Resources["ComboBoxBackground"] = new SolidColorBrush(buttonBg);
            app.Resources["ComboBoxBackgroundPointerOver"] = new SolidColorBrush(buttonHover);
            app.Resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(buttonHover);
            app.Resources["ComboBoxBorderBrush"] = new SolidColorBrush(separator);
            app.Resources["ComboBoxBorderBrushPointerOver"] = new SolidColorBrush(accent);
            app.Resources["ComboBoxBorderBrushPressed"] = new SolidColorBrush(accent);
            app.Resources["ComboBoxForeground"] = new SolidColorBrush(textFg);
            app.Resources["ComboBoxForegroundPointerOver"] = new SolidColorBrush(textFg);
            app.Resources["ComboBoxItemBackground"] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            app.Resources["ComboBoxItemBackgroundPointerOver"] = new SolidColorBrush(buttonHover);
            app.Resources["ComboBoxItemBackgroundPressed"] = new SolidColorBrush(buttonHover);
            app.Resources["ComboBoxItemBackgroundSelected"] = new SolidColorBrush(accent);
            app.Resources["ComboBoxItemBackgroundSelectedPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["ComboBoxItemBackgroundSelectedPressed"] = new SolidColorBrush(accent);
            app.Resources["ComboBoxItemForeground"] = new SolidColorBrush(textFg);
            app.Resources["ComboBoxItemForegroundPointerOver"] = new SolidColorBrush(textFg);
            app.Resources["ComboBoxItemForegroundSelected"] = new SolidColorBrush(accentFg);
            app.Resources["ComboBoxItemForegroundSelectedPointerOver"] = new SolidColorBrush(accentFg);

            app.Resources["MenuFlyoutPresenterBackground"] = new SolidColorBrush(panelBg);
            app.Resources["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(separator);
            app.Resources["FlyoutPresenterBackground"] = new SolidColorBrush(panelBg);
            app.Resources["PopupBackground"] = new SolidColorBrush(panelBg);

            app.Resources["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(accent);
            app.Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["CheckBoxCheckBackgroundFillCheckedPressed"] = new SolidColorBrush(accent);
            app.Resources["CheckBoxCheckBackgroundStrokeChecked"] = new SolidColorBrush(accent);
            app.Resources["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["CheckBoxCheckGlyphForegroundChecked"] = new SolidColorBrush(accentFg);

            app.Resources["SliderThumbBackground"] = new SolidColorBrush(accent);
            app.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["SliderThumbBackgroundPressed"] = new SolidColorBrush(accent);
            app.Resources["SliderTrackValueFill"] = new SolidColorBrush(accent);
            app.Resources["SliderTrackValueFillPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["SliderTrackValueFillPressed"] = new SolidColorBrush(accent);

            app.Resources["ProgressBarForeground"] = new SolidColorBrush(accent);
            app.Resources["ToggleSwitchFillOn"] = new SolidColorBrush(accent);
            app.Resources["ToggleSwitchFillOnPointerOver"] = new SolidColorBrush(accentHover);
            app.Resources["ToggleSwitchStrokeOn"] = new SolidColorBrush(accent);
            app.Resources["TextControlSelectionHighlightColor"] = accent;

            Logger.WriteLine($"Matugen: applied colors from '{path}' (primary={accent}, bg={windowBg}, surface={panelBg})");
            ThemeChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Matugen: failed to load theme from '{path}': {ex.Message}");
            ApplyFallback(app);
            ThemeChanged?.Invoke();
            return false;
        }
    }

    private static void ApplyFallback(Application app)
    {
        var accent = Color.Parse("#4CC2FF");
        var accentHover = Color.Parse("#5FCDFF");
        var accentFg = Color.Parse("#000000");

        app.Resources["WindowBackground"] = new SolidColorBrush(Color.Parse("#1C1C1C"));
        app.Resources["PanelBackground"] = new SolidColorBrush(Color.Parse("#262626"));
        app.Resources["ButtonBackground"] = new SolidColorBrush(Color.Parse("#373737"));
        app.Resources["ButtonHover"] = new SolidColorBrush(Color.Parse("#454545"));
        app.Resources["TextForeground"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
        app.Resources["TextDim"] = new SolidColorBrush(Color.Parse("#A0A0A0"));
        app.Resources["AccentColor"] = new SolidColorBrush(accent);
        app.Resources["AccentHover"] = new SolidColorBrush(accentHover);
        app.Resources["AccentForeground"] = new SolidColorBrush(accentFg);
        app.Resources["SeparatorColor"] = new SolidColorBrush(Color.Parse("#333333"));

        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = accentHover;
        app.Resources["SystemAccentColorLight2"] = accentHover;
        app.Resources["SystemAccentColorLight3"] = accentHover;
        app.Resources["SystemAccentColorDark1"] = accent;
        app.Resources["SystemAccentColorDark2"] = accent;
        app.Resources["SystemAccentColorDark3"] = accent;

        app.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(accent);
        app.Resources["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        app.Resources["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(Color.FromArgb(140, accent.R, accent.G, accent.B));
        app.Resources["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(accent);
        app.Resources["SystemControlHighlightAltAccentHighBrush"] = new SolidColorBrush(accentHover);

        app.Resources["ComboBoxDropDownBackground"] = new SolidColorBrush(Color.Parse("#262626"));
        app.Resources["ComboBoxPopupBackground"] = new SolidColorBrush(Color.Parse("#262626"));
        app.Resources["ComboBoxBackground"] = new SolidColorBrush(Color.Parse("#373737"));
        app.Resources["ComboBoxBackgroundPointerOver"] = new SolidColorBrush(Color.Parse("#454545"));
        app.Resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(Color.Parse("#454545"));
        app.Resources["ComboBoxBorderBrush"] = new SolidColorBrush(Color.Parse("#333333"));
        app.Resources["ComboBoxBorderBrushPointerOver"] = new SolidColorBrush(accent);
        app.Resources["ComboBoxBorderBrushPressed"] = new SolidColorBrush(accent);
        app.Resources["ComboBoxForeground"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
        app.Resources["ComboBoxForegroundPointerOver"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
        app.Resources["ComboBoxItemBackground"] = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        app.Resources["ComboBoxItemBackgroundPointerOver"] = new SolidColorBrush(Color.Parse("#454545"));
        app.Resources["ComboBoxItemBackgroundPressed"] = new SolidColorBrush(Color.Parse("#454545"));
        app.Resources["ComboBoxItemBackgroundSelected"] = new SolidColorBrush(accent);
        app.Resources["ComboBoxItemBackgroundSelectedPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["ComboBoxItemBackgroundSelectedPressed"] = new SolidColorBrush(accent);
        app.Resources["ComboBoxItemForeground"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
        app.Resources["ComboBoxItemForegroundPointerOver"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
        app.Resources["ComboBoxItemForegroundSelected"] = new SolidColorBrush(accentFg);
        app.Resources["ComboBoxItemForegroundSelectedPointerOver"] = new SolidColorBrush(accentFg);

        app.Resources["MenuFlyoutPresenterBackground"] = new SolidColorBrush(Color.Parse("#262626"));
        app.Resources["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(Color.Parse("#333333"));
        app.Resources["FlyoutPresenterBackground"] = new SolidColorBrush(Color.Parse("#262626"));
        app.Resources["PopupBackground"] = new SolidColorBrush(Color.Parse("#262626"));

        app.Resources["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(accent);
        app.Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["CheckBoxCheckBackgroundFillCheckedPressed"] = new SolidColorBrush(accent);
        app.Resources["CheckBoxCheckBackgroundStrokeChecked"] = new SolidColorBrush(accent);
        app.Resources["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["CheckBoxCheckGlyphForegroundChecked"] = new SolidColorBrush(accentFg);

        app.Resources["SliderThumbBackground"] = new SolidColorBrush(accent);
        app.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["SliderThumbBackgroundPressed"] = new SolidColorBrush(accent);
        app.Resources["SliderTrackValueFill"] = new SolidColorBrush(accent);
        app.Resources["SliderTrackValueFillPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["SliderTrackValueFillPressed"] = new SolidColorBrush(accent);

        app.Resources["ProgressBarForeground"] = new SolidColorBrush(accent);
        app.Resources["ToggleSwitchFillOn"] = new SolidColorBrush(accent);
        app.Resources["ToggleSwitchFillOnPointerOver"] = new SolidColorBrush(accentHover);
        app.Resources["ToggleSwitchStrokeOn"] = new SolidColorBrush(accent);
        app.Resources["TextControlSelectionHighlightColor"] = accent;
    }

    private static Dictionary<string, Color> ParseColorsFile(string path)
    {
        var result = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);

        // Pattern: key = "rgba(14140cff)" or key = "rgb(14,14,12)" or key = "#14140C"
        var regex = new Regex(@"^([a-zA-Z0-9_]+)\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var match = regex.Match(line.Trim());
            if (!match.Success)
                continue;

            string key = match.Groups[1].Value;
            string value = match.Groups[2].Value.Trim();

            if (TryParseColor(value, out var color))
            {
                result[key] = color;
            }
        }

        return result;
    }

    private static bool TryParseColor(string val, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(val))
            return false;

        // rgba(14140cff) or rgb(14140c)
        if (val.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(")"))
        {
            string hex = val[5..^1].Trim();
            if (hex.Length == 8)
            {
                // RRGGBBAA
                if (byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b) &&
                    byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a))
                {
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            else if (hex.Length == 6)
            {
                // RRGGBB
                if (byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
            }
        }

        // Standard hex #RRGGBB or #AARRGGBB
        if (val.StartsWith("#"))
        {
            if (Color.TryParse(val, out color))
                return true;
        }

        return false;
    }

    private static void WatchConfigFile()
    {
        try
        {
            string path = GetConfigPath();
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;

            string fileName = Path.GetFileName(path);
            _watcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            void OnChanged(object sender, FileSystemEventArgs e)
            {
                // Debounce rapid writes
                if ((DateTime.UtcNow - _lastReload).TotalMilliseconds < 250)
                    return;
                _lastReload = DateTime.UtcNow;

                Dispatcher.UIThread.Post(() =>
                {
                    Logger.WriteLine("Matugen: detected color scheme update on disk, reloading theme");
                    ApplyTheme();
                });
            }

            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Matugen: file watcher setup failed: {ex.Message}");
        }
    }
}
