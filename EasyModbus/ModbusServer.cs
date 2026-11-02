using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace EasyModbus;

/// <summary>A disposable Modbus server with one-based local register tables.</summary>
public partial class ModbusServer : IDisposable, IAsyncDisposable
{
    private readonly Lock _lifecycleLock = new();
    private readonly Lock _stateLock = new();
    private readonly AsyncLocal<bool> _processingRequest = new();
    private Session? _session;
    private bool _disposed;
    private int _numberOfConnections;
    private readonly ModbusProtocol?[] _modbusLogData = new ModbusProtocol?[100];

    public HoldingRegisterCollection HoldingRegisters;
    public InputRegisterCollection InputRegisters;
    public CoilCollection Coils;
    public DiscreteInputCollection DiscreteInputs;

    public ModbusServer()
    {
        HoldingRegisters = new(this);
        InputRegisters = new(this);
        Coils = new(this);
        DiscreteInputs = new(this);
    }

    public delegate void CoilsChangedHandler(int coil, int numberOfCoils);
    public delegate void HoldingRegistersChangedHandler(int register, int numberOfRegisters);
    public delegate void NumberOfConnectedClientsChangedHandler();
    public delegate void LogDataChangedHandler();
    public event CoilsChangedHandler? CoilsChanged;
    public event HoldingRegistersChangedHandler? HoldingRegistersChanged;
    public event NumberOfConnectedClientsChangedHandler? NumberOfConnectedClientsChanged;
    public event LogDataChangedHandler? LogDataChanged;

    public bool FunctionCode1Disabled { get; set; }
    public bool FunctionCode2Disabled { get; set; }
    public bool FunctionCode3Disabled { get; set; }
    public bool FunctionCode4Disabled { get; set; }
    public bool FunctionCode5Disabled { get; set; }
    public bool FunctionCode6Disabled { get; set; }
    public bool FunctionCode15Disabled { get; set; }
    public bool FunctionCode16Disabled { get; set; }
    public bool FunctionCode23Disabled { get; set; }
    public bool PortChanged { get; set; }
    public IPAddress LocalIPAddress { get; set; } = IPAddress.Any;
    public int Port { get; set; } = 502;
    public bool UDPFlag { get; set; }
    public bool SerialFlag { get; set; }
    public int Baudrate { get; set; } = 9600;
    public Parity Parity { get; set; } = Parity.Even;
    public StopBits StopBits { get; set; } = StopBits.One;
    public byte UnitIdentifier { get; set; } = 1;
    public string? SerialPort {
        get;
        set
        {
            field = value;
            SerialFlag = value is not null;
        }
    } = "COM1";
    public string? LogFileFilename {
        get => StoreLogData.Instance.Filename;
        set => StoreLogData.Instance.Filename = value;
    }
    public int NumberOfConnections => Volatile.Read(ref _numberOfConnections);
    public bool IsListening
    {
        get
        {
            lock (_lifecycleLock)
                return _session is not null;
        }
    }
    public ModbusProtocol?[] ModbusLogData
    {
        get
        {
            lock (_stateLock)
                return (ModbusProtocol?[])_modbusLogData.Clone();
        }
    }

