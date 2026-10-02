using UnityEngine;

namespace RockGame
{
    /// <summary>Every rebindable action (pause menu > Controls). Esc always pauses and can't be rebound.</summary>
    public enum Bind
    {
        Forward, Back, Left, Right, Jump, Sprint, Crouch, Slide,
        Attack, Aim, Interact, Inventory,
        Rotate, Demolish, Upgrade,
        PushToTalk,
        Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5, Hotbar6, Hotbar7,
    }

    /// <summary>Key bindings: a main key and an optional second key per action, saved on this PC.</summary>
    public static class Binds
    {
        public struct Info
        {
            public Bind Bind;
            public string Group, Label, Hint;
            public KeyCode Main, Alt;
        }

        /// <summary>The actions in the order the Controls screen shows them, with their default keys.</summary>
        public static readonly Info[] All =
        {
            new Info { Bind = Bind.Forward, Group = "MOVEMENT", Label = "Move forward", Main = KeyCode.W },
            new Info { Bind = Bind.Back, Group = "MOVEMENT", Label = "Move back", Main = KeyCode.S },
            new Info { Bind = Bind.Left, Group = "MOVEMENT", Label = "Move left", Main = KeyCode.A },
            new Info { Bind = Bind.Right, Group = "MOVEMENT", Label = "Move right", Main = KeyCode.D },
            new Info { Bind = Bind.Jump, Group = "MOVEMENT", Label = "Jump", Hint = "also climbs ladders and flies the jetpack", Main = KeyCode.Space },
            new Info { Bind = Bind.Sprint, Group = "MOVEMENT", Label = "Sprint", Hint = "gallop on a horse", Main = KeyCode.LeftShift },
            new Info { Bind = Bind.Crouch, Group = "MOVEMENT", Label = "Crouch", Hint = "crouch only (never slides)", Main = KeyCode.LeftControl },
            new Info { Bind = Bind.Slide, Group = "MOVEMENT", Label = "Slide", Hint = "press it while running to slide (crouches when standing still)", Main = KeyCode.C },

            new Info { Bind = Bind.Attack, Group = "ACTIONS", Label = "Attack / use", Hint = "hit, gather, place, throw the ball; hold to draw the bow", Main = KeyCode.Mouse0 },
            new Info { Bind = Bind.Aim, Group = "ACTIONS", Label = "Aim / eat", Hint = "eat, aim the crossbow / sniper; hold + Attack throws a spear", Main = KeyCode.Mouse1 },
            new Info { Bind = Bind.Interact, Group = "ACTIONS", Label = "Interact", Hint = "ball, doors, chests, bushes, items, horses, cars", Main = KeyCode.E },
            new Info { Bind = Bind.Inventory, Group = "ACTIONS", Label = "Inventory & crafting", Hint = "crafting works in your base (spears & hatchets anywhere)", Main = KeyCode.Tab },

            new Info { Bind = Bind.Rotate, Group = "BUILDING  (holding the building plan)", Label = "Rotate", Hint = "turns stairs · hold Aim for the building wheel", Main = KeyCode.R },
            new Info { Bind = Bind.Demolish, Group = "BUILDING  (holding the building plan)", Label = "Demolish", Hint = "take down your own piece (half the wood back)", Main = KeyCode.X },
            new Info { Bind = Bind.Upgrade, Group = "BUILDING  (holding the building plan)", Label = "Upgrade to stone", Main = KeyCode.F },

            new Info { Bind = Bind.PushToTalk, Group = "VOICE CHAT", Label = "Push to talk", Hint = "when voice chat is set to push to talk", Main = KeyCode.V },

            new Info { Bind = Bind.Hotbar1, Group = "HOTBAR", Label = "Slot 1", Hint = "the mouse wheel scrolls through them too", Main = KeyCode.Alpha1 },
            new Info { Bind = Bind.Hotbar2, Group = "HOTBAR", Label = "Slot 2", Main = KeyCode.Alpha2 },
            new Info { Bind = Bind.Hotbar3, Group = "HOTBAR", Label = "Slot 3", Main = KeyCode.Alpha3 },
            new Info { Bind = Bind.Hotbar4, Group = "HOTBAR", Label = "Slot 4", Main = KeyCode.Alpha4 },
            new Info { Bind = Bind.Hotbar5, Group = "HOTBAR", Label = "Slot 5", Main = KeyCode.Alpha5 },
            new Info { Bind = Bind.Hotbar6, Group = "HOTBAR", Label = "Slot 6", Main = KeyCode.Alpha6 },
            new Info { Bind = Bind.Hotbar7, Group = "HOTBAR", Label = "Slot 7", Main = KeyCode.Alpha7 },
        };

