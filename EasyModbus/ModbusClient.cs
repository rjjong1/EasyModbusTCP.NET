using System.IO.Ports;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EasyModbus.Exceptions;

namespace EasyModbus;

/// <summary>A serialized Modbus TCP, UDP, or RTU client. Dispose after use.</summary>
public partial class ModbusClient : IDisposable, IAsyncDisposable
{
    public enum RegisterOrder
    {
        LowHigh, HighLow
    }

    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private SerialPort? _serialport;
    private NetworkStream? _stream;
    private bool _disposed;
    private ushort _transactionIdentifier;
    private byte[] _receiveData = [];
    private byte[] _sendData = [];

    public delegate void ReceiveDataChangedHandler(object sender);
    public delegate void SendDataChangedHandler(object sender);
    public delegate void ConnectedChangedHandler(object sender);
    public event ReceiveDataChangedHandler? ReceiveDataChanged;
    public event SendDataChangedHandler? SendDataChanged;
    public event ConnectedChangedHandler? ConnectedChanged;

    public ReadOnlyMemory<byte> ReceivedData => _receiveData;
    public ReadOnlyMemory<byte> SentData => _sendData;
    public bool Connected => _serialport?.IsOpen ?? (_udpClient is not null || _tcpClient?.Connected == true);
    public string IPAddress {
        get;
        set {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    } = "127.0.0.1";
    public int Port {
        get;
        set {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 65535);
            field = value;
        }
    } = 502;
    public int ConnectionTimeout {
        get;
        set {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1000;
    public int NumberOfRetries {
        get;
        set {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 3;
    public bool UDPFlag {
        get; set;
    }
    public byte UnitIdentifier { get; set; } = 1;
    public int Baudrate {
        get;
        set {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 9600;
    public Parity Parity { get; set; } = Parity.Even;
    public StopBits StopBits { get; set; } = StopBits.One;
    public string? SerialPort {
        get => _serialport?.PortName;
        set {
            _operationLock.Wait();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                CloseTransport();
                _serialport?.Dispose();
                _serialport = value is null ? null : new SerialPort(value);
            }
            finally
            {
                _operationLock.Release();
            }
        }
    }

    public ModbusClient()
    {

    }

    public ModbusClient(string ipAddress, int port)
    {
        IPAddress = ipAddress;
        Port = port;
    }

    public ModbusClient(string serialPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialPort);
        SerialPort = serialPort;
    }

    public void Connect() => ConnectAsync().GetAwaiter().GetResult();
    public void Connect(string ipAddress, int port) => ConnectAsync(ipAddress, port).GetAwaiter().GetResult();

    public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        await ConnectCoreAsync(ipAddress, port, cancellationToken).ConfigureAwait(false);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => ConnectCoreAsync(IPAddress, Port, cancellationToken);

    private async Task ConnectCoreAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseTransport();
            IPAddress = ipAddress;
            Port = port;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectionTimeout);
            try
            {
                if (_serialport is not null)
                {
                    ConfigureSerialPort();
                    _serialport.Open();
                }
                else if (UDPFlag)
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(ipAddress, timeout.Token).ConfigureAwait(false);
                    _udpClient = new UdpClient(addresses[0].AddressFamily);
                    _udpClient.Connect(addresses[0], port);
                }
                else
                {
                    _tcpClient = new TcpClient { NoDelay = true };
                    await _tcpClient.ConnectAsync(ipAddress, port, timeout.Token).ConfigureAwait(false);
                    _stream = _tcpClient.GetStream();
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                CloseTransport();
                throw new ConnectionException("The Modbus connection timed out.", exception);
            }
            catch
            {
                CloseTransport();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }

        ConnectedChanged?.Invoke(this);
    }

    public void Disconnect()
    {
        _operationLock.Wait();

        try
        {
            CloseTransport();
        }
        finally
        {
            _operationLock.Release();
        }

        ConnectedChanged?.Invoke(this);
    }

    public bool Available(int timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeout);
        using var ping = new Ping();
        return ping.Send(IPAddress, timeout)?.Status == IPStatus.Success;
    }

    private void ConfigureSerialPort()
    {
        var serial = _serialport ?? throw new SerialPortNotOpenedException("No serial port is configured.");
        serial.BaudRate = Baudrate;
        serial.Parity = Parity;
        serial.StopBits = StopBits;
        serial.ReadTimeout = ConnectionTimeout;
        serial.WriteTimeout = ConnectionTimeout;
    }

    private void CloseTransport()
    {
        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
        _udpClient?.Dispose();
        _udpClient = null;
        if (_serialport?.IsOpen == true)
            _serialport.Close();
    }

    public void Dispose()
    {
        _operationLock.Wait();

        try
        {
            DisposeCore();
        }
        finally
        {
            _operationLock.Release();
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            DisposeCore();
        }
        finally
        {
            _operationLock.Release();
        }

        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseTransport();
        _serialport?.Dispose();
        _serialport = null;
        // Keep the managed semaphore alive so callers already waiting on it can
        // enter, observe disposal, and leave without a release/disposal race.
    }
}
