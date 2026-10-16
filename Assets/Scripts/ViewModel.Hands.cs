using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person alien hands (Settings > Display > Alien hands; off brings back the old square ones): a free,
    /// professionally made low-poly arms model (the CC0 "WRAD ARMS" by wriks - Tools/Hands; Tools/alien_hands.py makes
    /// Resources/Hands/AlienHands from it) in your alien's colour. Kept simple: each hand has two shapes baked in - a FIST
    /// (round any handle) and a CUPPED hand (carrying the rock, the ball, berries, C4...) - and nothing is posed finger by
    /// finger. Each frame the hand goes where the item's pose put the arm (m_R / m_L, as the block hands did): the fist's
    /// hole on the handle, lined up along it, or the cupped palm towards what it carries; and the arm reaches it from its
    /// own shoulder (two-bone IK), the elbow down and out, so the arms come in from the bottom corners of the screen.
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>Which way each elbow points (the right arm's, in the view; the left's is its mirror): down and out.</summary>
        static readonly Vector3 k_ElbowHint = new Vector3(0.75f, -1f, -0.2f);

        sealed class HandRig
        {
            public bool Right;
            public Transform Root, Upper, Fore, Wrist, Grip, GripA, Knuck, Palm, PalmN;
            public GameObject Fist, Cup;
            public Quaternion UpperRest, ForeRest;
        }

        HandRig m_HR, m_HL;
        static GameObject s_HandsPrefab;
        static bool s_HandsTried;

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        /// <summary>Builds the hand under `arm` ("alien": hidden while the square hands are picked); null if the model's missing.</summary>
        static HandRig BuildHand(Transform arm, HandColorHook hook, bool right)
        {
            if (!s_HandsTried) { s_HandsTried = true; s_HandsPrefab = Resources.Load<GameObject>("Hands/AlienHands"); }
            var root = new GameObject("alien").transform;
            root.SetParent(arm, false);
            if (s_HandsPrefab == null) return null;
            var inst = Object.Instantiate(s_HandsPrefab, root, false);
            inst.name = "hands model";
            var t = inst.transform;
            // (importing the model mirrors it left to right: its ".l" arm - ArmL_ - is the game's right one)
            string s = right ? ".l" : ".r", mine = right ? "ArmL_" : "ArmR_", other = right ? "ArmR_" : "ArmL_";
            for (int i = t.childCount - 1; i >= 0; i--) if (t.GetChild(i).name.StartsWith(other)) Object.DestroyImmediate(t.GetChild(i).gameObject);
            var h = new HandRig
            {
                Right = right, Root = t,
                Upper = FindDeep(t, "bicep" + s), Fore = FindDeep(t, "forearm" + s), Wrist = FindDeep(t, "wrist" + s),
                Grip = FindDeep(t, "grip" + s), GripA = FindDeep(t, "gripA" + s), Knuck = FindDeep(t, "knuck" + s),
                Palm = FindDeep(t, "palm" + s), PalmN = FindDeep(t, "palmN" + s),
                Fist = FindDeep(t, mine + "Fist")?.gameObject,
                Cup = FindDeep(t, mine + "Cup")?.gameObject,
            };
            if (h.Upper == null || h.Fore == null || h.Wrist == null || h.Grip == null || h.Fist == null || h.Cup == null) { Object.Destroy(inst); return null; }
            h.UpperRest = h.Upper.localRotation;
            h.ForeRest = h.Fore.localRotation;
            foreach (var go in new[] { h.Fist, h.Cup })
                if (go.TryGetComponent(out SkinnedMeshRenderer smr))
                {
                    smr.updateWhenOffscreen = true; // (it swings all over the view: never culled on a stale box)
                    hook.Track(go, HandColorHook.AlienSkin);
                }
            return h;
        }

        /// <summary>What a hand does with the item: wrap a fist round a handle running along `axis` (world), or carry it in
        /// a cupped palm facing `axis`.</summary>
        enum Hold { Fist, Cup }

        /// <summary>Each gun's grip, in the gun's own space: its middle, and how far it leans back (degrees).</summary>
        static bool GunGrip(Item it, out Vector3 at, out float tilt)
        {
            switch (it)
            {
                case Item.Pistol: at = new Vector3(0f, -0.05f, -0.012f); tilt = -12f; return true;
                case Item.Revolver: at = new Vector3(0f, -0.045f, -0.072f); tilt = -18f; return true;
                case Item.AlienGun: at = new Vector3(0f, -0.045f, -0.062f); tilt = -16f; return true;
                case Item.Shotgun: at = new Vector3(0f, -0.07f, -0.022f); tilt = -15f; return true;
                case Item.Sniper: at = new Vector3(0f, -0.065f, 0.02f); tilt = -15f; return true;
                case Item.PortalGun: at = new Vector3(0f, -0.07f, -0.05f); tilt = 0f; return true;
                case Item.RocketLauncher: at = new Vector3(0f, -0.09f, 0.05f); tilt = -10f; return true;
                case Item.Crossbow: at = new Vector3(0f, -0.075f, -0.02f); tilt = -15f; return true;
                case Item.AntGun: case Item.SlowGun: at = new Vector3(0f, -0.02f, -0.002f); tilt = -15f; return true;
                default: at = Vector3.zero; tilt = 0f; return false;
            }
        }

        /// <summary>This frame's hold for each hand: what the hand does, along / towards what, where (world), and which
        /// way the knuckles face.</summary>
        void HandHold(bool right, Item item, bool ball, out Hold hold, out Vector3 axis, out Vector3 point, out Vector3 facing)
        {
            var H = m_ItemHolder;
            var arm = right ? m_R : m_L;
            Vector3 toItem = (ball && m_Ball ? m_Ball.transform.position : H.position) - arm.position;
            hold = Hold.Fist;
            axis = H.up;
            point = arm.position;
            facing = arm.forward;
            // a gun in the right hand: the fist right round its grip, the knuckles towards the muzzle
            if (right && !ball && GunGrip(item, out var g, out float tilt))
            {
                point = H.TransformPoint(g);
                axis = H.rotation * (Quaternion.Euler(tilt, 0f, 0f) * Vector3.up);
                facing = H.forward;
                return;
            }
            if (ball) { hold = Hold.Cup; axis = toItem; return; }
            switch (item)
            {
                case Item.Rock: case Item.GoldenRock: case Item.Ram: case Item.HeavyRam:
                    hold = Hold.Cup; axis = toItem; return;
                case Item.Chainsaw:
                    axis = right ? H.up : H.forward; return; // (the rear handle stands up; the top bar runs along it)
                case Item.Crossbow: case Item.Sniper: case Item.Shotgun: case Item.PortalGun: case Item.RocketLauncher:
                case Item.Pistol: case Item.Revolver: case Item.AlienGun:
                    if (!right) { hold = Hold.Cup; axis = toItem; } // (the other hand under the fore-end, or cupping the grip)
                    return;
                case Item.Bow: case Item.Spear: case Item.None:
                    return;
            }
            // everything else in the right hand: tools, weapons, sticks and wands in a fist; small things and boxes carried
            if (Cfg.IsMelee(item) || IsStick(item)) return;
            hold = Hold.Cup;
            axis = toItem.sqrMagnitude > 1e-6f ? toItem : arm.up;
        }

        static bool IsStick(Item i) => i == Item.DeathWand || i == Item.GiantStaff || i == Item.Connect4Wand || i == Item.GameshowWand || i == Item.StickOfTruth
            || i == Item.GravityStaff || i == Item.ChaosSigil || i == Item.BallGrabber || i == Item.Decoy || i == Item.Arrow || i == Item.FakeFlare || i == Item.OmenMap
            || i == Item.AntGun || i == Item.SlowGun || i == Item.InvisPotion || i == Item.SpeedJuice || i == Item.ShieldPotion || i == Item.BearJuice;

        /// <summary>Puts both hands where this frame's poses put the arms.</summary>
        void PoseHands(State s)
        {
            PoseHand(m_HR, m_R, s);
            PoseHand(m_HL, m_L, s);
        }

        void PoseHand(HandRig h, Transform arm, State s)
        {
            if (h == null || !h.Root.gameObject.activeInHierarchy) return;
            // a hand put away out of sight (the free hand): the whole arm goes
            bool away = m_Root.InverseTransformPoint(arm.position).y < -0.7f;
            HandHold(h.Right, s.Item, s.Ball, out var hold, out var axis, out var point, out var facing);
            SetActive(h.Fist, !away && hold == Hold.Fist);
            SetActive(h.Cup, !away && hold == Hold.Cup);
            if (away) return;
            float sx = h.Right ? 1f : -1f;
            // the model's rest pose: put it back, the shoulders where they go
            h.Root.SetPositionAndRotation(m_Root.position, m_Root.rotation);
            h.Upper.localRotation = h.UpperRest;
            h.Fore.localRotation = h.ForeRest;

            // where the wrist goes: the fist's hole on the handle (its line along the handle, the knuckles forward), or the
            // cupped palm on what it carries, facing it
            var fwd = facing;
            Quaternion from, to;
            Vector3 at, marker;
            if (hold == Hold.Fist)
            {
                var across = h.GripA.position - h.Grip.position;           // (the handle's line through the fist: towards the little finger)
                var knuck = h.Knuck.position - h.Wrist.position;
                from = Quaternion.LookRotation(Perp(knuck, across), -across);
                var a = axis.sqrMagnitude > 1e-6f ? axis.normalized : arm.up;
                to = Quaternion.LookRotation(Perp(fwd, a), a);
                marker = h.Grip.position;
                at = point;
            }
            else
            {
                var n = h.PalmN.position - h.Palm.position;                // (out of the palm)
                var knuck = h.Knuck.position - h.Wrist.position;
                from = Quaternion.LookRotation(Perp(knuck, n), -n);
                var want = axis.sqrMagnitude > 1e-6f ? axis.normalized : arm.up;
                to = Quaternion.LookRotation(Perp(fwd, want), -want);
                marker = h.Palm.position;
                at = arm.position;
            }
            var turn = to * Quaternion.Inverse(from);
            var wristRot = turn * h.Wrist.rotation;
            var wristPos = at - turn * (marker - h.Wrist.position);

            // ---- the arm reaches the wrist from the shoulder, the elbow down and out ----
            var S = h.Upper.position;
            float L1 = (h.Fore.position - S).magnitude, L2 = (h.Wrist.position - h.Fore.position).magnitude;
            var d = wristPos - S;
            float dist = d.magnitude, reach = (L1 + L2) * 0.999f;
            if (dist > reach)
            {
                // out of reach: the shoulder comes along (it's out of sight) rather than the hand coming off the arm
                var shift = d * ((dist - reach) / dist);
                h.Root.position += shift;
                S += shift; d = wristPos - S; dist = reach;
            }
            dist = Mathf.Max(dist, Mathf.Abs(L1 - L2) + 0.02f);
            var dn = d.normalized;
            var pole = m_Root.TransformDirection(new Vector3(k_ElbowHint.x * sx, k_ElbowHint.y, k_ElbowHint.z));
            var po = pole - dn * Vector3.Dot(pole, dn);
            if (po.sqrMagnitude < 1e-6f) po = m_Root.TransformDirection(Vector3.down);
            po.Normalize();
            float along = (L1 * L1 - L2 * L2 + dist * dist) / (2f * dist);
            var E = S + dn * along + po * Mathf.Sqrt(Mathf.Max(0f, L1 * L1 - along * along));
            h.Upper.rotation = Quaternion.FromToRotation(h.Fore.position - S, E - S) * h.Upper.rotation;
            h.Fore.rotation = Quaternion.FromToRotation(h.Wrist.position - h.Fore.position, wristPos - h.Fore.position) * h.Fore.rotation;
            h.Wrist.SetPositionAndRotation(wristPos, wristRot);
        }

        static Vector3 Perp(Vector3 v, Vector3 n)
        {
            n.Normalize();
            var p = v - n * Vector3.Dot(v, n);
            return p.sqrMagnitude > 1e-8f ? p.normalized : Vector3.Cross(n, Vector3.right).normalized;
        }

        static void SetActive(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
    }
}
