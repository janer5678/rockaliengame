using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Something a wacky item left in the world (Gadgets.cs draws them on every screen).</summary>
    public struct Gadget : INetworkSerializeByMemcpy, System.IEquatable<Gadget>
    {
        /// <summary>A bouncy castle, an attention snail, a cloud of clutter, the Stick of Truth's bent ground, a decoy base,
        /// a scribbled word block, and smoke (a timeout ball popping).</summary>
        public const byte Castle = 1, Snail = 2, Clutter = 3, Truth = 4, DecoyBase = 5, Word = 6, Smoke = 7;
        public int Id, Seed;
        public byte Kind, Team;
        public Vector3 Pos;
        public float Yaw;
        /// <summary>Server time it goes (0 = it stays), and when it came.</summary>
        public double Until, Born;
        public FixedString32Bytes Text;
        public bool Equals(Gadget o) => Id == o.Id && Kind == o.Kind && Pos == o.Pos && Until == o.Until && Seed == o.Seed && Text.Equals(o.Text);
    }

    /// <summary>The Connect 4 game or the gameshow question everyone's watching (NetGame.Mini).</summary>
    public struct MiniGame : INetworkSerializeByMemcpy, System.IEquatable<MiniGame>
    {
        public const byte None = 0, Connect4 = 1, Gameshow = 2;
        public const int Cols = 7, Rows = 6;
        public byte Kind;
        /// <summary>A: who used the wand (moves first); B: who they zapped.</summary>
        public ulong A, B;
        /// <summary>Connect 4: whose move (0 A, 1 B). Each side's pieces as bits (column * 7 + row, row 0 at the bottom).</summary>
        public byte Turn;
        public ulong BoardA, BoardB;
        /// <summary>Connect 4's chess clocks (seconds left when the current move started), and when it started.</summary>
        public float ClockA, ClockB;
        public double TurnStart;
        /// <summary>The gameshow: the question (Trivia), and B's answer (-1 none yet).</summary>
        public short Question;
        public sbyte Answer;
        /// <summary>0 playing; 1 over - showing who lost until EndAt.</summary>
        public byte Phase;
        public ulong Loser;
        public double EndAt;
        public sbyte LastCol;
        /// <summary>Why it ended (0 four in a row / an answer, 1 a clock ran out, 2 a draw, 3 someone left).</summary>
        public byte Why;
        public bool Equals(MiniGame o) => Kind == o.Kind && A == o.A && B == o.B && Turn == o.Turn && BoardA == o.BoardA && BoardB == o.BoardB
            && ClockA == o.ClockA && ClockB == o.ClockB && TurnStart == o.TurnStart && Question == o.Question && Answer == o.Answer && Phase == o.Phase
            && Loser == o.Loser && EndAt == o.EndAt && LastCol == o.LastCol && Why == o.Why;

        public static bool Has(ulong board, int col, int row) => (board & (1UL << (col * 7 + row))) != 0;
        public int Height(int col) { int h = 0; while (h < Rows && (Has(BoardA, col, h) || Has(BoardB, col, h))) h++; return h; }
        public static bool Four(ulong b)
        {
            foreach (int d in new[] { 1, 7, 6, 8 })
            {
                ulong m = b & (b >> d);
                if ((m & (m >> (2 * d))) != 0) return true;
            }
            return false;
        }
    }

    public partial class NetGame
    {
        public readonly NetworkList<Gadget> Gadgets = new NetworkList<Gadget>();
        public readonly NetworkVariable<MiniGame> Mini = new NetworkVariable<MiniGame>();
        public bool MinigameOn => Mini.Value.Kind != MiniGame.None;

        int m_NextGadgetId = 1;
        bool m_MiniPausedTimer;
        double m_MiniBotAt = -1;

        // =====================================================================
        // gadgets
        // =====================================================================

        /// <summary>Server: something a wacky item leaves in the world. life 0 = it stays. Returns its id.</summary>
        public int ServerAddGadget(byte kind, Vector3 pos, float yaw, int team, float life, int seed, string text = null)
        {
            double now = NetworkManager.ServerTime.Time;
            // (not too many: the oldest ones go)
            while (Gadgets.Count >= 48) Gadgets.RemoveAt(0);
            var g = new Gadget { Id = m_NextGadgetId++, Kind = kind, Team = (byte)Mathf.Clamp(team, 0, 255), Pos = pos, Yaw = yaw, Seed = seed, Born = now, Until = life > 0f ? now + life : 0 };
            if (!string.IsNullOrEmpty(text)) g.Text = new FixedString32Bytes(text.Length > 28 ? text.Substring(0, 28) : text);
            Gadgets.Add(g);
            return g.Id;
        }

        public bool ServerFindGadget(int id, out Gadget g)
        {
            for (int i = 0; i < Gadgets.Count; i++) if (Gadgets[i].Id == id) { g = Gadgets[i]; return true; }
            g = default;
            return false;
        }

        /// <summary>Server: it goes (poof: in a big puff of smoke - and the decoy base laughs at you).</summary>
        public void ServerRemoveGadget(int id, bool poof)
        {
            for (int i = 0; i < Gadgets.Count; i++)
            {
                if (Gadgets[i].Id != id) continue;
                var g = Gadgets[i];
                Gadgets.RemoveAt(i);
                if (poof) Fx.Server(g.Kind == Gadget.DecoyBase ? FxKind.BigPoof : FxKind.Poof, g.Pos + Vector3.up, Vector3.up);
                return;
            }
        }

        void ServerTickGadgets(double now)
        {
            for (int i = Gadgets.Count - 1; i >= 0; i--)
            {
                var g = Gadgets[i];
                if (g.Until <= 0 || now < g.Until) continue;
                Gadgets.RemoveAt(i);
                if (g.Kind == Gadget.Castle) Fx.Server(FxKind.Deflate, g.Pos + Vector3.up, Vector3.up);
            }
        }

        // =====================================================================
        // the minigames (Connect 4, the gameshow)
        // =====================================================================

        /// <summary>Server: a wand zapped someone - everything stops (the clock too) and everyone watches.</summary>
        public bool ServerStartMinigame(byte kind, PlayerNet a, PlayerNet b)
        {
            if (MinigameOn || a == null || b == null || a == b) return false;
            double now = NetworkManager.ServerTime.Time;
            var m = new MiniGame
            {
                Kind = kind, A = a.NetworkObjectId, B = b.NetworkObjectId, Turn = 0,
                ClockA = Cfg.MinigameClock, ClockB = kind == MiniGame.Gameshow ? Cfg.GameshowAnswerTime : Cfg.MinigameClock,
                TurnStart = now, Question = (short)Random.Range(0, Trivia.Count), Answer = -1, LastCol = -1,
            };
            if (kind == MiniGame.Gameshow) m.Turn = 1; // (only the one who was zapped answers)
            m_MiniPausedTimer = !TimerPaused.Value;
            TimerPaused.Value = true;
            Mini.Value = m;
            m_MiniBotAt = now + 1.5;
            foreach (var p in PlayerNet.All) if (p != null && p.Riding) p.ServerDismount();
            Broadcast(kind == MiniGame.Connect4 ? $"{a.DisplayName} challenged {b.DisplayName} to CONNECT 4 - the loser dies!" : $"{b.DisplayName} is on the GAMESHOW - get it right or die!");
            return true;
        }

        PlayerNet ById(ulong id) => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) ? no.GetComponent<PlayerNet>() : null;

        /// <summary>A move from a player: a Connect 4 column (their turn), or the gameshow answer (the one zapped).</summary>
        public void ServerMiniMove(PlayerNet p, int arg)
        {
            var m = Mini.Value;
            if (m.Kind == MiniGame.None || m.Phase != 0 || p == null) return;
            double now = NetworkManager.ServerTime.Time;
            if (m.Kind == MiniGame.Connect4)
            {
                ulong mover = m.Turn == 0 ? m.A : m.B;
                if (p.NetworkObjectId != mover || arg < 0 || arg >= MiniGame.Cols) return;
                int h = m.Height(arg);
                if (h >= MiniGame.Rows) return;
                ulong bit = 1UL << (arg * 7 + h);
                float used = (float)(now - m.TurnStart);
                if (m.Turn == 0) { m.BoardA |= bit; m.ClockA = Mathf.Max(0f, m.ClockA - used); }
                else { m.BoardB |= bit; m.ClockB = Mathf.Max(0f, m.ClockB - used); }
                m.LastCol = (sbyte)arg;
                Fx.Server(FxKind.Clack, p.transform.position, Vector3.up);
                if (MiniGame.Four(m.Turn == 0 ? m.BoardA : m.BoardB)) { MiniOver(ref m, m.Turn == 0 ? m.B : m.A, 0); }
                else
                {
                    bool full = true;
                    for (int c = 0; c < MiniGame.Cols; c++) if (m.Height(c) < MiniGame.Rows) full = false;
                    if (full) MiniOver(ref m, 0, 2);
                    else { m.Turn = (byte)(1 - m.Turn); m.TurnStart = now; m_MiniBotAt = now + 1.2; }
                }
                Mini.Value = m;
            }
            else if (m.Kind == MiniGame.Gameshow)
            {
                if (p.NetworkObjectId != m.B || arg < 0 || arg > 3) return;
                m.Answer = (sbyte)arg;
                bool right = arg == Trivia.Correct(m.Question);
                MiniOver(ref m, right ? m.A : m.B, 0);
                Mini.Value = m;
            }
        }

        void MiniOver(ref MiniGame m, ulong loser, byte why)
        {
            m.Phase = 1;
            m.Loser = loser;
            m.Why = why;
            m.EndAt = NetworkManager.ServerTime.Time + 3.5;
        }

        void ServerTickMinigame(double now)
        {
            var m = Mini.Value;
            if (m.Kind == MiniGame.None) return;
            var a = ById(m.A);
            var b = ById(m.B);
            if (m.Phase == 0)
            {
                // someone left or died: it's off
                if (a == null || b == null || a.Dead.Value || b.Dead.Value) { MiniOver(ref m, 0, 3); Mini.Value = m; return; }
                // a clock ran out: that player loses
                float left = (m.Kind == MiniGame.Gameshow || m.Turn == 1 ? m.ClockB : m.ClockA) - (float)(now - m.TurnStart);
                if (left <= 0f) { MiniOver(ref m, m.Kind == MiniGame.Gameshow || m.Turn == 1 ? m.B : m.A, 1); Mini.Value = m; return; }
                // a bot's move
                var mover = m.Kind == MiniGame.Gameshow ? b : (m.Turn == 0 ? a : b);
                if (mover != null && mover.Bot.Value && now >= m_MiniBotAt)
                {
                    m_MiniBotAt = now + 1.5;
                    if (m.Kind == MiniGame.Gameshow) ServerMiniMove(mover, Random.value < 0.5f ? Trivia.Correct(m.Question) : Random.Range(0, 4));
                    else
                    {
                        int col = BotColumn(m, m.Turn);
                        ServerMiniMove(mover, col);
                    }
                }
                return;
            }
            if (now < m.EndAt) return;
            // the result's been shown: the loser dies, and everything carries on
            var loser = ById(m.Loser);
            var winner = loser == null ? null : loser == a ? b : a;
            Mini.Value = default;
            if (m_MiniPausedTimer) TimerPaused.Value = false;
            m_MiniPausedTimer = false;
            if (loser != null && !loser.Dead.Value)
            {
                loser.ServerKill(winner, (byte)(m.Kind == MiniGame.Connect4 ? Item.Connect4Wand : Item.GameshowWand));
                if (winner != null && loser.Dead.Value) winner.KillConfirmRpc();
            }
        }

        /// <summary>A bot's Connect 4 move: win if it can, block if it must, otherwise somewhere near the middle.</summary>
        static int BotColumn(MiniGame m, int turn)
        {
            ulong mine = turn == 0 ? m.BoardA : m.BoardB, theirs = turn == 0 ? m.BoardB : m.BoardA;
            for (int pass = 0; pass < 2; pass++)
                for (int c = 0; c < MiniGame.Cols; c++)
                {
                    int h = m.Height(c);
                    if (h >= MiniGame.Rows) continue;
                    ulong bit = 1UL << (c * 7 + h);
                    if (MiniGame.Four((pass == 0 ? mine : theirs) | bit)) return c;
                }
            for (int k = 0; k < 20; k++)
            {
                int c = Mathf.Clamp(3 + Mathf.RoundToInt(Random.Range(-2.5f, 2.5f)), 0, MiniGame.Cols - 1);
                if (m.Height(c) < MiniGame.Rows) return c;
            }
            for (int c = 0; c < MiniGame.Cols; c++) if (m.Height(c) < MiniGame.Rows) return c;
            return 0;
        }

        // =====================================================================
        // the Stick of Truth, the inventory swapper, the Sigil of CHAOS
        // =====================================================================

        /// <summary>Server: the Stick of Truth's blast - the ground there is bent out of shape (Gadgets.cs draws it, solid),
        /// buildings in it are twisted and badly damaged, and players in it are launched sky high.</summary>
        public void ServerStickOfTruth(Vector3 point, PlayerNet by)
        {
            var ground = PlayerNet.WackyGround(point, 6f);
            float r = Cfg.TruthRadius;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value || p.WackySafe) continue;
                var d = p.transform.position - ground; d.y *= 0.5f;
                if (d.magnitude > r + 1f) continue;
                if (p.Riding) p.ServerDismount();
                if (p.CarryingBall) Ball.Instance.ServerDrop(p.transform.position + Vector3.up * 1.5f, Vector3.up * 6f);
                var side = new Vector3(d.x, 0f, d.z);
                p.KnockbackRpc(side.normalized * 3f + Vector3.up * Cfg.TruthLaunch);
                p.WackyNoteRpc(new FixedString128Bytes("The TRUTH sends you SKY HIGH"));
            }
            for (int i = Structure.All.Count - 1; i >= 0; i--)
            {
                var s = Structure.All[i];
                if (s == null || !s.IsSpawned) continue;
                if (Vector3.Distance(s.transform.position, ground) > r + 1.5f) continue;
                s.Warp.Value = (byte)Random.Range(1, 255);
                s.ServerDamage(s.MaxHp * 0.45f);
            }
            ServerAddGadget(Gadget.Truth, ground, Random.Range(0f, 360f), by != null ? by.Team.Value : 0, 0f, Random.Range(1, 1 << 24));
            Fx.Server(FxKind.TruthBlast, ground + Vector3.up, Vector3.up);
        }

        /// <summary>Server: two players at random (anyone alive, the user too) swap their whole bags.</summary>
        public void ServerSwapInventories(PlayerNet by)
        {
            var alive = new List<PlayerNet>();
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && !p.Dead.Value) alive.Add(p);
            if (alive.Count < 2) { by.WackyNoteRpc(new FixedString128Bytes("Nobody to swap with - it fizzled")); return; }
            int i = Random.Range(0, alive.Count), j = Random.Range(0, alive.Count - 1);
            if (j >= i) j++;
            var a = alive[i];
            var b = alive[j];
            int n = Mathf.Min(a.Inv.Count, b.Inv.Count);
            for (int k = 0; k < n; k++) { var t = a.Inv[k]; a.Inv[k] = b.Inv[k]; b.Inv[k] = t; }
            Fx.Server(FxKind.Swap, a.transform.position + Vector3.up, Vector3.up);
            Fx.Server(FxKind.Swap, b.transform.position + Vector3.up, Vector3.up);
            Broadcast($"INVENTORY SWAP! {a.DisplayName} and {b.DisplayName} swapped everything");
            a.WackyNoteRpc(new FixedString128Bytes($"Your bag was swapped with {b.DisplayName}'s!"));
            b.WackyNoteRpc(new FixedString128Bytes($"Your bag was swapped with {a.DisplayName}'s!"));
        }

        /// <summary>The Sigil of CHAOS: until this server time every item lying in the world glows through walls on every
        /// screen (NetGame.SyncItemVisuals - ChaosGlow).</summary>
        public readonly NetworkVariable<double> ChaosGlowUntil = new NetworkVariable<double>(-1);
        public bool ChaosGlowOn => IsSpawned && ChaosGlowUntil.Value > NetworkManager.ServerTime.Time;

        /// <summary>Server: every item in the game - everyone's bags, every chest and crate, everything on the ground - rains
        /// down from the sky, spread evenly over the whole map (a shuffled grid of spots, one each), and they all glow
        /// through walls for Cfg.ChaosGlowTime so everyone can see where they went.</summary>
        public void ServerChaos(PlayerNet by)
        {
            var all = new List<ItemStack>();
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned) continue;
                for (int i = 0; i < p.Inv.Count; i++) { if (!p.Inv[i].Empty) all.Add(p.Inv[i]); p.Inv[i] = default; }
            }
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned || c.IsFakeDrop || !(c.Kind.Value == Container.Chest || c.IsAirdrop || c.IsBag)) continue;
                for (int i = 0; i < c.Slots.Count; i++) { if (!c.Slots[i].Empty) all.Add(c.Slots[i]); c.Slots[i] = default; }
            }
            for (int i = Items.Count - 1; i >= 0; i--) { all.Add(Items[i].Stack); m_ItemBorn.Remove(Items[i].Id); Items.RemoveAt(i); }
            all.RemoveAll(st => st.Empty);
            var spots = ChaosSpots(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var at = spots[i];
                ServerDropItem(all[i], at + Vector3.up * 0.5f, Random.insideUnitSphere, at + Vector3.up * 70f + Random.insideUnitSphere * 6f);
            }
            ChaosGlowUntil.Value = NetworkManager.ServerTime.Time + Cfg.ChaosGlowTime;
            Fx.Server(FxKind.ChaosFlash, by != null ? by.transform.position + Vector3.up : Vector3.up * 10f, Vector3.up);
            Broadcast($"{(by != null ? by.DisplayName : "Someone")} raised the SIGIL OF CHAOS - every item in the game is raining down!");
        }

        /// <summary>n spots spread evenly over the whole map: a grid of cells over it (not the dome while the glass is up, not
        /// the sea on a theme map), shuffled, one item in each - a little off the middle of its cell so it's not a parade.
        /// More items than cells: round again.</summary>
        static List<Vector3> ChaosSpots(int n)
        {
            var spots = new List<Vector3>();
            if (n <= 0) return spots;
            float half = Cfg.MapHalf - 8f;
            int side = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(n * 1.15f)));
            float cell = half * 2f / side;
            var cells = new List<Vector3>();
            for (int x = 0; x < side; x++)
                for (int z = 0; z < side; z++)
                {
                    var c = new Vector3(-half + (x + 0.5f) * cell, 0f, -half + (z + 0.5f) * cell);
                    if (MapBuilder.GlassUp && new Vector2(c.x, c.z).magnitude < MapBuilder.DomeRadius + 2f) continue;
                    if (!ThemeMaps.SpotOk(c)) continue; // THEME MAPS (not in the sea)
                    cells.Add(c);
                }
            if (cells.Count == 0) cells.Add(new Vector3(0f, 0f, -half * 0.5f));
            for (int i = cells.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = cells[i]; cells[i] = cells[j]; cells[j] = t; }
            for (int i = 0; i < n; i++)
            {
                var c = cells[i % cells.Count] + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)) * cell;
                c.x = Mathf.Clamp(c.x, -half, half); c.z = Mathf.Clamp(c.z, -half, half);
                c.y = MapBuilder.Height(c.x, c.z);
                spots.Add(c);
            }
            return spots;
        }

        // =====================================================================
        // the fake airdrop flare
        // =====================================================================

        /// <summary>Server: a FAKE airdrop - announced and beamed down like a real one, on a free lane. Its crate shows every
        /// item in the game and none of them come out (Container.FakeDrop).</summary>
        public bool ServerFakeDrop(PlayerNet by)
        {
            int lane = -1;
            for (int i = LaneTotal - 1; i >= 0 && lane < 0; i--)
                if (!m_Lanes[i].Incoming && (m_Lanes[i].Crate == null || !m_Lanes[i].Crate.IsSpawned || m_Lanes[i].Crate.Empty)) lane = i;
            if (lane < 0) return false;
            var l = m_Lanes[lane];
            if (l.Crate != null && l.Crate.IsSpawned) l.Crate.NetworkObject.Despawn(true);
            l.Crate = null;
            l.Region = -1;
            l.Fake = true;
            double now = NetworkManager.ServerTime.Time;
            ServerLaunchDrop(lane, now, PickDropSpot(-1), false);
            ServerAirdropIncoming(now);
            return true;
        }

        /// <summary>What's in a fake crate: one of every item in the game.</summary>
        static List<ItemStack> FakeLoot()
        {
            var l = new List<ItemStack>();
            foreach (var it in Cfg.AllFunItems) l.Add(Cfg.GiftStack(it));
            return l;
        }

        // =====================================================================
        // the instant base rebuild
        // =====================================================================

        struct GonePiece { public float At; public PieceType Type; public byte Team, Tier; public Vector3 Pos; public Quaternion Rot; public PieceKey Key; public bool HasKey; }
        readonly List<GonePiece> m_GonePieces = new List<GonePiece>();

        /// <summary>Server: a piece of someone's base was destroyed (not demolished) - remembered a while for the rebuild.</summary>
        public void ServerNoteStructureGone(Structure s)
        {
            if (s == null || !s.IsSpawned) return;
            m_GonePieces.Add(new GonePiece { At = Time.time, Type = s.PType, Team = s.Team.Value, Tier = s.Tier.Value, Pos = s.transform.position, Rot = s.transform.rotation, Key = s.Key, HasKey = s.HasKey });
            // (only the last minute or so is kept)
            m_GonePieces.RemoveAll(g => Time.time - g.At > Mathf.Max(30f, Cfg.RebuildWindow + 5f));
        }

        /// <summary>Server: everything of this team's destroyed in the last Cfg.RebuildWindow seconds comes back as it was.</summary>
        public int ServerRebuildBase(int team)
        {
            int n = 0;
            for (int i = m_GonePieces.Count - 1; i >= 0; i--)
            {
                var g = m_GonePieces[i];
                if (g.Team != team || Time.time - g.At > Cfg.RebuildWindow) continue;
                m_GonePieces.RemoveAt(i);
                if (g.HasKey && BuildGrid.Registry.TryGetValue(g.Key, out var there) && there != null && there.IsSpawned) continue; // (something's been built there since)
                var go = Instantiate(Bootstrap.I.structurePrefab, g.Pos, g.Rot);
                var s = go.GetComponent<Structure>();
                s.ServerInit(g.Type, g.Team, g.Key, g.HasKey);
                if (g.HasKey) BuildGrid.Registry[g.Key] = s;
                go.GetComponent<NetworkObject>().Spawn(true);
                if (g.Tier > 0 && s.Upgradable) s.ServerUpgrade(g.Tier);
                Fx.Server(FxKind.Spawn, g.Pos, Vector3.up);
                n++;
            }
            return n;
        }

        // =====================================================================
        // the scribble notebook
        // =====================================================================

        /// <summary>Server: whatever was written appears and does something. Item names give that item; a long list of words
        /// do their own thing (a horse, a bomb, food, a tower, speed...); anything else becomes a big block with the word
        /// written on it, right where you were looking.</summary>
        public void ServerScribble(PlayerNet p, string word, Vector3 aim)
        {
            string w = word.ToLowerInvariant().Trim();
            if (w.StartsWith("a ")) w = w.Substring(2);
            if (w.StartsWith("an ")) w = w.Substring(3);
            if (w.StartsWith("the ")) w = w.Substring(4);
            if (w.Length > 24) w = w.Substring(0, 24);
            var front = p.transform.position + p.transform.forward * 3f;
            if (aim == Vector3.zero || Vector3.Distance(aim, p.transform.position) > 40f || PlayerNet.GlassBetween(p.transform.position, aim)) aim = front;
            var ground = PlayerNet.WackyGround(aim, 3f);
            string said = null;
            bool Is(params string[] words) { foreach (var x in words) if (w == x || w == x + "s" || w.Contains(x)) return true; return false; }
            void Give(Item it, int count = 1)
            {
                var st = Cfg.GiftStack(it);
                int left = p.ServerGive(st.Id, Mathf.Max(count, st.Count), st.Data);
                if (left > 0) ServerDropItem(ItemStack.Of(st.Id, left, st.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
            }
            PlayerNet NearestEnemy()
            {
                PlayerNet best = null; float bd = float.MaxValue;
                foreach (var o in PlayerNet.All) { if (o == null || o.Dead.Value || o.Team.Value == p.Team.Value) continue; float d = Vector3.Distance(o.transform.position, p.transform.position); if (d < bd) { bd = d; best = o; } }
                return best;
            }

            // an item's own name: you get that item
            foreach (Item it in System.Enum.GetValues(typeof(Item)))
            {
                if (it == Item.None || it == Item.Scribble || it == Item.FortifyBuff || it == Item.WoodGenBuff || Cfg.Removed(it) || (it == Item.DisguiseKit && !Cfg.TeamGame)) continue;
                string name = Cfg.ItemName(it).ToLowerInvariant();
                if (w == name || w == it.ToString().ToLowerInvariant() || (w.Length >= 4 && name.Length >= 4 && (name == w + "s" || w == name + "s")))
                {
                    if (it == Item.Rock) break;
                    Give(it);
                    said = $"A {Cfg.ItemName(it)} appeared!";
                    break;
                }
            }
            // what people really write (guns, food, "aimbot", "nuke", "noob", "rizz", dragons...: NetGame.ScribbleWords.cs)
            if (said == null && ServerScribbleWords(p, w, ground, out var gamer)) said = gamer;
            if (said == null)
            {
                if (Is("unicorn")) { Vehicle.ServerSpawn(Vehicle.Horse, ground + Vector3.up * 0.3f, p.transform.eulerAngles.y, true); said = "A UNICORN!"; }
                else if (Is("horse", "pony", "stallion")) { Vehicle.ServerSpawn(Vehicle.Horse, ground + Vector3.up * 0.3f, p.transform.eulerAngles.y); said = "A horse appeared!"; }
                else if (Is("car", "truck", "vehicle", "kart", "bus")) { Vehicle.ServerSpawn(Vehicle.Car, ground + Vector3.up * 0.6f, p.transform.eulerAngles.y); said = "A car appeared!"; }
                else if (Is("slender", "monster", "ghost", "zombie", "demon", "creature", "alien")) { Vehicle.ServerSpawnSlender(ground + Vector3.up * 0.2f, p.Team.Value); said = "Something creepy appeared... it hunts your enemies"; }
                else if (Is("castle", "bouncy", "trampoline")) { ServerAddGadget(Gadget.Castle, ground, p.transform.eulerAngles.y + 180f, p.Team.Value, Cfg.CastleLife, Random.Range(1, 99999)); said = "A bouncy castle!"; }
                else if (Is("bush", "berry", "berries", "fruit", "plant")) { ServerSpawnBushAt(ground); said = "A berry bush grew!"; }
                else if (Is("food", "pizza", "burger", "apple", "cake", "steak", "sandwich", "taco", "cookie", "bread", "banana", "snack", "meal", "chicken")) { Give(Item.Meat, 2); said = "Food! (it heals you fully)"; }
                else if (Is("heal", "medic", "doctor", "health", "medkit", "bandage", "nurse", "hospital")) { p.Health.Value = Cfg.MaxHealth; said = "Healed to full!"; }
                else if (Is("nuke", "bomb", "explosion", "explode", "tnt", "dynamite", "grenade", "kaboom", "boom")) { ServerRocket(ground + Vector3.up * 0.5f, p); said = "KABOOM!"; }
                else if (Is("airstrike", "plane", "jet", "meteor", "asteroid")) { ServerAirstrike(ground, p); said = "Incoming from the sky!"; }
                else if (Is("rocket", "missile", "bazooka")) { Give(Item.RocketLauncher); said = "A rocket launcher!"; }
                else if (Is("tower", "lookout", "fort")) { ServerScribblePiece(PieceType.Tower, ground, p); said = "A tower popped up!"; }
                else if (Is("wall", "fence", "barrier", "cover")) { ServerScribblePiece(PieceType.Barrier, ground, p); said = "A wall appeared!"; }
                else if (Is("money", "gold", "cash", "coin", "treasure", "diamond", "rich", "wood", "log", "timber", "lumber")) { p.ServerGive(Cfg.GatherItem(Item.Wood), 1000); said = "1000 wood!"; }
                else if (Is("speed", "fast", "sonic", "flash", "cheetah", "zoom", "turbo", "coffee", "energy")) { p.SpeedUntil.Value = p.NetworkManager.ServerTime.Time + Cfg.SpeedJuiceTime; said = "SUPER SPEED!"; }
                else if (Is("invisible", "invisibility", "ninja", "stealth", "spy", "hide")) { p.InvisUntil.Value = p.NetworkManager.ServerTime.Time + Cfg.InvisTime; said = "You're invisible!"; }
                else if (Is("giant", "huge", "titan", "big", "tall")) { p.GiantUntil.Value = p.NetworkManager.ServerTime.Time + Cfg.GiantTime; said = "You're a GIANT!"; }
                else if (Is("shield", "god", "invincible", "immortal", "bubble", "force field", "forcefield")) { p.ShieldUntil.Value = p.NetworkManager.ServerTime.Time + Cfg.ShieldTime; said = "SHIELD UP!"; }
                else if (Is("armor", "armour", "knight", "helmet")) { Give(Item.HeavyArmor); said = "Armour!"; }
                else if (Is("teleport", "pearl", "warp")) { Give(Item.EnderPearl, 2); said = "Ender pearls!"; }
                else if (Is("dance", "party", "disco", "music", "rave")) { ServerDanceAt(ground, p); said = "PARTY!"; }
                else if (Is("glitter", "confetti", "sparkle", "fireworks", "rainbow", "junk", "mess")) { ServerAddGadget(Gadget.Clutter, ground, 0f, p.Team.Value, Cfg.ClutterTime, Random.Range(1, 99999)); said = "What a mess!"; }
                else if (Is("snail", "slug")) { ServerAddGadget(Gadget.Snail, ground, p.transform.eulerAngles.y + 180f, p.Team.Value, Cfg.SnailTime, Random.Range(1, 99999)); said = "Everyone, look at the snail!"; }
                else if (Is("bear", "grizzly", "panda")) { p.ServerMakeBear(); said = "You're a BEAR!"; }
                else if (Is("ant", "bug", "insect", "tiny", "small", "shrink")) { var e = NearestEnemy(); if (e != null && !e.WackySafe) { e.AntUntil.Value = e.NetworkManager.ServerTime.Time + Cfg.AntTime; Fx.Server(FxKind.AntZap, e.transform.position + Vector3.up, Vector3.up); said = $"{e.DisplayName} is an ant!"; } }
                else if (Is("slow", "sloth", "turtle", "glue", "goo", "mud", "sticky", "honey")) { var e = NearestEnemy(); if (e != null && !e.WackySafe) { e.SlowUntil.Value = e.NetworkManager.ServerTime.Time + Cfg.SlowTime; Fx.Server(FxKind.Goo, e.transform.position, Vector3.up); said = $"{e.DisplayName} is stuck in goo!"; } }
                else if (Is("tornado", "wind", "storm", "hurricane", "rain", "earthquake")) { ServerStickOfTruth(ground, p); said = "A STORM!"; }
                else if (Is("jump", "boot", "spring", "kangaroo", "frog")) { Give(Item.JumpBoots); said = "Triple jump boots!"; }
                else if (Is("drone", "robot", "bot", "camera")) { Give(Item.DroneBot); said = "A turret bot!"; }
                else if (Is("magnet", "ball")) { Give(Item.BallGrabber); said = "A Ball Grabber 3000!"; }
                else if (Is("box", "chest", "crate", "storage")) { Give(Item.Chest); said = "A storage chest!"; }
                else if (Is("map", "compass", "gps")) { Give(Item.OmenMap); said = "Omen's map!"; }
                else if (Is("clone", "twin", "decoy", "copy", "mirror")) { Vehicle.ServerSpawnDecoy(ground + Vector3.up * 0.1f, p.transform.eulerAngles.y, p, p.HeldItem); said = "A copy of you!"; }
                else if (Is("hammer", "tool", "builder")) { Give(Item.Hammer); said = "A hammer!"; }
                else if (Is("gun", "pistol", "weapon", "shooter")) { Give(Item.Revolver); Give(Item.RevolverAmmo, 18); said = "A revolver!"; }
            }
            if (said == null)
            {
                // anything else: a big solid block with the word written all over it, where you were looking
                ServerAddGadget(Gadget.Word, ground, p.transform.eulerAngles.y, p.Team.Value, 180f, Random.Range(1, 99999), word.Trim().ToUpperInvariant());
                said = $"\"{word.Trim().ToUpperInvariant()}\" appeared!";
            }
            Fx.Server(FxKind.Scribble, ground + Vector3.up, Vector3.up);
            p.WackyNoteRpc(new FixedString128Bytes(said));
        }

        /// <summary>A building piece the notebook wrote into the world (yours, standing on its own).</summary>
        void ServerScribblePiece(PieceType t, Vector3 at, PlayerNet p)
        {
            var toMe = p.transform.position - at; toMe.y = 0f;
            var rot = Quaternion.LookRotation(toMe.sqrMagnitude > 0.01f ? toMe : Vector3.forward);
            var go = Instantiate(Bootstrap.I.structurePrefab, at, rot);
            go.GetComponent<Structure>().ServerInit(t, p.Team.Value, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Break, at + Vector3.up * 0.5f, Vector3.up);
        }

        // =====================================================================
        // the dance bomb, the clutter bomb and the decoy base (thrown - PlayerNet.ThrownLandRpc)
        // =====================================================================

        /// <summary>Server: everyone within Cfg.DanceRadius dances for Cfg.DanceTime (whoever threw it too, if they're close).</summary>
        public void ServerDanceAt(Vector3 at, PlayerNet by)
        {
            double now = NetworkManager.ServerTime.Time;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value || p.WackySafe) continue;
                if (Vector3.Distance(p.transform.position, at) > Cfg.DanceRadius) continue;
                if (p.Riding) p.ServerDismount();
                p.DanceUntil.Value = now + Cfg.DanceTime;
                p.WackyNoteRpc(new FixedString128Bytes("DANCE!"));
            }
            Fx.Server(FxKind.DanceBlast, at + Vector3.up, Vector3.up);
        }

        public void ServerWackyLanded(Item kind, Vector3 point, PlayerNet by)
        {
            switch (kind)
            {
                case Item.BouncyCastle: if (by != null) by.ServerCastleLanded(this, point); break;
                case Item.BearJuice: ServerBearSplash(point, by); break;
                case Item.DanceBomb: ServerDanceAt(point, by); break;
                case Item.ClutterBomb:
                    ServerAddGadget(Gadget.Clutter, PlayerNet.WackyGround(point, 3f), 0f, by != null ? by.Team.Value : 0, Cfg.ClutterTime, Random.Range(1, 99999));
                    Fx.Server(FxKind.ClutterPop, point + Vector3.up, Vector3.up);
                    break;
                case Item.DecoyBase:
                {
                    // a copy of the thrower's whole base, its middle where it landed (Gadgets.cs builds it from their base)
                    var at = PlayerNet.WackyGround(point, 3f);
                    ServerAddGadget(Gadget.DecoyBase, new Vector3(at.x, at.y, at.z), 0f, by != null ? by.Team.Value : 0, 0f, Random.Range(1, 99999));
                    Fx.Server(FxKind.BigPoof, at + Vector3.up * 2f, Vector3.up);
                    break;
                }
            }
        }

        /// <summary>Server: the thrown bear juice smashed here - everyone it splashes (Cfg.BearSplash: an enemy it hit, or the
        /// thrower, if they threw it at their own feet) is a BEAR for Cfg.BearTime.</summary>
        public void ServerBearSplash(Vector3 point, PlayerNet by)
        {
            Fx.Server(FxKind.BearPoof, point + Vector3.up * 0.5f, Vector3.up);
            int n = 0;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value || p.WackySafe || p.IsBear) continue;
                var c = p.transform.position + Vector3.up * 0.9f;
                if (Vector3.Distance(c, point) > Cfg.BearSplash) continue;
                p.ServerMakeBear();
                n++;
                if (p != by) Broadcast($"{(by != null ? by.DisplayName : "Someone")} turned {p.DisplayName} into a BEAR!");
            }
            if (n == 0 && by != null) by.WackyNoteRpc(new FixedString128Bytes("The bear juice splashed nobody"));
        }

        /// <summary>Server: an explosion here - any decoy base it reaches is found out (gone in smoke).</summary>
        public void ServerBlastDecoys(Vector3 pos, float radius)
        {
            for (int i = Gadgets.Count - 1; i >= 0; i--)
            {
                var g = Gadgets[i];
                if (g.Kind != Gadget.DecoyBase) continue;
                var d = pos - g.Pos;
                if (Mathf.Abs(d.x) > Cfg.BaseHalf + radius || Mathf.Abs(d.z) > Cfg.BaseHalf + radius || d.y > 20f) continue;
                ServerRemoveGadget(g.Id, true);
            }
        }

        // =====================================================================
        // every frame
        // =====================================================================

        void ServerTickWacky(double now)
        {
            ServerTickGadgets(now);
            ServerTickMinigame(now);
            ServerTickLifts(now); // (the lifting stick: NetGame.Lift.cs)
        }
    }
}
