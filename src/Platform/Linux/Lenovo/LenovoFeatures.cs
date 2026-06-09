using System.Diagnostics;

namespace GHelper.Linux.Platform.Linux.Lenovo;

/// <summary>
/// Lenovo feature operations beyond the IHardwareControl surface, all through
/// mainline kernel interfaces (Vantage / LegionToolkit parity):
///
///   Rapid charge       battery charge_types = Fast (kernel 6.19+, mutually
///                      exclusive with conservation/Long_Life - the kernel
///                      enforces the exclusion, we surface it in the UI)
///   Always-on USB      ideapad usb_charging
///   Fn lock            ideapad fn_lock
///   Touchpad           ideapad touchpad (needs touchpad_ctrl_via_ec=1)
///   Camera privacy     ideapad camera_power
///   Manual fan RPM     lenovo_wmi_other hwmon fanN_target (kernel 7.0+)
///   Dust cleaning      max-RPM burst via fan targets, or ideapad fan_mode=2
///   Flip to Start      FBSWIF UEFI variable (root write via gpu-helper)
///   Mic boost fix      ALSA "Internal Mic Boost" clamp - Realtek ALC287
///                      Legions ship with excessive DMIC boost that distorts
///                      the internal microphone (see 16IAX10H sound saga;
///                      kernel-side fix is alc269_fixup_limit_int_mic_boost)
/// </summary>
public static class LenovoFeatures
{
    //  simple ideapad bool attrs 

    public static bool GetIdeapadBool(string attr)
    {
        var path = LenovoSysfs.IdeapadAttr(attr);
        return path != null && SysfsHelper.ReadInt(path, 0) == 1;
    }

    public static bool SetIdeapadBool(string attr, bool value)
    {
        var path = LenovoSysfs.IdeapadAttr(attr);
        if (path == null)
            return false;
        bool ok = SysfsHelper.WriteInt(path, value ? 1 : 0);
        Helpers.Logger.WriteLine($"LenovoFeatures: {attr} = {(value ? 1 : 0)} ({(ok ? "OK" : "FAILED")})");
        return ok;
    }

    public static bool GetFnLock() => GetIdeapadBool("fn_lock");
    public static bool SetFnLock(bool on) => SetIdeapadBool("fn_lock", on);

    public static bool GetUsbCharging() => GetIdeapadBool("usb_charging");
    public static bool SetUsbCharging(bool on) => SetIdeapadBool("usb_charging", on);

    public static bool GetCameraPower() => GetIdeapadBool("camera_power");
    public static bool SetCameraPower(bool on) => SetIdeapadBool("camera_power", on);

    public static bool GetTouchpad() => GetIdeapadBool("touchpad");
    public static bool SetTouchpad(bool on) => SetIdeapadBool("touchpad", on);

    //  rapid charge (charge_types) 
    // Reads like "Standard [Fast] Long_Life" with the active type in brackets.
    // The kernel turns conservation off when Fast is selected and vice versa.

    public static bool GetRapidCharge()
    {
        var path = LenovoSysfs.BatteryChargeTypes();
        if (path == null)
            return false;
        string? raw = SysfsHelper.ReadAttribute(path);
        return raw != null && raw.Contains("[Fast]");
    }

    public static bool SetRapidCharge(bool on)
    {
        var path = LenovoSysfs.BatteryChargeTypes();
        if (path == null)
            return false;
        // Turning rapid charge off returns to Standard (not conservation -
        // the user picks conservation separately via the battery limit).
        bool ok = SysfsHelper.WriteAttribute(path, on ? "Fast" : "Standard");
        Helpers.Logger.WriteLine($"LenovoFeatures: rapid charge {(on ? "ON" : "OFF")} ({(ok ? "OK" : "FAILED")})");
        return ok;
    }

