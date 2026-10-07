using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The telly's warm-up game in the ship lobby (LobbyArcade.cs): whether this player is playing it (their alien on the
    /// couch holds a controller and watches the screen) and the stick they're holding (WASD, sent when it changes).
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>Playing the telly's game in the lobby (server-written).</summary>
        public readonly NetworkVariable<bool> ArcadePlaying = new NetworkVariable<bool>();

        /// <summary>Server: the direction this player's holding in the telly's game (-1..1 each way; y up the screen).</summary>
        [System.NonSerialized] public Vector2 ServerArcadeStick;

        /// <summary>Owner: start / stop playing the telly's game (only in the lobby).</summary>
        [Rpc(SendTo.Server)]
        public void ArcadeJoinRpc(bool on)
        {
            var g = NetGame.Instance;
            if (on && (g == null || g.S != GameState.Waiting || Bot.Value)) return;
            ArcadePlaying.Value = on;
            ServerArcadeStick = Vector2.zero;
            LobbyArcade.ServerJoin(this, on);
        }

        /// <summary>Owner: shoot an arrow the way your little alien is facing (Space or a click while playing).</summary>
        [Rpc(SendTo.Server)]
        public void ArcadeShootRpc()
        {
            if (ArcadePlaying.Value) LobbyArcade.ServerShoot(this);
        }

        /// <summary>Owner: the stick (x, y each -100..100).</summary>
        [Rpc(SendTo.Server)]
        public void ArcadeStickRpc(sbyte x, sbyte y)
        {
            var v = new Vector2(x, y) / 100f;
            ServerArcadeStick = v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }
}
