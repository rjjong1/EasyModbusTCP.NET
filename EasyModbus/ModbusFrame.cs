using System.Buffers.Binary;

namespace EasyModbus;

internal static class ModbusFrame
{
    internal const int MaxLength = 260;

    internal static byte[] Create(ushort transaction, byte unit, ReadOnlySpan<byte> pdu)
    {
        var frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(pdu.Length + 1));
        frame[6] = unit;
        pdu.CopyTo(frame.AsSpan(7));
        return frame;
    }

    internal static void Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length is < 8 or > MaxLength || BinaryPrimitives.ReadUInt16BigEndian(frame[2..]) != 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(frame[4..]) != frame.Length - 6)
            throw new InvalidDataException("Invalid Modbus TCP/UDP frame header or length.");
    }

    internal static async ValueTask<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[7];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        if (length is < 2 or > 254 || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) != 0)
            throw new InvalidDataException("Invalid Modbus TCP frame header or length.");

        var frame = new byte[6 + length];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(7), cancellationToken).ConfigureAwait(false);
        return frame;
    }

    internal static ushort CalculateCrc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }
}
