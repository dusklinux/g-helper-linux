namespace GHelper.Linux.Peripherals.Mouse.Models;

public class TUFM5 : AsusMouse
{
    public TUFM5()
        : base(0x0B05, 0x1898, "mi_02", false) { }

    public override string GetDisplayName() => "TUF Gaming M5";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 6200;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}
