using UnityEngine;

namespace RockGame
{
    public partial class Hud
    {
        /// <summary>The lobby's BACK button (Hud.Lobby.cs calls this).</summary>
        void LobbyBackPressed(Bootstrap boot)
        {
            boot.Leave();
        }
    }
}
