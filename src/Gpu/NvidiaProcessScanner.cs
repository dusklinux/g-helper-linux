using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.Install;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Gpu;

/// <summary>
/// Sync state of the on-disk gpu-helper vs the copy embedded in this ghelper
/// binary. Drives the startup self-update prompt.
/// </summary>
public enum HelperState { InSync, Stale, Missing }

/// <summary>
/// One process detected as a NVIDIA holder. Two kinds:
///   FdCount &gt; 0   actively using the dGPU (/dev/nvidia* file descriptors).
///                  rmmod fails while this is non-zero - hard blocker for Eco.
///   LibsMapped &gt; 0 has libnvidia-*/libcuda mapped in /proc/&lt;pid&gt;/maps but
///                  no /dev/nvidia* FDs. Doesn't block kernel unload, but the
///                  process will fail silently if it tries to call into the
///                  driver after the modules are gone.
/// </summary>
public sealed record NvidiaHolder(int Pid, string Comm, string User, int FdCount, int LibsMapped, bool IsOwnedByCurrentUser, string ServiceUnit = "");

public static class NvidiaProcessScanner
{
    [DllImport("libc")]
    private static extern uint getuid();

    private static readonly Lazy<uint> _currentUid = new(() =>
    {
        try
        { return getuid(); }
        catch { return uint.MaxValue; }
    });

    private static readonly int _selfPid = Environment.ProcessId;

    private const string HelperPath = SysfsHelper.GpuHelperPath;
    private static volatile bool _helperChecked = false;
    private static readonly object _helperLock = new();

    private static readonly object _privCacheLock = new();
    private static DateTime _privCacheTime = DateTime.MinValue;
    private static List<NvidiaHolder>? _privCacheResults;
    private const int PrivilegedCacheSeconds = 5;

    private static readonly string[] SystemCommPrefixes = new[]
    {
        // NVIDIA daemons
        "nvidia-persiste", "nvidia-powerd", "nvidia-bug-repor",

        // Display servers + compositors
        "Xwayland", "Xorg", "kwin", "Hyprland", "sway", "river", "weston",

        // Display managers
        "sddm", "gdm", "lightdm",

        // KDE Plasma
        "plasmashell", "kded", "kdeconnect", "ksmserver", "ksecretd",
        "kaccess", "kwalletd", "ksystemstats", "kglobalaccel", "polkit-kde",
        "DiscoverNotifie", "krunner", "kactivitymanage", "kioslave", "kioworker",
        "baloo_", "akonadi", "dolphin", "drkonqi",

        // GNOME
        "gnome-shell", "mutter", "gnome-session", "gnome-keyring", "gsd-",
        "gjs", "gnome-terminal", "nautilus", "evolution-", "tracker-miner",
        "gnome-control", "gnome-disk",

        // XFCE
        "xfwm4", "xfce4-", "xfsettingsd", "xfdesktop",

        // LXQt
        "lxqt-",

        // Cinnamon
        "cinnamon", "csd-", "nemo",

        // MATE
        "mate-", "marco", "caja",

        // XDG portals
        "xdg-desktop-po", "xdg-document-p",

        // Audio / video pipeline (system-level)
        "pipewire", "wireplumber", "pulseaudio",

        // Session services
        "polkit", "dconf-service", "gvfs", "geoclue",
        "dbus-daemon", "dbus-broker", "at-spi-bus-lau", "at-spi2-registr",
    };

