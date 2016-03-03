using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caseomatic.Net.Matchmaking
{
    public interface IClientMMPacket : IClientPacket
    {
        ClientMMPacketType Type { get; }
    }

    public interface IServerMMPacket : IServerPacket
    {
        ServerMMPacketType Type { get; }
    }
}
