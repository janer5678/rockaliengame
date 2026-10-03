using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Headless end-to-end test driver. Inactive unless launched with "-autotest ball", "-autotest sd" or "-autotest shots".
    /// Drives real RPCs through the network: gather -> craft -> build -> chest/bag/berries/barrier -> (ball capture | sudden death kill).
    /// </summary>
    public partial class AutoTest : MonoBehaviour
    {
        string m_Mode;

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autotest") m_Mode = args[i + 1];
            if (m_Mode == null) { enabled = false; return; }
            // test runs make no sound
            GameSettings.Muted = true;
            GameSettings.Apply();
            if (m_Mode == "menushot") { StartCoroutine(MenuShots()); return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[AUTOTEST] " + s);

        /// <summary>Screenshots of the main menu, mode options, CHANGE VALUES and every settings tab; checks the settings export.</summary>
        IEnumerator MenuShots()
        {
            string dir = "shots";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.8f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForEndOfFrame();
                yield return null;
            }
            yield return new WaitForSeconds(2.2f);
            yield return Shot("menu_main");
            // the game modes: Tutorial, Classic (the Auto Wood rules) and Primitive (the original game) on the menu, the rest folded away
            Check(Cfg.MainRules.Length == 3 && Cfg.RulesName(Cfg.MainRules[0]) == "Tutorial" && Cfg.RulesName(Cfg.MainRules[1]) == "Classic" && Cfg.MainRules[1] == GameRules.AutoWood
                && Cfg.RulesName(Cfg.MainRules[2]) == "Primitive" && Cfg.MainRules[2] == GameRules.Classic && Cfg.RulesName(GameRules.Primitive) == "Primitive Limited"
                && Cfg.MainRules.Length + Cfg.MoreRules.Length == (int)GameRules.Dna + 1, "game modes: Tutorial, Classic (auto wood), Primitive (the original) on the menu, the other 8 under More modes");
            Check(Cfg.RulesId(GameRules.Classic) == "classic" && Cfg.RulesId(GameRules.AutoWood) == "autowood", "the -rules names are unchanged (classic = the original game, autowood = the menu's Classic)");
            Check(!GameSettings.ShowGraphicsPicker && GameSettings.GraphicsMode == 0, "graphics: just Normal (no PSX / AI PSX picker)");
            Hud.ShowMore(true, true);
            yield return Shot("menu_main_more_modes_and_maps");
            Hud.ShowMore(false, false);
            Hud.OpenModeOptions = true;
            yield return Shot("menu_mode_options");
            // the airdrop chances on the mode options screen add up to 100% (each item's rarity)
            {
                float sum = 0f;
                foreach (var it in Cfg.AirdropLoot) sum += Cfg.AirdropChance(it, Cfg.AirdropLoot);
                int was = Cfg.RarityC4;
                Cfg.SetAirdropRarity(Item.C4, was + 15);
                bool set = Cfg.RarityC4 == was + 15;
                Cfg.SetAirdropRarity(Item.C4, was);
                Check(Mathf.Abs(sum - 1f) < 0.01f && set, $"mode options: the airdrop chances add up to {sum * 100f:0.#}%, and - / + changes an item's rarity ({set})");
            }
            Hud.OpenValues = true;
            yield return Shot("menu_values");
            yield return ValuesSearchShots(Shot);
            for (int tab = 0; tab < 4; tab++)
            {
                Hud.OpenSettingsTab = tab;
                yield return Shot("menu_settings_" + tab);
                if (tab == 2)
                    Check(Hud.SettingsPanel.width <= Screen.width * 0.55f, $"Settings > Display is a side panel with the game beside it ({Hud.SettingsPanel.width:0} of {Screen.width} px)");
            }
            // Settings > Display > INTERFACE: another font and a bigger UI (not saved), then back
            {
                int font = GameSettings.UiFont, accent = GameSettings.UiAccent;
                float scale = GameSettings.UiScale, hud = GameSettings.HudOpacity;
                int pick = System.Array.IndexOf(GameSettings.FontChoices, "Bahnschrift");
                if (!GameSettings.FontInstalled(pick)) pick = System.Array.IndexOf(GameSettings.FontChoices, "Consolas");
                Hud.OpenSettingsTab = 2;
                GameSettings.SetInterface(pick, 1.2f, hud, 1, false);
                yield return Shot("menu_settings_display_font");
                Check(GameSettings.FontInstalled(pick) && UiLook.CurrentFontName.Contains(GameSettings.FontChoices[pick].Split(' ')[0]), $"the UI font changes ({UiLook.CurrentFontName})");
                Hud.OpenSettingsTab = 0;
                yield return Shot("menu_settings_sound_font");
                GameSettings.SetInterface(font, scale, hud, accent, false);
                yield return new WaitForSeconds(0.3f);
            }
            // CHANGE VALUES > Export writes every value to a file next to the game; Import reads them back
            float before = Cfg.WalkSpeed;
            Cfg.WalkSpeed = 6.5f;
            var path = SettingsFile.Export();
            Check(path != null && System.IO.File.Exists(path) && System.IO.File.ReadAllText(path).Contains("WalkSpeed = 6.5    # CHANGED (default 5)"), $"settings exported to {path}");
            Cfg.WalkSpeed = before;
            int n = SettingsFile.Import(out var err);
            Check(err == null && n == Cfg.TuneFields.Count && Mathf.Approximately(Cfg.WalkSpeed, 6.5f), $"settings imported back ({n} values, walk speed {Cfg.WalkSpeed})");
            Cfg.WalkSpeed = before;
            Cfg.SavePrefs();
            System.IO.File.Delete(path);
            Log("menu shots done");
            Application.Quit(0);
        }

        IEnumerator Run()
        {
            float timeout = Time.time + 60f;
            while (PlayerNet.Local == null || PlayerController.Local == null)
            {
                if (Time.time > timeout) { Log("FAIL: local player never spawned"); Application.Quit(2); yield break; }
                yield return null;
            }
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            var nm = NetworkManager.Singleton;
            yield return new WaitForSeconds(0.5f);
            StartCoroutine(Watch());
            // sd: a grave from before the client joins (late joiners must get it)
            if (m_Mode == "sd" && nm.IsServer && NetGame.Instance.S == GameState.Waiting)
                NetGame.Instance.ServerAddGrave(me.transform.position + Vector3.right * 2f, 0f, me.Team.Value);
            Log($"local player spawned: team={Cfg.TeamName[me.Team.Value]} host={nm.IsHost} mode={Cfg.ModeLabel} nodes={FindObjectsByType<ResourceNode>(FindObjectsSortMode.None).Length}");
            if (NetGame.Instance.S == GameState.Waiting)
                Check(Vector3.Distance(me.transform.position, Cfg.ArenaCenter) < 30f && me.HeldItem == Item.Rock, "waiting for players in the stadium with a rock");
            if (m_Mode == "arena") { yield return ArenaRoutine(me, pc); yield break; }
            while (NetGame.Instance.S == GameState.Waiting) yield return null;
            if ((m_Mode == "dome" || m_Mode == "scenery") && nm.IsServer) NetGame.Instance.TimerPaused.Value = true; // (it needs the glass wall up for a while)
            yield return new WaitForSeconds(0.8f);
            if (m_Mode == "teams") { yield return TeamsRoutine(me); yield break; }
            if (m_Mode == "batch5") { yield return Batch5Routine(me, pc); yield break; }
            if (m_Mode == "modes") { yield return ModesRoutine(me, pc); yield break; }
            if (m_Mode == "tracer") { yield return TracerRoutine(me, pc); yield break; }
            if (m_Mode == "craftui") { yield return CraftUiRoutine(me, pc); yield break; }
            if (m_Mode == "upgrades") { yield return UpgradesRoutine(me, pc); yield break; }
            if (m_Mode == "maps") { yield return MapsRoutine(me, pc); yield break; }
            if (m_Mode == "victory") { yield return VictoryRoutine(me, pc); yield break; }
            Check(Cfg.BaseTeamAt(me.transform.position) == me.Team.Value, $"spawned inside own base ({me.transform.position})");
            Check(me.Count(Item.Rock) == 0 && me.HeldItem == Item.Rock, "empty hand = holding the rock (no rock item)");
            Check(Vector3.Distance(me.transform.position, Cfg.SpawnPos(me.Team.Value, me.Slot.Value)) < 1.5f, $"sent home to the bedrock when the match started on {Cfg.MapLabel} (seed {Cfg.MapSeed})");
            Check(NetGame.Instance != null && NetGame.Instance.MapKey.Value == Cfg.MapKey && NetGame.Instance.MapSeed.Value == Cfg.MapSeed, "map + seed synced from the host");
            var bc = Cfg.BedrockCenter(me.Team.Value);
            var rockCell = new PieceKey(PieceKey.KFoundation, BuildGrid.CellOf(bc.x + 0.1f), BuildGrid.CellOf(bc.z + 0.1f), 0, 0);
            Check(BuildGrid.OnBedrock(rockCell), "can't build a foundation on the bedrock");
            Check(BuildGrid.IsSupported(new PieceKey(PieceKey.KEdge, rockCell.I, rockCell.J, 0, 1), k => false), "walls stand on the bedrock without a foundation");
            if (m_Mode == "rig") { yield return RigShots(me, pc); yield break; }
            if (m_Mode == "psxmodels") { yield return PsxModelShots(me, pc); yield break; }
            if (m_Mode == "hands") { yield return HandsShots(me, pc); yield break; }
            if (m_Mode == "psx") { yield return PsxShots(me, pc); yield break; }
            if (m_Mode == "grass") { yield return GrassShots(me, pc); yield break; }
            if (m_Mode == "world") { yield return WorldShots(me, pc); yield break; }
            if (m_Mode == "ui") { yield return UiShots(me, pc); yield break; }
            if (m_Mode == "dome") { yield return DomeRoutine(me, pc); yield break; }
            if (m_Mode == "scenery") { yield return SceneryRoutine(me, pc); yield break; }
            if (m_Mode == "aipsx") { yield return AiPsxShots(me, pc); yield break; }
            if (m_Mode == "outline") { yield return OutlineShots(me, pc); yield break; }
            if (m_Mode == "shots") yield return ShotsRoutine(me, pc);
            else if (nm.IsHost) yield return HostRoutine(me, pc);
            else yield return ClientRoutine(me, pc);
        }

        /// <summary>2v2 / free for all: every player checks where they ended up; the host checks the teams and quits the match.</summary>
        IEnumerator TeamsRoutine(PlayerNet me)
        {
            Check(Cfg.BaseTeamAt(me.transform.position) == me.Team.Value && Vector3.Distance(me.transform.position, Cfg.SpawnPos(me.Team.Value, me.Slot.Value)) < 1.5f,
                  $"{Cfg.TeamName[me.Team.Value]} (slot {me.Slot.Value}) sent home to its own base in {Cfg.ModeLabel} (at {me.transform.position}, spawn {Cfg.SpawnPos(me.Team.Value, me.Slot.Value)})");
            Check(Machine.ByTeam[me.Team.Value] != null, "our base has its machine");
            yield return new WaitForSeconds(3f);
            if (NetworkManager.Singleton.IsHost)
            {
                var perTeam = new int[4];
                foreach (var p in PlayerNet.All) perTeam[p.Team.Value]++;
                bool ok = true;
                for (int t = 0; t < Cfg.TeamCount; t++) ok &= perTeam[t] == Cfg.PlayersNeeded / Cfg.TeamCount;
                Check(ok && PlayerNet.All.Count == Cfg.PlayersNeeded, $"{Cfg.ModeLabel}: {PlayerNet.All.Count} players split {perTeam[0]}/{perTeam[1]}/{perTeam[2]}/{perTeam[3]} over {Cfg.TeamCount} teams");
                int bases = 0;
                for (int t = 0; t < 4; t++) if (Machine.ByTeam[t] != null) bases++;
                Check(bases == Cfg.TeamCount, $"{bases} bases built");
                // the wild respawn goes into an enemy's side
                me.ServerRespawn(true);
                yield return new WaitForSeconds(0.8f);
                int region = Cfg.RegionOf(me.transform.position);
                Check(Cfg.BaseTeamAt(me.transform.position) < 0, $"wild respawn landed out in the wild ({Cfg.TeamName[region]}'s side)");
                // sudden death is last team standing
                NetGame.Instance.DevStartSuddenDeath();
                yield return new WaitForSeconds(1f);
                foreach (var p in PlayerNet.All) if (p.Team.Value != me.Team.Value) p.ServerKill(me);
                yield return new WaitForSeconds(1f);
                Check(NetGame.Instance.S == GameState.GameOver && NetGame.Instance.Winner.Value == me.Team.Value, $"last team standing won ({NetGame.Instance.EndReason.Value})");
            }
            else
            {
                while (NetGame.Instance != null && NetGame.Instance.S != GameState.GameOver) yield return null;
            }
        }

        /// <summary>
        /// Solo host, fast timers: crafting anywhere vs in base, armour going straight on, the chainsaw recipe, slow berries,
        /// horses bleeding and fleeing, the hidden stadium, and the airdrop schedule (split while the wall is up).
        /// </summary>
        IEnumerator Batch5Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            // mode options: 3 airdrops after the wall drops (at 22.5 s, 45 s, 67.5 s of the 90 s fast ball phase), C4 only,
            // the first one "anywhere" (whatever was last picked in the menu on this PC)
            Cfg.AirdropCount = 3;
            Cfg.AirdropItemMask = 1;
            Cfg.AirdropSides = Cfg.AirdropCenter = false;
            int team = me.Team.Value;
            bool onGrid = true;
            for (int t = 0; t < Cfg.TeamCount; t++)
                onGrid &= Mathf.Abs(Mathf.Repeat(Cfg.BaseCenter[t].x + 0.001f, Cfg.Cell)) < 0.01f && Mathf.Abs(Mathf.Repeat(Cfg.BaseCenter[t].z + 0.001f, Cfg.Cell)) < 0.01f;
            Check(onGrid, $"bases sit on the 3 m building grid ({Cfg.BaseCenter[team]})");
            Check(Cfg.RecipeIndex(Item.BuildingPlan) == Cfg.RecipeIndex(Item.Spear) + 1 && Cfg.GetRecipe(Cfg.RecipeIndex(Item.Hatchet)).Wood == 50 && Cfg.GetRecipe(Cfg.RecipeIndex(Item.Hatchet)).Stone == 0,
                  "building plan right below the spear; hatchet costs 50 wood");
            Log($"map {Cfg.MapLabel}: half {Cfg.MapHalf}, base at {Cfg.BaseCenter[team]}, {FindObjectsByType<ResourceNode>(FindObjectsSortMode.None).Length} nodes, {Vehicle.All.Count} horses");
            Check(Cfg.HeadshotMul == 2f && Cfg.MatchLength == 900f && Cfg.BerryHeal == 25f, "headshots x2, 15 minutes after the wall drops, berries heal 25");
            if (Camera.main != null && Stadium.Instance != null)
            {
                yield return null;
                var tr = Stadium.Instance.GetComponentInChildren<TextMesh>().GetComponent<Renderer>();
                Check(!tr.enabled, "the far-away stadium isn't drawn from the map");
            }

            // crafting: spears and hatchets anywhere, the rest only in base
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 200);
            var outside = Cfg.BaseCenter[team] - Cfg.BackDir(team) * (Cfg.BaseHalf + 10f);
            outside.y = MapBuilder.Height(outside.x, outside.z) + 0.2f;
            pc.LocalTeleport(outside, 0f);
            yield return new WaitForSeconds(0.4f);
            foreach (var it in new[] { Item.Spear, Item.Hatchet, Item.Bow, Item.Armor })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.3f);
            }
            Check(me.Count(Item.Spear) == 1 && me.Count(Item.Hatchet) == 1 && me.Count(Item.Bow) == 0 && me.ArmorHp.Value == 0, "outside the base: spear and hatchet craft, bow and armour don't");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int wood = me.Count(Item.Wood);
            // armour and the chainsaw need a Workbench T1
            yield return BenchBuy(me, pc, Item.Armor);
            yield return BenchBuy(me, pc, Item.Chainsaw);
            Check(me.ArmorHp.Value == Cfg.ArmorHp && me.Count(Item.Armor) == 0, "armour from the workbench goes straight on");
            Check(me.Count(Item.Chainsaw) == 1 && me.Count(Item.Wood) == wood - Cfg.ArmorWood - Cfg.ChainsawWood, $"chainsaw made at the workbench for {Cfg.ChainsawWood} wood");

            // berries: one per eat time
            me.ServerGive(Item.Berry, 3);
            yield return Hold(me, Item.Berry);
            me.Health.Value = 40f;
            me.EatRpc();
            me.EatRpc();
            yield return new WaitForSeconds(0.3f);
            Check(me.Health.Value >= 64.9f && me.Health.Value < 67f, $"can't eat berries back to back (health {me.Health.Value:0})");
            yield return new WaitForSeconds(Cfg.BerryEatTime);
            me.EatRpc();
            yield return new WaitForSeconds(0.3f);
            Check(me.Health.Value >= 89.9f && me.Health.Value < 90f + Cfg.BaseRegen * (Cfg.BerryEatTime + 1f), $"ate again after {Cfg.BerryEatTime}s (health {me.Health.Value:0})");

            // horses: hit one, it bleeds and runs off
            Vehicle horse = null;
            foreach (var v in Vehicle.All) if (v.IsHorse && !v.HasDriver) { horse = v; break; }
            if (horse == null) Check(false, "there's a wild horse");
            else
            {
                var hp = horse.transform.position;
                var dir = (hp - Cfg.ArenaCenter).normalized;
                pc.LocalTeleport(hp + new Vector3(2f, 0.5f, 0f), 0f);
                yield return new WaitForSeconds(0.4f);
                float d0 = Vector3.Distance(horse.transform.position, me.transform.position);
                horse.ServerDamage(20f, me);
                yield return new WaitForSeconds(2f);
                float d1 = Vector3.Distance(horse.transform.position, me.transform.position);
                Check(Mathf.Approximately(horse.Hp.Value, Cfg.HorseHp - 20f) && horse.MaxHp == Cfg.HorseHp, $"horse has health ({horse.Hp.Value:0}/{horse.MaxHp:0})");
                Check(d1 > d0 + 8f, $"hurt horse fled ({d0:0.0} m -> {d1:0.0} m)");
            }
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));

            // tree camo: third person while you hold it, and you see the tree
            me.ServerGive(Item.TreeCamo, 1);
            yield return Hold(me, Item.TreeCamo);
            yield return new WaitForSeconds(1f);
            var cam = Camera.main;
            float camBack = cam != null ? Vector3.Distance(cam.transform.position, me.transform.position + Vector3.up * me.EyeHeight) : 0f;
            Check(pc.ThirdPerson && camBack > 2f && me.transform.Find("treeCamo") != null, $"holding the tree camo: third person ({camBack:0.0} m back) and you can see your tree");
            yield return Hold(me, Item.Spear);
            yield return new WaitForSeconds(1f);
            Check(!pc.ThirdPerson && me.transform.Find("treeCamo") == null, "put it away: back to first person");

            // airdrops only start after the wall drops, spread over the ball phase
            var drops = new System.Collections.Generic.List<Container>();
            int Crates() { drops.Clear(); foreach (var c in Container.All) if (c.IsAirdrop) drops.Add(c); return drops.Count; }
            while (g.S != GameState.BallLive) yield return null;
            float phase = Cfg.FastMatchLength;
            double t0 = g.PhaseEnd.Value - phase; // when the wall dropped
            while (g.NetworkManager.ServerTime.Time < t0 + phase / 4 - 1) yield return null;
            Check(Crates() == 0, $"no airdrops from the match start until a quarter of the way after the wall drops");
            while (g.NetworkManager.ServerTime.Time < t0 + phase / 4 + NetGame.DropLand + 1.5) yield return null;
            Check(Crates() == 1, $"first of 3 airdrops a quarter of the way after the wall drops; 'anywhere' drops just one ({drops.Count} crates)");
            bool c4 = drops.Count > 0;
            foreach (var c in drops) c4 &= c.Slots.Count > 0 && c.Slots[0].Id == Item.C4;
            Check(c4, "airdrops only have the picked items (C4 only)");
            // "one per side" for the second, "middle of the map" for the third
            Cfg.AirdropSides = true;
            while (g.NetworkManager.ServerTime.Time < t0 + phase / 2 + NetGame.DropLand + 1.5) yield return null;
            int n = Crates();
            var regions = new System.Collections.Generic.HashSet<int>();
            for (int i = 1; i < drops.Count; i++) regions.Add(Cfg.RegionOf(drops[i].transform.position));
            Check(n == 1 + Cfg.TeamCount && regions.Count == Cfg.TeamCount, $"second airdrop half way: one per side ({n} crates in all)");
            Cfg.AirdropCenter = true;
            while (g.NetworkManager.ServerTime.Time < t0 + phase * 3 / 4 + NetGame.DropLand + 1.5) yield return null;
            n = Crates();
            var last = drops[drops.Count - 1].transform.position;
            Check(n == 2 + Cfg.TeamCount && new Vector2(last.x, last.z).magnitude < 9f, $"third airdrop: in the middle of the map ({last}, {n} crates in all)");
            g.EndGame(team, "batch 5 test done");
        }

        IEnumerator Watch()
        {
            int last = -1;
            while (true)
            {
                var g = NetGame.Instance;
                if (g != null && g.State.Value != last)
                {
                    last = g.State.Value;
                    Log($"state -> {g.S} (timeLeft {g.TimeLeft:0.0}s, players {PlayerNet.All.Count})");
                    if (g.S == GameState.GameOver)
                    {
                        Log($"RESULT winner={(g.Winner.Value < 0 ? "DRAW" : Cfg.TeamName[g.Winner.Value])} reason=\"{g.EndReason.Value}\"");
                        if (m_Mode == "sd")
                        {
                            // every death left a grave (the sudden death kill in the arena, plus one made in the waiting stadium), the same on both screens; the client joined after the first
                            int inArena = 0;
                            foreach (var gr in g.Graves) if (Vector3.Distance(gr.Pos, Cfg.ArenaCenter) < 60f) inArena++;
                            yield return null;
                            Check(g.Graves.Count >= 2 && inArena >= 2 && GraveFx.Shown == g.Graves.Count,
                                $"graves synced ({(g.IsServer ? "host" : "client")}): {g.Graves.Count} graves, {inArena} in the arena, {GraveFx.Shown} drawn");
                        }
                        // won with the ball in the socket: the victory cutscene plays first, and only then the victory screen
                        if (g.CutsceneAt.Value >= 0)
                        {
                            if (m_Mode == "victory") yield break; // (VictoryRoutine checks it and quits)
                            float before = Hud.GameOverShownAt, start = Time.time;
                            bool early = false, ship = false;
                            int taken = 0;
                            while (VictoryCutscene.Active)
                            {
                                early |= Hud.GameOverShownAt != before;
                                ship |= VictoryCutscene.Ship != null;
                                taken = Mathf.Max(taken, VictoryCutscene.Taken);
                                yield return null;
                            }
                            float took = Time.time - start;
                            yield return new WaitForSeconds(1f);
                            bool hud = Hud.CutsceneShownAt >= 0f; // (no HUD at all when there are no graphics)
                            Check(ship && took > VictoryCutscene.Length - 1.5f && (!hud || (!early && Hud.GameOverShownAt > start + took - 0.3f)),
                                $"victory cutscene on the {(g.IsServer ? "host" : "client")}: the UFO came ({ship}), it ran {took:0.0} s, {g.CutsceneRiders.Count} beamed up ({taken} seen taken), then the victory screen{(hud ? "" : " (no HUD drawn)")}");
                        }
                        yield return new WaitForSeconds(3f);
                        Application.Quit(0);
                    }
                }
                yield return null;
            }
        }

        /// <summary>Put the item in hand, dragging it onto the last hotbar slot first if it's in the main inventory.</summary>
        static IEnumerator Hold(PlayerNet me, Item id)
        {
            if (id == Item.Rock)
            {
                // the rock = any empty hotbar slot
                for (int i = 0; i < Cfg.HotbarSize; i++) if (me.SlotAt(i).Empty) { me.HeldSlot.Value = (byte)i; break; }
                yield return new WaitForSeconds(0.3f);
                yield break;
            }
            int s = me.HotbarSlotOf(id);
            if (s < 0)
            {
                int from = -1;
                for (int i = Cfg.HotbarSize; i < Cfg.PlayerSlots && from < 0; i++) if (me.SlotAt(i).Id == id) from = i;
                if (from < 0) { Log($"FAIL: no {id} in the inventory"); yield break; }
                var displaced = me.SlotAt(Cfg.HotbarSize - 1);
                me.MoveItemRpc(0, (byte)from, 0, (byte)(Cfg.HotbarSize - 1), me.SlotAt(from).Count, default);
                yield return new WaitForSeconds(0.4f);
                s = me.HotbarSlotOf(id);
                if (s < 0 || me.SlotAt(from).Id != displaced.Id) { Log($"FAIL: dragging {id} onto the hotbar (swap) didn't work"); yield break; }
            }
            me.HeldSlot.Value = (byte)s;
            yield return new WaitForSeconds(0.4f);
        }

        static int RamHits(PlayerNet me)
        {
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Ram) return me.SlotAt(i).Data;
            return 0;
        }

        IEnumerator ClientRoutine(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];

            // ---- gather ----
            yield return Gather(me, pc, ResourceNode.Tree, 52, baseC);
            if (Cfg.WoodMode)
            {
                bool stone = false;
                foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Boulder) stone = true;
                Check(!stone && Cfg.RecipeIndex(Item.Pickaxe) < 0 && Cfg.GetRecipe(Cfg.RecipeIndex(Item.Bow)).Stone == 0, "wood mode: no stone nodes, no pickaxe, recipes cost wood only");
            }
            else yield return Gather(me, pc, ResourceNode.Boulder, 6, baseC);
            Log($"gathered wood={me.Count(Item.Wood)} stone={me.Count(Item.Stone)}");
            Check(me.Count(Item.Wood) >= 180, "gathered enough wood (as inventory items)");

            // ---- crafting only works at the machine ----
            pc.LocalTeleport(baseC + new Vector3(Cfg.BaseHalf + 6f, 0.1f, 0), 0);
            yield return new WaitForSeconds(0.3f);
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 0, "can't craft outside your base");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);

            // ---- craft building plan at the machine & build ----
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 1, "crafted building plan");
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0); // stand next to it (the UFO moves the free cells around)
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.3f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(1.3f);
            me.PlaceRpc((byte)PieceType.Wall, ci + 5, cj, 0, 1); // unsupported - must be rejected
            yield return new WaitForSeconds(1.3f);
            Check(CountStructures(PieceType.Foundation, team) == 1, "foundation built");
            Check(CountStructures(PieceType.Wall, team) == 1, "wall built on foundation, floating wall rejected");
            me.PlaceRpc((byte)PieceType.Foundation, 0, 0, 0, 0); // outside the base - must be rejected
            yield return new WaitForSeconds(1.3f);
            Check(CountStructures(PieceType.Foundation, team) == 1, "cannot build outside base");
            Log($"after building wood={me.Count(Item.Wood)}");

            // ---- the workbench from a client: locked until the ball's captured, then craftable standing on our foundation ----
            yield return ClientBenchCraft(me, pc, team, BuildGrid.CellCenter(ci, cj));

            if (m_Mode != "ball") yield break;

            // ---- ball capture ----
            while (Ball.Instance == null || NetGame.Instance.WallUp) yield return null; // (under the glass dome until the wall drops)
            yield return new WaitForSeconds(4f);
            var ball = Ball.Instance;
            Log($"ball landed at {ball.transform.position}");
            pc.LocalTeleport(new Vector3(ball.transform.position.x + 1.5f, 0.1f, ball.transform.position.z), 0);
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            Check(me.CarryingBall, "picked up ball");
            pc.LocalTeleport(baseC + new Vector3(6, 0.1f, 5), 0);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, Vector3.down, Vector3.zero);
            yield return new WaitForSeconds(3f);
            Check(!me.CarryingBall && ball.BaseTeam.Value == team && ball.SocketTeam.Value < 0, $"ball lying in own base does not count yet (BaseTeam={ball.BaseTeam.Value}, Socket={ball.SocketTeam.Value})");
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, 0.1f, 0), 270f);
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, (Cfg.SocketPos(team) - me.EyePos).normalized, Vector3.zero);
            yield return new WaitForSeconds(2f);
            Check(!me.CarryingBall && ball.SocketTeam.Value == team, $"threw the ball into own machine's socket and it snapped in (Socket={ball.SocketTeam.Value})");
        }

        IEnumerator HostRoutine(PlayerNet me, PlayerController pc)
        {
            if (m_Mode != "sd") yield break;
            yield return HostRaidTest(me, pc);
            // the countdown hits 0 (and stays there a moment) before everyone is sent to the arena
            while (NetGame.Instance != null && NetGame.Instance.S == GameState.BallLive && NetGame.Instance.TimeLeft > 0f) yield return null;
            if (NetGame.Instance != null && NetGame.Instance.S == GameState.BallLive)
            {
                float zeroAt = Time.time;
                yield return new WaitForSeconds(0.25f);
                bool stillZero = NetGame.Instance.S == GameState.BallLive && NetGame.Instance.TimeLeft <= 0f;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "sd_countdown_zero.png"));
                Log("shot sd_countdown_zero");
                while (NetGame.Instance.S == GameState.BallLive) yield return null;
                float held = Time.time - zeroAt;
                Check(stillZero && held > NetGame.ZeroHold - 0.4f && NetGame.Instance.S == GameState.SuddenDeath, $"the clock sits on 0 for {held:0.0}s before sudden death");
            }
            while (NetGame.Instance == null || NetGame.Instance.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "sd_ready.png"));
            Log("shot sd_ready " + Hud.FightWord(NetGame.Instance, out _));
            yield return new WaitForSeconds(1.5f);
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            Check(other != null, "opponent present in sudden death");
            Check(Mathf.Abs(me.transform.position.z - Cfg.ArenaCenter.z) < 30f, "host was teleported to the arena");
            Check(Mathf.Abs(other.transform.position.z - Cfg.ArenaCenter.z) < 30f, "client was teleported to the arena");
            Check(me.HeldItem == Item.Rock, "rock forced in sudden death");
            int stuff = 0;
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) stuff++;
            Check(stuff == 0 && me.ArmorHp.Value == 0 && me.HelmetHp.Value == 0, "inventory, armour and helmet cleared for sudden death");
            Check(NetGame.Instance.FightFrozen, "sudden death starts with the stadium countdown");
            other.ServerGive(Item.Wood, 50); // something to spill (the raid test may already have killed them once)
            int itemsBefore = NetGame.Instance.Items.Count;
            while (NetGame.Instance != null && NetGame.Instance.S == GameState.SuddenDeath)
            {
                var tp = other.transform.position;
                var dir = (me.transform.position - tp);
                dir.y = 0;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
                pc.LocalTeleport(tp + dir.normalized * 1.3f, Quaternion.LookRotation(-dir).eulerAngles.y);
                me.MeleeRpc(true, other.NetworkObject, tp + Vector3.up * 1.0f, false);
                yield return new WaitForSeconds(0.65f);
            }
            Check(NetGame.Instance.Items.Count > itemsBefore && other.Count(Item.Wood) == 0, $"killed player's items spilled out of the body ({NetGame.Instance.Items.Count - itemsBefore} piles)");
        }

        /// <summary>Host-only (the server may write its own inventory directly): crafting, building, chest, bag, berries, barrier, bow, spear, ram.</summary>
        IEnumerator HostRaidTest(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];
            yield return Hold(me, Item.Rock);
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 2000);
            yield return new WaitForSeconds(0.3f);
            Check(me.SlotAt(6).Id == Item.Wood && me.SlotAt(5).Id == Item.Wood && me.SlotAt(4).Id == Item.Wood && me.SlotAt(3).Id == Item.Stone && me.SlotAt(me.HeldSlot.Value).Empty,
                  "materials fill the hotbar from slot 7 backwards and keep clear of the rock slot");

            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            foreach (var it in new[] { Item.BuildingPlan, Item.Hatchet, Item.Spear, Item.Bow, Item.Arrow, Item.Ram, Item.Chest })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.3f);
            }
            yield return BenchBuy(me, pc, Item.Barrier); // (a Workbench T1 item)
            yield return BenchBuy(me, pc, Item.Pickaxe); // (a Workbench T2 item)
            Check(me.Count(Item.BuildingPlan) == 1 && me.Count(Item.Hatchet) == 1 && me.Count(Item.Pickaxe) == 1 && me.Count(Item.Spear) == 1 && me.Count(Item.Bow) == 1
                  && me.Count(Item.Arrow) == Cfg.ArrowsPerCraft && RamHits(me) == Cfg.RamUses && me.Count(Item.Chest) == 1 && me.Count(Item.Barrier) == 1,
                  "host crafted every item in base");
            {
                var sb = new System.Text.StringBuilder("inventory after crafting: ");
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) sb.Append($"[{i}]{me.SlotAt(i).Id}x{me.SlotAt(i).Count}/{me.SlotAt(i).Data} ");
                Log(sb.ToString());
            }
            Check(me.Count(Item.Wood) == 3000 - (Cfg.PlanWood + Cfg.HatchetWood + Cfg.PickaxeWood + Cfg.SpearWood + Cfg.BowWood + Cfg.ArrowWood + Cfg.RamWood + Cfg.ChestWood + Cfg.BarrierWood),
                  $"crafting consumed wood items (left {me.Count(Item.Wood)})");

            // build + upgrade + door
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0); // stand next to it (the UFO moves the free cells around)
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Doorway, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Floor, ci, cj, 1, 0);
            yield return new WaitForSeconds(1.2f);
            Structure wall = null, door = null, floor = null;
            foreach (var s in Structure.All)
            {
                if (s.Team.Value != team) continue;
                if (s.PType == PieceType.Wall) wall = s;
                if (s.PType == PieceType.Doorway) door = s;
                if (s.PType == PieceType.Floor) floor = s;
            }
            Check(wall != null && door != null && floor != null, "host built foundation, wall, doorway and floor");
            if (wall != null)
            {
                me.UpgradeRpc(wall.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(wall.Tier.Value == 1 && Mathf.Approximately(wall.Health.Value, Cfg.PieceHp(PieceType.Wall, 1)), $"wall upgraded to stone (tier {wall.Tier.Value}, hp {wall.Health.Value}, held {me.HeldItem}, stone {me.Count(Item.Stone)})");
            }
            if (door != null)
            {
                pc.LocalTeleport(door.transform.position + new Vector3(2f, -0.9f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.ToggleDoorRpc(door.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(door.DoorOpen.Value, "door opens for owner");
            }

            // chest: place in base, store stuff, take it back
            FreeCell(team, 1, out int chI, out int chJ);
            var chestPos = BuildGrid.CellCenter(chI, chJ);
            pc.LocalTeleport(chestPos + new Vector3(0, 0.1f, -2.5f), 0);
            yield return Hold(me, Item.Chest);
            me.PlaceDeployableRpc((byte)Item.Chest, chestPos, 180f);
            yield return new WaitForSeconds(0.6f);
            Container chest = null;
            foreach (var c in Container.All) if (c.Breakable) chest = c;
            Check(chest != null && chest.Slots.Count == Cfg.ChestSlots && me.Count(Item.Chest) == 0, "chest placed in base");
            if (chest != null)
            {
                int stoneSlot = -1;
                for (int i = 0; i < Cfg.PlayerSlots && stoneSlot < 0; i++) if (me.SlotAt(i).Id == Item.Stone) stoneSlot = i;
                int before = me.Count(Item.Stone);
                me.MoveItemRpc(0, (byte)stoneSlot, 1, 3, 250, chest.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                Check(chest.Slots[3].Id == Item.Stone && chest.Slots[3].Count == 250 && me.Count(Item.Stone) == before - 250, "dragged 250 stone into the chest (split stack)");
                me.MoveItemRpc(1, 3, 0, 255, 0, chest.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                Check(chest.Slots[3].Empty && me.Count(Item.Stone) == before, "shift-click took it back out");
            }
            var farPos = new Vector3(0, 0.1f, 0);
            Check(PlayerNet.DeployProblem(Item.Chest, team, Cfg.BaseCenter[1 - team], 0) != null && PlayerNet.DeployProblem(Item.Chest, team, farPos, 0) != null, "chests only allowed in own base");
            var onRock = Cfg.BedrockCenter(team) + new Vector3(2.3f, Cfg.BaseY, 0);
            Check(PlayerNet.DeployProblem(Item.Chest, team, onRock, 0) == null && PlayerNet.DeployProblem(Item.Chest, team, Cfg.SpawnPos(team), 0) != null, "chests can go on the bedrock by the machine (not on the spawn spot)");

            // high external wall (the barrier) out in the field: find a spot with room for it
            Vector3 FreeWallSpot(System.Func<Vector3, bool> where)
            {
                for (int tries = 0; tries < 400; tries++)
                {
                    var p = new Vector3(Random.Range(-Cfg.MapHalf + 10f, Cfg.MapHalf - 10f), 0f, Random.Range(-Cfg.MapHalf + 10f, Cfg.MapHalf - 10f));
                    p.y = MapBuilder.Height(p.x, p.z);
                    if (where(p) && PlayerNet.DeployProblem(Item.Barrier, team, p, 0f) == null) return p;
                }
                return new Vector3(20f, 0f, -30f);
            }
            var barrierPos = FreeWallSpot(p => Cfg.BaseTeamAt(p) < 0 && Mathf.Abs(p.z) > 6f);
            pc.LocalTeleport(barrierPos + new Vector3(0, 0.1f, -3f), 0);
            yield return Hold(me, Item.Barrier);
            me.PlaceDeployableRpc((byte)Item.Barrier, barrierPos, 0f);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.Barrier, team) == 1 && me.Count(Item.Barrier) == 0, "high external wall placed out on the map");
            // ...and one inside our own base
            me.ServerGive(Item.Barrier, 1);
            yield return new WaitForSeconds(0.2f);
            var inBase = FreeWallSpot(p => Cfg.BaseTeamAt(p) == team);
            pc.LocalTeleport(inBase + new Vector3(0, 0.1f, -3f), 0);
            yield return Hold(me, Item.Barrier);
            me.PlaceDeployableRpc((byte)Item.Barrier, inBase, 0f);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.Barrier, team) == 2 && me.Count(Item.Barrier) == 0, $"high external wall placed inside our own base ({inBase})");

            // berries: pick and eat
            ResourceNode bush = null;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.IsBush && (bush == null || (n.transform.position - me.transform.position).sqrMagnitude < (bush.transform.position - me.transform.position).sqrMagnitude)) bush = n;
            Check(bush != null, "berry bushes spawned");
            if (bush != null)
            {
                pc.LocalTeleport(bush.transform.position + new Vector3(1.5f, 0.1f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.PickBerriesRpc(bush.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Berry) == 1 && !bush.IsSpawned, "picked the whole berry bush (it's gone)");
                me.Health.Value = 50f;
                yield return Hold(me, Item.Berry);
                me.EatRpc();
                yield return new WaitForSeconds(0.5f);
                Check(Mathf.Approximately(me.Health.Value, 50f + Cfg.BerryHeal) && me.Count(Item.Berry) == 0, $"ate the berries to heal {Cfg.BerryHeal:0}");
                me.Health.Value = Cfg.MaxHealth;
            }

            // bow hit on the opponent
            PlayerNet other = null;
            while (other == null)
            {
                foreach (var p in PlayerNet.All) if (p != me) other = p;
                yield return null;
            }
            me.ServerGive(Item.Arrow, 10);
            int arrowsBefore = me.Count(Item.Arrow) + 1;
            yield return Hold(me, Item.Bow);
            float hpBefore = other.Health.Value;
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(other.Health.Value < hpBefore - 40f && me.Count(Item.Arrow) == arrowsBefore - 2, $"arrow hit opponent (hp {hpBefore:0} -> {other.Health.Value:0})");
            other.Health.Value = 500f; // padded so the headshot can't kill the client mid-routine
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Abs(other.Health.Value - (500f - Cfg.ArrowPlayerDamage * Cfg.HeadshotMul)) < 0.5f, $"headshot does x{Cfg.HeadshotMul} (500 -> {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;

            // thrown spear sticks in the opponent, then gets pulled out
            yield return Hold(me, Item.Spear);
            hpBefore = other.Health.Value;
            me.ThrowSpearRpc(me.EyePos, Vector3.forward * Cfg.SpearThrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.SpearLandRpc(true, other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Spear) == 0 && other.StuckSpears.Value == 1 && other.Health.Value < hpBefore, $"thrown spear stuck in opponent (hp {hpBefore:0} -> {other.Health.Value:0})");
            pc.LocalTeleport(other.transform.position + new Vector3(1.5f, 0, 0), 270f);
            yield return new WaitForSeconds(0.4f);
            me.PullSpearRpc(other.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Spear) == 1 && other.StuckSpears.Value == 0, "pulled the spear out of the opponent");
            other.Health.Value = Cfg.MaxHealth;
            yield return Hold(me, Item.Spear);
            me.ThrowSpearRpc(me.EyePos, Vector3.forward * Cfg.SpearThrowSpeed);
            yield return new WaitForSeconds(0.1f);
            var landAt = me.transform.position + new Vector3(0, 0.05f, 4f);
            me.SpearLandRpc(false, default, landAt, Vector3.down);
            yield return new WaitForSeconds(0.5f);
            int spearItem = FindWorldItem(Item.Spear);
            Check(spearItem >= 0 && me.Count(Item.Spear) == 0, "thrown spear landed on the ground as a world item");
            if (spearItem >= 0)
            {
                pc.LocalTeleport(landAt + new Vector3(0, 0.05f, -1f), 0);
                yield return new WaitForSeconds(0.4f);
                me.PickupItemRpc(spearItem);
                yield return new WaitForSeconds(0.5f);
                Check(FindWorldItem(Item.Spear) < 0 && me.Count(Item.Spear) == 1, "picked the spear back up (E)");
            }

            // drag an item out of the inventory onto the ground, then pick it back up
            int woodSlot = -1;
            for (int i = 0; i < Cfg.PlayerSlots && woodSlot < 0; i++) if (me.SlotAt(i).Id == Item.Wood) woodSlot = i;
            int woodBefore = me.Count(Item.Wood);
            me.DropItemRpc(0, (byte)woodSlot, 100, default);
            yield return new WaitForSeconds(0.5f);
            int woodItem = FindWorldItem(Item.Wood);
            Check(woodItem >= 0 && me.Count(Item.Wood) == woodBefore - 100, "dropped 100 wood on the ground");
            me.PickupItemRpc(woodItem);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Wood) == woodBefore && FindWorldItem(Item.Wood) < 0, "picked the wood back up");

            // hand-held ram on the opponent's wall
            float waitUntil = Time.time + 60f;
            Structure enemyWall = null;
            while (enemyWall == null && Time.time < waitUntil)
            {
                foreach (var s in Structure.All) if (s.Team.Value != team && s.PType == PieceType.Wall) enemyWall = s;
                yield return new WaitForSeconds(0.5f);
            }
            Check(enemyWall != null, "opponent built a wall to raid");
            if (enemyWall == null) yield break;
            yield return new WaitForSeconds(4f); // let the client verify its wall before we smash it
            var wp = enemyWall.transform.position;
            yield return Hold(me, Item.Ram);
            pc.LocalTeleport(new Vector3(wp.x, 0.1f, wp.z + 1.5f), 180f);
            yield return new WaitForSeconds(Cfg.RamWindup);
            me.RamStrikeRpc(enemyWall.NetworkObject, wp + Vector3.up * 1.5f);
            yield return new WaitForSeconds(0.6f);
            Check(!enemyWall.IsSpawned && RamHits(me) == Cfg.RamUses - 1, "ram smashed the wooden wall in one hit");
            if (wall != null && wall.IsSpawned)
            {
                // the ram can't hit your own walls, so exercise the stone -> wood path directly on ours
                wall.ServerDowngrade();
                Check(wall.Tier.Value == 0 && Mathf.Approximately(wall.Health.Value, Cfg.PieceHp(PieceType.Wall, 0)), "stone downgrades to full-health wood");
            }
            // demolish one of your own pieces with the building plan (part of the wood comes back)
            if (floor != null && floor.IsSpawned)
            {
                yield return Hold(me, Item.BuildingPlan);
                pc.LocalTeleport(floor.transform.position + new Vector3(-4f, -3.9f, 0), 90f);
                yield return new WaitForSeconds(0.3f);
                int woodBefore2 = me.Count(Item.Wood);
                me.DemolishRpc(floor.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(!floor.IsSpawned && me.Count(Item.Wood) == woodBefore2 + Mathf.FloorToInt(Cfg.FloorWood * Cfg.DemolishRefund), "demolished own floor with the plan (wood refunded)");
            }

            // the helmet stops one headshot completely and breaks
            other.Health.Value = 500f;
            other.HelmetHp.Value = 1;
            yield return Hold(me, Item.Bow);
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(other.HelmetHp.Value == 0 && Mathf.Abs(other.Health.Value - 500f) < 0.5f, $"helmet stopped a headshot completely and broke (hp {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;
            me.ServerGive(Item.Helmet, 1, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Helmet);
            me.UseItemRpc();
            yield return new WaitForSeconds(0.9f);
            Check(me.HelmetHp.Value == 1 && me.Count(Item.Helmet) == 0, "put on a found helmet");
            me.HelmetHp.Value = 0;

            // wooden armour: a second bar that takes the damage first
            other.ArmorHp.Value = 50;
            other.Health.Value = Cfg.MaxHealth;
            other.ServerDamage(80f, me);
            Check(other.ArmorHp.Value == 0 && Mathf.Abs(other.Health.Value - (Cfg.MaxHealth - 30f)) < 0.5f, $"armour soaked up the first 50 of 80 damage (hp {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;
            me.ServerGive(Item.Armor, 1, Cfg.ArmorHp);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Armor);
            me.UseItemRpc();
            yield return new WaitForSeconds(0.9f);
            Check(me.ArmorHp.Value == Cfg.ArmorHp && me.Count(Item.Armor) == 0, "put the armour on");
            me.ArmorHp.Value = 0;

            // crossbow: loads a bolt (uses an arrow), fires one hard hit
            me.ServerGive(Item.Crossbow, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Crossbow);
            int arrowsNow = me.Count(Item.Arrow);
            me.ReloadCrossbowRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == 1 && me.Count(Item.Arrow) == arrowsNow - 1, "crossbow loaded a bolt");
            other.Health.Value = 500f;
            me.FireCrossbowRpc(me.EyePos, Vector3.forward * Cfg.CrossbowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Abs(other.Health.Value - (500f - Cfg.CrossbowDamage)) < 0.5f, $"crossbow bolt did {Cfg.CrossbowDamage:0} (500 -> {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;

            // a window, and walls behind the machine
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 2, out int wi, out int wj);
            pc.LocalTeleport(BuildGrid.CellCenter(wi, wj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Foundation, wi, wj, 0, 0);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Window, wi - 1, wj, 0, 0);
            yield return new WaitForSeconds(0.5f);
            Check(CountStructures(PieceType.Window, team) == 1, "built a window");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            int bi = Mathf.RoundToInt(Cfg.BedrockCenter(team).x / Cfg.Cell), bj = Mathf.RoundToInt(Cfg.BedrockCenter(team).z / Cfg.Cell);
            int backJ = Cfg.BackDir(team).z < 0 ? bj - 2 : bj; // edge on the far side of the bedrock
            int wallsBefore = CountStructures(PieceType.Wall, team);
            me.PlaceRpc((byte)PieceType.Wall, bi - 1, backJ, 0, 1);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Wall, bi, backJ, 0, 1);
            yield return new WaitForSeconds(0.5f);
            Check(CountStructures(PieceType.Wall, team) == wallsBefore + 2, "walls fit behind the machine on the bedrock's back edge");

            // fort tower
            me.ServerGive(Item.FortTower, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.FortTower);
            var fortAt = new Vector3(-20f, 0f, baseC.z > 0 ? 35f : -35f);
            pc.LocalTeleport(fortAt + new Vector3(0, 1f, -6f), 0);
            yield return new WaitForSeconds(0.9f);
            me.ThrowFortRpc(me.EyePos, Vector3.forward * 10f);
            yield return new WaitForSeconds(0.1f);
            me.FortLandRpc(fortAt + Vector3.up * 0.5f);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.Tower, team) == 1 && me.Count(Item.FortTower) == 0, "threw a fort tower and it went up");

            // wooden car: place it, get in, get out
            me.ServerGive(Item.Car, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Car);
            var carAt = Vector3.zero;
            for (float cx2 = 10f; cx2 < 80f; cx2 += 4f)
            {
                carAt = new Vector3(cx2, 0f, baseC.z > 0 ? 35f : -35f);
                carAt.y = MapBuilder.Height(carAt.x, carAt.z);
                if (PlayerNet.DeployProblem(Item.Car, team, carAt, 0f) == null) break;
            }
            pc.LocalTeleport(carAt + new Vector3(0, 0.3f, -3.5f), 0);
            yield return new WaitForSeconds(0.9f);
            me.PlaceDeployableRpc((byte)Item.Car, carAt, 0f);
            yield return new WaitForSeconds(0.6f);
            Vehicle car = null;
            foreach (var v in Vehicle.All) if (!v.IsHorse) car = v;
            Check(car != null && me.Count(Item.Car) == 0, "placed the wooden car");
            if (car != null)
            {
                me.MountRpc(car.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(me.Riding && car.HasDriver, "got in the car");
                me.DismountRpc();
                yield return new WaitForSeconds(0.6f);
                Check(!me.Riding && !car.HasDriver, "got out of the car");
            }

            // horses: need a saddle
            int horses = 0;
            Vehicle horse = null;
            foreach (var v in Vehicle.All) if (v.IsHorse) { horses++; if (horse == null || (v.transform.position - me.transform.position).sqrMagnitude < (horse.transform.position - me.transform.position).sqrMagnitude) horse = v; }
            Check(horses == Cfg.HorsesPerSide * 2, $"wild horses roam the map ({horses})");
            if (horse != null)
            {
                pc.LocalTeleport(horse.transform.position + new Vector3(1.8f, 0.5f, 0), 270f);
                yield return new WaitForSeconds(0.6f);
                me.MountRpc(horse.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(!me.Riding, "can't ride a wild horse without a saddle");
                me.ServerGive(Item.Saddle, 1);
                yield return new WaitForSeconds(0.2f);
                pc.LocalTeleport(horse.transform.position + new Vector3(1.8f, 0.5f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.MountRpc(horse.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(me.Riding && horse.Saddled.Value && me.Count(Item.Saddle) == 0, "saddled the horse and got on");
                // the ball on horseback: pick it up from the saddle and throw it
                var ball = Ball.Instance;
                if (ball != null && !ball.IsCarried && !NetGame.Instance.WallUp)
                {
                    var ballWas = ball.transform.position;
                    // drop it on the ground beside the horse (clear of its body, or it can get knocked away)
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        var side = horse.transform.position + horse.transform.right * 1.8f;
                        side.y = MapBuilder.Height(side.x, side.z) + 0.7f;
                        ball.ServerDrop(side, Vector3.zero);
                        yield return new WaitForSeconds(0.5f);
                        if (Vector3.Distance(ball.transform.position, me.EyePos) < Cfg.InteractRange + 1.5f) break;
                    }
                    Log($"horse + ball: ball at {ball.transform.position} carried={ball.IsCarried} socket={ball.SocketTeam.Value}, eye at {me.EyePos} (distance {Vector3.Distance(ball.transform.position, me.EyePos):0.0}), riding={me.Riding}, state={NetGame.Instance.S}");
                    me.PickupBallRpc();
                    yield return new WaitForSeconds(0.3f);
                    Check(me.Riding && me.CarryingBall, "picked the ball up without getting off the horse");
                    me.ThrowBallRpc(me.EyePos, me.transform.forward, me.transform.forward * 8f);
                    yield return new WaitForSeconds(0.1f);
                    Check(me.Riding && !me.CarryingBall, "threw the ball from the horse");
                    ball.ServerDrop(ballWas, Vector3.zero);
                }
                else Log("horse + ball: no loose ball right now, skipped");
                me.DismountRpc();
                yield return new WaitForSeconds(0.5f);
            }

            // C4 on the opponent's foundation
            Structure enemyFoundation = null;
            foreach (var s in Structure.All) if (s.Team.Value != team && s.PType == PieceType.Foundation) enemyFoundation = s;
            if (enemyFoundation != null)
            {
                me.ServerGive(Item.C4, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.C4);
                var fp = enemyFoundation.transform.position;
                pc.LocalTeleport(new Vector3(fp.x, 0.1f, fp.z + 6f), 180f);
                yield return new WaitForSeconds(0.9f);
                me.ThrowC4Rpc(me.EyePos, Vector3.back * 10f);
                yield return new WaitForSeconds(0.1f);
                me.C4LandRpc(fp + Vector3.up * 1.03f, Vector3.up);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                Check(!enemyFoundation.IsSpawned && me.Count(Item.C4) == 0, "C4 blew up the enemy foundation");
            }
            else Check(false, "C4: no enemy foundation to blow up");

            // respawn in the wild lands anywhere out in the wild (never in a base)
            other.ServerRespawn(true);
            yield return new WaitForSeconds(0.8f);
            Check(Cfg.BaseTeamAt(other.transform.position) < 0, $"wild respawn puts you out in the wild, outside every base ({other.transform.position})");
            // (it's random now, so put them somewhere open for the next tests)
            var open = new Vector3(-30f, 0f, baseC.z > 0 ? 30f : -30f);
            open.y = MapBuilder.Height(open.x, open.z) + 0.1f;
            other.TeleportRpc(open, 0f);
            yield return new WaitForSeconds(0.8f);

            // death wand: one shot, instant kill
            me.ServerGive(Item.DeathWand, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.DeathWand);
            var op = other.transform.position;
            pc.LocalTeleport(op + new Vector3(0, 1.5f, -8f), 0f);
            yield return new WaitForSeconds(0.9f);
            other.Health.Value = Cfg.MaxHealth;
            me.WandRpc(((op + Vector3.up * 1.4f) - me.EyePos + new Vector3(1.2f, 0, 0)).normalized); // a near miss still kills
            yield return new WaitForSeconds(0.5f);
            Check(other.Dead.Value && me.Count(Item.DeathWand) == 0, "death wand near miss killed instantly");

            // ---------------- airdrop items ----------------
            NetGame.Instance.TimerPaused.Value = true; // plenty of time for these
            yield return LootTests(me, pc, other, team, baseC);

            // airdrop: call one in, open it and take the item
            NetGame.Instance.DevSpawnAirdrop();
            float dropWait = Time.time + 20f;
            Container drop = null;
            while (drop == null && Time.time < dropWait && NetGame.Instance.S == GameState.BallLive)
            {
                foreach (var c in Container.All) if (c.IsAirdrop && Cfg.BaseTeamAt(c.transform.position) < 0) drop = c;
                yield return new WaitForSeconds(0.5f);
            }
            Check(drop != null, "an airdrop was beamed down");
            if (drop != null)
            {
                var dp = drop.transform.position;
                Check(Mathf.Max(Mathf.Abs(dp.x - Cfg.BaseCenter[0].x), Mathf.Abs(dp.z - Cfg.BaseCenter[0].z)) - Cfg.BaseHalf >= Cfg.AirdropBaseDistance - 0.5f
                      && Mathf.Max(Mathf.Abs(dp.x - Cfg.BaseCenter[1].x), Mathf.Abs(dp.z - Cfg.BaseCenter[1].z)) - Cfg.BaseHalf >= Cfg.AirdropBaseDistance - 0.5f, $"airdrop not next to a base ({dp})");
                var loot = drop.Slots[0];
                pc.LocalTeleport(dp + new Vector3(0, 0.1f, -2f), 0f);
                yield return new WaitForSeconds(0.4f);
                int had = me.Count(loot.Id);
                me.MoveItemRpc(1, 0, 0, 255, 0, drop.NetworkObject);
                yield return new WaitForSeconds(1.2f);
                Check(me.Count(loot.Id) == had + loot.Count && !drop.IsSpawned, $"took the airdrop's {Cfg.ItemName(loot.Id)} x{loot.Count}, crate disappeared");
            }

            NetGame.Instance.TimerPaused.Value = false;
            NetGame.Instance.DevSetTimeLeft(3f);
            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
            yield return Hold(me, Item.Rock);
        }

        IEnumerator LootTests(PlayerNet me, PlayerController pc, PlayerNet other, int team, Vector3 baseC)
        {
            var g = NetGame.Instance;
            void Revive(PlayerNet p) { if (p.Dead.Value) p.ServerRespawn(false); p.Health.Value = Cfg.MaxHealth; p.ArmorHp.Value = 0; }
            var field = new Vector3(-30f, 0f, baseC.z > 0 ? 30f : -30f);
            field.y = MapBuilder.Height(field.x, field.z);

            // sniper: one shot kills, a helmet stops a headshot
            Revive(other);
            me.ServerGive(Item.Sniper, 1, Cfg.SniperAmmo);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Sniper);
            other.HelmetHp.Value = 1;
            me.SniperFireRpc(true, other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Check(!other.Dead.Value && other.HelmetHp.Value == 0, "a helmet stopped a sniper headshot");
            yield return new WaitForSeconds(1.2f);
            other.ArmorHp.Value = 100;
            me.SniperFireRpc(true, other.NetworkObject, other.transform.position + Vector3.up * 1f, Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Check(other.Dead.Value && me.HeldStack.Data == Cfg.SniperAmmo - 2, "sniper body shot killed through armour");
            Revive(other);
            yield return new WaitForSeconds(0.5f);

            // portal gun: two shots make a linked pair, then it's used up
            me.ServerGive(Item.PortalGun, 1, Cfg.PortalShots);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.PortalGun);
            int portals = g.Portals.Count;
            pc.LocalTeleport(field + new Vector3(6f, 0.1f, -4f), 0f); // portals have to be within reach
            yield return new WaitForSeconds(0.4f);
            me.PortalRpc(field + new Vector3(0, 0.05f, 0), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            me.PortalRpc(field + new Vector3(0, 0.05f, 8f), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Check(g.Portals.Count == portals + 2 && g.Portals[portals].Pair == g.Portals[portals + 1].Pair && me.Count(Item.PortalGun) == 0,
                $"two portal gun shots made a linked pair and used the gun up ({g.Portals.Count - portals} portals, {me.Count(Item.PortalGun)} gun left)");

            // jetpack burns fuel and runs out
            me.ServerGive(Item.Jetpack, 1, Cfg.JetpackFuel);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Jetpack);
            me.JetFuelRpc(Cfg.JetpackSeconds * 0.25f);
            me.JetFuelRpc(Cfg.JetpackSeconds * 0.25f);
            yield return new WaitForSeconds(0.3f);
            int fuel = me.HeldStack.Data;
            me.JetFuelRpc(2f); me.JetFuelRpc(2f); me.JetFuelRpc(2f); me.JetFuelRpc(2f);
            yield return new WaitForSeconds(0.4f);
            Check(fuel > 30 && fuel < 70 && me.Count(Item.Jetpack) == 0, $"jetpack used fuel ({fuel}%) and ran out");

            // rocket wrecks an enemy barrier
            var bar = Object.Instantiate(Bootstrap.I.structurePrefab, field + new Vector3(0, 0, 6f), Quaternion.identity);
            bar.GetComponent<Structure>().ServerInit(PieceType.Barrier, 1 - team, default, false);
            bar.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            var barS = bar.GetComponent<Structure>();
            me.ServerGive(Item.RocketLauncher, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.RocketLauncher);
            pc.LocalTeleport(field + new Vector3(0, 0.3f, -4f), 0);
            yield return new WaitForSeconds(0.6f);
            me.ThrowItemRpc(Item.RocketLauncher, me.EyePos, Vector3.forward * Cfg.RocketSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ThrownLandRpc(Item.RocketLauncher, barS.transform.position + Vector3.up * 0.7f, Vector3.back);
            yield return new WaitForSeconds(0.5f);
            Check(!barS.IsSpawned && me.Count(Item.RocketLauncher) == 0, "rocket destroyed an enemy barrier");

            // build egg lays a path of blocks
            int blocks = 0;
            me.ServerGive(Item.BuildEgg, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.BuildEgg);
            me.ThrowItemRpc(Item.BuildEgg, me.EyePos + Vector3.up, new Vector3(1f, 0.5f, 0.3f).normalized * 16f);
            yield return new WaitForSeconds(2.5f);
            foreach (var st in Structure.All) if (st.PType == PieceType.EggBlock) blocks++;
            Check(blocks >= 4, $"build egg laid a path of {blocks} blocks");

            // slenderman hatches (and can be killed)
            me.ServerGive(Item.SlenderEgg, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.SlenderEgg);
            me.ThrowItemRpc(Item.SlenderEgg, me.EyePos, Vector3.forward * 10f);
            yield return new WaitForSeconds(0.1f);
            me.ThrownLandRpc(Item.SlenderEgg, field + new Vector3(-8f, 0.2f, 0), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Vehicle slender = null;
            foreach (var v in Vehicle.All) if (v.IsSlender) slender = v;
            Check(slender != null, "Slenderman hatched from the egg");
            if (slender != null)
            {
                slender.ServerDamage(9999f, me);
                yield return new WaitForSeconds(0.4f);
                Check(!slender.IsSpawned, "Slenderman can be killed");
            }

            // staff of the giant
            me.ServerGive(Item.GiantStaff, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.GiantStaff);
            me.GiantStaffRpc();
            yield return new WaitForSeconds(0.4f);
            Check(other.Giant && me.Count(Item.GiantStaff) == 0, "staff of the giant turned the opponent into a giant");
            other.GiantUntil.Value = -1;

            // C4 right on top of someone kills them
            Revive(other);
            other.ArmorHp.Value = 100;
            g.ServerArmC4(other.transform.position + Vector3.up * 0.5f, Vector3.up, me);
            yield return new WaitForSeconds(Cfg.C4Fuse + 0.5f);
            Check(other.Dead.Value, "C4 right on top of someone kills them (even with armour)");
            Revive(other);

            // horse: killing it drops meat and its (team coloured) saddle; meat heals you fully
            Vehicle horse = null;
            foreach (var v in Vehicle.All) if (v.IsHorse && v.Saddled.Value) horse = v;
            if (horse == null) foreach (var v in Vehicle.All) if (v.IsHorse) horse = v;
            if (horse != null)
            {
                bool hadSaddle = horse.Saddled.Value;
                int saddleTeam = horse.SaddleTeam.Value;
                var hp = horse.transform.position;
                horse.ServerDamage(9999f, me);
                yield return new WaitForSeconds(0.5f);
                int meat = FindWorldItem(Item.Meat), saddle = FindWorldItem(Item.Saddle);
                Check(!horse.IsSpawned && meat >= 0 && (!hadSaddle || saddle >= 0), "killed a horse: it dropped meat" + (hadSaddle ? " and its saddle" : ""));
                if (hadSaddle && saddle >= 0)
                    foreach (var it in g.Items) if (it.Id == saddle) Check(it.Stack.Data == saddleTeam + 1 && saddleTeam == team, "the saddle is in the team colour of whoever saddled it");
                pc.LocalTeleport(hp + new Vector3(0, 0.5f, -1.5f), 0);
                yield return new WaitForSeconds(0.5f);
                me.PickupItemRpc(meat);
                yield return new WaitForSeconds(0.4f);
                me.Health.Value = 20f;
                yield return Hold(me, Item.Meat);
                me.EatRpc();
                yield return new WaitForSeconds(0.4f);
                Check(Mathf.Approximately(me.Health.Value, Cfg.MaxHealth), "horse meat healed fully");
            }
            else Check(false, "no horse to test");

            // felling a whole tree pays a bonus
            ResourceNode tree = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Tree && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0) { tree = n; break; }
            if (tree != null)
            {
                yield return Hold(me, Item.Rock);
                tree.Amount.Value = 3;
                var tp = tree.transform.position;
                pc.LocalTeleport(tp + new Vector3(0, 0.2f, -2f), 0);
                yield return new WaitForSeconds(0.7f);
                int wood = me.Count(Item.Wood);
                me.MeleeRpc(true, tree.NetworkObject, tp + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Wood) == wood + 3 + Cfg.TreeFellBonus, $"felling the whole tree gave the {Cfg.TreeFellBonus} bonus");
            }

            // airstrike flattens the zone (trees too)
            ResourceNode victim = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (!n.IsBush && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0 && Vector3.Distance(n.transform.position, other.transform.position) > 30f && Vector3.Distance(n.transform.position, me.transform.position) > 30f) { victim = n; break; }
            if (victim != null)
            {
                me.ServerGive(Item.Airstrike, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Airstrike);
                me.AirstrikeRpc(victim.transform.position);
                yield return new WaitForSeconds(Cfg.AirstrikeDelay + 0.8f);
                Check(victim.Amount.Value == 0 && me.Count(Item.Airstrike) == 0, "airstrike flattened the trees/rocks in its zone");
            }

            // fake bomb bush: whoever picks it blows up
            me.ServerGive(Item.BombBush, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.BombBush);
            me.ThrowItemRpc(Item.BombBush, me.EyePos, Vector3.forward * 8f);
            yield return new WaitForSeconds(0.1f);
            var bushAt = me.transform.position + me.transform.forward * 3f;
            me.ThrownLandRpc(Item.BombBush, bushAt, Vector3.up);
            yield return new WaitForSeconds(0.6f);
            ResourceNode trap = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.IsBush && n.TrapTeam.Value != ResourceNode.NoTrap) trap = n;
            Check(trap != null, "fake bomb bush planted");
            if (trap != null)
            {
                me.Health.Value = Cfg.MaxHealth;
                pc.LocalTeleport(trap.transform.position + new Vector3(0, 0.3f, -1.5f), 0);
                yield return new WaitForSeconds(0.5f);
                me.PickBerriesRpc(trap.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(me.Dead.Value && !trap.IsSpawned, "picking the fake bomb bush blew us up");
                Revive(me);
                yield return new WaitForSeconds(0.5f);
            }

        }

        /// <summary>
        /// PSX graphics: a forest view, a tree close up with its X revealed, the hit particles, then the same views in normal
        /// graphics. Checks the X sits on the visible trunk. Run with -host -solo -fast.
        /// </summary>
        IEnumerator PsxShots(PlayerNet me, PlayerController pc)
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            void Snap(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("shot " + name); }
            Check(PsxArt.TreeCount >= 30, $"PSX tree models loaded ({PsxArt.TreeCount})");
            // the tree nearest our base that has open ground in front of it
            ResourceNode tree = null;
            var home = me.transform.position;
            foreach (var n in ResourceNode.All)
                if (n.Kind.Value == ResourceNode.Tree && (tree == null || (n.transform.position - home).sqrMagnitude < (tree.transform.position - home).sqrMagnitude)) tree = n;
            if (tree == null) { Log("FAIL: no tree"); yield break; }
            var tp = tree.transform.position;
            var away = (home - tp); away.y = 0; away = away.normalized;
            // forest view
            pc.LocalTeleport(tp + away * 14f + Vector3.up * 0.1f, Quaternion.LookRotation(-away).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(-away).eulerAngles.y, -6f);
            yield return new WaitForSeconds(1f);
            Snap("psx_1_forest");
            yield return new WaitForEndOfFrame();
            // reveal the X facing us, then look at it close up
            tree.ServerHarvest(5, false, tp + away * 3f);
            yield return new WaitForSeconds(0.4f);
            Check(tree.TryGetSpot(out var spot, out _), "the weak spot X is showing");
            var x = tree.GetComponentsInChildren<Transform>(true);
            Transform marker = null;
            foreach (var t in x) if (t.name == "x") marker = t;
            var eye = spot + away * 2.6f;
            eye.y = MapBuilder.Height(eye.x, eye.z);
            pc.LocalTeleport(eye, Quaternion.LookRotation(-away).eulerAngles.y);
            var look = Quaternion.LookRotation(spot - (eye + Vector3.up * 1.6f)).eulerAngles;
            pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
            yield return new WaitForSeconds(0.8f);
            Snap("psx_2_tree_x");
            yield return new WaitForEndOfFrame();
            // the X sits on the trunk you see: about as far from the middle of the tree as the PSX trunk is thick
            if (marker != null)
            {
                var d = marker.position - tp;
                float fromAxis = new Vector2(d.x, d.z).magnitude;
                Log($"X {fromAxis:0.00} m from the middle of the trunk (the collider is 0.30)");
                Check(fromAxis > 0.05f && fromAxis < 0.7f, $"the X sits on the visible trunk ({fromAxis:0.00} m from its middle)");
            }
            // the hit effects: chips in the bark colour, the X chime, a felled tree's burst
            Fx.Play(FxKind.WoodChips, spot, away);
            Fx.Play(FxKind.WeakSpotTree, spot, away);
            yield return new WaitForSeconds(0.12f);
            Snap("psx_3_hit_fx");
            yield return new WaitForEndOfFrame();
            Fx.Play(FxKind.Timber, tp + Vector3.up * 2f, Vector3.up);
            yield return new WaitForSeconds(0.15f);
            Snap("psx_4_timber_fx");
            yield return new WaitForSeconds(1.2f);
            // the same close-up in normal graphics (switched live)
            GameSettings.SetPsx(false, false);
            yield return new WaitForSeconds(0.5f);
            Check(tree.TryGetSpot(out _, out _), "switching graphics keeps the tree and its X");
            Snap("psx_5_normal_tree_x");
            yield return new WaitForEndOfFrame();
            pc.LocalTeleport(tp + away * 14f + Vector3.up * 0.1f, Quaternion.LookRotation(-away).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(-away).eulerAngles.y, -6f);
            yield return new WaitForSeconds(0.8f);
            Snap("psx_6_normal_forest");
            yield return new WaitForEndOfFrame();
            GameSettings.SetPsx(true, false);
            yield return new WaitForSeconds(0.5f);
            // tree camo in PSX mode
            me.ServerGive(Item.TreeCamo, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.TreeCamo);
            yield return new WaitForSeconds(1f);
            Snap("psx_7_tree_camo");
            yield return new WaitForEndOfFrame();
            Log("psx shots done");
            Application.Quit(0);
        }

        /// <summary>
        /// Visual check of the body animations: a row of aliens in every pose (holding the real items), photographed from
        /// the front and from the side. Run with -host -solo -fast.
        /// </summary>
        IEnumerator RigShots(PlayerNet me, PlayerController pc)
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            var A = BodyAnimator.Act.None;
            var poses = new (string name, Item item, float speed, float height, float velY, bool crouch, BodyAnimator.Act act, bool swing, bool thrw, bool carry)[]
            {
                ("idle", Item.Hatchet, 0f, 0f, 0f, false, A, false, false, false),
                ("walk", Item.None, 3f, 0f, 0f, false, A, false, false, false),
                ("sprint", Item.None, 7.5f, 0f, 0f, false, A, false, false, false),
                ("sprint + spear", Item.Spear, 7.5f, 0f, 0f, false, A, false, false, false),
                ("crouch walk", Item.Pickaxe, 2.2f, 0f, 0f, true, A, false, false, false),
                ("jump (up)", Item.None, 4f, 1.4f, 5f, false, A, false, false, false),
                ("jump (down)", Item.None, 4f, 1.4f, -7f, false, A, false, false, false),
                ("slide", Item.None, 9f, 0f, 0f, true, BodyAnimator.Act.Slide, false, false, false),
                ("bow draw", Item.Bow, 0f, 0f, 0f, false, BodyAnimator.Act.BowDraw, false, false, false),
                ("spear wind-up", Item.Spear, 0f, 0f, 0f, false, BodyAnimator.Act.SpearAim, false, false, false),
                ("crossbow aim", Item.Crossbow, 0f, 0f, 0f, false, BodyAnimator.Act.Aim, false, false, false),
                ("eat", Item.Berry, 0f, 0f, 0f, false, BodyAnimator.Act.Eat, false, false, false),
                ("throw", Item.Spear, 0f, 0f, 0f, false, A, false, true, false),
                ("rock swing", Item.Rock, 0f, 0f, 0f, false, A, true, false, false),
                ("carry ball", Item.None, 4f, 0f, 0f, false, A, false, false, true),
                ("ram", Item.Ram, 0f, 0f, 0f, false, BodyAnimator.Act.Ram, false, false, false),
                ("riding", Item.None, 0f, 0.9f, 0f, false, A, false, false, false),
                ("dead", Item.None, 0f, 0f, 0f, false, A, false, false, false),
            };
            // a flat, empty spot in our half
            int team = me.Team.Value;
            var bc = Cfg.BaseCenter[team];
            var origin = new Vector3(0f, 0f, bc.z * 0.45f);
            var demos = new System.Collections.Generic.List<(Transform root, BodyAnimator anim, Transform hand, int i)>();
            for (int i = 0; i < poses.Length; i++)
            {
                var ps = poses[i];
                var r = new GameObject("rig_" + ps.name).transform;
                int row = i / 6, col = i % 6;
                r.position = origin + new Vector3((col - 2.5f) * 2.1f, ps.height, row * 40f);
                r.position = new Vector3(r.position.x, MapBuilder.Height(r.position.x, r.position.z) + ps.height, r.position.z);
                r.rotation = Quaternion.Euler(0, 180f, 0); // facing the camera
                var anim = BodyAnimator.TryCreate(r, Cfg.ModelWidth, out var model);
                if (anim == null) { Log("FAIL: rigged alien missing"); yield break; }
                // skinned and tinted like the real players, cycling through the team colours
                var tinted = new System.Collections.Generic.List<Material>();
                PlayerNet.SkinAlien(model, tinted);
                foreach (var m in tinted) { var c = PlayerNet.TeamTint(m, i % 4); m.SetColor("_BaseColor", c); m.color = c; }
                if (i % 6 == 1)
                {
                    // a helmet like PlayerNet.RebuildHelmet puts on, to check it sits on the head
                    var hm = ItemModels.Create(Item.Helmet, anim.HeadBone);
                    PlayerNet.FitHelmet(hm, anim.HeadBone, r);
                }
                Transform hand = null;
                if (ps.item != Item.None || ps.carry)
                {
                    hand = new GameObject("held").transform;
                    if (ps.carry) ItemModels.CreateBall(hand, 1.24f);
                    else ItemModels.Create(ps.item, hand);
                }
                var label = new GameObject("label");
                label.transform.SetParent(r, false);
                label.transform.localPosition = new Vector3(0, 2.25f - ps.height, 0);
                label.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                var tm = label.AddComponent<TextMesh>();
                tm.text = ps.name;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = 0.05f;
                tm.fontSize = 48;
                tm.color = Color.white;
                demos.Add((r, anim, hand, i));
                if (i == 0)
                {
                    foreach (var rend in r.GetComponentsInChildren<Renderer>())
                    {
                        var smr = rend as SkinnedMeshRenderer;
                        Log($"rig renderer {rend.name} {rend.GetType().Name} bones={(smr != null ? smr.bones.Length : -1)} root={(smr != null && smr.rootBone ? smr.rootBone.name : "-")} mesh={(smr != null && smr.sharedMesh ? smr.sharedMesh.name + " bw=" + smr.sharedMesh.boneWeights.Length + " bind=" + smr.sharedMesh.bindposes.Length : "-")}");
                        if (smr != null) foreach (var b in smr.bones) Log($"  bone {(b ? b.name : "null")}");
                    }
                    Log($"rig hand bone: {anim.RightHand.name} path parent {anim.RightHand.parent.name}");
                    Log($"rig head bone at {anim.HeadBone.position.y - r.position.y:F3} m, lossyScale {anim.HeadBone.lossyScale}, chest at {anim.ChestBone.position.y - r.position.y:F3} m");
                }
            }
            void Pose(float t, float dt)
            {
                foreach (var (r, anim, hand, i) in demos)
                {
                    var ps = poses[i];
                    var pose = new BodyAnimator.Pose
                    {
                        Crouch = ps.crouch, Holding = ps.item != Item.None, Carrying = ps.carry,
                        TwoHanded = ps.item == Item.Rock || ps.item == Item.Spear || ps.item == Item.Ram || ps.item == Item.Chainsaw || ps.item == Item.Crossbow,
                        Action = ps.act, Pitch = 0f,
                        Riding = ps.name == "riding", Dead = ps.name == "dead", DeadTime = t,
                        Swing = ps.swing ? Mathf.Clamp01(1f - (t % 0.9f) / 0.55f) : 0f,
                        Throw = ps.thrw ? Mathf.Clamp01(1f - (t % 0.9f) / 0.5f) : 0f,
                    };
                    anim.TickWithVelocity(pose, dt, r.forward * ps.speed + Vector3.up * ps.velY);
                    if (hand != null)
                    {
                        if (ps.carry) { hand.position = r.position + r.forward * 0.6f + Vector3.up * 1.15f; hand.rotation = r.rotation; }
                        else { anim.GripPose(out var gp, out var gr); hand.SetPositionAndRotation(gp, gr); }
                    }
                }
            }
            // pose in LateUpdate, like the game does (the model's Animator would overwrite anything earlier in the frame)
            float runFrom = 0f, runT0 = 0f;
            s_LateTick = () => Pose(runFrom + Time.time - runT0, Time.deltaTime);
            IEnumerator Run(float seconds, float from)
            {
                runFrom = from;
                runT0 = Time.time;
                yield return new WaitForSeconds(seconds);
            }
            void Snap(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("shot " + name); }
            for (int row = 0; row < 3; row++)
            {
                var c = origin + new Vector3(0, 0, row * 40f);
                // front: the camera 8 m in front of the row, looking back at them
                pc.LocalTeleport(c + new Vector3(0, MapBuilder.Height(c.x, c.z - 6.5f) + 0.1f, -6.5f), 0f);
                pc.SetLook(0f, 6f);
                yield return Run(1.2f, 0f);
                Snap($"rig_row{row}_front");
                yield return Run(0.35f, 1.2f);
                Snap($"rig_row{row}_front_b");
                yield return new WaitForEndOfFrame();
                // side: turn them all to face left
                foreach (var dm in demos) dm.root.rotation = Quaternion.Euler(0, 270f, 0);
                yield return Run(0.8f, 1.55f);
                Snap($"rig_row{row}_side");
                yield return new WaitForEndOfFrame();
                yield return null;
                foreach (var dm in demos) dm.root.rotation = Quaternion.Euler(0, 180f, 0);
                // close-ups of each half of the row, front and side
                for (int half = 0; half < 2; half++)
                {
                    var cc = c + new Vector3(half == 0 ? -3.15f : 3.15f, 0, -3.6f);
                    pc.LocalTeleport(cc + new Vector3(0, MapBuilder.Height(cc.x, cc.z) + 0.1f, 0), 0f);
                    pc.SetLook(0f, 8f);
                    yield return Run(0.3f, 1.2f);
                    Snap($"rig_row{row}_close{half}");
                    yield return new WaitForEndOfFrame();
                    foreach (var dm in demos) dm.root.rotation = Quaternion.Euler(0, 270f, 0);
                    yield return Run(0.3f, 1.55f);
                    Snap($"rig_row{row}_close{half}_side");
                    yield return new WaitForEndOfFrame();
                    yield return null;
                    foreach (var dm in demos) dm.root.rotation = Quaternion.Euler(0, 180f, 0);
                }
            }
            s_LateTick = null;
            foreach (var dm in demos) Object.Destroy(dm.root.gameObject);
            // the in-game screens: crafting, the pause menu and its controls page
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            me.ServerGive(Item.Wood, 120);
            me.ServerGive(Item.Meat, 1);
            yield return new WaitForSeconds(0.4f);
            pc.MenuOpen = true;
            yield return new WaitForSeconds(0.6f);
            Snap("ui_crafting");
            yield return new WaitForEndOfFrame();
            pc.MenuOpen = false;
            pc.Paused = true;
            Hud.OpenPause = 0;
            yield return new WaitForSeconds(0.6f);
            Snap("ui_pause");
            yield return new WaitForEndOfFrame();
            Hud.OpenPause = 1;
            yield return new WaitForSeconds(0.6f);
            Snap("ui_controls");
            yield return new WaitForEndOfFrame();
            pc.Paused = false;
            Log("rig shots done");
            Application.Quit(0);
        }

        /// <summary>Visual check: capture screenshots of the new features. Run windowed with -host -fast (optionally with a client).</summary>
        IEnumerator ShotsRoutine(PlayerNet me, PlayerController pc)
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            void Snap(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("shot " + name); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.6f);
                Snap(name);
                yield return new WaitForSeconds(0.3f);
            }
            int team = me.Team.Value;

            // 1: spawning on the bedrock
            yield return new WaitForSeconds(0.2f);
            NetGame.SpawnPoint(team, false, out var cp, out var cy);
            pc.LocalTeleport(cp, cy);
            pc.SetLook(cy, 8f);
            yield return Shot("01_spawn_on_bedrock");

            // 2: the bedrock and the alien machine from the front, and the glass wall
            var back = Cfg.BackDir(team);
            var front = Cfg.BedrockCenter(team) - back * 7f + Vector3.Cross(Vector3.up, back) * 3f + Vector3.up * 0.1f;
            var look = Cfg.MachinePos(team) + Vector3.up * 1.2f - front;
            pc.LocalTeleport(front, Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y, 6f);
            yield return Shot("02_machine");
            var above = Cfg.BedrockCenter(team) - back * 9f + Vector3.up * 8f;
            pc.LocalTeleport(above, Quaternion.LookRotation(back).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 30f);
            yield return Shot("02b_bedrock_above");
            pc.LocalTeleport(new Vector3(8f, 0.1f, -6f * Mathf.Sign(-back.z)), Quaternion.LookRotation(-back).eulerAngles.y + 20f);
            pc.SetLook(Quaternion.LookRotation(-back).eulerAngles.y + 20f, -5f);
            yield return Shot("02d_glass_wall");
            pc.LocalTeleport(new Vector3(Cfg.MapHalf * 0.55f, 45f, Cfg.BaseCenter[team].z - 10f), 0f);
            pc.SetLook(-30f, 32f);
            yield return Shot("02c_map_overview");

            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 1000);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            foreach (var it in new[] { Item.BuildingPlan, Item.Bow, Item.Arrow, Item.Hatchet, Item.Spear, Item.Chest })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.2f);
            }
            me.ServerGive(Item.Berry, 6);

            // 3: rock (two-handed) idle, swing that connects (bounces up) and one that misses (follows through)
            ResourceNode tree = null;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Tree && (tree == null || (n.transform.position - me.transform.position).sqrMagnitude < (tree.transform.position - me.transform.position).sqrMagnitude)) tree = n;
            var tp = tree.transform.position;
            var away = (me.transform.position - tp); away.y = 0; away.Normalize();
            float yawToTree = Quaternion.LookRotation(-away).eulerAngles.y;
            pc.LocalTeleport(tp + away * 2f + Vector3.up * 0.1f, yawToTree);
            pc.SetLook(yawToTree, 12f);
            yield return Hold(me, Item.Rock);
            yield return Shot("03_rock_idle");
            pc.DebugSwing(Cfg.RockCooldown);
            yield return new WaitForSeconds(0.05f);
            Snap("04_rock_raised");
            yield return new WaitForSeconds(ViewModel.ImpactTime - 0.05f);
            pc.DebugImpact(true);
            yield return new WaitForSeconds(0.14f);
            Snap("05_rock_hit_bounce");
            yield return new WaitForSeconds(0.8f);
            pc.SetLook(yawToTree + 90f, 12f);
            pc.DebugSwing(Cfg.RockCooldown);
            yield return new WaitForSeconds(ViewModel.ImpactTime);
            pc.DebugImpact(false);
            yield return new WaitForSeconds(0.1f);
            Snap("06_rock_miss_follow");
            yield return new WaitForSeconds(0.8f);

            // 4: bow (Rust style, right hand) + ball
            pc.SetLook(yawToTree + 90f, 4f);
            yield return Hold(me, Item.Bow);
            yield return Shot("07_bow");
            pc.DebugDraw = 1f;
            yield return Shot("08_bow_drawn");
            pc.DebugDraw = -1f;
            while (Ball.Instance == null || NetGame.Instance.WallUp) yield return null; // (under the glass dome until the wall drops)
            yield return new WaitForSeconds(3f);
            pc.LocalTeleport(Ball.Instance.transform.position + new Vector3(0, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.5f);
            pc.SetLook(0f, 3f);
            yield return Shot("09_ball_in_hands");
            me.ThrowBallRpc(me.EyePos, Vector3.forward + Vector3.up * 0.3f, Vector3.zero);
            yield return new WaitForSeconds(0.25f);
            Snap("10_ball_thrown");
            yield return new WaitForSeconds(0.6f);

            // 5: items dropped on the ground (drag out of the inventory)
            yield return Hold(me, Item.Rock);
            pc.SetLook(0f, 35f);
            for (int i = Cfg.PlayerSlots - 1; i >= 0; i--)
            {
                var st = me.SlotAt(i);
                if (st.Empty || st.Id == Item.Arrow) continue;
                me.DropItemRpc(0, (byte)i, (ushort)Mathf.Min(st.Count, 200), default);
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.2f);
            Snap("11_items_tossed");
            yield return Shot("12_items_on_ground");

            // 6: third-person rig demo (walk / sprint / crouch / jump / swing)
            var demoRoot = me.transform.position + new Vector3(0, 0, 6f);
            var demos = new System.Collections.Generic.List<(Transform root, BodyAnimator anim, int mode)>();
            for (int m = 0; m < 5; m++)
            {
                var r = new GameObject("demo" + m).transform;
                r.position = demoRoot + new Vector3((m - 2) * 1.8f, 0, 0);
                r.rotation = Quaternion.Euler(0, 200f, 0);
                var anim = BodyAnimator.TryCreate(r, Cfg.ModelWidth, out _);
                if (anim == null) { Log("FAIL: rigged alien missing"); break; }
                demos.Add((r, anim, m));
            }
            pc.LocalTeleport(me.transform.position, 0f);
            pc.SetLook(0f, 8f);
            float t0 = Time.time;
            while (Time.time - t0 < 1.35f)
            {
                float dt = Time.deltaTime, t = Time.time - t0;
                foreach (var (r, anim, m) in demos)
                {
                    float speed = m == 0 ? 3f : m == 1 ? 7.5f : m == 2 ? 2f : 0f;
                    r.position += r.forward * speed * dt * 0f; // animate in place, but feed the speed below
                    var pose = new BodyAnimator.Pose { Crouch = m == 2, Holding = m == 4, TwoHanded = m == 4, Swing = m == 4 ? Mathf.Clamp01(1f - (t % 0.55f) / 0.55f) : 0f };
                    if (m == 3) r.position = demoRoot + new Vector3((m - 2) * 1.8f, 1.2f, 0);
                    anim.TickWithVelocity(pose, dt, r.forward * speed);
                }
                yield return null;
            }
            Snap("13_rig_walk_sprint_crouch_jump_swing");
            yield return new WaitForSeconds(0.3f);

            // 7: inventory, and crafting at the machine
            pc.MenuOpen = true;
            yield return Shot("14_inventory");
            pc.CloseMenu();
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            pc.MenuOpen = true;
            yield return Shot("15_machine_crafting");
            pc.CloseMenu();

            // 8: airdrop loot in hand
            foreach (var it in new[] { Item.C4, Item.DeathWand, Item.InvisPotion })
            {
                me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return Shot("16_" + it);
            }
            me.ServerGive(Item.Chainsaw, 1, Cfg.ChainsawUses);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Chainsaw);
            yield return Shot("16_Chainsaw");

            // 9: crossbow, building wheel, pause menu
            me.ServerGive(Item.Crossbow, 1, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Crossbow);
            yield return Shot("17_crossbow");
            if (me.Count(Item.BuildingPlan) == 0) me.ServerGive(Item.BuildingPlan, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.BuildingPlan);
            pc.WheelOpen = true;
            Hud.WheelOpened();
            yield return Shot("18_build_wheel");
            pc.WheelOpen = false;
            me.ServerGive(Item.TreeCamo, 1);
            yield return Hold(me, Item.TreeCamo);
            yield return new WaitForSeconds(1f);
            yield return Shot("18b_tree_camo_third_person");
            yield return Hold(me, Item.BuildingPlan);
            pc.Paused = true;
            yield return Shot("19_pause_menu");
            pc.Paused = false;

            // 10: car, horse and a fort tower
            yield return Hold(me, Item.Rock);
            var spot = me.transform.position;
            me.DevRpc(DevCmd.SpawnCar);
            me.DevRpc(DevCmd.SpawnHorse);
            yield return new WaitForSeconds(1.5f);
            pc.LocalTeleport(spot + me.transform.forward * -3f + Vector3.up * 0.2f, me.transform.eulerAngles.y);
            pc.SetLook(me.transform.eulerAngles.y, 12f);
            yield return Shot("20_car_and_horse");
            me.ServerGive(Item.FortTower, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.FortTower);
            var fwd2 = me.transform.forward;
            me.ThrowFortRpc(me.EyePos, fwd2 * 8f);
            yield return new WaitForSeconds(0.1f);
            me.FortLandRpc(spot + Quaternion.Euler(0, 50, 0) * fwd2 * 9f + Vector3.up);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("21_fort_tower");

            // 11: new airdrop items, the airstrike map, then the sudden death stadium
            foreach (var it in new[] { Item.Sniper, Item.PortalGun, Item.RocketLauncher, Item.GiantStaff })
            {
                me.ServerGive(it, 1, Mathf.Clamp(Cfg.MaxData(it), 0, 255));
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return Shot("22_" + it);
            }
            me.ServerGive(Item.Airstrike, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Airstrike);
            pc.OpenAirstrikeMap();
            yield return Shot("23_airstrike_map");
            pc.CloseAirstrikeMap();
            NetGame.Instance.DevSetTimeLeft(9.5f);
            yield return new WaitForSeconds(2f);
            Snap("24_last_seconds_countdown");
            while (NetGame.Instance.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(1.5f);
            Snap("25_stadium_countdown");
            yield return new WaitForSeconds(4.2f);
            Snap("26_stadium_fight");
            pc.SetLook(0f, -8f);
            yield return new WaitForSeconds(1.5f);
            Snap("27_stadium_crowd");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        IEnumerator Gather(PlayerNet me, PlayerController pc, byte kind, int hits, Vector3 near)
        {
            ResourceNode node = null;
            for (int h = 0; h < hits; h++)
            {
                if (node == null || node.Amount.Value <= 0)
                {
                    node = Nearest(kind, near);
                    if (node == null) { Log("FAIL: no resource node"); yield break; }
                    var dir = (near - node.transform.position);
                    dir.y = 0;
                    dir.Normalize();
                    pc.LocalTeleport(node.transform.position + dir * 2.3f + Vector3.up * 0.1f, Quaternion.LookRotation(-dir).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                }
                me.MeleeRpc(true, node.NetworkObject, node.transform.position + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.65f);
            }
        }

        static ResourceNode Nearest(byte kind, Vector3 p)
        {
            ResourceNode best = null;
            float bd = float.MaxValue;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n.Kind.Value != kind || n.Amount.Value <= 0) continue;
                float d = (n.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        static int CountStructures(PieceType t, int team)
        {
            int c = 0;
            foreach (var s in Structure.All) if (s.PType == t && s.Team.Value == team) c++;
            return c;
        }

        /// <summary>The n-th cell in the base (centre outwards) where a foundation + walls on +x/+z fit (the UFO is random).</summary>
        static void FreeCell(int team, int n, out int ci, out int cj)
        {
            var c = Cfg.BaseCenter[team];
            int i0 = BuildGrid.CellOf(c.x + 0.1f), j0 = BuildGrid.CellOf(c.z + 0.1f);
            for (int r = 0; r < 6; r++)
            for (int di = -r; di <= r; di++)
            for (int dj = -r; dj <= r; dj++)
            {
                if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                int i = i0 + di, j = j0 + dj;
                bool ok = true;
                for (int a = -1; a <= 2 && ok; a++)
                for (int b = -1; b <= 2 && ok; b++)
                    ok = Cfg.CellInBase(team, i + a, j + b) && !Cfg.CellBlocked(i + a, j + b);
                if (ok && n-- == 0) { ci = i; cj = j; return; }
            }
            ci = i0; cj = j0;
        }

        static int FindWorldItem(Item id)
        {
            foreach (var it in NetGame.Instance.Items) if (it.Stack.Id == id) return it.Id;
            return -1;
        }

        static System.Action s_LateTick;
        void LateUpdate() => s_LateTick?.Invoke();

        static void Check(bool ok, string what) => Log((ok ? "PASS: " : "FAIL: ") + what);
    }
}
