using System;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Unity Relay for the exhibition build (Exhibition.cs): the host and the clients all connect out to Unity's relay
    /// server, so nothing has to reach a school PC from outside - no port, no IP, just a join code. It goes over secure
    /// WebSockets (wss, TCP port 443, what a browser uses for https), the likeliest thing to be let through a school's
    /// firewall; -relayudp uses DTLS over UDP instead, which is quicker but more often blocked (all four PCs have to match).
    /// Needs the project linked to Unity Cloud with Relay switched on (see README.md, Exhibition build).
    /// </summary>
    public static class ExhibitionRelay
    {
        /// <summary>The players and a few spectators.</summary>
        const int MaxConnections = 11;
        public static readonly bool Wss = Array.IndexOf(Environment.GetCommandLineArgs(), "-relayudp") < 0;
        static string Kind => Wss ? "wss" : "dtls";

        /// <summary>The next session goes through the relay (false: plain IP - HOST ON THIS NETWORK, the tests).</summary>
        public static bool Active { get; private set; }
        /// <summary>The host's current join code.</summary>
        public static string Code { get; private set; } = "";
        /// <summary>What UnityTransport needs for the allocation we've got.</summary>
        public static RelayServerData Data { get; private set; }

        public static void Clear() { Active = false; Code = ""; }

        static async Task SignIn()
        {
            if (string.IsNullOrEmpty(Application.cloudProjectId))
                throw new InvalidOperationException("This build isn't linked to a Unity Cloud project, so Relay can't start");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // (a fresh profile each run: several copies on one PC mustn't share one signed-in player)
                var options = new InitializationOptions();
                options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 16));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Host: a new allocation and its join code (Data and Code only change if it all worked).</summary>
        public static async Task Allocate()
        {
            await SignIn();
            var a = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            string code = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
            Data = Make(a.ServerEndpoints, a.AllocationIdBytes, a.ConnectionData, a.ConnectionData, a.Key);
            Code = code;
            Active = true;
        }

        /// <summary>Client: into the host's allocation by its join code.</summary>
        public static async Task Join(string code)
        {
            await SignIn();
            var a = await RelayService.Instance.JoinAllocationAsync(code);
            Data = Make(a.ServerEndpoints, a.AllocationIdBytes, a.ConnectionData, a.HostConnectionData, a.Key);
            Code = code;
            Active = true;
        }

        static RelayServerData Make(System.Collections.Generic.List<RelayServerEndpoint> ends, byte[] id, byte[] connection, byte[] hostConnection, byte[] key)
        {
            foreach (var e in ends)
                if (e.ConnectionType == Kind)
                    return new RelayServerData(e.Host, (ushort)e.Port, id, connection, hostConnection, key, e.Secure, Wss);
            throw new InvalidOperationException($"Relay offered no {Kind} connection");
        }

        /// <summary>A failed task's reason, for the screen.</summary>
        public static string Why(Exception e)
        {
            Debug.LogWarning("[RockGame] Relay: " + e);
            var inner = e is AggregateException ag && ag.InnerException != null ? ag.InnerException : e;
            if (inner is RelayServiceException rs)
            {
                if (rs.Reason == RelayExceptionReason.JoinCodeNotFound || rs.Reason == RelayExceptionReason.InvalidRequest) return "That join code wasn't found - check it, or ask the host for the current one";
                if (rs.Reason == RelayExceptionReason.Forbidden || rs.Reason == RelayExceptionReason.Unauthorized || rs.Reason == RelayExceptionReason.InactiveProject) return "Relay isn't switched on for this project (cloud.unity.com)";
            }
            return "Couldn't reach Unity Relay: " + inner.Message;
        }
    }
}
