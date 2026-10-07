using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The server's driver for a bot (PlayerNet.Bot.cs): it plays the match like a person would. A few times a second it
    /// thinks (BotBrain.Think.cs) - what's going on, what it needs, what its personality likes - and picks a job: farm
    /// trees, eat berries when hurt, take its wood home to a chest, craft better gear, build walls round its machine, go
    /// for the ball, carry it home, raid an enemy base for it, defend, hunt, or fight whoever it can see (never anyone
    /// hiding in tree camo). Every frame it then walks there like a player (the same character controller, speeds,
    /// jump and slide), steering round things, and does the job's actions through the same server calls a player's
    /// input ends up in (MeleeRpc, CraftRpc, PlaceRpc, MoveItemRpc, PickupBallRpc...), so every rule and price applies.
    /// In the waiting stadium / ship lobby it just ambles about.
    /// </summary>
    public partial class BotBrain : MonoBehaviour
    {
        /// <summary>What a bot is doing (tests and debugging can read it: Job).</summary>
        public enum Task : byte { Idle, Lounge, Wander, Farm, Berries, Store, Craft, Deploy, Build, Fetch, Carry, Escort, Raid, Defend, Hunt, Fight, Retreat }
        /// <summary>A bot's leaning in a team: raiders go for the enemy, gatherers build up the economy, defenders hold the base.</summary>
        public enum Bent : byte { Raider, Gatherer, Defender }

        PlayerNet m_P;
        CharacterController m_CC;

        // ---- personality (from the bot's network id, so each bot is a bit different) ----
        bool m_Init;
        Bent m_Bent;
        float m_Aggro, m_Skill, m_RetreatAt, m_StoreAt, m_Keep, m_Playful, m_ThinkEvery;
        float m_SideSign = 1f;

        // ---- the job ----
        Task m_Task;
        /// <summary>What the bot is doing right now.</summary>
        public Task Job => m_Task;
        /// <summary>Its leaning (raider / gatherer / defender).</summary>
        public Bent Leaning => m_Bent;
        float m_NextThink, m_TaskUntil;

        // ---- moving ----
        Vector3 m_Goal, m_LastPos, m_LookAt;
        bool m_HasLook, m_Sprint, m_Arrived;
        float m_Stop = 0.8f;
        float m_VelY, m_StuckCheck, m_SideUntil, m_StrafeFlip, m_StrafeSign = 1f;
        bool m_Strafe;
        int m_Stuck;
        // steering round things in the way (re-checked a few times a second)
        float m_NextSteer, m_SteerUntil;
        Vector3 m_SteerDir;
        // the slide (a client-predicted move for players: a bot plays it out here and shows it with Action = Slide)
        float m_SlideUntil, m_NextSlide, m_SlideSpeed;
        Vector3 m_SlideDir;
        // what's right in front of it (for breaking through enemy walls and opening its own doors)
        Collider m_Blocker;
        Vector3 m_BlockPoint;
        float m_BlockedAt = -10f;

        /// <summary>(tests) how many times it has slid / jumped.</summary>
        public int Slides { get; private set; }
        public int Jumps { get; private set; }

        const float ReachTree = 1.25f;

        static int Mask => ~(1 << PlayerNet.HitboxLayer);

        void Awake()
        {
            m_P = GetComponent<PlayerNet>();
            m_CC = GetComponent<CharacterController>();
            m_LastPos = transform.position;
            m_Goal = transform.position;
        }

        /// <summary>Put somewhere new (a respawn, the match starting, a test): forget the way it was going.</summary>
        public void Teleported()
        {
            m_VelY = 0f;
            m_LastPos = transform.position;
            m_Goal = transform.position;
            m_Task = Task.Idle;
            m_TaskUntil = 0f;
            m_NextThink = 0f;
            m_Stuck = 0;
            m_SlideUntil = 0f;
            m_Node = null;
            m_Target = null;
            m_Blocker = null;
        }

        void InitPersonality()
        {
            m_Init = true;
            // a stable little random generator per bot: the same bot keeps its character all match
            var r = new System.Random((int)(m_P.NetworkObjectId * 2654435761u % int.MaxValue) ^ 0x5bd1e995);
            float F() => (float)r.NextDouble();
            m_Bent = (Bent)r.Next(0, 3);
            m_Aggro = Mathf.Lerp(0.25f, 1f, F());
            m_Skill = Mathf.Lerp(0.35f, 0.95f, F());
            m_RetreatAt = Mathf.Lerp(0.22f, 0.45f, 1f - m_Aggro * 0.6f);
            m_StoreAt = Mathf.Lerp(350f, 750f, F());
            m_Keep = Mathf.Lerp(120f, 260f, F());
            m_Playful = Mathf.Lerp(0.4f, 1.6f, F());
            m_ThinkEvery = Mathf.Lerp(0.5f, 0.85f, F());
            m_SideSign = r.Next(0, 2) == 0 ? -1f : 1f;
            m_NextSlide = Time.time + Random.Range(2f, 6f);
        }

        void Update()
        {
            var g = NetGame.Instance;
            if (m_P == null || !m_P.IsServer || !m_P.IsSpawned || g == null || !g.IsSpawned) return;
            if (!m_Init) InitPersonality();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (m_P.Dead.Value)
            {
                SetAction(BodyAnimator.Act.None);
                m_Task = Task.Idle;
                m_EatAt = -1f;
                // back to life at home as soon as it's allowed (before the wall drops the game does it for everyone)
                if (g.S == GameState.BallLive && NetworkManager.Singleton.ServerTime.Time >= m_P.RespawnAt.Value && g.CanRespawn(m_P.Team.Value)) m_P.ServerRespawn(false);
                return;
            }
            if (m_CC == null || !m_CC.enabled || g.S == GameState.GameOver || g.FightFrozen || VictoryCutscene.Active) { SetAction(BodyAnimator.Act.None); return; }
            // added once the match had begun (it came in at the waiting stadium): off home to its base
            if ((g.S == GameState.PreBall || g.S == GameState.BallLive) && SpaceArena.NearArena(transform.position)) { m_P.ServerSendHome(); return; }
            bool lobby = g.S == GameState.Waiting;
            if (Time.time >= m_NextThink)
            {
                m_NextThink = Time.time + m_ThinkEvery * Random.Range(0.85f, 1.15f);
                Think(g, lobby);
            }
            m_Arrived = false;
            m_HasLook = false;
            m_Strafe = false;
            if (!lobby) Tick(g, dt); // the job's actions (swings, pick-ups, crafting...) - it may say it has arrived
            Move(g, lobby, dt);
        }

        // =====================================================================
        // Moving
        // =====================================================================

        /// <summary>Head for `goal` (stopping `stop` metres short), running or not.</summary>
        void GoTo(Vector3 goal, float stop, bool sprint)
        {
            m_Goal = Safe(goal);
            m_Stop = stop;
            m_Sprint = sprint;
        }

        /// <summary>A goal it can really walk to: on the map (clear of the edge), and on its own side of the glass wall while it's up.</summary>
        Vector3 Safe(Vector3 p)
        {
            var g = NetGame.Instance;
            if (g != null && (g.S == GameState.Waiting || g.S == GameState.SuddenDeath) && SpaceArena.NearArena(transform.position))
            {
                // the stadium / sudden death platform in space: stay well on it
                for (int i = 0; i < 6 && !SpaceArena.OverPlatform(p); i++) p = Vector3.Lerp(p, Cfg.ArenaCenter, 0.35f);
                return p;
            }
            float lim = Cfg.MapHalf - 8f;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            if (MapBuilder.GlassUp)
            {
                int region = Cfg.RegionOf(transform.position);
                var home = Cfg.BaseCenter[region];
                for (int i = 0; i < 8 && (Cfg.RegionOf(p) != region || Cfg.RegionOf(p + (p - home).normalized * 2f) != region); i++)
                    p = Vector3.Lerp(p, home, 0.3f);
            }
            return p;
        }

        void Move(NetGame g, bool lobby, float dt)
        {
            var pos = transform.position;
            var to = m_Goal - pos;
            to.y = 0f;
            float dist = to.magnitude;
            var wish = !m_Arrived && dist > m_Stop ? to / Mathf.Max(dist, 0.001f) : Vector3.zero;
            // fighting up close: circle the target, now one way, now the other
            if (m_Strafe && !lobby)
            {
                if (Time.time >= m_StrafeFlip) { m_StrafeFlip = Time.time + Random.Range(0.5f, 1.4f); m_StrafeSign = Random.value < 0.5f ? -1f : 1f; }
                var side = Vector3.Cross(Vector3.up, to.sqrMagnitude > 0.01f ? to.normalized : transform.forward) * m_StrafeSign;
                wish = (wish + side * 0.85f).normalized;
            }
            if (Time.time < m_SideUntil && wish.sqrMagnitude > 0.01f) wish = (wish + Vector3.Cross(Vector3.up, wish) * m_SideSign).normalized;
            if (wish.sqrMagnitude > 0.01f && !lobby) wish = Steer(wish, dist);
            // never off the platform in space (the waiting stadium, sudden death)
            if (wish.sqrMagnitude > 0.01f && SpaceArena.NearArena(pos) && !SpaceArena.OverPlatform(pos + wish * 1.5f))
            {
                var back = Cfg.ArenaCenter - pos;
                back.y = 0f;
                wish = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.zero;
            }

            float speed = lobby ? Cfg.WalkSpeed * 0.5f : m_Sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
            if (m_P.CarryingBall) speed *= Cfg.BallCarrySpeedMul;
            bool grounded = m_CC.isGrounded;
            var vel = wish * speed;

            // the slide: now and then, running flat out a long way, it drops into one (a boost that bleeds away)
            if (Time.time < m_SlideUntil)
            {
                m_SlideSpeed = Mathf.MoveTowards(m_SlideSpeed, 0f, (Cfg.SprintSpeed + Cfg.SlideBoost) / Mathf.Lerp(0.5f, 1.6f, Cfg.SlideSlipperiness / 10f) * dt);
                m_SlideDir = Vector3.RotateTowards(m_SlideDir, wish.sqrMagnitude > 0.01f ? wish : m_SlideDir, Cfg.SlideSteer * Mathf.Deg2Rad * dt, 0f);
                vel = m_SlideDir * m_SlideSpeed;
                if (m_SlideSpeed < Cfg.SlideMinSpeed || !grounded) m_SlideUntil = 0f;
            }
            else if (!lobby && m_Sprint && grounded && dist > 9f && Time.time >= m_NextSlide && Time.time >= m_SteerUntil && !m_P.CarryingBall && !Eating
                     && Vector3.Dot(wish, transform.forward) > 0.9f && Random.value < 0.6f * dt * m_Playful)
            {
                m_SlideUntil = Time.time + 1.6f;
                m_SlideSpeed = Mathf.Min(Cfg.SprintSpeed + Cfg.SlideBoost, Cfg.SlideMaxSpeed);
                m_SlideDir = wish;
                m_NextSlide = Time.time + Cfg.SlideBoostCooldown + Random.Range(3f, 9f) / m_Playful;
                Slides++;
                Sfx.Play(Sfx.Slide, pos, 0.5f, 0.08f, 35f);
            }
            bool sliding = Time.time < m_SlideUntil;
            if (!sliding && Action == BodyAnimator.Act.Slide) SetAction(BodyAnimator.Act.None);
            else if (sliding) SetAction(BodyAnimator.Act.Slide);

            if (grounded) m_VelY = Mathf.Max(m_VelY, -1f); else m_VelY -= Cfg.Gravity * dt;
            m_CC.Move((vel + Vector3.up * m_VelY) * dt);

            // face where it's looking (a target, a tree) or else where it's going
            var face = m_HasLook ? m_LookAt - pos : (vel.sqrMagnitude > 0.1f ? vel : to);
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face.normalized), 540f * dt);
            // and tip its head up / down at it
            float pitch = 0f;
            if (m_HasLook)
            {
                var d = m_LookAt - m_P.EyePos;
                float flat = new Vector2(d.x, d.z).magnitude;
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, Mathf.Max(flat, 0.1f)) * Mathf.Rad2Deg, -60f, 60f);
            }
            if (Mathf.Abs(m_P.Pitch.Value - pitch) > 3f) m_P.Pitch.Value = pitch;

            // stuck (a wall, a rock, a tree): jump, and sidestep for a moment; stuck for long, give up on the goal
            if (Time.time >= m_StuckCheck)
            {
                m_StuckCheck = Time.time + 0.8f;
                if (wish.sqrMagnitude > 0.1f && (transform.position - m_LastPos).sqrMagnitude < 0.25f * 0.25f)
                {
                    Jump();
                    m_SideUntil = Time.time + 0.9f;
                    m_SideSign = Random.value < 0.5f ? -1f : 1f;
                    if (++m_Stuck >= 5) { m_Stuck = 0; GaveUp(); }
                }
                else m_Stuck = 0;
                m_LastPos = transform.position;
            }
        }

        void Jump()
        {
            if (!m_CC.isGrounded || m_VelY > 0.5f) return;
            m_VelY = Cfg.JumpSpeed;
            Jumps++;
        }

        /// <summary>
        /// Round things in the way: a low thing (a log, a step) it jumps; something tall it walks round, trying a little
        /// to each side, then more. Whatever's right in front is remembered (an enemy wall to break, its own door to open).
        /// </summary>
        Vector3 Steer(Vector3 wish, float dist)
        {
            if (Time.time < m_SteerUntil && Vector3.Dot(m_SteerDir, wish) > 0f) return m_SteerDir;
            if (Time.time < m_NextSteer) return wish;
            m_NextSteer = Time.time + 0.12f;
            var pos = transform.position;
            float look = Mathf.Min(1.6f, dist);
            if (look < 0.4f) return wish;
            var chest = pos + Vector3.up * 1.0f;
            if (!Physics.Raycast(chest, wish, out var hit, look, Mask, QueryTriggerInteraction.Ignore) || hit.normal.y > 0.6f || IsTarget(hit.collider))
            {
                // only something low in the way: hop it
                if (Physics.Raycast(pos + Vector3.up * 0.35f, wish, out var low, 0.9f, Mask, QueryTriggerInteraction.Ignore) && low.normal.y < 0.6f && !IsTarget(low.collider)) Jump(); // (not a slope it can walk up)
                return wish;
            }
            m_Blocker = hit.collider;
            m_BlockPoint = hit.point;
            m_BlockedAt = Time.time;
            for (int k = 1; k <= 3; k++)
                for (int s = 0; s < 2; s++)
                {
                    float ang = k * 35f * (s == 0 ? m_SideSign : -m_SideSign);
                    var dir = Quaternion.Euler(0f, ang, 0f) * wish;
                    if (Physics.Raycast(chest, dir, 1.9f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    m_SteerDir = dir;
                    m_SteerUntil = Time.time + 0.45f;
                    return dir;
                }
            return wish; // boxed in: the stuck check takes over (a jump, a sidestep)
        }

        /// <summary>What it's heading for anyway - the player it's fighting, its tree, bush or chest, the ball (walking up
        /// to that isn't being blocked).</summary>
        bool IsTarget(Collider c)
        {
            if (c == null) return false;
            var t = c.transform;
            switch (m_Task)
            {
                case Task.Fight: case Task.Retreat: return m_Target != null && t.IsChildOf(m_Target.transform);
                case Task.Farm: return m_Node != null && t.IsChildOf(m_Node.transform);
                case Task.Berries: return m_Bush != null && t.IsChildOf(m_Bush.transform);
                case Task.Store: return m_Chest != null && t.IsChildOf(m_Chest.transform);
                case Task.Fetch: case Task.Raid: return Ball.Instance != null && t.IsChildOf(Ball.Instance.transform);
            }
            return false;
        }

        BodyAnimator.Act Action => (BodyAnimator.Act)m_P.Action.Value;

        void SetAction(BodyAnimator.Act a)
        {
            if (m_P != null && m_P.Action.Value != (byte)a) m_P.Action.Value = (byte)a;
        }

        /// <summary>A flat distance.</summary>
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Somewhere about `centre`, `radius` across.</summary>
        static Vector3 Around(Vector3 centre, float radius)
        {
            var c = Random.insideUnitCircle * radius;
            return centre + new Vector3(c.x, 0f, c.y);
        }

        float Now => NetworkManager.Singleton != null ? (float)NetworkManager.Singleton.ServerTime.Time : Time.time;
        int MyTeam => m_P.Team.Value;
        float Hp01 => m_P.Health.Value / Mathf.Max(1f, Cfg.MaxHealth);
        static readonly List<Collider> s_Cols = new List<Collider>();
    }
}
