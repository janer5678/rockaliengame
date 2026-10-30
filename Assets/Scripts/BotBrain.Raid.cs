using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot getting into an enemy base for the ball (BotBrain.Combat.cs: TickBall), the way a player would: never reaching
    /// through a wall for it (PlayerNet.WallBetween), it goes in as far as it can without breaking anything - through any
    /// door left open (or broken off), over a wall with a ladder when there's no roof on the other side - and only then
    /// breaks in the weakest way: the doors (their leaves are weaker than a wall; a ram on them if it brought one) before
    /// the walls. Before a raid, near home, it gears up (a weapon, berries, a ram or a ladder for the base it's going to).
    /// Also the Rusher's wait at the glass wall (Plan.Rush).
    /// </summary>
    public partial class BotBrain
    {
        Structure m_RaidDoor;
        float m_NextWay;
        bool m_WayLadder;
        Structure m_LadderWall;
        Vector3 m_LadderFoot, m_LadderOut;
        float m_LadderYaw, m_LadderTop;
        readonly Dictionary<Structure, float> m_DoorDone = new Dictionary<Structure, float>();
        static readonly RaycastHit[] s_RaidHits = new RaycastHit[16];

        /// <summary>(tests) doors it went in by while raiding, ladders it put up and climbed.</summary>
        public int RaidDoorsUsed { get; private set; }
        public int LaddersPlaced { get; private set; }
        public int LaddersClimbed { get; private set; }

        // ---------------------------------------------------------------- the way in

        /// <summary>Walled off from the ball in team's base: the way in (an open door, a ladder, the weakest door to break).
        /// False: no better way than straight at it (breaking whatever's in front).</summary>
        bool RaidWay(int team, Vector3 bp)
        {
            if (team < 0 || team > 3 || Flat(transform.position, bp) > 45f) return false;
            if (Time.time >= m_NextWay) { m_NextWay = Time.time + 1f; PickWay(team, bp); }
            if (m_WayLadder) return LadderWay();
            var d = m_RaidDoor;
            if (d == null || !d.IsSpawned) return false;
            var pos = transform.position;
            var dp = d.transform.position;
            var n = d.transform.forward; n.y = 0f; n.Normalize();
            float side = Vector3.Dot(pos - dp, n) >= 0f ? 1f : -1f;
            // which side of it the ball is on: it's through when it's on that side
            float ballSide = Vector3.Dot(bp - dp, n) >= 0f ? 1f : -1f;
            if (side == ballSide) { m_DoorDone[d] = Time.time; RaidDoorsUsed++; m_RaidDoor = null; m_NextWay = 0f; return false; }
            var lat = Vector3.Cross(Vector3.up, n);
            float off = Mathf.Abs(Vector3.Dot(pos - dp, lat));
            float ahead = Vector3.Dot(pos - dp, n * side);
            var front = dp + n * side * 1.4f;
            var through = dp - n * side * 1.8f;
            m_HasLook = true;
            bool open = !d.HasDoor || d.DoorOpen.Value;
            if (open)
            {
                // lined up with it: straight through; otherwise round to the front of it first
                if (off < 0.7f && ahead < 2.4f) GoTo(through, 0.2f, true);
                else GoTo(front, 0.3f, true);
                m_LookAt = through + Vector3.up * 1.4f;
                return true;
            }
            // shut: up to it and break it in (a ram if it brought one)
            m_LookAt = dp + Vector3.up * 1.1f;
            if (Flat(pos, front) > 0.9f && !(off < 0.9f && ahead < 2.2f)) { GoTo(front, 0.3f, true); return true; }
            var col = d.GetComponentInChildren<Collider>();
            if (col == null) return false;
            m_Blocker = col;
            m_BlockPoint = dp + Vector3.up * 1.1f + n * side * 0.12f;
            m_BlockedAt = Time.time;
            if (!TryBreakBlocker()) GoTo(dp, 0.5f, false);
            return true;
        }

        /// <summary>
        /// The best way in from here: an open door that gets it nearer the ball costs just the walk; a shut one the walk
        /// plus breaking its door; straight at it the walk plus breaking the wall in the way. Then, if the best of those
        /// means breaking something, a ladder over a one-storey wall with no roof behind it (if it has one) beats it.
        /// </summary>
        void PickWay(int team, Vector3 bp)
        {
            var pos = transform.position;
            float dMe = Flat(pos, bp);
            float smash = Mathf.Max(5f, BreakRate()); // (damage a second to buildings)
            float run = Cfg.SprintSpeed;
            m_RaidDoor = null;
            m_WayLadder = false;
            var first = FirstWall(m_P.EyePos, bp, team);
            float best = dMe + (first != null ? first.Health.Value / smash * run : 0f);
            bool bestOpen = first == null;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team || (s.PType != PieceType.Doorway && s.PType != PieceType.Gate)) continue;
                if (m_DoorDone.TryGetValue(s, out var at) && Time.time - at < 20f) continue;
                var dp = s.transform.position;
                float toBall = Flat(dp, bp);
                if (toBall > 22f || toBall >= dMe - 0.5f) continue; // (it has to get it nearer)
                var n = s.transform.forward; n.y = 0f;
                if (Vector3.Dot(pos - dp, n) * Vector3.Dot(bp - dp, n) > 0f) continue; // (it's already on the ball's side of that one)
                bool open = !s.HasDoor || s.DoorOpen.Value;
                float cost = Flat(pos, dp) + toBall + (open ? 0f : s.DoorHealth.Value / smash * run);
                // another wall between it and this door (an inner room's door seen from outside the ring): that's in the way too
                var w = FirstWall(m_P.EyePos, dp + Vector3.up * 1.2f - (dp - pos).normalized * 0.6f, team);
                if (w != null && w != s) cost += w.Health.Value / smash * run;
                if (cost < best) { best = cost; m_RaidDoor = s; bestOpen = open && w == null; }
            }
            if (bestOpen) return;
            // nothing open: over the wall with a ladder, if it has one and there's no roof on the far side
            if (Has(Item.Ladder) && first != null && LadderFits(first, bp)) { m_WayLadder = true; m_RaidDoor = null; }
        }

        /// <summary>The first piece of team's base in the way from a to b (null: a clear way, or open doors only).</summary>
        Structure FirstWall(Vector3 a, Vector3 b, int team)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.1f) return null;
            int n = Physics.RaycastNonAlloc(a, d / len, s_RaidHits, len, Mask, QueryTriggerInteraction.Ignore);
            Structure best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var s = s_RaidHits[i].collider.GetComponentInParent<Structure>();
                if (s == null || !s.IsSpawned || s.Team.Value != team) continue;
                if ((s.PType == PieceType.Doorway || s.PType == PieceType.Gate) && (!s.HasDoor || s.DoorOpen.Value)) continue;
                if (s_RaidHits[i].distance < bd) { bd = s_RaidHits[i].distance; best = s; }
            }
            return best;
        }

        /// <summary>How fast it breaks buildings with the best it carries (damage a second; a ram counts big).</summary>
        float BreakRate()
        {
            if (Has(Item.Ram)) return 60f;
            float best = Rate(Item.Rock, Use.Smash);
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++) if (Cfg.IsMelee(inv[i].Id)) best = Mathf.Max(best, Rate(inv[i].Id, Use.Smash));
            return best;
        }

        /// <summary>Right by the ball but walled off from it: break whatever's in the way to it.</summary>
        void BreakToward(Vector3 p)
        {
            var eye = m_P.EyePos;
            var d = p - eye;
            if (!Physics.Raycast(eye, d.normalized, out var hit, Mathf.Min(d.magnitude, 3f), Mask, QueryTriggerInteraction.Ignore)) return;
            var s = hit.collider.GetComponentInParent<Structure>();
            if (s == null || s.Team.Value == MyTeam) return;
            m_Blocker = hit.collider;
            m_BlockPoint = hit.point;
            m_BlockedAt = Time.time;
            TryBreakBlocker();
        }

        // ---------------------------------------------------------------- ladders

        /// <summary>No roof over this spot (open to the sky).</summary>
        static bool Roofless(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.3f, Vector3.up, s_RaidHits, 14f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (s_RaidHits[i].collider.GetComponentInParent<Structure>() != null) return false;
            return true;
        }

        /// <summary>A ladder goes up this wall and over it: an ordinary wall one storey high (not a spiked large wall),
        /// nothing on top of it, and no roof on the far side to stop it dropping in.</summary>
        bool LadderFits(Structure wall, Vector3 bp)
        {
            if (wall.PType != PieceType.Wall && wall.PType != PieceType.Window && wall.PType != PieceType.Doorway) return false;
            var k = wall.Key;
            if (k.Kind != PieceKey.KEdge) return false;
            if (BuildGrid.Registry.ContainsKey(new PieceKey(PieceKey.KEdge, k.I, k.J, k.L + 1, k.D))) return false; // (another storey on it)
            var wp = wall.transform.position;
            var n = wall.transform.forward; n.y = 0f; n.Normalize();
            float side = Vector3.Dot(transform.position - wp, n) >= 0f ? 1f : -1f;
            var inside = wp - n * side * 1.6f + Vector3.up * 1.2f;
            if (!Roofless(inside)) return false;
            float top = Deployables.PieceTop(wall);
            float ground = MapBuilder.Height(wp.x + n.x * side * 0.6f, wp.z + n.z * side * 0.6f);
            if (top - ground > Cfg.LevelH + Deployables.LadderHeight) return false; // (built up on something tall)
            m_LadderWall = wall;
            m_LadderTop = top;
            m_LadderYaw = Quaternion.LookRotation(-n * side).eulerAngles.y;
            m_LadderFoot = wp + n * side * 0.45f;
            m_LadderFoot.y = Mathf.Max(ground, top - Deployables.LadderHeight + 0.15f);
            m_LadderOut = wp - n * side * 0.9f;
            m_LadderOut.y = top + 0.12f;
            return true;
        }

        /// <summary>Over the wall: up to it, the ladder against it (PlaceDeployableRpc, like a player), then up and over.</summary>
        bool LadderWay()
        {
            var w = m_LadderWall;
            if (w == null || !w.IsSpawned) { m_WayLadder = false; return false; }
            var foot = m_LadderFoot;
            var n = (foot - w.transform.position); n.y = 0f; n.Normalize(); // (out from the wall, its side)
            var stand = foot + n * 0.5f;
            stand.y = transform.position.y;
            m_HasLook = true;
            m_LookAt = w.transform.position + Vector3.up * 2f;
            var ladder = LadderAt(foot);
            if (Flat(stand, transform.position) > 0.8f) { GoTo(stand, 0.3f, true); return true; }
            m_Arrived = true;
            if (ladder == null)
            {
                if (!Has(Item.Ladder)) { m_WayLadder = false; return false; }
                if (Time.time < m_NextUse) return true;
                m_NextUse = Time.time + 0.6f;
                int slot = HotbarFor(Item.Ladder);
                if (slot < 0) { m_WayLadder = false; return false; }
                Hold(slot);
                int before = CountItem(Item.Ladder);
                m_P.PlaceDeployableRpc((byte)Item.Ladder, foot, m_LadderYaw);
                if (CountItem(Item.Ladder) < before) LaddersPlaced++;
                else { m_WayLadder = false; m_NextWay = Time.time + 6f; } // (it wouldn't go there: another way)
                return true;
            }
            // on it and up: over the top and down inside
            StartClimb(new Vector3(foot.x, transform.position.y, foot.z), m_LadderOut);
            m_WayLadder = false;
            m_NextWay = Time.time + 2.5f;
            return true;
        }

        static Container LadderAt(Vector3 p)
        {
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.Kind.Value == Container.Ladder && Flat(c.transform.position, p) < 0.9f) return c;
            return null;
        }

        // ---------------------------------------------------------------- the climb (up the ladder's rungs, over the top)

        Vector3 m_ClimbFrom, m_ClimbTop, m_ClimbOver;
        float m_ClimbT = -1f, m_ClimbUpTime;

        void StartClimb(Vector3 bottom, Vector3 over)
        {
            m_ClimbFrom = transform.position;
            m_ClimbTop = new Vector3(bottom.x, over.y, bottom.z);
            m_ClimbOver = over;
            m_ClimbUpTime = Mathf.Max(0.4f, (over.y - transform.position.y) / 3.6f); // (a player's climbing speed)
            m_ClimbT = 0f;
            m_VelY = 0f;
            LaddersClimbed++;
            Note("over a wall by ladder");
        }

        /// <summary>Climbing: moved up the rungs (at a player's speed), then across the top of the wall, then let go to drop
        /// in. True while it's at it (nothing else moves it).</summary>
        bool TickClimb(float dt)
        {
            if (m_ClimbT < 0f) return false;
            if (m_P.Dead.Value) { m_ClimbT = -1f; return false; }
            float across = 0.45f;
            m_ClimbT += dt;
            Vector3 at;
            if (m_ClimbT < m_ClimbUpTime) at = Vector3.Lerp(m_ClimbFrom, m_ClimbTop, m_ClimbT / m_ClimbUpTime);
            else at = Vector3.Lerp(m_ClimbTop, m_ClimbOver, Mathf.Clamp01((m_ClimbT - m_ClimbUpTime) / across));
            m_CC.enabled = false;
            transform.position = at;
            var face = m_ClimbOver - m_ClimbFrom; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 360f * dt);
            m_CC.enabled = true;
            m_PitchNow = Mathf.SmoothDampAngle(m_PitchNow, m_ClimbT < m_ClimbUpTime ? -20f : 20f, ref m_PitchVel, 0.25f, 200f, dt);
            if (Mathf.Abs(m_P.Pitch.Value - m_PitchNow) > 0.35f) m_P.Pitch.Value = m_PitchNow;
            if (m_ClimbT >= m_ClimbUpTime + across)
            {
                m_ClimbT = -1f;
                m_VelY = 0f;
                m_Shove = face.normalized * 2f; // (off the top and in)
                Physics.SyncTransforms();
            }
            m_LastPos = transform.position;
            return true;
        }

        // ---------------------------------------------------------------- getting ready to raid

        bool m_Prepped;
        Vector3 m_RushSpot;

        /// <summary>Off to raid the ball out of enemy's machine, still near home: a weapon first, then a raiding tool for that
        /// base - a ladder when there's no roof over their machine, else a ram (wooden doors and walls go down fast to one).
        /// Once per raid. True: off to get it.</summary>
        bool RaidPrep(int enemy, bool homeSide)
        {
            if (m_Prepped || !homeSide || Flat(transform.position, Cfg.BaseCenter[MyTeam]) > 45f) return false;
            if (!HasWeapon && CraftJob(true)) return true;
            int onHand = m_P.Count(Cfg.CurrencyItem), wealth = onHand + ChestStock();
            var tool = Item.None;
            if (!Has(Item.Ladder) && Roofless(Cfg.SocketPos(enemy) + Vector3.up * 0.5f)) tool = Item.Ladder;
            else if (!Has(Item.Ram)) tool = Item.Ram;
            m_Prepped = true;
            if (tool == Item.None || !Craftable(tool, out int idx, out int cost) || wealth < cost + 40) return false;
            return StartStep(new Step { Goal = Goal.Weapons, Kind = StepKind.Craft, Item = tool, Idx = idx, Cost = cost }, onHand);
        }

        // ---------------------------------------------------------------- the Rusher

        /// <summary>Waiting at the glass wall for the drop: as near the ball as its side of the wall lets it, facing it,
        /// jiggling about a bit like a player waiting for the off.</summary>
        void TickRush(NetGame g)
        {
            var b = Ball.Instance;
            var target = b != null ? b.transform.position : Vector3.zero;
            var pos = transform.position;
            var home = Cfg.BaseCenter[MyTeam];
            // the closest spot to the ball on its own side (Safe pulls a goal back behind the glass)
            var spot = Safe(Vector3.Lerp(target, home, 0.02f));
            if (Time.time >= m_NextPatrol)
            {
                m_NextPatrol = Time.time + Random.Range(1.2f, 2.5f);
                var side = Vector3.Cross(Vector3.up, (target - home).normalized);
                m_RushSpot = spot + side * Random.Range(-1.5f, 1.5f);
            }
            var at = Flat(spot, pos) > 6f ? spot : Safe(m_RushSpot);
            GoTo(at, 0.8f, true);
            m_HasLook = Flat(spot, pos) < 10f;
            m_LookAt = target + Vector3.up * 0.5f;
            if (AtWallAt < 0f && Flat(spot, pos) < 4f) AtWallAt = Time.time;
            if (!MapBuilder.GlassUp && g.S == GameState.BallLive) m_NextThink = 0f; // (down: straight to the ball - Plan.Rush in PlanJob)
        }
    }
}
