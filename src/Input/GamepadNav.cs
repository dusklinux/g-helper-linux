using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.Input;

/// <summary>
/// Controller-driven focus navigation, modeled on Heroic's gamepad handler
/// (frontend/helpers/gamepad.ts): d-pad / left stick move focus spatially,
/// A activates the focused control, B closes the current child window /
/// dropdown, X toggles the on-screen keyboard, right stick scrolls.
///
/// Input comes straight from evdev gamepads (including the virtual X360 pad
/// Steam Input exposes in game mode), so it works in gamescope where a
/// non-Steam app otherwise gets no usable pointer. Devices are never
/// grabbed; events are only acted on while a G-Helper window has focus
/// (or unconditionally in game mode, where we are the only app).
///
/// Disable with config gamepad_nav=0.
/// </summary>
public static class GamepadNav
{
    private const int RescanMs = 5000;
    private const int InitialRepeatMs = 400;
    private const int RepeatMs = 170;
    private const float StickThreshold = 0.5f;

    private enum NavAction { Up, Down, Left, Right, Confirm, Back, Osk, ScrollUp, ScrollDown }

    private sealed class Pad
    {
        public int Fd = -1;
        public string Path = "";
        public string Name = "";
        // Normalization ranges for the left stick and right stick Y.
        public EvdevInterop.InputAbsInfo AbsX, AbsY, AbsRy;
        // Current direction state (from hat + stick combined) and repeat clock.
        public int DirX, DirY;
        public long NextRepeatAt;
        public float StickX, StickY, StickRy;
        public int HatX, HatY;
    }

    private static Thread? _thread;
    private static volatile bool _running;
    private static readonly List<Pad> _pads = new();
    private static readonly object _lock = new();

    // Set = arcade grabs raw input; focus-nav suspended.
    private static volatile IGamepadInput? _capture;

    public static void Capture(IGamepadInput target) => _capture = target;

    public static void ReleaseCapture(IGamepadInput target)
    {
        if (ReferenceEquals(_capture, target))
            _capture = null;
    }

    public static void Start()
    {
        if (AppConfig.Is("gamepad_nav_off"))
        {
            Logger.WriteLine("GamepadNav: disabled by config");
            return;
        }
        // Default-on only where a controller is the primary input. Desktops
        // opt in with gamepad_nav=1 (a permanently connected pad would
        // otherwise move focus while gaming).
        bool defaultOn = AppConfig.IsHandheldDevice()
            || Platform.Linux.ImmutableOs.IsSteamOs
            || Platform.Linux.SteamShortcuts.IsSteamDeckGameMode;
        if (!defaultOn && !AppConfig.Is("gamepad_nav"))
        {
            Logger.WriteLine("GamepadNav: off (not a handheld; set gamepad_nav=1 to enable)");
            return;
        }
        if (_running)
            return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ghelper-gamepad-nav" };
        _thread.Start();
    }

    public static void Stop()
    {
        _running = false;
        lock (_lock)
        {
            foreach (var p in _pads)
                EvdevInterop.close(p.Fd);
            _pads.Clear();
        }
    }

    // Device discovery

