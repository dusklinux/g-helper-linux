using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech;

/// <summary>
/// Per-wpid metadata for devices paired through a Unifying / Lightspeed receiver.
/// LogitechReceiverMouse looks up the wpid reported by the receiver pairing
/// register and applies the friendly name and hardware limits below.
/// </summary>
public static class ReceiverPairedDevices
{
    public record PairedSpec(
        string Name,
        DeviceKind Kind = DeviceKind.Mouse,
        uint MinDpi = 200,
        uint MaxDpi = 16000,
        uint DpiStep = 50,
        PollingRate[]? PollingRates = null);

    public enum DeviceKind { Mouse, Keyboard, Numpad, Touchpad, Trackball }

    public static readonly Dictionary<ushort, PairedSpec> ByWpid = new()
    {
        // Keyboards
        [0x0055] = new("Wireless Keyboard EX110", DeviceKind.Keyboard),
        [0x0056] = new("Wireless Keyboard S510", DeviceKind.Keyboard),
        [0x0060] = new("Wireless Wave Keyboard K550", DeviceKind.Keyboard),
        [0x0065] = new("Wireless Keyboard EX100", DeviceKind.Keyboard),
        [0x0068] = new("Wireless Keyboard MK300", DeviceKind.Keyboard),
        [0x2006] = new("Number Pad N545", DeviceKind.Numpad),
        [0x2007] = new("Wireless Compact Keyboard K340", DeviceKind.Keyboard),
        [0x2008] = new("Wireless Keyboard MK700", DeviceKind.Keyboard),
        [0x200A] = new("Wireless Wave Keyboard K350", DeviceKind.Keyboard),
        [0x200F] = new("Wireless Keyboard MK320", DeviceKind.Keyboard),
        [0x2011] = new("Wireless Keyboard K520", DeviceKind.Keyboard),
        [0x4002] = new("Wireless Solar Keyboard K750", DeviceKind.Keyboard),
        [0x4003] = new("Wireless Keyboard K270 (unifying)", DeviceKind.Keyboard),
        [0x4004] = new("Wireless Keyboard K360", DeviceKind.Keyboard),
        [0x400D] = new("Wireless Keyboard K230", DeviceKind.Keyboard),
        [0x4023] = new("Wireless Keyboard MK270", DeviceKind.Keyboard),
        [0x4032] = new("Illuminated Living-Room Keyboard K830", DeviceKind.Keyboard),
        [0x4061] = new("Wireless Keyboard K375s", DeviceKind.Keyboard),
        [0x405B] = new("Wireless Multi-Device Keyboard K780", DeviceKind.Keyboard),
        [0x4075] = new("Wireless Keyboard K470", DeviceKind.Keyboard),
        [0x404D] = new("Wireless Touch Keyboard K400 Plus", DeviceKind.Keyboard),
        [0x406E] = new("Wireless Illuminated Keyboard K800 new", DeviceKind.Keyboard),
        [0xC318] = new("Illuminated Keyboard", DeviceKind.Keyboard),
        [0xC714] = new("diNovo Edge Keyboard", DeviceKind.Keyboard),

        // Touchpads
        [0x4011] = new("Wireless Touchpad", DeviceKind.Touchpad),
        [0x4101] = new("Wireless Rechargeable Touchpad T650", DeviceKind.Touchpad),

        // Mice with HID++ 1.0 register 0x63 DPI range 100-1500
        [0x003C] = new("Wireless Wave Mouse M550", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x003F] = new("Wireless Mouse EX100", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x0036] = new("LX5 Cordless Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x0039] = new("LX7 Cordless Laser Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x0085] = new("Wireless Mouse M30", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1001] = new("MX610 Laser Cordless Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1002] = new("G7 Cordless Laser Mouse", MinDpi: 100, MaxDpi: 2000, DpiStep: 100),
        [0x1003] = new("V400 Laser Cordless Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1004] = new("MX610 Left-Handled Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1005] = new("V450 Laser Cordless Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1017] = new("Anywhere Mouse MX", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1024] = new("Wireless Mouse M310", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1025] = new("Wireless Mouse M510", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
        [0x1029] = new("Fujitsu Sonic Mouse", MinDpi: 100, MaxDpi: 1500, DpiStep: 100),

        // Mice with HID++ 2.0+ ADJUSTABLE_DPI auto-discovered
        [0x4007] = new("Couch Mouse M515"),
        [0x4008] = new("Wireless Mouse M175"),
        [0x400A] = new("Wireless Mouse M325"),
        [0x4013] = new("Wireless Mouse M525"),
        [0x4017] = new("Wireless Mouse M345"),
        [0x4019] = new("Wireless Mouse M187"),
        [0x401A] = new("Touch Mouse M600"),
        [0x4022] = new("Wireless Mouse M150"),
        [0x4038] = new("Wireless Mouse M185"),
        [0x4051] = new("Wireless Mouse M510"),
        [0x4054] = new("Wireless Mouse M185 new"),
        [0x4055] = new("Wireless Mouse M185/M235/M310"),
        [0x406B] = new("Multi Device Silent Mouse M585/M590"),
        [0x4080] = new("Wireless Mouse Pebble M350"),
        [0x4093] = new("PRO X Wireless"),

        // Trackball (wpid 0x1028 from kernel hid-logitech-dj table)
        [0x1028] = new("Wireless Trackball M570", DeviceKind.Trackball, MinDpi: 100, MaxDpi: 1500, DpiStep: 100),
    };

    public static string? NameFor(ushort wpid) =>
        ByWpid.TryGetValue(wpid, out var spec) ? spec.Name : null;

    public static PairedSpec? SpecFor(ushort wpid) =>
        ByWpid.TryGetValue(wpid, out var spec) ? spec : null;
}
