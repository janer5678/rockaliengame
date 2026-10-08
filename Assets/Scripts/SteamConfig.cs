namespace RockGame
{
    /// <summary>
    /// THE ONE PLACE THE STEAM APP ID LIVES.
    /// 480 is Valve's free test app ("Spacewar"): anyone with Steam open can host and join through it, and your friends
    /// list says you're "playing Spacewar" - that's normal. When the game has its own Steam app, change AppId to that
    /// number and rebuild - nothing else needs touching (steam_appid.txt next to the exe is written from this by the
    /// build: ProjectSetup.BuildWindows; with a real ID, a copy started outside Steam relaunches through Steam unless that
    /// file is next to it - SteamBoot.cs).
    /// </summary>
    public static class SteamConfig
    {
        public const uint AppId = 480;
        /// <summary>The free test app (no relaunching through Steam, everyone's "playing Spacewar").</summary>
        public static bool TestApp => AppId == 480;
    }
}
