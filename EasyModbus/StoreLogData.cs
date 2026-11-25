using System.Diagnostics;
using System.Globalization;

namespace EasyModbus;

/// <summary>Optional process-wide file logging retained for API compatibility.</summary>
public sealed class StoreLogData
{
    private readonly Lock _writeLock = new();
    public static StoreLogData Instance { get; } = new();
    public string? Filename
    {
        get; set;
    }
    private StoreLogData()
    {
    }
    public void Store(string message) => Write(message);
    public void Store(string message, DateTime timestamp) => Write(timestamp.ToString("dd.MM.yyyy H:mm:ss.ff ", CultureInfo.InvariantCulture) + message);
    private void Write(string message)
    {
        var filename = Filename;
        if (string.IsNullOrWhiteSpace(filename))
            return;
        try
        {
            lock (_writeLock)
                File.AppendAllText(filename, message + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceWarning($"Modbus file logging failed: {exception.Message}");
        }
    }
}
