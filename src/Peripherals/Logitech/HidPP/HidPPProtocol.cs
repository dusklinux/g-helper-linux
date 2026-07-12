using GHelper.Linux.Helpers;

namespace GHelper.Linux.Peripherals.Logitech.HidPP;

/// <summary>
/// Provides typed methods for reading and writing Logitech HID++ 2.0
/// device settings: battery, DPI, polling rate, RGB, backlight, etc.
/// Each method is a static helper that operates on an <see cref="HidPPDevice"/>.
/// </summary>
public static class HidPPProtocol
{
    /// <summary>
    /// Reads battery status from the device.
    /// Tries UNIFIED_BATTERY first, then BATTERY_STATUS, then BATTERY_VOLTAGE.
    /// </summary>
    /// <returns>Battery level (0-100) and charging flag, or null if unsupported.</returns>
    public static (int level, bool charging)? GetBattery(HidPPDevice device)
    {
        var unified = GetBatteryUnified(device);
        if (unified is not null)
            return unified;

        var status = GetBatteryStatus(device);
        if (status is not null)
            return status;

        return GetBatteryVoltage(device);
    }

    /// <summary>
    /// Reads UNIFIED_BATTERY (0x1004) feature.
    /// Function 0x10 = GetStatus: [0]=stateOfCharge%, [1]=batteryLevel, [2]=chargingStatus, [3]=externalPowerStatus.
    /// </summary>
    private static (int level, bool charging)? GetBatteryUnified(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.UNIFIED_BATTERY) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.UNIFIED_BATTERY, 0x10);
        if (reply is null || reply.Length < 5)
            return null;

        // reply[2] = stateOfCharge (0-100)
        // reply[3] = batteryLevel bitfield (8=full, 4=good, 2=low, 1=critical)
        // reply[4] = chargingStatus: 0=discharging, 1=charging, 2=chargingNearlyFull,
        // 3=full, 4=chargingSlow, 5=invalidBattery, 6=thermalError, 7=chargingError
        int soc = reply[2];
        byte chargingStatus = reply[4];
        bool charging = chargingStatus is 1 or 2 or 3 or 4;

        // If SoC is zero, estimate from batteryLevel bits.
        if (soc == 0)
        {
            byte levelBits = reply[3];
            soc = levelBits switch
            {
                8 => 90,
                4 => 50,
                2 => 20,
                1 => 5,
                _ => 0,
            };
        }

        return (soc, charging);
    }

    /// <summary>
    /// Reads BATTERY_STATUS (0x1000) feature.
    /// Function 0x00 = GetStatus: [0]=level%, [1]=nextLevel%, [2]=status.
    /// Status: 0=discharging, 1/2/3=recharging, 4=charge complete.
    /// </summary>
    private static (int level, bool charging)? GetBatteryStatus(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.BATTERY_STATUS) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.BATTERY_STATUS, 0x00);
        if (reply is null || reply.Length < 5)
            return null;

        int level = reply[2];
        byte status = reply[4];
        bool charging = status is >= 1 and <= 3;

        if (level == 0)
            return null;

        return (level, charging);
    }

    /// <summary>
    /// Reads BATTERY_VOLTAGE (0x1001) feature.
    /// Function 0x00 = GetBatteryVoltage: [0-1]=voltage (big-endian mV), [2]=flags.
    /// Estimates percentage from voltage using linear interpolation.
    /// </summary>
    private static (int level, bool charging)? GetBatteryVoltage(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.BATTERY_VOLTAGE) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.BATTERY_VOLTAGE, 0x00);
        if (reply is null || reply.Length < 5)
            return null;

        int voltage = (reply[2] << 8) | reply[3];
        byte flags = reply[4];
        bool charging = (flags & 0x80) != 0;

        int level = EstimateBatteryPercentage(voltage);
        return (level, charging);
    }

    /// <summary>
    /// Estimates battery percentage from voltage in mV.
    /// </summary>
    private static int EstimateBatteryPercentage(int millivolts)
    {
        ReadOnlySpan<(int voltage, int percent)> table =
        [
            (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60),
            (3811, 50),  (3778, 40), (3751, 30), (3717, 20), (3671, 10),
            (3646, 5),   (3579, 2),  (3500, 0),
        ];

        if (millivolts >= table[0].voltage)
            return 100;
        if (millivolts <= table[^1].voltage)
            return 0;

        for (int i = 0; i < table.Length - 1; i++)
        {
            var (vHigh, pHigh) = table[i];
            var (vLow, pLow) = table[i + 1];
            if (millivolts >= vLow && millivolts <= vHigh)
            {
                float pct = pLow + (pHigh - pLow) * (float)(millivolts - vLow) / (vHigh - vLow);
                return (int)Math.Round(pct);
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads the device name via DEVICE_NAME (0x0005) feature.
    /// Function 0x00 = GetNameLength, function 0x10 = GetName (chunked).
    /// </summary>
    public static string? GetDeviceName(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.DEVICE_NAME) < 0)
            return null;

        byte[]? lenReply = device.FeatureRequest(Feature.DEVICE_NAME, 0x00);
        if (lenReply is null || lenReply.Length < 3)
            return null;

        int nameLen = lenReply[2];
        if (nameLen == 0)
            return null;

        var nameBytes = new byte[nameLen];
        int offset = 0;
        while (offset < nameLen)
        {
            byte[]? chunk = device.FeatureRequest(Feature.DEVICE_NAME, 0x10, (byte)offset);
            if (chunk is null || chunk.Length < 3)
                break;

            // Payload starts at index 2 (after subId, addr).
            int available = Math.Min(chunk.Length - 2, nameLen - offset);
            Buffer.BlockCopy(chunk, 2, nameBytes, offset, available);
            offset += available;
        }

        return System.Text.Encoding.UTF8.GetString(nameBytes, 0, Math.Min(offset, nameLen));
    }

    /// <summary>
    /// Reads the device unit ID via DEVICE_UNIT_ID (0x0004).
    /// Returns a hex string (e.g. "A1B2C3D4") or null if unsupported.
    /// </summary>
    public static string? GetUnitId(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.DEVICE_UNIT_ID) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.DEVICE_UNIT_ID, 0x00);
        if (reply is null || reply.Length < 6)
            return null;

        // Payload starts at byte 2. Unit ID is 4 bytes (bytes 2-5).
        return $"{reply[2]:X2}{reply[3]:X2}{reply[4]:X2}{reply[5]:X2}";
    }

    /// <summary>
    /// Reads the firmware version via DEVICE_FW_VERSION (0x0003) feature.
    /// Function 0x00 = GetEntityCount, function 0x10 = GetFwInfo.
    /// Returns the main firmware version string (e.g. "09.02.B0004").
    /// </summary>
    public static string? GetFirmwareVersion(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.DEVICE_FW_VERSION) < 0)
            return null;

        byte[]? countReply = device.FeatureRequest(Feature.DEVICE_FW_VERSION, 0x00);
        if (countReply is null || countReply.Length < 3)
            return null;

        int count = countReply[2];

        for (int i = 0; i < count; i++)
        {
            byte[]? info = device.FeatureRequest(Feature.DEVICE_FW_VERSION, 0x10, (byte)i);
            if (info is null || info.Length < 10)
                continue;

            // info[2] = entity type (low nibble): 0=firmware, 1=bootloader, 2=hardware
            int entityType = info[2] & 0x0F;
            if (entityType != 0)
                continue;

            // info[3..5] = name (3 ASCII chars)
            // info[6] = version major (hex)
            // info[7] = version minor (hex)
            // info[8..9] = build number (big-endian)
            byte major = info[6];
            byte minor = info[7];
            int build = (info[8] << 8) | info[9];

            string version = $"{major:X2}.{minor:X2}";
            if (build != 0)
                version += $".B{build:X4}";

            return version;
        }

        return null;
    }

    /// <summary>
    /// Reads the current DPI via ADJUSTABLE_DPI (0x2201) feature.
    /// Function 0x10 = GetSensorDPI(sensor):
    ///   Response: [sensor, currentDPI_hi, currentDPI_lo, defaultDPI_hi, defaultDPI_lo, maxOrStep_hi, maxOrStep_lo]
    ///   If maxOrStep > 0xE000, it is the DPI step (value &amp; 0x1FFF).
    ///   Otherwise it is the maximum DPI.
    /// </summary>
    /// <returns>(currentDPI, maxDPI, step) where step=0 means the max field is the absolute max.</returns>
    public static (int current, int max, int step)? GetDPI(HidPPDevice device, byte sensor = 0)
    {
        if (device.GetFeatureIndex(Feature.ADJUSTABLE_DPI) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.ADJUSTABLE_DPI, 0x10, sensor);
        if (reply is null || reply.Length < 9)
            return null;

        int currentDpi = (reply[3] << 8) | reply[4];
        int defaultDpi = (reply[5] << 8) | reply[6];
        int maxOrStep = (reply[7] << 8) | reply[8];

        int maxDpi, step;
        // Values > 0xE000 encode the DPI step (low 13 bits).
        // In step mode the "default" field is the default DPI, not max.
        if (maxOrStep > 0xE000)
        {
            step = maxOrStep & 0x1FFF;
            maxDpi = 32000; // safe upper bound; real max unknown in step mode
        }
        else
        {
            step = 0;
            maxDpi = maxOrStep;
        }

        return (currentDpi, maxDpi, step);
    }

    /// <summary>
    /// Sets the DPI for a sensor via ADJUSTABLE_DPI (0x2201), function 0x20.
    /// </summary>
    public static bool SetDPI(HidPPDevice device, int dpi, byte sensor = 0)
    {
        if (device.GetFeatureIndex(Feature.ADJUSTABLE_DPI) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.ADJUSTABLE_DPI, 0x20,
            sensor,
            (byte)(dpi >> 8),
            (byte)(dpi & 0xFF));

        return reply is not null;
    }

    /// <summary>
    /// Reads the DPI for a sensor via EXTENDED_ADJUSTABLE_DPI (0x2202), function 0x10.
    /// Supports separate X/Y DPI.
    /// </summary>
    public static (int currentX, int currentY, int max, int step)? GetExtendedDPI(
        HidPPDevice device, byte sensor = 0)
    {
        if (device.GetFeatureIndex(Feature.EXTENDED_ADJUSTABLE_DPI) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.EXTENDED_ADJUSTABLE_DPI, 0x10, sensor);
        if (reply is null || reply.Length < 11)
            return null;

        // [subId, addr, sensor, dpiX_hi, dpiX_lo, dpiY_hi, dpiY_lo,
        //  defaultDpi_hi, defaultDpi_lo, maxOrStep_hi, maxOrStep_lo, ...]
        int dpiX = (reply[3] << 8) | reply[4];
        int dpiY = (reply[5] << 8) | reply[6];
        int maxOrStep = (reply[9] << 8) | reply[10];

        int maxDpi, step;
        if (maxOrStep > 0xE000)
        {
            step = maxOrStep & 0x1FFF;
            maxDpi = (reply[7] << 8) | reply[8];
        }
        else
        {
            step = 0;
            maxDpi = maxOrStep;
        }

        return (dpiX, dpiY, maxDpi, step);
    }

    /// <summary>
    /// Sets separate X/Y DPI via EXTENDED_ADJUSTABLE_DPI (0x2202), function 0x20.
    /// </summary>
    public static bool SetExtendedDPI(HidPPDevice device, int dpiX, int dpiY, byte sensor = 0)
    {
        if (device.GetFeatureIndex(Feature.EXTENDED_ADJUSTABLE_DPI) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.EXTENDED_ADJUSTABLE_DPI, 0x20,
            sensor,
            (byte)(dpiX >> 8), (byte)(dpiX & 0xFF),
            (byte)(dpiY >> 8), (byte)(dpiY & 0xFF));

        return reply is not null;
    }

    /// <summary>
    /// Reads the polling (report) rate via REPORT_RATE (0x8060).
    /// Function 0x00 = GetReportRateList: returns a bitmask of supported rates.
    /// Function 0x10 = GetReportRate: returns current rate in ms.
    /// Bitmask: bit0=1ms, bit1=2ms, bit2=4ms, bit3=8ms, bit4=500us, bit5=250us, bit6=125us.
    /// </summary>
    public static (int currentMs, int[] supportedMs)? GetPollingRate(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.REPORT_RATE) < 0)
            return null;

        byte[]? listReply = device.FeatureRequest(Feature.REPORT_RATE, 0x00);
        if (listReply is null || listReply.Length < 3)
            return null;

        byte rateBits = listReply[2];
        var supported = new List<int>();
        // REPORT_RATE bitmask: bit N = (N+1) ms
        for (int i = 0; i < 8; i++)
        {
            if ((rateBits & (1 << i)) != 0)
                supported.Add(i + 1);
        }

        byte[]? curReply = device.FeatureRequest(Feature.REPORT_RATE, 0x10);
        if (curReply is null || curReply.Length < 3)
            return null;

        int currentMs = curReply[2];

        return (currentMs, supported.ToArray());
    }

    /// <summary>
    /// Sets the polling rate in ms via REPORT_RATE (0x8060), function 0x20.
    /// </summary>
    public static bool SetPollingRate(HidPPDevice device, int rateMs)
    {
        if (device.GetFeatureIndex(Feature.REPORT_RATE) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.REPORT_RATE, 0x20, (byte)rateMs);
        return reply is not null;
    }

    /// <summary>
    /// Extended report rate index to microseconds mapping for EXTENDED_ADJUSTABLE_REPORT_RATE (0x8061).
    /// Bitmask: bit0=8ms, bit1=4ms, bit2=2ms, bit3=1ms, bit4=500us, bit5=250us, bit6=125us.
    /// </summary>
    private static readonly int[] ExtendedRateIndexToUs =
        [8000, 4000, 2000, 1000, 500, 250, 125];

    /// <summary>
    /// Reads extended polling rate via EXTENDED_ADJUSTABLE_REPORT_RATE (0x8061).
    /// Function 0x10 = GetReportRateList (2-byte bitmask), function 0x20 = GetReportRate.
    /// </summary>
    public static (int currentIndex, int[] supportedIndices)? GetExtendedPollingRate(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.EXTENDED_ADJUSTABLE_REPORT_RATE) < 0)
            return null;

        byte[]? listReply = device.FeatureRequest(Feature.EXTENDED_ADJUSTABLE_REPORT_RATE, 0x10);
        if (listReply is null || listReply.Length < 4)
            return null;

        ushort rateBits = (ushort)((listReply[2] << 8) | listReply[3]);
        var supported = new List<int>();
        for (int i = 0; i < ExtendedRateIndexToUs.Length; i++)
        {
            if ((rateBits & (1 << i)) != 0)
                supported.Add(i);
        }

        byte[]? curReply = device.FeatureRequest(Feature.EXTENDED_ADJUSTABLE_REPORT_RATE, 0x20);
        if (curReply is null || curReply.Length < 3)
            return null;

        int currentIndex = curReply[2];
        return (currentIndex, supported.ToArray());
    }

    /// <summary>
    /// Sets extended polling rate via EXTENDED_ADJUSTABLE_REPORT_RATE (0x8061), function 0x30.
    /// </summary>
    public static bool SetExtendedPollingRate(HidPPDevice device, byte rateIndex)
    {
        if (device.GetFeatureIndex(Feature.EXTENDED_ADJUSTABLE_REPORT_RATE) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.EXTENDED_ADJUSTABLE_REPORT_RATE, 0x30, rateIndex);
        return reply is not null;
    }

    /// <summary>
    /// Reads smart shift state via SMART_SHIFT (0x2110).
    /// Function 0x00 = GetRatchetControlMode:
    ///   [0]=mode (1=freewheel, 2=ratchet), [1]=autoDisengage threshold.
    /// </summary>
    public static (bool ratchet, int autoThreshold)? GetSmartShift(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.SMART_SHIFT) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.SMART_SHIFT, 0x00);
        if (reply is null || reply.Length < 4)
            return null;

        byte mode = reply[2]; // 1=freewheel, 2=ratchet
        byte threshold = reply[3];
        return (mode == 2, threshold);
    }

    /// <summary>
    /// Sets smart shift mode via SMART_SHIFT (0x2110), function 0x10.
    /// </summary>
    public static bool SetSmartShift(HidPPDevice device, bool ratchet, int autoThreshold = 0)
    {
        if (device.GetFeatureIndex(Feature.SMART_SHIFT) < 0)
            return false;

        byte mode = (byte)(ratchet ? 2 : 1);
        byte threshold = (byte)Math.Clamp(autoThreshold, 0, 255);

        byte[]? reply = device.FeatureRequest(Feature.SMART_SHIFT, 0x10, mode, threshold);
        return reply is not null;
    }

    /// <summary>
    /// Reads hi-res wheel mode via HIRES_WHEEL (0x2121).
    /// Function 0x10 = GetWheelMode: byte[0] bitfield:
    ///   bit 0 = target (HID++ vs HID), bit 1 = hi-res, bit 2 = invert.
    /// </summary>
    public static (bool hiRes, bool invert, bool divert)? GetHiResScroll(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.HIRES_WHEEL) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.HIRES_WHEEL, 0x10);
        if (reply is null || reply.Length < 3)
            return null;

        byte wheelMode = reply[2];
        bool divert = (wheelMode & 0x01) != 0;
        bool hiRes = (wheelMode & 0x02) != 0;
        bool invert = (wheelMode & 0x04) != 0;
        return (hiRes, invert, divert);
    }

    /// <summary>
    /// Sets hi-res wheel mode via HIRES_WHEEL (0x2121), function 0x20.
    /// </summary>
    public static bool SetHiResScroll(HidPPDevice device, bool hiRes, bool invert, bool divert = false)
    {
        if (device.GetFeatureIndex(Feature.HIRES_WHEEL) < 0)
            return false;

        byte mode = 0;
        if (divert)
            mode |= 0x01;
        if (hiRes)
            mode |= 0x02;
        if (invert)
            mode |= 0x04;

        byte[]? reply = device.FeatureRequest(Feature.HIRES_WHEEL, 0x20, mode);
        return reply is not null;
    }

    /// <summary>
    /// Gets the number of RGB zones via RGB_EFFECTS (0x8071), function 0x00.
    /// </summary>
    public static int GetRGBZoneCount(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.RGB_EFFECTS) < 0)
            return 0;

        // Args [0xFF, 0xFF, 0x00] to query general info.
        byte[]? reply = device.FeatureRequest(Feature.RGB_EFFECTS, 0x00, 0xFF, 0xFF, 0x00);
        if (reply is null || reply.Length < 5)
            return 0;

        // Response: [subId, addr, ?, ?, zoneCount, ...]
        return reply[4];
    }

    /// <summary>
    /// Gets the current RGB effect for a zone via RGB_EFFECTS (0x8071), function 0x30.
    /// </summary>
    /// <returns>Tuple of (R, G, B, effectId) or null.</returns>
    public static (byte r, byte g, byte b, byte effect)? GetRGBZoneEffect(HidPPDevice device, byte zone)
    {
        if (device.GetFeatureIndex(Feature.RGB_EFFECTS) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.RGB_EFFECTS, 0x30, zone);
        if (reply is null || reply.Length < 7)
            return null;

        byte effectId = reply[4];
        byte r = reply.Length > 5 ? reply[5] : (byte)0;
        byte g = reply.Length > 6 ? reply[6] : (byte)0;
        byte b = reply.Length > 7 ? reply[7] : (byte)0;

        return (r, g, b, effectId);
    }

    /// <summary>
    /// Sets the RGB effect for a zone via RGB_EFFECTS (0x8071), function 0x40.
    /// Effects: 0x00=off, 0x01=static, 0x02=pulse/breathing, 0x03=cycle, 0x04=wave.
    /// </summary>
    public static bool SetRGBZoneEffect(HidPPDevice device, byte zone, byte effect,
        byte r, byte g, byte b, byte speed = 0)
    {
        if (device.GetFeatureIndex(Feature.RGB_EFFECTS) < 0)
            return false;

        ushort period = (ushort)(speed * 100);

        byte[]? reply = device.FeatureRequest(Feature.RGB_EFFECTS, 0x40,
            zone, effect, r, g, b,
            (byte)(period >> 8), (byte)(period & 0xFF),
            0x00, 0x00, 0x00, 0x00);

        return reply is not null;
    }

    /// <summary>
    /// Reads the backlight configuration via BACKLIGHT2 (0x1982), function 0x00.
    /// Returns the current brightness level (0-based index).
    /// </summary>
    public static int? GetBacklightLevel(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.BACKLIGHT2) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.BACKLIGHT2, 0x00);
        if (reply is null || reply.Length < 7)
            return null;

        // [subId, addr, enabled, options, supported, effectsHi, effectsLo, level, ...]
        return reply[7];
    }

    /// <summary>
    /// Sets the backlight brightness via BACKLIGHT2 (0x1982), function 0x10.
    /// </summary>
    public static bool SetBacklightLevel(HidPPDevice device, int level)
    {
        if (device.GetFeatureIndex(Feature.BACKLIGHT2) < 0)
            return false;

        byte[]? current = device.FeatureRequest(Feature.BACKLIGHT2, 0x00);
        if (current is null || current.Length < 14)
            return false;

        // SetBacklightConfig: [enabled, options, 0xFF, level, dho_hi, dho_lo, dhi_hi, dhi_lo, dpow_hi, dpow_lo]
        byte enabled = current[2];
        byte options = current[3];
        // Force manual mode (mode=3 in bits 3-4) to allow direct level control.
        options = (byte)((options & 0x07) | (0x03 << 3));

        byte[]? reply = device.FeatureRequest(Feature.BACKLIGHT2, 0x10,
            enabled, options, 0xFF, (byte)level,
            current[8], current[9],
            current[10], current[11],
            current[12], current[13]);

        return reply is not null;
    }

    /// <summary>
    /// Gets the active onboard profile index via ONBOARD_PROFILES (0x8100), function 0x20.
    /// </summary>
    public static int? GetActiveProfile(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.ONBOARD_PROFILES) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.ONBOARD_PROFILES, 0x20);
        if (reply is null || reply.Length < 3)
            return null;

        return reply[2];
    }

    /// <summary>
    /// Sets the active onboard profile via ONBOARD_PROFILES (0x8100), function 0x10.
    /// </summary>
    public static bool SetActiveProfile(HidPPDevice device, int profile)
    {
        if (device.GetFeatureIndex(Feature.ONBOARD_PROFILES) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.ONBOARD_PROFILES, 0x10, (byte)profile);
        return reply is not null;
    }

    /// <summary>
    /// Reads the onboard mode via ONBOARD_PROFILES (0x8100), function 0x20.
    /// Returns true for onboard mode (0x02), false for host mode (0x01).
    /// </summary>
    public static bool? GetOnboardMode(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.ONBOARD_PROFILES) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.ONBOARD_PROFILES, 0x20);
        if (reply is null || reply.Length < 3)
            return null;

        return reply[2] == 0x02;
    }

    /// <summary>
    /// Sets the onboard mode via ONBOARD_PROFILES (0x8100), function 0x10.
    /// When onboard=true, the device uses stored profiles (0x02).
    /// When onboard=false, the device is controlled by host software (0x01).
    /// </summary>
    public static bool SetOnboardMode(HidPPDevice device, bool onboard)
    {
        if (device.GetFeatureIndex(Feature.ONBOARD_PROFILES) < 0)
            return false;

        byte mode = (byte)(onboard ? 0x02 : 0x01);
        byte[]? reply = device.FeatureRequest(Feature.ONBOARD_PROFILES, 0x10, mode);
        return reply is not null;
    }

    /// <summary>
    /// Reads LED brightness via BRIGHTNESS_CONTROL (0x8040), function 0x10.
    /// Returns brightness as 0-100.
    /// </summary>
    public static int? GetBrightnessControl(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.BRIGHTNESS_CONTROL) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.BRIGHTNESS_CONTROL, 0x10);
        if (reply is null || reply.Length < 4)
            return null;

        int brightness = (reply[2] << 8) | reply[3];
        return Math.Clamp(brightness, 0, 100);
    }

    /// <summary>
    /// Sets LED brightness via BRIGHTNESS_CONTROL (0x8040), function 0x20.
    /// Level is 0-100.
    /// </summary>
    public static bool SetBrightnessControl(HidPPDevice device, int level)
    {
        if (device.GetFeatureIndex(Feature.BRIGHTNESS_CONTROL) < 0)
            return false;

        level = Math.Clamp(level, 0, 100);
        byte[]? reply = device.FeatureRequest(Feature.BRIGHTNESS_CONTROL, 0x20,
            (byte)(level >> 8), (byte)(level & 0xFF));
        return reply is not null;
    }

    /// <summary>
    /// Reads smart shift state via SMART_SHIFT_ENHANCED (0x2111), function 0x10.
    /// Returns mode, auto-disengage threshold, and ratchet torque.
    /// </summary>
    public static (bool ratchet, int autoThreshold, int torque)? GetSmartShiftEnhanced(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.SMART_SHIFT_ENHANCED) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.SMART_SHIFT_ENHANCED, 0x10);
        if (reply is null || reply.Length < 5)
            return null;

        byte mode = reply[2]; // 1=freewheel, 2=ratchet
        byte threshold = reply[3];
        byte torque = reply[4];
        return (mode == 2, threshold, torque);
    }

    /// <summary>
    /// Sets smart shift mode via SMART_SHIFT_ENHANCED (0x2111), function 0x20.
    /// </summary>
    public static bool SetSmartShiftEnhanced(HidPPDevice device, bool ratchet,
        int autoThreshold = 0, int torque = 0)
    {
        if (device.GetFeatureIndex(Feature.SMART_SHIFT_ENHANCED) < 0)
            return false;

        byte mode = (byte)(ratchet ? 2 : 1);
        byte thresholdVal = (byte)Math.Clamp(autoThreshold, 0, 255);
        byte torqueVal = (byte)Math.Clamp(torque, 0, 100);

        byte[]? reply = device.FeatureRequest(Feature.SMART_SHIFT_ENHANCED, 0x20,
            mode, thresholdVal, torqueVal);
        return reply is not null;
    }

    /// <summary>
    /// Reads thumb wheel status via THUMB_WHEEL (0x2150), function 0x10.
    /// Response bytes: [0]=divert flags, [1]=invert flags.
    /// Divert bit 0: HID++ notification mode. Invert bit 0: scroll direction inverted.
    /// </summary>
    public static (bool divert, bool invert)? GetThumbWheel(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.THUMB_WHEEL) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.THUMB_WHEEL, 0x10);
        if (reply is null || reply.Length < 4)
            return null;

        bool divert = (reply[2] & 0x01) != 0;
        bool invert = (reply[3] & 0x01) != 0;
        return (divert, invert);
    }

    /// <summary>
    /// Sets thumb wheel mode via THUMB_WHEEL (0x2150), function 0x20.
    /// </summary>
    public static bool SetThumbWheel(HidPPDevice device, bool divert, bool invert)
    {
        if (device.GetFeatureIndex(Feature.THUMB_WHEEL) < 0)
            return false;

        byte divertByte = (byte)(divert ? 0x01 : 0x00);
        byte invertByte = (byte)(invert ? 0x01 : 0x00);

        byte[]? reply = device.FeatureRequest(Feature.THUMB_WHEEL, 0x20, divertByte, invertByte);
        return reply is not null;
    }

    /// <summary>
    /// Reads the number of reprogrammable controls via REPROG_CONTROLS_V4 (0x1B04),
    /// function 0x00. Falls back to REPROG_CONTROLS_V2 (0x1B01) if V4 is absent.
    /// </summary>
    public static int GetReprogControlsCount(HidPPDevice device)
    {
        ushort feature = device.GetFeatureIndex(Feature.REPROG_CONTROLS_V4) >= 0
            ? Feature.REPROG_CONTROLS_V4
            : Feature.REPROG_CONTROLS_V2;

        if (device.GetFeatureIndex(feature) < 0)
            return 0;

        byte[]? reply = device.FeatureRequest(feature, 0x00);
        if (reply is null || reply.Length < 3)
            return 0;

        return reply[2];
    }

    /// <summary>
    /// Reads info for a single reprogrammable control via REPROG_CONTROLS_V4 (0x1B04),
    /// function 0x10. Returns control ID (CID), native task ID, and capability flags.
    /// </summary>
    public static (ushort cid, ushort taskId, ushort flags)? GetReprogControlInfo(
        HidPPDevice device, byte index)
    {
        ushort feature = device.GetFeatureIndex(Feature.REPROG_CONTROLS_V4) >= 0
            ? Feature.REPROG_CONTROLS_V4
            : Feature.REPROG_CONTROLS_V2;

        if (device.GetFeatureIndex(feature) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(feature, 0x10, index);
        if (reply is null || reply.Length < 7)
            return null;

        ushort cid = (ushort)((reply[2] << 8) | reply[3]);
        ushort taskId = (ushort)((reply[4] << 8) | reply[5]);
        byte flags1 = reply[6];
        byte flags2 = reply.Length > 10 ? reply[10] : (byte)0;
        ushort flags = (ushort)(flags1 | (flags2 << 8));
        return (cid, taskId, flags);
    }

    /// <summary>
    /// Reads low-resolution wheel reporting mode via LOWRES_WHEEL (0x2130), function 0x00.
    /// Returns true if the wheel reports via HID++ (diverted), false for standard HID.
    /// </summary>
    public static bool? GetLowResWheelDiverted(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.LOWRES_WHEEL) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.LOWRES_WHEEL, 0x00);
        if (reply is null || reply.Length < 3)
            return null;

        return (reply[2] & 0x01) != 0;
    }

    /// <summary>
    /// Reads hi-res scrolling mode via HI_RES_SCROLLING (0x2120), function 0x00.
    /// This is the older feature (distinct from HIRES_WHEEL 0x2121).
    /// Returns (enabled, resolution multiplier).
    /// </summary>
    public static (bool enabled, int resolution)? GetHiResScrollingLegacy(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.HI_RES_SCROLLING) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.HI_RES_SCROLLING, 0x00);
        if (reply is null || reply.Length < 4)
            return null;

        byte mode = reply[2];
        byte resolution = reply[3];
        return (mode != 0, resolution);
    }

    /// <summary>
    /// Sets hi-res scrolling mode via HI_RES_SCROLLING (0x2120), function 0x10.
    /// </summary>
    public static bool SetHiResScrollingLegacy(HidPPDevice device, bool enabled)
    {
        if (device.GetFeatureIndex(Feature.HI_RES_SCROLLING) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.HI_RES_SCROLLING, 0x10,
            (byte)(enabled ? 0x01 : 0x00));
        return reply is not null;
    }

    /// <summary>
    /// Reads crown state via CROWN (0x4600), function 0x10.
    /// Payload byte 0 = divert (0x01=normal, 0x02=diverted),
    /// byte 1 = scroll mode (0x01=smooth, 0x02=ratchet).
    /// </summary>
    public static (bool smoothScroll, bool diverted)? GetCrown(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.CROWN) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.CROWN, 0x10);
        if (reply is null || reply.Length < 4)
            return null;

        bool diverted = reply[2] == 0x02;
        bool smooth = reply[3] == 0x01;
        return (smooth, diverted);
    }

    /// <summary>
    /// Sets crown state via CROWN (0x4600), function 0x20.
    /// </summary>
    public static bool SetCrown(HidPPDevice device, bool smoothScroll, bool diverted)
    {
        if (device.GetFeatureIndex(Feature.CROWN) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.CROWN, 0x20,
            (byte)(diverted ? 0x02 : 0x01),
            (byte)(smoothScroll ? 0x01 : 0x02));
        return reply is not null;
    }

    /// <summary>
    /// Reads the pointer speed via POINTER_SPEED (0x2205), function 0x00.
    /// Returns the raw 16-bit speed value. 256 = 1.0x, range 0x002E to 0x01FF.
    /// </summary>
    public static int? GetPointerSpeed(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.POINTER_SPEED) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.POINTER_SPEED, 0x00);
        if (reply is null || reply.Length < 4)
            return null;

        int speed = (reply[2] << 8) | reply[3];
        return speed;
    }

    /// <summary>
    /// Sets the pointer speed via POINTER_SPEED (0x2205), function 0x10.
    /// Value 256 = 1.0x, valid range 0x002E (46) to 0x01FF (511).
    /// </summary>
    public static bool SetPointerSpeed(HidPPDevice device, int speed)
    {
        if (device.GetFeatureIndex(Feature.POINTER_SPEED) < 0)
            return false;

        speed = Math.Clamp(speed, 0x002E, 0x01FF);
        byte[]? reply = device.FeatureRequest(Feature.POINTER_SPEED, 0x10,
            (byte)(speed >> 8),
            (byte)(speed & 0xFF));
        return reply is not null;
    }

    /// <summary>
    /// Reads host info via CHANGE_HOST (0x1814), function 0x00.
    /// Returns the number of pairing slots and the current host index (0-based).
    /// </summary>
    public static (int hostCount, int currentHost)? GetHostInfo(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.CHANGE_HOST) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.CHANGE_HOST, 0x00);
        if (reply is null || reply.Length < 4)
            return null;

        int hostCount = reply[2];
        int currentHost = reply[3];

        if (hostCount <= 0 || hostCount > 3)
            hostCount = 3;

        return (hostCount, currentHost);
    }

    /// <summary>
    /// Switches the active host via CHANGE_HOST (0x1814), function 0x10.
    /// The device disconnects after switching, so no reply is expected.
    /// </summary>
    public static void SetChangeHost(HidPPDevice device, int hostIndex)
    {
        if (device.GetFeatureIndex(Feature.CHANGE_HOST) < 0)
            return;

        device.FeatureRequest(Feature.CHANGE_HOST, 0x10, (byte)hostIndex);
    }

    // -- REPROG_CONTROLS_V4 (0x1B04) button enumeration --

    private static readonly Dictionary<ushort, string> CidNames = new()
    {
        [0x0050] = "Left Click",
        [0x0051] = "Right Click",
        [0x0052] = "Middle Click",
        [0x0053] = "Back",
        [0x0056] = "Forward",
        [0x00BD] = "Thumb Wheel",
        [0x00C3] = "Gesture Button",
        [0x00C4] = "SmartShift",
        [0x00D7] = "Top Button",
    };

    /// <summary>
    /// Enumerates all reprogrammable controls, returning CID, task ID, capability flags,
    /// and a human-readable name for each button.
    /// </summary>
    public static List<(ushort cid, ushort taskId, ushort flags, string name)> GetReprogControlsList(HidPPDevice device)
    {
        int count = GetReprogControlsCount(device);
        var result = new List<(ushort cid, ushort taskId, ushort flags, string name)>();
        for (int i = 0; i < count; i++)
        {
            var info = GetReprogControlInfo(device, (byte)i);
            if (info is null)
                continue;
            string name = CidNames.GetValueOrDefault(info.Value.cid, $"Button 0x{info.Value.cid:X4}");
            result.Add((info.Value.cid, info.Value.taskId, info.Value.flags, name));
        }
        return result;
    }

    /// <summary>
    /// Reads current CID reporting state (diversion etc.) via REPROG_CONTROLS_V4 (0x1B04), fn 0x20.
    /// Returns the low byte of mapping flags (DIVERTED = 0x01).
    /// </summary>
    public static byte? GetCidReporting(HidPPDevice device, ushort cid)
    {
        if (device.GetFeatureIndex(Feature.REPROG_CONTROLS_V4) < 0)
            return null;
        byte[]? reply = device.FeatureRequest(Feature.REPROG_CONTROLS_V4, 0x20,
            (byte)(cid >> 8), (byte)(cid & 0xFF));
        if (reply is null || reply.Length < 5)
            return null;
        return reply[4];
    }

    /// <summary>
    /// Sets the divert flag for a CID via REPROG_CONTROLS_V4 (0x1B04), fn 0x30.
    /// Packet: cid_hi, cid_lo, bfield, remap_hi, remap_lo. bfield bit0 = DIVERTED, bit1 = Xvalid.
    /// </summary>
    public static bool SetCidReporting(HidPPDevice device, ushort cid, bool diverted, bool persisted = false)
    {
        if (device.GetFeatureIndex(Feature.REPROG_CONTROLS_V4) < 0)
            return false;

        byte bfield = 0x02; // Xvalid for DIVERTED bit
        if (diverted)
            bfield |= 0x01;
        if (persisted)
            bfield |= 0x0C; // PERSISTENTLY_DIVERTED (0x04) + its Xvalid (0x08)

        byte[]? reply = device.FeatureRequest(Feature.REPROG_CONTROLS_V4, 0x30,
            (byte)(cid >> 8), (byte)(cid & 0xFF), bfield, 0x00, 0x00);
        return reply is not null;
    }

    // -- GESTURE_2 (0x6501) gesture enable/disable --

    private static readonly Dictionary<byte, string> GestureNames = new()
    {
        [1] = "Tap 1 Finger",
        [2] = "Tap 2 Fingers",
        [3] = "Tap 3 Fingers",
        [4] = "Click 1 Finger",
        [5] = "Click 2 Fingers",
        [6] = "Click 3 Fingers",
        [10] = "Double Tap 1 Finger",
        [11] = "Double Tap 2 Fingers",
        [20] = "Track 1 Finger",
        [21] = "Tracking Acceleration",
        [30] = "Tap Drag 1 Finger",
        [31] = "Tap Drag 2 Fingers",
        [33] = "Tap Gestures",
        [40] = "Scroll 1 Finger",
        [41] = "Scroll 2 Fingers",
        [42] = "Scroll 2F Horizontal",
        [43] = "Scroll 2F Vertical",
        [45] = "Natural Scrolling",
        [48] = "V-Scroll Inertia",
        [49] = "V-Scroll Ballistics",
        [50] = "Swipe 2F Horizontal",
        [51] = "Swipe 3F Horizontal",
        [52] = "Swipe 4F Horizontal",
        [80] = "Zoom 2 Fingers",
        [87] = "Rotate 2 Fingers",
    };

    /// <summary>
    /// Parses the GESTURE_2 field table and returns all toggleable gestures with
    /// their current enabled state and bitmap enable index.
    /// Protocol: fn 0x00 returns 8 field pairs per call. Gesture fields have
    /// high byte &amp; 0x80 set. Each gesture that can be enabled or is default-enabled
    /// gets a sequential enable index used for bitmap read/write via fn 0x10/0x20.
    /// </summary>
    public static List<(ushort gestureId, string name, bool enabled, int enableIndex)> GetGestureList(
        HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return [];

        var gestures = new List<(ushort gestureId, string name, bool enabled, int enableIndex)>();
        int fieldIndex = 0;
        int nextEnableIndex = 0;

        while (fieldIndex < 256)
        {
            byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x00,
                (byte)(fieldIndex >> 8), (byte)(fieldIndex & 0xFF));
            if (reply is null || reply.Length < 18)
                break;

            bool done = false;
            for (int i = 0; i < 8; i++)
            {
                byte high = reply[2 + i * 2];
                byte low = reply[2 + i * 2 + 1];

                if (high == 0x01)
                {
                    done = true;
                    break;
                }

                if ((high & 0x80) != 0)
                {
                    bool canEnable = (high & 0x01) != 0;
                    bool defaultEnabled = (high & 0x20) != 0;

                    if (canEnable || defaultEnabled)
                    {
                        int enableIndex = nextEnableIndex++;
                        if (canEnable)
                        {
                            bool enabled = defaultEnabled;
                            int offset = enableIndex >> 3;
                            byte mask = (byte)(1 << (enableIndex % 8));
                            byte[]? state = device.FeatureRequest(Feature.GESTURE_2, 0x10,
                                (byte)offset, 0x01, mask);
                            if (state is not null && state.Length >= 3)
                                enabled = (state[2] & mask) != 0;

                            string name = GestureNames.GetValueOrDefault(low, $"Gesture {low}");
                            gestures.Add((low, name, enabled, enableIndex));
                        }
                    }
                }

                fieldIndex++;
            }

            if (done)
                break;
        }

        return gestures;
    }

    /// <summary>
    /// Enables or disables a gesture by its bitmap enable index.
    /// fn 0x20: args = [offset, 0x01, mask, value].
    /// </summary>
    public static bool SetGestureEnabled(HidPPDevice device, int enableIndex, bool enabled)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return false;

        int offset = enableIndex >> 3;
        byte mask = (byte)(1 << (enableIndex % 8));
        byte value = enabled ? mask : (byte)0;

        byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x20,
            (byte)offset, 0x01, mask, value);
        return reply is not null;
    }

    /// <summary>
    /// Parses the GESTURE_2 field table for divertable gestures and returns each
    /// with its current diverted state and bitmap divert index.
    /// fn 0x30 reads diverted bitmap, fn 0x40 writes it. High byte bit 0x02 marks divertable.
    /// </summary>
    public static List<(ushort gestureId, string name, bool diverted, int divertIndex)> GetGestureDivertList(
        HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return [];

        var result = new List<(ushort, string, bool, int)>();
        int fieldIndex = 0;
        int nextDivertIndex = 0;

        while (fieldIndex < 256)
        {
            byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x00,
                (byte)(fieldIndex >> 8), (byte)(fieldIndex & 0xFF));
            if (reply is null || reply.Length < 18)
                break;

            bool done = false;
            for (int i = 0; i < 8; i++)
            {
                byte high = reply[2 + i * 2];
                byte low = reply[2 + i * 2 + 1];

                if (high == 0x01)
                {
                    done = true;
                    break;
                }

                if ((high & 0x80) != 0)
                {
                    bool canDivert = (high & 0x02) != 0;
                    if (canDivert)
                    {
                        int divertIndex = nextDivertIndex++;
                        int offset = divertIndex >> 3;
                        byte mask = (byte)(1 << (divertIndex % 8));
                        bool diverted = false;
                        byte[]? state = device.FeatureRequest(Feature.GESTURE_2, 0x30,
                            (byte)offset, 0x01, mask);
                        if (state is not null && state.Length >= 3)
                            diverted = (state[2] & mask) != 0;

                        string name = GestureNames.GetValueOrDefault(low, $"Gesture {low}");
                        result.Add((low, name, diverted, divertIndex));
                    }
                }

                fieldIndex++;
            }

            if (done)
                break;
        }

        return result;
    }

    /// <summary>
    /// Sets gesture diverted state. fn 0x40: args = [offset, 0x01, mask, value].
    /// </summary>
    public static bool SetGestureDiverted(HidPPDevice device, int divertIndex, bool diverted)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return false;

        int offset = divertIndex >> 3;
        byte mask = (byte)(1 << (divertIndex % 8));
        byte value = diverted ? mask : (byte)0;

        byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x40,
            (byte)offset, 0x01, mask, value);
        return reply is not null;
    }

    private static readonly Dictionary<byte, string> GestureParamNames = new()
    {
        [0x01] = "Scale Factor",
        [0x02] = "MixedButtons",
    };

    /// <summary>
    /// Enumerates gesture params via fn 0x00 walk (param fields have high byte 0x20-0x3F),
    /// reads current value via fn 0x70 and default/max via fn 0x60.
    /// Returns (paramIndex, name, currentValue, maxValue).
    /// </summary>
    public static List<(int index, string name, int currentValue, int maxValue)> GetGestureParams(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return [];

        var result = new List<(int, string, int, int)>();
        int fieldIndex = 0;
        int nextParamIndex = 0;

        while (fieldIndex < 256)
        {
            byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x00,
                (byte)(fieldIndex >> 8), (byte)(fieldIndex & 0xFF));
            if (reply is null || reply.Length < 18)
                break;

            bool done = false;
            for (int i = 0; i < 8; i++)
            {
                byte high = reply[2 + i * 2];
                byte low = reply[2 + i * 2 + 1];

                if (high == 0x01)
                {
                    done = true;
                    break;
                }

                // Param fields use high nibble 0x20 or 0x30
                if ((high & 0xF0) == 0x20 || (high & 0xF0) == 0x30)
                {
                    int size = high & 0x0F;
                    int paramIndex = nextParamIndex++;

                    int current = 0;
                    byte[]? cur = device.FeatureRequest(Feature.GESTURE_2, 0x70,
                        (byte)paramIndex, 0xFF);
                    if (cur is not null && cur.Length >= 2 + size)
                    {
                        for (int b = 0; b < size; b++)
                            current = (current << 8) | cur[2 + b];
                    }

                    int max = 0;
                    byte[]? def = device.FeatureRequest(Feature.GESTURE_2, 0x60,
                        (byte)paramIndex, 0xFF);
                    if (def is not null && def.Length >= 2 + size)
                    {
                        for (int b = 0; b < size; b++)
                            max = (max << 8) | def[2 + b];
                    }
                    // Default value is the "normal" value; double it as a sensible slider max.
                    if (max > 0)
                        max = max * 2;
                    else
                        max = 1024;

                    string name = GestureParamNames.GetValueOrDefault(low, $"Param {low}");
                    result.Add((paramIndex, name, current, max));
                }

                fieldIndex++;
            }

            if (done)
                break;
        }

        return result;
    }

    /// <summary>
    /// Sets a gesture param value via fn 0x80. Encoded as big-endian 2-byte int.
    /// </summary>
    public static bool SetGestureParam(HidPPDevice device, int paramIndex, int value)
    {
        if (device.GetFeatureIndex(Feature.GESTURE_2) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.GESTURE_2, 0x80,
            (byte)paramIndex, (byte)(value >> 8), (byte)(value & 0xFF), 0xFF);
        return reply is not null;
    }

    // ── HAPTIC (0x19B0) ──

    /// <summary>Reads haptic feedback state. fn 0x10: [enabled, level, levelType].</summary>
    public static (bool enabled, int level)? GetHapticLevel(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.HAPTIC) < 0)
            return null;

        byte[]? reply = device.FeatureRequest(Feature.HAPTIC, 0x10);
        if (reply is null || reply.Length < 4)
            return null;

        bool enabled = (reply[2] & 0x01) != 0;
        int level = enabled ? reply[3] : 0;
        return (enabled, level);
    }

    /// <summary>Sets haptic feedback. fn 0x20: [enabled, level].</summary>
    public static bool SetHapticLevel(HidPPDevice device, bool enabled, int level)
    {
        if (device.GetFeatureIndex(Feature.HAPTIC) < 0)
            return false;

        byte en = (byte)(enabled ? 0x01 : 0x00);
        byte lv = (byte)Math.Clamp(enabled ? level : 50, 0, 100);
        byte[]? reply = device.FeatureRequest(Feature.HAPTIC, 0x20, en, lv);
        return reply is not null;
    }

    // ── FORCE_SENSING_BUTTON (0x19C0) ──

    /// <summary>Reads force-sensing button configs. Returns list of (index, current, min, max).</summary>
    public static List<(byte index, int current, int min, int max)> GetForceSensingButtons(HidPPDevice device)
    {
        var result = new List<(byte, int, int, int)>();
        if (device.GetFeatureIndex(Feature.FORCE_SENSING_BUTTON) < 0)
            return result;

        byte[]? countReply = device.FeatureRequest(Feature.FORCE_SENSING_BUTTON, 0x00);
        if (countReply is null || countReply.Length < 3)
            return result;

        int count = countReply[2];
        for (byte i = 0; i < count && i < 8; i++)
        {
            byte[]? info = device.FeatureRequest(Feature.FORCE_SENSING_BUTTON, 0x10, i);
            if (info is null || info.Length < 8)
                continue;

            int minForce = (info[4] << 8) | info[5];
            int maxForce = (info[6] << 8) | info[7];

            byte[]? cur = device.FeatureRequest(Feature.FORCE_SENSING_BUTTON, 0x20, i);
            int current = (cur is not null && cur.Length >= 5)
                ? (cur[3] << 8) | cur[4]
                : minForce;

            result.Add((i, current, minForce, maxForce));
        }
        return result;
    }

    /// <summary>Sets force for a button. fn 0x30: [index, forceHi, forceLo].</summary>
    public static bool SetForceSensingButton(HidPPDevice device, byte index, int force)
    {
        if (device.GetFeatureIndex(Feature.FORCE_SENSING_BUTTON) < 0)
            return false;

        byte[]? reply = device.FeatureRequest(Feature.FORCE_SENSING_BUTTON, 0x30,
            index, (byte)(force >> 8), (byte)(force & 0xFF));
        return reply is not null;
    }

    // ── ANALOG_BUTTONS (0x1B0C) ──

    /// <summary>Reads analog button capabilities and per-button config.</summary>
    public static (int buttonCount, int maxActuation, int maxRapidTrigger, int maxHaptics,
        List<(byte index, int actuation, int rapidTrigger, int haptics)> buttons)?
        GetAnalogButtons(HidPPDevice device)
    {
        if (device.GetFeatureIndex(Feature.ANALOG_BUTTONS) < 0)
            return null;

        byte[]? caps = device.FeatureRequest(Feature.ANALOG_BUTTONS, 0x00);
        if (caps is null || caps.Length < 7)
            return null;

        int buttonCount = Math.Min((int)caps[3], 2); // only L/R user-accessible
        int maxAct = caps[4] > 0 ? caps[4] >> 2 : 10;
        int maxRt = caps[5] > 0 ? caps[5] >> 2 : 5;
        int maxHap = caps[6] > 0 ? caps[6] >> 2 : 5;

        var buttons = new List<(byte, int, int, int)>();
        for (byte i = 0; i < buttonCount; i++)
        {
            byte[]? cfg = device.FeatureRequest(Feature.ANALOG_BUTTONS, 0x20, i);
            if (cfg is null || cfg.Length < 6)
                continue;
            buttons.Add((i, cfg[3] >> 2, cfg[4] >> 2, cfg[5] >> 2));
        }

        return (buttonCount, maxAct, maxRt, maxHap, buttons);
    }

    /// <summary>Sets analog button config. Preserves sensitivityFlag in rapidTrigger byte.</summary>
    public static bool SetAnalogButton(HidPPDevice device, byte index,
        int actuation, int rapidTrigger, int haptics)
    {
        if (device.GetFeatureIndex(Feature.ANALOG_BUTTONS) < 0)
            return false;

        // Read current to preserve sensitivityFlag (byte 4 bit 0).
        byte[]? cur = device.FeatureRequest(Feature.ANALOG_BUTTONS, 0x20, index);
        byte sensFlag = (cur is not null && cur.Length >= 5) ? (byte)(cur[4] & 0x01) : (byte)0;

        byte wireAct = (byte)((actuation & 0x3F) << 2);
        byte wireRt = (byte)(((rapidTrigger & 0x3F) << 2) | sensFlag);
        byte wireHap = (byte)((haptics & 0x3F) << 2);

        byte[]? reply = device.FeatureRequest(Feature.ANALOG_BUTTONS, 0x10,
            index, wireAct, wireRt, wireHap);
        return reply is not null;
    }
}
