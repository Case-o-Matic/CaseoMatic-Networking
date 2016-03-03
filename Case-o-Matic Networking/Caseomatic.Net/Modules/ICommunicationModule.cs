using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net
{
    public interface ICommunicationModule<TReceivePacket, TSendPacket>
        : IModule where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        TReceivePacket ConvertReceive(byte[] bytes);
        byte[] ConvertSend(TSendPacket packet);
    }
}
