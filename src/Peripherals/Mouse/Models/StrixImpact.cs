namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixImpact : AsusMouse
{
    public StrixImpact()
        : base(0x0B05, 0x1847, "mi_02", false) { }

    public override string GetDisplayName() => "ROG Strix Impact";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 5000;
    public override uint MinDPI() => 50;
    public override uint DPIIncrement() => 50;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}
