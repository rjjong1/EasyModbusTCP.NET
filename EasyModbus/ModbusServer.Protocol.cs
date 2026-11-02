using System.Buffers.Binary;

namespace EasyModbus;

public partial class ModbusServer
{
    private byte[]? ProcessRequest(byte[] frame)
    {
        ModbusFrame.Validate(frame);
        if (frame[6] != UnitIdentifier && frame[6] != 0)
            return null;

        var previous = _processingRequest.Value;
        _processingRequest.Value = true;
        try
        {
            byte[] response;
            (int Address, int Count, bool Coils)? change;
            lock (_stateLock)
            {
                var pdu = HandlePdu(frame.AsSpan(7), out change);
                response = ModbusFrame.Create(BinaryPrimitives.ReadUInt16BigEndian(frame), frame[6], pdu);
                Array.Copy(_modbusLogData, 0, _modbusLogData, 2, _modbusLogData.Length - 2);
                _modbusLogData[0] = CreateLog(frame, true);
                _modbusLogData[1] = CreateLog(response, false);
            }

            StoreLogData.Instance.Store($"Request: {Convert.ToHexString(frame)}; response: {Convert.ToHexString(response)}", DateTime.Now);
            if (change is { } updated)
            {
                if (updated.Coils)
                    CoilsChanged?.Invoke(updated.Address + 1, updated.Count);
                else
                    HoldingRegistersChanged?.Invoke(updated.Address + 1, updated.Count);
            }

            LogDataChanged?.Invoke();
            return response;
        }
        finally
        {
            _processingRequest.Value = previous;
        }
    }

    private byte[] HandlePdu(ReadOnlySpan<byte> pdu, out (int Address, int Count, bool Coils)? change)
    {
        change = null;
        var function = pdu[0];
        var disabled = function switch
        {
            1 => FunctionCode1Disabled,
            2 => FunctionCode2Disabled,
            3 => FunctionCode3Disabled,
            4 => FunctionCode4Disabled,
            5 => FunctionCode5Disabled,
            6 => FunctionCode6Disabled,
            15 => FunctionCode15Disabled,
            16 => FunctionCode16Disabled,
            23 => FunctionCode23Disabled,
            _ => true
        };

        if (disabled)
            return Error(function, 1);

        if (pdu.Length < 5)
            return Error(function, 3);

        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu[1..]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu[3..]);

        if (function is >= 1 and <= 4)
        {
            if (pdu.Length != 5 || quantity == 0 || quantity > (function <= 2 ? 2000 : 125))
                return Error(function, 3);

            if (!ValidRange(address, quantity))
                return Error(function, 2);

            if (function <= 2)
            {
                var table = function == 1 ? Coils._localArray : DiscreteInputs._localArray;
                var result = new byte[2 + (quantity + 7) / 8];
                result[0] = function;
                result[1] = (byte)(result.Length - 2);
                for (var index = 0; index < quantity; index++)
                    if (table[address + index + 1])
                        result[2 + index / 8] |= (byte)(1 << (index % 8));

                return result;
            }

            return ReadRegisterResponse(function, address, quantity, function == 3 ? HoldingRegisters._localArray : InputRegisters._localArray);
        }

        if (function is 5 or 6)
        {
            if (pdu.Length != 5 || (function == 5 && quantity is not 0 and not 0xFF00))
                return Error(function, 3);

            if (function == 5)
                Coils._localArray[address + 1] = quantity != 0;
            else
                HoldingRegisters._localArray[address + 1] = unchecked((short)quantity);

            change = (address, 1, function == 5);
            return pdu.ToArray();
        }

        if (function is 15 or 16)
        {
            if (pdu.Length < 6 || quantity == 0 || quantity > (function == 15 ? 1968 : 123))
                return Error(function, 3);

            var byteCount = function == 15 ? (quantity + 7) / 8 : quantity * 2;
            if (pdu[5] != byteCount || pdu.Length != byteCount + 6)
                return Error(function, 3);

            if (!ValidRange(address, quantity))
                return Error(function, 2);

            for (var index = 0; index < quantity; index++)
            {
                if (function == 15)
                    Coils._localArray[address + index + 1] = (pdu[6 + index / 8] & (1 << (index % 8))) != 0;
                else
                    HoldingRegisters._localArray[address + index + 1] = BinaryPrimitives.ReadInt16BigEndian(pdu[(6 + index * 2)..]);
            }

            change = (address, quantity, function == 15);
            return pdu[..5].ToArray();
        }

        if (pdu.Length < 10)
            return Error(function, 3);

