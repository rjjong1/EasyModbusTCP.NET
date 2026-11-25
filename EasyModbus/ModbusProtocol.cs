namespace EasyModbus;

public class ModbusProtocol
{
    public enum ProtocolType
    {
        ModbusTCP = 0,
        ModbusUDP = 1,
        ModbusRTU = 2
    };

    public DateTime TimeStamp;
    public bool Request;
    public bool Response;
    public ushort TransactionIdentifier;
    public ushort ProtocolIdentifier;
    public ushort Length;
    public byte UnitIdentifier;
    public byte FunctionCode;
    public ushort StartingAdress;
    public ushort StartingAddressRead;
    public ushort StartingAddressWrite;
    public ushort Quantity;
    public ushort QuantityRead;
    public ushort QuantityWrite;
    public byte ByteCount;
    public byte ExceptionCode;
    public byte ErrorCode;
    public ushort[] ReceiveCoilValues = [];
    public ushort[] ReceiveRegisterValues = [];
    public short[] SendRegisterValues = [];
    public bool[] SendCoilValues = [];
    public ushort Crc;
}
