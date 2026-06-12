namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixImpactII : AsusMouse
{
    public StrixImpactII() : this(0x18E1, "mi_00", false) { }
    protected StrixImpactII(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Strix Impact II";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 6200;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class StrixImpactIIElectroPunk : StrixImpactII
{
    public StrixImpactIIElectroPunk() : base(0x1956, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Strix Impact II Electro Punk";
}
