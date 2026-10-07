using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Spectating from the ship lobby, by choice: anyone in the lobby can press SPECTATE (their alien gets up off the
    /// couch - their player object goes - and they watch the match when it starts), and a spectator can press PLAY to
    /// take a seat again while there's room. Anyone who joins a lobby that's already full comes in as a spectator
    /// (Bootstrap.Approve). Only before the match starts.
    /// </summary>
    public static partial class SpectatorSwitch
    {
        /// <summary>(tests) the last switch the server turned down, and why.</summary>
        public static string LastRefusal = "";
    }

    public partial class Spectator
    {
        /// <summary>Server: this client now watches (their name as shown).</summary>
        public static void ServerAdd(ulong clientId, string name) => s_Approved[clientId] = (name ?? "", Time.unscaledTime);
        /// <summary>Server: this client plays again.</summary>
        public static void ServerRemove(ulong clientId) => s_Approved.Remove(clientId);
    }

    public partial class NetGame
    {
        /// <summary>Someone in the lobby asks to watch instead of playing (watch true) or to play again (false).</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void SpectateRpc(bool watch, RpcParams rpcParams = default) => ServerSetSpectating(rpcParams.Receive.SenderClientId, watch);

        /// <summary>Server: client `id` watches / plays from now on (only in the lobby; playing needs a free place).</summary>
        public bool ServerSetSpectating(ulong id, bool watch)
        {
            if (!IsServer || S != GameState.Waiting) { SpectatorSwitch.LastRefusal = "not in the lobby"; return false; }
            var nm = NetworkManager;
            if (watch)
            {
                if (Spectator.ServerIs(id)) return true;
                if (!Spectator.ServerHasRoom) { SpectatorSwitch.LastRefusal = "no room to spectate"; return false; }
                PlayerNet p = null;
                foreach (var q in PlayerNet.All) if (q != null && !q.Bot.Value && q.OwnerClientId == id) { p = q; break; }
                if (p == null) { SpectatorSwitch.LastRefusal = "no player"; return false; }
                Spectator.ServerAdd(id, p.DisplayName);
                p.NetworkObject.Despawn(true);
                Broadcast($"{p.DisplayName} is spectating");
                return true;
            }
            if (!Spectator.ServerIs(id)) return true;
            int players = Spectator.ServerPlayerClients(nm) + PlayerNet.BotCount;
            if (players >= Cfg.PlayersNeeded) { SpectatorSwitch.LastRefusal = "full"; FullRpc(RpcTarget.Single(id, RpcTargetUse.Temp)); return false; }
            Spectator.ServerRemove(id);
            var prefab = Bootstrap.I != null && Bootstrap.I.playerPrefab != null ? Bootstrap.I.playerPrefab : nm.NetworkConfig.PlayerPrefab;
            if (prefab == null) { SpectatorSwitch.LastRefusal = "no player prefab"; return false; }
            var go = Instantiate(prefab, Cfg.ArenaCenter + new Vector3(Random.Range(-6f, 6f), 0.1f, Random.Range(-6f, 6f)), Quaternion.identity);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);
            return true;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void FullRpc(RpcParams rpcParams) => Hud.Banner("LOBBY FULL", "There's no free place to play right now");
    }
}
