using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace RockGame
{
    /// <summary>
    /// The host's transport when Steam is up: Steam's relay (SteamTransport: invites, Join Game, SteamID room IDs) AND
    /// plain IP (UnityTransport on the usual port) at the same time, so a friend on the same network can still JOIN with
    /// the host's IP, as before Steam. Clients use one or the other directly (Bootstrap.Join); only the host needs both.
    /// Each connected client is remembered by which transport brought it in, and sent to through that one. Steam's ids
    /// are SteamID64s (7656...), UnityTransport's are small connection ids, so they never clash.
    /// UnityTransport does its work in Netcode's EarlyUpdate / PostLateUpdate, which Netcode only calls on its own
    /// transport (this one), so they're passed on to it here.
    /// </summary>
    public class HybridTransport : NetworkTransport
    {
        public UnityTransport Ip;
        public SteamTransport Steam;

        readonly HashSet<ulong> m_IpIds = new HashSet<ulong>();
        bool m_IpOn, m_SteamOn;
        static readonly MethodInfo s_Early = typeof(NetworkTransport).GetMethod("EarlyUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo s_Late = typeof(NetworkTransport).GetMethod("PostLateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        public override ulong ServerClientId => 0;
        public override bool IsSupported => true;

        public override void Initialize(NetworkManager networkManager = null)
        {
            Ip.Initialize(networkManager);
            Steam.Initialize(networkManager);
            Ip.OnTransportEvent -= FromIp;
            Ip.OnTransportEvent += FromIp;
        }

        /// <summary>UnityTransport's events (it pushes them from its EarlyUpdate) passed on as ours.</summary>
        void FromIp(NetworkEvent e, ulong id, ArraySegment<byte> payload, float time)
        {
            if (e == NetworkEvent.Connect) m_IpIds.Add(id);
            InvokeOnTransportEvent(e, id, payload, time);
            if (e == NetworkEvent.Disconnect) m_IpIds.Remove(id);
        }

        public override bool StartServer()
        {
            m_IpIds.Clear();
            m_IpOn = Ip.StartServer(); // (the port can be taken: then it's Steam only)
            m_SteamOn = Steam.StartServer();
            if (!m_IpOn) UnityEngine.Debug.LogWarning("[RockGame] hosting on Steam only: the IP port couldn't be opened");
            return m_IpOn || m_SteamOn;
        }

        public override bool StartClient() => false; // (clients use UnityTransport or SteamTransport themselves)

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            if (m_SteamOn) return Steam.PollEvent(out clientId, out payload, out receiveTime);
            clientId = 0; payload = default; receiveTime = UnityEngine.Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (m_IpIds.Contains(clientId)) Ip.Send(clientId, payload, delivery);
            else if (m_SteamOn) Steam.Send(clientId, payload, delivery);
        }

        public override ulong GetCurrentRtt(ulong clientId) => m_IpIds.Contains(clientId) ? Ip.GetCurrentRtt(clientId) : m_SteamOn ? Steam.GetCurrentRtt(clientId) : 0;

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (m_IpIds.Contains(clientId)) Ip.DisconnectRemoteClient(clientId);
            else if (m_SteamOn) Steam.DisconnectRemoteClient(clientId);
        }

        public override void DisconnectLocalClient() { }

        public override void Shutdown()
        {
            if (m_IpOn) Ip.Shutdown();
            if (m_SteamOn) Steam.Shutdown();
            m_IpOn = m_SteamOn = false;
            m_IpIds.Clear();
            Ip.OnTransportEvent -= FromIp;
        }

        protected override void OnEarlyUpdate()
        {
            if (m_IpOn) s_Early?.Invoke(Ip, null);
            base.OnEarlyUpdate();
        }

        protected override void OnPostLateUpdate()
        {
            if (m_IpOn) s_Late?.Invoke(Ip, null);
            base.OnPostLateUpdate();
        }
    }
}
