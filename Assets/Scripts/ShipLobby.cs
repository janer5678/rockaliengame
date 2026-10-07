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
    /// Spectators (Spectator.Lobby.cs) sit on the end of the couch too, grey. The telly shows a tiny pixel version of
    /// the game (LobbyArcade.cs): click it and the camera moves in on it to play; whoever's playing holds a controller and
    /// watches the screen. Now and then the aliens look round at each other. The room's colours and lights are in
    /// Settings > Display > SHIP LOBBY and LOBBY LOOK (LobbyLooks.cs). More of the room: ShipLobby.Dressing.cs.
    /// </summary>
    [DefaultExecutionOrder(1000)] // (after PlayerController: the lobby has the camera)
    public partial class ShipLobby : MonoBehaviour
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
        Light m_TvLight, m_Strip, m_PassLight, m_LampLight, m_WinLight, m_FillLight;
        Renderer m_StripTube;
        Transform m_Lamp;
        float m_LampKick;
        Light m_Sun;
        float m_SunWas = -1f, m_AmbientWas = -1f;
        Material m_StripOn, m_StripOff;

        // the camera: where it is and where it looks (eased), and the look round the room (mouse at the screen's edges)
        Vector3 m_CamPos;
        Quaternion m_CamRot = Quaternion.identity;
        bool m_CamSet, m_WasFocused;
        float m_Yaw, m_Pitch;

        class Puff { public Transform T; public float Born = -99f; public Vector3 Vel; public float Size; }

        class Avatar
        {
            public PlayerNet P;               // (null: a spectator, sat there grey)
            public ulong Id;
            public Transform Root, Seat, ArmL, ArmR;
            public BodyAnimator Anim;
            public readonly List<Material> Mats = new List<Material>();
            public int Team = -1, Variant, HatShown = -1;
            public float Seed, NextPuff;
            public Vector3 SeatAt;
            public GameObject Hat;
            public Transform Prop;            // cigarette / can / phone
            public Transform Pad;             // the controller (playing the telly's game)
            public Renderer Tip;              // the cigarette's tip
            public readonly List<Puff> Puffs = new List<Puff>();
            public bool Gaming;
            // looking round at a neighbour now and then (or at the telly, playing)
            public Avatar LookAt;
            public float LookUntil, NextLook = -1f, Turn, TurnYaw;
        }
        readonly Dictionary<PlayerNet, Avatar> m_Avatars = new Dictionary<PlayerNet, Avatar>();
        /// <summary>The spectators' grey aliens, by client id.</summary>
        readonly Dictionary<ulong, Avatar> m_Ghosts = new Dictionary<ulong, Avatar>();
        static readonly List<PlayerNet> s_Order = new List<PlayerNet>();
        static readonly List<PlayerNet> s_Gone = new List<PlayerNet>();
        static readonly List<ulong> s_GoneIds = new List<ulong>();
        static readonly List<SpectatorEntry> s_Specs = new List<SpectatorEntry>();
        /// <summary>Everyone on the couch in seat order (the players, then the spectators).</summary>
        static readonly List<Avatar> s_Seats = new List<Avatar>();
        /// <summary>Who does what, by client id: the fun ones first (smoking, a beer, the phone), then the rest.</summary>
        static readonly int[] k_Variants = { 4, 5, 6, 3, 2, 0, 1 };

        Material m_TipOff, m_TipOn, m_Smoke;

        /// <summary>The lobby's up: this PC is a player in a match that hasn't started (and isn't solo).</summary>
        public static bool Active
        {
            get
            {
                var g = NetGame.Instance;
                return g != null && g.IsSpawned && g.S == GameState.Waiting && NetGame.ReadyLobby && (PlayerNet.Local != null || Spectator.Active); // (spectators wait in it too)
            }
        }

        /// <summary>CUSTOMISE ALIEN is open (Hud.Lobby.cs): the camera's on your alien's head and it sits still.</summary>
        public static bool Customising;

        /// <summary>Where a player's head is on screen in the lobby (for their name tag), and whether it's in view.</summary>
        public static bool HeadOnScreen(PlayerNet p, out Vector2 gui)
        {
            gui = default;
            return s_I != null && p != null && s_I.m_Avatars.TryGetValue(p, out var a) && HeadGui(a, out gui);
        }

        /// <summary>Where a spectator's grey alien's head is on screen (their name tag).</summary>
        public static bool SpectatorHeadOnScreen(ulong id, out Vector2 gui)
        {
            gui = default;
            return s_I != null && s_I.m_Ghosts.TryGetValue(id, out var a) && HeadGui(a, out gui);
        }

        static bool HeadGui(Avatar a, out Vector2 gui)
        {
            gui = default;
            if (a == null || a.Root == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            var head = a.Anim != null && a.Anim.HeadBone != null ? a.Anim.HeadBone.position + Vector3.up * (a.Hat != null ? 0.5f : 0.3f) : a.Root.position + Vector3.up * 1.35f;
            var sp = cam.WorldToScreenPoint(head); // (just over the head)
            if (sp.z <= 0f) return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        /// <summary>The players in the lobby, in their seat order (by team, then slot).</summary>
        public static IReadOnlyList<PlayerNet> Seated => s_Order;
        /// <summary>The spectators sat (grey) on the end of the couch, after the players.</summary>
        public static IReadOnlyList<SpectatorEntry> SeatedSpectators => s_Specs;
        /// <summary>(tests) how many grey spectator aliens are on the couch, and whether they're all grey.</summary>
        public static int GreyAliens => s_I != null ? s_I.m_Ghosts.Count : 0;
        public static bool GhostsGrey
        {
            get
            {
                if (s_I == null) return false;
                foreach (var a in s_I.m_Ghosts.Values) if (a.Team != GreyTeam) return false;
                return true;
            }
        }
        /// <summary>(tests) this player's alien is playing the telly's game (a controller in its hands, eyes on the screen).</summary>
        public static bool IsGaming(PlayerNet p) => s_I != null && p != null && s_I.m_Avatars.TryGetValue(p, out var a) && a.Gaming && a.Pad != null && a.Pad.gameObject.activeSelf;
        /// <summary>(tests) how far the camera is from the telly's screen (m).</summary>
        public static float CamToTelly => s_I != null && s_I.m_Tv != null && Camera.main != null ? Vector3.Distance(Camera.main.transform.position, s_I.m_Tv.TransformPoint(k_Screen)) : 99f;
        /// <summary>(tests) click the telly this frame (as if the mouse were on it).</summary>
        public static bool TestClickTv;
        /// <summary>(tests) any two aliens looking at each other right now.</summary>
        public static bool AnyLooking
        {
            get
            {
                foreach (var a in s_Seats) if (a.LookAt != null && Time.time < a.LookUntil && a.Turn > 0.3f) return true;
                return false;
            }
        }
        /// <summary>(tests) the camera close on the seated player with this index (-1: the usual shot).</summary>
        public static int TestFocus = -1;
        /// <summary>(tests) everyone at this point of their animation (-1: their own time).</summary>
        public static float TestPhase = -1f;
        /// <summary>(tests) how far the camera has turned to look round the room (degrees).</summary>
        public static float LookYaw => s_I != null ? s_I.m_Yaw : 0f;
        /// <summary>(tests) how far the camera has tilted up or down (always 0: the lobby only looks left and right).</summary>
        public static float LookPitch => s_I != null ? s_I.m_Pitch : 0f;
        /// <summary>(tests) every seated alien has an idle of its own (none the same, up to seven).</summary>
        public static bool IdlesAllDifferent
        {
            get
            {
                if (s_I == null) return false;
                var seen = new HashSet<int>();
                foreach (var a in s_I.m_Avatars.Values) if (!seen.Add(a.Variant) && s_I.m_Avatars.Count <= k_Variants.Length) return false;
                return true;
            }
        }
        /// <summary>(tests) any seated alien is wearing a hat.</summary>
        public static bool AnyHat
        {
            get
            {
                if (s_I == null) return false;
                foreach (var a in s_I.m_Avatars.Values) if (a.Hat != null) return true;
                return false;
            }
        }
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
            LobbyArcade.Ensure(host); // (the telly's game)
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
            Dim(true);
            ApplyTones();
            SyncAvatars();
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            var me = PlayerNet.Local;
            ClickTelly();
            Looks(t, dt);
            foreach (var a in s_Seats) TickAvatar(a, me, t);
            AnimateRoom(t, dt);
            DriveCamera(t, dt, me);
        }

        void TickAvatar(Avatar a, PlayerNet me, float t)
        {
            if (a.Anim == null) return;
            bool still = Customising && a.P != null && a.P == me; // (customising: sat still, nothing in the hands)
            bool gaming = !still && a.P != null && LobbyArcade.InGame(a.P);
            a.Gaming = gaming;
            float lt = still ? 0.6f : TestPhase >= 0f ? TestPhase : t + a.Seed;
            // the head turned to a neighbour or the telly (not mid-drag or mid-swig: that's tipped back)
            float busy = gaming ? 0f : a.Variant == 4 ? BodyAnimator.Drag(lt) : a.Variant == 5 ? BodyAnimator.Swig(lt) : 0f;
            a.Anim.TurnYaw = a.TurnYaw;
            a.Anim.TurnW = still ? 0f : a.Turn * (1f - busy);
            a.Anim.Lounge(still ? 0 : gaming ? BodyAnimator.LoungeGaming : a.Variant, lt);
            FitSeat(a);
            if (a.Prop) a.Prop.gameObject.SetActive(!still && !gaming);
            if (gaming) PlacePad(a);
            else if (a.Pad) a.Pad.gameObject.SetActive(false);
            if (!still && !gaming)
            {
                Reach(a, lt); // (the hand to the mouth on a drag or a swig)
                PlaceProp(a, lt, t);
            }
            Smoke(a, t);
            // (hats are off for now: Cosmetics.HatsOn)
            int hat = Cosmetics.HatsOn && a.P != null ? a.P.Hat.Value : 0;
            if (a.HatShown != hat)
            {
                a.HatShown = hat;
                if (a.Hat) Destroy(a.Hat);
                a.Hat = Cosmetics.Wear(a.HatShown, a.Root, a.Anim);
            }
        }

        /// <summary>Now and then an alien looks round at the one next to them for a bit (and often they look back);
        /// whoever's playing the telly's game keeps their eyes on the screen.</summary>
        void Looks(float t, float dt)
        {
            for (int i = 0; i < s_Seats.Count; i++)
            {
                var a = s_Seats[i];
                if (a.Anim == null) continue;
                Vector3? at = null;
                if (a.Gaming && m_Tv != null) at = m_Tv.TransformPoint(k_Screen);
                else if (TestPhase < 0f && a.LookAt != null && t < a.LookUntil && a.LookAt.Anim != null && a.LookAt.Anim.HeadBone != null)
                    at = a.LookAt.Anim.HeadBone.position;
                else if (TestPhase < 0f)
                {
                    a.LookAt = null;
                    if (a.NextLook < 0f) a.NextLook = t + Random.Range(2f, 9f);
                    if (t >= a.NextLook)
                    {
                        a.NextLook = t + Random.Range(5f, 12f);
                        int j = Random.value < 0.5f ? i - 1 : i + 1;
                        if (j < 0 || j >= s_Seats.Count) j = i + (i == 0 ? 1 : -1);
                        if (j >= 0 && j < s_Seats.Count && j != i && Random.value < 0.75f)
                        {
                            var b = s_Seats[j];
                            a.LookAt = b;
                            a.LookUntil = t + Random.Range(1.6f, 3.4f);
                            // ...and often they turn and look back
                            if (!b.Gaming && (b.LookAt == null || t >= b.LookUntil) && Random.value < 0.7f)
                            {
                                b.LookAt = a;
                                b.LookUntil = a.LookUntil - 0.3f;
                                b.NextLook = Mathf.Max(b.NextLook, a.LookUntil + Random.Range(2f, 5f));
                            }
                        }
                    }
                }
                if (at.HasValue && a.Anim.HeadBone != null)
                {
                    var local = Quaternion.Inverse(a.Root.rotation) * (at.Value - a.Anim.HeadBone.position);
                    float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80f, 80f);
                    a.TurnYaw = a.Turn < 0.05f ? yaw : Mathf.LerpAngle(a.TurnYaw, yaw, 1f - Mathf.Exp(-8f * dt));
                }
                a.Turn = Mathf.MoveTowards(a.Turn, at.HasValue ? 1f : 0f, dt * (a.Gaming ? 3f : 2.2f));
            }
        }

        /// <summary>Clicking the telly (or Esc to leave it): the camera moves in on it and you play its game (LobbyArcade).</summary>
        void ClickTelly()
        {
            if (LobbyArcade.Focused)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && !Chat.Open) LobbyArcade.Focus(false);
                TestClickTv = false;
                return;
            }
            bool test = TestClickTv;
            TestClickTv = false;
            if (m_Tv == null || Customising) return;
            if (!test && !(Input.GetMouseButtonDown(0) && Application.isFocused && !Hud.MouseOverUI && !Chat.Open)) return;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = test ? new Ray(cam.transform.position, m_Tv.TransformPoint(k_Screen) - cam.transform.position) : cam.ScreenPointToRay(Input.mousePosition);
            var local = new Ray(m_Tv.InverseTransformPoint(ray.origin), m_Tv.InverseTransformDirection(ray.direction));
            if (k_TvBounds.IntersectRay(local)) LobbyArcade.Focus(true);
        }

        void Clear()
        {
            Dim(false);
            foreach (var a in m_Avatars.Values) DestroyAvatar(a);
            foreach (var a in m_Ghosts.Values) DestroyAvatar(a);
            m_Avatars.Clear();
            m_Ghosts.Clear();
            s_Order.Clear();
            s_Specs.Clear();
            s_Seats.Clear();
            foreach (var tm in m_Tones) if (tm.M) Destroy(tm.M);
            m_Tones.Clear();
            m_TonesSet = false;
            m_Tv = null;
            if (m_Graffiti) Destroy(m_Graffiti);
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
            float fov = GameSettings.LobbyFovNow; // (Settings > Display > SHIP LOBBY)
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
            else if (LobbyArcade.Focused && m_Tv != null)
            {
                // the telly's game: the screen fills the middle of the view, a bit of the set and the speakers round it
                var scr = m_Tv.TransformPoint(k_Screen);
                pos = scr + m_Tv.forward * 0.6f + Vector3.up * 0.03f; // (close: the picture fills most of the view)
                look = scr - Vector3.up * 0.01f;
                fov = 42f;
                m_Yaw = Mathf.Lerp(m_Yaw, 0f, 1f - Mathf.Exp(-6f * dt));
                m_Pitch = 0f;
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
                m_Yaw = Mathf.Clamp(m_Yaw + ex * Mathf.Abs(ex) * 40f * dt, -32f, 32f);
                m_Pitch = 0f; // (only left and right: no looking up and down in the lobby)
            }
            var rot = Quaternion.LookRotation(look - pos);
            rot = Quaternion.Euler(0f, m_Yaw, 0f) * rot * Quaternion.Euler(m_Pitch, 0f, 0f);
            // the ship's going flat out: the room hums and rattles (and jolts now and then)
            float jolt = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.9f)), 40f);
            var rattle = new Vector3(Mathf.PerlinNoise(t * 17f, 1.1f) - 0.5f, Mathf.PerlinNoise(t * 19f, 4.2f) - 0.5f, Mathf.PerlinNoise(t * 13f, 8.3f) - 0.5f) * (1f + jolt * 4f);
            float shake = LobbyArcade.Focused ? 0.35f : 1f; // (gentler on the telly: the picture has to stay readable)
            pos += rattle * 0.012f * shake;
            rot *= Quaternion.Euler(rattle.y * 0.35f * shake, rattle.x * 0.35f * shake, rattle.z * 0.5f * shake);
            // eased (a smooth move into and out of the close-ups)
            if (!m_CamSet) { m_CamPos = pos; m_CamRot = rot; m_CamSet = true; cam.fieldOfView = fov; }
            float k = 1f - Mathf.Exp(-(Customising ? 5f : LobbyArcade.Focused || m_WasFocused ? 4f : 8f) * dt);
            if (LobbyArcade.Focused) m_WasFocused = true;
            else if (Vector3.Distance(m_CamPos, pos) < 0.2f) m_WasFocused = false; // (eased all the way back from the telly)
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
            foreach (var p in s_Gone) { DestroyAvatar(m_Avatars[p]); m_Avatars.Remove(p); }
            s_Order.Clear();
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) s_Order.Add(p);
            s_Order.Sort((a, b) => a.Team.Value != b.Team.Value ? a.Team.Value.CompareTo(b.Team.Value) : a.Slot.Value != b.Slot.Value ? a.Slot.Value.CompareTo(b.Slot.Value) : a.OwnerClientId.CompareTo(b.OwnerClientId));
            // the spectators: grey, on the end of the couch after the players
            s_Specs.Clear();
            var g = NetGame.Instance;
            if (g != null && g.IsSpawned) foreach (var s in g.Spectators) s_Specs.Add(s);
            s_GoneIds.Clear();
            foreach (var kv in m_Ghosts)
            {
                bool still = false;
                foreach (var s in s_Specs) if (s.Id == kv.Key) { still = true; break; }
                if (!still) s_GoneIds.Add(kv.Key);
            }
            foreach (var id in s_GoneIds) { DestroyAvatar(m_Ghosts[id]); m_Ghosts.Remove(id); }
            int np = s_Order.Count, n = np + s_Specs.Count;
            int rows = n > 8 ? 2 : 1, perRow = Mathf.CeilToInt(n / (float)rows);
            s_Seats.Clear();
            for (int i = 0; i < n; i++)
            {
                // each seat its own idle (no two the same, up to seven of them): someone moving seat gets the new one's
                int variant = k_Variants[i % k_Variants.Length];
                Avatar a;
                if (i < np)
                {
                    var p = s_Order[i];
                    if (m_Avatars.TryGetValue(p, out var old) && old.Variant != variant) { DestroyAvatar(old); m_Avatars.Remove(p); }
                    if (!m_Avatars.TryGetValue(p, out a)) m_Avatars[p] = a = MakeAvatar(p, p.OwnerClientId, p.DisplayName, variant);
                    if (a.Team != p.Team.Value) Tint(a, p.Team.Value);
                }
                else
                {
                    var s = s_Specs[i - np];
                    if (m_Ghosts.TryGetValue(s.Id, out var old) && old.Variant != variant) { DestroyAvatar(old); m_Ghosts.Remove(s.Id); }
                    if (!m_Ghosts.TryGetValue(s.Id, out a)) { m_Ghosts[s.Id] = a = MakeAvatar(null, s.Id, s.Name.ToString(), variant); Grey(a); }
                }
                s_Seats.Add(a);
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
            }
        }

        void DestroyAvatar(Avatar a)
        {
            if (a == null) return;
            if (a.Root) Destroy(a.Root.gameObject);
            foreach (var pf in a.Puffs) if (pf.T) Destroy(pf.T.gameObject); // (the smoke hangs in the room, not on them)
            a.Puffs.Clear();
            foreach (var m in a.Mats) if (m) Destroy(m);
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

        Avatar MakeAvatar(PlayerNet p, ulong id, string name, int variant)
        {
            var a = new Avatar { P = p, Id = id, Seed = (id * 7.31f) % 50f, Variant = variant };
            var go = new GameObject("lobby alien " + name);
            go.transform.SetParent(m_Room.transform, true);
            a.Root = go.transform;
            a.Anim = BodyAnimator.TryCreate(a.Root, Cfg.ModelWidth, out var model);
            if (model != null)
            {
                PlayerNet.SkinAlien(model, a.Mats);
                SmoothShadeHook.Add(model, true);
            }
            // their stretch of the couch: a battered couch (mustard as built: Settings > Display > SHIP LOBBY > Couch),
            // worn and stained (the cushion sinks to the hips: FitSeat)
            var fabric = Color.white;
            var wood = new Color(0.3f, 0.2f, 0.12f);
            var couch = new GameObject("couch").transform;
            couch.SetParent(a.Root, false);
            Art.Box(couch, wood, new Vector3(0f, 0.12f, -0.12f), new Vector3(1.3f, 0.2f, 0.92f));                       // the base
            Art.Box(couch, fabric, new Vector3(0f, 0.27f, -0.12f), new Vector3(1.28f, 0.14f, 0.9f), default, false, Tone(Tones.Couch, 0.8f)); // the frame under the cushion
            Art.Box(couch, fabric, new Vector3(0f, 0.72f, -0.53f), new Vector3(1.3f, 0.86f, 0.2f), new Vector3(-10f, 0f, 0f), false, Tone(Tones.Couch, 0.8f)); // the back
            Art.Box(couch, fabric, new Vector3(0f, 0.74f, -0.42f), new Vector3(1.16f, 0.62f, 0.14f), new Vector3(-10f, 0f, 0f), false, Tone(Tones.Couch, 0.92f)); // the back cushion
            Art.Box(couch, fabric, new Vector3(0.2f, 0.6f, -0.34f), new Vector3(0.3f, 0.2f, 0.02f), new Vector3(-10f, 0f, 14f), false, Tone(Tones.Couch, 0.6f));   // (a stain)
            for (int s = -1; s <= 1; s += 2)
                Art.Box(couch, wood * 0.7f, new Vector3(s * 0.55f, 0.02f, 0.25f), new Vector3(0.08f, 0.04f, 0.08f));       // (stubby feet)
            a.Seat = new GameObject("cushion").transform;
            a.Seat.SetParent(couch, false);
            Art.Box(a.Seat, fabric, new Vector3(0f, 0f, -0.08f), new Vector3(1.22f, 0.16f, 0.78f), default, false, Tone(Tones.Couch, 1f));
            Art.Box(a.Seat, fabric, new Vector3(0f, -0.005f, 0.31f), new Vector3(1.22f, 0.15f, 0.04f), default, false, Tone(Tones.Couch, 0.85f)); // (the cushion's front seam)
            if (id % 3 == 1) Art.Box(a.Seat, new Color(0.3f, 0.22f, 0.1f), new Vector3(-0.25f, 0.082f, 0f), new Vector3(0.3f, 0.005f, 0.22f), new Vector3(0, 25f, 0)); // (a mystery stain)
            a.ArmL = Art.Box(couch, fabric, new Vector3(-0.7f, 0.45f, -0.12f), new Vector3(0.22f, 0.5f, 0.92f), default, false, Tone(Tones.Couch, 0.8f)).transform;
            a.ArmR = Art.Box(couch, fabric, new Vector3(0.7f, 0.45f, -0.12f), new Vector3(0.22f, 0.5f, 0.92f), default, false, Tone(Tones.Couch, 0.8f)).transform;
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
                    Art.Box(a.Prop, new Color(0.95f, 0.94f, 0.9f), new Vector3(0, 0, 0.05f), new Vector3(0.024f, 0.024f, 0.11f));
                    Art.Box(a.Prop, new Color(0.85f, 0.55f, 0.25f), new Vector3(0, 0, -0.02f), new Vector3(0.026f, 0.026f, 0.04f));
                    a.Tip = Art.Box(a.Prop, Color.white, new Vector3(0, 0, 0.11f), new Vector3(0.027f, 0.027f, 0.015f), default, false, m_TipOff).GetComponent<Renderer>();
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
            Art.Part(can.transform, Art.Cylinder, new Color(0.85f, 0.75f, 0.2f), Vector3.zero, new Vector3(0.095f / CR, 0.17f / CH, 0.095f / CR));
            Art.Part(can.transform, Art.Cylinder, new Color(0.75f, 0.76f, 0.8f), new Vector3(0, 0.088f, 0), new Vector3(0.088f / CR, 0.01f / CH, 0.088f / CR));
            Art.Part(can.transform, Art.Cylinder, new Color(0.15f, 0.35f, 0.75f), new Vector3(0, -0.008f, 0), new Vector3(0.098f / CR, 0.06f / CH, 0.098f / CR));
            return can;
        }

        /// <summary>The cushion sinks or rises under the hips (wherever the pose has put them).</summary>
        static void FitSeat(Avatar a)
        {
            float y = Mathf.Clamp(a.Anim.HipsHeight - 0.16f, 0.26f, 0.6f);
            a.Seat.localPosition = new Vector3(0f, y, 0f);
        }

        /// <summary>How far past the wrist the fingers close round something (the alien's long hand).</summary>
        const float Grip = 0.2f;
        /// <summary>Where the alien's mouth is from its head bone: up and forward (m).</summary>
        public static float MouthUp = 0.015f, MouthFwd = 0.15f;

        /// <summary>The mouth (world), and the head's own forward and up (it tips back on a drag or a swig).</summary>
        static Vector3 Mouth(Avatar a, out Vector3 hfwd, out Vector3 hup)
        {
            hfwd = a.Root.forward; hup = a.Root.up;
            var head = a.Anim.HeadBone;
            if (head == null) return a.Root.position + hup * 1.2f + hfwd * MouthFwd;
            var frame = head.rotation * a.Anim.HeadFrameOffset; // (the head's own frame: as the root's at rest)
            hfwd = frame * Vector3.forward; hup = frame * Vector3.up;
            return head.position + hup * MouthUp + hfwd * MouthFwd;
        }

        /// <summary>Where the smoker's fingers are when the cigarette's at their lips (just in front of the mouth).</summary>
        static Vector3 DragPalm(Vector3 mouth, Vector3 hfwd, Vector3 hup) => mouth + hfwd * 0.085f - hup * 0.02f;
        /// <summary>The can's axis (bottom to top) at the height of a swig: tipped right up, the top at the lips.</summary>
        static Vector3 SwigAxis(Vector3 hfwd, Vector3 up) => (-hfwd - up * 0.3f).normalized;
        static Vector3 SwigCan(Vector3 mouth, Vector3 hfwd, Vector3 up) => mouth + hfwd * 0.02f - SwigAxis(hfwd, up) * 0.085f;

        /// <summary>The smoker and the drinker: the right arm reaches so the hand really gets to the mouth on a drag /
        /// a swig (BodyAnimator.ReachRight), the cigarette or the can still in it.</summary>
        static void Reach(Avatar a, float t)
        {
            if (a.Variant != 4 && a.Variant != 5) return;
            var mouth = Mouth(a, out var hf, out var hu);
            if (a.Variant == 4) a.Anim.ReachRight(DragPalm(mouth, hf, hu), BodyAnimator.Drag(t), Grip);
            else a.Anim.ReachRight(SwigCan(mouth, hf, a.Root.up), BodyAnimator.Swig(t), Grip);
        }

        /// <summary>Playing the telly's game: the controller in both hands, tipped up towards the face.</summary>
        void PlacePad(Avatar a)
        {
            if (a.Pad == null)
            {
                a.Pad = Controller(a.Root, new Color(0.12f, 0.12f, 0.14f), true).transform;
                foreach (var r in a.Pad.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            a.Pad.gameObject.SetActive(true);
            var rh = a.Anim.RightHand; var rf = a.Anim.RightFore; var lh = a.Anim.LeftHand; var lf = a.Anim.LeftFore;
            if (rh == null || rf == null || lh == null || lf == null) return;
            var rp = rh.position + (rh.position - rf.position).normalized * (Grip * 0.7f);
            var lp = lh.position + (lh.position - lf.position).normalized * (Grip * 0.7f);
            var at = (rp + lp) * 0.5f + a.Root.up * 0.015f;
            var across = lp - rp;
            var fwd = Vector3.ProjectOnPlane(a.Root.forward, across.sqrMagnitude > 1e-4f ? across.normalized : a.Root.right);
            if (fwd.sqrMagnitude < 1e-4f) fwd = a.Root.forward;
            a.Pad.SetPositionAndRotation(at, Quaternion.LookRotation(fwd.normalized, a.Root.up) * Quaternion.Euler(-30f, 0f, 0f));
        }

        /// <summary>The prop in the hand(s) this frame (and the cigarette's smoke coming off it).</summary>
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
                    var mouth = Mouth(a, out var hf, out var hu);
                    // held between two fingers, sticking out sideways, a little low in the hand - and on a drag the hand
                    // takes it to the mouth (Reach): the filter between the lips, the lit end pointing out, the fingers
                    // just in front of it. It goes with the hand all the way: the hand gets there as it does.
                    var held = palm + dir * 0.02f - up * 0.035f;
                    var heldRot = Quaternion.LookRotation(-side.normalized, up);
                    var lips = mouth + hf * 0.04f;
                    var follow = lips + (palm - DragPalm(mouth, hf, hu)); // (where the hand has it, relative to the lips)
                    float m = Mathf.SmoothStep(0f, 1f, d);
                    a.Prop.SetPositionAndRotation(Vector3.Lerp(held, Vector3.Lerp(follow, lips, m), m), Quaternion.Slerp(heldRot, Quaternion.LookRotation(hf, hu), m));
                    if (a.Tip) a.Tip.sharedMaterial = d > 0.6f ? m_TipOn : m_TipOff;
                    // breathing the smoke out after the drag, and a thin wisp off the tip the rest of the time
                    float u = Mathf.Repeat(t, 6f);
                    if (now >= a.NextPuff && GameSettings.LobbySmokeNow)
                    {
                        bool exhale = u > 2.1f && u < 3.5f;
                        var from = exhale ? mouth + hf * 0.03f : a.Tip != null ? a.Tip.transform.position : palm;
                        Emit(a, from, exhale ? hf * 0.35f + up * 0.2f : up * 0.18f + fwd * 0.03f, exhale ? 0.035f : 0.012f, now);
                        a.NextPuff = now + (exhale ? 0.12f : 0.55f);
                    }
                    break;
                }
                case 5:
                {
                    // the can stays in the hand; on a swig the hand brings it up (Reach) and it tips right up, the top
                    // at the lips
                    float d = BodyAnimator.Swig(t);
                    var mouth = Mouth(a, out var hf, out _);
                    var axis = Vector3.Slerp(up, SwigAxis(hf, up), Mathf.SmoothStep(0f, 1f, d));
                    a.Prop.SetPositionAndRotation(palm, Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.LookRotation(fwd, up));
                    if (d > 0.5f)
                    {
                        // (and right on the lips at the top of it, wherever the reach fell short)
                        var top = a.Prop.position + axis * 0.088f;
                        a.Prop.position += (mouth + hf * 0.02f - top) * Mathf.SmoothStep(0f, 1f, (d - 0.5f) * 2f);
                    }
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
        }

        /// <summary>The smoke rising, spreading and thinning out.</summary>
        static void Smoke(Avatar a, float now)
        {
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

        /// <summary>A spectator's alien's Team (grey).</summary>
        const int GreyTeam = -2;

        /// <summary>A spectator's alien: grey all over (the same shading as a team colour, without the colour).</summary>
        static void Grey(Avatar a)
        {
            a.Team = GreyTeam;
            var c = new Color(0.5f, 0.5f, 0.52f);
            foreach (var m in a.Mats)
            {
                var t = !m.name.Contains("Alien2") ? Color.Lerp(Color.white, c, 0.75f) : m.name.Contains("Head") ? Color.Lerp(Color.white, c, 0.6f) * 1.3f : c * 1.6f;
                t.a = 1f;
                m.SetColor("_BaseColor", t);
                m.color = t;
            }
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
            // the lamp swings on its cable with the ship's bumps (a jolt sets it going, then it settles)
            if (m_Lamp)
            {
                float jolt = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.9f)), 40f);
                m_LampKick = Mathf.Max(m_LampKick * Mathf.Exp(-0.6f * dt), jolt * 9f);
                float sx = Mathf.Sin(t * 2.1f) * (1.5f + m_LampKick) + (Mathf.PerlinNoise(t * 3f, 2f) - 0.5f) * 2f;
                float sz = Mathf.Sin(t * 1.7f + 1f) * (1.2f + m_LampKick * 0.7f);
                m_Lamp.localRotation = GameSettings.LobbySwayNow ? Quaternion.Euler(sx, 0f, sz) : Quaternion.identity;
            }
            // the room's lights (Settings > Display > SHIP LOBBY can turn each up or down, and the room darker)
            // (darker than ever, the lights stronger: bright pools in the gloom)
            float dark = GameSettings.LobbyDarkNow;
            if (m_LampLight) m_LampLight.intensity = 8.5f * GameSettings.LobbyLampNow;
            if (m_WinLight) m_WinLight.intensity = 1.4f * GameSettings.LobbyWindowNow;
            if (m_FillLight) m_FillLight.intensity = 0.07f / dark;
            Dim(true);
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
            // the telly: its game (LobbyArcade draws the picture) - the light it throws on their faces takes the picture's
            // colour and flickers like an old CRT
            {
                float fl = 0.8f + 0.2f * Mathf.PerlinNoise(t * 24f, 2f);
                var glow = LobbyArcade.Glow;
                float lum = Mathf.Max(0.05f, glow.maxColorComponent);
                if (m_TvLight) { m_TvLight.color = Color.Lerp(glow / lum, new Color(0.7f, 0.8f, 1f), 0.35f); m_TvLight.intensity = 6.5f * fl * GameSettings.LobbyTvNow; }
                if (m_TvMat != null) m_TvMat.SetColor("_Color", Color.Lerp(glow / lum, Color.white, 0.3f) * fl); // (only if the picture's material is missing)
            }
            // the dying strip light in the corner: it stutters on and off
            if (m_Strip)
            {
                float n = Mathf.PerlinNoise(t * 3f, 7f);
                bool on = n > 0.32f || Mathf.PerlinNoise(t * 30f, 1f) > 0.7f;
                m_Strip.enabled = on;
                if (m_StripTube) m_StripTube.sharedMaterial = on ? m_StripOn : m_StripOff;
            }
            // a light sweeps through the room from the window every few seconds: something flashing past outside (we're
            // fast) - each one its own speed, height and colour, now and then two close together
            if (m_PassLight)
            {
                const float Every = 2.6f;
                float cycle = Mathf.Floor(t / Every), u = t - cycle * Every;
                float h1 = Hash(cycle), h2 = Hash(cycle + 17.3f), h3 = Hash(cycle + 41.9f);
                float len = 0.45f + h1 * 0.7f, start = h2 * 0.6f;
                bool pass = h3 < 0.85f && u >= start && u < start + len;
                m_PassLight.enabled = pass;
                if (pass)
                {
                    float k = (u - start) / len;
                    float dirX = h2 < 0.25f ? -1f : 1f; // (mostly going past one way: we're overtaking them)
                    m_PassLight.transform.localPosition = new Vector3(Mathf.Lerp(9f, -9f, dirX > 0 ? k : 1f - k), WinY + (h1 - 0.5f) * 1.2f, WinZ + 1.2f + h3);
                    m_PassLight.color = h1 < 0.6f ? new Color(0.7f, 0.85f, 1f) : h1 < 0.85f ? new Color(1f, 0.7f, 0.45f) : new Color(0.6f, 1f, 0.7f);
                    m_PassLight.intensity = Mathf.Sin(k * Mathf.PI) * 26f * GameSettings.LobbyWindowNow; // (bright: it lights the whole room up as it goes by)
                }
            }
        }

        static float Hash(float x)
        {
            float s = Mathf.Sin(x * 12.9898f + 4.1414f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        // ------------------------------------------------------------------ the room

        void BuildRoom()
        {
            m_Room = new GameObject("ShipLobby");
            m_Room.transform.position = Center;
            var t = m_Room.transform;
            var wallC = new Color(0.3f, 0.28f, 0.24f);       // grimy beige panels, in the gloom
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
            // (the carpet's colour: Settings > Display > SHIP LOBBY > Floor)
            Art.Box(t, carpet, new Vector3(0, 0.004f, 0.3f), new Vector3(12.5f, 0.008f, 8.2f), default, false, Tone(Tones.Floor, 1f));
            for (int i = 0; i < 16; i++) // (stains)
                Art.Part(t, Art.Cylinder, carpet, new Vector3(R() * 10f - 5f, 0.01f + i * 0.0003f, R() * 6.5f - 2.8f), new Vector3((0.3f + R() * 0.9f) / CR, 0.002f, (0.25f + R() * 0.7f) / CR), new Vector3(0, R() * 180f, 0), false, Tone(Tones.Floor, 0.5f + R() * 0.3f));
            Art.Part(t, Art.Cylinder, new Color(0.35f, 0.15f, 0.12f), new Vector3(0, 0.012f, 0.4f), new Vector3(2.6f / CR, 0.004f, 1.7f / CR)); // (a small old round rug in front of the couch)

            // ---- the back wall, round a long rounded window: the stars streak past it ----
            BuildWindowWall(t, wallC, trim, metal, cyan);

            // ---- the side walls (the hull leaning in at the top): grubby panels, pipes, posters ----
            foreach (float side in new[] { -1f, 1f })
            {
                var wall = new GameObject("wall").transform;
                wall.SetParent(t, false);
                wall.localPosition = new Vector3(side * 6.6f, 0f, 0f);
                wall.localRotation = Quaternion.Euler(0, 0, side * 8f);
                WallBox(wall, new Vector3(0, RoomH * 0.5f, 0), new Vector3(0.3f, RoomH + 0.4f, 13f));
                for (int i = -2; i <= 2; i++)
                {
                    Art.Box(wall, trim * 0.8f, new Vector3(-side * 0.17f, RoomH * 0.5f, i * 2.4f), new Vector3(0.08f, RoomH, 0.2f));
                    Art.Box(wall, wallDark, new Vector3(-side * 0.16f, 0.45f, i * 2.4f + 1.2f), new Vector3(0.04f, 0.6f, 2f), default, false, Tone(Tones.Wall, 0.9f)); // (scuffed kick panels)
                    Art.Box(wall, wallC, new Vector3(-side * 0.16f, 1.2f + R() * 1.4f, i * 2.4f + 0.6f + R()), new Vector3(0.03f, 0.3f + R() * 0.5f, 0.4f + R() * 0.6f), default, false, Tone(Tones.Wall, 0.75f + R() * 0.15f)); // (grime)
                    // (stains running down the panels)
                    Art.Box(wall, wallC, new Vector3(-side * 0.158f, 0.9f + R() * 1.2f, i * 2.4f + R() * 2f - 1f), new Vector3(0.02f, 0.5f + R() * 0.8f, 0.08f + R() * 0.2f), default, false, Tone(Tones.Wall, 0.55f + R() * 0.15f));
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
                // the screen: the warm-up game's picture (LobbyArcade.cs) on a quad facing the couch (click it to play)
                m_Tv = tv;
                var arcade = LobbyArcade.ScreenMat;
                m_TvMat = arcade != null ? null : new Material(Unlit(Color.white, 1.6f)) { name = "telly" };
                var scr = new GameObject("telly screen");
                scr.transform.SetParent(tv, false);
                scr.transform.localPosition = k_Screen + new Vector3(0f, 0f, 0.002f);
                scr.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // (a quad faces -z: turned round to face out)
                scr.transform.localScale = new Vector3(0.54f, 0.42f, 1f);
                scr.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                scr.AddComponent<MeshRenderer>().sharedMaterial = arcade != null ? arcade : m_TvMat;
                Art.Box(tv, new Color(0.03f, 0.03f, 0.035f), k_Screen + new Vector3(0f, 0f, -0.004f), new Vector3(0.56f, 0.44f, 0.006f)); // (the tube behind it)
                BuildTellyKit(tv); // (speakers, the console on the floor: ShipLobby.Dressing.cs)
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
            for (int i = 0; i < 6; i++) // (the laundry pile by the left wall)
                Art.Part(t, Art.Sphere, Color.HSVToRGB(R(), 0.35f, 0.5f), new Vector3(-5.85f + R() * 0.5f, 0.12f + R() * 0.15f, 0.4f + R() * 0.6f), new Vector3(0.6f + R() * 0.4f, 0.25f + R() * 0.2f, 0.5f + R() * 0.3f) / SR);
            {
                // the lava lamp on a little side table at the couch's end
                var st = new Vector3(-4.6f, 0f, 2.3f);
                Art.Box(t, new Color(0.3f, 0.2f, 0.12f), st + new Vector3(0, 0.27f, 0), new Vector3(0.5f, 0.06f, 0.5f));
                Art.Box(t, new Color(0.25f, 0.17f, 0.1f), st + new Vector3(0, 0.12f, 0), new Vector3(0.08f, 0.26f, 0.08f));
                Art.Part(t, Art.Cylinder, metal, st + new Vector3(0, 0.36f, 0), new Vector3(0.14f / CR, 0.12f / CH, 0.14f / CR));
                Art.Part(t, Art.Sphere, Color.white, st + new Vector3(0, 0.6f, 0), new Vector3(0.14f, 0.38f, 0.14f) / SR, default, false, Unlit(new Color(1f, 0.35f, 0.6f), 1.6f));
                Light(t, st + new Vector3(0, 0.65f, 0), new Color(1f, 0.35f, 0.6f), 1.9f, 3.8f);
            }
            {
                // the mini fridge against the right wall, a magnet or two
                var fr = new Vector3(5.6f, 0f, -0.6f);
                Art.Box(t, new Color(0.8f, 0.8f, 0.76f), fr + new Vector3(0, 0.5f, 0), new Vector3(0.7f, 1f, 0.7f), new Vector3(0, -90f, 0));
                Art.Box(t, new Color(0.55f, 0.55f, 0.52f), fr + new Vector3(-0.36f, 0.62f, -0.2f), new Vector3(0.03f, 0.25f, 0.04f));
                Art.Box(t, new Color(0.9f, 0.3f, 0.2f), fr + new Vector3(-0.36f, 0.85f, 0.15f), new Vector3(0.01f, 0.06f, 0.06f));
            }
            {
                // a dartboard by the window, over the trade station (one dart in the wall, nowhere near it)
                var db = new Vector3(5.6f, 2.3f, WinZ - 0.17f);
                Art.Part(t, Art.Cylinder, new Color(0.1f, 0.1f, 0.1f), db, new Vector3(0.48f / CR, 0.04f / CH, 0.48f / CR), new Vector3(90, 0, 0));
                Art.Part(t, Art.Cylinder, new Color(0.85f, 0.2f, 0.15f), db + new Vector3(0, 0, -0.025f), new Vector3(0.3f / CR, 0.01f / CH, 0.3f / CR), new Vector3(90, 0, 0));
                Art.Part(t, Art.Cylinder, new Color(0.2f, 0.6f, 0.25f), db + new Vector3(0, 0, -0.03f), new Vector3(0.1f / CR, 0.01f / CH, 0.1f / CR), new Vector3(90, 0, 0));
                Art.Box(t, new Color(0.8f, 0.8f, 0.2f), db + new Vector3(0.5f, 0.35f, -0.06f), new Vector3(0.01f, 0.01f, 0.12f));
            }
            BuildClutter(t, rng);
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
            Art.Box(t, wallDark, new Vector3(0, RoomH + 0.15f, 0), new Vector3(15f, 0.3f, 13f), default, false, Tone(Tones.Wall, 0.9f));
            for (int i = -2; i <= 2; i++) Art.Box(t, trim * 0.6f, new Vector3(i * 2.6f, RoomH - 0.04f, 0), new Vector3(0.15f, 0.1f, 12.5f));
            Art.Part(t, Art.Cylinder, pipe, new Vector3(1.4f, RoomH - 0.25f, 0), new Vector3(0.14f / CR, 13f / CH, 0.14f / CR), new Vector3(90f, 0, 0));
            Art.Box(t, new Color(0.08f, 0.08f, 0.08f), new Vector3(-2.2f, RoomH - 0.45f, -1.5f), new Vector3(0.02f, 0.9f, 0.02f), new Vector3(0, 0, 10f)); // (a dangling cable)
            // the lamp over the couch hangs from its cable on a pivot: it sways with the ship's bumps (AnimateRoom)
            m_Lamp = new GameObject("lamp").transform;
            m_Lamp.SetParent(t, false);
            m_Lamp.localPosition = new Vector3(0, RoomH, 1.3f);
            Art.Box(m_Lamp, new Color(0.08f, 0.08f, 0.08f), new Vector3(0, -0.4f, 0), new Vector3(0.02f, 0.8f, 0.02f));
            Art.Part(m_Lamp, Art.Cone, new Color(0.45f, 0.4f, 0.3f), new Vector3(0, -0.95f, 0), new Vector3(0.9f, 0.35f, 0.9f));
            Art.Part(m_Lamp, Art.Sphere, Color.white, new Vector3(0, -0.92f, 0), Vector3.one * (0.14f / SR), default, false, Workbench.Glow(warm, 3f));
            {
                var tube = new Vector3(3.6f, RoomH - 0.08f, -1.8f);
                Art.Box(t, metal, tube + new Vector3(0, 0.03f, 0), new Vector3(0.25f, 0.05f, 1.6f));
                m_StripOn = Workbench.Glow(new Color(0.85f, 1f, 0.9f), 2.4f);
                m_StripOff = Art.Mat(new Color(0.5f, 0.55f, 0.52f));
                m_StripTube = Art.Part(t, Art.Cylinder, Color.white, tube, new Vector3(0.07f / CR, 1.5f / CH, 0.07f / CR), new Vector3(90f, 0, 0), false, m_StripOn).GetComponent<Renderer>();
                m_Strip = Light(t, tube + Vector3.down * 0.3f, new Color(0.8f, 1f, 0.9f), 2.4f, 6.5f);
            }

            // ---- lights: the warm lamp over them, the telly (above), cool light in through the window, a dim fill ----
            // (very dark otherwise: the lights are bright pools in it - AnimateRoom sets their strength)
            m_LampLight = Light(m_Lamp, new Vector3(0, -1.05f, -0.1f), warm, 8.5f, 7f);
            m_WinLight = Light(t, new Vector3(0, WinY + 0.6f, WinZ - 1.4f), new Color(0.55f, 0.7f, 1f), 1.4f, 5f);
            m_FillLight = Light(t, new Vector3(0, 2.2f, -4.5f), new Color(0.6f, 0.62f, 0.75f), 0.07f, 8f);
            m_PassLight = Light(t, new Vector3(9f, WinY, WinZ + 1.5f), new Color(0.7f, 0.85f, 1f), 0f, 18f);
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
            WallBox(t, new Vector3(0, wb * 0.5f, WinZ), new Vector3(15f, wb, 0.3f));
            WallBox(t, new Vector3(0, (wt + RoomH + 0.4f) * 0.5f, WinZ), new Vector3(15f, RoomH + 0.4f - wt, 0.3f));
            foreach (float s in new[] { -1f, 1f })
                WallBox(t, new Vector3(s * (WinHalfX + 7.5f) * 0.5f, WinY, WinZ), new Vector3(7.5f - WinHalfX, r * 2f, 0.3f));
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
                    WallBox(t, new Vector3(cx, WinY + hy + hgt * 0.5f, WinZ), new Vector3(x1 - x0 + 0.01f, hgt + 0.01f, 0.3f));
                    WallBox(t, new Vector3(cx, WinY - hy - hgt * 0.5f, WinZ), new Vector3(x1 - x0 + 0.01f, hgt + 0.01f, 0.3f));
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

        /// <summary>The room's dark: while it's up the sun and the sky light are turned right down (the room has no
        /// shadows of its own, so they'd light it up through the ceiling) - the telly, the lamp and the window light it.
        /// Put back as it closes.</summary>
        void Dim(bool on)
        {
            if (on)
            {
                if (m_SunWas < 0f)
                {
                    m_Sun = RenderSettings.sun;
                    if (m_Sun == null)
                        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                            if (l.type == LightType.Directional && l.enabled) { m_Sun = l; break; }
                    m_SunWas = m_Sun != null ? m_Sun.intensity : 0f;
                    m_AmbientWas = RenderSettings.ambientIntensity;
                    m_AmbientColWas = RenderSettings.ambientLight;
                }
                // even darker than it was (and Room darkness in the settings on top)
                float dark = GameSettings.LobbyDarkNow;
                if (m_Sun != null) m_Sun.intensity = m_SunWas * 0.012f / dark;
                RenderSettings.ambientIntensity = m_AmbientWas * 0.08f / dark;
                RenderSettings.ambientLight = m_AmbientColWas * (0.08f / dark);
            }
            else
            {
                if (m_SunWas < 0f) return;
                if (m_Sun != null) m_Sun.intensity = m_SunWas;
                RenderSettings.ambientIntensity = m_AmbientWas;
                RenderSettings.ambientLight = m_AmbientColWas;
                m_SunWas = -1f;
            }
        }
        Color m_AmbientColWas;

        /// <summary>The mess on the floor and the walls: bin bags, screwed-up paper, newspapers, a bike against the wall,
        /// the ball, stuff from the game (a hatchet, a spear, a bow, C4, a pickaxe, a chest), and posters all over.</summary>
        void BuildClutter(Transform t, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            // bin bags and screwed-up paper
            foreach (var at in new[] { new Vector3(5.8f, 0f, -1.7f), new Vector3(5.4f, 0f, -2.2f), new Vector3(-5.6f, 0f, -1.6f) })
            {
                Art.Part(t, Art.Sphere, new Color(0.08f, 0.1f, 0.08f), at + Vector3.up * 0.3f, new Vector3(0.6f, 0.6f, 0.55f) / SR, new Vector3(0, R() * 360f, 0));
                Art.Part(t, Art.Cone, new Color(0.06f, 0.08f, 0.06f), at + Vector3.up * 0.58f, new Vector3(0.14f, 0.16f, 0.14f));
            }
            for (int i = 0; i < 14; i++)
                Art.Part(t, Art.Ico, new Color(0.88f, 0.86f, 0.8f), new Vector3(R() * 10f - 5f, 0.05f, R() * 5f - 2.5f), Vector3.one * (0.07f + R() * 0.05f), new Vector3(R() * 360f, R() * 360f, 0));
            // newspapers: a few pages spread out, columns of print on them
            for (int i = 0; i < 4; i++)
            {
                var np = new GameObject("newspaper").transform;
                np.SetParent(t, false);
                np.localPosition = new Vector3(R() * 7f - 3.5f, 0.012f + i * 0.002f, -0.6f - R() * 1.6f);
                np.localRotation = Quaternion.Euler(0, R() * 360f, 0);
                Art.Box(np, new Color(0.82f, 0.8f, 0.74f), Vector3.zero, new Vector3(0.55f, 0.004f, 0.4f));
                Art.Box(np, new Color(0.2f, 0.2f, 0.2f), new Vector3(0, 0.003f, -0.15f), new Vector3(0.45f, 0.002f, 0.05f)); // (the headline)
                for (int c = 0; c < 3; c++)
                    for (int l = 0; l < 6; l++)
                        Art.Box(np, new Color(0.45f, 0.45f, 0.45f), new Vector3(-0.17f + c * 0.17f, 0.003f, -0.08f + l * 0.04f), new Vector3(0.14f, 0.002f, 0.012f));
            }
            // a bike on its stand, back towards the window at the end of the couch (ShipLobby.Dressing.cs)
            BuildBike(t);
            // the ball, and gear from the game lying about
            ItemModels.CreateBall(t, 0.5f).transform.localPosition = new Vector3(3.3f, 0.25f, -1.1f);
            void Lay(Item it, Vector3 at, float yaw, float roll)
            {
                var go = ItemModels.Create(it, t);
                if (go == null) return;
                go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, roll);
            }
            Lay(Item.Hatchet, new Vector3(-1.9f, 0.05f, -1.2f), 30f, 90f);
            Lay(Item.Spear, new Vector3(5.8f, 0f, 2.4f), 0f, 14f);
            Lay(Item.Bow, new Vector3(2.2f, 0.06f, -1.9f), 120f, 90f);
            Lay(Item.C4, new Vector3(-3.7f, 0.02f, 3.3f), 70f, 0f);
            Lay(Item.Pickaxe, new Vector3(1.0f, 0.06f, -2.3f), -40f, 90f);
            Lay(Item.Rock, new Vector3(-0.6f, 0.15f, -1.7f), 10f, 0f);
            var chest = new GameObject("chest").transform;
            chest.SetParent(t, false);
            chest.localPosition = new Vector3(2.95f, 0f, 3.55f);
            chest.localRotation = Quaternion.Euler(0, 200f, 0);
            Container.CreateVisual(Container.Chest, 0, chest, null);
            // posters everywhere: on the back wall either side of the window, and more on the side walls
            void Poster(Vector3 at, float yaw, Vector2 size, int design)
            {
                var p = new GameObject("poster").transform;
                p.SetParent(t, false);
                p.localPosition = at;
                p.localRotation = Quaternion.Euler(0, yaw, R() * 6f - 3f);
                var bg = Color.HSVToRGB(R(), 0.5f + R() * 0.3f, 0.5f + R() * 0.3f);
                Art.Box(p, new Color(0.9f, 0.88f, 0.82f), Vector3.zero, new Vector3(size.x + 0.06f, size.y + 0.06f, 0.01f));
                Art.Box(p, bg, new Vector3(0, 0, -0.006f), new Vector3(size.x, size.y, 0.01f));
                var ink = Color.Lerp(bg, Color.white, 0.75f);
                switch (design % 4)
                {
                    case 0: // an alien head
                        Art.Part(p, Art.Sphere, ink, new Vector3(0, size.y * 0.08f, -0.014f), new Vector3(size.x * 0.5f, size.y * 0.45f, 0.01f) / SR);
                        foreach (float s in new[] { -1f, 1f }) Art.Part(p, Art.Sphere, Color.black, new Vector3(s * size.x * 0.1f, size.y * 0.02f, -0.02f), new Vector3(size.x * 0.12f, size.y * 0.1f, 0.01f) / SR, new Vector3(0, 0, s * 25f));
                        break;
                    case 1: // the ball on a beam
                        Art.Box(p, Color.Lerp(bg, Color.white, 0.4f), new Vector3(0, 0, -0.012f), new Vector3(size.x * 0.12f, size.y * 0.9f, 0.01f));
                        Art.Part(p, Art.Ico, new Color(1f, 0.85f, 0.15f), new Vector3(0, size.y * 0.05f, -0.03f), Vector3.one * size.x * 0.22f);
                        break;
                    case 2: // a band: big letters (bars)
                        for (int i = 0; i < 4; i++) Art.Box(p, ink, new Vector3(-size.x * 0.3f + i * size.x * 0.2f, size.y * 0.25f, -0.012f), new Vector3(size.x * 0.12f, size.y * 0.22f, 0.01f));
                        Art.Box(p, ink, new Vector3(0, -size.y * 0.2f, -0.012f), new Vector3(size.x * 0.7f, size.y * 0.05f, 0.01f));
                        break;
                    default: // a planet with a ring
                        Art.Part(p, Art.Sphere, ink, new Vector3(0, 0, -0.014f), new Vector3(size.x * 0.4f, size.x * 0.4f, 0.01f) / SR);
                        Art.Box(p, Color.Lerp(ink, Color.black, 0.3f), new Vector3(0, 0, -0.02f), new Vector3(size.x * 0.8f, size.y * 0.04f, 0.01f), new Vector3(0, 0, -15f));
                        break;
                }
            }
            // (the back wall's ones up over the trade stations - none on the window)
            Poster(new Vector3(-5.45f, 2.5f, WinZ - 0.17f), 0f, new Vector2(0.6f, 0.8f), 0);
            Poster(new Vector3(-4.5f, 2.6f, WinZ - 0.17f), 0f, new Vector2(0.45f, 0.6f), 1);
            Poster(new Vector3(4.4f, 2.5f, WinZ - 0.17f), 0f, new Vector2(0.6f, 0.85f), 2);
            Poster(new Vector3(-6.25f, 2.5f, -2.6f), 90f, new Vector2(0.75f, 1f), 3);
            Poster(new Vector3(6.25f, 2.3f, 2.9f), -90f, new Vector2(0.6f, 0.9f), 0);
            Poster(new Vector3(6.25f, 1.6f, -2.2f), -90f, new Vector2(0.8f, 0.6f), 2);
            // ...and more of them
            Poster(new Vector3(-6.25f, 1.6f, -0.9f), 90f, new Vector2(0.6f, 0.8f), 1);
            Poster(new Vector3(-6.25f, 1.9f, 1.4f), 90f, new Vector2(0.9f, 0.65f), 0);
            Poster(new Vector3(-6.25f, 2.55f, 3.2f), 90f, new Vector2(0.5f, 0.7f), 2);
            Poster(new Vector3(6.25f, 2.55f, 0.4f), -90f, new Vector2(0.7f, 0.5f), 3);
            Poster(new Vector3(6.25f, 1.45f, 1.2f), -90f, new Vector2(0.5f, 0.65f), 1);
            BuildMiddle(t, rng);
            BuildDressing(t, rng); // (the trade stations, the gear all round, the guitar, graffiti...: ShipLobby.Dressing.cs)
        }

        /// <summary>The middle of the room: a coffee table in front of the couch, covered in beer cans, a bottle, an
        /// ashtray, a pizza box and rubbish - and more mess on the floor round it.</summary>
        void BuildMiddle(Transform t, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var wood = new Color(0.32f, 0.22f, 0.14f);
            var table = new GameObject("coffee table").transform;
            table.SetParent(t, false);
            table.localPosition = new Vector3(0.1f, 0f, 0.25f);
            table.localRotation = Quaternion.Euler(0, 4f, 0);
            Art.Box(table, wood, new Vector3(0, 0.4f, 0), new Vector3(1.5f, 0.06f, 0.68f));                 // the top
            Art.Box(table, wood * 0.7f, new Vector3(0, 0.12f, 0), new Vector3(1.36f, 0.03f, 0.56f));         // the shelf under it
            foreach (float x in new[] { -0.68f, 0.68f })
                foreach (float z in new[] { -0.28f, 0.28f })
                    Art.Box(table, wood * 0.8f, new Vector3(x, 0.19f, z), new Vector3(0.06f, 0.38f, 0.06f));
            Art.Part(table, Art.Cylinder, wood * 0.55f, new Vector3(0.3f, 0.432f, 0.1f), new Vector3(0.16f / CR, 0.002f, 0.16f / CR)); // (a ring stain)
            // beer cans standing and knocked over, a bottle
            void Can(Vector3 at, bool down, float yaw)
            {
                var c = BeerCan(table).transform;
                c.localPosition = at + (down ? new Vector3(0, 0.05f, 0) : new Vector3(0, 0.085f, 0));
                c.localRotation = Quaternion.Euler(0, yaw, down ? 90f : 0f);
            }
            Can(new Vector3(-0.55f, 0.43f, -0.12f), false, 0f);
            Can(new Vector3(-0.42f, 0.43f, 0.15f), false, 40f);
            Can(new Vector3(0.05f, 0.43f, -0.2f), true, 70f);
            Can(new Vector3(0.55f, 0.43f, 0.18f), false, 10f);
            Can(new Vector3(0.62f, 0.43f, -0.15f), true, -30f);
            Can(new Vector3(-0.2f, 0.135f, 0.05f), true, 20f); // (one on the shelf)
            var bottle = new Color(0.15f, 0.35f, 0.12f);
            Art.Part(table, Art.Cylinder, bottle, new Vector3(-0.15f, 0.54f, 0.12f), new Vector3(0.08f / CR, 0.22f / CH, 0.08f / CR));
            Art.Part(table, Art.Cylinder, bottle, new Vector3(-0.15f, 0.69f, 0.12f), new Vector3(0.03f / CR, 0.09f / CH, 0.03f / CR));
            // a pizza box, open, a slice left in it
            var box = new Color(0.72f, 0.58f, 0.38f);
            Art.Box(table, box, new Vector3(0.25f, 0.445f, 0.02f), new Vector3(0.42f, 0.03f, 0.42f), new Vector3(0, -12f, 0));
            Art.Box(table, box * 0.9f, new Vector3(0.27f, 0.63f, 0.25f), new Vector3(0.42f, 0.42f, 0.02f), new Vector3(-20f, -12f, 0));
            Art.Box(table, new Color(0.9f, 0.6f, 0.2f), new Vector3(0.22f, 0.465f, -0.02f), new Vector3(0.16f, 0.012f, 0.12f), new Vector3(0, 30f, 0));
            // an ashtray full of butts, crisp packets, screwed-up paper
            Art.Part(table, Art.Cylinder, new Color(0.25f, 0.25f, 0.28f), new Vector3(-0.3f, 0.445f, -0.18f), new Vector3(0.14f / CR, 0.03f / CH, 0.14f / CR));
            for (int i = 0; i < 4; i++)
                Art.Box(table, new Color(0.85f, 0.55f, 0.25f), new Vector3(-0.3f + (R() - 0.5f) * 0.08f, 0.465f, -0.18f + (R() - 0.5f) * 0.08f), new Vector3(0.012f, 0.012f, 0.04f), new Vector3(0, R() * 180f, 0));
            Art.Box(table, new Color(0.85f, 0.15f, 0.2f), new Vector3(0.6f, 0.44f, 0.0f), new Vector3(0.16f, 0.015f, 0.22f), new Vector3(0, 35f, 4f));
            Art.Box(table, new Color(0.2f, 0.45f, 0.9f), new Vector3(-0.62f, 0.145f, -0.1f), new Vector3(0.16f, 0.015f, 0.2f), new Vector3(0, -20f, 0));
            for (int i = 0; i < 4; i++)
                Art.Part(table, Art.Ico, new Color(0.88f, 0.86f, 0.8f), new Vector3((R() - 0.5f) * 1.3f, 0.47f, (R() - 0.5f) * 0.5f), Vector3.one * 0.06f, new Vector3(R() * 360f, R() * 360f, 0));
            // the floor round it: more cans, pizza boxes, a sock, a game controller
            for (int i = 0; i < 6; i++)
            {
                float ang = R() * Mathf.PI * 2f, r = 1.1f + R() * 1.4f;
                var at = new Vector3(Mathf.Cos(ang) * r * 1.4f, 0f, 0.2f + Mathf.Sin(ang) * r * 0.7f);
                if (at.z > 0.9f) at.z = 0.9f - R() * 0.3f; // (not under the couch)
                var c = BeerCan(t).transform;
                c.localPosition = at + new Vector3(0, 0.05f, 0);
                c.localRotation = Quaternion.Euler(0, R() * 360f, 90f);
            }
            foreach (var at in new[] { new Vector3(-1.6f, 0f, -0.6f), new Vector3(1.8f, 0f, 0.5f) })
            {
                Art.Box(t, box, at + new Vector3(0, 0.025f, 0), new Vector3(0.42f, 0.05f, 0.42f), new Vector3(0, R() * 90f, 0));
                Art.Box(t, box * 0.85f, at + new Vector3(0.02f, 0.075f, 0.01f), new Vector3(0.42f, 0.05f, 0.42f), new Vector3(0, R() * 90f, 0));
            }
            Art.Box(t, new Color(0.9f, 0.9f, 0.85f), new Vector3(1.3f, 0.015f, -0.4f), new Vector3(0.1f, 0.03f, 0.3f), new Vector3(0, 50f, 0)); // (a sock)
            var pad = new GameObject("controller").transform;
            pad.SetParent(t, false);
            pad.localPosition = new Vector3(-0.9f, 0.03f, 0.75f);
            pad.localRotation = Quaternion.Euler(0, 20f, 0);
            Art.Box(pad, new Color(0.12f, 0.12f, 0.14f), Vector3.zero, new Vector3(0.18f, 0.04f, 0.1f));
            foreach (float s in new[] { -1f, 1f }) Art.Box(pad, new Color(0.12f, 0.12f, 0.14f), new Vector3(s * 0.08f, 0, -0.04f), new Vector3(0.06f, 0.04f, 0.08f), new Vector3(0, s * 20f, 0));
            for (int i = 0; i < 6; i++)
                Art.Part(t, Art.Ico, new Color(0.88f, 0.86f, 0.8f), new Vector3((R() - 0.5f) * 4f, 0.04f, (R() - 0.5f) * 2f - 0.2f), Vector3.one * (0.06f + R() * 0.04f), new Vector3(R() * 360f, R() * 360f, 0));
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
