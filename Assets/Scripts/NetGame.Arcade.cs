using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The ship lobby's network bits that aren't a player's own:
    /// - the telly's warm-up game (LobbyArcade.cs): the server runs it and sends everyone a small snapshot ~15 times a
    ///   second, so every screen shows the same game;
    /// - the SPECTATE team option (Hud.Lobby.cs): a spectator clicking a team takes a seat on that team in one go.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Server -> everyone else: the telly's game as it is now (LobbyArcade.Write / Read).</summary>
        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        public void ArcadeStateRpc(byte[] state) => LobbyArcade.ClientReceive(state);

        /// <summary>A spectator in the lobby clicked a team: play again, on that team (if it has room).</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PlayOnTeamRpc(byte team, RpcParams rpcParams = default) => ServerPlayOnTeam(rpcParams.Receive.SenderClientId, team);

        /// <summary>Server: spectator `id` takes a seat again (ServerSetSpectating) and goes onto `team` if it has room
        /// (otherwise they stay on the team they were given).</summary>
        public bool ServerPlayOnTeam(ulong id, int team)
        {
            if (!ServerSetSpectating(id, false)) return false;
            PlayerNet p = null;
            foreach (var q in PlayerNet.All) if (q != null && q.IsSpawned && !q.Bot.Value && q.OwnerClientId == id) { p = q; break; }
            if (p == null || team < 0 || team >= Cfg.TeamCount || p.Team.Value == team) return p != null;
            int n = 0;
            foreach (var q in PlayerNet.All) if (q != null && q != p && q.Team.Value == team) n++;
            if (n < Cfg.TeamCap(team)) p.ServerSetLobbyTeam(team);
            return true;
        }
    }
}
