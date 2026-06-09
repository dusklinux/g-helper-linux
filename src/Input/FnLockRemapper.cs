using System.Runtime.InteropServices;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Input;

/// <summary>
/// In-process userland fn-lock remapper. While running, it grabs every
/// internal-keyboard /dev/input/event* device exclusively (EVIOCGRAB) and
/// re-emits each key event through a single /dev/uinput virtual keyboard,
/// optionally rewriting bare F1..F12 to media keys based on the
/// <see cref="FnLockOn"/> toggle.
///
/// Architecture mirrors keyd: enumerate, classify, grab, replay through
/// uinput. Differences:
///   - Single-process, lifecycle bound to the host app (Start/Stop).
///   - Integrated keyboards only (heuristic on EVIOCGID bus type).
///   - No layers or chord engine; only F-key passthrough vs media-key remap
///     and the toggle hotkey detection.
///   - No daemon, no system config, no IPC.
///
/// Permissions:
///   The host process must have read+write access to /dev/uinput and
///   /dev/input/event*. Typically requires the user to be in the "input"
///   group, or a udev rule granting ACL. The class detects this up-front
///   via <see cref="CheckCapability"/>.
/// </summary>
public sealed class FnLockRemapper : IDisposable
{
    private const string VirtualDeviceName = "g-helper virtual keyboard";

    /// <summary>Polling timeout (ms) on the event loop. Bounds shutdown latency.</summary>
    private const int PollTimeoutMs = 500;

    private readonly object _stateLock = new();
    private readonly List<GrabbedDevice> _devices = new();
    private int _uinputFd = -1;
    private int[] _stopPipe = { -1, -1 };
    private Thread? _loopThread;
    private volatile bool _running;
    private Dictionary<ushort, FnLockTarget> _activeMap = new();

    // Toggle-hotkey state machine. Default: Super (LEFTMETA) + F8.
    private ushort _toggleModifier = EvdevInterop.KEY_LEFTMETA;
    private ushort _toggleKey = EvdevInterop.KEY_F2;
    private bool _modifierDown;
    private bool _toggleSuppressed;  // true after we ate the press; eat the release too.

    /// <summary>
    /// When true, log only keys-of-interest (F1..F12, the configured toggle
    /// modifier+key, and the configured remap targets) for debugging. Never
    /// logs alphanumeric or other unrelated keys; not a keylogger.
    /// Read fresh on every <see cref="Start"/>.
    /// </summary>
    private volatile bool _debug;
    private HashSet<ushort> _interestSet = new();

    /// <summary>
    /// Most recent MSC_SCAN value seen on each grabbed device fd.
    /// The Linux input subsystem emits EV_MSC/MSC_SCAN immediately before the
    /// paired EV_KEY in the same SYN frame; we cache it here so the EV_KEY
    /// handler can consult it for ASUS hotkey scancode → binding-name lookup.
    /// </summary>
    private readonly Dictionary<int, int> _lastScanByFd = new();

    /// <summary>
    /// Current fn-lock state, matching Windows g-helper convention:
    ///   true  = media keys active (F1..F12 produce mapped media targets)
    ///   false = F-keys passthrough (F1..F12 emit literally)
    /// Setter is thread-safe.
    /// </summary>
    public bool FnLockOn
    {
        get => _fnLockOn;
        set
        {
            if (_fnLockOn == value)
                return;
            _fnLockOn = value;
            FnLockChanged?.Invoke(value);
        }
    }
    private volatile bool _fnLockOn;

    /// <summary>Raised whenever <see cref="FnLockOn"/> flips.</summary>
    public event Action<bool>? FnLockChanged;

    /// <summary>True if the subsystem is currently grabbing input.</summary>
    public bool IsActive => _running;

    /// <summary>
    /// Human-readable list of currently grabbed device names. Useful for the
    /// "Active on: ..." status line in the UI.
    /// </summary>
    public string[] ActiveDeviceNames
    {
        get
        {
            lock (_stateLock)
                return _devices.Select(d => d.Name).ToArray();
        }
    }

    /// <summary>
    /// Set the toggle hotkey at runtime. Caller is responsible for persisting.
    /// </summary>
    public void SetToggleHotkey(ushort modifier, ushort key)
    {
        lock (_stateLock)
        {
            _toggleModifier = modifier;
            _toggleKey = key;
            _modifierDown = false;
            _toggleSuppressed = false;
            RebuildInterestSet();
        }
    }

