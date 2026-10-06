using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>Your teammates show through walls as a silhouette in your team's colour (never enemies).</summary>
        public static bool TeammateSilhouettes = true;
        /// <summary>How solid that silhouette is (0..1).</summary>
        public const float TeammateSilhouetteAlpha = 0.5f;
    }

    /// <summary>
    /// Teammates through walls: every teammate's body gets one extra pass in the team's colour that is only drawn where
    /// something is in front of it (ZTest Greater) - so out in the open they look as they always do, and behind a wall,
    /// a hill or a tree you see their silhouette. Only ever put on players of the local player's own team (enemies get
    /// nothing; the wallhack glasses are SetEsp). Cheap: one shared unlit material per team, no textures, no depth
    /// writes; meshes made of several parts (the rigged alien) get one copy drawn with it, like the alien outlines.
    /// </summary>
    public partial class PlayerNet
    {
        const string k_TeamSeeName = "team see", k_TeamSeeCopy = "team see copy";
        static readonly Material[] s_TeamSeeMats = new Material[4];
        readonly List<GameObject> m_TeamSeeCopies = new List<GameObject>();
        bool m_TeamSee;
        float m_NextTeamSeeRefresh;
        /// <summary>This teammate is drawn through walls on this machine (tests).</summary>
        public bool SeenThroughWalls => m_TeamSee;

        static Material TeamSeeMat(int team)
        {
            team = Mathf.Clamp(team, 0, 3);
            if (s_TeamSeeMats[team] == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return null;
                var m = new Material(sh) { name = k_TeamSeeName };
                m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Greater); // only where it's hidden behind something
                m.SetInt("_ZWrite", 0);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                m.renderQueue = 4000;
                s_TeamSeeMats[team] = m;
            }
            var c = Color.Lerp(Cfg.TeamColor[Mathf.Clamp(team, 0, Cfg.TeamColor.Length - 1)], Color.white, 0.15f);
            c.a = Cfg.TeammateSilhouetteAlpha;
            s_TeamSeeMats[team].color = c;
            return s_TeamSeeMats[team];
        }

        /// <summary>Every frame (PlayerNet.Update): teammates of the local player are drawn through walls; nobody else.</summary>
        void TickTeamSee(PlayerNet local, bool dead)
        {
            bool on = Cfg.TeammateSilhouettes && !Mine && local != null && local != this && local.Team.Value == Team.Value && !dead
                && m_VisualRoot != null && m_VisualRoot.gameObject.activeSelf;
            if (on != m_TeamSee) SetTeamSee(on);
            // (held items and armour get swapped: give the new ones the pass too)
            else if (on && Time.time >= m_NextTeamSeeRefresh) SetTeamSee(true);
        }

        void SetTeamSee(bool on)
        {
            m_TeamSee = on;
            m_NextTeamSeeRefresh = Time.time + 1f;
            foreach (var c in m_TeamSeeCopies) if (c != null) Destroy(c);
            m_TeamSeeCopies.Clear();
            var mat = on ? TeamSeeMat(Team.Value) : null;
            foreach (var r in m_VisualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r.name == k_TeamSeeCopy || r.name == "outline copy") continue;
                var shared = r.sharedMaterials;
                bool has = false;
                foreach (var m in shared) if (m != null && m.name == k_TeamSeeName) has = true;
                // a mesh made of several parts: an extra material would only cover the last part, so a copy is drawn with it on every part
                if (mat != null && r is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.subMeshCount > 1)
                {
                    var go = new GameObject(k_TeamSeeCopy);
                    go.transform.SetParent(smr.transform, false);
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = smr.sharedMesh;
                    copy.bones = smr.bones;
                    copy.rootBone = smr.rootBone;
                    copy.localBounds = smr.localBounds;
                    copy.updateWhenOffscreen = smr.updateWhenOffscreen;
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    var all = new Material[smr.sharedMesh.subMeshCount];
                    for (int i = 0; i < all.Length; i++) all[i] = mat;
                    copy.sharedMaterials = all;
                    m_TeamSeeCopies.Add(go);
                    if (!has) continue;
                }
                else if (mat != null && has) continue; // already on
                else if (mat == null && !has) continue;
                var mats = new List<Material>(shared);
                mats.RemoveAll(m => m != null && m.name == k_TeamSeeName);
                if (mat != null && !(r is SkinnedMeshRenderer s2 && s2.sharedMesh != null && s2.sharedMesh.subMeshCount > 1)) mats.Add(mat);
                r.sharedMaterials = mats.ToArray();
            }
        }
    }
}
