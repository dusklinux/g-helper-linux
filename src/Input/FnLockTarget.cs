namespace GHelper.Linux.Input;

/// <summary>
/// What an F-key (F1..F12) produces when the software fn-lock remapper is
/// active and FnLockOn=false. Either:
///   - a Linux KEY_* code (forwarded to the virtual keyboard), or
///   - a g-helper internal action (dispatched via App.RaiseActionFromFnLock),
///     reusing the same action vocabulary as the Extra window's Key Bindings
///     section ("ghelper", "aura", "performance", "micmute", etc.).
///
/// Persisted in AppConfig as a single string with prefix:
///   - "key:&lt;ushort&gt;"  for keycode targets
///   - "action:&lt;name&gt;" for action targets
///
/// Old configs that stored a bare integer keycode (pre-FnLockTarget) are
/// migrated transparently in <see cref="FnLockKeymap.ResolveActiveMap"/>.
/// </summary>
public sealed class FnLockTarget
{
    /// <summary>Linux KEY_* code, or null when this is an action target.</summary>
    public ushort? KeyCode { get; init; }

    /// <summary>Action id (e.g. "aura"), or null when this is a keycode target.</summary>
    public string? Action { get; init; }

    /// <summary>Human-readable display name for the dropdown.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Stable persistable tag, e.g. "key:113" or "action:aura".</summary>
    public string Tag { get; init; } = "";

    public bool IsKey => KeyCode.HasValue;
    public bool IsAction => Action != null;

    public static FnLockTarget Key(ushort code, string displayName) => new()
    {
        KeyCode = code,
        DisplayName = displayName,
        Tag = $"key:{code}",
    };

    public static FnLockTarget Act(string action, string displayName) => new()
    {
        Action = action,
        DisplayName = displayName,
        Tag = $"action:{action}",
    };

    /// <summary>
    /// Parse a persisted tag back into a target. Returns null for malformed
    /// tags. The returned target's <see cref="DisplayName"/> is filled by
    /// looking up the canonical display name from <see cref="FnLockKeymap"/>.
    /// </summary>
    public static FnLockTarget? FromTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        if (tag.StartsWith("key:") && ushort.TryParse(tag.AsSpan(4), out var code))
            return Key(code, FnLockKeymap.ResolveDisplayName($"key:{code}"));
        if (tag.StartsWith("action:"))
        {
            string a = tag[7..];
            return Act(a, FnLockKeymap.ResolveDisplayName($"action:{a}"));
        }
        return null;
    }
}
