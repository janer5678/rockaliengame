using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wacky airdrop items on your own machine: what LMB does with each, the screens some open (Omen's map, the
    /// drawing canvas, the scribble notebook - drawn by Hud.Wacky.cs), and what they do to your moving and your camera:
    /// a bear's third-person view and claws, the turret bot's camera, the attention snail's shot, falling up with the
    /// gravity staff (the view upside down), double jumps, bouncy castles, being an ant, dancing, knocked flat, timed out.
    /// </summary>
    public partial class PlayerController
    {
        public bool OmenMapOpen { get; private set; }
        public bool DrawOpen { get; private set; }
        public bool ScribbleOpen { get; private set; }
        /// <summary>One of the wacky items' screens is up (the mouse is free for it, you stand still).</summary>
        public bool WackyUiOpen => OmenMapOpen || DrawOpen || ScribbleOpen;
        Vector3 m_ScribbleAim;
        float m_DrawOpenedAt;
        /// <summary>What you held before the decoy (the decoy holds it too).</summary>
        Item m_PrevHeld = Item.Rock;
        bool m_DoubleJumped, m_FlipBump;
        float m_NextBounce, m_FlipRoll, m_KnockRoll;
        /// <summary>For the tests: double jumps and castle bounces done.</summary>
        public int DoubleJumps { get; private set; }
        public int CastleBounces { get; private set; }

        public float DrawTimeLeft => DrawOpen ? Mathf.Max(0f, Cfg.DrawTime - (Time.time - m_DrawOpenedAt)) : 0f;

        public void CloseWackyUi() { OmenMapOpen = DrawOpen = ScribbleOpen = false; }

        void TrackPrevHeld(Item held) { if (held != Item.Decoy && held != Item.None) m_PrevHeld = held; }

        /// <summary>Held still by a wacky item (or flying the turret bot, or with one of their screens up).</summary>
        bool WackyMoveBlocked => m_Net.WackyHeld || m_Net.Droning || WackyUiOpen;

        /// <summary>Update, before the held item's own controls: true = they're skipped (a bear has claws, the turret bot
        /// has its own buttons, and nobody shielded, an ant or held still can attack).</summary>
        bool WackyInput(Item held)
        {
            if (m_Net.IsBear) { HandleBear(); return true; }
            if (m_Net.Droning) { HandleDroneInput(); return true; }
            if (m_Net.WackyHeld) return true;
            if (m_Net.Shielded || m_Net.IsAnt)
            {
                if (Binds.Down(Bind.Attack) && Time.time >= m_NextSwing) { m_NextSwing = Time.time + 0.5f; Hud.WackyNote(m_Net.IsAnt ? "Ants can't attack!" : "You can't attack while your shield is up"); Sfx.Play2D(Sfx.Click, 0.4f); }
                return true;
            }
            return false;
        }

        bool Clicked(float cooldown = 0.5f)
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return false;
            m_NextSwing = Time.time + cooldown;
            return true;
        }

        void UseWacky(Item kind, Vector3 point, NetworkObject target = null, int arg = 0)
        {
            var ray = CenterRay();
            m_Net.UseWackyRpc(kind, ray.origin, ray.direction, point, target != null, target != null ? new NetworkObjectReference(target) : default, arg);
        }

        /// <summary>The wacky items' LMB (the melee ones - the knockback sticks, the tree breaker, the golden rock - swing
        /// like any other melee weapon). True if it's one of them.</summary>
        bool HandleWacky(Item held)
        {
            var ray = CenterRay();
            switch (held)
            {
                case Item.BouncyCastle:
                    if (!Clicked()) return true;
                    UseWacky(held, Aim(ray, 45f, out var ch) ? ch.point : ray.GetPoint(8f));
                    m_VM.Use(); Sfx.Play2D(Sfx.Pop, 0.6f);
                    return true;
                case Item.Decoy:
                    if (!Clicked()) return true;
                    UseWacky(held, Vector3.zero, null, (int)m_PrevHeld);
                    m_VM.Use(); Sfx.Play2D(Sfx.Pop, 0.6f);
                    return true;
                case Item.OmenMap:
                    if (Binds.Down(Bind.Attack)) { OmenMapOpen = true; MenuOpen = false; Sfx.Play2D(Sfx.UiClick, 0.5f); }
                    return true;
                case Item.BlueShell:
                case Item.SuicideBomb:
                case Item.InvSwapper:
                case Item.ShieldPotion:
                case Item.BearJuice:
                case Item.ChaosSigil:
                case Item.DroneBot:
                case Item.BaseRebuild:
                case Item.FakeFlare:
                case Item.DisguiseKit:
                    if (!(Clicked(0.8f) || (Binds.Down(Bind.Aim) && Time.time >= m_NextSwing))) return true;
                    m_NextSwing = Time.time + 0.8f;
                    UseWacky(held, Vector3.zero);
                    if (held == Item.ShieldPotion || held == Item.BearJuice) { m_VM.Eat(); Sfx.Play2D(Sfx.Eat, 0.6f); m_EatFlashUntil = Time.time + 0.9f; }
                    else { m_VM.Use(); Sfx.Play2D(held == Item.SuicideBomb ? Sfx.Beep : Sfx.Zap, 0.6f); }
                    return true;
                case Item.Connect4Wand:
                case Item.GameshowWand:
                case Item.AntGun:
                {
                    if (!Clicked(0.7f)) return true;
                    NetworkObject tno = null;
                    var end = ray.GetPoint(40f);
                    if (AimWithAssist(ray, held == Item.AntGun ? 60f : 40f, Cfg.ProjectileAssist + 0.25f, out var wh))
                    {
                        end = wh.point;
                        var no = wh.collider.GetComponentInParent<NetworkObject>();
                        if (no != null && no.GetComponent<PlayerNet>() != null && no.GetComponent<PlayerNet>() != m_Net) tno = no;
                    }
                    UseWacky(held, end, tno);
                    Fx.Play(FxKind.WackyBeam, m_VM.Muzzle(), end - m_VM.Muzzle());
                    m_VM.Use(); Fx.Kick(2f);
                    return true;
                }
                case Item.StickOfTruth:
                case Item.SlowGun:
                {
                    if (!Clicked(0.8f)) return true;
                    var end = Aim(ray, held == Item.StickOfTruth ? 120f : 90f, out var sh) ? sh.point : ray.GetPoint(held == Item.StickOfTruth ? 40f : 30f);
                    UseWacky(held, end);
                    Fx.Play(FxKind.WackyBeam, m_VM.Muzzle(), end - m_VM.Muzzle());
                    m_VM.Use(); Fx.Kick(3f); Fx.Shake(0.15f);
                    return true;
                }
                case Item.GravityStaff:
                    if (!Clicked(0.6f)) return true;
                    m_Net.GravFlip.Value = !m_Net.GravFlip.Value;
                    m_VelY = 0f;
                    m_DoubleJumped = false;
                    m_VM.Use();
                    Sfx.Play2D(Sfx.Portal, 0.7f);
                    Fx.Punch(6f);
                    return true;
                case Item.AttentionSnail:
                    if (!Clicked()) return true;
                    UseWacky(held, Aim(ray, 12f, out var snh) ? snh.point : Vector3.zero);
                    m_VM.Use(); Sfx.Play2D(Sfx.Place, 0.6f);
                    return true;
                case Item.TimeoutBall:
                {
                    if (!Clicked(0.7f)) return true;
                    var origin = SafeOrigin(ray, 0.6f);
                    var vel = ray.direction * 17f + Vector3.up * 3f;
                    TimeoutBall.Spawn(origin, vel, m_Net, true);
                    m_Net.ThrowTimeoutRpc(origin, vel);
                    m_VM.Throw(); Sfx.Play2D(Sfx.Throw, 0.6f);
                    return true;
                }
                case Item.DrawMonster:
                    if (Binds.Down(Bind.Attack)) { DrawOpen = true; m_DrawOpenedAt = Time.time; MenuOpen = false; Hud.BeginDrawing(); Sfx.Play2D(Sfx.UiClick, 0.5f); }
                    return true;
                case Item.Scribble:
                    if (Binds.Down(Bind.Attack)) { ScribbleOpen = true; m_ScribbleAim = Aim(ray, 40f, out var sch) ? sch.point : Vector3.zero; MenuOpen = false; Hud.BeginScribble(); Sfx.Play2D(Sfx.UiClick, 0.5f); }
                    return true;
                case Item.DanceBomb:
                case Item.ClutterBomb:
                case Item.DecoyBase:
                    HandleLootThrow(held);
                    return true;
                case Item.BallGrabber:
                {
                    if (!Clicked(0.6f)) return true;
                    var b = Ball.Instance;
                    if (b == null || !b.IsSpawned) { Hud.WackyNote("There's no ball"); return true; }
                    var to = b.transform.position - ray.origin;
                    if (Vector3.Angle(ray.direction, to) > 9f) { Hud.WackyNote("Look right at the ball, then click"); return true; }
                    UseWacky(held, b.transform.position);
                    Fx.Play(FxKind.Tractor, ray.origin, to.normalized);
                    m_VM.Use();
                    return true;
                }
                case Item.JumpBoots: return true; // (they go on by themselves)
            }
            return false;
        }

        // ---------------- the screens ----------------

        /// <summary>Omen's map: clicked a spot - off you go.</summary>
        public void ConfirmOmen(Vector3 world)
        {
            OmenMapOpen = false;
            if (m_Net.HeldItem != Item.OmenMap) return;
            UseWacky(Item.OmenMap, world);
        }

        /// <summary>The drawing's done (or its time's up): it comes alive.</summary>
        public void SubmitDrawing(byte[] px)
        {
            DrawOpen = false;
            if (m_Net.HeldItem != Item.DrawMonster || px == null) return;
            m_Net.MonsterDrawingRpc(px);
            Sfx.Play2D(Sfx.Zap, 0.6f);
        }

        public void SubmitScribble(string word)
        {
            ScribbleOpen = false;
            if (m_Net.HeldItem != Item.Scribble || string.IsNullOrWhiteSpace(word)) return;
            word = word.Trim();
            if (word.Length > 28) word = word.Substring(0, 28);
            m_Net.ScribbleRpc(new Unity.Collections.FixedString64Bytes(word), m_ScribbleAim);
            Sfx.Play2D(Sfx.Pop, 0.6f);
        }

        /// <summary>Every frame: the screens close if what opened them is gone (or you died).</summary>
        void TickWackyUi(bool dead)
        {
            var held = m_Net.HeldItem;
            if (dead || held != Item.OmenMap) OmenMapOpen = false;
            if (dead || held != Item.Scribble) ScribbleOpen = false;
            if (dead || held != Item.DrawMonster) DrawOpen = false;
            if (DrawOpen && DrawTimeLeft <= 0f) SubmitDrawing(Hud.DrawingPixels());
        }

        // ---------------- the bear and the turret bot ----------------

        void HandleBear()
        {
            if (Input.GetKeyDown(KeyCode.X)) { m_Net.BearOffRpc(); Sfx.Play2D(Sfx.Pop, 0.6f); return; }
            if (!Clicked(0.6f)) return;
            var ray = CenterRay();
            Sfx.Play2D(Sfx.Swing, 0.5f, 0.1f);
            Fx.Kick(1.5f);
            if (AimWithAssist(ray, 4f, Cfg.MeleeAssist, out var hit))
            {
                var no = hit.collider.GetComponentInParent<NetworkObject>();
                if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value) { Fx.Blood(hit.point, ray.direction, false); Hud.HitMarker(false, false); Fx.Shake(0.25f); }
                else Fx.Play(FxKind.StructureHit, hit.point, hit.normal);
                if (no != null) m_Net.BearClawRpc(true, no, hit.point);
                else m_Net.BearClawRpc(false, default, hit.point);
            }
            else m_Net.BearClawRpc(false, default, ray.GetPoint(3f));
        }

        void HandleDroneInput()
        {
            if (Binds.Down(Bind.Attack)) { m_Net.DroneBlastRpc(); Sfx.Play2D(Sfx.Beep, 0.6f); }
            else if (Binds.Down(Bind.Interact)) { m_Net.DroneExitRpc(); m_SkipInteract = Time.frameCount; }
        }

        Vehicle MyDrone => m_Net.Droning && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(m_Net.DroneId.Value, out var no) ? no.GetComponent<Vehicle>() : null;

        /// <summary>A bear can only pick up the ball, open doors and put the ball in its machine - nothing else.</summary>
        bool BearMayUse(Interactable t) => t.Kind == TargetKind.Ball || t.Kind == TargetKind.Door || t.Kind == TargetKind.Machine;

        // ---------------- moving ----------------

        /// <summary>Gravity's way: 1 normally, -1 with the gravity staff flipped (you fall up).</summary>
        float GravSign => m_Net.Flipped ? -1f : 1f;

        /// <summary>The highest you can fall up to with nothing over you: you walk on the sky there, upside down.</summary>
        float SkyCeiling => MapBuilder.Height(transform.position.x, transform.position.z) + 45f;

        /// <summary>Flipped: standing on whatever's over you (or the sky's ceiling).</summary>
        bool FlipGrounded => m_FlipBump || transform.position.y >= SkyCeiling - 0.05f;

        /// <summary>After the move: flipped, keep under the sky's ceiling, and note whether you're up against something.</summary>
        void AfterMove(CollisionFlags flags)
        {
            m_FlipBump = m_Net.Flipped && (flags & CollisionFlags.Above) != 0;
            if (m_Net.Flipped && transform.position.y > SkyCeiling)
            {
                var p = transform.position;
                p.y = SkyCeiling;
                bool was = m_CC.enabled;
                m_CC.enabled = false;
                transform.position = p;
                m_CC.enabled = was;
                if (m_VelY > 0f) m_VelY = 0f;
            }
        }

        /// <summary>The double jump boots: a second jump in the air (once until you land).</summary>
        bool TryDoubleJump(bool grounded, bool move)
        {
            if (grounded) { m_DoubleJumped = false; return false; }
            if (!move || !m_Net.WornBoots.Value || m_DoubleJumped || !Binds.Down(Bind.Jump)) return false;
            m_DoubleJumped = true;
            m_VelY = Cfg.JumpSpeed * 1.05f * GravSign;
            DoubleJumps++;
            Sfx.Play2D(Sfx.Pop, 0.5f);
            for (int i = 0; i < 6; i++) FxParticle.Puff(transform.position + Random.insideUnitSphere * 0.3f, new Color(1f, 1f, 1f, 0.5f), Random.Range(0.3f, 0.5f));
            return true;
        }

        /// <summary>Landed on a bouncy castle's floor: up you go (higher if you jump as you land).</summary>
        void TickBounce(bool grounded)
        {
            if (!grounded || m_VelY > 0.5f || Time.time < m_NextBounce || m_Net.Flipped) return;
            if (!Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var h, 0.6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) return;
            if (h.collider.GetComponentInParent<BouncySurface>() == null) return;
            m_NextBounce = Time.time + 0.25f;
            m_VelY = Cfg.CastleBounce * (Binds.Held(Bind.Jump) ? 1.35f : 1f);
            m_JumpedSinceGround = true;
            CastleBounces++;
            Sfx.Play2D(Sfx.Boing, 0.6f);
        }

        // ---------------- the camera ----------------

        /// <summary>Your eye's height with the wacky items: an ant's is down by the ground; flipped, your head's at the
        /// bottom of you (your feet are on the ceiling).</summary>
        float WackyEye(float eye)
        {
            if (m_Net.IsAnt) return 0.22f;
            if (m_Net.KnockedDown) return 0.35f;
            if (m_Net.Flipped) return (Crouching ? Cfg.CrouchHeight : Cfg.StandHeight) - eye;
            return eye;
        }

        /// <summary>The third-person view (as the tree camo has): a bear, and while you dance.</summary>
        bool WackyThird => m_Net.IsBear || m_Net.Dancing;

        /// <summary>LateUpdate: the wacky items' camera - the turret bot's eye, the attention snail's shot, the view rolled
        /// over when flipped or knocked flat. `hands`: whether your hands still show.</summary>
        void WackyCamera(ref Vector3 camPos, ref Quaternion rot, ref float fov, ref bool hands)
        {
            float dt = Time.deltaTime;
            m_FlipRoll = Mathf.MoveTowards(m_FlipRoll, m_Net.Flipped ? 180f : 0f, dt * 420f);
            m_KnockRoll = Mathf.MoveTowards(m_KnockRoll, m_Net.KnockedDown ? 70f : 0f, dt * (m_Net.KnockedDown ? 400f : 160f));
            if (m_FlipRoll > 0.01f) rot *= Quaternion.Euler(0f, 0f, m_FlipRoll);
            if (m_KnockRoll > 0.01f) { rot *= Quaternion.Euler(-m_KnockRoll * 0.5f, 0f, m_KnockRoll); hands = false; }
            if (m_Net.IsAnt || m_Net.IsBear) hands = false;
            var drone = MyDrone;
            if (drone != null)
            {
                camPos = drone.DroneEye;
                rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f) * Quaternion.Euler(Fx.ShakeEuler());
                fov = 88f;
                hands = false;
                return;
            }
            if (SnailShot(out var sp, out var sr))
            {
                camPos = sp;
                rot = sr;
                fov = 34f;
                hands = false;
            }
        }

        /// <summary>The attention snail: while one's down every camera is on it - close in behind it, looking past it at
        /// whoever's nearest to it.</summary>
        public static bool SnailShot(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var g = NetGame.Instance;
            if (g == null || !g.IsSpawned) return false;
            double now = g.NetworkManager.ServerTime.Time;
            Gadget snail = default;
            bool any = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Snail && gd.Until > now && (!any || gd.Born > snail.Born)) { snail = gd; any = true; }
            if (!any) return false;
            var s = snail.Pos + Vector3.up * 0.3f;
            PlayerNet near = null;
            float best = float.MaxValue;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value) continue;
                float d = Vector3.Distance(p.transform.position, s);
                if (d < best) { best = d; near = p; }
            }
            var look = near != null ? near.transform.position + Vector3.up * 1.1f : s + Quaternion.Euler(0, snail.Yaw, 0) * Vector3.forward * 5f;
            var away = s - look; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Quaternion.Euler(0, snail.Yaw, 0) * Vector3.back;
            away.Normalize();
            // a little off to one side and up, close in: the snail big in the bottom of the shot, whoever's nearest
            // to it in the background
            var across = Vector3.Cross(Vector3.up, away);
            pos = s + away * 2.1f + across * 0.6f + Vector3.up * 0.45f;
            var focus = s + Vector3.up * 0.1f + (look - s).normalized * 2.2f;
            rot = Quaternion.LookRotation(focus - pos);
            return true;
        }
    }

    /// <summary>The bouncy castle's floor (Gadgets.cs): landing on it bounces you up (PlayerController.TickBounce).</summary>
    public class BouncySurface : MonoBehaviour { }
}
