using Unity.Netcode;

namespace RockGame
{
    /// <summary>What a player has open on their screen, so the spectators watching them see it too (Hud.Spectator.cs).</summary>
    public enum WatchUi : byte { None, Bag, Container, Upgrades }

    /// <summary>
    /// For the ones watching a player (spectators: Spectator.cs / Hud.Spectator.cs, and the kill cam's replay:
    /// DeathReplay.cs): which screen they have open - the bag, a chest (and which) or the upgrade station - written by
    /// their own machine (PlayerController.Watch.cs), and the animation triggers everyone already gets (a swing, a throw).
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>What this player has open (WatchUi), and the container when it's one (its NetworkObjectId; 0 none).</summary>
        public readonly NetworkVariable<byte> WatchOpen = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<ulong> WatchBox = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>The swing animation everyone sees on this player's body (1 the moment a swing lands, falling to 0).</summary>
        public float SwingAnim => m_Swing;
        /// <summary>The throw animation everyone sees (1 the moment something's thrown, falling to 0).</summary>
        public float ThrowAnim => m_Throw;

        /// <summary>The container this player has open (null: none, or it's gone).</summary>
        public Container WatchContainer
        {
            get
            {
                if (WatchOpen.Value != (byte)WatchUi.Container || WatchBox.Value == 0 || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(WatchBox.Value, out var no) && no != null ? no.GetComponent<Container>() : null;
            }
        }
    }
}
