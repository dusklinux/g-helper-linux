namespace GHelper.Linux.Peripherals.Mouse.Models;

public class Chakram : AsusMouse
{
    public Chakram() : this(0x18E5, "mi_00", true) { }
    protected Chakram(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Chakram";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 16000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class ChakramWired : Chakram
{
    public ChakramWired() : base(0x18E3, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Chakram (Wired)";
}
