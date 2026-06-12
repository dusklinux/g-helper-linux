namespace GHelper.Linux.Peripherals.Mouse.Models;

public class ChakramCore : AsusMouse
{
    public ChakramCore() : base(0x0B05, 0x1958, "mi_00", false) { }

    public override string GetDisplayName() => "ROG Chakram Core";
    public override int ProfileCount() => 3;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 16000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override bool HasDebounce() => false;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel];
}
