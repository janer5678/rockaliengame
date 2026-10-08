using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// JONAH IDEAS TEST MODE (GameRules.JonahTest): Classic, plus two ideas that only exist in this mode.
    /// 1. ALIEN DUST. The upgrade station (left of the alien machine) is also a converter: E on it opens UPGRADES with a
    ///    slot on top - drag wood in, press CONVERT, and it turns into alien dust 1 for 1. Everything that wouldn't make
    ///    sense to make out of wood (metal, guns, machines - Cfg.DustItem) is bought with alien dust instead; of the
    ///    starter items only the Trade Station is.
    /// 2. UPGRADE 10 WALLS (instead of Fortify All Walls). It costs Upgrade10WallsDust alien dust: you're dropped into a
    ///    black void holding only your team's building pieces, laid out to scale where they stand (WallPicker), fly round
    ///    it (a small boundary keeps you near them) and click 10 of them - click one again to put it back - and the
    ///    moment the 10th is picked you're back where you were and those 10 have gone up a tier. Again as often as you like.
    /// Every other mode is untouched.
    /// </summary>
    public static partial class Cfg
    {
        public static bool Jonah => Rules == GameRules.JonahTest;
        [Tune("Jonah test mode")] public static int Upgrade10WallsDust = 1000;
        public const int WallsPerUpgrade = 10;

        /// <summary>Jonah mode: things that aren't made of wood - bought with alien dust (the Trade Station is the only starter item).</summary>
        public static bool DustItem(Item i)
        {
            switch (i)
            {
                case Item.Workbench: case Item.Workbench2:
                case Item.Pickaxe: case Item.Sword: case Item.Chainsaw: case Item.Saddle: case Item.HeavyRam: case Item.BearTrap:
                case Item.Shotgun: case Item.ShotgunShell: case Item.Revolver: case Item.RevolverAmmo: case Item.Pistol: case Item.PistolAmmo:
                case Item.Helmet: case Item.HeavyArmor: case Item.AutoTurret: case Item.C4:
                case Item.WoodGenBuff: case Item.FortifyBuff:
                    return true;
                default: return false;
            }
        }

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

    public static class JonahMode
    {
        /// <summary>Server: a converter (an invisible one-slot container) at every upgrade station - Jonah mode only.</summary>
        public static void ServerSpawnConverters()
        {
            if (!Cfg.Jonah || Cfg.Builder || Bootstrap.I == null || Bootstrap.I.containerPrefab == null) return;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var go = Object.Instantiate(Bootstrap.I.containerPrefab, Cfg.UpgradeStationPos(t), Cfg.UpgradeStationRot(t));
                go.GetComponent<Container>().ServerInit(Container.Converter, t, 1, null);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
        }

        /// <summary>A team's converter (null outside Jonah mode).</summary>
        public static Container ConverterOf(int team)
        {
            if (!Cfg.Jonah) return null;
            foreach (var c in Container.All) if (c != null && c.IsSpawned && c.IsConverter && c.Team.Value == team) return c;
            return null;
        }

        /// <summary>Server: only wood goes into a converter (and nothing else gets swapped in when you take some out).</summary>
        public static bool ServerMoveOk(Container c, NetworkList<ItemStack> src, int srcIdx, NetworkList<ItemStack> other, int dstIdx)
        {
            var s = src[srcIdx];
            if (s.Empty) return true;
            bool into = src != c.Slots;
            if (into) return s.Id == Item.Wood;
            if (dstIdx == 255 || dstIdx >= other.Count) return true;
            var d = other[dstIdx];
            return d.Empty || d.Id == Item.Wood;
        }
    }

    public partial class PlayerNet
    {
        /// <summary>CONVERT on your converter: all the wood in it becomes alien dust in your bag (1 for 1).</summary>
        [Rpc(SendTo.Server)]
        public void ConvertDustRpc(NetworkObjectReference box)
        {
            if (Dead.Value || !Cfg.Jonah || !box.TryGet(out var no) || !no.TryGetComponent(out Container c) || !c.IsConverter) return;
            if (c.Team.Value != Team.Value || !c.InReachServer(EyePos)) return;
            int wood = 0;
            for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == Item.Wood) wood += c.Slots[i].Count;
            if (wood <= 0) { Notify("Drag some wood into the converter first"); return; }
            for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == Item.Wood) c.Slots[i] = default;
            int left = ServerGive(Item.AlienDust, wood);
            var g = NetGame.Instance;
            // (no room in the bag: the rest lands at your feet)
            while (left > 0 && g != null)
            {
                int n = Mathf.Min(left, Cfg.MaxStack(Item.AlienDust));
                g.ServerDropItem(ItemStack.Of(Item.AlienDust, n), transform.position + Vector3.up * 0.8f, transform.forward, EyePos);
                left -= n;
            }
            Fx.Server(FxKind.Spawn, c.transform.position + Vector3.up * 1.2f, Vector3.up);
            ConvertedRpc(wood);
        }

        [Rpc(SendTo.Owner)]
        void ConvertedRpc(int dust)
        {
            Hud.Push($"+{dust} alien dust");
            Sfx.Play2D(Sfx.Unlock, 0.6f, 0.1f);
        }

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
    /// where they stand, to scale. Fly about (WASD, Space up, Crouch down, Sprint faster) inside a small glowing boundary,
    /// aim with the crosshair and click pieces to pick them (click again to put one back). Picking the 10th (or the last
    /// one there is, with fewer than 10) sends them to the server and you're straight back where you were. Esc gives up.
    /// </summary>
    public partial class WallPicker : MonoBehaviour
    {
        public static bool Active => s_I != null;
        static WallPicker s_I;
        const int Layer = 26;
        static readonly Vector3 Offset = new Vector3(0f, -4000f, 0f);

        class Pick { public Structure S; public GameObject Copy; public Renderer[] Rs; public Material[][] Orig; public bool Sel, Maxed; }
        readonly List<Pick> m_Picks = new List<Pick>();
        Camera m_Cam;
        Light m_Light;
        Transform m_Root;
        Bounds m_Area;
        float m_Yaw, m_Pitch;
        Pick m_Hover;
        int m_Need;
        Material m_SelMat, m_HoverMat, m_MaxMat, m_EdgeMat;
        int m_SkipFrames = 3;

        public static void Begin()
        {
            if (s_I != null) return;
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (me == null || pc == null) return;
            int team = me.Team.Value;
            int can = Cfg.UpgradablePieces(team);
            if (can == 0) { Hud.Push("Nothing to upgrade - build some walls first (or they're all armoured)"); return; }
            if (!me.CanAfford(Cfg.Upgrade10WallsRecipe)) { Hud.Push($"Upgrade 10 Walls costs {Cfg.Upgrade10WallsDust} alien dust - convert wood at this station"); return; }
            pc.CloseMenu();
            var go = new GameObject("WallPicker");
            s_I = go.AddComponent<WallPicker>();
            s_I.Build(team, Mathf.Min(Cfg.WallsPerUpgrade, can));
        }

        static Material Glow(Color c, float k)
        {
            var m = new Material(Art.Mat(c));
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * k); }
            return m;
        }

        void Build(int team, int need)
        {
            m_Need = need;
            m_SelMat = Glow(new Color(0.3f, 1f, 0.45f), 1.6f);
            m_HoverMat = Glow(new Color(1f, 0.85f, 0.3f), 1.4f);
            m_MaxMat = Art.Mat(new Color(0.18f, 0.18f, 0.2f));
            m_EdgeMat = Glow(new Color(0.45f, 0.75f, 1f), 2f);
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
                foreach (var mr in s.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var part = new GameObject("part");
                    part.layer = Layer;
                    part.transform.SetParent(copy.transform, false);
                    part.transform.SetPositionAndRotation(mr.transform.position + Offset, mr.transform.rotation);
                    part.transform.localScale = mr.transform.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = part.AddComponent<MeshRenderer>();
                    r.sharedMaterials = mr.sharedMaterials;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    rs.Add(r);
                    orig.Add(mr.sharedMaterials);
                    if (!hasB) { b = mr.bounds; hasB = true; } else b.Encapsulate(mr.bounds);
                }
                if (!hasB) { Destroy(copy); continue; }
                // what the crosshair hits: the piece's whole box
                var bc = copy.AddComponent<BoxCollider>();
                bc.center = b.center + Offset;
                bc.size = Vector3.Max(b.size, new Vector3(0.3f, 0.3f, 0.3f));
                var p = new Pick { S = s, Copy = copy, Rs = rs.ToArray(), Orig = orig.ToArray(), Maxed = s.Tier.Value >= Cfg.MaxFortify };
                m_Picks.Add(p);
                if (!any) { m_Area = b; any = true; } else m_Area.Encapsulate(b);
                Paint(p);
            }
            // the boundary: a little room round the pieces, its edges glowing
            m_Area.Expand(new Vector3(16f, 10f, 16f));
            m_Area.center += Offset;
            BuildEdges();
            // a light of our own (the scene's sun may not reach this layer the same way)
            var lg = new GameObject("WallPickerLight");
            lg.transform.SetParent(m_Root, false);
            lg.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Directional;
            m_Light.intensity = 1.1f;
            m_Light.cullingMask = 1 << Layer;
            m_Light.shadows = LightShadows.None;
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
            // start up and back from the middle, looking at it
            var c = m_Area.center;
            var start = c + new Vector3(0f, m_Area.extents.y * 0.5f, -m_Area.extents.z * 0.8f);
            m_Cam.transform.position = start;
            var look = Quaternion.LookRotation(c - start).eulerAngles;
            m_Yaw = look.y;
            m_Pitch = look.x > 180f ? look.x - 360f : look.x;
            Sfx.Play2D(Sfx.Place, 0.5f);
        }

        void BuildEdges()
        {
            var mn = m_Area.min;
            var mx = m_Area.max;
            var pts = new[] { new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z),
                new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z) };
            int[,] e = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int i = 0; i < 12; i++)
            {
                var a = pts[e[i, 0]];
                var b = pts[e[i, 1]];
                var go = Art.Box(m_Root, Color.white, (a + b) * 0.5f, new Vector3(0.12f, 0.12f, (b - a).magnitude), default, false, m_EdgeMat);
                go.transform.rotation = Quaternion.LookRotation(b - a);
                go.layer = Layer;
            }
            // a faint grid on the floor of the room, so you can tell up from down
            var gridMat = Art.Mat(new Color(0.1f, 0.13f, 0.18f));
            for (float x = Mathf.Ceil(mn.x / 3f) * 3f; x <= mx.x; x += 3f)
                Art.Box(m_Root, Color.white, new Vector3(x, mn.y, m_Area.center.z), new Vector3(0.04f, 0.02f, m_Area.size.z), default, false, gridMat).layer = Layer;
            for (float z = Mathf.Ceil(mn.z / 3f) * 3f; z <= mx.z; z += 3f)
                Art.Box(m_Root, Color.white, new Vector3(m_Area.center.x, mn.y, z), new Vector3(m_Area.size.x, 0.02f, 0.04f), default, false, gridMat).layer = Layer;
        }

        int Selected { get { int n = 0; foreach (var p in m_Picks) if (p.Sel) n++; return n; } }

        void Paint(Pick p)
        {
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
            if (me == null || me.Dead.Value || !Cfg.Jonah || (NetGame.Instance != null && NetGame.Instance.S == GameState.GameOver)) { End(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Hud.Push("Upgrade 10 Walls cancelled - nothing was spent"); End(); return; }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            float dt = Time.unscaledDeltaTime;
            // look
            if (m_SkipFrames > 0) m_SkipFrames--;
            else
            {
                m_Yaw += Input.GetAxisRaw("Mouse X") * GameSettings.MouseSensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch - Input.GetAxisRaw("Mouse Y") * GameSettings.MouseSensitivity, -89f, 89f);
            }
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            m_Cam.transform.rotation = rot;
            // fly (noclip), kept inside the boundary
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
            // aim
            Pick hover = null;
            if (Physics.Raycast(pos, rot * Vector3.forward, out var hit, 300f, 1 << Layer, QueryTriggerInteraction.Collide))
                foreach (var p in m_Picks) if (p.Copy == hit.collider.gameObject) { hover = p; break; }
            if (hover != m_Hover)
            {
                var old = m_Hover;
                m_Hover = hover;
                if (old != null) Paint(old);
                if (hover != null) Paint(hover);
            }
            if (Input.GetMouseButtonDown(0) && m_Hover != null)
            {
                var p = m_Hover;
                if (p.Maxed) Hud.Push("That one's already armoured - as strong as it gets");
                else
                {
                    p.Sel = !p.Sel;
                    Paint(p);
                    Sfx.Play2D(p.Sel ? Sfx.Pop : Sfx.Thud, 0.45f, 0.1f);
                    if (p.Sel && Selected >= m_Need) Finish();
                }
            }
        }

        void Finish()
        {
            var ids = new List<ulong>();
            foreach (var p in m_Picks) if (p.Sel && p.S != null && p.S.IsSpawned) ids.Add(p.S.NetworkObjectId);
            var me = PlayerNet.Local;
            if (me != null && ids.Count > 0) me.UpgradeWallsRpc(ids.ToArray());
            End();
        }

        void End()
        {
            if (s_I == this) s_I = null;
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
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            var mid = new GUIStyle(big) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Normal };
            int n = Selected;
            GUI.Label(new Rect(0, 24 * k, sw, 50 * k), $"UPGRADE {m_Need} WALLS   <color=#7dff8a>{n} / {m_Need}</color>", big);
            GUI.Label(new Rect(0, 70 * k, sw, 30 * k), "<color=#cccccc>Click your pieces to pick them (click again to un-pick). The last pick upgrades them all a tier.</color>", mid);
            GUI.Label(new Rect(0, sh - 60 * k, sw, 30 * k), $"<color=#aaaaaa>{Binds.Name(Bind.Forward)}{Binds.Name(Bind.Left)}{Binds.Name(Bind.Back)}{Binds.Name(Bind.Right)} fly   {Binds.Name(Bind.Jump)} up   {Binds.Name(Bind.Crouch)} down   {Binds.Name(Bind.Sprint)} faster   LMB pick   Esc cancel</color>", mid);
            // crosshair
            var cc = new Vector2(sw / 2f, sh / 2f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cc.x - 1.5f * k, cc.y - 9 * k, 3 * k, 18 * k), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cc.x - 9 * k, cc.y - 1.5f * k, 18 * k, 3 * k), Texture2D.whiteTexture);
            if (m_Hover != null && m_Hover.S != null)
            {
                var s = m_Hover.S;
                string t = m_Hover.Maxed ? $"{s.DisplayName}  <color=#ff8a7a>already armoured</color>"
                    : $"{s.DisplayName}  <color=#7dff8a>{Cfg.TierName(s.Tier.Value)} → {Cfg.TierName(s.Tier.Value + 1)}</color>" + (m_Hover.Sel ? "  <color=#ffd24a>(picked)</color>" : "");
                GUI.Label(new Rect(0, cc.y + 20 * k, sw, 30 * k), t, mid);
            }
        }
    }
}
