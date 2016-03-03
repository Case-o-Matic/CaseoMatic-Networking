using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caseomatic.Net.Matchmaking
{
    [Serializable]
    public class MatchmakingUser
    {
        public readonly string name;
        public readonly Guid globalId;

        public MatchmakingUser(string name)
        {
            this.name = name;
            this.globalId = Guid.NewGuid();
        }
    }
}
