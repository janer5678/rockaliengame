using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The lobby before a match (replacing the old waiting stadium on screen): the inside of an alien ship, far off the
    /// map, with every player sitting about in it as an alien in their team's colour - chilling in one of a few poses,
    /// breathing and looking round - their names over their heads (Hud.Lobby.cs draws those and the buttons: LEAVE,
    /// COPY ROOM ID, GAME OPTIONS for the host, the team picker and READY). The camera holds one slowly drifting shot of
    /// them, like a group sat in a waiting room. Only the look: the players' real bodies still wait in the stadium
    /// (PlayerController doesn't move them while the lobby's up), and the match starts once everyone is READY
    /// (NetGame.ServerReadyToStart). Solo, the tutorial and the tests (no lobby) skip it.
    /// </summary>
    [DefaultExecutionOrder(1000)] // (after PlayerController: the lobby has the camera)
    public class ShipLobby : MonoBehaviour
    {
        /// <summary>Where the room is: far out past the map, where nothing else is.</summary>
        public static readonly Vector3 Center = new Vector3(3000f, 60f, 3000f);

        static ShipLobby s_I;
        GameObject m_Room;
        readonly List<Renderer> m_Blinkers = new List<Renderer>();
        readonly List<Material> m_BlinkOn = new List<Material>();
        Material m_BlinkOff;
        Transform m_Window;

        class Avatar
        {
            public PlayerNet P;
            public Transform Root;
            public BodyAnimator Anim;
            public readonly List<Material> Mats = new List<Material>();
            public int Team = -1, Variant;
            public float Seed;
            public Vector3 Seat;
        }
        readonly Dictionary<PlayerNet, Avatar> m_Avatars = new Dictionary<PlayerNet, Avatar>();
        static readonly List<PlayerNet> s_Order = new List<PlayerNet>();
        static readonly List<PlayerNet> s_Gone = new List<PlayerNet>();

        /// <summary>The lobby's up: this PC is a player in a match that hasn't started (and isn't solo / the tutorial).</summary>
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
            var sp = cam.WorldToScreenPoint(a.Root.position + Vector3.up * 1.45f); // (just over a seated head)
            if (sp.z <= 0f) return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        /// <summary>The players in the lobby, in their seat order (by team, then slot).</summary>
        public static IReadOnlyList<PlayerNet> Seated => s_Order;

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
            float t = Time.time;
            foreach (var a in m_Avatars.Values) a.Anim?.Lounge(a.Variant, t + a.Seed);
            // blinking console lights
            for (int i = 0; i < m_Blinkers.Count; i++)
                if (m_Blinkers[i]) m_Blinkers[i].sharedMaterial = Mathf.PerlinNoise(i * 3.1f, t * 0.9f) > 0.48f ? m_BlinkOn[i % m_BlinkOn.Count] : m_BlinkOff;
            if (m_Window) m_Window.localRotation = Quaternion.Euler(0, 0, t * 1.5f); // (the stars turn slowly past the porthole)
            // one slowly drifting shot of them all, a little from above, like the reference
            var cam = Camera.main;
            if (cam != null)
            {
                // close enough that they fill the shot (further back the more there are)
                int n = s_Order.Count;
                float back = Mathf.Clamp(3.3f + Mathf.Max(0, n - 3) * 0.6f, 3.3f, 7.2f);
                var pos = Center + new Vector3(Mathf.Sin(t * 0.11f) * 0.35f, 1.6f + back * 0.07f + Mathf.Sin(t * 0.17f) * 0.06f, 1.6f - back);
                var look = Center + new Vector3(Mathf.Sin(t * 0.07f) * 0.25f, 0.95f, 1.6f);
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
                if ((seat - a.Seat).sqrMagnitude > 0.0001f || a.Root.position == Vector3.zero)
                {
                    a.Seat = seat;
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
            var a = new Avatar { P = p, Seed = (p.OwnerClientId * 7.31f) % 50f, Variant = (int)(p.OwnerClientId % 4) };
            var go = new GameObject("lobby alien " + p.DisplayName);
            go.transform.SetParent(m_Room.transform, true);
            a.Root = go.transform;
            a.Anim = BodyAnimator.TryCreate(a.Root, Cfg.ModelWidth, out var model);
            if (model != null)
            {
                PlayerNet.SkinAlien(model, a.Mats);
                SmoothShadeHook.Add(model, true);
            }
            // the chair under them
            var metal = new Color(0.3f, 0.32f, 0.38f);
            Art.Box(a.Root, metal, new Vector3(0f, 0.33f, -0.12f), new Vector3(0.62f, 0.07f, 0.56f));
            Art.Box(a.Root, new Color(0.42f, 0.2f, 0.55f), new Vector3(0f, 0.39f, -0.12f), new Vector3(0.56f, 0.06f, 0.5f));
            Art.Box(a.Root, metal, new Vector3(0f, 0.8f, -0.42f), new Vector3(0.6f, 0.8f, 0.06f), new Vector3(-8f, 0f, 0f));
            Art.Box(a.Root, metal * 0.7f, new Vector3(0f, 0.16f, -0.12f), new Vector3(0.12f, 0.32f, 0.12f));
            Art.Box(a.Root, metal * 0.7f, new Vector3(0f, 0.02f, -0.12f), new Vector3(0.5f, 0.04f, 0.4f));
            return a;
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
            var hull = new Color(0.27f, 0.29f, 0.35f);
            var dark = new Color(0.14f, 0.15f, 0.2f);
            var trim = new Color(0.5f, 0.52f, 0.6f);
            var cyan = new Color(0.3f, 0.95f, 1f);
            var pink = new Color(1f, 0.35f, 0.85f);
            // floor: dark deck plates with glowing seams
            Art.Box(t, dark, new Vector3(0, -0.1f, 0), new Vector3(15f, 0.2f, 13f));
            for (int i = -3; i <= 3; i++)
                Art.Box(t, Color.white, new Vector3(i * 2f, 0.005f, 0), new Vector3(0.04f, 0.01f, 12.5f), default, false, Workbench.Glow(cyan * 0.6f, 1.2f));
            // the back wall with a big round porthole full of stars, and a planet going by
            Art.Box(t, hull, new Vector3(0, 3f, 4.2f), new Vector3(15f, 6.4f, 0.3f));
            var port = new GameObject("porthole").transform;
            port.SetParent(t, false);
            port.localPosition = new Vector3(0, 2.9f, 4.02f);
            float cr = 2f * Art.Cylinder.bounds.extents.x; // (the cylinder mesh's width: scale by size / cr)
            Art.Part(port, Art.Cylinder, Color.black, Vector3.zero, new Vector3(3.7f / cr, 0.01f, 3.7f / cr), new Vector3(90f, 0, 0), false, Art.Mat(new Color(0.02f, 0.02f, 0.06f)));
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Art.Box(port, trim, new Vector3(Mathf.Cos(a) * 1.85f, Mathf.Sin(a) * 1.85f, -0.05f), new Vector3(0.5f, 0.22f, 0.2f), new Vector3(0, 0, a * Mathf.Rad2Deg + 90f));
            }
            m_Window = new GameObject("stars").transform;
            m_Window.SetParent(port, false);
            m_Window.localPosition = new Vector3(0, 0, -0.03f);
            var rng = new System.Random(7);
            var star = Workbench.Glow(Color.white, 2.2f);
            for (int i = 0; i < 70; i++)
            {
                float r = 1.7f * Mathf.Sqrt((float)rng.NextDouble()), a = (float)rng.NextDouble() * Mathf.PI * 2f, s = 0.015f + (float)rng.NextDouble() * 0.035f;
                Art.Part(m_Window, Art.Sphere, Color.white, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0), Vector3.one * s, default, false, star);
            }
            Art.Part(port, Art.Sphere, Color.white, new Vector3(0.75f, -0.6f, -0.02f), new Vector3(1.2f, 1.2f, 0.05f), default, false, Workbench.Glow(new Color(0.95f, 0.45f, 0.3f), 1.1f));
            Art.Part(port, Art.Sphere, Color.white, new Vector3(-0.9f, 0.7f, -0.02f), new Vector3(0.35f, 0.35f, 0.05f), default, false, Workbench.Glow(new Color(0.5f, 0.85f, 1f), 1.2f));
            // the side walls lean in at the top (it's a hull), with ribs and glowing strips along them
            foreach (float side in new[] { -1f, 1f })
            {
                Art.Box(t, hull, new Vector3(side * 7.1f, 2.8f, 0), new Vector3(0.3f, 6.4f, 13f), new Vector3(0, 0, side * 12f));
                for (int i = -2; i <= 2; i++)
                    Art.Box(t, trim, new Vector3(side * 6.75f, 2.8f, i * 2.4f), new Vector3(0.25f, 6.2f, 0.3f), new Vector3(0, 0, side * 12f));
                Art.Box(t, Color.white, new Vector3(side * 6.55f, 1.2f, 0), new Vector3(0.05f, 0.06f, 12.5f), new Vector3(0, 0, side * 12f), false, Workbench.Glow(side < 0 ? pink : cyan, 1.6f));
                // a console on each side, with blinking lights
                var con = new GameObject("console").transform;
                con.SetParent(t, false);
                con.localPosition = new Vector3(side * 5.6f, 0, -1.2f);
                con.localRotation = Quaternion.Euler(0, side * -90f, 0);
                Art.Box(con, dark, new Vector3(0, 0.5f, 0), new Vector3(2.2f, 1f, 0.8f));
                Art.Box(con, hull, new Vector3(0, 1.05f, 0.1f), new Vector3(2.2f, 0.12f, 1f), new Vector3(-20f, 0, 0));
                for (int i = 0; i < 8; i++)
                {
                    var b = Art.Box(con, Color.white, new Vector3(-0.85f + i * 0.24f, 1.13f, 0.05f + (i % 2) * 0.2f), new Vector3(0.12f, 0.04f, 0.12f), new Vector3(-20f, 0, 0));
                    m_Blinkers.Add(b.GetComponent<Renderer>());
                }
            }
            m_BlinkOn.Add(Workbench.Glow(cyan, 2f));
            m_BlinkOn.Add(Workbench.Glow(pink, 2f));
            m_BlinkOn.Add(Workbench.Glow(new Color(0.6f, 1f, 0.3f), 2f));
            m_BlinkOff = Art.Mat(new Color(0.1f, 0.12f, 0.15f));
            // ceiling with light panels
            Art.Box(t, hull * 0.8f, new Vector3(0, 6.1f, 0), new Vector3(15f, 0.3f, 13f));
            for (int i = -1; i <= 1; i++)
                Art.Box(t, Color.white, new Vector3(i * 3.2f, 5.92f, 0.5f), new Vector3(1.8f, 0.06f, 0.5f), default, false, Workbench.Glow(new Color(1f, 0.95f, 0.85f), 1.8f));
            // lights: warm from above, cyan and pink from the sides
            Light(t, new Vector3(0, 5.2f, 0.6f), new Color(1f, 0.92f, 0.8f), 3.2f, 14f);
            Light(t, new Vector3(-5f, 2f, -0.5f), pink, 2.2f, 9f);
            Light(t, new Vector3(5f, 2f, -0.5f), cyan, 2.2f, 9f);
            Light(t, new Vector3(0, 2.5f, -5f), new Color(0.75f, 0.8f, 1f), 1.4f, 10f);
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
