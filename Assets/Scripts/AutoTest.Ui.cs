using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest ui -host -solo -map plains -shotdir DIR: one gun shot = one tracer (revolver, sniper; the shotgun's own
    /// pellets and nothing extra), post processing on / off / each effect (pictures, pixel checks, frame times, off in
    /// PSX / AI PSX), the Dev settings search (and the game's keys muted while typing in it), the colour picker (open on
    /// the sky and on the hands; a drag shows straight away and is saved on release) and Settings > Display.
    /// -autotest tracer -host / -client 127.0.0.1 (two players): the client's shot and the host's shot each show one
    /// tracer on both screens.
    /// Everything it changes on this PC (post processing, colours) is put back as it was.
    /// </summary>
    public partial class AutoTest
    {
        static int GunRounds(PlayerNet p, Item gun)
        {
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (p.SlotAt(i).Id == gun) return p.SlotAt(i).Data;
            return -1;
        }

        /// <summary>Mean brightness, colourfulness, the corners' and the middle's brightness, sampled over the screen.</summary>
        struct ScreenStats { public float Lum, Sat, Corner, Center; public Color32[] Px; public int W, H; }

        static IEnumerator Grab(System.Action<ScreenStats> done)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var st = new ScreenStats { W = tex.width, H = tex.height, Px = tex.GetPixels32() };
            Object.Destroy(tex);
            double lum = 0, sat = 0, corner = 0, center = 0;
            int n = 0, nc = 0, nm = 0;
            for (int y = 0; y < st.H; y += 4)
                for (int x = 0; x < st.W; x += 4)
                {
                    var c = st.Px[y * st.W + x];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
                    lum += l; sat += mx > 0.001f ? (mx - mn) / mx : 0f; n++;
                    float u = x / (float)st.W, v = y / (float)st.H;
                    // corners: the outer 12% at both ends, kept above the hotbar (bottom 20%)
                    if ((u < 0.12f || u > 0.88f) && (v > 0.2f && v < 0.32f || v > 0.86f)) { corner += l; nc++; }
                    if (u > 0.4f && u < 0.6f && v > 0.55f && v < 0.75f) { center += l; nm++; }
                }
            st.Lum = (float)(lum / n); st.Sat = (float)(sat / n);
            st.Corner = (float)(corner / Mathf.Max(1, nc)); st.Center = (float)(center / Mathf.Max(1, nm));
            done(st);
        }

        /// <summary>Mean difference per channel (0..255) between two grabs.</summary>
        static float Diff(ScreenStats a, ScreenStats b)
        {
            if (a.Px == null || b.Px == null || a.Px.Length != b.Px.Length) return -1f;
            double d = 0; int n = 0;
            for (int i = 0; i < a.Px.Length; i += 7)
            {
                d += Mathf.Abs(a.Px[i].r - b.Px[i].r) + Mathf.Abs(a.Px[i].g - b.Px[i].g) + Mathf.Abs(a.Px[i].b - b.Px[i].b);
                n += 3;
            }
            return (float)(d / n);
        }

        IEnumerator UiShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            int shot = 0;
            string res = $"{Screen.width}x{Screen.height}";
            // (the picture is taken at the end of the frame: wait a couple of frames before changing anything)
            IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"ui_{res}_{shot++:00}_{name}.png"));
                Log("shot " + name);
                yield return null; yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
            }
            GameSettings.SetGraphics(0, false);
            GameSettings.ResetWorldLook(false);
            // this PC's own settings, put back at the end
            bool pOn = GameSettings.PostFx, pB = GameSettings.PostBloom, pV = GameSettings.PostVignette, pG = GameSettings.PostGrading;
            float pBs = GameSettings.PostBloomStrength, pVs = GameSettings.PostVignetteStrength, pGs = GameSettings.PostGradingStrength;
            string skyKey = "RockGame.World." + ColorSlots.Sky.Id, handKey = "RockGame.World." + ColorSlots.Hands.Id;
            string skyPref = PlayerPrefs.GetString(skyKey, null), handPref = PlayerPrefs.GetString(handKey, null);
            bool hadTeamPref = PlayerPrefs.HasKey("RockGame.HandsTeam");
            int teamPref = PlayerPrefs.GetInt("RockGame.HandsTeam", 1);
            bool handsTeam = ColorSlots.HandsTeam;
            Color skyWas = ColorSlots.Sky.Value, handsWas = ColorSlots.Hands.Value;
            yield return new WaitForSeconds(0.5f);

            var team = me.Team.Value;
            var bc = Cfg.BaseCenter[team];
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            var mid = Ground(Mathf.Lerp(bc.x, 0, 0.45f), Mathf.Lerp(bc.z, 0, 0.45f));
            float outYaw = Quaternion.LookRotation(new Vector3(-bc.x, 0, -bc.z)).eulerAngles.y;

            // ---- one shot, one tracer ----
            pc.LocalTeleport(mid + Vector3.up * 0.1f, outYaw);
            pc.SetLook(outYaw + 40f, -10f);
            me.ServerGive(Item.Revolver, 1, 3);
            yield return Hold(me, Item.Revolver);
            yield return new WaitForSeconds(0.4f);
            for (int s = 0; s < 2; s++)
            {
                int t0 = Fx.TracerCount, r0 = me.HeldStack.Data;
                Binds.TestPress(Bind.Attack);
                yield return null; yield return null; yield return null;
                if (s == 0) yield return Shot("revolver_one_tracer");
                yield return new WaitForSeconds(0.9f);
                Check(Fx.TracerCount - t0 == 1 && me.HeldStack.Data == r0 - 1, $"revolver shot {s + 1}: one tracer ({Fx.TracerCount - t0} drawn, {me.HeldStack.Data} rounds left)");
                yield return new WaitForSeconds(Cfg.GunFireRate(Item.Revolver));
            }
            me.ServerGive(Item.Sniper, 1, Cfg.SniperAmmo);
            yield return Hold(me, Item.Sniper);
            yield return new WaitForSeconds(0.4f);
            {
                int t0 = Fx.TracerCount;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(1.2f);
                Check(Fx.TracerCount - t0 == 1, $"sniper shot: one tracer ({Fx.TracerCount - t0} drawn)");
            }
            me.ServerGive(Item.Shotgun, 1, 1);
            yield return Hold(me, Item.Shotgun);
            yield return new WaitForSeconds(0.4f);
            {
                int t0 = Fx.TracerCount, want = (Mathf.Clamp(Cfg.ShotgunPellets, 1, 30) + 1) / 2;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(1.2f);
                Check(Fx.TracerCount - t0 == want, $"shotgun shot: just its own {want} pellet tracers, no extra one from the server ({Fx.TracerCount - t0} drawn)");
            }
            yield return Hold(me, Item.Rock);

            // ---- post processing: off, on, each effect alone ----
            var sd = SkySun.Direction;
            float sunYaw = Quaternion.LookRotation(new Vector3(sd.x, 0, sd.z)).eulerAngles.y;
            float sunPitch = -Mathf.Asin(Mathf.Clamp(sd.y, -1f, 1f)) * Mathf.Rad2Deg;
            // out in the open meadow (no tall wheat right round us), facing the sun
            var spot = mid;
            var field = GrassField.Current;
            if (field != null)
                for (int k = 0; k < 600; k++)
                {
                    var p = mid + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * k * 0.25f;
                    bool clear = field.WheatAt(p.x, p.z) < 0.01f && Cfg.BaseTeamAt(p) < 0;
                    for (int a = 0; a < 12 && clear; a++)
                    {
                        var o = Quaternion.Euler(0, a * 30f, 0) * Vector3.forward * (a % 2 == 0 ? 5f : 9f);
                        clear = field.WheatAt(p.x + o.x, p.z + o.z) < 0.01f;
                    }
                    if (clear) { spot = Ground(p.x, p.z); break; }
                }
            pc.LocalTeleport(spot + Vector3.up * 0.1f, sunYaw + 28f);
            pc.SetLook(sunYaw + 28f, Mathf.Max(-24f, sunPitch + 22f));
            yield return new WaitForSeconds(1f);
            // time stands still while the pictures are compared (no swaying grass or drifting clouds between them)
            Time.timeScale = 0f;
            ScreenStats off = default, off2 = default, on = default, bloom = default, vig = default, grade = default;
            IEnumerator Look(string name, bool all, bool b, bool v, bool g, System.Action<ScreenStats> got)
            {
                GameSettings.SetPostFx(all, b, v, g, 0.5f, 0.5f, 0.5f, false);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Grab(got);
                if (name != null) yield return Shot(name);
                yield return new WaitForSecondsRealtime(0.2f);
            }
            yield return Look("postfx_off", false, true, true, true, s => off = s);
            Check(!PostFx.CameraOn, "post processing off: the camera renders none");
            yield return Look("postfx_on", true, true, true, true, s => on = s);
            Check(PostFx.CameraOn, "post processing on: the main camera renders it");
            yield return Look("postfx_bloom_only", true, true, false, false, s => bloom = s);
            yield return Look("postfx_vignette_only", true, false, true, false, s => vig = s);
            yield return Look("postfx_grading_only", true, false, false, true, s => grade = s);
            yield return Look(null, false, true, true, true, s => off2 = s);
            Time.timeScale = 1f;
            float noise = Diff(off, off2), dOn = Diff(off, on);
            Log($"post fx pixels: off lum {off.Lum:F3} sat {off.Sat:F3} corners {off.Corner:F3} middle {off.Center:F3} | on lum {on.Lum:F3} sat {on.Sat:F3} corners {on.Corner:F3} middle {on.Center:F3} | "
                + $"bloom lum {bloom.Lum:F3} | vignette corners {vig.Corner:F3} middle {vig.Center:F3} | grading sat {grade.Sat:F3} lum {grade.Lum:F3} | diff on/off {dOn:F2}, off/off {noise:F2}");
            Check(dOn > 1.5f && dOn > noise * 4f + 0.2f, $"post processing changes the picture in the built game (mean difference {dOn:F2} vs {noise:F2} between two plain frames)");
            float dBloom = Diff(off, bloom);
            Check(bloom.Lum > off.Lum && dBloom > noise * 4f + 0.2f, $"bloom adds glow ({off.Lum:F3} -> {bloom.Lum:F3} mean brightness, {dBloom:F2} mean difference)");
            Check(vig.Corner < off.Corner * 0.94f && Mathf.Abs(vig.Center - off.Center) < 0.03f, $"vignette darkens the corners ({off.Corner:F3} -> {vig.Corner:F3}), not the middle ({off.Center:F3} -> {vig.Center:F3})");
            Check(grade.Sat > off.Sat + 0.01f, $"colour grading: richer colour ({off.Sat:F3} -> {grade.Sat:F3})");

            // frame time with it on and off (same view, uncapped)
            IEnumerator Measure(System.Action<float> done)
            {
                int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
                yield return new WaitForSeconds(0.4f);
                float sum = 0; int n = 0;
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) { yield return null; sum += Time.unscaledDeltaTime; n++; }
                QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
                done(sum / Mathf.Max(1, n) * 1000f);
            }
            float tOn = 0, tOff = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                bool p = pass % 2 == 0;
                GameSettings.SetPostFx(p, true, true, true, 0.5f, 0.5f, 0.5f, false);
                float t = 0;
                yield return Measure(v => t = v);
                if (p) tOn += t / 3f; else tOff += t / 3f;
            }
            Log($"frame time at {res}: post processing on {tOn:F2} ms, off {tOff:F2} ms (cost {tOn - tOff:F2} ms)");

            // PSX and AI PSX keep their own look: no post processing there
            GameSettings.SetPostFx(true, true, true, true, 0.5f, 0.5f, 0.5f, false);
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            Check(!PostFx.CameraOn, "PSX: no post processing");
            GameSettings.SetGraphics(2, false);
            yield return new WaitForSeconds(0.8f);
            Check(!PostFx.CameraOn, "AI PSX: no post processing");
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Check(PostFx.CameraOn, "Normal again: post processing back on");

            // ---- the Dev settings search ----
            pc.Paused = true;
            Hud.OpenPause = 3;
            Hud.SetDevSearch = "ball";
            yield return new WaitForSeconds(0.8f);
            yield return Shot("dev_search_ball");
            Check(Hud.DevMatches == 3, $"Dev settings search \"ball\": the 3 ball buttons ({Hud.DevMatches})");
            Hud.SetDevSearch = "wood";
            yield return new WaitForSeconds(0.5f);
            Check(Hud.DevMatches == 1, $"Dev settings search \"wood\": +1000 wood ({Hud.DevMatches})");
            Hud.SetDevSearch = "teleport";
            yield return new WaitForSeconds(0.5f);
            yield return Shot("dev_search_teleport_section");
            Check(Hud.DevMatches == 4, $"Dev settings search \"teleport\": the whole TELEPORT section ({Hud.DevMatches})");
            Hud.FocusSearch("search dev");
            yield return null; yield return null; yield return null;
            Check(Hud.Typing && Binds.Muted, "typing in the search box mutes the game's keys");
            yield return Shot("dev_search_typing");
            yield return new WaitForSeconds(0.3f);
            Check(Hud.BackOut() && pc.Paused, "Esc while typing lets go of the search box (and stays in the menu)");
            yield return null; yield return null; yield return null;
            Check(!Hud.Typing && !Binds.Muted, "the game's keys work again once the search box lets go");
            Hud.SetDevSearch = "zzqx";
            yield return new WaitForSeconds(0.5f);
            yield return Shot("dev_search_nothing");
            Check(Hud.DevMatches == 0, "a search with no match shows nothing (and says so)");
            Hud.SetDevSearch = "";
            yield return new WaitForSeconds(0.4f);
            Check(Hud.DevMatches == -1, "cleared: every dev setting again");

            // ---- Settings > Display: post processing rows, the colour picker ----
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("settings_display_postfx");
            Hud.OpenPickerFor = ColorSlots.Sky.Index;
            Hud.ScrollToColours = true;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("picker_sky_open");
            Check(Hud.PickerSlot == ColorSlots.Sky.Index, "the colour picker opens on the sky's row");
            var picked = Color.HSVToRGB(0.07f, 0.55f, 0.95f);
            string savedBefore = PlayerPrefs.GetString(skyKey, "");
            Hud.TestPick(0.07f, 0.55f, 0.95f, false);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("picker_sky_dragging");
            Check(ColorSlots.Same(ColorSlots.Sky.Value, picked) && PlayerPrefs.GetString(skyKey, "") == savedBefore, $"dragging in the picker shows the colour straight away (#{ColorUtility.ToHtmlStringRGB(ColorSlots.Sky.Value)}), not saved yet");
            Hud.TestPick(0.07f, 0.55f, 0.95f, true);
            Check(PlayerPrefs.GetString(skyKey, "") == ColorUtility.ToHtmlStringRGB(picked), "let go: the picked colour is saved");
            Hud.OpenPickerFor = ColorSlots.Hands.Index;
            Hud.ScrollToColours = true;
            yield return new WaitForSeconds(0.6f);
            Hud.TestPick(0.52f, 0.7f, 0.9f, true);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("picker_hands");
            var hc = Color.HSVToRGB(0.52f, 0.7f, 0.9f);
            int handRs = 0, handOk = 0;
            foreach (var hook in FindObjectsByType<HandColorHook>(FindObjectsSortMode.None))
                foreach (var r in hook.GetComponentsInChildren<Renderer>(true))
                {
                    bool alien = false;
                    for (var t = r.transform; t != null && t != hook.transform; t = t.parent) alien |= t.name == "alien fit";
                    if (!alien) continue;
                    handRs++;
                    if (ColorSlots.Same(r.sharedMaterial.color, hc)) handOk++;
                }
            Check(Hud.PickerSlot == ColorSlots.Hands.Index && !ColorSlots.HandsTeam && ColorSlots.Same(ColorSlots.Hands.Value, hc) && handRs > 0 && handOk == handRs,
                $"the picker on the hands: their own colour, on the hands ({handOk} / {handRs} hand meshes)");
            pc.Paused = false;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("picker_hands_in_game");

            // ---- put this PC's settings back ----
            ColorSlots.Set(ColorSlots.Sky, skyWas, false);
            ColorSlots.Set(ColorSlots.Hands, handsWas, false);
            ColorSlots.SetHandsTeam(handsTeam, false);
            if (skyPref == null) PlayerPrefs.DeleteKey(skyKey); else PlayerPrefs.SetString(skyKey, skyPref);
            if (handPref == null) PlayerPrefs.DeleteKey(handKey); else PlayerPrefs.SetString(handKey, handPref);
            if (hadTeamPref) PlayerPrefs.SetInt("RockGame.HandsTeam", teamPref); else PlayerPrefs.DeleteKey("RockGame.HandsTeam");
            PlayerPrefs.Save();
            GameSettings.SetPostFx(pOn, pB, pV, pG, pBs, pVs, pGs, false);
            Log("ui test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>Main menu > CHANGE VALUES with searches typed in (run from -autotest menushot).</summary>
        IEnumerator ValuesSearchShots(System.Func<string, IEnumerator> shot)
        {
            // how many rows a search should leave: every word in the value's name or its section (any case)
            int Expect(params string[] words)
            {
                int n = 0;
                foreach (var f in Cfg.TuneFields)
                {
                    var sec = Cfg.SectionOf(f);
                    if (sec == "Mode options") continue;
                    string t = (f.Name + " " + sec).ToLowerInvariant();
                    bool all = true;
                    foreach (var w in words) all &= t.Contains(w) || t.Replace(" ", "").Contains(w);
                    if (all) n++;
                }
                return n;
            }
            Hud.SetValuesSearch = "slide";
            yield return shot("menu_values_search_slide");
            int want = Expect("slide");
            Check(want > 0 && Hud.ValuesMatches == want, $"CHANGE VALUES search \"slide\": {Hud.ValuesMatches} rows (expected {want})");
            Hud.SetValuesSearch = "RARITY";
            yield return shot("menu_values_search_rarity_section");
            want = Expect("rarity");
            Check(want >= 10 && Hud.ValuesMatches == want, $"search \"RARITY\" (any case, a whole section): {Hud.ValuesMatches} rows (expected {want})");
            Hud.SetValuesSearch = "bow speed";
            yield return shot("menu_values_search_two_words");
            want = Expect("bow", "speed");
            Check(want > 0 && Hud.ValuesMatches == want, $"search \"bow speed\" (both words): {Hud.ValuesMatches} rows (expected {want})");
            Hud.SetValuesSearch = "qqzzx";
            yield return shot("menu_values_search_none");
            Check(Hud.ValuesMatches == 0, "a search with no match shows no rows (and says so)");
            Hud.SetValuesSearch = "slide";
            Hud.FocusSearch("search values");
            yield return null; yield return null; yield return null;
            Check(Hud.Typing && Binds.Muted, "typing in the search box mutes the game's keys");
            yield return shot("menu_values_search_typing");
            Check(Hud.BackOut(), "Esc while typing lets go of the search box first");
            yield return null; yield return null; yield return null;
            Check(!Hud.Typing && !Binds.Muted, "the game's keys work again once it lets go");
            Hud.SetValuesSearch = "";
            yield return new WaitForSeconds(0.3f);
            Check(Hud.ValuesMatches == -1, "cleared: every section again");
        }

        /// <summary>Host + client: each one's revolver shot is one tracer on both screens.</summary>
        IEnumerator TracerRoutine(PlayerNet me, PlayerController pc)
        {
            bool host = Unity.Netcode.NetworkManager.Singleton.IsServer;
            yield return new WaitForSeconds(1f);
            if (host)
            {
                PlayerNet other = null;
                foreach (var p in PlayerNet.All) if (p != me) other = p;
                Check(other != null, $"a client is in the match ({PlayerNet.All.Count} players)");
                foreach (var p in PlayerNet.All) p.ServerGive(Item.Revolver, 1, 3);
                int c0 = Fx.TracerCount;
                float until = Time.time + 30f;
                while (Time.time < until && (other == null || GunRounds(other, Item.Revolver) != 2)) yield return null;
                yield return new WaitForSeconds(1f);
                Check(other != null && GunRounds(other, Item.Revolver) == 2 && Fx.TracerCount - c0 == 1, $"host: the client's revolver shot shows one tracer ({Fx.TracerCount - c0})");
                yield return new WaitForSeconds(2f);
                yield return Hold(me, Item.Revolver);
                pc.SetLook(me.transform.eulerAngles.y, -10f);
                yield return new WaitForSeconds(0.3f);
                c0 = Fx.TracerCount;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(1.2f);
                Check(Fx.TracerCount - c0 == 1 && me.HeldStack.Data == 2, $"host: its own revolver shot shows one tracer ({Fx.TracerCount - c0})");
                yield return new WaitForSeconds(5f);
                NetGame.Instance.EndGame(me.Team.Value, "tracer test done");
            }
            else
            {
                float until = Time.time + 20f;
                while (Time.time < until && me.Count(Item.Revolver) == 0) yield return null;
                yield return Hold(me, Item.Revolver);
                until = Time.time + 5f;
                while (Time.time < until && me.HeldStack.Data != 3) yield return null;
                pc.SetLook(me.transform.eulerAngles.y, -10f);
                yield return new WaitForSeconds(0.5f);
                int c0 = Fx.TracerCount;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(1.5f);
                Check(Fx.TracerCount - c0 == 1, $"client: its own revolver shot shows one tracer ({Fx.TracerCount - c0})");
                // the host fires a few seconds after it saw ours
                c0 = Fx.TracerCount;
                until = Time.time + 12f;
                while (Time.time < until && Fx.TracerCount == c0) yield return null;
                yield return new WaitForSeconds(1f);
                Check(Fx.TracerCount - c0 == 1, $"client: the host's revolver shot shows one tracer ({Fx.TracerCount - c0})");
            }
        }
    }
}
