using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// First-person hands. Every item has a hand pose; one-handed items sit in the right fist, two-handed
    /// items (spear, ram, ball, bow) place the second hand on the item. Rust-style chop for melee:
    /// quick raise, hard swing down, slow recover.
    /// </summary>
    public class ViewModel
    {
        /// <summary>Seconds from click to the moment the swing connects (the hit is applied then).</summary>
        public const float ImpactTime = 0.13f;

        public struct State
        {
            public Item Item;
            public bool Ball, Visible, SpearAim, Crouch, HasArrow;
            public float Draw, RamCharge, Bob, Speed;
            public Vector2 Look;
        }

        readonly Transform m_Root, m_R, m_L, m_ItemHolder;
        GameObject m_Item, m_Arrow, m_Ball;
        Item m_ItemId = (Item)255;
        bool m_WasBall;
        float m_SwingStart = -10f, m_SwingDur = 0.6f, m_ThrowStart = -10f, m_EatStart = -10f, m_UseStart = -10f, m_EquipStart = -10f;
        Vector2 m_Sway;
        float m_CrouchK;

        public ViewModel(Transform cam, Color team)
        {
            m_Root = new GameObject("ViewModel").transform;
            m_Root.SetParent(cam, false);
            var skin = new Color(0.6f, 0.64f, 0.58f);
            skin = Color.Lerp(skin, team, 0.25f);
            m_R = MakeArm(m_Root, skin, team, true);
            m_L = MakeArm(m_Root, skin, team, false);
            m_ItemHolder = new GameObject("item").transform;
            m_ItemHolder.SetParent(m_Root, false);
        }

        public void Destroy()
        {
            if (m_Root) Object.Destroy(m_Root.gameObject);
        }

        public void Swing(float cooldown) { m_SwingStart = Time.time; m_SwingDur = Mathf.Max(0.3f, cooldown); }
        public void Throw() => m_ThrowStart = Time.time;
        public void Eat() => m_EatStart = Time.time;
        public void Use() => m_UseStart = Time.time;

        static Transform MakeArm(Transform parent, Color skin, Color team, bool right)
        {
            var hand = new GameObject(right ? "R" : "L").transform;
            hand.SetParent(parent, false);
            float side = right ? 1f : -1f;
            var dark = skin * 0.85f; dark.a = 1f;
            Art.Box(hand, skin, Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f));                                  // fist
            Art.Box(hand, dark, new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f));             // knuckles
            Art.Box(hand, skin, new Vector3(-0.045f * side, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), new Vector3(0, 20 * side, 0)); // thumb
            Art.Box(hand, team, new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f));               // wrist band
            Art.Box(hand, skin, new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f));                  // forearm
            foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return hand;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        public void Update(State s)
        {
            float dt = Time.deltaTime;
            bool vis = s.Visible;
            if (m_Root.gameObject.activeSelf != vis) m_Root.gameObject.SetActive(vis);
            if (!vis) return;

            // ---- models ----
            var want = s.Ball ? (Item)254 : s.Item;
            if (want != m_ItemId)
            {
                if (m_Item) Object.Destroy(m_Item);
                if (m_Ball) Object.Destroy(m_Ball);
                m_Item = null; m_Ball = null;
                if (s.Ball) m_Ball = ItemModels.CreateBall(m_Root, 0.62f);
                else if (s.Item != Item.None) m_Item = ItemModels.Create(s.Item, m_ItemHolder);
                foreach (var r in m_Root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!(m_WasBall && !s.Ball)) m_EquipStart = Time.time; // no raise after throwing the ball
                m_ItemId = want;
                m_WasBall = s.Ball;
            }
            bool wantArrow = s.Item == Item.Bow && s.Draw > 0 && s.HasArrow;
            if (wantArrow != (m_Arrow != null))
            {
                if (m_Arrow) Object.Destroy(m_Arrow);
                else
                {
                    m_Arrow = ItemModels.Create(Item.Arrow, m_Root);
                    foreach (var r in m_Arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // ---- shared motion ----
            m_Sway = Vector2.Lerp(m_Sway, -s.Look * 0.0035f, dt * 10f);
            m_Sway = Vector2.ClampMagnitude(m_Sway, 0.05f);
            m_CrouchK = Mathf.MoveTowards(m_CrouchK, s.Crouch ? 1f : 0f, dt * 5f);
            float bobAmt = Mathf.Clamp01(s.Speed / 5f);
            Vector3 bob = new Vector3(Mathf.Sin(s.Bob * 1.6f) * 0.014f, Mathf.Abs(Mathf.Cos(s.Bob * 1.6f)) * 0.018f, 0) * bobAmt;
            float equip = Smooth(Mathf.Clamp01((Time.time - m_EquipStart) / 0.25f));
            Vector3 shared = bob + new Vector3(m_Sway.x, m_Sway.y - m_CrouchK * 0.02f, 0) + Vector3.down * (1f - equip) * 0.35f;
            Quaternion sharedRot = Quaternion.Euler(m_Sway.y * 300f, -m_Sway.x * 300f, 0);

            float swingE = Time.time - m_SwingStart;
            bool swinging = swingE < m_SwingDur;

            if (s.Ball) { PoseBall(shared, sharedRot); return; }

            switch (s.Item)
            {
                case Item.None: PoseFists(shared, sharedRot, swinging, swingE); break;
                case Item.Rock:
                case Item.Hatchet:
                case Item.Pickaxe: PoseChop(s.Item, shared, sharedRot, swinging, swingE); break;
                case Item.Spear: PoseSpear(s, shared, sharedRot, swinging, swingE); break;
                case Item.Bow: PoseBow(s, shared, sharedRot); break;
                case Item.Ram: PoseRam(s, shared, sharedRot); break;
                default: PoseHeld(s.Item, shared, sharedRot); break;
            }
        }

        void Set(Transform t, Vector3 pos, Quaternion rot) { t.localPosition = pos; t.localRotation = rot; }
        void HideLeft() => Set(m_L, new Vector3(-0.3f, -0.9f, 0.2f), Quaternion.identity);

        void AttachItemToRight(Vector3 localPos, Vector3 localEuler, float scale = 1f)
        {
            m_ItemHolder.SetParent(m_R, false);
            Set(m_ItemHolder, localPos, Quaternion.Euler(localEuler));
            m_ItemHolder.localScale = Vector3.one * scale;
        }

        void AttachItemToRoot(Vector3 pos, Quaternion rot, float scale = 1f)
        {
            m_ItemHolder.SetParent(m_Root, false);
            Set(m_ItemHolder, pos, rot);
            m_ItemHolder.localScale = Vector3.one * scale;
        }

        /// <summary>Melee curve: 0 idle, then raise (-1) and slam (+1), then recover. Returns (raise, slam) weights.</summary>
        Vector2 ChopCurve(bool swinging, float e)
        {
            if (!swinging) return Vector2.zero;
            const float up = 0.08f, down = 0.14f, hold = 0.22f;
            float rec = Mathf.Max(0.15f, m_SwingDur * 0.8f - hold);
            if (e < up) return new Vector2(Smooth(e / up), 0);
            if (e < down) { float k = Smooth((e - up) / (down - up)); return new Vector2(1f - k, k); }
            if (e < hold) return new Vector2(0, 1);
            float r = Smooth(Mathf.Clamp01((e - hold) / rec));
            return new Vector2(0, 1f - r);
        }

        void PoseChop(Item item, Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            // the hand only tilts a little; the tool pivots in the fist so the forearm stays out of frame
            bool rock = item == Item.Rock;
            var c = ChopCurve(swinging, e);
            Vector3 idle = rock ? new Vector3(0.25f, -0.25f, 0.52f) : new Vector3(0.27f, -0.3f, 0.5f);
            Vector3 raised = idle + new Vector3(0.03f, 0.2f, -0.12f);
            Vector3 slam = rock ? new Vector3(0.12f, -0.36f, 0.64f) : new Vector3(0.14f, -0.38f, 0.62f);
            Vector3 pos = idle + (raised - idle) * c.x + (slam - idle) * c.y;
            float handX = -8f - 22f * c.x + 14f * c.y;
            Set(m_R, shared + pos, sharedRot * Quaternion.Euler(handX, -18f + c.y * 10f, -8f - c.y * 8f));
            float itemX = rock ? 0f : 22f - 40f * c.x + 60f * c.y;
            if (rock) AttachItemToRight(new Vector3(0, 0.03f, 0.06f), new Vector3(0, 0, 0), 0.8f);
            else AttachItemToRight(new Vector3(0, -0.06f, 0.01f), new Vector3(itemX, 0, 0));
            HideLeft();
        }

        void PoseFists(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            float jab = 0;
            if (swinging)
            {
                if (e < ImpactTime) jab = Smooth(e / ImpactTime);
                else jab = 1f - Smooth(Mathf.Clamp01((e - ImpactTime) / Mathf.Max(0.1f, m_SwingDur * 0.7f)));
            }
            Set(m_R, shared + Vector3.Lerp(new Vector3(0.2f, -0.24f, 0.4f), new Vector3(0.06f, -0.15f, 0.62f), jab), sharedRot * Quaternion.Euler(-8f, -12f + jab * 8f, -10f));
            Set(m_L, shared + new Vector3(-0.21f, -0.26f, 0.38f), sharedRot * Quaternion.Euler(-8f, 12f, 10f));
            AttachItemToRight(Vector3.zero, Vector3.zero);
        }

        void PoseSpear(State s, Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            // the spear lives on the rig; both hands grab the shaft with natural wrist angles
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.25f);
            bool throwing = throwK < 1f;
            if (s.SpearAim || throwing)
            {
                float d = throwing ? 0f : s.Draw;
                Vector3 pos = new Vector3(0.4f, 0.17f, 0.15f - d * 0.25f);
                if (throwing) pos = Vector3.Lerp(pos, new Vector3(0.15f, -0.02f, 0.8f), Smooth(throwK));
                var rot = sharedRot * Quaternion.Euler(86f, -6f, 0);
                AttachItemToRoot(shared + pos, rot);
                if (m_Item) m_Item.SetActive(!throwing || throwK < 0.4f);
                m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.02f, 0.15f, -0.03f)));
                m_R.localRotation = sharedRot * Quaternion.Euler(-55f, -30f, -20f);
                Set(m_L, shared + new Vector3(-0.16f, -0.14f, 0.55f), sharedRot * Quaternion.Euler(-25f, 25f, 25f));
                return;
            }
            if (m_Item && !m_Item.activeSelf) m_Item.SetActive(true);
            float thrust = 0;
            if (swinging)
            {
                if (e < ImpactTime) thrust = Smooth(e / ImpactTime);
                else thrust = 1f - Smooth(Mathf.Clamp01((e - ImpactTime) / Mathf.Max(0.15f, m_SwingDur * 0.7f)));
            }
            var spearRot = sharedRot * Quaternion.Euler(80f, -5f, 0);
            AttachItemToRoot(shared + new Vector3(0.22f, -0.25f, 0.08f + thrust * 0.4f), spearRot);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.03f, 0.12f, 0)));
            m_R.localRotation = sharedRot * Quaternion.Euler(-25f, -28f, -15f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.03f, 0.62f, 0)));
            m_L.localRotation = sharedRot * Quaternion.Euler(-30f, 22f, 15f);
        }

        void PoseBow(State s, Vector3 shared, Quaternion sharedRot)
        {
            // bow held out on the left, right hand pulls the string back towards the face
            var bowPos = shared + new Vector3(-0.12f + s.Draw * 0.04f, -0.1f + s.Draw * 0.04f, 0.62f);
            var bowRot = sharedRot * Quaternion.Euler(0, 0, -12f + s.Draw * 8f);
            AttachItemToRoot(bowPos, bowRot, 0.75f);
            m_L.localPosition = bowPos + new Vector3(-0.02f, -0.01f, -0.02f);
            m_L.localRotation = sharedRot * Quaternion.Euler(-30f, 28f, -5f);
            var stringPoint = m_ItemHolder.TransformPoint(new Vector3(0, 0, -0.02f - s.Draw * 0.3f));
            m_R.localPosition = m_Root.InverseTransformPoint(stringPoint) + new Vector3(0.07f, -0.05f, -0.02f);
            m_R.localRotation = sharedRot * Quaternion.Euler(-20f, -55f, -30f);
            if (m_Arrow)
            {
                m_Arrow.transform.localPosition = m_Root.InverseTransformPoint(stringPoint) + new Vector3(0, 0, 0.12f);
                m_Arrow.transform.localRotation = sharedRot * Quaternion.Euler(90f, 0, 0);
            }
        }

        void PoseRam(State s, Vector3 shared, Quaternion sharedRot)
        {
            float strikeK = Mathf.Clamp01((Time.time - m_UseStart) / 0.45f);
            float thrust = strikeK < 1f ? (strikeK < 0.3f ? Smooth(strikeK / 0.3f) : 1f - Smooth((strikeK - 0.3f) / 0.7f)) : 0f;
            var pos = shared + new Vector3(0.18f, -0.4f, 0.55f - s.RamCharge * 0.3f + thrust * 0.7f);
            var rot = sharedRot * Quaternion.Euler(-4f + s.RamCharge * 6f, -8f, 0);
            AttachItemToRoot(pos, rot, 0.75f);
            // hands under the log, one near the back and one further forward
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.13f, -0.08f, -0.28f)));
            m_R.localRotation = rot * Quaternion.Euler(-10f, -10f, -60f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.13f, -0.08f, 0.22f)));
            m_L.localRotation = rot * Quaternion.Euler(-10f, 10f, 60f);
        }

        void PoseBall(Vector3 shared, Quaternion sharedRot)
        {
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.3f);
            bool throwing = throwK < 1f;
            // big ball in both hands filling the lower screen
            var ballPos = shared + new Vector3(0, -0.3f, 0.62f);
            if (throwing) ballPos += new Vector3(0, 0.2f, 0.6f) * Smooth(throwK);
            if (m_Ball)
            {
                m_Ball.transform.localPosition = ballPos;
                m_Ball.transform.localRotation = sharedRot;
                m_Ball.SetActive(!throwing || throwK < 0.6f);
            }
            float push = throwing ? Smooth(Mathf.Min(1f, throwK * 2f)) * 0.25f : 0f;
            Set(m_R, ballPos + new Vector3(0.3f, -0.06f, -0.08f + push), sharedRot * Quaternion.Euler(0, -35f, -80f));
            Set(m_L, ballPos + new Vector3(-0.3f, -0.06f, -0.08f + push), sharedRot * Quaternion.Euler(0, 35f, 80f));
        }

        void PoseHeld(Item item, Vector3 shared, Quaternion sharedRot)
        {
            float eatK = Mathf.Clamp01((Time.time - m_EatStart) / 0.6f);
            float toMouth = eatK < 1f ? Mathf.Sin(eatK * Mathf.PI) : 0f;
            float useK = Mathf.Clamp01((Time.time - m_UseStart) / 0.3f);
            float use = useK < 1f ? Mathf.Sin(useK * Mathf.PI) : 0f;
            Vector3 idle = new Vector3(0.26f, -0.27f, 0.45f);
            Vector3 pos = Vector3.Lerp(idle, new Vector3(0.04f, -0.13f, 0.22f), toMouth) + new Vector3(0, -0.05f, 0.12f) * use;
            Set(m_R, shared + pos, sharedRot * Quaternion.Euler(-12f - toMouth * 20f, -14f, -4f));
            switch (item)
            {
                case Item.BuildingPlan: AttachItemToRight(new Vector3(0, 0.03f, 0.05f), Vector3.zero, 0.55f); break;
                case Item.Chest: AttachItemToRight(new Vector3(0, 0.06f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.Barrier: AttachItemToRight(new Vector3(0, 0.04f, 0.06f), new Vector3(0, 20, 0), 0.8f); break;
                case Item.Arrow: AttachItemToRight(new Vector3(0, -0.1f, 0.02f), new Vector3(-30, 0, 0), 0.8f); break;
                default: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), Vector3.zero, 1f); break;
            }
            HideLeft();
        }
    }
}
