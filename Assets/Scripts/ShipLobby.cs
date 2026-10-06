using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The lobby before a match (replacing the old waiting stadium on screen): the crew lounge of an alien ship, far off
    /// the map, with every player sitting about in it as an alien in their team's colour - chilling in one of seven poses
    /// (BodyAnimator.Lounge: some smoke, some drink a beer, some are on their phone), breathing and looking round - their
    /// names over their heads (Hud.Lobby.cs draws those and the buttons: LEAVE, COPY ROOM ID, GAME OPTIONS for the host,
    /// the team picker and READY). Behind them a long panoramic window with the stars streaking past (the ship's going
    /// fast), round them the lounge (deck plates, pipes, a vending machine, crates, a plant, a hanging lamp). The camera
    /// holds one slowly drifting shot of them, like a group sat in a waiting room. Only the look: the players' real bodies
    /// still wait in the stadium (PlayerController doesn't move them while the lobby's up), and the match starts once
    /// everyone is READY (NetGame.ServerReadyToStart). Solo and the tests (no lobby) skip it.
    /// </summary>
    [DefaultExecutionOrder(1000)] // (after PlayerController: the lobby has the camera)
    public class ShipLobby : MonoBehaviour
    {
        /// <summary>Where the room is: far out past the map, where nothing else is.</summary>
        public static readonly Vector3 Center = new Vector3(3000f, 60f, 3000f);

        /// <summary>The window: an opening in the back wall (room space), x and y half sizes about its middle.</summary>
        const float WinZ = 4.2f, WinY = 2.25f, WinHalfX = 4.2f, WinHalfY = 0.85f;

        static ShipLobby s_I;
        GameObject m_Room;
        readonly List<Renderer> m_Blinkers = new List<Renderer>();
        readonly List<Material> m_BlinkOn = new List<Material>();
        Material m_BlinkOff;

        // the stars going by outside (room space): position, speed
        readonly List<Transform> m_Stars = new List<Transform>();
        readonly List<float> m_StarSpeed = new List<float>(), m_StarSpan = new List<float>();
        Transform m_Planet;
        /// <summary>The sphere mesh's diameter (scale by size / SR).</summary>
        static float SR => 2f * Art.Sphere.bounds.extents.x;

        class Puff { public Transform T; public float Born = -99f; public Vector3 Vel; public float Size; }

        class Avatar
        {
            public PlayerNet P;
            public Transform Root, Seat, Stem;
            public BodyAnimator Anim;
            public readonly List<Material> Mats = new List<Material>();
            public int Team = -1, Variant;
            public float Seed, NextPuff;
            public Vector3 SeatAt;
            public Transform Prop;            // cigarette / can / phone
            public Renderer Tip;              // the cigarette's tip
            public readonly List<Puff> Puffs = new List<Puff>();
        }
        readonly Dictionary<PlayerNet, Avatar> m_Avatars = new Dictionary<PlayerNet, Avatar>();
        static readonly List<PlayerNet> s_Order = new List<PlayerNet>();
        static readonly List<PlayerNet> s_Gone = new List<PlayerNet>();
        /// <summary>Who does what, by client id: the fun ones first (smoking, a beer, the phone), then the rest.</summary>
        static readonly int[] k_Variants = { 4, 5, 6, 3, 2, 0, 1 };

        Material m_TipOff, m_TipOn, m_Smoke;

        /// <summary>The lobby's up: this PC is a player in a match that hasn't started (and isn't solo).</summary>
        public static bool Active
        {
            get
            {
                var g = NetGame.Instance;
                return g != null && g.IsSpawned && g.S == GameState.Waiting && NetGame.ReadyLobby && PlayerNet.Local != null && !Spectator.Active;
            }
        }

        /// <summary>Where a player's head is on screen in the lobby (for their name tag), and whether it's in view.</summary>
        public static bool HeadOnScreen(PlayerNet p, out Vector2 gui)
        {
            gui = default;
            if (s_I == null || p == null || !s_I.m_Avatars.TryGetValue(p, out var a) || a.Root == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            var head = a.Anim != null && a.Anim.HeadBone != null ? a.Anim.HeadBone.position + Vector3.up * 0.42f : a.Root.position + Vector3.up * 1.45f;
            var sp = cam.WorldToScreenPoint(head); // (just over the head)
            if (sp.z <= 0f) return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        /// <summary>The players in the lobby, in their seat order (by team, then slot).</summary>
        public static IReadOnlyList<PlayerNet> Seated => s_Order;
        /// <summary>(tests) the camera close on the seated player with this index (-1: the usual shot).</summary>
        public static int TestFocus = -1;
        /// <summary>(tests) everyone at this point of their animation (-1: their own time).</summary>
        public static float TestPhase = -1f;

        /// <summary>(tests) how far the lowest seated alien's lowest foot is under the floor (0: none are).</summary>
        public static float WorstFootSink
        {
            get
            {
                float worst = 0f;
                if (s_I == null) return 0f;
                foreach (var a in s_I.m_Avatars.Values)
                {
                    if (a.Root == null) continue;
                    foreach (var r in a.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var m = new Mesh();
                        r.BakeMesh(m, true);
                        var v = m.vertices;
                        var tr = r.transform;
                        for (int i = 0; i < v.Length; i++) worst = Mathf.Max(worst, Center.y - tr.TransformPoint(v[i]).y);
                        Destroy(m);
                    }
                }
                return worst;
            }
        }

        public static void Ensure(GameObject host)
        {
            if (s_I == null) s_I = host.GetComponent<ShipLobby>() ?? host.AddComponent<ShipLobby>();
        }

        void Awake() => s_I = this;

        void LateUpdate()
        {
            if (!Active)
            {
                if (m_Room != null) Clear();
                return;
            }
            if (m_Room == null) BuildRoom();
            SyncAvatars();
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            foreach (var a in m_Avatars.Values)
            {
                if (a.Anim == null) continue;
                float lt = TestPhase >= 0f ? TestPhase : t + a.Seed;
                a.Anim.Lounge(a.Variant, lt);
                FitSeat(a);
                PlaceProp(a, lt, t);
            }
            // blinking console lights
            for (int i = 0; i < m_Blinkers.Count; i++)
                if (m_Blinkers[i]) m_Blinkers[i].sharedMaterial = Mathf.PerlinNoise(i * 3.1f, t * 0.9f) > 0.48f ? m_BlinkOn[i % m_BlinkOn.Count] : m_BlinkOff;
            // the stars streaking past the window (the near ones faster), the planet drifting by far off
            for (int i = 0; i < m_Stars.Count; i++)
            {
                var s = m_Stars[i];
                var p = s.localPosition;
                p.x -= m_StarSpeed[i] * dt;
                if (p.x < -m_StarSpan[i]) p.x += m_StarSpan[i] * 2f;
                s.localPosition = p;
            }
            if (m_Planet)
            {
                var p = m_Planet.localPosition;
                p.x = 60f - Mathf.Repeat(t * 0.9f + 40f, 120f);
                m_Planet.localPosition = p;
                m_Planet.localRotation = Quaternion.Euler(12f, t * 3f, 0f);
            }
            // one slowly drifting shot of them all, a little from above
            var cam = Camera.main;
            if (cam != null && TestFocus >= 0 && TestFocus < s_Order.Count && m_Avatars.TryGetValue(s_Order[TestFocus], out var fa))
            {
                // (tests: close on one of them)
                var c = fa.Root.position + Vector3.up * 0.95f;
                var pos = c + fa.Root.forward * 1.7f + Vector3.up * 0.25f + fa.Root.right * 0.3f;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c - pos));
                cam.fieldOfView = 50f;
            }
            else if (cam != null)
            {
                // close enough that they fill the shot (further back the more there are)
                int n = s_Order.Count;
                float back = Mathf.Clamp(3.4f + Mathf.Max(0, n - 3) * 0.6f, 3.4f, 7.2f);
                var pos = Center + new Vector3(Mathf.Sin(t * 0.11f) * 0.35f, 1.55f + back * 0.06f + Mathf.Sin(t * 0.17f) * 0.06f, 1.6f - back);
                var look = Center + new Vector3(Mathf.Sin(t * 0.07f) * 0.25f, 1.1f, 1.6f);
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.fieldOfView = 56f;
            }
        }

        void Clear()
        {
            foreach (var a in m_Avatars.Values) if (a.Root) Destroy(a.Root.gameObject);
            m_Avatars.Clear();
            s_Order.Clear();
            if (m_Room) Destroy(m_Room);
            m_Room = null;
            m_Blinkers.Clear();
            m_BlinkOn.Clear();
            m_Stars.Clear();
            m_StarSpeed.Clear();
            m_StarSpan.Clear();
            m_Planet = null;
        }

        void OnDestroy() => Clear();

        // ------------------------------------------------------------------ the players

        void SyncAvatars()
        {
            s_Gone.Clear();
            foreach (var kv in m_Avatars) if (kv.Key == null || !kv.Key.IsSpawned) s_Gone.Add(kv.Key);
            foreach (var p in s_Gone) { if (m_Avatars[p].Root) Destroy(m_Avatars[p].Root.gameObject); m_Avatars.Remove(p); }
            s_Order.Clear();
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) s_Order.Add(p);
            s_Order.Sort((a, b) => a.Team.Value != b.Team.Value ? a.Team.Value.CompareTo(b.Team.Value) : a.Slot.Value != b.Slot.Value ? a.Slot.Value.CompareTo(b.Slot.Value) : a.OwnerClientId.CompareTo(b.OwnerClientId));
            for (int i = 0; i < s_Order.Count; i++)
            {
                var p = s_Order[i];
                if (!m_Avatars.TryGetValue(p, out var a)) m_Avatars[p] = a = MakeAvatar(p);
                var seat = SeatPos(i, s_Order.Count);
                if ((seat - a.SeatAt).sqrMagnitude > 0.0001f || a.Root.position == Vector3.zero)
                {
                    a.SeatAt = seat;
                    a.Root.position = Center + seat;
                    // facing the camera, turned a little towards the middle of the group
                    a.Root.rotation = Quaternion.Euler(0f, 180f - seat.x * 4f, 0f);
                }
                if (a.Team != p.Team.Value) Tint(a, p.Team.Value);
            }
        }

        /// <summary>Seat i of n: one gentle arc across the room (two rows past eight: the front one lower and closer).</summary>
        static Vector3 SeatPos(int i, int n)
        {
            int rows = n > 8 ? 2 : 1;
            int perRow = Mathf.CeilToInt(n / (float)rows);
            int row = i / perRow, col = i % perRow;
            int inRow = row == rows - 1 ? n - perRow * (rows - 1) : perRow;
            float span = Mathf.Min(9f, 1.35f * (inRow - 1));
            float x = inRow <= 1 ? 0f : -span * 0.5f + span * col / (inRow - 1);
            float z = 1.6f - row * 1.7f + Mathf.Abs(x) * 0.18f; // (curving back at the ends)
            return new Vector3(x, 0f, z);
        }

        Avatar MakeAvatar(PlayerNet p)
        {
            var a = new Avatar { P = p, Seed = (p.OwnerClientId * 7.31f) % 50f, Variant = k_Variants[(int)(p.OwnerClientId % (ulong)k_Variants.Length)] };
            var go = new GameObject("lobby alien " + p.DisplayName);
            go.transform.SetParent(m_Room.transform, true);
            a.Root = go.transform;
            a.Anim = BodyAnimator.TryCreate(a.Root, Cfg.ModelWidth, out var model);
            if (model != null)
            {
                PlayerNet.SkinAlien(model, a.Mats);
                SmoothShadeHook.Add(model, true);
            }
            // the chair: a padded seat and back on a stem (the seat goes up and down to sit under the hips: FitSeat)
            var metal = new Color(0.3f, 0.32f, 0.38f);
            var pad = new Color(0.36f, 0.18f, 0.48f);
            a.Seat = new GameObject("seat").transform;
            a.Seat.SetParent(a.Root, false);
            Art.Box(a.Seat, metal, new Vector3(0f, 0f, -0.12f), new Vector3(0.64f, 0.07f, 0.58f));
            Art.Box(a.Seat, pad, new Vector3(0f, 0.06f, -0.1f), new Vector3(0.58f, 0.07f, 0.54f));
            Art.Box(a.Seat, metal, new Vector3(0f, 0.45f, -0.43f), new Vector3(0.62f, 0.86f, 0.07f), new Vector3(-8f, 0f, 0f));
            Art.Box(a.Seat, pad, new Vector3(0f, 0.47f, -0.385f), new Vector3(0.54f, 0.72f, 0.05f), new Vector3(-8f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
                Art.Box(a.Seat, metal * 0.85f, new Vector3(s * 0.33f, 0.16f, -0.12f), new Vector3(0.06f, 0.06f, 0.5f)); // (arm rests)
            a.Stem = Art.Box(a.Root, metal * 0.7f, new Vector3(0f, 0.16f, -0.12f), new Vector3(0.12f, 0.32f, 0.12f)).transform;
            Art.Part(a.Root, Art.Cylinder, metal * 0.6f, new Vector3(0f, 0.02f, -0.12f), new Vector3(0.24f, 0.02f, 0.24f));
            // what they've got in their hands
            if (m_TipOff == null)
            {
                m_TipOff = Workbench.Glow(new Color(1f, 0.35f, 0.1f), 0.8f);
                m_TipOn = Workbench.Glow(new Color(1f, 0.45f, 0.1f), 3.2f);
                m_Smoke = Art.Ghost(new Color(0.95f, 0.95f, 1f, 0.4f));
            }
            switch (a.Variant)
            {
                case 4: // a cigarette, its tip glowing brighter on a drag, and the smoke
                    a.Prop = new GameObject("cigarette").transform;
                    a.Prop.SetParent(a.Root, false);
                    Art.Box(a.Prop, new Color(0.95f, 0.94f, 0.9f), new Vector3(0, 0, 0.03f), new Vector3(0.014f, 0.014f, 0.07f));
                    Art.Box(a.Prop, new Color(0.85f, 0.55f, 0.25f), new Vector3(0, 0, -0.012f), new Vector3(0.015f, 0.015f, 0.024f));
                    a.Tip = Art.Box(a.Prop, Color.white, new Vector3(0, 0, 0.068f), new Vector3(0.016f, 0.016f, 0.01f), default, false, m_TipOff).GetComponent<Renderer>();
                    for (int i = 0; i < 10; i++)
                    {
                        var puff = Art.Part(m_Room.transform, Art.Sphere, Color.white, Vector3.zero, Vector3.zero, default, false, m_Smoke, "smoke").transform;
                        a.Puffs.Add(new Puff { T = puff });
                    }
                    break;
                case 5: // a can of beer (and an empty one by the chair)
                    a.Prop = BeerCan(a.Root).transform;
                    var empty = BeerCan(a.Root).transform;
                    empty.localPosition = new Vector3(0.42f, 0.03f, 0.05f);
                    empty.localRotation = Quaternion.Euler(0f, 30f, 90f);
                    break;
                case 6: // a phone, its screen lit
                    a.Prop = new GameObject("phone").transform;
                    a.Prop.SetParent(a.Root, false);
                    Art.Box(a.Prop, new Color(0.08f, 0.08f, 0.1f), Vector3.zero, new Vector3(0.075f, 0.15f, 0.012f));
                    Art.Box(a.Prop, Color.white, new Vector3(0, 0, 0.007f), new Vector3(0.066f, 0.135f, 0.002f), default, false, Workbench.Glow(new Color(0.55f, 0.8f, 1f), 1.8f));
                    break;
            }
            foreach (var r in a.Root.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return a;
        }

        static GameObject BeerCan(Transform parent)
        {
            var can = new GameObject("beer");
            can.transform.SetParent(parent, false);
            float cr = 2f * Art.Cylinder.bounds.extents.x;
            Art.Part(can.transform, Art.Cylinder, new Color(0.85f, 0.75f, 0.2f), Vector3.zero, new Vector3(0.066f / cr, 0.06f, 0.066f / cr));
            Art.Part(can.transform, Art.Cylinder, new Color(0.75f, 0.76f, 0.8f), new Vector3(0, 0.062f, 0), new Vector3(0.06f / cr, 0.004f, 0.06f / cr));
            Art.Part(can.transform, Art.Cylinder, new Color(0.15f, 0.35f, 0.75f), new Vector3(0, -0.005f, 0), new Vector3(0.068f / cr, 0.022f, 0.068f / cr));
            return can;
        }

        /// <summary>The seat under the hips (wherever the pose has put them), the stem down to the floor.</summary>
        static void FitSeat(Avatar a)
        {
            float y = Mathf.Clamp(a.Anim.HipsHeight - 0.18f, 0.18f, 0.75f);
            a.Seat.localPosition = new Vector3(0f, y, 0f);
            a.Stem.localScale = new Vector3(0.12f, y, 0.12f);
            a.Stem.localPosition = new Vector3(0f, y * 0.5f, -0.12f);
        }

        /// <summary>The prop in the hand(s) this frame, and the smoke.</summary>
        void PlaceProp(Avatar a, float t, float now)
        {
            if (a.Prop == null) return;
            var rh = a.Anim.RightHand; var rf = a.Anim.RightFore;
            if (rh == null || rf == null) return;
            var dir = (rh.position - rf.position).normalized;
            var palm = rh.position + dir * 0.07f;
            var up = a.Root.up; var fwd = a.Root.forward;
            switch (a.Variant)
            {
                case 4:
                {
                    float d = BodyAnimator.Drag(t);
                    var side = Vector3.Cross(dir, up);
                    if (side.sqrMagnitude < 0.01f) side = a.Root.right;
                    a.Prop.SetPositionAndRotation(palm, Quaternion.LookRotation(-side.normalized, up));
                    if (a.Tip) a.Tip.sharedMaterial = d > 0.6f ? m_TipOn : m_TipOff;
                    // breathing the smoke out after the drag, and a thin wisp off the tip the rest of the time
                    float u = Mathf.Repeat(t, 6f);
                    var head = a.Anim.HeadBone != null ? a.Anim.HeadBone.position : a.Root.position + up * 1.2f;
                    if (now >= a.NextPuff)
                    {
                        bool exhale = u > 2.1f && u < 3.5f;
                        var from = exhale ? head + fwd * 0.17f + up * 0.08f : a.Tip != null ? a.Tip.transform.position : palm;
                        Emit(a, from, exhale ? fwd * 0.35f + up * 0.2f : up * 0.18f + fwd * 0.03f, exhale ? 0.035f : 0.012f, now);
                        a.NextPuff = now + (exhale ? 0.12f : 0.55f);
                    }
                    break;
                }
                case 5:
                {
                    float d = BodyAnimator.Swig(t);
                    var axis = (up - fwd * 1.4f * d).normalized; // (the top of the can toward the mouth on a swig)
                    a.Prop.SetPositionAndRotation(palm - axis * 0.01f, Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.LookRotation(fwd, up));
                    break;
                }
                case 6:
                {
                    var lh = a.Anim.LeftHand; var lf = a.Anim.LeftFore;
                    var lpalm = lh != null && lf != null ? lh.position + (lh.position - lf.position).normalized * 0.07f : palm;
                    var at = (palm + lpalm) * 0.5f + up * 0.03f;
                    var head = a.Anim.HeadBone != null ? a.Anim.HeadBone.position : at + up * 0.4f;
                    var face = head - at;
                    if (face.sqrMagnitude > 0.0001f) a.Prop.SetPositionAndRotation(at, Quaternion.LookRotation(face.normalized, up));
                    break;
                }
            }
            // the smoke rising, spreading and thinning out
            foreach (var p in a.Puffs)
            {
                float age = now - p.Born;
                if (age > 2.4f) { if (p.T.localScale.x > 0f) p.T.localScale = Vector3.zero; continue; }
                p.T.position += p.Vel * Time.deltaTime;
                p.Vel = Vector3.Lerp(p.Vel, Vector3.up * 0.12f, Time.deltaTime * 1.2f);
                float s = p.Size * (1f + age * 1.6f) * (1f - Mathf.SmoothStep(0f, 1f, (age - 1.4f) / 1f));
                p.T.localScale = Vector3.one * (Mathf.Max(0f, s) / SR);
            }
        }

        static void Emit(Avatar a, Vector3 at, Vector3 vel, float size, float now)
        {
            Puff best = null;
            foreach (var p in a.Puffs) if (best == null || p.Born < best.Born) best = p;
            if (best == null) return;
            best.Born = now;
            best.T.position = at;
            best.Vel = vel + Random.insideUnitSphere * 0.04f;
            best.Size = size;
        }

        static void Tint(Avatar a, int team)
        {
            a.Team = team;
            foreach (var m in a.Mats)
            {
                var c = PlayerNet.TeamTint(m, team);
                m.SetColor("_BaseColor", c);
                m.color = c;
            }
        }

        // ------------------------------------------------------------------ the room

        void BuildRoom()
        {
            m_Room = new GameObject("ShipLobby");
            m_Room.transform.position = Center;
            var t = m_Room.transform;
            var hull = new Color(0.3f, 0.32f, 0.38f);
            var panel = new Color(0.24f, 0.26f, 0.31f);
            var dark = new Color(0.13f, 0.14f, 0.18f);
            var trim = new Color(0.52f, 0.54f, 0.62f);
            var pipe = new Color(0.42f, 0.38f, 0.34f);
            var cyan = new Color(0.3f, 0.95f, 1f);
            var pink = new Color(1f, 0.35f, 0.85f);
            var warm = new Color(1f, 0.82f, 0.6f);
            float cr = 2f * Art.Cylinder.bounds.extents.x; // (the cylinder mesh's width: scale by size / cr)

            // floor: deck plates in two tones with glowing seams, a rug under the seats
            Art.Box(t, dark, new Vector3(0, -0.1f, 0), new Vector3(15f, 0.2f, 13f));
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 2; z++)
                    if (((x + z) & 1) == 0) Art.Box(t, dark * 1.25f, new Vector3(x * 2f, 0.002f, z * 2f + 1f), new Vector3(1.92f, 0.004f, 1.92f));
            for (int i = -3; i <= 4; i++)
                Art.Box(t, Color.white, new Vector3(i * 2f - 1f, 0.006f, 0), new Vector3(0.03f, 0.01f, 12.5f), default, false, Workbench.Glow(cyan * 0.5f, 1f));
            Art.Part(t, Art.Cylinder, new Color(0.17f, 0.14f, 0.2f), new Vector3(0, 0.008f, 1.7f), new Vector3(9.5f / cr, 0.006f, 3.4f / cr));

            // the back wall, round a long panoramic window: the stars streak past it (the ship's going fast)
            float wl = -WinHalfX, wr = WinHalfX, wb = WinY - WinHalfY, wt = WinY + WinHalfY;
            Art.Box(t, hull, new Vector3(0, wb * 0.5f, WinZ), new Vector3(15f, wb, 0.3f));
            Art.Box(t, hull, new Vector3(0, (wt + 6.4f) * 0.5f, WinZ), new Vector3(15f, 6.4f - wt, 0.3f));
            foreach (float s in new[] { -1f, 1f })
                Art.Box(t, hull, new Vector3(s * (WinHalfX + 7.5f) * 0.5f + s * 0f, WinY, WinZ), new Vector3(7.5f - WinHalfX, WinHalfY * 2f, 0.3f));
            // its frame: thick bevelled trim, ribs across it, little lights in the sill
            Art.Box(t, trim, new Vector3(0, wt + 0.1f, WinZ - 0.18f), new Vector3(WinHalfX * 2f + 0.5f, 0.22f, 0.16f));
            Art.Box(t, trim, new Vector3(0, wb - 0.1f, WinZ - 0.22f), new Vector3(WinHalfX * 2f + 0.5f, 0.22f, 0.26f));
            foreach (float s in new[] { -1f, 1f }) Art.Box(t, trim, new Vector3(s * (WinHalfX + 0.14f), WinY, WinZ - 0.18f), new Vector3(0.28f, WinHalfY * 2f + 0.4f, 0.16f));
            for (int i = 1; i <= 3; i++)
                Art.Box(t, trim * 0.85f, new Vector3(wl + i * (wr - wl) / 4f, WinY, WinZ - 0.05f), new Vector3(0.1f, WinHalfY * 2f, 0.1f));
            for (int i = 0; i < 12; i++)
                Art.Box(t, Color.white, new Vector3(wl + 0.35f + i * (wr - wl - 0.7f) / 11f, wb - 0.02f, WinZ - 0.36f), new Vector3(0.08f, 0.03f, 0.04f), default, false, Workbench.Glow(cyan, 2f));
            // outside: black space, the streaking stars at different depths (parallax), a planet drifting by far off
            var space = new GameObject("space").transform;
            space.SetParent(t, false);
            Art.Box(space, Color.black, new Vector3(0, WinY, 40f), new Vector3(140f, 70f, 0.5f), default, false, Art.Mat(new Color(0.008f, 0.006f, 0.02f)));
            var rng = new System.Random(7);
            var starMat = Workbench.Glow(Color.white, 2.4f);
            var blue = Workbench.Glow(new Color(0.7f, 0.85f, 1f), 2.2f);
            for (int i = 0; i < 160; i++)
            {
                float z = 5f + (float)rng.NextDouble() * 30f;
                float near = Mathf.InverseLerp(35f, 5f, z);
                float speed = Mathf.Lerp(3f, 26f, near * near);
                float spread = (z + 2f) / (WinZ + 2f);
                float span = WinHalfX * (z + 2f) / (WinZ + 2f) + 3f;
                var pos = new Vector3(((float)rng.NextDouble() * 2f - 1f) * span, WinY + ((float)rng.NextDouble() * 2f - 1f) * WinHalfY * spread * 1.15f, z);
                float len = 0.05f + speed * 0.022f, th = 0.02f + near * 0.03f;
                var s = Art.Box(space, Color.white, pos, new Vector3(len, th, th), default, false, rng.NextDouble() < 0.25 ? blue : starMat).transform;
                m_Stars.Add(s);
                m_StarSpeed.Add(speed);
                m_StarSpan.Add(span);
            }
            m_Planet = Art.Part(space, Art.Sphere, Color.white, new Vector3(0, WinY - 2.5f, 36f), Vector3.one * (6f / SR), default, false, Workbench.Glow(new Color(0.9f, 0.45f, 0.3f), 0.9f)).transform;
            Art.Part(m_Planet, Art.Sphere, Color.white, new Vector3(0.05f, 0.1f, -0.02f), Vector3.one * 0.98f, default, false, Workbench.Glow(new Color(1f, 0.6f, 0.4f), 0.7f));

            // the side walls lean in at the top (it's a hull): panels, ribs, pipes and glowing strips along them
            foreach (float side in new[] { -1f, 1f })
            {
                var wall = new GameObject("wall").transform;
                wall.SetParent(t, false);
                wall.localPosition = new Vector3(side * 7.1f, 0f, 0f);
                wall.localRotation = Quaternion.Euler(0, 0, side * 12f);
                Art.Box(wall, hull, new Vector3(0, 2.8f, 0), new Vector3(0.3f, 6.4f, 13f));
                for (int i = -2; i <= 2; i++)
                {
                    Art.Box(wall, trim, new Vector3(-side * 0.32f, 2.8f, i * 2.4f), new Vector3(0.22f, 6.2f, 0.28f));
                    Art.Box(wall, panel, new Vector3(-side * 0.18f, 2.2f, i * 2.4f + 1.2f), new Vector3(0.06f, 1.6f, 1.8f));
                    Art.Box(wall, panel * 0.8f, new Vector3(-side * 0.18f, 4.2f, i * 2.4f + 1.2f), new Vector3(0.06f, 1.4f, 1.8f));
                }
                foreach (float py in new[] { 3.15f, 3.35f })
                    Art.Part(wall, Art.Cylinder, pipe, new Vector3(-side * 0.42f, py, 0), new Vector3(0.12f / cr, 6.4f, 0.12f / cr), new Vector3(90f, 0, 0));
                Art.Box(wall, Color.white, new Vector3(-side * 0.55f, 1.15f, 0), new Vector3(0.05f, 0.06f, 12.5f), default, false, Workbench.Glow(side < 0 ? pink : cyan, 1.6f));
            }
            // a console on the right, with blinking lights and a screen
            {
                var con = new GameObject("console").transform;
                con.SetParent(t, false);
                con.localPosition = new Vector3(5.9f, 0, 1.6f);
                con.localRotation = Quaternion.Euler(0, -90f, 0);
                Art.Box(con, dark, new Vector3(0, 0.5f, 0), new Vector3(2.4f, 1f, 0.8f));
                Art.Box(con, hull, new Vector3(0, 1.05f, 0.1f), new Vector3(2.4f, 0.12f, 1f), new Vector3(-20f, 0, 0));
                Art.Box(con, Color.white, new Vector3(0, 1.75f, 0.42f), new Vector3(1.6f, 0.8f, 0.04f), new Vector3(10f, 0, 0), false, Workbench.Glow(new Color(0.15f, 0.55f, 0.6f), 1.2f));
                for (int i = 0; i < 10; i++)
                {
                    var b = Art.Box(con, Color.white, new Vector3(-0.95f + i * 0.21f, 1.13f, 0.05f + (i % 2) * 0.2f), new Vector3(0.11f, 0.04f, 0.11f), new Vector3(-20f, 0, 0));
                    m_Blinkers.Add(b.GetComponent<Renderer>());
                }
            }
            // a vending machine on the left, lit up
            {
                var vm = new GameObject("vending machine").transform;
                vm.SetParent(t, false);
                vm.localPosition = new Vector3(-5.7f, 0, 2.2f);
                vm.localRotation = Quaternion.Euler(0, 90f, 0);
                Art.Box(vm, new Color(0.55f, 0.12f, 0.2f), new Vector3(0, 1.05f, 0), new Vector3(1.1f, 2.1f, 0.8f));
                Art.Box(vm, Color.white, new Vector3(-0.1f, 1.2f, 0.41f), new Vector3(0.7f, 1.4f, 0.02f), default, false, Workbench.Glow(new Color(0.9f, 0.95f, 1f), 1.1f));
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                        Art.Box(vm, new[] { new Color(0.85f, 0.75f, 0.2f), new Color(0.2f, 0.7f, 0.3f), new Color(0.9f, 0.3f, 0.3f) }[(r + c) % 3], new Vector3(-0.32f + c * 0.22f, 0.7f + r * 0.32f, 0.44f), new Vector3(0.1f, 0.18f, 0.04f));
                Art.Box(vm, dark, new Vector3(0.38f, 1.3f, 0.41f), new Vector3(0.18f, 0.5f, 0.02f));
                Art.Box(vm, Color.white, new Vector3(0, 1.98f, 0.41f), new Vector3(0.9f, 0.14f, 0.02f), default, false, Workbench.Glow(pink, 2f));
                Light(vm, new Vector3(0, 1.3f, 1f), new Color(1f, 0.6f, 0.8f), 1.2f, 4f);
            }
            // crates stacked in the back right corner, a plant with glowing bulbs in the back left
            Art.Box(t, new Color(0.35f, 0.38f, 0.3f), new Vector3(5.3f, 0.45f, 3.4f), new Vector3(0.9f, 0.9f, 0.9f), new Vector3(0, 8f, 0));
            Art.Box(t, new Color(0.3f, 0.33f, 0.4f), new Vector3(4.3f, 0.35f, 3.5f), new Vector3(0.7f, 0.7f, 0.7f), new Vector3(0, -12f, 0));
            Art.Box(t, new Color(0.38f, 0.3f, 0.25f), new Vector3(5.2f, 1.2f, 3.4f), new Vector3(0.6f, 0.6f, 0.6f), new Vector3(0, 30f, 0));
            Art.Part(t, Art.Cylinder, new Color(0.25f, 0.27f, 0.3f), new Vector3(-5.2f, 0.25f, 3.4f), new Vector3(0.6f / cr, 0.25f, 0.6f / cr));
            var leaf = new Color(0.25f, 0.6f, 0.4f);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f;
                Art.Box(t, leaf, new Vector3(-5.2f, 0.85f, 3.4f) + Quaternion.Euler(0, a, 0) * new Vector3(0, 0.1f * (i % 3), 0.18f), new Vector3(0.07f, 0.9f + (i % 3) * 0.2f, 0.18f), new Vector3(18f, a, 0));
                Art.Part(t, Art.Sphere, Color.white, new Vector3(-5.2f, 1.35f + (i % 3) * 0.18f, 3.4f) + Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 0.3f), Vector3.one * (0.07f / SR), default, false, Workbench.Glow(new Color(0.6f, 1f, 0.5f), 2f));
            }
            m_BlinkOn.Add(Workbench.Glow(cyan, 2f));
            m_BlinkOn.Add(Workbench.Glow(pink, 2f));
            m_BlinkOn.Add(Workbench.Glow(new Color(0.6f, 1f, 0.3f), 2f));
            m_BlinkOff = Art.Mat(new Color(0.1f, 0.12f, 0.15f));

            // ceiling: panels, pipes along it, light strips, and a lamp hanging over the group
            Art.Box(t, hull * 0.75f, new Vector3(0, 6.1f, 0), new Vector3(15f, 0.3f, 13f));
            for (int i = -2; i <= 2; i++) Art.Box(t, trim * 0.8f, new Vector3(i * 2.6f, 5.88f, 0), new Vector3(0.2f, 0.16f, 12.5f));
            for (int i = -1; i <= 1; i += 2)
                Art.Box(t, Color.white, new Vector3(i * 4.2f, 5.9f, 0.5f), new Vector3(0.3f, 0.04f, 8f), default, false, Workbench.Glow(new Color(1f, 0.95f, 0.85f), 1.5f));
            Art.Part(t, Art.Cylinder, pipe, new Vector3(-1.3f, 5.6f, 0), new Vector3(0.16f / cr, 6.4f, 0.16f / cr), new Vector3(90f, 0, 0));
            Art.Box(t, dark, new Vector3(0, 4.6f, 1.4f), new Vector3(0.03f, 1.8f, 0.03f));
            Art.Part(t, Art.Cone, new Color(0.18f, 0.2f, 0.24f), new Vector3(0, 3.45f, 1.4f), new Vector3(1.3f, 0.5f, 1.3f));
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 3.5f, 1.4f), Vector3.one * (0.16f / SR), default, false, Workbench.Glow(warm, 3f));

            // lights: the warm lamp over the group, a cool glow in through the window, cyan and pink from the sides
            Light(t, new Vector3(0, 3.3f, 1.3f), warm, 4.2f, 7.5f);
            Light(t, new Vector3(0, 3.4f, 2.6f), new Color(0.55f, 0.7f, 1f), 1.1f, 5f);
            Light(t, new Vector3(-5f, 1.6f, -0.5f), pink, 1.6f, 8f);
            Light(t, new Vector3(5f, 1.6f, -0.5f), cyan, 1.6f, 8f);
            Light(t, new Vector3(0, 2.2f, -4.5f), new Color(0.75f, 0.8f, 1f), 1.6f, 10f);
            foreach (var r in m_Room.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Light(Transform t, Vector3 at, Color c, float intensity, float range)
        {
            var go = new GameObject("light");
            go.transform.SetParent(t, false);
            go.transform.localPosition = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }
    }
}
