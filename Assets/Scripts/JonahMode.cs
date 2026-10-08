using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// JONAH IDEAS TEST MODE (GameRules.JonahTest): Classic, plus two ideas that only exist in this mode.
    /// 1. ALIEN DUST (it looks like a bundle of sugar cane). Everything from both trade stations (and the Trade Station
    ///    itself, and the base upgrades) costs alien dust instead of wood. You get it by buying it: "1000 Alien Dust for
    ///    1000 wood" is the first thing in the crafting list (TAB), and it's on the UPGRADES screen at the upgrade station too.
    /// 2. UPGRADE 10 WALLS (instead of Fortify All Walls). It costs Upgrade10WallsDust alien dust: you're dropped into a
    ///    black void holding your team's building pieces - and your machine and base underneath them, to see where you
    ///    are - laid out to scale (WallPicker). Fly round, look straight at a piece (it goes yellow) and click 10 of them -
    ///    click one again to put it back - and the moment the 10th is picked you're back exactly where you were, looking
    ///    the same way, with an upgrade sound, and those 10 have gone up a tier. Again as often as you like.
    /// Every other mode is untouched.
    /// </summary>
    public static partial class Cfg
    {
        public static bool Jonah => Rules == GameRules.JonahTest;
        [Tune("Jonah test mode")] public static int Upgrade10WallsDust = 1000;
        /// <summary>What the "buy alien dust" craft gives, and what it costs in wood.</summary>
        [Tune("Jonah test mode")] public static int DustPerBuy = 1000, DustBuyWood = 1000;
        public const int WallsPerUpgrade = 10;

        /// <summary>Jonah mode: everything is bought with alien dust (only alien dust itself is bought with wood).</summary>
        public static bool DustItem(Item i) => i != Item.AlienDust;

        /// <summary>What building pieces cost (and give back when demolished): alien dust in Jonah mode, else the usual currency.</summary>
        public static Item BuildItem => Jonah ? Item.AlienDust : CurrencyItem;
        public static string BuildName => Jonah ? "alien dust" : CurrencyName;

        /// <summary>Jonah mode: a dust item's price moves over to alien dust (the same amount).</summary>
        public static Recipe JonahPriced(Recipe r)
        {
            if (!Jonah || !DustItem(r.Output)) return r;
            r.Dust += r.Wood + r.Stone;
            r.Wood = 0;
            r.Stone = 0;
            return r;
        }

        /// <summary>Jonah mode's "Upgrade 10 Walls" (it takes Fortify All Walls' place on the UPGRADES screen).</summary>
        public static Recipe Upgrade10WallsRecipe => new Recipe { Output = Item.FortifyBuff, Count = 1, Dust = Upgrade10WallsDust };

        /// <summary>How many of a team's pieces can still go up a tier.</summary>
        public static int UpgradablePieces(int team)
        {
            int n = 0;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.Team.Value == team && s.Upgradable && s.Tier.Value < MaxFortify) n++;
            return n;
        }
    }

    public partial class PlayerNet
    {
        /// <summary>The wall picker's 10th pick: those pieces (by network id) go up a tier, for Upgrade10WallsDust alien dust.</summary>
        [Rpc(SendTo.Server)]
        public void UpgradeWallsRpc(ulong[] ids)
        {
            var g = NetGame.Instance;
            if (!Cfg.Jonah || Dead.Value || g == null || ids == null || InSuddenDeath || g.S == GameState.GameOver) return;
            int team = Team.Value;
            if (!Cfg.AtOwnStation(team, transform.position)) { Notify("Upgrade 10 Walls is bought at your upgrade station"); return; }
            var picked = new List<Structure>();
            foreach (var id in ids)
            {
                if (picked.Count >= Cfg.WallsPerUpgrade) break;
                if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) || !no.TryGetComponent(out Structure s)) continue;
                if (s.Team.Value != team || !s.Upgradable || s.Tier.Value >= Cfg.MaxFortify || picked.Contains(s)) continue;
                picked.Add(s);
            }
            int need = Mathf.Min(Cfg.WallsPerUpgrade, Cfg.UpgradablePieces(team));
            if (picked.Count == 0 || picked.Count < need) { Notify("Some of those pieces are gone or already armoured - pick again"); return; }
            var r = Cfg.Upgrade10WallsRecipe;
            if (!CanAfford(r)) { Notify($"Upgrade 10 Walls costs {r.Dust} alien dust"); return; }
            ServerPay(r);
            foreach (var s in picked) s.ServerUpgrade(s.Tier.Value + 1);
            g.Broadcast($"{Cfg.TeamLabel(team)} upgraded {picked.Count} piece{(picked.Count == 1 ? "" : "s")} a tier!");
            UpgradedRpc((byte)Item.FortifyBuff, (byte)picked.Count);
            UpgradeFxRpc((byte)team);
        }
    }

    /// <summary>
    /// Jonah mode's Upgrade 10 Walls screen (this machine only): a black void with copies of your team's building pieces
    /// where they stand, to scale, over a copy of your base (its floor, bedrock, machine and stations - for reference, not
    /// pickable). Fly about (WASD, Space up, Crouch down, Sprint faster), kept near them; the piece the crosshair is right
    /// on goes yellow, a click picks it (green; click again to put it back). Picking the 10th (or the last there is, with
    /// fewer than 10) sends them to the server and you're put back exactly where you stood, looking the same way, with an
    /// upgrade sound. Esc gives up. The only words on screen: PICK 10 WALLS TO UPGRADE!
    /// </summary>
    public partial class WallPicker : MonoBehaviour
    {
        public static bool Active => s_I != null;
        static WallPicker s_I;
        const int Layer = 26;
        static readonly Vector3 Offset = new Vector3(0f, -4000f, 0f);

        class Pick { public Structure S; public GameObject Copy; public Renderer[] Rs; public Material[][] Orig; public bool Sel, Maxed; }
        readonly List<Pick> m_Picks = new List<Pick>();
        readonly Dictionary<Collider, Pick> m_ByCollider = new Dictionary<Collider, Pick>();
        Camera m_Cam;
        Transform m_Root;
        Bounds m_Area;
        float m_Yaw, m_Pitch;
        Pick m_Hover;
        int m_Need;
        Material m_SelMat, m_HoverMat, m_MaxMat;
        int m_SkipFrames = 3;
        // where you were, to be put back exactly
        Vector3 m_FromPos;
        float m_FromYaw, m_FromPitch;

        public static void Begin()
        {
            if (s_I != null) return;
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (me == null || pc == null) return;
            int team = me.Team.Value;
            int can = Cfg.UpgradablePieces(team);
            if (can == 0) { Hud.Push("Nothing to upgrade - build some walls first (or they're all armoured)"); return; }
            if (!me.CanAfford(Cfg.Upgrade10WallsRecipe)) { Hud.Push($"Upgrade 10 Walls costs {Cfg.Upgrade10WallsDust} alien dust - buy some in your crafting list"); return; }
            pc.CloseMenu();
            var go = new GameObject("WallPicker");
            s_I = go.AddComponent<WallPicker>();
            s_I.m_FromPos = pc.transform.position;
            s_I.m_FromYaw = pc.LookYaw;
            s_I.m_FromPitch = pc.LookPitch;
            s_I.Build(team, Mathf.Min(Cfg.WallsPerUpgrade, can));
        }

        static Material Glow(Color c, float k)
        {
            var m = new Material(Art.Mat(c));
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * k); }
            return m;
        }

        /// <summary>A copy of a renderer, moved into the void (and its own mesh collider if it can be picked).</summary>
        GameObject CopyRenderer(Transform under, MeshRenderer mr, bool pickable)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            var part = new GameObject("part");
            part.layer = Layer;
            part.transform.SetParent(under, false);
            part.transform.SetPositionAndRotation(mr.transform.position + Offset, mr.transform.rotation);
            part.transform.localScale = mr.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (pickable) part.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; // (exactly the shape: the piece the crosshair is ON lights up)
            return part;
        }

        void Build(int team, int need)
        {
            m_Need = need;
            m_SelMat = Glow(new Color(0.3f, 1f, 0.45f), 1.6f);
            m_HoverMat = Glow(new Color(1f, 0.85f, 0.3f), 1.4f);
            m_MaxMat = Art.Mat(new Color(0.18f, 0.18f, 0.2f));
            m_Root = new GameObject("WallPickerVoid").transform;
            bool any = false;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team || !s.Upgradable) continue;
                var copy = new GameObject("piece");
                copy.layer = Layer;
                copy.transform.SetParent(m_Root, false);
                var rs = new List<Renderer>();
                var orig = new List<Material[]>();
                Bounds b = default;
                bool hasB = false;
                var p = new Pick { S = s, Copy = copy, Maxed = s.Tier.Value >= Cfg.MaxFortify };
                foreach (var mr in s.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    var part = CopyRenderer(copy.transform, mr, true);
                    if (part == null) continue;
                    var r = part.GetComponent<MeshRenderer>();
                    rs.Add(r);
                    orig.Add(mr.sharedMaterials);
                    m_ByCollider[part.GetComponent<Collider>()] = p;
                    if (!hasB) { b = mr.bounds; hasB = true; } else b.Encapsulate(mr.bounds);
                }
                if (!hasB) { Destroy(copy); continue; }
                p.Rs = rs.ToArray();
                p.Orig = orig.ToArray();
                m_Picks.Add(p);
                if (!any) { m_Area = b; any = true; } else m_Area.Encapsulate(b);
                Paint(p);
            }
            // your base underneath, for reference: its floor, the bedrock, the alien machine and the stations (not pickable)
            var world = MapBuilder.Root;
            var bc = Cfg.BaseCenter[team];
            float reach = Cfg.BaseHalf + 1.2f;
            if (world != null)
            {
                var refRoot = new GameObject("base").transform;
                refRoot.SetParent(m_Root, false);
                foreach (var mr in world.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    var rb = mr.bounds;
                    if (rb.min.x < bc.x - reach || rb.max.x > bc.x + reach || rb.min.z < bc.z - reach || rb.max.z > bc.z + reach) continue;
                    if (rb.max.y > Cfg.BaseY + 7f || rb.min.y < -3f) continue;
                    CopyRenderer(refRoot, mr, false);
                    if (!any) { m_Area = rb; any = true; } else m_Area.Encapsulate(rb);
                }
                foreach (var r in refRoot.GetComponentsInChildren<Renderer>()) r.gameObject.layer = Layer;
            }
            // a little room round it all (an invisible boundary)
            m_Area.Expand(new Vector3(16f, 10f, 16f));
            m_Area.center += Offset;
            var lg = new GameObject("WallPickerLight");
            lg.transform.SetParent(m_Root, false);
            lg.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var light = lg.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;
            // the camera: black all round, only this layer
            var cg = new GameObject("WallPickerCam");
            cg.transform.SetParent(transform, false);
            m_Cam = cg.AddComponent<Camera>();
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.backgroundColor = Color.black;
            m_Cam.cullingMask = 1 << Layer;
            m_Cam.fieldOfView = 70f;
            m_Cam.nearClipPlane = 0.05f;
            m_Cam.farClipPlane = 600f;
            var main = Camera.main;
            m_Cam.depth = (main != null ? main.depth : 0f) + 50f;
            // start where you are, looking the way you were
            var start = m_FromPos + Vector3.up * Cfg.EyeHeight + Offset;
            m_Cam.transform.position = new Vector3(Mathf.Clamp(start.x, m_Area.min.x, m_Area.max.x), Mathf.Clamp(start.y, m_Area.min.y, m_Area.max.y), Mathf.Clamp(start.z, m_Area.min.z, m_Area.max.z));
            m_Yaw = m_FromYaw;
            m_Pitch = m_FromPitch;
            Sfx.Play2D(Sfx.Place, 0.5f);
        }

        int Selected { get { int n = 0; foreach (var p in m_Picks) if (p.Sel) n++; return n; } }

        void Paint(Pick p)
        {
            if (p.Rs == null) return;
            for (int i = 0; i < p.Rs.Length; i++)
            {
                if (p.Rs[i] == null) continue;
                Material over = p.Sel ? m_SelMat : p == m_Hover && !p.Maxed ? m_HoverMat : p.Maxed ? m_MaxMat : null;
                if (over == null) { p.Rs[i].sharedMaterials = p.Orig[i]; continue; }
                var arr = new Material[p.Orig[i].Length];
                for (int k = 0; k < arr.Length; k++) arr[k] = over;
                p.Rs[i].sharedMaterials = arr;
            }
        }

        void Update()
        {
            var me = PlayerNet.Local;
            if (me == null || me.Dead.Value || !Cfg.Jonah || (NetGame.Instance != null && NetGame.Instance.S == GameState.GameOver)) { End(false); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { End(false); return; }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            float dt = Time.unscaledDeltaTime;
            if (m_SkipFrames > 0) m_SkipFrames--;
            else
            {
                m_Yaw += Input.GetAxisRaw("Mouse X") * GameSettings.MouseSensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch - Input.GetAxisRaw("Mouse Y") * GameSettings.MouseSensitivity, -89f, 89f);
            }
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            m_Cam.transform.rotation = rot;
            var move = Vector3.zero;
            if (Binds.Held(Bind.Forward)) move += rot * Vector3.forward;
            if (Binds.Held(Bind.Back)) move -= rot * Vector3.forward;
            if (Binds.Held(Bind.Right)) move += rot * Vector3.right;
            if (Binds.Held(Bind.Left)) move -= rot * Vector3.right;
            if (Binds.Held(Bind.Jump)) move += Vector3.up;
            if (Binds.Held(Bind.Crouch)) move -= Vector3.up;
            float speed = Binds.Held(Bind.Sprint) ? 22f : 11f;
            var pos = m_Cam.transform.position + move.normalized * speed * dt;
            pos = new Vector3(Mathf.Clamp(pos.x, m_Area.min.x, m_Area.max.x), Mathf.Clamp(pos.y, m_Area.min.y, m_Area.max.y), Mathf.Clamp(pos.z, m_Area.min.z, m_Area.max.z));
            m_Cam.transform.position = pos;
            // the piece the crosshair is right on (the first surface along the ray: the base underneath blocks nothing)
            Pick hover = null;
            if (Physics.Raycast(pos, rot * Vector3.forward, out var hit, 300f, 1 << Layer, QueryTriggerInteraction.Ignore))
                m_ByCollider.TryGetValue(hit.collider, out hover);
            if (hover != m_Hover)
            {
                var old = m_Hover;
                m_Hover = hover;
                if (old != null) Paint(old);
                if (hover != null) Paint(hover);
            }
            if (Input.GetMouseButtonDown(0) && m_Hover != null && !m_Hover.Maxed)
            {
                var p = m_Hover;
                p.Sel = !p.Sel;
                Paint(p);
                Sfx.Play2D(p.Sel ? Sfx.Pop : Sfx.Thud, 0.45f, 0.1f);
                if (p.Sel && Selected >= m_Need) Finish();
            }
        }

        void Finish()
        {
            var ids = new List<ulong>();
            foreach (var p in m_Picks) if (p.Sel && p.S != null && p.S.IsSpawned) ids.Add(p.S.NetworkObjectId);
            var me = PlayerNet.Local;
            if (me != null && ids.Count > 0) me.UpgradeWallsRpc(ids.ToArray());
            End(true);
        }

        /// <summary>Back where you were (exactly, looking the same way); with the upgrade sound if they were sent off.</summary>
        void End(bool upgraded)
        {
            if (s_I == this) s_I = null;
            var pc = PlayerController.Local;
            if (pc != null && PlayerNet.Local != null && !PlayerNet.Local.Dead.Value)
            {
                pc.LocalTeleport(m_FromPos, m_FromYaw);
                pc.SetLook(m_FromYaw, m_FromPitch);
            }
            if (upgraded)
            {
                Sfx.Play2D(Sfx.Unlock, 0.9f, 0f);
                if (PlayerNet.Local != null) UpgradeStation.Celebrate(PlayerNet.Local.Team.Value);
            }
            if (m_Root != null) Destroy(m_Root.gameObject);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (s_I == this) s_I = null;
            if (m_Root != null) Destroy(m_Root.gameObject);
        }

        void OnGUI()
        {
            GUI.depth = -100;
            float sw = Screen.width, sh = Screen.height, k = Mathf.Max(0.6f, sh / 1080f);
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(38 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            GUI.Label(new Rect(0, 28 * k, sw, 56 * k), $"PICK {m_Need} WALLS TO UPGRADE!", big);
            // the crosshair
            var cc = new Vector2(sw / 2f, sh / 2f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cc.x - 1.5f * k, cc.y - 9 * k, 3 * k, 18 * k), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cc.x - 9 * k, cc.y - 1.5f * k, 18 * k, 3 * k), Texture2D.whiteTexture);
        }
    }
}
