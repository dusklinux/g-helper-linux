namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// CPU energy_performance_preference (EPP) control via cpufreq sysfs.
/// EPP biases the hardware frequency governor between battery life and
/// responsiveness. Choices come from the kernel, typically:
/// default, performance, balance_performance, balance_power, power.
/// Applied to every CPU. Per-mode values are stored as epp_(baseMode)
/// and re-applied on each performance mode switch.
/// </summary>
public static class CpuEpp
{
    private const string CpuBase = "/sys/devices/system/cpu";
    private const string Available = CpuBase + "/cpu0/cpufreq/energy_performance_available_preferences";
    private const string Cpu0Pref = CpuBase + "/cpu0/cpufreq/energy_performance_preference";

    public static bool IsSupported() => SysfsHelper.Exists(Cpu0Pref);

    public static string[] GetChoices()
    {
        var raw = SysfsHelper.ReadAttribute(Available);
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string? Get() => SysfsHelper.ReadAttribute(Cpu0Pref)?.Trim();

    /// <summary>Write the preference to all CPUs. Returns the number of CPUs written.</summary>
    public static int SetAll(string epp)
    {
        if (!GetChoices().Contains(epp))
        {
            Helpers.Logger.WriteLine($"CpuEpp: '{epp}' not in available preferences, skipping");
            return 0;
        }

        int written = 0;
        foreach (var dir in Directory.GetDirectories(CpuBase, "cpu*"))
        {
            var path = Path.Combine(dir, "cpufreq", "energy_performance_preference");
            if (File.Exists(path) && SysfsHelper.WriteAttribute(path, epp))
                written++;
        }

        Helpers.Logger.WriteLine($"CpuEpp: '{epp}' applied to {written} CPUs");
        return written;
    }
}