    private static bool IsSystemProcess(string comm)
    {
        foreach (var prefix in SystemCommPrefixes)
            if (comm.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static IReadOnlyList<NvidiaHolder> ScanHolders()
    {
        var privileged = TryPrivilegedScanCached();
        if (privileged != null)
            return privileged;
        return UnprivilegedFdScan();
    }

    public static void InvalidateScanCache()
    {
        lock (_privCacheLock)
        {
            _privCacheTime = DateTime.MinValue;
            _privCacheResults = null;
        }
    }

    private static List<NvidiaHolder> UnprivilegedFdScan()
    {
        var holders = new Dictionary<int, NvidiaHolder>();

        if (!Directory.Exists("/proc"))
            return new List<NvidiaHolder>();

        IEnumerable<string> procDirs;
        try
        { procDirs = Directory.EnumerateDirectories("/proc"); }
        catch { return new List<NvidiaHolder>(); }

        foreach (var procDir in procDirs)
        {
            var name = Path.GetFileName(procDir);
            if (!int.TryParse(name, out int pid))
                continue;
            if (pid == _selfPid)
                continue;

            var fdDir = Path.Combine(procDir, "fd");
            if (!Directory.Exists(fdDir))
                continue;

            int count = CountNvidiaFds(fdDir, out _);
            if (count > 0)
            {
                string comm = ReadComm(pid);
                if (IsSystemProcess(comm))
                    continue;
                uint procUid = ReadUid(pid);
                string user = ResolveUserName(procUid);
                bool owned = procUid == _currentUid.Value;
                string serviceUnit = ResolveServiceUnit(pid);
                holders[pid] = new NvidiaHolder(pid, comm, user, count, 0, owned, serviceUnit);
            }
        }

        return new List<NvidiaHolder>(holders.Values);
    }

    public static int CountFdHolders()
    {
        var privileged = TryPrivilegedScanCached();
        if (privileged != null)
        {
            int n = 0;
            foreach (var h in privileged)
                if (h.FdCount > 0)
                    n++;
            return n;
        }

        return CountHolders();
    }

    public static int CountHolders()
    {
        var privileged = TryPrivilegedScanCached();
        if (privileged != null)
            return privileged.Count;

        int count = 0;
        if (!Directory.Exists("/proc"))
            return 0;

        IEnumerable<string> procDirs;
        try
        { procDirs = Directory.EnumerateDirectories("/proc"); }
        catch { return 0; }

        foreach (var procDir in procDirs)
        {
            var name = Path.GetFileName(procDir);
            if (!int.TryParse(name, out int pid))
                continue;
            if (pid == _selfPid)
                continue;
            var fdDir = Path.Combine(procDir, "fd");
            if (!Directory.Exists(fdDir))
                continue;
            if (CountNvidiaFds(fdDir, out _) > 0)
                count++;
        }
        return count;
    }

    private static int CountNvidiaFds(string fdDir, out bool permDenied)
    {
        permDenied = false;
        int count = 0;
        IEnumerable<string> entries;
        try
        { entries = Directory.EnumerateFileSystemEntries(fdDir); }
        catch (UnauthorizedAccessException) { permDenied = true; return 0; }
        catch (IOException) { permDenied = true; return 0; }
        catch { return 0; }

        foreach (var entry in entries)
        {
            try
            {
                var target = File.ResolveLinkTarget(entry, returnFinalTarget: false);
                if (target?.FullName is string path && IsNvidiaDevice(path))
                    count++;
            }
            catch
            {
            }
        }
        return count;
    }

    private static List<NvidiaHolder>? TryPrivilegedScanCached()
    {
        lock (_privCacheLock)
        {
            if (_privCacheResults != null
                && (DateTime.UtcNow - _privCacheTime).TotalSeconds < PrivilegedCacheSeconds)
                return _privCacheResults;
        }

        if (!EnsureHelper())
            return null;

        var sudoArgs = new[] { "-n", HelperPath, "list", _selfPid.ToString() };
        var output = SysfsHelper.RunCommandWithTimeout(SysfsHelper.SudoPath, sudoArgs, 3000);
        if (output == null)
            return null; // sudoers rejected or helper failed - fall through to unprivileged scan

        var holders = new Dictionary<int, NvidiaHolder>();
        int rawCount = 0;
        int filteredSystem = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4)
                continue;
            if (!int.TryParse(parts[0], out int pid))
                continue;
            if (!int.TryParse(parts[1], out int fdCount))
                continue;
            if (!uint.TryParse(parts[2], out uint procUid))
                continue;
            string comm = parts[3];
            int libsMapped = (parts.Length >= 5 && int.TryParse(parts[4], out int lm)) ? lm : 0;
            // Column 6 (optional): systemd unit owning the process, "-" or absent
            // when the process is not part of a service.
            string serviceUnit = (parts.Length >= 6 && parts[5] != "-") ? parts[5] : "";

            // Helper only emits a row when fdCount > 0 OR libsMapped > 0,
            // but defend against malformed input.
            if (fdCount == 0 && libsMapped == 0)
                continue;
            rawCount++;

            // Hide NVIDIA daemons, DE shells, display servers, portals etc.
            // Only third-party apps surface to the UI and the active-driver
            // check that gates the Eco blocking dialog.
            if (IsSystemProcess(comm))
            {
                filteredSystem++;
                continue;
            }

            string user = ResolveUserName(procUid);
            bool owned = procUid == _currentUid.Value;
            holders[pid] = new NvidiaHolder(pid, comm, user, fdCount, libsMapped, owned, serviceUnit);
        }
        if (rawCount > 0)
            Helpers.Logger.WriteLine($"NvidiaProcessScanner: {rawCount} raw, {filteredSystem} filtered system, {holders.Count} shown");

        var result = new List<NvidiaHolder>(holders.Values);
        lock (_privCacheLock)
        {
            _privCacheResults = result;
            _privCacheTime = DateTime.UtcNow;
        }
        return result;
    }

