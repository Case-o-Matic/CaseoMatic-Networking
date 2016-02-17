using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caseomatic.Net.Matchmaking
{
    public class Match
    {
        public readonly string name;
        public readonly MatchSettings settings;
        private readonly Dictionary<Guid, MatchmakingUser> users;

        public Match(string name, MatchSettings settings)
        {
            this.name = name;
            this.settings = settings;
            users = new Dictionary<Guid, MatchmakingUser>(settings.neededPlayersToStart);
        }

        public float EvaulateAppliedSearchSettings()
        {

        }
    }

    public class MatchSettings
    {
        public int neededPlayersToStart;

        public MatchSettings(int neededPlayersToStart)
        {
            this.neededPlayersToStart = neededPlayersToStart;
        }
    }
}
