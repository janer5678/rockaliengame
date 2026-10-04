using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The giant alien ship that beams airdrops down (local visuals only, driven by the lane's synced start time and spot on
    /// every peer, so they all see the same thing at the same moment): it drops out of the sky, hovers high above the drop
    /// spot - over the map's glass dome (HoverAt) - opens a round hatch in its belly while a round hole opens in the glass
    /// under it (MapDome.SetHole: a rim in the frame's style round it, its edge glowing as it opens); only once the hole
    /// is all the way open does its purple beam come out, straight down through the hole to the ground (it never touches
    /// the glass); the crate comes down the beam (slowly) through the hole. Once the crate
    /// is down and the beam is off, the hole closes up again (patched, its edge glowing), the hatch shuts, and only then
    /// does the ship fly off over the nearest edge of the map - shrinking away to nothing.
    /// The server spawns the real (networked) crate on the ground when the beam gets there; the dome's collider stays
    /// whole the whole time (the crate coming down is only a picture), so nothing can get out through the hole.
    ///
    /// Every flight is its own object (one per lane start), so a ship that's flying off always finishes shrinking away even
    /// if its lane has already started the next drop (it used to be torn down on the spot - the ship "popped" out of
    /// existence). Its whole path stays low (it comes in from about 110 m over its hover point and leaves under the
    /// clouds, never climbing into them - from far away, clouds in the way used to swallow it whole as it climbed off -
    /// and nowhere near the sky's distant planets).
    /// </summary>
    public class AirdropShip
    {
        const float Arrive = 4f, Leave = 6f;
        /// <summary>How high it hovers over the drop spot (it used to be 45 m), but never above HoverCeiling - its top stays
        /// well under the clouds (their undersides start about 100 m up) and nowhere near the Highlands' sky planets (over
        /// a kilometre out) - unless the dome's glass makes it go higher.</summary>
        public const float Hover = 70f, HoverCeiling = 88f;
        /// <summary>The way in: from this far out over the map's edge and this high over the hover point.</summary>
        public const float ArriveOut = 90f, ArriveUp = 110f;
        /// <summary>The way out: this far on out over the edge, climbing this much (never above LeaveCeiling: under the clouds).</summary>
        public const float LeaveOut = 75f, LeaveUp = 18f, LeaveCeiling = 98f;
        /// <summary>The hole in the glass (seconds after the lane starts): it starts opening at CutStart (the ship's just
        /// got there, its hatch is swinging open) and is all the way open OpenTime later - before the beam comes out
        /// (BeamStart); once the crate's down and the beam's off it closes up from SealStart to SealEnd; the ship leaves
        /// at LeaveStart and is gone at Gone.</summary>
        public const float BeamStart = NetGame.DropArrive - 0.2f, OpenTime = 0.7f, CutStart = BeamStart - 0.05f - OpenTime;
        /// <summary>Seconds for the beam to get from the hatch down to the ground.</summary>
        public const float BeamReach = 0.6f;
        public const float SealStart = NetGame.DropLand + 0.8f, SealEnd = SealStart + 1.4f;
        public const float LeaveStart = SealEnd + 0.4f, Gone = LeaveStart + Leave;
        /// <summary>How wide the hole is (a little wider than the beam).</summary>
        public const float HoleR = HatchR * 1.2f;
        /// <summary>Seconds for the hatch to swing open (it's open before the beam starts at NetGame.DropArrive) or shut.</summary>
        const float HatchTime = 0.8f;
        /// <summary>The hatch: radius of the hole.</summary>
        public const float HatchR = 5.5f;
        /// <summary>How far below the ship's centre the hatch is: just under the bottom of the hull (worked out from the sphere mesh's real size).</summary>
        public static float HatchY => -Art.Sphere.bounds.extents.y * 4.5f - 0.04f;
        /// <summary>The built-in cylinder's real radius and half height (to size discs in metres).</summary>
        static float CylR => Art.Cylinder.bounds.extents.x;
        static float CylH => Art.Cylinder.bounds.extents.y;
        public static readonly Color Glow = new Color(0.75f, 0.35f, 1f);
        /// <summary>The colour of the light shining down out of the ship's belly.</summary>
        public static readonly Color BellyLight = new Color(0.86f, 0.72f, 1f);

        /// <summary>The ship's moving parts (BuildShip).</summary>
        public class Parts
        {
            public GameObject Root;
            public Transform Rim, DoorL, DoorR;
            public Light Point, Spot, Under;
            public Material BellyGlow;
        }

        Parts m_Ship;
        GameObject m_Beam, m_Crate, m_GroundGlow;
        Light m_GroundLight, m_CrateLight;
        Material m_BeamMat, m_CoreMat, m_HaloMat, m_GroundMat;
        Transform m_Core, m_Halo, m_Main;
        Vector3 m_Hover, m_Out, m_Ground;
        /// <summary>The height of the glass over the drop spot (float.MinValue: no glass there, no hole).</summary>
        float m_GlassY;
        /// <summary>The lane it flies for, its start (server time) and its own hole in the dome.</summary>
        int m_Lane, m_HoleId;
        double m_Start;
        bool m_HoleDone;

        static readonly List<AirdropShip> s_Flights = new List<AirdropShip>();
        static int s_NextHole = 1;

        /// <summary>
        /// Where the ship hovers over a drop spot: Hover metres up, or higher if the map's glass dome is in the way - then
        /// it hovers clear over the glass (its open hatch doors too, all the way across the hull) and beams down through it.
        /// Never higher than HoverCeiling for the height alone. glass: false when the dome's gone (the victory cutscene).
        /// </summary>
        public static Vector3 HoverAt(Vector3 ground, float up = Hover, bool glass = true)
        {
            float y = Mathf.Min(ground.y + up, Mathf.Max(HoverCeiling, ground.y + 12f));
            float hullR = Mathf.Max(26f * Art.Sphere.bounds.extents.x, 14.6f + 0.6f);
            float below = -HatchY + HatchR + 2.5f; // (the open doors hang about this far under its middle)
            float top = glass ? MapDome.HighestOver(ground.x, ground.z, hullR) : float.MinValue;
            if (top > float.MinValue) y = Mathf.Max(y, top + below);
            return new Vector3(ground.x, y, ground.z);
        }

        /// <summary>Which way a ship over this spot comes in from and leaves towards: the edge of the map it's nearest.</summary>
        public static Vector3 OutDir(Vector3 ground)
        {
            var outward = new Vector3(ground.x, 0, ground.z);
            return outward.sqrMagnitude > 4f ? outward.normalized : new Vector3(0.83f, 0, 0.55f);
        }

        /// <summary>Where the ship is (and how big) e seconds after it showed up: in over the edge from ArriveUp metres up,
        /// then hovering from `arrive` until `leave`, then off over the edge and shrinking away to nothing in `leaveTime`.</summary>
        public static Vector3 FlightPos(Vector3 hover, Vector3 outDir, float e, float arrive, float leave, float leaveTime, out float scale)
        {
            scale = 1f;
            if (e < arrive)
            {
                float k = Mathf.Clamp01(e / arrive);
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                return Vector3.Lerp(hover + outDir * ArriveOut + Vector3.up * ArriveUp, hover, k);
            }
            if (e < leave) return hover + Vector3.up * Mathf.Sin(e * 2f) * 0.4f;
            // flying off into the distance: it drifts out and a little up - staying under the clouds - and shrinks smoothly to nothing
            float u = Mathf.Clamp01((e - leave) / leaveTime);
            float ease = u * u * (3f - 2f * u);
            float up = Mathf.Clamp(LeaveCeiling - hover.y, 0f, LeaveUp);
            scale = Mathf.Pow(1f - ease, 1.5f);
            return hover + (outDir * LeaveOut + Vector3.up * up) * ease;
        }

        public static void Clear()
        {
            foreach (var f in s_Flights) f.Destroy(true);
            s_Flights.Clear();
            HatchOpen = 0f;
            ShipScale = 1f;
            HoleOpen = 0f;
        }

        void Destroy(bool hole)
        {
            if (m_Ship != null && m_Ship.Root) Object.Destroy(m_Ship.Root);
            if (m_Beam) Object.Destroy(m_Beam);
            if (m_Crate) Object.Destroy(m_Crate);
            if (m_GroundGlow) Object.Destroy(m_GroundGlow);
            if (m_GroundLight) Object.Destroy(m_GroundLight.gameObject);
            foreach (var m in new[] { m_BeamMat, m_CoreMat, m_HaloMat, m_GroundMat, m_Ship != null ? m_Ship.BellyGlow : null }) if (m) Object.Destroy(m);
            if (hole && !m_HoleDone) MapDome.SetHole(m_HoleId, 0f, 0f, 0f, 0f);
            m_Ship = null;
        }

        /// <summary>Every frame on every peer: a flight for every lane that's started one, each one flying (and shrinking
        /// away) until it's gone - even after its lane has started the next.</summary>
        public static void Tick(NetGame g)
        {
            double now = g.NetworkManager.ServerTime.Time;
            for (int i = 0; i < NetGame.LaneTotal; i++)
            {
                double start = g.LaneStartAt(i);
                if (start < 0 || now - start < 0 || now - start > Gone) continue;
                bool have = false;
                foreach (var f in s_Flights) if (f.m_Lane == i && f.m_Start == start) { have = true; break; }
                if (!have) s_Flights.Add(Begin(i, start, g.LanePosAt(i)));
            }
            Flying = 0;
            for (int k = s_Flights.Count - 1; k >= 0; k--)
            {
                var f = s_Flights[k];
                float e = (float)(now - f.m_Start);
                if (e < 0f || e > Gone)
                {
                    f.Destroy(true);
                    s_Flights.RemoveAt(k);
                    continue;
                }
                f.TickFlight(e);
                Flying++;
            }
            var lead = Lane0;
            if (lead != null) { ShipScale = lead.m_LastScale; HatchOpen = lead.m_LastHatch; }
        }

        static AirdropShip Begin(int lane, double start, Vector3 ground)
        {
            var f = new AirdropShip { m_Lane = lane, m_Start = start, m_Ground = ground, m_HoleId = s_NextHole++ };
            f.Build(ground);
            f.m_Hover = HoverAt(ground);
            f.m_Out = OutDir(ground); // it comes in from (and leaves towards) the edge of the map it's nearest, so it's always over the glass dome
            Sfx.Play2D(Sfx.Hum, 0.35f, 0f);
            return f;
        }

        float m_LastScale = 1f, m_LastHatch;

        void TickFlight(float e)
        {
            var ground = m_Ground;
            var hover = m_Hover;
            float beamT = NetGame.DropArrive, land = NetGame.DropLand;
            var pos = FlightPos(hover, m_Out, e, Arrive, LeaveStart, Leave, out float scale);
            var t = m_Ship.Root.transform;
            t.position = pos;
            t.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            m_LastScale = scale;
            m_Ship.Rim.Rotate(0, 60f * Time.deltaTime, 0, Space.Self);

            // the round hatch underneath: swings open once the ship is hovering, shuts again after the beam
            float open = Mathf.Clamp01(Mathf.Min((e - (BeamStart - HatchTime)) / HatchTime, 1f - (e - (land + 0.6f)) / HatchTime));
            open = open * open * (3f - 2f * open);
            m_LastHatch = open;
            SetHatch(m_Ship, open, scale, pos.y - ground.y);

            // the hole in the glass under it: opened before the beam comes out, patched once the crate's down and the
            // beam's off (its edge glows while it's opening or closing). Its own hole: nothing else touches it.
            bool glass = m_GlassY > ground.y + 1f;
            float holeK = 0f;
            if (glass && !m_HoleDone)
            {
                float opening = Mathf.Clamp01((e - CutStart) / OpenTime), sealing = Mathf.Clamp01((e - SealStart) / (SealEnd - SealStart));
                holeK = Mathf.Min(opening, 1f - sealing);
                holeK = holeK * holeK * (3f - 2f * holeK);
                float glow = opening < 1f || sealing > 0f ? 1f : 0.35f;
                MapDome.SetHole(m_HoleId, ground.x, ground.z, HoleR * holeK, glow);
                if (sealing >= 1f) m_HoleDone = true;
            }
            if (this == Lane0) HoleOpen = holeK;

            // the beam out of the open hatch: straight down through the hole (already open) to the ground, with the
            // crate sliding down it
            bool beaming = e >= BeamStart && e < land + 0.8f;
            m_Beam.SetActive(beaming);
            float groundGlow = 0f;
            if (beaming)
            {
                float top = pos.y + HatchY * scale;
                float bottom = Mathf.Lerp(top, ground.y, Mathf.Clamp01((e - BeamStart) / BeamReach));
                if (this == Lane0) BeamBottom = bottom;
                float a = Mathf.Clamp01(e < beamT ? (e - BeamStart) / 0.2f : e > land ? 1f - (e - land) / 0.8f : 1f);
                float flicker = 0.9f + 0.1f * Mathf.Sin(Time.time * 12f);
                PlaceBeam(m_Beam.transform, m_Main, m_Core, m_Halo, new Vector3(ground.x, 0f, ground.z), top, bottom, HatchR * 0.95f * scale);
                BeamFx.Set(m_BeamMat, Glow, 1.3f * a * flicker);
                BeamFx.Set(m_CoreMat, new Color(0.95f, 0.85f, 1f), 2.0f * a * flicker);
                BeamFx.Set(m_HaloMat, Glow, 0.55f * a);
                if (bottom < ground.y + 0.5f) groundGlow = a;
            }
            m_GroundLight.intensity = 6f * groundGlow;
            m_GroundGlow.SetActive(groundGlow > 0.01f);
            BeamFx.Set(m_GroundMat, Glow, 2.2f * groundGlow);
            bool crate = e >= beamT && e < land;
            m_Crate.SetActive(crate);
            if (crate)
            {
                float k = (e - beamT) / (land - beamT);
                m_Crate.transform.position = Vector3.Lerp(hover + Vector3.up * (HatchY - 1.2f), ground, k * k * (3f - 2f * k));
                m_Crate.transform.Rotate(0, 45f * Time.deltaTime, 0);
                // its light hangs off the side you're looking from
                var cam = Camera.main;
                if (cam != null && m_CrateLight != null)
                {
                    var cp = m_Crate.transform.position + Vector3.up * 0.6f;
                    var toCam = cam.transform.position - cp;
                    m_CrateLight.transform.position = cp + (toCam.sqrMagnitude > 0.01f ? toCam.normalized : Vector3.up) * 2.4f + Vector3.up * 0.8f;
                }
            }
            // the beam goes see-through round the crate coming down it (and the crate's lit up), so you can watch it come
            float clearR = crate ? 1.9f : 0f;
            var cc = m_Crate.transform.position + Vector3.up * 0.7f;
            BeamFx.SetClear(m_BeamMat, 0, cc, clearR);
            BeamFx.SetClear(m_CoreMat, 0, cc, clearR);
            BeamFx.SetClear(m_HaloMat, 0, cc, clearR);
        }

        /// <summary>A beam (main column, bright core, wide faint halo) straight down from `top` to `bottom` over `at`, radius r.</summary>
        public static void PlaceBeam(Transform root, Transform main, Transform core, Transform halo, Vector3 at, float top, float bottom, float r)
        {
            root.position = new Vector3(at.x, (top + bottom) * 0.5f, at.z);
            float h = Mathf.Max(0.01f, top - bottom) * 0.5f / CylH;
            main.localScale = new Vector3(r / CylR, h, r / CylR);
            core.localScale = new Vector3(r * 0.3f / CylR, h, r * 0.3f / CylR);
            halo.localScale = new Vector3(r * 1.45f / CylR, h, r * 1.45f / CylR);
        }

        /// <summary>Swing the hatch doors (0 shut .. 1 open) and set the belly lights; height: how far over the ground it is.</summary>
        public static void SetHatch(Parts s, float open, float scale, float height)
        {
            s.DoorR.localRotation = Quaternion.Euler(0, 0, 100f * open);
            s.DoorL.localRotation = Quaternion.Euler(0, 0, -100f * open);
            s.Point.intensity = (3f + 5f * open) * scale;
            // the light shining down out of its belly: always on, brighter with the hatch open, reaching the ground
            s.Spot.range = Mathf.Max(20f, height + 25f);
            s.Spot.intensity = (1.1f + 1.3f * open) * scale * height * height; // (URP lights fade with the square of the distance)
            BeamFx.Set(s.BellyGlow, Glow, (1.4f + 1.2f * open) * Mathf.Clamp01(scale * 1.5f));
            // the underside lit softly from below (the light shrinks in with the ship as it flies off)
            if (s.Under != null)
            {
                float d = UnderLightDown * Mathf.Max(0.05f, scale);
                s.Under.range = d + 8f * scale;
                s.Under.intensity = UnderLightK * d * d;
            }
        }

        void Build(Vector3 ground)
        {
            m_Ship = BuildShip("AirdropShip");
            m_Beam = new GameObject("AirdropBeam");
            MakeBeam(m_Beam.transform, Glow, -0.7f, out m_BeamMat, out m_CoreMat, out m_HaloMat, out m_Main, out m_Core, out m_Halo);
            m_Beam.SetActive(false);
            // where the beam lands: a glow on the ground and a light
            var gl = new GameObject("beamLight");
            gl.transform.position = ground + Vector3.up * 2f;
            m_GroundLight = gl.AddComponent<Light>();
            m_GroundLight.type = LightType.Point;
            m_GroundLight.color = Glow;
            m_GroundLight.range = 16f;
            m_GroundMat = BeamFx.AsBeam(BeamFx.Glow(Glow, 0f, 1.6f)); // (fades down with the beam as you come up to it)
            m_GroundGlow = BeamFx.Disc(null, m_GroundMat, "AirdropGroundGlow");
            m_GroundGlow.transform.position = ground + Vector3.up * 0.08f;
            m_GroundGlow.transform.localScale = Vector3.one * HatchR * 3.2f;
            m_GroundGlow.SetActive(false);

            // where the beam meets the map's glass dome (it cuts its hole there)
            m_GlassY = MapDome.HeightAt(ground.x, ground.z);

            // the crate coming down the beam: the real crate's solid look (not a see-through ghost any more, so it shows
            // inside the light), without its beacon, lit from in front by a soft white light that comes down with it
            m_Crate = new GameObject("AirdropCrateFalling");
            var vis = Container.CreateVisual(Container.Airdrop, 2, m_Crate.transform, null);
            var bc = vis.transform.Find("beacon");
            if (bc != null) Object.Destroy(bc.gameObject);
            foreach (var r in vis.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var cl = new GameObject("crateLight");
            cl.transform.SetParent(m_Crate.transform, false);
            cl.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var crl = m_CrateLight = cl.AddComponent<Light>();
            crl.type = LightType.Point;
            crl.color = new Color(1f, 0.95f, 1f);
            crl.range = 6f;
            crl.intensity = 4f;
            crl.shadows = LightShadows.None;
            m_Crate.SetActive(false);
        }

        /// <summary>The beam's three layers under `root` (each a column centred on root, sized by PlaceBeam): the main
        /// column, a bright thin core and a wide faint halo; scroll: which way its bands run (+ up).</summary>
        public static void MakeBeam(Transform root, Color c, float scroll, out Material main, out Material core, out Material halo,
            out Transform mainT, out Transform coreT, out Transform haloT)
        {
            // (beams: as bright as the ball's from far away, fading down as you come up to them - Settings > Display > BEAMS)
            main = BeamFx.AsBeam(BeamFx.Column(c, 0f, 1.2f, 0.3f, scroll, 0.02f, 0.04f, 0.18f));
            core = BeamFx.AsBeam(BeamFx.Column(new Color(0.95f, 0.85f, 1f), 0f, 2.2f, 0.2f, scroll * 1.5f, 0.02f, 0.04f, 0.3f));
            halo = BeamFx.AsBeam(BeamFx.Column(c, 0f, 2.5f, 0f, 0f, 0.05f, 0.1f));
            mainT = BeamFx.Cylinder(root, main, "beam").transform;
            coreT = BeamFx.Cylinder(root, core, "beam core").transform;
            haloT = BeamFx.Cylinder(root, halo, "beam halo").transform;
        }

        /// <summary>
        /// The ship itself (also the victory UFO): a big flying saucer with a glass dome on top, a turning rim of lights, a
        /// round hatch in its belly (two half-round doors that swing down), a ring of glowing lights round its underside,
        /// a soft glow under it, a purple light round it and a spotlight shining down out of its belly.
        /// </summary>
        public static Parts BuildShip(string name)
        {
            var p = new Parts();
            var metal = new Color(0.42f, 0.45f, 0.5f);
            p.Root = new GameObject(name);
            var t = p.Root.transform;
            Art.Part(t, Art.Sphere, metal, Vector3.zero, new Vector3(26f, 4.5f, 26f));
            Art.Part(t, Art.Cylinder, metal * 0.8f, new Vector3(0, -0.2f, 0), new Vector3(30f, 0.35f, 30f));
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 2f, 0), new Vector3(9f, 5f, 9f), default, false, Art.Ghost(new Color(0.6f, 0.9f, 1f, 0.5f)));
            // the hatch: a dark frame ring, the glowing hole, and two half-round doors hinged on its edge
            Art.Part(t, Art.Cylinder, metal * 0.45f, new Vector3(0, HatchY + 0.1f, 0), new Vector3(HatchR * 1.2f / CylR, 0.05f / CylH, HatchR * 1.2f / CylR)); // (HatchY + 0.05 .. 0.15: a dark ring just above the hole)
            Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, HatchY, 0), new Vector3(HatchR / CylR, 0.03f / CylH, HatchR / CylR), default, false, GlowMat(), "hatch hole"); // (HatchY +- 0.03: below the frame, above the doors)
            p.DoorR = HatchDoor(t, 1f, metal * 0.85f);
            p.DoorL = HatchDoor(t, -1f, metal * 0.85f);
            p.Rim = new GameObject("rim").transform;
            p.Rim.SetParent(t, false);
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                Art.Box(p.Rim, i % 2 == 0 ? Glow : new Color(1f, 0.85f, 0.4f), new Vector3(Mathf.Sin(a) * 14.6f, -0.2f, Mathf.Cos(a) * 14.6f), new Vector3(1.2f, 0.5f, 1.2f));
            }
            // a ring of glowing lights round the underside (they stick out under the hull, so they're seen from below)
            float hy = Art.Sphere.bounds.extents.y * 4.5f;
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 12f, r = 9.5f;
                float y = -hy * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(r / 13f)));
                Art.Part(t, Art.Sphere, Color.white, new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r), new Vector3(1.5f, 0.55f, 1.5f), default, false, GlowMat(), "belly light");
            }
            foreach (var r in p.Root.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            // the soft glow under it, round the hatch
            p.BellyGlow = BeamFx.Glow(Glow, 1.4f, 1.3f);
            var bg = BeamFx.Disc(t, p.BellyGlow, "belly glow");
            bg.transform.localPosition = new Vector3(0, HatchY - 0.35f, 0);
            bg.transform.localScale = Vector3.one * 30f;
            var sl = new GameObject("shipLight");
            sl.transform.SetParent(t, false);
            sl.transform.localPosition = new Vector3(0, -4f, 0);
            var l = p.Point = sl.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Glow;
            l.range = 40f;
            l.intensity = 3f;
            // the light shining down out of its belly onto the ground under it
            var sp = new GameObject("bellySpot");
            sp.transform.SetParent(t, false);
            sp.transform.localPosition = new Vector3(0, HatchY - 0.2f, 0);
            sp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var s = p.Spot = sp.AddComponent<Light>();
            s.type = LightType.Spot;
            s.color = BellyLight;
            s.spotAngle = 62f;
            s.innerSpotAngle = 28f;
            s.range = 70f;
            s.intensity = 14f;
            s.shadows = LightShadows.None;
            // a soft light from under it shining back up at the hull, so its underside isn't the same colour as the sky
            // (lit only by the sun from above, it was a flat sky-blue disc from the ground)
            var ul = new GameObject("underLight");
            ul.transform.SetParent(t, false);
            ul.transform.localPosition = new Vector3(0, -UnderLightDown, 0);
            ul.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var u = p.Under = ul.AddComponent<Light>();
            u.type = LightType.Spot;
            u.color = UnderColour;
            u.spotAngle = 128f;
            u.innerSpotAngle = 70f;
            u.range = UnderLightDown + 8f;
            u.intensity = UnderLightK * UnderLightDown * UnderLightDown;
            u.shadows = LightShadows.None;
            return p;
        }

        /// <summary>The light under the hull: how far under its middle it is, how bright it lights the underside, its colour.</summary>
        const float UnderLightDown = 11f, UnderLightK = 1.5f;
        static readonly Color UnderColour = new Color(0.82f, 0.78f, 1f);

        static float Sq(float x) => x * x;

        /// <summary>Test hooks: how open the first lane's newest ship's hatch is (0 shut, 1 open) and how big it is (1, shrinking to 0 as it leaves).</summary>
        public static float HatchOpen { get; private set; }
        public static float ShipScale { get; private set; } = 1f;
        /// <summary>Test hook: how many airdrop ships are in the sky right now (a leaving one counts until it's gone).</summary>
        public static int Flying { get; private set; }
        /// <summary>The first lane's newest flight (null when none).</summary>
        static AirdropShip Lane0
        {
            get
            {
                AirdropShip best = null;
                foreach (var f in s_Flights) if (f.m_Lane == 0 && (best == null || f.m_Start > best.m_Start)) best = f;
                return best;
            }
        }
        /// <summary>Test hook: the first lane's (newest) ship (null when none is flying).</summary>
        public static Transform ShipTransform => Lane0 != null && Lane0.m_Ship != null && Lane0.m_Ship.Root ? Lane0.m_Ship.Root.transform : null;
        /// <summary>Test hook: the ships in the sky, oldest first.</summary>
        public static List<Transform> Ships
        {
            get
            {
                var l = new List<Transform>();
                foreach (var f in s_Flights) if (f.m_Ship != null && f.m_Ship.Root) l.Add(f.m_Ship.Root.transform);
                return l;
            }
        }
        /// <summary>Test hook: the id of the first lane's ship's hole in the dome (MapDome.HoleRadius / HoleRim).</summary>
        public static int HoleId => Lane0 != null ? Lane0.m_HoleId : -1;
        /// <summary>Test hooks: how open the first lane's hole in the glass is (0 none / patched, 1 all the way open), and
        /// how far down its beam reaches.</summary>
        public static float HoleOpen { get; private set; }
        public static float BeamBottom { get; private set; }
        /// <summary>Test hook: the first lane's crate on its way down the beam (null when there's none).</summary>
        public static Transform FallingCrate => Lane0 != null && Lane0.m_Crate != null && Lane0.m_Crate.activeSelf ? Lane0.m_Crate.transform : null;
        /// <summary>Test hook: where the first lane's ship hovers.</summary>
        public static Vector3 HoverPoint => Lane0 != null ? Lane0.m_Hover : Vector3.zero;
        /// <summary>Test hook: the first lane's beam's brightness (0 off).</summary>
        public static float BeamIntensity => Lane0 != null && Lane0.m_Beam != null && Lane0.m_Beam.activeSelf ? BeamFx.Intensity(Lane0.m_BeamMat) : 0f;
        /// <summary>Test hook: the first lane's ship's belly spotlight.</summary>
        public static Light BellySpot => Lane0 != null && Lane0.m_Ship != null ? Lane0.m_Ship.Spot : null;
        /// <summary>Test hook: the first lane's ship's light under its hull (shining up at its underside).</summary>
        public static Light UnderLight => Lane0 != null && Lane0.m_Ship != null ? Lane0.m_Ship.Under : null;
        /// <summary>Test hook: the first lane's beam material (main column).</summary>
        public static Material BeamMaterial => Lane0 != null ? Lane0.m_BeamMat : null;
        /// <summary>Test hook: how far the first lane's beam is see-through round the crate coming down it (0 none).</summary>
        public static float CrateClear => Lane0 != null && Lane0.m_BeamMat != null && Lane0.m_BeamMat.HasProperty("_Clear0") ? Lane0.m_BeamMat.GetVector("_Clear0").w : 0f;

        static Material s_GlowMat;

        /// <summary>Unlit glowing purple (the hatch hole, the door seams and the belly lights).</summary>
        static Material GlowMat()
        {
            if (s_GlowMat != null) return s_GlowMat;
            var sh = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (sh == null || !sh.isSupported) return s_GlowMat = Art.Mat(new Color(0.92f, 0.7f, 1f));
            s_GlowMat = new Material(sh) { name = "hatch glow" };
            s_GlowMat.SetColor("_Color", new Color(0.92f, 0.7f, 1f));
            return s_GlowMat;
        }

        /// <summary>One half of the round hatch: a half disc hinged on the hatch's rim at x = side * HatchR (it swings down).</summary>
        static Transform HatchDoor(Transform ship, float side, Color c)
        {
            var hinge = new GameObject(side > 0 ? "hatch door R" : "hatch door L").transform;
            hinge.SetParent(ship, false);
            hinge.localPosition = new Vector3(side * HatchR, HatchY - 0.06f, 0);
            // the half disc, centred on its own centroid (so the prism's faces point the right way)
            const int n = 10;
            float cx = 4f * HatchR / (3f * Mathf.PI);
            var half = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / n;
                half[i] = new Vector2(side * (Mathf.Cos(a) * HatchR - cx), Mathf.Sin(a) * HatchR);
            }
            var mb = new MeshBatch();
            mb.Frustum(half, 0f, 1f, -0.22f, 1f, true, true, 4f);
            var door = mb.Build(hinge, "door", Art.Mat(c), false);
            door.transform.localPosition = new Vector3(side * (cx - HatchR), 0, 0);
            // a glowing seam along the straight edge, where the two doors meet
            var seam = new MeshBatch();
            seam.Line(new Vector3(-side * (HatchR - 0.1f), -0.24f, -HatchR * 0.95f), new Vector3(-side * (HatchR - 0.1f), -0.24f, HatchR * 0.95f), 0.16f, 0.03f, Vector3.down);
            seam.Build(hinge, "door seam", GlowMat(), false);
            return hinge;
        }
    }
}
