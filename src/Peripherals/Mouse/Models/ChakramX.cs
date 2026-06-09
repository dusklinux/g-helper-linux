namespace GHelper.Linux.Peripherals.Mouse.Models;

public class ChakramX : AsusMouse
{
    public ChakramX() : this(0x1A1A, "mi_00", true) { }
    protected ChakramX(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Chakram X";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override bool HasDPIColors() => true;
}

public class ChakramXWired : ChakramX
{
    public ChakramXWired() : base(0x1A18, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Chakram X (Wired)";
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz,
         PollingRate.PR2000Hz, PollingRate.PR4000Hz, PollingRate.PR8000Hz];
}
