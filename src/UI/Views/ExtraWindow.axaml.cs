using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using GHelper.Linux.Display;
using GHelper.Linux.I18n;
using GHelper.Linux.Platform.Linux;
using GHelper.Linux.USB;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Extra settings window - keyboard backlight power zones,
/// display, power management, system info, advanced options.
/// Linux port of G-Helper's Extra form.
/// </summary>
public partial class ExtraWindow : Window
{
    private bool _suppressEvents = true;

    /// <summary>PID of the systemd-inhibit process for clamshell mode, or -1 if inactive.</summary>
    private static int _clamshellInhibitPid = -1;

    /// <summary>Polls display brightness sysfs every 2s to catch external changes (physical Fn keys).</summary>
    private Avalonia.Threading.DispatcherTimer? _brightnessTimer;

    public ExtraWindow()
    {
        InitializeComponent();
        Labels.LanguageChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            bool prev = _suppressEvents;
            _suppressEvents = true;
            ApplyLabels();
            _suppressEvents = prev;
        });
        Loaded += (_, _) =>
        {
            _suppressEvents = true;
            InitLanguage();
            InitAppearance();
            InitKeyboardBacklight();
            InitKeyBindings();
            RefreshDisplay();
            RefreshGpuBackend();
            RefreshOther();
            RefreshTrayIcons();
            RefreshPower();
            RefreshSystemInfo();
            RefreshAdvanced();
            ApplyLabels();
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _suppressEvents = false,
                Avalonia.Threading.DispatcherPriority.Background);

            StartBrightnessPolling();
        };
        Closed += (_, _) =>
        {
            _brightnessTimer?.Stop();
        };
    }

    // LANGUAGE

    private void InitLanguage()
    {
        comboLanguage.Items.Clear();

        // First item: Auto (system locale)
        comboLanguage.Items.Add(new ComboBoxItem { Content = Labels.Get("language_auto"), Tag = "" });

        // All available languages
        int selectedIdx = 0;
        int idx = 1;
        string? savedLang = Helpers.AppConfig.GetString("language");

        foreach (var (code, name) in Labels.AvailableLanguages)
        {
            comboLanguage.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            if (!string.IsNullOrEmpty(savedLang) && code == savedLang)
                selectedIdx = idx;
            idx++;
        }

        comboLanguage.SelectedIndex = selectedIdx;
    }

    private void ComboLanguage_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (comboLanguage.SelectedItem is not ComboBoxItem item)
            return;

        string code = item.Tag as string ?? "";

        if (string.IsNullOrEmpty(code))
            Labels.ResetToAuto();
        else
            Labels.SetLanguage(code);
    }

    // APPEARANCE (icon set)

    private void InitAppearance()
    {
        // Populate dropdown from the compile-time set registry. Items are
        // ordered by IconSets.AvailableSlugs (alphabetical by slug); display
        // names are derived via title-casing, no i18n entries required.
        comboIconSet.Items.Clear();
        int selected = 0;
        string saved = UI.Controls.IconSets.Normalize(
            Helpers.AppConfig.GetString("icon_set", UI.Controls.IconSets.Default));
        for (int i = 0; i < UI.Controls.IconSets.AvailableSlugs.Count; i++)
        {
            string slug = UI.Controls.IconSets.AvailableSlugs[i];
            comboIconSet.Items.Add(new ComboBoxItem
            {
                Content = UI.Controls.IconSets.DisplayName(slug),
                Tag = slug,
            });
            if (slug == saved)
                selected = i;
        }
        comboIconSet.SelectedIndex = selected;
    }

    private void ComboIconSet_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (comboIconSet.SelectedItem is not ComboBoxItem item)
            return;

        string newSet = item.Tag as string ?? "noto";
        if (newSet == App.IconSet)
            return;

        // Persist for next launch, then flip the static property. The setter
        // raises IconSetChanged on the UI thread, which every attached Icon
        // control is listening for - they rebuild their SVG in place.
        Helpers.AppConfig.Set("icon_set", newSet);
        App.IconSet = newSet;
    }

    private void ApplyLabels()
    {
        Title = Labels.Get("extra_title");

        // Language section
        labelLanguageHeader.Text = Labels.Get("language_header");
        labelLanguageLabel.Text = Labels.Get("language_header");

        // Appearance section
        labelAppearanceHeader.Text = Labels.Get("appearance_header");
        labelIconSetLabel.Text = Labels.Get("icon_set_label");

        // Keyboard Backlight
        headerKbdBacklight.Text = Labels.Get("kbd_backlight_header");
        labelKbdBrightnessLabel.Text = Labels.Get("brightness");
        labelAnimSpeed.Text = Labels.Get("animation_speed");
        labelPowerZones.Text = Labels.Get("power_zones");
        labelColAwake.Text = Labels.Get("awake");
        labelColBoot.Text = Labels.Get("boot");
        labelColSleep.Text = Labels.Get("sleep");
        labelColShutdown.Text = Labels.Get("shutdown");
        labelColBattery.Text = Labels.Get("battery");
        labelRowKeyboard.Text = Labels.Get("kbd_keyboard");
        labelRowLogo.Text = Labels.Get("kbd_logo");
        labelRowLightbar.Text = Labels.Get("kbd_lightbar");
        labelRowLid.Text = Labels.Get("kbd_lid");

        // Display
        headerDisplay.Text = Labels.Get("display_header");
        labelControllerLabel.Text = Labels.Get("controller");
        labelDisplayBrightnessLabel.Text = Labels.Get("brightness");
        checkOverdrive.Content = Labels.Get("panel_overdrive_check");
        labelOptimalBrightness.Text = Labels.Get("optimal_brightness_label");
        labelGammaLabel.Text = Labels.Get("gamma");

        // Other
        headerOther.Text = Labels.Get("other_header");
        checkBootSound.Content = Labels.Get("boot_sound");
        checkTopmost.Content = Labels.Get("window_topmost");
        checkBWIcon.Content = Labels.Get("bw_tray_icon");
        checkClamshell.Content = Labels.Get("clamshell_mode");
        // Ally has no lid - clamshell-mode toggle is meaningless on a handheld.
        checkClamshell.IsVisible = !Helpers.AppConfig.IsAlly();

        // ROG Ally: APU UMA buffer combo. Read possible_values from the
        // kernel; show the panel only when the attribute is exposed (newer
        // asus-armoury on AMD APU systems).
        labelApuMemHeader.Text = Labels.Get("ally_apu_mem_header");
        labelApuMemValue.Text = Labels.Get("ally_apu_mem_label");
        labelApuMemReboot.Text = Labels.Get("ally_apu_mem_reboot_required");
        InitApuMem();

        // XG Mobile dock controls. Visible only when a real dock is on the bus.
        labelXgmHeader.Text = Labels.Get("xgm_extra_header");
        checkXGMLights.Content = Labels.Get("xgm_extra_lights_label");
        labelXgmBrightnessLabel.Text = Labels.Get("xgm_extra_brightness_label");
        InitXgmPanel();
        checkSilentStart.Content = Labels.Get("start_minimized");
        checkDisableOsd.Content = Labels.Get("disable_osd_label");
        checkKeepBacklight.Content = Labels.Get("keep_backlight_on");
        checkCamera.Content = Labels.Get("camera");
        checkTouchpad.Content = Labels.Get("touchpad");
        checkTouchscreen.Content = Labels.Get("touchscreen");

        // System Tray Icons
        headerTrayIcons.Text = Labels.Get("tray_icons_header");
        checkCpuTrayIcon.Content = Labels.Get("cpu_temp_tray");
        checkGpuTrayIcon.Content = Labels.Get("gpu_temp_tray");
        checkCpuTrayTransparent.Content = Labels.Get("tray_bg_transparent");
        checkGpuTrayTransparent.Content = Labels.Get("tray_bg_transparent");
        ToolTip.SetTip(btnCpuTrayBg, Labels.Get("tray_bg_color"));
        ToolTip.SetTip(btnCpuTrayText, Labels.Get("tray_text_color"));
        ToolTip.SetTip(btnGpuTrayBg, Labels.Get("tray_bg_color"));
        ToolTip.SetTip(btnGpuTrayText, Labels.Get("tray_text_color"));

        // Key Bindings
        headerKeyBindings.Text = Labels.Get("key_bindings_header");
        labelKeyM4.Text = Labels.Get("key_rog_m5");
        labelKeyFnF4.Text = Labels.Get("key_fnf4_aura");
        labelKeyFnF5.Text = Labels.Get("key_fnf5_m4");

        // Power Management
        headerPowerMgmt.Text = Labels.Get("power_mgmt_header");
        labelProfileSilent.Text = Labels.Get("profile_silent_label");
        labelProfileBalanced.Text = Labels.Get("profile_balanced_label");
        labelProfileTurbo.Text = Labels.Get("profile_turbo_label");
        labelBatteryDetails.Text = Labels.Get("details");

        // System Info
        headerSystemInfo.Text = Labels.Get("system_info_header");
        labelSystemInfoMore.Text = Labels.Get("details");

        // Function Key Remap (Details button under Key Bindings)
        labelFnLockTeaser.Text = Labels.Get("fnlock_header");
        labelFnLockTeaserSub.Text = Labels.Get("fnlock_teaser_sub");
        labelFnLockDetails.Text = Labels.Get("details");

        // Advanced
        headerAdvanced.Text = Labels.Get("advanced_header");
        checkAutoApplyPower.Content = Labels.Get("auto_apply_power");
        checkScreenAuto.Content = Labels.Get("auto_switch_refresh");
        labelCpuCoresLabel.Text = Labels.Get("cpu_cores");

        // GPU Backend - both opt-in flags live here so users can compare
        // the two approaches side by side instead of hunting through
        // Advanced for the second one.
        headerGpuBackend.Text = Labels.Get("gpu_backend_header");
        labelGpuBackendIntro.Text = Labels.Get("gpu_backend_intro");
        checkPciGpuBackend.Content = Labels.Get("gpu_backend_pci_label");
        labelPciGpuBackendHint.Text = Labels.Get("gpu_backend_pci_hint");
        checkRawWmi.Content = Labels.Get("raw_wmi_mode");
        labelRawWmiHint.Text = Labels.Get("raw_wmi_hint");

        // Rebuild combo items with new language strings
        RefreshSpeedCombo();
        RefreshKeyBindingCombos();
        RefreshLanguageComboAuto();

        // Refresh dynamic content with new labels
        RefreshSystemInfo();
        RefreshPower();
        RefreshGpuBackend();
    }

    /// <summary>Rebuild keyboard speed combo with current language strings.</summary>
    private void RefreshSpeedCombo()
    {
        int savedSpeed = comboKbdSpeed.SelectedItem is ComboBoxItem sel && sel.Tag is int s ? s : (int)Aura.Speed;
        comboKbdSpeed.Items.Clear();
        int selectedIdx = 0, idx = 0;
        foreach (var kv in Aura.GetSpeeds())
        {
            comboKbdSpeed.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = (int)kv.Key });
            if ((int)kv.Key == savedSpeed)
                selectedIdx = idx;
            idx++;
        }
        comboKbdSpeed.SelectedIndex = selectedIdx;
    }

    /// <summary>Rebuild key binding combos with current language strings.</summary>
    private void RefreshKeyBindingCombos()
    {
        foreach (var (combo, bindingName) in _keyBindingCombos)
            PopulateKeyBindingCombo(combo, bindingName);
    }

    /// <summary>Update the "Auto (system)" label in the language combo.</summary>
    private void RefreshLanguageComboAuto()
    {
        if (comboLanguage.Items.Count > 0 && comboLanguage.Items[0] is ComboBoxItem first)
            first.Content = Labels.Get("language_auto");
    }

    // KEYBOARD BACKLIGHT

    private void InitKeyboardBacklight()
    {
        // Settings-form retry: if the AURA probe didn't run / didn't succeed
        // earlier (e.g. hidraw races at startup, transient permission glitch),
        // re-run Init() now so power-zone visibility + mode list reflect the
        // hardware. skip_aura honors a user override for fully RGB-disabled
        // setups.
        if (!Aura.IsBacklightDetected
            && !Helpers.AppConfig.Is("skip_aura")
            && Aura.IsAvailable())
        {
            try
            { Aura.Init(); }
            catch (Exception ex) { Helpers.Logger.WriteLine($"ExtraWindow: Aura.Init retry failed: {ex.Message}"); }
        }

        // Brightness (0-3)
        int brightness = App.Wmi?.GetKeyboardBrightness() ?? 3;
        sliderKbdBrightness.Value = brightness;
        labelKbdBrightness.Text = brightness.ToString();

        // Speed combo - populate items, then hide for modes where speed has
        // no effect (Static / Heatmap / GpuMode / Battery / Gradient / ZoneTest).
        // Cleaner than always showing a non-functional dropdown; diverges from
        // Windows g-helper which always shows speed.
        var speeds = Aura.GetSpeeds();
        comboKbdSpeed.Items.Clear();
        int selectedSpeedIdx = 0;
        int idx = 0;
        foreach (var kv in speeds)
        {
            comboKbdSpeed.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = (int)kv.Key });
            if (kv.Key == Aura.Speed)
                selectedSpeedIdx = idx;
            idx++;
        }
        comboKbdSpeed.SelectedIndex = selectedSpeedIdx;
        comboKbdSpeed.IsVisible = Aura.UsesSpeed();

        // Keyboard
        checkAwake.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_awake");
        checkBoot.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_boot");
        checkSleep.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_sleep");
        checkShutdown.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_shutdown");
        checkBattery.IsChecked = Helpers.AppConfig.IsOnBattery("keyboard_awake");

        // Logo
        checkAwakeLogo.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_awake_logo");
        checkBootLogo.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_boot_logo");
        checkSleepLogo.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_sleep_logo");
        checkShutdownLogo.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_shutdown_logo");
        checkBatteryLogo.IsChecked = Helpers.AppConfig.IsOnBattery("keyboard_awake_logo");

        // Lightbar
        checkAwakeBar.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_awake_bar");
        checkBootBar.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_boot_bar");
        checkSleepBar.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_sleep_bar");
        checkShutdownBar.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_shutdown_bar");
        checkBatteryBar.IsChecked = Helpers.AppConfig.IsOnBattery("keyboard_awake_bar");

        // Lid
        checkAwakeLid.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_awake_lid");
        checkBootLid.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_boot_lid");
        checkSleepLid.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_sleep_lid");
        checkShutdownLid.IsChecked = Helpers.AppConfig.IsNotFalse("keyboard_shutdown_lid");
        checkBatteryLid.IsChecked = Helpers.AppConfig.IsOnBattery("keyboard_awake_lid");

        // Power-zone row visibility:
        //   - keyboard battery toggle: visible iff IsBacklightZones() && !IsARCNM()
        //   - lightbar / logo / lid rows: visible iff probe says zone exists
        // When BacklightType == Unknown (probe failed / not run) the Has* flags
        // are all false, so chassis-light rows hide and only keyboard remains.
        bool hideBattery = !Helpers.AppConfig.IsBacklightZones() || Helpers.AppConfig.IsARCNM();
        rowPowerKeyboard.FindControl<CheckBox>("checkBattery")!.IsVisible = !hideBattery;

        rowPowerBar.IsVisible = Aura.HasLightbar;
        rowPowerLogo.IsVisible = Aura.HasLogo;
        rowPowerLid.IsVisible = Aura.HasRearglow && !Helpers.AppConfig.IsZ13();

        // Z13 rear-glow zone (independent device, PID 0x18C6) - own mode + color.
        InitRearLight();
    }

    /// <summary>
    /// Populate the rear-light combo + swatch from AppConfig and show the panel
    /// on Z13. Hidden on all other models (HasRearLight returns false).
    /// </summary>
    private void InitRearLight()
    {
        if (!Helpers.AppConfig.HasRearLight())
        {
            panelRearLight.IsVisible = false;
            return;
        }

        Aura.RearMode = (AuraMode)Helpers.AppConfig.Get("rear_mode");
        Aura.SetRearColor(Helpers.AppConfig.Get("rear_color", unchecked((int)0xFFFFFFFF)));

        var modes = Aura.GetRearModes();
        comboRearLight.Items.Clear();
        int selectedIdx = 0;
        int idx = 0;
        foreach (var kv in modes)
        {
            comboRearLight.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = (int)kv.Key });
            if (kv.Key == Aura.RearMode)
                selectedIdx = idx;
            idx++;
        }
        comboRearLight.SelectedIndex = selectedIdx;

        UpdateRearSwatch();
        labelRearLight.Text = Labels.Get("rear_light");
        labelRearMode.Text = Labels.Get("rear_mode");
        panelRearLight.IsVisible = true;
    }

    private void UpdateRearSwatch()
    {
        swatchRearColor.Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.FromRgb(Aura.RearR, Aura.RearG, Aura.RearB));
    }

    private void ComboRearLight_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (comboRearLight.SelectedItem is ComboBoxItem item && item.Tag is int modeVal)
        {
            Helpers.AppConfig.Set("rear_mode", modeVal);
            Aura.RearMode = (AuraMode)modeVal;
            // ApplyAura() invokes ApplyRearLight() at the end; saves a separate write.
            Task.Run(() => Aura.ApplyAura());
        }
    }

    private void SwatchRearColor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Helpers.ColorPicker.Show(this, Aura.RearR, Aura.RearG, Aura.RearB, (r, g, b) =>
        {
            Aura.RearR = r;
            Aura.RearG = g;
            Aura.RearB = b;
            Helpers.AppConfig.Set("rear_color", Aura.GetRearColorArgb());
            UpdateRearSwatch();
            Task.Run(() => Aura.ApplyAura());
        });
    }

    /// <summary>Update keyboard brightness slider from external change (physical Fn key press).</summary>
    public void RefreshKeyboardBrightness()
    {
        int brightness = App.Wmi?.GetKeyboardBrightness() ?? -1;
        if (brightness < 0)
            return;

        bool prev = _suppressEvents;
        _suppressEvents = true;
        sliderKbdBrightness.Value = brightness;
        labelKbdBrightness.Text = brightness.ToString();
        _suppressEvents = prev;
    }

    /// <summary>Poll display + keyboard brightness every 2s to catch external changes.</summary>
    private void StartBrightnessPolling()
    {
        _brightnessTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _brightnessTimer.Tick += (_, _) =>
        {
            // Display brightness
            var display = App.Display;
            if (display != null && rowBrightnessSlider.IsVisible)
            {
                int brightness = display.GetBrightness();
                if (brightness >= 0 && brightness != (int)sliderBrightness.Value)
                {
                    bool prev = _suppressEvents;
                    _suppressEvents = true;
                    sliderBrightness.Value = Math.Max(brightness, LinuxDisplayControl.MinBrightnessPercent);
                    labelBrightness.Text = $"{brightness}%";
                    _suppressEvents = prev;
                }
            }

            // Keyboard brightness
            int kbdBrightness = App.Wmi?.GetKeyboardBrightness() ?? -1;
            if (kbdBrightness >= 0 && kbdBrightness != (int)sliderKbdBrightness.Value)
            {
                bool prev = _suppressEvents;
                _suppressEvents = true;
                sliderKbdBrightness.Value = kbdBrightness;
                labelKbdBrightness.Text = kbdBrightness.ToString();
                _suppressEvents = prev;
            }
        };
        _brightnessTimer.Start();
    }

    private void SliderKbdBrightness_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        int level = (int)e.NewValue;
        labelKbdBrightness.Text = level.ToString();
        // Persist under the AC- or battery-specific key based on current power state.
        Helpers.AppConfig.Set(Aura.GetBrightnessConfigKey(), level);
        Aura.ApplyBrightness(level, "KbdSlider");
        App.MainWindowInstance?.RefreshKeyboard();
    }

    private void ComboKbdSpeed_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (comboKbdSpeed.SelectedItem is ComboBoxItem item && item.Tag is int speedVal)
        {
            Helpers.AppConfig.Set("aura_speed", speedVal);
            Aura.Speed = (AuraSpeed)speedVal;
            Task.Run(() => Aura.ApplyAura());
        }
    }

    private void CheckPower_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        // Save all power zone states
        Helpers.AppConfig.Set("keyboard_awake", (checkAwake.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_boot", (checkBoot.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_sleep", (checkSleep.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_shutdown", (checkShutdown.IsChecked ?? false) ? 1 : 0);

        Helpers.AppConfig.Set("keyboard_awake_bar", (checkAwakeBar.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_boot_bar", (checkBootBar.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_sleep_bar", (checkSleepBar.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_shutdown_bar", (checkShutdownBar.IsChecked ?? false) ? 1 : 0);

        Helpers.AppConfig.Set("keyboard_awake_lid", (checkAwakeLid.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_boot_lid", (checkBootLid.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_sleep_lid", (checkSleepLid.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_shutdown_lid", (checkShutdownLid.IsChecked ?? false) ? 1 : 0);

        Helpers.AppConfig.Set("keyboard_awake_logo", (checkAwakeLogo.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_boot_logo", (checkBootLogo.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_sleep_logo", (checkSleepLogo.IsChecked ?? false) ? 1 : 0);
        Helpers.AppConfig.Set("keyboard_shutdown_logo", (checkShutdownLogo.IsChecked ?? false) ? 1 : 0);

        // Battery variants
        if (Helpers.AppConfig.IsBacklightZones())
        {
            Helpers.AppConfig.Set("keyboard_awake_bat", (checkBattery.IsChecked ?? false) ? 1 : 0);
            Helpers.AppConfig.Set("keyboard_awake_bar_bat", (checkBatteryBar.IsChecked ?? false) ? 1 : 0);
            Helpers.AppConfig.Set("keyboard_awake_lid_bat", (checkBatteryLid.IsChecked ?? false) ? 1 : 0);
            Helpers.AppConfig.Set("keyboard_awake_logo_bat", (checkBatteryLogo.IsChecked ?? false) ? 1 : 0);
        }

        // Apply power message + re-apply current mode.
        //
        // The power message [5D BD 01 keyb bar lid rear FF] tells the firmware
        // which zones stay lit on awake/boot/sleep/shutdown. For firmware modes
        // (Static, Breathe, ColorCycle, etc.) the keyboard resumes its mode
        // automatically when a zone is toggled back on - the firmware retains
        // mode + color state internally.
        //
        // For animation / direct-RGB modes (Comet, Gradient, ZoneTest) the
        // firmware drops the per-key buffer when the keyboard zone goes off,
        // and toggling it back on leaves the zone dark with no buffer to
        // render. Re-applying ApplyAura re-sends both the firmware mode (0xB3)
        // and any direct-RGB packets (0xBC) so these modes restore correctly.
        Task.Run(() =>
        {
            Aura.ApplyPower();
            Aura.ApplyAura();
        });
    }

    // KEY BINDINGS

    /// <summary>Maps combo box controls to their config key names.</summary>
    private readonly Dictionary<ComboBox, string> _keyBindingCombos = new();

    private void InitKeyBindings()
    {
        bool isAlly = Helpers.AppConfig.IsAlly();
        _keyBindingCombos[comboKeyM4] = "m4";
        _keyBindingCombos[comboKeyFnF4] = isAlly ? "paddle" : "fnf4";
        _keyBindingCombos[comboKeyFnF5] = isAlly ? "cc" : "fnf5";

        if (isAlly)
        {
            labelKeyM4.Text = Labels.Get("ally_extra_btn_rog");
            labelKeyFnF4.Text = Labels.Get("ally_extra_btn_paddle");
            labelKeyFnF5.Text = Labels.Get("ally_extra_btn_cc");
        }

        foreach (var (combo, bindingName) in _keyBindingCombos)
        {
            PopulateKeyBindingCombo(combo, bindingName);
        }
    }

    private void PopulateKeyBindingCombo(ComboBox combo, string bindingName)
    {
        combo.Items.Clear();

        string currentAction = App.GetKeyAction(bindingName);
        int selectedIdx = 0;
        int idx = 0;

        foreach (var (actionId, _) in App.AvailableKeyActions)
        {
            combo.Items.Add(new ComboBoxItem { Content = App.GetKeyActionDisplayName(actionId), Tag = actionId });
            if (actionId == currentAction)
                selectedIdx = idx;
            idx++;
        }

        combo.SelectedIndex = selectedIdx;
    }

    private void ComboKeyBinding_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (sender is not ComboBox combo)
            return;
        if (!_keyBindingCombos.TryGetValue(combo, out string? bindingName))
            return;
        if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string actionId)
            return;

        Helpers.AppConfig.Set(bindingName, actionId);
        Helpers.Logger.WriteLine($"Key binding: {bindingName} → {actionId}");
    }

    // DISPLAY

    /// <summary>Module name stored when the enable-backlight button is shown.</summary>
    private string? _pendingBacklightModule;

    private void RefreshDisplay()
    {
        var display = App.Display as LinuxDisplayControl;
        if (display == null)
            return;

        // Populate backlight controller dropdown
        var backlights = LinuxDisplayControl.GetAvailableBacklights();
        if (backlights.Count > 1)
        {
            bool prev = _suppressEvents;
            _suppressEvents = true;
            comboBacklight.ItemsSource = backlights;
            var active = display.ActiveBacklightName;
            if (active != null && backlights.Contains(active))
                comboBacklight.SelectedItem = active;
            _suppressEvents = prev;
            rowBacklightSelector.IsVisible = true;
        }
        else
        {
            rowBacklightSelector.IsVisible = false;
        }

        if (display.HasBacklight)
        {
            // Backlight available - show slider
            rowBrightnessSlider.IsVisible = true;
            labelBacklightHint.IsVisible = false;

            int brightness = display.GetBrightness();
            if (brightness >= 0)
            {
                sliderBrightness.Value = Math.Max(brightness, LinuxDisplayControl.MinBrightnessPercent);
                labelBrightness.Text = $"{brightness}%";
            }

            // Even with a backlight, offer module load if nvidia is active but nvidia backlight is missing
            _pendingBacklightModule = LinuxDisplayControl.GetMissingBacklightModule();
            if (_pendingBacklightModule != null)
            {
                buttonEnableBacklight.Content = Labels.Format("load_module", _pendingBacklightModule);
                buttonEnableBacklight.IsVisible = true;
                buttonEnableBacklight.IsEnabled = true;
            }
            else
            {
                buttonEnableBacklight.IsVisible = false;
            }
        }
        else
        {
            // No backlight - hide slider, check if we can offer a fix
            rowBrightnessSlider.IsVisible = false;

            _pendingBacklightModule = LinuxDisplayControl.GetMissingBacklightModule();
            if (_pendingBacklightModule != null)
            {
                buttonEnableBacklight.Content = Labels.Format("enable_backlight_load", _pendingBacklightModule);
                buttonEnableBacklight.IsVisible = true;
                buttonEnableBacklight.IsEnabled = true;
                labelBacklightHint.IsVisible = false;
            }
            else
            {
                buttonEnableBacklight.IsVisible = false;
                labelBacklightHint.Text = LinuxDisplayControl.GetBacklightHint();
                labelBacklightHint.IsVisible = true;
            }
        }

        bool overdrive = App.Wmi?.GetPanelOverdrive() ?? false;
        checkOverdrive.IsChecked = overdrive;

        RefreshOptimalBrightness();

        // Gamma only works on X11 (xrandr). Hide on Wayland backends.
        bool supportsGamma = display.Backend?.SupportsGamma ?? false;
        rowGamma.IsVisible = supportsGamma;
        if (supportsGamma)
            sliderGamma.Value = 100;
    }

    private void RefreshOptimalBrightness()
    {
        bool supported = OptimalBrightness.IsSupported();
        rowOptimalBrightness.IsVisible = supported;
        if (!supported)
            return;

        bool prev = _suppressEvents;
        _suppressEvents = true;
        comboOptimalBrightness.Items.Clear();
        comboOptimalBrightness.Items.Add(Labels.Get("optimal_brightness_off"));
        comboOptimalBrightness.Items.Add(Labels.Get("optimal_brightness_always"));
        comboOptimalBrightness.Items.Add(Labels.Get("optimal_brightness_battery"));

        // -1 stored = user never touched it; fall back to current firmware
        // state so the visible selection matches reality (0=Off, 1=On Always).
        int stored = OptimalBrightness.GetStoredMode();
        int selected = stored >= 0
            ? Math.Clamp(stored, 0, 2)
            : Math.Clamp(OptimalBrightness.GetFirmwareState(), 0, 1);
        comboOptimalBrightness.SelectedIndex = selected;
        _suppressEvents = prev;
    }

    private void ComboOptimalBrightness_Changed(object? sender,
        Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        int mode = comboOptimalBrightness.SelectedIndex;
        if (mode < 0)
            return;
        OptimalBrightness.SetMode(mode);
    }

    private void ButtonEnableBacklight_Click(object? sender, RoutedEventArgs e)
    {
        if (_pendingBacklightModule == null)
            return;

        string module = _pendingBacklightModule;
        buttonEnableBacklight.Content = Labels.Get("loading");
        buttonEnableBacklight.IsEnabled = false;

        // Run modprobe on background thread (pkexec shows password dialog)
        Task.Run(() =>
        {
            var display = App.Display as LinuxDisplayControl;
            bool success = display?.TryLoadBacklightModule(module) ?? false;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                bool prev = _suppressEvents;
                _suppressEvents = true;
                RefreshDisplay();
                _suppressEvents = prev;

                if (!success)
                {
                    buttonEnableBacklight.IsVisible = false;
                    labelBacklightHint.Text = Labels.Get("backlight_failed") + "\n" + LinuxDisplayControl.GetBacklightHint();
                    labelBacklightHint.IsVisible = true;
                }
            });
        });
    }

    private void ComboBacklight_SelectionChanged(object? sender,
        Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        var display = App.Display as LinuxDisplayControl;
        if (display == null)
            return;

        var selected = comboBacklight.SelectedItem as string;
        if (selected == null || selected == display.ActiveBacklightName)
            return;

        display.SetActiveBacklight(selected);

        // Refresh slider to show brightness from the new controller
        bool prev = _suppressEvents;
        _suppressEvents = true;
        int brightness = display.GetBrightness();
        if (brightness >= 0)
        {
            sliderBrightness.Value = Math.Max(brightness, LinuxDisplayControl.MinBrightnessPercent);
            labelBrightness.Text = $"{brightness}%";
        }
        _suppressEvents = prev;
    }

    private void SliderBrightness_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        int percent = (int)e.NewValue;
        labelBrightness.Text = $"{percent}%";
        App.Display?.SetBrightness(percent);
    }

    private void CheckOverdrive_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkOverdrive.IsChecked ?? false;
        App.Wmi?.SetPanelOverdrive(enabled);
    }

    private void SliderGamma_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        float gamma = (float)(e.NewValue / 100.0);
        labelGamma.Text = $"{gamma:F2}";
        App.Display?.SetGamma(gamma, gamma, gamma);
    }

    // OTHER

    private void RefreshOther()
    {
        // Boot sound
        int bootSound = Helpers.AppConfig.Get("boot_sound", 0);
        checkBootSound.IsChecked = bootSound == 1;

        // Window always on top
        checkTopmost.IsChecked = Helpers.AppConfig.Is("topmost");
        if (Helpers.AppConfig.Is("topmost"))
            this.Topmost = true;

        // B&W tray icon
        checkBWIcon.IsChecked = Helpers.AppConfig.IsBWIcon();

        // Clamshell mode
        checkClamshell.IsChecked = Helpers.AppConfig.Is("toggle_clamshell_mode");

        // Silent start (minimized to tray)
        checkSilentStart.IsChecked = Helpers.AppConfig.Is("silent_start");

        // Disable OSD/notifications
        checkDisableOsd.IsChecked = Helpers.AppConfig.Is("disable_osd");

        // Keep keyboard/lightbar lit (override system/idle dimming)
        checkKeepBacklight.IsChecked = Helpers.AppConfig.Is("kb_keep_on");

        // Camera
        checkCamera.IsChecked = LinuxSystemIntegration.IsCameraEnabled();

        // Touchpad (hide if not found)
        var touchpadState = LinuxSystemIntegration.IsTouchpadEnabled();
        if (touchpadState == null)
        {
            checkTouchpad.IsVisible = false;
        }
        else
        {
            checkTouchpad.IsVisible = true;
            checkTouchpad.IsChecked = touchpadState.Value;
        }

        // Touchscreen (hide if not found)
        var touchscreenState = LinuxSystemIntegration.IsTouchscreenEnabled();
        if (touchscreenState == null)
        {
            checkTouchscreen.IsVisible = false;
        }
        else
        {
            checkTouchscreen.IsVisible = true;
            checkTouchscreen.IsChecked = touchscreenState.Value;
        }
    }

    private void CheckBootSound_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        int val = (checkBootSound.IsChecked ?? false) ? 1 : 0;
        Helpers.AppConfig.Set("boot_sound", val);

        // Try to set via sysfs (asus-nb-wmi or asus-armoury firmware-attributes)
        try
        {
            var path = Platform.Linux.SysfsHelper.ResolveAttrPath(Platform.Linux.AsusAttributes.BootSound);
            if (path != null)
                Platform.Linux.SysfsHelper.WriteAttribute(path, val.ToString());
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Boot sound write failed: {ex.Message}");
        }

        Helpers.Logger.WriteLine($"Boot sound → {val}");
    }

    private void CheckTopmost_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool on = checkTopmost.IsChecked ?? false;
        Helpers.AppConfig.Set("topmost", on ? 1 : 0);

        // Apply to ALL open windows, not just this one
        App.SetTopmostAll(on);
    }

    private void CheckBWIcon_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        Helpers.AppConfig.Set("bw_icon", (checkBWIcon.IsChecked ?? false) ? 1 : 0);
        Helpers.Logger.WriteLine($"B&W tray icon → {checkBWIcon.IsChecked}");
        // Update the tray icon immediately
        App.UpdateTrayIcon();
    }

    // The GPU row is hidden on iGPU-only systems (no discrete GPU detected).

    /// <summary>Default colors when the user has never set one. CPU=blue, GPU=green.</summary>
    private const string DefaultCpuBg = "#3AAEEF";
    private const string DefaultGpuBg = "#06B48A";
    private const string DefaultTextColor = "#FFFFFF";

    /// <summary>
    /// Initialize tray-icon panel state from saved config: checkboxes,
    /// color-swatch button backgrounds, and dGPU-gated row visibility.
    /// Called once on window load, after <c>InitOther</c> (does not need
    /// to re-fire on language change since only <see cref="ApplyLabels"/>
    /// touches text strings).
    /// </summary>
    private void RefreshTrayIcons()
    {
        // Master toggles
        checkCpuTrayIcon.IsChecked = Helpers.AppConfig.Is("cpu_tray_enabled");
        checkCpuTrayTransparent.IsChecked = Helpers.AppConfig.Is("cpu_tray_bg_transparent");
        checkGpuTrayIcon.IsChecked = Helpers.AppConfig.Is("gpu_tray_enabled");
        checkGpuTrayTransparent.IsChecked = Helpers.AppConfig.Is("gpu_tray_bg_transparent");

        // Color swatches: button background reflects the saved color so the
        // user sees the current state at a glance.
        UpdateSwatch(btnCpuTrayBg, Helpers.AppConfig.GetString("cpu_tray_bg") ?? DefaultCpuBg);
        UpdateSwatch(btnCpuTrayText, Helpers.AppConfig.GetString("cpu_tray_text") ?? DefaultTextColor);
        UpdateSwatch(btnGpuTrayBg, Helpers.AppConfig.GetString("gpu_tray_bg") ?? DefaultGpuBg);
        UpdateSwatch(btnGpuTrayText, Helpers.AppConfig.GetString("gpu_tray_text") ?? DefaultTextColor);

        rowGpuTray.IsVisible = App.GpuModeCtrl?.GetCurrentMode() != Gpu.GpuMode.Eco;
    }

    /// <summary>
    /// Paint a color swatch on a <see cref="Button"/>. Catches malformed
    /// hex (e.g. config tampering) and falls back to grey rather than
    /// throwing; the user can re-pick to fix.
    /// </summary>
    private static void UpdateSwatch(Button btn, string hex)
    {
        try
        {
            btn.Background = new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            btn.Background = new SolidColorBrush(Color.Parse("#808080"));
        }
    }

    //  CPU temp icon handlers 

    private void CheckCpuTrayIcon_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool on = checkCpuTrayIcon.IsChecked ?? false;
        Helpers.AppConfig.Set("cpu_tray_enabled", on ? 1 : 0);
        Helpers.TraySystemMonitor.SetCpuIconEnabled(on);
        Helpers.Logger.WriteLine($"CPU temp tray icon → {on}");
    }

    private void CheckCpuTrayTransparent_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        Helpers.AppConfig.Set("cpu_tray_bg_transparent",
            (checkCpuTrayTransparent.IsChecked ?? false) ? 1 : 0);
        Helpers.TraySystemMonitor.RefreshIconAppearance();
    }

    private void BtnCpuTrayBg_Click(object? sender, RoutedEventArgs e)
    {
        string current = Helpers.AppConfig.GetString("cpu_tray_bg") ?? DefaultCpuBg;
        Helpers.ColorPicker.Show(this, current, hex =>
        {
            Helpers.AppConfig.Set("cpu_tray_bg", hex);
            UpdateSwatch(btnCpuTrayBg, hex);
            Helpers.TraySystemMonitor.RefreshIconAppearance();
        });
    }

    private void BtnCpuTrayText_Click(object? sender, RoutedEventArgs e)
    {
        string current = Helpers.AppConfig.GetString("cpu_tray_text") ?? DefaultTextColor;
        Helpers.ColorPicker.Show(this, current, hex =>
        {
            Helpers.AppConfig.Set("cpu_tray_text", hex);
            UpdateSwatch(btnCpuTrayText, hex);
            Helpers.TraySystemMonitor.RefreshIconAppearance();
        });
    }

    //  GPU temp icon handlers (mirrors of CPU) 

    private void CheckGpuTrayIcon_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool on = checkGpuTrayIcon.IsChecked ?? false;
        Helpers.AppConfig.Set("gpu_tray_enabled", on ? 1 : 0);
        Helpers.TraySystemMonitor.SetGpuIconEnabled(on);
        Helpers.Logger.WriteLine($"GPU temp tray icon → {on}");
    }

    private void CheckGpuTrayTransparent_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        Helpers.AppConfig.Set("gpu_tray_bg_transparent",
            (checkGpuTrayTransparent.IsChecked ?? false) ? 1 : 0);
        Helpers.TraySystemMonitor.RefreshIconAppearance();
    }

    private void BtnGpuTrayBg_Click(object? sender, RoutedEventArgs e)
    {
        string current = Helpers.AppConfig.GetString("gpu_tray_bg") ?? DefaultGpuBg;
        Helpers.ColorPicker.Show(this, current, hex =>
        {
            Helpers.AppConfig.Set("gpu_tray_bg", hex);
            UpdateSwatch(btnGpuTrayBg, hex);
            Helpers.TraySystemMonitor.RefreshIconAppearance();
        });
    }

    private void BtnGpuTrayText_Click(object? sender, RoutedEventArgs e)
    {
        string current = Helpers.AppConfig.GetString("gpu_tray_text") ?? DefaultTextColor;
        Helpers.ColorPicker.Show(this, current, hex =>
        {
            Helpers.AppConfig.Set("gpu_tray_text", hex);
            UpdateSwatch(btnGpuTrayText, hex);
            Helpers.TraySystemMonitor.RefreshIconAppearance();
        });
    }

    private void CheckClamshell_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool on = checkClamshell.IsChecked ?? false;
        Helpers.AppConfig.Set("toggle_clamshell_mode", on ? 1 : 0);

        // Toggle lid switch handling via systemd-inhibit (runs as current user, no root needed)
        try
        {
            if (on)
            {
                StartClamshellInhibit();
            }
            else
            {
                StopClamshellInhibit();
            }
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Clamshell mode toggle failed: {ex.Message}");
        }
    }

    private void CheckSilentStart_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        Helpers.AppConfig.Set("silent_start", (checkSilentStart.IsChecked ?? false) ? 1 : 0);
    }

    private void CheckDisableOsd_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        Helpers.AppConfig.Set("disable_osd", (checkDisableOsd.IsChecked ?? false) ? 1 : 0);
    }

    private void CheckKeepBacklight_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool on = checkKeepBacklight.IsChecked ?? false;
        Helpers.AppConfig.Set("kb_keep_on", on ? 1 : 0);
        // Apply immediately: re-light to the configured level when enabling.
        if (on)
            USB.Aura.ApplyConfiguredBrightness("KeepOn");
    }

    /// <summary>Start a systemd-inhibit process that prevents lid-close suspend.</summary>
    public static void StartClamshellInhibit()
    {
        StopClamshellInhibit(); // Kill any existing one first

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "systemd-inhibit",
                Arguments = "--what=handle-lid-switch --who=\"G-Helper\" --why=\"Clamshell mode\" sleep infinity",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                _clamshellInhibitPid = proc.Id;
                Helpers.Logger.WriteLine($"Clamshell mode ON (inhibit PID {_clamshellInhibitPid})");
            }
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"Failed to start systemd-inhibit: {ex.Message}");
        }
    }

    /// <summary>Kill the systemd-inhibit process, restoring normal lid behavior.</summary>
    public static void StopClamshellInhibit()
    {
        if (_clamshellInhibitPid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(_clamshellInhibitPid);
                proc.Kill();
                proc.WaitForExit(2000);
                Helpers.Logger.WriteLine($"Clamshell mode OFF (killed PID {_clamshellInhibitPid})");
            }
            catch
            {
                // Process already exited
            }

            _clamshellInhibitPid = -1;
        }
    }

    private void CheckCamera_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkCamera.IsChecked ?? true;
        LinuxSystemIntegration.SetCameraEnabled(enabled);
        App.System?.ShowNotification(Labels.Get("camera"),
            enabled ? Labels.Get("enabled") : Labels.Get("disabled"),
            enabled ? "camera-on" : "camera-off");
    }

    private void CheckTouchpad_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkTouchpad.IsChecked ?? true;
        LinuxSystemIntegration.SetTouchpadEnabled(enabled);
        App.System?.ShowNotification(Labels.Get("touchpad"),
            enabled ? Labels.Get("enabled") : Labels.Get("disabled"),
            enabled ? "input-touchpad-on" : "input-touchpad-off");
    }

    private void CheckTouchscreen_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkTouchscreen.IsChecked ?? true;
        LinuxSystemIntegration.SetTouchscreenEnabled(enabled);
        App.System?.ShowNotification(Labels.Get("touchscreen"),
            enabled ? Labels.Get("enabled") : Labels.Get("disabled"),
            "preferences-desktop-touchscreen");
    }


    // POWER MANAGEMENT
    //
    // Three platform_profile dropdowns, one per ghelper performance mode (Silent /
    // Balanced / Turbo). All combos are populated at runtime from
    // /sys/firmware/acpi/platform_profile_choices so users only see kernel-supported
    // values. Selections are persisted per-mode in AppConfig as platform_profile_<N>
    // (where N = base mode index: 0=Balanced, 1=Turbo, 2=Silent).
    //
    // ModeControl.SetPerformanceMode reads the per-mode override on every mode switch
    // (via AppConfig.GetModeString("platform_profile") which resolves to platform_profile_<currentMode>).
    // If unset, ModeControl uses canonical defaults: Silent→low-power, Balanced→balanced,
    // Turbo→performance. SetPlatformProfile then maps to firmware-supported names via
    // its synonym table (e.g. low-power→quiet on legacy firmware).
    //
    // Changing a dropdown for the currently-active mode applies immediately. Changing
    // dropdowns for inactive modes only persists - the value lands when the user
    // switches into that mode.

    private bool _powerInitialized;

    /// <summary>Returns the canonical mode-derived platform_profile default for a
    /// given base mode (matches ModeControl fallback). Used when no per-mode override
    /// is saved yet, so dropdowns show the value the system would actually apply.</summary>
    private static string CanonicalProfileForMode(int baseMode) => baseMode switch
    {
        0 => "balanced",
        1 => "performance",
        2 => "low-power",
        _ => "balanced"
    };

    private void InitPowerCombos()
    {
        var power = App.Power;
        if (power == null)
            return;

        var choices = power.GetPlatformProfileChoices();
        var combos = new[] { comboProfileSilent, comboProfileBalanced, comboProfileTurbo };

        foreach (var combo in combos)
        {
            combo.Items.Clear();
            if (choices.Length == 0)
            {
                combo.IsEnabled = false;
                combo.PlaceholderText = Labels.Get("platform_profile_unavailable");
            }
            else
            {
                combo.IsEnabled = true;
                foreach (var choice in choices)
                    combo.Items.Add(new ComboBoxItem { Content = choice });
            }
        }

        _powerInitialized = true;
    }

    /// <summary>Selects the dropdown item with content matching value. No-op if
    /// nothing matches (combo retains current selection or empty state).</summary>
    private static void SelectComboValue(ComboBox combo, string value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item &&
                item.Content?.ToString() == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Reads the saved per-mode profile (or canonical default if unset)
    /// and selects the corresponding combo item. baseMode: 0=Balanced, 1=Turbo, 2=Silent.
    /// If the saved value isn't a kernel-exposed choice (firmware change, stale config),
    /// resolves through <see cref="LinuxPowerManager.TryResolveSupportedProfile"/> so
    /// the dropdown shows the value that would actually land on sysfs; if no synonym
    /// exists, falls back to the first available kernel choice.</summary>
    private void RefreshProfileCombo(ComboBox combo, int baseMode, string[] choices)
    {
        string? saved = Helpers.AppConfig.GetString($"platform_profile_{baseMode}");
        string desired = saved ?? CanonicalProfileForMode(baseMode);
        desired = LinuxPowerManager.TryResolveSupportedProfile(desired, choices)
                  ?? (choices.Length > 0 ? choices[0] : desired);
        SelectComboValue(combo, desired);
    }

    private void RefreshPower()
    {
        var power = App.Power;
        if (power == null)
            return;

        if (!_powerInitialized)
            InitPowerCombos();

        var choices = power.GetPlatformProfileChoices();
        if (choices.Length > 0)
        {
            RefreshProfileCombo(comboProfileSilent, 2, choices);
            RefreshProfileCombo(comboProfileBalanced, 0, choices);
            RefreshProfileCombo(comboProfileTurbo, 1, choices);
        }

        // Battery health
        int health = power.GetBatteryHealth();
        if (health >= 0)
            labelBatteryHealth.Text = Labels.Format("battery_health_format", health);
        else
            labelBatteryHealth.Text = Labels.Get("battery_health_unknown");

        int drain = power.GetBatteryDrainRate();
        if (drain != 0)
            labelPowerDraw.Text = drain > 0
                ? Labels.Format("power_draw_discharge", drain)
                : Labels.Format("power_draw_charge", -drain);
        else
            labelPowerDraw.Text = Labels.Get("power_draw_unknown");
    }

    /// <summary>Common handler body for all three per-mode profile combos. Persists
    /// the user's choice to AppConfig only. The selection lands on sysfs the next
    /// time the user activates that mode via MainWindow buttons - this UI is
    /// configuration-only, never the trigger for an active mode change. Avoids
    /// the derived-policy trap on asus-armoury kernels where writing
    /// platform_profile silently flips throttle_thermal_policy.</summary>
    private void OnProfileComboChanged(int baseMode, ComboBox combo)
    {
        if (_suppressEvents)
            return;
        if (combo.SelectedItem is not ComboBoxItem item || item.Content is not string profile)
            return;

        Helpers.AppConfig.Set($"platform_profile_{baseMode}", profile);
        Helpers.Logger.WriteLine($"Platform profile (mode {baseMode}) saved → {profile}");
    }

    private void ComboProfileSilent_Changed(object? sender, SelectionChangedEventArgs e)
        => OnProfileComboChanged(2, comboProfileSilent);

    private void ComboProfileBalanced_Changed(object? sender, SelectionChangedEventArgs e)
        => OnProfileComboChanged(0, comboProfileBalanced);

    private void ComboProfileTurbo_Changed(object? sender, SelectionChangedEventArgs e)
        => OnProfileComboChanged(1, comboProfileTurbo);

    private BatteryInfoWindow? _batteryInfoWindow;
    private SystemInfoWindow? _systemInfoWindow;

    private void ButtonBatteryInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (_batteryInfoWindow == null || !_batteryInfoWindow.IsVisible)
        {
            _batteryInfoWindow = new BatteryInfoWindow();
            if (Helpers.AppConfig.Is("topmost"))
                _batteryInfoWindow.Topmost = true;
            Helpers.WindowPositioner.CenterOfMainWindowOrPrimaryMonitor(_batteryInfoWindow);
            _batteryInfoWindow.Show();
        }
        else
        {
            _batteryInfoWindow.Activate();
        }
    }

    private void ButtonSystemInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (_systemInfoWindow == null || !_systemInfoWindow.IsVisible)
        {
            _systemInfoWindow = new SystemInfoWindow();
            if (Helpers.AppConfig.Is("topmost"))
                _systemInfoWindow.Topmost = true;
            Helpers.WindowPositioner.CenterOfMainWindowOrPrimaryMonitor(_systemInfoWindow);
            _systemInfoWindow.Show();
        }
        else
        {
            _systemInfoWindow.Activate();
        }
    }

    // SYSTEM INFO

    private void RefreshSystemInfo()
    {
        var sys = App.System;
        if (sys == null)
            return;

        labelModel.Text = Labels.Format("model_prefix", sys.GetModelName());
        labelBios.Text = Labels.Format("bios_prefix", sys.GetBiosVersion());
        labelKernel.Text = Labels.Format("kernel_prefix", sys.GetKernelVersion());

        bool wmiLoaded = sys.IsAsusWmiLoaded();
        labelAsusWmi.Text = wmiLoaded ? Labels.Get("asus_wmi_loaded") : Labels.Get("asus_wmi_not_loaded");

        // Feature detection
        var features = new List<string>();
        var wmi = App.Wmi;
        if (wmi != null)
        {
            if (wmi.IsFeatureSupported(AsusAttributes.ThrottleThermalPolicy))
                features.Add(Labels.Get("feature_perf_modes"));
            if (wmi.IsFeatureSupported(AsusAttributes.DgpuDisable))
                features.Add(Labels.Get("feature_gpu_eco"));
            if (wmi.IsFeatureSupported(AsusAttributes.GpuMuxMode))
                features.Add(Labels.Get("feature_mux"));
            if (wmi.IsFeatureSupported(AsusAttributes.PanelOd))
                features.Add(Labels.Get("feature_overdrive"));
            if (wmi.IsFeatureSupported(AsusAttributes.MiniLedMode))
                features.Add(Labels.Get("feature_miniled"));
            if (wmi.IsFeatureSupported(AsusAttributes.PptPl1Spl))
                features.Add(Labels.Get("feature_ppt"));
            if (wmi.IsFeatureSupported(AsusAttributes.NvDynamicBoost))
                features.Add(Labels.Get("feature_nv_boost"));
        }

        labelFeatures.Text = features.Count > 0
            ? Labels.Format("features_prefix", string.Join(", ", features))
            : Labels.Get("no_features");

        // Kernel version check
        var kernelVer = sys.GetKernelVersionParsed();
        if (kernelVer < new Version(6, 2))
        {
            labelFeatures.Text += "\n" + Labels.Get("kernel_warning");
        }
    }

    // ADVANCED

    private void RefreshAdvanced()
    {
        // checkRawWmi moved to the GPU Backend panel (RefreshGpuBackend
        // owns its IsChecked state now).
        checkAutoApplyPower.IsChecked = Helpers.AppConfig.IsMode("auto_apply_power");
        checkScreenAuto.IsChecked = Helpers.AppConfig.Is("screen_auto");

        // CPU cores
        int total = LinuxSystemIntegration.GetCpuCount();
        int online = LinuxSystemIntegration.GetOnlineCpuCount();

        if (total > 1)
        {
            panelCpuCores.IsVisible = true;
            sliderCpuCores.Maximum = total;
            sliderCpuCores.Value = online;
            labelCpuCores.Text = Labels.Format("cpu_cores_format", online, total);
            labelCpuCoresInfo.Text = Labels.Format("cpu_cores_info", online, total);
        }
        else
        {
            panelCpuCores.IsVisible = false;
        }
    }

    private void CheckAutoApplyPower_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkAutoApplyPower.IsChecked ?? false;
        Helpers.AppConfig.SetMode("auto_apply_power", enabled ? 1 : 0);
    }

    private void CheckScreenAuto_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkScreenAuto.IsChecked ?? false;
        Helpers.AppConfig.Set("screen_auto", enabled ? 1 : 0);
        Helpers.Logger.WriteLine($"Screen auto refresh → {enabled}");
        if (enabled)
            (App.Current as App)?.AutoScreen();
        App.MainWindowInstance?.RefreshScreenPublic();
    }

    private void CheckRawWmi_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkRawWmi.IsChecked ?? false;
        // Idempotency guard
        if (enabled == Helpers.AppConfig.Is("raw_wmi"))
            return;

        Helpers.AppConfig.Set("raw_wmi", enabled ? 1 : 0);
        Helpers.AppConfig.Flush();
        Helpers.Logger.WriteLine($"Raw WMI mode → {enabled}, restarting app");

        // Restart app so the new setting takes effect immediately
        var exePath = Environment.ProcessPath;
        if (exePath != null)
        {
            System.Diagnostics.Process.Start(exePath);
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Show the GPU Backend panel whenever the user could plausibly switch
    /// dGPU modes: ASUS WMI present, NVIDIA dGPU on the PCI bus, PCI mode
    /// already opted in, block artifacts on disk (PCI eco currently
    /// active), or NVIDIA modules installed on disk. This covers the case
    /// where the dGPU was hot-removed by the udev rule (vendor 0x10de
    /// vanishes from the PCI tree) but is otherwise functional.
    /// </summary>
    private void RefreshGpuBackend()
    {
        var wmi = App.Wmi;
        bool canToggle = wmi?.CanToggleGpuBackend() ?? false;
        panelGpuBackend.IsVisible = canToggle;
        if (!canToggle)
            return;

        // Match the RefreshAdvanced pattern: the outer Loaded handler may
        // hold _suppressEvents=true, so save and restore it instead of
        // unconditionally clearing.
        bool prevSuppress = _suppressEvents;
        _suppressEvents = true;
        try
        {
            checkPciGpuBackend.IsChecked = Helpers.AppConfig.IsPciGpuBackend();
            checkRawWmi.IsChecked = Helpers.AppConfig.Is("raw_wmi");
        }
        finally
        {
            _suppressEvents = prevSuppress;
        }
    }

    private void CheckPciGpuBackend_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        bool enabled = checkPciGpuBackend.IsChecked ?? false;
        string newBackend = enabled ? "pci" : "asus-wmi";
        if (newBackend == Helpers.AppConfig.GetGpuBackend())
            return;

        Helpers.AppConfig.Set("gpu_backend", newBackend);
        Helpers.AppConfig.Flush();
        Helpers.Logger.WriteLine($"GPU backend → {newBackend}");

        // Push the backend marker file so the boot service knows which flow
        // to run on the next boot, even before the user requests a mode
        // change. Best-effort: failure here just delays the marker write
        // until the next eco/standard switch.
        try
        {
            App.GpuModeCtrl?.PushBackendMarker(newBackend);
        }
        catch (Exception ex)
        {
            Helpers.Logger.WriteLine($"GpuBackend toggle: marker push failed: {ex.Message}");
        }

        // Refresh main window so the GPU panel re-evaluates which buttons
        // to show (Ultimate/Optimized are hidden in PCI mode).
        App.MainWindowInstance?.RefreshGpuModePublic();
    }

    private void SliderCpuCores_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        int target = (int)e.NewValue;
        int total = LinuxSystemIntegration.GetCpuCount();
        labelCpuCores.Text = Labels.Format("cpu_cores_format", target, total);
        labelCpuCoresInfo.Text = Labels.Format("cpu_cores_info", target, total);

        // Apply in background to avoid UI stall
        Task.Run(() => LinuxSystemIntegration.SetOnlineCpuCount(target));
    }


    // FUNCTION KEY REMAP details button

    private FnLockWindow? _fnLockWindow;

    private void ButtonFnLockDetails_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_fnLockWindow == null || !_fnLockWindow.IsVisible)
        {
            _fnLockWindow = new FnLockWindow();
            if (Helpers.AppConfig.Is("topmost"))
                _fnLockWindow.Topmost = true;
            Helpers.WindowPositioner.CenterOfMainWindowOrPrimaryMonitor(_fnLockWindow);
            _fnLockWindow.Show();
        }
        else
        {
            _fnLockWindow.Activate();
        }
    }

    private bool _suppressApuMem;

    /// <summary>
    /// Populate the APU memory combo from the kernel's possible_values list.
    /// Hides the entire panel when the attribute isn't exposed (non-Ally,
    /// or older kernels missing apu_mem support).
    /// </summary>
    private void InitApuMem()
    {
        var values = Platform.Linux.SysfsHelper.ReadPossibleValues(
            Platform.Linux.AsusAttributes.ApuMem);

        if (!Helpers.AppConfig.IsAlly() || values == null)
        {
            panelApuMem.IsVisible = false;
            return;
        }

        panelApuMem.IsVisible = true;
        comboApuMem.Items.Clear();

        // Some kernels expose values like "0 1 2 3 ..." (enum index) and
        // others like "256 512 1024 ..." (MB). Display whatever the kernel
        // gave us - the user picks one and we write it back as-is.
        foreach (var v in values)
            comboApuMem.Items.Add(new ComboBoxItem { Content = v, Tag = v });

        // Reflect the current value.
        var path = Platform.Linux.SysfsHelper.ResolveAttrPath(
            Platform.Linux.AsusAttributes.ApuMem);
        if (path != null)
        {
            var current = Platform.Linux.SysfsHelper.ReadAttribute(path)?.Trim();
            if (current != null)
            {
                _suppressApuMem = true;
                try
                {
                    foreach (ComboBoxItem item in comboApuMem.Items.Cast<ComboBoxItem>())
                    {
                        if ((item.Tag as string) == current)
                        {
                            comboApuMem.SelectedItem = item;
                            break;
                        }
                    }
                }
                finally { _suppressApuMem = false; }
            }
        }
    }

    private void ComboApuMem_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressApuMem)
            return;
        if (comboApuMem.SelectedItem is not ComboBoxItem item)
            return;
        if (item.Tag is not string value)
            return;

        bool ok = Platform.Linux.SysfsHelper.WriteToAllBackends(
            Platform.Linux.AsusAttributes.ApuMem, value);
        Helpers.Logger.WriteLine($"APU mem → {value} (ok={ok})");

        App.System?.ShowNotification(Labels.Get("ally_apu_mem_header"),
            Labels.Get("ally_apu_mem_reboot_required"), "system-reboot");
    }

    // XG Mobile dock controls
    //
    // Visibility is driven entirely by USB-HID enumeration via XGM.IsConnected;
    // the laptop-side egpu_connected fw-attr can disagree (e.g. dock plugged
    // in but never enabled), and this UI is purely about controlling the
    // dock's own LED ring through report 0x5E.

    private bool _suppressXgm;

    private void InitXgmPanel()
    {
        bool present = USB.XGM.IsConnected();
        panelXGM.IsVisible = present;
        if (!present)
            return;

        bool light = Helpers.AppConfig.Get("xmg_light", 1) == 1;
        int brightness = Helpers.AppConfig.Get("xmg_brightness", 3);
        if (brightness < 0)
            brightness = 0;
        if (brightness > 3)
            brightness = 3;

        _suppressXgm = true;
        try
        {
            checkXGMLights.IsChecked = light;
            sliderXgmBrightness.Value = brightness;
            labelXgmBrightness.Text = brightness.ToString();
        }
        finally { _suppressXgm = false; }
    }

    private void CheckXGMLights_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressXgm)
            return;

        bool on = checkXGMLights.IsChecked == true;
        Helpers.AppConfig.Set("xmg_light", on ? 1 : 0);

        try
        { USB.XGM.Light(on); }
        catch (Exception ex) { Helpers.Logger.WriteLine($"XGM.Light: {ex.Message}"); }
    }

    private void SliderXgmBrightness_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int level = (int)Math.Round(e.NewValue);
        if (level < 0)
            level = 0;
        if (level > 3)
            level = 3;
        labelXgmBrightness.Text = level.ToString();

        if (_suppressXgm)
            return;

        Helpers.AppConfig.Set("xmg_brightness", level);
        try
        { USB.XGM.LightBrightness((byte)level); }
        catch (Exception ex) { Helpers.Logger.WriteLine($"XGM.LightBrightness: {ex.Message}"); }
    }
}
