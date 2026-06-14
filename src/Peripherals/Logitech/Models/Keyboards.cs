using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

// ponytail: keyboards are HID++ devices. LogitechMouse base class + auto-discovery
// handles them. Mouse-specific UI auto-hides when DPI/scroll features absent.

/// <summary>Craft Advanced Keyboard (BT, PID 0xB350).</summary>
public class CraftKeyboard : LogitechMouse
{
    public CraftKeyboard() : base(0xB350, "", wireless: true) { }
    public override string GetDisplayName() => "Craft Keyboard";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Keys Keyboard (BT, PID 0xB35B).</summary>
public class MXKeysKeyboard : LogitechMouse
{
    public MXKeysKeyboard() : base(0xB35B, "", wireless: true) { }
    public override string GetDisplayName() => "MX Keys";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G915 TKL keyboard (USB, PID 0xC343).</summary>
public class G915TKL : LogitechMouse
{
    public G915TKL() : base(0xC343, "", wireless: true) { }
    public override string GetDisplayName() => "G915 TKL";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
