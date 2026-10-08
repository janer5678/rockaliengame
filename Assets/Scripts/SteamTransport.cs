using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Netcode over Steam's networking: players are addressed by SteamID, and Valve's relays carry the traffic (no IPs,
    /// no port forwarding, no Hamachi). Bootstrap switches the NetworkManager to it for a Steam HOST / join, and back to
    /// UnityTransport for IP games and the tests.
    /// Based on the SteamNetworkingSockets transport from Unity's multiplayer-community-contributions (MIT), with its
    /// rough edges fixed for this game: cleanup is immediate (the host restarts its session straight after a match:
    /// Bootstrap.BackToLobby), the "back to the lobby" reason gets through before a connection closes (linger), every
    /// waiting message is read each poll, and ping is real.
    /// </summary>
    public class SteamTransport : NetworkTransport
    {
        /// <summary>Steam's largest message (512 KB), less our channel byte.</summary>
        public const int MaxMessage = 512 * 1024 - 64;

        /// <summary>The host to connect to (StartClient).</summary>
        public ulong ConnectToSteamID;

        class Conn { public CSteamID Id; public HSteamNetConnection Handle; public bool Live; } // (Live: Netcode's been told it's connected)
        static readonly SteamNetworkingConfigValue_t[] s_NoOptions = new SteamNetworkingConfigValue_t[0];

        Callback<SteamNetConnectionStatusChangedCallback_t> m_StatusCb;
        readonly Queue<SteamNetConnectionStatusChangedCallback_t> m_Status = new Queue<SteamNetConnectionStatusChangedCallback_t>();
        readonly Dictionary<ulong, Conn> m_Conns = new Dictionary<ulong, Conn>();
        readonly Queue<(ulong id, byte[] data)> m_Inbox = new Queue<(ulong, byte[])>();
        readonly IntPtr[] m_Msgs = new IntPtr[64];
        HSteamListenSocket m_Listen = HSteamListenSocket.Invalid;
        Conn m_Server;
        bool m_IsServer;

        public override ulong ServerClientId => 0;
        public override bool IsSupported => SteamBoot.Ready;

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!SteamBoot.Ready) Debug.LogError("[RockGame] SteamTransport: Steam isn't running");
        }

        void Listen()
        {
            if (m_StatusCb == null) m_StatusCb = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(p => m_Status.Enqueue(p));
            m_Status.Clear();
            m_Inbox.Clear();
            m_Conns.Clear();
        }

        public override bool StartServer()
        {
            Listen();
            m_IsServer = true;
            m_Server = null;
            m_Listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, s_NoOptions);
            return m_Listen != HSteamListenSocket.Invalid;
        }

        public override bool StartClient()
        {
            Listen();
            m_IsServer = false;
            var id = new CSteamID(ConnectToSteamID);
            var who = new SteamNetworkingIdentity();
            who.SetSteamID(id);
            m_Server = new Conn { Id = id, Handle = SteamNetworkingSockets.ConnectP2P(ref who, 0, 0, s_NoOptions) };
            if (m_Server.Handle == HSteamNetConnection.Invalid) { m_Server = null; return false; }
            m_Conns[ConnectToSteamID] = m_Server;
            return true;
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            // connections coming and going first
            while (m_Status.Count > 0)
            {
                var p = m_Status.Dequeue();
                ulong id = p.m_info.m_identityRemote.GetSteamID64();
                var state = p.m_info.m_eState;
                if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
                {
                    // someone joining us (the host): let them in - the game's own approval decides (Bootstrap.Approve)
                    if (m_IsServer && p.m_info.m_hListenSocket == m_Listen && SteamNetworkingSockets.AcceptConnection(p.m_hConn) == EResult.k_EResultOK)
                        m_Conns[id] = new Conn { Id = p.m_info.m_identityRemote.GetSteamID(), Handle = p.m_hConn };
                }
                else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    if (!m_Conns.TryGetValue(id, out var c) || c.Handle != p.m_hConn) continue; // (an old connection)
                    c.Live = true;
                    clientId = id;
                    return NetworkEvent.Connect;
                }
                else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                    || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                {
                    SteamNetworkingSockets.CloseConnection(p.m_hConn, 0, "Closed", false);
                    if (!m_Conns.TryGetValue(id, out var c) || c.Handle != p.m_hConn) continue;
                    m_Conns.Remove(id);
                    if (c == m_Server) m_Server = null;
                    Debug.Log($"[RockGame] Steam connection closed ({state}: {p.m_info.m_szEndDebug})");
                    clientId = id;
                    return NetworkEvent.Disconnect;
                }
            }
            // then the messages
            if (m_Inbox.Count == 0)
            {
                foreach (var c in m_Conns.Values)
                {
                    if (!c.Live) continue; // (nothing before Netcode knows the connection)
                    int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(c.Handle, m_Msgs, m_Msgs.Length);
                    for (int i = 0; i < n; i++)
                    {
                        var m = Marshal.PtrToStructure<SteamNetworkingMessage_t>(m_Msgs[i]);
                        if (m.m_cbSize > 1)
                        {
                            var buf = new byte[m.m_cbSize - 1]; // (the last byte is the channel)
                            Marshal.Copy(m.m_pData, buf, 0, buf.Length);
                            m_Inbox.Enqueue((c.Id.m_SteamID, buf));
                        }
                        SteamNetworkingMessage_t.Release(m_Msgs[i]);
                    }
                }
            }
            if (m_Inbox.Count > 0)
            {
                var (id, data) = m_Inbox.Dequeue();
                clientId = id;
                payload = new ArraySegment<byte>(data);
                return NetworkEvent.Data;
            }
            clientId = 0;
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> segment, NetworkDelivery delivery)
        {
            Conn c = clientId == 0 && !m_IsServer ? m_Server : m_Conns.TryGetValue(clientId, out var cc) ? cc : null;
            if (c == null) return;
            var data = new byte[segment.Count + 1];
            Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
            data[segment.Count] = (byte)delivery;
            int flags = delivery switch
            {
                NetworkDelivery.Reliable or NetworkDelivery.ReliableFragmentedSequenced => Constants.k_nSteamNetworkingSend_Reliable,
                NetworkDelivery.ReliableSequenced => Constants.k_nSteamNetworkingSend_ReliableNoNagle,
                NetworkDelivery.UnreliableSequenced => Constants.k_nSteamNetworkingSend_UnreliableNoNagle,
                _ => Constants.k_nSteamNetworkingSend_Unreliable,
            };
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            EResult r;
            try { r = SteamNetworkingSockets.SendMessageToConnection(c.Handle, h.AddrOfPinnedObject(), (uint)data.Length, flags, out long _); }
            finally { h.Free(); }
            if (r != EResult.k_EResultOK && r != EResult.k_EResultIgnored) Debug.LogWarning($"[RockGame] Steam send failed: {r} ({data.Length} bytes)");
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            Conn c = clientId == 0 && !m_IsServer ? m_Server : m_Conns.TryGetValue(clientId, out var cc) ? cc : null;
            if (c == null) return 0;
            var st = new SteamNetConnectionRealTimeStatus_t();
            var lane = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(c.Handle, ref st, 0, ref lane) == EResult.k_EResultOK ? (ulong)Mathf.Max(0, st.m_nPing) : 0;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!m_Conns.TryGetValue(clientId, out var c)) return;
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Disconnected", true); // (linger: the reason gets there first)
            m_Conns.Remove(clientId);
        }

        public override void DisconnectLocalClient()
        {
            if (m_Server != null) SteamNetworkingSockets.CloseConnection(m_Server.Handle, 0, "Left", true);
            if (m_Server != null) m_Conns.Remove(m_Server.Id.m_SteamID);
            m_Server = null;
        }

        public override void Shutdown()
        {
            if (!SteamBoot.Ready) return;
            foreach (var c in m_Conns.Values) SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Shutdown", true);
            m_Conns.Clear();
            m_Inbox.Clear();
            m_Status.Clear();
            m_Server = null;
            if (m_Listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(m_Listen);
            m_Listen = HSteamListenSocket.Invalid;
            m_IsServer = false;
        }
    }
}
