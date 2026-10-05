using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// DNA mode's gambling machine: a slot machine on the bedrock to the left of the alien machine (as seen from the spawn).
    /// It's a Container (kind Gamble) with 3 slots that only take DNA: E opens it next to your inventory, the GAMBLE button
    /// (Hud.Gamble.cs) bets everything in the slots. The server rolls it (50/50, Cfg.GambleWinChance) and every screen plays
    /// it out on the machine itself: the lever, three reels of item icons spinning and landing one by one, then either three
    /// DNA - confetti, a jingle and double the bet spat out of the hatch - or a red screen full of X's, a loud buzzer, a
    /// bouncing shaking cabinet and a voice going "LOSER".
    /// </summary>
    public class GambleMachine : MonoBehaviour
    {
        // ------------------------------------------------------------------ server

        /// <summary>AutoTest: force the next gamble's result (1 win, 0 lose, -1 random).</summary>
        public static int ForceNext = -1;

        static readonly Dictionary<Container, GambleMachine> s_Of = new Dictionary<Container, GambleMachine>();
        static readonly Dictionary<Container, double> s_BusyUntil = new Dictionary<Container, double>();

        public static GambleMachine Of(Container c) => c != null && s_Of.TryGetValue(c, out var g) ? g : null;

        static Vector3 Left(int team) => -Vector3.Cross(Vector3.up, Cfg.BackDir(team));
        /// <summary>On the bedrock, to the left of the alien machine (looking at it from the spawn).</summary>
        public static Vector3 GamblePos(int team) => Cfg.BaseCenter[team] + Cfg.BackDir(team) * 1.3f + Left(team) * 2.2f + Vector3.up * Cfg.BaseY;
        /// <summary>Turned to face the spawn.</summary>
        public static Quaternion GambleRot(int team)
        {
            var d = Cfg.SpawnPos(team) - GamblePos(team);
            d.y = 0;
            return Quaternion.LookRotation(d.normalized);
        }

        /// <summary>Server: a gambling machine in every base (DNA mode only).</summary>
        public static void ServerSpawnAll()
        {
            if (!Cfg.DnaRules || Cfg.Builder || Bootstrap.I == null || Bootstrap.I.containerPrefab == null) return;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var go = Instantiate(Bootstrap.I.containerPrefab, GamblePos(t), GambleRot(t));
                go.GetComponent<Container>().ServerInit(Container.Gamble, t, 3, null);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
        }

        /// <summary>Server: only DNA goes in (and nothing that isn't DNA gets swapped in when you take some out).</summary>
        public static bool ServerMoveOk(Container c, NetworkList<ItemStack> src, int srcIdx, NetworkList<ItemStack> other, int dstIdx)
        {
            var s = src[srcIdx];
            if (s.Empty) return true;
            bool into = src != c.Slots;
            if (into) return s.Id == Item.Dna;
            if (dstIdx == 255 || dstIdx >= other.Count) return true;
            var d = other[dstIdx];
            return d.Empty || d.Id == Item.Dna;
        }

        /// <summary>Server: bet what's in the slots. Returns why it can't (null = it's spinning).</summary>
        public static string ServerGamble(Container c, PlayerNet p)
        {
            double now = NetworkManager.Singleton.ServerTime.Time;
            if (s_BusyUntil.TryGetValue(c, out var until) && now < until) return "The machine is still spinning...";
            int bet = 0;
            for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == Item.Dna) bet += c.Slots[i].Count;
            if (bet <= 0) return "Put some DNA in the slots first - that's your bet";
            for (int i = 0; i < c.Slots.Count; i++) c.Slots[i] = default;
            bool win = ForceNext >= 0 ? ForceNext == 1 : Random.value < Cfg.GambleWinChance;
            ForceNext = -1;
            int seed = Random.Range(1, int.MaxValue);
            var plan = MakePlan(win, seed);
            s_BusyUntil[c] = now + plan.End + 0.4f;
            p.GambleSpinRpc(c.NetworkObject, win, bet, seed);
            c.StartCoroutine(ServerPayout(c, p, win, bet, plan.End));
            return null;
        }

        static IEnumerator ServerPayout(Container c, PlayerNet p, bool win, int bet, float after)
        {
            yield return new WaitForSeconds(after);
            var g = NetGame.Instance;
            if (c == null || !c.IsSpawned || g == null) yield break;
            string who = p != null ? p.DisplayName : "Someone";
            if (!win) { g.Broadcast($"{who} gambled {bet} DNA and LOST it all"); yield break; }
            // double the bet comes flying out of the hatch, in full stacks
            int left = bet * 2, n = 0;
            var hatch = c.transform.TransformPoint(new Vector3(0, 0.35f, 0.45f));
            var fwd = c.transform.forward;
            while (left > 0)
            {
                int k = Mathf.Min(left, Cfg.MaxStack(Item.Dna));
                left -= k;
                var side = c.transform.right * ((n % 3) - 1) * 0.35f;
                g.ServerDropItem(ItemStack.Of(Item.Dna, k), hatch + fwd * (0.9f + 0.25f * (n / 3)) + side, fwd, hatch);
                n++;
            }
            g.Broadcast($"JACKPOT! {who} doubled {bet} DNA into {bet * 2}");
        }

        // ------------------------------------------------------------------ the plan (the same on every peer)

        /// <summary>The faces around each reel (face 0 is DNA - three of them is the win).</summary>
        static readonly Item[] k_Faces = { Item.Dna, Item.Berry, Item.Wood, Item.C4, Item.Stone, Item.Spear, Item.Helmet, Item.Crossbow, Item.Meat, Item.Hatchet };
        const int Faces = 10;
        const float FaceDeg = 360f / Faces;

        struct Plan { public int[] Face; public float[] Stop; public float End; }

        /// <summary>What each reel lands on and when it stops. A loss is often a near miss (DNA, DNA, ...), with the last reel taking its time.</summary>
        static Plan MakePlan(bool win, int seed)
        {
            var rng = new System.Random(seed);
            var face = new int[3];
            if (win) { face[0] = face[1] = face[2] = 0; }
            else if (rng.NextDouble() < 0.45) { face[0] = face[1] = 0; face[2] = 1 + rng.Next(Faces - 1); }
            else
            {
                do { for (int i = 0; i < 3; i++) face[i] = rng.Next(Faces); }
                while (face[0] == 0 && face[1] == 0 && face[2] == 0);
            }
            var stop = new[] { 1.4f, 2.0f, 2.6f };
            if (face[0] == 0 && face[1] == 0) stop[2] = 3.6f; // two DNA up: the last one keeps you waiting
            return new Plan { Face = face, Stop = stop, End = stop[2] + 0.12f };
        }

        // ------------------------------------------------------------------ the look

        Container m_Box;
        Transform m_Body, m_Lever, m_Hatch, m_Helix;
        readonly Transform[] m_Reels = new Transform[3];
        readonly float[] m_Angle = new float[3];
        readonly List<MeshRenderer> m_Bulbs = new List<MeshRenderer>();
        readonly List<MeshRenderer> m_Icons = new List<MeshRenderer>();
        readonly List<Transform> m_Xs = new List<Transform>();
        GameObject m_RedGlass;
        Material m_Marquee;
        Light m_ReelLight, m_TopLight;
        bool m_IconsSet;

        // the spin being played
        bool m_Spinning, m_Win, m_Mine;
        int m_Bet;
        float m_T0;
        Plan m_Plan;
        readonly float[] m_From = new float[3], m_Omega = new float[3], m_StopStart = new float[3];
        readonly bool[] m_Stopped = new bool[3];
        int m_LastTickFace = int.MinValue;
        float m_NextTick;
        float m_ResultAt = -100f; // when the last result happened
        int m_Result;             // 0 none, 1 win, 2 lose
        float m_Squash, m_SquashV, m_ShakeUntil;
        float m_LeverAt = -10f, m_HatchAt = -10f;

        const float Accel = 0.3f, StopDur = 0.6f, Back = 1.2f; // easeOutBack
        const float Cruise = 900f; // degrees a second at full spin

        static readonly Color k_Cabinet = new Color(0.45f, 0.06f, 0.16f), k_Trim = new Color(0.95f, 0.75f, 0.2f), k_Dark = new Color(0.07f, 0.06f, 0.08f),
            k_Metal = new Color(0.55f, 0.57f, 0.62f), k_Face = new Color(0.97f, 0.95f, 0.88f), k_BulbOff = new Color(0.35f, 0.24f, 0.12f), k_Red = new Color(1f, 0.08f, 0.06f);
        static readonly Color[] k_Party = { new Color(1f, 0.85f, 0.2f), new Color(0.3f, 1f, 0.5f), new Color(0.3f, 0.8f, 1f), new Color(1f, 0.35f, 0.85f), new Color(1f, 0.5f, 0.15f), new Color(0.7f, 0.45f, 1f) };

        /// <summary>Called by the Container when it spawns (kind Gamble): builds the cabinet and sizes its collider.</summary>
        public static void Setup(Container c, Transform visual, BoxCollider bc)
        {
            bc.center = new Vector3(0, 1.05f, 0);
            bc.size = new Vector3(1.05f, 2.1f, 0.9f);
            var gm = c.gameObject.AddComponent<GambleMachine>();
            gm.m_Box = c;
            s_Of[c] = gm;
            gm.Build(visual);
        }

        void OnDestroy()
        {
            if (m_Box != null) { s_Of.Remove(m_Box); s_BusyUntil.Remove(m_Box); }
            if (m_Marquee) Destroy(m_Marquee);
        }

        void Build(Transform visual)
        {
            m_Body = new GameObject("slotMachine").transform;
            m_Body.SetParent(visual, false);
            var t = m_Body;
            // plinth, lower cabinet with gold trim
            Art.Box(t, k_Dark, new Vector3(0, 0.06f, 0), new Vector3(1.08f, 0.12f, 0.88f));
            Art.Box(t, k_Cabinet, new Vector3(0, 0.6f, 0), new Vector3(0.95f, 0.96f, 0.76f));
            for (int k = -1; k <= 1; k += 2) Art.Box(t, k_Trim, new Vector3(k * 0.478f, 0.6f, 0.38f), new Vector3(0.03f, 0.96f, 0.03f));
            Art.Box(t, k_Trim, new Vector3(0, 0.14f, 0.385f), new Vector3(0.96f, 0.04f, 0.02f));
            // the payout hatch: a dark slot with a flap hinged at the top, and a tray under it
            Art.Box(t, k_Dark, new Vector3(0, 0.36f, 0.381f), new Vector3(0.52f, 0.26f, 0.02f));
            Art.Box(t, k_Trim, new Vector3(0, 0.36f, 0.385f), new Vector3(0.58f, 0.3f, 0.012f)).transform.localScale = new Vector3(0.58f, 0.3f, 0.01f);
            Art.Box(t, k_Dark, new Vector3(0, 0.36f, 0.392f), new Vector3(0.52f, 0.26f, 0.012f));
            m_Hatch = new GameObject("hatch").transform;
            m_Hatch.SetParent(t, false);
            m_Hatch.localPosition = new Vector3(0, 0.48f, 0.405f);
            Art.Box(m_Hatch, k_Metal, new Vector3(0, -0.115f, 0.008f), new Vector3(0.48f, 0.22f, 0.02f));
            Art.Box(m_Hatch, k_Trim, new Vector3(0, -0.2f, 0.022f), new Vector3(0.14f, 0.03f, 0.02f)); // handle
            Art.Box(t, k_Trim, new Vector3(0, 0.2f, 0.45f), new Vector3(0.6f, 0.05f, 0.16f)); // tray
            Art.Box(t, k_Trim, new Vector3(0, 0.24f, 0.525f), new Vector3(0.6f, 0.07f, 0.02f));
            // the control ledge with a big red button
            Art.Box(t, k_Dark, new Vector3(0, 1.1f, 0.33f), new Vector3(0.97f, 0.07f, 0.36f), new Vector3(-12, 0, 0));
            Art.Part(t, Art.Cylinder, k_Red, new Vector3(0.22f, 1.15f, 0.38f), new Vector3(0.12f, 0.02f, 0.12f), new Vector3(-12, 0, 0));
            Art.Part(t, Art.Cylinder, k_Trim, new Vector3(-0.05f, 1.15f, 0.38f), new Vector3(0.08f, 0.015f, 0.08f), new Vector3(-12, 0, 0));
            Art.Part(t, Art.Cylinder, new Color(0.3f, 0.8f, 1f), new Vector3(-0.22f, 1.15f, 0.38f), new Vector3(0.08f, 0.015f, 0.08f), new Vector3(-12, 0, 0));

            // upper cabinet: back, sides and top round an open window with the reels in it
            float y0 = 1.14f, y1 = 1.96f, ym = (y0 + y1) * 0.5f, hh = y1 - y0;
            Art.Box(t, k_Cabinet, new Vector3(0, ym, -0.33f), new Vector3(0.95f, hh, 0.1f));
            for (int k = -1; k <= 1; k += 2) Art.Box(t, k_Cabinet, new Vector3(k * 0.435f, ym, 0), new Vector3(0.08f, hh, 0.76f));
            Art.Box(t, k_Cabinet, new Vector3(0, y1 - 0.05f, 0), new Vector3(0.95f, 0.1f, 0.76f));
            Art.Box(t, k_Dark, new Vector3(0, ym, -0.27f), new Vector3(0.8f, hh - 0.1f, 0.02f)); // dark inside
            // the window frame (open in the middle, y 1.27 - 1.71)
            Art.Box(t, k_Cabinet, new Vector3(0, 1.84f, 0.35f), new Vector3(0.95f, 0.26f, 0.06f));
            Art.Box(t, k_Cabinet, new Vector3(0, 1.205f, 0.35f), new Vector3(0.95f, 0.13f, 0.06f));
            for (int k = -1; k <= 1; k += 2) Art.Box(t, k_Cabinet, new Vector3(k * 0.43f, 1.49f, 0.35f), new Vector3(0.09f, 0.46f, 0.06f));
            Art.Box(t, k_Trim, new Vector3(0, 1.715f, 0.385f), new Vector3(0.8f, 0.025f, 0.02f));
            Art.Box(t, k_Trim, new Vector3(0, 1.265f, 0.385f), new Vector3(0.8f, 0.025f, 0.02f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, k_Trim, new Vector3(k * 0.39f, 1.49f, 0.385f), new Vector3(0.025f, 0.475f, 0.02f));
                Art.Box(t, k_Trim, new Vector3(k * 0.128f, 1.49f, 0.37f), new Vector3(0.02f, 0.45f, 0.03f)); // between the reels
            }
            // the pay line
            Art.Box(t, k_Red, new Vector3(0, 1.49f, 0.375f), new Vector3(0.8f, 0.008f, 0.004f));

            // the reels: drums of 10 faces turning on the x axis, the front face showing through the window
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            float fh = 0.16f, r = fh / (2f * Mathf.Tan(Mathf.PI / Faces));
            for (int i = 0; i < 3; i++)
            {
                var drum = new GameObject("reel" + i).transform;
                drum.SetParent(t, false);
                drum.localPosition = new Vector3((i - 1) * 0.255f, 1.49f, 0.335f - r);
                m_Reels[i] = drum;
                for (int f = 0; f < Faces; f++)
                {
                    var rot = Quaternion.Euler(f * FaceDeg, 0, 0);
                    var face = Art.Box(drum, k_Face, rot * new Vector3(0, 0, r - 0.006f), new Vector3(0.22f, fh * 1.02f, 0.012f), (rot).eulerAngles);
                    face.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var icon = Art.Part(drum, quad, Color.white, rot * new Vector3(0, 0, r + 0.002f), Vector3.one * 0.17f, (rot * Quaternion.Euler(0, 180, 0)).eulerAngles, false, null, "icon" + f);
                    var mr = icon.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.enabled = false; // until the icons are ready
                    m_Icons.Add(mr);
                }
                m_Angle[i] = -(i * 3 + 2) * FaceDeg; // something mixed up showing to start with
                drum.localRotation = Quaternion.Euler(m_Angle[i], 0, 0);
                // the red X that pops up over this reel when you lose
                var x = new GameObject("x" + i).transform;
                x.SetParent(t, false);
                x.localPosition = new Vector3((i - 1) * 0.255f, 1.49f, 0.4f);
                Art.Box(x, k_Red, Vector3.zero, new Vector3(0.24f, 0.04f, 0.012f), new Vector3(0, 0, 45));
                Art.Box(x, k_Red, Vector3.zero, new Vector3(0.24f, 0.04f, 0.012f), new Vector3(0, 0, -45));
                x.gameObject.SetActive(false);
                m_Xs.Add(x);
            }
            m_RedGlass = Art.Part(t, Art.Cube, Color.white, new Vector3(0, 1.49f, 0.36f), new Vector3(0.78f, 0.45f, 0.005f), default, false, Art.Ghost(new Color(1f, 0.05f, 0.05f, 0.45f)), "redGlass");
            m_RedGlass.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_RedGlass.SetActive(false);

            // the marquee on top: a glowing screen with a big X when you lose, the DNA helix spinning above it
            Art.Box(t, k_Trim, new Vector3(0, 2.12f, 0.12f), new Vector3(0.92f, 0.34f, 0.12f));
            m_Marquee = new Material(Art.Ghost(new Color(0.3f, 1f, 0.6f, 0.8f)));
            var screen = Art.Part(t, Art.Cube, Color.white, new Vector3(0, 2.12f, 0.185f), new Vector3(0.82f, 0.26f, 0.01f), default, false, m_Marquee, "marquee");
            screen.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bigX = new GameObject("bigX").transform;
            bigX.SetParent(t, false);
            bigX.localPosition = new Vector3(0, 2.12f, 0.2f);
            Art.Box(bigX, k_Red, Vector3.zero, new Vector3(0.3f, 0.05f, 0.012f), new Vector3(0, 0, 30));
            Art.Box(bigX, k_Red, Vector3.zero, new Vector3(0.3f, 0.05f, 0.012f), new Vector3(0, 0, -30));
            bigX.gameObject.SetActive(false);
            m_Xs.Add(bigX);
            var helixHold = new GameObject("helixHold").transform;
            helixHold.SetParent(t, false);
            helixHold.localPosition = new Vector3(0, 2.29f, 0.12f);
            Art.Part(helixHold, Art.Cylinder, k_Dark, new Vector3(0, 0.01f, 0), new Vector3(0.26f, 0.02f, 0.26f));
            m_Helix = DnaArt.Helix(helixHold, 0.5f, 14);

            // bulbs round the marquee and the window
            for (int i = 0; i < 9; i++) Bulb(t, new Vector3(-0.4f + i * 0.1f, 2.31f, 0.19f));
            for (int i = 0; i < 9; i++) Bulb(t, new Vector3(0.4f - i * 0.1f, 1.93f, 0.39f));
            for (int i = 0; i < 4; i++) Bulb(t, new Vector3(-0.44f, 1.82f - i * 0.17f, 0.39f));
            for (int i = 0; i < 4; i++) Bulb(t, new Vector3(0.44f, 1.31f + i * 0.17f, 0.39f));

            // the lever on the right (as you face it: the machine's -x)
            Art.Box(t, k_Metal, new Vector3(-0.51f, 1.3f, 0.05f), new Vector3(0.08f, 0.22f, 0.22f));
            m_Lever = new GameObject("lever").transform;
            m_Lever.SetParent(t, false);
            m_Lever.localPosition = new Vector3(-0.57f, 1.3f, 0.05f);
            Art.Part(m_Lever, Art.Cylinder, k_Metal, new Vector3(0, 0.27f, 0), new Vector3(0.04f, 0.27f, 0.04f));
            Art.Part(m_Lever, Art.Sphere, k_Red, new Vector3(0, 0.58f, 0), Vector3.one * 0.13f);
            Art.Part(m_Lever, Art.Cylinder, k_Trim, Vector3.zero, new Vector3(0.1f, 0.03f, 0.1f), new Vector3(0, 0, 90));

            // lights: on the reels and on the marquee
            m_ReelLight = AddLight(t, new Vector3(0, 1.55f, 0.75f), new Color(1f, 0.95f, 0.85f), 2.2f, 1.6f);
            m_TopLight = AddLight(t, new Vector3(0, 2.25f, 0.6f), new Color(0.4f, 1f, 0.6f), 3f, 1.2f);
        }

        void Bulb(Transform t, Vector3 p)
        {
            var b = Art.Part(t, Art.Sphere, k_BulbOff, p, Vector3.one * 0.055f);
            var mr = b.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Bulbs.Add(mr);
        }

        static Light AddLight(Transform t, Vector3 p, Color c, float range, float intensity)
        {
            var go = new GameObject("light");
            go.transform.SetParent(t, false);
            go.transform.localPosition = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        static Material s_IconBase;
        void SetIcons()
        {
            if (m_IconsSet || ItemIcons.Get(Item.Dna) == null) return;
            m_IconsSet = true;
            if (s_IconBase == null) s_IconBase = Resources.Load<Material>("PsxTrees/PsxCutout");
            for (int i = 0; i < m_Icons.Count; i++)
            {
                var tex = ItemIcons.Get(k_Faces[i % Faces]);
                if (tex == null) continue;
                m_Icons[i].sharedMaterial = IconMat(tex);
                m_Icons[i].enabled = true;
            }
        }

        static readonly Dictionary<Texture, Material> s_IconMats = new Dictionary<Texture, Material>();
        static Material IconMat(Texture tex)
        {
            if (s_IconMats.TryGetValue(tex, out var m) && m) return m;
            m = s_IconBase != null ? new Material(s_IconBase) : new Material(Art.Mat(Color.white));
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.3f);
            s_IconMats[tex] = m;
            return m;
        }

        // ------------------------------------------------------------------ playing a spin

        /// <summary>Every peer: play the spin the server rolled. `mine`: it's this player's bet (they get the big YOU LOST / JACKPOT).</summary>
        public void Play(bool win, int bet, int seed, bool mine)
        {
            m_Plan = MakePlan(win, seed);
            m_Win = win;
            m_Bet = bet;
            m_Mine = mine;
            m_Spinning = true;
            m_T0 = Time.time;
            m_Result = 0;
            m_LastTickFace = int.MinValue;
            foreach (var x in m_Xs) x.gameObject.SetActive(false);
            m_RedGlass.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                m_Stopped[i] = false;
                m_From[i] = Mathf.Repeat(m_Angle[i], 360f);
                float s = m_Plan.Stop[i] - StopDur;
                m_StopStart[i] = s;
                // spin speed picked so the reel lands exactly on its face after the ease out
                float k = (s - Accel * 0.5f) + StopDur / (3f + Back);
                float target = Mathf.Repeat(-m_Plan.Face[i] * FaceDeg - m_From[i], 360f);
                float turns = Mathf.Max(1f, Mathf.Round((Cruise * k - target) / 360f));
                m_Omega[i] = (target + 360f * turns) / k;
            }
            m_LeverAt = Time.time;
            Kick(0.25f);
            var at = transform.position + Vector3.up * 1.3f;
            Sfx.Play(Clips.Lever, at, 0.9f, 0.03f, 40f);
            Sfx.Play(Clips.Coin, at, 0.6f, 0.05f, 30f);
            if (mine) Sfx.Play2D(Sfx.UiClick, 0.5f);
        }

        float ReelAngle(int i, float t)
        {
            float w = m_Omega[i], s = m_StopStart[i];
            if (t < Accel) return m_From[i] + w * t * t / (2f * Accel);
            if (t < s) return m_From[i] + w * (t - Accel * 0.5f);
            float phiS = m_From[i] + w * (s - Accel * 0.5f);
            float d = w * StopDur / (3f + Back);
            float u = Mathf.Clamp01((t - s) / StopDur) - 1f;
            return phiS + d * (1f + (Back + 1f) * u * u * u + Back * u * u);
        }

        void Update()
        {
            SetIcons();
            float now = Time.time;
            if (m_Spinning)
            {
                float t = now - m_T0;
                for (int i = 0; i < 3; i++)
                {
                    if (m_Stopped[i]) continue;
                    if (t >= m_Plan.Stop[i])
                    {
                        m_Stopped[i] = true;
                        m_Angle[i] = -m_Plan.Face[i] * FaceDeg;
                        var p = m_Reels[i].position;
                        Sfx.Play(Clips.Thunk, p, 0.8f, 0.04f, 35f);
                        if (m_Plan.Face[i] == 0) Sfx.Play(Clips.Ding, p, 0.55f, 0f, 35f);
                        Kick(0.12f);
                    }
                    else m_Angle[i] = ReelAngle(i, t);
                }
                // a tick each time a face goes past the pay line (on whichever reel is still turning, not too often)
                int moving = -1;
                for (int i = 2; i >= 0; i--) if (!m_Stopped[i]) { moving = i; break; }
                if (moving >= 0)
                {
                    int fidx = Mathf.FloorToInt(m_Angle[moving] / FaceDeg);
                    if (fidx != m_LastTickFace && now >= m_NextTick)
                    {
                        m_LastTickFace = fidx;
                        m_NextTick = now + 0.05f;
                        Sfx.Play(Clips.Tick, m_Reels[moving].position, 0.35f, 0.02f, 25f);
                    }
                }
                if (t >= m_Plan.End) { m_Spinning = false; Result(); }
            }
            for (int i = 0; i < 3; i++) m_Reels[i].localRotation = Quaternion.Euler(m_Angle[i], 0, 0);
            Animate(now);
        }

        void Result()
        {
            m_ResultAt = Time.time;
            m_Result = m_Win ? 1 : 2;
            var top = transform.TransformPoint(new Vector3(0, 2.2f, 0.3f));
            if (m_Win)
            {
                Sfx.Play(Clips.Jingle, top, 1f, 0f, 60f);
                Confetti(top, 110);
                m_HatchAt = Time.time;
                Kick(0.6f);
                if (m_Mine) { GambleOverlay.Show(true, m_Bet); Fx.Punch(-3f); }
            }
            else
            {
                Sfx.Play(Clips.Buzzer, top, 1f, 0f, 90f);
                StartCoroutine(SayLoser());
                foreach (var x in m_Xs) { x.gameObject.SetActive(true); x.localScale = Vector3.zero; }
                m_RedGlass.SetActive(true);
                m_ShakeUntil = Time.time + 0.7f;
                Kick(-0.7f);
                if (m_Mine) { GambleOverlay.Show(false, m_Bet); Fx.Shake(0.6f); }
            }
        }

        IEnumerator SayLoser()
        {
            yield return new WaitForSeconds(0.55f);
            var clip = Clips.Loser;
            if (clip == null) yield break;
            var at = transform.TransformPoint(new Vector3(0, 1.6f, 0.4f));
            Sfx.Play(clip, at, 1f, 0f, 80f);
            if (m_Mine) Sfx.Play2D(clip, 0.55f, 0f); // right in your ears too
        }

        /// <summary>A second burst follows the first, from a little higher up.</summary>
        void Confetti(Vector3 at, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.8f, 1.6f), Random.Range(-0.4f, 1.2f));
                v = transform.TransformDirection(v) * Random.Range(2.5f, 5.5f);
                FxParticle.Spawn(at + Random.insideUnitSphere * 0.15f, v, k_Party[i % k_Party.Length], Random.Range(0.035f, 0.07f), Random.Range(1.6f, 2.8f), 4.5f, false);
            }
            Fx.Sparks(at, transform.forward + Vector3.up, 30);
            StartCoroutine(MoreConfetti(at));
        }

        IEnumerator MoreConfetti(Vector3 at)
        {
            for (int b = 0; b < 2; b++)
            {
                yield return new WaitForSeconds(0.45f);
                for (int i = 0; i < 50; i++)
                {
                    var v = transform.TransformDirection(new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1f, 1.8f), Random.Range(0f, 1f))) * Random.Range(2f, 4.5f);
                    FxParticle.Spawn(at + Vector3.up * 0.2f, v, k_Party[(i + b) % k_Party.Length], Random.Range(0.035f, 0.065f), Random.Range(1.5f, 2.5f), 4f, false);
                }
                Sfx.Play(Sfx.Pop, at, 0.6f, 0.2f, 30f);
            }
        }

        /// <summary>A squash-and-stretch kick: positive stretches up first, negative squashes down.</summary>
        void Kick(float v) => m_SquashV += v * 9f;

        void Animate(float now)
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            // squash and stretch on a spring
            m_SquashV += (-m_Squash * 260f - m_SquashV * 9f) * dt;
            m_Squash += m_SquashV * dt;
            float s = Mathf.Clamp(m_Squash, -0.35f, 0.35f);
            m_Body.localScale = new Vector3(1f - s * 0.45f, 1f + s, 1f - s * 0.45f);
            // losing shakes it about
            if (now < m_ShakeUntil)
            {
                float a = (m_ShakeUntil - now) / 0.7f;
                m_Body.localPosition = new Vector3(Mathf.Sin(now * 71f) * 0.05f, Mathf.Abs(Mathf.Sin(now * 37f)) * 0.03f, Mathf.Sin(now * 53f) * 0.025f) * a;
                m_Body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(now * 47f) * 4f * a);
            }
            else { m_Body.localPosition = Vector3.zero; m_Body.localRotation = Quaternion.identity; }

            // the lever: pulled down, then springs back
            float lt = now - m_LeverAt;
            float pull = lt < 0.15f ? lt / 0.15f : lt < 0.25f ? 1f : Mathf.Max(0f, 1f - (lt - 0.25f) / 0.35f);
            if (lt >= 0.25f && lt < 0.85f) pull += Mathf.Sin((lt - 0.25f) * 25f) * 0.08f * (1f - (lt - 0.25f) / 0.6f);
            m_Lever.localRotation = Quaternion.Euler(70f * pull, 0, 0);
            // the hatch flips open while the winnings come out
            float ht = now - m_HatchAt;
            float open = ht < 0.12f ? ht / 0.12f : ht < 1.6f ? 1f : Mathf.Max(0f, 1f - (ht - 1.6f) / 0.3f);
            m_Hatch.localRotation = Quaternion.Euler(-80f * open, 0, 0);
            if (m_Helix) m_Helix.localRotation = Quaternion.Euler(0, now * (m_Spinning ? 400f : 60f), 0);

            float since = now - m_ResultAt;
            bool showing = m_Result != 0 && since < 3.2f;
            if (m_Result == 2 && since >= 3.2f) { foreach (var x in m_Xs) x.gameObject.SetActive(false); m_RedGlass.SetActive(false); m_Result = 0; }
            if (m_Result == 1 && since >= 3.2f) m_Result = 0;
            // the red X's pop in with an overshoot, then throb
            if (showing && m_Result == 2)
            {
                for (int i = 0; i < m_Xs.Count; i++)
                {
                    float xt = Mathf.Max(0f, since - i * 0.08f);
                    float pop = xt < 0.18f ? xt / 0.18f * 1.35f : 1f + 0.35f * Mathf.Cos((xt - 0.18f) * 16f) * Mathf.Exp(-(xt - 0.18f) * 5f);
                    m_Xs[i].localScale = Vector3.one * pop * (1f + 0.06f * Mathf.Sin(now * 14f));
                }
            }

            // bulbs, marquee and lights
            Color marquee, top;
            if (showing && m_Result == 1)
            {
                bool flash = Mathf.Repeat(now * 8f, 1f) < 0.5f;
                for (int i = 0; i < m_Bulbs.Count; i++) m_Bulbs[i].sharedMaterial = Art.Mat(k_Party[(i + Mathf.FloorToInt(now * 12f)) % k_Party.Length]);
                marquee = flash ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(1f, 1f, 0.7f, 0.95f);
                top = k_Party[Mathf.FloorToInt(now * 6f) % k_Party.Length];
                m_ReelLight.color = new Color(1f, 0.9f, 0.4f);
                m_ReelLight.intensity = flash ? 3f : 2f;
            }
            else if (showing && m_Result == 2)
            {
                bool on = Mathf.Repeat(now * 5f, 1f) < 0.5f;
                for (int i = 0; i < m_Bulbs.Count; i++) m_Bulbs[i].sharedMaterial = Art.Mat(on ? k_Red : k_BulbOff);
                marquee = on ? new Color(1f, 0.05f, 0.05f, 0.95f) : new Color(0.5f, 0f, 0f, 0.9f);
                top = k_Red;
                m_ReelLight.color = new Color(1f, 0.15f, 0.1f);
                m_ReelLight.intensity = on ? 2.6f : 1.4f;
            }
            else
            {
                // idle: lights chase round; spinning: they chase fast
                float speed = m_Spinning ? 22f : 5f;
                int head = Mathf.FloorToInt(now * speed);
                for (int i = 0; i < m_Bulbs.Count; i++) m_Bulbs[i].sharedMaterial = Art.Mat((i + head) % 3 == 0 ? k_Trim : k_BulbOff);
                float hue = Mathf.Repeat(now * (m_Spinning ? 0.6f : 0.08f), 1f);
                var c = Color.HSVToRGB(hue, 0.65f, 1f);
                marquee = new Color(c.r, c.g, c.b, 0.85f);
                top = c;
                m_ReelLight.color = new Color(1f, 0.95f, 0.85f);
                m_ReelLight.intensity = 1.6f;
            }
            m_Marquee.SetColor("_BaseColor", marquee);
            m_TopLight.color = top;
        }

        // ------------------------------------------------------------------ sounds

        static class Clips
        {
            public static readonly AudioClip Tick, Thunk, Lever, Ding, Jingle, Buzzer, Coin;
            static AudioClip s_Loser;
            static bool s_LoserTried;
            public static AudioClip Loser
            {
                get
                {
                    if (!s_LoserTried) { s_LoserTried = true; s_Loser = Resources.Load<AudioClip>("Gamble/loser"); }
                    return s_Loser;
                }
            }

            const int Rate = 44100;

            static Clips()
            {
                var rng = new System.Random(11);
                float N() => (float)rng.NextDouble() * 2f - 1f;
                float Env(float t, float d) => Mathf.Exp(-t / Mathf.Max(0.001f, d) * 3f);
                float Sq(float t, float f) => Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * f));
                Tick = Make("gtick", 0.03f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 2600) * Env(t, 0.01f) * 0.6f + N() * Env(t, 0.004f) * 0.4f);
                Thunk = Make("gthunk", 0.22f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(200, 80, t / d)) * Env(t, 0.1f) + N() * Env(t, 0.015f) * 0.6f, 0.4f);
                Lever = Make("glever", 0.45f, (t, d) => (t < 0.25f ? N() * (Mathf.Sin(t * 2 * Mathf.PI * 30) > 0.6f ? 0.7f : 0.05f) : 0f)
                    + (t > 0.22f ? Mathf.Sin((t - 0.22f) * 2 * Mathf.PI * 110) * Env(t - 0.22f, 0.12f) : 0f), 0.5f);
                Ding = Make("gding", 0.6f, (t, d) => (Mathf.Sin(t * 2 * Mathf.PI * 1568) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 3136) * 0.2f + Mathf.Sin(t * 2 * Mathf.PI * 2349) * 0.15f) * Env(t, 0.35f));
                Coin = Make("gcoin", 0.25f, (t, d) => (t < 0.06f ? Mathf.Sin(t * 2 * Mathf.PI * 1976) : Mathf.Sin(t * 2 * Mathf.PI * 2637)) * Env(t < 0.06f ? t : t - 0.06f, 0.12f) * 0.5f);
                // the win: a quick climbing arpeggio and a big bright chord
                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f, 2093f };
                Jingle = Make("gjingle", 1.9f, (t, d) =>
                {
                    float v = 0f;
                    for (int i = 0; i < notes.Length; i++)
                    {
                        float st = i * 0.09f;
                        if (t >= st) v += (Mathf.Sin((t - st) * 2 * Mathf.PI * notes[i]) * 0.6f + Sq(t - st, notes[i]) * 0.12f) * Env(t - st, 0.25f) * 0.35f;
                    }
                    if (t >= 0.66f)
                    {
                        float ct = t - 0.66f;
                        float vib = 1f + Mathf.Sin(ct * 2 * Mathf.PI * 6f) * 0.004f;
                        foreach (var f in new[] { 1046.5f, 1318.5f, 1568f, 2093f }) v += Mathf.Sin(ct * 2 * Mathf.PI * f * vib) * 0.16f * Env(ct, 1.0f);
                    }
                    return v;
                });
                // the loss: a loud, harsh game-show buzzer
                Buzzer = Make("gbuzzer", 1.15f, (t, d) =>
                {
                    float saw1 = Mathf.Repeat(t * 98f, 1f) * 2f - 1f, saw2 = Mathf.Repeat(t * 103.5f, 1f) * 2f - 1f;
                    float v = (saw1 + saw2) * 0.5f + Sq(t, 196f) * 0.3f + N() * 0.05f;
                    v = Mathf.Clamp(v * 2.2f, -1f, 1f) * 0.9f;
                    float a = Mathf.Min(1f, t * 60f) * (t > d - 0.08f ? (d - t) / 0.08f : 1f);
                    return v * a;
                }, 0.55f);
            }

            static AudioClip Make(string name, float dur, System.Func<float, float, float> f, float lowpass = 1f)
            {
                int n = Mathf.CeilToInt(dur * Rate);
                var data = new float[n];
                float y = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    y += (f(t, dur) - y) * lowpass;
                    data[i] = Mathf.Clamp(y, -1f, 1f) * Mathf.Clamp01((n - i) / 200f);
                }
                var clip = AudioClip.Create(name, n, 1, Rate, false);
                clip.SetData(data, 0);
                return clip;
            }
        }
    }

    /// <summary>The gambler's own screen: a huge bouncing YOU LOST (red, X's, shaking) or JACKPOT (gold, confetti).</summary>
    public class GambleOverlay : MonoBehaviour
    {
        static GambleOverlay s_I;
        static bool s_Win;
        static int s_Bet;
        static float s_At = -100f;
        const float ShowTime = 3.4f;

        struct Bit { public Vector2 P, V; public float Rot, Spin, Size; public Color C; }
        static readonly List<Bit> s_Bits = new List<Bit>();
        static readonly Color[] k_Party = { new Color(1f, 0.85f, 0.2f), new Color(0.3f, 1f, 0.5f), new Color(0.3f, 0.8f, 1f), new Color(1f, 0.35f, 0.85f), new Color(1f, 0.5f, 0.15f), new Color(0.7f, 0.45f, 1f) };

        /// <summary>AutoTest: what's on screen right now (0 nothing, 1 jackpot, 2 you lost).</summary>
        public static int Showing => Time.time - s_At < ShowTime ? (s_Win ? 1 : 2) : 0;

        public static void Show(bool win, int bet)
        {
            if (s_I == null)
            {
                var go = new GameObject("GambleOverlay");
                DontDestroyOnLoad(go);
                s_I = go.AddComponent<GambleOverlay>();
            }
            s_Win = win;
            s_Bet = bet;
            s_At = Time.time;
            s_Bits.Clear();
            if (win)
                for (int i = 0; i < 140; i++)
                    s_Bits.Add(new Bit
                    {
                        P = new Vector2(Random.Range(0.3f, 0.7f) * Screen.width, Screen.height * 0.45f),
                        V = new Vector2(Random.Range(-1f, 1f) * Screen.width * 0.55f, -Random.Range(0.5f, 1.3f) * Screen.height),
                        Rot = Random.Range(0f, 360f), Spin = Random.Range(-720f, 720f), Size = Random.Range(8f, 18f) * Screen.height / 900f,
                        C = k_Party[i % k_Party.Length],
                    });
        }

        void OnGUI()
        {
            float t = Time.time - s_At;
            if (t > ShowTime) return;
            GUI.depth = -50;
            float sw = Screen.width, sh = Screen.height, k = sh / 900f;
            float fade = Mathf.Clamp01((ShowTime - t) / 0.5f);
            var old = GUI.color;
            var oldM = GUI.matrix;
            if (!s_Win)
            {
                // red flash that keeps throbbing, X's in the corners
                float pulse = 0.22f + 0.16f * Mathf.Abs(Mathf.Sin(t * 7f)) + Mathf.Max(0f, 0.4f - t) * 0.8f;
                GUI.color = new Color(0.9f, 0f, 0f, pulse * fade);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                for (int i = 0; i < 4; i++)
                {
                    var c = new Vector2(i % 2 == 0 ? sw * 0.14f : sw * 0.86f, i < 2 ? sh * 0.2f : sh * 0.8f);
                    float xt = Mathf.Max(0f, t - 0.1f * i);
                    float sc = xt < 0.15f ? xt / 0.15f * 1.3f : 1f + 0.3f * Mathf.Cos((xt - 0.15f) * 14f) * Mathf.Exp(-(xt - 0.15f) * 4f);
                    DrawX(c, 110f * k * sc, 22f * k * sc, new Color(1f, 0.1f, 0.05f, 0.9f * fade), Mathf.Sin(t * 3f + i) * 10f);
                }
            }
            else
            {
                GUI.color = new Color(1f, 0.85f, 0.2f, Mathf.Max(0f, 0.35f - t * 0.5f) * fade);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                // confetti raining down the screen
                float dt = Time.deltaTime;
                for (int i = 0; i < s_Bits.Count; i++)
                {
                    var b = s_Bits[i];
                    if (Event.current.type == EventType.Repaint)
                    {
                        b.V.y += sh * 1.4f * dt;
                        b.V *= 1f - 0.9f * dt;
                        b.P += b.V * dt;
                        b.Rot += b.Spin * dt;
                        s_Bits[i] = b;
                    }
                    GUI.matrix = oldM;
                    GUIUtility.RotateAroundPivot(b.Rot, b.P);
                    GUI.color = new Color(b.C.r, b.C.g, b.C.b, fade);
                    GUI.DrawTexture(new Rect(b.P.x - b.Size * 0.5f, b.P.y - b.Size * 0.3f, b.Size, b.Size * 0.6f), Texture2D.whiteTexture);
                }
                GUI.matrix = oldM;
            }

            // the big word: pops in with an elastic bounce and a wobble
            float s = t < 0.16f ? t / 0.16f * 1.4f : 1f + 0.4f * Mathf.Cos((t - 0.16f) * 13f) * Mathf.Exp(-(t - 0.16f) * 3.5f);
            float wob = Mathf.Sin(t * 9f) * 7f * Mathf.Exp(-t * 1.2f) + (s_Win ? 0f : Mathf.Sin(t * 31f) * 1.5f);
            var center = new Vector2(sw * 0.5f, sh * 0.4f + Mathf.Abs(Mathf.Sin(t * 6f)) * -18f * k * Mathf.Exp(-t * 0.8f));
            GUI.matrix = oldM;
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Max(0.01f, s), center);
            GUIUtility.RotateAroundPivot(wob, center);
            string word = s_Win ? "JACKPOT!" : "YOU LOST";
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(130 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
            var rect = new Rect(center.x - sw * 0.5f, center.y - 90 * k, sw, 180 * k);
            st.normal.textColor = new Color(0, 0, 0, 0.85f * fade);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(rect.x + dx * 5 * k, rect.y + dy * 5 * k + 4 * k, rect.width, rect.height), word, st);
            st.normal.textColor = s_Win ? Color.Lerp(new Color(1f, 0.85f, 0.15f), Color.white, Mathf.Abs(Mathf.Sin(t * 10f)) * 0.5f) * new Color(1, 1, 1, fade)
                                        : Color.Lerp(new Color(1f, 0.1f, 0.05f), new Color(1f, 0.6f, 0.5f), Mathf.Abs(Mathf.Sin(t * 12f)) * 0.4f) * new Color(1, 1, 1, fade);
            GUI.color = Color.white;
            GUI.Label(rect, word, st);
            GUI.matrix = oldM;

            // what it cost / paid, bouncing in a beat later
            float t2 = t - 0.35f;
            if (t2 > 0f)
            {
                float s2 = t2 < 0.12f ? t2 / 0.12f * 1.25f : 1f + 0.25f * Mathf.Cos((t2 - 0.12f) * 15f) * Mathf.Exp(-(t2 - 0.12f) * 4f);
                var c2 = new Vector2(sw * 0.5f, sh * 0.4f + 120 * k);
                GUIUtility.ScaleAroundPivot(Vector2.one * s2, c2);
                string sub = s_Win ? $"+{s_Bet * 2} DNA" : $"-{s_Bet} DNA     LOSER!";
                var st2 = new GUIStyle(st) { fontSize = Mathf.RoundToInt(46 * k) };
                var r2 = new Rect(0, c2.y - 35 * k, sw, 70 * k);
                st2.normal.textColor = new Color(0, 0, 0, 0.85f * fade);
                GUI.Label(new Rect(r2.x + 3 * k, r2.y + 3 * k, r2.width, r2.height), sub, st2);
                st2.normal.textColor = s_Win ? new Color(0.5f, 1f, 0.7f, fade) : new Color(1f, 0.85f, 0.85f, fade);
                GUI.Label(r2, sub, st2);
                GUI.matrix = oldM;
            }
            GUI.color = old;
        }

        static void DrawX(Vector2 c, float len, float thick, Color col, float rot)
        {
            var m = GUI.matrix;
            GUI.color = col;
            foreach (var a in new[] { 45f, -45f })
            {
                GUI.matrix = m;
                GUIUtility.RotateAroundPivot(a + rot, c);
                GUI.DrawTexture(new Rect(c.x - len * 0.5f, c.y - thick * 0.5f, len, thick), Texture2D.whiteTexture);
            }
            GUI.matrix = m;
        }
    }
}
