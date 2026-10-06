using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Lives on the NetworkManager object. Holds prefab/material references, builds the world,
    /// and starts/stops hosting or joining.
    /// Command line: -host | -client [ip] | -port N | -solo | -fast | -map plains|highlands | -small | -big | -wood | -normal | -sides | -anywhere | -mode 1v1|2v2|ffa3|ffa4 | -rules classic|autowood|primitive|... | -seed N
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap I;
        public static bool Solo, Fast;
        /// <summary>Started with -autotest: the tests (no ready-up lobby, no first-launch name screen).</summary>
        public static bool Testing;
        /// <summary>Map picked in the menu (Cfg.MapKey format). A random seed is rolled when hosting.</summary>
        public static int MapChoice;
        static int s_SeedOverride = -1;

        public GameObject playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ballPrefab, containerPrefab, vehiclePrefab;
        public Material baseMaterial, ghostMaterial;

        [HideInInspector] public string Ip = "127.0.0.1", Port = "7777", Status = "";

        NetworkManager m_Nm;
        UnityTransport m_Ut;
        float m_MenuOrbit;

        /// <summary>This PC is hosting the session (the lobby's GAME OPTIONS, its own IP for COPY ROOM ID).</summary>
        public bool IsHostSession => m_Nm != null && m_Nm.IsHost;
        public bool InSession =>m_Nm != null && (m_Nm.IsListening || m_Nm.ShutdownInProgress);
        public bool IsHost => m_Nm != null && m_Nm.IsHost;

        void Awake()
        {
            I = this;
            Cfg.LoadPrefs();
            GameSettings.Load();
            if (GetComponent<VoiceChat>() == null) gameObject.AddComponent<VoiceChat>();
            if (GetComponent<Spectator>() == null) gameObject.AddComponent<Spectator>(); // (watching a match you couldn't join)
            ShipLobby.Ensure(gameObject); // (the ship lobby before a match)
            // the refresh rate: the highest the screen has (or what was picked in Settings > Display)
            bool test = Testing = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;
            GameSettings.ApplyDisplayAtStartup(test);
            Application.runInBackground = true;
            // a first start: the (new) Classic, the Auto Wood rules
            MapChoice = PlayerPrefs.GetInt("RockGame.Map", test ? 0 : (int)GameRules.AutoWood << Cfg.RulesShift);
            // wood is the normal materials now (the menu has no Materials row). The tests keep the old stone materials
            // unless they ask for -wood, so they don't depend on what this PC last hosted; -normal still gets stone.
            if (test || !Cfg.WoodIsNormal) MapChoice &= ~Cfg.WoodBit; else MapChoice |= Cfg.WoodBit;
            ParseMapArgs();
            Cfg.SetMap(MapChoice, 0);
            MapBuilder.Build();
        }

        void Start()
        {
            m_Nm = GetComponent<NetworkManager>();
            m_Ut = GetComponent<UnityTransport>();
            foreach (var p in new[] { playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ballPrefab, containerPrefab, vehiclePrefab })
                if (p != null && !m_Nm.NetworkConfig.Prefabs.Contains(p)) m_Nm.AddNetworkPrefab(p);

            m_Nm.NetworkConfig.ConnectionApproval = true;
            m_Nm.ConnectionApprovalCallback = Approve;
            m_Nm.OnServerStarted += OnServerStarted;
            m_Nm.OnClientDisconnectCallback += OnClientDisconnect;

            var args = System.Environment.GetCommandLineArgs();
            bool host = false, client = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-host": host = true; break;
                    case "-client":
                        client = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) Ip = args[i + 1];
                        break;
                    case "-port": if (i + 1 < args.Length) Port = args[i + 1]; break;
                    case "-solo": Solo = true; break;
                    case "-fast": Fast = true; break;
                    case "-psx": GameSettings.SetPsx(true, false); break;
                    case "-aipsx": GameSettings.SetGraphics(2, false); break;
                    case "-nopsx": GameSettings.SetPsx(false, false); break;
                }
            }
            if (host) Host();
            else if (client) Join();
        }

        static void ParseMapArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-map":
                        if (i + 1 < args.Length)
                        {
                            MapChoice = (MapChoice & ~15) | (args[i + 1].ToLowerInvariant().StartsWith("h") ? (int)MapKind.Highlands : (int)MapKind.Plains);
                            foreach (var tk in ThemeMaps.Kinds) if (args[i + 1].ToLowerInvariant() == tk.ToString().ToLowerInvariant()) MapChoice = (MapChoice & ~15) | (int)tk; // THEME MAPS
                        }
                        break;
                    case "-small": MapChoice = SetSize(MapChoice, MapSize.Small); break;
                    case "-big": MapChoice = SetSize(MapChoice, MapSize.Big); break;
                    case "-large": MapChoice = SetSize(MapChoice, MapSize.Large); break;
                    case "-huge": MapChoice = SetSize(MapChoice, MapSize.Huge); break;
                    case "-wood": MapChoice |= Cfg.WoodBit; break;
                    case "-normal": MapChoice &= ~Cfg.WoodBit; break;
                    case "-mode":
                        if (i + 1 < args.Length)
                        {
                            string m = args[i + 1].ToLowerInvariant();
                            int mode = m == "2v2" ? 1 : m == "ffa3" ? 2 : m == "ffa4" ? 3 : m == "3v3" ? 4 : m == "4v4" ? 5 : m == "2v2v2" ? 6 : m == "2v2v2v2" ? 7 : 0;
                            MapChoice = (MapChoice & ~(Cfg.ModeMask << Cfg.ModeShift)) | (mode << Cfg.ModeShift);
                        }
                        break;
                    case "-rules":
                        if (i + 1 < args.Length)
                        {
                            // (the enum's names, as before: classic = the original game - the menu calls it Primitive now -
                            // and autowood = the menu's Classic. original / primitivelimited / limited are new aliases.)
                            string rs = args[i + 1].ToLowerInvariant().Replace("-", "").Replace("_", "");
                            int rv = rs == "arsenal" ? 1 : rs == "builder" ? 2 : rs == "fun" ? 3 : rs == "funrandom" ? 4 : rs == "funrandomlimited" ? 5
                                : rs == "primitive" || rs == "primitivelimited" || rs == "limited" ? 6 : rs == "buildingprimitive" ? 7 : rs == "autowood" ? 8
                                : rs == "tutorial" ? 9 : rs == "dna" ? (int)GameRules.Dna : rs == "bedwars" ? (int)GameRules.Bedwars : rs == "threegoal" || rs == "3goal" ? (int)GameRules.ThreeGoal
                                : rs == "progress" ? (int)GameRules.Progress : rs == "assassin" ? (int)GameRules.Assassin : rs == "domination" ? (int)GameRules.Domination : 0; // (classic, original: 0)
                            MapChoice = (MapChoice & ~(Cfg.RulesMask << Cfg.RulesShift)) | (rv << Cfg.RulesShift);
                        }
                        break;
                    case "-sides": MapChoice |= Cfg.SidesBit; break;
                    case "-anywhere": MapChoice &= ~Cfg.SidesBit; break;
                    case "-seed": if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sd)) s_SeedOverride = sd; break;
                }
            }
        }

        static int SetSize(int key, MapSize s) => (key & ~Cfg.SmallBit & ~(3 << Cfg.SizeShift)) | ((int)s << Cfg.SizeShift);

        /// <summary>Menu: pick the map (rebuilds the background preview).</summary>
        public void SetMapChoice(int key)
        {
            MapChoice = key;
            PlayerPrefs.SetInt("RockGame.Map", key);
            Cfg.SetMap(key, 0);
            MapBuilder.Build();
        }

        ushort ParsedPort => ushort.TryParse(Port, out var p) ? p : (ushort)7777;

        /// <summary>The main menu's HOST GAME (solo = false) and SOLO TEST (solo = true: hosts and starts without an opponent).</summary>
        public void Host(bool solo)
        {
            Solo = solo;
            Host();
        }

        public void Host()
        {
            Status = "";
            // the host's graphics are everyone's graphics for the match
            MapChoice = (MapChoice & ~(Cfg.GraphicsMask << Cfg.GraphicsShift)) | (GameSettings.GraphicsMode << Cfg.GraphicsShift);
            Cfg.SetMap(MapChoice, s_SeedOverride >= 0 ? s_SeedOverride : Random.Range(1, 999999));
            MapBuilder.Build();
            m_Ut.SetConnectionData("127.0.0.1", ParsedPort, "0.0.0.0");
            if (!m_Nm.StartHost()) Status = "Could not start host (is the port already in use?)";
        }

        public void Join()
        {
            Status = "Connecting to " + Ip + ":" + ParsedPort + " ...";
            m_Ut.SetConnectionData(Ip.Trim(), ParsedPort);
            // our name goes with the request: if we end up spectating it's what the others see (Spectator.cs)
            m_Nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(GameSettings.PlayerName);
            if (!m_Nm.StartClient()) Status = "Could not start client";
        }

        public void Leave()
        {
            m_Nm.Shutdown();
            Status = "";
            CleanupLocal();
        }

        static void CleanupLocal()
        {
            foreach (var a in FindObjectsByType<ArrowProjectile>(FindObjectsSortMode.None)) Destroy(a.gameObject);
            AirdropShip.Clear();
            Hud.Clear();
            Cfg.LoadPrefs(); // drop the host's settings, back to our own
            GameSettings.RestoreGraphics(); // and our own graphics
            Tutorial.Reset();
            Chat.Reset();
            Cfg.SetMap(MapChoice, 0);
            MapBuilder.Build();
        }

        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            bool isHost = req.ClientNetworkId == NetworkManager.ServerClientId;
            if (isHost) Spectator.ServerReset(); // (a new session: nobody's watching)
            int count = Spectator.ServerPlayerClients(m_Nm) + PlayerNet.BotCount; // (the players: spectators don't take a place; the host's bots do)
            bool waiting = NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting;
            // the tutorial: join any time, straight in - unless the host is playing it solo (Tutorial.Friend)
            bool soloTutorial = Cfg.Tutorial && Solo;
            bool ok = isHost || (count < Cfg.PlayersNeeded && !soloTutorial && (waiting || Cfg.Tutorial));
            // can't play (the match has started, or it's full): in as a spectator instead - no player object (Spectator.cs)
            // (not a solo tutorial: that's nobody else's to watch)
            bool watch = !ok && !soloTutorial && NetGame.Instance != null && Spectator.ServerHasRoom;
            if (watch) Spectator.ServerApprove(req.ClientNetworkId, req.Payload);
            resp.Approved = ok || watch;
            resp.CreatePlayerObject = ok;
            // everyone starts in the stadium (waiting area); the player places itself properly once it's spawned
            resp.Position = Cfg.ArenaCenter + new Vector3(Random.Range(-6f, 6f), 0.1f, Random.Range(-6f, 6f));
            resp.Rotation = Quaternion.identity;
            if (!ok && !watch) resp.Reason = soloTutorial ? "That tutorial is being played solo" : !Spectator.ServerHasRoom ? "Game is full (no room to spectate either)" : count >= Cfg.PlayersNeeded ? $"Game is full ({Cfg.ModeLabel})" : "Match already in progress";
        }

        void OnServerStarted()
        {
            var go = Instantiate(netGamePrefab);
            go.GetComponent<NetworkObject>().Spawn(true);
            if (s_BotsToReAdd != null) StartCoroutine(ReAddBots()); // (back from a match: its bots too)
        }

        // ------------------------------------------------------------------ back to the lobby after a match

        /// <summary>What the host tells everyone as it restarts after a match (they come straight back in: Rejoin).</summary>
        public const string BackToLobbyReason = "BACK TO LOBBY";
        static System.Collections.Generic.List<int> s_BotsToReAdd;
        bool m_Restarting, m_Rejoining, m_RejoinTried;
        /// <summary>The host said it's restarting for the lobby (NetGame.BackToLobbyRpc): a disconnect until then is that.</summary>
        public static float s_RejoinExpectedUntil = -1f;
        float m_RejoinUntil, m_NextRejoin;

        /// <summary>(tests / HUD) the host is restarting the session for the lobby, or this client is getting back in.</summary>
        public bool BackingToLobby => m_Restarting || m_Rejoining;

        /// <summary>
        /// Host, once a match is over and its cutscene done (NetGame.ServerTickBackToLobby): everyone back to the ship lobby
        /// for the next one. The session restarts with the same settings (a fresh map, a fresh match): the clients are let
        /// go with BackToLobbyReason and rejoin by themselves (Rejoin), and the host's bots are put back on their teams.
        /// </summary>
        public void ServerBackToLobby()
        {
            if (m_Nm == null || !m_Nm.IsServer || m_Restarting) return;
            StartCoroutine(BackToLobby());
        }

        System.Collections.IEnumerator BackToLobby()
        {
            m_Restarting = true;
            s_BotsToReAdd = PlayerNet.BotTeams();
            // (tell everyone first: however their connection drops, they know to come back in)
            if (NetGame.Instance != null && NetGame.Instance.IsSpawned) NetGame.Instance.BackToLobbyRpc();
            yield return new WaitForSecondsRealtime(0.5f);
            var ids = new System.Collections.Generic.List<ulong>(m_Nm.ConnectedClientsIds);
            foreach (var id in ids) if (id != NetworkManager.ServerClientId) m_Nm.DisconnectClient(id, BackToLobbyReason);
            yield return new WaitForSecondsRealtime(0.4f);
            m_Nm.Shutdown();
            while (m_Nm.ShutdownInProgress) yield return null;
            yield return null;
            foreach (var a in FindObjectsByType<ArrowProjectile>(FindObjectsSortMode.None)) Destroy(a.gameObject);
            AirdropShip.Clear();
            Hud.Clear();
            Tutorial.Reset();
            Host(Solo);
            m_Restarting = false;
        }

        /// <summary>Host: the bots from the last match back on their teams (once the new session's game is up).</summary>
        System.Collections.IEnumerator ReAddBots()
        {
            var teams = s_BotsToReAdd;
            s_BotsToReAdd = null;
            if (teams == null || teams.Count == 0) yield break;
            for (int i = 0; i < 20 && (NetGame.Instance == null || !NetGame.Instance.IsSpawned); i++) yield return null;
            foreach (int t in teams) PlayerNet.ServerAddBot(t);
        }

        /// <summary>Client, let go with BackToLobbyReason: back in to the same host as soon as it's up again.</summary>
        void TickRejoin()
        {
            if (!m_Rejoining) return;
            if (m_Nm.IsConnectedClient && m_RejoinTried) { m_Rejoining = false; s_RejoinExpectedUntil = -1f; return; } // (back in)
            if (m_Nm.IsConnectedClient && !m_RejoinTried && !m_Nm.ShutdownInProgress) { m_Nm.Shutdown(); m_NextRejoin = Time.unscaledTime + 0.5f; return; } // (still on the old connection: drop it)
            if (Time.unscaledTime > m_RejoinUntil) { m_Rejoining = false; Status = "Couldn't get back into the lobby"; return; }
            if (m_Nm.ShutdownInProgress || Time.unscaledTime < m_NextRejoin) return;
            if (m_Nm.IsListening) { m_Nm.Shutdown(); m_NextRejoin = Time.unscaledTime + 0.5f; return; } // (the old connection's still open: close it first)
            m_NextRejoin = Time.unscaledTime + 1.5f;
            Join();
            Debug.Log("[RockGame] rejoining the host for the lobby");
            m_RejoinTried = true;
            Status = "Back to the lobby...";
        }

        void OnClientDisconnect(ulong id)
        {
            if (m_Nm.IsServer) return;
            if (id == m_Nm.LocalClientId || id == NetworkManager.ServerClientId)
            {
                string reason = m_Nm.DisconnectReason;
                Debug.Log($"[RockGame] disconnected (reason \"{reason}\", rejoin expected {Time.unscaledTime < s_RejoinExpectedUntil})");
                Status = string.IsNullOrEmpty(reason) ? "Disconnected from host" : "Disconnected: " + reason;
                CleanupLocal();
                if (reason == BackToLobbyReason || Time.unscaledTime < s_RejoinExpectedUntil) { m_Rejoining = true; m_RejoinTried = false; m_RejoinUntil = Time.unscaledTime + 25f; m_NextRejoin = Time.unscaledTime + 1.2f; Status = "Back to the lobby..."; }
            }
        }

        void Update()
        {
            TickRejoin(); // (back to the lobby after a match)
            // Menu camera: one long, slow cinematic loop through the map while not in a match (and the menu's trees) - MenuScene.cs
            if (PlayerController.Local == null && Camera.main != null && InSession && !Spectator.Active) // (a spectator's camera: Spectator.cs)
            {
                // (connecting / waiting to spawn: the old slow orbit over the map)
                m_MenuOrbit += Time.deltaTime * 4f;
                var cam = Camera.main.transform;
                var p = Quaternion.Euler(0, m_MenuOrbit, 0) * new Vector3(0, 55, -120) * (Cfg.MapHalf / 100f);
                cam.position = p;
                cam.LookAt(new Vector3(0, 0, 0));
            }
            Ragdoll.Tick(); // (the bodies of the dead: Ragdoll.cs)
            MenuScene.Preview = !InSession && Hud.MapPreview; // (the map page: a quick, low look round the map)
            MenuSpace.Tick(!InSession && !Hud.DevMenuShown && !Hud.MapPreview); // (the new main menu: the UFO in space)
            MenuScene.Tick(InSession || (!Hud.DevMenuShown && !Hud.MapPreview)); // (the dev main menu: the flight over the map)
        }
    }
}
