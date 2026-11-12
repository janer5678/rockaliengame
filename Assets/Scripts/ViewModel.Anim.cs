using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// What the first-person animations are made of (ViewModel.cs poses with them):
    ///  - a Key is one moment of a hold or a swing - where the hand is in the view, how it's turned, how far the tool is
    ///    tipped forward in it; swings go from key to key along curves (Arc), not straight lines, so a tool's head
    ///    travels round the way a real one does;
    ///  - the eases give each part of a move its own pace: a wind-up that slows at its top (EaseOut), a strike that
    ///    speeds up all the way to the hit (EaseIn), a way back to rest that starts gently, swings a touch past and
    ///    settles (InOutBack);
    ///  - a Spring is something with weight: it lags behind what it follows and swings a little past it as it catches up
    ///    (the hands as you look round, lean into a strafe, or land).
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>One moment of a hold or swing: the hand's place in the view, its turn (pitch, yaw, roll in degrees),
        /// and how far the tool is tipped forward in the hand.</summary>
        public struct Key
        {
            public Vector3 Pos, Rot;
            public float Item;
            public Key(float x, float y, float z, float pitch, float yaw, float roll, float item = 0f)
            {
                Pos = new Vector3(x, y, z); Rot = new Vector3(pitch, yaw, roll); Item = item;
            }
            public Quaternion Q => Quaternion.Euler(Rot);
            /// <summary>This key moved and turned a little.</summary>
            public Key Off(Vector3 move, Vector3 turn, float item) => new Key { Pos = Pos + move, Rot = Rot + turn, Item = item };
        }

        /// <summary>Part way from one key to another (t may go a little under 0 or over 1: a move swinging past).</summary>
        static Key Mix(Key a, Key b, float t)
            => new Key { Pos = Vector3.LerpUnclamped(a.Pos, b.Pos, t), Rot = Vector3.LerpUnclamped(a.Rot, b.Rot, t), Item = Mathf.LerpUnclamped(a.Item, b.Item, t) };

        /// <summary>The same, the hand travelling round by way of `via` (a curve, not a straight line).</summary>
        static Key Arc(Key a, Key b, Vector3 via, float t)
        {
            var k = Mix(a, b, t);
            k.Pos = Curve(a.Pos, via, b.Pos, t);
            return k;
        }

        static Vector3 Curve(Vector3 a, Vector3 via, Vector3 b, float t)
        {
            float u = 1f - t;
            return a * (u * u) + via * (2f * u * t) + b * (t * t);
        }

        /// <summary>Fast away, slowing to a stop.</summary>
        static float EaseOut(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        /// <summary>Slow away, faster and faster to the end (a strike).</summary>
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t * (0.35f + 0.65f * t); }
        /// <summary>Eases away (leaning back a touch first), swings a little past the end and settles on it.</summary>
        static float InOutBack(float t)
        {
            const float c = 1.15f * 1.525f;
            t = Mathf.Clamp01(t);
            if (t < 0.5f) { float a = 2f * t; return a * a * ((c + 1f) * a - c) * 0.5f; }
            float b = 2f * t - 2f;
            return (b * b * ((c + 1f) * b + c) + 2f) * 0.5f;
        }

        /// <summary>The way home after a swing, like something on a spring: it starts gently, is there by two fifths of
        /// the time, swings a few percent past and settles.</summary>
        static float Settle(float t)
        {
            if (t >= 1f) return 1f;
            if (t <= 0f) return 0f;
            return 1f - Mathf.Exp(-6f * t) * (Mathf.Cos(6f * t) + Mathf.Sin(6f * t));
        }

        /// <summary>Something with weight following a target: how fast it swings (freq, radians a second) and how soon it
        /// settles (damp: 1 = no swing past at all, 0.4 = a couple of swings).</summary>
        struct Spring
        {
            public float X, V;
            public void Step(float target, float freq, float damp, float dt)
            {
                if (dt <= 0f) return;
                int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.006f), 1, 12);
                float h = Mathf.Min(dt, 0.08f) / n, k = freq * freq, c = 2f * damp * freq;
                for (int i = 0; i < n; i++) { V += ((target - X) * k - V * c) * h; X += V * h; }
            }
        }
    }
}
