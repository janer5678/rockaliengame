using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The SMOOTH hands on the first-person arms (Settings > Display > Hands: Smooth; SmoothHand.cs sculpts the arm):
    /// each arm gets a second pose skeleton - the same shape as the Alien arms' (the hand, three joints for each of the
    /// three fingers and the thumb, the forearm), in the smooth hand's own measurements - and one skinned mesh on it.
    /// ViewModel.Fingers.cs poses it exactly as it does the Alien arms (every item's grip, the finger solver, the
    /// actions); on top of the skeleton's own bones, each joint has three in-between bones that take a quarter, a half
    /// and three quarters of the joint's turn (ApplySmooth), which the skin is weighted to across the joint.
    ///
    /// The skeleton's measurements (s_Base, s_Len, s_Rad, the palm, the grip) are shared statics: they're the arms
    /// model's while the Alien arms show and the smooth hand's while it does (UseProfile).
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>The smooth arms (m_AR / m_AL are the Alien ones).</summary>
        AlienArm m_SR, m_SL;

        /// <summary>The smooth arm's extra bones: the in-between ones across the wrist and at every joint.</summary>
        sealed class SmoothRig
        {
            public readonly Transform[] Wrist = new Transform[3];
            /// <summary>[digit slot, joint, step]</summary>
            public readonly Transform[,,] H = new Transform[4, 3, 3];
            /// <summary>Each digit's knuckle as the mesh was made (fanned out; the thumb down and out).</summary>
            public readonly Quaternion[] Rest0 = new Quaternion[4];
        }

        // ======== the skeleton's measurements: the arms model's or the smooth hand's ========

        sealed class HandProfile
        {
            public Vector3[] Base;
            public float[] Len, Rad;
            public float PalmHalf, GripZ, KnuckleZ;
        }

        static HandProfile s_RigProfile, s_SmoothProfile;
        static bool s_SmoothNow;
        /// <summary>Goes up whenever the measurements change (solved grips are thrown away).</summary>
        static int s_ProfileVersion;
        int m_ProfileSeen = -1;

        static void EnsureProfiles()
        {
            if (s_RigProfile != null) return;
            MeasureRig(); // (the statics start as the arms model's)
            s_RigProfile = new HandProfile
            {
                Base = (Vector3[])s_Base.Clone(), Len = (float[])s_Len.Clone(), Rad = (float[])s_Rad.Clone(),
                PalmHalf = PalmHalf, GripZ = GripZ, KnuckleZ = KnuckleZ,
            };
            s_SmoothProfile = new HandProfile
            {
                Base = SmoothHand.Base, Len = SmoothHand.Len, Rad = SmoothHand.Rad,
                PalmHalf = SmoothHand.PalmHalf, GripZ = SmoothHand.GripZ, KnuckleZ = SmoothHand.KnuckleZ,
            };
            s_SmoothNow = false;
        }

        /// <summary>The pose skeleton's measurements become the smooth hand's (or the arms model's again).</summary>
        static void UseProfile(bool smooth)
        {
            EnsureProfiles();
            if (s_SmoothNow == smooth) return;
            s_SmoothNow = smooth;
            var p = smooth ? s_SmoothProfile : s_RigProfile;
            System.Array.Copy(p.Base, s_Base, s_Base.Length);
            System.Array.Copy(p.Len, s_Len, s_Len.Length);
            System.Array.Copy(p.Rad, s_Rad, s_Rad.Length);
            PalmHalf = p.PalmHalf; GripZ = p.GripZ; KnuckleZ = p.KnuckleZ;
            s_ProfileVersion++;
        }

        // ======== the arm ========

        /// <summary>Builds the smooth arm under `arm` ("smooth": hidden unless that style's picked): its skeleton, the
        /// in-between bones, and the skin on them.</summary>
        static AlienArm BuildSmoothArm(Transform arm, HandColorHook hook, bool right)
        {
            bool was = s_SmoothNow;
            UseProfile(true);
            float sx = right ? 1f : -1f;
            var a = new AlienArm { Right = right, Smooth = new SmoothRig() };
            var sm = a.Smooth;
            a.Root = Node(arm, "smooth", Vector3.zero);
            a.Hand = Node(a.Root, "hand", Vector3.zero);
            a.Hand.localScale = Vector3.one * HandScale;
            a.Forearm = Node(a.Root, "forearm", Vector3.zero);
            var bones = new Transform[SmoothHand.BoneCount];
            bones[SmoothHand.BoneHand] = a.Hand;
            bones[SmoothHand.BoneArm] = a.Forearm;
            for (int h = 0; h < 3; h++) bones[SmoothHand.BoneWrist + h] = sm.Wrist[h] = Node(a.Root, "wrist" + h, Vector3.zero);
            foreach (int d in s_Digits)
            {
                int q = SmoothHand.Slot(d);
                // (in the pose the mesh was made in: bound like this)
                var rest = d < 4 ? Quaternion.Euler(0f, SmoothHand.RestSpread[d] * sx, 0f) : right ? SmoothHand.RestThumb : Mirror(SmoothHand.RestThumb);
                sm.Rest0[q] = rest;
                var at = new Vector3(s_Base[d].x * sx, s_Base[d].y, s_Base[d].z);
                Transform parent = a.Hand;
                for (int j = 0; j < 3; j++)
                {
                    var pos = j == 0 ? at : new Vector3(0f, 0f, Len(d, j - 1));
                    int b = SmoothHand.JointBase(q, j);
                    for (int h = 0; h < 3; h++)
                    {
                        var t = Node(parent, "b" + j + h, pos);
                        if (j == 0) t.localRotation = rest;
                        sm.H[q, j, h] = bones[b + h] = t;
                    }
                    var joint = Node(parent, j == 0 ? (d < 4 ? "finger" + d : "thumb") : "j" + j, pos);
                    if (j == 0) joint.localRotation = rest;
                    a.J[d, j] = bones[b + 3] = joint;
                    parent = joint;
                }
            }
            var skin = new GameObject("skin");
            skin.transform.SetParent(a.Root, false);
            var mesh = SmoothHand.Skin(right);
            if (mesh != null)
            {
                var smr = skin.AddComponent<SkinnedMeshRenderer>();
                if (mesh.bindposes == null || mesh.bindposes.Length != bones.Length)
                {
                    var bind = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
                    mesh.bindposes = bind;
                }
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = a.Hand;
                smr.quality = SkinQuality.Bone4; // (a point between two fingers follows both, and the hand)
                smr.updateWhenOffscreen = true;  // (it swings all over the view: never culled on a stale box)
                smr.receiveShadows = false;      // (the shader reads the sun's shadow by the camera: no acne on the skin)
                hook.Track(skin, HandColorHook.SmoothSkin);
            }
            UseProfile(was);
            return a;
        }

        /// <summary>The in-between bones follow the pose: each takes its share of the turn of the joint it sits on.</summary>
        static void ApplySmooth(AlienArm a)
        {
            var sm = a.Smooth;
            var hp = a.Hand.localPosition;
            Quaternion hr = a.Hand.localRotation, fr = a.Forearm.localRotation;
            for (int h = 0; h < 3; h++)
            {
                sm.Wrist[h].localPosition = hp;
                sm.Wrist[h].localRotation = Quaternion.Slerp(hr, fr, (h + 1) * 0.25f);
            }
            foreach (int d in s_Digits)
            {
                int q = SmoothHand.Slot(d);
                for (int j = 0; j < 3; j++)
                {
                    var from = j == 0 ? sm.Rest0[q] : Quaternion.identity;
                    var to = a.J[d, j].localRotation;
                    for (int h = 0; h < 3; h++) sm.H[q, j, h].localRotation = Quaternion.Slerp(from, to, (h + 1) * 0.25f);
                }
            }
        }

        // ======== (tests) the hands held up to be looked at ========

        /// <summary>When set (>= 0), the arms hold nothing and show themselves in pose number DebugShow (AutoTest.Hands:
        /// the backs and the palms open, relaxed, fists, cupped, from the side).</summary>
        public static int DebugShow = -1;
        public const int ShowCount = 6;
        public static readonly string[] ShowNames = { "open", "relaxed", "fist", "fistside", "cup", "ahead" };

        /// <summary>Puts the arms in show pose DebugShow (the item put away).</summary>
        void PoseShow()
        {
            if (m_Item) m_Item.SetActive(false);
            AttachItemToRoot(new Vector3(0f, -2f, 0f), Quaternion.identity);
            Vector3 up = Vector3.up, toYou = Vector3.back;
            switch (DebugShow)
            {
                case 0: // fingers up: the right hand's back to you, the left's palm
                case 2:
                    Set(m_R, new Vector3(0.13f, -0.15f, 0.44f), Quaternion.LookRotation(up, toYou));
                    Set(m_L, new Vector3(-0.13f, -0.15f, 0.44f), Quaternion.LookRotation(up, -toYou));
                    break;
                case 1: // turned three quarters
                    Set(m_R, new Vector3(0.13f, -0.14f, 0.44f), Quaternion.LookRotation(new Vector3(-0.25f, 0.9f, 0.3f), new Vector3(0.55f, 0.1f, -0.8f)));
                    Set(m_L, new Vector3(-0.13f, -0.14f, 0.44f), Quaternion.LookRotation(new Vector3(0.25f, 0.9f, 0.3f), new Vector3(0.55f, -0.1f, 0.8f)));
                    break;
                case 3: // fists from the thumb side and from the outer side
                    Set(m_R, new Vector3(0.13f, -0.1f, 0.44f), Quaternion.LookRotation(new Vector3(-0.2f, 0.1f, 1f), new Vector3(1f, 0.25f, 0.1f)));
                    Set(m_L, new Vector3(-0.13f, -0.1f, 0.44f), Quaternion.LookRotation(new Vector3(-0.9f, 0.2f, 0.4f), new Vector3(0.1f, 1f, 0f)));
                    break;
                case 4: // cupped, palms up
                    Set(m_R, new Vector3(0.12f, -0.2f, 0.42f), Quaternion.LookRotation(new Vector3(-0.2f, 0.25f, 1f), new Vector3(0.15f, -1f, 0.2f)));
                    Set(m_L, new Vector3(-0.12f, -0.2f, 0.42f), Quaternion.LookRotation(new Vector3(0.2f, 0.25f, 1f), new Vector3(-0.15f, -1f, 0.2f)));
                    break;
                default: // held out ahead, as you'd see them running
                    Set(m_R, new Vector3(0.2f, -0.2f, 0.46f), Quaternion.LookRotation(new Vector3(-0.15f, 0.25f, 1f), new Vector3(0.5f, 1f, 0f)));
                    Set(m_L, new Vector3(-0.2f, -0.2f, 0.46f), Quaternion.LookRotation(new Vector3(0.15f, 0.25f, 1f), new Vector3(-0.5f, 1f, 0f)));
                    break;
            }
        }

        /// <summary>The fingers of show pose DebugShow (into m_TR / m_TL).</summary>
        void ShowGoals()
        {
            Fp f; Tp t;
            switch (DebugShow)
            {
                case 0: f = Fp.Open; t = Tp.Open; break;
                case 1: f = Fp.Relaxed; t = Tp.Relaxed; break;
                case 2: case 3: f = Fp.LooseFist; t = Tp.Tuck; break;
                case 4: f = Fp.Pinch; t = Tp.Support; break;
                default: f = Fp.Relaxed; t = Tp.Relaxed; break;
            }
            One(m_TR, Free(Quaternion.identity, f, t), true, 0);
            One(m_TL, Free(Quaternion.identity, f, t), false, 0);
            if (DebugShow == 2 || DebugShow == 3) { Squeeze(m_TR, 22f); Squeeze(m_TL, 22f); }
        }
    }
}
