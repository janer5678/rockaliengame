using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wacky airdrop items on the server (Wacky.cs has the list): what using each one does, the states they put a
    /// player in (shielded, slowed, an ant, locked in a timeout ball, dancing, knocked flat, glittering, disguised, a bear,
    /// flying a turret bot, wearing the jump boots, gravity flipped) and how those change damage and death.
    /// </summary>
    public partial class PlayerNet
    {
        // ---- what the wacky items have done to this player (server-written; "until" = server time) ----
        public readonly NetworkVariable<double> ShieldUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> SlowUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> AntUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> PrisonUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> DanceUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> KnockUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> GlitterUntil = new NetworkVariable<double>(-1);
        /// <summary>The disguise kit: the team this player looks like (255 = none).</summary>
        public readonly NetworkVariable<byte> DisguiseTeam = new NetworkVariable<byte>(255);
        /// <summary>Bear juice: the bear's health left (0 = not a bear), and the server time it wears off (Cfg.BearTime - no
        /// turning back before then, unless you die).</summary>
        public readonly NetworkVariable<float> BearHpLeft = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<double> BearUntil = new NetworkVariable<double>(-1);
        /// <summary>Wearing the double jump boots (they break when you die).</summary>
        public readonly NetworkVariable<bool> WornBoots = new NetworkVariable<bool>();
        /// <summary>The turret bot this player is driving (its NetworkObjectId; 0 = none).</summary>
        public readonly NetworkVariable<ulong> DroneId = new NetworkVariable<ulong>();
        /// <summary>The gravity staff: this player falls up and walks upside down (the owner flips it: they move themselves).</summary>
        public readonly NetworkVariable<bool> GravFlip = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool Shielded => ShieldUntil.Value > Now;
        public bool Slowed => SlowUntil.Value > Now;
        public bool IsAnt => AntUntil.Value > Now;
        public bool Prisoned => PrisonUntil.Value > Now;
        public bool Dancing => DanceUntil.Value > Now;
        public bool KnockedDown => KnockUntil.Value > Now;
        public bool Glittering => GlitterUntil.Value > Now;
        public bool Disguised => DisguiseTeam.Value < 4 && DisguiseTeam.Value != Team.Value && !Dead.Value;
        public bool IsBear => BearHpLeft.Value > 0f && !Dead.Value;
        public bool Droning => DroneId.Value != 0;
        public bool Flipped => GravFlip.Value && !Dead.Value;
        /// <summary>A Connect 4 game or a gameshow question is on: everyone stops and watches.</summary>
        public static bool MinigameOn => NetGame.Instance != null && NetGame.Instance.IsSpawned && NetGame.Instance.MinigameOn;
        /// <summary>Held where you are (locked in a ball, dancing, knocked flat, a minigame on).</summary>
        public bool WackyHeld => Prisoned || Dancing || KnockedDown || MinigameOn;
        /// <summary>Can't attack or use things right now.</summary>
        public bool WackyNoAttack => Shielded || IsAnt || WackyHeld;
        /// <summary>Nothing can hurt this player right now.</summary>
        public bool WackySafe => Shielded || IsAnt || Prisoned || MinigameOn;
        public float WackySpeedMul => (Slowed ? Cfg.SlowMul : 1f) * (IsBear ? Cfg.BearSpeedMul : 1f) * (IsAnt ? Cfg.AntSpeedMul : 1f);
        /// <summary>The team this player looks like they're on (a disguise shows the enemy's).</summary>
        public int ApparentTeam => Disguised ? DisguiseTeam.Value : Team.Value;
        /// <summary>The name over / on them: a disguised player borrows one of that team's players' names.</summary>
        public string ApparentName
        {
            get
            {
                if (!Disguised) return DisplayName;
                foreach (var p in All) if (p != null && p != this && p.Team.Value == DisguiseTeam.Value) return p.DisplayName;
                return Cfg.TeamName[Mathf.Clamp(DisguiseTeam.Value, 0, 3)];
            }
        }

        float m_NextWacky;

        // =====================================================================
        // damage and death
        // =====================================================================

        /// <summary>Server, at the top of ServerDamage: the wacky states first. True = nothing more to do (the hit's
        /// stopped, or a bear soaked it up).</summary>
        bool ServerWackyDamage(ref float dmg, PlayerNet attacker)
        {
            if (dmg <= 0f) return false;
            if (WackySafe)
            {
                if (Shielded) Fx.Server(FxKind.ShieldPing, transform.position + Vector3.up * 1.1f, Vector3.up);
                return true;
            }
            if (Disguised && attacker != null && attacker != this) { DisguiseTeam.Value = 255; WackyNoteRpc(new FixedString128Bytes("Your disguise fell off!")); }
            if (IsBear)
            {
                float left = BearHpLeft.Value - dmg;
                if (left > 0f) { BearHpLeft.Value = left; return true; }
                // the bear's down: and so are you
                BearHpLeft.Value = 0f;
                dmg = Health.Value + 1f;
            }
            return false;
        }

        /// <summary>Server, when a hit would kill: holding the golden rock, you don't die - it's used up instead, you're back
        /// to full health and covered in gold glitter.</summary>
        bool ServerCheatDeath()
        {
            var st = HeldStack;
            if (st.Id != Item.GoldenRock || InSuddenDeath) return false;
            ServerConsumeHeld();
            Health.Value = Cfg.MaxHealth;
            GlitterUntil.Value = Now + Cfg.GlitterTime;
            Fx.Server(FxKind.GoldBurst, transform.position + Vector3.up * 1.1f, Vector3.up);
            WackyNoteRpc(new FixedString128Bytes("The ROCK OF IMMORTALITY saved you!"));
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{DisplayName}'s Rock of Immortality saved them from death!");
            return true;
        }

        /// <summary>Server, as this player dies: everything wacky on them ends (the boots break, a bear's a player again).</summary>
        void ServerWackyDied()
        {
            ShieldUntil.Value = SlowUntil.Value = AntUntil.Value = PrisonUntil.Value = DanceUntil.Value = KnockUntil.Value = -1;
            DisguiseTeam.Value = 255;
            BearHpLeft.Value = 0f;
            BearUntil.Value = -1;
            if (WornBoots.Value) { WornBoots.Value = false; Notify("Your double jump boots broke"); }
            ServerEndDrone(false);
            UnflipRpc();
        }

        /// <summary>Server, whenever this player attacks (Reveal): a disguise falls off.</summary>
        void ServerWackyAttacked()
        {
            if (Disguised) { DisguiseTeam.Value = 255; WackyNoteRpc(new FixedString128Bytes("You attacked - your disguise fell off")); }
        }

        /// <summary>The owner: gravity back to normal (they died, or a dev reset).</summary>
        [Rpc(SendTo.Owner)]
        void UnflipRpc() { if (GravFlip.Value && IsOwner) GravFlip.Value = false; }

        /// <summary>The owner: a big line in the middle of the screen (the wacky items' news - the old messages are off).</summary>
        [Rpc(SendTo.Owner)]
        public void WackyNoteRpc(FixedString128Bytes text) { if (!Bot.Value) Hud.WackyNote(text.ToString()); }

        /// <summary>The owner: a drawn monster got you - copies of it fill your screen for a few seconds.</summary>
        [Rpc(SendTo.Owner)]
        public void MonsterSplatRpc(FixedString4096Bytes pic) { if (!Bot.Value) Hud.MonsterSplat(pic.ToString()); }

        void Note(string text) => WackyNoteRpc(new FixedString128Bytes(text.Length > 120 ? text.Substring(0, 120) : text));

        // =====================================================================
        // using them
        // =====================================================================

        /// <summary>
        /// Every click-to-use wacky item: `origin` / `dir` the user's look, `point` what they aimed at (a spot on the map for
        /// Omen's map), `target` who they hit (the wands, the ant gun), `arg` an extra number (the decoy: what you held before).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void UseWackyRpc(Item kind, Vector3 origin, Vector3 dir, Vector3 point, bool hasTarget, NetworkObjectReference target, int arg)
        {
            var g = NetGame.Instance;
            if (g == null || Dead.Value || HeldItem != kind || Time.time < m_NextWacky || !GameAllowsCombat || InSuddenDeath || IsBear) return;
            if (WackyHeld) return;
            bool offensive = kind == Item.Connect4Wand || kind == Item.GameshowWand || kind == Item.StickOfTruth || kind == Item.SuicideBomb || kind == Item.SlowGun || kind == Item.AntGun || kind == Item.BlueShell;
            if (offensive && (Shielded || IsAnt)) { Note(Shielded ? "You can't attack while your shield is up" : "Ants can't attack"); return; }
            m_NextWacky = Time.time + 0.4f;
            if ((origin - EyePos).sqrMagnitude > 25f) origin = EyePos;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            PlayerNet tp = null;
            NetworkObject tobj = null;
            if (hasTarget && target.TryGet(out var tno)) { tobj = tno; tp = tno.GetComponent<PlayerNet>(); }
            // the zappy ones: their beam for everyone else (the user drew their own)
            if (kind == Item.Connect4Wand || kind == Item.GameshowWand || kind == Item.AntGun || kind == Item.SlowGun || kind == Item.StickOfTruth)
                Fx.Server(FxKind.WackyBeam, origin + dir * 0.4f, Vector3.ClampMagnitude(point - origin, 150f), OwnerClientId);
            switch (kind)
            {
                case Item.Decoy:
                {
                    ServerConsumeHeld();
                    var held = arg > 0 && arg < 256 && System.Enum.IsDefined(typeof(Item), (byte)arg) ? (Item)arg : Item.Rock;
                    var at = transform.position + transform.forward * 1.3f + Vector3.up * 0.1f;
                    Vehicle.ServerSpawnDecoy(at, transform.eulerAngles.y, this, held);
                    Fx.Server(FxKind.Poof, at + Vector3.up, Vector3.up);
                    break;
                }
                case Item.OmenMap: ServerOmenTeleport(point); break;
                case Item.BlueShell: ServerBlueShell(); break;
                case Item.Connect4Wand:
                case Item.GameshowWand:
                {
                    if (tp == null || tp == this || tp.Dead.Value || Vector3.Distance(tp.transform.position, transform.position) > 40f || GlassBetween(transform.position, tp.transform.position)) { Note("Missed - zap someone"); return; }
                    if (!g.ServerStartMinigame(kind == Item.Connect4Wand ? MiniGame.Connect4 : MiniGame.Gameshow, this, tp)) { Note("A game's already on"); return; }
                    ServerConsumeHeld();
                    break;
                }
                case Item.StickOfTruth:
                {
                    // the lifting stick: whatever it hits goes up (NetGame.Lift.cs)
                    if (!PortalPass.ReachOk(EyePos, point, 125f)) point = EyePos + dir * 40f;
                    if (tobj != null && (Vector3.Distance(tobj.transform.position, EyePos) > 130f || GlassBetween(transform.position, tobj.transform.position))) tobj = null;
                    ServerUseCharge(Item.StickOfTruth);
                    if (!g.ServerLift(point, tobj, this)) Note("Nothing there to lift");
                    break;
                }
                case Item.SuicideBomb:
                    ServerConsumeHeld();
                    m_SuicideAt = Time.time + Cfg.SuicideFuse;
                    Fx.Server(FxKind.Fuse, transform.position + Vector3.up * 1.2f, Vector3.up);
                    if (g != null) g.Broadcast($"{DisplayName} pulled the pin on a SUICIDE BOMB!");
                    break;
                case Item.AttentionSnail:
                {
                    var at = WackyGround(point.sqrMagnitude > 0f && Vector3.Distance(point, transform.position) < 12f ? point : transform.position + transform.forward * 2f);
                    ServerConsumeHeld();
                    float life = g.S == GameState.BallLive && g.TimeLeft < 30f ? Cfg.SnailShortTime : Cfg.SnailTime;
                    g.ServerAddGadget(Gadget.Snail, at, transform.eulerAngles.y + 180f, Team.Value, life, Random.Range(1, 99999));
                    g.Broadcast("An ATTENTION SNAIL has everyone's attention!");
                    break;
                }
                case Item.InvSwapper: ServerConsumeHeld(); g.ServerSwapInventories(this); break;
                case Item.ShieldPotion:
                    ServerConsumeHeld();
                    ShieldUntil.Value = Now + Cfg.ShieldTime;
                    Fx.Server(FxKind.Drink, transform.position, Vector3.up);
                    Note($"SHIELD UP: nothing can hurt you for {Cfg.ShieldTime:0} s");
                    break;
                case Item.SlowGun:
                {
                    if (!PortalPass.ReachOk(EyePos, point, 90f)) point = EyePos + dir * 30f;
                    ServerUseCharge(Item.SlowGun);
                    Fx.Server(FxKind.Goo, point, Vector3.up);
                    foreach (var p in All)
                    {
                        if (p == null || p.Dead.Value || p == this || p.WackySafe) continue;
                        if (Vector3.Distance(p.transform.position + Vector3.up * 0.9f, point) > Cfg.SlowRadius) continue;
                        p.SlowUntil.Value = Now + Cfg.SlowTime;
                        p.Note("SLOWED!");
                    }
                    break;
                }
                case Item.ChaosSigil: ServerConsumeHeld(); g.ServerChaos(this); break;
                case Item.DroneBot: ServerStartDrone(); break;
                case Item.BaseRebuild:
                {
                    int n = g.ServerRebuildBase(Team.Value);
                    if (n == 0) { Note($"Nothing of your base was destroyed in the last {Cfg.RebuildWindow:0} s"); return; }
                    ServerConsumeHeld();
                    Note($"Rebuilt {n} piece{(n == 1 ? "" : "s")} of your base!");
                    break;
                }
                case Item.FakeFlare:
                    if (!g.ServerFakeDrop(this)) { Note("The sky's busy - try again in a moment"); return; }
                    ServerConsumeHeld();
                    break;
                case Item.DisguiseKit:
                {
                    if (!Cfg.TeamGame) { Note("The disguise kit only works in team games"); return; }
                    int enemy = -1;
                    for (int k = 1; k < Cfg.TeamCount && enemy < 0; k++) { int t = (Team.Value + k) % Cfg.TeamCount; foreach (var p in All) if (p != null && p.Team.Value == t) { enemy = t; break; } }
                    if (enemy < 0) enemy = (Team.Value + 1) % Mathf.Max(2, Cfg.TeamCount);
                    ServerConsumeHeld();
                    DisguiseTeam.Value = (byte)enemy;
                    Fx.Server(FxKind.Poof, transform.position + Vector3.up, Vector3.up);
                    Note($"Disguised as {Cfg.TeamLabel(enemy)} - until you hit someone or get hit");
                    break;
                }
                case Item.BallGrabber: ServerStartBallGrab(); break;
                case Item.AntGun:
                {
                    if (tp == null || tp == this || tp.Dead.Value || tp.WackySafe || Vector3.Distance(tp.transform.position, transform.position) > 60f || GlassBetween(transform.position, tp.transform.position)) { ServerUseCharge(Item.AntGun); return; }
                    ServerUseCharge(Item.AntGun);
                    tp.AntUntil.Value = Now + Cfg.AntTime;
                    if (tp.CarryingBall) Ball.Instance.ServerDrop(tp.transform.position + Vector3.up * 0.5f, Vector3.up * 2f);
                    Fx.Server(FxKind.AntZap, tp.transform.position + Vector3.up, Vector3.up);
                    tp.Note($"You're an ANT for {Cfg.AntTime:0} s!");
                    break;
                }
            }
        }

        /// <summary>One shot / use off the held item's Data (gone at 0).</summary>
        void ServerUseCharge(Item kind)
        {
            var st = HeldStack;
            if (st.Id != kind) return;
            int max = Mathf.Max(1, Cfg.MaxData(kind));
            int left = (st.Data <= 0 || st.Data > max ? max : st.Data) - 1;
            if (left <= 0) ServerClearSlot(HeldSlot.Value);
            else Inv[HeldSlot.Value] = ItemStack.Of(kind, 1, left);
        }

        /// <summary>The ground (or a floor / roof) right under a spot: the highest solid thing there that isn't the glass
        /// dome or wall, a player or a trigger.</summary>
        public static Vector3 WackyGround(Vector3 p, float above = 40f)
        {
            float top = Mathf.Max(p.y, MapBuilder.Height(p.x, p.z)) + above;
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, above + 80f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider == MapDome.Collider || h.collider.name == "glass" || h.collider.name == "glass dome") continue;
                if (h.collider.GetComponentInParent<PlayerNet>() != null || h.collider.GetComponentInParent<Ball>() != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (float.IsNegativeInfinity(best)) best = MapBuilder.Height(p.x, p.z);
            return new Vector3(p.x, best, p.z);
        }

        /// <summary>The thrown bouncy castle landed (ThrownLandRpc -> NetGame.ServerWackyLanded): it pops up there - unless
        /// that's right against the enemy's buildings or over the glass wall, when it bounces back into your bag.</summary>
        public void ServerCastleLanded(NetGame g, Vector3 point)
        {
            var at = WackyGround(point, 4f);
            string problem = GlassBetween(transform.position, at) ? "Not on the other side of the glass wall" : Deployables.BarricadeEnemyProblem(Team.Value, at);
            if (problem != null)
            {
                Note(problem + " - it bounced back to you");
                int left = ServerGive(Item.BouncyCastle, 1);
                if (left > 0) g.ServerDropItem(ItemStack.Of(Item.BouncyCastle, left), transform.position + transform.forward, transform.forward, EyePos);
                return;
            }
            var toMe = transform.position - at; toMe.y = 0f;
            float yaw = toMe.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toMe).eulerAngles.y : 0f;
            g.ServerAddGadget(Gadget.Castle, at, yaw, Team.Value, Cfg.CastleLife, Random.Range(1, 99999));
            Fx.Server(FxKind.Boing, at + Vector3.up, Vector3.up);
        }

        void ServerOmenTeleport(Vector3 point)
        {
            float half = Cfg.MapHalf - 4f;
            var p = new Vector3(Mathf.Clamp(point.x, -half, half), 0f, Mathf.Clamp(point.z, -half, half));
            if (MapBuilder.GlassUp && Cfg.RegionOf(p) != Cfg.RegionOf(transform.position)) { Note("The glass wall is still up - pick a spot on your side"); return; }
            var at = WackyGround(p) + Vector3.up * 0.15f;
            ServerConsumeHeld();
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 1.2f, Vector3.zero); // (the ball doesn't come along)
            ServerDismount();
            Fx.Server(FxKind.OmenFlash, transform.position + Vector3.up, Vector3.up);
            TeleportRpc(at, transform.eulerAngles.y);
            Fx.Server(FxKind.OmenFlash, at + Vector3.up, Vector3.up);
        }

        // ---------------- the blue shell ----------------

        void ServerBlueShell()
        {
            // the leader: an enemy carrying the ball, else the enemy with the most kills (then the nearest)
            PlayerNet best = null;
            float bestScore = float.MinValue;
            foreach (var p in All)
            {
                if (p == null || p == this || p.Dead.Value || p.Team.Value == Team.Value) continue;
                float score = (p.CarryingBall ? 100000f : 0f) + p.Kills.Value * 100f - Vector3.Distance(p.transform.position, transform.position) * 0.01f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best == null) { Note("Nobody to send it at"); return; }
            ServerConsumeHeld();
            Vehicle.ServerSpawnShell(transform.position + Vector3.up * 2.2f, this, best);
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{DisplayName} fired a BLUE SHELL at {best.DisplayName}!");
            best.Note("A BLUE SHELL is coming for you...");
        }

        /// <summary>Server: the blue shell got them - knocked flat for a moment and half of what they carry spills out
        /// (no damage).</summary>
        public void ServerKnockDown(Vector3 from)
        {
            if (Dead.Value || WackySafe) return;
            KnockUntil.Value = Now + Cfg.KnockDownTime;
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 1.2f, Vector3.up * 4f);
            ServerDismount();
            var spill = new List<ItemStack>();
            for (int i = 0; i < Inv.Count; i++)
            {
                var st = Inv[i];
                if (st.Empty) continue;
                // half of every stack (half the single things, picked at random)
                int n = st.Count > 1 ? st.Count / 2 : (Random.value < 0.5f ? 1 : 0);
                if (n <= 0) continue;
                spill.Add(st.WithCount(n));
                Inv[i] = st.WithCount(st.Count - n);
            }
            if (spill.Count > 0 && NetGame.Instance != null) NetGame.Instance.ServerScatter(spill, transform.position + Vector3.up * 1.1f);
            var push = transform.position - from; push.y = 0f;
            KnockbackRpc((push.sqrMagnitude > 0.01f ? push.normalized : -transform.forward) * 6f + Vector3.up * 7f);
            Fx.Server(FxKind.ShellBoom, transform.position + Vector3.up, Vector3.up);
            Note("KNOCKED FLAT by the blue shell - half your things fell out!");
        }

        // ---------------- the suicide bomb ----------------

        float m_SuicideAt = -1f;

        void ServerTickSuicide()
        {
            if (m_SuicideAt < 0f || Time.time < m_SuicideAt) return;
            m_SuicideAt = -1f;
            if (Dead.Value) return;
            var at = transform.position + Vector3.up * 1f;
            Fx.Server(FxKind.Explosion, at, Vector3.up);
            // everyone close by who can be seen from the bomb (no walls in the way) goes with you
            foreach (var p in All)
            {
                if (p == null || p == this || p.Dead.Value) continue;
                var c = p.transform.position + Vector3.up * 1f;
                if (Vector3.Distance(c, at) > Cfg.SuicideRadius) continue;
                if (WallBetween(at, c, p)) continue;
                p.ServerKill(this, (byte)Item.SuicideBomb);
                if (p.Dead.Value) KillConfirmRpc();
            }
            bool god = m_God; m_God = false;
            ServerKill(this, (byte)Item.SuicideBomb);
            m_God = god;
        }

        /// <summary>Something solid (not a player, not the ball) between two points.</summary>
        static bool WallBetween(Vector3 a, Vector3 b, PlayerNet skip)
        {
            var d = b - a;
            foreach (var h in Physics.RaycastAll(a, d.normalized, d.magnitude, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerNet>() != null || h.collider.GetComponentInParent<Ball>() != null) continue;
                if (h.collider.GetComponentInParent<Vehicle>() != null) continue;
                return true;
            }
            return false;
        }

        // ---------------- the turret bot ----------------

        void ServerStartDrone()
        {
            if (Droning || Riding) return;
            ServerConsumeHeld();
            var at = transform.position + transform.forward * 1.2f + Vector3.up * 0.4f;
            var v = Vehicle.ServerSpawnDrone(at, transform.eulerAngles.y, this);
            DroneId.Value = v.NetworkObjectId;
            Note("Driving the turret bot: WASD, Space hops, LMB blows it up, E back to your body");
        }

        /// <summary>The bot's LMB: it blows up - anyone close is left on 5 HP, anyone on 40 or less dies.</summary>
        [Rpc(SendTo.Server)]
        public void DroneBlastRpc()
        {
            var v = MyDrone;
            if (v == null) { DroneId.Value = 0; return; }
            var at = v.transform.position + Vector3.up * 0.3f;
            Fx.Server(FxKind.Explosion, at, Vector3.up);
            foreach (var p in All)
            {
                if (p == null || p.Dead.Value || p == this || p.WackySafe) continue;
                var c = p.transform.position + Vector3.up * 1f;
                if (Vector3.Distance(c, at) > Cfg.DroneBlastRadius || WallBetween(at, c, p)) continue;
                if (p.Health.Value <= 40f) { p.ServerKill(this, (byte)Item.DroneBot); if (p.Dead.Value) KillConfirmRpc(); }
                else { p.ArmorHp.Value = 0; p.Health.Value = 5f; p.ServerBleed(false, c, (c - at).normalized); p.Note("The turret bot blew up next to you - 5 HP left!"); }
            }
            ServerEndDrone(false);
        }

        /// <summary>E while driving the bot: back to your body (the bot powers down where it is, in a puff).</summary>
        [Rpc(SendTo.Server)]
        public void DroneExitRpc() => ServerEndDrone(true);

        Vehicle MyDrone => DroneId.Value != 0 && NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(DroneId.Value, out var no) ? no.GetComponent<Vehicle>() : null;

        public void ServerEndDrone(bool poof)
        {
            if (!IsServer) return;
            var v = MyDrone;
            DroneId.Value = 0;
            if (v == null || !v.IsSpawned) return;
            if (poof) Fx.Server(FxKind.Poof, v.transform.position + Vector3.up * 0.3f, Vector3.up);
            v.NetworkObject.Despawn(true);
        }

        // ---------------- the ball grabber ----------------

        /// <summary>The ball snaps straight into your hands (a beam from it to you), and the grabber breaks.</summary>
        void ServerStartBallGrab()
        {
            var b = Ball.Instance;
            var g = NetGame.Instance;
            if (b == null || !b.IsSpawned) { Note("There's no ball"); return; }
            if (b.IsCarried) { Note("Someone's carrying it - you can't grab it out of their hands"); return; }
            if (g != null && g.WallUp) { Note("The ball's still under the dome"); return; }
            float d = Vector3.Distance(b.transform.position, EyePos);
            if (d > Cfg.BallGrabRange) { Note($"Too far away ({d:0} m) - get within {Cfg.BallGrabRange:0} m"); return; }
            if (WallBetween(EyePos, b.transform.position, this)) { Note("Something's in the way - it can't pull through walls"); return; }
            var from = b.transform.position;
            Fx.Server(FxKind.Tractor, EyePos, (from - EyePos).normalized);
            if (!b.ServerPickup(this)) { Note("It slipped out of the beam"); return; }
            ServerClearSlot(HeldSlot.Value); // (it breaks)
            Fx.Server(FxKind.Poof, from, Vector3.up);
            Fx.Server(FxKind.Break, Ball.CarryPoint(this), Vector3.up);
            Note("Got it! The Ball Grabber 3000 broke");
        }

        // ---------------- the melee ones ----------------

        /// <summary>The knockback sticks: no damage, just a shove - the one-use stick a huge one (and it breaks).</summary>
        void ServerKnockStick(PlayerNet p, Item item, Vector3 dir)
        {
            if (GlassBetween(transform.position, p.transform.position) || p.WackySafe) return;
            var flat = new Vector3(dir.x, 0f, dir.z);
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
            bool big = item == Item.KnockStick1;
            p.KnockbackRpc(flat * (big ? Cfg.KnockStick1Push : Cfg.KnockStickPush) + Vector3.up * (big ? 16f : 6f));
            if (p.Disguised) p.DisguiseTeam.Value = 255;
            Fx.Server(FxKind.Bonk, p.transform.position + Vector3.up * 1.2f, flat);
            if (big)
            {
                if (p.CarryingBall) Ball.Instance.ServerDrop(p.transform.position + Vector3.up * 1.5f, flat * 8f + Vector3.up * 4f);
                ServerConsumeHeld();
                Note("SMACK! The knockback stick broke");
            }
        }

        /// <summary>The tree breaker: the whole tree comes down in one hit, for a flat Cfg.TreeBreakerWood.</summary>
        void ServerTreeBreak(ResourceNode n, Vector3 point, Vector3 dir)
        {
            int got = n.ServerHarvest(999999, false, transform.position);
            if (got <= 0) return;
            ServerGive(Cfg.GatherItem(Item.Wood), Cfg.GatherCount(Item.Wood, Mathf.Max(1, Cfg.TreeBreakerWood)));
            TimberRpc(Cfg.TreeBreakerWood);
            Fx.Server(FxKind.Timber, n.transform.position + Vector3.up * 2f, Vector3.up);
            Fx.Server(FxKind.WoodChips, point, -dir, OwnerClientId);
            ServerUseCharge(Item.TreeBreaker);
        }

        // ---------------- the timeout ball ----------------

        int m_PendingTimeout;

        [Rpc(SendTo.Server)]
        public void ThrowTimeoutRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || HeldItem != Item.TimeoutBall || WackyNoAttack || Time.time < m_NextThrow || InSuddenDeath) return;
            m_NextThrow = Time.time + 0.5f;
            ServerConsumeHeld();
            m_PendingTimeout = Mathf.Min(m_PendingTimeout + 1, 3);
            TimeoutVisualRpc(origin, velocity);
        }

        [Rpc(SendTo.NotOwner)]
        void TimeoutVisualRpc(Vector3 origin, Vector3 velocity) { TimeoutBall.Spawn(origin, velocity, this, false); LocalThrowAnim(); }

        /// <summary>The thrower's ball hit someone (them too, after a bounce): locked in for Cfg.PrisonTime.</summary>
        [Rpc(SendTo.Server)]
        public void TimeoutHitRpc(NetworkObjectReference target)
        {
            if (m_PendingTimeout <= 0) return;
            m_PendingTimeout--;
            if (!target.TryGet(out var no) || !no.TryGetComponent(out PlayerNet p) || p.Dead.Value || p.Prisoned) return;
            if (Vector3.Distance(p.transform.position, transform.position) > 80f) return;
            p.PrisonUntil.Value = Now + Cfg.PrisonTime;
            p.m_PrisonWasOn = true;
            if (p.CarryingBall) Ball.Instance.ServerDrop(p.transform.position + Vector3.up * 2.6f, Vector3.up * 3f);
            Fx.Server(FxKind.Boing, p.transform.position + Vector3.up, Vector3.up);
            p.Note(p == this ? $"You timed YOURSELF out! {Cfg.PrisonTime:0} s" : $"TIMEOUT! Locked in a ball for {Cfg.PrisonTime:0} s");
        }

        [Rpc(SendTo.Server)]
        public void TimeoutMissRpc() { if (m_PendingTimeout > 0) m_PendingTimeout--; }

        bool m_PrisonWasOn;

        /// <summary>Server: the timeout ball's time is up - it pops in a cloud of smoke that hangs about a while.</summary>
        void ServerTickPrison()
        {
            if (!m_PrisonWasOn || Prisoned) return;
            m_PrisonWasOn = false;
            if (Dead.Value || NetGame.Instance == null) return;
            NetGame.Instance.ServerAddGadget(Gadget.Smoke, transform.position, 0f, Team.Value, 6f, Random.Range(1, 99999));
            Fx.Server(FxKind.Poof, transform.position + Vector3.up, Vector3.up);
        }

        // ---------------- bear juice ----------------

        /// <summary>Server: this player is a BEAR for Cfg.BearTime (no turning back until then, unless they die).</summary>
        public void ServerMakeBear()
        {
            if (Dead.Value) return;
            if (Riding) ServerDismount();
            BearHpLeft.Value = Cfg.BearHp;
            BearUntil.Value = Now + Cfg.BearTime;
            Fx.Server(FxKind.BearPoof, transform.position + Vector3.up, Vector3.up);
            Note($"You're a BEAR for {Cfg.BearTime / 60f:0.#} minutes! Claws: LMB");
        }

        /// <summary>Server: the bear's time is up - back to an alien.</summary>
        void ServerTickBear()
        {
            if (BearHpLeft.Value <= 0f || Now < BearUntil.Value) return;
            BearHpLeft.Value = 0f;
            BearUntil.Value = -1;
            if (Dead.Value) return;
            Fx.Server(FxKind.BearPoof, transform.position + Vector3.up, Vector3.up);
            Note("The bear juice wore off - you're an alien again");
        }

        float m_NextClaw;

        /// <summary>A bear's swipe: big damage to players, and it tears through buildings.</summary>
        [Rpc(SendTo.Server)]
        public void BearClawRpc(bool hasTarget, NetworkObjectReference target, Vector3 point)
        {
            if (!IsBear || Dead.Value || Time.time < m_NextClaw || WackyHeld || !GameAllowsCombat) return;
            m_NextClaw = Time.time + 0.6f;
            SwingRpc();
            Reveal();
            Fx.Server(FxKind.BearSwipe, EyePos + transform.forward * 1.7f - Vector3.up * 0.4f, transform.right);
            if (!hasTarget || !target.TryGet(out var no) || Vector3.Distance(EyePos, point) > 4.5f) return;
            var dir = (point - EyePos).normalized;
            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value) return;
                ServerHitPlayer(p, Cfg.BearClawDamage, point, dir, Cfg.BearClawDamage * 1.5f, (byte)Item.BearJuice);
                p.KnockbackRpc(dir * 6f + Vector3.up * 3f);
            }
            else if (no.TryGetComponent(out Structure s)) { s.ServerDamageAt(Cfg.BearClawStructure * (s.Tier.Value >= 2 ? 0.5f : 1f), point); Fx.Server(FxKind.Smash, point, -dir); }
            else if (no.TryGetComponent(out Container c) && c.Breakable) { c.ServerDamage(Cfg.BearClawStructure); Fx.Server(FxKind.Smash, point, -dir); }
            else if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(Cfg.BearClawDamage, this);
        }

        // ---------------- drawn monsters and the scribble notebook ----------------

        /// <summary>The finished drawing (Hud.Wacky's canvas, 64 x 64, 4 bits a pixel): a monster made of it comes alive.</summary>
        [Rpc(SendTo.Server)]
        public void MonsterDrawingRpc(byte[] pixels)
        {
            if (Dead.Value || HeldItem != Item.DrawMonster || pixels == null || pixels.Length != MonsterArt.Bytes) return;
            ServerConsumeHeld();
            var at = transform.position + transform.forward * 2f + Vector3.up * 0.2f;
            Vehicle.ServerSpawnMonster(at, this, pixels);
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{DisplayName}'s drawing came ALIVE!");
        }

        /// <summary>The scribble notebook: whatever was typed appears and does something (NetGame.Wacky.cs: ServerScribble).</summary>
        [Rpc(SendTo.Server)]
        public void ScribbleRpc(FixedString64Bytes word, Vector3 aimPoint)
        {
            if (Dead.Value || HeldItem != Item.Scribble || NetGame.Instance == null) return;
            string w = word.ToString().Trim();
            if (w.Length == 0) return;
            ServerConsumeHeld();
            NetGame.Instance.ServerScribble(this, w, aimPoint);
        }

        // ---------------- the minigames' moves ----------------

        /// <summary>A Connect 4 column, or a gameshow answer (0-3).</summary>
        [Rpc(SendTo.Server)]
        public void MiniMoveRpc(int arg) { if (NetGame.Instance != null) NetGame.Instance.ServerMiniMove(this, arg); }

        /// <summary>Someone hit a decoy base: it's gone in smoke (and a laugh).</summary>
        [Rpc(SendTo.Server)]
        public void DecoyBaseHitRpc(int id)
        {
            var g = NetGame.Instance;
            if (g == null) return;
            if (g.ServerFindGadget(id, out var gd) && gd.Kind == Gadget.DecoyBase && Vector3.Distance(gd.Pos, transform.position) < 120f)
                g.ServerRemoveGadget(id, true);
        }

        // =====================================================================
        // every frame (server)
        // =====================================================================

        void ServerTickWacky()
        {
            ServerTickSuicide();
            ServerTickPrison();
            ServerTickBear();
            // a drone that's gone (shot down) gives you your body back
            if (Droning && MyDrone == null) DroneId.Value = 0;
            // carrying the ball as an ant: you drop it
            if (IsAnt && CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 0.4f, Vector3.up * 2f);
        }
    }
}
