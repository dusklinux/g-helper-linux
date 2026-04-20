using Avalonia;
using GHelper.Linux;
using GHelper.Linux.Helpers;

// G-Helper for Linux - single binary ASUS laptop control
// Port of https://github.com/seerge/g-helper

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Extract and preload embedded native libraries (libSkiaSharp.so, libHarfBuzzSharp.so)
        // from the binary's embedded resources to ~/.cache/ghelper/libs/ before any
        // Avalonia/SkiaSharp code runs. Cached across launches, invalidated on version change.
        NativeLibExtractor.ExtractAndLoad();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
