using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;
using GHelper.Linux.Input;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// On-screen keyboard for touch-first devices without a physical keyboard,
/// aimed at SteamOS-style desktop mode on handhelds (Steam Deck, Legion Go).
/// Layout follows Android / Steam's game-mode keyboard: a letters page with
/// a prominent Backspace, a ?123 symbols page, and a PC page (F-keys,
/// navigation, latching modifiers) for terminals.
///
/// Keys are injected through a /dev/uinput virtual keyboard (OskUinput), so
/// they reach whatever window the compositor has focused, on X11 and Wayland
/// alike. The window itself must never take focus: it opts out via
/// ShowActivated/Focusable and, on KDE (SteamOS desktop mode), a KWin window
/// rule forces "accept focus: no" so taps cannot activate it.
///
/// Dock mode (pin button) additionally marks the window as an EWMH dock
/// with a bottom strut, so KWin pushes/resizes other windows above the
/// keyboard the way the upstream virtual keyboards do.
///
/// Labels show US QWERTY; the compositor applies the user's XKB layout to
/// the emitted keycodes, exactly like a physical US keyboard would behave.
/// </summary>
public partial class OnScreenKeyboardWindow : Window
{
    // Linux input-event-codes for the layout (linux/input-event-codes.h).
    private const ushort KEY_ESC = 1, KEY_MINUS = 12, KEY_EQUAL = 13, KEY_BACKSPACE = 14,
        KEY_TAB = 15, KEY_LEFTBRACE = 26, KEY_RIGHTBRACE = 27, KEY_ENTER = 28, KEY_LEFTCTRL = 29,
        KEY_SEMICOLON = 39, KEY_APOSTROPHE = 40, KEY_GRAVE = 41, KEY_LEFTSHIFT = 42,
        KEY_BACKSLASH = 43, KEY_COMMA = 51, KEY_DOT = 52, KEY_SLASH = 53, KEY_LEFTALT = 56,
        KEY_SPACE = 57, KEY_SYSRQ = 99, KEY_HOME = 102, KEY_UP = 103, KEY_PAGEUP = 104,
        KEY_LEFT = 105, KEY_RIGHT = 106, KEY_END = 107, KEY_DOWN = 108, KEY_PAGEDOWN = 109,
        KEY_INSERT = 110, KEY_DELETE = 111, KEY_LEFTMETA = 125, KEY_COMPOSE = 127;

    private enum Kind { Normal, Shift, Ctrl, Alt, Super, Page }

    private enum OskPage { Letters, Symbols, Pc }

    private sealed record OskKey(string Label, string Shift, ushort Code,
        double Width = 1, Kind Kind = Kind.Normal, bool NeedShift = false,
        OskPage Target = OskPage.Letters);

    private static readonly IBrush KeyBg = new SolidColorBrush(Color.Parse("#262B33"));
    private static readonly IBrush KeyBgSpecial = new SolidColorBrush(Color.Parse("#1F242B"));
    private static readonly IBrush KeyFg = new SolidColorBrush(Color.Parse("#E8EDF2"));
    private static readonly IBrush LatchBg = new SolidColorBrush(Color.Parse("#4CC2FF"));
    private static readonly IBrush LatchFg = new SolidColorBrush(Color.Parse("#10141A"));

    private readonly OskUinput _uinput = new();
    private readonly List<(RepeatButton Button, OskKey Key)> _keys = new();

    private bool _shift, _ctrl, _alt, _super, _docked;
    private OskPage _page = OskPage.Letters;

    public OnScreenKeyboardWindow()
    {
        InitializeComponent();

        // The no-focus-steal rule must exist before the window is mapped;
        // KWin evaluates rules when the surface appears.
        KwinRules.EnsureOskRule(Title ?? "G-Helper Keyboard");

        ToolTip.SetTip(buttonPin, Labels.Get("osk_dock_tip"));
        _docked = Helpers.AppConfig.Is("osk_docked");

        BuildKeys();

        Loaded += (_, _) =>
        {
            TryStartDevice();
            PositionBottomCenter();
            RefreshPinButton();
            if (_docked)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDock(),
                    Avalonia.Threading.DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) =>
        {
            if (_docked && IsVisible)
            {
                PositionBottomCenter();
                ApplyDock(remap: false);
            }
        };
        Closing += (_, e) =>
        {
            // Keep the instance (and the uinput device) alive; the tray
            // toggle just re-shows it. Real shutdown still closes us.
            if (App.IsShuttingDown)
                return;
            e.Cancel = true;
            ClearDock();
            Hide();
        };
    }

    /// <summary>Called on app shutdown to release the uinput device.</summary>
    public void ShutdownDevice() => _uinput.Dispose();

