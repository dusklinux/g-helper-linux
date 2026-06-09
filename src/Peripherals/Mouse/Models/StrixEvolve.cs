namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixEvolve : AsusMouse
{
    public StrixEvolve()
        : base(0x0B05, 0x185B, "mi_00", false) { }

    public override string GetDisplayName() => "ROG Strix Evolve";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 7200;
    public override uint MinDPI() => 50;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}
