namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Status LED indicators control (ACPI device 0x000600C2). Value 7 = LEDs on,
/// 0 = LEDs off. Linux has no kernel interface for this device, so it goes
/// through the raw WMI debugfs path and is only available when raw_wmi is
/// enabled. The auto_status_led config additionally turns the LEDs on at app
/// start and off at shutdown.
/// </summary>
public static class StatusLed
{
    public const uint DeviceId = 0x000600C2;

    private const uint PresenceBit = 0x00010000;

    public static bool IsChannelAvailable() =>
        Helpers.AppConfig.Is("raw_wmi") && AsusWmiDebugfs.IsAvailable();

    /// <summary>Current LED state value, or -1 when unsupported. One privileged call.</summary>
    public static int Get()
    {
        if (!IsChannelAvailable())
            return -1;

        var raw = AsusWmiDebugfs.Dsts(DeviceId);
        if (raw == null || (raw.Value & PresenceBit) == 0)
            return -1;
        return (int)(raw.Value & 0xFFFF);
    }

    /// <summary>Turn the status LEDs on (7) or off (0). One privileged call.</summary>
    public static void Set(bool on)
    {
        if (!IsChannelAvailable())
            return;

        var result = AsusWmiDebugfs.Devs(DeviceId, on ? 7u : 0u);
        Helpers.Logger.WriteLine($"StatusLed: {(on ? "on" : "off")} result={(result.HasValue ? $"0x{result.Value:X}" : "failed")}");
    }

    public static void Init()
    {
        if (Helpers.AppConfig.IsAutoStatusLed())
            Set(true);
    }

    public static void Shutdown()
    {
        if (Helpers.AppConfig.IsAutoStatusLed())
            Set(false);
    }
}
