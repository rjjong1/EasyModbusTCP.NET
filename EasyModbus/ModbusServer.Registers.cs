namespace EasyModbus;

public partial class ModbusServer
{
    // Wire addresses 0..65535 map to the legacy local indices 1..65536.
    public class HoldingRegisterCollection(ModbusServer modbusServer)
    {
        internal readonly short[] _localArray = new short[65537];
        public short this[int index]
        {
            get
            {
                lock (modbusServer._stateLock)
                    return _localArray[index];
            }
            set
            {
                lock (modbusServer._stateLock)
                    _localArray[index] = value;
            }
        }
    }

    public class InputRegisterCollection(ModbusServer modbusServer)
    {
        internal readonly short[] _localArray = new short[65537];
        public short this[int index]
        {
            get
            {
                lock (modbusServer._stateLock)
                    return _localArray[index];
            }
            set
            {
                lock (modbusServer._stateLock)
                    _localArray[index] = value;
            }
        }
    }

    public class CoilCollection(ModbusServer modbusServer)
    {
        internal readonly bool[] _localArray = new bool[65537];
        public bool this[int index]
        {
            get
            {
                lock (modbusServer._stateLock)
                    return _localArray[index];
            }
            set
            {
                lock (modbusServer._stateLock)
                    _localArray[index] = value;
            }
        }
    }

    public class DiscreteInputCollection(ModbusServer modbusServer)
    {
        internal readonly bool[] _localArray = new bool[65537];
        public bool this[int index]
        {
            get
            {
                lock (modbusServer._stateLock)
                    return _localArray[index];
            }
            set
            {
                lock (modbusServer._stateLock)
                    _localArray[index] = value;
            }
        }
    }
}
