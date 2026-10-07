using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest looks (at its start): the designer's Settings > Display export that was made the defaults
        /// (Resources/Settings/DefaultDisplay.txt) against each setting's DEFAULT in the code (not its value on this PC):
        /// every key in the file that the game still has must default to the file's value. Keys the game no longer has
        /// are listed, not failed.
        /// </summary>
        static void DefaultsMatchExport()
        {
            var file = Resources.Load<TextAsset>("Settings/DefaultDisplay");
            if (file == null) { Check(false, "DEFAULTS: Resources/Settings/DefaultDisplay.txt is missing"); return; }
            var wrong = new List<string>();
            var gone = new List<string>();
            int matched = 0;
            foreach (var raw in file.text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(DisplayCode.Header, StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                // (a "#" starts a comment - except the one a colour starts with: as DisplayCode.Apply reads it)
                int hash = val.IndexOf('#', val.StartsWith("#") ? 1 : 0);
                if (hash >= 0) val = val.Substring(0, hash).Trim();
                var e = DisplayCode.Find(key);
                if (e == null) { gone.Add(key); continue; }
                if (SameSettingValue(e.Default, val)) matched++;
                else wrong.Add($"{key} (default {e.Default}, file {val})");
            }
            if (gone.Count > 0) Log($"DEFAULTS: keys in the export the game no longer has (ignored): {string.Join(", ", gone)}");
            Check(wrong.Count == 0 && matched > 100,
                wrong.Count == 0 ? $"DEFAULTS: every one of the export's {matched} settings defaults to its value"
                    : $"DEFAULTS: {wrong.Count} setting(s) don't default to the export's value: {string.Join("; ", wrong)}");
        }

        /// <summary>Two values from the display settings code are the same (numbers within rounding, the rest ignoring case).</summary>
        static bool SameSettingValue(string a, string b)
        {
            a = (a ?? "").Trim(); b = (b ?? "").Trim();
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            return float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && Mathf.Abs(x - y) < 0.0002f;
        }
    }
}
