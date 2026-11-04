using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace RockGame
{
    /// <summary>The exhibition build's way in (Exhibition.cs): HOST and JOIN through Unity Relay (ExhibitionRelay.cs).</summary>
    public partial class Bootstrap
    {
        /// <summary>Talking to Relay: the front screen's buttons wait.</summary>
        public bool RelayBusy { get; private set; }
        /// <summary>Hosting through Relay failed: the front screen offers plain IP instead.</summary>
        public bool RelayFailed { get; private set; }

        /// <summary>A join code rather than an IP (it has no dots).</summary>
        static bool IsRelayCode(string s) => !string.IsNullOrWhiteSpace(s) && s.IndexOf('.') < 0;

        /// <summary>HOST on the front screen.</summary>
        public void ExhibitionHost()
        {
            if (!RelayBusy) StartCoroutine(ExhibitionHostRoutine());
        }

        /// <summary>HOST ON THIS NETWORK: plain IP, when Relay isn't there (the other PCs JOIN with the IP on the READY screen).</summary>
        public void ExhibitionHostLan()
        {
            ExhibitionRelay.Clear();
            Host();
        }

        IEnumerator ExhibitionHostRoutine()
        {
            RelayBusy = true;
            RelayFailed = false;
            Status = "Getting a join code...";
            var t = ExhibitionRelay.Allocate();
            while (!t.IsCompleted) yield return null;
            RelayBusy = false;
            if (t.IsFaulted) { Status = ExhibitionRelay.Why(t.Exception); RelayFailed = true; yield break; }
            Host();
        }

        IEnumerator ExhibitionJoinRoutine()
        {
            RelayBusy = true;
            Status = "Joining...";
            string code = Ip.Trim().ToUpperInvariant();
            var t = ExhibitionRelay.Join(code);
            while (!t.IsCompleted) yield return null;
            RelayBusy = false;
            if (t.IsFaulted) { Status = ExhibitionRelay.Why(t.Exception); yield break; }
            UseSteam(false);
            m_Ut.UseWebSockets = ExhibitionRelay.Wss;
            m_Ut.SetRelayServerData(ExhibitionRelay.Data);
            m_Nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(GameSettings.PlayerName);
            if (!m_Nm.StartClient()) Status = "Could not start client";
        }

        /// <summary>Host, as it restarts for the next part: on to the other part's rules, and (through Relay) a new allocation
        /// and join code, sent to everyone while they can still hear it. An allocation's gone once its host has let go of it,
        /// so it has to come before the restart. If it can't be had the old one's tried again.</summary>
        IEnumerator ExhibitionBeforeRestart()
        {
            MapChoice = Cfg.Tutorial ? Exhibition.MatchKey : Exhibition.LobbyKey;
            Exhibition.ExpectBack = PlayerNet.All.Count;
            Exhibition.RememberTeam();
            if (!ExhibitionRelay.Active) yield break;
            var t = ExhibitionRelay.Allocate();
            while (!t.IsCompleted) yield return null;
            if (t.IsFaulted) { ExhibitionRelay.Why(t.Exception); yield break; }
            if (NetGame.Instance != null && NetGame.Instance.IsSpawned) NetGame.Instance.ExhibitionCodeRpc(new Unity.Collections.FixedString32Bytes(ExhibitionRelay.Code));
        }
    }
}
