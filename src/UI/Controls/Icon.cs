using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using AvaloniaSvg = Avalonia.Svg.Skia.Svg;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// Renders one SVG from the active icon set (<see cref="App.IconSet"/>).
/// 
/// Usage in XAML:
///   <code>
///   &lt;local:Icon IconName="bolt"   Width="24" Height="24"/&gt;
///   &lt;local:Icon IconName="bolt"   Width="18" Height="18" Bold="True"/&gt;
///   &lt;local:Icon IconName="gear"   Width="14" Height="14"/&gt;
///   &lt;local:Icon IconName="warning" Width="22" Height="22"/&gt;
///   </code>
/// 
/// Resolution rules:
///   1. With <c>Bold=True</c>, try <c>Icons/{set}/{name}-bold.svg</c> first.
///   2. If missing (or <c>Bold=False</c>), fall back to <c>Icons/{set}/{name}.svg</c>.
///   3. If that is also missing, the control renders empty (logs a warning).
/// 
/// No color manipulation. Each SVG renders with whatever fill/stroke attributes
/// its author embedded. Noto emoji stay polychrome; Tabler/Phosphor stay white.
/// </summary>
public class Icon : ContentControl
{
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<Icon, string>(nameof(IconName), string.Empty);

    public static readonly StyledProperty<bool> BoldProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(Bold), false);

    /// <summary>Semantic icon name, e.g. "bolt", "gear", "warning".</summary>
    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    /// <summary>If true, prefer the <c>-bold</c> variant when the set ships one.</summary>
    public bool Bold
    {
        get => GetValue(BoldProperty);
        set => SetValue(BoldProperty, value);
    }

    static Icon()
    {
        // Rebuild visual whenever IconName/Bold changes at runtime.
        IconNameProperty.Changed.AddClassHandler<Icon>((icon, _) => icon.Rebuild());
        BoldProperty.Changed.AddClassHandler<Icon>((icon, _) => icon.Rebuild());
    }

    public Icon()
    {
        // Subscribe to the app-wide icon-set change event only while this
        // control is in the visual tree. Detach cleanup is essential: the
        // static event would otherwise keep Icon instances alive after their
        // host window closes.
        AttachedToVisualTree += (_, _) =>
        {
            App.IconSetChanged += OnSetChanged;
            Rebuild();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            App.IconSetChanged -= OnSetChanged;
        };
    }

    private void OnSetChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (string.IsNullOrEmpty(IconName))
        {
            Content = null;
            return;
        }

        var set = App.IconSet;

        // Preferred path (with -bold suffix if requested) then plain fallback.
        // Sets without bold variants (e.g. noto) always resolve to the plain file.
        var candidates = Bold
            ? new[] { $"{IconName}-bold", IconName }
            : new[] { IconName };

        foreach (var candidate in candidates)
        {
            var uriString = $"avares://ghelper/UI/Assets/Icons/{set}/{candidate}.svg";
            var uri = new Uri(uriString);
            if (!AssetLoader.Exists(uri))
                continue;

            Content = new AvaloniaSvg(default(Uri)!) { Path = uriString };
            return;
        }

        // Nothing matched. Leave Content null so the space stays reserved.
        Helpers.Logger.WriteLine($"Icon '{IconName}' (Bold={Bold}) not found in set '{set}'");
        Content = null;
    }
}
