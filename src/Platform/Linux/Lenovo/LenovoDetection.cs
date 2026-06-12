namespace GHelper.Linux.Platform.Linux.Lenovo;

/// <summary>
/// Lenovo capability probing and BIOS-generation quirks. Vendor identity
/// itself lives in AppConfig (GetDmiVendor / IsLenovoDevice); this class
/// answers "what does this Lenovo expose" questions.
/// BIOS generation matching follows the Legion convention: the first four
/// characters of dmi bios_version identify the platform (e.g. J2CN54WW
/// is generation J2CN = Legion 5 Pro 16IAH7H).
/// </summary>
public static class LenovoDetection
{
    private static string? _biosPrefix;

    /// <summary>First four characters of the DMI BIOS version (e.g. "J2CN"), uppercase.</summary>
    public static string BiosPrefix()
    {
        if (_biosPrefix != null)
            return _biosPrefix;

        string? bios = SysfsHelper.ReadAttribute(Path.Combine(SysfsHelper.DmiId, "bios_version"));
        _biosPrefix = bios != null && bios.Length >= 4
            ? bios[..4].ToUpperInvariant()
            : "";
        return _biosPrefix;
    }

    /// <summary>Legion 5 Pro 16IAH7H (J2CN): switching low-power directly to
    /// performance is unreliable; the setter must bounce through balanced.</summary>
    public static bool HasQuietToPerformanceBug() => BiosPrefix() == "J2CN";

    // Capability probes (presence of a sysfs node == capability)

    public static bool HasPlatformProfile() => SysfsHelper.Exists(SysfsHelper.PlatformProfile);

    public static bool HasIdeapad() => LenovoSysfs.IdeapadDevice() != null;

    public static bool HasFanMode() => LenovoSysfs.IdeapadAttr("fan_mode") != null;

    public static bool HasConservationMode() => LenovoSysfs.IdeapadAttr("conservation_mode") != null;

    public static bool HasChargeTypes() => LenovoSysfs.BatteryChargeTypes() != null;

    public static bool HasFnLock() => LenovoSysfs.IdeapadAttr("fn_lock") != null;

    public static bool HasPptAttributes() => LenovoSysfs.FirmwareAttrCurrentValue("ppt_pl1_spl") != null;

    public static bool HasFanRpm() => LenovoSysfs.FanHwmon() != null;

    public static bool HasKbdBacklight() => LenovoSysfs.KbdBacklightLed() != null;

    /// <summary>Manual fan RPM (lenovo_wmi_other hwmon fanN_target, kernel 7.0+).</summary>
    public static bool HasFanTarget() => LenovoSysfs.FanTargetHwmon() != null;

    public static bool HasUsbCharging() => LenovoSysfs.IdeapadAttr("usb_charging") != null;

    public static bool HasCameraPower() => LenovoSysfs.IdeapadAttr("camera_power") != null;

    /// <summary>ideapad touchpad attr - only registered with touchpad_ctrl_via_ec=1.</summary>
    public static bool HasTouchpadCtl() => LenovoSysfs.IdeapadAttr("touchpad") != null;

    /// <summary>Rapid charge: charge_types advertises "Fast" (kernel 6.19+, GBMD bit 17).</summary>
    public static bool HasRapidCharge()
    {
        var path = LenovoSysfs.BatteryChargeTypes();
        if (path == null)
            return false;
        string? raw = SysfsHelper.ReadAttribute(path);
        return raw != null && raw.Contains("Fast");
    }

    /// <summary>platform_profile choices contain a given profile token.</summary>
    public static bool HasProfileChoice(string profile)
    {
        string? choices = SysfsHelper.ReadAttribute(SysfsHelper.PlatformProfile + "_choices");
        if (choices == null)
            return false;
        foreach (var token in choices.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token.Trim('[', ']') == profile)
                return true;
        return false;
    }

    /// <summary>Extreme mode: gamezone max-power profile (SmartFan >= 6).</summary>
    public static bool HasExtremeProfile() => HasProfileChoice("max-power");

    /// <summary>Custom (God-mode) profile - required for PPT writes.</summary>
    public static bool HasCustomProfile() => HasProfileChoice("custom");

    /// <summary>Flip to Start UEFI variable present (writable via efivarfs as root).</summary>
    public static bool HasFlipToStart() => File.Exists(LenovoSysfs.FlipToStartEfivar);

    //  BIOS-prefix capability table 
    // Ported from the LenovoLegionLinux allowlist research: the first 4 chars
    // of the BIOS version identify the platform generation and what its EC /
    // firmware supports. Used for diagnostics and user-facing hints only -
    // actual feature gating stays sysfs-presence based.

