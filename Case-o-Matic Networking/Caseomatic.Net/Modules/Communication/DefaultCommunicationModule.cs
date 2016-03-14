using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net
{
    public class DefaultCommunicationModule<TReceivePacket, TSendPacket>
        : CommunicationModule<TReceivePacket, TSendPacket> where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        public override TReceivePacket ConvertReceive(byte[] bytes)
        {
            return PacketConverter.ToPacket<TReceivePacket>(bytes);
        }

        public override byte[] ConvertSend(TSendPacket packet)
        {
            return PacketConverter.ToBytes(packet);
        }
    }
}
