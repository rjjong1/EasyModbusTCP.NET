/*
Copyright (c) 2018-2020 Rossmann-Engineering
Permission is hereby granted, free of charge,
to any person obtaining a copy of this software
and associated documentation files (the "Software"),
to deal in the Software without restriction,
including without limitation the rights to use,
copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission
notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
