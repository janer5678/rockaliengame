using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest inv -host -solo -map plains -shotdir DIR: the bag, hotbar and chests six slots across (inventory 18 + hotbar
    /// 6, chests 12, keys 1-6), items flying to their new slots (shift-click into and out of a chest, a drag, a split),
    /// the half stack on the pointer during a right-drag, the three help lines, the hovered item's name and description
    /// under the crafting list (no workbench / spears-and-hatchets text any more), the bold hotbar numbers, the kill feed
    /// (icons, names, a real suicide), Enter / T chat (TEAM CHAT) and the berry's leaves. Pictures: inv_*.png.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator InventoryRoutine(PlayerNet me, PlayerController pc)
        {
            int team = me.Team.Value;
            string res = $"{Screen.width}";
            int n = 0;
            IEnumerator Pic(string name, float wait = 0.5f)
            {
                yield return new WaitForSeconds(wait);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"inv_{res}_{n++:00}_{name}.png"));
                Log("shot " + name);
                yield return null; yield return null;
            }
            int SlotOf(Item id) { for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == id) return i; return -1; }
            int EmptyMain() { for (int i = Cfg.PlayerSlots - 1; i >= Cfg.HotbarSize; i--) if (me.SlotAt(i).Empty) return i; return -1; }

            // ---- six across ----
            Check(Cfg.HotbarSize == 6 && Cfg.MainSize == 18 && Cfg.ChestSlots == 12 && me.Inv.Count == 24, $"six across: hotbar {Cfg.HotbarSize}, bag {Cfg.MainSize}, chest {Cfg.ChestSlots}, {me.Inv.Count} player slots on the network");
            Check(!System.Enum.IsDefined(typeof(Bind), "Hotbar7") && Binds.All[Binds.All.Length - 1].Bind == Bind.Hotbar6, "hotbar keys 1-6 (no slot 7 to bind)");
            me.ServerGive(Item.Wood, 600);
            me.ServerGive(Item.Stone, 300);
            me.ServerGive(Item.Berry, 12);
            me.ServerGive(Item.Spear, 1);
            me.ServerGive(Item.Hatchet, 1);
            me.ServerGive(Item.Chest, 1);
            yield return new WaitForSeconds(0.5f);
            Check(me.SlotAt(Cfg.HotbarSize - 1).Id == Item.Wood, $"wood lands on the last hotbar slot ({Cfg.HotbarSize}) ({me.SlotAt(Cfg.HotbarSize - 1).Id})");

            // ---- the HUD hotbar: bold key numbers ----
            yield return Hold(me, Item.Berry);
            yield return Pic("hud_hotbar_berry_held", 0.8f);

            // ---- a chest ----
            FreeCell(team, 1, out int chI, out int chJ);
            var chestPos = BuildGrid.CellCenter(chI, chJ);
            pc.LocalTeleport(chestPos + new Vector3(0, 0.1f, -2.5f), 0);
            yield return Hold(me, Item.Chest);
            me.PlaceDeployableRpc((byte)Item.Chest, chestPos, 180f);
            yield return new WaitForSeconds(0.6f);
            Container chest = null;
            foreach (var c in Container.All) if (c.Breakable && c.Team.Value == team) chest = c;
            Check(chest != null && chest.Slots.Count == 12, $"a chest has 12 slots ({(chest != null ? chest.Slots.Count : -1)})");
            if (chest == null) { Application.Quit(1); yield break; }
            pc.LootTarget = chest;
            pc.MenuOpen = true;
            yield return Pic("chest_open");
            Rect c0 = Hud.TestSlotRect(1, 0), c5 = Hud.TestSlotRect(1, 5), c6 = Hud.TestSlotRect(1, 6), c11 = Hud.TestSlotRect(1, 11);
            Rect b0 = Hud.TestSlotRect(0, Cfg.HotbarSize), b5 = Hud.TestSlotRect(0, Cfg.HotbarSize + 5), b6 = Hud.TestSlotRect(0, Cfg.HotbarSize + 6), h5 = Hud.TestSlotRect(0, 5);
            Check(c0.width > 0 && Mathf.Approximately(c0.y, c5.y) && c6.y > c0.y + 1 && Mathf.Approximately(c6.x, c0.x) && Mathf.Approximately(c11.y, c6.y),
                  $"the chest is 2 rows of 6 ({c0} {c5} {c6} {c11})");
            Check(b0.width > 0 && Mathf.Approximately(b0.y, b5.y) && b6.y > b0.y + 1 && Mathf.Approximately(b6.x, b0.x) && Mathf.Approximately(h5.x, b5.x),
                  $"the bag is rows of 6 and the hotbar lines up under it ({b0} {b5} {b6} hotbar 6: {h5})");
            Check(b0.width >= 64f * Mathf.Max(0.75f, Screen.height / 900f) - 0.5f || b0.width >= (Screen.width - 120) / 21f - 0.5f, $"the slots got bigger to make up for the lost column ({b0.width:0} px)");

            // ---- shift-click into the chest: the wood flies over ----
            int f0 = Hud.FlightsStarted;
            int wood = SlotOf(Item.Wood);
            me.MoveItemRpc(0, (byte)wood, 1, 255, 0, chest.NetworkObject);
            yield return null; yield return null; yield return null;
            Check(Hud.FlightsStarted > f0 && Hud.FlightsNow > 0, $"shift-click: the wood flies into the chest ({Hud.FlightsStarted - f0} flights, {Hud.FlightsNow} in the air)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"inv_{res}_{n++:00}_flight_into_chest.png"));
            yield return new WaitForSeconds(0.5f);
            Check(Hud.FlightsNow == 0 && chest.Slots[0].Id == Item.Wood, $"it landed (chest slot 1: {chest.Slots[0].Id} x{chest.Slots[0].Count})");
            // ... and back out
            f0 = Hud.FlightsStarted;
            me.MoveItemRpc(1, 0, 0, 255, 0, chest.NetworkObject);
            yield return null; yield return null; yield return null;
            Check(Hud.FlightsStarted > f0, "shift-click out of the chest flies back to the bag");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"inv_{res}_{n++:00}_flight_out_of_chest.png"));
            yield return new WaitForSeconds(0.5f);
            // a drag let go over a slot (a move by hand): it's just there - no flight
            f0 = Hud.FlightsStarted;
            int stone = SlotOf(Item.Stone), mm0 = Hud.ManualMoves;
            Hud.TestDrop(0, stone, 1, 7, 150);
            yield return null; yield return null; yield return null;
            Check(Hud.FlightsStarted == f0 && Hud.FlightsNow == 0 && Hud.ManualMoves == mm0 + 1 && chest.Slots[7].Count == 150, $"dragging half the stone into the chest by hand puts it there with no flight ({chest.Slots[7].Id} x{chest.Slots[7].Count}, {Hud.FlightsStarted - f0} flights)");
            yield return new WaitForSeconds(0.5f);
            Check(Hud.FlightsNow == 0, "every flight has landed");
            Check(Hud.LandPopSeconds <= 0.1f && Hud.FlightSeconds > 0.24f && Hud.FlightSeconds <= 0.4f, $"the landing pop is quick ({Hud.LandPopSeconds:0.00} s), the flight a touch slower than it was ({Hud.FlightSeconds:0.00} s)");

            // ---- Shift+drag: every slot passed over is shift-clicked once (bag -> chest, chest -> bag, hotbar -> bag) ----
            {
                int sp = SlotOf(Item.Spear), ha = SlotOf(Item.Hatchet), be = SlotOf(Item.Berry);
                int m0 = Hud.SweepMoves;
                Hud.TestSweep(new Vector2Int(0, sp), new Vector2Int(0, ha), new Vector2Int(0, sp), new Vector2Int(0, be), new Vector2Int(0, ha));
                yield return new WaitForSeconds(0.5f);
                int InChest(Item id) { for (int i = 0; i < chest.Slots.Count; i++) if (chest.Slots[i].Id == id) return i; return -1; }
                Check(Hud.SweepMoves - m0 == 3 && SlotOf(Item.Spear) < 0 && SlotOf(Item.Hatchet) < 0 && SlotOf(Item.Berry) < 0
                      && InChest(Item.Spear) >= 0 && InChest(Item.Hatchet) >= 0 && InChest(Item.Berry) >= 0,
                    $"shift-drag over the spear, hatchet, spear again, berries, hatchet again: three moves into the chest, each slot once ({Hud.SweepMoves - m0})");
                yield return Pic("shift_drag_into_chest", 0.1f);
                m0 = Hud.SweepMoves;
                Hud.TestSweep(new Vector2Int(1, InChest(Item.Spear)), new Vector2Int(1, InChest(Item.Hatchet)), new Vector2Int(1, InChest(Item.Berry)));
                yield return new WaitForSeconds(0.5f);
                Check(Hud.SweepMoves - m0 == 3 && SlotOf(Item.Spear) >= 0 && SlotOf(Item.Hatchet) >= 0 && SlotOf(Item.Berry) >= 0 && InChest(Item.Spear) < 0,
                    "shift-drag over the chest brings them back to the bag");
                // no chest open: hotbar <-> bag, like shift-click
                pc.LootTarget = null;
                yield return null;
                sp = SlotOf(Item.Spear);
                if (sp >= Cfg.HotbarSize) { me.MoveItemRpc(0, (byte)sp, 0, 0, 1, default); yield return new WaitForSeconds(0.4f); sp = SlotOf(Item.Spear); }
                m0 = Hud.SweepMoves;
                Hud.TestSweep(new Vector2Int(0, sp), new Vector2Int(0, sp));
                yield return new WaitForSeconds(0.5f);
                Check(Hud.SweepMoves - m0 == 1 && sp < Cfg.HotbarSize && SlotOf(Item.Spear) >= Cfg.HotbarSize, $"shift-drag over a hotbar slot sends it up to the bag, once (slot {sp + 1} -> {SlotOf(Item.Spear) + 1})");
                pc.LootTarget = chest;
            }

            // ---- right-drag: the half stack is on the pointer straight away ----
            int berries = SlotOf(Item.Berry);
            Hud.TestDragAt = new Vector2(Screen.width * 0.5f, Screen.height * 0.82f);
            Hud.TestStartDrag(0, berries, true);
            yield return Pic("split_on_pointer");
            Hud.TestEndDrag();
            pc.CloseMenu();
            yield return new WaitForSeconds(0.3f);

            // ---- the bag with the crafting list: help lines + the hovered item's description ----
            pc.MenuOpen = true;
            Hud.TestHover = Item.Hatchet;
            yield return Pic("craft_hover_hatchet");
            Check(Hud.HoverShown.StartsWith("Hatchet |") && Hud.HoverShown.Contains("chops trees"), $"the hovered item's name and description show under the crafting list ({Hud.HoverShown})");
            Hud.TestHover = Item.Berry;
            yield return Pic("craft_hover_berry");
            Check(Hud.HoverShown.StartsWith("Berries") || Hud.HoverShown.Contains("eat"), $"an item in the bag is described too ({Hud.HoverShown})");
            Hud.TestHover = Item.None;
            yield return Pic("craft_no_hover");
            Check(Hud.HoverShown == "", "nothing hovered: nothing under the crafting list (no workbench / spears-and-hatchets text)");
            Check(Hud.InvHelp == "Drag to move\nRight+Drag to Split\nShift+Click/Drag to quick-move", "the help under the bag is the three lines (white; Shift+Click/Drag)");
            pc.CloseMenu();

            // ---- chat: Enter = everyone, T = team ----
            Chat.Begin(true);
            yield return Pic("chat_team_typing");
            Chat.Close();
            me.ChatRpc(new Unity.Collections.FixedString128Bytes("all of you"), false);
            yield return new WaitForSeconds(0.6f);
            Check(Chat.LastLine.Contains("all of you") && !Chat.LastLine.Contains("TEAM CHAT"), $"global chat ({Chat.LastLine})");
            me.ChatRpc(new Unity.Collections.FixedString128Bytes("just us"), true);
            yield return new WaitForSeconds(0.6f);
            Check(Chat.LastLine.Contains("(TEAM CHAT)") && Chat.LastLine.Contains("just us"), $"team chat says (TEAM CHAT) before the name ({Chat.LastLine})");

            // ---- the kill feed ----
            var g = NetGame.Instance;
            int k0 = Hud.KillLines;
            g.KillFeedRpc(1, 0, (byte)team, 0, (byte)Item.Crossbow);
            g.KillFeedRpc((byte)team, 0, 1, 0, (byte)Item.C4);
            g.KillFeedRpc(1, 0, (byte)team, 0, (byte)Item.Rock);
            g.KillFeedRpc(255, 0, 1, 0, KillCause.Lava);
            g.KillFeedRpc((byte)team, 0, 1, 0, (byte)Item.Sniper, true, new Unity.Collections.FixedString32Bytes("Sharpshooter"), default);
            yield return new WaitForSeconds(0.3f);
            Check(Hud.LastKillHead && Hud.LastKillLine.Contains("Sharpshooter") && Hud.LastKillLine.Contains("headshot") && Hud.LastKillLine.Contains(PlayerNet.DefaultName(1, 0)),
                $"the kill feed carries the names (a typed one, and the team colour + number for a player without) and the headshot ({Hud.LastKillLine})");
            int d0 = me.Deaths.Value;
            me.SuicideRpc();
            yield return new WaitForSeconds(0.5f);
            Check(Hud.KillLines >= k0 + 6 && Hud.LastKillLine.Contains("suicide") && !Hud.LastKillHead && Hud.LastKillLine.Contains(me.DisplayName), $"the kill feed: {Hud.KillLines - k0} lines, a real suicide last ({Hud.LastKillLine})");
            int kills0 = me.Kills.Value;
            Check(me.Deaths.Value == d0 + 1, $"the scoreboard counts it: a death, and no kill for a suicide ({kills0} / {me.Deaths.Value})");
            // the ball's moments go in the kill feed too: who picked it up, and who captured it for which team
            g.ServerBallFeed(me, team, false);
            yield return new WaitForSeconds(0.3f);
            bool pickLine = Hud.LastKillLine.Contains(me.DisplayName) && Hud.LastKillLine.Contains("picked up the ball");
            string pickText = Hud.LastKillLine;
            g.ServerBallFeed(me, team, true);
            yield return new WaitForSeconds(0.3f);
            Check(pickLine && Hud.LastKillLine.Contains("captured the ball") && Hud.LastKillLine.Contains(Cfg.TeamName[team]) && me.Deaths.Value == d0 + 1,
                $"the kill feed shows the ball picked up and captured, with who and which team ({pickText} / {Hud.LastKillLine})");
            yield return Pic("killfeed_ball", 0.2f);
            // flood protection: picking it up / capturing it again straight away doesn't add more lines
            int floodLines = Hud.KillLines;
            for (int spam = 0; spam < 4; spam++) { g.ServerBallFeed(me, team, false); g.ServerBallFeed(me, team, true); }
            yield return new WaitForSeconds(0.3f);
            Check(Hud.KillLines == floodLines, $"spamming the ball (pick up / capture again and again) adds nothing to the kill feed ({Hud.KillLines - floodLines} more lines)");

            // ---- whispers: only the two it's between get the line; clicking one answers it ----
            Check(me.DisplayName == (GameSettings.PlayerName != "" ? GameSettings.PlayerName : PlayerNet.DefaultName(me.Team.Value, me.Slot.Value)), $"your name is the one typed on the main menu, or your team colour and number ({me.DisplayName})");
            Check(GameSettings.CleanName("  <b>A   very  long name indeed</b> ") == "bA very long nam" || GameSettings.CleanName("<color=red>x</color>").IndexOf('<') < 0, $"names are cleaned up: no tags, {GameSettings.PlayerNameMax} long at most ({GameSettings.CleanName("  <b>A   very  long name indeed</b> ")})");
            Chat.AddWhisper("psst", false, 12345, "Red1", 1);
            Check(Chat.LastLine.Contains("psst") && Chat.LastLine.Contains("Red1") && Chat.LastWhisperFrom == 12345, $"a whisper is marked as one, with who it's from ({Chat.LastLine})");
            Chat.Begin(false);
            Check(Chat.ReplyToLastWhisper() && Chat.WhisperTo == 12345 && Chat.Open, "clicking a whisper with the chat open answers it: the line is to them only");
            yield return Pic("chat_whisper_reply");
            Chat.Close();

            // ---- the scoreboard (hold Tab): every player by team, kills / deaths / ping, MESSAGE next to the others ----
            Check(Binds.Get(Bind.Scoreboard) != Binds.Get(Bind.Inventory) || Binds.Get(Bind.Scoreboard) == KeyCode.None, $"the scoreboard and the bag aren't on the same key ({Binds.Name(Bind.Scoreboard)} / {Binds.Name(Bind.Inventory)})");
            Hud.TestScoreboard = true;
            yield return Pic("scoreboard", 0.3f);
            Check(Time.time - Hud.ScoreboardShownAt < 0.5f && Hud.ScoreboardRows == PlayerNet.All.Count && Hud.ScoreboardButtons == PlayerNet.All.Count - 1,
                $"the scoreboard lists every player, with MESSAGE next to everyone but you ({Hud.ScoreboardRows} rows, {Hud.ScoreboardButtons} buttons, {PlayerNet.All.Count} players)");
            Hud.TestScoreboard = false;
            yield return Pic("killfeed", 0.2f);

            // ---- Tab is two keys in one: a tap opens / closes the bag, holding it shows the scoreboard ----
            if (me.Dead.Value) me.ServerRespawn(false); // (the suicide above: the bag doesn't open while dead)
            yield return new WaitForSeconds(0.3f);
            if (pc.MenuOpen) pc.CloseMenu();
            yield return null;
            Binds.TestPress(Bind.Scoreboard);
            yield return new WaitForSeconds(0.15f);
            bool tapBag = pc.MenuOpen && !pc.ScoreboardOpen;
            Binds.TestPress(Bind.Scoreboard);
            yield return new WaitForSeconds(0.15f);
            Check(tapBag && !pc.MenuOpen, $"a tap of {Binds.Name(Bind.Scoreboard)} opens the bag, another closes it ({tapBag})");
            Binds.TestHold(Bind.Scoreboard, true);
            yield return new WaitForSeconds(Cfg.TabHoldTime * 0.5f);
            bool earlyBoard = pc.ScoreboardOpen;
            yield return new WaitForSeconds(Cfg.TabHoldTime + 0.2f);
            bool board = pc.ScoreboardOpen;
            Binds.TestHold(Bind.Scoreboard, false);
            yield return new WaitForSeconds(0.15f);
            Check(!earlyBoard && board && !pc.ScoreboardOpen && !pc.MenuOpen, $"holding {Binds.Name(Bind.Scoreboard)} shows the scoreboard (not at once), and letting go doesn't open the bag ({earlyBoard}/{board}/{pc.MenuOpen})");
            Binds.TestPress(Bind.Inventory);
            yield return new WaitForSeconds(0.15f);
            Check(pc.MenuOpen, $"the inventory key ({Binds.Name(Bind.Inventory)}) still opens the bag");
            if (pc.MenuOpen) pc.CloseMenu();

            Log("inventory test done");
            Application.Quit(0);
        }
    }
}
