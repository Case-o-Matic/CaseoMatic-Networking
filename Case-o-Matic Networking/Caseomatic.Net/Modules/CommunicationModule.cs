using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;

namespace Caseomatic.Net
{
    public abstract class CommunicationModule<TReceivePacket, TSendPacket>
        where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        public abstract TReceivePacket ConvertReceive(byte[] bytes);
        public abstract byte[] ConvertSend(TSendPacket packet);
    }
}
