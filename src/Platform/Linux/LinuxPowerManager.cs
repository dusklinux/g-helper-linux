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
    public event Action? MonitorSlept;
    public event Action? MonitorWoke;

    private bool? _lastDpmsOn;

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

                bool? currentDpms = ReadDpmsAnyOn();
                if (currentDpms.HasValue)
                {
                    if (_lastDpmsOn == true && !currentDpms.Value)
                    {
                        Helpers.Logger.WriteLine("Monitor sleep (DPMS On -> Off)");
                        MonitorSlept?.Invoke();
                    }
                    else if (_lastDpmsOn == false && currentDpms.Value)
                    {
                        Helpers.Logger.WriteLine("Monitor wake (DPMS Off -> On)");
                        MonitorWoke?.Invoke();
                    }
                    _lastDpmsOn = currentDpms;
                }
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
        // Mode switch: reset the PPT custom-profile circuit-breaker so the
        // next SetPptLimit call retries switching to "custom".
        if (Helpers.AppConfig.IsLenovoDevice())
            Lenovo.LinuxLenovoWmi.ResetCustomSwitchBreaker();

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

        // Fallback: power-profiles-daemon CLI, then D-Bus (tuned-ppd on COSMIC/Atomic)
        if (SysfsHelper.RunCommand("powerprofilesctl", $"set {profile}") == null)
            Cosmic.SetPowerProfile(profile);
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

        return SysfsHelper.RunCommand("powerprofilesctl", "get")
            ?? Cosmic.GetPowerProfile()
            ?? "balanced";
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

    public async Task SetAspmPolicy(string policy)
    {
        if (!_aspmWritable)
            return; // Kernel blocks writes on some systems (built-in module)

        if (!SysfsHelper.Exists(SysfsHelper.PcieAspm))
            return;

        // Pre-flight: when leaving powersave, wake NVMe devices first to prevent
        // D3cold->D0 resume failures that can panic.
        string current = GetAspmPolicy();
        bool leavingDeepSave = current is "powersave" or "powersupersave"
                            && policy is not "powersave" and not "powersupersave";

        if (leavingDeepSave && !await WakeNvmeDevicesAsync())
        {
            Helpers.Logger.WriteLine(
                "ASPM: NVMe device(s) stuck in deep sleep - skipping ASPM transition "
                + "to avoid potential data loss. Consider adding "
                + "nvme_core.default_ps_max_latency_us=0 to kernel boot params.");
            return;
        }

        if (!SysfsHelper.WriteAttribute(SysfsHelper.PcieAspm, policy))
        {
            _aspmWritable = false;
            Helpers.Logger.WriteLine("ASPM policy is read-only on this kernel - use boot param pcie_aspm.policy=... instead");
        }
    }

    /// <summary>
    /// Wake all NVMe devices from deep PCIe power states (D3cold/D3hot) before
    /// an ASPM transition. Returns true when all devices are in D0 (safe to
    /// proceed), false if any device is stuck and the ASPM change should be aborted.
    /// </summary>
    private async Task<bool> WakeNvmeDevicesAsync()
    {
        const string nvmeClass = "/sys/class/nvme";
        if (!Directory.Exists(nvmeClass))
            return true; // No NVMe devices = nothing to worry about

        foreach (var nvmeDir in Directory.GetDirectories(nvmeClass))
        {
            var pciDevDir = Path.Combine(nvmeDir, "device");
            var powerStatePath = Path.Combine(pciDevDir, "power_state");
            var state = SysfsHelper.ReadAttribute(powerStatePath);

            if (state == null || state == "D0")
                continue; // Already awake or path unreadable

            var name = Path.GetFileName(nvmeDir);
            Helpers.Logger.WriteLine($"ASPM pre-flight: {name} in {state}, waking...");

            // Trigger runtime resume by reading a controller attribute
            SysfsHelper.ReadAttribute(Path.Combine(nvmeDir, "serial"));

            // Poll for D0 (up to 500ms)
            bool woke = false;
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(50);
                state = SysfsHelper.ReadAttribute(powerStatePath);
                if (state == "D0")
                { woke = true; break; }
            }

            if (!woke)
            {
                // Last resort: temporarily disable d3cold and wait
                var d3coldPath = Path.Combine(pciDevDir, "d3cold_allowed");
                SysfsHelper.WriteAttribute(d3coldPath, "0");
                await Task.Delay(200);
                state = SysfsHelper.ReadAttribute(powerStatePath);
                woke = state == "D0";
                // Restore d3cold_allowed once awake (don't permanently disable it)
                if (woke)
                    SysfsHelper.WriteAttribute(d3coldPath, "1");
            }

            if (!woke)
            {
                Helpers.Logger.WriteLine($"ASPM pre-flight: {name} STUCK in {state} "
                    + "- aborting ASPM change");
                return false;
            }

            Helpers.Logger.WriteLine($"ASPM pre-flight: {name} woke to D0");
        }

        return true;
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

    /// <summary>
    /// Returns true if any connected DRM output is in DPMS On, false if all are Off,
    /// null when the DPMS attributes are unreadable (no DRM exposed).
    /// </summary>
    private static bool? ReadDpmsAnyOn()
    {
        const string drmRoot = "/sys/class/drm";
        if (!Directory.Exists(drmRoot))
            return null;

        bool sawAny = false;
        bool anyOn = false;
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(drmRoot))
            {
                string dpmsPath = Path.Combine(dir, "dpms");
                string statusPath = Path.Combine(dir, "status");
                if (!File.Exists(dpmsPath) || !File.Exists(statusPath))
                    continue;

                string status = (SysfsHelper.ReadAttribute(statusPath) ?? "").Trim();
                if (status != "connected")
                    continue;

                sawAny = true;
                string state = (SysfsHelper.ReadAttribute(dpmsPath) ?? "").Trim();
                if (state == "On")
                {
                    anyOn = true;
                    break;
                }
            }
        }
        catch
        {
            return null;
        }
        return sawAny ? anyOn : null;
    }
}
