namespace GHelper.Linux.Peripherals.Logitech.HidPP;

/// <summary>
/// HID++ 2.0 feature IDs. Each device exposes a subset of these features,
/// discovered at runtime via the FEATURE_SET index table.
/// </summary>
public static class Feature
{
    public const ushort ROOT = 0x0000;
    public const ushort FEATURE_SET = 0x0001;
    public const ushort FEATURE_INFO = 0x0002;
    public const ushort DEVICE_FW_VERSION = 0x0003;
    public const ushort DEVICE_UNIT_ID = 0x0004;
    public const ushort DEVICE_NAME = 0x0005;
    public const ushort DEVICE_GROUPS = 0x0006;
    public const ushort DEVICE_FRIENDLY_NAME = 0x0007;
    public const ushort KEEP_ALIVE = 0x0008;
    public const ushort CONFIG_CHANGE = 0x0020;
    public const ushort CRYPTO_ID = 0x0021;
    public const ushort TARGET_SOFTWARE = 0x0030;
    public const ushort WIRELESS_SIGNAL_STRENGTH = 0x0080;

    public const ushort DFUCONTROL_LEGACY = 0x00C0;
    public const ushort DFUCONTROL_UNSIGNED = 0x00C1;
    public const ushort DFUCONTROL_SIGNED = 0x00C2;
    public const ushort DFUCONTROL = 0x00C3;
    public const ushort DFU = 0x00D0;

    public const ushort BATTERY_STATUS = 0x1000;
    public const ushort BATTERY_VOLTAGE = 0x1001;
    public const ushort UNIFIED_BATTERY = 0x1004;
    public const ushort CHARGING_CONTROL = 0x1010;

    public const ushort LED_CONTROL = 0x1300;
    public const ushort GENERIC_TEST = 0x1800;
    public const ushort DEVICE_RESET = 0x1802;
    public const ushort OOBSTATE = 0x1805;
    public const ushort CHANGE_HOST = 0x1814;
    public const ushort HOSTS_INFO = 0x1815;

    public const ushort BACKLIGHT = 0x1981;
    public const ushort BACKLIGHT2 = 0x1982;
    public const ushort BACKLIGHT3 = 0x1983;
    public const ushort ILLUMINATION = 0x1990;

    public const ushort HAPTIC = 0x19B0;
    public const ushort FORCE_SENSING_BUTTON = 0x19C0;

    public const ushort REPROG_CONTROLS = 0x1B00;
    public const ushort REPROG_CONTROLS_V2 = 0x1B01;
    public const ushort REPROG_CONTROLS_V3 = 0x1B03;
    public const ushort REPROG_CONTROLS_V4 = 0x1B04;
    public const ushort ANALOG_BUTTONS = 0x1B0C;
    public const ushort PERSISTENT_REMAPPABLE_ACTION = 0x1C00;
    public const ushort WIRELESS_DEVICE_STATUS = 0x1D4B;
    public const ushort ENABLE_HIDDEN_FEATURES = 0x1E00;
    public const ushort FIRMWARE_PROPERTIES = 0x1F1F;

    public const ushort LEFT_RIGHT_SWAP = 0x2001;
    public const ushort SWAP_BUTTON_CANCEL = 0x2005;
    public const ushort VERTICAL_SCROLLING = 0x2100;
    public const ushort SMART_SHIFT = 0x2110;
    public const ushort SMART_SHIFT_ENHANCED = 0x2111;
    public const ushort HI_RES_SCROLLING = 0x2120;
    public const ushort HIRES_WHEEL = 0x2121;
    public const ushort LOWRES_WHEEL = 0x2130;
    public const ushort THUMB_WHEEL = 0x2150;
    public const ushort MOUSE_POINTER = 0x2200;
    public const ushort ADJUSTABLE_DPI = 0x2201;
    public const ushort EXTENDED_ADJUSTABLE_DPI = 0x2202;
    public const ushort POINTER_SPEED = 0x2205;
    public const ushort ANGLE_SNAPPING = 0x2230;
    public const ushort SURFACE_TUNING = 0x2240;

    public const ushort FN_INVERSION = 0x40A0;
    public const ushort NEW_FN_INVERSION = 0x40A2;
    public const ushort K375S_FN_INVERSION = 0x40A3;
    public const ushort LOCK_KEY_STATE = 0x4220;
    public const ushort KEYBOARD_DISABLE_KEYS = 0x4521;
    public const ushort DUALPLATFORM = 0x4530;
    public const ushort MULTIPLATFORM = 0x4531;
    public const ushort CROWN = 0x4600;

