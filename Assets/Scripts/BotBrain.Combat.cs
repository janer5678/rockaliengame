using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot fighting and playing for the ball (BotBrain.cs): it closes in with its best weapon out, circles a target up
    /// close, swings on the weapon's own cooldown (a good bot hits more and aims for the head more), backs off home to
    /// heal when it's losing; fetches a loose ball and carries it home to its machine, escorts a teammate carrying it,
    /// raids the base the ball's in - breaking through enemy walls in the way - defends its own machine, and goes
    /// hunting on the enemy side. Swings go through MeleeRpc (PlayerNet.BotMelee), the ball through PickupBallRpc and
    /// InsertBallRpc, its own doors through ToggleDoorRpc: the same as a player.
    /// </summary>
    public partial class BotBrain
    {
        float m_NextPatrol;

        void TickFight()
        {
            var t = m_Target;
            if (t == null || !Hostile(t)) { m_Target = null; Done(); return; }
            var tp = t.transform.position;
            float d = Flat(tp, transform.position);
            HoldBest(Use.Fight);
            var item = m_P.HeldItem;
            var st = Cfg.Melee(item);
            float reach = st.Range > 0f ? st.Range : Cfg.RockRange;
            GoTo(tp, Mathf.Max(1.3f, reach - 0.9f), d > 2.5f);
            m_Strafe = d < reach + 1.2f && m_Skill > 0.45f && !Eating;
            m_HasLook = true;
            m_LookAt = t.EyePos - Vector3.up * 0.3f;
            if (d > reach + 1f && TryBreakBlocker()) return; // (walled off from them: through the wall)
            if (Eating || m_P.CarryingBall || Time.time < m_NextSwing || !Cfg.IsMelee(item)) return;
            var body = tp + Vector3.up * (t.Crouch.Value ? 0.8f : 1.1f);
            if (Vector3.Distance(m_P.EyePos, body) > reach + 0.4f) return;
            m_NextSwing = Time.time + st.Cooldown + Random.Range(0.05f, 0.4f) * (1.3f - m_Skill);
            bool hit = Random.value < 0.5f + 0.42f * m_Skill;
            bool head = Random.value < 0.06f + 0.16f * m_Skill;
            var point = head ? tp + Vector3.up * (t.Crouch.Value ? 1.25f : 1.65f) : body;
            if (hit) m_P.BotMelee(t.NetworkObject, point, false);
            else m_P.BotMelee(null, point + Vector3.Cross(Vector3.up, (point - m_P.EyePos).normalized) * 0.9f, false); // (a whiff)
        }

        /// <summary>Losing: run home (its base heals it), eating when there's a moment.</summary>
        void TickRetreat()
        {
            var home = Cfg.SpawnPos(MyTeam);
            GoTo(home, 2f, true);
            if (m_Target != null && Flat(m_Target.transform.position, transform.position) < 2.5f && Time.time >= m_NextSwing)
            {
                // cornered: swing back
                HoldBest(Use.Fight);
                var st = Cfg.Melee(m_P.HeldItem);
                if (st.Cooldown > 0f && !Eating)
                {
                    m_NextSwing = Time.time + st.Cooldown + 0.2f;
                    m_P.BotMelee(m_Target.NetworkObject, m_Target.transform.position + Vector3.up * 1.1f, false);
                }
            }
            TryBreakBlocker();
        }

        // ---------------------------------------------------------------- the ball

        /// <summary>Go and pick up the ball: loose (fetch), or in an enemy's machine (raid).</summary>
        void TickBall(bool raid)
        {
            var b = Ball.Instance;
            if (b == null || !b.IsSpawned || b.IsCarried) { Done(); return; }
            int sock = b.SocketTeam.Value;
            if (raid ? sock < 0 || sock == MyTeam : sock >= 0) { Done(); return; }
            var bp = b.transform.position;
            GoTo(bp, 0.7f, true);
            m_HasLook = true;
            m_LookAt = bp;
            if (Vector3.Distance(bp, m_P.EyePos) <= Cfg.InteractRange + 1.5f)
            {
                if (Time.time >= m_NextUse)
                {
                    m_NextUse = Time.time + 0.25f;
                    m_P.PickupBallRpc();
                    if (m_P.CarryingBall) { Set(Task.Carry); m_NextThink = Time.time + 0.3f; }
                }
                return;
            }
            TryBreakBlocker();
        }

        /// <summary>Carrying the ball: home to its machine and into the socket (Builder: put down at home).</summary>
        void TickCarry()
        {
            if (!m_P.CarryingBall) { Done(); return; }
            int team = MyTeam;
            if (Cfg.Builder)
            {
                var spot = Cfg.SpawnPos(team);
                GoTo(spot, 2f, true);
                if (Flat(spot, transform.position) > 4f || Time.time < m_NextUse) return;
                m_NextUse = Time.time + 0.5f;
                var ground = transform.position + transform.forward * 2.2f + Random.insideUnitSphere * 0.6f;
                ground.y = MapBuilder.Height(ground.x, ground.z);
                m_P.PlantBallRpc(ground);
                return;
            }
            var sockPos = Cfg.SocketPos(team);
            GoTo(Cfg.BaseCenter[team] - Cfg.BackDir(team) * 1.2f, 0.8f, true);
            m_HasLook = Flat(sockPos, transform.position) < 8f;
            m_LookAt = sockPos;
            if (Vector3.Distance(m_P.EyePos, sockPos) <= Cfg.MachineRange && Time.time >= m_NextUse)
            {
                m_NextUse = Time.time + 0.3f;
                m_P.InsertBallRpc();
            }
            TryBreakBlocker(); // (its own doors: open them)
        }

        /// <summary>A teammate has the ball: run along beside them (enemies about get fought: Think).</summary>
        void TickEscort()
        {
            var b = Ball.Instance;
            var c = b != null ? b.Carrier : null;
            if (c == null || c.Team.Value != MyTeam) { Done(); return; }
            var at = c.transform.position - c.transform.forward * 3f + c.transform.right * 2.5f * m_SideSign;
            GoTo(at, 1.5f, Flat(at, transform.position) > 3f);
        }

        /// <summary>Hold the base: about the front of its machine, looking out.</summary>
        void TickDefend()
        {
            int team = MyTeam;
            var back = Cfg.BackDir(team);
            if (Time.time >= m_NextPatrol || Flat(m_Goal, transform.position) < 1f && Random.value < 0.01f)
            {
                m_NextPatrol = Time.time + Random.Range(4f, 9f);
                var side = Vector3.Cross(Vector3.up, back) * m_SideSign;
                GoTo(Around(Cfg.BaseCenter[team] - back * Random.Range(4.5f, 9f) + side * Random.Range(-2f, 5f), 3f), 1f, false);
            }
            if (Flat(m_Goal, transform.position) < 1.5f)
            {
                m_HasLook = true;
                m_LookAt = transform.position - back * 10f + Vector3.up * 1.5f;
            }
        }

        /// <summary>Off onto the enemy side, looking for a fight.</summary>
        void TickHunt()
        {
            var g = NetGame.Instance;
            if (g != null && g.S == GameState.SuddenDeath) { if (Flat(m_Goal, transform.position) < 2f) GoTo(Around(Cfg.ArenaCenter, 8f), 1f, true); return; }
            int enemy = m_HuntTeam >= 0 && m_HuntTeam < Cfg.TeamCount && m_HuntTeam != MyTeam ? m_HuntTeam : NearestEnemyTeam();
            if (enemy < 0) { Done(); return; }
            if (Flat(m_Goal, transform.position) < 2.5f || Flat(m_Goal, Cfg.BaseCenter[MyTeam]) < 20f)
                GoTo(Around(Vector3.Lerp(Cfg.BaseCenter[MyTeam], Cfg.BaseCenter[enemy], Random.Range(0.5f, 0.92f)), 18f), 2f, true);
            TryBreakBlocker();
        }

        /// <summary>Bedwars: up to an enemy machine and smash it (MeleeRpc with no target: NetGame.ServerMaybeHitMachine).</summary>
        void TickMachineRaid(NetGame g)
        {
            int t = m_RaidTeam;
            if (t < 0 || t >= Cfg.TeamCount || t == MyTeam || g.MachineDown(t)) { Done(); return; }
            var m = Cfg.MachinePos(t);
            var front = m - Cfg.BackDir(t) * 2.2f;
            GoTo(front, 0.8f, true);
            var to = transform.position - m;
            to.y = 0f;
            var point = m + (to.sqrMagnitude > 0.01f ? to.normalized : -Cfg.BackDir(t)) * 1.0f + Vector3.up * 1.0f;
            m_HasLook = Flat(m, transform.position) < 8f;
            m_LookAt = point;
            if (Vector3.Distance(m_P.EyePos, point) > 2.8f) { TryBreakBlocker(); return; }
            m_Arrived = true;
            if (Eating || Time.time < m_NextSwing) return;
            HoldBest(Use.Smash);
            var st = Cfg.Melee(m_P.HeldItem);
            if (st.Cooldown <= 0f) return;
            m_NextSwing = Time.time + Mathf.Max(st.Cooldown, 0.55f) + Random.Range(0.05f, 0.3f);
            m_P.BotMelee(null, point, false);
        }

        // ---------------------------------------------------------------- through walls

        /// <summary>
        /// Something right in front of it on the way: its own team's closed door - open it; an enemy's wall, door or chest
        /// - smash through it (with whatever does most damage to buildings). True while it's busy smashing.
        /// </summary>
        bool TryBreakBlocker()
        {
            if (m_Blocker == null || Time.time - m_BlockedAt > 1.2f || Eating) return false;
            var s = m_Blocker.GetComponentInParent<Structure>();
            NetworkObject no = null;
            if (s != null && s.IsSpawned)
            {
                if (s.Team.Value == MyTeam)
                {
                    if (s.HasDoor && !s.DoorOpen.Value && Time.time >= m_NextUse) { m_NextUse = Time.time + 1f; m_P.ToggleDoorRpc(s.NetworkObject); }
                    return false;
                }
                no = s.NetworkObject;
            }
            else
            {
                var c = m_Blocker.GetComponentInParent<Container>();
                if (c != null && c.IsSpawned && c.Breakable && c.Team.Value != MyTeam) no = c.NetworkObject;
            }
            if (no == null || m_P.CarryingBall) return false;
            if (Vector3.Distance(m_P.EyePos, m_BlockPoint) > 2.6f) return false;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = m_BlockPoint;
            m_BlockedAt = Time.time; // (still in the way while it's at it)
            if (Time.time < m_NextSwing) return true;
            HoldBest(Use.Smash);
            var st = Cfg.Melee(m_P.HeldItem);
            if (st.Cooldown <= 0f) return true;
            m_NextSwing = Time.time + st.Cooldown + Random.Range(0.05f, 0.25f);
            m_P.BotMelee(no, m_BlockPoint, false);
            // broken (or not there any more): on its way again
            if (!no.IsSpawned) m_Blocker = null;
            return true;
        }
    }
}
