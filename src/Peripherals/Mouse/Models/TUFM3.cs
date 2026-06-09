namespace GHelper.Linux.Peripherals.Mouse.Models;

public class TUFM3 : AsusMouse
{
    public TUFM3() : this(0x1910, "mi_01", false) { }
    protected TUFM3(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "TUF Gaming M3";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 7000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];
}

public class TUFM3GenII : TUFM3
{
    public TUFM3GenII() : base(0x1A9B, "mi_02", false) { }
    public override string GetDisplayName() => "TUF Gaming M3 Gen II";
    public override uint MaxDPI() => 8000;
    public override uint DPIIncrement() => 50;
    public override int MaxBrightness() => 100;
    public override bool HasDPIColors() => true;
}
