namespace GHelper.Linux.Peripherals.Mouse.Models;

public class HarpeIIAceWired : AsusMouse
{
    public HarpeIIAceWired() : this(0x1C69, "mi_00", false) { }
    protected HarpeIIAceWired(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Harpe II Ace (Wired)";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 42000;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz,
         PollingRate.PR2000Hz, PollingRate.PR4000Hz, PollingRate.PR8000Hz];
    public override bool HasMotionSync() => true;
    public override bool CanChangeDPICount() => true;
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Scrollwheel];
}

public class HarpeIIAceWireless : HarpeIIAceWired
{
    public HarpeIIAceWireless() : base(0x1AD0, "mi_02&col03", true, 0x03) { }
    public override string GetDisplayName() => "ROG Harpe II Ace";
    public override bool HasZoneMode() => true;
}
