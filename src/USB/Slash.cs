using GHelper.Linux.AnimeMatrix;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.USB;

/// <summary>
/// Hidraw facade for the ASUS ROG "Slash" LED bar (A-cover).
/// All protocol bytes live in <see cref="SlashDevice"/>; this class only picks
/// the report ID / PID for the current model and writes exact-size packets
/// to /dev/hidraw* via <see cref="HidrawHelper"/>.
///
/// Slash models come in two generations:
///   - 2024 (GA403, GU605): PID 0x193B, report ID 0x5E
///   - 2025 (GA403W/UH/UM/UP/GM, GA605, GA605K, GU605C, G614F):
///           PID 0x19B6, report ID 0x5D
/// </summary>
public static class Slash
{
    private const int PKT = 32; // standard packet size

    /// <summary>Ordered list of all UI modes (for combo box population).</summary>
    public static readonly SlashMode[] AllModes =
    {
        SlashMode.Static, SlashMode.Bounce, SlashMode.Slash, SlashMode.Loading,
        SlashMode.Flow, SlashMode.Transmission, SlashMode.BitStream,
        SlashMode.Phantom, SlashMode.Flux, SlashMode.Spectrum,
        SlashMode.Hazard, SlashMode.Interfacing, SlashMode.Ramp,
        SlashMode.GameOver, SlashMode.Start, SlashMode.Buzzer,
    };

    /// <summary>Firmware mode code for a mode (from SlashDevice.ModeCodes).</summary>
    public static int ModeCode(SlashMode m) => SlashDevice.ModeCodes[m];

    /// <summary>Display name for each mode (used in combo box).</summary>
    public static string ModeName(SlashMode m) => m switch
    {
        SlashMode.GameOver => "Game Over",
        _ => m.ToString(),
    };

    // -- Detection -----------------------------------------------------------

    /// <summary>True when the current device is a slash-capable model.</summary>
    public static bool IsSupported => AppConfig.IsSlash();

    /// <summary>
    /// Determine the correct report ID for the current model.
    /// 2024 models (GA403 without W/UH/UM/UP/GM suffix, GU605 without C suffix)
    /// use 0x5E; all 2025+ models use 0x5D.
    /// </summary>
    private static byte ReportId
    {
        get
        {
            // 2025 models: explicit suffixes
            if (AppConfig.ContainsModel("GA403W") || AppConfig.ContainsModel("GA403UH") ||
                AppConfig.ContainsModel("GA403UM") || AppConfig.ContainsModel("GA403UP") ||
                AppConfig.ContainsModel("GA403GM"))
                return SlashDevice.ReportIdAura;
            if (AppConfig.ContainsModel("GA605"))
                return SlashDevice.ReportIdAura;
            if (AppConfig.ContainsModel("GU605C"))
                return SlashDevice.ReportIdAura;
            if (AppConfig.ContainsModel("G614F"))
                return SlashDevice.ReportIdAura;

            // 2024 models: GA403 or GU605 without the 2025 suffixes
            if (AppConfig.ContainsModel("GA403") || AppConfig.ContainsModel("GU605"))
                return SlashDevice.ReportIdStandard;

            // Everything else in IsSlash() (GU405, GU606, GX651) - assume 2025 protocol
            return SlashDevice.ReportIdAura;
        }
    }

    /// <summary>
    /// The target PID for the current model. Matches the report-ID generation.
    /// </summary>
    private static int TargetPid => ReportId == SlashDevice.ReportIdStandard
        ? SlashDevice.ProductIdStandard
        : SlashDevice.ProductIdAura;

    // -- Low-level write -----------------------------------------------------

    /// <summary>Prepends the report ID and pads to minLength (legacy packet sizing).</summary>
    private static byte[] BuildPacket(byte[] command, int minLength = 0)
    {
        var p = new byte[Math.Max(command.Length + 1, minLength)];
        p[0] = ReportId;
        Array.Copy(command, 0, p, 1, command.Length);
        return p;
    }

    /// <summary>
    /// Write a single slash packet to the appropriate hidraw device.
    /// Uses HidrawHelper.WriteAllForPids with the slash PID array.
    /// </summary>
    private static bool Write(byte[] data, string? log = null)
    {
        int pid = TargetPid;
        return HidrawHelper.WriteAllForPids(
            data[0],  // report ID is byte[0]
            new[] { data },
            new[] { pid },
            data.Length,  // exact packet size, no extra padding
            log);
    }

    // -- High-level API ------------------------------------------------------

