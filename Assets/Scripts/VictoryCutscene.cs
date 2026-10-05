using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The victory cutscene: when a team wins with its ball in its machine's socket as the battle timer runs out, everyone
    /// watches the winners escape. The map's glass dome isn't there for any of it (MapDome.SetFade(0) from the first
    /// frame: the sky's open for the UFO and the camera, nothing for the ship to clip through), and the ball's beam of
    /// light is off (Ball.LateUpdate: it used to run straight up through the UFO); a UFO like the airdrop ship
    /// (AirdropShip.BuildShip) flies in under the clouds and hovers over the winners' bedrock, opens its hatch and its
    /// beam (in the winning team's colour) comes down - and when it hits, the winners' base blows up like a rocket hit it (Blast: only a picture, on every
    /// peer - explosions, its pieces gone, chunks of them flying); then, one by one, the winners float up the beam,
    /// spinning, and rise full size right up through the hatch into the ship's hull (the beam goes see-through round them,
    /// so you see them inside the light) while the camera pushes in close on them - their name tags over their heads
    /// (DrawTags) - and pulls back out once the last is a little way up; then the beam
    /// goes off, the hatch shuts, the ship
    /// dips, tilts, spins up and lifts high up over the mountains' tops (CruiseY), and only then shoots off over the edge
    /// of the map (over the mountains, not into them), vanishing in a twinkle of light. Then the victory screen comes up.
    ///
    /// The camera films it from a hill near the base, not from inside it (a base they've built there hides everything):
    /// PlanCamera tries spots all round the base (further out, higher up, on the higher ground first) and sphere-casts from
    /// each - and from points along the camera's move - to the winners on the ground, up the beam to the hatch and to the
    /// ship coming in, keeping the spot that sees the most of it with nothing (no wall, no roof, no hill, no tree) in the way.
    /// (Trees' needles have no colliders: they're checked as cones from the trees' own bounds; any tree still in the way
    /// of the winners or the beam from the chosen move is hidden for the cutscene.) It drifts in and round a little over the
    /// cutscene, eases in close on the winners and back out (CloseUp: one smooth ramp for the dolly and the lens), and
    /// eases its aim (critically damped) from the ship coming in, to the winners as they float up, to the ship as it leaves.
    ///
    /// Networked like the airdrops: the server picks the winners to beam up (alive and connected - the dead and the gone are
    /// skipped), sends them home onto their bedrock, and sets NetGame.CutsceneAt / CutsceneSpot / CutsceneRiders together
    /// with the game being over; every peer then plays the same timeline from that synced start time (nothing in it is a
    /// network object, so nothing can be despawned out from under it). A rider who leaves mid-cutscene is just skipped.
    /// While it plays nobody can move, look, fight or open menus (PlayerController), the camera is the cutscene's, the HUD
    /// is just letterbox bars and a caption (Hud.DrawVictoryCutscene), and the victory screen waits until it's over.
    /// </summary>
    public static class VictoryCutscene
    {
        /// <summary>The timeline (seconds after the start; the dome is gone from the start): the ship comes in and hovers by
        /// Arrive; its hatch opens at HatchAt; the beam comes on at BeamOn and blows the base up when it hits the ground
        /// (BlastAt); rider i starts floating up at LiftStart + i * LiftStagger (after the blast) and is in the ship
        /// LiftTime later; the beam goes off at BeamOff - right after the last one's in; the hatch shuts and
        /// the ship winds up from LeaveStart (dips, tilts, spins up, lifts up over the mountains) and dashes off at
        /// DashStart, gone at Gone; the victory screen comes up at Length.</summary>
        public const float Arrive = 4.2f, HatchAt = 3.9f, HatchTime = 0.8f, BeamOn = 4.9f;
        public const float BlastAt = BeamOn + 0.35f;
        public const float LiftStart = 6.9f, LiftStagger = 0.6f, LiftTime = 3.2f;
        public const int MaxRiders = 8;
        public const float BeamOff = LiftStart + (4 - 1) * LiftStagger + LiftTime + 0.3f;
        public const float LeaveStart = BeamOff + 0.45f, WindUp = 2.5f, DashStart = LeaveStart + WindUp, DashTime = 2.2f, LeaveTime = WindUp + DashTime;
        /// <summary>The camera's lens: wide (WideFov), a little tighter once the beam's on (BeamFov), right in on the winners
        /// as they stand in the beam and start up it (ZoomFov, CloseUp), then back out.</summary>
        public const float WideFov = 62f, BeamFov = 54f, ZoomFov = 26f;
        public const float Gone = LeaveStart + LeaveTime, Length = Gone + 0.9f;
        /// <summary>How high the UFO hovers over the winners.</summary>
        public const float HoverUp = 26f;
        /// <summary>How far it dashes off (out over the edge) and climbs as it goes.</summary>
        const float DashOut = 240f, DashUp = 20f;
        /// <summary>How high it lifts before it dashes off: clear over the tops of the nearest mountains (it used to fly
        /// off low and clip into them), and always a good way up from where it hovered.</summary>
        public static float CruiseY => Mathf.Max(s_Hover.y + 14f, MapScenery.RangeTop(0) + 14f);

        static AirdropShip.Parts s_Ship;
        static GameObject s_Beam, s_Glow, s_Twinkle;
        static Transform s_Main, s_Core, s_Halo;
        static Material s_BeamMat, s_CoreMat, s_HaloMat, s_GlowMat, s_TwinkleMat;
        static Light s_GroundLight, s_TwinkleLight;
        static AudioSource s_HumLoop;
        static double s_Built = -1;
        static Vector3 s_Spot, s_Hover, s_Out, s_Look, s_TwinklePos;
        static bool s_LookSet;
        static readonly HashSet<ulong> s_Puffed = new HashSet<ulong>(), s_Taken = new HashSet<ulong>();
        static readonly HashSet<int> s_Cues = new HashSet<int>();

        static NetGame G => NetGame.Instance;

        /// <summary>Seconds into the cutscene (-1: none).</summary>
        public static float Elapsed
        {
            get
            {
                var g = G;
                if (g == null || !g.IsSpawned || g.CutsceneAt.Value < 0) return -1f;
                return Mathf.Max(0f, (float)(g.NetworkManager.ServerTime.Time - g.CutsceneAt.Value));
            }
        }

        /// <summary>Is the cutscene playing right now (the game's over, the victory screen waits)?</summary>
        public static bool Active
        {
            get
            {
                var g = G;
                if (g == null || !g.IsSpawned || g.S != GameState.GameOver || g.CutsceneAt.Value < 0) return false;
                return g.NetworkManager.ServerTime.Time - g.CutsceneAt.Value < Length;
            }
        }

        /// <summary>The winners to beam up: everyone on the team who's alive and still here (server).</summary>
        public static List<PlayerNet> PickRiders(int team, IEnumerable<PlayerNet> players)
        {
            var list = new List<PlayerNet>();
            foreach (var p in players)
            {
                if (p == null || !p.IsSpawned || p.Team.Value != team || p.Dead.Value) continue;
                list.Add(p);
                if (list.Count >= MaxRiders) break;
            }
            list.Sort((a, b) => a.Slot.Value.CompareTo(b.Slot.Value));
            return list;
        }

        /// <summary>Test hooks: is its ship built, where it is, how far up the beam rider i is (0 on the ground .. 1 in the ship).</summary>
        public static Transform Ship => s_Ship != null && s_Ship.Root ? s_Ship.Root.transform : null;
        public static float ShipScale { get; private set; }
        public static Vector3 HoverPoint => s_Hover;
        public static float BeamIntensity => s_Beam != null && s_Beam.activeSelf ? BeamFx.Intensity(s_BeamMat) : 0f;
        public static float LiftOf(int i) => Mathf.Clamp01((Elapsed - (LiftStart + i * LiftStagger)) / LiftTime);
        /// <summary>Test hook: how many riders this peer has seen taken up into the ship.</summary>
        public static int Taken => s_Taken.Count;
        /// <summary>Test hooks: the camera's planned shot - where it starts and ends, how much of the action it sees
        /// (0..1: the winners, the beam, the hatch, the ship coming in, from every point of its move) and how many spots it tried.</summary>
        public static Vector3 CamFrom { get; private set; }
        public static Vector3 CamTo { get; private set; }
        public static float CamClear { get; private set; }
        public static int CamTried { get; private set; }

        /// <summary>Every frame on every peer (NetGame.Update): build, fly and tear down the ship and its beam.</summary>
        public static void Tick(NetGame g)
        {
            if (!Active)
            {
                if (s_Ship != null) Clear();
                // (the blown-up base stays gone behind the victory screen; its pieces show again if the game moves on with them still there)
                if (s_Blown.Count > 0 && (g == null || g.S != GameState.GameOver)) Unblast();
                // the dome comes back once the game's moved on (a new map builds a new one anyway)
                if (MapDome.Fade < 1f && (g == null || g.S != GameState.GameOver)) MapDome.SetFade(1f);
                return;
            }
            float e = Elapsed;
            if (s_Ship == null || s_Built != g.CutsceneAt.Value) Build(g);

            // no glass dome over the map for the whole cutscene: the sky's open for the UFO (it used to fly in through
            // the dome while it was still fading) and nothing's in the camera's way
            MapDome.SetFade(0f);

            var pos = ShipPos(e, out float scale, out var rot);
            var t = s_Ship.Root.transform;
            t.SetPositionAndRotation(pos, rot);
            t.localScale = new Vector3(scale, scale * (e > DashStart ? Mathf.Lerp(1f, 0.7f, Mathf.Clamp01((e - DashStart) / 0.6f)) : 1f), scale);
            ShipScale = e >= Gone ? 0f : scale;
            if (e >= Gone && s_Ship.Root.activeSelf) s_Ship.Root.SetActive(false);
            // its rim of lights turns, and spins right up as it winds up to leave
            float spin = e < LeaveStart ? 60f : Mathf.Lerp(60f, 900f, Mathf.Clamp01((e - LeaveStart) / WindUp));
            s_Ship.Rim.Rotate(0, spin * Time.deltaTime, 0, Space.Self);
            float open = Mathf.Clamp01(Mathf.Min((e - HatchAt) / HatchTime, 1f - (e - (BeamOff + 0.15f)) / HatchTime));
            open = open * open * (3f - 2f * open);
            AirdropShip.SetHatch(s_Ship, open, Mathf.Max(0.001f, scale), pos.y - s_Spot.y);
            if (s_HumLoop != null) s_HumLoop.pitch = e < DashStart ? 0.75f : Mathf.Lerp(0.75f, 2.2f, Mathf.Clamp01((e - DashStart) / 1.2f));

            // the beam: out of the hatch, down to the winners, pulling them up
            bool beaming = e >= BeamOn - 0.2f && e < BeamOff;
            if (s_Beam.activeSelf != beaming) s_Beam.SetActive(beaming);
            float groundA = 0f;
            if (beaming)
            {
                float top = pos.y + AirdropShip.HatchY * scale;
                float bottom = Mathf.Lerp(top, s_Spot.y - 0.3f, Mathf.SmoothStep(0f, 1f, (e - (BeamOn - 0.2f)) / 0.5f));
                float a = Mathf.Clamp01(Mathf.Min((e - BeamOn + 0.2f) / 0.25f, (BeamOff - e) / 0.4f));
                float flicker = 0.92f + 0.08f * Mathf.Sin(Time.time * 11f);
                AirdropShip.PlaceBeam(s_Beam.transform, s_Main, s_Core, s_Halo, s_Spot, top, bottom, AirdropShip.HatchR * 0.8f * scale);
                var c = BeamColour(g);
                // (as bright as the other beams; it goes see-through round each winner in it, so you see them inside it)
                BeamFx.Set(s_BeamMat, c, 1.0f * a * flicker);
                BeamFx.Set(s_CoreMat, Color.Lerp(c, Color.white, 0.6f), 1.3f * a * flicker);
                BeamFx.Set(s_HaloMat, c, 0.55f * a);
                if (bottom < s_Spot.y + 0.5f) groundA = a;
            }
            s_GroundLight.intensity = 7f * groundA;
            if (s_Glow.activeSelf != groundA > 0.01f) s_Glow.SetActive(groundA > 0.01f);
            BeamFx.Set(s_GlowMat, BeamColour(g), 2.4f * groundA);

            // each rider: a puff of light and a rising whoosh as they leave the ground, a pop as they're taken in; the beam
            // is see-through round each one in it
            int slot = 0;
            for (int i = 0; i < g.CutsceneRiders.Count; i++)
            {
                ulong id = g.CutsceneRiders[i];
                var p = Rider(g, id);
                if (p == null) continue;
                float u = LiftOf(i);
                if (u > 0f && s_Puffed.Add(id))
                {
                    Fx.Play(FxKind.Spawn, p.transform.position, Vector3.up);
                    Sfx.Play2D(Sfx.Portal, 0.5f, 0.1f);
                }
                if (u >= 1f && s_Taken.Add(id))
                {
                    Fx.Play(FxKind.Spawn, pos + Vector3.up * AirdropShip.HatchY * scale, Vector3.down);
                    Sfx.Play2D(Sfx.Pop, 0.8f, 0.1f);
                    Sfx.Play2D(Sfx.Ding, 0.35f, 0.05f);
                }
                if (u < 1f && slot < 4)
                {
                    float up = RiderUp(p, u);
                    var at = p.transform.position + Vector3.up * (up + 1f);
                    foreach (var m in new[] { s_BeamMat, s_CoreMat, s_HaloMat }) BeamFx.SetClear(m, slot, at, 2.2f);
                    slot++;
                }
            }
            for (; slot < 4; slot++) foreach (var m in new[] { s_BeamMat, s_CoreMat, s_HaloMat }) BeamFx.SetClear(m, slot, Vector3.zero, 0f);

            Blast(g, e);
            Cues(e, pos);
            Twinkle(e);
        }

        // ------------------------------------------------------------------ the base blows up

        /// <summary>The blast's explosions: when (after BlastAt) and where (out from the middle of the base, as a part of its half width).</summary>
        static readonly (float at, Vector2 off)[] k_Blasts = { (0f, Vector2.zero), (0.14f, new Vector2(0.55f, 0.3f)), (0.28f, new Vector2(-0.45f, -0.5f)), (0.42f, new Vector2(-0.3f, 0.6f)) };
        /// <summary>How many chunks of the base fly at most, how long they last (s) and how hard gravity pulls them.</summary>
        const int MaxChunks = 70;
        const float ChunkLife = 3.2f, ChunkGravity = 14f;
        static readonly List<Renderer> s_Blown = new List<Renderer>();
        static int s_Blasts;
        /// <summary>Test hooks: how many of the base's renderers the blast has hidden on this peer, and how many of its explosions have gone off.</summary>
        public static int BlownCount => s_Blown.Count;
        public static int Blasts => s_Blasts;

        /// <summary>
        /// The beam hits the winners' base and it blows up like a rocket hit it - before anyone goes up the beam. Only a
        /// picture, the same on every peer (the match is over; nothing on the server changes): a few of the rocket's
        /// explosions (Fx.Explosion: flash, fireball, smoke, shockwave, shake, boom) across the base, every building piece
        /// and chest in it gone, and chunks of them thrown up and out.
        /// </summary>
        static void Blast(NetGame g, float e)
        {
            if (e < BlastAt || s_Blasts >= k_Blasts.Length) return;
            if (e > BlastAt + 2f)
            {
                // (joined late: no bangs, but the base is gone)
                if (s_Blasts == 0) HideBase(g, false);
                s_Blasts = k_Blasts.Length;
                return;
            }
            int team = Mathf.Clamp(g.Winner.Value, 0, 3);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            while (s_Blasts < k_Blasts.Length && e >= BlastAt + k_Blasts[s_Blasts].at)
            {
                var o = k_Blasts[s_Blasts].off * Cfg.BaseHalf;
                var at = s_Spot + side * o.x + back * o.y;
                if (s_Blasts == 0) HideBase(g, true);
                Fx.Explosion(new Vector3(at.x, Mathf.Max(at.y, MapBuilder.Height(at.x, at.z)) + 0.8f, at.z));
                s_Blasts++;
            }
        }

        /// <summary>Is this spot part of the winners' base (in it, or built on out from it)?</summary>
        static bool InBase(Vector3 p, int team) =>
            Cfg.BaseTeamAt(p) == team || new Vector2(p.x - s_Spot.x, p.z - s_Spot.z).magnitude < Cfg.BaseHalf + 5f;

        /// <summary>Hide every building piece and chest of the winners' base (their renderers: the objects stay), and - chunks - throw bits of them up and out.</summary>
        static void HideBase(NetGame g, bool chunks)
        {
            int team = Mathf.Clamp(g.Winner.Value, 0, 3);
            var roots = new List<Transform>();
            foreach (var s in Structure.All) if (s != null && InBase(s.transform.position, team)) roots.Add(s.transform);
            foreach (var c in Container.All) if (c != null && !c.IsAirdrop && InBase(c.transform.position, team)) roots.Add(c.transform);
            int budget = MaxChunks, per = Mathf.Clamp(MaxChunks / Mathf.Max(1, roots.Count), 1, 4);
            foreach (var root in roots)
            {
                Material mat = null;
                Bounds b = default;
                bool any = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (r is MeshRenderer && r.sharedMaterial != null && mat == null) mat = r.sharedMaterial;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                    r.enabled = false;
                    s_Blown.Add(r);
                }
                if (!chunks || !any || mat == null) continue;
                for (int i = 0; i < per && budget > 0; i++, budget--)
                {
                    var from = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                    var away = from - s_Spot;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.05f ? away.normalized : Random.onUnitSphere;
                    var vel = away * Random.Range(4f, 13f) + Vector3.up * Random.Range(7f, 17f) + Random.insideUnitSphere * 3f;
                    VictoryChunk.Spawn(from, vel, mat, new Vector3(Random.Range(0.35f, 1.3f), Random.Range(0.15f, 0.5f), Random.Range(0.35f, 1.1f)), ChunkLife * Random.Range(0.7f, 1f), ChunkGravity);
                }
            }
        }

        /// <summary>Show the base's pieces again (the ones still there).</summary>
        static void Unblast()
        {
            foreach (var r in s_Blown) if (r != null) r.enabled = true;
            s_Blown.Clear();
        }

        /// <summary>The sounds on the timeline (each once on every peer).</summary>
        static void Cues(float e, Vector3 shipPos)
        {
            bool Cue(int id, float at) => e >= at && s_Cues.Add(id);
            if (Cue(1, 0.3f) && s_Ship != null) s_HumLoop = Sfx.Loop(Sfx.Hum, s_Ship.Root.transform, 1f, 0.75f, 600f, 0.4f);
            if (Cue(2, HatchAt)) Sfx.Play2D(Sfx.Door, 0.6f, 0f);
            if (Cue(3, BeamOn - 0.15f)) { Sfx.Play2D(Sfx.Zap, 0.7f, 0f); Sfx.Play2D(Sfx.Hum, 0.6f, 0f); }
            if (Cue(4, BeamOff)) Sfx.Play2D(Sfx.Hiss, 0.4f, 0f);
            if (Cue(5, BeamOff + 0.2f)) Sfx.Play2D(Sfx.Door, 0.6f, 0f);
            if (Cue(6, LeaveStart)) Sfx.Play2D(Sfx.Engine, 0.45f, 0f);
            if (Cue(7, DashStart)) { Sfx.Play2D(Sfx.Rocket, 0.8f, 0f); Sfx.Play2D(Sfx.Whiz, 0.6f, 0f); }
            // (no sound as it vanishes any more: just the twinkle)
        }

        /// <summary>The twinkle as it vanishes: a star of light that flares and fades where it went.</summary>
        static void Twinkle(float e)
        {
            float u = (e - (Gone - 0.12f)) / 0.7f;
            bool on = u > 0f && u < 1f;
            if (on && s_Twinkle == null)
            {
                s_TwinklePos = s_Ship != null ? s_Ship.Root.transform.position : s_Hover;
                s_TwinkleMat = BeamFx.Ball(new Color(0.9f, 0.8f, 1f), 0f, 1.2f);
                s_Twinkle = BeamFx.Cylinder(null, s_TwinkleMat, "VictoryTwinkle");
                s_Twinkle.GetComponent<MeshFilter>().sharedMesh = Art.Sphere;
                var lg = new GameObject("VictoryTwinkleLight");
                lg.transform.SetParent(s_Twinkle.transform, false);
                s_TwinkleLight = lg.AddComponent<Light>();
                s_TwinkleLight.type = LightType.Point;
                s_TwinkleLight.color = new Color(0.85f, 0.75f, 1f);
                s_TwinkleLight.range = 60f;
                s_TwinkleLight.shadows = LightShadows.None;
            }
            if (s_Twinkle == null) return;
            if (s_Twinkle.activeSelf != on) s_Twinkle.SetActive(on);
            if (!on) return;
            float flare = u < 0.15f ? u / 0.15f : Mathf.Pow(1f - (u - 0.15f) / 0.85f, 2f);
            s_Twinkle.transform.position = s_TwinklePos;
            s_Twinkle.transform.localScale = Vector3.one * (4f + 10f * flare);
            BeamFx.Set(s_TwinkleMat, new Color(0.9f, 0.8f, 1f), 5f * flare);
            s_TwinkleLight.intensity = 40f * flare;
        }

        /// <summary>Where the ship is e seconds in, how big, and how it's turned: in over the edge like an airdrop ship,
        /// hovering, then the departure - it dips and tilts towards the way out while its rim spins up, lifts up to
        /// CruiseY (over the mountains' tops), then shoots off over the edge, stretching, shrinking away to a point of light.</summary>
        static Vector3 ShipPos(float e, out float scale, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (e < LeaveStart) return AirdropShip.FlightPos(s_Hover, s_Out, e, Arrive, float.MaxValue, 1f, out scale);
            scale = 1f;
            var side = Vector3.Cross(Vector3.up, s_Out).normalized;
            // the wind-up: a little dip, a lean towards the way out, then up it goes - high over the mountains' tops
            float w = Mathf.Clamp01((e - LeaveStart) / WindUp);
            float dip = Mathf.Sin(Mathf.Clamp01(w / 0.35f) * Mathf.PI) * 1.8f;
            float rise = (CruiseY - s_Hover.y) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((w - 0.2f) / 0.8f));
            rot = Quaternion.AngleAxis(-14f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w * 2f)), side);
            var p = s_Hover + Vector3.up * (rise - dip);
            if (e < DashStart) return p;
            // the dash: off over the edge, faster and faster, shrinking away to nothing
            float u = Mathf.Clamp01((e - DashStart) / (Gone - DashStart));
            float k = u * u * (1.6f - 0.6f * u);
            scale = Mathf.Pow(1f - u, 1.25f);
            return p + (s_Out * DashOut + Vector3.up * DashUp) * k;
        }

        /// <summary>The beam's colour (and its glow on the ground and its light): the winning team's own colour.</summary>
        public static Color BeamColour(NetGame g) => Cfg.TeamColor[Mathf.Clamp(g != null ? g.Winner.Value : 0, 0, 3)];

        static PlayerNet Rider(NetGame g, ulong id) =>
            g.NetworkManager != null && g.NetworkManager.SpawnManager != null && g.NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) && no != null ? no.GetComponent<PlayerNet>() : null;

        static void Build(NetGame g)
        {
            Clear();
            s_Built = g.CutsceneAt.Value;
            s_Spot = g.CutsceneSpot.Value;
            s_Hover = AirdropShip.HoverAt(s_Spot, HoverUp, false); // (the dome's going: no need to clear its glass)
            s_Out = AirdropShip.OutDir(s_Spot);
            s_Ship = AirdropShip.BuildShip("VictoryShip");
            s_Ship.Root.transform.position = s_Hover + s_Out * AirdropShip.ArriveOut + Vector3.up * AirdropShip.ArriveUp;
            s_Beam = new GameObject("VictoryBeam");
            AirdropShip.MakeBeam(s_Beam.transform, BeamColour(g), 0.9f, out s_BeamMat, out s_CoreMat, out s_HaloMat, out s_Main, out s_Core, out s_Halo);
            s_Beam.SetActive(false);
            s_GlowMat = BeamFx.AsBeam(BeamFx.Glow(BeamColour(g), 0f, 1.6f));
            s_Glow = BeamFx.Disc(null, s_GlowMat, "VictoryGroundGlow");
            s_Glow.transform.position = s_Spot + Vector3.up * 0.1f;
            s_Glow.transform.localScale = Vector3.one * AirdropShip.HatchR * 3.2f;
            s_Glow.SetActive(false);
            var gl = new GameObject("VictoryBeamLight");
            gl.transform.position = s_Spot + Vector3.up * 2.5f;
            s_GroundLight = gl.AddComponent<Light>();
            s_GroundLight.type = LightType.Point;
            s_GroundLight.color = BeamColour(g);
            s_GroundLight.range = 16f;
            s_GroundLight.intensity = 0f;
            PlanCamera();
        }

        /// <summary>Tear it all down (it's over, or the match ended).</summary>
        public static void Clear()
        {
            if (s_Ship != null && s_Ship.Root) Object.Destroy(s_Ship.Root);
            if (s_Ship != null && s_Ship.BellyGlow) Object.Destroy(s_Ship.BellyGlow);
            if (s_Beam) Object.Destroy(s_Beam);
            if (s_Glow) Object.Destroy(s_Glow);
            if (s_Twinkle) Object.Destroy(s_Twinkle);
            if (s_GroundLight) Object.Destroy(s_GroundLight.gameObject);
            foreach (var m in new[] { s_BeamMat, s_CoreMat, s_HaloMat, s_GlowMat, s_TwinkleMat }) if (m) Object.Destroy(m);
            s_Ship = null;
            s_Beam = s_Glow = s_Twinkle = null;
            s_HumLoop = null;
            s_Built = -1;
            s_LookSet = false;
            s_LookVel = Vector3.zero;
            ShowTrees();
            s_Puffed.Clear();
            s_Taken.Clear();
            s_Cues.Clear();
            s_Blasts = 0;
        }

        // ------------------------------------------------------------------ the riders

        /// <summary>How far over the hatch a rider's feet end up: their whole body inside the hull (it's about 2.2 m from
        /// the hatch up to the ship's middle), hidden by it.</summary>
        const float InsideUp = 0.35f;

        /// <summary>How far up rider p's body is floated u of the way up the beam (only the picture moves): slowly off the
        /// ground, then faster and faster, still going as it passes up through the hatch into the hull.</summary>
        static float RiderUp(PlayerNet p, float u)
        {
            if (u <= 0f) return 0f;
            // (where the ship really is: it bobs a little as it hovers)
            float shipY = s_Ship != null && s_Ship.Root ? s_Ship.Root.transform.position.y : s_Hover.y;
            float inside = shipY + AirdropShip.HatchY + InsideUp;
            float ease = u * u * (1.6f - 0.6f * u);
            return Mathf.Max(0f, inside - p.transform.position.y) * ease + Mathf.Sin(u * Mathf.PI) * 0.4f;
        }

        /// <summary>
        /// Is this player being beamed up right now (and how)? Their body (only the picture - the player stays where they
        /// are) floats up the beam, turning faster and faster, and - full size (it used to shrink away at the top) - rises
        /// on up through the hatch into the ship's hull, so they're seen going inside; gone: it's in the ship (hide it).
        /// Everyone else - and everyone outside the cutscene - false.
        /// </summary>
        public static bool Lift(PlayerNet p, out float up, out float spin, out float scale, out bool gone)
        {
            up = spin = 0f;
            scale = 1f;
            gone = false;
            var g = G;
            if (!Active || g == null || s_Ship == null) return false;
            int i = -1;
            for (int k = 0; k < g.CutsceneRiders.Count; k++) if (g.CutsceneRiders[k] == p.NetworkObjectId) { i = k; break; }
            if (i < 0) return false;
            float u = LiftOf(i);
            if (u <= 0f) return true;
            if (u >= 1f) { gone = true; return true; }
            up = RiderUp(p, u);
            spin = 540f * u * u;
            return true;
        }

        // ------------------------------------------------------------------ the camera

        static Vector3 s_CamFrom, s_CamTo;

        /// <summary>
        /// Pick the shot: a spot on the higher ground near the winners' base, out of it (not inside their walls), looking
        /// at it. Tries spots all round the base at a few distances and heights (the higher ground and the side the ship
        /// leaves towards first), and from each - and from points along the camera's move - sphere-casts to what it has to
        /// see: the winners on the ground, points up the beam, the hatch, the ship coming in and where it goes. Buildings,
        /// hills and trees all block; the players themselves don't. The spot that sees the most wins (ties: the higher
        /// ground, nearer the ideal distance). If nothing sees the ground, it ends up high enough to see the beam over the roofs.
        /// </summary>
        static void PlanCamera()
        {
            var up = Vector3.up;
            float h = s_Hover.y - s_Spot.y;
            // the trees round about (their needles have no colliders: the casts would see straight through them)
            GatherTrees();
            // what it wants to see, and how much each matters
            var targets = new List<(Vector3 p, float w)>
            {
                (s_Spot + up * 1.2f, 3f), (s_Spot + up * 3.5f, 2f), (s_Spot + up * (h * 0.3f), 1.5f), (s_Spot + up * (h * 0.6f), 1.5f),
                (s_Hover + up * AirdropShip.HatchY, 2f), (s_Hover + up * 2f, 1f),
                (s_Hover + s_Out * AirdropShip.ArriveOut * 0.35f + up * AirdropShip.ArriveUp * 0.35f, 0.7f),
                (new Vector3(s_Hover.x, CruiseY, s_Hover.z), 0.7f),
            };
            float total = 0f;
            foreach (var tg in targets) total += tg.w;
            float edge = Cfg.MapHalf - 6f;
            float bestScore = float.MinValue;
            Vector3 bestFrom = s_Spot - s_Out * 50f + up * 25f, bestTo = bestFrom;
            float bestClear = 0f;
            int tried = 0;
            float[] dists = { 58f, 70f, 84f, 46f };
            float[] lifts = { 3f, 6.5f, 11f, 18f, 28f };
            const float orbit = 10f;
            for (int yi = 0; yi < 18; yi++)
            {
                float yaw = yi * 20f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * -s_Out;
                // (the ship comes in from and leaves towards s_Out: from the other side you see it come at you and go off past the base)
                float facing = Vector3.Dot(dir, -s_Out);
                foreach (var dist in dists)
                {
                    var flat = s_Spot + dir * dist;
                    if (Mathf.Abs(flat.x) > edge || Mathf.Abs(flat.z) > edge) continue;
                    float ground = MapBuilder.Height(flat.x, flat.z);
                    foreach (var lift in lifts)
                    {
                        tried++;
                        // the move: in a little and round a little, every point of it checked
                        var from = Cam(s_Spot, yaw - orbit * 0.5f, dist * 1.08f, lift + 1f);
                        var to = Cam(s_Spot, yaw + orbit * 0.5f, dist * 0.9f, lift);
                        float seen = float.MaxValue;
                        bool ok = true;
                        for (int s = 0; s < 3 && ok; s++)
                        {
                            var at = Vector3.Lerp(from, to, s * 0.5f);
                            if (Physics.CheckSphere(at, 0.8f, Mask, QueryTriggerInteraction.Ignore) || InTree(at)) { ok = false; break; }
                            float v = 0f;
                            foreach (var tg in targets) if (Clear(at, tg.p)) v += tg.w;
                            seen = Mathf.Min(seen, v / total);
                        }
                        if (!ok || Blocked(from, to) || TreeInWay(from, to)) continue;
                        float hill = Mathf.Clamp(ground - s_Spot.y, -5f, 12f);
                        float score = seen * 10f + hill * 0.12f - lift * 0.05f - Mathf.Abs(dist - 66f) * 0.02f + facing * 0.4f;
                        if (score > bestScore) { bestScore = score; bestFrom = from; bestTo = to; bestClear = seen; }
                    }
                }
            }
            CamTried = tried;
            CamClear = bestClear;
            s_CamFrom = CamFrom = bestFrom;
            s_CamTo = CamTo = bestTo;

            // the push in on the winners (CameraPose): as far in along its line to them as it can go - still seeing them,
            // up the beam and the hatch, inside nothing (no tree's needles either), and PushKeep out - checked from where
            // it'll be on its move then (toward their feet; or, if walls hide those, toward the bottom of the beam over the walls)
            var beamMid = s_Spot + up * (h * 0.5f);
            var hatch = s_Hover + up * AirdropShip.HatchY;
            CamPush = 0f;
            foreach (var aim in new[] { s_Spot + up * 1.2f, s_Spot + up * 4.5f, s_Spot + up * (h * 0.3f) })
            {
                for (float f = MaxPush; f > 0.04f && CamPush <= 0f; f -= 0.08f)
                {
                    bool ok = true;
                    foreach (float de in new[] { -1f, 0f, 1f, 2f })
                    {
                        var at = BaseAt(PushPeak + de);
                        var d = aim - at;
                        var near = at + d.normalized * Mathf.Min(f * d.magnitude, PushDist);
                        if (new Vector2(near.x - s_Spot.x, near.z - s_Spot.z).magnitude < PushKeep || !Clear(at, aim) || InTree(near)
                            || Physics.CheckSphere(near, 0.8f, Mask, QueryTriggerInteraction.Ignore) || !Clear(near, beamMid) || !Clear(near, hatch)) { ok = false; break; }
                    }
                    if (ok) { CamPush = f; s_PushAim = aim; }
                }
                if (CamPush > 0f) break;
            }

            // the fallback: any tree still in the way of the winners, the beam or the hatch from anywhere on the camera's
            // whole move (the clearest spot can still have one) - or that the camera passes through - is hidden for the cutscene
            HideTreesInWay(new[] { s_Spot + up * 1.2f, s_Spot + up * 3.5f, beamMid, hatch });
        }

        // ------------------------------------------------------------------ trees in the way

        /// <summary>A tree near the base, as the camera sees it: an upright cone of needles (`r` wide at the bottom of its
        /// crown, to a point at `top`) over a trunk. The trees' colliders are only their trunks, so the casts miss their needles.</summary>
        struct TreeShape { public Vector3 Foot; public float R, Top; public ResourceNode Node; public bool Hidden; }
        static readonly List<TreeShape> s_Trees = new List<TreeShape>();
        static readonly List<Renderer> s_HiddenTrees = new List<Renderer>();
        /// <summary>Test hook: how many trees the cutscene hid because no angle saw past them.</summary>
        public static int TreesHidden { get; private set; }

        /// <summary>The standing trees within reach of the shot (from their renderers' bounds; a tree with none showing: the map's usual size).</summary>
        static void GatherTrees()
        {
            s_Trees.Clear();
            TreesHidden = 0;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || !n.IsSpawned || n.Kind.Value != ResourceNode.Tree || n.Amount.Value <= 0) continue;
                var p = n.transform.position;
                if (new Vector2(p.x - s_Spot.x, p.z - s_Spot.z).magnitude > 140f) continue;
                bool any = false;
                Bounds b = default;
                foreach (var r in n.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                }
                var t = new TreeShape { Foot = p, Node = n };
                if (any)
                {
                    t.Foot = new Vector3(b.center.x, Mathf.Min(p.y, b.min.y), b.center.z);
                    t.R = Mathf.Max(b.extents.x, b.extents.z) * 0.9f + 0.3f;
                    t.Top = b.max.y;
                }
                else { t.R = 3.6f; t.Top = p.y + (PsxArt.On ? 20f : 10.5f); }
                if (t.Top - t.Foot.y < 1f) continue;
                s_Trees.Add(t);
            }
        }

        /// <summary>How wide tree t is at height y: its crown is a cone, full width over its bottom 40%, to a point at the top
        /// (never thinner than its trunk); 0 above it or under the ground.</summary>
        static float TreeRadiusAt(in TreeShape t, float y)
        {
            float hgt = t.Top - t.Foot.y;
            if (y > t.Top || y < t.Foot.y - 0.5f) return 0f;
            return Mathf.Max(0.8f, t.R * Mathf.Clamp01((t.Top - y) / (0.6f * hgt)));
        }

        /// <summary>Does the line a..b pass through tree t's needles (or trunk)?</summary>
        static bool Crosses(in TreeShape t, Vector3 a, Vector3 b)
        {
            float fx = a.x - t.Foot.x, fz = a.z - t.Foot.z, dx = b.x - a.x, dz = b.z - a.z;
            float A = dx * dx + dz * dz, B = 2f * (fx * dx + fz * dz), C = fx * fx + fz * fz - t.R * t.R;
            float t0 = 0f, t1 = 1f;
            if (A > 1e-5f)
            {
                float disc = B * B - 4f * A * C;
                if (disc < 0f) return false;
                float s = Mathf.Sqrt(disc);
                t0 = Mathf.Max(0f, (-B - s) / (2f * A));
                t1 = Mathf.Min(1f, (-B + s) / (2f * A));
                if (t0 > t1) return false;
            }
            else if (C > 0f) return false;
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(a, b, Mathf.Lerp(t0, t1, i / (float)steps));
                float r = TreeRadiusAt(t, p.y);
                float qx = p.x - t.Foot.x, qz = p.z - t.Foot.z;
                if (r > 0f && qx * qx + qz * qz < r * r) return true;
            }
            return false;
        }

        /// <summary>Is a tree (not one the cutscene hid) in the way between a and b? Also a test hook.</summary>
        public static bool TreeInWay(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < s_Trees.Count; i++) if (!s_Trees[i].Hidden && Crosses(s_Trees[i], a, b)) return true;
            return false;
        }

        /// <summary>Is this point inside a tree's crown (with a little room round it)?</summary>
        static bool InTree(Vector3 p)
        {
            foreach (var t in s_Trees)
            {
                float r = TreeRadiusAt(t, p.y) + 0.8f;
                float qx = p.x - t.Foot.x, qz = p.z - t.Foot.z;
                if (!t.Hidden && p.y < t.Top + 0.8f && p.y > t.Foot.y - 0.5f && qx * qx + qz * qz < r * r) return true;
            }
            return false;
        }

        /// <summary>Hide (just their pictures) the trees still in the way of these targets from anywhere on the camera's move,
        /// or that it passes through.</summary>
        static void HideTreesInWay(Vector3[] targets)
        {
            const float step = 0.1f;
            for (float e = 0f; e <= Gone + 0.01f; e += step)
            {
                var cam = CamPathAt(e);
                var prev = CamPathAt(Mathf.Max(0f, e - step));
                for (int i = 0; i < s_Trees.Count; i++)
                {
                    var t = s_Trees[i];
                    if (t.Hidden) continue;
                    bool hit = Crosses(t, prev, cam);
                    foreach (var tg in targets) if (!hit && Crosses(t, cam, tg)) hit = true;
                    if (!hit) continue;
                    t.Hidden = true;
                    s_Trees[i] = t;
                    if (t.Node == null) continue;
                    foreach (var r in t.Node.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { r.enabled = false; s_HiddenTrees.Add(r); }
                    TreesHidden++;
                }
            }
        }

        /// <summary>Show the trees the cutscene hid again.</summary>
        static void ShowTrees()
        {
            foreach (var r in s_HiddenTrees) if (r != null) r.enabled = true;
            s_HiddenTrees.Clear();
            s_Trees.Clear();
        }

        static Vector3 s_PushAim;

        /// <summary>The push in on the winners: at most this much of the way in to them (and at most PushDist m - a long
        /// dolly in a couple of seconds is a lurch: the lens does the rest), never nearer than PushKeep (m, flat).</summary>
        const float MaxPush = 0.5f, PushDist = 20f, PushKeep = 28f;
        /// <summary>The push in's timing: it starts (very gently) as the beam comes down, so the blast is still seen wide,
        /// and is all the way in at PushPeak, as the first winner starts up the beam; the way back out takes OutTime.</summary>
        const float PushFrom = BeamOn - 0.2f, PushPeak = LiftStart + 0.5f, OutTime = 2.4f;
        /// <summary>Test hook: how far in (0..MaxPush of the way) the camera pushes in on the winners.</summary>
        public static float CamPush { get; private set; }

        /// <summary>How far in on the winners the camera is (0..1) - one parameter for both the dolly and the lens: it eases
        /// in from as the beam comes down to as the first of them starts up it, and eases back out once the last of them is a little
        /// way up (it used to stay in until they were all in the ship), all the way out a little before the beam goes off.
        /// Both ways it's a smooth ramp (Ease: no jolt as it starts or stops, no sprint in the middle).</summary>
        public static float CloseUp(float e)
        {
            var g = G;
            int n = Mathf.Clamp(g != null ? g.CutsceneRiders.Count : 1, 1, 4);
            float outAt = Mathf.Clamp(LiftStart + (n - 1) * LiftStagger + LiftTime * 0.4f, PushPeak + 0.5f, BeamOff - 0.9f - OutTime);
            return Ease((e - PushFrom) / (PushPeak - PushFrom)) * (1f - Ease((e - outAt) / OutTime));
        }

        /// <summary>A smooth 0..1 ramp for camera moves: its speed rises smoothly over the first 40% (no jolt: the speed and
        /// the acceleration both start at 0), holds, and falls smoothly over the last 40% - so its top speed is only 1.67x the
        /// average (smoothstep's acceleration jumps at the ends; smootherstep peaks at 1.9x).</summary>
        public static float Ease(float x)
        {
            const float a = 0.4f;
            x = Mathf.Clamp01(x);
            static float Ramp(float y) => y * y * y - 0.5f * y * y * y * y; // (the integral of smoothstep)
            float p = x < a ? a * Ramp(x / a)
                : x <= 1f - a ? a * 0.5f + (x - a)
                : (1f - a) - a * Ramp((1f - x) / a);
            return p / (1f - a);
        }

        /// <summary>Where the camera is e seconds in (without its hand-held sway): the slow drift in and round from
        /// CamFrom to CamTo, and the push in on the winners along its clear line to them (PlanCamera checked it).</summary>
        public static Vector3 CamPathAt(float e)
        {
            var pos = BaseAt(e);
            var d = s_PushAim - pos;
            float len = d.magnitude;
            if (len < 0.01f) return pos;
            return pos + d / len * Mathf.Min(CamPush * len, PushDist) * CloseUp(e);
        }

        static Vector3 BaseAt(float e) => Vector3.Lerp(s_CamFrom, s_CamTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / Gone)));

        /// <summary>
        /// The winners' name tags (Hud.DrawVictoryCutscene): each one's name in their team's colour over their head as the
        /// camera's in close - standing in the beam and floating up it - fading out as they reach the hatch.
        /// </summary>
        public static void DrawTags(float k, GUIStyle style)
        {
            var g = G;
            var cam = Camera.main;
            if (!Active || g == null || cam == null || s_Ship == null) return;
            float e = Elapsed;
            float show = Mathf.Clamp01((e - (BlastAt + 0.5f)) / 0.6f);
            if (show <= 0f) return;
            var st = new GUIStyle(style) { fontSize = Mathf.RoundToInt(17 * k), fontStyle = FontStyle.Bold, richText = false };
            for (int i = 0; i < g.CutsceneRiders.Count; i++)
            {
                var p = Rider(g, g.CutsceneRiders[i]);
                if (p == null) continue;
                float u = LiftOf(i);
                float a = show * Mathf.Clamp01((0.85f - u) / 0.2f);
                if (a <= 0.01f) continue;
                var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * (RiderUp(p, u) + 2.5f));
                if (sp.z < 0.5f) continue;
                var r = new Rect(sp.x - 120 * k, Screen.height - sp.y - 14 * k, 240 * k, 28 * k);
                st.normal.textColor = new Color(0f, 0f, 0f, a * 0.85f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), p.DisplayName, st);
                var c = PlayerNet.NameColor(p.Team.Value);
                st.normal.textColor = new Color(c.r, c.g, c.b, a);
                GUI.Label(r, p.DisplayName, st);
            }
        }

        static int Mask => ~(1 << PlayerNet.HitboxLayer);

        /// <summary>A camera spot `dist` out from the spot along `yaw` (from the side the ship comes in from), `lift` over the ground there.</summary>
        static Vector3 Cam(Vector3 spot, float yaw, float dist, float lift)
        {
            var p = spot + Quaternion.Euler(0f, yaw, 0f) * -s_Out * dist;
            p.y = Mathf.Max(MapBuilder.Height(p.x, p.z), spot.y - 2f) + lift;
            return p;
        }

        /// <summary>Is anything solid (not a player) in a fat line between a and b?</summary>
        static bool Blocked(Vector3 a, Vector3 b)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return false;
            foreach (var hit in Physics.SphereCastAll(a, 0.45f, d / len, len, Mask, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<PlayerNet>() == null) return true;
            return false;
        }

        /// <summary>Can the camera at `cam` see point p (nothing but players in the way, a fat line so a gap a hair wide doesn't count)?</summary>
        static bool Clear(Vector3 cam, Vector3 p)
        {
            var d = p - cam;
            float len = d.magnitude;
            if (len < 0.5f) return true;
            // the ship itself (no collider): a camera up above its hull can't see down the beam under it
            float py = s_Hover.y - 1f;
            if ((cam.y - py) * (p.y - py) < 0f)
            {
                var x = Vector3.Lerp(cam, p, (py - cam.y) / (p.y - cam.y));
                if (new Vector2(x.x - s_Hover.x, x.z - s_Hover.z).magnitude < 15.5f) return false;
            }
            // a tree's needles (no collider) hide it as well as a wall does
            if (TreeInWay(cam, p)) return false;
            return !Blocked(cam, cam + d / len * Mathf.Max(0.1f, len - 1.2f));
        }

        /// <summary>The cutscene's camera this frame (PlayerController.LateUpdate uses it instead of your eyes); false when it isn't playing.</summary>
        public static bool CameraPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = default;
            rot = Quaternion.identity;
            fov = 60f;
            if (!Active || s_Ship == null) return false;
            float e = Elapsed;
            // the move: a slow drift in and round, in close on the winners and back out (CamPathAt), a gentle sway like a
            // hand-held camera
            pos = CamPathAt(e) + new Vector3(Mathf.Sin(e * 0.7f) * 0.12f, Mathf.Sin(e * 0.9f + 1f) * 0.08f, Mathf.Cos(e * 0.6f) * 0.12f);
            float close = CloseUp(e);
            // what it looks at: the ship coming in, the winners as the beam takes them up, then the ship leaving - one
            // point between them that slides smoothly from one to the next (it used to jump at each step of the timeline)
            var shipPos = s_Ship.Root.activeSelf ? s_Ship.Root.transform.position : s_TwinklePos;
            var want = Vector3.Lerp(Riser(e), shipPos, LookToShip(e, close));
            if (!s_LookSet) { s_Look = want; s_LookVel = Vector3.zero; s_LookSet = true; }
            // (critically damped: it eases onto what it looks at - no snap, no overshoot)
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            s_Look = Vector3.SmoothDamp(s_Look, want, ref s_LookVel, Mathf.Lerp(0.45f, 0.25f, Smoother((e - DashStart) / 0.6f)), float.PositiveInfinity, dt);
            rot = Quaternion.LookRotation(s_Look - pos);
            // the lens: wide as it comes in, a little tighter as the beam comes on, right in on the winners (with the push
            // in above, on the same ramp) as they stand in the beam and start up it, then - once the last is a little way
            // up, well before they're all in - back out to where it was to see the ship go
            float tight = Smoother((e - (BeamOn - 0.8f)) / 1.6f) * (1f - Smoother((e - BeamOff) / 1.4f));
            fov = Mathf.Lerp(Mathf.Lerp(WideFov, BeamFov, tight), ZoomFov, close);
            return true;
        }

        static Vector3 s_LookVel;

        /// <summary>0..1 with no jolt at either end (smootherstep), clamped.</summary>
        static float Smoother(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (6f * x - 15f) + 10f);
        }

        /// <summary>The bottom of what the camera looks at: the winners on the ground, raised towards the ones floating up the
        /// beam (each counts more the further up it they are, none at the ground or the hatch - so it never jumps from one
        /// to the next as the camera used to).</summary>
        static Vector3 Riser(float e)
        {
            var ground = s_Spot + Vector3.up * 1.4f;
            var g = G;
            if (g == null || e < LiftStart || e >= BeamOff) return ground;
            float hatch = s_Hover.y + AirdropShip.HatchY;
            float sum = 0.15f, y = ground.y * 0.15f;
            for (int i = 0; i < Mathf.Min(g.CutsceneRiders.Count, MaxRiders); i++)
            {
                float u = LiftOf(i);
                if (u <= 0f || u >= 1f) continue;
                float w = Mathf.Sin(u * Mathf.PI);
                sum += w;
                y += w * Mathf.Lerp(ground.y, hatch, u * u * (3f - 2f * u) * 0.85f);
            }
            return new Vector3(s_Spot.x, y / sum, s_Spot.z);
        }

        /// <summary>How far from the winners (Riser) up towards the ship the camera looks: mostly the ship as it comes in,
        /// down towards the winners as the beam comes on (more so in close, so they and their name tags fill the shot), up
        /// at the ship again once the beam's off, and all on it as it dashes off - each step blended smoothly into the next.</summary>
        static float LookToShip(float e, float close)
        {
            float f = Mathf.Lerp(0.75f, 0.3f, Smoother((e - (Arrive - 0.6f)) / (LiftStart - Arrive + 0.6f)));
            f = Mathf.Lerp(f, 0.22f, Smoother((e - LiftStart) / 0.8f));
            f *= 1f - 0.7f * close;
            f = Mathf.Lerp(f, 0.7f, Smoother((e - (BeamOff - 0.2f)) / 1.2f));
            return Mathf.Lerp(f, 1f, Smoother((e - (DashStart - 0.3f)) / 0.7f));
        }
    }

    /// <summary>A chunk of the winners' base thrown out by the cutscene's blast: it flies, tumbles, falls, and shrinks away at the end.</summary>
    public class VictoryChunk : MonoBehaviour
    {
        Vector3 m_Vel, m_Spin, m_Size;
        float m_Age, m_Life, m_Grav;

        public static void Spawn(Vector3 pos, Vector3 vel, Material mat, Vector3 size, float life, float grav)
        {
            var go = Art.Part(null, Art.Cube, Color.white, pos, size, Random.rotation.eulerAngles, false, mat, "VictoryChunk");
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var c = go.AddComponent<VictoryChunk>();
            c.m_Vel = vel; c.m_Size = size; c.m_Life = life; c.m_Grav = grav;
            c.m_Spin = Random.insideUnitSphere * 420f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            if (m_Age >= m_Life) { Destroy(gameObject); return; }
            m_Vel += Vector3.down * m_Grav * dt;
            var p = transform.position + m_Vel * dt;
            // it lands (and stays put) rather than falling through the ground
            float ground = MapBuilder.Height(p.x, p.z) + 0.05f;
            if (p.y < ground && m_Vel.y < 0f) { p.y = ground; m_Vel = Vector3.zero; m_Spin = Vector3.zero; }
            transform.position = p;
            transform.Rotate(m_Spin * dt, Space.Self);
            transform.localScale = m_Size * Mathf.Clamp01((m_Life - m_Age) / 0.5f);
        }
    }
}
