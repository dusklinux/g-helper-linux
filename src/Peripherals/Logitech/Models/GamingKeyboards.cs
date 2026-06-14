using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

// Wired gaming keyboards. Each reports a capability subset that the base
// class auto-discovers via HID++ feature probing.

/// <summary>G213 Prodigy Gaming Keyboard (USB, PID 0xC336).</summary>
public class G213 : LogitechMouse
{
    public G213() : base(0xC336, "", wireless: false) { }
    public override string GetDisplayName() => "G213 Prodigy Gaming Keyboard";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G512 RGB Mechanical Gaming Keyboard (USB, PID 0xC33C).</summary>
public class G512 : LogitechMouse
{
    public G512() : base(0xC33C, "", wireless: false) { }
    public override string GetDisplayName() => "G512 RGB Mechanical Gaming Keyboard";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle, LightingMode.Rainbow];
}

/// <summary>G815 Mechanical Keyboard (USB, PID 0xC33F).</summary>
public class G815 : LogitechMouse
{
    public G815() : base(0xC33F, "", wireless: false) { }
    public override string GetDisplayName() => "G815 Mechanical Keyboard";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle, LightingMode.Rainbow];
}

/// <summary>K845 Mechanical Keyboard (USB, PID 0xC341).</summary>
public class K845 : LogitechMouse
{
    public K845() : base(0xC341, "", wireless: false) { }
    public override string GetDisplayName() => "K845 Mechanical Keyboard";
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>Logitech PRO Gaming Keyboard (USB, PID 0xC339).</summary>
public class ProGamingKeyboard : LogitechMouse
{
    public ProGamingKeyboard() : base(0xC339, "", wireless: false) { }
    public override string GetDisplayName() => "PRO Gaming Keyboard";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle, LightingMode.Rainbow];
}

/// <summary>Logitech Illuminated Keyboard K740 (USB, PID 0xC318).</summary>
public class IlluminatedKeyboard : LogitechMouse
{
    public IlluminatedKeyboard() : base(0xC318, "", wireless: false) { }
    public override string GetDisplayName() => "Illuminated Keyboard";
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
