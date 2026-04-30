namespace GHelper.Linux.Ally;

/// <summary>
/// Static catalog of every input that can be bound to an Ally button.
/// Ported VERBATIM from Windows g-helper AllyControl.cs lines 132-308 - codes
/// MUST match exactly because the EC firmware looks them up by hex value.
///
/// Each entry pairs a wire <see cref="Code"/> (HID byte string) with a
/// <see cref="LabelKey"/>. Hardware names that are universally untranslated
/// across languages (e.g. "F1", "Q", "Num0", "M1", "A", "Esc", "Tab") are
/// stored as raw English strings via the IsLiteral helper - those entries
/// never look up the i18n table. Translatable strings ("L-Trigger", "Show
/// Desktop", etc.) carry an i18n key that <c>Labels.Get(key)</c> resolves.
///
/// First entry of each group is a divider for the dropdown (Code = "" + a
/// "----------" placeholder name). The UI layer renders entries with
/// non-empty Code as selectable rows and entries with empty Code as
/// non-selectable separators.
/// </summary>
public static class BindingGroups
{
    /// <summary>Disabled (no key) marker code - matches Windows convention.</summary>
    public const string Disabled = "00-00";

    // Controller buttons
    public const string BindA = "01-01";
    public const string BindB = "01-02";
    public const string BindX = "01-03";
    public const string BindY = "01-04";
    public const string BindLB = "01-05";
    public const string BindRB = "01-06";
    public const string BindLS = "01-07";
    public const string BindRS = "01-08";
    public const string BindDU = "01-09";
    public const string BindDD = "01-0A";
    public const string BindDL = "01-0B";
    public const string BindDR = "01-0C";
    public const string BindLT = "01-0D";
    public const string BindRT = "01-0E";
    public const string BindVB = "01-11";
    public const string BindMB = "01-12";
    public const string BindXB = "01-13";
    public const string BindM1 = "02-8F";
    public const string BindM2 = "02-8E";

    // Mouse
    public const string BindMouseL = "03-01";
    public const string BindMouseR = "03-02";

    // Keyboard arrows
    public const string BindKBU = "02-98";
    public const string BindKBD = "02-99";
    public const string BindKBL = "02-9A";
    public const string BindKBR = "02-9B";

    // Modifiers (single-byte page-2 codes)
    public const string BindTab = "02-0D";
    public const string BindEnter = "02-5A";
    public const string BindBack = "02-66";
    public const string BindEsc = "02-76";
    public const string BindPgU = "02-96";
    public const string BindPgD = "02-97";
    public const string BindShift = "02-88";
    public const string BindCtrl = "02-8C";
    public const string BindAlt = "02-8A";
    public const string BindWin = "02-82";

    // Multi-key combos (page 4)
    public const string BindTaskManager = "04-03-8C-88-76";  // Ctrl+Shift+Esc
    public const string BindCloseWindow = "04-02-8A-0C";     // Alt+F4
    public const string BindBrightnessDown = "04-04-8C-88-8A-05";
    public const string BindBrightnessUp = "04-04-8C-88-8A-06";
    public const string BindXGM = "04-04-8C-88-8A-04";
    public const string BindToggleMode = "04-04-8C-88-8A-0C";
    public const string BindToggleFPSLimit = "04-04-8C-88-8A-01";
    public const string BindToggleTouchScreen = "04-04-8C-88-8A-0B";
    public const string BindOverlay = "04-03-8C-88-44";       // AMD overlay
    public const string BindShiftTab = "04-02-88-0D";
    public const string BindAltTab = "04-02-8A-0D";
    public const string BindWinTab = "04-02-82-0D";
    public const string BindWinP = "04-02-82-4D";             // Project mode
    public const string BindWinH = "04-02-82-33";             // Dictation
    public const string BindScreenshot = "04-03-82-88-1B";   // Win+Shift+S
    public const string BindShowDesktop = "04-02-82-23";     // Win+D

    // System (page 5)
    public const string BindVolUp = "05-03";
    public const string BindVolDown = "05-02";
    public const string BindShowKeyboard = "05-19";

    // Misc
    public const string BindPrintScrn = "02-C3";
    public const string BindPause = "02-91";

    /// <summary>
    /// One row in a binding-group dropdown.
    /// <para>If <see cref="IsLiteral"/> is true, <see cref="Display"/> is
    /// rendered as-is (used for universal symbols: F1..F12, single
    /// alphanumerics, M1/M2, controller letters A/B/X/Y, etc.).</para>
    /// <para>Otherwise <see cref="Display"/> holds an i18n key that the UI
    /// resolves via <c>Labels.Get(...)</c> at render time.</para>
    /// </summary>
    public readonly record struct Entry(string Code, string Display, bool IsLiteral = false);

