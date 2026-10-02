using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest arena: the sudden death platform in space. Start the host on its own (-autotest arena -host -fast
    /// -shotdir DIR, windowed for the screenshots) and a client (-autotest arena -client 127.0.0.1) about a minute later.
    /// Alone in the lobby the host checks the look (flat colours, no lightning bolt, a black sky with low-poly stars, the
    /// galaxy and planets, 15% smaller, a dense crowd in every colour doing many different things in one stadium all the
    /// way round, a gap clear of the platform, the struts far below, only the sections in view drawn), photographs the platform, the crowd (first person, close
    /// up, a Mexican wave) and the galaxy, times frames with and without the crowd, and checks that walking off the edge
    /// just puts you back on top. When the client joins, sudden death starts (Ready? / Set / ROCK!, frozen until ROCK!,
    /// the crowd goes wild), and the host walks off the edge: that's a death like a kill, so the client wins the duel.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator ArenaRoutine(PlayerNet me, PlayerController pc)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsHost) yield break; // the client just stands there (Watch quits when the game is over)
            var g = NetGame.Instance;
            var c = Cfg.ArenaCenter;
            var cam = Camera.main;
            var hud = FindAnyObjectByType<Hud>();
            Check(g.S == GameState.Waiting, "alone in the lobby: still waiting for players");
            Check(SpaceArena.OverPlatform(me.transform.position) && Mathf.Abs(me.transform.position.y - c.y) < 0.6f, $"standing on the platform in the lobby ({me.transform.position - c})");
            Check(Stadium.Instance != null && Stadium.Instance.GetComponentInChildren<TextMesh>().GetComponent<Renderer>().enabled, "the arena is drawn while you're in it");
            Check(!RenderSettings.fog, "no fog in space");
            var st = Stadium.Instance;
            int textured = 0, starTris = 0, galaxyTris = 0, glowTris = 0, boltBits = 0, standsNear = 0, standVerts = 0, strutVerts = 0, strutsHigh = 0;
            var standAround = new bool[72];
            // how far a point (x, z, from the middle) is outside the platform's edge (0 on or over it)
            float PlatformDistance(Vector2 p)
            {
                if (SpaceArena.OverPlatform(c + new Vector3(p.x, 0f, p.y))) return 0f;
                float best = float.MaxValue;
                var o = SpaceArena.Outline;
                for (int i = 0; i < o.Length; i++)
                {
                    Vector2 a = o[i], b = o[(i + 1) % o.Length], e = b - a;
                    float u = Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + e * u));
                }
                return best;
            }
            Renderer shellR = null;
            foreach (var t in st.GetComponentsInChildren<Transform>(true))
            {
                t.TryGetComponent<MeshFilter>(out var mf);
                if (t.name == "stars" && mf) starTris = mf.sharedMesh.triangles.Length / 3;
                if (t.name == "galaxy" && mf) galaxyTris = mf.sharedMesh.triangles.Length / 3;
                if (t.name == "galaxy glow" && mf) glowTris = mf.sharedMesh.triangles.Length / 3;
                if (t.name == "space") shellR = t.GetComponent<Renderer>();
                if (t.name == "bolt inlay") boltBits++;
                // the lightning bolt was pink lines down the middle of the platform: only the edges are left
                if (t.name == "pink edges" && mf)
                    foreach (var v in mf.sharedMesh.vertices)
                        if (Mathf.Abs(v.x) < 5f && Mathf.Abs(v.z) < SpaceArena.HalfZ - 3f) boltBits++;
                // the stadium: one bowl all the way round, well clear of the platform (the gap you fall through)
                if (t.name == "crowd stands" && mf)
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        standVerts++;
                        var flat = new Vector2(v.x, v.z);
                        if (PlatformDistance(flat) < SpaceArena.Gap - 0.5f) standsNear++;
                        standAround[Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f) / 5f) % 72] = true;
                    }
                // the struts under it all are far below where you're lost in space
                if (t.name == "stadium struts" && mf)
                    foreach (var v in mf.sharedMesh.vertices) { strutVerts++; if (v.y > -SpaceArena.KillDepth - 1.5f) strutsHigh++; }
            }
            foreach (var r in st.GetComponentsInChildren<MeshRenderer>(true))
                if (r.GetComponent<TextMesh>() == null) // (the screens' text is a font)
                foreach (var m in r.sharedMaterials)
                    if (m != null && ((m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null && m.GetTexture("_MainTex").name != "UnityWhite"))) textured++;
            Check(starTris >= SpaceArena.StarCount * 10, $"low-poly 3D stars in the sky ({SpaceArena.StarCount} stars, {starTris} triangles in one mesh)");
            Check(galaxyTris >= SpaceArena.GalaxySpecks * 2 && glowTris > 500 && st.Planets >= SpaceArena.PlanetCount - 3,
                  $"a galaxy band, spiral galaxies and planets ({galaxyTris} triangles, {st.Planets} planets, {glowTris} glow triangles)");
            var skyCol = shellR != null && shellR.sharedMaterial.HasProperty("_Color") ? shellR.sharedMaterial.GetColor("_Color") : Color.white;
            Check(shellR != null && skyCol.maxColorComponent < 0.01f && SpaceArena.SkyColor.maxColorComponent < 0.01f, $"the night sky is black ({skyCol})");
            Check(boltBits == 0, $"no lightning bolt on the platform ({boltBits} bits of it left)");
            Check(st.CrowdCount >= 400 && st.CrowdAnimations >= 10 && st.CrowdHues >= 10,
                  $"a dense crowd ({st.CrowdCount} fans, {st.CrowdAnimations} of {SpaceArena.CrowdAnimations} animations, {st.CrowdHues} of 12 hues, {st.CrowdStands.Count} sections)");
            int around = 0;
            foreach (var b in standAround) if (b) around++;
            Check(standVerts > 0 && standsNear == 0, $"the stadium is clear of the platform: a {SpaceArena.Gap} m gap to fall through ({standsNear} of {standVerts} stand vertices closer)");
            Check(around == 72, $"the stands are one stadium all the way round, not separate floating stands ({around} of 72 five-degree slices have stands)");
            Check(strutVerts > 0 && strutsHigh == 0, $"the struts holding the platform are far below the edge ({strutsHigh} of {strutVerts} strut vertices above {SpaceArena.KillDepth + 1.5f} m down)");
            Check(textured == 0, $"flat colours, no textures ({textured} textured materials)");
            Check(Mathf.Approximately(SpaceArena.HalfX, 21f * 0.85f) && Mathf.Approximately(SpaceArena.HalfZ, 27f * 0.85f), $"the platform is 15% smaller ({SpaceArena.HalfX} x {SpaceArena.HalfZ})");
            for (int team = 0; team < 4; team++)
                for (int slot = 0; slot < 4; slot++)
                {
                    NetGame.SpawnPoint(team, true, slot, out var tp, out _);
                    if (!SpaceArena.OverPlatform(tp)) Check(false, $"arena spawn {team}/{slot} is off the platform ({tp - c})");
                }

            // first person, from the spawn spot
            NetGame.SpawnPoint(me.Team.Value, true, me.Slot.Value, out var sp, out var sy);
            pc.LocalTeleport(sp, sy);
            pc.SetLook(sy, 4f);
            yield return Snap("arena_01_fp_spawn");
            pc.SetLook(sy + 70f, -4f);
            yield return Snap("arena_02_fp_stars");
            pc.SetLook(sy + 150f, -32f);
            yield return Snap("arena_03_fp_space_up");
            // first person towards the crowd: the stand straight out to the side
            var side = st.CrowdStands[2].Centre - sp;
            float sideYaw = Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg;
            pc.SetLook(sideYaw, -2f);
            yield return Snap("arena_14_fp_crowd");
            Check(st.DrawnFans > 0 && st.DrawnFans < st.CrowdCount, $"only the sections of the stadium in view are drawn ({st.DrawnFans} of {st.CrowdCount} fans)");

            // frame time in the arena (uncapped): down the platform, across it and up at the sky - everything, without the
            // crowd, and without the crowd, its stands and the new sky (stars, galaxy, planets: about the old arena),
            // taking turns so a busy machine slows them all the same
            var extras = new System.Collections.Generic.List<Renderer>();
            foreach (var r in st.GetComponentsInChildren<Renderer>(true))
                if (r.name == "galaxy" || r.name == "galaxy glow" || r.name == "stars" || r.name == "crowd stands" || r.name == "stadium struts") extras.Add(r);
            IEnumerator Measure(string what, float yaw, float pitch)
            {
                pc.SetLook(yaw, pitch);
                int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
                var sum = new float[3];
                var cnt = new int[3];
                int drawn = 0;
                for (int pass = 0; pass < 9; pass++)
                {
                    int mode = pass % 3;
                    st.CrowdHidden = mode > 0;
                    foreach (var r in extras) r.enabled = mode < 2;
                    yield return new WaitForSeconds(0.3f);
                    float until = Time.realtimeSinceStartup + 1f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                        sum[mode] += Time.unscaledDeltaTime; cnt[mode]++;
                        if (mode == 0) drawn = st.DrawnFans;
                    }
                }
                st.CrowdHidden = false;
                foreach (var r in extras) r.enabled = true;
                QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
                float Ms(int m) => sum[m] / Mathf.Max(1, cnt[m]) * 1000f;
                Log($"arena frame time, {what}: {Ms(0):F2} ms with everything ({drawn} fans drawn), {Ms(1):F2} ms without the crowd, {Ms(2):F2} ms without the crowd, stands and new sky");
            }
            yield return Measure("down the platform", sy, 2f);
            yield return Measure("across", sy + 90f, 4f);
            yield return Measure("up at the sky", sy + 45f, 25f);

            // outside views: the camera on its own, no hands or HUD
            pc.enabled = false;
            if (hud) hud.enabled = false;
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Transform ch in cam.transform) if (ch.gameObject.activeSelf) { hidden.Add(ch.gameObject); ch.gameObject.SetActive(false); }
            IEnumerator View(string name, Vector3 from, Vector3 at)
            {
                cam.transform.position = c + from;
                cam.transform.LookAt(c + at);
                yield return Snap(name);
            }
            yield return View("arena_05_side", new Vector3(0f, 6f, -44f), new Vector3(0, -4f, 0));
            yield return View("arena_06_three_quarter", new Vector3(48f, 26f, -52f), new Vector3(0, -3f, 0));
            yield return View("arena_07_below", new Vector3(26f, -30f, -44f), new Vector3(0, -6f, 0));
            yield return View("arena_04_edge", new Vector3(9f, 1.5f, -33f), new Vector3(0f, -5f, -18f));
            yield return View("arena_08_stars", new Vector3(0f, 3f, 0f), new Vector3(60f, 45f, 120f));
            yield return View("arena_09_top", new Vector3(0f, 70f, -10f), new Vector3(0, 0, 0));
            // the crowd up close (a stand to the side, from in front of it at head height): every colour, all sorts of moves
            var stand = st.CrowdStands[2];
            var toStand = new Vector3(stand.Centre.x - c.x, 0f, stand.Centre.z - c.z).normalized;
            var eye = stand.Centre - c - toStand * 11f + Vector3.up * 3.5f;
            yield return View("arena_15_crowd_close_a", eye, stand.Centre - c + Vector3.up * 1.2f);
            yield return new WaitForSeconds(0.6f);
            yield return View("arena_15_crowd_close_b", eye, stand.Centre - c + Vector3.up * 1.2f);
            var stand2 = st.CrowdStands[9];
            var toStand2 = new Vector3(stand2.Centre.x - c.x, 0f, stand2.Centre.z - c.z).normalized;
            yield return View("arena_15_crowd_close_c", stand2.Centre - c - toStand2 * 13f + Vector3.up * 2f, stand2.Centre - c + Vector3.up * 2f);
            // a Mexican wave going round
            st.StartWave();
            yield return new WaitForSeconds(2.2f);
            yield return View("arena_16_crowd_wave", new Vector3(0f, 9f, -20f), new Vector3(0f, 0f, 40f));
            // the galaxy: a wide shot from out among the stars, and the galaxy band's bright middle from the platform
            yield return View("arena_17_galaxy_wide", new Vector3(-70f, 34f, -150f), new Vector3(10f, 30f, 60f));
            var core = st.Sky != null ? st.Sky.rotation * SpaceArena.GalaxyCore : Vector3.up;
            yield return View("arena_18_galaxy_core", new Vector3(0f, 2f, 0f), core * 100f);
            yield return View("arena_19_planets", new Vector3(0f, 2f, 0f), new Vector3(-80f, 10f, 100f));
            foreach (var h in hidden) h.SetActive(true);
            if (hud) hud.enabled = true;
            pc.enabled = true;

            // the lobby: walking off the edge just puts you back on the platform
            pc.LocalTeleport(c + new Vector3(SpaceArena.HalfX + 2.5f, 0.3f, 0), 90f);
            pc.SetLook(270f, 20f);
            yield return new WaitForSeconds(0.6f);
            Check(me.transform.position.y < c.y - 1f, $"stepped off the edge and fell (y {me.transform.position.y - c.y:0.0})");
            yield return Snap("arena_10_falling_lobby");
            float until = Time.time + 6f;
            while (Time.time < until && !(SpaceArena.OverPlatform(me.transform.position) && me.transform.position.y > c.y - 1f)) yield return null;
            Check(SpaceArena.OverPlatform(me.transform.position) && me.transform.position.y > c.y - 1f && !me.Dead.Value && g.S == GameState.Waiting,
                  $"lobby: falling into space puts you back on the platform, alive ({me.transform.position - c})");

            // sudden death as soon as the opponent is in
            Log("waiting for the client to join");
            while (g.S == GameState.Waiting) yield return null;
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            Check(other != null, "opponent joined");
            yield return new WaitForSeconds(1f);
            g.DevStartSuddenDeath();
            while (g.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(1.5f);
            Check(SpaceArena.OverPlatform(me.transform.position) && SpaceArena.OverPlatform(other.transform.position), "both players teleported onto the platform for sudden death");
            NetGame.SpawnPoint(me.Team.Value, true, me.Slot.Value, out sp, out sy);
            pc.SetLook(sy, 2f);
            // Ready? / Set / ROCK! - and nobody moves until ROCK!
            while (Hud.FightWord(g, out _) != "Ready?" && g.FightFrozen) yield return null;
            var frozenAt = me.transform.position;
            Binds.TestHold(Bind.Forward, true);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_ready.png"));
            Log("shot arena_11_sd_ready");
            while (Hud.FightWord(g, out _) == "Ready?") yield return null;
            Check(Hud.FightWord(g, out _) == "Set" && g.FightFrozen, $"Ready? then Set ({Hud.FightWord(g, out _)})");
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_set.png"));
            Log("shot arena_11_sd_set");
            float moved = 0f;
            while (g.FightFrozen)
            {
                moved = Mathf.Max(moved, Vector3.Distance(new Vector3(frozenAt.x, 0, frozenAt.z), new Vector3(me.transform.position.x, 0, me.transform.position.z)));
                yield return null;
            }
            Check(moved < 0.2f, $"holding W during Ready? / Set doesn't move you ({moved:0.00} m)");
            Check(Hud.FightWord(g, out _) == "ROCK!", $"then ROCK! ({Hud.FightWord(g, out _)})");
            yield return new WaitForSeconds(0.15f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_rock.png"));
            Log("shot arena_11_sd_rock");
            yield return new WaitForSeconds(0.6f);
            Binds.TestReleaseAll();
            float moved2 = Vector3.Distance(new Vector3(frozenAt.x, 0, frozenAt.z), new Vector3(me.transform.position.x, 0, me.transform.position.z));
            Check(moved2 > 1f, $"after ROCK! you can move ({moved2:0.0} m)");
            Check(st.Excitement > 0.6f, $"the crowd goes wild on ROCK! (excitement {st.Excitement:0.00})");
            pc.LocalTeleport(sp, sy);
            yield return new WaitForSeconds(2.5f);
            yield return Snap("arena_12_sd_duel");
            Check(g.S == GameState.SuddenDeath && !me.Dead.Value && !other.Dead.Value, "nobody died before anyone fell");

            // walk off the end of the platform: lost in space, and the opponent wins
            pc.LocalTeleport(c + new Vector3(0, 0.3f, -SpaceArena.HalfZ - 2.5f), 180f);
            pc.SetLook(0f, 25f);
            until = Time.time + 5f;
            while (Time.time < until && !me.Dead.Value) yield return null;
            Check(me.Dead.Value, $"sudden death: falling into space kills you (died at y {me.transform.position.y - c.y:0.0})");
            yield return Snap("arena_13_sd_fell");
            until = Time.time + 3f;
            while (Time.time < until && g.S != GameState.GameOver) yield return null;
            Check(g.S == GameState.GameOver && g.Winner.Value == other.Team.Value, $"the one who didn't fall wins the duel (winner {g.Winner.Value}, reason \"{g.EndReason.Value}\")");
            Log("arena done");
        }
    }
}
