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
    /// beam comes down; one by one the winners float up the beam, spinning, and are sucked in through the hatch (the beam
    /// goes see-through round them, so you see them inside the light); then the beam goes off, the hatch shuts, the ship
    /// dips, tilts, spins up and lifts high up over the mountains' tops (CruiseY), and only then shoots off over the edge
    /// of the map (over the mountains, not into them), vanishing in a twinkle of light. Then the victory screen comes up.
    ///
    /// The camera films it from a hill near the base, not from inside it (a base they've built there hides everything):
    /// PlanCamera tries spots all round the base (further out, higher up, on the higher ground first) and sphere-casts from
    /// each - and from points along the camera's move - to the winners on the ground, up the beam to the hatch and to the
    /// ship coming in, keeping the spot that sees the most of it with nothing (no wall, no roof, no hill, no tree) in the way.
    /// It drifts in and round a little over the cutscene, tracking the ship as it comes in, the winners as they float up and
    /// the ship again as it leaves.
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
        /// Arrive; its hatch opens at HatchAt; the beam comes on at BeamOn; rider i starts floating up at
        /// LiftStart + i * LiftStagger and is in the ship LiftTime later; the beam goes off at BeamOff; the hatch shuts and
        /// the ship winds up from LeaveStart (dips, tilts, spins up, lifts up over the mountains) and dashes off at
        /// DashStart, gone at Gone; the victory screen comes up at Length.</summary>
        public const float Arrive = 4.2f, HatchAt = 3.9f, HatchTime = 0.8f, BeamOn = 4.9f;
        public const float LiftStart = 6.3f, LiftStagger = 0.6f, LiftTime = 3.2f;
        public const int MaxRiders = 8;
        public const float BeamOff = LiftStart + (4 - 1) * LiftStagger + LiftTime + 0.6f;
        public const float LeaveStart = BeamOff + 0.9f, WindUp = 3f, DashStart = LeaveStart + WindUp, DashTime = 2.2f, LeaveTime = WindUp + DashTime;
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

            Cues(e, pos);
            Twinkle(e);
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
            if (Cue(8, Gone - 0.05f)) { Sfx.Play2D(Sfx.Zap, 0.6f, 0f); Sfx.Play2D(Sfx.Unlock, 0.5f, 0f); }
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

        static Color BeamColour(NetGame g) => Color.Lerp(AirdropShip.Glow, Cfg.TeamColor[Mathf.Clamp(g.Winner.Value, 0, 3)], 0.45f);

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
            s_GlowMat = BeamFx.AsBeam(BeamFx.Glow(AirdropShip.Glow, 0f, 1.6f));
            s_Glow = BeamFx.Disc(null, s_GlowMat, "VictoryGroundGlow");
            s_Glow.transform.position = s_Spot + Vector3.up * 0.1f;
            s_Glow.transform.localScale = Vector3.one * AirdropShip.HatchR * 3.2f;
            s_Glow.SetActive(false);
            var gl = new GameObject("VictoryBeamLight");
            gl.transform.position = s_Spot + Vector3.up * 2.5f;
            s_GroundLight = gl.AddComponent<Light>();
            s_GroundLight.type = LightType.Point;
            s_GroundLight.color = AirdropShip.Glow;
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
            s_Puffed.Clear();
            s_Taken.Clear();
            s_Cues.Clear();
        }

        // ------------------------------------------------------------------ the riders

        /// <summary>How far up rider p's body is floated u of the way up the beam (only the picture moves).</summary>
        static float RiderUp(PlayerNet p, float u)
        {
            if (u <= 0f) return 0f;
            float hatch = s_Hover.y + AirdropShip.HatchY - 0.6f;
            float ease = u * u * (3f - 2f * u);
            return Mathf.Max(0f, hatch - p.transform.position.y - 1.2f) * ease + Mathf.Sin(u * Mathf.PI) * 0.4f;
        }

        /// <summary>
        /// Is this player being beamed up right now (and how)? Their body (only the picture - the player stays where they
        /// are) floats up the beam towards the hatch, turning faster and faster, shrinking as it's sucked in; gone: it's
        /// in the ship (hide it). Everyone else - and everyone outside the cutscene - false.
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
            scale = u < 0.7f ? 1f : Mathf.Lerp(1f, 0.15f, (u - 0.7f) / 0.3f);
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
                            if (Physics.CheckSphere(at, 0.8f, Mask, QueryTriggerInteraction.Ignore)) { ok = false; break; }
                            float v = 0f;
                            foreach (var tg in targets) if (Clear(at, tg.p)) v += tg.w;
                            seen = Mathf.Min(seen, v / total);
                        }
                        if (!ok || Blocked(from, to)) continue;
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
            // the move: a slow drift in and round (eased), a gentle sway like a hand-held camera
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / Gone));
            pos = Vector3.Lerp(s_CamFrom, s_CamTo, k);
            pos += new Vector3(Mathf.Sin(e * 0.7f) * 0.12f, Mathf.Sin(e * 0.9f + 1f) * 0.08f, Mathf.Cos(e * 0.6f) * 0.12f);
            // what it looks at: the ship coming in, the winners as the beam takes them up, then the ship leaving
            var shipPos = s_Ship.Root.activeSelf ? s_Ship.Root.transform.position : s_TwinklePos;
            var ground = s_Spot + Vector3.up * 1.4f;
            var g = G;
            var riser = ground;
            if (g != null && e >= LiftStart && e < BeamOff)
            {
                // the newest winner floating up (the beam above them)
                int n = Mathf.Max(1, g.CutsceneRiders.Count);
                int i = Mathf.Clamp(Mathf.FloorToInt((e - LiftStart) / LiftStagger), 0, n - 1);
                float hatch = s_Hover.y + AirdropShip.HatchY;
                float u = LiftOf(i);
                riser = new Vector3(s_Spot.x, Mathf.Lerp(ground.y, hatch, u * u * (3f - 2f * u) * 0.85f), s_Spot.z);
            }
            Vector3 want;
            if (e < Arrive - 0.6f) want = Vector3.Lerp(ground, shipPos, 0.75f);
            else if (e < LiftStart) want = Vector3.Lerp(ground, shipPos, Mathf.Lerp(0.6f, 0.3f, (e - Arrive + 0.6f) / (LiftStart - Arrive + 0.6f)));
            else if (e < BeamOff) want = Vector3.Lerp(riser, shipPos, 0.22f);
            else if (e < DashStart) want = Vector3.Lerp(ground, shipPos, 0.7f);
            else want = shipPos;
            if (!s_LookSet) { s_Look = want; s_LookSet = true; }
            s_Look = Vector3.Lerp(s_Look, want, 1f - Mathf.Exp(-Time.deltaTime * (e > DashStart ? 4f : 2.2f)));
            rot = Quaternion.LookRotation(s_Look - pos);
            // the lens: wide as it comes in, closing in on the winners going up, wide again to see it go
            float tight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - BeamOn) / 1.5f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - BeamOff) / 1.2f)));
            fov = Mathf.Lerp(62f, 52f, tight);
            return true;
        }
    }
}