    /// <summary>Binds before returning, so startup failures are reported to the caller.</summary>
    public void Listen()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
                throw new InvalidOperationException("The Modbus server is already listening.");
            ArgumentNullException.ThrowIfNull(LocalIPAddress);
            ArgumentOutOfRangeException.ThrowIfNegative(Port);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535);
            var session = new Session();
            try
            {
                if (SerialFlag)
                {
                    session.Serial = new SerialPort(SerialPort ?? throw new InvalidOperationException("A serial port is required."), Baudrate, Parity, 8, StopBits)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000
                    };
                    session.SerialHandler = (_, _) => ReceiveSerial(session);
                    session.Serial.DataReceived += session.SerialHandler;
                    session.Serial.Open();
                }
                else if (UDPFlag)
                {
                    session.Udp = new UdpClient(new IPEndPoint(LocalIPAddress, Port));
                    Port = ((IPEndPoint)session.Udp.Client.LocalEndPoint!).Port;
                }
                else
                {
                    session.Listener = new TcpListener(LocalIPAddress, Port);
                    session.Listener.Start();
                    Port = ((IPEndPoint)session.Listener.LocalEndpoint).Port;
                }
                _session = session;
                session.Worker = session.Udp is not null ? ReceiveUdpAsync(session) :
                    session.Listener is not null ? AcceptClientsAsync(session) : Task.CompletedTask;
                PortChanged = false;
            }
            catch
            {
                session.RequestStop();
                session.Cancellation.Dispose();
                throw;
            }
        }
    }

    public void StopListening() => StopListeningAsync().GetAwaiter().GetResult();

    public Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (_lifecycleLock)
        {
            session = _session;
            _session = null;
        }
        if (session is null)
            return Task.CompletedTask;

        session.RequestStop();
        var stopped = FinishStopAsync(session);
        // A synchronous event handler may stop its own server. The transports
        // are already closed; let that request unwind before joining its task.
        return _processingRequest.Value ? Task.CompletedTask : stopped.WaitAsync(cancellationToken);
    }

    private static async Task FinishStopAsync(Session session)
    {
        try
        {
            try
            {
                await session.Worker.ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(session.Clients.Values.Select(connection => connection.Worker)).ConfigureAwait(false);
            }
        }
        catch (Exception exception) { Trace.TraceError($"Modbus server stopped after an error: {exception}"); }
        finally { session.Cancellation.Dispose(); }
    }

    private async Task AcceptClientsAsync(Session session)
    {
        try
        {
            while (!session.Cancellation.IsCancellationRequested)
            {
                var client = await session.Listener!.AcceptTcpClientAsync(session.Cancellation.Token).ConfigureAwait(false);
                client.NoDelay = true;
                var connection = new ClientConnection(client);
                session.Clients.TryAdd(client, connection);
                connection.Worker = HandleClientAsync(session, connection);
            }
        }
        catch (Exception exception) when (session.Cancellation.IsCancellationRequested && exception is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception exception) { Trace.TraceError($"Modbus accept failed: {exception}"); }
    }

    private async Task HandleClientAsync(Session session, ClientConnection connection)
    {
        Interlocked.Increment(ref _numberOfConnections);
        try
        {
            NotifyConnectedClientsChanged();
            var stream = connection.Client.GetStream();
            while (!session.Cancellation.IsCancellationRequested)
            {
                var request = await ModbusFrame.ReadAsync(stream, session.Cancellation.Token).ConfigureAwait(false);
                var response = ProcessRequest(request);
                if (response is not null)
                    await stream.WriteAsync(response, session.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        {
            if (!session.Cancellation.IsCancellationRequested && exception is not EndOfStreamException)
                Trace.TraceWarning($"Modbus client disconnected: {exception.Message}");
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Modbus client failed: {exception}");
        }
        finally
        {
            connection.Client.Dispose();
            session.Clients.TryRemove(connection.Client, out _);
            Interlocked.Decrement(ref _numberOfConnections);
            try
            {
                NotifyConnectedClientsChanged();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Modbus client notification failed: {exception}");
            }
        }
    }

    private void NotifyConnectedClientsChanged()
    {
        var previous = _processingRequest.Value;
        _processingRequest.Value = true;
        try
        {
            NumberOfConnectedClientsChanged?.Invoke();
        }
        finally
        {
            _processingRequest.Value = previous;
        }
    }

    private async Task ReceiveUdpAsync(Session session)
    {
        try
        {
            while (!session.Cancellation.IsCancellationRequested)
            {
                var datagram = await session.Udp!.ReceiveAsync(session.Cancellation.Token).ConfigureAwait(false);
                try
                {
                    ModbusFrame.Validate(datagram.Buffer);
                    var response = ProcessRequest(datagram.Buffer);
                    if (response is not null)
                        await session.Udp.SendAsync(response, datagram.RemoteEndPoint, session.Cancellation.Token).ConfigureAwait(false);
                }
                catch (InvalidDataException exception)
                {
                    Trace.TraceWarning($"Ignored invalid Modbus UDP frame: {exception.Message}");
                }
                catch (Exception exception) when (!session.Cancellation.IsCancellationRequested && exception is not OperationCanceledException)
                {
                    Trace.TraceError($"Modbus UDP request failed: {exception}");
                }
            }
        }
        catch (Exception exception) when (session.Cancellation.IsCancellationRequested && exception is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception exception) { Trace.TraceError($"Modbus UDP receive failed: {exception}"); }
    }

    private void ReceiveSerial(Session session)
    {
        lock (session.SerialLock)
        {
            var serial = session.Serial!;
            try
            {
                while (serial.IsOpen && serial.BytesToRead > 0)
                {
                    session.SerialBuffer.Add((byte)serial.ReadByte());
                    if (session.SerialBuffer.Count < 8)
                        continue;

                    var bytes = session.SerialBuffer;
                    var length = bytes[1] is 15 or 16 ? 9 + bytes[6] : bytes[1] == 23 ? bytes.Count < 11 ? 11 : 13 + bytes[10] : 8;
                    if (length > 256)
                    {
                        bytes.Clear();
                        continue;
                    }

                    if (bytes.Count < length)
                        continue;

                    var frame = bytes.GetRange(0, length).ToArray();
                    bytes.RemoveRange(0, length);
                    if (!ModbusClient.DetectValidModbusFrame(frame, frame.Length))
                        continue;

                    var response = ProcessRequest(ModbusFrame.Create(0, frame[0], frame.AsSpan(1, frame.Length - 3)));
                    if (response is null || frame[0] == 0)
                        continue;

                    var result = new byte[response.Length - 4];
                    response.AsSpan(6).CopyTo(result);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(result.Length - 2), ModbusFrame.CalculateCrc(result.AsSpan(0, result.Length - 2)));
                    if (serial.IsOpen)
                        serial.Write(result, 0, result.Length);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
            {
                if (!session.Cancellation.IsCancellationRequested)
                    Trace.TraceError($"Modbus serial receive failed: {exception}");
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
            _disposed = true;

        StopListening();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
            _disposed = true;

        await StopListeningAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private sealed class ClientConnection(TcpClient client)
    {
        internal TcpClient Client { get; } = client;
        internal Task Worker { get; set; } = Task.CompletedTask;
    }

    private sealed class Session
    {
        internal CancellationTokenSource Cancellation { get; } = new();
        internal ConcurrentDictionary<TcpClient, ClientConnection> Clients { get; } = new();
        internal TcpListener? Listener { get; set; }
        internal UdpClient? Udp { get; set; }
        internal SerialPort? Serial { get; set; }
        internal SerialDataReceivedEventHandler? SerialHandler { get; set; }
        internal Task Worker { get; set; } = Task.CompletedTask;
        internal Lock SerialLock { get; } = new();
        internal List<byte> SerialBuffer { get; } = [];
        internal void RequestStop()
        {
            Cancellation.Cancel();
            Listener?.Stop();
            Udp?.Dispose();
            foreach (var client in Clients.Keys)
                client.Dispose();

            if (Serial is not null)
            {
                if (SerialHandler is not null)
                    Serial.DataReceived -= SerialHandler;
                Serial.Dispose();
            }
        }
    }
}
