using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// CHANGE VALUES > Export: every game value (and the main menu choices) written to a text file in the game's folder,
    /// so it can be sent over as "make these the new defaults" or "make a game mode out of this". Import reads it back.
    /// </summary>
    public static class SettingsFile
    {
        public const string FileName = "RockBaseBrawl-settings.txt";

        /// <summary>The game's folder: next to the .exe (the project folder in the editor).</summary>
        public static string Folder => Directory.GetParent(Application.dataPath).FullName;
        public static string FilePath => Path.Combine(Folder, FileName);

        public static string Export()
        {
            try
            {
                var sb = new StringBuilder();
                int key = Bootstrap.MapChoice;
                var mode = (GameMode)((key >> Cfg.ModeShift) & Cfg.ModeMask);
                var size = (key & Cfg.SmallBit) != 0 ? MapSize.Small : (MapSize)((key >> Cfg.SizeShift) & 3);
                sb.AppendLine("# ROCK BASE BRAWL - settings export, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                sb.AppendLine("# Every value is \"Name = value\". Lines marked CHANGED differ from the built-in defaults.");
                sb.AppendLine("# Send this file over and say what it's for (new default settings, or the base of a new game mode).");
                sb.AppendLine();
                sb.AppendLine("[Main menu]");
                sb.AppendLine("PlayerName = " + GameSettings.PlayerName); // (empty = your team colour and number, like Blue1)
                sb.AppendLine("Players = " + Cfg.ModeName(mode));
                sb.AppendLine("Map = " + (MapKind)(key & 15));
                sb.AppendLine("Size = " + Cfg.SizeLabel(size));
                sb.AppendLine("GameMode = " + Cfg.RulesName(Cfg.RulesOf(key)));
                sb.AppendLine("Materials = " + ((key & Cfg.WoodBit) != 0 ? "Wood (normal)" : "Stone (the old normal)"));
                sb.AppendLine();
                sb.AppendLine("[Mode options]");
                sb.AppendLine("AirdropsLand = " + ((key & Cfg.CenterBit) != 0 ? "Middle of the map" : (key & Cfg.SidesBit) != 0 ? "One per side" : "Anywhere"));
                sb.AppendLine("Respawn = " + ((key & Cfg.RespawnLootBit) != 0 ? "With an airdrop item" : "Normal"));
                var items = new StringBuilder();
                foreach (var it in Cfg.AirdropLoot) items.Append(items.Length > 0 ? ", " : "").Append(Cfg.ItemName(it));
                sb.AppendLine("# airdrop items: " + items);
                string section = null;
                int changed = 0;
                foreach (var f in Cfg.TuneFields)
                {
                    var sec = Cfg.SectionOf(f);
                    if (sec != section)
                    {
                        if (section != null || sec != "Mode options") { sb.AppendLine(); sb.AppendLine("[" + sec + "]"); }
                        section = sec;
                    }
                    bool def = Cfg.IsDefault(f);
                    if (!def) changed++;
                    sb.Append(f.Name).Append(" = ").Append(Cfg.Format(f));
                    if (!def) sb.Append("    # CHANGED (default ").Append(Cfg.FormatDefault(f)).Append(')');
                    sb.AppendLine();
                }
                sb.AppendLine();
                sb.AppendLine($"# {changed} value{(changed == 1 ? "" : "s")} changed from the defaults.");
                File.WriteAllText(FilePath, sb.ToString());
                Debug.Log("[RockGame] Settings exported to " + FilePath);
                return FilePath;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RockGame] Export failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Reads the values back from the file. Returns how many were set; err says why nothing happened.</summary>
        public static int Import(out string err)
        {
            err = null;
            if (!File.Exists(FilePath)) { err = $"There's no {FileName} in the game folder yet (Export makes one)."; return 0; }
            int n = 0;
            try
            {
                var fields = new System.Collections.Generic.Dictionary<string, System.Reflection.FieldInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Cfg.TuneFields) fields[f.Name] = f;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    string line = raw;
                    // your name (the main menu's name box): the whole rest of the line, whatever is in it
                    if (line.TrimStart().StartsWith("PlayerName", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') > 0)
                    {
                        GameSettings.PlayerName = line.Substring(line.IndexOf('=') + 1);
                        n++;
                        continue;
                    }
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line.Substring(0, hash);
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    if (fields.TryGetValue(name, out var fi) && Cfg.TrySet(fi, value)) n++;
                }
                Cfg.SavePrefs();
            }
            catch (Exception e) { err = "Couldn't read the file: " + e.Message; }
            return n;
        }
    }
}
