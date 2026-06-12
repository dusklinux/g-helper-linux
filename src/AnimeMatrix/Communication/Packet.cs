namespace GHelper.Linux.AnimeMatrix.Communication;

/// <summary>
/// Base class for HID packets sent to ASUS AnimeMatrix / Slash devices.
/// Fixed-length byte array where Data[0] is the report ID; payload is appended starting at index 1.
/// </summary>
public abstract class Packet
{
    /// <summary>Current write position (starts at 1, index 0 is the report ID).</summary>
    private int _currentDataIndex = 1;

    public byte[] Data { get; }

    internal Packet(byte reportId, int packetLength, params byte[] data)
    {
        Data = new byte[packetLength];
        Data[0] = reportId;

        if (data.Length > 0)
            AppendData(data);
    }

    /// <summary>Appends payload bytes sequentially after the report ID.</summary>
    public Packet AppendData(params byte[] data)
    {
        return AppendData(out _, data);
    }

    /// <summary>Appends payload bytes and reports how many were written.</summary>
    public Packet AppendData(out int bytesWritten, params byte[] data)
    {
        bytesWritten = 0;

        for (int i = 0; i < data.Length && _currentDataIndex < Data.Length; i++)
        {
            Data[_currentDataIndex++] = data[i];
            bytesWritten++;
        }

        return this;
    }
}
