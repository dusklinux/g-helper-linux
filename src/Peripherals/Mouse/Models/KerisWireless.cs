namespace GHelper.Linux.Peripherals.Mouse.Models;

public class KerisWireless : AsusMouse
{
    public KerisWireless() : this(0x1960, "mi_00", true) { }
    protected KerisWireless(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Keris Wireless";
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

public class KerisWirelessWired : KerisWireless
{
    public KerisWirelessWired() : base(0x195E, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Keris Wireless (Wired)";
}

public class Keris : KerisWireless
{
    public Keris() : base(0x195C, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Keris";
}

public class KerisWirelessEvaEdition : KerisWireless
{
    public KerisWirelessEvaEdition() : base(0x1A59, "mi_00", true) { }
    public override string GetDisplayName() => "ROG Keris Wireless EVA Edition";
}

public class KerisWirelessEvaEditionWired : KerisWireless
{
    public KerisWirelessEvaEditionWired() : base(0x1A57, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Keris Wireless EVA Edition (Wired)";
}