    /// <summary>
    /// Hot-swap debug logging without restarting the remapper. Triggered by
    /// the FnLockWindow advanced-section checkbox to avoid the device
    /// regrab cycle that a Stop+Start would incur.
    /// </summary>
    public void SetDebug(bool enabled)
    {
        _debug = enabled;
    }

    /// <summary>
    /// Hot-reload the per-key map from AppConfig without restarting the
    /// remapper. Triggered by FnLockWindow when the user changes per-key
    /// dropdowns or hits Reset to defaults. Atomic swap of the dictionary;
    /// in-flight events will see either the old or new map but never a
    /// partially-mutated state.
    /// </summary>
    public void ReloadKeymap()
    {
        var fresh = FnLockKeymap.ResolveActiveMap();
        lock (_stateLock)
        {
            _activeMap = fresh;
            RebuildInterestSet();
        }
    }

    /// <summary>
    /// Probe whether /dev/uinput and /dev/input/event* are accessible. Returns
    /// (available, reason). reason is non-empty when available=false.
    /// Distinguishes ENOENT (uinput module not loaded) from EACCES (permission)
    /// so the user gets actionable guidance via the localized message strings.
    /// </summary>
    public static (bool Available, string Reason) CheckCapability()
    {
        int rc = EvdevInterop.access("/dev/uinput", EvdevInterop.W_OK);
        if (rc != 0)
        {
            int err = Marshal.GetLastWin32Error();
            // Surface the raw errno in the log for diagnostics, but show a
            // clean localized string in the UI.
            Logger.WriteLine($"FnLockRemapper: /dev/uinput access failed errno={err}");
            string reason = err switch
            {
                2 /* ENOENT */ => Labels.Get("fnlock_unavail_no_uinput"),
                13 /* EACCES */ => Labels.Get("fnlock_unavail_denied"),
                _ => Labels.Get("fnlock_unavail_errno"),
            };
            return (false, reason);
        }
        if (!Directory.Exists("/dev/input"))
            return (false, Labels.Get("fnlock_unavail_no_input_dir"));
        return (true, string.Empty);
    }

    /// <summary>
    /// Begin grabbing input devices and emitting through a virtual keyboard.
    /// Idempotent: calling Start when already running returns true.
    /// Returns false if any setup step fails (capability check, virtual
    /// keyboard creation, device grab, pipe). Specific failure reason is
    /// already in the log via the individual log lines below; the caller
    /// can show a generic notification on false return.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            if (_running)
                return true;

            var (ok, reason) = CheckCapability();
            if (!ok)
            {
                Logger.WriteLine($"FnLockRemapper: cannot start - {reason}");
                return false;
            }

            _activeMap = FnLockKeymap.ResolveActiveMap();
            _debug = AppConfig.Is("fnlock_debug");
            RebuildInterestSet();
            DetectKeydCoexistence();

            if (!CreateVirtualKeyboard())
            {
                Logger.WriteLine("FnLockRemapper: failed to create virtual keyboard");
                return false;
            }

            int grabbed = EnumerateAndGrab();
            if (grabbed == 0)
            {
                Logger.WriteLine("FnLockRemapper: no devices grabbed - check 'Devices to capture' in Extra Settings");
                DestroyVirtualKeyboard();
                return false;
            }

            // Self-pipe trick for clean wake-from-poll on shutdown.
            if (EvdevInterop.pipe(_stopPipe) != 0)
            {
                Logger.WriteLine("FnLockRemapper: pipe() failed");
                ReleaseAll();
                return false;
            }

            _running = true;
            _loopThread = new Thread(EventLoop) { IsBackground = true, Name = "fnlock-remap" };
            _loopThread.Start();

