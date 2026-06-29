namespace GHelper.Linux.Platform.Linux;

// COSMIC DE detection and helpers. Power profile D-Bus fallback
// (tuned-ppd), session env import for early-start systemd units.
public static class Cosmic
{
    private static bool? _isCosmic;

    // True when COSMIC DE is active. Falls back to process check
    // when XDG_CURRENT_DESKTOP is unset (early-start race).
    public static bool IsCosmic => _isCosmic ??= DetectCosmic();

    private static bool DetectCosmic()
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        if (!string.IsNullOrEmpty(desktop) &&
            desktop.Contains("COSMIC", StringComparison.OrdinalIgnoreCase))
            return true;

        // cosmic-comp is the compositor; if running, we're on COSMIC.
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("cosmic-comp");
            bool found = procs.Length > 0;
            foreach (var p in procs)
                p.Dispose();
            return found;
        }
        catch { return false; }
    }

    // Set power profile via net.hadess.PowerProfiles D-Bus.
    // Works with both power-profiles-daemon and tuned-ppd.
    public static bool SetPowerProfile(string profile)
    {
        try
        {
            var result = SysfsHelper.RunCommand("busctl", $"set-property net.hadess.PowerProfiles " +
                $"/net/hadess/PowerProfiles net.hadess.PowerProfiles ActiveProfile s {profile}");
            return result != null;
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Cosmic: D-Bus SetPowerProfile failed: {ex.Message}");
            return false;
        }
    }

    // Get power profile via D-Bus.
    public static string? GetPowerProfile()
    {
        try
        {
            var result = SysfsHelper.RunCommand("busctl",
                "get-property net.hadess.PowerProfiles " +
                "/net/hadess/PowerProfiles net.hadess.PowerProfiles ActiveProfile");
            // busctl returns: s "balanced"
            if (result != null && result.StartsWith("s "))
                return result[2..].Trim().Trim('"');
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Cosmic: D-Bus GetPowerProfile failed: {ex.Message}");
        }
        return null;
    }

    // True when net.hadess.PowerProfiles D-Bus service is reachable.
    public static bool HasPowerProfilesDbus()
    {
        try
        {
            var result = SysfsHelper.RunCommand("busctl",
                "status net.hadess.PowerProfiles");
            return result != null;
        }
        catch { return false; }
    }

    // Re-read session vars from systemd user manager into this process.
    // Fixes the race where ghelper starts before cosmic-session runs
    // its import-environment call.
    public static void ImportSessionEnvironment()
    {
        string[] keys = ["XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE", "WAYLAND_DISPLAY", "DISPLAY"];
        try
        {
            var result = SysfsHelper.RunCommand("systemctl",
                "--user show-environment");
            if (result == null)
                return;

            foreach (var line in result.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line[..eq];
                if (Array.IndexOf(keys, key) < 0)
                    continue;
                string val = line[(eq + 1)..];
                Environment.SetEnvironmentVariable(key, val);
                Helpers.Logger.WriteLine($"Cosmic: imported {key}={val}");
            }
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Cosmic: ImportSessionEnvironment failed: {ex.Message}");
        }
    }
}
