using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tutorial's training dummy (Vehicle.Dummy): a straw body on a post wearing an alien head - the players' own
    /// alien model's head, cut out of it, in the colour of the team the dummy stands for (the enemy of whoever it's
    /// put up for: OwnerTeam). It's hit like a player: anything above its neck (DummyNeck) is the head hitbox and does
    /// Cfg.HeadshotMul, the straw body below it the body hitbox (normal damage) - so the combat steps teach the
    /// difference. The guide hears which it was (Tutorial.OnEvent, arg bit 2).
    /// </summary>
    public partial class Vehicle
    {
        /// <summary>A hit on a training dummy above this height (from its feet) is a headshot - like a player's neck (PlayerNet.IsHeadshot).</summary>
        public const float DummyNeck = 1.5f;
        /// <summary>How tall the dummy's alien head is drawn.</summary>
        const float DummyHeadHeight = 0.52f;

        /// <summary>Server: the last HeadMul on this dummy was a headshot (read once by the ServerDamage that follows it).</summary>
        bool m_DummyHead;
        Transform m_DummyHeadT;

        /// <summary>Server: put up a training dummy for the tutorial; `team` is the team it stands for (its head's colour).</summary>
        public static Vehicle ServerSpawnDummy(Vector3 pos, float yaw, int team)
        {
            var go = Instantiate(Bootstrap.I.vehiclePrefab, pos, Quaternion.Euler(0, yaw, 0));
            var v = go.GetComponent<Vehicle>();
            v.Kind.Value = Dummy;
            v.OwnerTeam.Value = (byte)Mathf.Clamp(team, 0, 3);
            v.Hp.Value = v.MaxHp;
            go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            return v;
        }

        /// <summary>The simulated hitboxes: the head (above the neck) does double, like a player's; the body normal.</summary>
        float DummyHeadMul(Vector3 point)
        {
            m_DummyHead = point.y > transform.position.y + DummyNeck;
            return m_DummyHead ? Cfg.HeadshotMul : 1f;
        }

        /// <summary>Server: was the hit being dealt right now on the head? (then forgotten: an explosion's hit asks no HeadMul)</summary>
        bool TakeDummyHeadHit()
        {
            bool h = m_DummyHead;
            m_DummyHead = false;
            return h;
        }

        /// <summary>Every peer, once it's spawned: the alien head in its team's colour (and its hitbox), on the neck stub.</summary>
        void DressDummy()
        {
            if (m_Visual == null) return;
            if (m_DummyHeadT != null) Destroy(m_DummyHeadT.gameObject);
            int team = Mathf.Clamp(OwnerTeam.Value, 0, Cfg.TeamColor.Length - 1);
            var head = new GameObject("dummy head").transform;
            head.SetParent(m_Visual, false);
            head.localPosition = new Vector3(0, DummyNeck - 0.04f, 0);
            m_DummyHeadT = head;
            var mesh = AlienHeadMesh(out var matNames);
            if (mesh != null)
            {
                var go = new GameObject("alien head");
                go.transform.SetParent(head, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var mats = new Material[matNames.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = new Material(Art.Mat(Color.white)) { name = matNames[i] };
                r.sharedMaterials = mats;
                // the same textured materials and team tint as a player's alien (PlayerNet.SkinAlien / TeamTint)
                var tinted = new List<Material>();
                PlayerNet.SkinAlien(go, tinted);
                foreach (var m in r.sharedMaterials)
                {
                    var c = PlayerNet.TeamTint(m, team);
                    m.SetColor("_BaseColor", c);
                    m.color = c;
                }
            }
            else
            {
                // (no alien model: a plain round alien head in the team colour with big black eyes)
                var c = Cfg.TeamColor[team];
                Art.Part(head, Art.Sphere, Color.Lerp(Color.white, c, 0.6f), new Vector3(0, 0.26f, 0), new Vector3(0.42f, 0.5f, 0.44f));
                for (int k = -1; k <= 1; k += 2)
                    Art.Part(head, Art.Sphere, Color.black, new Vector3(k * 0.1f, 0.3f, 0.19f), new Vector3(0.12f, 0.16f, 0.06f), new Vector3(0, 0, k * -20f));
            }
            // the head hitbox: on the players' hitbox layer (shots find it, nothing bumps into it)
            var box = new GameObject("dummy head hitbox");
            box.transform.SetParent(head, false);
            box.layer = PlayerNet.HitboxLayer;
            var bc = box.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, DummyHeadHeight * 0.5f, 0);
            bc.size = new Vector3(0.5f, DummyHeadHeight, 0.5f);
        }

        // ---- the alien head, cut out of the players' rigged alien ----

        static Mesh s_AlienHead;
        static string[] s_AlienHeadMats;
        static bool s_AlienHeadTried;

        /// <summary>
        /// The head of the players' rigged alien (Resources/Alien/AlienRigged) as a mesh of its own: every triangle skinned
        /// mostly to the Head bone (or a bone under it), posed as the model stands, scaled to DummyHeadHeight (and the
        /// players' ModelWidth) with its bottom middle at the origin, facing +z. One submesh per material it uses (their
        /// names, for SkinAlien, in `mats`). Null if the model isn't there or can't be read.
        /// </summary>
        static Mesh AlienHeadMesh(out string[] mats)
        {
            mats = s_AlienHeadMats;
            if (s_AlienHeadTried) return s_AlienHead;
            s_AlienHeadTried = true;
            var prefab = Resources.Load<GameObject>("Alien/AlienRigged");
            if (prefab == null) return null;
            var model = Instantiate(prefab);
            model.SetActive(false); // (only its transforms and mesh are wanted)
            try
            {
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                var pos = new List<Vector3>();
                var nrm = new List<Vector3>();
                var uv = new List<Vector2>();
                var subs = new List<List<int>>();
                var names = new List<string>();
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.sharedMesh;
                    var bones = smr.bones;
                    if (src == null || !src.isReadable || bones == null || bones.Length == 0) continue;
                    Transform headBone = null;
                    foreach (var b in bones) if (b != null && b.name == "Head") headBone = b;
                    if (headBone == null) continue;
                    var isHead = new bool[bones.Length];
                    for (int i = 0; i < bones.Length; i++) isHead[i] = bones[i] != null && (bones[i] == headBone || bones[i].IsChildOf(headBone));
                    var bw = src.boneWeights;
                    var bind = src.bindposes;
                    var v = src.vertices;
                    var n = src.normals;
                    var t = src.uv;
                    if (bw.Length != v.Length) continue;
                    var skin = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++) skin[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
                    float HeadW(BoneWeight w) => (isHead[w.boneIndex0] ? w.weight0 : 0f) + (isHead[w.boneIndex1] ? w.weight1 : 0f) + (isHead[w.boneIndex2] ? w.weight2 : 0f) + (isHead[w.boneIndex3] ? w.weight3 : 0f);
                    var map = new Dictionary<int, int>();
                    int Vert(int i)
                    {
                        if (map.TryGetValue(i, out int j)) return j;
                        // posed as the model stands: the usual four-bone blend
                        var w = bw[i];
                        Vector3 p = Vector3.zero, q = Vector3.zero;
                        void Add(int b, float k) { if (k <= 0f) return; p += skin[b].MultiplyPoint3x4(v[i]) * k; if (n.Length == v.Length) q += skin[b].MultiplyVector(n[i]) * k; }
                        Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                        j = pos.Count;
                        pos.Add(p);
                        nrm.Add(q.sqrMagnitude > 1e-8f ? q.normalized : Vector3.up);
                        uv.Add(t.Length == v.Length ? t[i] : Vector2.zero);
                        map[i] = j;
                        return j;
                    }
                    var shared = smr.sharedMaterials;
                    for (int s = 0; s < src.subMeshCount; s++)
                    {
                        var tris = src.GetTriangles(s);
                        var keep = new List<int>();
                        for (int k = 0; k + 2 < tris.Length; k += 3)
                        {
                            if (HeadW(bw[tris[k]]) < 0.5f || HeadW(bw[tris[k + 1]]) < 0.5f || HeadW(bw[tris[k + 2]]) < 0.5f) continue;
                            keep.Add(Vert(tris[k])); keep.Add(Vert(tris[k + 1])); keep.Add(Vert(tris[k + 2]));
                        }
                        if (keep.Count == 0) continue;
                        subs.Add(keep);
                        names.Add(s < shared.Length && shared[s] != null ? shared[s].name : "Alien2_Head");
                    }
                }
                if (pos.Count == 0) return null;
                // its bottom middle to the origin, scaled to the dummy's head size (as wide as a player's: ModelWidth)
                var bounds = new Bounds(pos[0], Vector3.zero);
                foreach (var p in pos) bounds.Encapsulate(p);
                float k2 = bounds.size.y > 1e-4f ? DummyHeadHeight / bounds.size.y : 1f;
                var origin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var scale = new Vector3(k2 * Cfg.ModelWidth, k2, k2 * Cfg.ModelWidth);
                for (int i = 0; i < pos.Count; i++) pos[i] = Vector3.Scale(pos[i] - origin, scale);
                var mesh = new Mesh { name = "dummy alien head" };
                mesh.SetVertices(pos);
                mesh.SetNormals(nrm);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateBounds();
                s_AlienHead = mesh;
                s_AlienHeadMats = mats = names.ToArray();
                return mesh;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RockGame] couldn't cut the alien's head out for the training dummy: " + e.Message);
                return null;
            }
            finally
            {
                Destroy(model);
            }
        }
    }
}
