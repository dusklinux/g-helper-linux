using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.Input;

/// <summary>
/// Per-model F-key to remap-target mapping for software fn-lock.
///
/// When fn-lock is OFF (= "media keys on top row"), pressing bare F1..F12
/// emits the mapped target (a media key code OR an internal g-helper action)
/// instead of the F-key. When fn-lock is ON, F1..F12 pass through unchanged.
///
/// The map comes from four sources, merged in this order (later wins):
///   1. Generic ASUS modern-ROG/TUF default (matches Windows g-helper).
///   2. Per-model substring overrides:
///        - NoMKeys:     older models without dedicated M keys (F2/F3=Volume, F4=MicMute)
///        - MediaKeys:   G14 2020 and friends (F2/F3/F4 = transport)
///        - ProArt:      F2/F3=Volume, F4=Aura, F5/F6=Brightness
///        - Z13/DUO:     F11 = backlight cycle
///   3. User overrides stored in AppConfig as fnlock_map_&lt;keycode&gt; strings
///      with shape "key:&lt;n&gt;" or "action:&lt;name&gt;".
///
/// Old configs that stored a bare integer keycode (pre-action-target) are
/// migrated transparently when read.
/// </summary>
public static class FnLockKeymap
{
    // CHOICES

    /// <summary>
    /// All targets selectable in the per-key UI. Order = dropdown order.
    /// First half: keycode targets (concrete keys to emit).
    /// Second half: action targets (internal g-helper actions, mirroring the
    /// Extra window's Key Bindings vocabulary so users see consistent terms).
    /// </summary>
    public static FnLockTarget[] AllChoices => _allChoicesCache ??= BuildAllChoices();
    private static FnLockTarget[]? _allChoicesCache;

    private static FnLockTarget[] BuildAllChoices()
    {
        return new[]
        {
            // Keycode targets
            FnLockTarget.Key(EvdevInterop.KEY_MUTE, Labels.Get("fnlock_target_mute")),
            FnLockTarget.Key(EvdevInterop.KEY_VOLUMEDOWN, Labels.Get("fnlock_target_voldown")),
            FnLockTarget.Key(EvdevInterop.KEY_VOLUMEUP, Labels.Get("fnlock_target_volup")),
            FnLockTarget.Key(EvdevInterop.KEY_BRIGHTNESSDOWN, Labels.Get("fnlock_target_brightdown")),
            FnLockTarget.Key(EvdevInterop.KEY_BRIGHTNESSUP, Labels.Get("fnlock_target_brightup")),
            FnLockTarget.Key(EvdevInterop.KEY_KBDILLUMTOGGLE, Labels.Get("fnlock_target_kbdtoggle")),
            FnLockTarget.Key(EvdevInterop.KEY_KBDILLUMDOWN, Labels.Get("action_brightness_down")),
            FnLockTarget.Key(EvdevInterop.KEY_KBDILLUMUP, Labels.Get("action_brightness_up")),
            FnLockTarget.Key(EvdevInterop.KEY_PLAYPAUSE, Labels.Get("fnlock_target_playpause")),
            FnLockTarget.Key(EvdevInterop.KEY_PREVIOUSSONG, Labels.Get("fnlock_target_prev")),
            FnLockTarget.Key(EvdevInterop.KEY_NEXTSONG, Labels.Get("fnlock_target_next")),
            FnLockTarget.Key(EvdevInterop.KEY_CAMERA, Labels.Get("action_camera")),
            FnLockTarget.Key(EvdevInterop.KEY_TOUCHPAD_TOGGLE, Labels.Get("action_touchpad")),
            FnLockTarget.Key(EvdevInterop.KEY_RFKILL, Labels.Get("fnlock_target_airplane")),
            FnLockTarget.Key(EvdevInterop.KEY_SLEEP, Labels.Get("fnlock_target_sleep")),
            FnLockTarget.Key(EvdevInterop.KEY_SYSRQ, Labels.Get("fnlock_target_printscreen")),
            FnLockTarget.Key(EvdevInterop.KEY_SWITCHVIDEOMODE, Labels.Get("fnlock_target_displayswitch")),
            FnLockTarget.Key(EvdevInterop.KEY_F13, "F13"),
            FnLockTarget.Key(EvdevInterop.KEY_F14, "F14"),
            FnLockTarget.Key(EvdevInterop.KEY_F15, "F15"),
            FnLockTarget.Key(EvdevInterop.KEY_F16, "F16"),

            // Action targets (synced with App.AvailableKeyActions)
            FnLockTarget.Act("ghelper", Labels.Get("action_ghelper")),
            FnLockTarget.Act("performance", Labels.Get("action_performance")),
            FnLockTarget.Act("aura", Labels.Get("action_aura")),
            FnLockTarget.Act("micmute", Labels.Get("action_micmute")),
            FnLockTarget.Act("mute", Labels.Get("action_mute")),
            FnLockTarget.Act("screen_refresh", Labels.Get("action_screen_refresh")),
            FnLockTarget.Act("overdrive", Labels.Get("action_overdrive")),
            FnLockTarget.Act("miniled", Labels.Get("action_miniled")),
            FnLockTarget.Act("camera", Labels.Get("action_camera")),
            FnLockTarget.Act("touchpad", Labels.Get("action_touchpad")),
        };
    }

