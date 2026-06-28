namespace GHelper.Linux.Platform.Linux.Lenovo;

/// <summary>
/// Path constants and cached resolvers for the mainline Lenovo kernel interfaces:
///   ideapad-laptop (ACPI VPC2004): conservation_mode, fn_lock, fan_mode,
///     usb_charging, camera_power, kbd backlight LED, charge_types battery extension
///   lenovo-wmi-gamezone (kernel 6.17+): platform_profile provider on Legion/LOQ
///   lenovo-wmi-other (kernel 6.17+): firmware-attributes PPT tunables + fan hwmon
/// All lookups are presence-gated so any Lenovo generation degrades gracefully
/// (e.g. Legion Y530/Y540 only have the ideapad-laptop interfaces).
/// </summary>
public static class LenovoSysfs
{
    public const string IdeapadDriverBase = "/sys/bus/platform/drivers/ideapad_acpi";
    public const string FirmwareAttributesClass = "/sys/class/firmware-attributes";
    public const string LenovoFwAttrPrefix = "lenovo-wmi-other";

    // hwmon names, in preference order: lenovo-wmi-other fan channels (RPM),
    // yogafan (read-only EC fans on Yoga/Legion/IdeaPad), acpi_fan (generic PNP0C0B)
    private static readonly string[] FanHwmonNames = { "lenovo_wmi_other", "yogafan", "acpi_fan" };

    private static readonly string[] KbdBacklightLedNames =
        { "platform::kbd_backlight", "platform::kbd_backlight_1" };

    private static string? _ideapadDevice;
    private static bool _ideapadResolved;
    private static string? _fwAttrsDir;
    private static bool _fwAttrsResolved;
    private static string? _fanHwmonDir;
    private static bool _fanHwmonResolved;
    private static string? _kbdLedDir;
    private static bool _kbdLedResolved;

