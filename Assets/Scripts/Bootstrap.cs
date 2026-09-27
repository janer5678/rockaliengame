using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Lives on the NetworkManager object. Holds prefab/material references, builds the world,
    /// and starts/stops hosting or joining.
    /// Command line: -host | -client [ip] | -port N | -solo | -fast
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap I;
        public static bool Solo, Fast;

        public GameObject playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ramPrefab, ballPrefab;
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
            Application.targetFrameRate = 144;
            Application.runInBackground = true;
            MapBuilder.Build();
        }

        void Start()
        {
            m_Nm = GetComponent<NetworkManager>();
            m_Ut = GetComponent<UnityTransport>();
            foreach (var p in new[] { playerPrefab, netGamePrefab, structurePrefab, nodePrefab, ramPrefab, ballPrefab })
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

        ushort ParsedPort => ushort.TryParse(Port, out var p) ? p : (ushort)7777;

        public void Host()
        {
            Status = "";
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
            Hud.Clear();
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
                var p = Quaternion.Euler(0, m_MenuOrbit, 0) * new Vector3(0, 55, -120);
                cam.position = p;
                cam.LookAt(new Vector3(0, 0, 0));
            }
        }
    }
}