    /// <summary>
    /// Invalidate the cached AllChoices array so it picks up new translations
    /// after a language change.
    /// </summary>
    public static void InvalidateCache() => _allChoicesCache = null;

    /// <summary>
    /// Resolve display name for a persisted tag. Used by <see cref="FnLockTarget.FromTag"/>
    /// to recover human-readable names when reading config back.
    /// </summary>
    public static string ResolveDisplayName(string tag)
    {
        foreach (var t in AllChoices)
            if (t.Tag == tag)
                return t.DisplayName;
        // Fall back: parse the keycode out so unknown keys still get something.
        if (tag.StartsWith("key:") && ushort.TryParse(tag.AsSpan(4), out var code))
            return Labels.Format("fnlock_target_unknown", code);
        return tag;
    }

    // GENERIC DEFAULTS

    private static Dictionary<ushort, FnLockTarget> GenericDefaults() => new()
    {
        [EvdevInterop.KEY_F1] = FindByTag("key:113")!,                        // Mute
        [EvdevInterop.KEY_F2] = FindByTag($"key:{EvdevInterop.KEY_KBDILLUMDOWN}")!, // Kbd brightness down
        [EvdevInterop.KEY_F3] = FindByTag($"key:{EvdevInterop.KEY_KBDILLUMUP}")!,   // Kbd brightness up
        [EvdevInterop.KEY_F4] = FindByTag("action:aura")!,                    // Cycle Aura
        [EvdevInterop.KEY_F5] = FindByTag("action:performance")!,             // Cycle Performance
        [EvdevInterop.KEY_F6] = FindByTag($"key:{EvdevInterop.KEY_SYSRQ}")!,  // PrintScreen
        [EvdevInterop.KEY_F7] = FindByTag($"key:{EvdevInterop.KEY_BRIGHTNESSDOWN}")!,
        [EvdevInterop.KEY_F8] = FindByTag($"key:{EvdevInterop.KEY_BRIGHTNESSUP}")!,
        [EvdevInterop.KEY_F9] = FindByTag($"key:{EvdevInterop.KEY_SWITCHVIDEOMODE}")!, // Project / display switch
        [EvdevInterop.KEY_F10] = FindByTag($"key:{EvdevInterop.KEY_TOUCHPAD_TOGGLE}")!,
        [EvdevInterop.KEY_F11] = FindByTag($"key:{EvdevInterop.KEY_SLEEP}")!,
        [EvdevInterop.KEY_F12] = FindByTag("action:ghelper")!,                // Toggle G-Helper
    };

    private static FnLockTarget? FindByTag(string tag)
    {
        foreach (var t in AllChoices)
            if (t.Tag == tag)
                return t;
        return null;
    }

    // PER-MODEL OVERRIDES

