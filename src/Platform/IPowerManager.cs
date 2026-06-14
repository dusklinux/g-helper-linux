namespace GHelper.Linux.Platform;

/// <summary>
/// Abstraction over OS power management.
/// Windows: PowrProf.dll (power plans, CPU boost, ASPM)
/// Linux: sysfs cpufreq, platform_profile, pcie_aspm, logind
/// </summary>
public interface IPowerManager
{
    /// <summary>Enable/disable CPU turbo boost.</summary>
    void SetCpuBoost(bool enabled);

    /// <summary>Get CPU turbo boost state.</summary>
    bool GetCpuBoost();

    /// <summary>Set platform performance profile. "balanced", "performance", "low-power"</summary>
    void SetPlatformProfile(string profile);

    /// <summary>Get current platform profile.</summary>
    string GetPlatformProfile();

    /// <summary>Get list of platform profile values the kernel/firmware accepts.
    /// Empty array = sysfs not exposed at all.</summary>
    string[] GetPlatformProfileChoices();

    /// <summary>Set PCIe ASPM policy. "default", "performance", "powersave", "powersupersave".
    /// When transitioning away from powersave, NVMe devices are woken first to prevent
    /// D3cold resume failures that can kill the root filesystem.</summary>
    Task SetAspmPolicy(string policy);

    /// <summary>Get current ASPM policy.</summary>
    string GetAspmPolicy();

    /// <summary>Check if on AC power.</summary>
    bool IsOnAcPower();

    /// <summary>Get battery percentage (0-100).</summary>
    int GetBatteryPercentage();

    /// <summary>Get battery discharge rate in milliwatts (positive = discharging).</summary>
    int GetBatteryDrainRate();

    /// <summary>Get battery health (full charge capacity / design capacity * 100).</summary>
    int GetBatteryHealth();

    /// <summary>
    /// Fired when AC power state changes (plugged in / unplugged).
    /// Argument: true = on AC, false = on battery.
    /// </summary>
    event Action<bool>? PowerStateChanged;

    /// <summary>
    /// Fired after the system wakes from suspend. Some ASUS firmware resets
    /// the battery charge limit on resume, so subscribers re-apply settings.
    /// </summary>
    event Action? SystemResumed;

    /// <summary>
    /// Fired when all connected displays transition from DPMS On to Off.
    /// DEs typically kill keyboard backlight at this point.
    /// </summary>
    event Action? MonitorSlept;

    /// <summary>
    /// Fired when the display returns from DPMS Off to On (monitor auto-off then back).
    /// Distinct from SystemResumed because it does not involve a kernel suspend.
    /// </summary>
    event Action? MonitorWoke;

    /// <summary>Start monitoring power state changes (polls AC adapter status).</summary>
    void StartPowerMonitoring();

    /// <summary>Stop monitoring power state changes.</summary>
    void StopPowerMonitoring();
}