    /// <summary>
    /// Full initialization sequence. Call once at startup for slash models.
    /// Sends init packets, then applies saved config (or defaults).
    /// </summary>
    public static void Init()
    {
        if (!IsSupported)
            return;

        Logger.WriteLine("Slash: initializing");

        // Init handshake
        var inits = SlashDevice.CommandInit();
        Write(BuildPacket(inits[0], PKT), "Slash Init 1");
        Write(BuildPacket(inits[1], PKT), "Slash Init 2");

        // Apply saved state
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        int mode = AppConfig.Get("slash_mode", ModeCode(SlashMode.Flow));

        Write(BuildPacket(SlashDevice.CommandEnable(enabled), PKT), "Slash Enable");
        Write(BuildPacket(SlashDevice.CommandOptions(enabled, brightness, interval)), "Slash Options");
        Write(BuildPacket(SlashDevice.CommandSetModeLegacy((byte)mode), PKT), "Slash Mode");

        // Power states
        Write(BuildPacket(SlashDevice.CommandShowOnBoot(AppConfig.IsNotFalse("slash_show_boot"))), "Slash Boot");
        Write(BuildPacket(SlashDevice.CommandShowOnSleep(!AppConfig.IsNotFalse("slash_show_sleep"))), "Slash Sleep");
        Write(BuildPacket(SlashDevice.CommandShowOnShutdown(AppConfig.IsNotFalse("slash_show_shutdown"))), "Slash Shutdown");
        Write(BuildPacket(SlashDevice.CommandBatterySaver(!AppConfig.IsNotFalse("slash_show_battery"))), "Slash Battery");
        Write(BuildPacket(SlashDevice.CommandShowLowBattery(AppConfig.IsNotFalse("slash_show_low_battery"))), "Slash LowBat");

        Logger.WriteLine($"Slash: init done (enabled={enabled}, brightness={brightness}, mode={mode})");
    }

    /// <summary>Enable or disable the slash bar and persist.</summary>
    public static void SetEnabled(bool enabled)
    {
        AppConfig.Set("slash_enabled", enabled ? 1 : 0);
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        Write(BuildPacket(SlashDevice.CommandEnable(enabled), PKT), "Slash Enable");
        Write(BuildPacket(SlashDevice.CommandOptions(enabled, brightness, interval)), "Slash Options");
    }

    /// <summary>Set brightness (0-255) and persist.</summary>
    public static void SetBrightness(int brightness)
    {
        byte b = (byte)Math.Clamp(brightness, 0, 255);
        AppConfig.Set("slash_brightness", b);
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        Write(BuildPacket(SlashDevice.CommandOptions(enabled, b, interval)), "Slash Brightness");
    }

    /// <summary>Set animation mode (firmware mode code) and persist.</summary>
    public static void SetMode(int modeCode)
    {
        AppConfig.Set("slash_mode", modeCode);
        Write(BuildPacket(SlashDevice.CommandSetModeLegacy((byte)modeCode), PKT), "Slash Mode");
        Write(BuildPacket(SlashDevice.CommandSave(), PKT), "Slash Save");
    }

    /// <summary>Set animation speed/interval (0-5) and persist.</summary>
    public static void SetInterval(int interval)
    {
        byte v = (byte)Math.Clamp(interval, 0, 5);
        AppConfig.Set("slash_interval", v);
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        Write(BuildPacket(SlashDevice.CommandOptions(enabled, brightness, v)), "Slash Interval");
    }

    /// <summary>Set a power-state toggle and persist.</summary>
    public static void SetPowerState(string key, bool enabled)
    {
        AppConfig.Set(key, enabled ? 1 : 0);
        byte[] command = key switch
        {
            "slash_show_boot" => SlashDevice.CommandShowOnBoot(enabled),
            "slash_show_sleep" => SlashDevice.CommandShowOnSleep(!enabled),
            "slash_show_shutdown" => SlashDevice.CommandShowOnShutdown(enabled),
            "slash_show_battery" => SlashDevice.CommandBatterySaver(!enabled),
            "slash_show_low_battery" => SlashDevice.CommandShowLowBattery(enabled),
            "slash_show_lid_closed" => SlashDevice.CommandLidCloseAnimation(enabled),
            _ => Array.Empty<byte>(),
        };
        if (command.Length > 0)
        {
            Write(BuildPacket(command), $"Slash {key}");
            if (key == "slash_show_lid_closed")
                Write(BuildPacket(SlashDevice.CommandSave(), PKT), "Slash Save");
        }
    }
}
