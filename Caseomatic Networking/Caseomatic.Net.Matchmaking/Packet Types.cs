using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caseomatic.Net.Matchmaking
{
    [Serializable]
    public enum ClientMMPacketType : byte
    {
        Register,
        Unregister,
        Update,
        AnswerHeartbeat
    }

    [Serializable]
    public enum ServerMMPacketType : byte
    {
        AnswerRegister,
        RequestHeartbeat
    }
}
