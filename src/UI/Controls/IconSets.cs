using System.Collections.Generic;
using System.Linq;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// Registry of icon set slugs, discovered at build time from the folder
/// layout under <c>src/UI/Assets/Icons/</c>. The slugs are baked into the
/// binary via <see cref="IconSetsGenerated.Slugs"/> emitted by the
/// <c>GenerateIconSets</c> MSBuild task; the list is therefore fixed at
/// compile time and costs nothing at runtime.
/// 
/// Adding a new set = drop a folder of SVGs under <c>UI/Assets/Icons/</c>
/// and rebuild. The <c>ValidateIcons</c> MSBuild task enforces that every
/// set contains the same base icons as the <c>noto</c> reference set.
/// </summary>
public static class IconSets
{
    /// <summary>All set slugs, alphabetically sorted, fixed at compile time.</summary>
    public static IReadOnlyList<string> AvailableSlugs => IconSetsGenerated.Slugs;

    /// <summary>Default slug when config is missing or references an unknown set.</summary>
    public const string Default = "noto";

    /// <summary>True if <paramref name="slug"/> names a bundled set.</summary>
    public static bool Exists(string? slug) =>
        !string.IsNullOrEmpty(slug) && AvailableSlugs.Contains(slug);

    /// <summary>
    /// Normalizes an arbitrary config value to a valid slug.
    /// Returns <paramref name="slug"/> if it exists, otherwise <see cref="Default"/>
    /// if that exists, otherwise the first available slug.
    /// </summary>
    public static string Normalize(string? slug)
    {
        if (Exists(slug))
            return slug!;
        if (Exists(Default))
            return Default;
        return AvailableSlugs.FirstOrDefault() ?? Default;
    }

    /// <summary>
    /// User-facing label. Title-cases the slug and replaces separators with
    /// spaces. Examples: <c>openmoji-black</c> → "Openmoji Black",
    /// <c>fluent-flat</c> → "Fluent Flat".
    /// </summary>
    public static string DisplayName(string slug) =>
        string.Join(" ", slug.Split('-', '_')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpper(w[0]) + w[1..]));
}
