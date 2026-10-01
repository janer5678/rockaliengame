using UnityEngine;

namespace RockGame
{
    /// <summary>DNA mode's gambling machine, opened like a chest: under its 3 slots, what you're betting and a big GAMBLE button.</summary>
    public partial class Hud
    {
        void DrawGamblePanel(Container c, PlayerNet me, PlayerController pc, float x, float y, float w, float slot, float k)
        {
            int bet = 0;
            for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == Item.Dna) bet += c.Slots[i].Count;
            float gap = 6 * k, bw = 3 * slot + 2 * gap;
            float t = Time.time;

            Shadowed(new Rect(x, y + 4 * k, w, 44 * k), bet > 0
                ? $"<b>BET: <color=#7dffb0>{bet} DNA</color></b>   win: <color=#ffd24a><b>{bet * 2} DNA</b></color>"
                : "<color=#bbbbbb>Put DNA in the slots - that's your bet.</color>", m_Label);
            Shadowed(new Rect(x, y + 28 * k, w, 40 * k), "<color=#bbbbbb>50/50: three DNA pays out DOUBLE, anything else and it's gone.</color>", m_Small);

            // the GAMBLE button: big, gold, and bouncing while there's a bet in
            var r = new Rect(x, y + 56 * k, bw, 70 * k);
            float bounce = bet > 0 ? 1f + 0.05f * Mathf.Abs(Mathf.Sin(t * 5f)) : 1f;
            var cpt = r.center;
            r = new Rect(cpt.x - r.width * bounce * 0.5f, cpt.y - r.height * bounce * 0.5f, r.width * bounce, r.height * bounce);
            Fill(new Rect(r.x + 4 * k, r.y + 5 * k, r.width, r.height), new Color(0, 0, 0, 0.5f));
            Fill(r, bet > 0 ? Color.Lerp(new Color(0.85f, 0.12f, 0.18f), new Color(1f, 0.3f, 0.2f), Mathf.Abs(Mathf.Sin(t * 3f))) : new Color(0.3f, 0.3f, 0.3f, 0.8f));
            Fill(new Rect(r.x, r.y, r.width, 4 * k), bet > 0 ? new Color(1f, 0.85f, 0.25f) : new Color(0.5f, 0.5f, 0.5f));
            Fill(new Rect(r.x, r.yMax - 4 * k, r.width, 4 * k), bet > 0 ? new Color(1f, 0.85f, 0.25f) : new Color(0.5f, 0.5f, 0.5f));
            var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(34 * k * bounce), fontStyle = FontStyle.Bold };
            Shadowed(r, bet > 0 ? "<color=#fff1a8>GAMBLE</color>" : "<color=#999999>GAMBLE</color>", st);
            var none = new GUIStyle();
            if (bet > 0 && BtnAt(r, "", none))
            {
                me.GambleRpc(c.NetworkObject);
                pc.CloseMenu(); // out of the menu: watch the reels on the machine
                Sfx.PlayUi(Sfx.UiClick, 0.8f, 0.7f);
            }
        }
    }
}
