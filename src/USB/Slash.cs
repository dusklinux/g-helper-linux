using GHelper.Linux.Helpers;

namespace GHelper.Linux.USB;

/// <summary>
/// Controls the ASUS ROG "Slash" LED bar on the laptop lid (A-cover).
/// Protocol reverse-engineered from asusctl's rog-slash crate (v6.3.8).
///
/// Slash models come in two generations:
///   - 2024 (GA403, GU605): PID 0x193B, report ID 0x5E
///   - 2025 (GA403W/UH/UM/UP/GM, GA605, GA605K, GU605C, G614F):
///           PID 0x19B6, report ID 0x5D
///
/// All commands are sent as HID feature reports via /dev/hidraw*. Packet
/// sizes vary (6-32 bytes) but the report-ID byte is always first.
/// </summary>
public static class Slash
{
    // -- Device identification -----------------------------------------------

    /// <summary>Slash-capable product IDs (ASUS vendor 0x0B05).</summary>
    public static readonly int[] SLASH_PIDS = { 0x193B, 0x19B6 };

    private const int PID_2024 = 0x193B;
    private const int PID_2025 = 0x19B6;
    private const byte RID_2024 = 0x5E;
    private const byte RID_2025 = 0x5D;
    private const int PKT = 32; // standard packet size

    // -- Slash animation modes -----------------------------------------------

    /// <summary>Available animation modes for the slash LED bar.</summary>
    public enum SlashMode : byte
    {
        Static = 0x06,
        Bounce = 0x10,
        Slash = 0x12,
        Loading = 0x13,
        Flow = 0x19,  // default
        Transmission = 0x1A,
        BitStream = 0x1D,
        Phantom = 0x24,
        Flux = 0x25,
        Spectrum = 0x26,
        Hazard = 0x32,
        Interfacing = 0x33,
        Ramp = 0x34,
        GameOver = 0x42,
        Start = 0x43,
        Buzzer = 0x44,
    }

    /// <summary>Ordered list of all modes (for combo box population).</summary>
    public static readonly SlashMode[] AllModes =
    {
        SlashMode.Static, SlashMode.Bounce, SlashMode.Slash, SlashMode.Loading,
        SlashMode.Flow, SlashMode.Transmission, SlashMode.BitStream,
        SlashMode.Phantom, SlashMode.Flux, SlashMode.Spectrum,
        SlashMode.Hazard, SlashMode.Interfacing, SlashMode.Ramp,
        SlashMode.GameOver, SlashMode.Start, SlashMode.Buzzer,
    };

