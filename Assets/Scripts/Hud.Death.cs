using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Dead:
    /// - THE KILL CAM (PlayerController.KillCam.cs) and its REPLAY (DeathReplay.cs): cinematic black bars slide in;
    ///   the bottom one says who it was in their team colour, with what and how much health they've left; during the
    ///   replay what the killer saw - their crosshair, hit markers and damage numbers (DrawReplayHits) - a blinking REPLAY
    ///   tag (SLOW MOTION around the death) and how to skip it.
    /// - THE DEATH SCREEN: the edges darken to blood red, YOU DIED slams in big with a thin line drawn out under it, and
    ///   below that a bar fills up to the respawn - or the respawn choices as dark cards with their key on the left
    ///   (green while the mouse is on one).
    /// </summary>
    public partial class Hud
    {
        /// <summary>The death screen's red accent (Settings > Display > HUD & TIMER > DEATH SCREEN: DeathLooks.cs).</summary>
        static Color k_DeathRed => GameSettings.DeathAccent.Value;
        float m_DeadSince = -1f, m_TestDeadSince = -1f;

        /// <summary>The death screen's backdrop and YOU DIED `since` seconds in (its own look: DeathLooks.cs). Returns the
        /// y under it, where the respawn bar / choices go.</summary>
        float DrawYouDied(float since, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, sh = Screen.height, cx = sw / 2f;
            float vig = GameSettings.DeathVignette.Value, zs = GameSettings.DeathSize.Value, ink = GameSettings.DeathInk.Value;
            var font = GameSettings.DeathFontNow;
            // the screen: darker and redder towards the edges
            if (vig > 0.001f)
            {
                Fill(new Rect(0, 0, sw, sh), new Color(0.06f, 0f, 0f, Mathf.Clamp01(0.35f * vig)));
                float edge = sh * 0.22f * Mathf.Max(1f, vig);
                for (int i = 0; i < 12; i++)
                {
                    float a = 0.07f * (1f - i / 12f) * vig;
                    Fill(new Rect(0, i * edge / 12f, sw, edge / 12f), new Color(0.2f, 0f, 0f, a * 3f));
                    Fill(new Rect(0, sh - (i + 1) * edge / 12f, sw, edge / 12f), new Color(0.2f, 0f, 0f, a * 3f));
                    Fill(new Rect(i * edge / 12f, 0, edge / 12f, sh), new Color(0.2f, 0f, 0f, a * 2f));
                    Fill(new Rect(sw - (i + 1) * edge / 12f, 0, edge / 12f, sh), new Color(0.2f, 0f, 0f, a * 2f));
                }
            }
            // YOU DIED: slams in from big, then a line drawn out under it
            float slam = Mathf.Clamp01(since / 0.35f);
            float size = 92f * k * zs * (1f + 0.6f * (1f - slam) * (1f - slam));
            float ty = sh * 0.3f + GameSettings.DeathY.Value * k;
            var st = new GUIStyle(m_Big) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) st.font = font;
            if (GameSettings.DeathGlow.Value) Tinted(new Rect(cx - 420 * k * zs, ty - 160 * k * zs, 840 * k * zs, 320 * k * zs), s_GlowTex, new Color(0.8f, 0.05f, 0.05f, 0.35f * slam));
            var fill = GameSettings.DeathTextColour.Value; fill.a = slam;
            var edgeC = GameSettings.DeathInkColour.Value; edgeC.a = 0.9f * slam;
            DeathInkPx = 4f * k * ink; DeathFontPx = st.fontSize; DeathShownFrame = Time.frameCount; // (tests)
            InkText(new Rect(0, ty - 60 * k * zs, sw, 120 * k * zs), "YOU DIED", st, fill, edgeC, 4f * k * ink);
            float half = 60 * k * zs; // (the line and what's under it follow YOU DIED's size)
            if (GameSettings.DeathLine.Value)
            {
                float lw = 560 * k * zs * Smooth(Mathf.Clamp01((since - 0.2f) / 0.5f));
                Tinted(new Rect(cx - lw / 2f, ty + half - 2 * k, lw, 3 * k), s_BandTex, k_DeathRed);
            }
            return ty + half + 20 * k;
        }

        /// <summary>(Settings > Display > DEATH SCREEN: Show a test death screen) YOU DIED with a respawn bar going round
        /// every 5 s, over everything (DrawTestNotification).</summary>
        void DrawTestDeath(float k)
        {
            if (!TestDeathScreen) return;
            if (m_TestDeadSince < 0f) m_TestDeadSince = Time.unscaledTime;
            float since = Time.unscaledTime - m_TestDeadSince;
            float y = DrawYouDied(since, k) + 14 * k;
            DrawRespawnBar(y, 1f - Mathf.Repeat(since, 5f) / 5f * 1f, 5f - Mathf.Repeat(since, 5f), true, k);
        }

        /// <summary>The respawn bar (`fill` 0..1) and RESPAWNING ... IN n over it.</summary>
        void DrawRespawnBar(float y, float fill, float t, bool home, float k)
        {
            float sw = Screen.width, cx = sw / 2f;
            float ink = GameSettings.DeathInk.Value;
            var font = GameSettings.DeathFontNow;
            if (GameSettings.DeathBar.Value)
            {
                float w = 360 * k, h = 6 * k;
                var bar = new Rect(cx - w / 2f, y + 34 * k, w, h);
                Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fill), bar.height), Color.Lerp(k_DeathRed, Color.white, fill * fill));
            }
            var lab = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(22 * k), alignment = TextAnchor.MiddleCenter };
            if (font != null) lab.font = font;
            string num = ColorUtility.ToHtmlStringRGB(Color.Lerp(k_DeathRed, Color.white, 0.35f));
            InkText(new Rect(0, y, sw, 30 * k), (home ? "RESPAWNING ON YOUR BEDROCK IN " : "RESPAWNING IN ") + $"<color=#{num}>{Mathf.CeilToInt(t)}</color>", lab, Color.white, new Color(0f, 0f, 0f, 0.85f), 2f * k * ink);
        }

        void DrawDeath(NetGame game, PlayerNet me, PlayerController pc, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, cx = sw / 2f;
            if (m_DeadSince < 0f || Time.time - m_DeadSince > 60f) m_DeadSince = Time.time;
            var killer = pc.KillCamTarget;
            if (killer != null) { DrawKillCam(killer, pc, k); return; }
            float since = Time.time - m_DeadSince;
            float y = DrawYouDied(since, k); // (the backdrop and YOU DIED: their own look, DeathLooks.cs)
            var small = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(17 * k) };
            if (game != null && !game.CanRespawn(me.Team.Value))
            {
                var est = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * k) };
                if (GameSettings.DeathFontNow != null) est.font = GameSettings.DeathFontNow;
                InkText(new Rect(0, y, sw, 30 * k), $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(k_DeathRed, Color.white, 0.3f))}>ELIMINATED</color>", est, Color.white, new Color(0f, 0f, 0f, 0.8f), 2f * k * GameSettings.DeathInk.Value);
                Shadowed(new Rect(0, y + 34 * k, sw, 26 * k), "Your machine is destroyed - there's no coming back. Cheer your team on!", small);
                return;
            }
            y += 14 * k;
            if (me.ChoosingRespawn)
            {
                float bw = 300 * k, bh = 54 * k;
                if (DeathCard(new Rect(cx - bw - 8 * k, y, bw, bh), "1", "RESPAWN IN BASE", "", true, KeyCode.Alpha1)) pc.ChooseRespawn(false);
                if (DeathCard(new Rect(cx + 8 * k, y, bw, bh), "2", "RESPAWN IN THE WILD", "", true, KeyCode.Alpha2)) pc.ChooseRespawn(true);
                // your team's sleeping bags (Container.Deployables.cs): one more card each, once a minute each
                Deployables.BagsOf(me.Team.Value, s_Bags);
                double now = me.NetworkManager.ServerTime.Time;
                float by = y + bh + 12 * k;
                for (int i = 0; i < s_Bags.Count && i < 4; i++)
                {
                    var bag = s_Bags[i];
                    float wait = Mathf.Max(0f, (float)(bag.ReadyAt.Value - now));
                    float dist = Vector3.Distance(bag.transform.position, Cfg.BaseCenter[Mathf.Clamp(me.Team.Value, 0, 3)]);
                    var rb = new Rect(cx - bw - 8 * k, by + i * (bh * 0.8f + 8 * k), bw * 2f + 16 * k, bh * 0.8f);
                    if (DeathCard(rb, (i + 3).ToString(), "SLEEPING BAG", wait > 0f ? $"ready in {Mathf.CeilToInt(wait)} s" : $"{dist:0} m from base", wait <= 0f, KeyCode.Alpha3 + i))
                        me.RespawnAtBagRpc(bag.NetworkObject);
                }
                return;
            }
            // the bar filling up to the respawn
            float t = Mathf.Max(0, (float)(me.RespawnAt.Value - me.NetworkManager.ServerTime.Time));
            float fill = 1f - Mathf.Clamp01(t / Mathf.Max(0.1f, Cfg.RespawnTime));
            DrawRespawnBar(y, fill, t, game == null || game.WallUp, k);
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>One respawn choice: a dark card, its key in a box on the left; true when it's clicked (or its key pressed).</summary>
        bool DeathCard(Rect r, string key, string title, string sub, bool on, KeyCode code)
        {
            float k = m_Scale;
            bool hover = on && r.Contains(Event.current.mousePosition);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, hover ? new Color(0.06f, 0.34f, 0.12f, 0.92f) : new Color(0.04f, 0.02f, 0.02f, on ? 0.82f : 0.55f));
            Fill(new Rect(r.x, r.y, 4 * k, r.height), hover ? new Color(0.35f, 1f, 0.45f) : on ? k_DeathRed : new Color(0.4f, 0.4f, 0.4f));
            Frame(r, new Color(1f, 1f, 1f, hover ? 0.7f : 0.18f), 1f);
            var kr = new Rect(r.x + 14 * k, r.y + (r.height - 28 * k) / 2f, 28 * k, 28 * k);
            Frame(kr, new Color(1f, 1f, 1f, on ? 0.85f : 0.3f), 1.5f);
            var kst = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(16 * k), fontStyle = FontStyle.Bold };
            ShadowedPlain(kr, key, kst);
            var tst = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(18 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var col = on ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            tst.normal.textColor = col;
            string text = sub.Length > 0 ? $"{title}   <size={Mathf.RoundToInt(14 * k)}><color=#{(on ? "c9b4b0" : "777777")}>{sub}</color></size>" : title;
            ShadowedPlain(new Rect(kr.xMax + 14 * k, r.y, r.width - 60 * k, r.height), text, tst);
            bool click = on && GUI.Button(r, GUIContent.none, GUIStyle.none);
            bool press = on && Event.current.type == EventType.KeyDown && Event.current.keyCode == code;
            if (press) Event.current.Use();
            if (click || press) ClickSound();
            return click || press;
        }

        void DrawKillCam(PlayerNet killer, PlayerController pc, float k)
        {
            float sw = Screen.width, sh = Screen.height;
            float since = Time.time - m_DeadSince;
            // cinematic bars sliding in
            float bar = sh * 0.11f * Smooth(Mathf.Clamp01(since / 0.4f));
            Fill(new Rect(0, 0, sw, bar), Color.black);
            Fill(new Rect(0, sh - bar, sw, bar), Color.black);
            Fill(new Rect(0, sh - bar, sw * Smooth(Mathf.Clamp01(since / 0.7f)), 2 * k), k_DeathRed);
            var kc = Cfg.TeamColor[Mathf.Clamp(killer.Team.Value, 0, 3)];
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(kc, Color.white, 0.3f));
            // who it was, in the bottom bar
            float by = sh - bar;
            var cap = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(14 * k), fontStyle = FontStyle.Bold };
            ShadowedPlain(new Rect(0, by + bar * 0.12f, sw, 20 * k), "<color=#ff7a6e>K I L L E D   B Y</color>", cap);
            var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(34 * k) };
            InkText(new Rect(0, by + bar * 0.12f + 18 * k, sw, 42 * k), $"<color=#{hex}>{killer.DisplayName}</color>", big, Color.white, new Color(0f, 0f, 0f, 0.9f), 2f * k);
            string with = killer.HeldItem == Item.None || killer.HeldItem == Item.Rock ? "a rock" : Cfg.ItemName(killer.HeldItem);
            ShadowedPlain(new Rect(0, by + bar * 0.12f + 60 * k, sw, 22 * k), $"<color=#c8c8c8>with {with}  ·  </color><color=#ff8080>{Mathf.CeilToInt(killer.Health.Value)} HP</color><color=#c8c8c8> left</color>"
                + (killer.ArmorHp.Value > 0 ? $"<color=#c8c8c8> (+{killer.ArmorHp.Value} armour)</color>" : ""), new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(15 * k) });
            if (!pc.KillCamReplay) return;
            // the replay: what the killer saw on their screen - their crosshair, the hit markers and damage numbers
            DrawReplayHits(k);
            // a blinking REPLAY tag, slow motion at the end, and how to skip it
            float rt = pc.KillCamReplayT;
            bool blink = Mathf.Repeat(Time.time, 1f) < 0.6f;
            var tag = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            float tx = 40 * k, tyy = bar * 0.5f - 12 * k;
            if (blink) Tinted(new Rect(tx, tyy + 5 * k, 14 * k, 14 * k), s_GlowTex, k_DeathRed);
            Fill(new Rect(tx + 3 * k, tyy + 8 * k, 8 * k, 8 * k), blink ? k_DeathRed : new Color(0.4f, 0.1f, 0.1f));
            ShadowedPlain(new Rect(tx + 22 * k, tyy, 400 * k, 24 * k), DeathReplay.Slow(rt) ? "REPLAY  <color=#ff7a6e>·  SLOW MOTION</color>" : "REPLAY  <color=#bbbbbb>·  THEIR VIEW</color>", tag);
            var skip = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleRight };
            ShadowedPlain(new Rect(sw - 440 * k, tyy, 400 * k, 24 * k), $"<color=#bbbbbb>{Binds.Name(Bind.Jump)}  skip</color>", skip);
            // its progress along the top bar's edge
            Fill(new Rect(0, bar - 2 * k, sw * Mathf.Clamp01(rt / DeathReplay.Duration), 2 * k), new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>Test hook: when the replay last drew a damage number (Time.time; -1 never).</summary>
        public static float ReplayNumberShownAt { get; private set; } = -1f;

        /// <summary>
        /// The kill cam's replay, as the killer saw it: their crosshair in the middle, the ticks of a hit marker round it as
        /// each hit on you lands (red for the kill), and the damage numbers popping up off you - all on the replay's own
        /// clock, so they slow down with it.
        /// </summary>
        void DrawReplayHits(float k)
        {
            float sw = Screen.width, sh = Screen.height, cx = sw / 2f, cy = sh / 2f;
            Fill(new Rect(cx - 1, cy - 8, 2, 16), new Color(1, 1, 1, 0.8f));
            Fill(new Rect(cx - 8, cy - 1, 16, 2), new Color(1, 1, 1, 0.8f));
            // the hit marker
            float age = DeathReplay.HitMarkerAge(out bool kill);
            float dur = kill ? 0.55f : 0.32f;
            if (age >= 0f && age < dur)
            {
                var hc = kill ? new Color(1f, 0.2f, 0.15f) : Color.white;
                float a = Mathf.Clamp01((dur - age) / 0.16f);
                float pop = 1f - Mathf.Clamp01(age / 0.09f);
                float gap = (kill ? 8f : 6f) * k + pop * 6f * k + (kill ? age * 18f * k : 0f);
                float len = (kill ? 11f : 7f) * k * (1f + pop * 0.35f);
                float thick = Mathf.Max(2f, (kill ? 3f : 2f) * k);
                var oldM = GUI.matrix;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < 4; i++)
                    {
                        float ang = 45f + i * 90f, rad = ang * Mathf.Deg2Rad;
                        var c = new Vector2(cx, cy) + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (gap + len * 0.5f);
                        GUI.matrix = oldM;
                        GUIUtility.RotateAroundPivot(ang, c);
                        if (pass == 0) Fill(new Rect(c.x - len / 2 - 1, c.y - thick / 2 - 1, len + 2, thick + 2), new Color(0, 0, 0, 0.55f * a));
                        else Fill(new Rect(c.x - len / 2, c.y - thick / 2, len, thick), new Color(hc.r, hc.g, hc.b, a));
                    }
                GUI.matrix = oldM;
            }
            // the damage numbers: pop in, drift up, fade (white, red for the kill)
            var cam = Camera.main;
            if (cam == null) return;
            const float Life = 1.2f;
            float bar = sh * 0.11f;
            for (int i = 0; i < DeathReplay.HitCount; i++)
            {
                if (!DeathReplay.GetHit(i, out var pos, out float amount, out float nage, out bool nk) || nage > Life) continue;
                var sp = cam.WorldToScreenPoint(pos + Vector3.up * 0.35f);
                if (sp.z < 0) continue;
                float a = Mathf.Clamp01((Life - nage) / 0.3f);
                float pop = nage < 0.06f ? Mathf.Lerp(0.55f, 1.5f, nage / 0.06f) : Mathf.Lerp(1.5f, 1f, Mathf.SmoothStep(0f, 1f, (nage - 0.06f) / 0.16f));
                float rise = (1f - Mathf.Exp(-nage * 2.2f)) * 55f * k;
                float size = (nk ? 30f : 20f) * pop;
                float dist = Vector3.Distance(cam.transform.position, pos);
                float x = sp.x + (i % 2 == 0 ? 10f : -10f) * k, y = sh - sp.y - rise;
                if (dist < 4.5f)
                {
                    // (up close it'd sit right on the crosshair: out to the side and above it, bigger)
                    size *= 1.4f;
                    x = Mathf.Max(x, cx + 70 * k);
                    y = Mathf.Min(y, cy - 50 * k - rise * 0.6f);
                }
                x = Mathf.Clamp(x, 60 * k, sw - 60 * k);
                y = Mathf.Clamp(y, bar + 30 * k, sh - bar - 30 * k);
                var st = new GUIStyle(m_Center) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size * k)), fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                var c = nk ? new Color(1f, 0.28f, 0.2f, a) : new Color(1f, 1f, 1f, a);
                var fillC = Color.Lerp(c, new Color(1f, 1f, 1f, a), Mathf.Clamp01(1f - nage / 0.1f) * 0.7f);
                // (its edge: Settings > Display > DAMAGE NUMBERS, as the live ones - HudTextLooks.cs)
                EdgeText(new Rect(x - 80, y - 25, 160, 50), Mathf.RoundToInt(amount).ToString(), st, fillC, GameSettings.DamageEdge.Value, GameSettings.DamageInk.Value * 1.25f * k, new Color(0f, 0f, 0f, a * 0.9f));
                ReplayNumberShownAt = Time.time;
            }
        }
    }
}
