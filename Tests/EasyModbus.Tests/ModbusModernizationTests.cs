using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using EasyModbus.Exceptions;
using NUnit.Framework;

namespace EasyModbus.Tests;

[TestFixture, NonParallelizable, CancelAfter(10000)]
public class ModbusModernizationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task AsyncClientSupportsEveryFunctionOverTcpAndUdp(bool udp)
    {
        await using var server = new ModbusServer { LocalIPAddress = IPAddress.Loopback, Port = 0, UDPFlag = udp };
        server.InputRegisters[1] = -123;
        server.DiscreteInputs[1] = true;
        server.Listen();
        await using var client = new ModbusClient("127.0.0.1", server.Port) { UDPFlag = udp };
        await client.ConnectAsync();

        Assert.That(await client.ReadInputRegistersAsync(0, 1), Is.EqualTo(new[] { -123 }));
        Assert.That(await client.ReadDiscreteInputsAsync(0, 1), Is.EqualTo(new[] { true }));
        await client.WriteSingleCoilAsync(0, true);
        Assert.That(await client.ReadCoilsAsync(0, 1), Is.EqualTo(new[] { true }));
        await client.WriteMultipleCoilsAsync(1, [true, false, true]);
        Assert.That(await client.ReadCoilsAsync(1, 3), Is.EqualTo(new[] { true, false, true }));
        await client.WriteSingleRegisterAsync(0, -1234);
        Assert.That(await client.ReadHoldingRegistersAsync(0, 1), Is.EqualTo(new[] { -1234 }));
        await client.WriteMultipleRegistersAsync(1, [123, 456]);
        Assert.That(await client.ReadHoldingRegistersAsync(1, 2), Is.EqualTo(new[] { 123, 456 }));
        Assert.That(await client.ReadWriteMultipleRegistersAsync(0, 3, 0, [100, 200, 300]), Is.EqualTo(new[] { 100, 200, 300 }));

        await client.WriteSingleRegisterAsync(65535, ushort.MaxValue);
        Assert.That(await client.ReadHoldingRegistersAsync(65535, 1), Is.EqualTo(new[] { -1 }));
        await client.WriteSingleCoilAsync(65535, true);
        Assert.That(await client.ReadCoilsAsync(65535, 1), Is.EqualTo(new[] { true }));
        Assert.That(client.ReceivedData.IsEmpty, Is.False);
        Assert.That(client.SentData.IsEmpty, Is.False);
    }

    [Test]
    public async Task ConcurrentAsyncRequestsKeepTheirOwnResponses()
    {
        await using var server = NewServer();
        server.Listen();
        await using var client = new ModbusClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        await Task.WhenAll(Enumerable.Range(1, 32).Select(async value =>
        {
            var result = await client.ReadWriteMultipleRegistersAsync(0, 1, 0, [value]);
            Assert.That(result, Is.EqualTo(new[] { value }));
        }));
    }

    [Test]
    public async Task ClientReassemblesFragmentedResponse()
    {
        using var listener = StartPeer();
        var peer = ServePeerAsync(listener, async (stream, request) =>
        {
            var response = RegisterReply(request, 0x1234);
            foreach (var value in response)
            {
                await stream.WriteAsync(new[] { value });
                await Task.Delay(2);
            }
        });
        await using var client = new ModbusClient("127.0.0.1", PeerPort(listener));
        await client.ConnectAsync();
        Assert.That(await client.ReadHoldingRegistersAsync(0, 1), Is.EqualTo(new[] { 0x1234 }));
        await peer;
    }

    [Test]
    public async Task ServerReassemblesFragmentedAndBatchedRequests()
    {
        await using var server = NewServer();
        server.HoldingRegisters[1] = 0x1234;
        server.Listen();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        byte[] request = [0, 1, 0, 0, 0, 6, 1, 3, 0, 0, 0, 1];
        foreach (var value in request)
        {
            await stream.WriteAsync(new[] { value });
            await Task.Delay(2);
        }
        Assert.That(await ReadFrameAsync(stream), Is.EqualTo(RegisterReply(request, 0x1234)));
        byte[] batch = [.. request, .. request];
        await stream.WriteAsync(batch);
        Assert.That(await ReadFrameAsync(stream), Is.EqualTo(RegisterReply(request, 0x1234)));
        Assert.That(await ReadFrameAsync(stream), Is.EqualTo(RegisterReply(request, 0x1234)));
    }

    [TestCase("transaction")]
    [TestCase("unit")]
    [TestCase("protocol")]
    [TestCase("function")]
    [TestCase("count")]
    public async Task ClientRejectsInvalidReplies(string field)
    {
        using var listener = StartPeer();
        var peer = ServePeerAsync(listener, async (stream, request) =>
        {
            var response = RegisterReply(request, 42);
            response[field switch
            {
                "transaction" => 0,
                "unit" => 6,
                "protocol" => 2,
                "function" => 7,
                _ => 8
            }] ^= 1;
            await stream.WriteAsync(response);
        });
        await using var client = new ModbusClient("127.0.0.1", PeerPort(listener));
        await client.ConnectAsync();
        Assert.ThrowsAsync<InvalidDataException>(async () => await client.ReadHoldingRegistersAsync(0, 1));
        await peer;
    }

    [Test]
    public async Task PeerDisconnectDuringResponseIsReportedAndClosesClient()
    {
        using var listener = StartPeer();
        var peer = ServePeerAsync(listener, (stream, _) => stream.WriteAsync(new byte[] { 0, 1, 0 }).AsTask());
        await using var client = new ModbusClient("127.0.0.1", PeerPort(listener));
        await client.ConnectAsync();
        Assert.ThrowsAsync<ConnectionException>(async () => await client.ReadHoldingRegistersAsync(0, 1));
        Assert.That(client.Connected, Is.False);
        await peer;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TimeoutAndCancellationCloseIncompleteRequests(bool cancel)
    {
        using var listener = StartPeer();
        using var peerStop = new CancellationTokenSource();
        var requestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = ServePeerAsync(listener, async (_, _) =>
        {
            requestArrived.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, peerStop.Token);
            }
            catch (OperationCanceledException) { }
        });
        await using var client = new ModbusClient("127.0.0.1", PeerPort(listener)) { ConnectionTimeout = cancel ? 2000 : 100 };
        await client.ConnectAsync();
        using var cancellation = new CancellationTokenSource();
        var read = client.ReadHoldingRegistersAsync(0, 1, cancellation.Token);
        await requestArrived.Task;
        if (cancel)
            cancellation.Cancel();
        try
        {
            if (cancel)
                Assert.CatchAsync<OperationCanceledException>(async () => await read);
            else
                Assert.ThrowsAsync<TimeoutException>(async () => await read);
            Assert.That(client.Connected, Is.False);
        }
        finally { peerStop.Cancel(); await peer; }
    }

    [Test]
    public async Task CancelingQueuedRequestDoesNotInterruptCurrentRequest()
    {
        using var listener = StartPeer();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = ServePeerAsync(listener, async (stream, request) =>
        {
            arrived.SetResult();
            await reply.Task;
            await stream.WriteAsync(RegisterReply(request, 42));
        });
        await using var client = new ModbusClient("127.0.0.1", PeerPort(listener));
        await client.ConnectAsync();
        var current = client.ReadHoldingRegistersAsync(0, 1);
        await arrived.Task;
        using var cancellation = new CancellationTokenSource();
        var queued = client.ReadHoldingRegistersAsync(1, 1, cancellation.Token);
        cancellation.Cancel();
        try
        {
            Assert.CatchAsync<OperationCanceledException>(async () => await queued);
        }
        finally { reply.TrySetResult(); }
        Assert.That(await current, Is.EqualTo(new[] { 42 }));
        await peer;
    }

    [Test]
    public async Task ModbusExceptionsKeepConnectionUsable()
    {
        await using var server = NewServer();
        server.FunctionCode3Disabled = true;
        server.Listen();
        await using var client = new ModbusClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        Assert.ThrowsAsync<FunctionCodeNotSupportedException>(async () => await client.ReadHoldingRegistersAsync(0, 1));
        Assert.That(await client.ReadInputRegistersAsync(0, 1), Is.EqualTo(new[] { 0 }));
        Assert.That(client.Connected, Is.True);
    }

    [Test]
    public async Task MalformedWritesDoNotPartiallyChangeRegisters()
    {
        await using var server = NewServer();
        server.HoldingRegisters[1] = 77;
        server.Listen();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 0, 1, 0, 0, 0, 9, 1, 16, 0, 0, 0, 2, 4, 0, 42 });
        Assert.That(await ReadFrameAsync(stream), Is.EqualTo(new byte[] { 0, 1, 0, 0, 0, 3, 1, 0x90, 3 }));
        Assert.That(server.HoldingRegisters[1], Is.EqualTo(77));
        await stream.WriteAsync(new byte[] { 0, 2, 0, 0, 0, 2, 1, 0x80 });
        Assert.That(await ReadFrameAsync(stream), Is.EqualTo(new byte[] { 0, 2, 0, 0, 0, 3, 1, 0x80, 1 }));
        await stream.WriteAsync(new byte[] { 0, 3, 0, 0, 0, 6, 1, 3, 0, 0, 0, 1 });
        Assert.That((await ReadFrameAsync(stream))[^1], Is.EqualTo(77));
    }

    [Test]
    public async Task StoppingServerReleasesIdleConnections()
    {
        await using var server = NewServer();
        server.Listen();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        Assert.That(() => server.NumberOfConnections, Is.EqualTo(1).After(2000, 10));
        await server.StopListeningAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(server.IsListening, Is.False);
        Assert.That(server.NumberOfConnections, Is.Zero);
        Assert.That(await client.GetStream().ReadAsync(new byte[1]), Is.Zero);
    }

    [Test]
    public void BindFailuresAreSynchronousAndServerCanRetry()
    {
        using var listener = StartPeer();
        using var server = NewServer();
        server.Port = PeerPort(listener);
        Assert.Throws<SocketException>(server.Listen);
        Assert.That(server.IsListening, Is.False);
        listener.Stop();
        Assert.DoesNotThrow(server.Listen);
    }

    [Test]
    public async Task NotificationCanStopItsOwnServerWithoutDeadlock()
    {
        await using var server = NewServer();
        server.HoldingRegistersChanged += (_, _) => server.StopListening();
        server.Listen();
        await using var client = new ModbusClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        Assert.ThrowsAsync<ConnectionException>(async () => await client.WriteSingleRegisterAsync(0, 42).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(server.IsListening, Is.False);
    }

    [Test]
    public async Task ConnectionNotificationCanStopItsOwnServerWithoutDeadlock()
    {
        await using var server = NewServer();
        server.NumberOfConnectedClientsChanged += () =>
        {
            if (server.NumberOfConnections == 1)
                server.StopListening();
        };
        server.Listen();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        Assert.That(await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)), Is.Zero);
        Assert.That(server.IsListening, Is.False);
    }

    [Test]
    public async Task DisposalIsIdempotentAndPreventsReconnection()
    {
        var server = NewServer();
        server.Listen();
        var client = new ModbusClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        await client.DisposeAsync();
        client.Dispose();
        Assert.That(client.Connected, Is.False);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.ConnectAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.ReadHoldingRegistersAsync(0, 1));
        await server.DisposeAsync();
        server.Dispose();
        Assert.Throws<ObjectDisposedException>(server.Listen);
    }

    [TestCase(-1, 1)]
    [TestCase(65536, 1)]
    [TestCase(0, 0)]
    [TestCase(0, -1)]
    [TestCase(0, 126)]
    [TestCase(65535, 2)]
    public void ClientValidatesAddressRangesBeforeIo(int address, int quantity)
    {
        using var client = new ModbusClient();
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.ReadHoldingRegistersAsync(address, quantity));
    }

    private static ModbusServer NewServer() => new() { LocalIPAddress = IPAddress.Loopback, Port = 0 };
    private static TcpListener StartPeer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }
    private static int PeerPort(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;
    private static async Task ServePeerAsync(TcpListener listener, Func<NetworkStream, byte[], Task> respond)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        var stream = connection.GetStream();
        await respond(stream, await ReadFrameAsync(stream));
    }
    private static byte[] RegisterReply(byte[] request, ushort value) =>
        [request[0], request[1], 0, 0, 0, 5, request[6], 3, 2, (byte)(value >> 8), (byte)value];
    private static async Task<byte[]> ReadFrameAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var header = new byte[6];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var payload = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4))];
        await stream.ReadExactlyAsync(payload, timeout.Token);
        return [.. header, .. payload];
    }
}
