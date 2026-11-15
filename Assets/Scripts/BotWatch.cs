using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Launch with -host -watchbots (e.g. -map highlands -mode 2v2): a bots-only match to watch. In the ship lobby the
    /// host's own player becomes a spectator (as if it had pressed SPECTATE), every team is filled with bots, and the
    /// match starts by itself once they're all READY - you watch from the bots' eyes (left / right click to switch).
    /// </summary>
    public class BotWatch : MonoBehaviour
    {
        bool m_Done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-watchbots") < 0) return;
            var go = new GameObject("BotWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<BotWatch>();
        }

        void Update()
        {
            if (m_Done) return;
            var nm = NetworkManager.Singleton;
            var g = NetGame.Instance;
            if (nm == null || !nm.IsServer || g == null || !g.IsSpawned || g.S != GameState.Waiting || PlayerNet.Local == null) return;
            m_Done = true;
            g.ServerSetSpectating(nm.LocalClientId, true);
            for (int t = 0; t < Cfg.TeamCount; t++)
                for (int k = 0; k < Cfg.TeamCap(t); k++)
                    if (!PlayerNet.ServerAddBot(t)) break;
        }
    }
}
