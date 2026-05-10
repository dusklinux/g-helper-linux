using GHelper.Linux.Helpers;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Gpu;

/// <summary>
/// Best-effort detection of the AMD discrete GPU sitting at the other end
/// of an XG Mobile dock. The dock only exposes its dGPU on the PCIe bus
/// once <c>egpu_enable=1</c>; before that, this returns null on every read.
///
/// <para>
/// The motivating use case is the Radeon RX 6850M XT XG Mobile dock that
/// ships with the Flow X13 GV301RA / GV302XA. That dock's firmware needs
/// the special <c>0x101</c> ACPI control parameter on enable instead of
/// the regular <c>1</c> - mirrors Windows g-helper's
/// <c>HardwareControl.cs:582</c> "xgm_special" auto-detection. We persist
/// the flag in AppConfig so the second-and-subsequent enable cycles take
/// the special path even though the dGPU isn't visible at the moment of
/// the click.
/// </para>
/// </summary>
public static class LinuxAmdDgpuDetect
{
    private const int VendorAMD = 0x1002;
    private const string DrmRoot = "/sys/class/drm";

    // Navi 22 (RX 6850M XT) PCI device ID. The dock uses this exact silicon
    // - confirmed by lspci output from XG Mobile users on r/FlowX13.
    private static readonly HashSet<int> RX6850MDeviceIds = new()
    {
        0x73DF,  // RX 6700/6800/6850M XT (Navi 22 mobile)
    };

    /// <summary>
    /// Returns true iff a Radeon RX 6850M XT is currently visible on the
    /// PCI bus. Walks <c>/sys/class/drm/card*</c>; cheap (a handful of
    /// sysfs reads) and safe to call from a background thread.
    /// </summary>
    public static bool IsRX6850M()
    {
        try
        {
            if (!Directory.Exists(DrmRoot))
                return false;

            foreach (var dir in Directory.EnumerateDirectories(DrmRoot, "card*"))
            {
                var devicePath = Path.Combine(dir, "device");
                if (!Directory.Exists(devicePath))
                    continue;

                var vendorRaw = SysfsHelper.ReadAttribute(Path.Combine(devicePath, "vendor"));
                if (vendorRaw == null)
                    continue;

                if (!int.TryParse(vendorRaw.Trim().Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int vendor) || vendor != VendorAMD)
                    continue;

                var deviceRaw = SysfsHelper.ReadAttribute(Path.Combine(devicePath, "device"));
                if (deviceRaw == null)
                    continue;

                if (!int.TryParse(deviceRaw.Trim().Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int device))
                    continue;

                if (RX6850MDeviceIds.Contains(device))
                {
                    Logger.WriteLine($"LinuxAmdDgpuDetect: found RX 6850M XT at {devicePath} (PCI {vendor:X4}:{device:X4})");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"LinuxAmdDgpuDetect.IsRX6850M failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Probe the PCI bus and persist the <c>xgm_special</c> flag if a
    /// Radeon RX 6850M XT is now visible. Once set the flag stays set; we
    /// never automatically clear it because the user may toggle the dock
    /// off and we still want to remember the silicon for the next enable.
    /// </summary>
    public static void RefreshXgmSpecialFlag()
    {
        if (AppConfig.Is("xgm_special"))
            return;

        if (IsRX6850M())
        {
            AppConfig.Set("xgm_special", 1);
            Logger.WriteLine("LinuxAmdDgpuDetect: xgm_special flag set - subsequent enables will use ACPI 0x101");
        }
    }
}