    private void TryStartDevice()
    {
        if (_uinput.Started)
            return;
        string? error = OskUinput.ProbeError();
        if (error == null && _uinput.Start())
        {
            labelError.IsVisible = false;
            keysHost.IsEnabled = true;
            return;
        }
        labelError.Text = error ?? Labels.Get("fnlock_unavail_errno");
        labelError.IsVisible = true;
        keysHost.IsEnabled = false;
    }

    public new void Show()
    {
        base.Show();
        TryStartDevice();
        if (_docked)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDock(),
                Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Bottom-center of the primary working area. Positioning is
    /// honored on X11; Wayland compositors place the window themselves.</summary>
    private void PositionBottomCenter()
    {
        try
        {
            var wa = Screens.Primary?.WorkingArea;
            if (wa is not { } r)
                return;
            var size = PixelSize.FromSize(ClientSize, DesktopScaling);
            Position = new PixelPoint(
                r.X + (r.Width - size.Width) / 2,
                r.Y + r.Height - size.Height - 12);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"OSK: positioning skipped: {ex.Message}");
        }
    }

    // Dock mode (EWMH strut)

    private void ButtonPin_Click(object? sender, RoutedEventArgs e)
    {
        _docked = !_docked;
        Helpers.AppConfig.Set("osk_docked", _docked ? 1 : 0);
        RefreshPinButton();
        if (_docked)
        {
            PositionBottomCenter();
            ApplyDock();
        }
        else
        {
            ClearDock(remap: true);
        }
    }

    private void RefreshPinButton()
    {
        // Down-to-bar = dock, up-from-bar = float.
        buttonPin.Content = _docked ? "\u2912" : "\u2913";
    }

    /// <summary>Reserve the bottom screen band under the keyboard. A quick
    /// remap makes KWin re-read the dock window type (it is only honored at
    /// manage time); the strut itself updates dynamically.</summary>
    private void ApplyDock(bool remap = true)
    {
        var handle = TryGetPlatformHandle();
        if (handle == null)
            return;
        try
        {
            var screen = Screens.Primary?.Bounds;
            if (screen is not { } s)
                return;
            var size = PixelSize.FromSize(ClientSize, DesktopScaling);
            int bottom = s.Y + s.Height - Position.Y;
            bool ok = X11Strut.Apply(handle.Handle, bottom,
                Position.X, Position.X + size.Width - 1);
            if (ok && remap && IsVisible)
            {
                base.Hide();
                base.Show();
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"OSK: dock failed: {ex.Message}");
        }
    }

    private void ClearDock(bool remap = false)
    {
        var handle = TryGetPlatformHandle();
        if (handle == null)
            return;
        X11Strut.Clear(handle.Handle);
        if (remap && IsVisible)
        {
            base.Hide();
            base.Show();
        }
    }

    // Layout

    private static List<OskKey[]> BuildLayout(OskPage page) => page switch
    {
        OskPage.Symbols => BuildSymbolsPage(),
        OskPage.Pc => BuildPcPage(),
        _ => BuildLettersPage(),
    };

    /// <summary>Android / Steam style: numbers, three letter rows, prominent
    /// Backspace next to M, space bar with comma/period, bottom Enter.</summary>
    private static List<OskKey[]> BuildLettersPage() => new()
    {
        new[]
        {
            new OskKey("1", "!", 2), new OskKey("2", "@", 3), new OskKey("3", "#", 4),
            new OskKey("4", "$", 5), new OskKey("5", "%", 6), new OskKey("6", "^", 7),
            new OskKey("7", "&", 8), new OskKey("8", "*", 9), new OskKey("9", "(", 10),
            new OskKey("0", ")", 11),
        },
        new[]
        {
            new OskKey("q", "Q", 16), new OskKey("w", "W", 17), new OskKey("e", "E", 18),
            new OskKey("r", "R", 19), new OskKey("t", "T", 20), new OskKey("y", "Y", 21),
            new OskKey("u", "U", 22), new OskKey("i", "I", 23), new OskKey("o", "O", 24),
            new OskKey("p", "P", 25),
        },
        new[]
        {
            new OskKey("a", "A", 30), new OskKey("s", "S", 31), new OskKey("d", "D", 32),
            new OskKey("f", "F", 33), new OskKey("g", "G", 34), new OskKey("h", "H", 35),
            new OskKey("j", "J", 36), new OskKey("k", "K", 37), new OskKey("l", "L", 38),
            new OskKey("'", "\"", KEY_APOSTROPHE),
        },
        new[]
        {
            new OskKey("\u21E7", "", KEY_LEFTSHIFT, 1.5, Kind.Shift),
            new OskKey("z", "Z", 44), new OskKey("x", "X", 45), new OskKey("c", "C", 46),
            new OskKey("v", "V", 47), new OskKey("b", "B", 48), new OskKey("n", "N", 49),
            new OskKey("m", "M", 50),
            new OskKey("\u232B", "", KEY_BACKSPACE, 1.5),
        },
        new[]
        {
            new OskKey("?123", "", 0, 1.5, Kind.Page, Target: OskPage.Symbols),
            new OskKey("PC", "", 0, 1.5, Kind.Page, Target: OskPage.Pc),
            new OskKey(",", "", KEY_COMMA),
            new OskKey("", "", KEY_SPACE, 4),
            new OskKey(".", "", KEY_DOT),
            new OskKey("\u23CE", "", KEY_ENTER, 2),
        },
    };

