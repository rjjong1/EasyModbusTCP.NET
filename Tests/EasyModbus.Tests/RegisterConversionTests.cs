using NUnit.Framework;

namespace EasyModbus.Tests;

[TestFixture]
public class RegisterConversionTests
{
    [TestCase(ModbusClient.RegisterOrder.LowHigh)]
    [TestCase(ModbusClient.RegisterOrder.HighLow)]
    public void NumericConversionsPreserveBitsInEitherWordOrder(ModbusClient.RegisterOrder order)
    {
        foreach (var value in new[] { int.MinValue, -1, 0, 0x12345678, int.MaxValue })
            Assert.That(ModbusClient.ConvertRegistersToInt(ModbusClient.ConvertIntToRegisters(value, order), order), Is.EqualTo(value));
        foreach (var value in new[] { long.MinValue, -1, 0, 0x123456789ABCDEF0, long.MaxValue })
            Assert.That(ModbusClient.ConvertRegistersToLong(ModbusClient.ConvertLongToRegisters(value, order), order), Is.EqualTo(value));
        foreach (var bits in new[] { int.MinValue, 0, 0x3F800000, 0x7F800000, 0x7FC01234 })
        {
            var registers = ModbusClient.ConvertFloatToRegisters(BitConverter.Int32BitsToSingle(bits), order);
            Assert.That(BitConverter.SingleToInt32Bits(ModbusClient.ConvertRegistersToFloat(registers, order)), Is.EqualTo(bits));
        }
        foreach (var bits in new[] { long.MinValue, 0, 0x3FF0000000000000, 0x7FF0000000000000, 0x7FF8123456789ABC })
        {
            var registers = ModbusClient.ConvertDoubleToRegisters(BitConverter.Int64BitsToDouble(bits), order);
            Assert.That(BitConverter.DoubleToInt64Bits(ModbusClient.ConvertRegistersToDouble(registers, order)), Is.EqualTo(bits));
        }
    }

    [Test]
    public void RegisterOrderMatchesKnownWireValues()
    {
        Assert.That(ModbusClient.ConvertIntToRegisters(0x12345678), Is.EqualTo(new[] { 0x5678, 0x1234 }));
        Assert.That(ModbusClient.ConvertIntToRegisters(0x12345678, ModbusClient.RegisterOrder.HighLow), Is.EqualTo(new[] { 0x1234, 0x5678 }));
        Assert.That(ModbusClient.ConvertFloatToRegisters(1.0f), Is.EqualTo(new[] { 0, 0x3F80 }));
        Assert.That(ModbusClient.ConvertDoubleToRegisters(1.0), Is.EqualTo(new[] { 0, 0, 0, 0x3FF0 }));
        Assert.That(ModbusClient.ConvertRegistersToInt([-1, -1]), Is.EqualTo(-1));
    }

    [TestCase("")]
    [TestCase("A")]
    [TestCase("ABC")]
    [TestCase("Texplorer ")]
    public void AsciiStringsRoundTripIncludingOddLengths(string text)
    {
        var registers = ModbusClient.ConvertStringToRegisters(text);
        Assert.That(ModbusClient.ConvertRegistersToString(registers, 0, text.Length), Is.EqualTo(text));
    }

    [Test]
    public void StringOffsetsAndLengthAreChecked()
    {
        Assert.That(ModbusClient.ConvertRegistersToString([0, 0x4241, 0x4443], 1, 3), Is.EqualTo("ABC"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModbusClient.ConvertRegistersToString([0], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModbusClient.ConvertRegistersToString([0], 0, 3));
        Assert.Throws<ArgumentException>(() => ModbusClient.ConvertRegistersToFloat([0]));
        Assert.Throws<ArgumentNullException>(() => ModbusClient.ConvertRegistersToInt(null!));
    }

    [Test]
    public void CrcMatchesPublishedRtuFrameAndRejectsCorruption()
    {
        byte[] request = [1, 3, 0, 0, 0, 10];
        Assert.That(ModbusClient.CalculateCRC(request), Is.EqualTo(0xCDC5));
        Assert.That(ModbusClient.CalculateCRC(request, 6, 0), Is.EqualTo(0xCDC5));
        byte[] frame = [.. request, 0xC5, 0xCD];
        Assert.That(ModbusClient.DetectValidModbusFrame(frame, frame.Length), Is.True);
        frame[3] = 1;
        Assert.That(ModbusClient.DetectValidModbusFrame(frame, frame.Length), Is.False);
        Assert.That(ModbusClient.DetectValidModbusFrame([], 0), Is.False);
    }
}
