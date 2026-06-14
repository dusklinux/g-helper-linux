using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

// ponytail: headsets are HID++ devices with battery. Mouse UI auto-hides
// irrelevant sections. Battery + connection features work automatically.

/// <summary>G533 Gaming Headset (USB, PID 0x0A66).</summary>
public class G533Headset : LogitechMouse
{
    public G533Headset() : base(0x0A66, "", wireless: true) { }
    public override string GetDisplayName() => "G533 Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G535 Gaming Headset (USB, PID 0x0AC4).</summary>
public class G535Headset : LogitechMouse
{
    public G535Headset() : base(0x0AC4, "", wireless: true) { }
    public override string GetDisplayName() => "G535 Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G733 Gaming Headset (USB, PID 0x0AB5).</summary>
public class G733Headset : LogitechMouse
{
    public G733Headset() : base(0x0AB5, "", wireless: true) { }
    public override string GetDisplayName() => "G733 Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G733 Gaming Headset new (USB, PID 0x0AFE).</summary>
public class G733HeadsetNew : LogitechMouse
{
    public G733HeadsetNew() : base(0x0AFE, "", wireless: true) { }
    public override string GetDisplayName() => "G733 Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G935 Gaming Headset (USB, PID 0x0A87).</summary>
public class G935Headset : LogitechMouse
{
    public G935Headset() : base(0x0A87, "", wireless: true) { }
    public override string GetDisplayName() => "G935 Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.All];
    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>PRO X Wireless Gaming Headset (USB, PID 0x0ABA).</summary>
public class ProXHeadset : LogitechMouse
{
    public ProXHeadset() : base(0x0ABA, "", wireless: true) { }
    public override string GetDisplayName() => "PRO X Headset";
    public override int DPIProfileCount() => 0;
    public override uint MaxDPI() => 0;
    public override uint MinDPI() => 0;
    public override uint DPIIncrement() => 1;
    public override PollingRate[] SupportedPollingrates() => [];
    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
