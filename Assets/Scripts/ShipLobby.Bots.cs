using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The AI bots (PlayerNet.Bot.cs) in the ship lobby: they're always READY - so when only bots are left to ready up,
    /// the match starts by itself. The server puts any bot's READY back on whenever something clears it (a team swap,
    /// the host's GAME OPTIONS un-readying everyone: ServerKeepBotsReady, every frame while the lobby's up), and the
    /// lobby's screen counts and ticks them as ready either way (IsReady). (And a test helper: where the telly is on screen.)
    /// </summary>
    public partial class ShipLobby
    {
        /// <summary>This player counts as READY in the lobby (pressed READY, or a bot).</summary>
        public static bool IsReady(PlayerNet p) => p != null && (p.LobbyReady.Value || (p.IsSpawned && p.Bot.Value));

        /// <summary>(tests) where the telly's picture is on screen (GUI space: y down from the top; empty if it isn't up).</summary>
        public static Rect TellyOnScreen
        {
            get
            {
                var cam = Camera.main;
                if (s_I == null || s_I.m_Tv == null || cam == null) return default;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var c in new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f) })
                {
                    var w = s_I.m_Tv.TransformPoint(k_Screen + new Vector3(c.x * 0.27f, c.y * 0.21f, 0f)); // (the picture: 0.54 x 0.42)
                    var sp = cam.WorldToScreenPoint(w);
                    if (sp.z <= 0f) return default;
                    float gy = Screen.height - sp.y;
                    x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                    y0 = Mathf.Min(y0, gy); y1 = Mathf.Max(y1, gy);
                }
                return Rect.MinMaxRect(x0, y0, x1, y1);
            }
        }

        /// <summary>Server, every frame the lobby's up (LobbyArcade's server tick): every bot's READY stays on.</summary>
        public static void ServerKeepBotsReady()
        {
            foreach (var p in PlayerNet.All)
                if (p != null && p.IsSpawned && p.Bot.Value && !p.LobbyReady.Value) p.LobbyReady.Value = true;
        }
    }
}
