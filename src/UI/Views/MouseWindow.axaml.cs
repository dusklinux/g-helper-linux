using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GHelper.Linux.Peripherals;
using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Settings window for the first connected ASUS mouse.
/// Sections appear based on the capabilities the mouse class reports.
/// With no mouse connected, all sections are shown disabled as a dev preview.
/// </summary>
public partial class MouseWindow : Window
{
    private static readonly PowerOffSetting[] PowerOffValues =
    [
        PowerOffSetting.Minutes1, PowerOffSetting.Minutes2, PowerOffSetting.Minutes3,
        PowerOffSetting.Minutes5, PowerOffSetting.Minutes10, PowerOffSetting.Never,
    ];

    private static readonly DebounceTime[] DebounceValues =
    [
        DebounceTime.OFF, DebounceTime.MS8, DebounceTime.MS12, DebounceTime.MS16,
        DebounceTime.MS20, DebounceTime.MS24, DebounceTime.MS28, DebounceTime.MS32,
    ];

    private AsusMouse? _mouse;
    private PollingRate[] _pollingRates = [];
    private LightingMode[] _lightingModes = [];
    private LightingZone[] _lightingZones = [];
    private int _zoneIndex;
    private bool _loading = true;

    private readonly AsusMouse? _requestedMouse;

    public MouseWindow() : this(null) { }

    public MouseWindow(AsusMouse? mouse)
    {
        _requestedMouse = mouse;
        InitializeComponent();

        PeripheralsProvider.DeviceChanged += OnDeviceChanged;
        Closing += (_, _) => PeripheralsProvider.DeviceChanged -= OnDeviceChanged;
        Loaded += (_, _) => Refresh();
    }

    private void OnDeviceChanged()
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        _loading = true;

        // Requested mouse wins while still connected, otherwise first available.
        _mouse = _requestedMouse != null && PeripheralsProvider.ConnectedMice.Contains(_requestedMouse)
            ? _requestedMouse
            : PeripheralsProvider.ConnectedMice.FirstOrDefault();

        if (_mouse is null)
            ShowPreview();
        else
            ShowMouse(_mouse);

