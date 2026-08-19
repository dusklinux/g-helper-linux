using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Display;

/// <summary>
/// Native display backend for Hyprland (Wayland / Aquamarine).
/// Communicates directly via hyprctl JSON output and Lua/keyword monitor dispatchers.
/// </summary>
public partial class HyprlandBackend : IDisplayBackend
{
    [GeneratedRegex(@"^(?:(?<w>\d+)x(?<h>\d+))?@(?<hz>\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex ModeRegex();

    public string Name => "hyprland";
    public bool SupportsGamma => false;

    public int GetRefreshRate()
    {
        try
        {
            var json = RunMonitorsJson();
            if (string.IsNullOrEmpty(json))
                return -1;

            using var doc = JsonDocument.Parse(json);
            var output = FindLaptopOutput(doc);
            if (output == null)
                return -1;

            if (output.Value.TryGetProperty("refreshRate", out var rr))
                return (int)Math.Round(rr.GetDouble());
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("HyprlandBackend.GetRefreshRate failed", ex);
        }
        return -1;
    }

    public List<int> GetAvailableRefreshRates()
    {
        var rates = new List<int>();
        try
        {
            var json = RunMonitorsJson();
            if (string.IsNullOrEmpty(json))
                return rates;

            using var doc = JsonDocument.Parse(json);
            var output = FindLaptopOutput(doc);
            if (output == null)
                return rates;

            int curW = output.Value.TryGetProperty("width", out var wp) ? wp.GetInt32() : 0;
            int curH = output.Value.TryGetProperty("height", out var hp) ? hp.GetInt32() : 0;

            if (output.Value.TryGetProperty("availableModes", out var modes) && modes.ValueKind == JsonValueKind.Array)
            {
                foreach (var modeElem in modes.EnumerateArray())
                {
                    var modeStr = modeElem.GetString();
                    if (string.IsNullOrEmpty(modeStr))
                        continue;

                    var match = ModeRegex().Match(modeStr);
                    if (!match.Success)
                        continue;

                    if (match.Groups["w"].Success && curW > 0)
                    {
                        int w = int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture);
                        int h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                        if (w != curW || h != curH)
                            continue;
                    }

                    if (double.TryParse(match.Groups["hz"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz))
                    {
                        int rate = (int)Math.Round(hz);
                        if (rate > 0 && !rates.Contains(rate))
                            rates.Add(rate);
                    }
                }
            }

            rates.Sort();
            rates.Reverse();
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("HyprlandBackend.GetAvailableRefreshRates failed", ex);
        }
        return rates;
    }

    public void SetRefreshRate(int hz)
    {
        try
        {
            Helpers.Logger.WriteLine($"HyprlandBackend.SetRefreshRate: requesting {hz}Hz");

            var json = RunMonitorsJson();
            if (string.IsNullOrEmpty(json))
            {
                Helpers.Logger.WriteLine("HyprlandBackend.SetRefreshRate: hyprctl monitors -j returned empty");
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var output = FindLaptopOutput(doc);
            if (output == null)
            {
                Helpers.Logger.WriteLine("HyprlandBackend.SetRefreshRate: no target monitor found");
                return;
            }

            var name = output.Value.GetProperty("name").GetString() ?? "eDP-1";
            int width = output.Value.GetProperty("width").GetInt32();
            int height = output.Value.GetProperty("height").GetInt32();
            int x = output.Value.TryGetProperty("x", out var xp) ? xp.GetInt32() : 0;
            int y = output.Value.TryGetProperty("y", out var yp) ? yp.GetInt32() : 0;
            double scale = output.Value.TryGetProperty("scale", out var sp) ? sp.GetDouble() : 1.0;

            string scaleStr = scale.ToString("0.##", CultureInfo.InvariantCulture);
            string modeStr = $"{width}x{height}@{hz}";

            // 1. Try Hyprland 0.56+ Lua eval
            string luaCmd = $"hl.monitor({{ output = '{name}', mode = '{modeStr}', position = '{x}x{y}', scale = {scaleStr} }})";
            var evalResult = SysfsHelper.RunCommand("hyprctl", $"eval \"{luaCmd}\"");
            if (evalResult != null && !evalResult.Contains("error:"))
            {
                Helpers.Logger.WriteLine($"HyprlandBackend.SetRefreshRate: success via Lua eval ({name} -> {modeStr})");
                return;
            }

            // 2. Fallback to hyprctl keyword monitor
            string keywordArg = $"monitor {name},{modeStr},{x}x{y},{scaleStr}";
            SysfsHelper.RunCommand("hyprctl", $"keyword {keywordArg}");
            Helpers.Logger.WriteLine($"HyprlandBackend.SetRefreshRate: applied via hyprctl keyword ({keywordArg})");
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("HyprlandBackend.SetRefreshRate failed", ex);
        }
    }

    public void SetGamma(float r, float g, float b)
    {
        Helpers.Logger.WriteLine("HyprlandBackend: Gamma adjustment is handled via shaders/hyprsunset");
    }

    public string? GetDisplayName()
    {
        try
        {
            var json = RunMonitorsJson();
            if (string.IsNullOrEmpty(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            var output = FindLaptopOutput(doc);
            if (output == null)
                return null;

            if (output.Value.TryGetProperty("description", out var desc))
            {
                var d = desc.GetString();
                if (!string.IsNullOrEmpty(d))
                    return d;
            }

            return output.Value.GetProperty("name").GetString();
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("HyprlandBackend.GetDisplayName failed", ex);
            return null;
        }
    }

    public static bool Probe()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            return true;

        var result = SysfsHelper.RunCommand("which", "hyprctl");
        return result != null;
    }

    private static string? RunMonitorsJson()
    {
        return SysfsHelper.RunCommandWithTimeout("hyprctl", ["monitors", "-j"], 3000);
    }

    private static JsonElement? FindLaptopOutput(JsonDocument doc)
    {
        foreach (var output in doc.RootElement.EnumerateArray())
        {
            var name = output.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
            if (name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase))
                return output;
        }
        foreach (var output in doc.RootElement.EnumerateArray())
        {
            if (output.TryGetProperty("focused", out var f) && f.GetBoolean())
                return output;
        }
        foreach (var output in doc.RootElement.EnumerateArray())
        {
            if (output.TryGetProperty("disabled", out var d) && !d.GetBoolean())
                return output;
            return output;
        }
        return null;
    }
}
