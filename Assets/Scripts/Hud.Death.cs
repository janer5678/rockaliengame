using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Dead:
    /// - THE KILL CAM (PlayerController.KillCam.cs) and its REPLAY (DeathReplay.cs): cinematic black bars slide in;
    ///   the bottom one says who it was in their team colour, with what and how much health they've left; during the
    ///   replay a blinking REPLAY tag (SLOW MOTION for the last moment) and how to skip it.
    /// - THE DEATH SCREEN: the edges darken to blood red, YOU DIED slams in big with a thin line drawn out under it, and
    ///   below that a bar fills up to the respawn - or the respawn choices as dark cards with their key on the left.
    /// </summary>
    public partial class Hud
    {
        static readonly Color k_DeathRed = new Color(1f, 0.22f, 0.2f);
        float m_DeadSince = -1f;

        void DrawDeath(NetGame game, PlayerNet me, PlayerController pc, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, sh = Screen.height, cx = sw / 2f;
            if (m_DeadSince < 0f || Time.time - m_DeadSince > 60f) m_DeadSince = Time.time;
            var killer = pc.KillCamTarget;
            if (killer != null) { DrawKillCam(killer, pc, k); return; }
            float since = Time.time - m_DeadSince;
            // the screen: darker and redder towards the edges
            Fill(new Rect(0, 0, sw, sh), new Color(0.06f, 0f, 0f, 0.35f));
            float edge = sh * 0.22f;
            for (int i = 0; i < 12; i++)
            {
                float a = 0.07f * (1f - i / 12f);
                Fill(new Rect(0, i * edge / 12f, sw, edge / 12f), new Color(0.2f, 0f, 0f, a * 3f));
                Fill(new Rect(0, sh - (i + 1) * edge / 12f, sw, edge / 12f), new Color(0.2f, 0f, 0f, a * 3f));
                Fill(new Rect(i * edge / 12f, 0, edge / 12f, sh), new Color(0.2f, 0f, 0f, a * 2f));
                Fill(new Rect(sw - (i + 1) * edge / 12f, 0, edge / 12f, sh), new Color(0.2f, 0f, 0f, a * 2f));
            }
            // YOU DIED: slams in from big, then a line drawn out under it
            float slam = Mathf.Clamp01(since / 0.35f);
            float size = 92f * k * (1f + 0.6f * (1f - slam) * (1f - slam));
            float ty = sh * 0.3f;
            var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(size), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            Tinted(new Rect(cx - 420 * k, ty - 160 * k, 840 * k, 320 * k), s_GlowTex, new Color(0.8f, 0.05f, 0.05f, 0.35f * slam));
            InkText(new Rect(0, ty - 60 * k, sw, 120 * k), "Y O U   D I E D", st, new Color(1f, 0.93f, 0.9f, slam), new Color(0.25f, 0f, 0f, 0.9f * slam), 4f * k);
            float lw = 560 * k * Smooth(Mathf.Clamp01((since - 0.2f) / 0.5f));
            Tinted(new Rect(cx - lw / 2f, ty + 58 * k, lw, 3 * k), s_BandTex, k_DeathRed);
            float y = ty + 80 * k;
            var small = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(17 * k) };
            if (game != null && !game.CanRespawn(me.Team.Value))
            {
                InkText(new Rect(0, y, sw, 30 * k), "<color=#ff6a60>ELIMINATED</color>", new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * k) }, Color.white, new Color(0f, 0f, 0f, 0.8f), 2f * k);
                Shadowed(new Rect(0, y + 34 * k, sw, 26 * k), "Your machine is destroyed - there's no coming back. Cheer your team on!", small);
                return;
            }
            Shadowed(new Rect(0, y, sw, 26 * k), "<color=#d8c0bc>Your stuff spilled out where you died</color>", small);
            y += 40 * k;
            if (me.ChoosingRespawn)
            {
                float bw = 300 * k, bh = 54 * k;
                if (DeathCard(new Rect(cx - bw - 8 * k, y, bw, bh), "1", "RESPAWN IN BASE", "", true, KeyCode.Alpha1)) pc.ChooseRespawn(false);
                if (DeathCard(new Rect(cx + 8 * k, y, bw, bh), "2", "RESPAWN IN THE WILD", "", true, KeyCode.Alpha2)) pc.ChooseRespawn(true);
                Shadowed(new Rect(0, y + bh + 4 * k, sw, 24 * k), "<color=#a89a98>The wild drops you somewhere random in the enemy's half of the map</color>", new GUIStyle(small) { fontSize = Mathf.RoundToInt(14 * k) });
                // your team's sleeping bags (Container.Deployables.cs): one more card each, once a minute each
                Deployables.BagsOf(me.Team.Value, s_Bags);
                double now = me.NetworkManager.ServerTime.Time;
                float by = y + bh + 34 * k;
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
            float w = 360 * k, h = 6 * k;
            var bar = new Rect(cx - w / 2f, y + 34 * k, w, h);
            Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(bar.x, bar.y, bar.width * fill, bar.height), Color.Lerp(k_DeathRed, Color.white, fill * fill));
            bool home = game == null || game.WallUp;
            var lab = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(22 * k), alignment = TextAnchor.MiddleCenter };
            InkText(new Rect(0, y, sw, 30 * k), (home ? "RESPAWNING ON YOUR BEDROCK IN " : "RESPAWNING IN ") + $"<color=#ff7a6e>{Mathf.CeilToInt(t)}</color>", lab, Color.white, new Color(0f, 0f, 0f, 0.85f), 2f * k);
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>One respawn choice: a dark card, its key in a box on the left; true when it's clicked (or its key pressed).</summary>
        bool DeathCard(Rect r, string key, string title, string sub, bool on, KeyCode code)
        {
            float k = m_Scale;
            bool hover = on && r.Contains(Event.current.mousePosition);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, hover ? new Color(0.35f, 0.04f, 0.04f, 0.92f) : new Color(0.04f, 0.02f, 0.02f, on ? 0.82f : 0.55f));
            Fill(new Rect(r.x, r.y, 4 * k, r.height), on ? k_DeathRed : new Color(0.4f, 0.4f, 0.4f));
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
            // the replay: a blinking REPLAY tag, slow motion at the end, and how to skip it
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
    }
}
