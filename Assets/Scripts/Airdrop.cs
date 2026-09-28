using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The giant alien ship that beams airdrops down (local visuals only, driven by NetGame.DropStart / DropPos on every peer):
    /// it drops out of the sky, hovers high above the drop spot, beams the crate down, then flies off.
    /// The server spawns the real (networked) crate when the beam reaches the ground.
    /// </summary>
    public static class AirdropShip
    {
        const float Arrive = 4f, Hover = 45f, Leave = 6f;
        static readonly Color k_Glow = new Color(0.75f, 0.35f, 1f);
        static GameObject s_Ship, s_Beam, s_Crate;
        static Transform s_Rim;
        static Light s_GroundLight;
        static Material s_BeamMat;
        static double s_Start = -2;

        public static void Clear()
        {
            if (s_Ship) Object.Destroy(s_Ship);
            if (s_Beam) Object.Destroy(s_Beam);
            if (s_Crate) Object.Destroy(s_Crate);
            if (s_GroundLight) Object.Destroy(s_GroundLight.gameObject);
            if (s_BeamMat) Object.Destroy(s_BeamMat);
            s_Ship = s_Beam = s_Crate = null;
            s_Start = -2;
        }

        public static void Tick(NetGame g)
        {
            double start = g.DropStart.Value;
            float e = start < 0 ? -1f : (float)(g.NetworkManager.ServerTime.Time - start);
            if (e < 0f || e > NetGame.DropLand + Leave)
            {
                if (s_Ship) Clear();
                return;
            }
            var ground = g.DropPos.Value;
            if (s_Ship == null || s_Start != start)
            {
                Clear();
                s_Start = start;
                Build(ground);
                Sfx.Play2D(Sfx.Hum, 0.35f, 0f);
            }

            var hover = ground + Vector3.up * Hover;
            float beamT = Arrive, land = NetGame.DropLand;
            Vector3 pos;
            if (e < Arrive)
            {
                float k = e / Arrive;
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                pos = Vector3.Lerp(hover + new Vector3(60f, 300f, 40f), hover, k);
            }
            else if (e < land) pos = hover + Vector3.up * Mathf.Sin(e * 2f) * 0.4f;
            else
            {
                float k = (e - land) / Leave;
                pos = hover + new Vector3(-80f, 260f, -30f) * k * k;
            }
            s_Ship.transform.position = pos;
            s_Rim.Rotate(0, 60f * Time.deltaTime, 0, Space.Self);

            // the beam, with the crate sliding down it
            bool beaming = e >= beamT - 0.3f && e < land + 0.8f;
            s_Beam.SetActive(beaming);
            if (beaming)
            {
                float top = pos.y - 1.5f, bottom = ground.y;
                s_Beam.transform.position = new Vector3(ground.x, (top + bottom) * 0.5f, ground.z);
                s_Beam.transform.localScale = new Vector3(7f, (top - bottom) * 0.5f, 7f);
                float a = e < beamT ? (e - beamT + 0.3f) / 0.3f : e > land ? 1f - (e - land) / 0.8f : 1f;
                var c = k_Glow;
                c.a = 0.28f * Mathf.Clamp01(a) * (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
                s_BeamMat.SetColor("_BaseColor", c);
                s_GroundLight.intensity = 5f * Mathf.Clamp01(a);
            }
            else s_GroundLight.intensity = 0f;
            bool crate = e >= beamT && e < land;
            s_Crate.SetActive(crate);
            if (crate)
            {
                float k = (e - beamT) / (land - beamT);
                s_Crate.transform.position = Vector3.Lerp(hover - Vector3.up * 3f, ground, k * k * (3f - 2f * k));
                s_Crate.transform.Rotate(0, 45f * Time.deltaTime, 0);
            }
        }

        static void Build(Vector3 ground)
        {
            var metal = new Color(0.42f, 0.45f, 0.5f);
            s_Ship = new GameObject("AirdropShip");
            var t = s_Ship.transform;
            Art.Part(t, Art.Sphere, metal, Vector3.zero, new Vector3(26f, 4.5f, 26f));
            Art.Part(t, Art.Cylinder, metal * 0.8f, new Vector3(0, -0.2f, 0), new Vector3(30f, 0.35f, 30f));
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 2f, 0), new Vector3(9f, 5f, 9f), default, false, Art.Ghost(new Color(0.6f, 0.9f, 1f, 0.5f)));
            Art.Part(t, Art.Cylinder, k_Glow, new Vector3(0, -2.2f, 0), new Vector3(7f, 0.1f, 7f));
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
            var l = sl.AddComponent<Light>();
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

            s_Crate = new GameObject("AirdropCrateFalling");
            Container.CreateVisual(Container.Airdrop, 2, s_Crate.transform, Art.Ghost(new Color(0.8f, 0.55f, 1f, 0.85f)));
            s_Crate.SetActive(false);
        }
    }
}
