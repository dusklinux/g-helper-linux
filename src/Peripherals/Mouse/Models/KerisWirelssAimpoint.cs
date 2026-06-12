namespace GHelper.Linux.Peripherals.Mouse.Models;

public class KerisWirelssAimpoint : AsusMouse
{
    public KerisWirelssAimpoint() : this(0x1A68, "mi_00", true) { }
    protected KerisWirelssAimpoint(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Keris Wireless AimPoint";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override bool HasDPIColors() => true;
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}

public class KerisWirelssAimpointWired : KerisWirelssAimpoint
{
    public KerisWirelssAimpointWired() : base(0x1A66, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Keris Wireless AimPoint (Wired)";
}
