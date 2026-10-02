using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The giant alien ship that beams airdrops down (local visuals only, driven by the lane's synced start time and spot on
    /// every peer, so they all see the same thing at the same moment): it drops out of the sky, hovers high above the drop
    /// spot - over the map's glass dome (HoverAt) - opens a round hatch in its belly, and its purple beam comes down onto
    /// the glass and cuts a round hole in it (MapDome.SetHole: a rim in the frame's style round it, its edge glowing as it
    /// opens), then carries on down to the ground; the crate comes down the beam (slowly) through the hole. Once the crate
    /// is down and the beam is off, the hole closes up again (patched, its edge glowing), the hatch shuts, and only then
    /// does the ship fly off over the nearest edge of the map - shrinking away to nothing.
    /// The server spawns the real (networked) crate on the ground when the beam gets it there; the dome's collider stays
    /// whole the whole time (the crate coming down is only a picture), so nothing can get out through the hole.
    /// </summary>
    public class AirdropShip
    {
        const float Arrive = 4f, Hover = 45f, Leave = 6f;
        /// <summary>The hole in the glass (seconds after the lane starts): the beam is down on the glass just before
        /// CutStart and starts cutting then, the hole is all the way open OpenTime later; once the crate's down and the beam's off it closes up
        /// from SealStart to SealEnd; the ship leaves at LeaveStart and is gone at Gone.</summary>
        public const float CutStart = NetGame.DropArrive + 0.2f, OpenTime = 0.9f;
        public const float SealStart = NetGame.DropLand + 0.8f, SealEnd = SealStart + 1.4f;
        public const float LeaveStart = SealEnd + 0.4f, Gone = LeaveStart + Leave;
        /// <summary>How wide the hole is (a little wider than the beam).</summary>
        public const float HoleR = HatchR * 1.2f;
        /// <summary>Seconds for the hatch to swing open (it's open before the beam starts at NetGame.DropArrive) or shut.</summary>
        const float HatchTime = 0.8f;
        /// <summary>The hatch: radius of the hole and how far below the ship's centre it is.</summary>
        const float HatchR = 5.5f;
        /// <summary>How far below the ship's centre the hatch is: just under the bottom of the hull (worked out from the sphere mesh's real size).</summary>
        static float HatchY => -Art.Sphere.bounds.extents.y * 4.5f - 0.04f;
        /// <summary>The built-in cylinder's real radius and half height (to size discs in metres).</summary>
        static float CylR => Art.Cylinder.bounds.extents.x;
        static float CylH => Art.Cylinder.bounds.extents.y;
        static readonly Color k_Glow = new Color(0.75f, 0.35f, 1f);
        GameObject s_Ship, s_Beam, s_Crate;
        Transform s_Rim, s_DoorL, s_DoorR;
        Light s_ShipLight;
        Light s_GroundLight;
        Material s_BeamMat;
        Vector3 s_Hover, s_Out;
        /// <summary>The height of the glass over the drop spot (float.MinValue: no glass there, no hole).</summary>
        float s_GlassY;
        /// <summary>This lane's number (its hole's id in the dome).</summary>
        int s_LaneId;

        /// <summary>
        /// Where the ship hovers over a drop spot: Hover metres up, or higher if the map's glass dome is in the way - then
        /// it hovers clear over the glass (its open hatch doors too, all the way across the hull) and beams down through it.
        /// </summary>
        public static Vector3 HoverAt(Vector3 ground)
        {
            float y = ground.y + Hover;
            float hullR = Mathf.Max(26f * Art.Sphere.bounds.extents.x, 14.6f + 0.6f);
            float below = -HatchY + HatchR + 2.5f; // (the open doors hang about this far under its middle)
            float glass = MapDome.HighestOver(ground.x, ground.z, hullR);
            if (glass > float.MinValue) y = Mathf.Max(y, glass + below);
            return new Vector3(ground.x, y, ground.z);
        }
        double s_Start = -2;
        static readonly AirdropShip[] s_Lanes = MakeLanes();

        static AirdropShip[] MakeLanes()
        {
            var a = new AirdropShip[NetGame.LaneTotal];
            for (int i = 0; i < a.Length; i++) a[i] = new AirdropShip { s_LaneId = i };
            return a;
        }

        public static void Clear()
        {
            foreach (var l in s_Lanes) l.ClearLane();
        }

        void ClearLane()
        {
            if (s_Ship) Object.Destroy(s_Ship);
            if (s_Beam) Object.Destroy(s_Beam);
            if (s_Crate) Object.Destroy(s_Crate);
            if (s_GroundLight) Object.Destroy(s_GroundLight.gameObject);
            if (s_BeamMat) Object.Destroy(s_BeamMat);
            MapDome.SetHole(s_LaneId, 0f, 0f, 0f, 0f);
            s_Ship = s_Beam = s_Crate = null;
            s_DoorL = s_DoorR = null;
            s_ShipLight = null;
            s_Start = -2;
        }

        public static void Tick(NetGame g)
        {
            for (int i = 0; i < s_Lanes.Length; i++) s_Lanes[i].TickLane(g, g.LaneStartAt(i), g.LanePosAt(i));
        }

        void TickLane(NetGame g, double start, Vector3 ground)
        {
            float e = start < 0 ? -1f : (float)(g.NetworkManager.ServerTime.Time - start);
            if (e < 0f || e > Gone)
            {
                if (s_Ship) ClearLane();
                return;
            }
            if (s_Ship == null || s_Start != start)
            {
                ClearLane();
                s_Start = start;
                Build(ground);
                s_Hover = HoverAt(ground);
                // it comes in from (and leaves towards) the edge of the map it's nearest, so it's always over the glass dome
                var outward = new Vector3(ground.x, 0, ground.z);
                s_Out = outward.sqrMagnitude > 4f ? outward.normalized : new Vector3(0.83f, 0, 0.55f);
                Sfx.Play2D(Sfx.Hum, 0.35f, 0f);
            }

            var hover = s_Hover;
            float beamT = NetGame.DropArrive, land = NetGame.DropLand;
            Vector3 pos;
            float scale = 1f;
            if (e < Arrive)
            {
                float k = e / Arrive;
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                pos = Vector3.Lerp(hover + s_Out * 72f + Vector3.up * 300f, hover, k);
            }
            else if (e < LeaveStart) pos = hover + Vector3.up * Mathf.Sin(e * 2f) * 0.4f; // (it waits for the hole to be patched)
            else
            {
                // flying off into the distance: it drifts up and away and shrinks smoothly to nothing (no popping out)
                float k = Mathf.Clamp01((e - LeaveStart) / Leave);
                float ease = k * k * (3f - 2f * k);
                pos = hover + (s_Out * 48f + Vector3.up * 60f) * ease;
                scale = Mathf.Pow(1f - ease, 1.5f);
            }
            s_Ship.transform.position = pos;
            s_Ship.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            ShipScale = scale;
            s_Rim.Rotate(0, 60f * Time.deltaTime, 0, Space.Self);

            // the round hatch underneath: swings open once the ship is hovering, shuts again after the beam
            float open = Mathf.Clamp01(Mathf.Min((e - (beamT - 0.2f - HatchTime)) / HatchTime, 1f - (e - (land + 0.6f)) / HatchTime));
            open = open * open * (3f - 2f * open);
            HatchOpen = open;
            s_DoorR.localRotation = Quaternion.Euler(0, 0, 100f * open);
            s_DoorL.localRotation = Quaternion.Euler(0, 0, -100f * open);
            s_ShipLight.intensity = (3f + 5f * open) * scale;

            // the hole in the glass under it: cut open when the beam gets there, patched once the crate's down and the
            // beam's off (its edge glows while it's opening or closing)
            bool glass = s_GlassY > ground.y + 1f;
            float holeK = 0f;
            if (glass)
            {
                float opening = Mathf.Clamp01((e - CutStart) / OpenTime), sealing = Mathf.Clamp01((e - SealStart) / (SealEnd - SealStart));
                holeK = Mathf.Min(opening, 1f - sealing);
                holeK = holeK * holeK * (3f - 2f * holeK);
                float glow = opening < 1f || sealing > 0f ? 1f : 0.35f;
                MapDome.SetHole(s_LaneId, ground.x, ground.z, HoleR * holeK, glow);
            }
            if (s_LaneId == 0) HoleOpen = holeK;

            // the beam out of the open hatch: down onto the glass, where it waits for the hole, then on down to the
            // ground, with the crate sliding down it
            bool beaming = e >= beamT - 0.2f && e < land + 0.8f;
            s_Beam.SetActive(beaming);
            if (beaming)
            {
                float top = pos.y + HatchY * scale;
                float stop = glass && MapDome.Shown ? s_GlassY : ground.y;
                float reach = Mathf.Clamp01((e - (beamT - 0.2f)) / (CutStart - 0.15f - beamT + 0.2f)); // (on the glass a moment before it cuts)
                float bottom = Mathf.Lerp(top, stop, reach);
                if (stop > ground.y) bottom = Mathf.Lerp(bottom, ground.y, Mathf.Clamp01((e - CutStart - OpenTime * 0.5f) / 0.5f));
                if (s_LaneId == 0) BeamBottom = bottom;
                s_Beam.transform.position = new Vector3(ground.x, (top + bottom) * 0.5f, ground.z);
                s_Beam.transform.localScale = new Vector3(HatchR * 0.95f * scale / CylR, Mathf.Max(0.01f, top - bottom) * 0.5f / CylH, HatchR * 0.95f * scale / CylR);
                float a = e < beamT ? (e - beamT + 0.2f) / 0.2f : e > land ? 1f - (e - land) / 0.8f : 1f;
                var c = k_Glow;
                c.a = 0.28f * Mathf.Clamp01(a) * (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
                s_BeamMat.SetColor("_BaseColor", c);
                s_GroundLight.intensity = bottom < ground.y + 0.5f ? 5f * Mathf.Clamp01(a) : 0f;
            }
            else s_GroundLight.intensity = 0f;
            bool crate = e >= beamT && e < land;
            s_Crate.SetActive(crate);
            if (crate)
            {
                float k = (e - beamT) / (land - beamT);
                s_Crate.transform.position = Vector3.Lerp(hover + Vector3.up * (HatchY - 1.2f), ground, k * k * (3f - 2f * k));
                s_Crate.transform.Rotate(0, 45f * Time.deltaTime, 0);
            }
        }

        void Build(Vector3 ground)
        {
            var metal = new Color(0.42f, 0.45f, 0.5f);
            s_Ship = new GameObject("AirdropShip");
            var t = s_Ship.transform;
            Art.Part(t, Art.Sphere, metal, Vector3.zero, new Vector3(26f, 4.5f, 26f));
            Art.Part(t, Art.Cylinder, metal * 0.8f, new Vector3(0, -0.2f, 0), new Vector3(30f, 0.35f, 30f));
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 2f, 0), new Vector3(9f, 5f, 9f), default, false, Art.Ghost(new Color(0.6f, 0.9f, 1f, 0.5f)));
            // the hatch: a dark frame ring, the glowing hole, and two half-round doors hinged on its edge
            Art.Part(t, Art.Cylinder, metal * 0.45f, new Vector3(0, HatchY + 0.1f, 0), new Vector3(HatchR * 1.2f / CylR, 0.05f / CylH, HatchR * 1.2f / CylR)); // (HatchY + 0.05 .. 0.15: a dark ring just above the hole)
            Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, HatchY, 0), new Vector3(HatchR / CylR, 0.03f / CylH, HatchR / CylR), default, false, GlowMat(), "hatch hole"); // (HatchY +- 0.03: below the frame, above the doors)
            s_DoorR = HatchDoor(t, 1f, metal * 0.85f);
            s_DoorL = HatchDoor(t, -1f, metal * 0.85f);
            s_Rim = new GameObject("rim").transform;
            s_Rim.SetParent(t, false);
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                Art.Box(s_Rim, i % 2 == 0 ? k_Glow : new Color(1f, 0.85f, 0.4f), new Vector3(Mathf.Sin(a) * 14.6f, -0.2f, Mathf.Cos(a) * 14.6f), new Vector3(1.2f, 0.5f, 1.2f));
            }
            foreach (var r in s_Ship.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var sl = new GameObject("shipLight");
            sl.transform.SetParent(t, false);
            sl.transform.localPosition = new Vector3(0, -4f, 0);
            var l = s_ShipLight = sl.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = k_Glow;
            l.range = 40f;
            l.intensity = 3f;

            s_BeamMat = new Material(Art.Ghost(k_Glow));
            s_Beam = Art.Part(null, Art.Cylinder, Color.white, ground, Vector3.one, default, false, s_BeamMat, "AirdropBeam");
            s_Beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var gl = new GameObject("beamLight");
            gl.transform.position = ground + Vector3.up * 2f;
            s_GroundLight = gl.AddComponent<Light>();
            s_GroundLight.type = LightType.Point;
            s_GroundLight.color = k_Glow;
            s_GroundLight.range = 14f;
            s_Beam.SetActive(false);

            // where the beam meets the map's glass dome (it cuts its hole there)
            s_GlassY = MapDome.HeightAt(ground.x, ground.z);

            s_Crate = new GameObject("AirdropCrateFalling");
            Container.CreateVisual(Container.Airdrop, 2, s_Crate.transform, Art.Ghost(new Color(0.8f, 0.55f, 1f, 0.85f)));
            s_Crate.SetActive(false);
        }

        /// <summary>Test hooks: how open the newest ship's hatch is (0 shut, 1 open) and how big the ship is (1, shrinking to 0 as it leaves).</summary>
        public static float HatchOpen { get; private set; }
        public static float ShipScale { get; private set; } = 1f;
        /// <summary>Test hook: the first lane's ship (null when none is flying).</summary>
        public static Transform ShipTransform => s_Lanes[0].s_Ship != null ? s_Lanes[0].s_Ship.transform : null;
        /// <summary>Test hooks: how open the first lane's hole in the glass is (0 none / patched, 1 all the way open), and
        /// how far down its beam reaches.</summary>
        public static float HoleOpen { get; private set; }
        public static float BeamBottom { get; private set; }
        /// <summary>Test hook: the first lane's crate on its way down the beam (null when there's none).</summary>
        public static Transform FallingCrate => s_Lanes[0].s_Crate != null && s_Lanes[0].s_Crate.activeSelf ? s_Lanes[0].s_Crate.transform : null;
        /// <summary>Test hook: where the first lane's ship hovers.</summary>
        public static Vector3 HoverPoint => s_Lanes[0].s_Hover;

        static Material s_GlowMat;

        /// <summary>Unlit glowing purple (the hatch hole and the door seams).</summary>
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
