namespace GHelper.Linux.Peripherals.Mouse.Models;

public class SpathaX : AsusMouse
{
    public SpathaX() : this(0x1979, "mi_00", true) { }
    protected SpathaX(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Spatha X";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 19000;
    public override bool HasDPIColors() => true;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class SpathaXWired : SpathaX
{
    public SpathaXWired() : base(0x1977, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Spatha X (Wired)";
}