    private static readonly (string Prefix, string Note)[] BiosCapabilityNotes =
    {
        ("BHCN", "Legion Y530/Y7000 2018: no SmartFan, ideapad fan_mode fallback only"),
        ("8JCN", "Legion Y530 2018: no SmartFan, ideapad fan_mode fallback only"),
        ("BVCN", "Legion Y540/Y7000 2019: no SmartFan, ideapad fan_mode fallback only"),
        ("BGCN", "Legion Y540 2019: no SmartFan, ideapad fan_mode fallback only"),
        ("EUCN", "Legion 5 2020: SmartFan gen 1 (quiet/balanced/performance)"),
        ("EFCN", "Legion 5 2020: SmartFan gen 1"),
        ("FSCN", "Legion 5P 2020: SmartFan gen 1"),
        ("GKCN", "Legion 5/7 2021: SmartFan + custom mode"),
        ("H1CN", "Legion 7 2021 (AMD): SmartFan + custom mode"),
        ("HHCN", "Legion 5 Pro 2021: SmartFan + custom mode"),
        ("HACN", "Legion 5 Pro 2021: SmartFan + custom mode"),
        ("J2CN", "Legion 5 Pro 16IAH7H 2022: quiet->performance switch needs balanced bounce"),
        ("JUCN", "Legion 5 2022: SmartFan + custom mode"),
        ("JYCN", "Legion 5 Pro 2022 (AMD): SmartFan + custom mode"),
        ("K1CN", "Legion 2023: leave custom mode by stepping modes one at a time (firmware bug)"),
        ("K9CN", "Legion Slim 2023: SmartFan + custom mode"),
        ("KWCN", "Legion Pro 2023: SmartFan + custom mode"),
        ("LPCN", "Legion Pro 2024: SmartFan gen 6 (extreme mode capable)"),
        ("M3CN", "LOQ 2024: SmartFan gen 6 (extreme mode capable)"),
        ("M5CN", "Legion 2024: SmartFan gen 6 (extreme mode capable)"),
        ("N1CN", "Legion 2025: SmartFan gen 7+, Spectrum per-key RGB on gen 10 chassis"),
        ("NZCN", "Legion 5 2021 refresh: SmartFan + custom mode"),
        ("R3CN", "Legion Go S: extreme mode disabled by firmware quirk"),
        ("V1CN", "Legion Go: extreme mode disabled by firmware quirk"),
    };

    /// <summary>Human-readable capability note for this BIOS generation, or null.</summary>
    public static string? BiosCapabilityNote()
    {
        string prefix = BiosPrefix();
        foreach (var (p, note) in BiosCapabilityNotes)
            if (p == prefix)
                return note;
        return null;
    }

    /// <summary>K1CN firmware bug: switching out of custom mode must step
    /// through adjacent modes one at a time instead of jumping.</summary>
    public static bool HasCustomModeSwitchBug() => BiosPrefix() == "K1CN";

    /// <summary>Best descriptor of the active Lenovo platform driver for the
    /// status line: prefers the richest stack present.</summary>
    public static (string name, bool loaded) PlatformDriver()
    {
        if (LenovoSysfs.FirmwareAttributesDir() != null)
            return ("lenovo-wmi", true);
        if (HasPlatformProfile() && HasIdeapad())
            return ("ideapad-laptop + platform_profile", true);
        if (HasIdeapad())
            return ("ideapad-laptop", true);
        if (HasPlatformProfile())
            return ("platform_profile", true);
        return ("ideapad-laptop", false);
    }

    /// <summary>One-shot startup dump of everything detected.</summary>
    public static void LogCapabilities()
    {
        Helpers.Logger.WriteLine($"Lenovo platform: bios prefix={BiosPrefix()}");
        Helpers.Logger.WriteLine($"  ideapad device: {LenovoSysfs.IdeapadDevice() ?? "none"}");
        Helpers.Logger.WriteLine($"  platform_profile: {(HasPlatformProfile() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  fan_mode fallback: {(HasFanMode() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  fan RPM hwmon: {LenovoSysfs.FanHwmon() ?? "none"}");
        Helpers.Logger.WriteLine($"  PPT firmware-attributes: {LenovoSysfs.FirmwareAttributesDir() ?? "none"}");
        Helpers.Logger.WriteLine($"  kbd backlight LED: {LenovoSysfs.KbdBacklightLed() ?? "none"}");
        Helpers.Logger.WriteLine($"  battery charge_types: {(HasChargeTypes() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  conservation_mode: {(HasConservationMode() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  fn_lock attr: {(HasFnLock() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  fan target (manual RPM): {(HasFanTarget() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  rapid charge (Fast): {(HasRapidCharge() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  usb_charging: {(HasUsbCharging() ? "YES" : "no")} camera_power: {(HasCameraPower() ? "YES" : "no")} touchpad: {(HasTouchpadCtl() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  extreme profile (max-power): {(HasExtremeProfile() ? "YES" : "no")} custom: {(HasCustomProfile() ? "YES" : "no")}");
        Helpers.Logger.WriteLine($"  flip-to-start efivar: {(HasFlipToStart() ? "YES" : "no")}");
        var note = BiosCapabilityNote();
        if (note != null)
            Helpers.Logger.WriteLine($"  generation: {note}");
    }
}