    private static void Loop()
    {
        long nextScan = 0;
        var buf = Marshal.AllocHGlobal(EvdevInterop.InputEventSize * 64);
        try
        {
            while (_running)
            {
                long now = Environment.TickCount64;
                if (now >= nextScan)
                {
                    Rescan();
                    nextScan = now + RescanMs;
                }

                EvdevInterop.Pollfd[] fds;
                lock (_lock)
                    fds = _pads.Select(p => new EvdevInterop.Pollfd
                    { fd = p.Fd, events = EvdevInterop.POLLIN }).ToArray();

                if (fds.Length == 0)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                int rc = EvdevInterop.poll(fds, (ulong)fds.Length, 200);
                if (rc < 0)
                {
                    Thread.Sleep(200);
                    continue;
                }

                lock (_lock)
                {
                    for (int i = _pads.Count - 1; i >= 0; i--)
                    {
                        var p = _pads[i];
                        var revents = fds.FirstOrDefault(f => f.fd == p.Fd).revents;
                        if ((revents & (EvdevInterop.POLLERR | EvdevInterop.POLLHUP | EvdevInterop.POLLNVAL)) != 0)
                        {
                            EvdevInterop.close(p.Fd);
                            _pads.RemoveAt(i);
                            Logger.WriteLine($"GamepadNav: lost {p.Name}");
                            continue;
                        }
                        if ((revents & EvdevInterop.POLLIN) != 0)
                            Drain(p, buf);
                    }

                    // Held-direction repeat, independent of new events. Skipped
                    // while captured (arcade holds its own state).
                    long t = Environment.TickCount64;
                    if (_capture == null)
                        foreach (var p in _pads)
                        {
                            if ((p.DirX != 0 || p.DirY != 0) && t >= p.NextRepeatAt)
                            {
                                p.NextRepeatAt = t + RepeatMs;
                                FireDirection(p);
                            }
                        }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void Rescan()
    {
        string[] nodes;
        try
        {
            nodes = Directory.GetFiles("/dev/input", "event*");
        }
        catch
        {
            return;
        }

        lock (_lock)
        {
            foreach (var node in nodes)
            {
                if (_pads.Any(p => p.Path == node))
                    continue;
                var pad = TryOpenGamepad(node);
                if (pad != null)
                {
                    _pads.Add(pad);
                    Logger.WriteLine($"GamepadNav: using {pad.Name} ({node})");
                }
            }
        }
    }

    /// <summary>Open a node read-only and keep it when it looks like a
    /// gamepad: BTN_SOUTH plus an ABS_X axis. Requiring both rules out
    /// virtual keyboards that lazily declare every key bit (RustDesk, keyd).
    /// Our own virtual devices are skipped by name.</summary>
    private static Pad? TryOpenGamepad(string node)
    {
        int fd = EvdevInterop.open(node, EvdevInterop.O_RDONLY | EvdevInterop.O_NONBLOCK | EvdevInterop.O_CLOEXEC);
        if (fd < 0)
            return null;

        try
        {
            string name = GetName(fd);
            if (name.StartsWith("G-Helper"))
            { EvdevInterop.close(fd); return null; }

            if (!HasKeyBit(fd, EvdevInterop.BTN_SOUTH) || !HasAbsBit(fd, EvdevInterop.ABS_X))
            { EvdevInterop.close(fd); return null; }

            var pad = new Pad { Fd = fd, Path = node, Name = name };
            pad.AbsX = ReadAbs(fd, EvdevInterop.ABS_X);
            pad.AbsY = ReadAbs(fd, EvdevInterop.ABS_Y);
            pad.AbsRy = ReadAbs(fd, EvdevInterop.ABS_RY);
            return pad;
        }
        catch
        {
            EvdevInterop.close(fd);
            return null;
        }
    }

    private static string GetName(int fd)
    {
        var buf = Marshal.AllocHGlobal(256);
        try
        {
            if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGNAME(256), buf) < 0)
                return "";
            return Marshal.PtrToStringAnsi(buf) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static bool HasKeyBit(int fd, ushort code)
        => HasEvBit(fd, EvdevInterop.EV_KEY, code, EvdevInterop.KEY_MAX);

    private static bool HasAbsBit(int fd, ushort code)
        => HasEvBit(fd, EvdevInterop.EV_ABS, code, 0x3f /* ABS_MAX */);

    private static bool HasEvBit(int fd, ushort ev, ushort code, int max)
    {
        int bytes = (max / 8) + 1;
        var buf = Marshal.AllocHGlobal(bytes);
        try
        {
            if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGBIT(ev, (uint)bytes), buf) < 0)
                return false;
            byte b = Marshal.ReadByte(buf, code / 8);
            return (b & (1 << (code % 8))) != 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static EvdevInterop.InputAbsInfo ReadAbs(int fd, ushort axis)
    {
        var buf = Marshal.AllocHGlobal(Marshal.SizeOf<EvdevInterop.InputAbsInfo>());
        try
        {
            if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGABS(axis), buf) < 0)
                return default;
            return Marshal.PtrToStructure<EvdevInterop.InputAbsInfo>(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // Event handling

    private static void Drain(Pad p, IntPtr buf)
    {
        while (true)
        {
            long n = EvdevInterop.read(p.Fd, buf, (ulong)(EvdevInterop.InputEventSize * 64));
            if (n < EvdevInterop.InputEventSize)
                return;
            for (long off = 0; off + EvdevInterop.InputEventSize <= n; off += EvdevInterop.InputEventSize)
            {
                var ev = Marshal.PtrToStructure<EvdevInterop.InputEvent>(buf + (int)off);
                Handle(p, ev);
            }
        }
    }

    private static void Handle(Pad p, EvdevInterop.InputEvent ev)
    {
        if (ev.type == EvdevInterop.EV_KEY && (ev.value == 0 || ev.value == 1))
        {
            bool pressed = ev.value == 1;

            // Captured: forward press+release; no nav mapping.
            if (_capture != null)
            {
                GamepadInputButton? b = ev.code switch
                {
                    EvdevInterop.BTN_SOUTH => GamepadInputButton.South,
                    EvdevInterop.BTN_EAST => GamepadInputButton.East,
                    EvdevInterop.BTN_NORTH => GamepadInputButton.North,
                    EvdevInterop.BTN_WEST => GamepadInputButton.West,
                    _ => null,
                };
                if (b is { } btn)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _capture?.GamepadButton(btn, pressed));
                return;
            }

            if (!pressed)
                return;
            switch (ev.code)
            {
                case EvdevInterop.BTN_SOUTH:
                    Post(NavAction.Confirm);
                    return;
                case EvdevInterop.BTN_EAST:
                    Post(NavAction.Back);
                    return;
                case EvdevInterop.BTN_NORTH:
                case EvdevInterop.BTN_WEST:
                    Post(NavAction.Osk);
                    return;
            }
            return;
        }

        if (ev.type != EvdevInterop.EV_ABS)
            return;

        switch (ev.code)
        {
            case EvdevInterop.ABS_HAT0X:
                p.HatX = Math.Sign(ev.value);
                break;
            case EvdevInterop.ABS_HAT0Y:
                p.HatY = Math.Sign(ev.value);
                break;
            case EvdevInterop.ABS_X:
                p.StickX = Normalize(ev.value, p.AbsX);
                break;
            case EvdevInterop.ABS_Y:
                p.StickY = Normalize(ev.value, p.AbsY);
                break;
            case EvdevInterop.ABS_RY:
                p.StickRy = Normalize(ev.value, p.AbsRy);
                break;
            default:
                return;
        }

        // Right stick: scroll while deflected (repeat comes from the event
        // stream itself; sticks report continuously while held). Not captured.
        if (ev.code == EvdevInterop.ABS_RY && Math.Abs(p.StickRy) > StickThreshold)
        {
            if (_capture == null)
                Post(p.StickRy > 0 ? NavAction.ScrollDown : NavAction.ScrollUp);
            return;
        }

        int dirX = p.HatX != 0 ? p.HatX : (Math.Abs(p.StickX) > StickThreshold ? Math.Sign(p.StickX) : 0);
        int dirY = p.HatY != 0 ? p.HatY : (Math.Abs(p.StickY) > StickThreshold ? Math.Sign(p.StickY) : 0);

        if (dirX == p.DirX && dirY == p.DirY)
            return;

        p.DirX = dirX;
        p.DirY = dirY;

        // Captured: forward held state; no repeat (game polls each tick).
        if (_capture != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _capture?.GamepadDirection(dirX, dirY));
            return;
        }

        if (dirX != 0 || dirY != 0)
        {
            p.NextRepeatAt = Environment.TickCount64 + InitialRepeatMs;
            FireDirection(p);
        }
    }

    private static float Normalize(int value, EvdevInterop.InputAbsInfo abs)
    {
        if (abs.maximum <= abs.minimum)
            return 0;
        float mid = (abs.maximum + abs.minimum) / 2f;
        float half = (abs.maximum - abs.minimum) / 2f;
        return Math.Clamp((value - mid) / half, -1f, 1f);
    }

    private static void FireDirection(Pad p)
    {
        // Dominant axis wins; ties prefer vertical (lists are vertical).
        if (p.DirY != 0)
            Post(p.DirY < 0 ? NavAction.Up : NavAction.Down);
        else if (p.DirX != 0)
            Post(p.DirX < 0 ? NavAction.Left : NavAction.Right);
    }

    private static void Post(NavAction action)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => Navigate(action));

    // UI-thread navigation

    private static Window? ActiveWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        // 1. The X11-active window when it is one of ours. gamescope does
        //    not always set this; other launchers may steal it briefly.
        var active = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (active != null)
            return active;

        // 2. Topmost visible child window. gamescope renders children as
        //    overlays but never marks them IsActive, so without this Back
        //    lands on the main window and open children cannot be closed.
        var child = desktop.Windows.LastOrDefault(w =>
            w.IsVisible
            && !ReferenceEquals(w, App.MainWindowInstance)
            && w is not UI.Views.OnScreenKeyboardWindow);
        if (child != null)
            return child;

        // 3. Fall back to the main window if it is visible. The OSK is
        //    excluded from navigation targets: it is a sibling overlay,
        //    not the window the user is driving with the controller.
        var main = App.MainWindowInstance;
        if (main != null && main.IsVisible)
            return main;
        return null;
    }

    private static void Navigate(NavAction action)
    {
        var win = ActiveWindow();
        if (win == null)
            return;

        switch (action)
        {
            case NavAction.Confirm:
                Confirm(win);
                return;
            case NavAction.Back:
                Back(win);
                return;
            case NavAction.Osk:
                // Game mode has Steam's own overlay keyboard; ours is for
                // desktop sessions only.
                if (!Platform.Linux.SteamShortcuts.IsSteamDeckGameMode)
                    App.ToggleOskWindow();
                return;
            case NavAction.ScrollUp:
            case NavAction.ScrollDown:
                Scroll(win, action == NavAction.ScrollDown ? 1 : -1);
                return;
        }

        var focused = win.FocusManager?.GetFocusedElement() as Control;

        // Focused controls that consume horizontal/vertical arrows keep them.
        if (focused != null && ForwardArrowToControl(focused, action))
            return;

        MoveFocus(win, focused, action);
    }

    /// <summary>Sliders take left/right (value change), open dropdowns and
    /// list boxes take up/down (item change). Everything else navigates.</summary>
    private static bool ForwardArrowToControl(Control focused, NavAction action)
    {
        bool horizontal = action is NavAction.Left or NavAction.Right;
        bool vertical = action is NavAction.Up or NavAction.Down;

        bool wants = focused switch
        {
            Slider s => (s.Orientation == Avalonia.Layout.Orientation.Horizontal) == horizontal,
            ComboBox cb => cb.IsDropDownOpen && vertical,
            ComboBoxItem => vertical,
            ListBoxItem => vertical,
            _ => false,
        };
        if (!wants)
            return false;

        SendKey(focused, action switch
        {
            NavAction.Left => Key.Left,
            NavAction.Right => Key.Right,
            NavAction.Up => Key.Up,
            _ => Key.Down,
        });
        return true;
    }

    private static void Confirm(Window win)
    {
        var focused = win.FocusManager?.GetFocusedElement() as Control;
        if (focused == null)
        {
            MoveFocus(win, null, NavAction.Down);
            return;
        }

        // Text inputs: pull up the on-screen keyboard, like Heroic focusing
        // its virtual keyboard on text fields. Not in game mode (Steam has
        // its own overlay keyboard there).
        if (focused is TextBox)
        {
            if (!Platform.Linux.SteamShortcuts.IsSteamDeckGameMode
                && (App.OskWindowInstance == null || !App.OskWindowInstance.IsVisible))
                App.ToggleOskWindow();
            return;
        }

        // Buttons, toggles, checkboxes, combo boxes all activate on Enter.
        SendKey(focused, Key.Enter);
    }

    private static void Back(Window win)
    {
        var focused = win.FocusManager?.GetFocusedElement() as Control;

        // Close open dropdowns first.
        if (focused is ComboBox { IsDropDownOpen: true } || focused is ComboBoxItem)
        {
            SendKey(focused, Key.Escape);
            return;
        }

        // Child windows close; the main window stays (it is the whole app
        // in game mode).
        if (win is not UI.Views.MainWindow && win is not UI.Views.OnScreenKeyboardWindow)
        {
            win.Close();
            return;
        }

        if (focused != null)
            SendKey(focused, Key.Escape);
    }

    private static void Scroll(Window win, int direction)
    {
        var focused = win.FocusManager?.GetFocusedElement() as Control;
        var viewer = (focused ?? win.Content as Control)?
            .FindAncestorOfType<ScrollViewer>(includeSelf: true)
            ?? (win.Content as Control)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer == null)
            return;
        viewer.Offset = viewer.Offset.WithY(
            Math.Max(0, viewer.Offset.Y + direction * 60));
    }

