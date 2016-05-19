using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net.Utility
{
    public static class PacketUtility
    {
        public static NetOutgoingMessage CreateMessageFromPacket<TPacket>(this IPacket packet, NetPeer peer) where TPacket : IPacket
        {
            var bytes = PacketConverter.ToBytes(packet);
            var message = peer.CreateMessage(bytes.Length);
            message.Write(bytes);

            return message;
        }
    }
}