        const int Count = (int)Bind.Hotbar7 + 1;
        const string SlideMigrateKey = "RockGame.Keys.SlideSplit";
        static readonly KeyCode[] s_Main = new KeyCode[Count], s_Alt = new KeyCode[Count];
        static bool s_Loaded;

        static void Ensure()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            foreach (var i in All)
            {
                s_Main[(int)i.Bind] = (KeyCode)PlayerPrefs.GetInt("RockGame.Key." + i.Bind, (int)i.Main);
                s_Alt[(int)i.Bind] = (KeyCode)PlayerPrefs.GetInt("RockGame.Key2." + i.Bind, (int)i.Alt);
            }
            // once: C used to be crouch's second key (crouch slid when running); now Ctrl only crouches and C is its own Slide key
            if (PlayerPrefs.GetInt(SlideMigrateKey, 0) == 0)
            {
                PlayerPrefs.SetInt(SlideMigrateKey, 1);
                int c = (int)Bind.Crouch, sl = (int)Bind.Slide;
                if (s_Alt[c] == KeyCode.C) s_Alt[c] = KeyCode.None;
                if (s_Main[c] == KeyCode.C) { s_Main[c] = s_Alt[c] != KeyCode.None ? s_Alt[c] : KeyCode.LeftControl; s_Alt[c] = KeyCode.None; }
                // C goes to Slide unless the player put it on something else themselves
                bool cTaken = false;
                for (int i = 0; i < Count; i++) if (i != sl && (s_Main[i] == KeyCode.C || s_Alt[i] == KeyCode.C)) cTaken = true;
                if (!cTaken && s_Main[sl] == KeyCode.None) s_Main[sl] = KeyCode.C;
                Save();
            }
        }

        public static KeyCode Get(Bind b, bool alt = false) { Ensure(); return alt ? s_Alt[(int)b] : s_Main[(int)b]; }

        public static void Set(Bind b, bool alt, KeyCode key)
        {
            Ensure();
            // a key only does one thing: take it off whatever had it before
            if (key != KeyCode.None)
                for (int i = 0; i < Count; i++)
                {
                    if (s_Main[i] == key && !(i == (int)b && !alt)) { s_Main[i] = s_Alt[i]; s_Alt[i] = KeyCode.None; }
                    if (s_Alt[i] == key && !(i == (int)b && alt)) s_Alt[i] = KeyCode.None;
                }
            if (alt) s_Alt[(int)b] = key; else s_Main[(int)b] = key;
            Save();
        }

        public static void ResetDefaults()
        {
            foreach (var i in All) { s_Main[(int)i.Bind] = i.Main; s_Alt[(int)i.Bind] = i.Alt; }
            s_Loaded = true;
            Save();
        }

        static void Save()
        {
            foreach (var i in All)
            {
                PlayerPrefs.SetInt("RockGame.Key." + i.Bind, (int)s_Main[(int)i.Bind]);
                PlayerPrefs.SetInt("RockGame.Key2." + i.Bind, (int)s_Alt[(int)i.Bind]);
            }
            PlayerPrefs.Save();
        }