    /// <summary>?123 page: plain digits plus two symbol rows.</summary>
    private static List<OskKey[]> BuildSymbolsPage() => new()
    {
        new[]
        {
            new OskKey("1", "", 2), new OskKey("2", "", 3), new OskKey("3", "", 4),
            new OskKey("4", "", 5), new OskKey("5", "", 6), new OskKey("6", "", 7),
            new OskKey("7", "", 8), new OskKey("8", "", 9), new OskKey("9", "", 10),
            new OskKey("0", "", 11),
        },
        new[]
        {
            new OskKey("!", "", 2, NeedShift: true), new OskKey("@", "", 3, NeedShift: true),
            new OskKey("#", "", 4, NeedShift: true), new OskKey("$", "", 5, NeedShift: true),
            new OskKey("%", "", 6, NeedShift: true), new OskKey("^", "", 7, NeedShift: true),
            new OskKey("&", "", 8, NeedShift: true), new OskKey("*", "", 9, NeedShift: true),
            new OskKey("(", "", 10, NeedShift: true), new OskKey(")", "", 11, NeedShift: true),
        },
        new[]
        {
            new OskKey("-", "", KEY_MINUS), new OskKey("_", "", KEY_MINUS, NeedShift: true),
            new OskKey("=", "", KEY_EQUAL), new OskKey("+", "", KEY_EQUAL, NeedShift: true),
            new OskKey("[", "", KEY_LEFTBRACE), new OskKey("]", "", KEY_RIGHTBRACE),
            new OskKey("{", "", KEY_LEFTBRACE, NeedShift: true),
            new OskKey("}", "", KEY_RIGHTBRACE, NeedShift: true),
            new OskKey("\\", "", KEY_BACKSLASH), new OskKey("|", "", KEY_BACKSLASH, NeedShift: true),
        },
        new[]
        {
            new OskKey(";", "", KEY_SEMICOLON), new OskKey(":", "", KEY_SEMICOLON, NeedShift: true),
            new OskKey("'", "", KEY_APOSTROPHE), new OskKey("\"", "", KEY_APOSTROPHE, NeedShift: true),
            new OskKey("`", "", KEY_GRAVE), new OskKey("~", "", KEY_GRAVE, NeedShift: true),
            new OskKey("/", "", KEY_SLASH), new OskKey("?", "", KEY_SLASH, NeedShift: true),
            new OskKey("<", "", KEY_COMMA, NeedShift: true), new OskKey(">", "", KEY_DOT, NeedShift: true),
        },
        new[]
        {
            new OskKey("ABC", "", 0, 1.5, Kind.Page, Target: OskPage.Letters),
            new OskKey("PC", "", 0, 1.5, Kind.Page, Target: OskPage.Pc),
            new OskKey("", "", KEY_SPACE, 5),
            new OskKey("\u232B", "", KEY_BACKSPACE, 1.5),
            new OskKey("\u23CE", "", KEY_ENTER, 2),
        },
    };

    /// <summary>PC page: Esc/F-keys, navigation cluster, latching modifiers
    /// and arrows, for terminals and shortcuts.</summary>
    private static List<OskKey[]> BuildPcPage() => new()
    {
        new[]
        {
            new OskKey("Esc", "", KEY_ESC, 1.5),
            new OskKey("F1", "", 59), new OskKey("F2", "", 60), new OskKey("F3", "", 61),
            new OskKey("F4", "", 62), new OskKey("F5", "", 63), new OskKey("F6", "", 64),
            new OskKey("F7", "", 65), new OskKey("F8", "", 66), new OskKey("F9", "", 67),
            new OskKey("F10", "", 68), new OskKey("F11", "", 87), new OskKey("F12", "", 88),
            new OskKey("\u232B", "", KEY_BACKSPACE, 2),
        },
        new[]
        {
            new OskKey("Tab", "", KEY_TAB, 2),
            new OskKey("`", "~", KEY_GRAVE),
            new OskKey("Home", "", KEY_HOME, 1.5), new OskKey("End", "", KEY_END, 1.5),
            new OskKey("PgUp", "", KEY_PAGEUP, 1.5), new OskKey("PgDn", "", KEY_PAGEDOWN, 1.5),
            new OskKey("Ins", "", KEY_INSERT, 1.5), new OskKey("Del", "", KEY_DELETE, 1.5),
            new OskKey("PrtSc", "", KEY_SYSRQ, 1.5), new OskKey("Menu", "", KEY_COMPOSE, 1.5),
            new OskKey("\\", "|", KEY_BACKSLASH),
        },
        new[]
        {
            new OskKey("Shift", "", KEY_LEFTSHIFT, 2, Kind.Shift),
            new OskKey("Ctrl", "", KEY_LEFTCTRL, 1.5, Kind.Ctrl),
            new OskKey("Super", "", KEY_LEFTMETA, 1.5, Kind.Super),
            new OskKey("Alt", "", KEY_LEFTALT, 1.5, Kind.Alt),
            new OskKey("\u2190", "", KEY_LEFT, 1.5),
            new OskKey("\u2191", "", KEY_UP, 1.5),
            new OskKey("\u2193", "", KEY_DOWN, 1.5),
            new OskKey("\u2192", "", KEY_RIGHT, 1.5),
        },
        new[]
        {
            new OskKey("ABC", "", 0, 2, Kind.Page, Target: OskPage.Letters),
            new OskKey("?123", "", 0, 2, Kind.Page, Target: OskPage.Symbols),
            new OskKey("", "", KEY_SPACE, 6),
            new OskKey("\u23CE", "", KEY_ENTER, 2.5),
        },
    };

