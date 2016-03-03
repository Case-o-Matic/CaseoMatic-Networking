using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net
{
    public class DefaultCommunicationModule<TReceivePacket, TSendPacket>
        : ICommunicationModule<TReceivePacket, TSendPacket> where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        public TReceivePacket ConvertReceive(byte[] bytes)
        {
            return PacketConverter.ToPacket<TReceivePacket>(bytes);
        }

        public byte[] ConvertSend(TSendPacket packet)
        {
            return PacketConverter.ToBytes(packet);
        }
    }
}
