using Avalonia;
using Avalonia.Skia;
using Avalonia.X11;
using GHelper.Linux;
using GHelper.Linux.Cli;
using GHelper.Linux.Helpers;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        SetGpuPreferenceEnv();

        var rc = ResourceExtractorCli.TryDispatch(args);
        if (rc.HasValue)
        {
            Environment.Exit(rc.Value);
            return;
        }

        NativeLibExtractor.ExtractAndLoad();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseX11()
            .UseSkia()
            .UseHarfBuzz()
            .LogToTrace();

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
