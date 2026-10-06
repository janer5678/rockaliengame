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
    /// player" (Mine), its owner-only messages go nowhere, and a BotBrain moves it about and fights on the server.
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

        /// <summary>Server: take the newest bot out (false: there are none).</summary>
        public static bool ServerRemoveBot()
        {
            PlayerNet last = null;
            foreach (var p in All) if (p != null && p.IsSpawned && p.Bot.Value) last = p;
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

    /// <summary>
    /// The server's driver for a bot: back to life when it can, then off after the nearest enemy it can get at - running
    /// at them, swinging its rock when close (it misses now and then) - or, with nobody about, roaming: round its base
    /// while the wall's up, then out across the map towards the enemy. It walks like a player (the same character
    /// controller, speeds and jump) and jumps or sidesteps when it gets stuck.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        PlayerNet m_P;
        CharacterController m_CC;
        float m_VelY, m_NextSwing, m_NextThink, m_StuckCheck, m_SideUntil, m_SideSign = 1f;
        Vector3 m_Goal, m_LastPos;
        PlayerNet m_Target;

        void Awake()
        {
            m_P = GetComponent<PlayerNet>();
            m_CC = GetComponent<CharacterController>();
            m_LastPos = transform.position;
        }

        public void Teleported() { m_VelY = 0f; m_LastPos = transform.position; m_Goal = transform.position; }

        void Update()
        {
            var g = NetGame.Instance;
            if (m_P == null || !m_P.IsServer || g == null || !g.IsSpawned) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (m_P.Dead.Value)
            {
                // back to life at home as soon as it's allowed (before the wall drops the game does it for everyone)
                if (g.S == GameState.BallLive && NetworkManager.Singleton.ServerTime.Time >= m_P.RespawnAt.Value && g.CanRespawn(m_P.Team.Value)) m_P.ServerRespawn(false);
                return;
            }
            if (m_CC == null || !m_CC.enabled || g.S == GameState.GameOver || g.FightFrozen || VictoryCutscene.Active) return;
            bool lobby = g.S == GameState.Waiting;
            // think a few times a second: who to go for, where to go
            if (Time.time >= m_NextThink)
            {
                m_NextThink = Time.time + 0.3f;
                m_Target = lobby ? null : NearestEnemy(40f);
                if (m_Target == null && (m_Goal - transform.position).sqrMagnitude < 4f || m_Goal == Vector3.zero) m_Goal = Roam(g, lobby);
            }
            var goal = m_Target != null ? m_Target.transform.position : m_Goal;
            var to = goal - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            bool chasing = m_Target != null;
            // move: run at a target, walk when roaming; stop within swinging reach
            var wish = Vector3.zero;
            if (dist > (chasing ? 1.7f : 0.6f)) wish = to / Mathf.Max(dist, 0.001f);
            if (Time.time < m_SideUntil) wish = (wish + Vector3.Cross(Vector3.up, wish) * m_SideSign).normalized;
            float speed = lobby ? Cfg.WalkSpeed * 0.5f : chasing ? Cfg.SprintSpeed * 0.92f : Cfg.WalkSpeed;
            if (m_CC.isGrounded) m_VelY = Mathf.Max(m_VelY, -1f); else m_VelY -= Cfg.Gravity * dt;
            m_CC.Move((wish * speed + Vector3.up * m_VelY) * dt);
            if (to.sqrMagnitude > 0.01f)
            {
                var face = Quaternion.LookRotation(to.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, 540f * dt);
            }
            // stuck (a wall, a rock): jump, and sidestep for a moment
            if (Time.time >= m_StuckCheck)
            {
                m_StuckCheck = Time.time + 0.8f;
                if (wish.sqrMagnitude > 0.1f && (transform.position - m_LastPos).sqrMagnitude < 0.25f * 0.25f)
                {
                    if (m_CC.isGrounded) m_VelY = Cfg.JumpSpeed;
                    m_SideUntil = Time.time + 0.9f;
                    m_SideSign = Random.value < 0.5f ? -1f : 1f;
                    if (m_Target == null) m_Goal = Roam(g, lobby);
                }
                m_LastPos = transform.position;
            }
            // swing at a target in reach (the rock's cooldown, with a bit of hesitation; it misses now and then)
            if (chasing && dist < 2.4f && Time.time >= m_NextSwing && g.S != GameState.Waiting)
            {
                var st = Cfg.Melee(Item.Rock);
                m_NextSwing = Time.time + st.Cooldown + Random.Range(0.1f, 0.45f);
                m_P.BotSwing();
                if (Random.value < 0.72f)
                {
                    bool head = Random.value < 0.12f;
                    var point = m_Target.transform.position + Vector3.up * (head ? 1.75f : 1.15f);
                    m_P.BotHit(m_Target, st.PlayerDamage, point);
                }
            }
        }

        /// <summary>The nearest living enemy it could get at (not behind the glass, not invisible).</summary>
        PlayerNet NearestEnemy(float range)
        {
            PlayerNet best = null;
            float bd = range * range;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p == m_P || !p.IsSpawned || p.Dead.Value || p.Team.Value == m_P.Team.Value || p.Hidden) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d >= bd || PlayerNet.GlassBetween(transform.position, p.transform.position)) continue;
                bd = d; best = p;
            }
            return best;
        }

        /// <summary>Somewhere to wander: about the stadium while waiting, round its own base while the wall's up, then
        /// across the map towards an enemy base.</summary>
        Vector3 Roam(NetGame g, bool lobby)
        {
            int team = m_P.Team.Value;
            if (lobby) return Cfg.ArenaCenter + new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
            if (g.WallUp || g.S == GameState.PreBall)
                return Cfg.BaseCenter[team] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * Cfg.BaseHalf * 0.8f;
            int enemy = team;
            for (int i = 0; i < 8 && enemy == team; i++) enemy = Random.Range(0, Cfg.TeamCount);
            if (enemy == team) return Vector3.zero;
            return Vector3.Lerp(Vector3.zero, Cfg.BaseCenter[enemy], Random.Range(0.3f, 0.95f)) + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
        }
    }
}