        _loading = false;
    }

    /// <summary>Dev preview: all sections visible but disabled.</summary>
    private void ShowPreview()
    {
        Title = "Mouse Settings";
        labelDevice.Text = "No ASUS mouse detected";
        labelBattery.Text = "--";

        rowBattery.IsVisible = true;
        panelDpi.IsVisible = true;
        panelPerformance.IsVisible = true;
        rowAngleSnapping.IsVisible = true;
        rowDebounce.IsVisible = true;
        rowLiftOff.IsVisible = true;
        panelEnergy.IsVisible = true;
        rowPowerOff.IsVisible = true;
        rowLowBattery.IsVisible = true;
        panelLighting.IsVisible = true;
        rowZone.IsVisible = true;

        _pollingRates = Enum.GetValues<PollingRate>();
        _lightingModes = [LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle,
            LightingMode.Rainbow, LightingMode.React, LightingMode.Comet, LightingMode.BatteryState, LightingMode.Off];
        _lightingZones = [LightingZone.All];
        _zoneIndex = 0;

        FillCombo(comboDpiProfile, Enumerable.Range(1, 4).Select(i => $"Profile {i}"), 0);
        sliderDpi.Minimum = 100;
        sliderDpi.Maximum = 36000;
        sliderDpi.TickFrequency = 50;
        sliderDpi.Value = 800;
        labelDpiValue.Text = "800";

        FillCombo(comboPollingRate, _pollingRates.Select(PollingRateName), 3);
        checkAngleSnapping.IsChecked = false;
        FillCombo(comboDebounce, DebounceValues.Select(DebounceName), 2);
        FillCombo(comboLiftOff, ["Low", "High"], 0);
        FillCombo(comboPowerOff, PowerOffValues.Select(PowerOffName), 2);
        sliderLowBattery.Value = 25;
        labelLowBattery.Text = "25%";

        FillCombo(comboLightingZone, _lightingZones.Select(ZoneName), 0);
        FillCombo(comboLightingMode, _lightingModes.Select(LightingModeName), 0);
        sliderBrightness.Maximum = 100;
        sliderBrightness.Value = 100;
        labelBrightness.Text = "100";
        borderColor.Background = Brushes.White;

        SetSectionsEnabled(false);
    }

    private void ShowMouse(AsusMouse mouse)
    {
        Title = mouse.GetDisplayName();
        labelDevice.Text = mouse.GetDisplayName();

        mouse.ReadBattery();
        rowBattery.IsVisible = mouse.HasBattery();
        labelBattery.Text = mouse.Battery < 0
            ? "--"
            : $"{mouse.Battery}%{(mouse.Charging ? " (Charging)" : "")}";

        // DPI
        panelDpi.IsVisible = true;
        int profileCount = mouse.DpiSettings.Length;
        int profile = Math.Clamp(mouse.DpiProfile, 0, profileCount - 1);
        FillCombo(comboDpiProfile, Enumerable.Range(1, profileCount).Select(i => $"Profile {i}"), profile);
        sliderDpi.Minimum = mouse.MinDPI();
        sliderDpi.Maximum = mouse.MaxDPI();
        sliderDpi.TickFrequency = mouse.DPIIncrement();
        sliderDpi.Value = mouse.DpiSettings[profile].DPI;
        labelDpiValue.Text = mouse.DpiSettings[profile].DPI.ToString();

        // Performance
        _pollingRates = mouse.SupportedPollingrates();
        int rateIndex = Math.Max(0, Array.IndexOf(_pollingRates, mouse.PollingRate));
        FillCombo(comboPollingRate, _pollingRates.Select(PollingRateName), rateIndex);

        rowAngleSnapping.IsVisible = mouse.HasAngleSnapping();
        checkAngleSnapping.IsChecked = mouse.AngleSnapping;

        rowDebounce.IsVisible = mouse.HasDebounce();
        int debounceIndex = Math.Max(0, Array.IndexOf(DebounceValues, mouse.Debounce));
        FillCombo(comboDebounce, DebounceValues.Select(DebounceName), debounceIndex);

        rowLiftOff.IsVisible = true;
        FillCombo(comboLiftOff, ["Low", "High"], (int)mouse.LiftOff);

        // Energy
        rowPowerOff.IsVisible = mouse.HasAutoPowerOff();
        rowLowBattery.IsVisible = mouse.HasLowBatteryWarning();
        panelEnergy.IsVisible = rowPowerOff.IsVisible || rowLowBattery.IsVisible;
        int powerOffIndex = Math.Max(0, Array.IndexOf(PowerOffValues, mouse.PowerOff));
        FillCombo(comboPowerOff, PowerOffValues.Select(PowerOffName), powerOffIndex);
        sliderLowBattery.Value = mouse.LowBatteryWarning;
        labelLowBattery.Text = $"{mouse.LowBatteryWarning}%";

        // Lighting
        _lightingZones = mouse.SupportedLightingZones();
        _lightingModes = mouse.SupportedLightingModes();
        panelLighting.IsVisible = _lightingZones.Length > 0;
        if (panelLighting.IsVisible)
        {
            _zoneIndex = Math.Clamp(_zoneIndex, 0, _lightingZones.Length - 1);
            rowZone.IsVisible = _lightingZones.Length > 1;
            FillCombo(comboLightingZone, _lightingZones.Select(ZoneName), _zoneIndex);
            sliderBrightness.Maximum = mouse.MaxBrightness();
            RefreshLightingControls(mouse);
        }

        SetSectionsEnabled(true);
    }

    private void RefreshLightingControls(AsusMouse mouse)
    {
        var setting = mouse.LightingSettings[_zoneIndex];
        int modeIndex = Math.Max(0, Array.IndexOf(_lightingModes, setting.Mode));
        FillCombo(comboLightingMode, _lightingModes.Select(LightingModeName), modeIndex);
        sliderBrightness.Value = Math.Min(setting.Brightness, (byte)mouse.MaxBrightness());
        labelBrightness.Text = ((int)sliderBrightness.Value).ToString();
        borderColor.Background = new SolidColorBrush(Color.FromRgb(setting.R, setting.G, setting.B));
    }

    private void SetSectionsEnabled(bool enabled)
    {
        panelDpi.IsEnabled = enabled;
        panelPerformance.IsEnabled = enabled;
        panelEnergy.IsEnabled = enabled;
        panelLighting.IsEnabled = enabled;
    }

    private static void FillCombo(ComboBox combo, IEnumerable<string> items, int selectedIndex)
    {
        combo.Items.Clear();
        foreach (var item in items)
            combo.Items.Add(new ComboBoxItem { Content = item });
        if (combo.Items.Count > 0)
            combo.SelectedIndex = Math.Clamp(selectedIndex, 0, combo.Items.Count - 1);
    }

    // Event handlers

    private void ComboDpiProfile_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboDpiProfile.SelectedIndex < 0)
            return;

        _mouse.DpiProfile = comboDpiProfile.SelectedIndex;
        _mouse.WriteDPI();

        _loading = true;
        sliderDpi.Value = _mouse.DpiSettings[_mouse.DpiProfile].DPI;
        labelDpiValue.Text = _mouse.DpiSettings[_mouse.DpiProfile].DPI.ToString();
        _loading = false;
    }

    private void SliderDpi_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        labelDpiValue.Text = ((int)sliderDpi.Value).ToString();
        if (_loading || _mouse is null)
            return;

        int profile = Math.Clamp(_mouse.DpiProfile, 0, _mouse.DpiSettings.Length - 1);
        _mouse.DpiSettings[profile].DPI = (uint)sliderDpi.Value;
        _mouse.WriteDPI();
    }

    private void ComboPollingRate_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboPollingRate.SelectedIndex < 0)
            return;

        _mouse.PollingRate = _pollingRates[comboPollingRate.SelectedIndex];
        _mouse.WritePollingRate();
    }

    private void CheckAngleSnapping_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading || _mouse is null)
            return;

        _mouse.AngleSnapping = checkAngleSnapping.IsChecked == true;
        _mouse.WriteAngleSnapping();
    }

    private void ComboDebounce_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboDebounce.SelectedIndex < 0)
            return;

        _mouse.Debounce = DebounceValues[comboDebounce.SelectedIndex];
        _mouse.WriteDebounce();
    }

    private void ComboLiftOff_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboLiftOff.SelectedIndex < 0)
            return;

        _mouse.LiftOff = (LiftOffDistance)comboLiftOff.SelectedIndex;
        _mouse.WriteLiftOffDistance();
    }

    private void ComboPowerOff_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboPowerOff.SelectedIndex < 0)
            return;

        _mouse.PowerOff = PowerOffValues[comboPowerOff.SelectedIndex];
        _mouse.WritePowerOff();
    }

    private void SliderLowBattery_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        labelLowBattery.Text = $"{(int)sliderLowBattery.Value}%";
        if (_loading || _mouse is null)
            return;

        _mouse.LowBatteryWarning = (byte)sliderLowBattery.Value;
        _mouse.WriteLowBatteryWarning();
    }

    private void ComboLightingZone_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboLightingZone.SelectedIndex < 0)
            return;

        _zoneIndex = comboLightingZone.SelectedIndex;
        _loading = true;
        RefreshLightingControls(_mouse);
        _loading = false;
    }

    private void ComboLightingMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mouse is null || comboLightingMode.SelectedIndex < 0)
            return;

        _mouse.LightingSettings[_zoneIndex].Mode = _lightingModes[comboLightingMode.SelectedIndex];
        _mouse.WriteLightingSetting();
    }

    private void SliderBrightness_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        labelBrightness.Text = ((int)sliderBrightness.Value).ToString();
        if (_loading || _mouse is null)
            return;

        _mouse.LightingSettings[_zoneIndex].Brightness = (byte)sliderBrightness.Value;
        _mouse.WriteLightingSetting();
    }

    private void ButtonColor_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_mouse is null)
            return;

        var setting = _mouse.LightingSettings[_zoneIndex];
        Helpers.ColorPicker.Show(this, setting.R, setting.G, setting.B, (r, g, b) =>
        {
            setting.R = r;
            setting.G = g;
            setting.B = b;
            _mouse.WriteLightingSetting();
            borderColor.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        });
    }

    // Display name helpers

    private static string PollingRateName(PollingRate rate) => rate switch
    {
        PollingRate.PR125Hz => "125 Hz",
        PollingRate.PR250Hz => "250 Hz",
        PollingRate.PR500Hz => "500 Hz",
        PollingRate.PR1000Hz => "1000 Hz",
        PollingRate.PR2000Hz => "2000 Hz",
        PollingRate.PR4000Hz => "4000 Hz",
        PollingRate.PR8000Hz => "8000 Hz",
        PollingRate.PR16000Hz => "16000 Hz",
        _ => rate.ToString(),
    };

    private static string DebounceName(DebounceTime time) => time switch
    {
        DebounceTime.OFF => "Off",
        DebounceTime.MS8 => "8 ms",
        DebounceTime.MS12 => "12 ms",
        DebounceTime.MS16 => "16 ms",
        DebounceTime.MS20 => "20 ms",
        DebounceTime.MS24 => "24 ms",
        DebounceTime.MS28 => "28 ms",
        DebounceTime.MS32 => "32 ms",
        _ => time.ToString(),
    };

    private static string PowerOffName(PowerOffSetting setting) => setting switch
    {
        PowerOffSetting.Minutes1 => "1 min",
        PowerOffSetting.Minutes2 => "2 min",
        PowerOffSetting.Minutes3 => "3 min",
        PowerOffSetting.Minutes5 => "5 min",
        PowerOffSetting.Minutes10 => "10 min",
        PowerOffSetting.Never => "Never",
        _ => setting.ToString(),
    };

    private static string LightingModeName(LightingMode mode) => mode switch
    {
        LightingMode.Off => "Off",
        LightingMode.Static => "Static",
        LightingMode.Breathing => "Breathing",
        LightingMode.ColorCycle => "Color Cycle",
        LightingMode.Rainbow => "Rainbow",
        LightingMode.React => "React",
        LightingMode.Comet => "Comet",
        LightingMode.BatteryState => "Battery State",
        _ => mode.ToString(),
    };

    private static string ZoneName(LightingZone zone) => zone switch
    {
        LightingZone.Logo => "Logo",
        LightingZone.Scrollwheel => "Scroll Wheel",
        LightingZone.Underglow => "Underglow",
        LightingZone.All => "All Zones",
        LightingZone.Dock => "Dock",
        _ => zone.ToString(),
    };
}
