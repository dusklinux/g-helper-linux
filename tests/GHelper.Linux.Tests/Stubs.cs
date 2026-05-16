// Minimal stubs for types the SUT references but the tests do not exercise.
// These let GpuModeController compile in isolation without dragging Avalonia,
// libusb, SkiaSharp etc. into the test binary.

namespace GHelper.Linux.USB;

/// <summary>
/// Stub AURA mode enum. The real enum lives in src/USB/Aura.cs and is
/// referenced by GpuModeController.RequestModeSwitch when deciding whether
/// to refresh the keyboard's GPU-color effect after a successful mode
/// switch. Tests never set <c>aura_mode</c> so the cast result never
/// equals <c>GpuMode</c> and <c>ApplyGpuColor()</c> is never called.
/// </summary>
public enum AuraMode
{
    Static = 0,
    Breathe = 1,
    Strobe = 2,
    GpuMode = 99,
}

/// <summary>
/// Stub for the AURA keyboard colour helper. No-op in tests; production
/// code talks to the HID device which is absent in the test environment.
/// </summary>
public static class CustomRgb
{
    public static void ApplyGpuColor()
    {
        // Intentionally empty - stubbed for tests.
    }
}