    private void BuildKeys()
    {
        keysHost.Children.Clear();
        keysHost.RowDefinitions.Clear();
        _keys.Clear();

        var rows = BuildLayout(_page);
        for (int r = 0; r < rows.Count; r++)
            keysHost.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        for (int r = 0; r < rows.Count; r++)
        {
            var rowGrid = new Grid();
            Grid.SetRow(rowGrid, r);
            foreach (var key in rows[r])
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(key.Width, GridUnitType.Star));

            for (int c = 0; c < rows[r].Length; c++)
            {
                var key = rows[r][c];
                var button = new RepeatButton
                {
                    Delay = 450,
                    Interval = 70,
                    Focusable = false,
                    Margin = new Thickness(2.5),
                    Padding = new Thickness(0),
                    MinHeight = 40,
                    FontSize = 17,
                    CornerRadius = new CornerRadius(7),
                    Background = key.Kind == Kind.Normal && key.Label.Length <= 1 ? KeyBg : KeyBgSpecial,
                    Foreground = KeyFg,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = key.Label,
                };
                button.Click += (_, _) => OnKey(key);
                Grid.SetColumn(button, c);
                rowGrid.Children.Add(button);
                _keys.Add((button, key));
            }
            keysHost.Children.Add(rowGrid);
        }
        RefreshKeys();
    }

    // Input handling

    private void OnKey(OskKey key)
    {
        switch (key.Kind)
        {
            case Kind.Shift:
                _shift = !_shift;
                RefreshKeys();
                return;
            case Kind.Ctrl:
                _ctrl = !_ctrl;
                RefreshKeys();
                return;
            case Kind.Alt:
                _alt = !_alt;
                RefreshKeys();
                return;
            case Kind.Super:
                _super = !_super;
                RefreshKeys();
                return;
            case Kind.Page:
                _page = key.Target;
                BuildKeys();
                return;
        }

        var mods = new List<ushort>(4);
        if (_ctrl)
            mods.Add(KEY_LEFTCTRL);
        if (_alt)
            mods.Add(KEY_LEFTALT);
        if (_super)
            mods.Add(KEY_LEFTMETA);
        if (_shift || key.NeedShift)
            mods.Add(KEY_LEFTSHIFT);

        _uinput.Tap(key.Code, mods);

        // Latching modifiers apply to one keystroke, like handheld OSKs.
        if (_shift || _ctrl || _alt || _super)
        {
            _shift = _ctrl = _alt = _super = false;
            RefreshKeys();
        }
    }

    /// <summary>Update labels for the shift state and highlight latched
    /// modifiers.</summary>
    private void RefreshKeys()
    {
        foreach (var (button, key) in _keys)
        {
            if (key.Kind == Kind.Normal)
                button.Content = _shift && key.Shift.Length > 0 ? key.Shift : key.Label;

            bool latched = key.Kind switch
            {
                Kind.Shift => _shift,
                Kind.Ctrl => _ctrl,
                Kind.Alt => _alt,
                Kind.Super => _super,
                _ => false,
            };
            button.Background = latched
                ? LatchBg
                : key.Kind == Kind.Normal && key.Label.Length <= 1 ? KeyBg : KeyBgSpecial;
            button.Foreground = latched ? LatchFg : KeyFg;
        }
    }

    // Window chrome

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Docked windows are managed by the WM; dragging makes no sense.
        if (!_docked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void ResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void ButtonHide_Click(object? sender, RoutedEventArgs e)
    {
        ClearDock();
        Hide();
    }
}