    // Spatial focus movement

    private static void MoveFocus(Window win, Control? focused, NavAction action)
    {
        var candidates = Candidates(win);
        if (candidates.Count == 0)
            return;

        if (focused == null || !focused.IsEffectivelyVisible)
        {
            FocusControl(candidates.OrderBy(c => c.Rect.Y).ThenBy(c => c.Rect.X).First().Control);
            return;
        }

        var from = BoundsIn(win, focused);
        if (from == null)
        {
            FocusControl(candidates[0].Control);
            return;
        }

        var fc = from.Value.Center;
        (Control Control, Avalonia.Rect Rect)? best = null;
        double bestScore = double.MaxValue;

        foreach (var cand in candidates)
        {
            if (ReferenceEquals(cand.Control, focused))
                continue;
            var cc = cand.Rect.Center;
            double dx = cc.X - fc.X, dy = cc.Y - fc.Y;

            double primary, ortho;
            switch (action)
            {
                case NavAction.Up:
                    primary = -dy;
                    ortho = Math.Abs(dx);
                    break;
                case NavAction.Down:
                    primary = dy;
                    ortho = Math.Abs(dx);
                    break;
                case NavAction.Left:
                    primary = -dx;
                    ortho = Math.Abs(dy);
                    break;
                default:
                    primary = dx;
                    ortho = Math.Abs(dy);
                    break;
            }
            if (primary < 4)
                continue; // not in that direction

            double score = primary + ortho * 2.5;
            if (score < bestScore)
            {
                bestScore = score;
                best = cand;
            }
        }

        if (best != null)
        {
            FocusControl(best.Value.Control);
            EnsureVisible(best.Value.Control);
        }
    }

