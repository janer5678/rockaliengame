using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The lobby before a match (replacing the old waiting stadium on screen): the crew's grubby living room on an alien
    /// ship tearing through space, far off the map. Every player is an alien in their team's colour slumped on one long
    /// battered couch - chilling in one of seven poses (BodyAnimator.Lounge: some smoke, some drink a beer, some are on
    /// their phone) - lit by a flickering old CRT telly in front of them, their names over their heads (Hud.Lobby.cs draws
    /// those and the buttons). Behind them a long rounded window with the stars streaking past; the room hums and
    /// rattles with the speed, and a light sweeps through now and then as something flashes by outside. Junk everywhere:
    /// pizza boxes, cans, a laundry pile, a lava lamp, a mini fridge, posters. The camera holds a drifting shot of them;
    /// put the mouse near the edge of the screen to look round the room. CUSTOMISE ALIEN (Hud.Lobby.cs, Customising) zooms
    /// in on your alien's head, sat still, to try hats on (Cosmetics.cs).
    /// Only the look: the players' real bodies still wait in the stadium (PlayerController doesn't move them while the
    /// lobby's up), and the match starts once everyone is READY (NetGame.ServerReadyToStart). Solo and the tests (no
    /// lobby) skip it.
    /// </summary>
    [DefaultExecutionOrder(1000)] // (after PlayerController: the lobby has the camera)
    public class ShipLobby : MonoBehaviour
    {
        /// <summary>Where the room is: far out past the map, where nothing else is.</summary>
        public static readonly Vector3 Center = new Vector3(3000f, 60f, 3000f);

        /// <summary>The window: a long rounded opening in the back wall (room space), its middle height and half sizes.</summary>
        const float WinZ = 4.2f, WinY = 1.95f, WinHalfX = 3.9f, WinHalfY = 0.78f;
        const float RoomH = 3.5f;

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
        static float CR => 2f * Art.Cylinder.bounds.extents.x;
        static float CH => 2f * Art.Cylinder.bounds.extents.y;

        // the telly (its screen and the light it throws flicker), the dying strip light, the light flashing past outside
        Material m_TvMat;
        Light m_TvLight, m_Strip, m_PassLight;
        Renderer m_StripTube;
        Material m_StripOn, m_StripOff;

        // the camera: where it is and where it looks (eased), and the look round the room (mouse at the screen's edges)
        Vector3 m_CamPos;
        Quaternion m_CamRot = Quaternion.identity;
        bool m_CamSet;
        float m_Yaw, m_Pitch;

        class Puff { public Transform T; public float Born = -99f; public Vector3 Vel; public float Size; }

        class Avatar
        {
            public PlayerNet P;
            public Transform Root, Seat, ArmL, ArmR;
            public BodyAnimator Anim;
            public readonly List<Material> Mats = new List<Material>();
            public int Team = -1, Variant, HatShown = -1;
            public float Seed, NextPuff;
            public Vector3 SeatAt;
            public GameObject Hat;
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

        /// <summary>CUSTOMISE ALIEN is open (Hud.Lobby.cs): the camera's on your alien's head and it sits still.</summary>
        public static bool Customising;

        /// <summary>Where a player's head is on screen in the lobby (for their name tag), and whether it's in view.</summary>
        public static bool HeadOnScreen(PlayerNet p, out Vector2 gui)
        {
            gui = default;
            if (s_I == null || p == null || !s_I.m_Avatars.TryGetValue(p, out var a) || a.Root == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            var head = a.Anim != null && a.Anim.HeadBone != null ? a.Anim.HeadBone.position + Vector3.up * (a.Hat != null ? 0.62f : 0.42f) : a.Root.position + Vector3.up * 1.45f;
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
        /// <summary>(tests) how far the camera has turned to look round the room (degrees).</summary>
        public static float LookYaw => s_I != null ? s_I.m_Yaw : 0f;
        /// <summary>(tests) hold the mouse at this point of the screen (0..1; x &lt; 0: the real mouse).</summary>
        public static Vector2 TestMouse = new Vector2(-1f, -1f);

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
                Customising = false;
                return;
            }
            if (m_Room == null) BuildRoom();
            SyncAvatars();
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            var me = PlayerNet.Local;
            foreach (var kv in m_Avatars)
            {
                var a = kv.Value;
                if (a.Anim == null) continue;
                bool still = Customising && kv.Key == me; // (customising: sat still, nothing in the hands)
                float lt = still ? 0.6f : TestPhase >= 0f ? TestPhase : t + a.Seed;
                a.Anim.Lounge(still ? 0 : a.Variant, lt);
                FitSeat(a);
                if (a.Prop) a.Prop.gameObject.SetActive(!still);
                if (!still) PlaceProp(a, lt, t);
                if (a.HatShown != a.P.Hat.Value)
                {
                    a.HatShown = a.P.Hat.Value;
                    if (a.Hat) Destroy(a.Hat);
                    a.Hat = Cosmetics.Wear(a.HatShown, a.Root, a.Anim);
                }
            }
            AnimateRoom(t, dt);
            DriveCamera(t, dt, me);
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
            m_CamSet = false;
            m_Yaw = m_Pitch = 0f;
        }

        void OnDestroy() => Clear();

        // ------------------------------------------------------------------ the camera

        void DriveCamera(float t, float dt, PlayerNet me)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 pos, look;
            float fov = 56f;
            Avatar mine = null;
            if (me != null) m_Avatars.TryGetValue(me, out mine);
            if (Customising && mine != null && mine.Anim != null && mine.Anim.HeadBone != null)
            {
                // CUSTOMISE ALIEN: close on your alien's face (a little from the side, so the hat shows)
                var head = mine.Anim.HeadBone.position + Vector3.up * 0.16f;
                var f = mine.Root.forward;
                pos = head + f * 1.15f + mine.Root.right * 0.32f + Vector3.up * 0.12f;
                look = head + Vector3.up * 0.05f;
                fov = 38f;
                m_Yaw = Mathf.Lerp(m_Yaw, 0f, 1f - Mathf.Exp(-4f * dt));
                m_Pitch = Mathf.Lerp(m_Pitch, 0f, 1f - Mathf.Exp(-4f * dt));
            }
            else if (TestFocus >= 0 && TestFocus < s_Order.Count && m_Avatars.TryGetValue(s_Order[TestFocus], out var fa))
            {
                // (tests: close on one of them)
                var c = fa.Root.position + Vector3.up * 0.95f;
                pos = c + fa.Root.forward * 1.7f + Vector3.up * 0.25f + fa.Root.right * 0.3f;
                look = c;
                fov = 50f;
                m_CamSet = false;
            }
            else
            {
                // one slowly drifting shot of them all on the couch (further back the more there are)
                int n = s_Order.Count;
                float back = Mathf.Clamp(3.6f + Mathf.Max(0, n - 3) * 0.6f, 3.6f, 7.2f);
                pos = Center + new Vector3(Mathf.Sin(t * 0.11f) * 0.3f, 1.5f + back * 0.06f + Mathf.Sin(t * 0.17f) * 0.05f, 1.6f - back);
                look = Center + new Vector3(Mathf.Sin(t * 0.07f) * 0.25f, 1.05f, 1.6f);
                // looking round the room: the mouse near an edge of the screen turns the camera that way (smoothly)
                Vector2 m = TestMouse.x >= 0f ? TestMouse : new Vector2(Input.mousePosition.x / Mathf.Max(1, Screen.width), Input.mousePosition.y / Mathf.Max(1, Screen.height));
                bool free = (Application.isFocused || TestMouse.x >= 0f) && !Hud.MouseOverUI && !Chat.Open && m.x >= 0f && m.x <= 1f && m.y >= 0f && m.y <= 1f;
                const float Edge = 0.12f;
                float ex = !free ? 0f : m.x < Edge ? -(1f - m.x / Edge) : m.x > 1f - Edge ? (m.x - (1f - Edge)) / Edge : 0f;
                float ey = !free ? 0f : m.y < Edge ? -(1f - m.y / Edge) : m.y > 1f - Edge ? (m.y - (1f - Edge)) / Edge : 0f;
                m_Yaw = Mathf.Clamp(m_Yaw + ex * Mathf.Abs(ex) * 55f * dt, -70f, 70f);
                m_Pitch = Mathf.Clamp(m_Pitch - ey * Mathf.Abs(ey) * 35f * dt, -22f, 28f);
            }
            var rot = Quaternion.LookRotation(look - pos);
            rot = Quaternion.Euler(0f, m_Yaw, 0f) * rot * Quaternion.Euler(m_Pitch, 0f, 0f);
            // the ship's going flat out: the room hums and rattles (and jolts now and then)
            float jolt = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.9f)), 40f);
            var rattle = new Vector3(Mathf.PerlinNoise(t * 17f, 1.1f) - 0.5f, Mathf.PerlinNoise(t * 19f, 4.2f) - 0.5f, Mathf.PerlinNoise(t * 13f, 8.3f) - 0.5f) * (1f + jolt * 4f);
            pos += rattle * 0.012f;
            rot *= Quaternion.Euler(rattle.y * 0.35f, rattle.x * 0.35f, rattle.z * 0.5f);
            // eased (a smooth move into and out of the close-up)
            if (!m_CamSet) { m_CamPos = pos; m_CamRot = rot; m_CamSet = true; cam.fieldOfView = fov; }
            float k = 1f - Mathf.Exp(-(Customising ? 5f : 8f) * dt);
            m_CamPos = Vector3.Lerp(m_CamPos, pos, k);
            m_CamRot = Quaternion.Slerp(m_CamRot, rot, k);
            cam.transform.SetPositionAndRotation(m_CamPos, m_CamRot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
        }

        // ------------------------------------------------------------------ the players

        void SyncAvatars()
        {
            s_Gone.Clear();
            foreach (var kv in m_Avatars) if (kv.Key == null || !kv.Key.IsSpawned) s_Gone.Add(kv.Key);
            foreach (var p in s_Gone) { if (m_Avatars[p].Root) Destroy(m_Avatars[p].Root.gameObject); m_Avatars.Remove(p); }
            s_Order.Clear();
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) s_Order.Add(p);
            s_Order.Sort((a, b) => a.Team.Value != b.Team.Value ? a.Team.Value.CompareTo(b.Team.Value) : a.Slot.Value != b.Slot.Value ? a.Slot.Value.CompareTo(b.Slot.Value) : a.OwnerClientId.CompareTo(b.OwnerClientId));
            int n = s_Order.Count;
            int rows = n > 8 ? 2 : 1, perRow = Mathf.CeilToInt(n / (float)rows);
            for (int i = 0; i < n; i++)
            {
                var p = s_Order[i];
                if (!m_Avatars.TryGetValue(p, out var a)) m_Avatars[p] = a = MakeAvatar(p);
                var seat = SeatPos(i, n);
                if ((seat - a.SeatAt).sqrMagnitude > 0.0001f || a.Root.position == Vector3.zero)
                {
                    a.SeatAt = seat;
                    a.Root.position = Center + seat;
                    // facing the camera (and the telly), turned a little towards the middle of the group
                    a.Root.rotation = Quaternion.Euler(0f, 180f - seat.x * 4f, 0f);
                }
                // the couch's arms only at its two ends (the sections in between join up into one long couch)
                int row = i / perRow, col = i % perRow, inRow = row == rows - 1 ? n - perRow * (rows - 1) : perRow;
                // (the alien faces the camera: its right is the room's left)
                if (a.ArmR) a.ArmR.gameObject.SetActive(col == 0);
                if (a.ArmL) a.ArmL.gameObject.SetActive(col == inRow - 1);
                if (a.Team != p.Team.Value) Tint(a, p.Team.Value);
            }
        }

        /// <summary>Seat i of n: one gentle arc along the couch (two couches past eight: the front one lower and closer).</summary>
        static Vector3 SeatPos(int i, int n)
        {
            int rows = n > 8 ? 2 : 1;
            int perRow = Mathf.CeilToInt(n / (float)rows);
            int row = i / perRow, col = i % perRow;
            int inRow = row == rows - 1 ? n - perRow * (rows - 1) : perRow;
            float span = Mathf.Min(9f, 1.25f * (inRow - 1));
            float x = inRow <= 1 ? 0f : -span * 0.5f + span * col / (inRow - 1);
            float z = 1.6f - row * 1.8f + Mathf.Abs(x) * 0.16f; // (curving round at the ends)
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
            // their stretch of the couch: a battered mustard couch, worn and stained (the cushion sinks to the hips: FitSeat)
            var fabric = new Color(0.52f, 0.4f, 0.2f);
            var worn = fabric * 0.8f;
            var wood = new Color(0.3f, 0.2f, 0.12f);
            var couch = new GameObject("couch").transform;
            couch.SetParent(a.Root, false);
            Art.Box(couch, wood, new Vector3(0f, 0.12f, -0.12f), new Vector3(1.3f, 0.2f, 0.92f));                       // the base
            Art.Box(couch, worn, new Vector3(0f, 0.27f, -0.12f), new Vector3(1.28f, 0.14f, 0.9f));                      // the frame under the cushion
            Art.Box(couch, worn, new Vector3(0f, 0.72f, -0.53f), new Vector3(1.3f, 0.86f, 0.2f), new Vector3(-10f, 0f, 0f)); // the back
            Art.Box(couch, fabric * 0.92f, new Vector3(0f, 0.74f, -0.42f), new Vector3(1.16f, 0.62f, 0.14f), new Vector3(-10f, 0f, 0f)); // the back cushion
            Art.Box(couch, fabric * 0.6f, new Vector3(0.2f, 0.6f, -0.34f), new Vector3(0.3f, 0.2f, 0.02f), new Vector3(-10f, 0f, 14f));   // (a stain)
            for (int s = -1; s <= 1; s += 2)
                Art.Box(couch, wood * 0.7f, new Vector3(s * 0.55f, 0.02f, 0.25f), new Vector3(0.08f, 0.04f, 0.08f));       // (stubby feet)
            a.Seat = new GameObject("cushion").transform;
            a.Seat.SetParent(couch, false);
            Art.Box(a.Seat, fabric, new Vector3(0f, 0f, -0.08f), new Vector3(1.22f, 0.16f, 0.78f));
            Art.Box(a.Seat, fabric * 0.85f, new Vector3(0f, -0.005f, 0.31f), new Vector3(1.22f, 0.15f, 0.04f)); // (the cushion's front seam)
            if (p.OwnerClientId % 3 == 1) Art.Box(a.Seat, new Color(0.3f, 0.22f, 0.1f), new Vector3(-0.25f, 0.082f, 0f), new Vector3(0.3f, 0.005f, 0.22f), new Vector3(0, 25f, 0)); // (a mystery stain)
            a.ArmL = Art.Box(couch, worn, new Vector3(-0.7f, 0.45f, -0.12f), new Vector3(0.22f, 0.5f, 0.92f)).transform;
            a.ArmR = Art.Box(couch, worn, new Vector3(0.7f, 0.45f, -0.12f), new Vector3(0.22f, 0.5f, 0.92f)).transform;
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
                case 5: // a can of beer (and an empty one on the floor)
                    a.Prop = BeerCan(a.Root).transform;
                    var empty = BeerCan(a.Root).transform;
                    empty.localPosition = new Vector3(0.42f, 0.03f, 0.35f);
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
            Art.Part(can.transform, Art.Cylinder, new Color(0.85f, 0.75f, 0.2f), Vector3.zero, new Vector3(0.066f / CR, 0.12f / CH, 0.066f / CR));
            Art.Part(can.transform, Art.Cylinder, new Color(0.75f, 0.76f, 0.8f), new Vector3(0, 0.062f, 0), new Vector3(0.06f / CR, 0.008f / CH, 0.06f / CR));
            Art.Part(can.transform, Art.Cylinder, new Color(0.15f, 0.35f, 0.75f), new Vector3(0, -0.005f, 0), new Vector3(0.068f / CR, 0.044f / CH, 0.068f / CR));
            return can;
        }

        /// <summary>The cushion sinks or rises under the hips (wherever the pose has put them).</summary>
        static void FitSeat(Avatar a)
        {
            float y = Mathf.Clamp(a.Anim.HipsHeight - 0.16f, 0.26f, 0.6f);
            a.Seat.localPosition = new Vector3(0f, y, 0f);
        }

        /// <summary>How far past the wrist the fingers close round something (the alien's long hand).</summary>
        const float Grip = 0.13f;

        /// <summary>The prop in the hand(s) this frame, and the smoke.</summary>
        void PlaceProp(Avatar a, float t, float now)
        {
            if (a.Prop == null) return;
            var rh = a.Anim.RightHand; var rf = a.Anim.RightFore;
            if (rh == null || rf == null) return;
            var dir = (rh.position - rf.position).normalized;
            var palm = rh.position + dir * Grip; // (in the fingers, not at the wrist)
            var up = a.Root.up; var fwd = a.Root.forward;
            switch (a.Variant)
            {
                case 4:
                {
                    float d = BodyAnimator.Drag(t);
                    var side = Vector3.Cross(dir, up);
                    if (side.sqrMagnitude < 0.01f) side = a.Root.right;
                    // held between two fingers, sticking out sideways
                    a.Prop.SetPositionAndRotation(palm + dir * 0.02f, Quaternion.LookRotation(-side.normalized, up));
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
                    a.Prop.SetPositionAndRotation(palm, Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.LookRotation(fwd, up));
                    break;
                }
                case 6:
                {
                    var lh = a.Anim.LeftHand; var lf = a.Anim.LeftFore;
                    var lpalm = lh != null && lf != null ? lh.position + (lh.position - lf.position).normalized * Grip : palm;
                    var at = (palm + lpalm) * 0.5f + up * 0.02f;
                    var head = a.Anim.HeadBone != null ? a.Anim.HeadBone.position + up * 0.1f : at + up * 0.4f;
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

        // ------------------------------------------------------------------ the room going by

        void AnimateRoom(float t, float dt)
        {
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
                p.x = 60f - Mathf.Repeat(t * 2.2f + 40f, 120f);
                m_Planet.localPosition = p;
                m_Planet.localRotation = Quaternion.Euler(12f, t * 3f, 0f);
            }
            // the telly: a jumpy cartoon - colours cutting from scene to scene, flickering, lighting their faces
            if (m_TvMat != null)
            {
                float cut = Mathf.Floor(t * 1.3f);
                var scene = Color.HSVToRGB(Mathf.Repeat(cut * 0.37f, 1f), 0.55f, 1f);
                float fl = 0.75f + 0.25f * Mathf.PerlinNoise(t * 24f, 2f) + (Mathf.Repeat(t * 1.3f, 1f) < 0.05f ? 0.6f : 0f);
                m_TvMat.SetColor("_Color", Color.Lerp(scene, Color.white, 0.35f) * fl);
                if (m_TvLight) { m_TvLight.color = Color.Lerp(scene, new Color(0.7f, 0.8f, 1f), 0.4f); m_TvLight.intensity = 3.2f * fl; }
            }
            // the dying strip light in the corner: it stutters on and off
            if (m_Strip)
            {
                float n = Mathf.PerlinNoise(t * 3f, 7f);
                bool on = n > 0.32f || Mathf.PerlinNoise(t * 30f, 1f) > 0.7f;
                m_Strip.enabled = on;
                if (m_StripTube) m_StripTube.sharedMaterial = on ? m_StripOn : m_StripOff;
            }
            // now and then a light sweeps through the room from the window: something flashing past outside (we're fast)
            if (m_PassLight)
            {
                float u = Mathf.Repeat(t, 7f);
                bool pass = u < 0.9f;
                m_PassLight.enabled = pass;
                if (pass)
                {
                    m_PassLight.transform.localPosition = new Vector3(Mathf.Lerp(9f, -9f, u / 0.9f), WinY + 0.4f, WinZ + 1.5f);
                    m_PassLight.intensity = Mathf.Sin(u / 0.9f * Mathf.PI) * 9f;
                }
            }
        }

        // ------------------------------------------------------------------ the room

        void BuildRoom()
        {
            m_Room = new GameObject("ShipLobby");
            m_Room.transform.position = Center;
            var t = m_Room.transform;
            var wallC = new Color(0.4f, 0.37f, 0.32f);       // grimy beige panels
            var wallDark = new Color(0.27f, 0.25f, 0.22f);
            var trim = new Color(0.46f, 0.46f, 0.5f);
            var metal = new Color(0.32f, 0.33f, 0.36f);
            var carpet = new Color(0.22f, 0.2f, 0.15f);
            var pipe = new Color(0.45f, 0.38f, 0.3f);
            var cyan = new Color(0.3f, 0.95f, 1f);
            var warm = new Color(1f, 0.78f, 0.55f);
            var rng = new System.Random(11);
            float R() => (float)rng.NextDouble();

            // ---- the floor: a worn carpet over deck plates, stains and junk on it ----
            Art.Box(t, metal * 0.6f, new Vector3(0, -0.1f, 0), new Vector3(15f, 0.2f, 13f));
            Art.Box(t, carpet, new Vector3(0, 0.004f, 0.3f), new Vector3(12.5f, 0.008f, 8.2f));
            for (int i = 0; i < 9; i++) // (stains)
                Art.Part(t, Art.Cylinder, carpet * (0.6f + R() * 0.2f), new Vector3(R() * 10f - 5f, 0.01f, R() * 6f - 2.5f), new Vector3((0.4f + R() * 0.8f) / CR, 0.002f, (0.3f + R() * 0.6f) / CR));
            Art.Part(t, Art.Cylinder, new Color(0.35f, 0.15f, 0.12f), new Vector3(0, 0.012f, 0.4f), new Vector3(6.5f / CR, 0.004f, 3f / CR)); // (an old round rug in front of the couch)

            // ---- the back wall, round a long rounded window: the stars streak past it ----
            BuildWindowWall(t, wallC, trim, metal, cyan);

            // ---- the side walls (the hull leaning in at the top): grubby panels, pipes, posters ----
            foreach (float side in new[] { -1f, 1f })
            {
                var wall = new GameObject("wall").transform;
                wall.SetParent(t, false);
                wall.localPosition = new Vector3(side * 6.6f, 0f, 0f);
                wall.localRotation = Quaternion.Euler(0, 0, side * 8f);
                Art.Box(wall, wallC, new Vector3(0, RoomH * 0.5f, 0), new Vector3(0.3f, RoomH + 0.4f, 13f));
                for (int i = -2; i <= 2; i++)
                {
                    Art.Box(wall, trim * 0.8f, new Vector3(-side * 0.17f, RoomH * 0.5f, i * 2.4f), new Vector3(0.08f, RoomH, 0.2f));
                    Art.Box(wall, wallDark, new Vector3(-side * 0.16f, 0.45f, i * 2.4f + 1.2f), new Vector3(0.04f, 0.6f, 2f)); // (scuffed kick panels)
                    Art.Box(wall, wallC * (0.75f + R() * 0.15f), new Vector3(-side * 0.16f, 1.2f + R() * 1.4f, i * 2.4f + 0.6f + R()), new Vector3(0.03f, 0.3f + R() * 0.5f, 0.4f + R() * 0.6f)); // (grime)
                }
                foreach (float py in new[] { 2.75f, 2.95f })
                    Art.Part(wall, Art.Cylinder, pipe, new Vector3(-side * 0.3f, py, 0), new Vector3(0.1f / CR, 13f / CH, 0.1f / CR), new Vector3(90f, 0, 0));
                // posters: a band, a planet, a cat (in a manner of speaking)
                for (int i = 0; i < 2; i++)
                {
                    var pc = Color.HSVToRGB(R(), 0.6f, 0.8f);
                    float z = side < 0 ? -0.8f + i * 2.6f : 0.9f - i * 2.8f;
                    Art.Box(wall, Color.white * 0.85f, new Vector3(-side * 0.17f, 1.75f, z), new Vector3(0.02f, 0.95f, 0.7f), new Vector3(R() * 6f - 3f, 0, 0));
                    Art.Box(wall, pc, new Vector3(-side * 0.18f, 1.8f, z), new Vector3(0.02f, 0.7f, 0.58f), new Vector3(R() * 6f - 3f, 0, 0));
                    Art.Part(wall, Art.Sphere, pc * 0.5f + Color.white * 0.4f, new Vector3(-side * 0.19f, 1.85f, z), new Vector3(0.02f, 0.3f, 0.3f) / SR);
                }
            }

            // ---- the telly: a chunky old CRT on a crate in front of them, off to one side, facing the couch ----
            {
                var tv = new GameObject("telly").transform;
                tv.SetParent(t, false);
                tv.localPosition = new Vector3(-2.6f, 0f, -0.6f);
                tv.localRotation = Quaternion.LookRotation(new Vector3(2.6f, 0f, 2.2f));
                Art.Box(tv, new Color(0.45f, 0.32f, 0.18f), new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.5f, 0.6f));            // the crate
                for (int i = 0; i < 3; i++) Art.Box(tv, new Color(0.35f, 0.24f, 0.13f), new Vector3(0, 0.08f + i * 0.17f, 0.302f), new Vector3(0.9f, 0.04f, 0.01f));
                Art.Box(tv, new Color(0.16f, 0.15f, 0.14f), new Vector3(0, 0.82f, -0.05f), new Vector3(0.82f, 0.64f, 0.62f));     // the set
                Art.Box(tv, new Color(0.12f, 0.11f, 0.1f), new Vector3(0, 0.84f, -0.42f), new Vector3(0.6f, 0.46f, 0.3f));       // its back
                Art.Box(tv, new Color(0.08f, 0.08f, 0.08f), new Vector3(-0.06f, 0.84f, 0.262f), new Vector3(0.6f, 0.48f, 0.01f));
                m_TvMat = new Material(Unlit(Color.white, 1.6f)) { name = "telly" };
                Art.Box(tv, Color.white, new Vector3(-0.06f, 0.84f, 0.27f), new Vector3(0.54f, 0.42f, 0.01f), default, false, m_TvMat);
                for (int i = 0; i < 2; i++) Art.Part(tv, Art.Cylinder, new Color(0.6f, 0.6f, 0.6f), new Vector3(0.32f, 0.68f + i * 0.14f, 0.27f), new Vector3(0.06f / CR, 0.02f / CH, 0.06f / CR), new Vector3(90, 0, 0)); // (knobs)
                foreach (float s in new[] { -1f, 1f }) // (rabbit ears)
                    Art.Box(tv, new Color(0.7f, 0.7f, 0.72f), new Vector3(s * 0.12f, 1.38f, -0.1f), new Vector3(0.015f, 0.5f, 0.015f), new Vector3(0, 0, s * -28f));
                var lg = new GameObject("telly light");
                lg.transform.SetParent(tv, false);
                lg.transform.localPosition = new Vector3(0, 0.9f, 0.5f);
                lg.transform.localRotation = Quaternion.LookRotation(Vector3.forward + Vector3.up * 0.1f);
                m_TvLight = lg.AddComponent<Light>();
                m_TvLight.type = LightType.Spot;
                m_TvLight.spotAngle = 95f;
                m_TvLight.range = 9f;
                m_TvLight.intensity = 3f;
                m_TvLight.shadows = LightShadows.None;
            }

            // ---- the mess: pizza boxes, cans, a laundry pile, a lava lamp, a mini fridge, a dartboard ----
            for (int i = 0; i < 4; i++)
                Art.Box(t, new Color(0.75f, 0.6f, 0.4f) * (0.9f + R() * 0.1f), new Vector3(4.4f + R() * 0.1f, 0.05f + i * 0.07f, 2.6f), new Vector3(0.55f, 0.06f, 0.55f), new Vector3(0, R() * 30f, 0));
            Art.Box(t, new Color(0.85f, 0.2f, 0.15f), new Vector3(4.42f, 0.33f, 2.6f), new Vector3(0.2f, 0.002f, 0.2f), new Vector3(0, 12f, 0)); // (a logo on the top one)
            for (int i = 0; i < 9; i++)
            {
                var can = BeerCan(t).transform;
                float x = R() * 9f - 4.5f;
                bool lying = R() < 0.6f;
                can.localPosition = new Vector3(x, lying ? 0.035f : 0.06f, Mathf.Abs(x) < 2.2f ? -0.4f - R() * 1.5f : R() * 3f - 0.5f);
                can.localRotation = Quaternion.Euler(0f, R() * 360f, lying ? 90f : 0f);
            }
            for (int i = 0; i < 6; i++) // (the laundry pile in the back corner)
                Art.Part(t, Art.Sphere, Color.HSVToRGB(R(), 0.35f, 0.5f), new Vector3(-5.2f + R() * 0.6f, 0.12f + R() * 0.15f, 3f + R() * 0.6f), new Vector3(0.6f + R() * 0.4f, 0.25f + R() * 0.2f, 0.5f + R() * 0.3f) / SR);
            {
                // the lava lamp on a little side table at the couch's end
                var st = new Vector3(-4.6f, 0f, 2.3f);
                Art.Box(t, new Color(0.3f, 0.2f, 0.12f), st + new Vector3(0, 0.27f, 0), new Vector3(0.5f, 0.06f, 0.5f));
                Art.Box(t, new Color(0.25f, 0.17f, 0.1f), st + new Vector3(0, 0.12f, 0), new Vector3(0.08f, 0.26f, 0.08f));
                Art.Part(t, Art.Cylinder, metal, st + new Vector3(0, 0.36f, 0), new Vector3(0.14f / CR, 0.12f / CH, 0.14f / CR));
                Art.Part(t, Art.Sphere, Color.white, st + new Vector3(0, 0.6f, 0), new Vector3(0.14f, 0.38f, 0.14f) / SR, default, false, Unlit(new Color(1f, 0.35f, 0.6f), 1.6f));
                Light(t, st + new Vector3(0, 0.65f, 0), new Color(1f, 0.35f, 0.6f), 1.2f, 3.5f);
            }
            {
                // the mini fridge against the right wall, a magnet or two
                var fr = new Vector3(5.6f, 0f, -0.6f);
                Art.Box(t, new Color(0.8f, 0.8f, 0.76f), fr + new Vector3(0, 0.5f, 0), new Vector3(0.7f, 1f, 0.7f), new Vector3(0, -90f, 0));
                Art.Box(t, new Color(0.55f, 0.55f, 0.52f), fr + new Vector3(-0.36f, 0.62f, -0.2f), new Vector3(0.03f, 0.25f, 0.04f));
                Art.Box(t, new Color(0.9f, 0.3f, 0.2f), fr + new Vector3(-0.36f, 0.85f, 0.15f), new Vector3(0.01f, 0.06f, 0.06f));
            }
            {
                // a dartboard by the window (one dart in the wall, nowhere near it)
                var db = new Vector3(5.2f, 1.7f, WinZ - 0.17f);
                Art.Part(t, Art.Cylinder, new Color(0.1f, 0.1f, 0.1f), db, new Vector3(0.48f / CR, 0.04f / CH, 0.48f / CR), new Vector3(90, 0, 0));
                Art.Part(t, Art.Cylinder, new Color(0.85f, 0.2f, 0.15f), db + new Vector3(0, 0, -0.025f), new Vector3(0.3f / CR, 0.01f / CH, 0.3f / CR), new Vector3(90, 0, 0));
                Art.Part(t, Art.Cylinder, new Color(0.2f, 0.6f, 0.25f), db + new Vector3(0, 0, -0.03f), new Vector3(0.1f / CR, 0.01f / CH, 0.1f / CR), new Vector3(90, 0, 0));
                Art.Box(t, new Color(0.8f, 0.8f, 0.2f), db + new Vector3(0.5f, 0.35f, -0.06f), new Vector3(0.01f, 0.01f, 0.12f));
            }
            // a console by the right wall with blinking lights (the ship's still a ship)
            {
                var con = new GameObject("console").transform;
                con.SetParent(t, false);
                con.localPosition = new Vector3(5.7f, 0, 1.4f);
                con.localRotation = Quaternion.Euler(0, -90f, 0);
                Art.Box(con, metal * 0.7f, new Vector3(0, 0.45f, 0), new Vector3(1.6f, 0.9f, 0.6f));
                Art.Box(con, metal, new Vector3(0, 0.95f, 0.06f), new Vector3(1.6f, 0.1f, 0.75f), new Vector3(-20f, 0, 0));
                for (int i = 0; i < 8; i++)
                {
                    var b = Art.Box(con, Color.white, new Vector3(-0.66f + i * 0.19f, 1.02f, 0.03f + (i % 2) * 0.16f), new Vector3(0.1f, 0.04f, 0.1f), new Vector3(-20f, 0, 0));
                    m_Blinkers.Add(b.GetComponent<Renderer>());
                }
            }
            m_BlinkOn.Add(Workbench.Glow(cyan, 2f));
            m_BlinkOn.Add(Workbench.Glow(new Color(1f, 0.35f, 0.3f), 2f));
            m_BlinkOn.Add(Workbench.Glow(new Color(0.6f, 1f, 0.3f), 2f));
            m_BlinkOff = Art.Mat(new Color(0.1f, 0.12f, 0.15f));

            // ---- the ceiling: low, panels, a cable or two hanging, a warm lamp over the couch, a dying strip light ----
            Art.Box(t, wallDark, new Vector3(0, RoomH + 0.15f, 0), new Vector3(15f, 0.3f, 13f));
            for (int i = -2; i <= 2; i++) Art.Box(t, trim * 0.6f, new Vector3(i * 2.6f, RoomH - 0.04f, 0), new Vector3(0.15f, 0.1f, 12.5f));
            Art.Part(t, Art.Cylinder, pipe, new Vector3(1.4f, RoomH - 0.25f, 0), new Vector3(0.14f / CR, 13f / CH, 0.14f / CR), new Vector3(90f, 0, 0));
            Art.Box(t, new Color(0.08f, 0.08f, 0.08f), new Vector3(-2.2f, RoomH - 0.45f, -1.5f), new Vector3(0.02f, 0.9f, 0.02f), new Vector3(0, 0, 10f)); // (a dangling cable)
            Art.Box(t, new Color(0.08f, 0.08f, 0.08f), new Vector3(0, RoomH - 0.4f, 1.3f), new Vector3(0.02f, 0.8f, 0.02f));
            Art.Part(t, Art.Cone, new Color(0.45f, 0.4f, 0.3f), new Vector3(0, RoomH - 0.95f, 1.3f), new Vector3(0.9f, 0.35f, 0.9f));
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, RoomH - 0.92f, 1.3f), Vector3.one * (0.14f / SR), default, false, Workbench.Glow(warm, 3f));
            {
                var tube = new Vector3(3.6f, RoomH - 0.08f, -1.8f);
                Art.Box(t, metal, tube + new Vector3(0, 0.03f, 0), new Vector3(0.25f, 0.05f, 1.6f));
                m_StripOn = Workbench.Glow(new Color(0.85f, 1f, 0.9f), 2.4f);
                m_StripOff = Art.Mat(new Color(0.5f, 0.55f, 0.52f));
                m_StripTube = Art.Part(t, Art.Cylinder, Color.white, tube, new Vector3(0.07f / CR, 1.5f / CH, 0.07f / CR), new Vector3(90f, 0, 0), false, m_StripOn).GetComponent<Renderer>();
                m_Strip = Light(t, tube + Vector3.down * 0.3f, new Color(0.8f, 1f, 0.9f), 1.6f, 6f);
            }

            // ---- lights: the warm lamp over them, the telly (above), cool light in through the window, a dim fill ----
            Light(t, new Vector3(0, RoomH - 1.05f, 1.2f), warm, 3f, 6f);
            Light(t, new Vector3(0, WinY + 0.6f, WinZ - 1.4f), new Color(0.55f, 0.7f, 1f), 1f, 5f);
            Light(t, new Vector3(0, 2.2f, -4.5f), new Color(0.6f, 0.62f, 0.75f), 1.1f, 10f);
            m_PassLight = Light(t, new Vector3(9f, WinY, WinZ + 1.5f), new Color(0.7f, 0.85f, 1f), 0f, 12f);
            m_PassLight.enabled = false;
            foreach (var r in m_Room.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The back wall with a long window in it, rounded at both ends (a stadium shape): the wall built round
        /// it, a thick bevelled frame following the curve with rivets, a faint glass pane, little lights along the sill -
        /// and outside, black space with the stars streaking past at different depths and a planet drifting by.</summary>
        void BuildWindowWall(Transform t, Color wallC, Color trim, Color metal, Color cyan)
        {
            float r = WinHalfY, straight = WinHalfX - r, wb = WinY - r, wt = WinY + r;
            // the wall round the opening's bounding box
            Art.Box(t, wallC, new Vector3(0, wb * 0.5f, WinZ), new Vector3(15f, wb, 0.3f));
            Art.Box(t, wallC, new Vector3(0, (wt + RoomH + 0.4f) * 0.5f, WinZ), new Vector3(15f, RoomH + 0.4f - wt, 0.3f));
            foreach (float s in new[] { -1f, 1f })
                Art.Box(t, wallC, new Vector3(s * (WinHalfX + 7.5f) * 0.5f, WinY, WinZ), new Vector3(7.5f - WinHalfX, r * 2f, 0.3f));
            // ...and in its corners, round the rounded ends (thin slices from the curve out to the box)
            const int Slices = 14;
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < Slices; k++)
                {
                    float x0 = k * r / Slices, x1 = (k + 1) * r / Slices;
                    float hy = Mathf.Sqrt(Mathf.Max(0f, r * r - x1 * x1)); // (the outer edge of the slice: no gaps)
                    float hgt = r - hy;
                    if (hgt < 0.002f) continue;
                    float cx = s * (straight + (x0 + x1) * 0.5f);
                    Art.Box(t, wallC, new Vector3(cx, WinY + hy + hgt * 0.5f, WinZ), new Vector3(x1 - x0 + 0.01f, hgt + 0.01f, 0.3f));
                    Art.Box(t, wallC, new Vector3(cx, WinY - hy - hgt * 0.5f, WinZ), new Vector3(x1 - x0 + 0.01f, hgt + 0.01f, 0.3f));
                }
            // the frame: a thick rounded rim along the whole outline, a darker inner lip, rivets
            Vector3 Outline(float u, float grow)
            {
                // u 0..1 round the stadium: top straight, right curve, bottom straight, left curve
                float len = 4f * straight + 2f * Mathf.PI * r, d = u * len, rr = r + grow;
                if (d < 2f * straight) return new Vector3(-straight + d, WinY + rr, 0);
                d -= 2f * straight;
                if (d < Mathf.PI * r) { float a = d / r; return new Vector3(straight + Mathf.Sin(a) * rr, WinY + Mathf.Cos(a) * rr, 0); }
                d -= Mathf.PI * r;
                if (d < 2f * straight) return new Vector3(straight - d, WinY - rr, 0);
                d -= 2f * straight;
                { float a = d / r; return new Vector3(-straight - Mathf.Sin(a) * rr, WinY - Mathf.Cos(a) * rr, 0); }
            }
            const int N = 96;
            for (int i = 0; i < N; i++)
            {
                var a = Outline(i / (float)N, 0.12f);
                var b = Outline((i + 1) / (float)N, 0.12f);
                var mid = (a + b) * 0.5f;
                float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                float seg = Vector3.Distance(a, b) + 0.02f;
                Art.Box(t, trim, new Vector3(mid.x, mid.y, WinZ - 0.2f), new Vector3(seg, 0.24f, 0.18f), new Vector3(0, 0, ang));
                var ai = Outline(i / (float)N, 0.0f);
                var bi = Outline((i + 1) / (float)N, 0.0f);
                var mi = (ai + bi) * 0.5f;
                Art.Box(t, metal * 0.7f, new Vector3(mi.x, mi.y, WinZ - 0.05f), new Vector3(Vector3.Distance(ai, bi) + 0.02f, 0.06f, 0.32f), new Vector3(0, 0, Mathf.Atan2(bi.y - ai.y, bi.x - ai.x) * Mathf.Rad2Deg));
                if (i % 6 == 0) Art.Part(t, Art.Sphere, metal * 1.3f, new Vector3(mid.x, mid.y, WinZ - 0.3f), Vector3.one * (0.06f / SR));
            }
            // the glass: barely there, a scratch or two
            var glass = Art.Ghost(new Color(0.6f, 0.8f, 1f, 0.05f));
            Art.Box(t, Color.white, new Vector3(0, WinY, WinZ), new Vector3(straight * 2f, r * 2f, 0.02f), default, false, glass);
            foreach (float s in new[] { -1f, 1f })
                Art.Part(t, Art.Cylinder, Color.white, new Vector3(s * straight, WinY, WinZ), new Vector3(r * 2f / CR, 0.02f / CH, r * 2f / CR), new Vector3(90, 0, 0), false, glass);
            Art.Box(t, Color.white, new Vector3(-1.1f, WinY + 0.2f, WinZ - 0.02f), new Vector3(0.7f, 0.008f, 0.005f), new Vector3(0, 0, 18f), false, Art.Ghost(new Color(1f, 1f, 1f, 0.18f)));
            // little lights along the sill
            for (int i = 0; i < 12; i++)
                Art.Box(t, Color.white, new Vector3(-straight + i * 2f * straight / 11f, wb - 0.16f, WinZ - 0.34f), new Vector3(0.08f, 0.03f, 0.04f), default, false, Workbench.Glow(cyan, 2f));
            // outside: black space, the streaking stars at different depths (parallax: the near ones whip past), a planet
            var space = new GameObject("space").transform;
            space.SetParent(t, false);
            Art.Box(space, Color.black, new Vector3(0, WinY, 40f), new Vector3(160f, 80f, 0.5f), default, false, Art.Mat(new Color(0.008f, 0.006f, 0.02f)));
            var rng = new System.Random(7);
            var starMat = Workbench.Glow(Color.white, 2.4f);
            var blue = Workbench.Glow(new Color(0.7f, 0.85f, 1f), 2.2f);
            for (int i = 0; i < 220; i++)
            {
                float z = 5f + (float)rng.NextDouble() * 30f;
                float near = Mathf.InverseLerp(35f, 5f, z);
                float speed = Mathf.Lerp(6f, 60f, near * near); // (fast: it's going flat out)
                float spread = (z + 2f) / (WinZ + 2f);
                float span = WinHalfX * (z + 2f) / (WinZ + 2f) + 4f;
                var pos = new Vector3(((float)rng.NextDouble() * 2f - 1f) * span, WinY + ((float)rng.NextDouble() * 2f - 1f) * WinHalfY * spread * 1.2f, z);
                float len = 0.08f + speed * 0.04f, th = 0.02f + near * 0.03f;
                var s = Art.Box(space, Color.white, pos, new Vector3(len, th, th), default, false, rng.NextDouble() < 0.25 ? blue : starMat).transform;
                m_Stars.Add(s);
                m_StarSpeed.Add(speed);
                m_StarSpan.Add(span);
            }
            m_Planet = Art.Part(space, Art.Sphere, Color.white, new Vector3(0, WinY - 2.5f, 36f), Vector3.one * (6f / SR), default, false, Workbench.Glow(new Color(0.9f, 0.45f, 0.3f), 0.9f)).transform;
            Art.Part(m_Planet, Art.Sphere, Color.white, new Vector3(0.05f, 0.1f, -0.02f), Vector3.one * 0.98f, default, false, Workbench.Glow(new Color(1f, 0.6f, 0.4f), 0.7f));
        }

        /// <summary>An unlit glowing material (the same bright colour whatever the light).</summary>
        static Material Unlit(Color c, float intensity)
        {
            var sh = Resources.Load<Shader>("World/Glow");
            if (sh == null || !sh.isSupported) return Workbench.Glow(c, intensity);
            var m = new Material(sh) { name = "lobby glow" };
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            return m;
        }

        static Light Light(Transform t, Vector3 at, Color c, float intensity, float range)
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
            return l;
        }
    }
}
