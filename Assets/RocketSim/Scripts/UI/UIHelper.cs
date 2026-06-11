using System;
using UnityEngine.UIElements;

namespace RocketSim
{
    public static class UIHelper
    {
        // ── Section headers ───────────────────────────────────────────────────
        public static Label SectionLabel(string text)
        {
            var l = new Label(text.ToUpper());
            l.AddToClassList("rs-section-label");
            return l;
        }

        // ── Float slider ──────────────────────────────────────────────────────
        public static VisualElement Slider(string label, float value,
                                           float min, float max,
                                           Action<float> onChange)
        {
            var row    = Row();
            var lbl    = Lbl(label, "rs-field-label");
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("rs-slider");
            var val = new Label(value.ToString("F1"));
            val.AddToClassList("rs-field-value");

            slider.RegisterValueChangedCallback(e => {
                val.text = e.newValue.ToString("F1");
                onChange?.Invoke(e.newValue);
            });

            row.Add(lbl); row.Add(slider); row.Add(val);
            return row;
        }

        // ── Integer slider ────────────────────────────────────────────────────
        public static VisualElement IntSlider(string label, int value,
                                              int min, int max,
                                              Action<int> onChange)
        {
            var row    = Row();
            var lbl    = Lbl(label, "rs-field-label");
            var slider = new SliderInt(min, max) { value = value };
            slider.AddToClassList("rs-slider");
            var val = new Label(value.ToString());
            val.AddToClassList("rs-field-value");

            slider.RegisterValueChangedCallback(e => {
                val.text = e.newValue.ToString();
                onChange?.Invoke(e.newValue);
            });

            row.Add(lbl); row.Add(slider); row.Add(val);
            return row;
        }

        // ── Scientific-notation float slider (learning rates, etc.) ──────────
        public static VisualElement ScientificSlider(string label, float value,
                                                     float min, float max,
                                                     Action<float> onChange)
        {
            var row    = Row();
            var lbl    = Lbl(label, "rs-field-label");
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("rs-slider");
            var val = new Label(FormatSci(value));
            val.AddToClassList("rs-field-value");

            slider.RegisterValueChangedCallback(e => {
                val.text = FormatSci(e.newValue);
                onChange?.Invoke(e.newValue);
            });

            row.Add(lbl); row.Add(slider); row.Add(val);
            return row;
        }

        // ── Bool toggle ───────────────────────────────────────────────────────
        public static VisualElement Toggle(string label, bool value,
                                           Action<bool> onChange)
        {
            var row = Row();
            row.Add(Lbl(label, "rs-field-label"));
            var t = new Toggle { value = value };
            t.AddToClassList("rs-toggle");
            t.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            row.Add(t);
            return row;
        }

        // ── Read-only computed display ────────────────────────────────────────
        public static VisualElement ReadOnly(string label, string text)
        {
            var row = Row();
            row.Add(Lbl(label, "rs-field-label"));
            row.Add(Lbl(text,  "rs-field-readonly"));
            return row;
        }

        // ── Buttons ───────────────────────────────────────────────────────────
        public static Button ActionButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("rs-action-btn");
            return b;
        }

        public static Button DangerButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("rs-danger-btn");
            return b;
        }

        // ── Private helpers ───────────────────────────────────────────────────
        static VisualElement Row()
        {
            var v = new VisualElement();
            v.AddToClassList("rs-field-row");
            return v;
        }

        static Label Lbl(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        static string FormatSci(float v)
        {
            if (v == 0f) return "0";
            return System.Math.Abs(v) < 0.001f ? v.ToString("G2") : v.ToString("G3");
        }
    }
}