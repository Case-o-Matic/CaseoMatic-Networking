using System;
using System.Collections.Generic;
using System.Text;

namespace Caseomatic.Net
{
    public interface IPacketRequestable
    {
    }

    public interface IPacket
    {
    }

    public interface IClientPacket : IPacket
    {
    }
    public interface IServerPacket : IPacket
    {
    }
}
