namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// AMD Ryzen SMU power/temp/clock control via gpu-helper's ryzen-* subcommands.
/// Writes the SMU mailbox directly (not the cosmetic asus-wmi sysfs).
/// Separate from RyzenSmu.cs which handles Curve Optimizer via ryzen_smu driver.
/// </summary>
public static class RyzenPower
{
    private static HashSet<string>? _supported;
    private static bool _probed;

    public static HashSet<string>? SupportedParams => _probed ? _supported : Probe();

    public static bool Available => SupportedParams is { Count: > 0 };

    public static bool IsSupported(string param)
        => SupportedParams?.Contains(param) == true;

    public static HashSet<string>? Probe()
    {
        _probed = true;
        try
        {
            var (stdout, _, exitCode) = SysfsHelper.RunSudoOrPkexecEx(
                SysfsHelper.GpuHelperPath, new[] { "ryzen-probe" });
            if (exitCode != 0 || stdout == null)
            {
                _supported = null;
                return _supported;
            }
            _supported = new HashSet<string>();
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("supported="))
                {
                    foreach (var p in line["supported=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        _supported.Add(p.Trim());
                }
            }
        }
        catch
        {
            _supported = null;
        }
        return _supported;
    }

    /// <summary>Set a parameter. Value is raw (mW for power, °C for temp, mA for current, MHz for clocks).</summary>
    public static bool Set(string param, int value)
    {
        var result = SysfsHelper.RunSudoOrPkexec(
            SysfsHelper.GpuHelperPath,
            new[] { "ryzen-set", param, value.ToString() });
        return result != null;
    }

    /// <summary>Read the PM table as key=value pairs.</summary>
    public static Dictionary<string, float>? ReadInfo()
    {
        var (stdout, _, exitCode) = SysfsHelper.RunSudoOrPkexecEx(
            SysfsHelper.GpuHelperPath, new[] { "ryzen-info" });
        if (exitCode != 0 || stdout == null)
            return null;

        var dict = new Dictionary<string, float>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq];
            if (float.TryParse(line[(eq + 1)..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
                dict[key] = val;
        }
        return dict;
    }

    /// <summary>Map PPT attribute name to SMU parameter name.</summary>
    public static string? PptToParam(string attribute) => attribute switch
    {
        "ppt_pl1_spl" => "stapm-limit",
        "ppt_fppt" => "fast-limit",
        "ppt_pl2_sppt" => "slow-limit",
        "ppt_apu_sppt" => "apu-slow-limit",
        _ => null,
    };

    /// <summary>Try to set a PPT attribute via SMU. Returns true if handled.</summary>
    public static bool TrySetPpt(string attribute, int watts)
    {
        var param = PptToParam(attribute);
        if (param == null || !IsSupported(param))
            return false;
        return Set(param, watts * 1000); // watts to mW
    }
}