    /// <summary>True while conservation (Long_Life) is the active charge type.</summary>
    public static bool IsConservationActive()
    {
        var path = LenovoSysfs.BatteryChargeTypes();
        if (path != null)
        {
            string? raw = SysfsHelper.ReadAttribute(path);
            if (raw != null)
                return raw.Contains("[Long_Life]");
        }
        var conservation = LenovoSysfs.IdeapadAttr("conservation_mode");
        return conservation != null && SysfsHelper.ReadInt(conservation, 0) == 1;
    }

    //  manual fan target RPM (kernel 7.0+) 

    /// <summary>Current target RPM for 1-based fan number; 0 = auto, -1 = unsupported.</summary>
    public static int GetFanTarget(int fan)
    {
        var path = LenovoSysfs.FanTargetPath(fan);
        return path != null ? SysfsHelper.ReadInt(path, -1) : -1;
    }

    /// <summary>Set target RPM (rounded down to the EC divisor by the kernel).
    /// 0 returns the fan to automatic EC control.</summary>
    public static bool SetFanTarget(int fan, int rpm)
    {
        var path = LenovoSysfs.FanTargetPath(fan);
        if (path == null)
            return false;
        if (rpm != 0)
        {
            var range = LenovoSysfs.FanTargetRange(fan);
            if (range != null)
                rpm = Math.Clamp(rpm, range.Value.Min, range.Value.Max);
        }
        bool ok = SysfsHelper.WriteInt(path, rpm);
        Helpers.Logger.WriteLine($"LenovoFeatures: fan{fan}_target = {rpm} ({(ok ? "OK" : "FAILED")})");
        return ok;
    }

    /// <summary>Return every fan to automatic EC control.</summary>
    public static void ResetFanTargets()
    {
        for (int fan = 1; fan <= 4; fan++)
            if (LenovoSysfs.FanTargetPath(fan) != null)
                SetFanTarget(fan, 0);
    }

    //  dust cleaning (max-fan burst) 
    // Modern path: write fanN_target = fanN_max for the burst, then 0 (auto).
    // Legacy path: ideapad fan_mode = 2 (Dust Cleaning EC preset), restore after.

    public static bool IsDustCleanSupported()
        => LenovoSysfs.FanTargetHwmon() != null || LenovoSysfs.IdeapadAttr("fan_mode") != null;

    private static volatile bool _dustCleanRunning;
    public static bool IsDustCleanRunning => _dustCleanRunning;

    /// <summary>Spin all fans at max for <paramref name="seconds"/>, then return
    /// to automatic control. Runs on a background task; no-op when active.</summary>
    public static void StartDustClean(int seconds = 30, Action? onDone = null)
    {
        if (_dustCleanRunning || !IsDustCleanSupported())
            return;
        _dustCleanRunning = true;

        Task.Run(() =>
        {
            Helpers.Logger.WriteLine($"LenovoFeatures: dust cleaning burst for {seconds}s");
            string? fanModePath = null;
            int savedFanMode = -1;
            try
            {
                if (LenovoSysfs.FanTargetHwmon() != null)
                {
                    for (int fan = 1; fan <= 4; fan++)
                    {
                        var range = LenovoSysfs.FanTargetRange(fan);
                        if (range != null)
                            SetFanTarget(fan, range.Value.Max);
                    }
                }
                else
                {
                    fanModePath = LenovoSysfs.IdeapadAttr("fan_mode");
                    if (fanModePath == null)
                        return;
                    savedFanMode = SysfsHelper.ReadInt(fanModePath, -1);
                    SysfsHelper.WriteInt(fanModePath, 2); // EC Dust Cleaning preset
                }

                for (int waited = 0; waited < seconds && _dustCleanRunning; waited++)
                    Thread.Sleep(1000);
            }
            finally
            {
                if (LenovoSysfs.FanTargetHwmon() != null)
                    ResetFanTargets();
                else if (fanModePath != null && savedFanMode >= 0)
                    SysfsHelper.WriteInt(fanModePath, savedFanMode);
                _dustCleanRunning = false;
                Helpers.Logger.WriteLine("LenovoFeatures: dust cleaning done, fans back to auto");
                onDone?.Invoke();
            }
        });
    }

