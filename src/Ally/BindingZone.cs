namespace GHelper.Linux.Ally;

/// <summary>
/// Binding zone enum from Windows AllyControl.cs (verbatim values).
/// Each zone covers a logical pair of physical inputs that share a binding
/// packet (e.g. DPadUpDown = D-Pad-Up + D-Pad-Down sent in one HID write).
///
/// Wire byte = (byte)zone in [0x5A, 0xD1, 0x02, zone, 0x2C, ...] packet.
/// </summary>
public enum BindingZone : byte
{
    DPadUpDown = 1,
    DPadLeftRight = 2,
    StickClick = 3,
    Bumper = 4,
    AB = 5,
    XY = 6,
    ViewMenu = 7,
    M1M2 = 8,
    Trigger = 9,
}
