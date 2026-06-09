using System.Text;
using Avalonia.Media;
using GHelper.Linux.AnimeMatrix.Communication.Platform;
using GHelper.Linux.Helpers;
using HidSharp;
using AsusDevice = GHelper.Linux.AnimeMatrix.Communication.Device;

namespace GHelper.Linux.Peripherals.Mouse;

public enum PowerOffSetting : byte
{
    Minutes1 = 0,
    Minutes2 = 1,
    Minutes3 = 2,
    Minutes5 = 3,
    Minutes10 = 4,
    Never = 0xFF,
}

public enum DebounceTime : byte
{
    OFF = 0,
    MS8 = 1,
    MS12 = 2,
    MS16 = 3,
    MS20 = 4,
    MS24 = 5,
    MS28 = 6,
    MS32 = 7,
}

public enum PollingRate : byte
{
    PR125Hz = 0,
    PR250Hz = 1,
    PR500Hz = 2,
    PR1000Hz = 3,
    PR2000Hz = 4,
    PR4000Hz = 5,
    PR8000Hz = 6,
    PR16000Hz = 7,
}

public enum LiftOffDistance : byte
{
    Low = 0,
    High = 1,
}

public enum AnimationDirection : byte
{
    Clockwise = 0,
    CounterClockwise = 1,
}

public enum AnimationSpeed : byte
{
    Slow = 9,
    Medium = 7,
    Fast = 5,
}

public enum LightingMode : byte
{
    Off = 0xF0,
    Static = 0,
    Breathing = 1,
    ColorCycle = 2,
    Rainbow = 3,
    React = 4,
    Comet = 5,
    BatteryState = 6,
}

public enum LightingZone : byte
{
    Logo = 0,
    Scrollwheel = 1,
    Underglow = 2,
    All = 3,
    Dock = 4,
}

public class LightingSetting
{
    public LightingMode Mode { get; set; } = LightingMode.Static;
    public byte Brightness { get; set; } = 100;
    public byte R { get; set; } = 255;
    public byte G { get; set; }
    public byte B { get; set; }
    public bool RandomColor { get; set; }
    public AnimationSpeed Speed { get; set; } = AnimationSpeed.Medium;
    public AnimationDirection Direction { get; set; } = AnimationDirection.Clockwise;

    /// <summary>Serialise to a fixed 9-byte wire format.</summary>
    public byte[] Export()
    {
        return new byte[]
        {
            (byte)Mode,
            Brightness,
            R, G, B,
            (byte)(RandomColor ? 1 : 0),
            (byte)Speed,
            (byte)Direction,
            0, // reserved
        };
    }

    /// <summary>Deserialise from wire bytes (9 bytes expected).</summary>
    public void Import(byte[] data)
    {
        if (data.Length < 8)
            return;
        Mode = (LightingMode)data[0];
        Brightness = data[1];
        R = data[2];
        G = data[3];
        B = data[4];
        RandomColor = data[5] != 0;
        Speed = (AnimationSpeed)data[6];
        Direction = (AnimationDirection)data[7];
    }
}

public class AsusMouseDPI
{
    public uint DPI { get; set; } = 800;
    public Color Color { get; set; } = Colors.White;

    /// <summary>Serialise to a fixed 5-byte wire format (2 DPI + 3 RGB).</summary>
    public byte[] Export()
    {
        return new byte[]
        {
            (byte)(DPI >> 8),
            (byte)(DPI & 0xFF),
            Color.R, Color.G, Color.B,
        };
    }

    /// <summary>Deserialise from wire bytes (5 bytes expected).</summary>
    public void Import(byte[] data)
    {
        if (data.Length < 5)
            return;
        DPI = (uint)((data[0] << 8) | data[1]);
        Color = Color.FromRgb(data[2], data[3], data[4]);
    }
}

/// <summary>Base class for ASUS mouse peripherals. Uses HID transport from Device and implements IPeripheral.</summary>
public abstract class AsusMouse : AsusDevice, IPeripheral
{
    // Export header
    private static readonly byte[] ExportMagic = "GMP1"u8.ToArray();

    private readonly string _path;
    private readonly bool _wireless;
    private readonly byte _reportId;

    public bool IsDeviceReady { get; protected set; }
    public bool Wireless => _wireless;
    public int Battery { get; protected set; } = -1;
    public bool Charging { get; protected set; }

