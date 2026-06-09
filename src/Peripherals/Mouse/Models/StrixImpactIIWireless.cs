namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixImpactIIWireless : AsusMouse
{
    public StrixImpactIIWireless() : this(0x1949, "mi_00", true) { }
    protected StrixImpactIIWireless(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Strix Impact II Wireless";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 16000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel];
}

public class StrixImpactIIWirelessWired : StrixImpactIIWireless
{
    public StrixImpactIIWirelessWired() : base(0x1947, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Strix Impact II Wireless (Wired)";
}