            Logger.WriteLine($"FnLockRemapper: active on {grabbed} device(s); FnLockOn={FnLockOn}");
            return true;
        }
    }

    /// <summary>
    /// Stop grabbing, ungrab, destroy the virtual keyboard, join the loop
    /// thread. Safe to call repeatedly.
    /// </summary>
    public void Stop()
    {
        Thread? t;
        lock (_stateLock)
        {
            if (!_running)
                return;
            _running = false;
            t = _loopThread;
            // Wake the poll
            if (_stopPipe[1] >= 0)
            {
                try
                {
                    var b = Marshal.AllocHGlobal(1);
                    Marshal.WriteByte(b, (byte)'q');
                    EvdevInterop.write(_stopPipe[1], b, 1);
                    Marshal.FreeHGlobal(b);
                }
                catch { }
            }
        }

        try
        { t?.Join(2000); }
        catch { }

        lock (_stateLock)
        {
            ReleaseAll();
        }
        Logger.WriteLine("FnLockRemapper: stopped");
    }

    public void Dispose() => Stop();

    // INTERNALS

    private sealed class GrabbedDevice
    {
        public int Fd;
        public string Name = "";
        public string Path = "";
        public ushort BusType;
    }

    private bool CreateVirtualKeyboard()
    {
        int fd = EvdevInterop.open("/dev/uinput",
            EvdevInterop.O_WRONLY | EvdevInterop.O_NONBLOCK | EvdevInterop.O_CLOEXEC);
        if (fd < 0)
        {
            Logger.WriteLine($"FnLockRemapper: open /dev/uinput failed (errno={Marshal.GetLastWin32Error()})");
            return false;
        }

        // Declare event types. EV_REP signals key-repeat support so libinput
        // and compositors classify us as a real keyboard, not a hotkey HID.
        if (EvdevInterop.ioctl_int(fd, EvdevInterop.UI_SET_EVBIT, EvdevInterop.EV_KEY) < 0
            || EvdevInterop.ioctl_int(fd, EvdevInterop.UI_SET_EVBIT, EvdevInterop.EV_SYN) < 0
            || EvdevInterop.ioctl_int(fd, EvdevInterop.UI_SET_EVBIT, EvdevInterop.EV_REP) < 0)
        {
            Logger.WriteLine("FnLockRemapper: UI_SET_EVBIT failed");
            EvdevInterop.close(fd);
            return false;
        }

        // Declare every key code we might emit. Casting a wide net is fine -
        // the kernel just records capabilities; emitting unannounced codes
        // is what fails. Walk 0..255 + the higher F13..F24 + KEY_TOUCHPAD_TOGGLE etc.
        for (uint k = 1; k < 256; k++)
            EvdevInterop.ioctl_int(fd, EvdevInterop.UI_SET_KEYBIT, (int)k);

        ushort[] extraKeys =
        {
            EvdevInterop.KEY_F13, EvdevInterop.KEY_F14, EvdevInterop.KEY_F15,
            EvdevInterop.KEY_F16, EvdevInterop.KEY_F17, EvdevInterop.KEY_F18,
            EvdevInterop.KEY_F19, EvdevInterop.KEY_F20, EvdevInterop.KEY_F21,
            EvdevInterop.KEY_F22, EvdevInterop.KEY_F23, EvdevInterop.KEY_F24,
            EvdevInterop.KEY_FN, EvdevInterop.KEY_FN_ESC,
            EvdevInterop.KEY_TOUCHPAD_TOGGLE, EvdevInterop.KEY_RFKILL,
            EvdevInterop.KEY_KBDILLUMTOGGLE, EvdevInterop.KEY_KBDILLUMUP,
            EvdevInterop.KEY_KBDILLUMDOWN, EvdevInterop.KEY_BRIGHTNESSUP,
            EvdevInterop.KEY_BRIGHTNESSDOWN, EvdevInterop.KEY_PROG1,
            EvdevInterop.KEY_PROG2, EvdevInterop.KEY_PROG3, EvdevInterop.KEY_PROG4,
        };
        foreach (var k in extraKeys)
            EvdevInterop.ioctl_int(fd, EvdevInterop.UI_SET_KEYBIT, k);

        // Build uinput_user_dev and write() it (legacy creation path).
        var udev = new EvdevInterop.UinputUserDev
        {
            name = VirtualDeviceName,
            id = new EvdevInterop.InputId
            {
                bustype = EvdevInterop.BUS_VIRTUAL,
                vendor = 0x0FAC,    // matches keyd convention
                product = 0x6770,
                version = 1,
            },
            ff_effects_max = 0,
            absmax = new int[64],
            absmin = new int[64],
            absfuzz = new int[64],
            absflat = new int[64],
        };

        int size = Marshal.SizeOf<EvdevInterop.UinputUserDev>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(udev, buf, false);
            long n = EvdevInterop.write(fd, buf, (ulong)size);
            if (n != size)
            {
                Logger.WriteLine($"FnLockRemapper: write(uinput_user_dev) returned {n}, expected {size} (errno={Marshal.GetLastWin32Error()})");
                EvdevInterop.close(fd);
                return false;
            }
        }
        finally
        {
            Marshal.DestroyStructure<EvdevInterop.UinputUserDev>(buf);
            Marshal.FreeHGlobal(buf);
        }

        if (EvdevInterop.ioctl_uint(fd, EvdevInterop.UI_DEV_CREATE, 0) < 0)
        {
            Logger.WriteLine("FnLockRemapper: UI_DEV_CREATE failed");
            EvdevInterop.close(fd);
            return false;
        }

        _uinputFd = fd;
        return true;
    }

    private void DestroyVirtualKeyboard()
    {
        if (_uinputFd < 0)
            return;
        EvdevInterop.ioctl_uint(_uinputFd, EvdevInterop.UI_DEV_DESTROY, 0);
        EvdevInterop.close(_uinputFd);
        _uinputFd = -1;
    }

    /// <summary>
    /// Walk /dev/input/event*, classify, grab integrated keyboards.
    /// </summary>
    private int EnumerateAndGrab()
    {
        if (!Directory.Exists("/dev/input"))
            return 0;

        foreach (string path in Directory.EnumerateFiles("/dev/input", "event*"))
        {
            try
            {
                if (TryGrabDevice(path, out var grabbed) && grabbed != null)
                    _devices.Add(grabbed);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"FnLockRemapper: error on {path}: {ex.Message}");
            }
        }
        return _devices.Count;
    }

    private bool TryGrabDevice(string path, out GrabbedDevice? device)
    {
        device = null;

        int fd = EvdevInterop.open(path, EvdevInterop.O_RDWR | EvdevInterop.O_NONBLOCK | EvdevInterop.O_CLOEXEC);
        if (fd < 0)
            return false;

        try
        {
            // Read device id + name to drive selection.
            if (!ReadInputId(fd, out var info))
            {
                EvdevInterop.close(fd);
                return false;
            }

            // Hard exclude: our own virtual device (vendor 0x0FAC, product 0x6770).
            // We deliberately keep keyd's virtual devices (also 0x0FAC) eligible
            // for capture in case the user wants to chain through them.
            if (info.vendor == 0x0FAC && info.product == 0x6770)
            {
                EvdevInterop.close(fd);
                return false;
            }

            string name = ReadDeviceName(fd);
            bool hasKbd = HasAnyKeyboardKey(fd);
            if (!hasKbd)
            {
                // Plain mice, joysticks, audio jacks etc. - not capturable.
                EvdevInterop.close(fd);
                return false;
            }

            // User-configured per-device choice. Stable across reboots: keyed
            // by vendor:product:bus rather than evdev path which can shuffle.
            int choice = AppConfig.Get(MakeDeviceKey(info), -1);
            bool grab;
            if (choice == 1)
            {
                grab = true;
            }
            else if (choice == 0)
            {
                grab = false;
            }
            else
            {
                // No explicit choice yet → default heuristic:
                // capture if (bus=internal) OR (vendor=ASUS 0x0B05) OR (name has ASUS).
                grab = LooksIntegrated(info, name);
            }

            if (!grab)
            {
                EvdevInterop.close(fd);
                return false;
            }

            // Exclusive grab.
            if (EvdevInterop.ioctl_int(fd, EvdevInterop.EVIOCGRAB, 1) < 0)
            {
                Logger.WriteLine($"FnLockRemapper: EVIOCGRAB failed on {path} ({name}) - already grabbed by another process");
                EvdevInterop.close(fd);
                return false;
            }

            device = new GrabbedDevice
            {
                Fd = fd,
                Path = path,
                Name = name,
                BusType = info.bustype,
            };
            Logger.WriteLine($"FnLockRemapper: grabbed {path} '{name}' bus=0x{info.bustype:x} vid:pid={info.vendor:x4}:{info.product:x4}");
            return true;
        }
        catch
        {
            EvdevInterop.close(fd);
            return false;
        }
    }

    private static bool ReadInputId(int fd, out EvdevInterop.InputId info)
    {
        info = default;
        IntPtr idBuf = Marshal.AllocHGlobal(Marshal.SizeOf<EvdevInterop.InputId>());
        try
        {
            if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGID, idBuf) < 0)
                return false;
            info = Marshal.PtrToStructure<EvdevInterop.InputId>(idBuf);
            return true;
        }
        finally { Marshal.FreeHGlobal(idBuf); }
    }

    /// <summary>
    /// True if the device exposes ANY keyboard-ish key (F-keys, modifiers, or
    /// alphanumerics). Filters out pure mice/joysticks/audio-jack hotplugs.
    /// </summary>
    private static bool HasAnyKeyboardKey(int fd)
    {
        ushort[] markers =
        {
            EvdevInterop.KEY_F1, EvdevInterop.KEY_F12, EvdevInterop.KEY_LEFTMETA,
            30 /* KEY_A */, 28 /* KEY_ENTER */, EvdevInterop.KEY_ESC,
        };
        foreach (var k in markers)
            if (HasKey(fd, k))
                return true;
        return false;
    }

    /// <summary>
    /// Heuristic for the default-tick state in the device picker. Considers
    /// internal-bus devices (i8042, i2c, hil, host) AND laptop-vendor devices
    /// (ASUS 0x0B05, Lenovo 0x17EF) integrated. Everything else (USB Logitech,
    /// Bluetooth) defaults off until the user explicitly opts in.
    /// </summary>
    private static bool LooksIntegrated(EvdevInterop.InputId info, string name)
    {
        if (info.bustype == EvdevInterop.BUS_I8042
            || info.bustype == EvdevInterop.BUS_I2C
            || info.bustype == EvdevInterop.BUS_HIL
            || info.bustype == EvdevInterop.BUS_HOST)
            return true;
        if (info.vendor == 0x0B05) // ASUSTeK
            return true;
        if (info.vendor == 0x17EF) // Lenovo
            return true;
        if (name.Contains("N-KEY", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Stable per-device config key. Format: fnlock_capture_VVVV_PPPP_BB
    /// where VVVV/PPPP/BB are zero-padded hex.
    /// </summary>
    internal static string MakeDeviceKey(EvdevInterop.InputId info)
        => $"fnlock_capture_{info.vendor:x4}_{info.product:x4}_{info.bustype:x}";

    private void RebuildInterestSet()
    {
        var set = new HashSet<ushort>();
        foreach (ushort k in EvdevInterop.FunctionKeys)
            set.Add(k);
        set.Add(_toggleModifier);
        set.Add(_toggleKey);
        // Add only keycode targets to the debug interest set; action targets
        // dispatch via App and don't have a destination keycode worth logging.
        foreach (var v in _activeMap.Values)
            if (v.IsKey)
                set.Add(v.KeyCode!.Value);
        // Bridge keys: ASUS-specific hotkeys we forward to App so user-bound
        // actions (toggle g-helper, cycle aura, etc.) keep working when
        // fn-lock has exclusively grabbed event6/event7.
        set.Add(EvdevInterop.KEY_PROG1);          // M5/ROG button
        set.Add(EvdevInterop.KEY_PROG2);
        set.Add(EvdevInterop.KEY_PROG3);          // Fn+F4 Aura
        set.Add(EvdevInterop.KEY_PROG4);          // Fn+F5 Performance
        set.Add(EvdevInterop.KEY_KBDILLUMUP);     // Fn+F3 keyboard brightness up
        set.Add(EvdevInterop.KEY_KBDILLUMDOWN);   // Fn+F2 keyboard brightness down
        _interestSet = set;
    }

    /// <summary>
    /// Log a one-time warning if the keyd daemon is running. Empty-config keyd
    /// coexists fine; only matters when keyd has overlapping rules.
    /// </summary>
    private static void DetectKeydCoexistence()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                string baseName = Path.GetFileName(dir);
                if (!int.TryParse(baseName, out _))
                    continue;
                try
                {
                    string commPath = Path.Combine(dir, "comm");
                    if (!File.Exists(commPath))
                        continue;
                    string comm = File.ReadAllText(commPath).Trim();
                    if (comm.StartsWith("keyd", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.WriteLine("FnLockRemapper: keyd daemon detected (PID " + baseName + "). Both can coexist when keyd has no overlapping bindings; if remap is unreliable, stop keyd: sudo systemctl stop keyd");
                        return;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Information about a candidate input device, returned by
    /// <see cref="EnumerateCandidates"/>. The UI uses this to populate a
    /// device picker; the user ticks which devices the remapper should grab.
    /// </summary>
    public sealed class DeviceCandidate
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public ushort Vendor { get; init; }
        public ushort Product { get; init; }
        public ushort BusType { get; init; }
        public bool HasFKeys { get; init; }
        public bool LooksIntegrated { get; init; }
        public bool IsOurOwn { get; init; }
        public bool IsKeydVirtual { get; init; }
        public string ConfigKey { get; init; } = "";

        public string BusName => BusType switch
        {
            EvdevInterop.BUS_I8042 => "i8042",
            EvdevInterop.BUS_I2C => "i2c",
            EvdevInterop.BUS_USB => "usb",
            EvdevInterop.BUS_BLUETOOTH => "bt",
            EvdevInterop.BUS_HIL => "hil",
            EvdevInterop.BUS_VIRTUAL => "virtual",
            EvdevInterop.BUS_HOST => "host",
            _ => $"0x{BusType:x}",
        };
    }

    /// <summary>
    /// Enumerate /dev/input/event* and return all keyboard-capable devices
    /// for display in the UI. Cheap; opens each fd briefly. Does not grab.
    /// </summary>
    public static List<DeviceCandidate> EnumerateCandidates()
    {
        var list = new List<DeviceCandidate>();
        if (!Directory.Exists("/dev/input"))
            return list;

        foreach (string path in Directory.EnumerateFiles("/dev/input", "event*"))
        {
            int fd = EvdevInterop.open(path, EvdevInterop.O_RDONLY | EvdevInterop.O_NONBLOCK | EvdevInterop.O_CLOEXEC);
            if (fd < 0)
                continue;
            try
            {
                if (!ReadInputId(fd, out var info))
                    continue;
                string name = ReadDeviceName(fd);
                if (!HasAnyKeyboardKey(fd))
                    continue;

                bool isOwn = info.vendor == 0x0FAC && info.product == 0x6770;
                bool isKeyd = info.vendor == 0x0FAC && !isOwn;

                list.Add(new DeviceCandidate
                {
                    Path = path,
                    Name = name,
                    Vendor = info.vendor,
                    Product = info.product,
                    BusType = info.bustype,
                    HasFKeys = HasKey(fd, EvdevInterop.KEY_F1) && HasKey(fd, EvdevInterop.KEY_F12),
                    LooksIntegrated = LooksIntegrated(info, name),
                    IsOurOwn = isOwn,
                    IsKeydVirtual = isKeyd,
                    ConfigKey = MakeDeviceKey(info),
                });
            }
            finally { EvdevInterop.close(fd); }
        }

        return list;
    }

    private static string ReadDeviceName(int fd)
    {
        const int len = 256;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            for (int i = 0; i < len; i++)
                Marshal.WriteByte(buf, i, 0);
            if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGNAME(len), buf) < 0)
                return "";
            return Marshal.PtrToStringAnsi(buf) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static bool HasKey(int fd, ushort code)
    {
        // EVIOCGBIT(EV_KEY, len) returns a bitmask of supported KEY_* codes.
        const int len = (EvdevInterop.KEY_MAX / 8) + 1;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            for (int i = 0; i < len; i++)
                Marshal.WriteByte(buf, i, 0);
            int rc = EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGBIT(EvdevInterop.EV_KEY, len), buf);
            if (rc < 0)
                return false;

            int byteIdx = code / 8;
            int bit = code % 8;
            if (byteIdx >= len)
                return false;
            byte b = Marshal.ReadByte(buf, byteIdx);
            return (b & (1 << bit)) != 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void EventLoop()
    {
        // Build pollfd array once; rebuild only on hot-plug (deferred).
        var poll = BuildPollSet();

        IntPtr eventBuf = Marshal.AllocHGlobal(EvdevInterop.InputEventSize);
        try
        {
            while (_running)
            {
                int rc = EvdevInterop.poll(poll, (ulong)poll.Length, PollTimeoutMs);
                if (rc < 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 4 /* EINTR */)
                        continue;
                    Logger.WriteLine($"FnLockRemapper: poll() failed errno={err}");
                    break;
                }
                if (rc == 0)
                    continue;

                // Last entry is the stop pipe.
                if ((poll[poll.Length - 1].revents & EvdevInterop.POLLIN) != 0)
                    break;

                for (int i = 0; i < poll.Length - 1; i++)
                {
                    if ((poll[i].revents & EvdevInterop.POLLIN) == 0)
                        continue;
                    DrainDeviceFd(poll[i].fd, eventBuf);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(eventBuf);
        }
    }

    private EvdevInterop.Pollfd[] BuildPollSet()
    {
        lock (_stateLock)
        {
            int n = _devices.Count + 1; // +1 for stop pipe
            var arr = new EvdevInterop.Pollfd[n];
            for (int i = 0; i < _devices.Count; i++)
            {
                arr[i].fd = _devices[i].Fd;
                arr[i].events = EvdevInterop.POLLIN;
            }
            arr[n - 1].fd = _stopPipe[0];
            arr[n - 1].events = EvdevInterop.POLLIN;
            return arr;
        }
    }

    private void DrainDeviceFd(int fd, IntPtr eventBuf)
    {
        // Read may return one or more concatenated input_event records.
        // Loop until EAGAIN.
        while (true)
        {
            long n = EvdevInterop.read(fd, eventBuf, (ulong)EvdevInterop.InputEventSize);
            if (n < 0)
            {
                // EAGAIN = no more events right now.
                return;
            }
            if (n != EvdevInterop.InputEventSize)
                return;

            var ev = Marshal.PtrToStructure<EvdevInterop.InputEvent>(eventBuf);
            HandleEvent(fd, ev);
        }
    }

    private void HandleEvent(int fd, EvdevInterop.InputEvent ev)
    {
        if (ev.type == EvdevInterop.EV_SYN)
        {
            EmitEvent(ev);
            return;
        }

        // Cache MSC_SCAN values per-device. The kernel emits EV_MSC/MSC_SCAN
        // immediately before the paired EV_KEY in the same SYN frame so we
        // can consult it when deciding whether the EV_KEY is an ASUS hotkey.
        // We pass the MSC event through to the virtual keyboard unchanged;
        // a stray MSC without a matching KEY is harmless to consumers.
        if (ev.type == EvdevInterop.EV_MSC)
        {
            if (ev.code == EvdevInterop.MSC_SCAN)
                _lastScanByFd[fd] = ev.value;
            EmitEvent(ev);
            return;
        }

        if (ev.type != EvdevInterop.EV_KEY)
        {
            EmitEvent(ev);
            return;
        }

        // ASUS-hotkey bridge: when fn-lock has exclusively grabbed event6
        // (N-KEY) or event7 (WMI hotkeys), the M5/Fn+F4/Fn+F5 + brightness
        // events stop reaching LinuxAsusWmi. Detect them here, suppress the
        // passthrough (these aren't supposed to reach apps), and dispatch
        // the same action via InputDispatcher.RaiseKeyBindingFromFnLock /
        // InputDispatcher.RaiseHotkeyFromFnLock so user-configured bindings still fire.
        int cachedScan = _lastScanByFd.TryGetValue(fd, out var sc) ? sc : 0;
        string bindingName = LinuxAsusWmi.MapLinuxKeyToBindingName(ev.code);
        if (string.IsNullOrEmpty(bindingName) && cachedScan != 0)
            bindingName = LinuxAsusWmi.MapScanCodeToBindingName(cachedScan);
        if (!string.IsNullOrEmpty(bindingName))
        {
            if (ev.value == 1) // press only; release/repeat consumed silently
            {
                DebugLogIfInterest(ev, $"bridge-binding({bindingName})", ev.code);
                InputDispatcher.RaiseKeyBindingFromFnLock(bindingName);
            }
            // Always suppress; clear cached scan so it isn't reused.
            _lastScanByFd.Remove(fd);
            return;
        }

        // Legacy non-configurable hotkey bridge (brightness keys).
        int legacyEvent = LinuxAsusWmi.MapLinuxKeyToLegacyEvent(ev.code);
        if (legacyEvent < 0 && cachedScan != 0)
            legacyEvent = LinuxAsusWmi.MapScanCodeToLegacyEvent(cachedScan);
        if (legacyEvent > 0)
        {
            if (ev.value == 1)
            {
                DebugLogIfInterest(ev, $"bridge-hotkey({legacyEvent})", ev.code);
                InputDispatcher.RaiseHotkeyFromFnLock(legacyEvent);
            }
            _lastScanByFd.Remove(fd);
            return;
        }

        // Track modifier state for the toggle hotkey.
        if (ev.code == _toggleModifier)
        {
            _modifierDown = ev.value != 0;
            DebugLogIfInterest(ev, "toggle-mod", ev.code);
            EmitEvent(ev);
            return;
        }

        // Toggle combo: modifier-down + toggle-key-press.
        if (_modifierDown && ev.code == _toggleKey)
        {
            if (ev.value == 1) // press
            {
                FnLockOn = !FnLockOn;
                _toggleSuppressed = true;
                Logger.WriteLine($"FnLockRemapper: toggle hotkey -> FnLockOn={FnLockOn}");
                EmitSyn();
                return;
            }
            if (ev.value == 0 && _toggleSuppressed) // release of the eaten press
            {
                _toggleSuppressed = false;
                EmitSyn();
                return;
            }
            if (ev.value == 2 && _toggleSuppressed) // autorepeat
            {
                EmitSyn();
                return;
            }
        }

        // F1..F12 remap when fnlock is ON (= "media keys active on top row" -
        // matches Windows g-helper convention; opposite of older Linux builds).
        if (_fnLockOn && _activeMap.TryGetValue(ev.code, out var target))
        {
            if (target.IsKey)
            {
                // Keycode target: rewrite the event in-place and emit it
                // through the virtual keyboard so apps receive the mapped key.
                var rewritten = ev;
                rewritten.code = target.KeyCode!.Value;
                DebugLogIfInterest(ev, "remap", target.KeyCode!.Value);
                EmitEvent(rewritten);
            }
            else if (target.IsAction)
            {
                // Action target: dispatch into the g-helper action handler
                // on press only. Press, release and autorepeat are all
                // suppressed from the virtual keyboard so apps don't see
                // a stray F-key event.
                if (ev.value == 1)
                {
                    DebugLogIfInterest(ev, $"remap-action({target.Action})", ev.code);
                    InputDispatcher.RaiseActionFromFnLock(target.Action!);
                }
            }
            // KEY consumed; clear scan cache so it doesn't bleed into the next event.
            _lastScanByFd.Remove(fd);
            return;
        }

        // Default: pass through unchanged.
        DebugLogIfInterest(ev, "passthrough", ev.code);
        EmitEvent(ev);
        _lastScanByFd.Remove(fd);
    }

    /// <summary>
    /// Conditionally log an event when (a) debug mode is on AND (b) the event
    /// code is in our interest set: F1..F12, the configured toggle keys, or
    /// any remap target. This intentionally avoids logging alphanumeric keys
    /// to keep the log non-keylogger.
    /// </summary>
    private void DebugLogIfInterest(EvdevInterop.InputEvent ev, string action, ushort outCode)
    {
        if (!_debug)
            return;
        if (ev.value == 2)
            return; // suppress autorepeat noise
        if (!_interestSet.Contains(ev.code) && !_interestSet.Contains(outCode))
            return;
        string val = ev.value == 1 ? "down" : ev.value == 0 ? "up" : ev.value.ToString();
        Logger.WriteLine($"fnlock: {action} code={ev.code}->{outCode} {val} fnLockOn={_fnLockOn}");
    }

    private void EmitEvent(EvdevInterop.InputEvent ev)
    {
        if (_uinputFd < 0)
            return;
        IntPtr buf = Marshal.AllocHGlobal(EvdevInterop.InputEventSize);
        try
        {
            // Zero out timestamps; the kernel will fill them.
            ev.tv_sec = 0;
            ev.tv_usec = 0;
            Marshal.StructureToPtr(ev, buf, false);
            EvdevInterop.write(_uinputFd, buf, (ulong)EvdevInterop.InputEventSize);

            // After every key event, send SYN_REPORT - the kernel input core
            // batches by SYN, and consumers expect frame boundaries.
            if (ev.type == EvdevInterop.EV_KEY)
                EmitSyn();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void EmitSyn()
    {
        if (_uinputFd < 0)
            return;
        var syn = new EvdevInterop.InputEvent
        {
            type = EvdevInterop.EV_SYN,
            code = EvdevInterop.SYN_REPORT,
            value = 0,
        };
        IntPtr buf = Marshal.AllocHGlobal(EvdevInterop.InputEventSize);
        try
        {
            Marshal.StructureToPtr(syn, buf, false);
            EvdevInterop.write(_uinputFd, buf, (ulong)EvdevInterop.InputEventSize);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void ReleaseAll()
    {
        foreach (var d in _devices)
        {
            try
            {
                EvdevInterop.ioctl_int(d.Fd, EvdevInterop.EVIOCGRAB, 0);
                EvdevInterop.close(d.Fd);
            }
            catch { }
        }
        _devices.Clear();
        _lastScanByFd.Clear();

        DestroyVirtualKeyboard();

        if (_stopPipe[0] >= 0)
        { try { EvdevInterop.close(_stopPipe[0]); } catch { } _stopPipe[0] = -1; }
        if (_stopPipe[1] >= 0)
        { try { EvdevInterop.close(_stopPipe[1]); } catch { } _stopPipe[1] = -1; }
    }
}
