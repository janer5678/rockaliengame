using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Menu widgets: buttons, choice buttons, sliders and fold-open section headers, with hover / click / slider sounds.</summary>
    public partial class Hud
    {
        GUIStyle m_SmallWrap, m_SmallNoClip, m_SlotKey, m_LabelWrap, m_Header, m_Caption, m_KeyCell, m_Primary, m_Choice;

        /// <summary>The extra menu styles (made from the normal ones in Styles()).</summary>
        void UiStyles()
        {
            float k = m_Scale;
            m_Button.richText = true;
            m_SmallWrap = new GUIStyle(m_Small) { wordWrap = true };
            m_SmallNoClip = new GUIStyle(m_Small) { clipping = TextClipping.Overflow, wordWrap = false };
            m_SlotKey = new GUIStyle(m_SmallNoClip) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(15 * k) }; // hotbar key numbers
            m_LabelWrap = new GUIStyle(m_Label) { wordWrap = true };
            m_Caption = new GUIStyle(m_Small) { fontStyle = FontStyle.Bold };
            m_Caption.normal.textColor = new Color(1f, 0.82f, 0.48f);
            m_Header = new GUIStyle(m_Button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 4, 4) };
            m_KeyCell = new GUIStyle(m_Button) { fontSize = Mathf.RoundToInt(14 * k), fontStyle = FontStyle.Bold };
            m_Primary = new GUIStyle(m_Button) { fontSize = Mathf.RoundToInt(19 * k), fontStyle = FontStyle.Bold };
            // a picked option looks pressed in (the darker "held down" background) with gold text, so it's obvious which one is on
            m_Choice = new GUIStyle(m_Button);
            m_Choice.onNormal.background = m_Choice.onHover.background = m_Choice.onActive.background = m_Choice.onFocused.background = m_Button.active.background;
            m_Choice.onNormal.textColor = m_Choice.onHover.textColor = m_Choice.onActive.textColor = m_Choice.onFocused.textColor = new Color(1f, 0.82f, 0.35f);
            m_Choice.fontStyle = FontStyle.Normal;
            m_KeyCell.onNormal.background = m_KeyCell.onHover.background = m_Button.active.background;
        }

        // ------------------------------------------------------------------ hover / click / slider sounds

        int m_HoverKey, m_LastHoverKey;
        float m_LastHoverSound, m_LastSlideSound;

        /// <summary>Remembers which control the mouse is over this frame (the hover blip plays when it changes).</summary>
        void TrackHover(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.Repaint || !GUI.enabled || !r.Contains(e.mousePosition)) return;
            var p = GUIUtility.GUIToScreenPoint(r.position);
            m_HoverKey = (Mathf.RoundToInt(p.x) * 73856093) ^ (Mathf.RoundToInt(p.y) * 19349663) ^ (Mathf.RoundToInt(r.width) * 83492791) | 1;
        }

        void BeginHoverFrame()
        {
            if (Event.current.type == EventType.Repaint) m_HoverKey = 0;
        }

        void EndHoverFrame()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (m_HoverKey != 0 && m_HoverKey != m_LastHoverKey && Time.unscaledTime - m_LastHoverSound > 0.04f)
            {
                m_LastHoverSound = Time.unscaledTime;
                Sfx.PlayUi(Sfx.UiHover, 0.55f);
            }
            m_LastHoverKey = m_HoverKey;
        }

        static void ClickSound() => Sfx.PlayUi(Sfx.UiClick, 0.6f);

        bool Btn(string text, GUIStyle st, params GUILayoutOption[] o)
        {
            bool r = GUILayout.Button(text, st, o);
            TrackHover(GUILayoutUtility.GetLastRect());
            if (r) ClickSound();
            return r;
        }

        bool Btn(string text, params GUILayoutOption[] o) => Btn(text, m_Button, o);

        /// <summary>A button at a fixed rect (the only clickable thing on that rect).</summary>
        bool BtnAt(Rect rect, string text, GUIStyle st)
        {
            bool r = GUI.Button(rect, text, st);
            TrackHover(rect);
            if (r) ClickSound();
            return r;
        }

        /// <summary>One of a set of options (shown pressed while it's the selected one). True when it gets picked.</summary>
        bool Choice(bool on, string text, params GUILayoutOption[] o)
        {
            bool r = GUILayout.Toggle(on, text, m_Choice, o);
            TrackHover(GUILayoutUtility.GetLastRect());
            if (r != on) ClickSound();
            return r && !on;
        }

        /// <summary>An on/off button (pressed = on). Returns the new value.</summary>
        bool ToggleBtn(bool on, string text, params GUILayoutOption[] o)
        {
            bool r = GUILayout.Toggle(on, text, m_Choice, o);
            TrackHover(GUILayoutUtility.GetLastRect());
            if (r != on) ClickSound();
            return r;
        }

        /// <summary>"Label  [----o----]  shown" on one row, ticking as it moves.</summary>
        float SliderRow(string label, float v, float min, float max, string shown, float labelW = 0f)
        {
            float k = m_Scale;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, m_Label, GUILayout.Width(labelW > 0 ? labelW : 190 * k));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(22 * k));
            TrackHover(GUILayoutUtility.GetLastRect());
            GUILayout.Label(shown, new GUIStyle(m_Label) { alignment = TextAnchor.UpperRight }, GUILayout.Width(72 * k));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(nv, v) && Time.unscaledTime - m_LastSlideSound > 0.035f)
            {
                m_LastSlideSound = Time.unscaledTime;
                Sfx.PlayUi(Sfx.UiSlide, 0.7f, 0.8f + 0.7f * Mathf.InverseLerp(min, max, nv));
            }
            return nv;
        }

        readonly HashSet<string> m_Open = new HashSet<string>();

        /// <summary>A fold-open section header (closed to start with). Returns whether it's open.</summary>
        bool Section(string key, string title, string extra = "")
        {
            bool open = m_Open.Contains(key);
            if (Btn((open ? "▼   " : "►   ") + title + extra, m_Header, GUILayout.Height(30 * m_Scale)))
            {
                if (open) m_Open.Remove(key); else m_Open.Add(key);
                open = !open;
            }
            return open;
        }

        void Caption(string text)
        {
            GUILayout.Space(6 * m_Scale);
            GUILayout.Label(text, m_Caption);
        }

        void RowLabel(string text, float w = 0f) => GUILayout.Label(text, m_Label, GUILayout.Width(w > 0 ? w : 100 * m_Scale), GUILayout.Height(30 * m_Scale));
    }
}