    /// <summary>
    /// Substring matchers for ASUS models that lack dedicated M keys.
    /// Mirrors <c>AppConfig.NoMKeys()</c> in Windows g-helper:
    /// (Z13 &amp;&amp; !IsARCNM) || FX706 || FA706 || FA506 || FX506 || Duo || FX505.
    /// Z13 inclusion is conditional (excluded for ARCNM/GZ301VIC) so it's
    /// handled by <see cref="MatchesNoMKeys"/> rather than this raw list.
    /// </summary>
    private static readonly string[] NoMKeysSubstrings =
    {
        "FX706", "FA706", "FA506", "FX506", "Duo", "FX505",
    };

    /// <summary>
    /// Substring matchers for the G14 2020 / G712L / GX502L family that has
    /// media transport on F2/F3/F4. Mirrors <c>AppConfig.MediaKeys()</c> plus
    /// NoAura models (GA502IU, HN7306, M6500X) which have media keys etched
    /// on the keycaps. GA401I (excluding GA401IHR) handled by helper below.
    /// </summary>
    private static readonly string[] MediaKeysSubstrings =
    {
        "G712L", "GX502L", "GA502IU", "HN7306", "M6500X",
    };

    /// <summary>Z13 / Duo family. F11 = backlight cycle.</summary>
    private static readonly string[] Z13DuoSubstrings =
    {
        "Z13", "Duo", "GX550", "GX551", "GX650", "UX840", "UX482",
    };

