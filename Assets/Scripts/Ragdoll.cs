using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A player who dies drops as a ragdoll (instead of a gravestone): on every screen, the alien's body as it was the
    /// moment they died - pose, team colour, hat - is copied and given limbs of its own (a rigidbody on the hips, spine,
    /// chest, head and each arm and leg bone, joined with character joints), then knocked away from whoever killed them
    /// and left to flop to the ground. A pool of blood spreads under it. After a few seconds it sinks into the ground and
    /// is gone. Only the look: nothing in the game touches it (it's on Unity's Ignore Raycast layer, so shots, building and
    /// pick-ups go straight through it), and its parts don't push players about.
    /// </summary>
    public static class Ragdoll
    {
        /// <summary>How long a body lies there, and how long it then takes to sink away (seconds).</summary>
        public const float Lie = 7f, Sink = 1.5f;
        const int Max = 12;
        const int Layer = 2; // (Ignore Raycast)

        class Body { public GameObject Go; public Transform Hips; public Transform Pool; public float Born; public Vector3 PoolAt; bool m_Pooled; public bool Pooled { get => m_Pooled; set => m_Pooled = value; } }
        static readonly List<Body> s_Bodies = new List<Body>();
        static Material s_Blood;

        /// <summary>(tests) how many ragdolls are lying about right now.</summary>
        public static int Count => s_Bodies.Count;
        /// <summary>(tests) the newest ragdoll's hips (null: none).</summary>
        public static Transform NewestHips => s_Bodies.Count > 0 ? s_Bodies[s_Bodies.Count - 1].Hips : null;

        static readonly string[] k_Bones =
        {
            "Hips", "Spine", "Chest", "Head", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm",
            "LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg",
        };
        static readonly Dictionary<string, string> k_Child = new Dictionary<string, string>
        {
            { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" }, { "Head", null },
            { "LeftUpperArm", "LeftLowerArm" }, { "LeftLowerArm", "LeftHand" }, { "RightUpperArm", "RightLowerArm" }, { "RightLowerArm", "RightHand" },
            { "LeftUpperLeg", "LeftLowerLeg" }, { "LeftLowerLeg", "LeftFoot" }, { "RightUpperLeg", "RightLowerLeg" }, { "RightLowerLeg", "RightFoot" },
        };

        /// <summary>Copies `model` (the dead alien, posed) into a ragdoll and knocks it along `push` (m/s).</summary>
        public static void Spawn(GameObject model, GameObject hat, Vector3 push)
        {
            if (model == null) return;
            while (s_Bodies.Count >= Max) { var old = s_Bodies[0]; s_Bodies.RemoveAt(0); if (old.Go) Object.Destroy(old.Go); if (old.Pool) Object.Destroy(old.Pool.gameObject); }
            var go = Object.Instantiate(model, model.transform.position, model.transform.rotation);
            go.name = "ragdoll";
            go.transform.localScale = model.transform.lossyScale;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // (your own body was shadows-only: it's seen now)
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                if (r.name == "outline copy") r.enabled = false;
            }
            var map = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
            if (hat != null && map.TryGetValue("Head", out var headBone))
            {
                var h = Object.Instantiate(hat, hat.transform.position, hat.transform.rotation);
                foreach (var f in h.GetComponentsInChildren<HatFollow>()) Object.Destroy(f);
                h.transform.SetParent(headBone, true);
            }
            // a rigidbody and a collider on each big bone, joined to the one above it
            var bodies = new Dictionary<string, Rigidbody>();
            foreach (var name in k_Bones)
            {
                if (!map.TryGetValue(name, out var b)) continue;
                var rb = b.gameObject.AddComponent<Rigidbody>();
                rb.mass = name == "Hips" || name == "Chest" ? 8f : name == "Spine" ? 6f : name == "Head" ? 4f : 2.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.linearDamping = 0.15f;
                rb.angularDamping = 0.6f;
                b.gameObject.layer = Layer;
                // the collider along the bone, to its child joint (in the bone's own, scaled space)
                Transform child = k_Child.TryGetValue(name, out var cn) && cn != null && map.TryGetValue(cn, out var ct) ? ct : null;
                var lossy = b.lossyScale;
                float sc = Mathf.Max(0.0001f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);
                if (name == "Head")
                {
                    var sph = b.gameObject.AddComponent<SphereCollider>();
                    sph.radius = 0.17f / sc;
                    sph.center = b.InverseTransformPoint(b.position + model.transform.up * 0.17f);
                }
                else if (child != null)
                {
                    var local = b.InverseTransformPoint(child.position);
                    var cap = b.gameObject.AddComponent<CapsuleCollider>();
                    var a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                    cap.direction = a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
                    float radius = (name == "Hips" || name == "Chest" || name == "Spine" ? 0.15f : name.Contains("Upper") ? 0.07f : 0.055f) / sc;
                    cap.radius = radius;
                    cap.height = local.magnitude + radius * 2f;
                    cap.center = local * 0.5f;
                }
                bodies[name] = rb;
            }
            foreach (var kv in bodies)
            {
                if (kv.Key == "Hips") continue;
                // join it to the nearest bone above it that has a body
                var p = kv.Value.transform.parent;
                Rigidbody parentRb = null;
                while (p != null && parentRb == null) { parentRb = p.GetComponent<Rigidbody>(); p = p.parent; }
                if (parentRb == null) continue;
                var j = kv.Value.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parentRb;
                j.enableProjection = true;
                bool knee = kv.Key.Contains("LowerLeg"), elbow = kv.Key.Contains("LowerArm");
                j.lowTwistLimit = new SoftJointLimit { limit = knee || elbow ? -5f : -30f };
                j.highTwistLimit = new SoftJointLimit { limit = knee || elbow ? 110f : 30f };
                j.swing1Limit = new SoftJointLimit { limit = knee || elbow ? 5f : kv.Key == "Head" ? 35f : 45f };
                j.swing2Limit = new SoftJointLimit { limit = knee || elbow ? 5f : 30f };
            }
            // knocked away from the killer, a little up, tumbling
            foreach (var rb in bodies.Values)
            {
                rb.linearVelocity = push + Random.insideUnitSphere * 0.6f;
                rb.angularVelocity = Random.insideUnitSphere * 2f;
            }
            if (bodies.TryGetValue("Chest", out var chest)) chest.AddForce(push * 4f, ForceMode.VelocityChange);
            s_Bodies.Add(new Body { Go = go, Hips = map.TryGetValue("Hips", out var hips) ? hips : go.transform, Born = Time.time });
        }

        /// <summary>Every frame (Bootstrap): the blood spreading under each body, and the old ones sinking away.</summary>
        public static void Tick()
        {
            for (int i = s_Bodies.Count - 1; i >= 0; i--)
            {
                var b = s_Bodies[i];
                float age = Time.time - b.Born;
                if (b.Go == null) { if (b.Pool) Object.Destroy(b.Pool.gameObject); s_Bodies.RemoveAt(i); continue; }
                // once it's come to rest: a pool of blood spreads out under it
                if (!b.Pooled && age > 0.9f && b.Hips != null)
                {
                    b.Pooled = true;
                    if (Physics.Raycast(b.Hips.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~((1 << Layer) | (1 << PlayerNet.HitboxLayer)), QueryTriggerInteraction.Ignore))
                    {
                        if (s_Blood == null) s_Blood = Art.Mat(new Color(0.35f, 0.02f, 0.02f));
                        float cr = 2f * Art.Cylinder.bounds.extents.x;
                        var pool = Art.Part(null, Art.Cylinder, Color.white, hit.point + hit.normal * 0.015f, new Vector3(0.01f, 0.004f, 0.01f), default, false, s_Blood, "blood pool");
                        pool.transform.up = hit.normal;
                        if (pool.TryGetComponent(out Renderer pr)) pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        b.Pool = pool.transform;
                        b.PoolAt = new Vector3(1.3f / cr, 0.004f, 1.1f / cr);
                    }
                }
                if (b.Pool != null)
                {
                    float grow = Mathf.Clamp01((age - 0.9f) / 2.5f);
                    grow = 1f - (1f - grow) * (1f - grow);
                    float fade = age > Lie ? 1f - Mathf.Clamp01((age - Lie) / Sink) : 1f;
                    b.Pool.localScale = new Vector3(b.PoolAt.x * Mathf.Max(0.05f, grow) * fade, b.PoolAt.y, b.PoolAt.z * Mathf.Max(0.05f, grow) * fade);
                }
                // then it sinks into the ground and is gone (its limbs stop being physical first)
                if (age > Lie)
                {
                    if (age - Time.deltaTime <= Lie)
                        foreach (var rb in b.Go.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; }
                    b.Go.transform.position += Vector3.down * (0.9f / Sink) * Time.deltaTime;
                }
                if (age > Lie + Sink)
                {
                    Object.Destroy(b.Go);
                    if (b.Pool) Object.Destroy(b.Pool.gameObject);
                    s_Bodies.RemoveAt(i);
                }
            }
        }

        /// <summary>Every body gone at once (a new match).</summary>
        public static void Clear()
        {
            foreach (var b in s_Bodies) { if (b.Go) Object.Destroy(b.Go); if (b.Pool) Object.Destroy(b.Pool.gameObject); }
            s_Bodies.Clear();
        }
    }
}
