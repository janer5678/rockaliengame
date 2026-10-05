using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The one format for everything you read when you look at something (the upgrade station's): the thing's name in
    /// capitals, bold and in a colour that means something - its team's colour when a team owns it (so no "(BLUE)" after
    /// it), purple for an airdrop, gold for the ball... - then a dash and the details (health, what the keys do).
    /// Every look-at line goes through Tip / TeamTip, so a new one comes out the same.
    /// </summary>
    public partial class PlayerController
    {
        public static readonly Color TipGold = new Color(1f, 0.76f, 0.29f), TipAirdrop = new Color(0.79f, 0.55f, 1f), TipGamble = new Color(0.49f, 1f, 0.69f),
            TipPlant = new Color(0.62f, 0.86f, 0.48f), TipStone = new Color(0.81f, 0.83f, 0.85f), TipAnimal = new Color(0.88f, 0.75f, 0.54f),
            TipUnicorn = new Color(1f, 0.61f, 0.9f), TipDanger = new Color(1f, 0.33f, 0.33f), TipWood = new Color(0.85f, 0.63f, 0.4f), TipPlain = Color.white;

        /// <summary>"<b>HEADER</b> — detail": the header in capitals and in `colour`; no dash when there's no detail.</summary>
        public static string Tip(string header, Color colour, string detail = "")
        {
            string head = $"<color=#{ColorUtility.ToHtmlStringRGB(colour)}><b>{(header ?? "").ToUpper()}</b></color>";
            detail = detail == null ? "" : detail.Trim();
            return detail.Length > 0 ? head + " — " + detail : head;
        }

        /// <summary>A tip for something a team owns: the header is in that team's colour (which says whose it is).</summary>
        public static string TeamTip(string header, int team, string detail = "") => Tip(header, PlayerNet.NameColor(team), detail);

        /// <summary>The colour for a horse, unicorn, car, boat or Slenderman's header.</summary>
        static Color VehicleTipColor(Vehicle v) => v.IsSlender ? TipDanger : v.IsUnicorn ? TipUnicorn : v.IsHorse ? TipAnimal : TipWood;
    }
}
