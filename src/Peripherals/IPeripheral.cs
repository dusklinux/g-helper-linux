namespace GHelper.Linux.Peripherals;

public enum PeripheralType { Mouse, Keyboard }

public interface IPeripheral
{
    bool IsDeviceReady { get; }
    bool Wireless { get; }
    int Battery { get; }
    bool Charging { get; }
    bool CanExport();
    byte[] Export();
    bool Import(byte[] blob);
    PeripheralType DeviceType();
    string GetDisplayName();
    bool HasBattery();
    void SynchronizeDevice();
    void ReadBattery();
}
