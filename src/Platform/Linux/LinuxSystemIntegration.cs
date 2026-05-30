using GHelper.Linux.I18n;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Linux system integration: DMI info, autostart, notifications, kernel checks.
/// Replaces WMI Win32_ComputerSystem/Win32_BIOS, Task Scheduler, etc.
/// </summary>
public class LinuxSystemIntegration : ISystemIntegration
{
    private readonly string _autostartDir;
    private readonly string _desktopFilePath;

    public LinuxSystemIntegration()
    {
        _autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart");
        _desktopFilePath = Path.Combine(_autostartDir, "ghelper.desktop");
    }

    public string GetModelName()
    {
        return SysfsHelper.ReadAttribute(Path.Combine(SysfsHelper.DmiId, "product_name"))
            ?? Labels.Get("unknown_asus");
    }

    public string GetBiosVersion()
    {
        return SysfsHelper.ReadAttribute(Path.Combine(SysfsHelper.DmiId, "bios_version"))
            ?? "Unknown";
    }

    public string GetKernelVersion()
    {
        return SysfsHelper.RunCommand("uname", "-r") ?? "Unknown";
    }

    public Version GetKernelVersionParsed()
    {
        try
        {
            var raw = GetKernelVersion();
            // Parse "6.8.0-45-generic" → Version(6, 8, 0)
            var parts = raw.Split('-')[0].Split('.');
            if (parts.Length >= 3)
                return new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            if (parts.Length == 2)
                return new Version(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        catch { }
        return new Version(0, 0);
    }

    public void SetAutostart(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(_autostartDir);
            var execField = ResolveLauncherExecField();
            var desktop = $"""
                [Desktop Entry]
                Type=Application
                Name={Labels.Get("ghelper")}
                Comment={Labels.Get("asus_laptop_control")}
                Exec={execField}
                Icon=ghelper
                Terminal=false
                Categories=System;HardwareSettings;
                StartupNotify=false
                X-GNOME-Autostart-enabled=true
                """;
            File.WriteAllText(_desktopFilePath, desktop);
            Helpers.Logger.WriteLine($"Autostart enabled: {_desktopFilePath} (exec={execField})");
        }
        else
        {
            if (File.Exists(_desktopFilePath))
            {
                File.Delete(_desktopFilePath);
                Helpers.Logger.WriteLine("Autostart disabled");
            }
        }
    }

    /// <summary>
    /// Resolves the path to write into a <c>.desktop</c> <c>Exec=</c> field
    /// (autostart entry and the application-menu entry both use this).
    ///
    /// When running from an AppImage, <see cref="GetExecutablePath"/> returns
    /// something like <c>/tmp/.mount_GHelpeemfglL/usr/bin/ghelper</c> - a
    /// FUSE mount that disappears the moment the AppImage process exits.
    /// Writing that into a launcher entry breaks it on the next boot.
    ///
    /// AppImage's runtime sets the <c>APPIMAGE</c> env var to the original
    /// <c>.AppImage</c> file path, which is what we actually want to launch.
    /// We prefer that when present, falling back to the regular binary path
    /// for direct-binary deployments (~/ghelper/ghelper, /usr/local/bin/ghelper).
    ///
    /// Must be called from the unprivileged process: pkexec strips the
    /// environment, so <c>APPIMAGE</c> is not visible to the privileged writer.
    /// </summary>
    internal static string ResolveLauncherExec()
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImagePath) && File.Exists(appImagePath))
        {
            Helpers.Logger.WriteLine($"Launcher exec: using APPIMAGE path {appImagePath}");
            return appImagePath;
        }
        return GetExecutablePath();
    }

    /// <summary>
    /// The resolved launcher path as a ready-to-write <c>Exec=</c> field value,
    /// quoted per the .desktop spec when it contains whitespace. Typical install
    /// paths (/usr/local/bin/ghelper, ~/ghelper/ghelper) hit the unquoted fast
    /// path; quoting only triggers for paths with spaces in $HOME or unusual
    /// install locations.
    /// </summary>
    internal static string ResolveLauncherExecField()
    {
        var path = ResolveLauncherExec();
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    /// <summary>
    /// A path to the running executable that ROOT (via pkexec) can actually
    /// execute, paired with cleanup. For a bare binary this is just the running
    /// path. For an AppImage, <see cref="Environment.ProcessPath"/> points inside
    /// the per-user FUSE mount (/tmp/.mount_*), which root cannot read
    /// ("Permission denied", pkexec exit 127) - so the inner binary is copied to a
    /// root-readable temp file and that path is returned instead. Disposing
    /// deletes the copy. The inner binary is a self-contained AOT build and the
    /// privileged CLI verbs (--apply-system-files / --install-gpu-helper) dispatch
    /// before any native-lib or Avalonia load, so it runs fine outside the
    /// AppImage mount.
    /// </summary>
    public sealed class PrivilegedSelf : IDisposable
    {
        public string Path { get; }
        private readonly string? _tempCopy;
        internal PrivilegedSelf(string path, string? tempCopy)
        {
            Path = path;
            _tempCopy = tempCopy;
        }
        public void Dispose()
        {
            if (_tempCopy == null)
                return;
            try
            { File.Delete(_tempCopy); }
            catch { }
        }
    }

    /// <summary>Resolve a pkexec-executable copy of ourselves (see
    /// <see cref="PrivilegedSelf"/>). Call on the unprivileged process - $APPIMAGE
    /// is needed to detect AppImage mode and is stripped under pkexec.</summary>
    public static PrivilegedSelf ResolvePrivilegedSelf()
    {
        string self = Environment.ProcessPath ?? "/proc/self/exe";

        // Bare binary: the path is already root-accessible.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")))
            return new PrivilegedSelf(self, null);

        // AppImage: copy the inner binary out of the user-private FUSE mount to a
        // root-readable temp file so pkexec can execute it as root.
        try
        {
            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"ghelper-priv-{Guid.NewGuid():N}");
            File.Copy(self, tmp, overwrite: true);
#pragma warning disable CA1416
            File.SetUnixFileMode(tmp,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
            Helpers.Logger.WriteLine(
                $"PrivilegedSelf: AppImage - copied inner binary to {tmp} for pkexec (root cannot read the FUSE mount)");
            return new PrivilegedSelf(tmp, tmp);
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"PrivilegedSelf: copy failed ({ex.Message}); using {self}");
            return new PrivilegedSelf(self, null);
        }
    }

    /// <summary>
    /// Resolves the running binary's absolute path. Uses Environment.ProcessPath
    /// (which on Linux is readlink("/proc/self/exe")). Avoids
    /// Process.GetCurrentProcess().MainModule.FileName which on Native AOT can
    /// resolve to a random mmap'd shared library (issue #80: was writing
    /// /usr/lib/x86_64-linux-gnu/libLLVM.so.21.1 into the autostart .desktop file
    /// instead of the ghelper binary path, breaking autostart on Ubuntu 26.04).
    /// </summary>
    private static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
            return path;

        // Fallback: directly resolve /proc/self/exe in case ProcessPath is null
        // for some reason on this host. /proc/self/exe is a magic symlink that
        // always points at the current process binary.
        try
        {
            var fi = new FileInfo("/proc/self/exe");
            if (fi.ResolveLinkTarget(true) is FileInfo resolved)
                return resolved.FullName;
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"GetExecutablePath: /proc/self/exe resolve failed: {ex.Message}");
        }

        // Last-ditch fallback: bare name relies on PATH lookup at autostart time.
        return "ghelper";
    }

    public bool IsAutostartEnabled()
    {
        return File.Exists(_desktopFilePath);
    }

    public void ShowNotification(string title, string body, string? iconName = null)
    {
        // Honor user opt-out: when disable_osd is true, skip the notify-send pop-up
        // entirely. We still log to the in-memory logger so debugging stays usable.
        if (Helpers.AppConfig.Is("disable_osd"))
        {
            Helpers.Logger.WriteLine($"ShowNotification (suppressed): {title} - {body}");
            return;
        }

        try
        {
            Helpers.Logger.WriteLine($"ShowNotification: {title} - {body}");

            // Build notify-send args:
            // -a "G-Helper"  → app name shown in notification center
            // -e             → transient (auto-dismiss, won't pile up in history)
            // -i ICON        → context-appropriate Breeze/freedesktop icon
            // -h string:x-canonical-private-synchronous:TAG
            // → same-tagged notifications replace each other (no stacking)
            var tag = title.Replace(" ", "").ToLowerInvariant();
            var args = $"-a \"G-Helper\" -e -h string:x-canonical-private-synchronous:{tag}";

            if (iconName != null)
                args += $" -i {iconName}";

            args += $" \"{title}\" \"{body}\"";

            var result = SysfsHelper.RunCommand("notify-send", args);
            if (result == null)
            {
                Helpers.Logger.WriteLine("notify-send failed (returned null)");
            }
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("ShowNotification failed", ex);
        }
    }

    public bool IsAsusWmiLoaded()
    {
        // Check if asus-nb-wmi module is loaded
        var modules = SysfsHelper.RunCommand("lsmod", "");
        if (modules != null && modules.Contains("asus_nb_wmi"))
            return true;

        // Also check if sysfs path exists (module might be built-in)
        return SysfsHelper.Exists(SysfsHelper.AsusWmiPlatform);
    }

    // Camera Toggle

    /// <summary>Check if the camera (uvcvideo) module is currently loaded.</summary>
    public static bool IsCameraEnabled()
    {
        var modules = SysfsHelper.RunCommand("lsmod", "");
        return modules != null && modules.Contains("uvcvideo");
    }

    /// <summary>Toggle camera by loading/unloading the uvcvideo kernel module.
    /// Requires root - tries modprobe directly, then pkexec (graphical prompt).</summary>
    public static void SetCameraEnabled(bool enabled)
    {
        // Try modprobe directly (works if running as root or via polkit rule)
        string[] args = enabled ? new[] { "uvcvideo" } : new[] { "-r", "uvcvideo" };
        var result = SysfsHelper.RunCommand("modprobe", string.Join(' ', args));
        if (result != null || IsCameraEnabled() == enabled)
        {
            Helpers.Logger.WriteLine($"Camera {(enabled ? "enabled" : "disabled")} via modprobe");
            return;
        }

        // Root helper (whitelisted modprobe of uvcvideo). pkexec GUI fallback.
        var helperArgs = new string[args.Length + 1];
        helperArgs[0] = "modprobe";
        Array.Copy(args, 0, helperArgs, 1, args.Length);
        result = SysfsHelper.RunSudoOrPkexec(SysfsHelper.GpuHelperPath, helperArgs);
        Helpers.Logger.WriteLine($"Camera {(enabled ? "enabled" : "disabled")}: {(result != null ? "OK (sudo or pkexec)" : "failed (needs root)")}");
    }

    // Touchpad Toggle

    /// <summary>Find the touchpad xinput device ID. Returns null if not found.
    /// Requires xinput (works on X11 and Wayland with XWayland).</summary>
    public static string? FindTouchpadId()
    {
        var fullList = SysfsHelper.RunCommand("xinput", "list");
        if (fullList == null)
            return null;

        foreach (var line in fullList.Split('\n'))
        {
            if (line.Contains("Touchpad", StringComparison.OrdinalIgnoreCase))
            {
                // Extract id=N from the line
                var match = System.Text.RegularExpressions.Regex.Match(line, @"id=(\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>Check if the touchpad is currently enabled.</summary>
    public static bool? IsTouchpadEnabled()
    {
        var id = FindTouchpadId();
        if (id == null)
            return null; // No touchpad found

        var props = SysfsHelper.RunCommand("xinput", $"list-props {id}");
        if (props == null)
            return null;

        // Look for "Device Enabled" property
        foreach (var line in props.Split('\n'))
        {
            if (line.Contains("Device Enabled"))
            {
                return line.TrimEnd().EndsWith("1");
            }
        }
        return null;
    }

    /// <summary>Enable or disable the touchpad via xinput.</summary>
    public static void SetTouchpadEnabled(bool enabled)
    {
        var id = FindTouchpadId();
        if (id == null)
        {
            Helpers.Logger.WriteLine("Touchpad not found in xinput");
            return;
        }

        string action = enabled ? "enable" : "disable";
        SysfsHelper.RunCommand("xinput", $"{action} {id}");
        Helpers.Logger.WriteLine($"Touchpad {action}d (xinput id={id})");
    }

    // Touchscreen Toggle

    /// <summary>Find the touchscreen xinput device ID. Returns null if not found.</summary>
    public static string? FindTouchscreenId()
    {
        var fullList = SysfsHelper.RunCommand("xinput", "list");
        if (fullList == null)
            return null;

        foreach (var line in fullList.Split('\n'))
        {
            if (line.Contains("Touchscreen", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Touch Screen", StringComparison.OrdinalIgnoreCase) ||
                (line.Contains("touch", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains("screen", StringComparison.OrdinalIgnoreCase)))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"id=(\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>Check if the touchscreen is currently enabled.</summary>
    public static bool? IsTouchscreenEnabled()
    {
        var id = FindTouchscreenId();
        if (id == null)
            return null; // No touchscreen found

        var props = SysfsHelper.RunCommand("xinput", $"list-props {id}");
        if (props == null)
            return null;

        foreach (var line in props.Split('\n'))
        {
            if (line.Contains("Device Enabled"))
            {
                return line.TrimEnd().EndsWith("1");
            }
        }
        return null;
    }

    /// <summary>Enable or disable the touchscreen via xinput.</summary>
    public static void SetTouchscreenEnabled(bool enabled)
    {
        var id = FindTouchscreenId();
        if (id == null)
        {
            Helpers.Logger.WriteLine("Touchscreen not found in xinput");
            return;
        }

        string action = enabled ? "enable" : "disable";
        SysfsHelper.RunCommand("xinput", $"{action} {id}");
        Helpers.Logger.WriteLine($"Touchscreen {action}d (xinput id={id})");
    }

    // CPU Core Control

    /// <summary>Get the total number of CPU threads (logical processors).</summary>
    public static int GetCpuCount()
    {
        try
        {
            return Directory.GetDirectories("/sys/devices/system/cpu/", "cpu[0-9]*").Length;
        }
        catch { return 0; }
    }

    /// <summary>Get the number of currently online CPU cores.</summary>
    public static int GetOnlineCpuCount()
    {
        int count = 0;
        try
        {
            var cpuDirs = Directory.GetDirectories("/sys/devices/system/cpu/", "cpu[0-9]*");
            foreach (var dir in cpuDirs)
            {
                var onlinePath = Path.Combine(dir, "online");
                if (!File.Exists(onlinePath))
                {
                    count++; // cpu0 has no online file, it's always on
                    continue;
                }
                if (SysfsHelper.ReadInt(onlinePath, 0) == 1)
                    count++;
            }
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("GetOnlineCpuCount failed", ex);
        }
        return count;
    }

    /// <summary>Set the number of online CPU cores. Disables from the highest-numbered cores down.</summary>
    public static void SetOnlineCpuCount(int targetCount)
    {
        try
        {
            var cpuDirs = Directory.GetDirectories("/sys/devices/system/cpu/", "cpu[0-9]*");
            // Sort numerically
            Array.Sort(cpuDirs, (a, b) =>
            {
                int numA = int.Parse(Path.GetFileName(a).Replace("cpu", ""));
                int numB = int.Parse(Path.GetFileName(b).Replace("cpu", ""));
                return numA.CompareTo(numB);
            });

            int total = cpuDirs.Length;
            targetCount = Math.Clamp(targetCount, 1, total);

            for (int i = 0; i < total; i++)
            {
                var onlinePath = Path.Combine(cpuDirs[i], "online");
                if (!File.Exists(onlinePath))
                    continue; // cpu0 can't be toggled

                bool shouldBeOnline = i < targetCount;
                SysfsHelper.WriteAttribute(onlinePath, shouldBeOnline ? "1" : "0");
            }

            Helpers.Logger.WriteLine($"CPU cores set to {targetCount}/{total}");
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine("SetOnlineCpuCount failed", ex);
        }
    }
}
