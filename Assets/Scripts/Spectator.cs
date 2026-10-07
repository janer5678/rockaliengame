using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Someone watching the match (Spectator.cs): their client id and the name they typed on the main menu.</summary>
    public struct SpectatorEntry : INetworkSerializeByMemcpy, System.IEquatable<SpectatorEntry>
    {
        public ulong Id;
        public FixedString32Bytes Name;
        public bool Equals(SpectatorEntry o) => Id == o.Id && Name.Equals(o.Name);
    }

    public partial class NetGame
    {
        /// <summary>Everyone watching instead of playing (joined a match that had already started, or a full one). They
        /// have no player object, no team and no say in anything: they're not in PlayerNet.All.</summary>
        public readonly NetworkList<SpectatorEntry> Spectators = new NetworkList<SpectatorEntry>();

        public bool IsSpectator(ulong clientId)
        {
            if (!IsSpawned) return false;
            foreach (var s in Spectators) if (s.Id == clientId) return true;
            return false;
        }
    }

    /// <summary>
    /// Spectator mode. A client that joins a match it can't play in (already going, or full - Bootstrap.Approve used to
    /// turn them away) is let in with no player object and watches instead: the camera is a player's own first-person
    /// view (their eyes, where they look - the synced yaw and PlayerNet.Pitch, smoothed - and what's in their hands),
    /// left click goes to the next player and right click back. The HUD is just a bar along the bottom (Hud.Spectator.cs).
    /// The server keeps who's watching (NetGame.Spectators); they never count as players (teams, the lobby, the win
    /// checks, the scoreboard - which lists them on a line of their own).
    /// Lives on the NetworkManager object (added by Bootstrap).
    /// </summary>
    public partial class Spectator : MonoBehaviour
    {
        /// <summary>The most spectators a match lets in (after that a late joiner is turned away as before).</summary>
        public const int MaxSpectators = 8;

        public static Spectator I;

        // ---- server side: who was let in to watch (and when, so ones that never finish connecting are forgotten) ----
        static readonly Dictionary<ulong, (string name, float at)> s_Approved = new Dictionary<ulong, (string, float)>();

        /// <summary>Server: a new session (the host starting) - nobody's watching.</summary>
        public static void ServerReset() => s_Approved.Clear();

        /// <summary>Server: is this client one of the watchers (approved as one, connected or not yet)?</summary>
        public static bool ServerIs(ulong clientId) => s_Approved.ContainsKey(clientId);

        /// <summary>Server: room for another watcher?</summary>
        public static bool ServerHasRoom => s_Approved.Count < MaxSpectators;

        /// <summary>Server (connection approval): let this client in to watch. payload: their name (UTF-8, Bootstrap.Join).</summary>
        public static void ServerApprove(ulong clientId, byte[] payload)
        {
            string name = "";
            try { if (payload != null && payload.Length > 0 && payload.Length <= 64) name = GameSettings.CleanName(System.Text.Encoding.UTF8.GetString(payload)); }
            catch { name = ""; }
            s_Approved[clientId] = (name, Time.unscaledTime);
        }

        /// <summary>Server: the players who are really playing (connected clients that aren't watching).</summary>
        public static int ServerPlayerClients(NetworkManager nm)
        {
            int n = 0;
            foreach (var id in nm.ConnectedClientsIds) if (!s_Approved.ContainsKey(id)) n++;
            return n;
        }

        // ---- client side ----
        /// <summary>This machine is watching the match.</summary>
        public static bool Active
        {
            get
            {
                var nm = NetworkManager.Singleton;
                var g = NetGame.Instance;
                // (the host too, once it's chosen to spectate from the lobby: Spectator.Lobby.cs)
                return nm != null && nm.IsConnectedClient && PlayerNet.Local == null && g != null && g.IsSpectator(nm.LocalClientId);
            }
        }

        /// <summary>Who's being watched (null: nobody to watch right now).</summary>
        public static PlayerNet Target => I != null ? I.m_Target : null;
        /// <summary>Test hooks: how many times the watched player changed, and the camera's last pose.</summary>
        public static int Switches { get; private set; }
        public static Vector3 CamPos { get; private set; }

        PlayerNet m_Target;
        float m_Pitch, m_DeadSince = -1f;
        bool m_Snap, m_Was;
        ViewModel m_VM;
        int m_VMTeam = -1;
        readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> m_Hidden = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
        float m_NextHide;
        static readonly List<PlayerNet> s_Order = new List<PlayerNet>();

        void Awake() => I = this;

        /// <summary>The next (dir 1) or previous (dir -1) player; AutoTest uses it in place of a click.</summary>
        public static void Step(int dir)
        {
            if (I != null && Active) I.Cycle(dir, false);
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer) ServerSync(nm);

            bool on = Active;
            if (!on)
            {
                if (m_Was) Stop();
                return;
            }
            if (!m_Was)
            {
                m_Was = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Chat.Add(ShipLobby.Active ? "<color=#bbbbbb>You're spectating: you'll watch the match from the players' eyes. PLAY takes a seat again while there's room.</color>"
                    : "<color=#bbbbbb>The match has already started (or is full): you're spectating. Left click: next player, right click: previous.</color>");
            }
            if (ShipLobby.Active) return; // (in the ship lobby: the room's camera, nobody to follow yet)
            // the watched one went (left the game) or there was nobody yet: the next one there is
            if (m_Target == null || !m_Target.IsSpawned) Cycle(1, true);
            else if (m_Target.Dead.Value && !VictoryCutscene.Active)
            {
                // they died: stay on them a moment, then on to someone who's alive (if anyone is)
                if (m_DeadSince < 0f) m_DeadSince = Time.time;
                if (Time.time - m_DeadSince > 2.5f && AnyAliveOther()) Cycle(1, true);
            }
            else m_DeadSince = -1f;

            bool overUi = Hud.MouseOverUI;
            if (!overUi && !Chat.Open)
            {
                if (Input.GetMouseButtonDown(0)) Cycle(1, false);
                else if (Input.GetMouseButtonDown(1)) Cycle(-1, false);
            }
        }

        bool AnyAliveOther()
        {
            foreach (var p in PlayerNet.All) if (p != null && p != m_Target && p.IsSpawned && !p.Dead.Value) return true;
            return false;
        }

        /// <summary>To the next / previous player (in a steady order: by client id). preferAlive skips the dead if it can.</summary>
        void Cycle(int dir, bool preferAlive)
        {
            s_Order.Clear();
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) s_Order.Add(p);
            if (s_Order.Count == 0) { SetTarget(null); return; }
            s_Order.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            int at = m_Target != null ? s_Order.IndexOf(m_Target) : -1;
            if (at < 0) at = dir > 0 ? -1 : 0;
            for (int n = 1; n <= s_Order.Count; n++)
            {
                var p = s_Order[((at + dir * n) % s_Order.Count + s_Order.Count) % s_Order.Count];
                if (preferAlive && p.Dead.Value && n < s_Order.Count) continue;
                SetTarget(p);
                return;
            }
            SetTarget(s_Order[0]);
        }

        void SetTarget(PlayerNet p)
        {
            if (p == m_Target) return;
            ShowBody(true);
            m_Target = p;
            m_DeadSince = -1f;
            m_Snap = true;
            m_NextHide = 0f;
            if (p != null) Switches++;
        }

        void Stop()
        {
            m_Was = false;
            ShowBody(true);
            m_Target = null;
            m_VM?.Destroy();
            m_VM = null;
            m_VMTeam = -1;
        }

        void OnDisable() => Stop();

        /// <summary>The watched player's own body is hidden from their eyes (it casts its shadow, like it does for them).</summary>
        void ShowBody(bool show)
        {
            if (show)
            {
                foreach (var kv in m_Hidden) if (kv.Key) kv.Key.shadowCastingMode = kv.Value;
                m_Hidden.Clear();
                return;
            }
            if (m_Target == null || Time.time < m_NextHide) return;
            m_NextHide = Time.time + 0.25f; // (things they pick up or put on show up later: looked for again now and then)
            foreach (var r in m_Target.GetComponentsInChildren<Renderer>(true))
            {
                if (m_Hidden.ContainsKey(r)) continue;
                m_Hidden[r] = r.shadowCastingMode;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        void LateUpdate()
        {
            if (!m_Was || ShipLobby.Active) return; // (in the ship lobby the room has the camera)
            var cam = Camera.main;
            if (cam == null) return;
            // the victory cutscene has the camera, for the watchers too
            if (VictoryCutscene.CameraPose(out var cutPos, out var cutRot, out var cutFov))
            {
                ShowBody(true);
                cam.transform.SetPositionAndRotation(cutPos, cutRot);
                cam.fieldOfView = cutFov;
                CamPos = cutPos;
                m_VM?.Update(new ViewModel.State { Item = Item.Rock, Visible = false, Visible2 = false });
                m_Snap = true;
                return;
            }
            // the match intro (MatchIntro.cs): for the watchers too
            if (MatchIntro.CameraPose(out var inPos, out var inRot, out var inFov)) { ShowBody(true); cam.transform.SetPositionAndRotation(inPos, inRot); cam.fieldOfView = inFov; CamPos = inPos; m_VM?.Update(new ViewModel.State { Item = Item.Rock, Visible = false, Visible2 = false }); m_Snap = true; return; }
            var t = m_Target;
            if (t == null || !t.IsSpawned)
            {
                // nobody to watch: a slow look over the map from up high
                float a = Time.time * 4f;
                var p = Quaternion.Euler(0, a, 0) * new Vector3(0, 55, -120) * (Cfg.MapHalf / 100f);
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-p));
                m_VM?.Update(new ViewModel.State { Item = Item.Rock, Visible = false, Visible2 = false });
                return;
            }
            ShowBody(false);
            float eye = t.EyeHeight;
            if (t.Riding)
                foreach (var v in Vehicle.All)
                    if (v != null && v.NetworkObjectId == t.RidingId.Value) { eye = v.IsHorse ? Cfg.EyeHeight + 0.75f : Cfg.EyeHeight - 0.35f; break; }
            // their look: the body's yaw (NetworkTransform, already smoothed) and their pitch (synced in 1 degree steps: smoothed here)
            float yaw = t.transform.eulerAngles.y;
            m_Pitch = m_Snap ? t.Pitch.Value : Mathf.LerpAngle(m_Pitch, t.Pitch.Value, 1f - Mathf.Exp(-14f * Time.deltaTime));
            var pos = t.transform.position + Vector3.up * eye;
            if (!m_Snap) pos = Vector3.Lerp(cam.transform.position, pos, 1f - Mathf.Exp(-30f * Time.deltaTime));
            m_Snap = false;
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(m_Pitch, yaw, 0f));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 8f);
            CamPos = pos;
            // their hands: what they're holding, in their team's colour
            int team = t.Team.Value;
            if (m_VM == null || m_VMTeam != team)
            {
                m_VM?.Destroy();
                m_VM = new ViewModel(cam.transform, Cfg.TeamColor[Mathf.Clamp(team, 0, 3)]);
                m_VMTeam = team;
            }
            // what they're doing (drawing, aiming, eating, sawing) and their swings and throws, as everyone sees them
            byte act = t.Action.Value;
            if (act != m_Act || t != m_ActOf) { m_Act = act; m_ActSince = Time.time; }
            float sw = t.SwingAnim, th = t.ThrowAnim;
            if (t == m_ActOf)
            {
                if (sw > m_PrevSwing + 0.05f) m_VM.WatchedSwing(t.HeldItem, ViewModel.ImpactTime);
                if (th > m_PrevThrow + 0.05f) m_VM.Throw();
            }
            m_ActOf = t;
            m_PrevSwing = sw; m_PrevThrow = th;
            m_VM.Update(ViewModel.Watched(t.HeldItem, (BodyAnimator.Act)act, Time.time - m_ActSince, t.Crouch.Value, t.CarryingBall,
                t.Count(Item.Arrow) > 0, !t.Dead.Value && !t.TreeCamo));
        }

        byte m_Act;
        float m_ActSince, m_PrevSwing, m_PrevThrow;
        PlayerNet m_ActOf;

        // ------------------------------------------------------------------ server

        float m_NextSync;

        /// <summary>Server: NetGame.Spectators is the watchers who are connected now (ones that never finished connecting
        /// are forgotten after a while; ones that left go at once).</summary>
        void ServerSync(NetworkManager nm)
        {
            if (Time.unscaledTime < m_NextSync) return;
            m_NextSync = Time.unscaledTime + 0.25f;
            var connected = new HashSet<ulong>(nm.ConnectedClientsIds);
            var gone = new List<ulong>();
            foreach (var kv in s_Approved)
                if (!connected.Contains(kv.Key) && (Time.unscaledTime - kv.Value.at > 30f || m_Seen.Contains(kv.Key))) gone.Add(kv.Key);
            foreach (var id in gone) { s_Approved.Remove(id); m_Seen.Remove(id); }
            var g = NetGame.Instance;
            if (g == null || !g.IsSpawned) return;
            // drop the ones that are gone, add the ones that have connected
            for (int i = g.Spectators.Count - 1; i >= 0; i--)
                if (!s_Approved.ContainsKey(g.Spectators[i].Id) || !connected.Contains(g.Spectators[i].Id)) g.Spectators.RemoveAt(i);
            int number = 0;
            foreach (var kv in s_Approved)
            {
                number++;
                if (!connected.Contains(kv.Key)) continue;
                m_Seen.Add(kv.Key);
                if (g.IsSpectator(kv.Key)) continue;
                string name = kv.Value.name.Length > 0 ? kv.Value.name : "Spectator" + number;
                FixedString32Bytes fs = default;
                try { fs = new FixedString32Bytes(name); } catch { fs = new FixedString32Bytes("Spectator"); }
                g.Spectators.Add(new SpectatorEntry { Id = kv.Key, Name = fs });
                g.Broadcast($"{name} is spectating");
            }
        }

        readonly HashSet<ulong> m_Seen = new HashSet<ulong>();
    }
}
