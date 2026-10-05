using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class GameSettings
    {
        /// <summary>Settings > Display > AIMING: draw where a bow's arrow, a crossbow's bolt or a thrown spear would fly (off by default).</summary>
        public static readonly DisplayPref.Bool AimTrajectory = new("aim.trajectory", "AIMING", false);
    }

    /// <summary>Settings > Display: the AIMING section (the trajectory line).</summary>
    public partial class Hud
    {
        void DrawAimPreviewSettings()
        {
            float k = m_Scale;
            Caption("AIMING  ·  just on this PC");
            GUILayout.BeginHorizontal();
            RowLabel("Trajectory line", 150 * k);
            bool on = ToggleBtn(GameSettings.AimTrajectory.Value, GameSettings.AimTrajectory.Value ? "On" : "Off", GUILayout.Width(150 * k), GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GameSettings.AimTrajectory.Set(on);
            GUILayout.Label("<color=#bbbbbb>Shows where your shot would go while you draw a bow, wind up a spear throw or hold a loaded crossbow. The line starts white and turns red as the shot reaches full power.</color>", m_SmallWrap);
        }
    }

    /// <summary>
    /// What you'd hit before you let go, on your own screen only: the trajectory line of the bow, crossbow and spear
    /// (Settings > Display > AIMING, off by default) and the portal gun's ghost of the portal it would open.
    /// </summary>
    public partial class PlayerController
    {
        // ------------------------------------------------------------------ trajectory line

        const int TrajDashes = 48;
        const float TrajStep = 0.04f, TrajSkip = 1.4f;
        static readonly RaycastHit[] s_TrajHits = new RaycastHit[16];
        GameObject m_Traj, m_TrajEnd;
        Transform[] m_TrajDash;
        Material m_TrajMat;

        /// <summary>For the tests: the line is showing, how many dashes it has, where it ends and its colour.</summary>
        public bool TrajectoryShown => m_Traj != null && m_Traj.activeSelf;
        public int TrajectoryDashes { get; private set; }
        public Vector3 TrajectoryEnd { get; private set; }
        public Color TrajectoryColor { get; private set; }

        /// <summary>
        /// What the held weapon would launch right now, exactly as its handler does it (HandleBow, HandleCrossbow,
        /// ReleaseSpear): where from, how fast, its gravity and how far towards full power it is (0..1).
        /// False: nothing to show (not drawing, not loaded, no arrows).
        /// </summary>
        bool TrajectoryLaunch(Item held, out Vector3 origin, out Vector3 vel, out float gravity, out float charge)
        {
            origin = vel = default;
            gravity = charge = 0f;
            var ray = CenterRay();
            switch (held)
            {
                case Item.Bow:
                {
                    if (m_DrawStart < 0f || m_Net.Count(Item.Arrow) <= 0) return false;
                    charge = Mathf.Clamp01((Time.time - m_DrawStart) / Mathf.Max(0.05f, Cfg.BowDrawTime));
                    origin = SafeOrigin(ray, 0.6f);
                    vel = ray.direction * Cfg.ArrowSpeed * Mathf.Lerp(Cfg.BowMinSpeed, 1f, charge);
                    gravity = Cfg.ArrowGravity;
                    return true;
                }
                case Item.Crossbow:
                    // (no draw: a loaded crossbow always shoots at full power)
                    if (!XbowLoaded(m_Net.HeldStack) || Time.time < m_XbowBusyUntil) return false;
                    charge = 1f;
                    origin = SafeOrigin(ray, 0.5f);
                    vel = ray.direction * Cfg.CrossbowSpeed;
                    gravity = Cfg.CrossbowGravity;
                    return true;
                case Item.Spear:
                {
                    bool committed = m_SpearReleaseAt >= 0f;
                    if (m_DrawStart < 0f && !committed) return false;
                    charge = committed ? Mathf.InverseLerp(0.65f, 1f, m_SpearPower) : Mathf.Clamp01((Time.time - m_DrawStart) / Mathf.Max(0.05f, Cfg.SpearDrawTime));
                    origin = SafeOrigin(ray, 0.8f);
                    vel = ray.direction * Cfg.SpearThrowSpeed * Mathf.Lerp(0.65f, 1f, charge);
                    gravity = Cfg.SpearGravity;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The trajectory line: dashes along the arc the projectile would fly (the same launch and the same gravity as the
        /// real one - ArrowProjectile), up to the first thing in its way, with a dot where it would land. White for a
        /// shot with no power behind it, turning red as it reaches full power.
        /// </summary>
        void UpdateTrajectory(Item held)
        {
            if (!GameSettings.AimTrajectory.Value || !TrajectoryLaunch(held, out var origin, out var vel, out float g, out float charge))
            {
                if (m_Traj != null && m_Traj.activeSelf) m_Traj.SetActive(false);
                TrajectoryDashes = 0;
                return;
            }
            if (m_Traj == null) BuildTrajectory();
            if (!m_Traj.activeSelf) m_Traj.SetActive(true);
            var c = Color.Lerp(Color.white, new Color(1f, 0.12f, 0.08f), charge);
            c.a = 0.85f;
            TrajectoryColor = c;
            m_TrajMat.SetColor("_BaseColor", c);
            m_TrajMat.color = c;

            var down = Vector3.down * g;
            Vector3 prev = origin, end = origin;
            float travelled = 0f;
            bool hit = false;
            int used = 0;
            for (int i = 1; i <= TrajDashes && !hit; i++)
            {
                float t = i * TrajStep;
                var p = origin + vel * t + down * (0.5f * t * t);
                var seg = p - prev;
                float len = seg.magnitude;
                if (len < 1e-4f) break;
                var dir = seg / len;
                // the first thing in its way that isn't us
                int n = Physics.RaycastNonAlloc(prev, dir, s_TrajHits, len, ~0, QueryTriggerInteraction.Ignore);
                float best = len;
                for (int k = 0; k < n; k++)
                {
                    if (s_TrajHits[k].collider.transform.IsChildOf(transform) || s_TrajHits[k].distance >= best) continue;
                    best = s_TrajHits[k].distance;
                    hit = true;
                }
                end = prev + dir * best;
                // (nothing right in front of the eye: the line starts a little way out)
                if (travelled + best > TrajSkip && used < m_TrajDash.Length)
                {
                    float from = Mathf.Max(0f, TrajSkip - travelled);
                    float dash = hit ? best - from : (best - from) * 0.6f;
                    if (dash > 0.01f)
                    {
                        var d = m_TrajDash[used++];
                        d.SetPositionAndRotation(prev + dir * (from + dash * 0.5f), Quaternion.LookRotation(dir));
                        // (thicker further off, so it doesn't thin away to nothing)
                        float w = 0.025f + 0.0016f * (travelled + best);
                        d.localScale = new Vector3(w, w, dash);
                        if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
                    }
                }
                travelled += best;
                prev = p;
            }
            for (int i = used; i < m_TrajDash.Length; i++)
                if (m_TrajDash[i].gameObject.activeSelf) m_TrajDash[i].gameObject.SetActive(false);
            TrajectoryDashes = used;
            TrajectoryEnd = end;
            bool dot = hit && travelled > TrajSkip;
            if (m_TrajEnd.activeSelf != dot) m_TrajEnd.SetActive(dot);
            if (dot)
            {
                m_TrajEnd.transform.position = end;
                m_TrajEnd.transform.localScale = Vector3.one * (0.12f + 0.006f * travelled);
            }
        }

        void BuildTrajectory()
        {
            m_Traj = new GameObject("trajectory");
            m_TrajMat = new Material(Art.Ghost(new Color(1f, 1f, 1f, 0.85f))) { name = "trajectory" };
            m_TrajDash = new Transform[TrajDashes];
            for (int i = 0; i < TrajDashes; i++)
            {
                var d = Art.Part(m_Traj.transform, Art.Cube, Color.white, Vector3.zero, Vector3.one * 0.03f, default, false, m_TrajMat, "dash");
                NoShadow(d);
                d.SetActive(false);
                m_TrajDash[i] = d.transform;
            }
            m_TrajEnd = Art.Part(m_Traj.transform, Art.Sphere, Color.white, Vector3.zero, Vector3.one * 0.15f, default, false, m_TrajMat, "end");
            NoShadow(m_TrajEnd);
        }

        static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ portal gun preview

        GameObject m_PortalGhost;
        Material m_PortalGhostMat, m_PortalGhostInner;
        Vector3 m_PortalFirstShot;
        bool m_PortalFirstShotKnown;
        GrassClear m_PortalGhostGrass;
        Vector3 m_PortalGrassAt = Vector3.positiveInfinity;

        /// <summary>For the tests: the portal ghost is showing, where, and in which colour.</summary>
        public bool PortalPreviewShown => m_PortalGhost != null && m_PortalGhost.activeSelf;
        public Vector3 PortalPreviewPos => m_PortalGhost != null ? m_PortalGhost.transform.position : default;
        public Color PortalPreviewColor { get; private set; }

        /// <summary>What the portal gun's shot would land on (the same ray as the shot itself): false if there's nothing there.</summary>
        bool PortalAim(out RaycastHit best)
        {
            var ray = CenterRay();
            best = default;
            bool found = false;
            foreach (var h in Physics.RaycastAll(ray, 120f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Which portal the next shot opens, as far as this PC can tell (the server keeps the real count): the gun's second
        /// shot is the partner of our first portal (the lone portal nearest to where we shot it); otherwise it starts the
        /// next pair. False: the shot would be refused (too close to the first portal).
        /// </summary>
        bool PortalNext(NetGame g, Vector3 point, out Color colour)
        {
            int shots = Mathf.Max(1, Cfg.PortalShots);
            int data = m_Net.HeldStack.Data;
            bool first = data <= 0 || data >= shots;
            int newest = -1;
            if (!first)
            {
                // our first portal: one on its own, the nearest to where we put it
                int pair = -1;
                float bd = float.MaxValue;
                Vector3 firstPos = default;
                for (int i = 0; i < g.Portals.Count; i++)
                {
                    var p = g.Portals[i];
                    if (p.Index != 0) continue;
                    bool alone = true;
                    for (int j = 0; j < g.Portals.Count; j++) if (j != i && g.Portals[j].Pair == p.Pair) alone = false;
                    if (!alone) continue;
                    float d = m_PortalFirstShotKnown ? Vector3.Distance(p.Pos, m_PortalFirstShot) : -p.Pair; // (not known: the newest)
                    if (d < bd) { bd = d; pair = p.Pair; firstPos = p.Pos; }
                }
                if (pair >= 0)
                {
                    colour = PortalInfo.ColorOf(pair, 1);
                    return Vector3.Distance(firstPos, point) >= PlayerNet.PortalMinGap;
                }
            }
            for (int i = 0; i < g.Portals.Count; i++) newest = Mathf.Max(newest, g.Portals[i].Pair);
            colour = PortalInfo.ColorOf(Mathf.Max(newest + 1, g.PortalPairsMade), 0);
            return true;
        }

        /// <summary>
        /// Holding the portal gun: a see-through ring on the surface under the crosshair, the size and the colour of the
        /// portal the shot would open there. Hidden when there's nothing to put a portal on, it's out of range or off the
        /// battlefield, or it's too close to the gun's first portal.
        /// </summary>
        void UpdatePortalPreview(Item held)
        {
            var g = NetGame.Instance;
            RaycastHit hit = default;
            bool show = held == Item.PortalGun && g != null && PortalAim(out hit);
            Color colour = default;
            Vector3 point = default, normal = Vector3.up;
            if (show)
            {
                normal = hit.normal.sqrMagnitude > 0.01f ? hit.normal.normalized : Vector3.up;
                point = hit.point + normal * 0.03f;
                show = Mathf.Abs(hit.point.x) <= Cfg.MapHalf + 2 && Mathf.Abs(hit.point.z) <= Cfg.MapHalf + 2 && PortalNext(g, hit.point, out colour);
            }
            if (!show)
            {
                if (m_PortalGhost != null && m_PortalGhost.activeSelf) m_PortalGhost.SetActive(false); // (its GrassClear lets the grass back)
                m_PortalGrassAt = Vector3.positiveInfinity;
                return;
            }
            if (m_PortalGhost == null) BuildPortalGhost();
            if (!m_PortalGhost.activeSelf) m_PortalGhost.SetActive(true);
            m_PortalGhost.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            // the spot it'd open on shows no grass, just as the portal won't (on this screen only; moved on every 15 cm)
            if (!((point - m_PortalGrassAt).sqrMagnitude < 0.15f * 0.15f))
            {
                m_PortalGrassAt = point;
                m_PortalGhostGrass.Clear();
            }
            PortalPreviewColor = colour;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            var ring = new Color(colour.r, colour.g, colour.b, 0.45f + 0.2f * pulse);
            var inner = new Color(colour.r, colour.g, colour.b, 0.1f + 0.06f * pulse);
            m_PortalGhostMat.SetColor("_BaseColor", ring);
            m_PortalGhostMat.color = ring;
            m_PortalGhostInner.SetColor("_BaseColor", inner);
            m_PortalGhostInner.color = inner;
        }

        /// <summary>The same outline as a real portal (PortalFx: 1.3 m wide, 2 m tall, flat on the surface), see-through and without its light.</summary>
        void BuildPortalGhost()
        {
            m_PortalGhost = new GameObject("portal preview");
            m_PortalGhostMat = new Material(Art.Ghost(new Color(1f, 1f, 1f, 0.5f))) { name = "portal preview" };
            m_PortalGhostInner = new Material(Art.Ghost(new Color(1f, 1f, 1f, 0.12f))) { name = "portal preview inner" };
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI * 2f / 16f;
                NoShadow(Art.Box(m_PortalGhost.transform, Color.white, new Vector3(Mathf.Cos(a) * 0.65f, Mathf.Sin(a) * 1.0f, 0), new Vector3(0.28f, 0.1f, 0.04f), new Vector3(0, 0, a * Mathf.Rad2Deg + 90f), false, m_PortalGhostMat));
            }
            NoShadow(Art.Part(m_PortalGhost.transform, Art.Cylinder, Color.white, Vector3.zero, new Vector3(1.3f, 0.01f, 2f), new Vector3(90, 0, 0), false, m_PortalGhostInner, "inner"));
            m_PortalGhostGrass = m_PortalGhost.AddComponent<GrassClear>();
        }

        // ------------------------------------------------------------------ demolish highlight

        // Demolish picked on the building wheel: every piece / chest of yours nearby that LMB could take down is tinted red
        // (on your screen only, with a MaterialPropertyBlock - the shared materials aren't touched), and the one in your
        // crosshair (in reach) a deeper red. The set is looked up a few times a second; letting go of the mode clears it.
        const float DemolishLitRange = 30f;
        static readonly Color k_DemoNear = new Color(1f, 0.32f, 0.26f), k_DemoAimed = new Color(1f, 0.08f, 0.05f);
        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor"), k_ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock s_DemoMpb;
        readonly List<(Renderer r, Unity.Netcode.NetworkObject owner)> m_DemoLit = new List<(Renderer, Unity.Netcode.NetworkObject)>();
        Unity.Netcode.NetworkObject m_DemoAimed;
        float m_DemoScanAt = -1f;
        /// <summary>(tests) how many pieces / chests are lit red right now, and the one in the crosshair (null: none).</summary>
        public int DemolishLitCount { get; private set; }
        public Unity.Netcode.NetworkObject DemolishAimed => m_DemoAimed;

        /// <summary>The piece / chest in the crosshair that Demolish would take down (yours, in reach), or null.</summary>
        Unity.Netcode.NetworkObject DemolishTarget()
        {
            if (!Aim(CenterRay(), Cfg.BuildRange, out var h)) return null;
            var no = h.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();
            return no != null && CanDemolish(no) ? no : null;
        }

        bool CanDemolish(Unity.Netcode.NetworkObject no)
        {
            if (no == null || !no.IsSpawned) return false;
            if (no.TryGetComponent(out Structure s)) return s.Team.Value == m_Net.Team.Value;
            return no.TryGetComponent(out Container c) && c.Breakable && c.Team.Value == m_Net.Team.Value;
        }

        void UpdateDemolishHighlight(bool on)
        {
            if (!on)
            {
                if (m_DemoLit.Count > 0 || m_DemoAimed != null) ClearDemolishLit();
                m_DemoScanAt = -1f;
                return;
            }
            var aimed = DemolishTarget();
            bool rescan = Time.time >= m_DemoScanAt;
            if (!rescan && aimed == m_DemoAimed) return;
            if (rescan)
            {
                m_DemoScanAt = Time.time + 0.25f;
                ClearDemolishLit();
                var me = transform.position;
                float r2 = DemolishLitRange * DemolishLitRange;
                foreach (var s in Structure.All)
                    if (s != null && s.IsSpawned && s.Team.Value == m_Net.Team.Value && (s.transform.position - me).sqrMagnitude < r2) AddDemolishLit(s.NetworkObject);
                foreach (var c in Container.All)
                    if (c != null && c.IsSpawned && c.Breakable && c.Team.Value == m_Net.Team.Value && (c.transform.position - me).sqrMagnitude < r2) AddDemolishLit(c.NetworkObject);
            }
            m_DemoAimed = aimed;
            if (aimed != null && !m_DemoLit.Exists(x => x.owner == aimed)) AddDemolishLit(aimed);
            // tint: the shared material's colour pulled towards red (deeper for the one in the crosshair)
            if (s_DemoMpb == null) s_DemoMpb = new MaterialPropertyBlock();
            var owners = new HashSet<Unity.Netcode.NetworkObject>();
            foreach (var (r, owner) in m_DemoLit)
            {
                if (r == null) continue;
                owners.Add(owner);
                var baseC = r.sharedMaterial != null && r.sharedMaterial.HasProperty(k_BaseColorId) ? r.sharedMaterial.GetColor(k_BaseColorId) : Color.white;
                var c = owner == aimed ? Color.Lerp(baseC, k_DemoAimed, 0.8f) : Color.Lerp(baseC, k_DemoNear, 0.5f);
                c.a = baseC.a;
                s_DemoMpb.Clear();
                s_DemoMpb.SetColor(k_BaseColorId, c);
                s_DemoMpb.SetColor(k_ColorId, c);
                r.SetPropertyBlock(s_DemoMpb);
            }
            DemolishLitCount = owners.Count;
        }

        void AddDemolishLit(Unity.Netcode.NetworkObject no)
        {
            foreach (var r in no.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer || r is SkinnedMeshRenderer) m_DemoLit.Add((r, no));
        }

        void ClearDemolishLit()
        {
            foreach (var (r, _) in m_DemoLit) if (r != null) r.SetPropertyBlock(null);
            m_DemoLit.Clear();
            m_DemoAimed = null;
            DemolishLitCount = 0;
        }

        /// <summary>Both previews, every frame (`held`: None when you can't shoot - dead, a menu open, carrying the ball...).</summary>
        void UpdateAimPreviews(Item held)
        {
            UpdateTrajectory(held);
            UpdatePortalPreview(held);
        }

        void DestroyAimPreviews()
        {
            if (m_Traj) Destroy(m_Traj);
            if (m_TrajMat) Destroy(m_TrajMat);
            if (m_PortalGhost) Destroy(m_PortalGhost);
            if (m_PortalGhostMat) Destroy(m_PortalGhostMat);
            if (m_PortalGhostInner) Destroy(m_PortalGhostInner);
            ClearDemolishLit(); // (the red demolish tint off everything it was on)
        }
    }
}
