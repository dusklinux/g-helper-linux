using System.ComponentModel;
using GHelper.Linux.Helpers;
using HidSharp;

namespace GHelper.Linux.AnimeMatrix.Communication.Platform;

/// <summary>
/// Linux HID transport for AnimeMatrix / Slash devices using HidSharpCore.
/// HidSharpCore wraps hidraw ioctls, so no platform-specific P/Invoke is needed.
/// </summary>
public class LinuxUsbProvider : UsbProvider
{
    private HidStream? _stream;

    /// <summary>Opens the first HID device matching vendor/product IDs with sufficient feature report length.</summary>
    public LinuxUsbProvider(ushort vendorId, ushort productId, int maxFeatureReportLength, string name = "Matrix")
        : base(vendorId, productId)
    {
        var devices = DeviceList.Local.GetHidDevices(vendorId, productId);

        HidDevice? target = null;
        foreach (var device in devices)
        {
            try
            {
                if (device.GetMaxFeatureReportLength() >= maxFeatureReportLength)
                {
                    target = device;
                    break;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"LinuxUsbProvider({name}): skipping device - {ex.Message}");
            }
        }

        if (target is null)
            throw new IOException(
                $"LinuxUsbProvider({name}): no HID device found for " +
                $"VID=0x{vendorId:X4} PID=0x{productId:X4} " +
                $"with feature report >= {maxFeatureReportLength}");

        Logger.WriteLine(
            $"LinuxUsbProvider({name}): opening {target.DevicePath} " +
            $"(VID=0x{vendorId:X4} PID=0x{productId:X4}, " +
            $"maxFeature={target.GetMaxFeatureReportLength()})");

        var config = new OpenConfiguration();
        config.SetOption(OpenOption.Interruptible, true);
        config.SetOption(OpenOption.Exclusive, false);
        config.SetOption(OpenOption.Priority, 10);

        _stream = target.Open(config);
    }

    /// <summary>Opens a HID device matching vendor/product IDs and device path. Used for Slash.</summary>
    public LinuxUsbProvider(ushort vendorId, ushort productId, string path, int timeout = 500)
        : base(vendorId, productId)
    {
        var devices = DeviceList.Local.GetHidDevices(vendorId, productId);

        HidDevice? target = null;
        foreach (var device in devices)
        {
            if (device.DevicePath.Contains(path, StringComparison.OrdinalIgnoreCase))
            {
                target = device;
                break;
            }
        }

        if (target is null)
            throw new IOException(
                $"LinuxUsbProvider(Slash): no HID device found for " +
                $"VID=0x{vendorId:X4} PID=0x{productId:X4} path containing '{path}'");

        Logger.WriteLine(
            $"LinuxUsbProvider(Slash): opening {target.DevicePath} " +
            $"(VID=0x{vendorId:X4} PID=0x{productId:X4})");

        var config = new OpenConfiguration();
        config.SetOption(OpenOption.Interruptible, true);
        config.SetOption(OpenOption.Exclusive, false);
        config.SetOption(OpenOption.Priority, 10);

        _stream = target.Open(config);
        _stream.ReadTimeout = timeout;
        _stream.WriteTimeout = timeout;
    }

    /// <inheritdoc />
    public override void Set(byte[] data)
    {
        try
        {
            _stream?.SetFeature(data);
            _stream?.Flush();
        }
        catch (IOException ex)
        {
            WrapException(ex);
        }
    }

    /// <inheritdoc />
    public override byte[] Get(byte[] data)
    {
        try
        {
            var result = new byte[data.Length];
            Array.Copy(data, result, data.Length);
            _stream?.GetFeature(result);
            _stream?.Flush();
            return result;
        }
        catch (IOException ex)
        {
            WrapException(ex);
            return [];
        }
    }

    /// <inheritdoc />
    public override void Read(byte[] data)
    {
        try
        {
            _stream?.Read(data);
        }
        catch (IOException ex)
        {
            WrapException(ex);
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] data)
    {
        try
        {
            _stream?.Write(data);
            _stream?.Flush();
        }
        catch (IOException ex)
        {
            WrapException(ex);
        }
    }

    /// <summary>Suppresses benign IO exceptions (NativeErrorCode == 0), re-throws genuine errors.</summary>
    private static void WrapException(IOException ex)
    {
        if (ex.InnerException is Win32Exception w32 && w32.NativeErrorCode == 0)
            return;

        Logger.WriteLine($"LinuxUsbProvider IO error: {ex.Message}");
        throw ex;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
