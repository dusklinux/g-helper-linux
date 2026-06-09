namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Linux power management via sysfs and kernel interfaces.
/// Replaces Windows PowrProf.dll functionality.
/// </summary>
public class LinuxPowerManager : IPowerManager
{
    private readonly string? _batteryDir;
    private readonly string? _acDir;
    private Thread? _powerMonitorThread;
    private volatile bool _powerMonitoring;
    private bool? _lastAcState;

    public event Action<bool>? PowerStateChanged;
    public event Action? SystemResumed;

    // Poll interval for the power monitor loop. A wall-clock gap much larger
    // than this between iterations means the system was suspended.
    private const int PollIntervalMs = 3000;
    private const long SuspendGapThresholdMs = 10000;

    public LinuxPowerManager()
    {
        _batteryDir = SysfsHelper.FindBattery();
        _acDir = SysfsHelper.FindAcAdapter();
    }

    public void StartPowerMonitoring()
    {
        if (_powerMonitoring)
            return;
        _powerMonitoring = true;
        _lastAcState = IsOnAcPower();

        _powerMonitorThread = new Thread(PowerMonitorLoop)
        {
            Name = "PowerMonitor",
            IsBackground = true
        };
        _powerMonitorThread.Start();
        Helpers.Logger.WriteLine($"Power state monitoring started (AC={_lastAcState})");
    }

    public void StopPowerMonitoring()
    {
        _powerMonitoring = false;
        _powerMonitorThread?.Join(500);
    }

