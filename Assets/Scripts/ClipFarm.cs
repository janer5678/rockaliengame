using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// CLIP FARM (GameRules.ClipFarm): Classic (keep the ball in your machine to fill your bar - no clock) turned up for
    /// highlight clips:
    /// - LOADED: everyone gets a random airdrop item when the wall drops and every time they respawn - and every airdrop
    ///   item is in (whatever's picked in the mode options), so it's all chaos;
    /// - an airdrop every ClipFarmDropEvery seconds once the wall's down (NetGame.ServerTickScheduledDrops), and purple
    ///   airdrop berry bushes all over the map, like Airdrop Test (NetGame.AirdropBushes.cs);
    /// - HYPE: the big moments go up in huge rainbow words across everyone's screen with an air horn - DOUBLE / TRIPLE /
    ///   QUAD KILL and RAMPAGE (kills ClipFarmMultiWindow s apart or less), LONG SHOT (a kill from ClipFarmLongShot m or
    ///   further, with the distance), HEADSHOT, KILLING SPREE / UNSTOPPABLE / GODLIKE (3, 5, 8 kills without dying),
    ///   SHUTDOWN (ending someone's spree), REVENGE (killing whoever killed you last) and YEET (the one-use knockback stick);
    /// - each one is also marked on Steam's game recording timeline for the players in it (Steam > Settings > Game
    ///   Recording), so the clip's easy to find and save afterwards (SteamClips).
    /// </summary>
    public static partial class Cfg
    {
        public static bool ClipFarm => Rules == GameRules.ClipFarm;
        [Tune("Clip Farm")] public static float ClipFarmDropEvery = 45f, ClipFarmMultiWindow = 8f, ClipFarmLongShot = 40f;
    }

    public partial class NetGame
    {
        class ClipStats { public int Streak, Multi; public float LastKill = -100f; public ulong LastKilledBy = ulong.MaxValue; }
        readonly Dictionary<ulong, ClipStats> m_Clip = new Dictionary<ulong, ClipStats>();
        ClipStats Clip(PlayerNet p) { if (!m_Clip.TryGetValue(p.NetworkObjectId, out var s)) m_Clip[p.NetworkObjectId] = s = new ClipStats(); return s; }

        /// <summary>(tests) the last hype sent: its title and words.</summary>
        public static string LastHype = "", LastHypeSub = "";
        public static int HypeCount;

        /// <summary>Server, Clip Farm: the wall's down - everyone gets an airdrop item.</summary>
        void ServerClipFarmLoadUp()
        {
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value) continue;
                var loot = RollAirdropLoot(p);
                p.ServerGive(loot.Id, loot.Count, loot.Data);
                p.NotifyPublic($"CLIP FARM: you got {Cfg.ItemName(loot.Id)} - go make a clip");
            }
        }

        /// <summary>Server, Clip Farm: someone died - is it a moment? (killer null: nobody killed them)</summary>
        public void ServerClipKill(PlayerNet victim, PlayerNet killer, bool head)
        {
            if (!Cfg.ClipFarm || victim == null) return;
            var vs = Clip(victim);
            int victimStreak = vs.Streak;
            vs.Streak = 0; vs.Multi = 0;
            if (killer == null || killer == victim) return;
            var ks = Clip(killer);
            float now = Time.time;
            ks.Multi = now - ks.LastKill <= Cfg.ClipFarmMultiWindow ? ks.Multi + 1 : 1;
            ks.LastKill = now;
            ks.Streak++;
            bool revenge = ks.LastKilledBy == victim.NetworkObjectId;
            if (revenge) ks.LastKilledBy = ulong.MaxValue;
            vs.LastKilledBy = killer.NetworkObjectId;
            float dist = Vector3.Distance(killer.transform.position, victim.transform.position);
            string who = killer.DisplayName, them = victim.DisplayName;
            // the biggest moment wins the screen
            string title = null, sub = null;
            if (ks.Multi >= 5) { title = "RAMPAGE"; sub = $"{who}: {ks.Multi} kills in a row"; }
            else if (ks.Multi == 4) { title = "QUAD KILL"; sub = who; }
            else if (ks.Multi == 3) { title = "TRIPLE KILL"; sub = who; }
            else if (ks.Multi == 2) { title = "DOUBLE KILL"; sub = who; }
            else if (dist >= Cfg.ClipFarmLongShot) { title = "LONG SHOT"; sub = $"{who} got {them} from {dist:0} m"; }
            else if (victimStreak >= 3) { title = "SHUTDOWN"; sub = $"{who} ended {them}'s {victimStreak} kill streak"; }
            else if (revenge) { title = "REVENGE"; sub = $"{who} got {them} back"; }
            else if (head) { title = "HEADSHOT"; sub = $"{who} on {them}"; }
            else if (ks.Streak == 3) { title = "KILLING SPREE"; sub = $"{who}: 3 kills without dying"; }
            else if (ks.Streak == 5) { title = "UNSTOPPABLE"; sub = $"{who}: 5 kills without dying"; }
            else if (ks.Streak >= 8 && ks.Streak % 4 == 0) { title = "GODLIKE"; sub = $"{who}: {ks.Streak} kills without dying"; }
            if (title != null) ServerClipHype(title, sub, killer, victim);
        }

        /// <summary>Server: a hype moment on everyone's screen (and on the Steam timeline of the two players in it).</summary>
        public void ServerClipHype(string title, string sub, PlayerNet a, PlayerNet b)
        {
            if (!IsServer || !IsSpawned) return;
            LastHype = title; LastHypeSub = sub ?? ""; HypeCount++;
            ClipHypeRpc(new FixedString32Bytes(title), new FixedString128Bytes(sub != null && sub.Length > 120 ? sub.Substring(0, 120) : sub ?? ""),
                a != null ? a.NetworkObjectId : ulong.MaxValue, b != null ? b.NetworkObjectId : ulong.MaxValue);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ClipHypeRpc(FixedString32Bytes title, FixedString128Bytes sub, ulong a, ulong b)
        {
            string t = title.ToString(), s = sub.ToString();
            Hud.BigFade(t, s);
            Sfx.Play2D(Sfx.AirHorn, 0.55f, 0f);
            Fx.Shake(0.3f);
            var me = PlayerNet.Local;
            if (me != null && (me.NetworkObjectId == a || me.NetworkObjectId == b))
                SteamClips.Mark(t, s, me.NetworkObjectId == b && t != "YEET" ? "steam_death" : "steam_starburst");
        }
    }

    /// <summary>Steam's game recording: a marker on the timeline at a big moment (Clip Farm), so it's easy to find and clip.
    /// Nothing happens without Steam (or with a Steam too old to have game recording).</summary>
    public static class SteamClips
    {
        static bool s_Broken;
        /// <summary>(tests) the markers asked for (whether or not Steam's there to take them).</summary>
        public static int Asked;

        public static void Mark(string title, string desc, string icon)
        {
            Asked++;
            if (s_Broken || !SteamBoot.Ready) return;
            try
            {
                Steamworks.SteamTimeline.AddInstantaneousTimelineEvent(title, desc, icon, 1, 0f, Steamworks.ETimelineEventClipPriority.k_ETimelineEventClipPriority_Featured);
            }
            catch (System.Exception e) { s_Broken = true; Debug.LogWarning("[RockGame] Steam timeline: " + e.Message); }
        }
    }

    public partial class Hud
    {
        static string s_BigSub;
        /// <summary>Big words across the middle that fade away, with a line under them.</summary>
        public static void BigFade(string text, string sub) { BigFade(text); s_BigSub = sub; }
        /// <summary>For the tests: the line under the big fading words.</summary>
        public static string BigFadeSubShown => BigFadeShown != null ? s_BigSub : null;
    }

    public static partial class Sfx
    {
        static AudioClip s_AirHorn;
        /// <summary>The air horn: BWAA BWA BWAAAAA.</summary>
        public static AudioClip AirHorn => s_AirHorn != null ? s_AirHorn : (s_AirHorn = MakeWacky("airhorn", 1.25f, (t, d, n) =>
        {
            // three blasts: short, short, long
            float on = t < 0.2f ? t : t < 0.26f ? -1f : t < 0.42f ? t - 0.26f : t < 0.48f ? -1f : t - 0.48f;
            if (on < 0f) return 0f;
            float len = t < 0.2f ? 0.2f : t < 0.42f ? 0.16f : d - 0.48f;
            float env = Mathf.Clamp01(on * 60f) * Mathf.Clamp01((len - on) * 25f);
            float f0 = 466f + Mathf.Sin(t * 2f * Mathf.PI * 7f) * 4f;
            float v = 0f;
            foreach (float m in new[] { 1f, 1.26f, 1.5f }) // (a brassy chord)
                for (int h = 1; h <= 6; h++) v += Mathf.Sin(2f * Mathf.PI * f0 * m * h * t + h) / h;
            return Mathf.Clamp(v * 0.35f, -0.9f, 0.9f) * env + n * 0.03f * env;
        }));
    }
}
