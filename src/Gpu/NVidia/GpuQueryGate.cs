using System;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.Gpu.NVidia;

/// <summary>
/// Proactive pause gate for the app's own GPU telemetry. An nvidia-smi spawned
/// while the driver is being unbound/unloaded opens /dev/nvidia* and becomes a
/// holder itself; killed on its timeout mid-ioctl it can wedge in D-state and
/// block rmmod forever. GPU mode switches pause BEFORE touching the driver and
/// resume once the dGPU is back (or let the window expire after Eco).
/// </summary>
public static class GpuQueryGate
{
    private static DateTime _pausedUntilUtc = DateTime.MinValue;

    public static bool IsPaused => DateTime.UtcNow < _pausedUntilUtc;

    public static void Pause(TimeSpan duration, string reason)
    {
        _pausedUntilUtc = DateTime.UtcNow + duration;
        Logger.WriteLine($"GpuQueryGate: GPU queries paused for {duration.TotalSeconds:0}s ({reason})");
    }

    public static void Resume()
    {
        _pausedUntilUtc = DateTime.MinValue;
        Logger.WriteLine("GpuQueryGate: GPU queries resumed");
    }
}
