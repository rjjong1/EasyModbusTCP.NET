using System.Buffers.Binary;
using EasyModbus.Exceptions;

namespace EasyModbus;

public partial class ModbusClient
{
    public bool[] ReadCoils(int startingAddress, int quantity) => ReadCoilsAsync(startingAddress, quantity).GetAwaiter().GetResult();
    public bool[] ReadDiscreteInputs(int startingAddress, int quantity) => ReadDiscreteInputsAsync(startingAddress, quantity).GetAwaiter().GetResult();
    public int[] ReadHoldingRegisters(int startingAddress, int quantity) => ReadHoldingRegistersAsync(startingAddress, quantity).GetAwaiter().GetResult();
    public int[] ReadInputRegisters(int startingAddress, int quantity) => ReadInputRegistersAsync(startingAddress, quantity).GetAwaiter().GetResult();
    public void WriteSingleCoil(int startingAddress, bool value) => WriteSingleCoilAsync(startingAddress, value).GetAwaiter().GetResult();
    public void WriteSingleRegister(int startingAddress, int value) => WriteSingleRegisterAsync(startingAddress, value).GetAwaiter().GetResult();
    public void WriteMultipleCoils(int startingAddress, bool[] values) => WriteMultipleCoilsAsync(startingAddress, values).GetAwaiter().GetResult();
    public void WriteMultipleRegisters(int startingAddress, int[] values) => WriteMultipleRegistersAsync(startingAddress, values).GetAwaiter().GetResult();
    public int[] ReadWriteMultipleRegisters(int startingAddressRead, int quantityRead, int startingAddressWrite, int[] values) => ReadWriteMultipleRegistersAsync(startingAddressRead, quantityRead, startingAddressWrite, values).GetAwaiter().GetResult();

    public Task<bool[]> ReadCoilsAsync(int startingAddress, int quantity, CancellationToken cancellationToken = default) => ReadBitsAsync(1, startingAddress, quantity, cancellationToken);
    public Task<bool[]> ReadDiscreteInputsAsync(int startingAddress, int quantity, CancellationToken cancellationToken = default) => ReadBitsAsync(2, startingAddress, quantity, cancellationToken);
    public Task<int[]> ReadHoldingRegistersAsync(int startingAddress, int quantity, CancellationToken cancellationToken = default) => ReadRegistersAsync(3, startingAddress, quantity, cancellationToken);
    public Task<int[]> ReadInputRegistersAsync(int startingAddress, int quantity, CancellationToken cancellationToken = default) => ReadRegistersAsync(4, startingAddress, quantity, cancellationToken);

    private async Task<bool[]> ReadBitsAsync(byte function, int address, int quantity, CancellationToken cancellationToken)
    {
        ValidateRange(address, quantity, 2000);
        var response = await ExecuteAsync(CreateRequest(function, address, quantity), cancellationToken).ConfigureAwait(false);
        ValidateReadResponse(response, (quantity + 7) / 8);
        var values = new bool[quantity];
        for (var index = 0; index < values.Length; index++)
            values[index] = (response[2 + index / 8] & (1 << (index % 8))) != 0;

        return values;
    }

    private async Task<int[]> ReadRegistersAsync(byte function, int address, int quantity, CancellationToken cancellationToken)
    {
        ValidateRange(address, quantity, 125);
        var response = await ExecuteAsync(CreateRequest(function, address, quantity), cancellationToken).ConfigureAwait(false);
        return DecodeRegisters(response, quantity);
    }

    public Task WriteSingleCoilAsync(int startingAddress, bool value, CancellationToken cancellationToken = default) =>
        WriteSingleAsync(5, startingAddress, value ? 0xFF00 : 0, cancellationToken);

    public Task WriteSingleRegisterAsync(int startingAddress, int value, CancellationToken cancellationToken = default)
    {
        ValidateRegisterValue(value);
        return WriteSingleAsync(6, startingAddress, value, cancellationToken);
    }