    private LightingSetting[]? _lightingSettings;
    public LightingSetting[] LightingSettings
    {
        get
        {
            if (_lightingSettings is null)
            {
                var zones = SupportedLightingZones();
                _lightingSettings = new LightingSetting[zones.Length];
                for (int i = 0; i < zones.Length; i++)
                    _lightingSettings[i] = new LightingSetting();
            }
            return _lightingSettings;
        }
        set => _lightingSettings = value;
    }

    private AsusMouseDPI[]? _dpiSettings;
    public AsusMouseDPI[] DpiSettings
    {
        get
        {
            if (_dpiSettings is null)
            {
                _dpiSettings = new AsusMouseDPI[DPIProfileCount()];
                for (int i = 0; i < _dpiSettings.Length; i++)
                    _dpiSettings[i] = new AsusMouseDPI { DPI = 800 };
            }
            return _dpiSettings;
        }
        set => _dpiSettings = value;
    }

    public int DpiProfile { get; set; }
    public int CurrentDPIProfileCount { get; set; } = 1;

    public PollingRate PollingRate { get; set; } = PollingRate.PR1000Hz;
    public bool AngleSnapping { get; set; }
    public int AngleAdjustmentDegrees { get; set; }
    public DebounceTime Debounce { get; set; } = DebounceTime.MS12;
    public bool Acceleration { get; set; }
    public bool Deceleration { get; set; }
    public bool MotionSync { get; set; }
    public bool ZoneMode { get; set; }
    public ushort[] ButtonBindings { get; set; } = new ushort[16];
    public bool Booster { get; set; }

    public PowerOffSetting PowerOff { get; set; } = PowerOffSetting.Minutes3;
    public LiftOffDistance LiftOff { get; set; } = LiftOffDistance.Low;
    public byte LowBatteryWarning { get; set; } = 25;

    /// <param name="vendorId">USB vendor ID (typically 0x0B05 for ASUS).</param>
    /// <param name="productId">USB product ID for this mouse model.</param>
    /// <param name="path">HidSharp device path substring for matching.</param>
    /// <param name="wireless">True if this is the wireless receiver entry.</param>
    /// <param name="reportId">HID report ID used for mouse protocol (0x00 = default output report).</param>
    protected AsusMouse(ushort vendorId, ushort productId, string path, bool wireless, byte reportId = 0x00)
        : base(vendorId, productId)
    {
        _path = path;
        _wireless = wireless;
        _reportId = reportId;
    }

    public abstract string GetDisplayName();
    public abstract int ProfileCount();
    public abstract int DPIProfileCount();
    public abstract PollingRate[] SupportedPollingrates();

    public virtual bool HasBattery() => Wireless;
    public virtual bool HasXYDPI() => false;
    public virtual bool HasAngleSnapping() => true;
    public virtual bool HasAngleTuning() => false;
    public virtual bool HasDPIColors() => false;
    public virtual bool HasAutoPowerOff() => Wireless;
    public virtual bool HasLowBatteryWarning() => false;
    public virtual bool HasDebounce() => true;
    public virtual bool HasAcceleration() => false;
    public virtual bool HasMotionSync() => false;
    public virtual bool HasZoneMode() => false;

    public virtual LightingMode[] SupportedLightingModes() =>
        [LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle, LightingMode.Rainbow, LightingMode.React];