    /// <summary>The ideapad-laptop platform device dir (VPC2004:00), or null.</summary>
    public static string? IdeapadDevice()
    {
        if (_ideapadResolved)
            return _ideapadDevice;
        _ideapadResolved = true;

        try
        {
            if (Directory.Exists(IdeapadDriverBase))
            {
                string exact = Path.Combine(IdeapadDriverBase, "VPC2004:00");
                if (Directory.Exists(exact))
                    return _ideapadDevice = exact;

                foreach (var dir in Directory.GetDirectories(IdeapadDriverBase, "VPC2004*"))
                    return _ideapadDevice = dir;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Path of an ideapad sysfs attribute (conservation_mode, fn_lock,
    /// fan_mode, usb_charging, camera_power), or null when unavailable.</summary>
    public static string? IdeapadAttr(string name)
    {
        var dev = IdeapadDevice();
        if (dev == null)
            return null;
        string path = Path.Combine(dev, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The lenovo-wmi-other firmware-attributes dir
    /// (/sys/class/firmware-attributes/lenovo-wmi-other-0/attributes), or null.</summary>
    public static string? FirmwareAttributesDir()
    {
        if (_fwAttrsResolved)
            return _fwAttrsDir;
        _fwAttrsResolved = true;

        try
        {
            if (Directory.Exists(FirmwareAttributesClass))
            {
                foreach (var dir in Directory.GetDirectories(FirmwareAttributesClass, LenovoFwAttrPrefix + "*"))
                {
                    string attrs = Path.Combine(dir, "attributes");
                    if (Directory.Exists(attrs))
                        return _fwAttrsDir = attrs;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Directory of a firmware attribute (contains current_value,
    /// min_value, max_value, scalar_increment, default_value), or null.</summary>
    public static string? FirmwareAttrDir(string name)
    {
        var attrs = FirmwareAttributesDir();
        if (attrs == null)
            return null;
        string dir = Path.Combine(attrs, name);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>current_value path of a firmware attribute, or null.</summary>
    public static string? FirmwareAttrCurrentValue(string name)
    {
        var dir = FirmwareAttrDir(name);
        if (dir == null)
            return null;
        string path = Path.Combine(dir, "current_value");
        return File.Exists(path) ? path : null;
    }

    /// <summary>hwmon dir with Lenovo fan RPM sensors, or null. Preference:
    /// lenovo_wmi_other (Legion/LOQ WMI fans), yogafan, acpi_fan.</summary>
    public static string? FanHwmon()
    {
        if (_fanHwmonResolved)
            return _fanHwmonDir;
        _fanHwmonResolved = true;

        foreach (var name in FanHwmonNames)
        {
            var dir = SysfsHelper.FindHwmonByName(name);
            if (dir != null && File.Exists(Path.Combine(dir, "fan1_input")))
                return _fanHwmonDir = dir;
        }
        return null;
    }

    /// <summary>Keyboard backlight LED dir (platform::kbd_backlight), or null.
    /// The "_1" variant exists when another driver claimed the base name.</summary>
    public static string? KbdBacklightLed()
    {
        if (_kbdLedResolved)
            return _kbdLedDir;
        _kbdLedResolved = true;

        try
        {
            foreach (var name in KbdBacklightLedNames)
            {
                string dir = Path.Combine(SysfsHelper.Leds, name);
                if (File.Exists(Path.Combine(dir, "brightness")))
                    return _kbdLedDir = dir;
            }

            // Fallback: any platform::kbd_backlight* LED
            if (Directory.Exists(SysfsHelper.Leds))
            {
                foreach (var dir in Directory.GetDirectories(SysfsHelper.Leds, "platform::kbd_backlight*"))
                    if (File.Exists(Path.Combine(dir, "brightness")))
                        return _kbdLedDir = dir;
            }
        }
        catch { }
        return null;
    }

    /// <summary>charge_types path on the battery (ideapad battery extension:
    /// Standard / Fast / Long_Life), or null.</summary>
    public static string? BatteryChargeTypes()
    {
        var battery = SysfsHelper.FindBattery();
        if (battery == null)
            return null;
        string path = Path.Combine(battery, "charge_types");
        return File.Exists(path) ? path : null;
    }

    //  Manual fan target RPM (lenovo_wmi_other hwmon, kernel 7.0+) 
    // fanN_target: RW, 0 = automatic EC control, otherwise RPM in multiples of
    // fanN_div (100). Valid range comes from fanN_min / fanN_max (LENOVO_FAN_TEST_DATA).

    private static string? _fanTargetHwmonDir;
    private static bool _fanTargetResolved;

    /// <summary>The lenovo_wmi_other hwmon dir when it exposes a writable
    /// fan1_target (manual fan RPM support, kernel 7.0+), or null.</summary>
    public static string? FanTargetHwmon()
    {
        if (_fanTargetResolved)
            return _fanTargetHwmonDir;
        _fanTargetResolved = true;

        var dir = SysfsHelper.FindHwmonByName("lenovo_wmi_other");
        if (dir != null && File.Exists(Path.Combine(dir, "fan1_target")))
            return _fanTargetHwmonDir = dir;
        return null;
    }

    /// <summary>fanN_target path (1-based fan number), or null.</summary>
    public static string? FanTargetPath(int fan)
    {
        var dir = FanTargetHwmon();
        if (dir == null)
            return null;
        string path = Path.Combine(dir, $"fan{fan}_target");
        return File.Exists(path) ? path : null;
    }

    /// <summary>(min, max, div) RPM bounds for fanN_target, or null. div is the
    /// EC's RPM granularity (100); targets are rounded down to multiples of it.</summary>
    public static (int Min, int Max, int Div)? FanTargetRange(int fan)
    {
        var dir = FanTargetHwmon();
        if (dir == null)
            return null;
        int min = SysfsHelper.ReadInt(Path.Combine(dir, $"fan{fan}_min"), -1);
        int max = SysfsHelper.ReadInt(Path.Combine(dir, $"fan{fan}_max"), -1);
        int div = SysfsHelper.ReadInt(Path.Combine(dir, $"fan{fan}_div"), 100);
        if (max <= 0)
            return null;
        return (Math.Max(min, 0), max, div <= 0 ? 100 : div);
    }

    //  LEDs (lenovo-wmi-hotkey-utilities kernel 6.14+, ideapad fnlock) 

    /// <summary>LED dir under /sys/class/leds, or null when absent.
    /// Known Lenovo names: platform::fnlock, platform::micmute, platform::mute.</summary>
    public static string? Led(string name)
    {
        string dir = Path.Combine(SysfsHelper.Leds, name);
        return File.Exists(Path.Combine(dir, "brightness")) ? dir : null;
    }

    //  ideapad debugfs (root-only; readable in Diagnostics when run via sudo
    //    or when debugfs perms allow) 

    public const string IdeapadDebugfsCfg = "/sys/kernel/debug/ideapad/cfg";
    public const string IdeapadDebugfsStatus = "/sys/kernel/debug/ideapad/status";

    //  per-device platform-profile class interface (kernel 6.12+) 
    // The legacy /sys/firmware/acpi/platform_profile aggregates all providers
    // and unconditionally rejects writes of "custom" (EINVAL). The per-device
    // class node bypasses that restriction.

    private const string PlatformProfileClass = "/sys/class/platform-profile";

    private static string? _gamezoneProfilePath;
    private static bool _gamezoneProfileResolved;

    /// <summary>Per-device profile sysfs path for the gamezone provider, or null.
    /// Writing "custom" here succeeds even though the legacy aggregated path rejects it.</summary>
    public static string? GamezoneProfilePath()
    {
        if (_gamezoneProfileResolved)
            return _gamezoneProfilePath;
        _gamezoneProfileResolved = true;

        try
        {
            if (!Directory.Exists(PlatformProfileClass))
                return null;

            foreach (var dir in Directory.GetDirectories(PlatformProfileClass, "platform-profile-*"))
            {
                string namePath = Path.Combine(dir, "name");
                if (!File.Exists(namePath))
                    continue;
                string name = File.ReadAllText(namePath).Trim();
                if (name == "lenovo-wmi-gamezone")
                {
                    string profile = Path.Combine(dir, "profile");
                    if (File.Exists(profile))
                        return _gamezoneProfilePath = profile;
                }
            }
        }
        catch { }
        return null;
    }

    //  Flip to Start UEFI variable (FBSWIF) 
    // 4-byte payload: byte0 = enabled, rest reserved. efivarfs prepends a
    // 4-byte LE attributes word (0x7 = NV+BS+RT), so the file is 8 bytes.

    public const string FlipToStartEfivar =
        "/sys/firmware/efi/efivars/FBSWIF-d743491e-f484-4952-a87d-8d5dd189b70c";
}
