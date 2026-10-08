using System;
using Steamworks;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Joining a friend through Steam. A Steam HOST opens a friends-only Steam lobby next to the game (it only carries the
    /// host's SteamID - the game's own ship lobby is still the lobby you see), and sets "connect" rich presence. Friends
    /// get in by:
    /// - INVITE FRIENDS in the lobby (the Steam overlay's invite list) and accepting the invite,
    /// - right-clicking the host on the friends list > Join Game,
    /// - or pasting the host's room ID (their SteamID: COPY ROOM ID) into JOIN, like an IP.
    /// If the game isn't running when they accept, Steam starts it with "+connect_lobby id" (Bootstrap.Start passes it here).
    /// </summary>
    public static class SteamLobby
    {
        public static CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public static bool InLobby => Lobby != CSteamID.Nil;
        const string HostKey = "rockhost";

        static Callback<GameLobbyJoinRequested_t> s_JoinRequested;
        static Callback<GameRichPresenceJoinRequested_t> s_PresenceJoin;
        static CallResult<LobbyCreated_t> s_Created;
        static CallResult<LobbyEnter_t> s_Entered;
        static bool s_Hosting;

        public static void Init()
        {
            s_JoinRequested = Callback<GameLobbyJoinRequested_t>.Create(p => Join(p.m_steamIDLobby));
            s_PresenceJoin = Callback<GameRichPresenceJoinRequested_t>.Create(p => FromConnectString(p.m_rgchConnect));
            s_Created = CallResult<LobbyCreated_t>.Create(OnCreated);
            s_Entered = CallResult<LobbyEnter_t>.Create(OnEntered);
        }

        /// <summary>Host: a Steam lobby for this session (kept through the restarts back to the ship lobby).</summary>
        public static void Host(int maxMembers)
        {
            if (!SteamBoot.Ready || (s_Hosting && InLobby)) return;
            Leave();
            s_Hosting = true;
            s_Created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Mathf.Clamp(maxMembers, 2, 250)));
        }

        static void OnCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Debug.LogWarning($"[RockGame] Steam lobby not made: {r.m_eResult}"); s_Hosting = false; return; }
            var bootNow = Bootstrap.I;
            if (!s_Hosting || bootNow == null || !bootNow.IsHostSession) { SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; } // (left before it was made)
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, HostKey, SteamBoot.MyId.ToString());
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", "In the lobby");
            Debug.Log($"[RockGame] Steam lobby {Lobby.m_SteamID} up");
        }

        /// <summary>Accepting an invite / Join Game: into the lobby, then the game connects to its host.</summary>
        public static void Join(CSteamID lobby)
        {
            if (!SteamBoot.Ready || !lobby.IsValid()) return;
            if (Bootstrap.I != null && Bootstrap.I.InSession) Bootstrap.I.Leave(); // (from whatever this PC was doing)
            Leave();
            Bootstrap.I?.SetStatus("Joining your friend through Steam...");
            s_Entered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        static void OnEntered(LobbyEnter_t r, bool ioFailure)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Bootstrap.I?.SetStatus("Couldn't join through Steam (the game may have ended)");
                return;
            }
            Lobby = lobby;
            if (s_Hosting) return;
            string host = SteamMatchmaking.GetLobbyData(lobby, HostKey);
            if (!SteamBoot.TryParseId(host, out _)) host = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID.ToString();
            if (Bootstrap.I != null) { Bootstrap.I.Ip = host; Bootstrap.I.Join(); }
        }

        /// <summary>"+connect_lobby 1234" (rich presence Join Game, or the command line Steam started us with).</summary>
        public static bool FromConnectString(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id)) { Join(new CSteamID(id)); return true; }
            return false;
        }

        /// <summary>The Steam overlay's invite list for this lobby (needs the overlay: not in the editor).</summary>
        public static bool Invite()
        {
            if (!SteamBoot.Ready || !InLobby) return false;
            SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
            return true;
        }

        public static void Leave()
        {
            s_Hosting = false;
            if (!SteamBoot.Ready) return;
            if (InLobby) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
            SteamFriends.ClearRichPresence();
        }
    }
}
