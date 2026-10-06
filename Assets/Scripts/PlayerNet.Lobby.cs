using Unity.Netcode;

namespace RockGame
{
    /// <summary>
    /// The ship lobby (ShipLobby.cs) before a match: each player's READY (the match starts once everyone is - at least
    /// two of them: NetGame.ServerReadyToStart) and, in team games, the team they pick (up to its size, Cfg.TeamCap).
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>Pressed READY in the lobby (server-written; cleared when they swap teams or the options change).</summary>
        public readonly NetworkVariable<bool> LobbyReady = new NetworkVariable<bool>();

        /// <summary>Owner: READY on / off.</summary>
        [Rpc(SendTo.Server)]
        public void LobbyReadyRpc(bool on)
        {
            var g = NetGame.Instance;
            if (g == null || g.S != GameState.Waiting) return;
            LobbyReady.Value = on;
        }

        /// <summary>Owner: join this team in the lobby (if it has room). Unreadies you.</summary>
        [Rpc(SendTo.Server)]
        public void LobbyTeamRpc(byte team)
        {
            var g = NetGame.Instance;
            if (g == null || g.S != GameState.Waiting || team >= Cfg.TeamCount || team == Team.Value) return;
            int n = 0;
            foreach (var p in All) if (p != this && p.Team.Value == team) n++;
            if (n >= Cfg.TeamCap(team)) return;
            Team.Value = team;
            Slot.Value = (byte)FreeSlot(team);
            LobbyReady.Value = false;
        }

        /// <summary>Server: the lowest spawn slot nobody else on this team has.</summary>
        int FreeSlot(int team)
        {
            for (int s = 0; s < 4; s++)
            {
                bool taken = false;
                foreach (var p in All) if (p != this && p.Team.Value == team && p.Slot.Value == s) { taken = true; break; }
                if (!taken) return s;
            }
            return 0;
        }

        /// <summary>Server: onto this team (the host's options made theirs too small).</summary>
        public void ServerSetLobbyTeam(int team)
        {
            Team.Value = (byte)team;
            Slot.Value = (byte)FreeSlot(team);
            LobbyReady.Value = false;
        }

        /// <summary>Server: everyone's READY is cleared (the host changed the match options).</summary>
        public static void ServerUnreadyAll()
        {
            foreach (var p in All) if (p != null && p.IsSpawned) p.LobbyReady.Value = false;
        }
    }
}
