namespace GHelper.Linux.Peripherals.Mouse.Models;

public class HarpeAceAimLabEdition : AsusMouse
{
    public HarpeAceAimLabEdition() : this(0x1A94, "mi_00", true) { }
    protected HarpeAceAimLabEdition(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Harpe Ace Aim Lab Edition";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MinDPI() => 50;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override bool HasDPIColors() => true;
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Scrollwheel];
}

public class HarpeAceAimLabEditionWired : HarpeAceAimLabEdition
{
    public HarpeAceAimLabEditionWired() : base(0x1A92, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Harpe Ace Aim Lab Edition (Wired)";
}
