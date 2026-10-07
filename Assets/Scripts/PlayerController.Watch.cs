namespace RockGame
{
    /// <summary>Tells the spectators which screen you have open (PlayerNet.Watch.cs), so they see your bag, the chest you're
    /// in or your upgrade station too (Hud.Spectator.cs).</summary>
    public partial class PlayerController
    {
        /// <summary>Update: what's open now, written when it changes.</summary>
        void TickWatchSync()
        {
            if (m_Net == null || !m_Net.IsSpawned || !m_Net.IsOwner) return;
            var ui = WatchUi.None;
            ulong box = 0;
            if (MenuOpen && !m_Net.Dead.Value)
            {
                if (UpgradesOpen) ui = WatchUi.Upgrades;
                else if (LootTarget != null && LootTarget.IsSpawned) { ui = WatchUi.Container; box = LootTarget.NetworkObjectId; }
                else ui = WatchUi.Bag;
            }
            if (m_Net.WatchOpen.Value != (byte)ui) m_Net.WatchOpen.Value = (byte)ui;
            if (m_Net.WatchBox.Value != box) m_Net.WatchBox.Value = box;
        }
    }
}