    private void PowerMonitorLoop()
    {
        long lastWallMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        while (_powerMonitoring)
        {
            try
            {
                Thread.Sleep(PollIntervalMs);
                if (!_powerMonitoring)
                    break;

                // Suspend detection: wall clock keeps advancing during suspend
                // while this loop is frozen, so the first iteration after wake
                // sees a gap far beyond the poll interval. Environment.TickCount64
                // is CLOCK_MONOTONIC on Linux, which stops during suspend, so it
                // cannot be used here.
                long nowWallMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowWallMs - lastWallMs > PollIntervalMs + SuspendGapThresholdMs)
                {
                    Helpers.Logger.WriteLine($"System Resume (slept ~{(nowWallMs - lastWallMs) / 1000}s)");
                    SystemResumed?.Invoke();
                }
                lastWallMs = nowWallMs;

                bool currentAc = IsOnAcPower();
                if (_lastAcState.HasValue && currentAc != _lastAcState.Value)
                    PowerStateChanged?.Invoke(currentAc);
                _lastAcState = currentAc;
            }
            catch (Exception ex)
            {
                Helpers.Logger.WriteLine($"PowerMonitorLoop error: {ex.Message}");
            }
        }
    }

    public void SetCpuBoost(bool enabled)
    {
        // Try Intel pstate first
        var intelPath = Path.Combine(SysfsHelper.IntelPstate, "no_turbo");
        if (SysfsHelper.Exists(intelPath))
        {
            SysfsHelper.WriteInt(intelPath, enabled ? 0 : 1); // no_turbo: 0=boost on, 1=boost off
            return;
        }

        // Generic cpufreq boost
        var boostPath = "/sys/devices/system/cpu/cpufreq/boost";
        if (SysfsHelper.Exists(boostPath))
        {
            SysfsHelper.WriteInt(boostPath, enabled ? 1 : 0);
            return;
        }

        // AMD pstate
        var amdPath = "/sys/devices/system/cpu/amd_pstate/status";
        if (SysfsHelper.Exists(amdPath))
        {
            // AMD pstate boost is per-CPU via cpufreq/boost
            SysfsHelper.WriteInt("/sys/devices/system/cpu/cpufreq/boost", enabled ? 1 : 0);
        }
    }

    public bool GetCpuBoost()
    {
        // Intel pstate
        var intelPath = Path.Combine(SysfsHelper.IntelPstate, "no_turbo");
        if (SysfsHelper.Exists(intelPath))
            return SysfsHelper.ReadInt(intelPath, 0) == 0; // 0 = boost enabled

        // Generic
        return SysfsHelper.ReadInt("/sys/devices/system/cpu/cpufreq/boost", 1) == 1;
    }

    public void SetPlatformProfile(string profile)
    {
        // /sys/firmware/acpi/platform_profile accepts a subset of: "low-power", "balanced", "performance", "quiet"
        // Available profiles vary by firmware - read platform_profile_choices first
        if (SysfsHelper.Exists(SysfsHelper.PlatformProfile))
        {
            var available = GetPlatformProfileChoices();

            if (available.Length > 0 && !available.Contains(profile))
            {
                string? fallback = TryResolveSupportedProfile(profile, available);
                if (fallback == null)
                {
                    Helpers.Logger.WriteLine($"Platform profile '{profile}' not supported (choices: {string.Join(' ', available)}), skipping");
                    return;
                }
                Helpers.Logger.WriteLine($"Platform profile '{profile}' not available, using '{fallback}' (choices: {string.Join(' ', available)})");
                profile = fallback;
            }

            // Legion 5 Pro 16IAH7H (J2CN BIOS): switching low-power directly to
            // performance is unreliable in firmware - bounce through balanced.
            if (profile == "performance"
                && Helpers.AppConfig.IsLenovoDevice()
                && Lenovo.LenovoDetection.HasQuietToPerformanceBug())
            {
                string? current = SysfsHelper.ReadAttribute(SysfsHelper.PlatformProfile);
                if (current is "low-power" or "quiet")
                {
                    SysfsHelper.WriteAttribute(SysfsHelper.PlatformProfile, "balanced");
                    Thread.Sleep(500);
                }
            }

            // Legion 2023 (K1CN BIOS): leaving the custom (God-mode) profile by
            // jumping straight to another mode hits a firmware bug (LLT quirk:
            // "leave custom by stepping modes one at a time") - bounce through
            // balanced first.
            if (profile != "custom" && profile != "balanced"
                && Helpers.AppConfig.IsLenovoDevice()
                && Lenovo.LenovoDetection.HasCustomModeSwitchBug())
            {
                string? current = SysfsHelper.ReadAttribute(SysfsHelper.PlatformProfile);
                if (current == "custom")
                {
                    Helpers.Logger.WriteLine("K1CN quirk: stepping custom -> balanced before target profile");
                    SysfsHelper.WriteAttribute(SysfsHelper.PlatformProfile, "balanced");
                    Thread.Sleep(500);
                }
            }

            SysfsHelper.WriteAttribute(SysfsHelper.PlatformProfile, profile);
            return;
        }

        // Fallback: power-profiles-daemon via D-Bus (via powerprofilesctl CLI)
        SysfsHelper.RunCommand("powerprofilesctl", $"set {profile}");
    }

    /// <summary>
    /// Map a canonical platform_profile name (e.g. "low-power", "balanced",
    /// "performance") to one supported by the running kernel/firmware.
    /// Returns:
    ///   - <paramref name="canonical"/> if it's already in <paramref name="available"/>
    ///   - a known synonym (e.g. "low-power" → "quiet") if one is available
    ///   - <paramref name="canonical"/> when <paramref name="available"/> is empty (no choices file)
    ///   - null when no equivalent exists in <paramref name="available"/>
    /// Single source of truth for the synonym map; consumed by
    /// <see cref="SetPlatformProfile"/> and by the Extra window's per-mode
    /// profile dropdowns when a saved value isn't in the current kernel choices.
    /// </summary>
    public static string? TryResolveSupportedProfile(string canonical, string[] available)
    {
        if (available.Length == 0)
            return canonical;
        if (available.Contains(canonical))
            return canonical;
        return canonical switch
        {
            "low-power" when available.Contains("quiet") => "quiet",
            "low-power" when available.Contains("balanced") => "balanced",
            "quiet" when available.Contains("low-power") => "low-power",
            "balanced" when available.Contains("balanced-performance") => "balanced-performance",
            "balanced-performance" when available.Contains("balanced") => "balanced",
            "performance" when available.Contains("balanced") => "balanced",
            _ => null,
        };
    }

    public string GetPlatformProfile()
    {
        if (SysfsHelper.Exists(SysfsHelper.PlatformProfile))
            return SysfsHelper.ReadAttribute(SysfsHelper.PlatformProfile) ?? "balanced";

        return SysfsHelper.RunCommand("powerprofilesctl", "get") ?? "balanced";
    }

    public string[] GetPlatformProfileChoices()
    {
        if (!SysfsHelper.Exists(SysfsHelper.PlatformProfileChoices))
            return Array.Empty<string>();

        string? raw = SysfsHelper.ReadAttribute(SysfsHelper.PlatformProfileChoices);
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool _aspmWritable = true; // Assume writable until proven otherwise

    public void SetAspmPolicy(string policy)
    {
        if (!_aspmWritable)
            return; // Kernel blocks writes on some systems (built-in module)

        if (SysfsHelper.Exists(SysfsHelper.PcieAspm))
        {
            if (!SysfsHelper.WriteAttribute(SysfsHelper.PcieAspm, policy))
            {
                _aspmWritable = false;
                Helpers.Logger.WriteLine("ASPM policy is read-only on this kernel - use boot param pcie_aspm.policy=... instead");
            }
        }
    }

    public string GetAspmPolicy()
    {
        var raw = SysfsHelper.ReadAttribute(SysfsHelper.PcieAspm) ?? "default";
        // The active policy is enclosed in brackets: "default performance [powersave] powersupersave"
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"\[(\w+)\]");
        return match.Success ? match.Groups[1].Value : raw;
    }

    public bool IsOnAcPower()
    {
        if (_acDir != null)
            return SysfsHelper.ReadInt(Path.Combine(_acDir, "online"), 0) == 1;

        // Fallback: check battery status
        if (_batteryDir != null)
        {
            var status = SysfsHelper.ReadAttribute(Path.Combine(_batteryDir, "status"));
            return status != null && (status == "Charging" || status == "Full" || status == "Not charging");
        }

        return true; // Assume AC if no battery info
    }

    public int GetBatteryPercentage()
    {
        if (_batteryDir == null)
            return -1;
        return SysfsHelper.ReadInt(Path.Combine(_batteryDir, "capacity"), -1);
    }

    public int GetBatteryDrainRate()
    {
        if (_batteryDir == null)
            return 0;

        // power_now is in microwatts
        int powerMw = 0;
        if (File.Exists(Path.Combine(_batteryDir, "power_now")))
        {
            int powerUw = SysfsHelper.ReadInt(Path.Combine(_batteryDir, "power_now"), 0);
            powerMw = powerUw / 1000;
        }
        // calculates power using current and voltage, if power isn't available
        else if (File.Exists(Path.Combine(_batteryDir, "current_now")) && File.Exists(Path.Combine(_batteryDir, "voltage_now")))
        {
            long currentUa = SysfsHelper.ReadInt(Path.Combine(_batteryDir, "current_now"), 0);
            long voltageUv = SysfsHelper.ReadInt(Path.Combine(_batteryDir, "voltage_now"), 0);
            powerMw = (int)((currentUa * voltageUv) / 1_000_000_000L);
        }

        var status = SysfsHelper.ReadAttribute(Path.Combine(_batteryDir, "status"));
        // Return positive for discharging, negative for charging
        return status == "Discharging" ? powerMw : -powerMw;
    }

    public int GetBatteryHealth()
    {
        if (_batteryDir == null)
            return -1;

        int fullCharge = SysfsHelper.ReadInt(Path.Combine(_batteryDir, "energy_full"), -1);
        int designCapacity = SysfsHelper.ReadInt(Path.Combine(_batteryDir, "energy_full_design"), -1);

        if (fullCharge < 0 || designCapacity <= 0)
            return -1;
        return (int)(fullCharge * 100.0 / designCapacity);
    }
}
