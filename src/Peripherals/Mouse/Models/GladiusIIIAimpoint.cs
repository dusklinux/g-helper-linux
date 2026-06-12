namespace GHelper.Linux.Peripherals.Mouse.Models;

public class GladiusIIIAimpoint : AsusMouse
{
    public GladiusIIIAimpoint() : this(0x1A72, "mi_00", true) { }
    protected GladiusIIIAimpoint(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Gladius III Wireless AimPoint";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override bool HasDPIColors() => true;
}

public class GladiusIIIAimpointWired : GladiusIIIAimpoint
{
    public GladiusIIIAimpointWired() : base(0x1A70, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Gladius III Wireless AimPoint (Wired)";
}

public class GladiusIIIAimpointEva2 : GladiusIIIAimpoint
{
    public GladiusIIIAimpointEva2() : base(0x1B0C, "mi_00", true) { }
    public override string GetDisplayName() => "ROG Gladius III Wireless AimPoint EVA-02";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}

public class GladiusIIIAimpointEva2Wired : GladiusIIIAimpoint
{
    public GladiusIIIAimpointEva2Wired() : base(0x1B0A, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Gladius III Wireless AimPoint EVA-02 (Wired)";
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}
