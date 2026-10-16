using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person arms MODEL: a real, artist-made rigged pair of arms (the CC0 "fps arms (rigged only)" model,
    /// made into the aliens' three fingers and a thumb by Tools/fps_arms.py - Resources/FpsArms/AlienArms), in your team's
    /// colour. The poses don't change: ViewModel.Fingers.cs still works out where each hand goes on each item and how every
    /// finger closes on it, on the invisible pose skeleton (AlienArm: the hand and each finger's joints). Every frame the
    /// model's bones are put onto that skeleton - the hand bone on the wrist, each finger and thumb bone turned the way its
    /// joint is - and each arm reaches its hand from its own shoulder with two-bone IK, so the elbow bends naturally
    /// (down and out to the side). The skeleton's measurements (knuckles, finger lengths and thickness, the palm) are taken
    /// from the model itself, so the grips close on items with the model's real fingers.
    ///
    /// Each arm is its own copy of the model under its arm (m_R / m_L), with the other arm's mesh taken off, so IsHand(),
    /// the hand colour and the post-fx layers all see it as that hand.
    /// </summary>
    public partial class ViewModel
    {
        sealed class ArmRig
        {
            public Transform Root, Hand, UpperArm, Forearm, ForearmEnd;
            public readonly Transform[,] F = new Transform[5, 3];
            /// <summary>Each digit bone's rest turned so it lies along the pose skeleton's joint (+Z, the back of the hand up).</summary>
            public readonly Quaternion[,] Map = new Quaternion[5, 3];
            public Quaternion HandMap, UpperRest, ForeRest, ForeToHand;
            public Vector3 ForeAxis;
        }

        /// <summary>Where the model's origin (between the shoulders) sits in the view: a little below the eye and a touch
        /// forward, so the shoulders are just out of sight and the arms reach into the view from the bottom corners.</summary>
        static readonly Vector3 k_RigOffset = new Vector3(0f, -0.22f, 0.05f);
        /// <summary>Which way each elbow points (the right arm's, in the view; the left's is its mirror): down and out.</summary>
        static readonly Vector3 k_ElbowHint = new Vector3(1.4f, -1f, -0.1f);

        /// <summary>(tests) Where the last pose put each arm's shoulder, elbow and wrist (view space).</summary>
        public static string DebugArm = "", DebugArmL = "";
        static GameObject s_RigPrefab;
        static bool s_RigTried, s_Measured;

        static GameObject RigPrefab
        {
            get
            {
                if (!s_RigTried) { s_RigTried = true; s_RigPrefab = Resources.Load<GameObject>("FpsArms/AlienArms"); }
                return s_RigPrefab;
            }
        }

        static readonly string[] k_FingerBones = { "f_index", "f_middle", "f_ring" };

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        /// <summary>A digit's bone j (0-2) and the joint after it (j 2: the tip).</summary>
        static Transform DigitBone(Transform root, int d, int j, string side)
        {
            string n = d == 4 ? "thumb" : k_FingerBones[d];
            return FindDeep(root, $"{n}.0{j + 1}{side}");
        }
        static Transform DigitNext(Transform root, int d, int j, string side)
        {
            string n = d == 4 ? "thumb" : k_FingerBones[d];
            return FindDeep(root, j < 2 ? $"{n}.0{j + 2}{side}" : $"{n}.03{side}_end");
        }

        /// <summary>A model hand's frame as the pose skeleton has it (world): the wrist, the fingers along +Z, the back of
        /// the hand +Y, the little-finger side +X for the right hand (the thumb side for the left).</summary>
        static Quaternion HandFrame(Transform root, bool right, out Vector3 wrist)
        {
            string s = right ? ".R" : ".L";
            wrist = FindDeep(root, "hand" + s).position;
            var ki = FindDeep(root, "f_index.01" + s).position;
            var km = FindDeep(root, "f_middle.01" + s).position;
            var kr = FindDeep(root, "f_ring.01" + s).position;
            var z = (km - wrist).normalized;
            var x = (kr - ki) * (right ? 1f : -1f);
            x = (x - z * Vector3.Dot(x, z)).normalized;
            return Quaternion.LookRotation(z, Vector3.Cross(z, x));
        }

        /// <summary>Takes the pose skeleton's measurements from the model (once): the knuckles, each bone's length, how thick
        /// each finger is, where the palm is.</summary>
        static void MeasureRig()
        {
            if (s_Measured) return;
            s_Measured = true;
            var prefab = RigPrefab;
            if (prefab == null) return;
            var inst = Object.Instantiate(prefab);
            try
            {
                var root = inst.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var F = HandFrame(root, true, out var w);
                var inv = Quaternion.Inverse(F);
                Vector3 Our(Vector3 p) => inv * (p - w);
                foreach (int d in s_Digits)
                {
                    s_Base[d] = Our(DigitBone(root, d, 0, ".R").position);
                    for (int j = 0; j < 3; j++) s_Len[d * 3 + j] = (DigitNext(root, d, j, ".R").position - DigitBone(root, d, j, ".R").position).magnitude;
                }
                s_Base[3] = s_Base[2]; for (int j = 0; j < 3; j++) s_Len[9 + j] = s_Len[6 + j];
                // the thickness of each bone, and the palm, from the mesh's own vertices
                var smr = FindDeep(root, "ArmR")?.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.isReadable)
                {
                    var mesh = smr.sharedMesh;
                    var verts = mesh.vertices;
                    var weights = mesh.boneWeights;
                    var bones = smr.bones;
                    var m = smr.transform.localToWorldMatrix;
                    float BoneRadius(Transform b, Transform next)
                    {
                        int bi = System.Array.IndexOf(bones, b);
                        if (bi < 0 || next == null) return -1f;
                        var a = b.position; var ab = next.position - a;
                        float sum = 0f; int n = 0;
                        for (int i = 0; i < verts.Length; i++)
                        {
                            if (weights[i].boneIndex0 != bi || weights[i].weight0 < 0.6f) continue;
                            var p = m.MultiplyPoint3x4(verts[i]);
                            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                            if (t < 0.2f || t > 0.8f) continue;
                            sum += (p - (a + ab * t)).magnitude; n++;
                        }
                        return n > 0 ? sum / n : -1f;
                    }
                    foreach (int d in s_Digits)
                    {
                        var r = new float[3];
                        for (int j = 0; j < 3; j++) r[j] = BoneRadius(DigitBone(root, d, j, ".R"), DigitNext(root, d, j, ".R"));
                        if (r[0] <= 0f || r[1] <= 0f || r[2] <= 0f) continue;
                        for (int j = 0; j < 3; j++) r[j] = Mathf.Clamp(r[j], 0.004f, 0.02f);
                        s_Rad[d * 4] = r[0]; s_Rad[d * 4 + 1] = (r[0] + r[1]) * 0.5f; s_Rad[d * 4 + 2] = (r[1] + r[2]) * 0.5f; s_Rad[d * 4 + 3] = r[2] * 0.95f;
                    }
                    for (int k = 0; k < 4; k++) s_Rad[12 + k] = s_Rad[8 + k];
                    // the palm: its underside under the middle of the hand
                    var hand = FindDeep(root, "hand.R");
                    float low = 0f; int pn = 0;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var bw = weights[i];
                        if (bw.weight0 < 0.6f || bones[bw.boneIndex0] == null) continue;
                        var bn = bones[bw.boneIndex0].name;
                        if (bn != "hand.R" && !bn.StartsWith("palm_")) continue;
                        var q = Our(m.MultiplyPoint3x4(verts[i]));
                        if (q.z < s_Base[1].z * 0.3f || q.z > s_Base[1].z * 0.8f || Mathf.Abs(q.x) > 0.02f) continue;
                        if (q.y < low) low = q.y;
                        pn++;
                    }
                    if (pn > 3) PalmHalf = Mathf.Clamp(-low, 0.008f, 0.03f);
                }
                KnuckleZ = s_Base[1].z;
                GripZ = KnuckleZ * 0.8f;
                Debug.Log($"[hands] the arms model: knuckles {s_Base[0]:F3} {s_Base[1]:F3} {s_Base[2]:F3}, thumb {s_Base[4]:F3}, palm {PalmHalf:F3}, fingers {s_Rad[0]:F4}..{s_Rad[3]:F4}");
            }
            finally { Object.Destroy(inst); }
        }

        /// <summary>The model's arm for one hand, under the arm's alien root (null: no model - the built mesh instead).</summary>
        static ArmRig BuildRig(Transform parent, HandColorHook hook, bool right)
        {
            var prefab = RigPrefab;
            if (prefab == null) return null;
            var inst = Object.Instantiate(prefab, parent, false);
            inst.name = "arm model";
            var root = inst.transform;
            // (the other arm's meshes - its skin and its claws - go)
            string otherSide = right ? "ArmL" : "ArmR";
            for (int i = root.childCount - 1; i >= 0; i--) if (root.GetChild(i).name.StartsWith(otherSide)) Object.DestroyImmediate(root.GetChild(i).gameObject);
            string s = right ? ".R" : ".L";
            var rig = new ArmRig
            {
                Root = root,
                Hand = FindDeep(root, "hand" + s),
                UpperArm = FindDeep(root, "upper_arm" + s),
                Forearm = FindDeep(root, "forearm" + s),
                ForearmEnd = FindDeep(root, "forearm" + s + "_end"),
            };
            if (rig.Hand == null || rig.UpperArm == null || rig.Forearm == null || rig.ForearmEnd == null) { Object.Destroy(inst); return null; }
            float sx = right ? 1f : -1f;
            var F = HandFrame(root, right, out _);
            var invF = Quaternion.Inverse(F);
            rig.HandMap = invF * rig.Hand.rotation;
            foreach (int d in s_Digits)
                for (int j = 0; j < 3; j++)
                {
                    var b = DigitBone(root, d, j, s);
                    var nx = DigitNext(root, d, j, s);
                    rig.F[d, j] = b;
                    if (b == null || nx == null) continue;
                    var dir = invF * (nx.position - b.position);
                    var up = d == 4 ? new Vector3(-0.6f * sx, 0.75f, 0f) : Vector3.up; // (the thumb's nail faces up and out)
                    var t0 = Quaternion.LookRotation(dir, up);
                    rig.Map[d, j] = Quaternion.Inverse(t0) * (invF * b.rotation);
                }
            rig.UpperRest = rig.UpperArm.localRotation;
            rig.ForeRest = rig.Forearm.localRotation;
            rig.ForeToHand = Quaternion.Inverse(rig.Hand.rotation) * rig.Forearm.rotation;
            rig.ForeAxis = rig.ForearmEnd.localPosition.normalized;
            var skin = FindDeep(root, right ? "ArmR" : "ArmL");
            if (skin != null && skin.TryGetComponent(out SkinnedMeshRenderer smr))
            {
                smr.updateWhenOffscreen = true; // (it swings all over the view: never culled on a stale box)
                smr.quality = SkinQuality.Bone4; // (every bone a vertex has: two only and the skin crumples at the joints)
                hook.Track(skin.gameObject, HandColorHook.AlienSkin);
            }
            // the dark claws on the fingertips (like the alien's)
            var claws = FindDeep(root, (right ? "ArmR" : "ArmL") + "_claws");
            if (claws != null && claws.TryGetComponent(out SkinnedMeshRenderer csmr))
            {
                csmr.updateWhenOffscreen = true;
                hook.Track(claws.gameObject, HandColorHook.AlienPad);
            }
            return rig;
        }

        /// <summary>Puts the model's arm onto the pose skeleton: the shoulders where they go in the view, the arm reaching
        /// the wrist (two-bone IK), the hand on the wrist, every finger and the thumb as their joints are.</summary>
        void ApplyRig(AlienArm a, ArmRig rig)
        {
            float sx = a.Right ? 1f : -1f;
            rig.Root.SetPositionAndRotation(m_Root.TransformPoint(k_RigOffset), m_Root.rotation);
            var target = a.Hand.position;

            // ---- the arm: from the shoulder to the wrist, the elbow down and out ----
            var up = rig.UpperArm; var fo = rig.Forearm; var fe = rig.ForearmEnd;
            up.localRotation = rig.UpperRest;
            fo.localRotation = rig.ForeRest;
            var S = up.position;
            float L1 = (fo.position - S).magnitude, L2 = (fe.position - fo.position).magnitude;
            var d = target - S;
            float dist = d.magnitude, reach = (L1 + L2) * 0.999f;
            if (dist > reach)
            {
                // out of reach: the shoulder comes forward (it's out of sight) rather than the hand coming off the arm
                var shift = d * ((dist - reach) / dist);
                rig.Root.position += shift;
                S += shift;
                d = target - S;
                dist = reach;
            }
            dist = Mathf.Max(dist, Mathf.Abs(L1 - L2) + 0.02f);
            var n = d.normalized;
            var pole = m_Root.TransformDirection(new Vector3(k_ElbowHint.x * sx, k_ElbowHint.y, k_ElbowHint.z));
            var po = pole - n * Vector3.Dot(pole, n);
            if (po.sqrMagnitude < 1e-6f) po = m_Root.TransformDirection(Vector3.down) - n * Vector3.Dot(m_Root.TransformDirection(Vector3.down), n);
            po.Normalize();
            float along = (L1 * L1 - L2 * L2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, L1 * L1 - along * along));
            var E = S + n * along + po * h;
            if (a.Right) DebugArm = $"shoulder {m_Root.InverseTransformPoint(S):F3} elbow {m_Root.InverseTransformPoint(E):F3} wrist {m_Root.InverseTransformPoint(target):F3}"; else DebugArmL = $"shoulder {m_Root.InverseTransformPoint(S):F3} elbow {m_Root.InverseTransformPoint(E):F3} wrist {m_Root.InverseTransformPoint(target):F3}";
            up.rotation = Quaternion.FromToRotation(fo.position - S, E - S) * up.rotation;
            fo.rotation = Quaternion.FromToRotation(fe.position - fo.position, target - fo.position) * fo.rotation;

            // ---- the hand and its digits ----
            var hr = a.Hand.rotation;
            rig.Hand.SetPositionAndRotation(target, hr * rig.HandMap);
            foreach (int dg in s_Digits)
            {
                var q = Quaternion.identity;
                for (int j = 0; j < 3; j++)
                {
                    q *= a.J[dg, j].localRotation;
                    var b = rig.F[dg, j];
                    if (b != null) b.rotation = hr * q * rig.Map[dg, j];
                }
            }

            // ---- the forearm takes half the hand's roll, so the wrist never twists like a wrung cloth ----
            var ideal = rig.Hand.rotation * rig.ForeToHand;
            var toWrist = target - fo.position;
            if (toWrist.sqrMagnitude > 1e-8f)
            {
                ideal = Quaternion.FromToRotation(ideal * rig.ForeAxis, toWrist) * ideal;
                fo.rotation = Quaternion.Slerp(fo.rotation, ideal, 0.5f);
            }
        }
    }
}
