namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixCarry : AsusMouse
{
    public StrixCarry()
        : base(0x0B05, 0x18B4, "mi_01", true) { }

    public override string GetDisplayName() => "ROG Strix Carry";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 7200;
    public override uint MinDPI() => 50;
    public override uint DPIIncrement() => 50;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [];
}
