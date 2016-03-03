using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caseomatic.Net.Matchmaking
{
    public class MatchmakingServer
    {
        private readonly Server<IServerMMPacket, IClientMMPacket> server;

        public MatchmakingServer(int port)
        {
            server = new Server<IServerMMPacket, IClientMMPacket>(port);
        }
    }
}
