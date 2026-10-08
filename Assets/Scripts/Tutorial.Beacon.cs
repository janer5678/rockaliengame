using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The tutorial's beacon: wherever the current step wants you to go (its Target), a tall see-through beam of yellow
    /// light stands on the ground, a big arrow bobs over the spot pointing down at it and a ring pulses round its foot -
    /// so you can see where to go from anywhere without reading the panel. Only on this machine, only in the tutorial.
    /// </summary>
    public static partial class Tutorial
    {
        static GameObject s_Beacon;
        static Transform s_BeaconArrow, s_BeaconRing, s_BeaconBeam;
        static Vector3 s_BeaconAt;
        static readonly Color BeaconColor = new Color(1f, 0.82f, 0.29f);

        static Material Glow(Color c, float strength)
        {
            var m = new Material(Art.Mat(c));
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * strength); }
            return m;
        }

        static void BuildBeacon()
        {
            s_Beacon = new GameObject("TutorialBeacon");
            var t = s_Beacon.transform;
            // the beam: a tall see-through column of light
            var beamMat = Art.Ghost(new Color(BeaconColor.r, BeaconColor.g, BeaconColor.b, 0.28f));
            s_BeaconBeam = Art.Part(t, Art.Cylinder, BeaconColor, new Vector3(0, 30f, 0), new Vector3(0.7f, 30f, 0.7f), default, false, beamMat, "beam").transform;
            // the arrow: a shaft and a head pointing down
            var glow = Glow(BeaconColor, 2.2f);
            s_BeaconArrow = new GameObject("arrow").transform;
            s_BeaconArrow.SetParent(t, false);
            Art.Box(s_BeaconArrow, BeaconColor, new Vector3(0, 1.1f, 0), new Vector3(0.38f, 1.1f, 0.38f), default, false, glow);
            Art.Part(s_BeaconArrow, Art.Cone, BeaconColor, new Vector3(0, 0.6f, 0), new Vector3(1.2f, 0.6f, 1.2f), new Vector3(180f, 0, 0), false, glow, "head");
            // the ring on the ground
            s_BeaconRing = new GameObject("ring").transform;
            s_BeaconRing.SetParent(t, false);
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f;
                Art.Box(s_BeaconRing, BeaconColor, Quaternion.Euler(0, a, 0) * new Vector3(0, 0.05f, 1.6f), new Vector3(0.62f, 0.06f, 0.16f), new Vector3(0, a, 0), false, glow);
            }
            foreach (var r in s_Beacon.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var c in s_Beacon.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        }

        /// <summary>Every frame (Tutorial.Tick): the beacon on the step's target, or hidden (null).</summary>
        static void UpdateBeacon(Vector3? target)
        {
            if (!target.HasValue) { HideBeacon(); return; }
            if (s_Beacon == null) BuildBeacon();
            if (!s_Beacon.activeSelf) s_Beacon.SetActive(true);
            var at = target.Value;
            // the ground under the spot (what's built there too), for the ring and the foot of the beam
            float ground = MapBuilder.Height(at.x, at.z);
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 30f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) ground = hit.point.y;
            var foot = new Vector3(at.x, ground, at.z);
            // glide to a new target rather than jump
            s_BeaconAt = (s_BeaconAt - foot).sqrMagnitude > 400f ? foot : Vector3.Lerp(s_BeaconAt, foot, 1f - Mathf.Exp(-10f * Time.deltaTime));
            s_Beacon.transform.position = s_BeaconAt;
            float over = Mathf.Max(at.y - ground, 0f);
            s_BeaconArrow.localPosition = new Vector3(0, over + 0.9f + Mathf.Abs(Mathf.Sin(Time.time * 3.2f)) * 0.7f, 0);
            s_BeaconArrow.localRotation = Quaternion.Euler(0, Time.time * 90f, 0);
            float pulse = 1f + 0.18f * Mathf.Sin(Time.time * 5f);
            s_BeaconRing.localScale = new Vector3(pulse, 1f, pulse);
            s_BeaconRing.localRotation = Quaternion.Euler(0, -Time.time * 40f, 0);
            // the beam fades away up close (so it doesn't fill the screen when you're standing in it)
            var me = Me;
            float d = me != null ? Vector3.Distance(new Vector3(me.transform.position.x, 0, me.transform.position.z), new Vector3(at.x, 0, at.z)) : 99f;
            s_BeaconBeam.gameObject.SetActive(d > 4f);
        }

        static void HideBeacon()
        {
            if (s_Beacon != null && s_Beacon.activeSelf) s_Beacon.SetActive(false);
        }
    }
}
