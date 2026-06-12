namespace GHelper.Linux.Peripherals.Mouse.Models;

public class MD200 : AsusMouse
{
    public MD200()
        : base(0x0B05, 0x1A24, "mi_02", true) { }

    public override string GetDisplayName() => "ASUS MD200";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 4200;
    public override bool HasAngleSnapping() => false;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz];
    public override LightingZone[] SupportedLightingZones() => [];
}
