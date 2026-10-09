using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt14 -host -solo -rules autowood (Classic):
        /// the chainsaw and saddle are Advanced Trade Station items, the chainsaw 2000 wood and never wears out; the High
        /// Strength Battering Ram (a Trade Station item) breaks a metal wall in one slam where the normal ram knocks it a
        /// step; a rocket breaks an armoured wall outright; a ladder needs a wall, breaks (nothing dropped) with it; the
        /// auto turret takes any weapon and only arrows in its second slot.
        /// -autotest prompt14 -host -solo -rules jonah: alien dust prices, the converter (wood in, CONVERT, dust out) and
        /// Upgrade 10 Walls through the wall picker.
        /// </summary>
        IEnumerator Prompt14Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            g.TimerPaused.Value = true;
            if (Cfg.Jonah) { yield return JonahRoutine(me, pc); Log("prompt14 done"); Application.Quit(0); yield break; }

            // ---- the theme maps' light never sticks: Swamp, then Jungle straight after it, then back to this map - exactly as before
            {
                var home = Cfg.Map;
                pc.SetLook(Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y, 5f);
                yield return Snap("p14_light_before");
                var sun = RenderSettings.sun;
                var amb0 = RenderSettings.ambientSkyColor;
                var sun0 = sun != null ? sun.color : Color.white;
                foreach (var k in new[] { MapKind.Swamp, MapKind.Jungle, MapKind.Mars, home })
                {
                    Cfg.Map = k;
                    MapBuilder.Build();
                    yield return new WaitForSeconds(0.6f);
                }
                Check(ThemeMaps.LightingIsPristine() && RenderSettings.ambientSkyColor == amb0 && (sun == null || sun.color == sun0),
                    $"after Swamp, Jungle and Mars the light is exactly the game's own again (ambient {RenderSettings.ambientSkyColor} vs {amb0}, sun {(sun != null ? sun.color : Color.white)} vs {sun0})");
                yield return Snap("p14_light_after");
            }

            // ---- no crashed UFO: cover round the ball instead; no auto turret to craft
            Check(CrashSite.Current == null && MapBuilder.Root != null && MapBuilder.Root.Find("CentreCover") != null, "no crashed UFO in the middle, cover round the ball instead");
            Check(Cfg.CraftIndexOf(Item.AutoTurret) < 0, "the auto turret is out for now");
            yield return Snap("p14_centre");

            // ---- items
            Check(Cfg.CraftTier(Item.Chainsaw) == 2 && Cfg.CraftTier(Item.Saddle) == 2, "the chainsaw and saddle are Advanced Trade Station items");
            int ci = Cfg.RecipeIndex(Item.Chainsaw);
            Check(ci >= 0 && Cfg.GetRecipe(ci).Wood == 2000, $"the chainsaw costs 2000 wood ({(ci >= 0 ? Cfg.GetRecipe(ci).Wood : -1)})");
            Check(Cfg.MaxData(Item.Chainsaw) == 0, "the chainsaw has no durability bar");
            Check(Cfg.CraftTier(Item.HeavyRam) == 1 && Cfg.RecipeIndex(Item.HeavyRam) >= 0, "the High Strength Battering Ram is a Trade Station item");
            // ---- rams on a metal wall
            var front = Cfg.BaseCenter[team] - Cfg.BackDir(team) * (Cfg.BaseHalf + 8f);
            front.y = MapBuilder.Height(front.x, front.z);
            float yaw = Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y;
            var side = Vector3.Cross(Vector3.up, Cfg.BackDir(team));
            Structure Wall(Vector3 at, int tier)
            {
                var s = SpawnKeylessPiece(PieceType.Wall, (team + 1) % Mathf.Max(2, Cfg.TeamCount), at, Quaternion.Euler(0, yaw, 0));
                s.ServerUpgrade(tier);
                return s;
            }
            var w1 = Wall(front + side * 4f, 2);
            var w2 = Wall(front - side * 4f, 2);
            yield return new WaitForSeconds(0.4f);
            IEnumerator Slam(Item ram, Structure w)
            {
                me.ServerGive(ram, 1, Cfg.MaxData(ram));
                yield return Hold(me, ram);
                var face = w.transform.position + Vector3.up * 1.3f - Cfg.BackDir(team) * 0.1f;
                pc.LocalTeleport(face - Cfg.BackDir(team) * 1.6f - Vector3.up * 1.3f, yaw);
                yield return new WaitForSeconds(0.5f);
                me.RamStrikeRpc(w.NetworkObject, face);
                yield return new WaitForSeconds(0.5f);
            }
            yield return Slam(Item.Ram, w1);
            Check(w1 != null && w1.IsSpawned && w1.Tier.Value == 1, "a normal ram knocks a metal wall down a step");
            yield return new WaitForSeconds(Cfg.RamWindup);
            yield return Slam(Item.HeavyRam, w2);
            Check(w2 == null || !w2.IsSpawned, "the High Strength ram breaks a metal wall in one slam");

            // ---- a rocket on an armoured wall
            var w3 = Wall(front, 3);
            yield return new WaitForSeconds(0.4f);
            g.ServerRocket(w3.transform.position + Vector3.up * 1.2f - Cfg.BackDir(team) * 0.25f, me);
            yield return new WaitForSeconds(0.5f);
            Check(w3 == null || !w3.IsSpawned, "one rocket breaks an armoured wall");

            // ---- the ladder: no wall, no ladder; it breaks with its wall and drops nothing
            var lp = front + side * 9f;
            lp.y = MapBuilder.Height(lp.x, lp.z);
            Check(PlayerNet.DeployProblem(Item.Ladder, team, lp, yaw) != null, "a ladder can't go on bare ground");
            var lw = SpawnKeylessPiece(PieceType.Wall, team, lp + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.45f, Quaternion.Euler(0, yaw, 0));
            yield return new WaitForSeconds(0.4f);
            Check(PlayerNet.DeployProblem(Item.Ladder, team, lp, yaw) == null, "a ladder goes against a wall");
            me.ServerGive(Item.Ladder, 1);
            yield return Hold(me, Item.Ladder);
            pc.LocalTeleport(lp - Quaternion.Euler(0, yaw, 0) * Vector3.forward * 2f, yaw);
            yield return new WaitForSeconds(0.4f);
            me.PlaceDeployableRpc((byte)Item.Ladder, lp, yaw);
            yield return new WaitForSeconds(0.6f);
            Container lad = null;
            foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Kind.Value == Container.Ladder && Vector3.Distance(c.transform.position, lp) < 1f) lad = c;
            Check(lad != null, "the ladder is up against the wall");
            yield return Snap("p14_ladder");
            int items0 = g.Items.Count;
            lw.ServerDamage(lw.Health.Value + 1f);
            yield return new WaitForSeconds(1.2f);
            Check(lad == null || !lad.IsSpawned, "the ladder breaks with its wall");
            Check(g.Items.Count <= items0, "...and drops nothing");

            // ---- a ladder can't go right up at the top of a large wall (the spikes)
            {
                var bp = front - side * 10f;
                bp.y = MapBuilder.Height(bp.x, bp.z);
                var big = Instantiate(Bootstrap.I.structurePrefab, bp, Quaternion.Euler(0, yaw, 0)).GetComponent<Structure>();
                big.ServerInit(PieceType.Barrier, team, default, false);
                big.NetworkObject.Spawn(true);
                yield return new WaitForSeconds(0.4f);
                var foot = bp - Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.45f;
                float maxFoot = Deployables.LadderMaxFoot(big);
                var high = foot; high.y = maxFoot + 0.6f;
                var ok = foot; ok.y = maxFoot - 0.1f;
                Check(PlayerNet.DeployProblem(Item.Ladder, team, high, yaw) == "Too high - the spikes are in the way" && PlayerNet.DeployProblem(Item.Ladder, team, ok, yaw) == null,
                    $"a ladder can't be put up at the very top of a large wall (max foot {maxFoot - bp.y:0.0} m up)");
            }

            // ---- the turret: any weapon, arrows only
            me.ServerGive(Item.AutoTurret, 1);
            var tp = Cfg.BaseCenter[team] - Cfg.BackDir(team) * 9f + side * 6f;
            tp.y = MapBuilder.Height(tp.x, tp.z);
            yield return Hold(me, Item.AutoTurret);
            pc.LocalTeleport(tp - Cfg.BackDir(team) * -2f, yaw);
            yield return new WaitForSeconds(0.4f);
            me.PlaceDeployableRpc((byte)Item.AutoTurret, tp, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.6f);
            Container tur = null;
            foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Kind.Value == Container.Turret && c.Team.Value == team) tur = c;
            Check(tur != null && tur.Slots.Count == 2, "an auto turret with two slots (a weapon, arrows)");
            if (tur != null)
            {
                pc.LootTarget = tur;
                pc.MenuOpen = true;
                yield return new WaitForSeconds(0.3f);
                yield return Snap("p14_turret_empty");
                Check(Deployables.TurretWeapon(Item.Sword) && Deployables.TurretWeapon(Item.Hatchet) && Deployables.TurretWeapon(Item.Pistol) && !Deployables.TurretWeapon(Item.Wood), "any weapon goes in a turret (not wood)");
                me.ServerGive(Item.Sword, 1);
                me.ServerGive(Item.Arrow, 20);
                me.ServerGive(Item.PistolAmmo, 10);
                yield return new WaitForSeconds(0.3f);
                int Slot(Item id) { for (int i = 0; i < me.Inv.Count; i++) if (me.Inv[i].Id == id) return i; return -1; }
                me.MoveItemRpc(0, (byte)Slot(Item.PistolAmmo), 1, 1, 10, tur.NetworkObject);
                yield return new WaitForSeconds(0.3f);
                Check(tur.Slots[1].Empty, "pistol ammo doesn't go in the arrow slot");
                me.MoveItemRpc(0, (byte)Slot(Item.Sword), 1, 0, 1, tur.NetworkObject);
                me.MoveItemRpc(0, (byte)Slot(Item.Arrow), 1, 1, 20, tur.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                Check(tur.Slots[0].Id == Item.Sword && tur.Slots[1].Id == Item.Arrow, "a sword and arrows go in");
                yield return Snap("p14_turret_armed");
                pc.CloseMenu();
            }
            Log("prompt14 done");
            Application.Quit(0);
        }

        IEnumerator JonahRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            Check(Cfg.Jonah && Cfg.RulesOf(Cfg.MapKey) == GameRules.JonahTest, "Jonah Ideas Test mode survives the map key");
            Check(Cfg.RulesOf(Cfg.WithRules(Cfg.MapKey, GameRules.Domination)) == GameRules.Domination, "...and the other modes still read back");
            var wb = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Workbench));
            var hatchet = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Hatchet));
            Check(wb.Wood > 0 && wb.Dust == 0 && hatchet.Wood > 0 && Cfg.RecipeIndex(Item.AlienDust) < 0 && Cfg.BuildItem == Item.Wood, "no alien dust any more: everything costs wood (the Trade Station, the hatchet, building)");
            me.ServerGive(Item.Wood, 1000); me.ServerGive(Item.Wood, 1000); me.ServerGive(Item.Wood, 1000); // (Upgrade 10 Walls: 2000)
            var st = Cfg.UpgradeStationPos(team);
            pc.LocalTeleport(st - Cfg.BackDir(team) * -2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y + 180f);
            yield return new WaitForSeconds(0.4f);
            pc.OpenUpgrades();
            yield return new WaitForSeconds(0.4f);
            yield return Snap("p14_jonah_station");
            pc.CloseMenu();
            // Upgrade 10 Walls: twelve walls, pick ten in the void
            var front = Cfg.BaseCenter[team] - Cfg.BackDir(team) * 6f;
            var side = Vector3.Cross(Vector3.up, Cfg.BackDir(team));
            var walls = new List<Structure>();
            for (int i = 0; i < 12; i++)
            {
                var at = front + side * (i - 6) * 3f;
                at.y = MapBuilder.Height(at.x, at.z);
                walls.Add(SpawnKeylessPiece(PieceType.Wall, team, at, Quaternion.LookRotation(-Cfg.BackDir(team))));
            }
                        yield return new WaitForSeconds(0.5f);
            pc.LocalTeleport(st - Cfg.BackDir(team) * -2.2f + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(0.3f);
            pc.SetLook(37f, 12f);
            yield return null;
            var at0 = me.transform.position;
            WallPicker.Begin();
            yield return new WaitForSeconds(0.6f);
            Check(WallPicker.Active, "Upgrade 10 Walls opens the wall picker");
            yield return Snap("p14_jonah_void");
            int dustBefore = me.Count(Item.Wood);
            var ids = new List<ulong>();
            for (int i = 0; i < 10; i++) ids.Add(walls[i].NetworkObjectId);
            WallPicker.TestPick(ids);
            yield return new WaitForSeconds(0.8f);
            Check(!WallPicker.Active, "the 10th pick closes the picker");
            Check(Vector3.Distance(me.transform.position, at0) < 0.3f && Mathf.Abs(Mathf.DeltaAngle(pc.LookYaw, 37f)) < 1f && Mathf.Abs(pc.LookPitch - 12f) < 1f, "...back exactly where we were, looking the same way");
            int up = 0;
            for (int i = 0; i < 12; i++) if (walls[i] != null && walls[i].Tier.Value == 1) up++;
            Check(up == 10 && walls[10].Tier.Value == 0, $"ten walls went up a tier ({up}), the others didn't");
            Check(dustBefore - me.Count(Item.Wood) == Cfg.Upgrade10WallsDust, $"for {Cfg.Upgrade10WallsDust} wood");
            yield return Snap("p14_jonah_after");
        }
    }

    public partial class WallPicker
    {
        /// <summary>AutoTest: pick these pieces as if clicked (the last pick finishes).</summary>
        public static void TestPick(List<ulong> ids)
        {
            if (s_I == null) return;
            foreach (var p in s_I.m_Picks)
                if (p.S != null && ids.Contains(p.S.NetworkObjectId) && !p.Sel)
                {
                    p.Sel = true;
                    s_I.Paint(p);
                    if (s_I.Selected >= s_I.m_Need) { s_I.Finish(); return; }
                }
        }
    }
}
