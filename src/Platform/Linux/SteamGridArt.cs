using GHelper.Linux.Helpers;
using SkiaSharp;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Renders Steam library artwork for the G-Helper non-Steam shortcut so the
/// entry shows a proper card in Big Picture and the library instead of an
/// empty gray tile. File names follow the convention Heroic and
/// steam-rom-manager use in userdata/[user]/config/grid/, keyed by the
/// unsigned shortcut appid:
///   [id]p.png       600x900 capsule (library grid, Big Picture tile)
///   [id].png        920x430 header (recent games, small capsule)
///   [id]_hero.png   1920x620 banner (game page background)
///   [id]_logo.png   transparent logo overlaid on the hero
/// Everything is generated from the app icon at runtime; no assets shipped.
/// </summary>
internal static class SteamGridArt
{
    private static readonly SKColor BgTop = new(0x23, 0x26, 0x2C);
    private static readonly SKColor BgBottom = new(0x10, 0x12, 0x16);
    private static readonly SKColor Accent = new(0x4C, 0xC2, 0xFF);

    /// <summary>Write all four images. Never throws; art is cosmetic and must
    /// not fail the shortcut operation.</summary>
    internal static void Write(string configDir, uint shortId, string iconPath)
    {
        try
        {
            string grid = Path.Combine(configDir, "grid");
            Directory.CreateDirectory(grid);

            using var icon = LoadIcon(iconPath);
            Render(Path.Combine(grid, $"{shortId}p.png"), 600, 900, c => DrawCapsule(c, 600, 900, icon));
            Render(Path.Combine(grid, $"{shortId}.png"), 920, 430, c => DrawHeader(c, 920, 430, icon));
            Render(Path.Combine(grid, $"{shortId}_hero.png"), 1920, 620, c => DrawHero(c, 1920, 620, icon));
            Render(Path.Combine(grid, $"{shortId}_logo.png"), 800, 260, c => DrawLogo(c, 800, 260, icon));
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"SteamGridArt: write failed: {ex.Message}");
        }
    }

    /// <summary>Delete our images, including the legacy Big Picture name
    /// ((id &lt;&lt; 32) | 0x02000000). Never throws.</summary>
    internal static void Remove(string configDir, uint shortId)
    {
        string grid = Path.Combine(configDir, "grid");
        ulong legacy = ((ulong)shortId << 32) | 0x02000000;
        string[] files =
        [
            $"{shortId}p.png", $"{shortId}.png", $"{shortId}_hero.png", $"{shortId}_logo.png",
            $"{legacy}.png",
        ];
        foreach (var f in files)
        {
            try
            {
                var path = Path.Combine(grid, f);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"SteamGridArt: remove {f} failed: {ex.Message}");
            }
        }
    }

    private static SKImage? LoadIcon(string iconPath)
    {
        try
        {
            if (iconPath.Length > 0 && File.Exists(iconPath))
                return SKImage.FromEncodedData(iconPath);
        }
        catch { }
        return null;
    }

    private static void Render(string path, int w, int h, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    private static void DrawBackground(SKCanvas c, int w, int h)
    {
        using var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, h),
                [BgTop, BgBottom], null, SKShaderTileMode.Clamp),
        };
        c.DrawRect(0, 0, w, h, bg);
    }

    /// <summary>Soft accent glow behind the icon so the tile reads as
    /// intentional art rather than a flat placeholder.</summary>
    private static void DrawGlow(SKCanvas c, float cx, float cy, float radius)
    {
        using var glow = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), radius,
                [Accent.WithAlpha(0x2E), Accent.WithAlpha(0x00)], null, SKShaderTileMode.Clamp),
        };
        c.DrawCircle(cx, cy, radius, glow);
    }

    private static void DrawIcon(SKCanvas c, SKImage? icon, float cx, float cy, float size, byte alpha = 0xFF)
    {
        if (icon == null)
            return;
        var dest = new SKRect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        c.DrawImage(icon, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    /// <summary>App name in white; skipped silently when no typeface resolves
    /// (headless or fontconfig-less systems).</summary>
    private static void DrawTitle(SKCanvas c, float cx, float baseline, float textSize, byte alpha = 0xFF)
    {
        try
        {
            using var typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold);
            if (typeface == null)
                return;
            using var font = new SKFont(typeface, textSize);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha), IsAntialias = true };
            c.DrawText(SteamShortcuts.AppName, cx, baseline, SKTextAlign.Center, font, paint);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"SteamGridArt: text skipped: {ex.Message}");
        }
    }

    private static void DrawCapsule(SKCanvas c, int w, int h, SKImage? icon)
    {
        DrawBackground(c, w, h);
        DrawGlow(c, w / 2f, h * 0.42f, w * 0.55f);
        DrawIcon(c, icon, w / 2f, h * 0.42f, w * 0.48f);
        DrawTitle(c, w / 2f, h * 0.78f, 64);
    }

    private static void DrawHeader(SKCanvas c, int w, int h, SKImage? icon)
    {
        DrawBackground(c, w, h);
        DrawGlow(c, w * 0.28f, h / 2f, h * 0.8f);
        DrawIcon(c, icon, w * 0.28f, h / 2f, h * 0.62f);
        DrawTitle(c, w * 0.66f, h / 2f + 26, 72);
    }

    private static void DrawHero(SKCanvas c, int w, int h, SKImage? icon)
    {
        DrawBackground(c, w, h);
        // Large faint icon off to the right; the logo art carries the name.
        DrawGlow(c, w * 0.82f, h / 2f, h * 0.9f);
        DrawIcon(c, icon, w * 0.82f, h / 2f, h * 0.72f, 0x55);
    }

    private static void DrawLogo(SKCanvas c, int w, int h, SKImage? icon)
    {
        // Transparent background: icon left, wordmark right.
        DrawIcon(c, icon, h * 0.5f, h / 2f, h * 0.9f);
        DrawTitle(c, h + (w - h) * 0.42f, h / 2f + 30, 84);
    }
}