    /// <summary>Display name for each mode (used in combo box).</summary>
    public static string ModeName(SlashMode m) => m switch
    {
        SlashMode.Static => "Static",
        SlashMode.Bounce => "Bounce",
        SlashMode.Slash => "Slash",
        SlashMode.Loading => "Loading",
        SlashMode.Flow => "Flow",
        SlashMode.Transmission => "Transmission",
        SlashMode.BitStream => "BitStream",
        SlashMode.Phantom => "Phantom",
        SlashMode.Flux => "Flux",
        SlashMode.Spectrum => "Spectrum",
        SlashMode.Hazard => "Hazard",
        SlashMode.Interfacing => "Interfacing",
        SlashMode.Ramp => "Ramp",
        SlashMode.GameOver => "Game Over",
        SlashMode.Start => "Start",
        SlashMode.Buzzer => "Buzzer",
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
                return RID_2025;
            if (AppConfig.ContainsModel("GA605"))
                return RID_2025;
            if (AppConfig.ContainsModel("GU605C"))
                return RID_2025;
            if (AppConfig.ContainsModel("G614F"))
                return RID_2025;

            // 2024 models: GA403 or GU605 without the 2025 suffixes
            if (AppConfig.ContainsModel("GA403") || AppConfig.ContainsModel("GU605"))
                return RID_2024;

            // Everything else in IsSlash() (GU405, GU606, GX651) - assume 2025 protocol
            return RID_2025;
        }
    }

    /// <summary>
    /// The target PID for the current model. Matches the report-ID generation.
    /// </summary>
    private static int TargetPid => ReportId == RID_2024 ? PID_2024 : PID_2025;

    // -- Low-level write -----------------------------------------------------

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

    // -- Packet builders -----------------------------------------------------

    private static byte[] Pkt(int len = PKT)
    {
        var p = new byte[len];
        p[0] = ReportId;
        return p;
    }

    /// <summary>Two init packets sent once after boot.</summary>
    public static byte[][] PktInit()
    {
        byte rid = ReportId;
        var p1 = new byte[PKT];
        p1[0] = rid;
        p1[1] = 0xD7;
        p1[4] = 0x01;
        p1[5] = 0xAC;

        var p2 = new byte[PKT];
        p2[0] = rid;
        p2[1] = 0xD2;
        p2[2] = 0x02;
        p2[3] = 0x01;
        p2[4] = 0x08;
        p2[5] = 0xAB;

        return new[] { p1, p2 };
    }

    /// <summary>Enable or disable the slash LED bar.</summary>
    public static byte[] PktEnable(bool enabled)
    {
        var p = Pkt();
        p[1] = 0xD8;
        p[2] = 0x02;
        p[4] = 0x01;
        p[5] = enabled ? (byte)0x00 : (byte)0x80;
        return p;
    }

    /// <summary>Save/flush the current state to firmware NVRAM.</summary>
    public static byte[] PktSave()
    {
        var p = Pkt();
        p[1] = 0xD4;
        p[4] = 0x01;
        p[5] = 0xAB;
        return p;
    }

    /// <summary>
    /// Set options: brightness (0-255), enabled state, and animation interval (0-5).
    /// </summary>
    public static byte[] PktOptions(bool enabled, byte brightness, byte interval)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD3, 0x03, 0x01, 0x08, 0xAB, 0xFF, 0x01,
            (byte)(enabled ? 0x01 : 0x00),
            0x06,
            brightness,
            0xFF,
            interval
        };
    }

    /// <summary>Set animation mode. Only pkt2 is actually needed.</summary>
    public static byte[] PktSetMode(SlashMode mode)
    {
        var p = Pkt();
        p[1] = 0xD3;
        p[2] = 0x04;
        p[4] = 0x0C;
        p[5] = 0x01;
        p[6] = (byte)mode;
        p[7] = 0x02;
        p[8] = 0x19;
        p[9] = 0x03;
        p[10] = 0x13;
        p[11] = 0x04;
        p[12] = 0x11;
        p[13] = 0x05;
        p[14] = 0x12;
        p[15] = 0x06;
        p[16] = 0x13;
        return p;
    }

    // -- Power-state packets -------------------------------------------------

    public static byte[] PktShowOnBoot(bool enabled)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD3, 0x03, 0x01, 0x08, 0xA0, 0x04, 0xFF,
            (byte)(enabled ? 0x01 : 0x00),
            0x01, 0xFF, 0x00
        };
    }

    public static byte[] PktShowOnSleep(bool enabled)
    {
        byte rid = ReportId;
        // Inverted: enabled=true => status byte 0x00
        return new byte[]
        {
            rid, 0xD3, 0x03, 0x01, 0x08, 0xA1, 0x00, 0xFF,
            (byte)(enabled ? 0x00 : 0x01),
            0x02, 0xFF, 0xFF
        };
    }

    public static byte[] PktShowLowBattery(bool enabled)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD3, 0x03, 0x01, 0x08, 0xA2, 0x01, 0xFF,
            (byte)(enabled ? 0x01 : 0x00),
            0x02, 0xFF, 0xFF
        };
    }

    public static byte[] PktShowOnShutdown(bool enabled)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD3, 0x03, 0x01, 0x08, 0xA4, 0x05, 0xFF,
            (byte)(enabled ? 0x01 : 0x00),
            0x01, 0xFF, 0x00
        };
    }

    /// <summary>Enable/disable slash on battery power.</summary>
    public static byte[] PktShowOnBattery(bool enabled)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD8, 0x01, 0x00, 0x01,
            (byte)(enabled ? 0x00 : 0x80)
        };
    }

    /// <summary>Enable/disable slash when lid is closed.</summary>
    public static byte[] PktShowOnLidClosed(bool enabled)
    {
        byte rid = ReportId;
        return new byte[]
        {
            rid, 0xD8, 0x00, 0x00, 0x02, 0xA5,
            (byte)(enabled ? 0x00 : 0x80)
        };
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
        var inits = PktInit();
        Write(inits[0], "Slash Init 1");
        Write(inits[1], "Slash Init 2");

        // Apply saved state
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        var mode = (SlashMode)AppConfig.Get("slash_mode", (int)SlashMode.Flow);

        Write(PktEnable(enabled), "Slash Enable");
        Write(PktOptions(enabled, brightness, interval), "Slash Options");
        Write(PktSetMode(mode), "Slash Mode");

        // Power states
        Write(PktShowOnBoot(AppConfig.IsNotFalse("slash_show_boot")), "Slash Boot");
        Write(PktShowOnSleep(AppConfig.IsNotFalse("slash_show_sleep")), "Slash Sleep");
        Write(PktShowOnShutdown(AppConfig.IsNotFalse("slash_show_shutdown")), "Slash Shutdown");
        Write(PktShowOnBattery(AppConfig.IsNotFalse("slash_show_battery")), "Slash Battery");
        Write(PktShowLowBattery(AppConfig.IsNotFalse("slash_show_low_battery")), "Slash LowBat");

        Logger.WriteLine($"Slash: init done (enabled={enabled}, brightness={brightness}, mode={mode})");
    }

    /// <summary>Enable or disable the slash bar and persist.</summary>
    public static void SetEnabled(bool enabled)
    {
        AppConfig.Set("slash_enabled", enabled ? 1 : 0);
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        Write(PktEnable(enabled), "Slash Enable");
        Write(PktOptions(enabled, brightness, interval), "Slash Options");
    }

    /// <summary>Set brightness (0-255) and persist.</summary>
    public static void SetBrightness(int brightness)
    {
        byte b = (byte)Math.Clamp(brightness, 0, 255);
        AppConfig.Set("slash_brightness", b);
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte interval = (byte)AppConfig.Get("slash_interval", 0);
        Write(PktOptions(enabled, b, interval), "Slash Brightness");
    }

    /// <summary>Set animation mode and persist.</summary>
    public static void SetMode(SlashMode mode)
    {
        AppConfig.Set("slash_mode", (int)mode);
        Write(PktSetMode(mode), "Slash Mode");
        Write(PktSave(), "Slash Save");
    }

    /// <summary>Set animation speed/interval (0-5) and persist.</summary>
    public static void SetInterval(int interval)
    {
        byte v = (byte)Math.Clamp(interval, 0, 5);
        AppConfig.Set("slash_interval", v);
        bool enabled = AppConfig.IsNotFalse("slash_enabled");
        byte brightness = (byte)AppConfig.Get("slash_brightness", 0xFF);
        Write(PktOptions(enabled, brightness, v), "Slash Interval");
    }

    /// <summary>Set a power-state toggle and persist.</summary>
    public static void SetPowerState(string key, bool enabled)
    {
        AppConfig.Set(key, enabled ? 1 : 0);
        byte[] pkt = key switch
        {
            "slash_show_boot" => PktShowOnBoot(enabled),
            "slash_show_sleep" => PktShowOnSleep(enabled),
            "slash_show_shutdown" => PktShowOnShutdown(enabled),
            "slash_show_battery" => PktShowOnBattery(enabled),
            "slash_show_low_battery" => PktShowLowBattery(enabled),
            "slash_show_lid_closed" => PktShowOnLidClosed(enabled),
            _ => Array.Empty<byte>(),
        };
        if (pkt.Length > 0)
        {
            Write(pkt, $"Slash {key}");
            if (key == "slash_show_lid_closed")
                Write(PktSave(), "Slash Save");
        }
    }
}