    public virtual LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel, LightingZone.Underglow];

    public virtual uint MaxDPI() => 36000;
    public virtual uint MinDPI() => 100;
    public virtual uint DPIIncrement() => 50;
    public virtual int MaxBrightness() => 100;
    public virtual int AngleTuningStep() => 1;
    public virtual int USBPacketSize() => 65;
    public virtual bool CanChangeDPICount() => false;

    /// <summary>Creates a LinuxUsbProvider for the vendor config interface.</summary>
    public override void SetProvider()
    {
        if (_usbProvider is not null)
            return;

        var device = FindDevice();
        if (device is null)
            throw new IOException($"AsusMouse({GetDisplayName()}): no HID interface found");

        _usbProvider = new LinuxUsbProvider(_vendorId, _productId, device.DevicePath);
    }

    /// <summary>Checks whether a matching HID device is present on the system.</summary>
    public bool IsDeviceConnected()
    {
        return FindDevice() is not null;
    }

    /// <summary>
    /// Finds the HID interface used for the mouse protocol. The Windows path hint
    /// (mi_xx) never matches Linux hidraw paths, so fall back to picking the
    /// interface with full-size output reports (the vendor config interface).
    /// </summary>
    private HidDevice? FindDevice()
    {
        try
        {
            foreach (var d in DeviceList.Local.GetHidDevices(_vendorId, _productId))
            {
                try
                {
                    if (d.DevicePath.Contains(_path, StringComparison.OrdinalIgnoreCase)
                        || d.GetMaxOutputReportLength() >= USBPacketSize() - 1)
                        return d;
                }
                catch
                {
                    // Unreadable descriptor, skip
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): device scan failed: {ex.Message}");
        }
        return null;
    }

    public void Connect()
    {
        SetProvider();
    }

    public void Disconnect()
    {
        Dispose();
    }

    /// <summary>Sends a packet and waits for a matching response via interrupt read/write.</summary>
    /// <param name="packet">Raw packet bytes, resized to USBPacketSize.</param>
    /// <param name="matchLength">Number of leading bytes that must match.</param>
    /// <returns>Response bytes, or null on timeout/mismatch.</returns>
    protected byte[]? WriteForResponse(byte[] packet, int matchLength = 3)
    {
        try
        {
            // Resize to device packet size
            int size = USBPacketSize();
            byte[] outBuf = new byte[size];
            Array.Copy(packet, outBuf, Math.Min(packet.Length, size));

            // Prepend report ID if needed
            if (_reportId != 0 && outBuf[0] == 0)
                outBuf[0] = _reportId;

            Write(outBuf);

            // Read response with retries
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] response = new byte[size];
                try
                {
                    Read(response);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                    continue;
                }

                // Check if the leading bytes match
                bool match = true;
                for (int i = 0; i < matchLength && i < packet.Length && i < response.Length; i++)
                {
                    if (packet[i] != response[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return response;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): WriteForResponse failed - {ex.Message}");
        }

        return null;
    }

    /// <summary>Reads all settings from the device. Sets IsDeviceReady on success.</summary>
    public void SynchronizeDevice()
    {
        try
        {
            ReadBattery();
            ReadProfile();
            ReadDPI();
            ReadPollingRate();
            ReadLiftOffDistance();
            ReadDebounce();
            ReadAcceleration();
            ReadLightingSetting();
            ReadMotionSync();
            ReadZoneMode();

            IsDeviceReady = true;
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): synchronised OK");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): SynchronizeDevice failed - {ex.Message}");
        }
    }

    public virtual void ReadBattery()
    {
        if (!HasBattery())
            return;
        // TODO: send [0x00, 0x12, 0x07], parse response[4] = %, [5] = charging
    }

    protected virtual void ReadProfile()
    {
        // TODO: send [0x00, 0x12, 0x01], parse active DPI profile
    }

    protected virtual void ReadDPI()
    {
        // TODO: send [0x00, 0x12, 0x04, profile] per profile
    }

    protected virtual void ReadPollingRate()
    {
        // TODO: send [0x00, 0x12, 0x02]
    }

    protected virtual void ReadLiftOffDistance()
    {
        // TODO: send [0x00, 0x12, 0x05]
    }

    protected virtual void ReadDebounce()
    {
        if (!HasDebounce())
            return;
        // TODO: send [0x00, 0x12, 0x06]
    }

    protected virtual void ReadAcceleration()
    {
        if (!HasAcceleration())
            return;
        // Stub
    }

    protected virtual void ReadLightingSetting()
    {
        // TODO: send [0x00, 0x12, 0x03, zone] per zone
    }

    protected virtual void ReadMotionSync()
    {
        if (!HasMotionSync())
            return;
        // Stub
    }

    protected virtual void ReadZoneMode()
    {
        if (!HasZoneMode())
            return;
        // Stub
    }

    public virtual void WriteDPI()
    {
        // TODO: send [0x00, 0x51, 0x31, profile, dpiHigh, dpiLow, ...]
    }

    public virtual void WritePollingRate()
    {
        // TODO: send [0x00, 0x51, 0x32, (byte)PollingRate]
    }

    public virtual void WriteLiftOffDistance()
    {
        // TODO: send [0x00, 0x51, 0x35, (byte)LiftOff]
    }

    public virtual void WriteDebounce()
    {
        if (!HasDebounce())
            return;
        // Stub
    }

    public virtual void WriteAcceleration()
    {
        if (!HasAcceleration())
            return;
        // Stub
    }

    public virtual void WriteLightingSetting()
    {
        // TODO: send [0x00, 0x51, 0x28, zone, mode, brightness, R, G, B, speed, direction]
    }

    public virtual void WriteMotionSync()
    {
        if (!HasMotionSync())
            return;
        // Stub
    }

    public virtual void WriteAngleSnapping()
    {
        if (!HasAngleSnapping())
            return;
        // Stub
    }

    public virtual void WritePowerOff()
    {
        if (!HasAutoPowerOff())
            return;
        // Stub
    }

    public virtual void WriteLowBatteryWarning()
    {
        if (!HasLowBatteryWarning())
            return;
        // Stub
    }

    /// <summary>Streams a single colour to the mouse LEDs for Aura Sync.</summary>
    public virtual void WriteColorDirect(Color color)
    {
        // TODO: send [0x00, 0x51, 0x28, 0x00, 0x00, 0x04, R, G, B] (direct mode)
    }

    /// <summary>Syncs lighting from the keyboard Aura profile when Aura Sync is enabled.</summary>
    public virtual void SyncFromKeyboardAura()
    {
        // Stub
    }

    public PeripheralType DeviceType() => PeripheralType.Mouse;

    public bool CanExport() => true;

    /// <summary>Serialises all mouse settings into a portable byte blob (GMP1 format).</summary>
    public byte[] Export()
    {
        try
        {
            using var ms = new MemoryStream();
            ms.Write(ExportMagic);        // 4 bytes magic
            ms.WriteByte(0x01);           // version

            // DPI profiles
            ms.WriteByte((byte)DpiSettings.Length);
            ms.WriteByte((byte)DpiProfile);
            foreach (var dpi in DpiSettings)
                ms.Write(dpi.Export());

            // Polling rate
            ms.WriteByte((byte)PollingRate);

            // Lift-off distance
            ms.WriteByte((byte)LiftOff);

            // Debounce
            ms.WriteByte((byte)Debounce);

            // Angle snapping + angle adjustment
            ms.WriteByte((byte)(AngleSnapping ? 1 : 0));
            ms.WriteByte((byte)AngleAdjustmentDegrees);

            // Acceleration / deceleration
            ms.WriteByte((byte)(Acceleration ? 1 : 0));
            ms.WriteByte((byte)(Deceleration ? 1 : 0));

            // Motion sync
            ms.WriteByte((byte)(MotionSync ? 1 : 0));

            // Lighting
            ms.WriteByte((byte)LightingSettings.Length);
            foreach (var ls in LightingSettings)
                ms.Write(ls.Export());

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): Export failed - {ex.Message}");
            return [];
        }
    }

    /// <summary>Restores settings from a GMP1 blob created by Export.</summary>
    public bool Import(byte[] blob)
    {
        try
        {
            if (blob.Length < 5)
                return false;

            // Check magic
            for (int i = 0; i < ExportMagic.Length; i++)
            {
                if (blob[i] != ExportMagic[i])
                    return false;
            }

            int pos = 4;
            byte version = blob[pos++];
            if (version != 0x01)
                return false;

            // DPI profiles
            int dpiCount = blob[pos++];
            DpiProfile = blob[pos++];
            var dpis = new AsusMouseDPI[dpiCount];
            for (int i = 0; i < dpiCount; i++)
            {
                dpis[i] = new AsusMouseDPI();
                dpis[i].Import(blob[pos..(pos + 5)]);
                pos += 5;
            }
            DpiSettings = dpis;

            // Polling rate
            PollingRate = (PollingRate)blob[pos++];

            // Lift-off distance
            LiftOff = (LiftOffDistance)blob[pos++];

            // Debounce
            Debounce = (DebounceTime)blob[pos++];

            // Angle snapping + angle adjustment
            AngleSnapping = blob[pos++] != 0;
            AngleAdjustmentDegrees = blob[pos++];

            // Acceleration / deceleration
            Acceleration = blob[pos++] != 0;
            Deceleration = blob[pos++] != 0;

            // Motion sync
            MotionSync = blob[pos++] != 0;

            // Lighting
            int lightCount = blob[pos++];
            var lights = new LightingSetting[lightCount];
            for (int i = 0; i < lightCount; i++)
            {
                lights[i] = new LightingSetting();
                lights[i].Import(blob[pos..(pos + 9)]);
                pos += 9;
            }
            LightingSettings = lights;

            Logger.WriteLine($"AsusMouse({GetDisplayName()}): imported settings OK");
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AsusMouse({GetDisplayName()}): Import failed - {ex.Message}");
            return false;
        }
    }
}
