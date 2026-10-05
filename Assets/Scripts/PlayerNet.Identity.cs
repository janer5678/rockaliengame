using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Your name (main menu, top of the options): what you're called on the scoreboard, in the kill feed, the chat...</summary>
    public static partial class GameSettings
    {
        public const int PlayerNameMax = 16;
        const string PlayerNameKey = "RockGame.PlayerName";
        static bool s_NameLoaded;
        static string s_PlayerName = "";

        /// <summary>The name typed on the main menu ("" = none: you're your team colour and number, like Blue1). Saved on this PC.</summary>
        public static string PlayerName
        {
            get
            {
                if (!s_NameLoaded) { s_NameLoaded = true; s_PlayerName = CleanName(PlayerPrefs.GetString(PlayerNameKey, "")); }
                return s_PlayerName;
            }
            set
            {
                string v = CleanName(value);
                if (s_NameLoaded && v == s_PlayerName) return;
                s_NameLoaded = true;
                s_PlayerName = v;
                PlayerPrefs.SetString(PlayerNameKey, v);
                PlayerPrefs.Save();
            }
        }

        /// <summary>A name made safe to show: no rich-text tags or control characters, single spaces, at most PlayerNameMax long.</summary>
        public static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(PlayerNameMax);
            bool space = true; // (no leading spaces, no doubles)
            foreach (char ch in raw)
            {
                if (ch == '<' || ch == '>' || char.IsControl(ch) || char.IsSurrogate(ch)) continue;
                bool ws = char.IsWhiteSpace(ch);
                if (ws && space) continue;
                sb.Append(ws ? ' ' : ch);
                space = ws;
                if (sb.Length >= PlayerNameMax) break;
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Who a player is: their name (the one they typed on the main menu, or their team colour and number - Blue1), their
    /// kills and deaths this match and their ping, all synced to everyone (the scoreboard - Hud.Scoreboard.cs). And
    /// private messages (whispers): only the sender and the one they're for get them.
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>The name this player typed on their main menu, cleaned up by the server ("" = none).</summary>
        public readonly NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<ushort> Kills = new NetworkVariable<ushort>();
        public readonly NetworkVariable<ushort> Deaths = new NetworkVariable<ushort>();
        /// <summary>Round trip to the server in ms (0 for the host), written by the server every couple of seconds.</summary>
        public readonly NetworkVariable<ushort> Ping = new NetworkVariable<ushort>();

        float m_NextPing, m_NextWhisper, m_NextName;
        /// <summary>Server: the hit being dealt right now is a headshot (the kill feed says so if it kills).</summary>
        bool m_HeadHit;

        /// <summary>"Blue" for team 0, and so on.</summary>
        public static string TeamTitle(int team)
        {
            string n = Cfg.TeamLabel(team);
            return n.Length > 1 ? char.ToUpperInvariant(n[0]) + n.Substring(1).ToLowerInvariant() : n;
        }

        /// <summary>What a player with no name of their own is called: team colour and their number in that team (Blue1).</summary>
        public static string DefaultName(int team, int slot) => TeamTitle(team) + (slot + 1);

        /// <summary>What to call this player everywhere (plain text): the name they typed, or Blue1 / Red2...</summary>
        public string DisplayName
        {
            get
            {
                string n = PlayerName.Value.ToString();
                return n.Length > 0 ? n : DefaultName(Team.Value, Slot.Value);
            }
        }

        /// <summary>The team's colour, lightened a little so it reads on dark strips (names in the chat, kill feed, scoreboard).</summary>
        public static Color NameColor(int team) => team >= 0 && team < Cfg.TeamColor.Length ? Color.Lerp(Cfg.TeamColor[team], Color.white, 0.3f) : new Color(0.8f, 0.6f, 1f);

        /// <summary>The name as rich text: bold, in the team's colour.</summary>
        public string ColoredName => $"<color=#{ColorUtility.ToHtmlStringRGB(NameColor(Team.Value))}><b>{DisplayName}</b></color>";

        /// <summary>The player a client owns (null if they've gone).</summary>
        public static PlayerNet OfClient(ulong clientId)
        {
            foreach (var p in All) if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return p;
            return null;
        }

        /// <summary>The owner tells the server its name when it spawns (and again if it changes).</summary>
        void SendMyName() => SetNameRpc(new FixedString32Bytes(GameSettings.PlayerName));

        [Rpc(SendTo.Server)]
        public void SetNameRpc(FixedString32Bytes name)
        {
            if (Time.time < m_NextName) return;
            m_NextName = Time.time + 0.5f;
            PlayerName.Value = new FixedString32Bytes(GameSettings.CleanName(name.ToString()));
        }

        /// <summary>Server: the damage about to be dealt to this player is a headshot (or not).</summary>
        public void ServerMarkHead(bool head) => m_HeadHit = head;

        /// <summary>Server, every frame: each player's ping, refreshed every two seconds.</summary>
        void ServerTickPing()
        {
            if (Time.unscaledTime < m_NextPing) return;
            m_NextPing = Time.unscaledTime + 2f;
            ushort ms = 0;
            var nm = NetworkManager;
            if (nm != null && OwnerClientId != NetworkManager.ServerClientId && nm.NetworkConfig != null && nm.NetworkConfig.NetworkTransport != null)
                ms = (ushort)Mathf.Min(9999, (int)nm.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId));
            if (Ping.Value != ms) Ping.Value = ms;
        }

        /// <summary>Test hook (server): how many screens the last whisper went to (2: the sender's and the one it's for).</summary>
        public static int LastWhisperTo;

        /// <summary>A private message: only the player it's for and the sender get it (never anyone else).</summary>
        [Rpc(SendTo.Server)]
        public void WhisperRpc(FixedString128Bytes text, ulong toClient)
        {
            var g = NetGame.Instance;
            if (Time.time < m_NextWhisper || g == null) return;
            m_NextWhisper = Time.time + 0.5f;
            string t = text.ToString().Replace("<", "(").Replace(">", ")"); // no rich-text tricks
            if (t.Length > 70) t = t.Substring(0, 70);
            if (t.Trim().Length == 0) return;
            var to = OfClient(toClient);
            if (to == null || to == this) { Notify("They've left the game"); return; }
            var ids = new List<ulong> { OwnerClientId };
            if (toClient != OwnerClientId) ids.Add(toClient);
            LastWhisperTo = ids.Count;
            g.WhisperLineRpc(new FixedString128Bytes(t), OwnerClientId, toClient,
                new FixedString32Bytes(DisplayName), Team.Value, new FixedString32Bytes(to.DisplayName), to.Team.Value, g.RpcTarget.Group(ids, RpcTargetUse.Temp));
        }
    }

    public partial class NetGame
    {
        /// <summary>A private message, on the two screens it's for: the sender sees "to NAME", the other "NAME whispers".</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        public void WhisperLineRpc(FixedString128Bytes text, ulong fromClient, ulong toClient, FixedString32Bytes fromName, byte fromTeam, FixedString32Bytes toName, byte toTeam, RpcParams rpcParams)
        {
            bool mine = NetworkManager != null && NetworkManager.LocalClientId == fromClient;
            Chat.AddWhisper(text.ToString(), mine, mine ? toClient : fromClient, mine ? toName.ToString() : fromName.ToString(), mine ? toTeam : fromTeam);
            Sfx.Play2D(Sfx.Pop, mine ? 0.25f : 0.5f, 0.1f);
            if (!mine) Sfx.Play2D(Sfx.Ding, 0.3f, 0f);
        }
    }
}
