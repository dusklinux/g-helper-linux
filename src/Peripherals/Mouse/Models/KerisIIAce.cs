namespace GHelper.Linux.Peripherals.Mouse.Models;

public class KerisIIAceWired : AsusMouse
{
    public KerisIIAceWired()
        : base(0x0B05, 0x1B16, "mi_00", true) { }

    public override string GetDisplayName() => "ROG Keris II Ace (Wired)";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 42000;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override int AngleTuningStep() => 5;
    public override bool HasDPIColors() => true;
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}