    /// <summary>GA401I but NOT GA401IHR (used by MediaKeys path).</summary>
    private static bool MatchesGA401I(string product)
    {
        return product.Contains("GA401I", StringComparison.OrdinalIgnoreCase)
               && !product.Contains("GA401IHR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the model qualifies for the NoMKeys override.
    /// Encodes the conditional Z13 inclusion: Z13 yes, GZ301VIC (ARCNM) no.</summary>
    private static bool MatchesNoMKeys(string product)
    {
        if (ContainsAny(product, NoMKeysSubstrings))
            return true;
        // Z13 family, but exclude ARCNM (GZ301VIC) which has a different layout.
        if (product.Contains("Z13", StringComparison.OrdinalIgnoreCase)
            && !product.Contains("GZ301VIC", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool ContainsAny(string product, string[] needles)
    {
        foreach (var n in needles)
            if (product.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Apply NoMKeys overrides: F2/F3 = Volume, F4 = MicMute.
    /// </summary>
    private static void ApplyNoMKeys(Dictionary<ushort, FnLockTarget> map)
    {
        var t1 = FindByTag($"key:{EvdevInterop.KEY_VOLUMEDOWN}");
        var t2 = FindByTag($"key:{EvdevInterop.KEY_VOLUMEUP}");
        var t3 = FindByTag("action:micmute");
        if (t1 != null)
            map[EvdevInterop.KEY_F2] = t1;
        if (t2 != null)
            map[EvdevInterop.KEY_F3] = t2;
        if (t3 != null)
            map[EvdevInterop.KEY_F4] = t3;
    }

    /// <summary>
    /// Apply MediaKeys overrides: F2/F3/F4 = Prev/Play/Next.
    /// </summary>
    private static void ApplyMediaKeys(Dictionary<ushort, FnLockTarget> map)
    {
        var t1 = FindByTag($"key:{EvdevInterop.KEY_PREVIOUSSONG}");
        var t2 = FindByTag($"key:{EvdevInterop.KEY_PLAYPAUSE}");
        var t3 = FindByTag($"key:{EvdevInterop.KEY_NEXTSONG}");
        if (t1 != null)
            map[EvdevInterop.KEY_F2] = t1;
        if (t2 != null)
            map[EvdevInterop.KEY_F3] = t2;
        if (t3 != null)
            map[EvdevInterop.KEY_F4] = t3;
    }

    /// <summary>
    /// Apply ProArt overrides. Mirrors Windows InputDispatcher ProArt block:
    /// F2/F3=Volume, F4=Aura, F5=BrightDown, F6=BrightUp,
    /// F7=DisplaySwitch (Win+P equivalent), F9=MicMute,
    /// F10=Camera, F11=PrintScreen. F8 (emoji) skipped - too DE-specific.
    /// </summary>
    private static void ApplyProArt(Dictionary<ushort, FnLockTarget> map)
    {
        var f2 = FindByTag($"key:{EvdevInterop.KEY_VOLUMEDOWN}");
        var f3 = FindByTag($"key:{EvdevInterop.KEY_VOLUMEUP}");
        var f4 = FindByTag("action:aura");
        var f5 = FindByTag($"key:{EvdevInterop.KEY_BRIGHTNESSDOWN}");
        var f6 = FindByTag($"key:{EvdevInterop.KEY_BRIGHTNESSUP}");
        var f7 = FindByTag($"key:{EvdevInterop.KEY_SWITCHVIDEOMODE}");
        var f9 = FindByTag("action:micmute");
        var f10 = FindByTag("action:camera");
        var f11 = FindByTag($"key:{EvdevInterop.KEY_SYSRQ}");
        if (f2 != null)
            map[EvdevInterop.KEY_F2] = f2;
        if (f3 != null)
            map[EvdevInterop.KEY_F3] = f3;
        if (f4 != null)
            map[EvdevInterop.KEY_F4] = f4;
        if (f5 != null)
            map[EvdevInterop.KEY_F5] = f5;
        if (f6 != null)
            map[EvdevInterop.KEY_F6] = f6;
        if (f7 != null)
            map[EvdevInterop.KEY_F7] = f7;
        if (f9 != null)
            map[EvdevInterop.KEY_F9] = f9;
        if (f10 != null)
            map[EvdevInterop.KEY_F10] = f10;
        if (f11 != null)
            map[EvdevInterop.KEY_F11] = f11;
    }

    /// <summary>
    /// Apply Z13 / DUO override: F11 = keyboard backlight toggle.
    /// </summary>
    private static void ApplyZ13Duo(Dictionary<ushort, FnLockTarget> map)
    {
        var t = FindByTag($"key:{EvdevInterop.KEY_KBDILLUMTOGGLE}");
        if (t != null)
            map[EvdevInterop.KEY_F11] = t;
    }

    // RESOLUTION

    /// <summary>
    /// Resolve the effective map for the running machine. Combines generic +
    /// model-specific + user overrides. Returns a fresh dictionary; the
    /// caller may retain or further mutate it.
    /// </summary>
    public static Dictionary<ushort, FnLockTarget> ResolveActiveMap()
    {
        var map = GenericDefaults();
        string product = AppConfig.GetModel() ?? "";

        // Order matters: NoMKeys first, MediaKeys second. Windows
        // InputDispatcher.KeyPressed checks NoMKeys then MediaKeys with
        // separate `if` blocks, each early-returning on match. Here we apply
        // both in sequence so MediaKeys wins on overlapping keys when both
        // match (matches Windows behavior since GA401I never hits NoMKeys
        // there - it's not in the NoMKeys substring list).
        if (MatchesNoMKeys(product))
            ApplyNoMKeys(map);

        // GA401I (excl GA401IHR) gets MediaKeys ONLY, not NoMKeys.
        if (ContainsAny(product, MediaKeysSubstrings) || MatchesGA401I(product))
            ApplyMediaKeys(map);

        if (product.Contains("ProArt", StringComparison.OrdinalIgnoreCase))
            ApplyProArt(map);

        if (ContainsAny(product, Z13DuoSubstrings))
            ApplyZ13Duo(map);

        // User overrides last. Persistence:
        //   - new format: "key:<n>" or "action:<name>" (string)
        //   - legacy:     bare integer keycode
        //   - sentinel:   "-1" or "" → no override (use default)
        foreach (ushort fkey in EvdevInterop.FunctionKeys)
        {
            string? raw = AppConfig.GetString($"fnlock_map_{fkey}");
            if (string.IsNullOrEmpty(raw) || raw == "-1")
                continue;

            FnLockTarget? t = null;
            if (raw.Contains(':'))
            {
                t = FnLockTarget.FromTag(raw);
            }
            else if (int.TryParse(raw, out int legacyCode) && legacyCode > 0 && legacyCode < EvdevInterop.KEY_MAX)
            {
                // Legacy migration: bare keycode int → wrap as key:<n>.
                t = FnLockTarget.Key((ushort)legacyCode, ResolveDisplayName($"key:{legacyCode}"));
            }

            if (t != null)
                map[fkey] = t;
        }

        return map;
    }
}
