using System;

namespace Caseomatic.Net
{
    public interface IPacketRequestable
    {
    }

    public interface IPacketHandshake
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
