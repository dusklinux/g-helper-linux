using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Fan;

/// <summary>
/// Fan hysteresis control (EC ramp damping, ACPI device 0x00110034).
/// Values 1-5 per direction: how aggressively the EC ramps fans up and down.
/// DSTS read packs them as low byte = up, next byte = down (plus the 0x10000
/// presence bit); DEVS write takes (down &lt;&lt; 8) | up.
/// Linux has no kernel interface for this, so it goes through the raw WMI
/// debugfs path and is only available when raw_wmi is enabled.
/// </summary>
public static class FanHysteresis
{
    public const uint DeviceId = 0x00110034;
    public const int Min = 1;
    public const int Max = 5;

    private const uint PresenceBit = 0x00010000;

    /// <summary>Raw WMI channel usable. Does not probe the device, no privileged call.</summary>
    public static bool IsChannelAvailable() =>
        Helpers.AppConfig.IsAsusDevice()
        && Helpers.AppConfig.Is("raw_wmi")
        && AsusWmiDebugfs.IsAvailable();

    /// <summary>Read current up/down values. (-1, -1) when unsupported. One privileged call.</summary>
    public static (int up, int down) Get()
    {
        if (!IsChannelAvailable())
            return (-1, -1);

        var raw = AsusWmiDebugfs.Dsts(DeviceId);
        if (raw == null || (raw.Value & PresenceBit) == 0)
            return (-1, -1);

        int up = (int)(raw.Value & 0xFF);
        int down = (int)((raw.Value >> 8) & 0xFF);
        Helpers.Logger.WriteLine($"FanHysteresis read: up={up} down={down} (raw=0x{raw.Value:X})");
        return (up, down);
    }

    /// <summary>Write up/down values. One privileged call.</summary>
    public static bool Set(int up, int down)
    {
        if (!IsChannelAvailable())
            return false;

        up = Math.Clamp(up, Min, Max);
        down = Math.Clamp(down, Min, Max);

        var result = AsusWmiDebugfs.Devs(DeviceId, (uint)((down << 8) | up));
        Helpers.Logger.WriteLine($"FanHysteresis write: up={up} down={down} result={(result.HasValue ? $"0x{result.Value:X}" : "failed")}");
        return result.HasValue;
    }

    /// <summary>Apply saved per-mode values if set. Called on mode switch.</summary>
    public static void ApplyForCurrentMode()
    {
        int up = Helpers.AppConfig.GetMode("hysteresis_up");
        int down = Helpers.AppConfig.GetMode("hysteresis_down");
        if (up > 0 && down > 0)
            Set(up, down);
    }
}
