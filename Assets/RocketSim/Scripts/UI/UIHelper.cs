// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/UIHelper.cs
// Purpose: Builds the panel's repeated labels, sliders, toggles, buttons, and
// foldouts with one consistent visual structure and callback pattern.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine.UIElements;

namespace RocketSim
{
    public static class UIHelper
    {
        // ── Section headers ───────────────────────────────────────────────────
        /// <summary>Creates the small uppercase heading used to separate control groups.</summary>
        public static Label SectionLabel(string text)
        {
            var l = new Label(text.ToUpper());
            l.AddToClassList("rs-section-label");
            return l;
        }

        // ── Float slider ──────────────────────────────────────────────────────
        /// <summary>
        /// Creates a floating-point slider row, formatted value readout, and the
        /// required plain-language description shown underneath it.
        /// </summary>
        public static VisualElement Slider(
            string label,
            float value,
            float min,
            float max,
            Action<float> onChange,
            string description,
            int decimals = 1,
            string suffix = "",
            float valueScale = 1f)
        {
            var row    = Row();
            var lbl    = Lbl(label, "rs-field-label");
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("rs-slider");
            var val = new Label(FormatNumber(value * valueScale, decimals, suffix));
            val.AddToClassList("rs-field-value");

            slider.RegisterValueChangedCallback(e => {
                val.text = FormatNumber(e.newValue * valueScale, decimals, suffix);
                onChange?.Invoke(e.newValue);
            });

            row.Add(lbl);
            row.Add(slider);
            row.Add(val);
            return WithDescription(row, description);
        }

        // ── Integer slider ────────────────────────────────────────────────────
        /// <summary>
        /// Creates an integer slider row with a live value and required explanation.
        /// </summary>
        public static VisualElement IntSlider(
            string label,
            int value,
            int min,
            int max,
            Action<int> onChange,
            string description)
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

            row.Add(lbl);
            row.Add(slider);
            row.Add(val);
            return WithDescription(row, description);
        }

        // ── Scientific-notation float slider (learning rates, etc.) ──────────
        /// <summary>
        /// Creates a logarithmic slider for values spanning orders of magnitude,
        /// such as learning rates, while passing the real value to the callback.
        /// </summary>
        public static VisualElement ScientificSlider(
            string label,
            float value,
            float min,
            float max,
            Action<float> onChange,
            string description)
        {
            var row    = Row();
            var lbl    = Lbl(label, "rs-field-label");
            float minPower = (float)Math.Log10(Math.Max(min, float.Epsilon));
            float maxPower = (float)Math.Log10(Math.Max(max, float.Epsilon));
            float valuePower = (float)Math.Log10(Math.Max(value, float.Epsilon));
            var slider = new Slider(minPower, maxPower) { value = valuePower };
            slider.AddToClassList("rs-slider");
            var val = new Label(FormatSci(value));
            val.AddToClassList("rs-field-value");

            slider.RegisterValueChangedCallback(e => {
                float nextValue = (float)Math.Pow(10d, e.newValue);
                val.text = FormatSci(nextValue);
                onChange?.Invoke(nextValue);
            });

            row.Add(lbl);
            row.Add(slider);
            row.Add(val);
            return WithDescription(row, description);
        }

        // ── Bool toggle ───────────────────────────────────────────────────────
        /// <summary>Creates a labeled Boolean toggle using the shared field-row layout.</summary>
        public static VisualElement Toggle(string label, bool value, Action<bool> onChange)
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
        /// <summary>Creates a labeled row for a derived value that the user cannot edit.</summary>
        public static VisualElement ReadOnly(string label, string text)
        {
            var row = Row();
            row.Add(Lbl(label, "rs-field-label"));
            row.Add(Lbl(text,  "rs-field-readonly"));
            return row;
        }

        // ── Buttons ───────────────────────────────────────────────────────────
        /// <summary>Creates the normal primary-action button used throughout the panel.</summary>
        public static Button ActionButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("rs-action-btn");
            return b;
        }

        /// <summary>
        /// Creates a button styled for destructive or stop actions.
        /// </summary>
        public static Button DangerButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("rs-danger-btn");
            return b;
        }

        /// <summary>
        /// Creates a consistently styled collapsible group for long tabs.
        /// </summary>
        public static Foldout Foldout(string title, bool expanded = false)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList("rs-foldout");
            SetFoldoutMuted(foldout, false);
            foldout.RegisterCallback<AttachToPanelEvent>(_ => SetFoldoutMuted(
                foldout,
                foldout.ClassListContains("rs-foldout-hardware-disabled")));
            foldout.RegisterValueChangedCallback(_ => foldout.schedule.Execute(() => SetFoldoutMuted(
                foldout,
                foldout.ClassListContains("rs-foldout-hardware-disabled"))));
            return foldout;
        }

        /// <summary>
        /// Applies the foldout title color inline so Unity's checked-state theme
        /// cannot darken the title when the group is opened.
        /// </summary>
        public static void SetFoldoutMuted(Foldout foldout, bool muted)
        {
            if (foldout == null) return;
            var title = foldout.Q<Label>(className: "unity-foldout__text") ?? foldout.Q<Label>();
            if (title == null) return;
            title.style.opacity = 1f;
            title.style.color = muted
                ? new StyleColor(new UnityEngine.Color(0.32f, 0.40f, 0.50f))
                : new StyleColor(new UnityEngine.Color(0.67f, 0.79f, 0.92f));

            var toggle = foldout.Q<Toggle>(className: "unity-foldout__toggle");
            if (toggle != null)
            {
                toggle.style.opacity = 1f;
                toggle.style.color = title.style.color;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────
        /// <summary>Creates the common horizontal container for a label and control.</summary>
        static VisualElement Row()
        {
            var v = new VisualElement();
            v.AddToClassList("rs-field-row");
            return v;
        }

        /// <summary>
        /// Keeps a control and its plain-language explanation together. Requiring
        /// the description in every slider call prevents new unexplained sliders.
        /// </summary>
        static VisualElement WithDescription(VisualElement control, string description)
        {
            var group = new VisualElement();
            group.AddToClassList("rs-described-control");
            group.Add(control);

            var help = new Label(description);
            help.AddToClassList("rs-control-description");
            group.Add(help);
            return group;
        }

        /// <summary>
        /// Creates a label with the requested USS class for reused field rows.
        /// </summary>
        static Label Lbl(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        /// <summary>
        /// Formats small ML-Agents numeric values compactly for slider readouts.
        /// </summary>
        static string FormatSci(float v)
        {
            if (v == 0f) return "0";
            return Math.Abs(v) < 0.001f ? v.ToString("G2") : v.ToString("G3");
        }

        /// <summary>
        /// Formats a slider readout with a bounded decimal count and optional unit suffix.
        /// </summary>
        static string FormatNumber(float value, int decimals, string suffix)
        {
            decimals = Math.Max(0, Math.Min(6, decimals));
            return value.ToString($"F{decimals}") + suffix;
        }
    }
}