        // the tutorial keeps each action locked until the step that teaches it (Tutorial.BindAllowed; always true outside it)
        public static bool Held(Bind b) { Ensure(); return Tutorial.BindAllowed(b) && (Key(s_Main[(int)b]) || Key(s_Alt[(int)b]) || TestHeld(b)); }
        public static bool Down(Bind b) => Tutorial.BindAllowed(b) && RawDown(b);
        public static bool Up(Bind b) { Ensure(); return Tutorial.BindAllowed(b) && (KeyUp(s_Main[(int)b]) || KeyUp(s_Alt[(int)b]) || s_TestUp[(int)b] == Time.frameCount); }
        /// <summary>Pressed this frame, even if the tutorial hasn't unlocked it yet (to say "not yet").</summary>
        public static bool RawDown(Bind b) { Ensure(); return KeyDown(s_Main[(int)b]) || KeyDown(s_Alt[(int)b]) || s_TestDown[(int)b] == Time.frameCount; }

        // ---- AutoTest: pretend keys are pressed (they go through the same gates as real ones) ----
        static readonly bool[] s_TestHeld = new bool[Count];
        static readonly int[] s_TestDown = new int[Count], s_TestUp = new int[Count];
        static bool TestHeld(Bind b) => s_TestHeld[(int)b] || s_TestDown[(int)b] == Time.frameCount;
        /// <summary>AutoTest: hold a key down (from the next frame) or let it go.</summary>
        public static void TestHold(Bind b, bool on)
        {
            if (s_TestHeld[(int)b] == on) return;
            s_TestHeld[(int)b] = on;
            if (on) s_TestDown[(int)b] = Time.frameCount + 1; else s_TestUp[(int)b] = Time.frameCount + 1;
        }
        /// <summary>AutoTest: tap a key (down next frame, up the frame after).</summary>
        public static void TestPress(Bind b) { s_TestDown[(int)b] = Time.frameCount + 1; s_TestUp[(int)b] = Time.frameCount + 2; }
        public static void TestReleaseAll() { for (int i = 0; i < Count; i++) TestHold((Bind)i, false); }
        /// <summary>1 / 0 / -1 from two opposite actions (movement axes).</summary>
        public static float Axis(Bind plus, Bind minus) => (Held(plus) ? 1f : 0f) - (Held(minus) ? 1f : 0f);

        /// <summary>Typing (the chat line, a menu search box, a hex code): the game's keys are muted - held and pressed
        /// read as nothing (letting go still counts, so nothing gets stuck).</summary>
        public static bool Muted => Chat.Open || Hud.Typing;

        static bool Key(KeyCode k) => k != KeyCode.None && !Muted && Input.GetKey(k);
        static bool KeyDown(KeyCode k) => k != KeyCode.None && !Muted && Input.GetKeyDown(k);
        static bool KeyUp(KeyCode k) => k != KeyCode.None && Input.GetKeyUp(k);

        /// <summary>What to call the main key in hints ("LMB", "E", "Shift").</summary>
        public static string Name(Bind b) => KeyName(Get(b));

        /// <summary>Tiny label for the hotbar corners.</summary>
        public static string Short(Bind b)
        {
            var k = Get(b);
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            string n = KeyName(k);
            return n.Length > 4 ? n.Substring(0, 4) : n;
        }

        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.None: return "-";
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "Middle mouse";
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.Mouse5: return "Mouse 6";
                case KeyCode.Mouse6: return "Mouse 7";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.Space: return "Space";
                case KeyCode.Return: return "Enter";
                case KeyCode.BackQuote: return "`";
                case KeyCode.CapsLock: return "Caps Lock";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
            }
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num " + (int)(k - KeyCode.Keypad0);
            return k.ToString();
        }

        /// <summary>Rebinding: the key or mouse button pressed right now (None if nothing).</summary>
        public static KeyCode Pressed()
        {
            if (s_Keys == null)
            {
                var l = new System.Collections.Generic.List<KeyCode>();
                foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                    if (k != KeyCode.None && k != KeyCode.Escape && k <= KeyCode.Mouse6 && !l.Contains(k)) l.Add(k);
                s_Keys = l.ToArray();
            }
            foreach (var k in s_Keys) if (Input.GetKeyDown(k)) return k;
            return KeyCode.None;
        }
        static KeyCode[] s_Keys;
    }
}
