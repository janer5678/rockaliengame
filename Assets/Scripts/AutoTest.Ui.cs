using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest ui -host -solo -map plains -shotdir DIR: one gun shot = one tracer (revolver, sniper; the shotgun's own
    /// pellets and nothing extra), post processing on / off / each effect (pictures, pixel checks, frame times, off in
    /// PSX / AI PSX), each extra look on its own and all together (pictures, pixel checks, each one's frame cost, all off
    /// again = the usual look pixel for pixel), the FPS counter, the Dev settings search (and the game's keys muted while
    /// typing in it), Settings > Display, the world colours screen (its own narrow panel with the world in full view
    /// beside it; the colour picker on the sky and on the hands: a drag shows straight away - the sky beside the panel
    /// changes - and is saved on release; it swaps sides; Esc goes back) and the last seconds before we win (the
    /// countdown with no coloured border round the screen).
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

        /// <summary>Mean colour of a part of a grab (r in GUI space: y down from the top of the screen).</summary>
        static Color Region(ScreenStats s, Rect r)
        {
            if (s.Px == null) return Color.clear;
            int x0 = Mathf.Clamp((int)r.xMin, 0, s.W - 1), x1 = Mathf.Clamp((int)r.xMax, x0 + 1, s.W);
            int y0 = Mathf.Clamp(s.H - (int)r.yMax, 0, s.H - 1), y1 = Mathf.Clamp(s.H - (int)r.yMin, y0 + 1, s.H);
            double cr = 0, cg = 0, cb = 0; int n = 0;
            for (int y = y0; y < y1; y += 3)
                for (int x = x0; x < x1; x += 3)
                {
                    var c = s.Px[y * s.W + x];
                    cr += c.r; cg += c.g; cb += c.b; n++;
                }
            n = Mathf.Max(1, n);
            return new Color((float)(cr / n / 255.0), (float)(cg / n / 255.0), (float)(cb / n / 255.0));
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

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
            var xWasOn = new bool[GameSettings.PostExtraCount];
            var xWasStr = new float[GameSettings.PostExtraCount];
            for (int i = 0; i < GameSettings.PostExtraCount; i++) { xWasOn[i] = GameSettings.PostExtraOn((GameSettings.PostExtra)i); xWasStr[i] = GameSettings.PostExtraStrength((GameSettings.PostExtra)i); }
            GameSettings.PostExtrasOff();
            bool fpsWas = GameSettings.ShowFps;
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

            // ---- the extra looks: each one on its own (over the usual post processing), all together, then all off ----
            GameSettings.SetPostFx(true, true, true, true, 0.5f, 0.5f, 0.5f, false);
            GameSettings.PostExtrasOff();
            ScreenStats xBase = default, xBase2 = default, xBack = default;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => xBase = s);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Grab(s => xBase2 = s);
            float xNoise = Diff(xBase, xBase2);
            var xDiff = new float[GameSettings.PostExtraCount];
            bool aoUp = false; float aoOn = 0f;
            for (int i = 0; i < GameSettings.PostExtraCount; i++)
            {
                var e = (GameSettings.PostExtra)i;
                GameSettings.SetPostExtra(e, true, 0.5f, false);
                ScreenStats x = default;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Grab(s => x = s);
                yield return Shot("postfx_extra_" + e);
                xDiff[i] = Diff(xBase, x);
                if (e == GameSettings.PostExtra.AmbientOcclusion) { aoOn = PostFx.AoIntensity; aoUp = aoOn > PostFx.AoBaseIntensity + 0.3f; }
                GameSettings.SetPostExtra(e, false, 0.5f, false);
            }
            for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, true, 0.5f, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("postfx_extras_all");
            for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, true, 1f, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("postfx_extras_all_full");
            GameSettings.PostExtrasOff();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => xBack = s);
            yield return Shot("postfx_extras_off_again");
            float dBack = Diff(xBase, xBack);
            var xs = new System.Text.StringBuilder();
            for (int i = 0; i < GameSettings.PostExtraCount; i++) xs.Append($"{(GameSettings.PostExtra)i} {xDiff[i]:F2}, ");
            Log($"extra looks, mean difference from the usual look: {xs}noise {xNoise:F2}, all off again {dBack:F3}");
            for (int i = 0; i < GameSettings.PostExtraCount; i++)
                Check(xDiff[i] > xNoise * 2f + 0.1f, $"{GameSettings.PostExtraNames[i]} changes the picture ({xDiff[i]:F2} mean difference vs {xNoise:F2} between two plain frames)");
            Check(aoUp, $"ambient occlusion turns up the renderer's SSAO (to {aoOn:F2} from {PostFx.AoBaseIntensity:F2} while on)");
            // (two frames of the very same picture still differ a hair here - the SSAO's noise moves every frame - so "the
            // same" is: no further from the usual look than two plain frames are from each other)
            ScreenStats xBack2 = default;
            yield return Grab(s => xBack2 = s);
            float backNoise = Diff(xBack, xBack2), dBack2 = Diff(xBase2, xBack2);
            Check(Mathf.Min(dBack, dBack2) <= Mathf.Max(xNoise, backNoise) * 1.6f + 0.01f && !PostFx.StylizeOn && Mathf.Approximately(PostFx.AoIntensity, PostFx.AoBaseIntensity),
                $"every extra look off again: the usual look ({dBack:F3} / {dBack2:F3} mean difference from it, {xNoise:F3} / {backNoise:F3} between two plain frames), our pass not drawn, the SSAO back to {PostFx.AoBaseIntensity:F2}");
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

            // each extra look's cost: the usual post processing with it off / on, switched back and forth every quarter of a
            // second (24 times) and the medians compared, so a busy machine slows both the same; then all of them
            GameSettings.SetPostFx(true, true, true, true, 0.5f, 0.5f, 0.5f, false);
            var costs = new System.Text.StringBuilder();
            {
                int vs0 = QualitySettings.vSyncCount, fr0 = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
                float Median(System.Collections.Generic.List<float> l) { l.Sort(); return l.Count == 0 ? 0f : l[l.Count / 2]; }
                for (int i = 0; i <= GameSettings.PostExtraCount; i++)
                {
                    var sOn = new System.Collections.Generic.List<float>();
                    var sOff = new System.Collections.Generic.List<float>();
                    for (int pass = 0; pass < 24; pass++)
                    {
                        bool p = pass % 2 == 1;
                        for (int j = 0; j < GameSettings.PostExtraCount; j++)
                            GameSettings.SetPostExtra((GameSettings.PostExtra)j, p && (j == i || i == GameSettings.PostExtraCount), 0.5f, false);
                        for (int f = 0; f < 4; f++) yield return null;
                        float sum = 0; int n = 0;
                        float until = Time.realtimeSinceStartup + 0.25f;
                        while (Time.realtimeSinceStartup < until) { yield return null; sum += Time.unscaledDeltaTime; n++; }
                        (p ? sOn : sOff).Add(sum / Mathf.Max(1, n) * 1000f);
                    }
                    float xOn = Median(sOn), xOff = Median(sOff);
                    string name = i < GameSettings.PostExtraCount ? ((GameSettings.PostExtra)i).ToString() : "all of them";
                    costs.Append($"{name} {xOn - xOff:+0.00;-0.00} ms ({xOff:F2} -> {xOn:F2}), ");
                }
                QualitySettings.vSyncCount = vs0; Application.targetFrameRate = fr0;
            }
            GameSettings.PostExtrasOff();
            Log($"extra looks' frame cost at {res}: {costs}");

            // the extra looks up close, at our base (machine, bedrock, buildings: the outlines and shading show on them)
            {
                var bed = Cfg.BedrockCenter(team);
                var eye = Ground(bed.x + (bc.x > 0 ? -9f : 9f), bed.z + 7f);
                float yawBed = Quaternion.LookRotation(new Vector3(bed.x - eye.x, 0, bed.z - eye.z)).eulerAngles.y;
                pc.LocalTeleport(eye + Vector3.up * 0.1f, yawBed);
                pc.SetLook(yawBed, 8f);
                yield return new WaitForSeconds(1f);
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("postfx_base_extras_off");
                foreach (var e in new[] { GameSettings.PostExtra.Outlines, GameSettings.PostExtra.AmbientOcclusion, GameSettings.PostExtra.CelBanding })
                {
                    GameSettings.SetPostExtra(e, true, 0.5f, false);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("postfx_base_" + e);
                    GameSettings.SetPostExtra(e, false, 0.5f, false);
                }
                for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, true, 0.5f, false);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("postfx_base_extras_all");
                GameSettings.PostExtrasOff();
                Time.timeScale = 1f;
            }

            // PSX and AI PSX keep their own look: no post processing there
            GameSettings.SetPostFx(true, true, true, true, 0.5f, 0.5f, 0.5f, false);
            for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, true, 0.5f, false);
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            Check(!PostFx.CameraOn && !PostFx.StylizeOn && Mathf.Approximately(PostFx.AoIntensity, PostFx.AoBaseIntensity), "PSX: no post processing (none of the extra looks either)");
            GameSettings.SetGraphics(2, false);
            yield return new WaitForSeconds(0.8f);
            Check(!PostFx.CameraOn && !PostFx.StylizeOn && Mathf.Approximately(PostFx.AoIntensity, PostFx.AoBaseIntensity), "AI PSX: no post processing (none of the extra looks either)");
            GameSettings.PostExtrasOff();
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Check(PostFx.CameraOn, "Normal again: post processing back on");

            // ---- the FPS counter (Settings > Display), top left under YOU ARE ... ----
            pc.LocalTeleport(spot + Vector3.up * 0.1f, sunYaw + 28f);
            pc.SetLook(sunYaw + 28f, Mathf.Max(-24f, sunPitch + 22f));
            GameSettings.SetShowFps(true, false);
            yield return new WaitForSeconds(1.3f);
            yield return Shot("fps_counter");
            Check(Hud.FpsShown > 1f, $"the FPS counter shows ({Hud.FpsShown:0} FPS)");
            GameSettings.SetShowFps(false, false);
            yield return new WaitForSeconds(0.3f);

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

            // ---- Settings > Display (the FPS counter row, post processing and its extra looks) ----
            GameSettings.SetShowFps(true, false);
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("settings_display_postfx");
            GameSettings.SetShowFps(false, false);
            yield return DisplayExtrasTests(Shot, pc, sunYaw + 28f, Mathf.Max(-24f, sunPitch + 22f));
            // ---- the world colours screen: the world as it is unpaused, then with the colour screen up (no dark cover) ----
            pc.Paused = false;
            yield return new WaitForSeconds(0.6f);
            ScreenStats plain = default, cscreen = default;
            yield return Grab(s => plain = s);
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.3f);
            Hud.ScrollToColours = true;
            yield return new WaitForSeconds(0.6f);
            yield return Grab(s => cscreen = s);
            yield return Shot("colour_screen");
            var panel = Hud.ColourPanel;
            var worldArea = new Rect(10, Screen.height * 0.12f, Mathf.Max(20f, panel.xMin - 30f), Screen.height * 0.45f);
            float lPlain = Lum(Region(plain, worldArea)), lScreen = Lum(Region(cscreen, worldArea));
            Check(Hud.ColourScreenOpen && panel.width < Screen.width * 0.5f && lScreen > lPlain * 0.9f,
                $"World colours is its own narrow panel ({panel.width:0} of {Screen.width} px) with the world in full view beside it (brightness {lPlain:F3} unpaused, {lScreen:F3} beside the panel)");
            Hud.OpenPickerFor = ColorSlots.Sky.Index;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("picker_sky_open");
            Check(Hud.PickerSlot == ColorSlots.Sky.Index, "the colour picker opens on the sky's row");
            var picked = Color.HSVToRGB(0.07f, 0.55f, 0.95f);
            string savedBefore = PlayerPrefs.GetString(skyKey, "");
            ScreenStats skyBefore = default, skyAfter = default;
            yield return Grab(s => skyBefore = s);
            Hud.TestPick(0.07f, 0.55f, 0.95f, false);
            yield return new WaitForSeconds(0.5f);
            yield return Grab(s => skyAfter = s);
            yield return Shot("picker_sky_dragging");
            // the sky beside the panel: how much of it went from blue to the picked orange
            int skyPx = 0, turned = 0;
            if (skyBefore.Px != null && skyAfter.Px != null && skyBefore.Px.Length == skyAfter.Px.Length)
                for (int y = (int)(skyBefore.H * 0.45f); y < skyBefore.H; y += 4)
                    for (int x = 0; x < (int)panel.xMin - 20; x += 4)
                    {
                        Color32 a = skyBefore.Px[y * skyBefore.W + x], b = skyAfter.Px[y * skyAfter.W + x];
                        if (a.b <= a.r + 20) continue; // (only what was sky blue before)
                        skyPx++;
                        if (b.r > a.r + 25 && b.b < a.b - 25) turned++;
                    }
            Check(skyPx > 0 && turned > skyPx * 0.3f, $"the sky changes colour beside the colour screen while you drag ({turned} of {skyPx} sky-blue samples turned orange)");
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
            Hud.SetColourSide(true);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("colour_screen_left");
            Check(Hud.ColourPanel.xMin < Screen.width * 0.1f, "the colour screen can swap to the other side");
            Hud.SetColourSide(false);
            Check(Hud.BackOut() && pc.Paused && !Hud.ColourScreenOpen, "Esc on the colour screen goes back to Display (still paused)");
            yield return new WaitForSeconds(0.4f);
            yield return Shot("colour_screen_back_to_display");
            pc.Paused = false;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("picker_hands_in_game");

            // ---- about to win: the last seconds count down with no coloured border round the screen ----
            {
                var game = NetGame.Instance;
                if (game.WallUp)
                {
                    me.DevRpc(DevCmd.DropWallNow);
                    float until = Time.time + 8f;
                    while (Time.time < until && game.S != GameState.BallLive) yield return null;
                }
                pc.LocalTeleport(spot + Vector3.up * 0.1f, sunYaw + 28f);
                pc.SetLook(sunYaw + 28f, Mathf.Max(-24f, sunPitch + 22f));
                yield return new WaitForSeconds(1.2f);
                ScreenStats before = default, during = default;
                yield return Grab(s => before = s);
                var ball = Ball.Instance;
                ball.ServerSocket(team);
                game.DevSetTimeLeft(7.4f); // (grabbed just before the tick to 7: when the old border pulsed strongest)
                yield return new WaitForSeconds(0.25f);
                yield return Grab(s => during = s);
                yield return Shot("win_countdown");
                yield return new WaitForSeconds(0.6f);
                yield return Shot("win_countdown_b");
                float bw = 50f * Mathf.Max(0.75f, Screen.height / 900f);
                float edge = 0f;
                foreach (var band in new[] { new Rect(0, Screen.height * 0.3f, bw, Screen.height * 0.4f), new Rect(Screen.width - bw, Screen.height * 0.3f, bw, Screen.height * 0.4f) })
                {
                    Color a = Region(before, band), b = Region(during, band);
                    edge = Mathf.Max(edge, Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))));
                }
                Check(game.S == GameState.BallLive && game.TimeLeft < 10f && ball.SocketTeam.Value == team && edge < 0.04f,
                    $"about to win: the countdown is up ({game.TimeLeft:0.0} s, YOU WIN IN) with no coloured border (the screen's edges changed by {edge * 255f:0} of 255)");
                game.DevSetTimeLeft(600f);
                ball.ServerPlaceInDome();
                yield return new WaitForSeconds(0.5f);
            }

            // ---- put this PC's settings back ----
            ColorSlots.Set(ColorSlots.Sky, skyWas, false);
            ColorSlots.Set(ColorSlots.Hands, handsWas, false);
            ColorSlots.SetHandsTeam(handsTeam, false);
            if (skyPref == null) PlayerPrefs.DeleteKey(skyKey); else PlayerPrefs.SetString(skyKey, skyPref);
            if (handPref == null) PlayerPrefs.DeleteKey(handKey); else PlayerPrefs.SetString(handKey, handPref);
            if (hadTeamPref) PlayerPrefs.SetInt("RockGame.HandsTeam", teamPref); else PlayerPrefs.DeleteKey("RockGame.HandsTeam");
            PlayerPrefs.Save();
            GameSettings.SetPostFx(pOn, pB, pV, pG, pBs, pVs, pGs, false);
            for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, xWasOn[i], xWasStr[i], false);
            GameSettings.SetShowFps(fpsWas, false);
            Log("ui test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// (paused, Settings > Display open) Display is a side panel with the game in view; the shadow darkness and
        /// distance reach the sun and the URP asset and change the picture; post processing on the UI too: with every
        /// effect at nothing the UI looks just as it does drawn straight to the screen (the right way up, in the right
        /// place), and with a strong vignette and grading the panel changes; then everything back.
        /// </summary>
        IEnumerator DisplayExtrasTests(System.Func<string, IEnumerator> shot, PlayerController pc, float yaw, float pitch)
        {
            var panel = Hud.SettingsPanel;
            Check(panel.width <= Screen.width * 0.55f && panel.xMin > Screen.width * 0.4f, $"Settings > Display is a side panel ({panel.width:0} of {Screen.width} px) with the game in view beside it");
            float ssWas = GameSettings.ShadowStrength, sdWas = GameSettings.ShadowDistance;
            bool pOn = GameSettings.PostFx, pB = GameSettings.PostBloom, pV = GameSettings.PostVignette, pG = GameSettings.PostGrading;
            float pBs = GameSettings.PostBloomStrength, pVs = GameSettings.PostVignetteStrength, pGs = GameSettings.PostGradingStrength;
            bool uiWas = GameSettings.PostOnUi;
            Time.timeScale = 0f;

            // ---- post processing on the UI too ----
            ScreenStats direct = default, composited = default, strongDirect = default, strongUi = default;
            GameSettings.SetPostFx(true, false, false, false, 0.5f, 0.5f, 0.5f, false);
            GameSettings.SetPostOnUi(false, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => direct = s);
            GameSettings.SetPostOnUi(true, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => composited = s);
            yield return shot("display_ui_under_post_plain");
            float same = Diff(direct, composited);
            Check(UiLook.UiUnderPost && same < 2.5f, $"UI under the post processing, every effect off: it looks as it did drawn to the screen ({same:F2} mean difference) - the right way up, in the right place");
            GameSettings.SetPostFx(true, false, true, true, 0.5f, 1f, 1f, false);
            GameSettings.SetPostOnUi(false, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => strongDirect = s);
            GameSettings.SetPostOnUi(true, false);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Grab(s => strongUi = s);
            yield return shot("display_ui_under_post_vignette_grading");
            Color a = Region(strongDirect, panel), b = Region(strongUi, panel);
            float panelDiff = (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) * 255f / 3f;
            float panelSame = 0f;
            { Color c = Region(direct, panel), d = Region(composited, panel); panelSame = (Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b)) * 255f / 3f; }
            Check(panelDiff > panelSame + 1f, $"with a strong vignette and grading the settings panel itself changes once the UI goes under them ({panelDiff:F2} vs {panelSame:F2} of 255)");
            GameSettings.SetPostOnUi(false, false);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(!UiLook.UiUnderPost, "UI under the post processing off again: drawn straight to the screen");


            // ---- shadows ----
            ScreenStats sFull = default, sNone = default;
            // (the back to the sun, looking down at the ground: the shadows fall away from us, in view)
            pc.SetLook(yaw + 180f, 35f);
            GameSettings.SetShadows(1f, 120f, false);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Grab(s => sFull = s);
            Check(Mathf.Approximately(UiLook.SunShadowStrength, 1f) && Mathf.Approximately(UiLook.UrpShadowDistance, 120f), $"shadow distance 120 m reaches the render pipeline ({UiLook.UrpShadowDistance:0} m)");
            GameSettings.SetShadows(0f, 120f, false);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Grab(s => sNone = s);
            yield return shot("display_shadows_none");
            float shadowDiff = Diff(sFull, sNone);
            Check(Mathf.Approximately(UiLook.SunShadowStrength, 0f) && shadowDiff > 0.3f && sNone.Lum > sFull.Lum,
                $"shadow darkness 0%: the sun's shadows go (strength {UiLook.SunShadowStrength:0.##}, the picture {shadowDiff:F2} different and brighter: {sFull.Lum:F3} -> {sNone.Lum:F3})");
            GameSettings.SetShadows(ssWas, sdWas, false);
            pc.SetLook(yaw, pitch);

            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(0.6f); // (the view settles back)
            GameSettings.SetPostOnUi(uiWas, false);
            GameSettings.SetPostFx(pOn, pB, pV, pG, pBs, pVs, pGs, false);
            Time.timeScale = 1f;
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
                    if (Hud.IsModeOptionsSection(sec)) continue;
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
            Hud.SetValuesSearch = "AIRDROP";
            yield return shot("menu_values_search_airdrop_section");
            want = Expect("airdrop");
            Check(want >= 10 && Hud.ValuesMatches == want, $"search \"AIRDROP\" (any case, a whole section): {Hud.ValuesMatches} rows (expected {want})");
            // the airdrop rarities moved to MODE OPTIONS (each item's chance there): none left in CHANGE VALUES
            Hud.SetValuesSearch = "rarity";
            yield return shot("menu_values_search_rarity_moved");
            Check(Expect("rarity") == 0 && Hud.ValuesMatches == 0, $"search \"rarity\": nothing - the airdrop rarities are on the MODE OPTIONS screen now ({Hud.ValuesMatches} rows)");
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
