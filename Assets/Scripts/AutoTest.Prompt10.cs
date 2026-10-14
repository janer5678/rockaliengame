using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt10 -host -solo [-rules autowood | jonahbruh] -shotdir DIR: no hammer - UPGRADE (the green plus at
        /// the top of the building plan's wheel) takes your own piece up a tier, lit green (stone 200, metal 500; no armoured
        /// walls; Jonah Test Mode Bruh: stone only, 250), the one-cell spawn base outside team games, Classic playing like
        /// Progress (no Progress mode on the menu, no clock, the other bars draining 5x slower than one fills), six revolver
        /// bullets for 500, and AIRDROP INCOMING taking over the middle of the screen.
        /// </summary>
        IEnumerator Prompt10Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);

            // ---- the numbers
            {
                int ri = Cfg.PowerIndex(Item.RevolverAmmo);
                var r = ri >= 0 ? Cfg.GetPowerRecipe(ri) : default;
                Check(ri >= 0 && r.Wood == 500 && r.Count == 6, $"revolver bullets: {r.Count} for {r.Wood}");
                Check(Cfg.MaxFortify == 2 && Cfg.TierName(Cfg.MaxFortify) == "Metal", $"no armoured walls: metal ({Cfg.TierName(Cfg.MaxFortify)}) is the top");
                Check(!Cfg.HammerOn && Cfg.PlanUpgradeOn && Cfg.RecipeIndex(Item.Hammer) < 0 && Cfg.Removed(Item.Hammer), "no hammer to craft - UPGRADE is on the building plan");
                if (Cfg.JonahBruh) Check(Cfg.HammerCost(0) == 250 && Cfg.HammerCost(1) == 0, $"Jonah Test Mode Bruh: stone only, {Cfg.HammerCost(0)} a piece");
                else Check(Cfg.HammerCost(0) == 200 && Cfg.HammerCost(1) == 500 && Cfg.HammerCost(2) == 0, $"upgrading costs {Cfg.HammerCost(0)} to stone, {Cfg.HammerCost(1)} to metal, nothing higher");
                var o = PlayerController.WheelOptions;
                int demo = System.Array.FindIndex(o, x => x.Demolish);
                Check(o[0].Upgrade && Mathf.Approximately(PlayerController.WheelAngle(0), 0f) && PlayerController.WheelSliceAt(355f) == 0 && PlayerController.WheelSliceAt(10f) == 0
                    && Mathf.Approximately(PlayerController.WheelAngle(demo), 180f) && PlayerController.WheelSliceAt(180f) == demo,
                    $"the wheel: UPGRADE straight up at the top ({PlayerController.WheelAngle(0):0} deg), demolish at the bottom ({PlayerController.WheelAngle(demo):0} deg)");
            }

            // ---- Classic is Progress now; Progress is off the menu
            {
                Check(System.Array.IndexOf(Hud.MenuModes, GameRules.Progress) < 0 && System.Array.IndexOf(Cfg.MoreRules, GameRules.Progress) < 0, "Progress isn't a mode on the menu any more");
                if (Cfg.Rules == GameRules.AutoWood) Check(Cfg.ProgressMode && Cfg.ClassicMode && Cfg.RulesName(Cfg.Rules) == "Classic", "Classic plays like Progress (fill your bar to win)");
            }

            // ---- the spawn base: one cell (2 x 2 in team games)
            {
                Check(Cfg.SmallBedrock, $"{Cfg.ModeName(Cfg.Mode)}: the spawn bedrock is one cell");
                var was = Cfg.Mode;
                Cfg.Mode = GameMode.Teams;
                bool teams = Cfg.SmallBedrock;
                Cfg.Mode = GameMode.Ffa4;
                bool ffa = Cfg.SmallBedrock;
                Cfg.Mode = was;
                Check(teams == Cfg.JonahBruh && ffa, $"2v2: {(teams ? "one cell" : "2 x 2")}; free for all: {(ffa ? "one cell" : "2 x 2")}");
                pc.LocalTeleport(Cfg.BedrockCenter(team) - back * 7f + side * 3f + Vector3.up * 3.5f, Quaternion.LookRotation(back - side * 0.4f).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(back - side * 0.4f).eulerAngles.y, 22f);
                yield return new WaitForSeconds(0.8f);
                yield return Snap("prompt10_one_cell_base");
            }

            // ---- UPGRADE on the plan: lit green, stone, then metal, and no higher
            {
                var at = OnGround(Cfg.BaseCenter[team] - back * 8f + side * 5f, 0f);
                var rot = Quaternion.LookRotation(side);
                var wall = SpawnKeylessPiece(PieceType.Wall, team, at + Vector3.up * 0.0f, rot);
                yield return new WaitForSeconds(0.4f);
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.BuildingPlan, 1);
                me.ServerGive(Cfg.BuildItem, 1000);
                yield return Hold(me, Item.BuildingPlan);
                var stand = OnGround(wall.transform.position - side * 3f, 0.1f);
                pc.LocalTeleport(stand, Quaternion.LookRotation(side).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                var lk = Quaternion.LookRotation(wall.transform.position + Vector3.up * 1.4f - me.EyePos).eulerAngles;
                pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                // hold RMB: the wheel, with UPGRADE at the top; let go over it to pick it
                Binds.TestHold(Bind.Aim, true);
                yield return new WaitForSeconds(0.3f);
                Hud.WheelHover = 0;
                Check(pc.WheelOpen, "holding RMB with the plan opens the wheel");
                yield return Snap("prompt10_wheel_upgrade");
                Hud.WheelHover = 0;
                Binds.TestHold(Bind.Aim, false);
                yield return new WaitForSeconds(0.5f);
                Check(pc.UpgradeMode && pc.PlanUpgradeAiming && pc.DemolishLitCount == 1 && pc.DemolishAimed == wall.NetworkObject, $"UPGRADE picked: your wall in the crosshair lights up ({pc.DemolishLitCount} lit)");
                var rend = wall.GetComponentInChildren<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();
                if (rend != null) rend.GetPropertyBlock(mpb);
                var tint = mpb.GetColor(Shader.PropertyToID("_BaseColor"));
                Check(tint.g > tint.r + 0.1f, $"...green (tint {tint})");
                Check(pc.AimText.Contains("UPGRADE"), $"the aim text says it: {pc.AimText}");
                yield return Snap("prompt10_upgrade_green");
                int wood0 = me.Count(Cfg.BuildItem);
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(0.8f);
                int wood1 = me.Count(Cfg.BuildItem);
                Check(wall.Tier.Value == 1 && wood0 - wood1 == Cfg.HammerCost(0), $"LMB: the wall goes to stone for {wood0 - wood1} (tier {wall.Tier.Value})");
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(0.8f);
                int wood2 = me.Count(Cfg.BuildItem);
                if (Cfg.JonahBruh) Check(wall.Tier.Value == 1 && wood2 == wood1, $"Jonah Test Mode Bruh: stone is as far as it goes (tier {wall.Tier.Value})");
                else
                {
                    Check(wall.Tier.Value == 2 && wood1 - wood2 == 500, $"again: metal for {wood1 - wood2} (tier {wall.Tier.Value})");
                    Binds.TestPress(Bind.Attack);
                    yield return new WaitForSeconds(0.8f);
                    Check(wall.Tier.Value == 2 && me.Count(Cfg.BuildItem) == wood2, $"and no higher - no armoured walls (tier {wall.Tier.Value})");
                }
                yield return Snap("prompt10_upgraded");
                pc.SelectWheel(System.Array.FindIndex(PlayerController.WheelOptions, x => x.Piece == PieceType.Foundation && !x.Demolish && !x.Upgrade));
                wall.NetworkObject.Despawn(true);
            }

            // ---- the wall drops: Progress / Classic has no clock, and the other bars drain 5x slower
            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(2f);
            if (Cfg.ProgressMode)
            {
                Check(g.NoClock && g.TimeLeft > 1e6f, $"no clock once the wall's down ({g.TimeLeft:0} s 'left')");
                int other = (team + 1) % Cfg.TeamCount;
                var v = Vector4.zero; v[team] = 0.3f; v[other] = 0.6f;
                g.ProgressBars.Value = v;
                if (Ball.Instance != null) Ball.Instance.ServerSocket(team);
                yield return new WaitForSeconds(0.2f);
                float m0 = g.ProgressOf(team), o0 = g.ProgressOf(other), t0 = Time.time;
                yield return new WaitForSeconds(4f);
                float up = g.ProgressOf(team) - m0, down = o0 - g.ProgressOf(other), dt = Time.time - t0;
                Check(up > 0f && down > 0f && Mathf.Abs(down / up - 1f / Cfg.ProgressDrainSlower) < 0.04f,
                    $"ball in your machine: yours fills ({up * 100f:0.00}% in {dt:0.0} s), theirs drains {up / Mathf.Max(1e-6f, down):0.0}x slower ({down * 100f:0.00}%)");
                yield return Snap("prompt10_progress_no_clock");
            }

            // ---- AIRDROP INCOMING: takes over the screen, then sits in the middle
            {
                g.ServerAirdropIncoming(g.NetworkManager.ServerTime.Time + 30);
                yield return new WaitForSeconds(0.5f);
                Check(Time.frameCount - Hud.AirdropTakeoverFrame < 5, "AIRDROP INCOMING takes over the whole screen");
                yield return Snap("prompt10_airdrop_takeover");
                pc.MenuOpen = true;
                yield return new WaitForSeconds(0.3f);
                Check(Time.frameCount - Hud.AirdropTakeoverFrame < 5, "...even over the open bag");
                yield return Snap("prompt10_airdrop_over_bag");
                pc.MenuOpen = false;
                for (float t = 0; t < 4f && Time.frameCount - Hud.AirdropTakeoverFrame <= 5; t += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.3f);
                Check(Time.frameCount - Hud.AirdropTakeoverFrame > 5 && Time.frameCount - Hud.BannerShownFrame < 5, "then it settles into its banner in the middle");
                yield return Snap("prompt10_airdrop_banner_middle");
            }
            Log("prompt10 done");
            Application.Quit(0);
        }
    }
}
