using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The lobby's BACK button. A player who joined just leaves. The host doesn't close the lobby: BACK opens the main
    /// menu's pages over it (its map page first, then BACK again goes on back through the game mode, the players and the
    /// battle type, as in the menu) while the session keeps running and everyone stays in the ship. CONFIRM on the map
    /// page takes the host back to the lobby with the picks: a new map (or size) restarts the session the way it does
    /// after a match (Bootstrap.ServerBackToLobby - the others come straight back in, the bots too); the same map with
    /// other options just applies them (NetGame.ServerApplyLobbyOptions). Only BACK past the first menu page closes the
    /// lobby (Bootstrap.Leave) and shows the main menu. While the host's in the menu they're never READY, so the match
    /// can't start without them. The menu is drawn by its own little GUI layer (LobbyMenuLayer) on top of the lobby's
    /// screen, and takes all the mouse so nothing under it can be pressed.
    /// </summary>
    public partial class Hud
    {
        bool m_LobbyMenu;
        float m_LobbyUnreadyAt = -10f;
        static readonly Dictionary<MapKind, Texture2D> s_MapShots = new Dictionary<MapKind, Texture2D>();

        /// <summary>The host is in the main menu's pages over the running lobby (BACK from the lobby).</summary>
        public static bool LobbyMenuOpen => s_I != null && s_I.m_LobbyMenu;
        /// <summary>(tests) which of the menu's pages the lobby menu is on (NewPage: 7 is the map page).</summary>
        public static int LobbyMenuPage => s_I != null ? (int)s_I.m_New : -1;
        /// <summary>(tests) press the lobby's BACK.</summary>
        public static void TestLobbyBack() { if (s_I != null && Bootstrap.I != null) s_I.LobbyBackPressed(Bootstrap.I); }
        /// <summary>(tests) pick this map on the lobby menu's map page.</summary>
        public static void TestLobbyPickMap(MapKind kind) { if (s_I != null) s_I.m_MapPick = Mathf.Max(0, System.Array.IndexOf(k_Maps, kind)); }
        /// <summary>(tests) the map page's CONFIRM.</summary>
        public static void TestLobbyConfirm() { if (s_I != null && s_I.m_LobbyMenu && Bootstrap.I != null) s_I.LobbyMenuApply(Bootstrap.I, k_Maps[Mathf.Clamp(s_I.m_MapPick, 0, k_Maps.Length - 1)]); }
        /// <summary>(tests) the menu's BACK (or Esc).</summary>
        public static void TestMenuBack() { if (s_I != null) s_I.NewBack(); }

        /// <summary>The lobby's BACK button (Hud.Lobby.cs calls this).</summary>
        void LobbyBackPressed(Bootstrap boot)
        {
            if (m_LobbyMenu) return; // (already in the menu)
            var g = NetGame.Instance;
            if (!boot.IsHostSession || Cfg.Tutorial || g == null || !g.IsServer) { boot.Leave(); return; } // (a player who joined, or the tutorial: just leave)
            m_LobbyOptions = false;
            ShipLobby.Customising = false;
            m_SoloFlow = false;
            m_ModesOpen = false;
            m_Page = MenuPage.Main;
            // the pages start on what's being hosted: the battle type, its sizes, the map
            if (Cfg.FreeForAll) { m_Battle = 2; m_FfaN = Mathf.Clamp(Cfg.PlayersNeeded, 3, 4); }
            else if (Cfg.TeamCap(0) <= 1 && Cfg.TeamCap(1) <= 1) m_Battle = 0;
            else { m_Battle = 1; m_CapA = Mathf.Clamp(Cfg.TeamCap(0), 1, 4); m_CapB = Mathf.Clamp(Cfg.TeamCap(1), 1, 4); }
            m_MapPick = Mathf.Max(0, System.Array.IndexOf(k_Maps, Cfg.Map));
            Bootstrap.MapChoice = Cfg.MapKey;
            m_New = NewPage.Map;
            m_LobbyMenu = true;
            KeepHostUnready(true);
            if (GetComponent<LobbyMenuLayer>() == null) gameObject.AddComponent<LobbyMenuLayer>();
        }

        /// <summary>The host in the menu is never READY (so the match can't start without them).</summary>
        void KeepHostUnready(bool now)
        {
            var me = PlayerNet.Local;
            if (me == null || !me.IsSpawned || !me.LobbyReady.Value) return;
            if (!now && Time.unscaledTime - m_LobbyUnreadyAt < 0.5f) return;
            m_LobbyUnreadyAt = Time.unscaledTime;
            me.LobbyReadyRpc(false);
        }

        /// <summary>BACK past the first page (or JOIN): the lobby closes and the main menu's up.</summary>
        void LobbyMenuLeave(Bootstrap boot)
        {
            m_LobbyMenu = false;
            m_Page = MenuPage.Main;
            m_New = NewPage.Root;
            m_PreviewKey = -1;
            if (boot != null && boot.InSession) boot.Leave();
        }

        /// <summary>CONFIRM on the lobby menu's map page: back to the lobby with the picks.</summary>
        void LobbyMenuApply(Bootstrap boot, MapKind kind)
        {
            int key = MenuKey(kind);
            m_LobbyMenu = false;
            m_Page = MenuPage.Main;
            m_New = NewPage.Root;
            m_PreviewKey = -1;
            Cfg.SavePrefs();
            Bootstrap.MapChoice = key;
            PlayerPrefs.SetInt("RockGame.Map", key);
            var size = (key & Cfg.SmallBit) != 0 ? MapSize.Small : (MapSize)((key >> Cfg.SizeShift) & 3);
            bool sameMap = (MapKind)(key & 15) == Cfg.Map && size == Cfg.Size;
            int gfx = Cfg.GraphicsMask << Cfg.GraphicsShift;
            if (sameMap)
            {
                // the same map: just the options (if any changed) - nobody has to rebuild anything
                if ((key & ~gfx) != (Cfg.MapKey & ~gfx)) NetGame.Instance?.ServerApplyLobbyOptions(key);
                return;
            }
            // a new map: everyone needs it built - the session restarts as it does after a match, and they're all back in
            Debug.Log($"[RockGame] the host changed the map in the lobby ({Cfg.Map} -> {(MapKind)(key & 15)}, {size}): restarting the lobby");
            boot.ServerBackToLobby();
        }

        /// <summary>(LobbyMenuLayer, every GUI event) the host's menu over the lobby, while it's open.</summary>
        public static void DrawLobbyMenuLayer()
        {
            if (s_I != null) s_I.LobbyMenuGui();
        }

        void LobbyMenuGui()
        {
            if (!m_LobbyMenu || TestHideAll) return;
            var boot = Bootstrap.I;
            if (boot == null || !boot.InSession || !boot.IsHostSession || !ShipLobby.Active || PlayerNet.Local == null)
            {
                // the session's gone (or the lobby with it): the menu goes back to normal
                if (boot == null || !boot.InSession || !ShipLobby.Active) { m_LobbyMenu = false; m_Page = MenuPage.Main; m_New = NewPage.Root; m_PreviewKey = -1; }
                return;
            }
            if (m_New == NewPage.Root) { m_New = NewPage.Map; }
            KeepHostUnready(false);
            MouseOverUI = true;
            switch (m_Page)
            {
                case MenuPage.ModeOptions: Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 1f)); DrawModeOptions(boot); break;
                case MenuPage.Values: Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 1f)); DrawValues(); break;
                case MenuPage.Settings: m_Page = MenuPage.Main; DrawNewMenu(boot); break;
                default: DrawNewMenu(boot); break;
            }
            // the lobby's screen underneath gets no mouse at all (its READY, its buttons)
            var e = Event.current;
            if (m_LobbyMenu && (e.isMouse || e.type == EventType.ScrollWheel)) e.Use();
        }

        /// <summary>Esc while the lobby menu's up: back a page (true: it was used here). Hud.BackOut calls it.</summary>
        bool LobbyMenuEsc()
        {
            if (!m_LobbyMenu) return false;
            if (m_Page != MenuPage.Main) m_Page = MenuPage.Main;
            else NewBack();
            ClickSound();
            return true;
        }

        /// <summary>
        /// The host's lobby map page is up with the map being hosted picked (same map, same size): its background is the
        /// map itself - the main menu's slow cinematic shots (MenuScene.Shots.cs), filmed over the session's own map while
        /// the page is up. (Another map can't be shown live: the session's map is the one built, and building another
        /// would pull the world out from under everyone in it - that one's picture drifts behind instead, until CONFIRM.)
        /// </summary>
        public static bool LobbyMapLive => s_I != null && s_I.LobbyMapLiveNow();

        bool LobbyMapLiveNow()
        {
            if (!m_LobbyMenu || m_New != NewPage.Map || m_Page != MenuPage.Main || TestHideAll) return false;
            var boot = Bootstrap.I;
            if (boot == null || !boot.InSession || !ShipLobby.Active || MapBuilder.Root == null) return false;
            return LobbyPickIsHosted();
        }

        /// <summary>The map page's pick (map and size) is what's being hosted.</summary>
        bool LobbyPickIsHosted()
        {
            var kind = k_Maps[Mathf.Clamp(m_MapPick, 0, k_Maps.Length - 1)];
            int key = Bootstrap.MapChoice;
            var size = (key & Cfg.SmallBit) != 0 ? MapSize.Small : (MapSize)((key >> Cfg.SizeShift) & 3);
            return kind == Cfg.Map && size == Cfg.Size;
        }

        int m_PicKind = -1;
        float m_PicFrom;

        /// <summary>Behind the lobby menu's pages: the lobby, dimmed nearly away - and on the map page, the map itself
        /// (the hosted one: LobbyMapLive) or another map's picture, slowly drifting and zooming in shots like the live ones.</summary>
        void DrawLobbyMenuBackdrop()
        {
            var full = new Rect(0, 0, Screen.width, Screen.height);
            if (m_New == NewPage.Map && LobbyMapLive)
            {
                // the live map: only the shots' fade through black between them
                if (MenuScene.Fade > 0.001f) Fill(full, new Color(0, 0, 0, MenuScene.Fade));
                m_PicKind = -1;
                return;
            }
            Fill(full, new Color(0.02f, 0.01f, 0.05f, m_New == NewPage.Map ? 1f : 0.94f));
            if (m_New != NewPage.Map) { m_PicKind = -1; return; }
            var kind = k_Maps[Mathf.Clamp(m_MapPick, 0, k_Maps.Length - 1)];
            if (!s_MapShots.TryGetValue(kind, out var tex)) { tex = Resources.Load<Texture2D>("MapShots/" + kind); s_MapShots[kind] = tex; }
            if (tex == null) return;
            if ((int)kind != m_PicKind) { m_PicKind = (int)kind; m_PicFrom = Time.unscaledTime; }
            // the picture as slow shots: each a gentle zoom and drift across part of it, cut through a short fade
            float t = Time.unscaledTime - m_PicFrom, len = MenuScene.ShotSeconds;
            int shot = Mathf.FloorToInt(t / len);
            float st = t - shot * len, u = st / len, e = u * u * (3f - 2f * u) * 0.5f + u * 0.5f;
            var rng = new System.Random(shot * 7919 + (int)kind * 31);
            float R() => (float)rng.NextDouble();
            float z0 = 1.05f + R() * 0.1f, z1 = z0 + 0.12f + R() * 0.08f;
            if (shot % 2 == 1) (z0, z1) = (z1, z0);
            float zoom = Mathf.Lerp(z0, z1, e);
            float sa = Screen.width / (float)Mathf.Max(1, Screen.height), ta = tex.width / (float)Mathf.Max(1, tex.height);
            float uw = sa > ta ? 1f : sa / ta, uh = sa > ta ? ta / sa : 1f;
            uw /= zoom; uh /= zoom;
            var c0 = new Vector2(R(), R()); var c1 = new Vector2(R(), R());
            var c = Vector2.Lerp(c0, c1, e * 0.6f + 0.2f);
            float cx = Mathf.Lerp(uw / 2f, 1f - uw / 2f, c.x), cy = Mathf.Lerp(uh / 2f, 1f - uh / 2f, c.y);
            GUI.DrawTextureWithTexCoords(full, tex, new Rect(cx - uw / 2f, cy - uh / 2f, uw, uh));
            float fin = Mathf.Clamp01(st / MenuScene.ShotFade), fout = Mathf.Clamp01((len - st) / MenuScene.ShotFade);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Min(fin, fout));
            if (fade > 0.001f) Fill(full, new Color(0, 0, 0, fade));
        }

        /// <summary>Top right on the lobby menu: the lobby's still open, and how to close it.</summary>
        void DrawLobbyMenuNote()
        {
            float k = m_Scale, sw = Screen.width;
            int n = ShipLobby.Seated.Count;
            string s = $"<b>YOUR LOBBY IS STILL OPEN</b>  ·  {n} in it\n<color=#bbbbbb>CONFIRM takes you back to it with the new picks.  BACK to the main menu closes it.</color>";
            if (m_New == NewPage.Map && !LobbyPickIsHosted()) s += "\n<color=#bbbbbb>A picture of this map: CONFIRM builds it for everyone.</color>";
            var st = new GUIStyle(m_Small) { alignment = TextAnchor.UpperRight, fontSize = Mathf.RoundToInt(15 * k), wordWrap = false };
            Shadowed(new Rect(0, 18 * k, sw - 22 * k, 80 * k), s, st);
        }
    }

    /// <summary>Draws the host's lobby menu (Hud.LobbyBack.cs) on top of the lobby's screen, taking its mouse.</summary>
    public class LobbyMenuLayer : MonoBehaviour
    {
        void OnGUI()
        {
            GUI.depth = -20;
            Hud.DrawLobbyMenuLayer();
        }
    }
}
