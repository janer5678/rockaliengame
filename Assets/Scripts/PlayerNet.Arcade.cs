using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The telly's warm-up game in the ship lobby (LobbyArcade.cs): whether this player is playing it (their alien on the
    /// couch holds a controller and watches the screen), the stick they're holding (WASD, sent when it changes) and where
    /// the mouse is aiming their bow on the screen (an angle, sent when it changes and with every shot).
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>Playing the telly's game in the lobby (server-written).</summary>
        public readonly NetworkVariable<bool> ArcadePlaying = new NetworkVariable<bool>();

        /// <summary>Server: the direction this player's holding in the telly's game (-1..1 each way; y up the screen).</summary>
        [System.NonSerialized] public Vector2 ServerArcadeStick;
        /// <summary>Server: where this player's bow points in the telly's game (the mouse; zero: not aiming - the stick turns it).</summary>
        [System.NonSerialized] public Vector2 ServerArcadeAim;

        /// <summary>Owner: start / stop playing the telly's game (only in the lobby).</summary>
        [Rpc(SendTo.Server)]
        public void ArcadeJoinRpc(bool on)
        {
            var g = NetGame.Instance;
            if (on && (g == null || g.S != GameState.Waiting || Bot.Value)) return;
            ArcadePlaying.Value = on;
            ServerArcadeStick = Vector2.zero;
            ServerArcadeAim = Vector2.zero;
            LobbyArcade.ServerJoin(this, on);
        }

        /// <summary>Owner: shoot an arrow (Space or a click while playing) - at `ang` (0..65535 round the circle, y down the
        /// screen) when `aimed` (the mouse on the screen), otherwise the way your little alien is facing.</summary>
        [Rpc(SendTo.Server)]
        public void ArcadeShootRpc(ushort ang, bool aimed)
        {
            if (!ArcadePlaying.Value) return;
            if (aimed) ServerArcadeAim = LobbyArcade.AimDir(ang);
            LobbyArcade.ServerShoot(this, aimed ? ServerArcadeAim : Vector2.zero);
        }

        /// <summary>Owner: where the mouse aims the bow (as ArcadeShootRpc's angle; aimed false: let go - the stick turns it).</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        public void ArcadeAimRpc(ushort ang, bool aimed)
        {
            ServerArcadeAim = aimed && ArcadePlaying.Value ? LobbyArcade.AimDir(ang) : Vector2.zero;
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
