namespace GHelper.Linux.I18n;

/// <summary>
/// Internationalization label manager.
/// Loads translations from language dictionaries, supports runtime switching,
/// auto-detects system locale, and persists user preference.
/// </summary>
public static class Labels
{
    private static Dictionary<string, string> _current = new();
    private static Dictionary<string, string> _english = new();

    /// <summary>Fired after language changes - all windows should re-apply labels.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Current language code (e.g. "en", "ru", "zh-cn").</summary>
    public static string CurrentLanguage { get; private set; } = "en";

    /// <summary>
    /// Available languages: code → loader function.
    /// Each loader returns a fresh dictionary of translations.
    /// Missing keys fall back to English.
    /// </summary>
    public static readonly Dictionary<string, Func<Dictionary<string, string>>> LanguageLoaders = new()
    {
        { "en", () => Languages.English.Translations },
    };

    /// <summary>
    /// Display names for each language.
    /// </summary>
    public static readonly (string Code, string NativeName)[] AvailableLanguages =
    {
        ("en", "English"),
    };

    /// <summary>
    /// Initialize i18n system. Call once at app startup before any UI is created.
    /// Checks user preference first, then falls back to system locale.
    /// </summary>
    public static void Initialize()
    {
        _english = Languages.English.Translations;

        // User explicitly chose a language → respect it forever
        string? saved = Helpers.AppConfig.GetString("language");
        if (!string.IsNullOrEmpty(saved) && LanguageLoaders.ContainsKey(saved))
        {
            SetLanguageInternal(saved);
            return;
        }

        // Auto-detect from system locale
        string detected = DetectLocale();
        SetLanguageInternal(detected);
    }

    /// <summary>
    /// Get the translated string for a key.
    /// Falls back to English if not found in current language, then to the key itself.
    /// Supports composite format: Labels.Get("key") with string.Format() for {0}, {1}...
    /// </summary>
    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var val))
            return val;
        if (_english.TryGetValue(key, out var en))
            return en;
        return key;
    }

    /// <summary>
    /// Get a translated format string and apply arguments.
    /// Example: Labels.Format("cpu_fan_info", "65\u00b0C", "2100RPM")
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }

    /// <summary>
    /// Switch language at runtime. Persists the choice and fires LanguageChanged.
    /// </summary>
    public static void SetLanguage(string code)
    {
        SetLanguageInternal(code);
        Helpers.AppConfig.Set("language", code);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Reset to auto-detected locale. Clears saved preference and fires LanguageChanged.
    /// </summary>
    public static void ResetToAuto()
    {
        Helpers.AppConfig.Set("language", "");
        string detected = DetectLocale();
        SetLanguageInternal(detected);
        LanguageChanged?.Invoke();
    }

    private static void SetLanguageInternal(string code)
    {
        if (LanguageLoaders.TryGetValue(code, out var loader))
        {
            _current = loader();
            CurrentLanguage = code;
        }
        else
        {
            _current = _english;
            CurrentLanguage = "en";
        }
    }

    /// <summary>
    /// Detect language from system locale environment variables.
    /// Tries LANG, LC_ALL, LC_MESSAGES in order.
    /// Parses "en_US.UTF-8" → "en", "zh_CN.UTF-8" → "zh-cn".
    /// </summary>
    private static string DetectLocale()
    {
        string? lang = Environment.GetEnvironmentVariable("LANG")
                    ?? Environment.GetEnvironmentVariable("LC_ALL")
                    ?? Environment.GetEnvironmentVariable("LC_MESSAGES");

        if (string.IsNullOrEmpty(lang))
            return "en";

        // Remove encoding: "en_US.UTF-8" → "en_US"
        string code = lang.Split('.')[0];

        // Try full match with country: "zh_CN" → "zh-cn", "pt_BR" → "pt-br"
        string full = code.Replace('_', '-').ToLowerInvariant();
        if (LanguageLoaders.ContainsKey(full))
            return full;

        // Try language only: "en_US" → "en"
        string langOnly = code.Split('_')[0].ToLowerInvariant();
        if (LanguageLoaders.ContainsKey(langOnly))
            return langOnly;

        // Special cases: "no" → "nb" (Norwegian Bokmål)
        if (langOnly == "no" && LanguageLoaders.ContainsKey("nb"))
            return "nb";

        // "tl" (Tagalog, ISO 639-1) - "fil" (Filipino): many distros ship the
        // Philippine locale as tl_PH rather than fil_PH, so map it explicitly.
        if (langOnly == "tl" && LanguageLoaders.ContainsKey("fil"))
            return "fil";

        return "en";
    }
}