    public static bool EnsureHelper()
    {
        // Cheap re-check on every call: a later install (via the startup
        // system-files prompt or its Install/Repair button) is picked up at once.
        if (File.Exists(HelperPath))
            return true;
        if (_helperChecked)
            return false; // self-install already attempted/decided this process

        // Don't race the startup system-files prompt's pkexec: wait for the user's
        // decision first. No-op on the UI thread and once the decision is made, so
        // this never blocks the thread showing the (modal) prompt. Done OUTSIDE
        // the lock below so a long wait can't block UI-thread callers on the lock.
        Installer.WaitForStartupDecision();

        // The prompt may have just installed the helper.
        if (File.Exists(HelperPath))
            return true;

        // When the startup prompt criteria were met, that prompt owns the
        // gpu-helper install. It is still absent here => either it is mid-flight
        // or the user declined; in both cases we must NOT fire a second, competing
        // pkexec (the user's rule: the same check that shows the window cancels
        // this self-install).
        if (Installer.StartupWillPrompt)
        {
            _helperChecked = true;
            Helpers.Logger.WriteLine(
                "NvidiaProcessScanner: startup system-files prompt handles gpu-helper - skipping self-install pkexec");
            return false;
        }

        // No prompt in play (check disabled, or no other problems): self-install,
        // serialized so concurrent callers can't fire two pkexec dialogs.
        lock (_helperLock)
        {
            if (File.Exists(HelperPath))
                return true;
            if (_helperChecked)
                return false;
            _helperChecked = true;
            Helpers.Logger.WriteLine(
                $"NvidiaProcessScanner: {HelperPath} not found - attempting pkexec self-install");
            return RunPkexecInstall();
        }
    }

