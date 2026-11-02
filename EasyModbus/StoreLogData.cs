/*
Copyright (c) 2018-2020 Rossmann-Engineering
Permission is hereby granted, free of charge,
to any person obtaining a copy of this software
and associated documentation files (the "Software"),
to deal in the Software without restriction,
including without limitation the rights to use,
copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission
notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
