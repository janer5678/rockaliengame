using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The kill cam's replay: this PC keeps the last few seconds of everyone - where each player stood and looked, and
    /// how your own alien was posed, bone by bone. When you're killed (PlayerController.KillCam.cs), after the glide to
    /// your killer's face, it plays those seconds back from the killer's own eyes - a locked spectator view - with a copy
    /// of your alien going through what you did, so you see exactly how you died (the last moment in slow motion).
    /// Only the look: nothing is sent anywhere, and the copy and the camera are gone the moment the kill cam ends.
    /// </summary>
    [DefaultExecutionOrder(5000)]
    public class DeathReplay : MonoBehaviour
    {
        /// <summary>How many seconds before the kill get played back, and the slow last moment.</summary>
        public const float Length = 2.5f, SlowPart = 0.3f, SlowRate = 0.4f, Hold = 0.4f;
        /// <summary>The replay's real running time.</summary>
        public static float Duration => Length - SlowPart + SlowPart / SlowRate + Hold;

        struct Look { public float T; public Vector3 Eye; public float Yaw, Pitch; }
        struct Pose { public float T; public Vector3 Pos; public Quaternion Rot; public Quaternion[] Bones; }

        static DeathReplay s_Me;
        const float Keep = 4f, Every = 1f / 40f;
        readonly Dictionary<ulong, List<Look>> m_Looks = new Dictionary<ulong, List<Look>>();
        readonly List<Pose> m_Poses = new List<Pose>();
        Transform[] m_Bones;
        Transform m_BonesOf;
        float m_Next;

        // the replay being played
        List<Look> m_KLooks;
        List<Pose> m_MyPoses;
        float m_Died;
        GameObject m_Ghost;
        Transform[] m_GhostBones;
        readonly List<Renderer> m_Hidden = new List<Renderer>();
        ulong m_Killer, m_Mine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            if (s_Me != null) return;
            var go = new GameObject("death replay");
            DontDestroyOnLoad(go);
            s_Me = go.AddComponent<DeathReplay>();
        }

        void LateUpdate()
        {
            if (Time.time < m_Next) return;
            m_Next = Time.time + Every;
            float now = Time.time;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value) continue;
                if (!m_Looks.TryGetValue(p.NetworkObjectId, out var list)) m_Looks[p.NetworkObjectId] = list = new List<Look>();
                list.Add(new Look { T = now, Eye = p.EyePos, Yaw = p.transform.eulerAngles.y, Pitch = p.Pitch.Value });
                Trim(list, now);
            }
            // your own alien, bone by bone (only while you're alive: the replay ends at the kill)
            var me = PlayerNet.Local;
            var model = me != null ? me.AlienModel : null;
            if (me != null && !me.Dead.Value && model != null && model.gameObject.activeInHierarchy)
            {
                if (m_BonesOf != model) { m_BonesOf = model; m_Bones = model.GetComponentsInChildren<Transform>(true); m_Poses.Clear(); }
                var bones = new Quaternion[m_Bones.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = m_Bones[i].localRotation;
                m_Poses.Add(new Pose { T = now, Pos = model.position, Rot = model.rotation, Bones = bones });
                while (m_Poses.Count > 0 && m_Poses[0].T < now - Keep) m_Poses.RemoveAt(0);
            }
        }

        static void Trim(List<Look> list, float now)
        {
            int n = 0;
            while (n < list.Count && list[n].T < now - Keep) n++;
            if (n > 0) list.RemoveRange(0, n);
        }

        /// <summary>You were just killed by `killer`: keeps what led up to it, ready to play back.</summary>
        public static bool Begin(PlayerNet me, PlayerNet killer)
        {
            if (s_Me == null || me == null || killer == null) return false;
            End();
            var r = s_Me;
            if (!r.m_Looks.TryGetValue(killer.NetworkObjectId, out var looks) || looks.Count < 4 || r.m_Poses.Count < 4) return false;
            r.m_KLooks = new List<Look>(looks);
            r.m_MyPoses = new List<Pose>(r.m_Poses);
            r.m_Died = r.m_MyPoses[r.m_MyPoses.Count - 1].T;
            r.m_Killer = killer.NetworkObjectId;
            r.m_Mine = me.NetworkObjectId;
            // the copy of your alien that acts it out (hidden until the replay starts)
            var model = me.AlienModel;
            if (model == null) return false;
            r.m_Ghost = Instantiate(model.gameObject, model.position, model.rotation);
            r.m_Ghost.name = "replay alien";
            r.m_Ghost.transform.localScale = model.lossyScale;
            foreach (var hf in r.m_Ghost.GetComponentsInChildren<HatFollow>(true)) Destroy(hf);
            foreach (var rd in r.m_Ghost.GetComponentsInChildren<Renderer>(true))
            {
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (rd is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                if (rd.name == "outline copy") rd.enabled = false;
            }
            foreach (var c in r.m_Ghost.GetComponentsInChildren<Collider>(true)) Destroy(c);
            r.m_GhostBones = r.m_Ghost.GetComponentsInChildren<Transform>(true);
            r.m_Ghost.SetActive(false);
            return true;
        }

        /// <summary>t seconds into the replay: puts the camera where the killer's eyes were and your copy where you were.
        /// False once it's over.</summary>
        public static bool Play(float t, Camera cam)
        {
            var r = s_Me;
            if (r == null || r.m_Ghost == null || cam == null) return false;
            if (t > Duration) return false;
            // the time in the recording: real time, then the last moment slowed right down, then a hold on the kill
            float fast = Length - SlowPart;
            float rt = t < fast ? t : Mathf.Min(Length, fast + (t - fast) * SlowRate);
            float at = r.m_Died - Length + rt;
            if (!r.m_Ghost.activeSelf)
            {
                r.m_Ghost.SetActive(true);
                Ragdoll.SetHidden(r.m_Mine, true);
                r.m_Hidden.Clear();
            }
            // the killer's own body - its outline shell, what it holds, all of it - is kept hidden (we're looking out of
            // their eyes: from inside the shell everything went their team's colour); every frame, as things get switched
            // on again by their own code
            foreach (var p in PlayerNet.All)
                if (p != null && p.NetworkObjectId == r.m_Killer)
                    foreach (var rd in p.GetComponentsInChildren<Renderer>(true))
                        if (!rd.forceRenderingOff) { rd.forceRenderingOff = true; r.m_Hidden.Add(rd); }
            // your copy, posed as you were
            int i = Find(r.m_MyPoses, at, out float f);
            var a = r.m_MyPoses[i];
            var b = r.m_MyPoses[Mathf.Min(i + 1, r.m_MyPoses.Count - 1)];
            r.m_Ghost.transform.SetPositionAndRotation(Vector3.Lerp(a.Pos, b.Pos, f), Quaternion.Slerp(a.Rot, b.Rot, f));
            int n = Mathf.Min(r.m_GhostBones.Length, a.Bones.Length);
            for (int j = 1; j < n; j++) r.m_GhostBones[j].localRotation = Quaternion.Slerp(a.Bones[j], b.Bones[j], f);
            // the camera: the killer's eyes and aim
            int li = FindLook(r.m_KLooks, at, out float lf);
            var la = r.m_KLooks[li];
            var lb = r.m_KLooks[Mathf.Min(li + 1, r.m_KLooks.Count - 1)];
            var eye = Vector3.Lerp(la.Eye, lb.Eye, lf);
            var rot = Quaternion.Euler(Mathf.LerpAngle(la.Pitch, lb.Pitch, lf), Mathf.LerpAngle(la.Yaw, lb.Yaw, lf), 0f);
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 8f);
            return true;
        }

        /// <summary>0..1 through the replay (for the HUD), and whether it's in its slow last moment.</summary>
        public static bool Slow(float t) => t > Length - SlowPart;

        /// <summary>The replay's over: the copy goes and the killer's body shows again.</summary>
        public static void End()
        {
            var r = s_Me;
            if (r == null) return;
            if (r.m_Ghost != null) { Destroy(r.m_Ghost); r.m_Ghost = null; Ragdoll.SetHidden(r.m_Mine, false); }
            foreach (var rd in r.m_Hidden) if (rd != null) rd.forceRenderingOff = false;
            r.m_Hidden.Clear();
        }

        static int Find(List<Pose> l, float t, out float f)
        {
            f = 0f;
            if (t <= l[0].T) return 0;
            for (int i = 0; i < l.Count - 1; i++)
                if (l[i + 1].T >= t) { f = Mathf.InverseLerp(l[i].T, l[i + 1].T, t); return i; }
            return l.Count - 1;
        }

        static int FindLook(List<Look> l, float t, out float f)
        {
            f = 0f;
            if (t <= l[0].T) return 0;
            for (int i = 0; i < l.Count - 1; i++)
                if (l[i + 1].T >= t) { f = Mathf.InverseLerp(l[i].T, l[i + 1].T, t); return i; }
            return l.Count - 1;
        }
    }

    public partial class PlayerNet
    {
        /// <summary>The alien model under the visual root (null: none, e.g. the PSX look).</summary>
        public Transform AlienModel => m_VisualRoot != null ? m_VisualRoot.Find("alien") : null;
        /// <summary>The root everyone sees this player's body under.</summary>
        public Transform VisualRoot => m_VisualRoot;
    }
}
