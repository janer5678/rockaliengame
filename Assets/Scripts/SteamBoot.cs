using System;
using System.IO;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Steam, started with the game (Bootstrap.Awake) and run every frame (Bootstrap.Update). The game works without it:
    /// with Steam closed (or -nosteam, or the tests) hosting and joining go by IP as before. With it, HOST goes through
    /// Steam's free relay (SteamTransport.cs) and friends join by invite (SteamLobby.cs). The app ID: SteamConfig.cs.
    /// </summary>
    public static class SteamBoot
    {
        /// <summary>Steam is up: hosting and joining can go through it.</summary>
        public static bool Ready { get; private set; }
        /// <summary>Why Steam isn't up ("" when it is, or wasn't tried).</summary>
        public static string Error { get; private set; } = "";
        static bool s_Tried;

        public static ulong MyId => Ready ? SteamUser.GetSteamID().m_SteamID : 0UL;
        public static string MyName => Ready ? SteamFriends.GetPersonaName() : "";

        public static void Init()
        {
            if (s_Tried) return;
            s_Tried = true;
            if (Bootstrap.Testing || Array.IndexOf(Environment.GetCommandLineArgs(), "-nosteam") >= 0) { Error = "Steam is off (-nosteam)"; return; }
            try
            {
                if (!Packsize.Test() || !DllCheck.Test()) { Error = "Steam's files are the wrong version"; return; }
                WriteAppIdFile();
                // a real app started outside Steam (no steam_appid.txt next to it): Steam starts it again, properly
                if (!SteamConfig.TestApp && !Application.isEditor && SteamAPI.RestartAppIfNecessary(new AppId_t(SteamConfig.AppId))) { Application.Quit(); return; }
                Environment.SetEnvironmentVariable("SteamAppId", SteamConfig.AppId.ToString()); // (started from a shortcut somewhere else: still the right app)
                Ready = SteamAPI.Init();
                if (!Ready) { Error = "Steam isn't running (open Steam and log in, then restart the game)"; return; }
                SteamNetworkingUtils.InitRelayNetworkAccess(); // (finds the relays now, so the first connection's quick)
                SetSendBuffer(8 * 1024 * 1024); // (the match's first sync is big: room to queue it all)
                SteamLobby.Init();
                Debug.Log($"[RockGame] Steam up: app {SteamConfig.AppId}, {MyName} ({MyId})");
            }
            catch (DllNotFoundException) { Error = "steam_api64.dll is missing"; }
            catch (Exception e) { Error = "Steam failed to start"; Debug.LogWarning("[RockGame] Steam: " + e.Message); }
            if (!Ready) Debug.Log("[RockGame] no Steam: " + Error);
        }

        /// <summary>(the editor) steam_appid.txt in the project folder, matching SteamConfig - Steam reads it from there.</summary>
        static void WriteAppIdFile()
        {
            if (!Application.isEditor) return;
            try
            {
                string want = SteamConfig.AppId.ToString();
                if (!File.Exists("steam_appid.txt") || File.ReadAllText("steam_appid.txt").Trim() != want) File.WriteAllText("steam_appid.txt", want);
            }
            catch (Exception) { }
        }

        static void SetSendBuffer(int bytes)
        {
            var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                    IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject());
            }
            finally { h.Free(); }
        }

        /// <summary>Every frame: Steam's callbacks (invites, lobbies, connections).</summary>
        public static void Tick()
        {
            if (Ready) SteamAPI.RunCallbacks();
        }

        public static void Shutdown()
        {
            if (!Ready) return;
            SteamLobby.Leave();
            SteamAPI.Shutdown();
            Ready = false;
        }

        /// <summary>A Steam room ID (the host's SteamID64: 17 digits, 7656...) rather than an IP?</summary>
        public static bool TryParseId(string s, out ulong id)
        {
            id = 0;
            s = (s ?? "").Trim();
            return s.Length == 17 && ulong.TryParse(s, out id) && id > 76561197960265728UL;
        }
    }
}
