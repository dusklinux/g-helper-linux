using GHelper.Linux.Helpers;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Gpu.AMD;

/// <summary>
/// AMD iGPU runtime metrics via sysfs (DRM + hwmon).
/// Linux equivalent of Windows' AmdGpuControl GetiGpuUse() / GetGpuPower(),
/// minus the FPS metrics (no kernel API for those - use MangoHud externally).
///
/// Auto-discovers the integrated AMD GPU on first call and caches the paths.
/// Returns null on every read for non-AMD systems or when sysfs nodes are
/// missing - callers must handle null gracefully.
///
/// Used by:
///   - AllyControl auto-mode switching (gpu_busy_percent &gt; threshold ⇒ Gamepad)
///   - Tray system monitor (busy% / power readouts on AMD iGPU laptops)
///
/// Hardware coverage: ROG Ally (Z1/Z1E APU), G14 AMD, GA403, GZ302, all-AMD Z13.
/// </summary>
public static class LinuxAmdGpuMetrics
{
    private const int VendorAMD = 0x1002;
    private const string DrmRoot = "/sys/class/drm";
    private const string HwmonRoot = "/sys/class/hwmon";

    private static bool _probed;
    private static string? _drmDevicePath;     // e.g. /sys/class/drm/card1/device
    private static string? _hwmonPath;         // e.g. /sys/class/hwmon/hwmon7

    /// <summary>
    /// One-time auto-discovery: walk /sys/class/drm/card* and pick the first
    /// AMD (vendor 0x1002) integrated card. Prefers "boot_vga=1" if multiple
    /// AMD cards exist; falls back to "first one with gpu_busy_percent" so
    /// it also works on Ally where no boot_vga is set.
    /// Cached for the lifetime of the process.
    /// </summary>
    private static void Probe()
    {
        if (_probed)
            return;
        _probed = true;

        try
        {
            if (!Directory.Exists(DrmRoot))
                return;

            string? bestPath = null;
            int bestScore = -1;

            foreach (var dir in Directory.EnumerateDirectories(DrmRoot, "card*"))
            {
                var devicePath = Path.Combine(dir, "device");
                if (!Directory.Exists(devicePath))
                    continue;

                var vendorRaw = SysfsHelper.ReadAttribute(Path.Combine(devicePath, "vendor"));
                if (vendorRaw == null)
                    continue;

                // /sys vendor format: "0x1002" with newline - strip + parse hex.
                if (!int.TryParse(vendorRaw.Trim().Replace("0x", ""),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int vendor))
                    continue;

                if (vendor != VendorAMD)
                    continue;

                int score = 0;

                // Prefer integrated/boot card.
                var bootVga = SysfsHelper.ReadAttribute(Path.Combine(devicePath, "boot_vga"));
                if (bootVga != null && bootVga.Trim() == "1")
                    score += 10;

                // Must support gpu_busy_percent (AMDGPU only - radeon doesn't).
                if (File.Exists(Path.Combine(devicePath, "gpu_busy_percent")))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = devicePath;
                }
            }

            if (bestPath == null)
            {
                Logger.WriteLine("LinuxAmdGpuMetrics: no AMD iGPU found in /sys/class/drm");
                return;
            }

            _drmDevicePath = bestPath;

            // Find the matching hwmon node by walking <device>/hwmon/hwmonN.
            var hwmonDir = Path.Combine(bestPath, "hwmon");
            if (Directory.Exists(hwmonDir))
            {
                var first = Directory.EnumerateDirectories(hwmonDir, "hwmon*").FirstOrDefault();
                if (first != null)
                    _hwmonPath = first;
            }

            // Fallback: scan top-level hwmon and match by name=amdgpu.
            if (_hwmonPath == null && Directory.Exists(HwmonRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(HwmonRoot, "hwmon*"))
                {
                    var name = SysfsHelper.ReadAttribute(Path.Combine(dir, "name"));
                    if (name != null && name.Trim() == "amdgpu")
                    { _hwmonPath = dir; break; }
                }
            }

            Logger.WriteLine($"LinuxAmdGpuMetrics: drm={_drmDevicePath} hwmon={_hwmonPath ?? "(none)"}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"LinuxAmdGpuMetrics.Probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether an AMD iGPU was discovered. Used by tray code to decide whether
    /// to display AMD metrics rows.
    /// </summary>
    public static bool IsAvailable
    {
        get { Probe(); return _drmDevicePath != null; }
    }

    /// <summary>
    /// Read /sys/class/drm/cardN/device/gpu_busy_percent. Range 0..100.
    /// Returns null if iGPU not found or sysfs read failed (kernel may not
    /// expose this on some AMDGPU revisions).
    /// </summary>
    public static int? GetIgpuBusyPercent()
    {
        Probe();
        if (_drmDevicePath == null)
            return null;

        var raw = SysfsHelper.ReadAttribute(Path.Combine(_drmDevicePath, "gpu_busy_percent"));
        if (raw == null || !int.TryParse(raw.Trim(), out int v))
            return null;

        // Kernel sometimes reports >100 during transient sampling glitches; clamp.
        if (v < 0)
            v = 0;
        if (v > 100)
            v = 100;
        return v;
    }

    /// <summary>
    /// Read /sys/class/hwmon/hwmonN/power1_average and convert μW → W.
    /// Returns null if no hwmon node, attribute missing (older AMDGPU) or
    /// returns ENODATA.
    /// </summary>
    public static float? GetIgpuPowerWatts()
    {
        Probe();
        if (_hwmonPath == null)
            return null;

        var raw = SysfsHelper.ReadAttribute(Path.Combine(_hwmonPath, "power1_average"));
        if (raw == null || !long.TryParse(raw.Trim(), out long uw))
            return null;

        return uw / 1_000_000f;
    }

    /// <summary>
    /// Read iGPU edge temperature in °C. Returns null if hwmon missing.
    /// Uses temp1_input (m°C in /sys/class/hwmon).
    /// </summary>
    public static int? GetIgpuTempCelsius()
    {
        Probe();
        if (_hwmonPath == null)
            return null;

        var raw = SysfsHelper.ReadAttribute(Path.Combine(_hwmonPath, "temp1_input"));
        if (raw == null || !int.TryParse(raw.Trim(), out int mc))
            return null;

        return mc / 1000;
    }
}