        var writeAddress = BinaryPrimitives.ReadUInt16BigEndian(pdu[5..]);
        var writeQuantity = BinaryPrimitives.ReadUInt16BigEndian(pdu[7..]);
        if (quantity is 0 or > 125 || writeQuantity is 0 or > 121 || pdu[9] != writeQuantity * 2 || pdu.Length != 10 + writeQuantity * 2)
            return Error(function, 3);

        if (!ValidRange(address, quantity) || !ValidRange(writeAddress, writeQuantity))
            return Error(function, 2);

        for (var index = 0; index < writeQuantity; index++)
            HoldingRegisters._localArray[writeAddress + index + 1] = BinaryPrimitives.ReadInt16BigEndian(pdu[(10 + index * 2)..]);

        change = (writeAddress, writeQuantity, false);
        return ReadRegisterResponse(function, address, quantity, HoldingRegisters._localArray);
    }

    private static bool ValidRange(int address, int quantity) => address + quantity <= 65536;
    private static byte[] Error(byte function, byte code) => [(byte)(function | 0x80), code];

    private static byte[] ReadRegisterResponse(byte function, int address, int quantity, short[] table)
    {
        var response = new byte[2 + quantity * 2];
        response[0] = function;
        response[1] = (byte)(quantity * 2);
        for (var index = 0; index < quantity; index++)
            BinaryPrimitives.WriteInt16BigEndian(response.AsSpan(2 + index * 2), table[address + index + 1]);

        return response;
    }

    private static ModbusProtocol CreateLog(byte[] frame, bool request)
    {
        var pdu = frame.AsSpan(7);
        var result = new ModbusProtocol
        {
            TimeStamp = DateTime.Now,
            Request = request,
            Response = !request,
            TransactionIdentifier = BinaryPrimitives.ReadUInt16BigEndian(frame),
            Length = (ushort)(frame.Length - 6),
            UnitIdentifier = frame[6],
            FunctionCode = pdu[0]
        };

        if ((pdu[0] & 0x80) != 0 && pdu.Length >= 2)
        {
            result.ErrorCode = pdu[0];
            result.ExceptionCode = pdu[1];
        }
        else if (request && pdu.Length >= 5)
        {
            result.StartingAdress = BinaryPrimitives.ReadUInt16BigEndian(pdu[1..]);
            result.Quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu[3..]);
            if (pdu[0] == 5)
                result.ReceiveCoilValues = [result.Quantity];

            if (pdu[0] == 6)
                result.ReceiveRegisterValues = [result.Quantity];

            if (pdu[0] is 16 or 23)
            {
                var offset = pdu[0] == 16 ? 6 : 10;
                if (pdu.Length < offset)
                    return result;

                result.ByteCount = pdu[offset - 1];
                result.ReceiveRegisterValues = new ushort[(pdu.Length - offset) / 2];
                for (var index = 0; index < result.ReceiveRegisterValues.Length; index++)
                    result.ReceiveRegisterValues[index] = BinaryPrimitives.ReadUInt16BigEndian(pdu[(offset + index * 2)..]);

                if (pdu[0] == 23)
                {
                    result.StartingAddressRead = result.StartingAdress;
                    result.QuantityRead = result.Quantity;
                    result.StartingAddressWrite = BinaryPrimitives.ReadUInt16BigEndian(pdu[5..]);
                    result.QuantityWrite = BinaryPrimitives.ReadUInt16BigEndian(pdu[7..]);
                }
            }

            if (pdu[0] == 15 && pdu.Length >= 6)
            {
                result.ByteCount = pdu[5];
                result.ReceiveCoilValues = new ushort[(pdu.Length - 6 + 1) / 2];
                for (var index = 6; index < pdu.Length; index++)
                    result.ReceiveCoilValues[(index - 6) / 2] |= (ushort)(pdu[index] << ((index - 6) % 2 * 8));
            }
        }
        else if (!request && pdu[0] is 1 or 2 or 3 or 4 or 23)
        {
            result.ByteCount = pdu[1];
            if (pdu[0] is 1 or 2)
            {
                result.SendCoilValues = new bool[result.ByteCount * 8];
                for (var index = 0; index < result.SendCoilValues.Length; index++)
                    result.SendCoilValues[index] = (pdu[2 + index / 8] & (1 << (index % 8))) != 0;
            }
            else
            {
                result.SendRegisterValues = new short[result.ByteCount / 2];
                for (var index = 0; index < result.SendRegisterValues.Length; index++)
                    result.SendRegisterValues[index] = BinaryPrimitives.ReadInt16BigEndian(pdu[(2 + index * 2)..]);
            }
        }

        return result;
    }
}
