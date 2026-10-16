using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest handanim -host -solo -shotdir DIR [-anims a,b] (windowed): the first-person hands' ANIMATIONS frame by
    /// frame (handanim_NAME_NN.png) - each swing, the switch to an item, a throw, eating, the bow's draw, the spear's
    /// wind-up, a gun's kick - so how they move can be looked at, not just how they hold still.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator HandAnimShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            int team = me.Team.Value;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            pc.SetLook(Cfg.SpawnYaw(team), 8f);
            GameSettings.SetGraphics(0, false);
            GameSettings.SetRoundHands(true, false);
            yield return new WaitForSeconds(0.8f);
            string only = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-anims") only = args[i + 1];
            bool Want(string n) => string.IsNullOrEmpty(only) || System.Array.Exists(only.Split(','), k => n.StartsWith(k));

            IEnumerator Give(Item it)
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, it == Item.Spear ? 3 : 1, it == Item.Revolver ? 6 : 0);
                if (it == Item.Bow) me.ServerGive(Item.Arrow, 5);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.6f);
            }
            void Clear()
            {
                pc.DebugDraw = -1f;
                ViewModel.DebugSwingE = ViewModel.DebugThrowE = ViewModel.DebugEatE = ViewModel.DebugUseE = ViewModel.DebugEquipE = -1f;
                ViewModel.DebugAim = ViewModel.DebugBall = false;
            }
            IEnumerator Frames(string name, int n, System.Action<int, float> set)
            {
                for (int f = 0; f < n; f++)
                {
                    set(f, f / (float)(n - 1));
                    yield return null; yield return null;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"handanim_{name}_{f:00}.png"));
                    yield return null;
                }
                Clear();
                Log("anim " + name);
                yield return new WaitForSeconds(0.3f);
            }

            // swings: the whole swing, from the click to back at rest
            foreach (var it in new[] { Item.Rock, Item.Hatchet, Item.Sword, Item.Pickaxe, Item.Spear })
            {
                string n = it.ToString().ToLower() + "_swing";
                if (!Want(n)) continue;
                yield return Give(it);
                pc.DebugSwing(0.6f);
                yield return Frames(n, 12, (f, k) => ViewModel.DebugSwingE = k * 0.6f);
            }
            // switching to an item: it comes up into the hand
            foreach (var it in new[] { Item.Hatchet, Item.Pistol, Item.Rock })
            {
                string n = it.ToString().ToLower() + "_deploy";
                if (!Want(n)) continue;
                yield return Give(it);
                yield return Frames(n, 8, (f, k) => ViewModel.DebugEquipE = k * 0.45f);
            }
            // a throw (C4), eating, the building plan's use
            if (Want("c4_throw")) { yield return Give(Item.C4); yield return Frames("c4_throw", 8, (f, k) => ViewModel.DebugThrowE = k * 0.35f); }
            if (Want("berry_eat")) { yield return Give(Item.Berry); yield return Frames("berry_eat", 8, (f, k) => ViewModel.DebugEatE = k * 0.6f); }
            // the bow drawn back, the spear wound up
            if (Want("bow_draw")) { yield return Give(Item.Bow); yield return Frames("bow_draw", 8, (f, k) => pc.DebugDraw = k); }
            if (Want("spear_windup")) { yield return Give(Item.Spear); yield return Frames("spear_windup", 8, (f, k) => pc.DebugDraw = k); }
            // a gun's shot (the kick)
            if (Want("revolver_fire")) { yield return Give(Item.Revolver); yield return Frames("revolver_fire", 8, (f, k) => ViewModel.DebugUseE = k * 0.42f); }
            Check(AudioListener.volume < 0.001f && GameSettings.Muted, $"test runs are silent (volume {AudioListener.volume})");
            Log("handanim done");
            Application.Quit(0);
        }
    }
}
