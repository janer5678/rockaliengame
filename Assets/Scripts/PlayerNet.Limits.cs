namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>The most doors (doorway pieces) a team's base can have: the next one is refused (a large gate isn't a door).</summary>
        [Tune("Building")] public static int MaxDoors = 10;
    }

    public partial class PlayerNet
    {
        /// <summary>Killed by someone, the respawn comes this long after the kill cam and its replay would end (s): only
        /// enough that the server's respawn doesn't cut off the replay's last frame - no wait you'd notice.</summary>
        public const float KillCamRespawnSlack = 0.15f;
        /// <summary>Server, for the tests: doors refused because the base already had Cfg.MaxDoors.</summary>
        public static int DoorLimitRefusals;
    }

    public partial class Structure
    {
        /// <summary>How many doorway pieces this team has standing (Cfg.MaxDoors is the most).</summary>
        public static int DoorsOf(int team)
        {
            int n = 0;
            foreach (var s in All) if (s != null && s.IsSpawned && s.PType == PieceType.Doorway && s.Team.Value == team) n++;
            return n;
        }
    }
}