    /// <summary>
    /// All binding groups, displayed in dropdown in this order. Group label
    /// is itself an i18n key (<c>"ally_grp_*"</c>) for the non-empty groups;
    /// empty string indicates the leading "header" group with the disable
    /// row.
    /// </summary>
    public static readonly IReadOnlyList<(string GroupLabelKey, IReadOnlyList<Entry> Items)> Groups =
    new (string, IReadOnlyList<Entry>)[]
    {
        ("", new[]
        {
            new Entry("",         "----------", IsLiteral: true),
            new Entry(Disabled,   "bind_disabled"),
        }),
        ("ally_grp_controller", new[]
        {
            new Entry(BindA,  "A",  IsLiteral: true),
            new Entry(BindB,  "B",  IsLiteral: true),
            new Entry(BindX,  "X",  IsLiteral: true),
            new Entry(BindY,  "Y",  IsLiteral: true),
            new Entry(BindLT, "btn_l_trigger"),
            new Entry(BindRT, "btn_r_trigger"),
            new Entry(BindLB, "btn_l_bumper"),
            new Entry(BindRB, "btn_r_bumper"),
            new Entry(BindLS, "btn_l_stick_click"),
            new Entry(BindRS, "btn_r_stick_click"),
            new Entry(BindDU, "btn_dpad_up"),
            new Entry(BindDD, "btn_dpad_down"),
            new Entry(BindDL, "btn_dpad_left"),
            new Entry(BindDR, "btn_dpad_right"),
            new Entry(BindVB, "btn_view"),
            new Entry(BindMB, "btn_menu"),
            new Entry(BindXB, "XBox/Steam", IsLiteral: true),
            new Entry(BindM1, "M1", IsLiteral: true),
            new Entry(BindM2, "M2", IsLiteral: true),
        }),
        ("ally_grp_mouse", new[]
        {
            new Entry(BindMouseL, "bind_mouse_left"),
            new Entry(BindMouseR, "bind_mouse_right"),
            new Entry("03-03",    "bind_mouse_middle"),
            new Entry("03-04",    "bind_mouse_scrollup"),
            new Entry("03-05",    "bind_mouse_scrolldn"),
        }),
        ("ally_grp_system", new[]
        {
            new Entry(BindToggleMode,        "bind_act_controller_mode"),
            new Entry(BindToggleFPSLimit,    "bind_act_fps_limit"),
            new Entry(BindToggleTouchScreen, "bind_act_touch_screen"),
            new Entry(BindVolUp,             "bind_act_vol_up"),
            new Entry(BindVolDown,           "bind_act_vol_down"),
            new Entry(BindBrightnessUp,      "bind_act_bright_up"),
            new Entry(BindBrightnessDown,    "bind_act_bright_down"),
            new Entry(BindShowKeyboard,      "bind_act_show_keyboard"),
            new Entry(BindShowDesktop,       "bind_act_show_desktop"),
            new Entry(BindScreenshot,        "bind_act_screenshot"),
            new Entry(BindOverlay,           "bind_act_amd_overlay"),
            new Entry(BindTaskManager,       "bind_act_task_manager"),
            new Entry(BindCloseWindow,       "bind_act_close_window"),
            new Entry(BindShiftTab,          "Shift-Tab",  IsLiteral: true),
            new Entry(BindAltTab,            "Alt-Tab",    IsLiteral: true),
            new Entry(BindWinTab,            "Win-Tab",    IsLiteral: true),
            new Entry(BindXGM,               "bind_act_xgm_toggle"),
            new Entry(BindWinP,              "bind_act_project_mode"),
            new Entry("05-1E",               "bind_act_start_recording"),
            new Entry("05-01",               "bind_act_mic_off"),
        }),
        ("ally_grp_modifiers", new[]
        {
            new Entry(BindEsc,       "Esc",       IsLiteral: true),
            new Entry(BindBack,      "bind_mod_backspace"),
            new Entry(BindTab,       "Tab",       IsLiteral: true),
            new Entry(BindEnter,     "Enter",     IsLiteral: true),
            new Entry(BindShift,     "L-Shift",   IsLiteral: true),
            new Entry(BindAlt,       "L-Alt",     IsLiteral: true),
            new Entry(BindCtrl,      "L-Ctl",     IsLiteral: true),
            new Entry(BindWin,       "Win",       IsLiteral: true),
            new Entry("02-89",       "R-Shift",   IsLiteral: true),
            new Entry("02-8B",       "R-Alt",     IsLiteral: true),
            new Entry("02-8D",       "R-Ctl",     IsLiteral: true),
            new Entry("02-84",       "bind_mod_app_menu"),
            new Entry("02-58",       "bind_mod_caps"),
            new Entry("02-29",       "bind_mod_space"),
            new Entry(BindPrintScrn, "bind_mod_print_scrn"),
            new Entry(BindPause,     "bind_mod_pause"),
            new Entry("02-7E",       "bind_mod_scrlk"),
        }),
        ("ally_grp_navigation", new[]
        {
            new Entry(BindPgU, "bind_nav_pgup"),
            new Entry(BindPgD, "bind_nav_pgdn"),
            new Entry(BindKBU, "bind_nav_uparr"),
            new Entry(BindKBD, "bind_nav_downarr"),
            new Entry(BindKBL, "bind_nav_leftarr"),
            new Entry(BindKBR, "bind_nav_rightarr"),
            new Entry("02-C2", "bind_nav_insert"),
            new Entry("02-C0", "bind_nav_delete"),
            new Entry("02-94", "bind_nav_home"),
            new Entry("02-95", "bind_nav_end"),
        }),
        ("ally_grp_fkeys", new[]
        {
            new Entry("02-05", "F1",  IsLiteral: true),
            new Entry("02-06", "F2",  IsLiteral: true),
            new Entry("02-04", "F3",  IsLiteral: true),
            new Entry("02-0C", "F4",  IsLiteral: true),
            new Entry("02-03", "F5",  IsLiteral: true),
            new Entry("02-0B", "F6",  IsLiteral: true),
            new Entry("02-80", "F7",  IsLiteral: true),
            new Entry("02-0A", "F8",  IsLiteral: true),
            new Entry("02-01", "F9",  IsLiteral: true),
            new Entry("02-09", "F10", IsLiteral: true),
            new Entry("02-78", "F11", IsLiteral: true),
            new Entry("02-07", "F12", IsLiteral: true),
        }),
        ("ally_grp_keyboard", new[]
        {
            new Entry("02-0E", "`",  IsLiteral: true),
            new Entry("02-16", "1",  IsLiteral: true),
            new Entry("02-1E", "2",  IsLiteral: true),
            new Entry("02-26", "3",  IsLiteral: true),
            new Entry("02-25", "4",  IsLiteral: true),
            new Entry("02-2E", "5",  IsLiteral: true),
            new Entry("02-36", "6",  IsLiteral: true),
            new Entry("02-3D", "7",  IsLiteral: true),
            new Entry("02-3E", "8",  IsLiteral: true),
            new Entry("02-46", "9",  IsLiteral: true),
            new Entry("02-45", "0",  IsLiteral: true),
            new Entry("02-4E", "-",  IsLiteral: true),
            new Entry("02-55", "=",  IsLiteral: true),
            new Entry("02-15", "Q",  IsLiteral: true),
            new Entry("02-1D", "W",  IsLiteral: true),
            new Entry("02-24", "E",  IsLiteral: true),
            new Entry("02-2D", "R",  IsLiteral: true),
            new Entry("02-2C", "T",  IsLiteral: true),
            new Entry("02-35", "Y",  IsLiteral: true),
            new Entry("02-3C", "U",  IsLiteral: true),
            new Entry("02-44", "O",  IsLiteral: true),
            new Entry("02-4D", "P",  IsLiteral: true),
            new Entry("02-54", "[",  IsLiteral: true),
            new Entry("02-5B", "]",  IsLiteral: true),
            new Entry("02-5D", "|",  IsLiteral: true),
            new Entry("02-1C", "A",  IsLiteral: true),
            new Entry("02-1B", "S",  IsLiteral: true),
            new Entry("02-23", "D",  IsLiteral: true),
            new Entry("02-2B", "F",  IsLiteral: true),
            new Entry("02-34", "G",  IsLiteral: true),
            new Entry("02-33", "H",  IsLiteral: true),
            new Entry("02-3B", "J",  IsLiteral: true),
            new Entry("02-42", "K",  IsLiteral: true),
            new Entry("02-4B", "L",  IsLiteral: true),
            new Entry("02-4C", ";",  IsLiteral: true),
            new Entry("02-52", "'",  IsLiteral: true),
            new Entry("02-22", "X",  IsLiteral: true),
            new Entry("02-1A", "Z",  IsLiteral: true),
            new Entry("02-21", "C",  IsLiteral: true),
            new Entry("02-2A", "V",  IsLiteral: true),
            new Entry("02-32", "B",  IsLiteral: true),
            new Entry("02-31", "N",  IsLiteral: true),
            new Entry("02-3A", "M",  IsLiteral: true),
            new Entry("02-41", ",",  IsLiteral: true),
            new Entry("02-49", ".",  IsLiteral: true),
        }),
        ("ally_grp_numpad", new[]
        {
            new Entry("02-77", "NumLock",   IsLiteral: true),
            new Entry("02-90", "NumSlash",  IsLiteral: true),
            new Entry("02-7C", "NumStar",   IsLiteral: true),
            new Entry("02-7B", "NumHyphen", IsLiteral: true),
            new Entry("02-79", "NumPlus",   IsLiteral: true),
            new Entry("02-81", "NumEnter",  IsLiteral: true),
            new Entry("02-71", "NumPeriod", IsLiteral: true),
            new Entry("02-70", "Num0", IsLiteral: true),
            new Entry("02-69", "Num1", IsLiteral: true),
            new Entry("02-72", "Num2", IsLiteral: true),
            new Entry("02-7A", "Num3", IsLiteral: true),
            new Entry("02-6B", "Num4", IsLiteral: true),
            new Entry("02-73", "Num5", IsLiteral: true),
            new Entry("02-74", "Num6", IsLiteral: true),
            new Entry("02-6C", "Num7", IsLiteral: true),
            new Entry("02-75", "Num8", IsLiteral: true),
            new Entry("02-7D", "Num9", IsLiteral: true),
        }),
    };
}
