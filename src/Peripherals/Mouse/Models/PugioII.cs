namespace GHelper.Linux.Peripherals.Mouse.Models;

public class PugioII : AsusMouse
{
    public PugioII() : this(0x1908, "mi_00", true) { }
    protected PugioII(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Pugio II";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 16000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class PugioIIWired : PugioII
{
    public PugioIIWired() : base(0x1906, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Pugio II (Wired)";
}
