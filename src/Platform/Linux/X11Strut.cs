using System.Runtime.InteropServices;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// EWMH screen-edge reservation for the on-screen keyboard's dock mode.
/// Sets _NET_WM_WINDOW_TYPE_DOCK plus _NET_WM_STRUT(_PARTIAL) on the X11
/// window so KWin resizes/pushes other windows off the keyboard area, the
/// way plasma-keyboard and onboard do. Works on X11 sessions and on KDE
/// Wayland through XWayland (KWin honors XWayland struts).
/// No-ops quietly when the X display is unavailable.
/// </summary>
internal static class X11Strut
{
    private const int PropModeReplace = 0;
    private const nint XA_ATOM = 4;
    private const nint XA_CARDINAL = 6;

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(string? name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(nint display, nint window, nint property,
        nint type, int format, int mode, long[] data, int nelements);

    [DllImport("libX11.so.6")]
    private static extern int XDeleteProperty(nint display, nint window, nint property);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(nint display);

    /// <summary>Reserve a bottom band and mark the window as a dock.
    /// bottom = band height in px from the bottom root edge; startX/endX =
    /// horizontal extent of the band.</summary>
    public static bool Apply(nint window, int bottom, int startX, int endX)
    {
        nint d = nint.Zero;
        try
        {
            d = XOpenDisplay(null);
            if (d == nint.Zero)
                return false;

            var dockAtom = XInternAtom(d, "_NET_WM_WINDOW_TYPE_DOCK", false);
            XChangeProperty(d, window, XInternAtom(d, "_NET_WM_WINDOW_TYPE", false),
                XA_ATOM, 32, PropModeReplace, [dockAtom], 1);

            // left, right, top, bottom, then per-edge start/end pairs.
            long[] partial =
                [0, 0, 0, bottom, 0, 0, 0, 0, 0, 0, startX, endX];
            XChangeProperty(d, window, XInternAtom(d, "_NET_WM_STRUT_PARTIAL", false),
                XA_CARDINAL, 32, PropModeReplace, partial, 12);
            XChangeProperty(d, window, XInternAtom(d, "_NET_WM_STRUT", false),
                XA_CARDINAL, 32, PropModeReplace, [0, 0, 0, bottom], 4);
            XFlush(d);
            return true;
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"X11Strut: apply failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (d != nint.Zero)
                XCloseDisplay(d);
        }
    }

    /// <summary>Drop the reservation and restore a normal window type.</summary>
    public static void Clear(nint window)
    {
        nint d = nint.Zero;
        try
        {
            d = XOpenDisplay(null);
            if (d == nint.Zero)
                return;
            XDeleteProperty(d, window, XInternAtom(d, "_NET_WM_STRUT_PARTIAL", false));
            XDeleteProperty(d, window, XInternAtom(d, "_NET_WM_STRUT", false));
            var normalAtom = XInternAtom(d, "_NET_WM_WINDOW_TYPE_NORMAL", false);
            XChangeProperty(d, window, XInternAtom(d, "_NET_WM_WINDOW_TYPE", false),
                XA_ATOM, 32, PropModeReplace, [normalAtom], 1);
            XFlush(d);
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"X11Strut: clear failed: {ex.Message}");
        }
        finally
        {
            if (d != nint.Zero)
                XCloseDisplay(d);
        }
    }
}
