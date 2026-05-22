// Minimal in-memory implementations of IAsusWmi and IPowerManager for
// the GpuModeController scenario tests. Only methods the controller
// actually calls are wired up; the rest throw NotImplementedException
// so accidental usage in a test surfaces loudly.

using GHelper.Linux.Platform;

namespace GHelper.Linux.Tests;

public sealed class FakeAsusWmi : IAsusWmi
{
    // Mutable state the scenario can read and assert on.
    public bool EcoEnabled;            // true ⇒ dgpu_disable=1
    public int MuxMode = 1;            // 1 = hybrid, 0 = Ultimate (dGPU direct)
    public bool DgpuDisableSupported = true;
    public bool MuxModeSupported = true;
    public bool CanToggleBackendValue = true;
    public bool ThrowOnSetGpuEco;      // simulate firmware rejection
    public bool ThrowOnSetGpuMuxMode;
    public Exception? NextSetGpuEcoException;

    // Call recording - useful for asserting side effects.
    public readonly List<(string Method, object? Arg)> Calls = new();

    public bool GetGpuEco()
    {
        Calls.Add(("GetGpuEco", null));
        return EcoEnabled;
    }

    public void SetGpuEco(bool enabled)
    {
        Calls.Add(("SetGpuEco", enabled));
        if (NextSetGpuEcoException != null)
        {
            var ex = NextSetGpuEcoException;
            NextSetGpuEcoException = null;
            throw ex;
        }
        if (ThrowOnSetGpuEco) throw new IOException("simulated firmware rejection");
        EcoEnabled = enabled;
    }

    public int GetGpuMuxMode()
    {
        Calls.Add(("GetGpuMuxMode", null));
        return MuxModeSupported ? MuxMode : 1;
    }

    public void SetGpuMuxMode(int mode)
    {
        Calls.Add(("SetGpuMuxMode", mode));
        if (ThrowOnSetGpuMuxMode) throw new IOException("simulated MUX rejection");
        if (EcoEnabled) throw new InvalidOperationException("MUX write rejected while dgpu_disable=1");
        MuxMode = mode;
    }

    public bool IsFeatureSupported(string feature)
    {
        Calls.Add(("IsFeatureSupported", feature));
        // The two feature checks that matter to GpuModeController paths
        if (feature == "dgpu_disable") return DgpuDisableSupported;
        if (feature == "gpu_mux_mode") return MuxModeSupported;
        return false;
    }

    public bool IsGpuEcoAvailable() => DgpuDisableSupported;
    public bool CanToggleGpuBackend() => CanToggleBackendValue;
    public int FanCount => 2;

    public event Action<int>? WmiEvent;
    public event Action<string>? KeyBindingEvent;

    // Everything below is irrelevant for these tests - left as throwing
    // stubs so a regression that suddenly calls them is visible.
    public int DeviceGet(int deviceId) => -1;
    public int DeviceSet(int deviceId, int value) => 0;
    public byte[]? DeviceGetBuffer(int deviceId, int args = 0) => null;
    public int GetThrottleThermalPolicy() => 0;
    public void SetThrottleThermalPolicy(int mode) { }
    public int GetFanRpm(int fanIndex) => 0;
    public byte[]? GetFanCurve(int fanIndex) => null;
    public void SetFanCurve(int fanIndex, byte[] curve) { }
    public void DisableFanCurve(int fanIndex) { }
    public byte[]? ResetFanCurveToDefaults(int fanIndex) => null;
    public bool IsFanCurveEnabled(int fanIndex) => false;
    public int GetBatteryChargeLimit() => 100;
    public bool SetBatteryChargeLimit(int percent) => true;
    public bool GetPanelOverdrive() => false;
    public void SetPanelOverdrive(bool enabled) { }
    public int GetMiniLedMode() => 0;
    public void SetMiniLedMode(int mode) { }
    public int GetScreenAutoBrightness() => -1;
    public void SetScreenAutoBrightness(bool enabled) { }
    public void SetPptLimit(string attribute, int watts) { }
    public int GetPptLimit(string attribute) => -1;
    public int GetKeyboardBrightness() => 0;
    public void SetKeyboardBrightness(int level) { }
    public void SetKeyboardRgb(byte r, byte g, byte b) { }
    public void SubscribeEvents() { }
    public void Dispose() { }

    public void TriggerWmiEvent(int code) => WmiEvent?.Invoke(code);
    public void TriggerKeyBindingEvent(string name) => KeyBindingEvent?.Invoke(name);
}

public sealed class FakePowerManager : IPowerManager
{
    public bool OnAc = true;
    public bool IsOnAcPower() => OnAc;

    public void SetCpuBoost(bool enabled) { }
    public bool GetCpuBoost() => true;
    public void SetPlatformProfile(string profile) { }
    public string GetPlatformProfile() => "balanced";
    public string[] GetPlatformProfileChoices() => Array.Empty<string>();
    public void SetAspmPolicy(string policy) { }
    public string GetAspmPolicy() => "default";
    public int GetBatteryPercentage() => 100;
    public int GetBatteryDrainRate() => 0;
    public int GetBatteryHealth() => 100;
    public event Action<bool>? PowerStateChanged;
    public void StartPowerMonitoring() { }
    public void StopPowerMonitoring() { }
    public void TriggerPowerStateChanged(bool onAc) => PowerStateChanged?.Invoke(onAc);
}
