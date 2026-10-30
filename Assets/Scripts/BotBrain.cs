using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The server's driver for a bot (PlayerNet.Bot.cs): it plays the match like a person would. A few times a second it
    /// thinks (BotBrain.Think.cs) - what's going on, what it needs, what its personality likes - and picks a job: farm
    /// trees, eat berries when hurt, pick up floor loot and empty airdrops, stash its wood in the team chest, and work on
    /// whatever its team is furthest behind the others on (BotBrain.Goals.cs): a bigger house (BotBrain.House.cs), better
    /// weapons and armour, more of the crafting list unlocked, or more base upgrades; go for the ball, carry it home, raid
    /// an enemy base for it, defend, hunt, or fight whoever it can see (never anyone hiding in tree camo). Every frame it then walks there like a player (the same character controller, speeds,
    /// jump and slide), steering round things, and does the job's actions through the same server calls a player's
    /// input ends up in (MeleeRpc, CraftRpc, PlaceRpc, MoveItemRpc, PickupBallRpc...), so every rule and price applies.
    /// In the waiting stadium / ship lobby it just ambles about.
    /// </summary>
    public partial class BotBrain : MonoBehaviour
    {
        /// <summary>What a bot is doing (tests and debugging can read it: Job).</summary>
        public enum Task : byte { Idle, Lounge, Wander, Farm, Berries, Store, Craft, Deploy, Build, Fetch, Carry, Escort, Raid, Defend, Hunt, Fight, Retreat, Loot, Upgrade, Arm, Rush }
        /// <summary>A bot's personality, dealt at random like the ghosts in Pac-Man - each its own play style, and all of them
        /// wanting the ball in their machine above anything: a Raider (the hunter: the best weapons and armour it can get,
        /// roaming the map for kills), a Gatherer (the crafter: farming, the Trade Stations, the upgrades), a Defender (the
        /// builder: the best base first, the ball after) and a Rusher (waiting at the glass wall before it drops, first to the ball).</summary>
        public enum Bent : byte { Raider, Gatherer, Defender, Rusher }

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
        // looking about (eased: Look) and moving (eased: m_MoveDir); backing out of a corner (Detour)
        float m_YawVel, m_PitchNow, m_PitchVel, m_DetourUntil;
        Vector3 m_MoveDir, m_Detour;
        /// <summary>(tests) how often it has been stuck, and pushed out of something it was inside.</summary>
        public int StuckTotal { get; private set; }
        public int PushedOut { get; private set; }
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

        /// <summary>(tests) what it has been up to: every change of plan and job, with the time.</summary>
        public readonly List<string> Trace = new List<string>(64);
        void Note(string s) { if (Trace.Count < 400) Trace.Add($"{Time.time:0.0} {s}"); }

        /// <summary>(tests) how many times it has slid / jumped.</summary>
        public int Slides { get; private set; }
        public int Jumps { get; private set; }

        const float ReachTree = 1.25f;
        /// <summary>(tests) every new bot gets this personality (-1: dealt at random).</summary>
        public static int TestBent = -1;

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
            m_LootBox = null;
            m_LootId = -1;
            m_OpenedDoors.Clear();
            m_DetourUntil = 0f;
            m_ClimbT = -1f;
            m_MoveDir = Vector3.zero;
        }

        void InitPersonality()
        {
            m_Init = true;
            // a stable little random generator per bot: the same bot keeps its character all match
            var r = new System.Random((int)(m_P.NetworkObjectId * 2654435761u % int.MaxValue) ^ 0x5bd1e995);
            float F() => (float)r.NextDouble();
            m_Bent = TestBent >= 0 ? (Bent)(TestBent & 3) : (Bent)r.Next(0, 4);
            m_Aggro = Mathf.Lerp(0.25f, 1f, F());
            m_Skill = Mathf.Lerp(0.35f, 0.95f, F());
            m_RetreatAt = Mathf.Lerp(0.22f, 0.45f, 1f - m_Aggro * 0.6f);
            m_StoreAt = Mathf.Lerp(260f, 520f, F());
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
            if (m_CC == null || !m_CC.enabled || g.S == GameState.GameOver || g.FightFrozen || VictoryCutscene.Active || MatchIntro.Active) { SetAction(BodyAnimator.Act.None); return; }
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
            if (!lobby)
            {
                Tick(g, dt); // the job's actions (swings, pick-ups, crafting...) - it may say it has arrived
                TickGear();  // armour / a helmet it picked up goes on (BotBrain.Loot.cs)
                TickDoors(); // its own doors: open in the way, shut behind it (BotBrain.House.cs)
            }
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

        Vector3 m_Shove;

        /// <summary>Knocked back (a knockback stick, the Stick of Truth, a car): thrown along v, like a player.</summary>
        public void Knockback(Vector3 v)
        {
            m_Shove = new Vector3(v.x, 0f, v.z);
            m_VelY = Mathf.Max(m_VelY, v.y);
        }

        void Move(NetGame g, bool lobby, float dt)
        {
            var pos = transform.position;
            // in or out of its own house: by way of the front doors (BotBrain.House.cs)
            bool routed = false;
            var goal = lobby ? m_Goal : Route(pos, out routed);
            if (!lobby && TickClimb(dt)) return; // (up a ladder and over a wall: BotBrain.Raid.cs)
            bool detour = !lobby && Time.time < m_DetourUntil;
            if (detour) goal = m_Detour; // (backing out of a corner it got stuck in: Detour)
            var to = goal - pos;
            to.y = 0f;
            float dist = to.magnitude;
            var wish = (!m_Arrived || detour) && dist > (routed || detour ? 0.25f : m_Stop) ? to / Mathf.Max(dist, 0.001f) : Vector3.zero;
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

            // a person runs anywhere more than a few steps off (walking only up close, circling someone, or about the lobby)
            bool run = m_Sprint || (dist > 4.5f && !m_Strafe);
            float speed = lobby ? Cfg.WalkSpeed * 0.5f : run ? Cfg.SprintSpeed : Cfg.WalkSpeed;
            if (m_P.CarryingBall) speed *= Cfg.BallCarrySpeedMul;
            speed *= m_P.WackySpeedMul; // (slowed, an ant, a bear)
            if (m_P.WackyFrozen) speed = 0f; // (timed out, knocked flat, a minigame on - dancing they still walk)
            bool grounded = m_CC.isGrounded;
            // speeding up and turning take a moment, like a player's (no instant flips from one way to another)
            m_MoveDir = Vector3.MoveTowards(m_MoveDir, wish, (wish.sqrMagnitude > 0.01f ? 9f : 12f) * dt);
            var vel = m_MoveDir * speed;
            // downhill ahead: how far the ground drops over the next 3 m, per metre
            float drop = 0f;
            if (wish.sqrMagnitude > 0.01f && !lobby) drop = (MapBuilder.Height(pos.x, pos.z) - MapBuilder.Height(pos.x + wish.x * 3f, pos.z + wish.z * 3f)) / 3f;

            // the slide: now and then, running flat out a long way, it drops into one (a boost that bleeds away)
            if (Time.time < m_SlideUntil)
            {
                m_SlideSpeed = Mathf.MoveTowards(m_SlideSpeed, 0f, (Cfg.SprintSpeed + Cfg.SlideBoost) / Mathf.Lerp(0.5f, 1.6f, Cfg.SlideSlipperiness / 10f) * dt);
                if (drop > 0.05f) m_SlideSpeed = Mathf.Min(Cfg.SlideMaxSpeed, m_SlideSpeed + drop * Cfg.Gravity * 0.9f * dt); // (downhill it keeps going, faster)
                m_SlideDir = Vector3.RotateTowards(m_SlideDir, wish.sqrMagnitude > 0.01f ? wish : m_SlideDir, Cfg.SlideSteer * Mathf.Deg2Rad * dt, 0f);
                vel = m_SlideDir * m_SlideSpeed;
                if (m_SlideSpeed < Cfg.SlideMinSpeed || !grounded) m_SlideUntil = 0f;
            }
            else if (!lobby && run && grounded && Time.time >= m_SteerUntil && !m_P.CarryingBall && !Eating && Vector3.Dot(wish, transform.forward) > 0.85f
                     && ((drop > 0.09f && dist > 6f && Time.time >= m_NextSlide - Cfg.SlideBoostCooldown && Random.value < 4f * dt) // (down a slope: nearly always)
                         || (dist > 9f && Time.time >= m_NextSlide && Random.value < 0.6f * dt * m_Playful)))
            {
                m_SlideUntil = Time.time + (drop > 0.09f ? 2.4f : 1.6f);
                m_SlideSpeed = Mathf.Min(Cfg.SprintSpeed + Cfg.SlideBoost, Cfg.SlideMaxSpeed);
                m_SlideDir = wish;
                m_MoveDir = wish;
                m_NextSlide = Time.time + Cfg.SlideBoostCooldown + Random.Range(3f, 9f) / m_Playful;
                Slides++;
                Sfx.Play(Sfx.Slide, pos, 0.5f, 0.08f, 35f);
            }
            bool sliding = Time.time < m_SlideUntil;
            if (!sliding && Action == BodyAnimator.Act.Slide) SetAction(BodyAnimator.Act.None);
            else if (sliding) SetAction(BodyAnimator.Act.Slide);

            if (grounded && m_VelY <= 0f) m_VelY = Mathf.Max(m_VelY, -1f); else m_VelY -= Cfg.Gravity * dt;
            m_Shove = Vector3.MoveTowards(m_Shove, Vector3.zero, (grounded ? 14f : 3f) * dt); // (a knockback fading out)
            var was = transform.position;
            var shoved = m_CC.Move((vel + m_Shove + Vector3.up * m_VelY) * dt);
            if ((shoved & CollisionFlags.Sides) != 0 && m_Shove.sqrMagnitude > 4f) // (a knockback into a wall stops there: blocked = hardly moving)
            {
                var tried = (vel + m_Shove) * dt; tried.y = 0f;
                var went = transform.position - was; went.y = 0f;
                if (tried.magnitude > 0.02f && went.magnitude < tried.magnitude * 0.3f) m_Shove = Vector3.zero;
            }

            Look(pos, vel, to, dt);

            // stuck (a wall, a rock, a tree, a doorway): a jump and a sidestep; still stuck, out of anything it's inside of and
            // back out of the corner another way; stuck for long, it gives up on that goal
            if (Time.time >= m_StuckCheck)
            {
                m_StuckCheck = Time.time + 0.7f;
                if (wish.sqrMagnitude > 0.1f && (transform.position - m_LastPos).sqrMagnitude < 0.3f * 0.3f)
                {
                    m_Stuck++;
                    StuckTotal++;
                    if (m_Stuck == 1 || m_Stuck == 3) Jump();
                    m_SideUntil = Time.time + 0.8f;
                    m_SideSign = Random.value < 0.5f ? -1f : 1f;
                    if (m_Stuck >= 2) { PushOut(); Detour(wish); }
                    if (m_Stuck >= 5) { m_Stuck = 0; GaveUp(); }
                }
                else m_Stuck = 0;
                m_LastPos = transform.position;
            }
        }

        /// <summary>
        /// Where it looks, like a person would: the head turns smoothly (eased in and out, never a snap) - quicker onto
        /// someone it's fighting; on the move, the way it's going with its eyes a little down the path; up or down at
        /// whatever it's looking at. So watching it (spectating, the kill cam) moves the way a player's view does.
        /// </summary>
        void Look(Vector3 pos, Vector3 vel, Vector3 to, float dt)
        {
            var face = m_HasLook ? m_LookAt - pos : (vel.sqrMagnitude > 0.5f ? vel : to);
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
            {
                float want = Quaternion.LookRotation(face.normalized).eulerAngles.y;
                bool fight = m_HasLook && (m_Task == Task.Fight || m_Task == Task.Retreat);
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, want, ref m_YawVel, fight ? 0.07f : m_HasLook ? 0.12f : 0.2f, fight ? 720f : 380f, dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            float pitch = 6f; // (on the move: eyes on the ground a little way ahead)
            if (m_HasLook)
            {
                var d = m_LookAt - m_P.EyePos;
                float flat = new Vector2(d.x, d.z).magnitude;
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, Mathf.Max(flat, 0.1f)) * Mathf.Rad2Deg, -60f, 60f);
            }
            else if (vel.sqrMagnitude < 0.5f) pitch = 2f + Mathf.Sin(Time.time * 0.6f + m_SideSign) * 4f; // (standing about: glancing round a little)
            m_PitchNow = Mathf.SmoothDampAngle(m_PitchNow, pitch, ref m_PitchVel, m_HasLook ? 0.1f : 0.25f, 240f, dt);
            if (Mathf.Abs(m_P.Pitch.Value - m_PitchNow) > 0.35f) m_P.Pitch.Value = m_PitchNow;
        }

        /// <summary>Somehow inside a building piece or a deployable (built round it, shoved into it): out of it.</summary>
        void PushOut()
        {
            var pos = transform.position;
            float r = m_CC.radius, h = m_CC.height;
            var move = Vector3.zero;
            foreach (var c in Physics.OverlapCapsule(pos + Vector3.up * (r + 0.05f), pos + Vector3.up * (h - r), r - 0.02f, Mask, QueryTriggerInteraction.Ignore))
            {
                if (c == m_CC || c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<Structure>() == null && c.GetComponentInParent<Container>() == null) continue;
                if (Physics.ComputePenetration(m_CC, pos, transform.rotation, c, c.transform.position, c.transform.rotation, out var dir, out float dd) && dd > 0.02f)
                {
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f) move += dir.normalized * (dd + 0.08f);
                }
            }
            if (move.sqrMagnitude < 0.0004f) return;
            m_CC.enabled = false;
            transform.position = pos + Vector3.ClampMagnitude(move, 1.2f);
            m_CC.enabled = true;
            Physics.SyncTransforms();
            PushedOut++;
        }

        /// <summary>Stuck against something: back off and round it for a moment (off to one side of the way it was going).</summary>
        void Detour(Vector3 wish)
        {
            var side = Vector3.Cross(Vector3.up, wish) * m_SideSign;
            m_Detour = transform.position - wish * Random.Range(1f, 2f) + side * Random.Range(2f, 3.5f);
            m_DetourUntil = Time.time + 1.1f;
            m_SteerUntil = 0f;
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
                case Task.Loot: return m_LootBox != null && t.IsChildOf(m_LootBox.transform);
                case Task.Arm: return m_Turret != null && t.IsChildOf(m_Turret.transform);
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
