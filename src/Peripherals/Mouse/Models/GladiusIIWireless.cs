namespace GHelper.Linux.Peripherals.Mouse.Models;

public class GladiusIIWireless : AsusMouse
{
    public GladiusIIWireless() : base(0x0B05, 0x18A0, "mi_02", true) { }

    public override string GetDisplayName() => "Gladius II Wireless";
    public override int ProfileCount() => 1;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 16000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override bool HasAngleTuning() => true;
    public override bool HasLowBatteryWarning() => true;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel];
}
