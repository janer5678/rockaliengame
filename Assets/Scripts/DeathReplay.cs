using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The kill cam's replay (Call of Duty style): this PC keeps the last few seconds of everyone - where each player stood
    /// and looked, what they held and were doing, their swings and throws - plus how your own alien was posed bone by
    /// bone, every hit you took, and (once you're dead) how your ragdoll fell. When you're killed
    /// (PlayerController.KillCam.cs), after the glide to your killer's face, it plays those seconds back out of the
    /// killer's eyes: their hands and weapon in their team's colour, swinging when they swung, their crosshair, a damage
    /// number and hit marker for each hit as they saw it (Hud.Death.cs), a copy of your alien acting out what you did -
    /// and at the end you see yourself die and go down as a ragdoll, the last moments before it and the fall in slow
    /// motion. Only the look: nothing is sent anywhere, and the copy, the hands and the camera are gone the moment it ends.
    /// </summary>
    [DefaultExecutionOrder(5000)]
    public class DeathReplay : MonoBehaviour
    {
        /// <summary>How many seconds before the kill get played back, how much of that (and of the fall after it) is slowed
        /// down, and how slow.</summary>
        public const float Length = 4f, SlowBefore = 0.6f, After = 0.8f, SlowRate = 0.4f;
        /// <summary>The replay's real running time.</summary>
        public static float Duration => Length - SlowBefore + (SlowBefore + After) / SlowRate;

        struct Look { public float T; public Vector3 Eye; public float Yaw, Pitch; public Item Held; public byte Act; public bool Crouch, Ball, Arrow; }
        struct Pose { public float T; public Vector3 Pos; public Quaternion Rot; public Quaternion[] Bones; }
        struct Ev { public float T; public bool Throw; public Item Held; }
        struct Hit { public float T; public Vector3 Pos; public float Amount; public bool Kill; }
        struct Fall { public float T; public Vector3[] Pos; public Quaternion[] Rot; }

        static DeathReplay s_Me;
        const float Keep = 8f, Every = 1f / 40f;
        readonly Dictionary<ulong, List<Look>> m_Looks = new Dictionary<ulong, List<Look>>();
        readonly Dictionary<ulong, List<Ev>> m_Evs = new Dictionary<ulong, List<Ev>>();
        readonly Dictionary<ulong, Vector2> m_PrevAnim = new Dictionary<ulong, Vector2>();
        readonly List<Pose> m_Poses = new List<Pose>();
        readonly List<Hit> m_Hits = new List<Hit>();
        readonly List<Fall> m_Falls = new List<Fall>();
        Transform[] m_Bones;
        Transform m_BonesOf;
        float m_Next;
        // your health and death, and your ragdoll falling
        PlayerNet m_HpOf;
        float m_PrevHp, m_DeathT = -100f;
        bool m_WasDead;
        Transform m_NewestBefore, m_FallRoot;
        Transform[] m_FallSrc;
        string[] m_FallNames;

        // the replay being played
        List<Look> m_KLooks;
        List<Pose> m_MyPoses;
        List<Ev> m_KEvs;
        List<Hit> m_RHits;
        List<Fall> m_RFalls;
        string[] m_RFallNames;
        Transform[] m_GhostFall;
        float m_Died, m_At, m_PrevAt, m_LastHitT = -100f;
        bool m_LastHitKill;
        GameObject m_Ghost, m_Spare;
        bool m_SpareDone;
        Transform[] m_GhostBones;
        ViewModel m_VM, m_PrevLast;
        Color m_KColor;
        readonly List<Renderer> m_Hidden = new List<Renderer>();
        ulong m_Killer, m_Mine;

        // ---- test hooks ----
        /// <summary>The killer's hands are on screen in the replay right now.</summary>
        public static bool KillerHandsShown => s_Me != null && s_Me.m_VM != null && s_Me.m_VM.Root != null && s_Me.m_VM.Root.gameObject.activeInHierarchy;
        /// <summary>Your copy has gone down as a ragdoll (the replay's reached your death).</summary>
        public static bool GhostFell { get; private set; }
        /// <summary>How fast the replay runs right now (1 real time, SlowRate in slow motion).</summary>
        public static float Rate { get; private set; } = 1f;
        /// <summary>How many hits were played back (damage numbers) this replay.</summary>
        public static int HitsPlayed { get; private set; }

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
            float now = Time.time;
            // every frame: everyone's swings and throws (the triggers every screen gets), your health, your ragdoll
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned) continue;
                ulong id = p.NetworkObjectId;
                var anim = new Vector2(p.SwingAnim, p.ThrowAnim);
                if (m_PrevAnim.TryGetValue(id, out var prev) && !p.Dead.Value)
                {
                    if (!m_Evs.TryGetValue(id, out var evs)) m_Evs[id] = evs = new List<Ev>();
                    if (anim.x > prev.x + 0.05f) evs.Add(new Ev { T = now, Throw = false, Held = p.HeldItem });
                    if (anim.y > prev.y + 0.05f) evs.Add(new Ev { T = now, Throw = true, Held = p.HeldItem });
                    while (evs.Count > 0 && evs[0].T < now - Keep) evs.RemoveAt(0);
                }
                m_PrevAnim[id] = anim;
            }
            TrackMe(now);

            if (now < m_Next) return;
            m_Next = now + Every;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value) continue;
                if (!m_Looks.TryGetValue(p.NetworkObjectId, out var list)) m_Looks[p.NetworkObjectId] = list = new List<Look>();
                list.Add(new Look
                {
                    T = now, Eye = p.EyePos, Yaw = p.transform.eulerAngles.y, Pitch = p.Pitch.Value,
                    Held = p.HeldItem, Act = p.Action.Value, Crouch = p.Crouch.Value, Ball = p.CarryingBall, Arrow = p.Count(Item.Arrow) > 0,
                });
                Trim(list, now);
            }
            // your own alien, bone by bone (only while you're alive: after that it's your ragdoll)
            var me = PlayerNet.Local;
            var model = me != null ? me.AlienModel : null;
            if (me != null && !me.Dead.Value && model != null && model.gameObject.activeInHierarchy)
            {
                if (m_BonesOf != model) { m_BonesOf = model; m_Bones = model.GetComponentsInChildren<Transform>(true); m_Poses.Clear(); }
                var bones = new Quaternion[m_Bones.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = m_Bones[i] != null ? m_Bones[i].localRotation : Quaternion.identity;
                m_Poses.Add(new Pose { T = now, Pos = model.position, Rot = model.rotation, Bones = bones });
                while (m_Poses.Count > 0 && m_Poses[0].T < now - Keep) m_Poses.RemoveAt(0);
            }
            // your ragdoll as it falls (for the slow-motion end of the replay)
            if (m_FallRoot != null && now - m_DeathT < 3f && m_FallSrc != null)
            {
                var f = new Fall { T = now, Pos = new Vector3[m_FallSrc.Length], Rot = new Quaternion[m_FallSrc.Length] };
                for (int i = 0; i < m_FallSrc.Length; i++)
                    if (m_FallSrc[i] != null) { f.Pos[i] = m_FallSrc[i].position; f.Rot[i] = m_FallSrc[i].rotation; }
                m_Falls.Add(f);
            }
        }

        /// <summary>Every frame: the hits you take (how much, where you were) and the moment you die - then your ragdoll is
        /// looked for so its fall can be kept.</summary>
        void TrackMe(float now)
        {
            var me = PlayerNet.Local;
            if (me == null || !me.IsSpawned) { m_HpOf = null; return; }
            float hp = me.Health.Value + me.ArmorHp.Value;
            bool dead = me.Dead.Value;
            if (me != m_HpOf) { m_HpOf = me; m_PrevHp = hp; m_WasDead = dead; m_Hits.Clear(); }
            var at = me.transform.position + Vector3.up * 1.25f;
            if (!m_WasDead)
            {
                if (!dead) m_NewestBefore = Ragdoll.NewestHips != null ? Ragdoll.NewestHips.root : null; // (the newest body before ours)
                if (hp < m_PrevHp - 0.01f) m_Hits.Add(new Hit { T = now, Pos = at, Amount = m_PrevHp - hp });
                if (dead)
                {
                    // the last blow
                    if (m_Hits.Count > 0 && now - m_Hits[m_Hits.Count - 1].T < 0.3f) { var h = m_Hits[m_Hits.Count - 1]; h.Kill = true; m_Hits[m_Hits.Count - 1] = h; }
                    else m_Hits.Add(new Hit { T = now, Pos = at, Amount = Mathf.Max(1f, m_PrevHp), Kill = true });
                    m_DeathT = now;
                    m_FallRoot = null; m_FallSrc = null; m_FallNames = null;
                    m_Falls.Clear();
                }
            }
            while (m_Hits.Count > 0 && m_Hits[0].T < now - Keep) m_Hits.RemoveAt(0);
            // just died: your ragdoll (Ragdoll.cs) is the newest one that turns up where you were
            if (dead && m_FallRoot == null && now - m_DeathT < 0.6f && m_Bones != null && m_Bones.Length > 0)
            {
                var hips = Ragdoll.NewestHips;
                if (hips != null && hips.root != m_NewestBefore && hips.root.name == "ragdoll" && m_Poses.Count > 0
                    && Vector3.Distance(hips.root.position, m_Poses[m_Poses.Count - 1].Pos) < 4f)
                {
                    m_FallRoot = hips.root;
                    var byName = new Dictionary<string, Transform>();
                    foreach (var t in m_FallRoot.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                    var names = new List<string>();
                    var src = new List<Transform>();
                    for (int i = 0; i < m_Bones.Length; i++)
                    {
                        if (m_Bones[i] == null) continue;
                        if (i == 0) { names.Add(""); src.Add(m_FallRoot); continue; } // (the root: "alien" here, "ragdoll" there)
                        if (byName.TryGetValue(m_Bones[i].name, out var t) && !names.Contains(m_Bones[i].name)) { names.Add(m_Bones[i].name); src.Add(t); }
                    }
                    m_FallNames = names.ToArray();
                    m_FallSrc = src.ToArray();
                }
            }
            m_PrevHp = hp;
            m_WasDead = dead;
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
            GhostFell = false; Rate = 1f; HitsPlayed = 0;
            r.m_SpareDone = false;
            if (!r.m_Looks.TryGetValue(killer.NetworkObjectId, out var looks) || looks.Count < 4 || r.m_Poses.Count < 4) return false;
            r.m_KLooks = new List<Look>(looks);
            r.m_MyPoses = new List<Pose>(r.m_Poses);
            r.m_KEvs = r.m_Evs.TryGetValue(killer.NetworkObjectId, out var evs) ? new List<Ev>(evs) : new List<Ev>();
            r.m_RHits = new List<Hit>(r.m_Hits);
            r.m_RFalls = new List<Fall>(r.m_Falls);
            r.m_RFallNames = r.m_FallNames;
            r.m_Died = Mathf.Abs(r.m_DeathT - r.m_MyPoses[r.m_MyPoses.Count - 1].T) < 0.5f ? r.m_DeathT : r.m_MyPoses[r.m_MyPoses.Count - 1].T;
            r.m_Killer = killer.NetworkObjectId;
            r.m_Mine = me.NetworkObjectId;
            r.m_KColor = Cfg.TeamColor[Mathf.Clamp(killer.Team.Value, 0, 3)];
            r.m_PrevAt = r.m_Died - Length - 0.01f;
            r.m_LastHitT = -100f;
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
            // the copy's bones by name, for the fall
            r.m_GhostFall = null;
            if (r.m_RFallNames != null && r.m_RFalls.Count > 1)
            {
                var byName = new Dictionary<string, Transform>();
                foreach (var t in r.m_GhostBones) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                r.m_GhostFall = new Transform[r.m_RFallNames.Length];
                for (int i = 0; i < r.m_RFallNames.Length; i++)
                    r.m_GhostFall[i] = i == 0 ? r.m_Ghost.transform : byName.TryGetValue(r.m_RFallNames[i], out var t) ? t : null;
            }
            r.m_Ghost.SetActive(false);
            return true;
        }

        /// <summary>t seconds into the replay: puts the camera where the killer's eyes were, their hands in front of it,
        /// and your copy where you were. False once it's over.</summary>
        public static bool Play(float t, Camera cam)
        {
            var r = s_Me;
            if (r == null || r.m_Ghost == null || cam == null) return false;
            if (t > Duration) return false;
            // the time in the recording: real time, then the last moment before the kill and the fall after it slowed down
            float fast = Length - SlowBefore;
            float rt = t < fast ? t : Mathf.Min(Length + After, fast + (t - fast) * SlowRate);
            Rate = t < fast ? 1f : SlowRate;
            float at = r.m_Died - Length + rt;
            r.m_At = at;
            if (!r.m_Ghost.activeSelf && !r.m_SpareDone)
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
            PoseGhost(r, at);
            // the camera: the killer's eyes and aim
            int li = FindLook(r.m_KLooks, at, out float lf);
            var la = r.m_KLooks[li];
            var lb = r.m_KLooks[Mathf.Min(li + 1, r.m_KLooks.Count - 1)];
            var eye = Vector3.Lerp(la.Eye, lb.Eye, lf);
            var rot = Quaternion.Euler(Mathf.LerpAngle(la.Pitch, lb.Pitch, lf), Mathf.LerpAngle(la.Yaw, lb.Yaw, lf), 0f);
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 8f);
            Hands(r, cam, at, li);
            r.m_PrevAt = at;
            return true;
        }

        /// <summary>Your copy: posed as you were, then - from the moment you died - going down the way your ragdoll did.</summary>
        static void PoseGhost(DeathReplay r, float at)
        {
            if (at >= r.m_Died)
            {
                GhostFell = true;
                if (r.m_GhostFall != null)
                {
                    int fi = FindFall(r.m_RFalls, at, out float ff);
                    var fa = r.m_RFalls[fi];
                    var fb = r.m_RFalls[Mathf.Min(fi + 1, r.m_RFalls.Count - 1)];
                    for (int j = 0; j < r.m_GhostFall.Length; j++)
                    {
                        var g = r.m_GhostFall[j];
                        if (g == null) continue;
                        g.SetPositionAndRotation(Vector3.Lerp(fa.Pos[j], fb.Pos[j], ff), Quaternion.Slerp(fa.Rot[j], fb.Rot[j], ff));
                    }
                    return;
                }
                // (no fall was kept - the ragdoll never turned up here: a ragdoll of the copy itself, knocked away from the killer)
                if (!r.m_SpareDone)
                {
                    var push = -r.m_Ghost.transform.forward * 2f + Vector3.up * 1.5f;
                    if (r.m_KLooks.Count > 0)
                    {
                        var d = r.m_Ghost.transform.position - r.m_KLooks[r.m_KLooks.Count - 1].Eye;
                        d.y = 0f;
                        if (d.sqrMagnitude > 0.01f) push = d.normalized * 4.5f + Vector3.up * 2f;
                    }
                    var before = Ragdoll.NewestHips;
                    r.m_SpareDone = true;
                    Ragdoll.Spawn(r.m_Ghost, null, push);
                    var hips = Ragdoll.NewestHips;
                    if (hips != null && hips != before) { r.m_Spare = hips.root.gameObject; r.m_Spare.name = "replay ragdoll"; }
                    r.m_Ghost.SetActive(false);
                }
                return;
            }
            int i = Find(r.m_MyPoses, at, out float f);
            var a = r.m_MyPoses[i];
            var b = r.m_MyPoses[Mathf.Min(i + 1, r.m_MyPoses.Count - 1)];
            r.m_Ghost.transform.SetPositionAndRotation(Vector3.Lerp(a.Pos, b.Pos, f), Quaternion.Slerp(a.Rot, b.Rot, f));
            int n = Mathf.Min(r.m_GhostBones.Length, a.Bones.Length);
            for (int j = 1; j < n; j++) r.m_GhostBones[j].localRotation = Quaternion.Slerp(a.Bones[j], b.Bones[j], f);
        }

        /// <summary>The killer's hands and weapon in front of the camera, on the recording's clock: what they held, what they
        /// were doing, each swing and throw when it happened, a kick for each shot that hit you; and the hits as they saw them.</summary>
        static void Hands(DeathReplay r, Camera cam, float at, int li)
        {
            if (r.m_VM == null)
            {
                r.m_PrevLast = ViewModel.Last;
                r.m_VM = new ViewModel(cam.transform, r.m_KColor); // (it's ViewModel.Last while it plays: the hands' own look)
            }
            r.m_VM.Clock = at;
            var look = r.m_KLooks[li];
            // the swings and throws that happened since the last frame
            foreach (var e in r.m_KEvs)
            {
                if (e.T <= r.m_PrevAt || e.T > at) continue;
                if (e.Throw) { r.m_VM.Throw(); continue; }
                bool landed = false;
                foreach (var h in r.m_RHits) if (h.T >= e.T - 0.05f && h.T <= e.T + 0.3f) landed = true;
                r.m_VM.WatchedSwing(e.Held, ViewModel.ImpactTime);
                r.m_VM.Impact(landed);
                Sfx.Play2D(Sfx.Swing, 0.35f, 0.15f);
            }
            // the hits on you: a damage number and hit marker each (Hud.Death.cs), and a gun's kick when it was a shot
            foreach (var h in r.m_RHits)
            {
                if (h.T <= r.m_PrevAt || h.T > at) continue;
                HitsPlayed++;
                r.m_LastHitT = h.T;
                r.m_LastHitKill = h.Kill;
                bool swung = false;
                foreach (var e in r.m_KEvs) if (!e.Throw && e.T >= h.T - 0.3f && e.T <= h.T + 0.05f) swung = true;
                if (!swung && Ranged(look.Held)) r.m_VM.Use();
                if (h.Kill) Sfx.Play2D(Sfx.Kill, 0.6f, 0f);
                else Sfx.Play2D(Sfx.Hit, 0.5f, 0.05f);
            }
            // how long they've been doing what they're doing (a bow draw builds up over it)
            int s = li;
            while (s > 0 && r.m_KLooks[s - 1].Act == look.Act) s--;
            r.m_VM.Update(ViewModel.Watched(look.Held, (BodyAnimator.Act)look.Act, at - r.m_KLooks[s].T, look.Crouch, look.Ball, look.Arrow, true));
        }

        static bool Ranged(Item i) => Cfg.IsGun(i) || i == Item.Shotgun || i == Item.Sniper || i == Item.Crossbow || i == Item.Bow
            || i == Item.DeathWand || i == Item.RocketLauncher || i == Item.PortalGun;

        /// <summary>Whether the replay's in its slow motion (the moments before the kill and the fall after it).</summary>
        public static bool Slow(float t) => t > Length - SlowBefore;

        /// <summary>The HUD (Hud.Death.cs): the hits played back so far that are still showing - where (world), how much,
        /// how long ago on the recording's clock (s), and whether it was the kill.</summary>
        public static int HitCount => s_Me != null && s_Me.m_RHits != null && s_Me.m_Ghost != null ? s_Me.m_RHits.Count : 0;
        public static bool GetHit(int i, out Vector3 pos, out float amount, out float age, out bool kill)
        {
            var h = s_Me.m_RHits[i];
            pos = h.Pos; amount = h.Amount; kill = h.Kill;
            age = s_Me.m_At - h.T;
            return age >= 0f;
        }

        /// <summary>The HUD: how long ago (recording clock) the last hit landed, and whether it was the kill (a red marker).</summary>
        public static float HitMarkerAge(out bool kill)
        {
            kill = s_Me != null && s_Me.m_LastHitKill;
            return s_Me != null && s_Me.m_Ghost != null ? s_Me.m_At - s_Me.m_LastHitT : 100f;
        }

        /// <summary>The replay's over: the copy, its ragdoll and the hands go, and the killer's body and your ragdoll show again.</summary>
        public static void End()
        {
            var r = s_Me;
            if (r == null) return;
            if (r.m_Ghost != null) { Destroy(r.m_Ghost); r.m_Ghost = null; Ragdoll.SetHidden(r.m_Mine, false); }
            if (r.m_Spare != null) { Destroy(r.m_Spare); r.m_Spare = null; }
            if (r.m_VM != null)
            {
                r.m_VM.Destroy();
                if (ViewModel.Last == r.m_VM) ViewModel.Last = r.m_PrevLast;
                r.m_VM = null;
            }
            foreach (var rd in r.m_Hidden) if (rd != null) rd.forceRenderingOff = false;
            r.m_Hidden.Clear();
            Rate = 1f;
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

        static int FindFall(List<Fall> l, float t, out float f)
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
