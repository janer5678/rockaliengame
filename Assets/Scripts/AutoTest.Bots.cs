using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest backtolobby (a host and a client): the ship lobby, both READY (and a bot the host adds), the match
        /// starts, the host ends it - and a few seconds after the result everyone is back in the ship lobby: the host's
        /// session restarted, the client back in by itself, the bot back on its team.
        /// </summary>
        IEnumerator BackToLobbyRoutine()
        {
            var nm = NetworkManager.Singleton;
            float until = Time.time + 60f;
            while ((PlayerNet.Local == null || NetGame.Instance == null || !NetGame.Instance.IsSpawned) && Time.time < until) yield return null;
            if (PlayerNet.Local == null) { Log("FAIL: never got into the lobby"); Application.Quit(2); yield break; }
            bool host = nm.IsHost;
            Check(ShipLobby.Active, "in the ship lobby to start with");
            // the host waits for the client, adds a bot; both press READY
            if (host)
            {
                until = Time.time + 40f;
                while (PlayerNet.All.Count < 2 && Time.time < until) yield return null;
                PlayerNet.ServerAddBot();
            }
            yield return new WaitForSeconds(2f);
            PlayerNet.Local.LobbyReadyRpc(true);
            until = Time.time + 30f;
            while (NetGame.Instance != null && NetGame.Instance.S == GameState.Waiting && Time.time < until) yield return null;
            Check(NetGame.Instance != null && NetGame.Instance.S != GameState.Waiting, "everyone READY (the bot too): the match starts");
            yield return new WaitForSeconds(2f);
            if (host) NetGame.Instance.EndGame(PlayerNet.Local.Team.Value, "test over");
            // the result, then back to the lobby: a new session, the same people
            until = Time.time + 45f;
            bool left = false;
            while (Time.time < until)
            {
                if (NetGame.Instance == null || !NetGame.Instance.IsSpawned) left = true;
                if (left && ShipLobby.Active && NetGame.Instance != null && NetGame.Instance.S == GameState.Waiting) break;
                yield return null;
            }
            Check(left && ShipLobby.Active, $"back in the ship lobby after the match ({(host ? "the host restarted" : "the client rejoined by itself")})");
            yield return new WaitForSeconds(4f);
            if (host)
            {
                Check(PlayerNet.BotCount == 1, $"the bot is back too ({PlayerNet.BotCount})");
                until = Time.time + 20f;
                while (PlayerNet.All.Count < 3 && Time.time < until) yield return null;
                Check(PlayerNet.All.Count == 3, $"everyone's back: {PlayerNet.All.Count} players (host, client, bot)");
            }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"backtolobby_{(host ? "host" : "client")}.png"));
            yield return new WaitForSeconds(host ? 6f : 1f);
            Log("back to lobby test done");
            Application.Quit(0);
        }

        /// <summary>
        /// -autotest bots -host -solo: the AI bots (PlayerNet.Bot.cs, BotBrain*.cs). One is added on the enemy team: it's a
        /// player like any other (named, on its team, always ready, a BotBrain on the server). While the wall's up it gets
        /// on with things on its own side - it moves about and farms trees (its wood goes up). Put right next to us while
        /// we're in tree camo it leaves us alone (to a bot that's a tree); out of the camo it comes for us and hits us.
        /// Removing it takes it out.
        /// </summary>
        IEnumerator BotsRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            g.TimerPaused.Value = true; // (the wall stays up the whole test)
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            int before = PlayerNet.All.Count;
            bool added = PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.6f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            Check(added && bot != null && PlayerNet.All.Count == before + 1, $"a bot joined ({PlayerNet.All.Count - before} new player)");
            if (bot == null) { Application.Quit(1); yield break; }
            var brain = bot.GetComponent<BotBrain>();
            Check(bot.Team.Value == enemy && bot.LobbyReady.Value && bot.DisplayName.StartsWith("Bot") && brain != null && !bot.Mine && PlayerNet.Local == me,
                $"it's on the enemy team ({bot.Team.Value}), ready, named {bot.DisplayName}, run by a BotBrain - and not this PC's player");
            if (brain == null) { Application.Quit(1); yield break; }
            // it moves about on its own (the wall's up: on its own side)
            if (bot.Dead.Value) bot.ServerRespawn(false);
            yield return new WaitForSeconds(0.5f);
            var p0 = bot.transform.position;
            yield return new WaitForSeconds(3f);
            Check(Vector3.Distance(bot.transform.position, p0) > 1.5f, $"it moves about on its own ({Vector3.Distance(bot.transform.position, p0):0.0} m in 3 s, {brain.Job})");
            // it farms: its wood goes up
            var wood = Cfg.GatherItem(Item.Wood);
            int w0 = bot.Count(wood);
            float until = Time.time + 45f;
            while (Time.time < until && bot.Count(wood) <= w0) yield return null;
            Check(bot.Count(wood) > w0, $"while the wall's up it farms trees: +{bot.Count(wood) - w0} {wood} ({brain.FarmHits} hits, {brain.Job}, a {brain.Leaning})");
            Check(Cfg.RegionOf(bot.transform.position) == Cfg.RegionOf(Cfg.BaseCenter[enemy]), "...on its own side of the glass");
            yield return Snap("bots_farming");
            // in tree camo right next to it: it doesn't see us
            me.Health.Value = Cfg.MaxHealth;
            me.ServerGive(Item.TreeCamo, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.TreeCamo);
            var near = me.transform.position + me.transform.forward * 4f;
            near.y = MapBuilder.Height(near.x, near.z) + 0.1f;
            bot.TeleportRpc(near, 0f);
            yield return new WaitForSeconds(5f);
            Check(me.TreeCamo && me.Health.Value >= Cfg.MaxHealth && brain.Job != BotBrain.Task.Fight,
                $"we're in tree camo right by it: it leaves us alone ({me.Health.Value:0} HP, it's on {brain.Job})");
            // out of the camo: it comes for us and hits
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.TreeCamo) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
            me.Health.Value = Cfg.MaxHealth;
            bot.TeleportRpc(near, 0f);
            until = Time.time + 8f;
            while (Time.time < until && me.Health.Value >= Cfg.MaxHealth) yield return null;
            Check(me.Health.Value < Cfg.MaxHealth, $"out of the camo, put next to us, it attacks ({me.Health.Value:0} HP, {brain.Job})");
            pc.SetLook(Quaternion.LookRotation(bot.transform.position - me.transform.position).eulerAngles.y, 5f);
            yield return Snap("bots_attacking");
            me.Health.Value = Cfg.MaxHealth;
            Check(PlayerNet.ServerRemoveBot(), "the bot can be taken out");
            yield return new WaitForSeconds(0.5f);
            Check(PlayerNet.BotCount == 0 && PlayerNet.All.Count == before, "...and it's gone");
            Log("bots test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
