using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The victory cutscene: when a team wins with its ball in its machine's socket as the battle timer runs out, everyone
    /// gets the same shot of the winners' base. A UFO like the airdrop ship (AirdropShip.BuildShip) flies in under the
    /// clouds, hovers over the winners' bedrock (cutting a hole in the map's glass dome if it's in the way, like the
    /// airdrops do), opens its hatch and its beam comes down; one by one the winners float up the beam, spinning, and are
    /// sucked in through the hatch; then the beam goes off, the hole is patched, the hatch shuts and the ship flies off over
    /// the edge of the map, shrinking away. Then the victory screen comes up.
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
        /// <summary>The timeline (seconds after the start): the ship comes in and hovers by Arrive; its hatch opens at
        /// HatchAt; the beam comes on at BeamOn and cuts the glass at CutAt; rider i starts floating up at
        /// LiftStart + i * LiftStagger and is in the ship LiftTime later; the beam goes off at BeamOff; the hole is patched
        /// from SealStart to SealEnd; the ship leaves at LeaveStart and is gone at Gone; the victory screen comes up at Length.</summary>
        public const float Arrive = 3.4f, HatchAt = 2.9f, HatchTime = 0.8f, BeamOn = 3.9f, CutAt = BeamOn + 0.25f, OpenTime = 0.9f;
        public const float LiftStart = 5.4f, LiftStagger = 0.5f, LiftTime = 3.3f;
        public const int MaxRiders = 8;
        public const float BeamOff = LiftStart + (4 - 1) * LiftStagger + LiftTime + 0.5f;
        public const float SealStart = BeamOff + 0.1f, SealEnd = SealStart + 1.2f, LeaveStart = SealEnd + 0.3f, LeaveTime = 4.2f;
        public const float Gone = LeaveStart + LeaveTime, Length = Gone + 0.6f;
        /// <summary>How high the UFO hovers over the winners (higher if the dome's in the way).</summary>
        public const float HoverUp = 24f;
        /// <summary>Its hole in the dome (an id no airdrop uses).</summary>
        const int HoleId = 90001;

        static AirdropShip.Parts s_Ship;
        static GameObject s_Beam, s_Glow;
        static Transform s_Main, s_Core, s_Halo;
        static Material s_BeamMat, s_CoreMat, s_HaloMat, s_GlowMat;
        static Light s_GroundLight;
        static double s_Built = -1;
        static Vector3 s_Spot, s_Hover, s_Out, s_CamFrom, s_CamTo, s_Look;
        static float s_GlassY;
        static bool s_HoleDone, s_LookSet;
        static readonly HashSet<ulong> s_Puffed = new HashSet<ulong>(), s_Taken = new HashSet<ulong>();

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

        /// <summary>Every frame on every peer (NetGame.Update): build, fly and tear down the ship and its beam.</summary>
        public static void Tick(NetGame g)
        {
            if (!Active)
            {
                if (s_Ship != null) Clear();
                return;
            }
            float e = Elapsed;
            if (s_Ship == null || s_Built != g.CutsceneAt.Value) Build(g);

            var pos = AirdropShip.FlightPos(s_Hover, s_Out, e, Arrive, LeaveStart, LeaveTime, out float scale);
            var t = s_Ship.Root.transform;
            t.position = pos;
            t.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            ShipScale = e >= Gone ? 0f : scale;
            if (e >= Gone && s_Ship.Root.activeSelf) s_Ship.Root.SetActive(false);
            s_Ship.Rim.Rotate(0, 60f * Time.deltaTime, 0, Space.Self);
            float open = Mathf.Clamp01(Mathf.Min((e - HatchAt) / HatchTime, 1f - (e - (BeamOff + 0.15f)) / HatchTime));
            open = open * open * (3f - 2f * open);
            AirdropShip.SetHatch(s_Ship, open, scale, pos.y - s_Spot.y);

            // a hole in the dome under it, if the glass is in the way (patched again before it leaves)
            bool glass = s_GlassY > s_Spot.y + 1f;
            if (glass && !s_HoleDone)
            {
                float opening = Mathf.Clamp01((e - CutAt) / OpenTime), sealing = Mathf.Clamp01((e - SealStart) / (SealEnd - SealStart));
                float k = Mathf.Min(opening, 1f - sealing);
                k = k * k * (3f - 2f * k);
                MapDome.SetHole(HoleId, s_Spot.x, s_Spot.z, AirdropShip.HoleR * k, opening < 1f || sealing > 0f ? 1f : 0.35f);
                if (sealing >= 1f) s_HoleDone = true;
            }

            // the beam: out of the hatch, down (onto the glass first, if it's there) to the winners, pulling them up
            bool beaming = e >= BeamOn - 0.2f && e < BeamOff;
            if (s_Beam.activeSelf != beaming) s_Beam.SetActive(beaming);
            float groundA = 0f;
            if (beaming)
            {
                float top = pos.y + AirdropShip.HatchY * scale;
                float stop = glass && MapDome.Shown ? s_GlassY : s_Spot.y;
                float bottom = Mathf.Lerp(top, stop, Mathf.Clamp01((e - (BeamOn - 0.2f)) / 0.35f));
                if (stop > s_Spot.y) bottom = Mathf.Lerp(bottom, s_Spot.y - 0.3f, Mathf.Clamp01((e - CutAt - OpenTime * 0.5f) / 0.45f));
                float a = Mathf.Clamp01(Mathf.Min((e - BeamOn + 0.2f) / 0.25f, (BeamOff - e) / 0.4f));
                float flicker = 0.9f + 0.1f * Mathf.Sin(Time.time * 11f);
                AirdropShip.PlaceBeam(s_Beam.transform, s_Main, s_Core, s_Halo, s_Spot, top, bottom, AirdropShip.HatchR * 0.95f * scale);
                var c = BeamColour(g);
                BeamFx.Set(s_BeamMat, c, 0.95f * a * flicker); // (softer than an airdrop's, so you see the winners inside it)
                BeamFx.Set(s_CoreMat, Color.Lerp(c, Color.white, 0.6f), 1.1f * a * flicker);
                BeamFx.Set(s_HaloMat, c, 0.6f * a);
                if (bottom < s_Spot.y + 0.5f) groundA = a;
            }
            s_GroundLight.intensity = 7f * groundA;
            if (s_Glow.activeSelf != groundA > 0.01f) s_Glow.SetActive(groundA > 0.01f);
            BeamFx.Set(s_GlowMat, BeamColour(g), 2.4f * groundA);

            // puffs of light as each rider leaves the ground and as they're taken in
            for (int i = 0; i < g.CutsceneRiders.Count; i++)
            {
                ulong id = g.CutsceneRiders[i];
                var p = Rider(g, id);
                if (p == null) continue;
                float u = LiftOf(i);
                if (u > 0f && s_Puffed.Add(id)) Fx.Play(FxKind.Spawn, p.transform.position, Vector3.up);
                if (u >= 1f && s_Taken.Add(id)) Fx.Play(FxKind.Spawn, pos + Vector3.up * AirdropShip.HatchY * scale, Vector3.down);
            }
        }

        static Color BeamColour(NetGame g) => Color.Lerp(AirdropShip.Glow, Cfg.TeamColor[Mathf.Clamp(g.Winner.Value, 0, 3)], 0.45f);

        static PlayerNet Rider(NetGame g, ulong id) =>
            g.NetworkManager != null && g.NetworkManager.SpawnManager != null && g.NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) && no != null ? no.GetComponent<PlayerNet>() : null;

        static void Build(NetGame g)
        {
            Clear();
            s_Built = g.CutsceneAt.Value;
            s_Spot = g.CutsceneSpot.Value;
            s_Hover = AirdropShip.HoverAt(s_Spot, HoverUp);
            s_Out = AirdropShip.OutDir(s_Spot);
            s_GlassY = MapDome.HeightAt(s_Spot.x, s_Spot.z);
            s_Ship = AirdropShip.BuildShip("VictoryShip");
            s_Ship.Root.transform.position = s_Hover + s_Out * AirdropShip.ArriveOut + Vector3.up * AirdropShip.ArriveUp;
            s_Beam = new GameObject("VictoryBeam");
            AirdropShip.MakeBeam(s_Beam.transform, BeamColour(g), 0.9f, out s_BeamMat, out s_CoreMat, out s_HaloMat, out s_Main, out s_Core, out s_Halo);
            s_Beam.SetActive(false);
            s_GlowMat = BeamFx.Glow(AirdropShip.Glow, 0f, 1.6f);
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
            Sfx.Play2D(Sfx.Hum, 0.5f, 0f);
        }

        /// <summary>Tear it all down (it's over, or the match ended).</summary>
        public static void Clear()
        {
            if (s_Ship != null && s_Ship.Root) Object.Destroy(s_Ship.Root);
            if (s_Ship != null && s_Ship.BellyGlow) Object.Destroy(s_Ship.BellyGlow);
            if (s_Beam) Object.Destroy(s_Beam);
            if (s_Glow) Object.Destroy(s_Glow);
            if (s_GroundLight) Object.Destroy(s_GroundLight.gameObject);
            foreach (var m in new[] { s_BeamMat, s_CoreMat, s_HaloMat, s_GlowMat }) if (m) Object.Destroy(m);
            if (!s_HoleDone) MapDome.SetHole(HoleId, 0f, 0f, 0f, 0f);
            s_Ship = null;
            s_Beam = s_Glow = null;
            s_Built = -1;
            s_HoleDone = false;
            s_LookSet = false;
            s_Puffed.Clear();
            s_Taken.Clear();
        }

        // ------------------------------------------------------------------ the riders

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
            float hatch = s_Hover.y + AirdropShip.HatchY - 0.6f;
            float ease = u * u * (3f - 2f * u);
            up = Mathf.Max(0f, hatch - p.transform.position.y - 1.2f) * ease + Mathf.Sin(u * Mathf.PI) * 0.4f;
            spin = 540f * u * u;
            scale = u < 0.7f ? 1f : Mathf.Lerp(1f, 0.15f, (u - 0.7f) / 0.3f);
            return true;
        }

        // ------------------------------------------------------------------ the camera

        /// <summary>
        /// Pick the shot: from out in front of the winners' base (the side facing the middle of the map), low and far
        /// enough back to get both the winners on the ground and the UFO over them in the frame, trying a few heights and
        /// angles until nothing (a wall they built, a hill) is in the way; it drifts in a little over the cutscene.
        /// </summary>
        static void PlanCamera()
        {
            var inward = -s_Out;
            float h = s_Hover.y - s_Spot.y;
            float dist = Mathf.Max(24f, h * 1.15f);
            var look = s_Spot + Vector3.up * 1.6f;
            int mask = ~(1 << PlayerNet.HitboxLayer);
            Vector3 best = look + inward * dist + Vector3.up * 4f;
            float bestClear = -1f;
            float[] heights = { 4f, 8f, 13f, 19f };
            float[] yaws = { 18f, -18f, 40f, -40f, 0f, 62f, -62f };
            foreach (var y in heights)
            {
                foreach (var yaw in yaws)
                {
                    var dir = Quaternion.Euler(0f, yaw, 0f) * inward;
                    var cand = s_Spot + dir * dist + Vector3.up * y;
                    cand.y = Mathf.Max(cand.y, MapBuilder.Height(cand.x, cand.z) + 2f);
                    var d = cand - look;
                    float len = d.magnitude;
                    float clear = Physics.SphereCast(look, 0.4f, d / len, out var hit, len, mask, QueryTriggerInteraction.Ignore) ? hit.distance : len;
                    if (clear > bestClear + 0.01f) { bestClear = clear; best = clear >= len ? cand : look + d / len * Mathf.Max(3f, clear - 0.6f); }
                    if (clear >= len) goto found;
                }
            }
        found:
            s_CamFrom = best;
            var toLook = look - best;
            toLook.y = 0f;
            s_CamTo = best + toLook * 0.18f;
            s_CamTo.y = Mathf.Max(s_CamTo.y, MapBuilder.Height(s_CamTo.x, s_CamTo.z) + 2f);
            if (Physics.Linecast(s_CamFrom, s_CamTo, mask, QueryTriggerInteraction.Ignore)) s_CamTo = s_CamFrom;
        }

        /// <summary>The cutscene's camera this frame (PlayerController.LateUpdate uses it instead of your eyes); false when it isn't playing.</summary>
        public static bool CameraPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = default;
            rot = Quaternion.identity;
            fov = 62f;
            if (!Active || s_Ship == null) return false;
            float e = Elapsed;
            float k = Mathf.Clamp01(e / Length);
            pos = Vector3.Lerp(s_CamFrom, s_CamTo, k * k * (3f - 2f * k));
            // what it looks at: up at the ship coming in, down at the winners as the beam takes them, then after the ship
            var shipPos = s_Ship.Root.transform.position;
            var ground = s_Spot + Vector3.up * 1.4f;
            float w = e < BeamOn ? 0.55f : e < LiftStart ? Mathf.Lerp(0.55f, 0.32f, (e - BeamOn) / (LiftStart - BeamOn))
                : e < BeamOff ? Mathf.Lerp(0.32f, 0.62f, (e - LiftStart) / (BeamOff - LiftStart)) : e < LeaveStart ? 0.62f : 0.85f;
            var want = Vector3.Lerp(ground, shipPos, w);
            if (!s_LookSet) { s_Look = want; s_LookSet = true; }
            s_Look = Vector3.Lerp(s_Look, want, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            rot = Quaternion.LookRotation(s_Look - pos);
            return true;
        }
    }
}
