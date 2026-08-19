namespace GHelper.Linux.Display;

/// <summary>
/// Auto-detects the best display backend for the current Wayland session.
///
/// Probe order:
///   wlr-randr → gdctl (GNOME 48+) → kscreen-doctor (KDE)
///
/// Each backend is probed once at startup. The first one that succeeds is used
/// for the lifetime of the session.
/// </summary>
public static class DisplayBackendFactory
{
    /// <summary>
    /// Probe for the best available Wayland display backend.
    /// Returns null only if no backend works at all.
    /// </summary>
    public static IDisplayBackend? Create()
    {
        return CreateWayland();
    }

    private static IDisplayBackend? CreateWayland()
    {
        Helpers.Logger.WriteLine("Display: probing Hyprland backend (hyprctl)...");
        if (HyprlandBackend.Probe())
        {
            Helpers.Logger.WriteLine("Display: Hyprland session, using native HyprlandBackend");
            return new HyprlandBackend();
        }

        Helpers.Logger.WriteLine("Display: WARNING - no Hyprland backend available");
        return null;
    }

    // Session detection helpers

    /// <summary>Detect if we're running on a Wayland session.</summary>
    public static bool IsWaylandSession()
    {
        var xdgType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (xdgType != null && xdgType.Equals("wayland", StringComparison.OrdinalIgnoreCase))
            return true;

        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return !string.IsNullOrEmpty(waylandDisplay);
    }

    /// <summary>
    /// Detect the active Wayland compositor.
    /// Returns: "kwin", "sway", "hyprland", "gnome-shell", "niri", "river", "wayfire", "labwc", "cosmic", or null.
    /// </summary>
    public static string? DetectCompositor()
    {
        // XDG_CURRENT_DESKTOP is the most reliable
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToLowerInvariant();
        if (desktop != null)
        {
            if (desktop.Contains("kde") || desktop.Contains("plasma"))
                return "kwin";
            if (desktop.Contains("gnome"))
                return "gnome-shell";
            if (desktop.Contains("sway"))
                return "sway";
            if (desktop.Contains("hyprland"))
                return "hyprland";
            if (desktop.Contains("niri"))
                return "niri";
            if (desktop.Contains("cosmic"))
                return "cosmic";
        }

        // HYPRLAND_INSTANCE_SIGNATURE is set by Hyprland
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            return "hyprland";

        // SWAYSOCK is set by Sway
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
            return "sway";

        return null;
    }
}
