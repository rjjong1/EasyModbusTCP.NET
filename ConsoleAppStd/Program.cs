using System.Globalization;
using System.Net;
using EasyModbus;

if (args.Length > 2)
{
    Console.Error.WriteLine("Usage: ConsoleAppStd [host [port]]");
    return 1;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await using var server = args.Length == 0 ? new ModbusServer { LocalIPAddress = IPAddress.Loopback, Port = 0 } : null;
try
{
    if (server is not null)
    {
        server.holdingRegisters[1] = 123;
        server.holdingRegisters[2] = 456;
        server.Listen();
    }
    var host = args.Length > 0 ? args[0] : "127.0.0.1";
    var port = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : server?.Port ?? 502;
    await using var client = new ModbusClient(host, port);
    await client.ConnectAsync(timeout.Token);
    var values = await client.ReadHoldingRegistersAsync(0, 2, timeout.Token);
    Console.WriteLine($"Holding registers 0 and 1: {string.Join(", ", values)}");
    return 0;
}
catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or OperationCanceledException or
    EasyModbus.Exceptions.ModbusException or ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
