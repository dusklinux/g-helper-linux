using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>MX Revolution M-RCL 124 (Bluetooth, PID 0xB007).</summary>
public class MXRevolution : LogitechMouse
{
    public MXRevolution() : base(0xB007, "", wireless: true) { }

    public override string GetDisplayName() => "MX Revolution";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 1600;
    public override uint MinDPI() => 400;
    public override uint DPIIncrement() => 50;

    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz];

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
