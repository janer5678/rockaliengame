using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Something the lifting stick lifted (NetGame.Hoists): a building piece, a chest, a tree... floating up.</summary>
    public struct Hoist : INetworkSerializeByMemcpy, System.IEquatable<Hoist>
    {
        public ulong Id;
        /// <summary>Where it was (it drops back there), and when it was lifted (server time).</summary>
        public Vector3 Base;
        public Quaternion Rot;
        public double Start;
        public bool Equals(Hoist o) => Id == o.Id && Start == o.Start && Base == o.Base;
    }

    /// <summary>
    /// THE LIFTING STICK (it was the Stick of Truth): whatever it's shot at goes up.
    /// - a PLAYER is thrown sky high (they drop the ball, get off their horse);
    /// - a HORSE / car / anything that drives is thrown up the same way (its driver's machine moves it);
    /// - the BALL pops up into the air;
    /// - a BUILDING PIECE, a CHEST / box, a TREE, a bush... floats up Cfg.LiftHeight, hangs there wobbling for Cfg.LiftTime
    ///   (a hole in that base meanwhile) and then drops back into its place with a thud. Those don't sync where they are,
    ///   so every screen moves them itself from the list (Hoists) and puts them back when they come off it.
    /// And the SIGIL OF CHAOS's glow: every item lying about shows through walls while it's on (ChaosGlow).
    /// </summary>
    public partial class NetGame
    {
        public readonly NetworkList<Hoist> Hoists = new NetworkList<Hoist>();

        // =====================================================================
        // the lifting stick (server)
        // =====================================================================

        /// <summary>Server: lift whatever `target` is (or, with none, whatever's nearest where the shot landed). False if
        /// there was nothing there to lift.</summary>
        public bool ServerLift(Vector3 point, NetworkObject target, PlayerNet by)
        {
            if (target == null) target = LiftableNear(point, by);
            if (target == null) return false;
            if (target.TryGetComponent(out PlayerNet p))
            {
                if (p.Dead.Value || p.WackySafe) return false;
                if (p.Riding) p.ServerDismount();
                if (p.CarryingBall) Ball.Instance.ServerDrop(p.transform.position + Vector3.up * 1.5f, Vector3.up * 6f);
                p.KnockbackRpc(Vector3.up * Cfg.LiftLaunch);
                p.WackyNoteRpc(new FixedString128Bytes(p == by ? "You LIFTED yourself!" : "LIFTED!"));
                Fx.Server(FxKind.Lift, p.transform.position, Vector3.up);
                return true;
            }
            if (target.TryGetComponent(out Ball b))
            {
                if (b.IsCarried) return false;
                b.ServerDrop(b.transform.position + Vector3.up * 0.4f, Vector3.up * 22f);
                Fx.Server(FxKind.Lift, b.transform.position, Vector3.up);
                return true;
            }
            if (target.TryGetComponent(out Vehicle v))
            {
                v.LiftUpRpc(Cfg.LiftLaunch * 0.75f);
                Fx.Server(FxKind.Lift, v.transform.position, Vector3.up);
                return true;
            }
            if (target.GetComponent<Structure>() != null || target.GetComponent<Container>() != null || target.GetComponent<ResourceNode>() != null)
            {
                ServerHoist(target);
                return true;
            }
            return false;
        }

        /// <summary>The nearest liftable thing to where a shot landed (within a couple of metres): a player (not the one who
        /// shot), the ball, something that drives, a building piece, a chest, a tree or bush.</summary>
        static NetworkObject LiftableNear(Vector3 point, PlayerNet by)
        {
            NetworkObject best = null;
            float bd = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(point, 2.2f, ~0, QueryTriggerInteraction.Collide))
            {
                var no = c.GetComponentInParent<NetworkObject>();
                if (no == null || !no.IsSpawned) continue;
                var p = no.GetComponent<PlayerNet>();
                if (p != null && p == by) continue;
                if (p == null && no.GetComponent<Ball>() == null && no.GetComponent<Vehicle>() == null && no.GetComponent<Structure>() == null
                    && no.GetComponent<Container>() == null && no.GetComponent<ResourceNode>() == null) continue;
                float d = (c.ClosestPoint(point) - point).sqrMagnitude - (p != null ? 1f : 0f); // (a player there comes first)
                if (d < bd) { bd = d; best = no; }
            }
            return best;
        }

        /// <summary>Server: a building piece / chest / tree floats up (and later drops back down where it was).</summary>
        void ServerHoist(NetworkObject no)
        {
            foreach (var h in Hoists) if (h.Id == no.NetworkObjectId) return; // (already up there)
            Hoists.Add(new Hoist { Id = no.NetworkObjectId, Base = no.transform.position, Rot = no.transform.rotation, Start = NetworkManager.ServerTime.Time });
            Fx.Server(FxKind.Lift, no.transform.position + Vector3.up * 0.3f, Vector3.up);
        }

        const float LiftRise = 1.2f, LiftFall = 0.45f;

        /// <summary>How high a lifted thing is `t` seconds after it was lifted (done: it's back down).</summary>
        static float HoistHeight(float t, out bool done)
        {
            float hang = Mathf.Max(0.5f, Cfg.LiftTime), h = Cfg.LiftHeight;
            done = t >= LiftRise + hang + LiftFall;
            if (t <= 0f || done) return 0f;
            if (t < LiftRise) { float u = t / LiftRise; return h * (1f - (1f - u) * (1f - u) * (1f - u)); }
            if (t < LiftRise + hang) return h + Mathf.Sin((t - LiftRise) * 1.7f) * 0.3f;
            float f = (t - LiftRise - hang) / LiftFall;
            return h * (1f - f * f);
        }

        void ServerTickLifts(double now)
        {
            for (int i = Hoists.Count - 1; i >= 0; i--)
            {
                var h = Hoists[i];
                bool gone = !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(h.Id, out var no) || no == null;
                HoistHeight((float)(now - h.Start), out bool done);
                if (!done && !gone) continue;
                Hoists.RemoveAt(i);
                if (gone) continue;
                no.transform.SetPositionAndRotation(h.Base, h.Rot);
                s_HoistBase.Remove(h.Id);
                Fx.Server(FxKind.Smash, h.Base + Vector3.up * 0.2f, Vector3.up);
            }
        }

        // =====================================================================
        // every screen: lifted things where they are now
        // =====================================================================

        static readonly Dictionary<ulong, Hoist> s_HoistBase = new Dictionary<ulong, Hoist>();
        static readonly List<ulong> s_HoistDone = new List<ulong>();
        static readonly HashSet<ulong> s_HoistSeen = new HashSet<ulong>();

        /// <summary>For the tests: how high something is lifted on this screen right now (0 = not lifted).</summary>
        public static float LiftedBy(NetworkObject no) => no != null && s_HoistBase.TryGetValue(no.NetworkObjectId, out var h) ? no.transform.position.y - h.Base.y : 0f;

        void TickLiftsLocal()
        {
            if (!IsSpawned) return;
            double now = NetworkManager.ServerTime.Time;
            s_HoistSeen.Clear();
            var spawned = NetworkManager.SpawnManager.SpawnedObjects;
            foreach (var h in Hoists)
            {
                if (!spawned.TryGetValue(h.Id, out var no) || no == null) continue;
                s_HoistSeen.Add(h.Id);
                s_HoistBase[h.Id] = h;
                float t = (float)(now - h.Start);
                float y = HoistHeight(t, out _);
                float k = Cfg.LiftHeight > 0.01f ? Mathf.Clamp01(y / Cfg.LiftHeight) : 0f;
                var wobble = Quaternion.Euler(Mathf.Sin(t * 1.3f + h.Id) * 5f * k, 0f, Mathf.Cos(t * 1.1f + h.Id) * 5f * k);
                no.transform.SetPositionAndRotation(h.Base + Vector3.up * y, h.Rot * wobble);
            }
            if (s_HoistBase.Count == s_HoistSeen.Count) return;
            // off the list: back where they were
            s_HoistDone.Clear();
            foreach (var kv in s_HoistBase) if (!s_HoistSeen.Contains(kv.Key)) s_HoistDone.Add(kv.Key);
            foreach (var id in s_HoistDone)
            {
                var h = s_HoistBase[id];
                s_HoistBase.Remove(id);
                if (spawned.TryGetValue(id, out var no) && no != null) no.transform.SetPositionAndRotation(h.Base, h.Rot);
            }
        }

        // =====================================================================
        // the Sigil of CHAOS's glow (every screen)
        // =====================================================================

        static Material s_ChaosMat, s_ChaosCoreMat;

        static Material XrayMat(Color c)
        {
            var m = new Material(Shader.Find("Hidden/Internal-Colored"));
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.color = c;
            m.renderQueue = 4000;
            return m;
        }

        /// <summary>One world item, this frame: while the chaos glow's on, a glowing orb over it that shows through anything
        /// (the same size on screen near or far, so every one of them can be found), and the item itself glows too.</summary>
        void ChaosGlow(DroppedItem it, GameObject go, bool on)
        {
            var glow = go.transform.Find("chaos glow");
            if (!on)
            {
                if (glow != null)
                {
                    Destroy(glow.gameObject);
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = new List<Material>(r.sharedMaterials);
                        if (mats.RemoveAll(m => m == s_ChaosMat) > 0) r.sharedMaterials = mats.ToArray();
                    }
                }
                return;
            }
            if (glow == null)
            {
                if (s_ChaosMat == null) { s_ChaosMat = XrayMat(new Color(1f, 0.8f, 0.2f, 0.5f)); s_ChaosCoreMat = XrayMat(new Color(1f, 0.95f, 0.7f, 0.9f)); }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new List<Material>(r.sharedMaterials) { s_ChaosMat };
                    r.sharedMaterials = mats.ToArray();
                }
                glow = new GameObject("chaos glow").transform;
                glow.SetParent(go.transform, false);
                Art.Part(glow, Art.Sphere, Color.white, Vector3.zero, Vector3.one, default, false, s_ChaosMat);
                Art.Part(glow, Art.Sphere, Color.white, Vector3.zero, Vector3.one * 0.45f, default, false, s_ChaosCoreMat);
                foreach (var mr in glow.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, go.transform.position) : 10f;
            glow.position = go.transform.position + Vector3.up * (0.6f + Mathf.Min(d * 0.01f, 1.5f));
            glow.rotation = Quaternion.identity;
            glow.localScale = Vector3.one * Mathf.Clamp(d * 0.018f, 0.3f, 4f) * (1f + Mathf.Sin(Time.time * 4f + it.Id) * 0.12f);
        }
    }
}
