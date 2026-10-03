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
        /// <summary>Map picked in the menu (Cfg.MapKey format). A random seed is rolled when hosting.</summary>
        public static int MapChoice;
        static int s_SeedOverride = -1;

        public GameObject playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ballPrefab, containerPrefab, vehiclePrefab;
        public Material baseMaterial, ghostMaterial;

        [HideInInspector] public string Ip = "127.0.0.1", Port = "7777", Status = "";

        NetworkManager m_Nm;
        UnityTransport m_Ut;
        float m_MenuOrbit;

        public bool InSession => m_Nm != null && (m_Nm.IsListening || m_Nm.ShutdownInProgress);
        public bool IsHost => m_Nm != null && m_Nm.IsHost;

        void Awake()
        {
            I = this;
            Cfg.LoadPrefs();
            GameSettings.Load();
            if (GetComponent<VoiceChat>() == null) gameObject.AddComponent<VoiceChat>();
            // the refresh rate: the highest the screen has (or what was picked in Settings > Display)
            bool test = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;
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
                                : rs == "tutorial" ? 9 : rs == "dna" ? (int)GameRules.Dna : 0; // (classic, original: 0)
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
            int count = m_Nm.ConnectedClientsIds.Count;
            bool waiting = NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting;
            bool ok = isHost || (count < Cfg.PlayersNeeded && (waiting || Cfg.Tutorial)); // the tutorial: join any time, straight in
            resp.Approved = ok;
            resp.CreatePlayerObject = ok;
            // everyone starts in the stadium (waiting area); the player places itself properly once it's spawned
            resp.Position = Cfg.ArenaCenter + new Vector3(Random.Range(-6f, 6f), 0.1f, Random.Range(-6f, 6f));
            resp.Rotation = Quaternion.identity;
            if (!ok) resp.Reason = count >= Cfg.PlayersNeeded ? $"Game is full ({Cfg.ModeLabel})" : "Match already in progress";
        }

        void OnServerStarted()
        {
            var go = Instantiate(netGamePrefab);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        void OnClientDisconnect(ulong id)
        {
            if (m_Nm.IsServer) return;
            if (id == m_Nm.LocalClientId || id == NetworkManager.ServerClientId)
            {
                string reason = m_Nm.DisconnectReason;
                Status = string.IsNullOrEmpty(reason) ? "Disconnected from host" : "Disconnected: " + reason;
                CleanupLocal();
            }
        }

        void Update()
        {
            // Menu camera: slow orbit over the map while not in a match
            if (PlayerController.Local == null && Camera.main != null)
            {
                m_MenuOrbit += Time.deltaTime * 4f;
                var cam = Camera.main.transform;
                var p = Quaternion.Euler(0, m_MenuOrbit, 0) * new Vector3(0, 55, -120) * (Cfg.MapHalf / 100f);
                cam.position = p;
                cam.LookAt(new Vector3(0, 0, 0));
            }
        }
    }
}
