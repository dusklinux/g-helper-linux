namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixImpactIII : AsusMouse
{
    public StrixImpactIII()
        : base(0x0B05, 0x1A88, "mi_00", false) { }

    public override string GetDisplayName() => "ROG Strix Impact III";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 12000;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel];
}
