using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// AI bot players: the host adds them in the ship lobby (ADD BOT / REMOVE BOT, Hud.Lobby.cs) and they take a place on
    /// a team like anyone else - they sit on the couch, are always READY, play the match and come back with everyone to
    /// the lobby after it. A bot is an ordinary player object owned by the host with Bot set: nothing of it is "this PC's
    /// player" (Mine), its owner-only messages go nowhere, and a BotBrain (BotBrain*.cs) plays the game for it on the server.
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>This player is an AI bot run by the host.</summary>
        public readonly NetworkVariable<bool> Bot = new NetworkVariable<bool>();

        /// <summary>This PC's own player (the host owns its bots too, but they aren't "mine").</summary>
        public bool Mine => IsOwner && !Bot.Value;

        static int s_NextBotTeam = -1;
        static int s_BotNumber;

        /// <summary>How many bots there are right now.</summary>
        public static int BotCount { get { int n = 0; foreach (var p in All) if (p != null && p.IsSpawned && p.Bot.Value) n++; return n; } }

        /// <summary>Server: put a bot in, on this team (-1: the team with the most room). False if every team's full.</summary>
        public static bool ServerAddBot(int team = -1)
        {
            var boot = Bootstrap.I;
            var nm = NetworkManager.Singleton;
            if (boot == null || nm == null || !nm.IsServer || boot.playerPrefab == null) return false;
            if (team < 0)
            {
                float best = float.MaxValue;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int n = 0;
                    foreach (var p in All) if (p != null && p.Team.Value == t) n++;
                    if (n >= Cfg.TeamCap(t)) continue;
                    float fill = n / (float)Cfg.TeamCap(t);
                    if (fill < best) { best = fill; team = t; }
                }
                if (team < 0) return false;
            }
            else
            {
                int n = 0;
                foreach (var p in All) if (p != null && p.Team.Value == team) n++;
                if (n >= Cfg.TeamCap(team)) return false;
            }
            var go = Object.Instantiate(boot.playerPrefab, Cfg.ArenaCenter + new Vector3(Random.Range(-6f, 6f), 0.1f, Random.Range(-6f, 6f)), Quaternion.identity);
            var pn = go.GetComponent<PlayerNet>();
            pn.Bot.Value = true;
            s_NextBotTeam = team;
            go.GetComponent<NetworkObject>().Spawn(true);
            s_NextBotTeam = -1;
            s_BotNumber++;
            pn.PlayerName.Value = new FixedString32Bytes("Bot " + s_BotNumber);
            pn.LobbyReady.Value = true;
            pn.m_LobbyReady = true;
            pn.Hat.Value = (byte)Random.Range(0, Cosmetics.HatCount);
            return true;
        }

        /// <summary>Server: take the newest bot out (of this team; -1: any team). False: there are none.</summary>
        public static bool ServerRemoveBot(int team = -1)
        {
            PlayerNet last = null;
            foreach (var p in All) if (p != null && p.IsSpawned && p.Bot.Value && (team < 0 || p.Team.Value == team)) last = p;
            if (last == null) return false;
            last.NetworkObject.Despawn(true);
            return true;
        }

        /// <summary>Server: every bot's team, to put them back in after the match restarts (Bootstrap.ServerBackToLobby).</summary>
        public static List<int> BotTeams()
        {
            var l = new List<int>();
            foreach (var p in All) if (p != null && p.IsSpawned && p.Bot.Value) l.Add(p.Team.Value);
            return l;
        }

        /// <summary>Server: a bot swings what it's holding (everyone sees it, the host too).</summary>
        public void BotSwing()
        {
            m_Swing = 1f;
            Sfx.Play(Sfx.Swing, transform.position + Vector3.up * 1.2f, 0.7f, 0.08f, 45f);
            SwingRpc();
        }

        /// <summary>Server: a bot hits a player (the same as a melee hit: damage, headshots, blood, the kill feed).</summary>
        public void BotHit(PlayerNet p, float damage, Vector3 point) => ServerHitPlayer(p, damage, point, (point - EyePos).normalized);

        /// <summary>
        /// Server: a bot swings what it's holding at `target` (null: at nothing - a miss, or a Bedwars machine at `point`).
        /// It goes through MeleeRpc, the same as a player's swing, so the held item's damage, reach, cooldown, gathering,
        /// structure damage and every other rule apply. The host sees the swing too (MeleeRpc only tells the other screens).
        /// </summary>
        public void BotMelee(NetworkObject target, Vector3 point, bool weak)
        {
            m_Swing = 1f;
            Sfx.Play(Sfx.Swing, transform.position + Vector3.up * 1.2f, 0.7f, 0.08f, 45f);
            MeleeRpc(target != null, target != null ? new NetworkObjectReference(target) : default, point, weak);
        }

        /// <summary>A bot put somewhere (the server moves it: it's the bot's owner).</summary>
        void BotTeleport(Vector3 pos, float yaw)
        {
            var cc = GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            var rot = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(pos, rot);
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt != null && nt.IsSpawned) nt.Teleport(pos, rot, transform.localScale);
            if (cc != null) cc.enabled = was;
            Physics.SyncTransforms();
            if (TryGetComponent(out BotBrain b)) b.Teleported();
        }
    }
}
