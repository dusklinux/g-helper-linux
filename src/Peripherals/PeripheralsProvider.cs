using Avalonia.Media;
using GHelper.Linux.Helpers;
using HidSharp;

namespace GHelper.Linux.Peripherals;

/// <summary>
/// Central registry for connected ASUS peripheral devices.
/// Detects, connects, and manages mice (keyboards planned).
/// </summary>
public static class PeripheralsProvider
{
    private static readonly object _lock = new();

    public static List<Mouse.AsusMouse> ConnectedMice { get; } = new();

    /// <summary>Fires when a peripheral is added or removed.</summary>
    public static event Action? DeviceChanged;

    public static bool IsAuraSync
    {
        get => AppConfig.Is("aura_sync_mouse");
        set => AppConfig.Set("aura_sync_mouse", value ? 1 : 0);
    }

    /// <summary>Probes for all known ASUS mouse models. Skips already-connected models.</summary>
    public static void DetectAllAsusMice()
    {
        DetectMouse(new Mouse.Models.ChakramX());
        DetectMouse(new Mouse.Models.ChakramXWired());
        DetectMouse(new Mouse.Models.GladiusIIIAimpoint());
        DetectMouse(new Mouse.Models.GladiusIIIAimpointWired());
        DetectMouse(new Mouse.Models.GladiusIIOrigin());
        DetectMouse(new Mouse.Models.GladiusIIOriginPink());
        DetectMouse(new Mouse.Models.GladiusII());
        DetectMouse(new Mouse.Models.GladiusIIWireless());
        DetectMouse(new Mouse.Models.KerisWireless());
        DetectMouse(new Mouse.Models.KerisWirelessWired());
        DetectMouse(new Mouse.Models.Keris());
        DetectMouse(new Mouse.Models.KerisWirelessEvaEdition());
        DetectMouse(new Mouse.Models.KerisWirelessEvaEditionWired());
        DetectMouse(new Mouse.Models.TUFM4Air());
        DetectMouse(new Mouse.Models.TUFM4Wirelss());
        DetectMouse(new Mouse.Models.TUFM4WirelssCN());
        DetectMouse(new Mouse.Models.StrixImpactIIWireless());
        DetectMouse(new Mouse.Models.StrixImpactIIWirelessWired());
        DetectMouse(new Mouse.Models.GladiusIIIWireless());
        DetectMouse(new Mouse.Models.GladiusIIIWired());
        DetectMouse(new Mouse.Models.GladiusIII());
        DetectMouse(new Mouse.Models.GladiusIIIAimpointEva2());
        DetectMouse(new Mouse.Models.GladiusIIIAimpointEva2Wired());
        DetectMouse(new Mouse.Models.HarpeAceAimLabEdition());
        DetectMouse(new Mouse.Models.HarpeAceAimLabEditionWired());
        DetectMouse(new Mouse.Models.HarpeAceMiniWired());
        DetectMouse(new Mouse.Models.HarpeAceMiniOmni());
        DetectMouse(new Mouse.Models.HarpeIIAceWireless());
        DetectMouse(new Mouse.Models.HarpeIIAceWired());
        DetectMouse(new Mouse.Models.TUFM3());
        DetectMouse(new Mouse.Models.TUFM3GenII());
        DetectMouse(new Mouse.Models.TUFM5());
        DetectMouse(new Mouse.Models.KerisWirelssAimpoint());
        DetectMouse(new Mouse.Models.KerisWirelssAimpointWired());
        DetectMouse(new Mouse.Models.KerisIIAceWired());
        DetectMouse(new Mouse.Models.KerisIIOriginWired());
        DetectMouse(new Mouse.Models.KerisIIOriginKJPWired());
        DetectMouse(new Mouse.Models.PugioII());
        DetectMouse(new Mouse.Models.PugioIIWired());
        DetectMouse(new Mouse.Models.StrixImpactII());
        DetectMouse(new Mouse.Models.StrixImpactIIElectroPunk());
        DetectMouse(new Mouse.Models.Chakram());
        DetectMouse(new Mouse.Models.ChakramWired());
        DetectMouse(new Mouse.Models.ChakramCore());
        DetectMouse(new Mouse.Models.SpathaX());
        DetectMouse(new Mouse.Models.SpathaXWired());
        DetectMouse(new Mouse.Models.StrixCarry());
        DetectMouse(new Mouse.Models.StrixImpactIII());
        DetectMouse(new Mouse.Models.StrixImpactIIIWirelessOmni());
        DetectMouse(new Mouse.Models.StrixImpact());
        DetectMouse(new Mouse.Models.TXGamingMini());
        DetectMouse(new Mouse.Models.TXGamingMiniWired());
        DetectMouse(new Mouse.Models.Pugio());
        DetectMouse(new Mouse.Models.MD200());
    }

    private static void DetectMouse(Mouse.AsusMouse am)
    {
        if (am.IsDeviceConnected())
        {
            lock (_lock)
            {
                if (ConnectedMice.Any(m => m.GetDisplayName() == am.GetDisplayName()))
                    return;

                try
                {
                    am.Connect();
                    am.SynchronizeDevice();
                    ConnectedMice.Add(am);
                    DeviceChanged?.Invoke();
                    Logger.WriteLine($"Peripherals: connected {am.GetDisplayName()}");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Peripherals: failed to connect {am.GetDisplayName()}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Re-reads battery level from every connected mouse.</summary>
    public static void RefreshBatteryForAllDevices(bool force = false)
    {
        lock (_lock)
        {
            foreach (var m in ConnectedMice)
            {
                try
                {
                    m.ReadBattery();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Peripherals: battery read failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Streams a single colour to all connected mice (Aura Sync).</summary>
    public static void StreamMouseColor(Color color)
    {
        lock (_lock)
        {
            foreach (var m in ConnectedMice)
            {
                try
                {
                    m.WriteColorDirect(color);
                }
                catch
                {
                    // Swallow, streaming is best-effort
                }
            }
        }
    }

    /// <summary>Subscribes to HidSharp device-change events to re-scan on plug/unplug.</summary>
    public static void RegisterForDeviceEvents()
    {
        try
        {
            DeviceList.Local.Changed += (_, _) =>
            {
                Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => DetectAllAsusMice());
            };
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Peripherals: HID event registration failed: {ex.Message}");
        }
    }
}
