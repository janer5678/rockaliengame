using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Every screen: the things the wacky items leave in the world (NetGame.Gadgets), built from the list and kept in step
    /// with it - the BOUNCY CASTLE (its floor bounces you: BouncySurface), the ATTENTION SNAIL, a cloud of CLUTTER, the
    /// Stick of Truth's BENT GROUND (jagged and solid), a DECOY BASE (a copy of a team's whole base, made of the base as this
    /// screen sees it), a scribbled WORD block and lingering SMOKE.
    /// </summary>
    public static class GadgetFx
    {
        static readonly Dictionary<int, GameObject> s_Built = new Dictionary<int, GameObject>();
        static readonly HashSet<int> s_Seen = new HashSet<int>();
        static readonly List<int> s_Gone = new List<int>();

        public static void Clear()
        {
            foreach (var go in s_Built.Values) if (go) { GrassField.Unblock(go.GetInstanceID()); Object.Destroy(go); }
            s_Built.Clear();
        }

        /// <summary>For the tests: how many gadgets this screen has built right now.</summary>
        public static int Count => s_Built.Count;

        /// <summary>How deep in a clutter bomb's cloud a point is (0 outside, 1 in the thick of it) - Hud.Wacky's screen
        /// full of junk when your eyes are in one.</summary>
        public static float ClutterAt(NetGame g, Vector3 p)
        {
            if (g == null || !g.IsSpawned) return 0f;
            double now = g.NetworkManager.ServerTime.Time;
            float best = 0f;
            foreach (var gd in g.Gadgets)
            {
                if (gd.Kind != Gadget.Clutter || (gd.Until > 0 && gd.Until <= now)) continue;
                var d = p - gd.Pos;
                if (d.y < -1f || d.y > 6f) continue;
                float flat = new Vector2(d.x, d.z).magnitude;
                float k = Mathf.Clamp01((Cfg.ClutterRadius + 1f - flat) / 3f);
                float fade = Mathf.Clamp01((float)(gd.Until - now) / 1.5f) * Mathf.Clamp01((float)(now - gd.Born) / 0.4f);
                best = Mathf.Max(best, k * fade);
            }
            return best;
        }
        public static GameObject Find(int id) => s_Built.TryGetValue(id, out var go) ? go : null;

        public static void Sync(NetGame g)
        {
            if (g == null || !g.IsSpawned) return;
            s_Seen.Clear();
            double now = g.NetworkManager.ServerTime.Time;
            foreach (var gd in g.Gadgets)
            {
                s_Seen.Add(gd.Id);
                if (!s_Built.TryGetValue(gd.Id, out var go) || !go)
                {
                    go = Build(gd);
                    s_Built[gd.Id] = go;
                }
                if (go) Animate(go, gd, now);
            }
            if (s_Built.Count == s_Seen.Count) return;
            s_Gone.Clear();
            foreach (var kv in s_Built) if (!s_Seen.Contains(kv.Key)) s_Gone.Add(kv.Key);
            foreach (var id in s_Gone) { if (s_Built[id]) { GrassField.Unblock(s_Built[id].GetInstanceID()); Object.Destroy(s_Built[id]); } s_Built.Remove(id); }
        }

        static GameObject Build(Gadget g)
        {
            var root = new GameObject("gadget " + g.Kind + " " + g.Id);
            root.transform.SetPositionAndRotation(g.Pos, Quaternion.Euler(0, g.Yaw, 0));
            var rng = new System.Random(g.Seed);
            switch (g.Kind)
            {
                case Gadget.Castle: BuildCastle(root.transform, g.Team, rng); break;
                case Gadget.Snail: BuildSnail(root.transform); break;
                case Gadget.Clutter: BuildClutter(root.transform, rng); break;
                case Gadget.Truth: BuildTruth(root.transform, rng); break;
                case Gadget.DecoyBase: BuildDecoyBase(root.transform, g); break;
                case Gadget.Word: BuildWord(root.transform, g.Text.ToString(), rng); break;
                case Gadget.Smoke: BuildSmoke(root.transform, rng); break;
            }
            // no grass poking through what's solid (and the snail's not lost in it)
            if (g.Kind == Gadget.Castle || g.Kind == Gadget.Snail || g.Kind == Gadget.Truth || g.Kind == Gadget.Word) GrassField.BlockRenderers(root.GetInstanceID(), root.transform);
            return root;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ---------------- the bouncy castle ----------------

        static readonly Color[] k_Bright = { new Color(0.95f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.15f), new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.3f), new Color(1f, 0.45f, 0.8f), new Color(1f, 0.55f, 0.1f) };

        /// <summary>The bouncy castle's pink gradient: hot pink at the bottom up to candy-floss pink at the top (0..1).</summary>
        public static Color CastlePink(float up) => Color.Lerp(new Color(0.92f, 0.12f, 0.5f), new Color(1f, 0.82f, 0.93f), Mathf.Clamp01(up));

        static void BuildCastle(Transform t, int team, System.Random r)
        {
            var body = new GameObject("castle").transform;
            body.SetParent(t, false);
            var teamC = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            const float S = 6f, H = 0.55f, Top = 5.2f;
            Color P(float y) => CastlePink(y / Top);   // (every part the pink of its height: a gradient up the whole castle)
            // the bouncy floor (solid, and it bounces you)
            var floor = Art.Box(body, P(0f), new Vector3(0, H * 0.5f, 0), new Vector3(S, H, S), default, true);
            floor.AddComponent<BouncySurface>();
            for (int i = -2; i <= 2; i++) Art.Box(body, i % 2 == 0 ? P(0.9f) : P(0.3f), new Vector3(i * 1.15f, H + 0.005f, 0), new Vector3(1.1f, 0.01f, S - 0.3f)); // (stripes)
            // puffy walls round three sides (low enough to bounce over), open at the front: two rows, the top one paler
            for (int side = 0; side < 3; side++)
            {
                var rot = Quaternion.Euler(0, side * 90f + 90f, 0);
                for (int k = 0; k < 6; k++)
                {
                    float x = -S * 0.5f + 0.5f + k * (S - 1f) / 5f;
                    var p = rot * new Vector3(x, H + 0.7f, S * 0.5f - 0.3f);
                    var col = Art.Part(body, Art.Capsule, P(H + 0.4f + (k % 2) * 0.25f), p, new Vector3(0.6f, 0.7f, 0.6f), default, true);
                    col.AddComponent<BouncySurface>();
                }
            }
            // the front's arch
            Art.Part(body, Art.Cylinder, P(H + 2.1f), new Vector3(0, H + 2.1f, -S * 0.5f + 0.3f), new Vector3(0.5f, 2.6f, 0.5f), new Vector3(0, 0, 90));
            // towers at the corners (in three bands, darker to lighter going up), with pointy roofs and flags
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var p = new Vector3(cx * (S * 0.5f - 0.2f), 0, cz * (S * 0.5f - 0.2f));
                    for (int band = 0; band < 3; band++)
                        Art.Part(body, Art.Cylinder, P(0.6f + band * 1.2f), p + Vector3.up * (0.6f + band * 1.2f), new Vector3(0.9f, 0.6f, 0.9f), default, true);
                    for (int b = 0; b < 4; b++) Art.Box(body, P(3.7f), p + Vector3.up * 3.7f + Quaternion.Euler(0, b * 90f, 0) * new Vector3(0, 0, 0.35f), new Vector3(0.22f, 0.25f, 0.22f));
                    Art.Part(body, Art.Cone, P(Top), p + Vector3.up * 3.85f, new Vector3(1f, 1.1f, 1f));
                    Art.Box(body, Art.DarkWood, p + Vector3.up * 5.2f, new Vector3(0.04f, 0.7f, 0.04f));
                    Art.Box(body, teamC, p + new Vector3(0.22f, 5.4f, 0), new Vector3(0.4f, 0.25f, 0.02f));
                }
            foreach (var mr in body.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // ---------------- the attention snail ----------------

        /// <summary>The attention snail's size (its model is about 30 cm long at 1): tiny.</summary>
        const float SnailScale = 0.7f;

        static void BuildSnail(Transform t)
        {
            var body = new GameObject("snail").transform;
            body.SetParent(t, false);
            body.localScale = Vector3.one * SnailScale; // (a tiny snail - right in front of every camera)
            var skin = new Color(0.55f, 0.6f, 0.45f);
            var shell = new Color(0.65f, 0.38f, 0.18f);
            Art.Part(body, Art.Capsule, skin, new Vector3(0, 0.07f, 0.02f), new Vector3(0.14f, 0.24f, 0.14f), new Vector3(90, 0, 0));
            var sh = Art.Part(body, Art.Sphere, shell, new Vector3(0, 0.2f, -0.06f), new Vector3(0.22f, 0.24f, 0.24f));
            for (int k = 0; k < 3; k++) Art.Part(body, Art.Cylinder, shell * (0.7f + k * 0.12f), new Vector3(0.115f, 0.2f, -0.06f), new Vector3(0.2f - k * 0.06f, 0.006f, 0.2f - k * 0.06f), new Vector3(0, 0, 90));
            for (int s = -1; s <= 1; s += 2)
            {
                var stalk = new GameObject("stalk").transform;
                stalk.SetParent(body, false);
                stalk.localPosition = new Vector3(s * 0.03f, 0.1f, 0.22f);
                Art.Part(stalk, Art.Cylinder, skin, new Vector3(0, 0.06f, 0), new Vector3(0.018f, 0.06f, 0.018f));
                Art.Part(stalk, Art.Sphere, Color.white, new Vector3(0, 0.125f, 0), Vector3.one * 0.035f);
                Art.Part(stalk, Art.Sphere, Color.black, new Vector3(0, 0.127f, 0.012f), Vector3.one * 0.018f);
            }
            sh.name = "shell";
        }

        // ---------------- clutter ----------------

        static void BuildClutter(Transform t, System.Random r)
        {
            float rad = Cfg.ClutterRadius;
            var bits = new GameObject("clutter").transform;
            bits.SetParent(t, false);
            // glitter and confetti everywhere, balloons, big shapes and thick blobs of colour hanging in the air - dense
            // enough that an alien in it is hard to make out
            for (int i = 0; i < 420; i++)
            {
                var p = new Vector3(R(r, -rad, rad), R(r, 0.2f, 4.5f), R(r, -rad, rad));
                if (new Vector2(p.x, p.z).magnitude > rad) continue;
                var c = k_Bright[r.Next(k_Bright.Length)];
                var go = Art.Box(bits, c, p, Vector3.one * R(r, 0.05f, 0.14f), new Vector3(R(r, 0, 360), R(r, 0, 360), 0));
                go.name = "bit";
            }
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(R(r, -rad, rad) * 0.8f, R(r, 1f, 3.5f), R(r, -rad, rad) * 0.8f);
                var c = k_Bright[r.Next(k_Bright.Length)];
                var b = Art.Part(bits, Art.Sphere, c, p, new Vector3(0.5f, 0.62f, 0.5f));
                b.name = "balloon";
                Art.Box(b.transform, Color.white, new Vector3(0, -1.1f, 0), new Vector3(0.02f, 1.6f, 0.02f));
            }
            for (int i = 0; i < 34; i++)
            {
                var p = new Vector3(R(r, -rad, rad) * 0.75f, R(r, 0.6f, 2.8f), R(r, -rad, rad) * 0.75f);
                var c = k_Bright[r.Next(k_Bright.Length)];
                var mesh = i % 3 == 0 ? Art.Cone : i % 3 == 1 ? Art.Ico : Art.Cube;
                var go = Art.Part(bits, mesh, c, p, Vector3.one * R(r, 0.6f, 1.4f), new Vector3(R(r, 0, 360), R(r, 0, 360), R(r, 0, 360)));
                go.name = "junk";
            }
            for (int i = 0; i < 46; i++)
            {
                var p = new Vector3(R(r, -rad, rad) * 0.8f, R(r, 0.6f, 2.6f), R(r, -rad, rad) * 0.8f);
                var c = k_Bright[r.Next(k_Bright.Length)];
                c.a = 0.78f;
                Art.Part(bits, Art.Sphere, Color.white, p, Vector3.one * R(r, 2f, 3.6f), default, false, Art.Ghost(c)).name = "blob";
            }
            foreach (var mr in bits.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------- the Stick of Truth's bent ground ----------------

        static void BuildTruth(Transform t, System.Random r)
        {
            float rad = Cfg.TruthRadius;
            var earth = new Color(0.42f, 0.34f, 0.24f);
            var rock = new Color(0.5f, 0.5f, 0.52f);
            var grass = new Color(0.32f, 0.55f, 0.26f);
            var zone = new GameObject("truth").transform;
            zone.SetParent(t, false);
            // a heaped, torn-up ring of ground round a crater
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + R(r, -0.15f, 0.15f);
                float d = rad * R(r, 0.55f, 0.95f);
                var p = new Vector3(Mathf.Cos(a) * d, R(r, -0.2f, 0.4f), Mathf.Sin(a) * d);
                var s = new Vector3(R(r, 2f, 3.6f), R(r, 0.8f, 2.4f), R(r, 1.6f, 3f));
                Art.Part(zone, Art.MakeRock(r.Next(1000), 0.35f), i % 2 == 0 ? grass : earth, p, s, new Vector3(R(r, -30, 30), a * Mathf.Rad2Deg, R(r, -30, 30)), true);
            }
            // jagged spikes of rock and earth shooting out at mad angles
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3(R(r, -rad, rad) * 0.8f, 0f, R(r, -rad, rad) * 0.8f);
                float len = R(r, 2.5f, 6.5f);
                var tilt = new Vector3(R(r, -55, 55), R(r, 0, 360), R(r, -55, 55));
                var go = Art.Part(zone, Art.MakeRock(r.Next(1000), 0.3f), i % 3 == 0 ? rock : earth, p + Quaternion.Euler(tilt) * Vector3.up * len * 0.4f, new Vector3(R(r, 0.5f, 1.1f), len, R(r, 0.5f, 1.1f)), tilt, true);
                go.name = "spike";
            }
            // slabs of ground tipped up on their edges
            for (int i = 0; i < 7; i++)
            {
                var p = new Vector3(R(r, -rad, rad) * 0.7f, R(r, 0.3f, 1.2f), R(r, -rad, rad) * 0.7f);
                Art.Box(zone, i % 2 == 0 ? grass : earth, p, new Vector3(R(r, 2f, 3.5f), 0.35f, R(r, 1.5f, 2.5f)), new Vector3(R(r, 20, 75), R(r, 0, 360), R(r, -30, 30)), true);
            }
            foreach (var mr in zone.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // ---------------- the decoy base ----------------

        /// <summary>A copy of a team's whole base - its floor, bedrock, machine, every piece and chest - as this screen
        /// sees it, put down with its middle where the decoy landed. Every part has a hit box (DecoyBasePart): hit any of it
        /// and it's gone (PlayerNet.DecoyBaseHitRpc).</summary>
        static void BuildDecoyBase(Transform t, Gadget g)
        {
            t.rotation = Quaternion.identity;
            int team = Mathf.Clamp(g.Team, 0, 3);
            var center = Cfg.BaseCenter[team];
            center.y = Cfg.BaseY;
            var offset = g.Pos - center;
            float reach = Cfg.BaseHalf + 1.5f;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var b = mr.bounds;
                var c = b.center;
                if (Mathf.Abs(c.x - center.x) > reach || Mathf.Abs(c.z - center.z) > reach || c.y > center.y + 16f || c.y < center.y - 3f) continue;
                if (b.size.x > reach * 2.2f || b.size.z > reach * 2.2f) continue; // (the ground)
                var tr = mr.transform;
                if (tr.GetComponentInParent<PlayerNet>() != null || tr.GetComponentInParent<Vehicle>() != null || tr.GetComponentInParent<Ball>() != null) continue;
                if (tr.GetComponentInParent<GroundMarker>() != null || tr.GetComponentInParent<FxParticle>() != null || tr.GetComponentInParent<DecoyBasePart>() != null) continue;
                if (tr.root.name.StartsWith("gadget") || tr.root.name == "WorldItem" || tr.root.name.StartsWith("Thrown")) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var copy = new GameObject("decoy part");
                copy.transform.SetParent(t, true);
                copy.transform.SetPositionAndRotation(tr.position + offset, tr.rotation);
                copy.transform.localScale = tr.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var cr = copy.AddComponent<MeshRenderer>();
                cr.sharedMaterials = mr.sharedMaterials;
                cr.shadowCastingMode = mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly ? UnityEngine.Rendering.ShadowCastingMode.On : mr.shadowCastingMode;
                var bc = copy.AddComponent<BoxCollider>();
                bc.center = mf.sharedMesh.bounds.center;
                bc.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.05f);
                copy.AddComponent<DecoyBasePart>().Id = g.Id;
            }
        }

        // ---------------- a scribbled word ----------------

        static void BuildWord(Transform t, string word, System.Random r)
        {
            if (string.IsNullOrEmpty(word)) word = "?";
            float w = Mathf.Clamp(0.6f + word.Length * 0.42f, 1.2f, 10f);
            var c = k_Bright[r.Next(k_Bright.Length)];
            Art.Box(t, c, new Vector3(0, 0.9f, 0), new Vector3(w, 1.8f, 1.2f), default, true);
            Art.Box(t, Color.Lerp(c, Color.white, 0.5f), new Vector3(0, 1.81f, 0), new Vector3(w - 0.1f, 0.02f, 1.1f));
            // the word on all four faces in solid pixel letters (a TextMesh shows through the block)
            int cols = PixelFont.Columns(word);
            for (int side = 0; side < 4; side++)
            {
                bool along = side < 2;
                float sgn = side % 2 == 0 ? -1f : 1f;
                float px = Mathf.Min(along ? (w - 0.3f) / cols : 1.0f / cols, 1.2f / 7f);
                var txt = PixelFont.Build(t, word, px, Color.white);
                txt.transform.localPosition = along ? new Vector3(0, 0.95f, sgn * 0.6f) : new Vector3(sgn * w * 0.5f, 0.95f, 0);
                txt.transform.localRotation = Quaternion.Euler(0, along ? (sgn < 0 ? 0f : 180f) : (sgn > 0 ? -90f : 90f), 0);
            }
        }

        // ---------------- lingering smoke ----------------

        static void BuildSmoke(Transform t, System.Random r)
        {
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(R(r, -2.2f, 2.2f), R(r, 0.3f, 2.6f), R(r, -2.2f, 2.2f));
                var c = new Color(0.92f, 0.92f, 0.92f, 0.55f);
                var go = Art.Part(t, Art.Sphere, Color.white, p, Vector3.one * R(r, 1.2f, 2.2f), new Vector3(R(r, 0, 360), R(r, 0, 360), 0), false, new Material(Art.Ghost(c)));
                go.name = "puff";
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---------------- moving ----------------

        static void Animate(GameObject go, Gadget g, double now)
        {
            float t = Time.time;
            float left = g.Until > 0 ? (float)(g.Until - now) : 999f;
            float born = (float)(now - g.Born);
            var tr = go.transform;
            switch (g.Kind)
            {
                case Gadget.Castle:
                {
                    // it inflates when it arrives, breathes a little, and sags as its time runs out
                    var body = tr.childCount > 0 ? tr.GetChild(0) : null;
                    if (body == null) break;
                    float grow = Mathf.Clamp01(born / 0.6f);
                    float sag = Mathf.Clamp01(left / 3f);
                    float breathe = 1f + Mathf.Sin(t * 2.4f) * 0.015f;
                    body.localScale = new Vector3(grow * breathe, Mathf.Max(0.05f, grow * sag * breathe), grow * breathe);
                    break;
                }
                case Gadget.Snail:
                {
                    var body = tr.childCount > 0 ? tr.GetChild(0) : null;
                    if (body == null) break;
                    body.localScale = new Vector3(SnailScale, SnailScale, SnailScale * (1f + Mathf.Sin(t * 2f) * 0.06f));
                    // it turns to look at you (the camera, in its close-up)
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var to = cam.transform.position - tr.position; to.y = 0f;
                        if (to.sqrMagnitude > 0.01f) body.rotation = Quaternion.Slerp(body.rotation, Quaternion.LookRotation(to), Time.deltaTime * 4f);
                    }
                    foreach (Transform c in body) if (c.name == "stalk") c.localRotation = Quaternion.Euler(Mathf.Sin(t * 3f + c.localPosition.x * 40f) * 15f, 0, Mathf.Sin(t * 2.3f + c.localPosition.x * 30f) * 12f);
                    break;
                }
                case Gadget.Clutter:
                {
                    var bits = tr.childCount > 0 ? tr.GetChild(0) : null;
                    if (bits == null) break;
                    float fade = Mathf.Clamp01(left / 1.5f) * Mathf.Clamp01(born / 0.4f);
                    bits.localScale = Vector3.one * fade;
                    int i = 0;
                    foreach (Transform c in bits)
                    {
                        i++;
                        if (c.name == "bit") c.Rotate(90f * Time.deltaTime, 140f * Time.deltaTime, 0, Space.Self);
                        else if (c.name == "balloon") c.localPosition += Vector3.up * Mathf.Sin(t * 1.3f + i) * 0.003f;
                        else if (c.name == "junk") c.Rotate(0, 40f * Time.deltaTime, 25f * Time.deltaTime, Space.Self);
                    }
                    if (Random.value < 0.3f) FxParticle.Spawn(g.Pos + new Vector3(Random.Range(-Cfg.ClutterRadius, Cfg.ClutterRadius), Random.Range(2f, 4f), Random.Range(-Cfg.ClutterRadius, Cfg.ClutterRadius)) * 0.7f, Vector3.down * 0.5f, k_Bright[Random.Range(0, k_Bright.Length)], 0.06f, 2f, 1f, false);
                    break;
                }
                case Gadget.Smoke:
                {
                    float fade = Mathf.Clamp01(left / 2f);
                    int i = 0;
                    foreach (Transform c in tr)
                    {
                        i++;
                        c.localPosition += new Vector3(Mathf.Sin(t + i) * 0.1f, 0.15f, Mathf.Cos(t * 0.7f + i) * 0.1f) * Time.deltaTime;
                        var mr = c.GetComponent<MeshRenderer>();
                        if (mr != null) { var col = mr.sharedMaterial.color; col.a = 0.55f * fade; mr.sharedMaterial.color = col; if (mr.sharedMaterial.HasProperty("_BaseColor")) mr.sharedMaterial.SetColor("_BaseColor", col); }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>A part of a decoy base: hitting it (a swing, an arrow, a spear, a shot) gives the game away.</summary>
    public class DecoyBasePart : MonoBehaviour
    {
        public int Id;
        static float s_NextReport;

        /// <summary>Your hit landed on this collider: if it's part of a decoy base, the server's told (it goes in smoke).</summary>
        public static bool Report(Collider c)
        {
            if (c == null) return false;
            var part = c.GetComponentInParent<DecoyBasePart>();
            if (part == null) return false;
            var me = PlayerNet.Local;
            if (me != null && me.IsSpawned && Time.time >= s_NextReport) { s_NextReport = Time.time + 0.3f; me.DecoyBaseHitRpc(part.Id); }
            return true;
        }
    }
}
