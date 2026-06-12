namespace GHelper.Linux.Peripherals.Mouse.Models;

public class StrixImpactIIIWirelessOmni : AsusMouse
{
    public StrixImpactIIIWirelessOmni() : base(0x0B05, 0x1ACE, "mi_02&col03", true, 0x03) { }

    public override string GetDisplayName() => "Strix Impact III Wireless (OMNI)";
    public override int ProfileCount() => 5;
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 36000;
    public override bool HasXYDPI() => true;
    public override bool HasAngleTuning() => true;
    public override bool HasLowBatteryWarning() => true;
    public override bool HasDPIColors() => true;
    public override int AngleTuningStep() => 5;
    public override int USBPacketSize() => 64;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
    public override LightingZone[] SupportedLightingZones() => [LightingZone.Scrollwheel];
}