    public static HelperState CheckHelper()
    {
        try
        {
            if (!File.Exists(HelperPath))
                return HelperState.Missing;

            using var res = typeof(NvidiaProcessScanner).Assembly
                .GetManifestResourceStream("gpu-helper");
            if (res == null)
                return HelperState.InSync; // no embedded copy to compare against

            using var sha = SHA256.Create();
            byte[] embedded = sha.ComputeHash(res);
            byte[] installed;
            using (var fs = File.OpenRead(HelperPath))
                installed = sha.ComputeHash(fs);

            return embedded.AsSpan().SequenceEqual(installed)
                ? HelperState.InSync
                : HelperState.Stale;
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"NvidiaProcessScanner: CheckHelper failed: {ex.Message}");
            return HelperState.InSync;
        }
    }

    public static bool RunPkexecInstall()
    {
        // Under an AppImage, ProcessPath is inside the per-user FUSE mount which
        // root cannot read; resolve a root-runnable copy (deleted on dispose,
        // after pkexec returns).
        using var self = LinuxSystemIntegration.ResolvePrivilegedSelf();
        if (string.IsNullOrEmpty(self.Path) || !File.Exists(self.Path))
        {
            Helpers.Logger.WriteLine(
                "NvidiaProcessScanner: cannot self-install scan helper - executable path unknown");
            return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pkexec",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(self.Path);
            psi.ArgumentList.Add("--install-gpu-helper");
            psi.ArgumentList.Add(Path.GetDirectoryName(HelperPath)!);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
                return false;

            var stdout = proc.StandardOutput.ReadToEnd().Trim();
            var stderr = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit(60000);

            bool ok = File.Exists(HelperPath);
            if (!string.IsNullOrEmpty(stdout))
                Helpers.Logger.WriteLine($"NvidiaProcessScanner: install-gpu-helper: {stdout}");
            if (!string.IsNullOrEmpty(stderr))
                Helpers.Logger.WriteLine($"NvidiaProcessScanner: install-gpu-helper stderr: {stderr}");
            Helpers.Logger.WriteLine(ok
                ? "NvidiaProcessScanner: gpu-helper installed OK"
                : $"NvidiaProcessScanner: gpu-helper install failed (exit {proc.ExitCode})");
            return ok;
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"NvidiaProcessScanner: pkexec self-install exception: {ex.Message}");
            return false;
        }
    }

    private static bool IsNvidiaDevice(string path)
        => path.StartsWith("/dev/nvidia", StringComparison.Ordinal);

    private static string ReadComm(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch
        {
            return "?";
        }
    }

    private static uint ReadUid(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/status"))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                    continue;
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && uint.TryParse(parts[1], out uint uid))
                    return uid;
            }
        }
        catch
        {
        }
        return uint.MaxValue;
    }

    private static string ResolveServiceUnit(int pid)
    {
        try
        {
            string? path = null;
            foreach (var line in File.ReadLines($"/proc/{pid}/cgroup"))
            {
                if (line.StartsWith("0::", StringComparison.Ordinal))
                {
                    path = line.Substring(3);
                    break;
                }
                if (path == null)
                {
                    int c = line.LastIndexOf(':');
                    if (c >= 0)
                        path = line.Substring(c + 1);
                }
            }
            if (string.IsNullOrEmpty(path))
                return "";

            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            return leaf.EndsWith(".service", StringComparison.Ordinal) ? leaf : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveUserName(uint uid)
    {
        if (uid == uint.MaxValue)
            return "?";
        try
        {
            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 3 && uint.TryParse(parts[2], out uint passwdUid) && passwdUid == uid)
                    return parts[0];
            }
        }
        catch
        {
        }
        return $"uid:{uid}";
    }

    private static IReadOnlyList<int> ReadChildren(int pid)
    {
        try
        {
            string path = $"/proc/{pid}/task/{pid}/children";
            if (!File.Exists(path))
                return Array.Empty<int>();
            string content = File.ReadAllText(path).Trim();
            if (content.Length == 0)
                return Array.Empty<int>();
            var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<int>(parts.Length);
            foreach (var p in parts)
                if (int.TryParse(p, out int cpid) && cpid != _selfPid)
                    result.Add(cpid);
            return result;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    public static IReadOnlyList<int> GetDescendantPids(int rootPid)
    {
        var visited = new HashSet<int> { rootPid };
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            foreach (var child in ReadChildren(p))
                if (visited.Add(child))
                    queue.Enqueue(child);
        }
        visited.Remove(rootPid);
        return new List<int>(visited);
    }

    /// Parse /proc/<pid>/stat to extract PPID. Robust against comm-with-spaces
    /// (e.g. "/proc/123/stat" = "123 (my comm) S 1 ..."). Returns 0 on failure.
    private static int ReadParentPid(int pid)
    {
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int closeParen = stat.LastIndexOf(')');
            if (closeParen < 0 || closeParen + 2 >= stat.Length)
                return 0;
            var fields = stat.Substring(closeParen + 2).Split(' ');
            if (fields.Length < 2)
                return 0;
            return int.TryParse(fields[1], out int ppid) ? ppid : 0;
        }
        catch { return 0; }
    }

    /// Walk parent chain. Return topmost ancestor whose comm matches pid's comm.
    /// Stops at PID 1 (init) or first non-matching comm. Caps walk at 20 hops
    /// against pathological proc-tree loops. Returns pid if no same-comm ancestor.
    /// For Chrome: holder is gpu-process leaf -> walks up through zygote -> main
    /// browser process whose parent is systemd (different comm) -> stops there.
    public static int FindSameCommAncestor(int pid)
    {
        string rootComm = ReadComm(pid);
        if (rootComm == "?")
            return pid;

        int current = pid;
        for (int depth = 0; depth < 20; depth++)
        {
            int parent = ReadParentPid(current);
            if (parent <= 1)
                break;
            string parentComm = ReadComm(parent);
            if (parentComm != rootComm)
                break;
            current = parent;
        }
        return current;
    }

    private static int ReadNvidiaRefcnt()
        => SysfsHelper.ReadInt("/sys/module/nvidia/refcnt", -1);

    public static bool KillProcess(int pid, bool force, NvidiaHolder holder, out string error)
    {
        error = string.Empty;
        if (pid == _selfPid)
        {
            error = "refusing to kill self";
            Logger.WriteLine($"NvidiaProcessScanner.KillProcess: refusing self-kill of PID {pid}");
            return false;
        }

        // Walk UP to find the topmost same-comm ancestor. Holders are typically
        // leaf processes (chrome gpu-process) whose parent zygote auto-respawns
        // them on death. Killing the leaf alone does nothing - the parent
        // refills the slot within milliseconds. We must kill the tree root.
        int root = FindSameCommAncestor(pid);
        if (root != pid)
            Logger.WriteLine($"NvidiaProcessScanner.KillProcess: walked up from {pid} to root {root} (same comm {holder.Comm})");

        var descendants = GetDescendantPids(root);
        var allTargets = new List<int>(descendants.Count + 1);
        allTargets.AddRange(descendants);
        allTargets.Add(root);

        string sig = force ? "-9" : "-15";
        int beforeRefcnt = ReadNvidiaRefcnt();

        Logger.WriteLine($"NvidiaProcessScanner.KillProcess: root={root} ({holder.Comm}) descendants={descendants.Count} sig={sig} nvidia/refcnt={beforeRefcnt}");

        try
        {
            // Service-aware kill via the root helper: stops the systemd unit
            // owning each holder (so it cannot respawn) then signals the pids.
            KillViaHelper(allTargets, sig);
            if (PollAllDead(allTargets, 2000))
            {
                Logger.WriteLine($"NvidiaProcessScanner.KillProcess: tree dead after helper kill; nvidia/refcnt={ReadNvidiaRefcnt()}");
                return true;
            }

            int survivors = 0;
            foreach (var p in allTargets)
                if (Directory.Exists($"/proc/{p}"))
                    survivors++;
            error = $"{survivors} process(es) still alive after kill attempt";
            Logger.WriteLine($"NvidiaProcessScanner.KillProcess: {error}; nvidia/refcnt={ReadNvidiaRefcnt()}");
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.WriteLine($"NvidiaProcessScanner.KillProcess({pid}, force={force}) failed: {ex.Message}");
            return false;
        }
    }

    private static bool AllDead(IEnumerable<int> pids)
    {
        foreach (var p in pids)
            if (Directory.Exists($"/proc/{p}"))
                return false;
        return true;
    }

    private static bool PollAllDead(IEnumerable<int> pids, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (AllDead(pids))
                return true;
            Thread.Sleep(100);
            waited += 100;
        }
        return AllDead(pids);
    }

    public static int KillAllHolders(bool force, out int killed, out int failed)
    {
        killed = 0;
        failed = 0;
        var holders = ScanHolders();
        if (holders.Count == 0)
            return 0;

        var allTargets = new HashSet<int>();
        bool anyForeignOwned = false;
        int skippedLibs = 0;
        foreach (var h in holders)
        {
            if (h.Pid == _selfPid)
                continue;
            if (h.FdCount == 0)
            {
                skippedLibs++;
                continue;
            }
            // Walk up to root of same-comm tree (handles chrome zygote respawn)
            int root = FindSameCommAncestor(h.Pid);
            if (root != h.Pid)
                Logger.WriteLine($"NvidiaProcessScanner.KillAllHolders: walked {h.Pid} -> {root} (comm {h.Comm})");
            allTargets.Add(root);
            foreach (var d in GetDescendantPids(root))
                allTargets.Add(d);
            if (!h.IsOwnedByCurrentUser)
                anyForeignOwned = true;
        }
        allTargets.Remove(_selfPid);
        if (skippedLibs > 0)
            Logger.WriteLine($"NvidiaProcessScanner.KillAllHolders: skipped {skippedLibs} lib-only holders (session-safety)");

        if (allTargets.Count == 0)
            return 0;

        string sig = force ? "-9" : "-15";
        int beforeRefcnt = ReadNvidiaRefcnt();

        Logger.WriteLine($"NvidiaProcessScanner.KillAllHolders: holders={holders.Count} totalTargets={allTargets.Count} sig={sig} foreign={anyForeignOwned} nvidia/refcnt={beforeRefcnt}");

        try
        {
            KillViaHelper(allTargets, sig);
            PollAllDead(allTargets, 2000);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"NvidiaProcessScanner.KillAllHolders: {ex.Message}");
        }

        int afterRefcnt = ReadNvidiaRefcnt();
        foreach (var p in allTargets)
        {
            if (Directory.Exists($"/proc/{p}"))
                failed++;
            else
                killed++;
        }
        Logger.WriteLine($"NvidiaProcessScanner.KillAllHolders: killed={killed} failed={failed} nvidia/refcnt {beforeRefcnt} -> {afterRefcnt}");

        return holders.Count;
    }

    /// <summary>
    /// Two-pass graceful-then-force kill targeting the caller-supplied snapshot
    /// of holders (typically the list shown in NvidiaProcessesWindow). Unlike
    /// <see cref="KillAllHolders"/>:
    ///   - includes lib-only holders (caller is responsible for session safety;
    ///     the dialog has already warned the user)
    ///   - sends SIGTERM first, polls 2s, then SIGKILL to survivors and polls 2s
    ///   - reports which input holders' root pids are still alive
    /// </summary>
    public static void KillHoldersGracefulThenForce(
        IReadOnlyList<NvidiaHolder> snapshot,
        out List<NvidiaHolder> survivors)
    {
        survivors = new List<NvidiaHolder>();
        if (snapshot == null || snapshot.Count == 0)
            return;

        // Walk each holder up to its same-comm ancestor, collect descendants.
        // Dedupe via HashSet - sibling holders (chrome --type=gpu-process etc.)
        // often share a zygote root, so distinct holders collapse to one tree.
        var allTargets = new HashSet<int>();
        var holderByRoot = new Dictionary<int, NvidiaHolder>();
        bool anyForeign = false;
        foreach (var h in snapshot)
        {
            if (h.Pid == _selfPid)
                continue;
            int root = FindSameCommAncestor(h.Pid);
            if (root != h.Pid)
                Logger.WriteLine($"NvidiaProcessScanner.KillHoldersGracefulThenForce: walked {h.Pid} -> {root} (comm {h.Comm})");
            holderByRoot[root] = h;
            allTargets.Add(root);
            foreach (var d in GetDescendantPids(root))
                allTargets.Add(d);
            if (!h.IsOwnedByCurrentUser)
                anyForeign = true;
        }
        allTargets.Remove(_selfPid);
        if (allTargets.Count == 0)
            return;

        int beforeRefcnt = ReadNvidiaRefcnt();
        Logger.WriteLine($"NvidiaProcessScanner.KillHoldersGracefulThenForce: snapshot={snapshot.Count} targets={allTargets.Count} foreign={anyForeign} nvidia/refcnt={beforeRefcnt}");

        try
        {
            // Pass 1: SIGTERM batch
            SendKillBatch(allTargets, "-15");
            PollAllDead(allTargets, 2000);

            // Pass 2: SIGKILL survivors
            var pass2 = new HashSet<int>();
            foreach (var p in allTargets)
                if (Directory.Exists($"/proc/{p}"))
                    pass2.Add(p);
            if (pass2.Count > 0)
            {
                Logger.WriteLine($"NvidiaProcessScanner.KillHoldersGracefulThenForce: {pass2.Count} survived SIGTERM, sending SIGKILL");
                SendKillBatch(pass2, "-9");
                PollAllDead(pass2, 2000);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"NvidiaProcessScanner.KillHoldersGracefulThenForce: {ex.Message}");
        }

        // Survivors: input holders whose root pid is still in /proc.
        foreach (var kv in holderByRoot)
        {
            if (Directory.Exists($"/proc/{kv.Key}"))
                survivors.Add(kv.Value);
        }

        int afterRefcnt = ReadNvidiaRefcnt();
        Logger.WriteLine($"NvidiaProcessScanner.KillHoldersGracefulThenForce: survivors={survivors.Count} nvidia/refcnt {beforeRefcnt} -> {afterRefcnt}");

        // Next ScanHolders() must see post-kill truth, not the pre-kill snapshot
        // cached up to 5s ago. UI refresh timers (NvidiaProcessesWindow / driver
        // dialog) also benefit from immediate convergence.
        InvalidateScanCache();
    }

    private static void SendKillBatch(ICollection<int> pids, string sig)
        => KillViaHelper(pids, sig);

    private static void KillViaHelper(ICollection<int> pids, string sig)
    {
        if (pids.Count == 0)
            return;

        if (EnsureHelper())
        {
            var args = new string[pids.Count + 2];
            args[0] = "kill";
            args[1] = sig;
            int j = 2;
            foreach (var p in pids)
                args[j++] = p.ToString();
            var r = SysfsHelper.RunSudoOrPkexec(HelperPath, args, sudoTimeoutMs: 20000, pkexecTimeoutMs: 60000);
            if (r != null)
            {
                if (!string.IsNullOrWhiteSpace(r))
                    foreach (var line in r.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        Logger.WriteLine($"NvidiaProcessScanner.KillViaHelper: {line.Trim()}");
                return;
            }
            Logger.WriteLine("NvidiaProcessScanner.KillViaHelper: helper kill failed, falling back to plain kill");
        }

        string pidArgs = string.Join(' ', pids);
        SysfsHelper.RunCommandWithTimeout("kill", $"{sig} {pidArgs}", 5000);
    }
}
