namespace GHelper.Linux.Peripherals.Mouse.Models;

public class GladiusIIIWireless : AsusMouse
{
    public GladiusIIIWireless() : this(0x197F, "mi_00", true) { }
    protected GladiusIIIWireless(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Gladius III Wireless";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 26000;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class GladiusIIIWired : GladiusIIIWireless
{
    public GladiusIIIWired() : base(0x197D, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Gladius III Wireless (Wired)";
}

public class GladiusIII : GladiusIIIWireless
{
    public GladiusIII() : base(0x197B, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Gladius III";
}
