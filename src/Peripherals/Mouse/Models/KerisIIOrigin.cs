namespace GHelper.Linux.Peripherals.Mouse.Models;

public class KerisIIOriginWired : AsusMouse
{
    public KerisIIOriginWired() : this(0x1C0C, "mi_00", true) { }
    protected KerisIIOriginWired(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Keris II Origin (Wired)";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 42000;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override int AngleTuningStep() => 5;
    public override bool HasDPIColors() => true;
    public override bool CanChangeDPICount() => true;
}

public class KerisIIOriginKJPWired : KerisIIOriginWired
{
    public KerisIIOriginKJPWired() : base(0x1D4C, "mi_00", true) { }
    public override string GetDisplayName() => "ROG Keris II Origin KJP (Wired)";
}
