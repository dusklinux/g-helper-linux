using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using GHelper.Linux.I18n;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Tray-icon tooltip orchestrator. Owns the periodic refresh timer, the
/// CPU/GPU temperature read path, and the reflection-based injection that
/// works around Avalonia 12.x's Linux <c>TrayIcon.ToolTipText</c> bug
/// (the framework only writes the SNI <c>Title</c> property; KDE Plasma
/// renders hover tooltips from the SNI <c>ToolTip</c> struct, which
/// Avalonia leaves empty).
///
/// Public API:
/// <list type="bullet">
///   <item><see cref="Start"/> - call once after the TrayIcon is created;
///         pushes the initial tooltip and starts the refresh timer.</item>
///   <item><see cref="Refresh"/> - call when something happens that should
///         update the tooltip immediately (e.g. perf-mode change), so the
///         user doesn't wait for the next timer tick.</item>
///   <item><see cref="Stop"/> - call during shutdown before the WMI handle
///         is disposed, so a tick mid-shutdown can't read a dead handle.</item>
/// </list>
///
/// Native AOT note: the reflection path walks Avalonia internals
/// (<c>TrayIcon._impl</c> →
/// <c>DBusTrayIconImpl._statusNotifierItemDbusObj</c> →
/// <c>OrgKdeStatusNotifierItemHandler.ToolTip</c> /
/// <c>EmitNewToolTip</c>). Those members are preserved by
/// <c>TrimmerRoots.xml</c> at the project root so the trimmer doesn't
/// strip the metadata at publish time.
/// </summary>
public static class TrayTooltip
{
    // Cached refs so Refresh()/timer ticks can run without re-fetching
    // App-level state on every call. _trayIcon is the only mutable handle;
    // everything else is rebuilt each tick.
    private static DispatcherTimer? _timer;
    private static TrayIcon? _trayIcon;

    // 3-second cadence: a battery-friendly mid-ground that catches
    // CPU/GPU transients within ~1-2 ticks. Matches MainWindow's perceived
    // freshness without doubling the sysfs read load when both are active.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Initialize the tooltip system: cache the tray-icon reference, push
    /// the initial tooltip text, and start the periodic refresh timer.
    /// Safe to call once from <c>SetupTrayIcon</c>; idempotent if the
    /// caller invokes it multiple times (timer is reset, not duplicated).
    /// </summary>
    public static void Start(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;

        // Push immediately so the user doesn't see "G-Helper - Balanced"
        // (Avalonia's leftover Title) for the first 3 seconds.
        Push(BuildBody());

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Push(BuildBody());
        _timer.Start();
    }

    /// <summary>
    /// Force an immediate tooltip refresh. Used by <c>UpdateTrayIcon</c>
    /// on perf-mode changes so the swap is visually atomic with the icon
    /// change.
    /// </summary>
    public static void Refresh()
    {
        if (_trayIcon == null)
            return;
        Push(BuildBody());
    }

    /// <summary>
    /// Halt the refresh timer. Call this <em>before</em> disposing the
    /// WMI handle during shutdown so an in-flight tick can't dereference
    /// a freed sysfs descriptor.
    /// </summary>
    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// Build the multi-line tooltip body. Always shows CPU temp (or "--"
    /// when the read fails). Shows GPU temp on a second line only when
    /// it reports a positive value, so dGPU eco / GPU-off states render
    /// cleanly as a single line. Mode information is intentionally not
    /// duplicated - the tray icon's color/shape already conveys that.
    /// </summary>
    private static string BuildBody()
    {
        int cpuTemp = -1;
        int gpuTemp = -1;
        try
        {
            cpuTemp = App.Wmi?.DeviceGet(0x00120094) ?? -1; // Temp_CPU
            gpuTemp = App.Wmi?.DeviceGet(0x00120097) ?? -1; // Temp_GPU
        }
        catch
        {
            // Sysfs reads can transiently fail (EAGAIN/ENODEV during
            // driver hotplug, suspend/resume). Fall back to placeholder
            // rather than letting the timer tick die.
        }

        string line1 = Labels.Format("tray_tooltip_cpu", cpuTemp > 0 ? $"{cpuTemp}°C" : "--");
        if (gpuTemp > 0)
        {
            string line2 = Labels.Format("tray_tooltip_gpu", $"{gpuTemp}°C");
            return line1 + "\n" + line2;
        }
        return line1;
    }

    /// <summary>
    /// Push <paramref name="body"/> into both surfaces:
    /// <list type="number">
    ///   <item>SNI <c>Title</c> (via <c>TrayIcon.ToolTipText</c>) - used
    ///         by accessibility layers and non-KDE consumers that read
    ///         the Title field as fallback. Cheap; harmless if ignored.</item>
    ///   <item>SNI <c>ToolTip</c> property (via reflection into Avalonia
    ///         internals) - what KDE Plasma actually renders on hover.</item>
    /// </list>
    /// </summary>
    private static void Push(string body)
    {
        if (_trayIcon == null)
            return;

        // Public Avalonia surface: sets SNI Title. Doesn't render as a
        // hover tooltip on KDE but some other DEs / a11y tools use it.
        _trayIcon.ToolTipText = body;

        // Real SNI ToolTip property KDE renders on hover.
        TrySetSniTooltip(body);
    }

    /// <summary>
    /// Reflect into Avalonia's <c>TrayIcon</c> implementation and set the
    /// real StatusNotifierItem ToolTip property, then trigger the
    /// <c>NewToolTip</c> signal so the panel re-fetches it. The framework
    /// API (<c>ToolTipText</c>) only writes the SNI <c>Title</c> field,
    /// not this struct, so KDE Plasma never receives a hover tooltip
    /// without this workaround.
    ///
    /// The SNI ToolTip struct is <c>(iconName, iconPixmap, title, body)</c>.
    /// We put the temps in the <c>title</c> field - Plasma renders the
    /// title as a bold headline and skips the popup entirely on some
    /// versions when the title is empty even if the body is populated.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Avalonia.FreeDesktop internals preserved via TrimmerRoots.xml")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Avalonia.FreeDesktop internals preserved via TrimmerRoots.xml")]
    private static void TrySetSniTooltip(string body)
    {
        if (_trayIcon == null)
            return;

        try
        {
            // TrayIcon._impl (private readonly ITrayIconImpl?)
            var implField = typeof(TrayIcon).GetField("_impl",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object? impl = implField?.GetValue(_trayIcon);
            if (impl == null)
                return;

            // DBusTrayIconImpl._statusNotifierItemDbusObj (private readonly StatusNotifierItemDbusObj?)
            var sniField = impl.GetType().GetField("_statusNotifierItemDbusObj",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object? sni = sniField?.GetValue(impl);
            if (sni == null)
                return;

            // ToolTip { get; set; } - public auto-property on the
            // OrgKdeStatusNotifierItemHandler base. Tuple shape:
            // (string? iconName, (int,int,byte[]?)[]? pixmap, string? title, string? body)
            var tooltipProp = sni.GetType().GetProperty("ToolTip");
            if (tooltipProp == null)
                return;

            (int, int, byte[])[]? pixmap = null;
            ValueTuple<string, (int, int, byte[])[]?, string, string> value = ("", pixmap, body, "");
            tooltipProp.SetValue(sni, value);

            // EmitNewToolTip() - protected on the base; KDE re-reads
            // ToolTip when this signal fires.
            var emit = sni.GetType().GetMethod("EmitNewToolTip",
                BindingFlags.NonPublic | BindingFlags.Instance);
            emit?.Invoke(sni, null);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"TrayTooltip: SNI injection failed: {ex.Message}");
        }
    }
}