    private static void FocusControl(Control c)
        => c.Focus(NavigationMethod.Directional);

    private static void EnsureVisible(Control c)
    {
        try
        {
            c.BringIntoView();
        }
        catch { }
    }

    private static List<(Control Control, Avalonia.Rect Rect)> Candidates(Window win)
    {
        var list = new List<(Control, Avalonia.Rect)>();
        if (win.Content is not Control root)
            return list;

        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is not Control c)
                continue;
            if (!c.Focusable || !c.IsEffectivelyVisible || !c.IsEffectivelyEnabled)
                continue;
            // Only interactive controls; plain focusable containers (e.g.
            // panels made focusable for shortcuts) just add noise.
            if (c is not (Button or ToggleSwitch or CheckBox or RadioButton or Slider
                or ComboBox or TextBox or ListBoxItem or TabItem or Avalonia.Controls.Primitives.ToggleButton))
                continue;
            var r = BoundsIn(win, c);
            if (r is { Width: > 0, Height: > 0 })
                list.Add((c, r.Value));
        }
        return list;
    }

    private static Avalonia.Rect? BoundsIn(Window win, Control c)
    {
        var origin = c.TranslatePoint(new Avalonia.Point(0, 0), win);
        if (origin == null)
            return null;
        return new Avalonia.Rect(origin.Value, c.Bounds.Size);
    }

    private static void SendKey(Control target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = target,
        });
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = key,
            Source = target,
        });
    }
}
