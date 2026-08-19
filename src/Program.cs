using Avalonia;
using Avalonia.Skia;
using Avalonia.Wayland;
using GHelper.Linux;
using GHelper.Linux.Cli;
using GHelper.Linux.Helpers;
using GHelper.Linux.Platform.Linux;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Early-start systemd units (COSMIC autostart) may lack session vars;
        // import them from the systemd user manager before anything reads them.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")))
            Cosmic.ImportSessionEnvironment();

        SetGpuPreferenceEnv();

        var rc = ResourceExtractorCli.TryDispatch(args);
        if (rc.HasValue)
        {
            Environment.Exit(rc.Value);
            return;
        }

        // "ghelper --osk" toggles the on-screen keyboard of a running
        // instance (hotkey/controller-chord friendly). When no instance is
        // running, normal startup continues and App opens the keyboard.
        if (args.Contains("--osk") && CommandIpc.TrySend("toggle-osk"))
            return;

        // Last-resort logging: capture fatal exceptions to the log file before
        // the process dies (stderr is swallowed under Steam game mode).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.WriteLine($"FATAL unhandled exception: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        NativeLibExtractor.ExtractAndLoad();

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            && !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            Logger.WriteLine("FATAL: G-Helper for Linux requires a Wayland session (WAYLAND_DISPLAY is unset).");
            Console.Error.WriteLine("Error: G-Helper Linux is configured as Wayland-only and requires an active Wayland compositor.");
            Environment.Exit(1);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseWayland()
            .With(BuildWaylandOptions())
            .UseSkia()
            .UseHarfBuzz()
            .LogToTrace();

    private static WaylandPlatformOptions BuildWaylandOptions()
    {
        var opts = new WaylandPlatformOptions();
        return opts;
    }

    private static void SetGpuPreferenceEnv()
    {
        SetIfUnset("__NV_PRIME_RENDER_OFFLOAD", "0");
        SetIfUnset("__GLX_VENDOR_LIBRARY_NAME", "mesa");
        SetIfUnset("DRI_PRIME", "0");
    }

    private static void SetIfUnset(string name, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            Environment.SetEnvironmentVariable(name, value);
    }
}