    public const ushort GESTURE_2 = 0x6501;

    public const ushort GKEY = 0x8010;
    public const ushort MKEYS = 0x8020;
    public const ushort MR = 0x8030;
    public const ushort BRIGHTNESS_CONTROL = 0x8040;
    public const ushort REPORT_RATE = 0x8060;
    public const ushort EXTENDED_ADJUSTABLE_REPORT_RATE = 0x8061;
    public const ushort COLOR_LED_EFFECTS = 0x8070;
    public const ushort RGB_EFFECTS = 0x8071;
    public const ushort PER_KEY_LIGHTING = 0x8080;
    public const ushort PER_KEY_LIGHTING_V2 = 0x8081;
    public const ushort MODE_STATUS = 0x8090;
    public const ushort ONBOARD_PROFILES = 0x8100;
    public const ushort MOUSE_BUTTON_SPY = 0x8110;

    public const ushort SIDETONE = 0x8300;
    public const ushort EQUALIZER = 0x8310;
    public const ushort HEADSET_OUT = 0x8320;

    // Centurion-transport headset features (G733 new / G522 / PRO X 2 etc).
    // Use the 0x0XXX subfeature range routed through Centurion frames.
    public const ushort HEADSET_VOLUME = 0x0200;
    public const ushort HEADSET_EQ = 0x0201;
    public const ushort HEADSET_ADVANCED_PARA_EQ = 0x020D;
    public const ushort HEADSET_MIC_TEST = 0x020E;
    public const ushort HEADSET_EQ_STYLES = 0x0213;
    public const ushort HEADSET_RGB_EFFECTS = 0x0600;
    public const ushort HEADSET_MIC_MUTE = 0x0601;
    public const ushort HEADSET_MIC_SNR = 0x0602;
    public const ushort HEADSET_AUDIO_SIDETONE = 0x0604;
    public const ushort HEADSET_HOST_SWITCH = 0x0607;
    public const ushort HEADSET_MIX = 0x0609;
    public const ushort HEADSET_TONES = 0x060B;
    public const ushort HEADSET_AI_NOISE_REDUCTION = 0x060E;
    public const ushort HEADSET_MIC_GAIN = 0x0611;
    public const ushort HEADSET_BATTERY_SAVER = 0x0618;
    public const ushort HEADSET_RGB_HOSTMODE = 0x0620;
    public const ushort HEADSET_RGB_ONBOARD_EFFECTS = 0x0621;
    public const ushort HEADSET_RGB_SIGNATURE_EFFECTS = 0x0622;
    public const ushort HEADSET_DO_NOT_DISTURB = 0x0631;
    public const ushort HEADSET_RGB_STREAMING = 0x0635;
    public const ushort HEADSET_ONBOARD_EQ = 0x0636;
    public const ushort CENTURION_AUTO_SLEEP = 0x0108;
    public const ushort ADC_MEASUREMENT = 0x1F20;
}

/// <summary>
/// HID++ 2.0 error codes returned in error response byte [3].
/// </summary>
public enum HidPPError : byte
{
    Success = 0x00,
    Unknown = 0x01,
    InvalidArgument = 0x02,
    OutOfRange = 0x03,
    HardwareError = 0x04,
    LogitechError = 0x05,
    InvalidFeatureIndex = 0x06,
    InvalidFunctionId = 0x07,
    Busy = 0x08,
    Unsupported = 0x09,
}

/// <summary>
/// HID++ 1.0 error codes returned in 0x8F error response byte [3].
/// </summary>
public enum Hidpp10Error : byte
{
    InvalidSubIdCommand = 0x01,
    InvalidAddress = 0x02,
    InvalidValue = 0x03,
    ConnectionRequestFailed = 0x04,
    TooManyDevices = 0x05,
    AlreadyExists = 0x06,
    Busy = 0x07,
    UnknownDevice = 0x08,
    ResourceError = 0x09,
    RequestUnavailable = 0x0A,
    UnsupportedParameterValue = 0x0B,
    WrongPinCode = 0x0C,
}

/// <summary>
/// Device type as reported by DEVICE_NAME feature (function 0x20).
/// </summary>
public enum DeviceKind : byte
{
    Keyboard = 0x00,
    RemoteControl = 0x01,
    Numpad = 0x02,
    Mouse = 0x03,
    Touchpad = 0x04,
    Trackball = 0x05,
    Presenter = 0x06,
    Receiver = 0x07,
}