    private async Task WriteSingleAsync(byte function, int address, int value, CancellationToken cancellationToken)
    {
        ValidateRange(address, 1, 1);
        var request = CreateRequest(function, address, value);
        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!request.AsSpan().SequenceEqual(response))
            throw new InvalidDataException("The Modbus write acknowledgement does not match the request.");
    }

    public async Task WriteMultipleCoilsAsync(int startingAddress, bool[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateRange(startingAddress, values.Length, 1968);
        var request = new byte[6 + (values.Length + 7) / 8];
        CreateRequest(15, startingAddress, values.Length).CopyTo(request, 0);
        request[5] = (byte)(request.Length - 6);
        for (var index = 0; index < values.Length; index++)
            if (values[index])
                request[6 + index / 8] |= (byte)(1 << (index % 8));

        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateWriteResponse(request, response);
    }

    public async Task WriteMultipleRegistersAsync(int startingAddress, int[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateRange(startingAddress, values.Length, 123);
        var request = new byte[6 + values.Length * 2];
        CreateRequest(16, startingAddress, values.Length).CopyTo(request, 0);
        request[5] = (byte)(values.Length * 2);
        EncodeRegisters(values, request.AsSpan(6));
        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateWriteResponse(request, response);
    }

    public async Task<int[]> ReadWriteMultipleRegistersAsync(int startingAddressRead, int quantityRead, int startingAddressWrite, int[] values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateRange(startingAddressRead, quantityRead, 125);
        ValidateRange(startingAddressWrite, values.Length, 121);
        var request = new byte[10 + values.Length * 2];
        CreateRequest(23, startingAddressRead, quantityRead).CopyTo(request, 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5), (ushort)startingAddressWrite);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(7), (ushort)values.Length);
        request[9] = (byte)(values.Length * 2);
        EncodeRegisters(values, request.AsSpan(10));
        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return DecodeRegisters(response, quantityRead);
    }

    private static byte[] CreateRequest(byte function, int address, int value)
    {
        var request = new byte[5];
        request[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(1), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(3), unchecked((ushort)value));
        return request;
    }

    private static void ValidateRange(int address, int quantity, int maxQuantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(address);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(address, 65535);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, maxQuantity);
        if ((long)address + quantity > 65536)
            throw new ArgumentOutOfRangeException(nameof(quantity), "The requested range exceeds the Modbus address space.");
    }

    private static void ValidateRegisterValue(int value)
    {
        if (value is < short.MinValue or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "A register must fit in a signed or unsigned 16-bit value.");
    }

    private static void EncodeRegisters(ReadOnlySpan<int> values, Span<byte> destination)
    {
        for (var index = 0; index < values.Length; index++)
        {
            ValidateRegisterValue(values[index]);
            BinaryPrimitives.WriteUInt16BigEndian(destination[(index * 2)..], unchecked((ushort)values[index]));
        }
    }

    private static int[] DecodeRegisters(byte[] response, int quantity)
    {
        ValidateReadResponse(response, quantity * 2);
        var values = new int[quantity];
        for (var index = 0; index < quantity; index++)
            values[index] = BinaryPrimitives.ReadInt16BigEndian(response.AsSpan(2 + index * 2));

        return values;
    }

    private static void ValidateReadResponse(byte[] response, int expectedBytes)
    {
        if (response.Length != expectedBytes + 2 || response[1] != expectedBytes)
            throw new InvalidDataException("The Modbus response byte count does not match the request.");
    }

    private static void ValidateWriteResponse(byte[] request, byte[] response)
    {
        if (!request.AsSpan(0, 5).SequenceEqual(response))
            throw new InvalidDataException("The Modbus write acknowledgement does not match the request.");
    }

    private async Task<byte[]> ExecuteAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var sent = false;
        var received = false;
        var wasConnected = Connected;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Connected)
                throw _serialport is not null ? new SerialPortNotOpenedException("The serial port is not open.") :
                    new ConnectionException("The Modbus client is not connected.");

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectionTimeout);
                try
                {
                    byte[] response;
                    if (_serialport is not null)
                    {
                        if (!_serialport.IsOpen)
                        {
                            ConfigureSerialPort();
                            _serialport.Open();
                        }
                        response = await ExchangeSerialAsync(pdu, timeout.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        var transaction = unchecked(++_transactionIdentifier);
                        _sendData = ModbusFrame.Create(transaction, UnitIdentifier, pdu);
                        sent = true;
                        StoreLogData.Instance.Store($"Send: {Convert.ToHexString(_sendData)}", DateTime.Now);
                        if (_udpClient is not null)
                        {
                            await _udpClient.SendAsync(_sendData, timeout.Token).ConfigureAwait(false);
                            _receiveData = (await _udpClient.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer;
                        }
                        else
                        {
                            await _stream!.WriteAsync(_sendData, timeout.Token).ConfigureAwait(false);
                            _receiveData = await ModbusFrame.ReadAsync(_stream, timeout.Token).ConfigureAwait(false);
                        }

                        received = true;
                        ModbusFrame.Validate(_receiveData);
                        if (BinaryPrimitives.ReadUInt16BigEndian(_receiveData) != transaction || _receiveData[6] != UnitIdentifier)
                            throw new InvalidDataException("The response transaction or unit identifier does not match the request.");

                        response = _receiveData[7..];
                    }

                    sent = received = true;
                    StoreLogData.Instance.Store($"Receive: {Convert.ToHexString(_receiveData)}", DateTime.Now);
                    ValidateFunction(response, pdu[0]);
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _serialport is not null && attempt < NumberOfRetries)
                {
                    CloseTransport();
                }
                catch (CRCCheckFailedException) when (_serialport is not null && attempt < NumberOfRetries)
                {
                    _serialport.DiscardInBuffer();
                }
                catch (OperationCanceledException exception)
                {
                    CloseTransport();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("The Modbus request timed out.", exception);
                }
            }
        }
        catch (IOException exception)
        {
            CloseTransport();
            throw new ConnectionException("The Modbus connection closed before a complete response was received.", exception);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            CloseTransport();
            throw new ConnectionException("The Modbus transport failed.", exception);
        }
        catch (InvalidDataException)
        {
            CloseTransport();
            throw;
        }
        finally
        {
            _operationLock.Release();
            if (sent)
                SendDataChanged?.Invoke(this);
            if (received)
                ReceiveDataChanged?.Invoke(this);
            if (wasConnected && !Connected)
                ConnectedChanged?.Invoke(this);
        }
    }

    private async Task<byte[]> ExchangeSerialAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        var serial = _serialport!;
        _sendData = new byte[pdu.Length + 3];
        _sendData[0] = UnitIdentifier;
        pdu.CopyTo(_sendData, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(_sendData.AsSpan(_sendData.Length - 2), ModbusFrame.CalculateCrc(_sendData.AsSpan(0, _sendData.Length - 2)));
        serial.DiscardInBuffer();

        // Closing the port also cancels drivers that do not honor async read cancellation.
        using var registration = cancellationToken.Register(serial.Close);
        try
        {
            await serial.BaseStream.WriteAsync(_sendData, cancellationToken).ConfigureAwait(false);
            var header = new byte[3];
            await serial.BaseStream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var remaining = (header[1] & 0x80) != 0 ? 2 : header[1] is 5 or 6 or 15 or 16 ? 5 : header[2] + 2;
            if (remaining + 3 > 256)
                throw new InvalidDataException("The RTU response is too large.");

            _receiveData = new byte[3 + remaining];
            header.CopyTo(_receiveData, 0);
            await serial.BaseStream.ReadExactlyAsync(_receiveData.AsMemory(3), cancellationToken).ConfigureAwait(false);
            if (!DetectValidModbusFrame(_receiveData, _receiveData.Length))
                throw new CRCCheckFailedException("The RTU response CRC is invalid.");

            if (_receiveData[0] != UnitIdentifier)
                throw new InvalidDataException("The RTU response unit identifier does not match the request.");

            return _receiveData[1..^2];
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            throw new OperationCanceledException("The serial operation was canceled.", exception, cancellationToken);
        }
    }

    private static void ValidateFunction(byte[] response, byte function)
    {
        if (response.Length == 0)
            throw new InvalidDataException("The Modbus response is empty.");

        if (response[0] == (function | 0x80))
        {
            if (response.Length != 2)
                throw new InvalidDataException("Invalid Modbus exception response length.");

            throw response[1] switch
            {
                1 => new FunctionCodeNotSupportedException("The server does not support the function code."),
                2 => new StartingAddressInvalidException("The server rejected the address range."),
                3 => new QuantityInvalidException("The server rejected the quantity or value."),
                _ => new ModbusException($"The server returned Modbus exception 0x{response[1]:X2}.")
            };
        }

        if (response[0] != function)
            throw new InvalidDataException("The response function code does not match the request.");
    }
}
