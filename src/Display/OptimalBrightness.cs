using GHelper.Linux.Helpers;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Display;

// Optimal Display Brightness (content-adaptive backlight dimming).
//
// Single owner for the feature: startup init, power-state re-apply, and the
// UI write path all go through this class. The platform layer
// (LinuxAsusWmi.Get/SetScreenAutoBrightness) only knows the raw firmware
// boolean; the three-state user preference (Off / On Always / On Battery)
// and AC-aware resolution live here.
//
// Linux kernel: writes 0/1 to
//   /sys/class/firmware-attributes/asus-armoury/attributes/screen_auto_brightness/current_value
// ACPI WMI DEVID: 0x0005002A (same endpoint Windows g-helper writes to via
// AsusACPI.ScreenOptimalBrightness).
public static class OptimalBrightness
{
    public const string ConfigKey = "optimal_brightness";

    // User-preference enum values stored in AppConfig under ConfigKey.
    public const int ModeOff = 0;
    public const int ModeAlways = 1;
    public const int ModeBatteryOnly = 2;

    public static bool IsSupported() =>
        App.Wmi?.IsFeatureSupported(AsusAttributes.ScreenAutoBrightness) ?? false;

    // Returns -1 when the user has never picked a mode (we leave firmware default alone).
    public static int GetStoredMode() => AppConfig.Get(ConfigKey, -1);

    // Returns the raw firmware boolean (0/1) or -1 if unsupported.
    public static int GetFirmwareState() => App.Wmi?.GetScreenAutoBrightness() ?? -1;

    // UI write path: persist preference and apply immediately.
    public static void SetMode(int mode)
    {
        AppConfig.Set(ConfigKey, mode);
        ApplyMode(mode);
    }

    // Called once at app startup. Mirrors Windows ScreenControl.InitOptimalBrightness.
    public static void Init()
    {
        try
        {
            int mode = GetStoredMode();
            if (mode >= 0 && IsSupported())
            {
                ApplyMode(mode);
                Logger.WriteLine($"Optimal Display Brightness: applied stored mode {mode}");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Optimal Display Brightness init failed", ex);
        }
    }

    // Called from App.OnPowerStateChanged. Only ModeBatteryOnly tracks AC
    // state; ModeOff/ModeAlways are static and don't need re-applying.
    public static void OnPowerStateChanged()
    {
        try
        {
            if (GetStoredMode() == ModeBatteryOnly && IsSupported())
                ApplyMode(ModeBatteryOnly);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Optimal Display Brightness re-apply failed", ex);
        }
    }

    private static void ApplyMode(int mode)
    {
        bool onAc = App.Power?.IsOnAcPower() ?? true;
        bool enable = mode switch
        {
            ModeAlways => true,
            ModeBatteryOnly => !onAc,
            _ => false,
        };
        App.Wmi?.SetScreenAutoBrightness(enable);
    }
}
