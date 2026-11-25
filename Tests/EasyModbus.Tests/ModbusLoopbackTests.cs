using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

namespace EasyModbus.Tests;

[TestFixture]
[NonParallelizable]
public class ModbusLoopbackTests
{
    [Test]
    [CancelAfter(10000)]
    public void TcpClientReadsAndWritesRegistersAndCoils()
    {
        var server = new ModbusServer { LocalIPAddress = IPAddress.Loopback, Port = GetFreeTcpPort() };
        var client = new ModbusClient("127.0.0.1", server.Port) { ConnectionTimeout = 2000 };
        server.HoldingRegisters[1] = 1234;
        server.InputRegisters[1] = 5678;
        server.DiscreteInputs[1] = true;
        server.Listen();

        try
        {
            ConnectWhenReady(client);
            Assert.That(client.ReadHoldingRegisters(0, 1), Is.EqualTo(new[] { 1234 }));
            Assert.That(client.ReadInputRegisters(0, 1), Is.EqualTo(new[] { 5678 }));
            Assert.That(client.ReadDiscreteInputs(0, 1), Is.EqualTo(new[] { true }));

            client.WriteSingleRegister(0, 4321);
            Assert.That(client.ReadHoldingRegisters(0, 1), Is.EqualTo(new[] { 4321 }));
            client.WriteMultipleRegisters(1, new[] { 100, 200 });
            Assert.That(client.ReadHoldingRegisters(1, 2), Is.EqualTo(new[] { 100, 200 }));

            client.WriteSingleCoil(0, true);
            Assert.That(client.ReadCoils(0, 1), Is.EqualTo(new[] { true }));
            client.WriteMultipleCoils(1, new[] { true, false, true });
            Assert.That(client.ReadCoils(1, 3), Is.EqualTo(new[] { true, false, true }));
        }
        finally
        {
            client.Disconnect();
            server.StopListening();
        }
    }

    [Test]
    [CancelAfter(10000)]
    public void TcpServerCanStopAndRestartOnTheSamePort()
    {
        var server = new ModbusServer { LocalIPAddress = IPAddress.Loopback, Port = GetFreeTcpPort() };
        server.StopListening();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var client = new ModbusClient("127.0.0.1", server.Port) { ConnectionTimeout = 2000 };
            server.Listen();
            try
            {
                ConnectWhenReady(client);
                Assert.That(client.ReadHoldingRegisters(0, 1), Has.Length.EqualTo(1));
            }
            finally
            {
                client.Disconnect();
                server.StopListening();
            }
        }

        server.StopListening();
        var listener = new TcpListener(IPAddress.Loopback, server.Port);
        try
        {
            listener.Start();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    [CancelAfter(10000)]
    public void IdleUdpServerStopsAndReleasesItsPort()
    {
        int port;
        using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;

        var server = new ModbusServer { LocalIPAddress = IPAddress.Loopback, Port = port, UDPFlag = true };
        server.Listen();
        try
        {
            Assert.That(() => CanBindUdpPort(port), Is.False.After(3000, 20));
        }
        finally
        {
            server.StopListening();
        }
        server.StopListening();
        using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
    }

    [Test]
    public void RegisterConversionsPreserveTexplorerValues()
    {
        const int integer = 123456789;
        const float real = 123.25f;
        Assert.That(ModbusClient.ConvertRegistersToInt(ModbusClient.ConvertIntToRegisters(integer)), Is.EqualTo(integer));
        Assert.That(ModbusClient.ConvertRegistersToFloat(ModbusClient.ConvertFloatToRegisters(real)), Is.EqualTo(real));
        var registers = ModbusClient.ConvertStringToRegisters("Texplorer ");
        Assert.That(ModbusClient.ConvertRegistersToString(registers, 0, 10), Is.EqualTo("Texplorer "));
    }

    private static bool CanBindUdpPort(int port)
    {
        try
        {
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void ConnectWhenReady(ModbusClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            try
            {
                client.Connect();
                return;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                client.Disconnect();
                Thread.Sleep(20);
            }
        }
    }
}
