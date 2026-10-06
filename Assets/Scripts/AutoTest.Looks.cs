using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest looks -host -solo: Settings > Display's two layers with looks of their own (LayerLooks.cs) and the
        /// mouse icons. HANDS AND TOOLS on (strong cel shading, no colour, thick outlines): the hands go on their layer and
        /// the Stylize pass runs; NOTIFICATIONS on (thick red outlines, glow): a banner goes through its own layer; a held
        /// item's hint shows the left mouse button as its icon. Each photographed; nothing is saved.
        /// </summary>
        IEnumerator LooksRoutine(PlayerNet me, PlayerController pc)
        {
            yield return Hold(me, Item.Rock);
            GameSettings.SetPostFx(true, false);
            yield return Snap("looks_00_default");

            // the hands and tools with looks of their own
            GameSettings.HandsOwn.Set(true, false);
            GameSettings.HandsCel.Set(true, false); GameSettings.HandsCelStrength.Set(1f, false);
            GameSettings.HandsOutline.Set(true, false); GameSettings.HandsOutlineStrength.Set(1f, false);
            GameSettings.HandsSaturation.Set(0f, false);
            yield return new WaitForSeconds(0.5f);
            int onLayer = 0, all = 0;
            var vm = ViewModel.Last;
            if (vm != null && vm.Root != null)
                foreach (var r in vm.Root.GetComponentsInChildren<Renderer>()) { all++; if (r.gameObject.layer == PostFx.HandLayer) onLayer++; }
            Check(all > 0 && onLayer == all && PostFx.HandsOwnLook && PostFx.StylizeOn, $"HANDS AND TOOLS own look: the hands on their layer ({onLayer}/{all}) and the stylize pass on");
            yield return Snap("looks_01_hands_own_grey_cel");
            GameSettings.ResetHandsLook(false);

            // the notifications with looks of their own
            GameSettings.NotifOwn.Set(true, false);
            GameSettings.NotifOutline.Set(true, false); GameSettings.NotifOutlineWidth.Set(8f, false);
            GameSettings.NotifOutlineColour.Set(new Color(0.8f, 0.1f, 0.1f), false);
            GameSettings.NotifBloom.Set(true, false); GameSettings.NotifBloomStrength.Set(1f, false);
            Hud.Banner("TRADE STATION UNLOCKED", "Craft it in your bag - a test of the notifications' own look");
            yield return new WaitForSeconds(0.5f);
            Check(UiLook.NotifOwnLayer, "NOTIFICATIONS own look: the banner went through its own layer");
            yield return Snap("looks_02_notification_own");
            GameSettings.ResetNotifLook(false);
            yield return new WaitForSeconds(0.3f);
            Hud.Banner("AIRDROP INCOMING", "the usual look again");
            yield return Snap("looks_03_notification_default");

            // the left mouse button as its icon in a held item's hint
            me.ServerGive(Item.C4, 1);
            yield return Hold(me, Item.C4);
            Check(Tutorial.WithMouseIcons("LMB: throw").IndexOf("LMB", System.StringComparison.Ordinal) < 0, "LMB in a hint turns into the mouse icon");
            yield return Snap("looks_04_lmb_icon_hint");
            Log("looks test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
