using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Window positioning helper. Matches Windows G-Helper placement:
/// - Main window: bottom-right of primary screen, 10px inset
/// - Fans window: docked left of main window, 5px gap
///
/// On X11 and KDE Wayland, Position is honored by the compositor.
/// On strict Wayland compositors (GNOME, Sway), Position may be ignored
/// and the AXAML CenterScreen fallback takes effect. This is a platform
/// limitation, not a bug.
/// </summary>
public static class WindowPositioner
{
    private const int EdgeInset = 10;
    private const int BottomExtraInset = 4;
    private const int DockGap = 5;
    // Avalonia on Linux uses client-side decorations (CSD) - the title bar is drawn
    // by Avalonia, not the compositor. ClientSize excludes it, FrameSize includes it.
    // When FrameSize is unavailable, add this estimate for the CSD title bar height.
    private const int CsdTitleBarEstimate = 38;

    /// <summary>
    /// Position window at bottom-right of primary screen, 10px from edges.
    /// Matches Windows G-Helper Program.cs SettingsToggle() positioning.
    ///
    /// Two-phase positioning:
    /// 1. Before Show(): estimate using MinHeight (prevents CenterScreen flash)
    /// 2. On Opened: reposition with exact FrameSize (pixel-perfect final position)
    /// Windows G-Helper does the same: repositions after Show() with actual size.
    /// </summary>
    public static void BottomRight(Window window)
    {
        if (window.FrameSize != null)
        {
            // Window was previously shown - FrameSize available, position is exact
            ApplyBottomRight(window, "reshow");
            return;
        }

        // Phase 1: initial estimate before first Show() (uses MinHeight for SizeToContent)
        ApplyBottomRight(window, "estimate");

        // Phase 2: reposition after layout when FrameSize/ClientSize is known.
        // Post at Loaded priority so the render pass completes first.
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            window.Opened -= handler;
            Dispatcher.UIThread.Post(
                () => ApplyBottomRight(window, "final"),
                DispatcherPriority.Loaded);
        };
        window.Opened += handler;
    }

    /// <summary>Apply bottom-right positioning with current best size info.</summary>
    private static void ApplyBottomRight(Window window, string phase)
    {
        var screen = GetPrimaryScreen(window);
        if (screen == null)
            return;

        var wa = screen.WorkingArea;
        double scale = screen.Scaling;

        // Trust ClientSize only after layout (final/reshow), not before Show (estimate)
        bool trustClientSize = phase != "estimate";
        var (winW, winH) = GetWindowPixelSize(window, scale, trustClientSize);

        int x = wa.X + wa.Width - EdgeInset - winW;
        int y = wa.Y + wa.Height - EdgeInset - BottomExtraInset - winH;

        // Guard: don't place above or left of working area
        x = Math.Max(wa.X, x);
        y = Math.Max(wa.Y, y);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(x, y);

        Logger.WriteLine($"WindowPositioner: BottomRight ({phase}) -> ({x},{y}) on {screen.DisplayName ?? "primary"} " +
            $"[win={winW}x{winH} wa={wa.Width}x{wa.Height}+{wa.X}+{wa.Y} scale={scale:F2}]");
    }

    /// <summary>
    /// Center window on the same screen as the MainWindow currently lives on,
    /// falling back to the primary monitor if the MainWindow instance is not
    /// available (null or disposed). Used for secondary windows (Updates, Extra,
    /// Arcade, Monitor, BatteryInfo, SystemInfo) so they follow the user's
    /// active display in multi-monitor setups instead of whichever screen the
    /// compositor picks when WindowStartupLocation=CenterScreen is left to
    /// defaults.
    ///
    /// Same two-phase pattern as BottomRight: estimate before Show(), exact
    /// reposition after Opened.
    /// </summary>
    public static void CenterOfMainWindowOrPrimaryMonitor(Window window)
    {
        if (window.FrameSize != null)
        {
            ApplyCenter(window, "reshow");
            return;
        }

        ApplyCenter(window, "estimate");

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            window.Opened -= handler;
            Dispatcher.UIThread.Post(
                () => ApplyCenter(window, "final"),
                DispatcherPriority.Loaded);
        };
        window.Opened += handler;
    }

    /// <summary>Apply center positioning, anchored to MainWindow's screen with primary fallback.</summary>
    private static void ApplyCenter(Window window, string phase)
    {
        string source;
        var screen = GetMainWindowScreen();
        if (screen != null)
        {
            source = "main";
        }
        else
        {
            screen = GetPrimaryScreen(window);
            source = "primary";
        }
        if (screen == null)
            return;

        var wa = screen.WorkingArea;
        double scale = screen.Scaling;

        bool trustClientSize = phase != "estimate";
        var (winW, winH) = GetWindowPixelSize(window, scale, trustClientSize);

        int x = wa.X + (wa.Width - winW) / 2;
        int y = wa.Y + (wa.Height - winH) / 2;

        // Guard: don't place above or left of working area
        x = Math.Max(wa.X, x);
        y = Math.Max(wa.Y, y);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(x, y);

        Logger.WriteLine($"WindowPositioner: CenterOfMainWindowOrPrimaryMonitor ({phase}, src={source}) -> ({x},{y}) on {screen.DisplayName ?? "primary"} " +
            $"[win={winW}x{winH} wa={wa.Width}x{wa.Height}+{wa.X}+{wa.Y} scale={scale:F2}]");
    }

    /// <summary>
    /// Get the screen the MainWindow currently lives on. Returns null if the
    /// MainWindow instance is unavailable (not yet created, or already disposed),
    /// or if the screen lookup throws.
    /// </summary>
    private static Screen? GetMainWindowScreen()
    {
        try
        {
            var main = App.MainWindowInstance;
            if (main == null || main.PlatformImpl == null)
                return null;
            return main.Screens.ScreenFromWindow(main);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Position child window to the left of parent, 5px gap.
    /// If child is taller than parent, bottom-aligns. Otherwise top-aligns.
    /// Matches Windows G-Helper Fans.cs FormPosition().
    /// </summary>
    public static void LeftOf(Window child, Window parent)
    {
        var screen = GetScreenForWindow(parent) ?? GetPrimaryScreen(child);
        if (screen == null)
            return;

        var wa = screen.WorkingArea;
        double scale = screen.Scaling;

        // Parent is already shown, child is pre-show - trust ClientSize for parent only
        var (_, parentH) = GetWindowPixelSize(parent, scale, useClientSize: true);
        var (childW, childH) = GetWindowPixelSize(child, scale);

        var parentPos = parent.Position;

        // Left of parent, 5px gap
        int x = parentPos.X - childW - DockGap;

        int y;
        if (childH > parentH)
        {
            // Child taller: bottom-align with parent
            y = parentPos.Y + parentH - childH;
        }
        else
        {
            // Child shorter or equal: top-align with parent
            y = parentPos.Y;
        }

        // Guard: don't place off-screen
        x = Math.Max(wa.X, x);
        y = Math.Max(wa.Y, y);

        child.WindowStartupLocation = WindowStartupLocation.Manual;
        child.Position = new PixelPoint(x, y);

        Logger.WriteLine($"WindowPositioner: LeftOf -> ({x},{y}) [child={childW}x{childH} parent at ({parentPos.X},{parentPos.Y})]");
    }

    /// <summary>
    /// Get window size in physical pixels. Tries sources in order:
    /// 1. FrameSize (includes decorations, available after full render)
    /// 2. ClientSize after layout (only reliable after Opened event)
    /// 3. Width/Height from XAML (may be NaN with SizeToContent)
    /// 4. MinWidth/MinHeight as last resort estimate
    ///
    /// The useClientSize flag controls whether ClientSize is trusted.
    /// Before Show(), ClientSize can contain stale/interim values from
    /// Avalonia's initialization - only trust it after Opened fires.
    /// </summary>
    private static (int w, int h) GetWindowPixelSize(Window window, double scale, bool useClientSize = false)
    {
        // Best: FrameSize includes window chrome, available after full render
        if (window.FrameSize is { } frame && frame.Width > 0 && frame.Height > 0)
            return ((int)frame.Width, (int)frame.Height);

        // Good: ClientSize after layout is complete (Opened event).
        // ClientSize is content area only - add CSD title bar estimate for total height.
        if (useClientSize)
        {
            var cs = window.ClientSize;
            if (cs.Width > 0 && cs.Height > 0)
                return ((int)(cs.Width * scale), (int)((cs.Height + CsdTitleBarEstimate) * scale));
        }

        // Fallback: XAML Width/Height, or MinWidth/MinHeight for SizeToContent.
        // Add CSD title bar if height is estimated (NaN from SizeToContent).
        double w = double.IsNaN(window.Width) ? window.MinWidth : window.Width;
        double h = double.IsNaN(window.Height) ? window.MinHeight + CsdTitleBarEstimate : window.Height;

        return ((int)(w * scale), (int)(h * scale));
    }

    /// <summary>Get primary screen with logging. Returns null if unavailable.</summary>
    private static Screen? GetPrimaryScreen(Window window)
    {
        try
        {
            var primary = window.Screens.Primary;
            if (primary == null)
            {
                Logger.WriteLine("WindowPositioner: no primary screen detected, using compositor default");
                return null;
            }
            return primary;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"WindowPositioner: Screens API unavailable ({ex.Message})");
            return null;
        }
    }

    /// <summary>Get the screen containing a window.</summary>
    private static Screen? GetScreenForWindow(Window window)
    {
        try
        {
            return window.Screens.ScreenFromWindow(window);
        }
        catch
        {
            return null;
        }
    }
}
