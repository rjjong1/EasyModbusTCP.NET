using System.Buffers.Binary;
using System.Text;

namespace EasyModbus;

public partial class ModbusClient
{
    public static float ConvertRegistersToFloat(int[] registers) => ConvertRegistersToFloat(registers, RegisterOrder.LowHigh);
    public static float ConvertRegistersToFloat(int[] registers, RegisterOrder registerOrder) => BitConverter.Int32BitsToSingle(ConvertRegistersToInt(registers, registerOrder));
    public static int ConvertRegistersToInt(int[] registers) => ConvertRegistersToInt(registers, RegisterOrder.LowHigh);
    public static int ConvertRegistersToInt(int[] registers, RegisterOrder registerOrder) => unchecked((int)ReadRegisterBits(registers, 2, registerOrder));
    public static long ConvertRegistersToLong(int[] registers) => ConvertRegistersToLong(registers, RegisterOrder.LowHigh);
    public static long ConvertRegistersToLong(int[] registers, RegisterOrder registerOrder) => unchecked((long)ReadRegisterBits(registers, 4, registerOrder));
    public static double ConvertRegistersToDouble(int[] registers) => ConvertRegistersToDouble(registers, RegisterOrder.LowHigh);
    public static double ConvertRegistersToDouble(int[] registers, RegisterOrder registerOrder) => BitConverter.Int64BitsToDouble(ConvertRegistersToLong(registers, registerOrder));

    public static int[] ConvertFloatToRegisters(float floatValue) => ConvertFloatToRegisters(floatValue, RegisterOrder.LowHigh);
    public static int[] ConvertFloatToRegisters(float floatValue, RegisterOrder registerOrder) => ConvertIntToRegisters(BitConverter.SingleToInt32Bits(floatValue), registerOrder);
    public static int[] ConvertIntToRegisters(int intValue) => ConvertIntToRegisters(intValue, RegisterOrder.LowHigh);
    public static int[] ConvertIntToRegisters(int intValue, RegisterOrder registerOrder) => WriteRegisterBits(unchecked((uint)intValue), 2, registerOrder);
    public static int[] ConvertLongToRegisters(long longValue) => ConvertLongToRegisters(longValue, RegisterOrder.LowHigh);
    public static int[] ConvertLongToRegisters(long longValue, RegisterOrder registerOrder) => WriteRegisterBits(unchecked((ulong)longValue), 4, registerOrder);
    public static int[] ConvertDoubleToRegisters(double doubleValue) => ConvertDoubleToRegisters(doubleValue, RegisterOrder.LowHigh);
    public static int[] ConvertDoubleToRegisters(double doubleValue, RegisterOrder registerOrder) => ConvertLongToRegisters(BitConverter.DoubleToInt64Bits(doubleValue), registerOrder);

    private static ulong ReadRegisterBits(int[] registers, int count, RegisterOrder order)
    {
        ArgumentNullException.ThrowIfNull(registers);
        if (registers.Length != count)
            throw new ArgumentException($"Expected exactly {count} registers.", nameof(registers));
        ValidateRegisterOrder(order);
        ulong value = 0;
        for (var index = 0; index < count; index++)
        {
            var register = order == RegisterOrder.LowHigh ? index : count - index - 1;
            value |= (ulong)(ushort)registers[register] << (index * 16);
        }
        return value;
    }

    private static int[] WriteRegisterBits(ulong value, int count, RegisterOrder order)
    {
        ValidateRegisterOrder(order);
        var registers = new int[count];
        for (var index = 0; index < count; index++)
            registers[order == RegisterOrder.LowHigh ? index : count - index - 1] = (ushort)(value >> (index * 16));
        return registers;
    }

    private static void ValidateRegisterOrder(RegisterOrder order)
    {
        if (order is not RegisterOrder.LowHigh and not RegisterOrder.HighLow)
            throw new ArgumentOutOfRangeException(nameof(order));
    }

    /// <summary>Decodes ASCII register bytes, low byte first. Length is in characters.</summary>
    public static string ConvertRegistersToString(int[] registers, int offset, int stringLength)
    {
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, registers.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(stringLength);
        if (stringLength > (registers.Length - offset) * 2L)
            throw new ArgumentOutOfRangeException(nameof(stringLength));
        Span<byte> bytes = stringLength <= 256 ? stackalloc byte[stringLength] : new byte[stringLength];
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)(registers[offset + index / 2] >> (index % 2 * 8));
        return Encoding.ASCII.GetString(bytes);
    }

    public static int[] ConvertStringToRegisters(string stringToConvert)
    {
        ArgumentNullException.ThrowIfNull(stringToConvert);
        var bytes = Encoding.ASCII.GetBytes(stringToConvert);
        var registers = new int[(bytes.Length + 1) / 2];
        for (var index = 0; index < bytes.Length; index++)
            registers[index / 2] |= bytes[index] << (index % 2 * 8);
        return registers;
    }

    public static ushort CalculateCRC(byte[] data, ushort numberOfBytes, int startByte)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ModbusFrame.CalculateCrc(data.AsSpan(startByte, numberOfBytes));
    }

    public static ushort CalculateCRC(ReadOnlySpan<byte> data) => ModbusFrame.CalculateCrc(data);

    public static bool DetectValidModbusFrame(byte[] readBuffer, int length)
    {
        ArgumentNullException.ThrowIfNull(readBuffer);
        if (length < 4 || length > Math.Min(readBuffer.Length, 256))
            return false;
        return ModbusFrame.CalculateCrc(readBuffer.AsSpan(0, length - 2)) ==
            BinaryPrimitives.ReadUInt16LittleEndian(readBuffer.AsSpan(length - 2, 2));
    }
}
