using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;

namespace Caseomatic.Net
{
    [Synchronization]
    public abstract class CommunicationModule<TReceivePacket, TSendPacket> : ContextBoundObject
        where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        public abstract TReceivePacket ConvertReceive(byte[] bytes);
        public abstract byte[] ConvertSend(TSendPacket packet);
    }
}
