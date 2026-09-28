using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Lives on the NetworkManager object. Holds prefab/material references, builds the world,
    /// and starts/stops hosting or joining.
    /// Command line: -host | -client [ip] | -port N | -solo | -fast | -map plains|highlands | -small | -big | -wood | -seed N
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap I;
        public static bool Solo, Fast;
        /// <summary>Map picked in the menu (Cfg.MapKey format). A random seed is rolled when hosting.</summary>
        public static int MapChoice;
        static int s_SeedOverride = -1;

        public GameObject playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ballPrefab, containerPrefab;
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
            Application.targetFrameRate = 144;
            Application.runInBackground = true;
            MapChoice = PlayerPrefs.GetInt("RockGame.Map", 0);
            ParseMapArgs();
            Cfg.SetMap(MapChoice, 0);
            MapBuilder.Build();
        }

        void Start()
        {
            m_Nm = GetComponent<NetworkManager>();
            m_Ut = GetComponent<UnityTransport>();
            foreach (var p in new[] { playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ballPrefab, containerPrefab })
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
                        if (i + 1 < args.Length) MapChoice = (MapChoice & ~15) | (args[i + 1].ToLowerInvariant().StartsWith("h") ? (int)MapKind.Highlands : (int)MapKind.Plains);
                        break;
                    case "-small": MapChoice |= Cfg.SmallBit; break;
                    case "-big": MapChoice &= ~Cfg.SmallBit; break;
                    case "-wood": MapChoice |= Cfg.WoodBit; break;
                    case "-seed": if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sd)) s_SeedOverride = sd; break;
                }
            }
        }

        /// <summary>Menu: pick the map (rebuilds the background preview).</summary>
        public void SetMapChoice(int key)
        {
            MapChoice = key;
            PlayerPrefs.SetInt("RockGame.Map", key);
            Cfg.SetMap(key, 0);
            MapBuilder.Build();
        }

        ushort ParsedPort => ushort.TryParse(Port, out var p) ? p : (ushort)7777;

        public void Host()
        {
            Status = "";
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
            Cfg.SetMap(MapChoice, 0);
            MapBuilder.Build();
        }

        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            bool isHost = req.ClientNetworkId == NetworkManager.ServerClientId;
            int count = m_Nm.ConnectedClientsIds.Count;
            bool waiting = NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting;
            bool ok = isHost || (count < 2 && waiting);
            resp.Approved = ok;
            resp.CreatePlayerObject = ok;
            int team = isHost ? 0 : 1;
            NetGame.SpawnPoint(team, false, out var pos, out var yaw);
            resp.Position = pos;
            resp.Rotation = Quaternion.Euler(0, yaw, 0);
            if (!ok) resp.Reason = count >= 2 ? "Game is full (1v1)" : "Match already in progress";
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
