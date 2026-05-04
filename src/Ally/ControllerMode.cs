namespace GHelper.Linux.Ally;

/// <summary>
/// ROG Ally controller behavior mode (matches Windows g-helper Ally/AllyControl.cs).
/// Wire-byte values are sent via [0x5A, 0xD1, 0x01, 0x01, mode].
/// Reference: asusctl rog-platform/examples/ally-gamepad-mode-changes.rs.
///
///   Auto    - software auto-switches between Gamepad/Mouse based on iGPU activity
///   Gamepad - XInput controller mode (default)
///   WASD    - emulates WASD-arrow keyboard for legacy games
///   Mouse   - sticks become mouse, triggers/buttons become clicks
///   Skip    - internal state: don't apply anything (used during init)
/// </summary>
public enum ControllerMode : int
{
    Auto = 0,
    Gamepad = 1,
    WASD = 2,
    Mouse = 3,
    Skip = -1,
}
