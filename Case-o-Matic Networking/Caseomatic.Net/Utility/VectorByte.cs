using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace Caseomatic.Net
{
    // Stores 8 flags into one byte efficiency
    public struct VectorByte
    {
        private BitVector32 bitVector;
        
        public bool this[byte index]
        {
            get { return bitVector[index]; }
            set { bitVector[index] = value; }
        }

        public VectorByte(byte value)
        {
            bitVector = new BitVector32(value);
        }
        public VectorByte(params bool[] flags)
        {
            bitVector = new BitVector32();
            for (byte i = 0; i < Math.Min(flags.Length, 8); i++)
            {
                bitVector[i] = flags[i];
            }
        }

        public static implicit operator byte(VectorByte vByte)
        {
            return (byte)vByte.bitVector.Data;
        }
        public static implicit operator VectorByte(byte b)
        {
            return new VectorByte(b);
        }
    }
}