    public static void StopDustClean() => _dustCleanRunning = false;

    //  Flip to Start (FBSWIF UEFI variable) 
    // efivarfs layout: 4-byte LE attributes word + 4-byte payload
    // (byte0 = enabled). Reading works as user; writing needs root and the
    // immutable flag cleared - both handled by the gpu-helper verb.

    public static bool? GetFlipToStart()
    {
        try
        {
            if (!File.Exists(LenovoSysfs.FlipToStartEfivar))
                return null;
            byte[] data = File.ReadAllBytes(LenovoSysfs.FlipToStartEfivar);
            if (data.Length < 5)
                return null;
            return data[4] == 1;
        }
        catch
        {
            return null;
        }
    }

    public static bool SetFlipToStart(bool on)
    {
        var result = SysfsHelper.RunSudoOrPkexec(
            SysfsHelper.GpuHelperPath,
            new[] { "lenovo-flip-to-start", on ? "1" : "0" },
            sudoTimeoutMs: 5000);
        bool ok = result != null && GetFlipToStart() == on;
        Helpers.Logger.WriteLine($"LenovoFeatures: flip-to-start {(on ? "ON" : "OFF")} ({(ok ? "OK" : "FAILED")})");
        return ok;
    }

    //  internal microphone boost fix 
    // Realtek ALC287 Legion generations (Legion 5/5i/Pro 7i, Y9000P, ...)
    // configure the internal DMIC with excessive capture boost, producing the
    // clipped/distorted mic audio users report. The proper fix is the kernel
    // quirk chain ALC287_FIXUP_LENOVO_LEGION_AW88399 -> limit_int_mic_boost;
    // until a machine's quirk lands, clamping the ALSA "Internal Mic Boost"
    // control to 0 dB achieves the same result from userspace.

    private static readonly string[] MicBoostControls =
    {
        "Internal Mic Boost",
        "Int Mic Boost",
        "Mic Boost",
    };

    /// <summary>True if any sound card exposes a known mic-boost control.</summary>
    public static bool IsMicBoostFixAvailable()
    {
        foreach (int card in EnumerateSoundCards())
            foreach (var control in MicBoostControls)
                if (AmixerHasControl(card, control))
                    return true;
        return false;
    }

    /// <summary>Clamp every known internal-mic boost control to 0 on all cards.
    /// Returns the number of controls clamped.</summary>
    public static int ApplyMicBoostFix()
    {
        int applied = 0;
        foreach (int card in EnumerateSoundCards())
        {
            foreach (var control in MicBoostControls)
            {
                if (!AmixerHasControl(card, control))
                    continue;
                if (RunAmixer($"-c {card} sset \"{control}\" 0") != null)
                {
                    Helpers.Logger.WriteLine($"LenovoFeatures: clamped '{control}' to 0 on card {card}");
                    applied++;
                }
                break; // first matching control per card is the internal mic
            }
        }
        return applied;
    }

    private static IEnumerable<int> EnumerateSoundCards()
    {
        var cards = new List<int>();
        try
        {
            if (File.Exists("/proc/asound/cards"))
            {
                foreach (var line in File.ReadLines("/proc/asound/cards"))
                {
                    var trimmed = line.TrimStart();
                    int space = trimmed.IndexOf(' ');
                    if (space > 0 && int.TryParse(trimmed[..space], out int idx) && !cards.Contains(idx))
                        cards.Add(idx);
                }
            }
        }
        catch { }
        return cards;
    }

    private static bool AmixerHasControl(int card, string control)
        => RunAmixer($"-c {card} sget \"{control}\"") != null;

    private static string? RunAmixer(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "amixer",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return null;
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
