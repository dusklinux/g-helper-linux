using System.Text.RegularExpressions;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Suspend variant control via /sys/power/mem_sleep.
///
/// The file lists the variants the firmware supports with the active one in
/// brackets, e.g. "[s2idle] deep":
///   - s2idle: software idle. Fast wake, but high battery drain during sleep
///     on many ASUS models.
///   - deep: S3 firmware suspend. Slower wake, much lower drain.
///
/// Writing requires root and the kernel resets the value on every boot, so
/// the chosen variant is also stored in /etc/ghelper/mem-sleep where the
/// ghelper-gpu-boot service re-applies it at boot.
/// </summary>
public static class MemSleep
{
    public const string SysPath = "/sys/power/mem_sleep";
    public const string StatePath = "/etc/ghelper/mem-sleep";

    private static readonly Regex VariantRegex = new("^[a-z0-9]+$");

    /// <summary>All variants the firmware offers, without brackets. Empty when unsupported.</summary>
    public static string[] GetOptions()
    {
        var raw = SysfsHelper.ReadAttribute(SysPath);
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        return raw.Replace("[", "").Replace("]", "")
                  .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>The active variant (the bracketed entry), or null.</summary>
    public static string? GetActive()
    {
        var raw = SysfsHelper.ReadAttribute(SysPath);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var m = Regex.Match(raw, @"\[(\w+)\]");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Both deep and s2idle offered, i.e. there is actually something to toggle.
    /// Firmware that dropped S3 only lists s2idle.
    /// </summary>
    public static bool IsToggleSupported()
    {
        var options = GetOptions();
        return options.Contains("deep") && options.Contains("s2idle");
    }

    /// <summary>
    /// Set the suspend variant now and persist it for re-apply at boot.
    /// One pkexec prompt. Returns true when the readback confirms the change.
    /// </summary>
    public static bool Set(string variant)
    {
        if (!VariantRegex.IsMatch(variant) || !GetOptions().Contains(variant))
        {
            Helpers.Logger.WriteLine($"MemSleep: refusing invalid variant '{variant}'");
            return false;
        }

        SysfsHelper.RunPkexecBash(
            $"echo {variant} > {SysPath} && mkdir -p /etc/ghelper && echo {variant} > {StatePath}");

        bool ok = GetActive() == variant;
        Helpers.Logger.WriteLine($"MemSleep: set '{variant}' {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
