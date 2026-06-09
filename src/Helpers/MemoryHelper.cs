using System.Runtime.InteropServices;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Reduces resident memory after expensive operations
/// using malloc_trim(3) and GC to release pages back to the OS.
/// </summary>
public static class MemoryHelper
{
    // glibc: releases free'd pages back to the OS
    [DllImport("libc", EntryPoint = "malloc_trim", SetLastError = false)]
    private static extern int MallocTrim(nint pad);

    /// <summary>Trim memory on a background thread, optionally waiting for a prerequisite task.</summary>
    public static void TrimAfter(Task? prerequisite = null, TimeSpan? timeout = null)
    {
        Task.Run(async () =>
        {
            if (prerequisite != null)
            {
                try
                {
                    await prerequisite.WaitAsync(timeout ?? TimeSpan.FromSeconds(3));
                }
                catch { }
            }

            Trim();
        });
    }

    private static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

        try
        { MallocTrim(0); }
        catch { /* musl libc doesn't export malloc_trim */ }
    }
}
