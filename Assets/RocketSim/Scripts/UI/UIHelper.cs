// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/UIHelper.cs
// Purpose: Builds the panel's repeated labels, sliders, toggles, buttons, and
// foldouts with one consistent visual structure and callback pattern.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace RocketSim
{
    public static class UIHelper
    {
        static VisualElement _focusedTextInput;

        /// <summary>
        /// True while one of the panel's editable text fields owns keyboard focus.
        /// Camera and scene shortcuts use this to avoid reacting to text entry.
        /// </summary>
        public static bool IsTextInputFocused
        {
            get
            {
                if (_focusedTextInput != null && _focusedTextInput.panel != null)
                {
                    VisualElement focusedElement =
                        _focusedTextInput.focusController?.focusedElement as VisualElement;
                    if (ReferenceEquals(focusedElement, _focusedTextInput) ||
                        (focusedElement != null && _focusedTextInput.Contains(focusedElement)))
                        return true;
                }

                _focusedTextInput = null;
                return false;
            }
        }

        /// <summary>
        /// Registers a text editor with the shared keyboard-focus guard. The detach
        /// callback prevents a rebuilt panel from leaving scene shortcuts disabled.
        /// </summary>
        public static void TrackTextInputFocus(TextField field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            field.RegisterCallback<FocusInEvent>(_ => _focusedTextInput = field);
            field.RegisterCallback<FocusOutEvent>(_ => ClearTrackedTextInput(field));
            field.RegisterCallback<DetachFromPanelEvent>(_ => ClearTrackedTextInput(field));
        }

        static void ClearTrackedTextInput(VisualElement field)
        {
            if (ReferenceEquals(_focusedTextInput, field))
                _focusedTextInput = null;
        }

        /// <summary>Defines how slider position maps to the represented numeric value.</summary>
        public enum NumericSliderScale
        {
            Linear,
            Logarithmic,
            Quadratic
        }

        /// <summary>Controls the semantic sign badge and its styling hook.</summary>
        public enum NumericSliderSign
        {
            Neutral,
            Positive,
            Negative
        }

        /// <summary>Provides a stable styling hook for different parameter families.</summary>
        public enum NumericSliderCategory
        {
            Default,
            Rate,
            Event,
            Terminal,
            Threshold
        }

        /// <summary>
        /// Describes a keyboard-editable numeric slider. The represented value is always
        /// expressed in model units; the slider's normalized position is an implementation
        /// detail and is never exposed to callers.
        /// </summary>
        public sealed class NumericSliderFieldOptions
        {
            public NumericSliderFieldOptions(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            /// <summary>Smallest value represented by the slider.</summary>
            public float Minimum { get; }

            /// <summary>Largest value represented by the slider.</summary>
            public float Maximum { get; }

            /// <summary>
            /// Optional lower keyboard-input limit. NaN uses Minimum. A hard limit may
            /// extend beyond the slider range; the thumb then pins at its nearest end.
            /// </summary>
            public float HardMinimum { get; set; } = float.NaN;

            /// <summary>
            /// Optional upper keyboard-input limit. NaN uses Maximum. This lets expert
            /// users type an exact outlier without making normal slider motion unusable.
            /// </summary>
            public float HardMaximum { get; set; } = float.NaN;

            /// <summary>
            /// Optional committed-value increment. Zero keeps continuous values; one
            /// turns the shared control into a keyboard-editable integer slider.
            /// </summary>
            public float ValueStep { get; set; }

            /// <summary>Linear by default; logarithmic is intended for multi-order ranges.</summary>
            public NumericSliderScale Scale { get; set; } = NumericSliderScale.Linear;

            /// <summary>
            /// Lowest non-zero value represented by a logarithmic slider. When left unset,
            /// it defaults to one ten-thousandth of Maximum. It is ignored for linear rows.
            /// </summary>
            public float SmallestPositiveValue { get; set; } = float.NaN;

            /// <summary>
            /// Fraction of a zero-inclusive logarithmic slider reserved for exact zero.
            /// Values in that left-hand detent snap to zero because log(0) is undefined.
            /// </summary>
            public float ZeroDetentFraction { get; set; } = 0.08f;

            /// <summary>Digits displayed after the decimal point in the keyboard field.</summary>
            public int Decimals { get; set; } = 3;

            /// <summary>Unit printed beside the keyboard field, for example "/s" or "m/s".</summary>
            public string Unit { get; set; } = string.Empty;

            /// <summary>Semantic sign shown at the start of the row.</summary>
            public NumericSliderSign Sign { get; set; } = NumericSliderSign.Neutral;

            /// <summary>Semantic family exposed as a USS class on the row container.</summary>
            public NumericSliderCategory Category { get; set; } = NumericSliderCategory.Default;

            /// <summary>
            /// Value restored by the optional Reset button. Leave null to omit that button.
            /// </summary>
            public float? ResetValue { get; set; }
        }

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

        // -- Keyboard-editable numeric slider --------------------------------------
        /// <summary>
        /// Creates a slider paired with a delayed <see cref="TextField"/>. Users can
        /// drag for exploration or type an exact decimal/scientific-notation value and
        /// commit it with Enter, Tab, or focus loss. Both inputs share one clamped value,
        /// and programmatic synchronization never produces a second change callback.
        /// </summary>
        /// <param name="label">Plain-language parameter name.</param>
        /// <param name="value">Initial value in model units.</param>
        /// <param name="options">Range, mapping, formatting, unit, and styling metadata.</param>
        /// <param name="onChange">Called once whenever the committed value changes.</param>
        /// <param name="description">Explanation displayed directly beneath the row.</param>
        /// <param name="onReset">
        /// Optional notification called after ResetValue has been committed. Use it for
        /// preset bookkeeping; the normal onChange callback is also invoked.
        /// </param>
        public static VisualElement NumericSliderField(
            string label,
            float value,
            NumericSliderFieldOptions options,
            Action<float> onChange,
            string description,
            Action onReset = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            ValidateNumericSliderOptions(options);

            float minimum = options.Minimum;
            float maximum = options.Maximum;
            float hardMinimum = IsFinite(options.HardMinimum) ? options.HardMinimum : minimum;
            float hardMaximum = IsFinite(options.HardMaximum) ? options.HardMaximum : maximum;
            float valueStep = options.ValueStep;
            NumericSliderScale scale = options.Scale;
            bool logarithmic = scale == NumericSliderScale.Logarithmic;
            bool hasZeroDetent = logarithmic && minimum == 0f;
            float positiveMinimum = ResolvePositiveMinimum(options);
            float zeroDetent = hasZeroDetent
                ? Clamp(options.ZeroDetentFraction, 0.02f, 0.40f)
                : 0f;
            int decimals = Math.Max(0, Math.Min(8, options.Decimals));
            string unit = options.Unit ?? string.Empty;
            float currentValue = ClampNumericValue(
                value,
                hardMinimum,
                hardMaximum,
                valueStep);

            var row = Row();
            row.AddToClassList("rs-numeric-slider-row");

            var sign = new Label(NumericSignText(options.Sign));
            sign.AddToClassList("rs-numeric-sign");
            sign.AddToClassList(NumericSignClass(options.Sign));

            var name = Lbl(label, "rs-field-label");
            name.AddToClassList("rs-numeric-slider-label");

            // A normalized slider keeps all mapping rules in this helper and prevents
            // logarithmic implementation details from leaking into configuration code.
            var slider = new Slider(0f, 1f);
            slider.AddToClassList("rs-slider");
            slider.AddToClassList("rs-numeric-slider");

            // TextField keeps partial decimal/scientific input intact until commit;
            // parsing here also accepts both the current locale and invariant notation.
            var field = new TextField { isDelayed = true };
            field.AddToClassList("rs-numeric-input");
            field.focusable = true;
            field.pickingMode = PickingMode.Position;
            field.textSelection.selectAllOnFocus = false;
            field.textSelection.selectAllOnMouseUp = false;
            field.tooltip =
                $"Type an exact value from {hardMinimum:G6} to {hardMaximum:G6}. " +
                "Decimal and scientific notation are accepted.";

            // Unity renders TextField content in a nested input element. Keep every
            // layer pickable so the visible box consistently accepts mouse focus.
            var fieldInput = field.Q<VisualElement>("unity-text-input");
            if (fieldInput != null)
            {
                fieldInput.focusable = true;
                fieldInput.pickingMode = PickingMode.Position;
            }
            TrackTextInputFocus(field);

            var unitLabel = new Label(unit);
            unitLabel.AddToClassList("rs-numeric-unit");
            unitLabel.EnableInClassList("rs-numeric-unit-empty", string.IsNullOrWhiteSpace(unit));

            slider.SetValueWithoutNotify(NumericValueToSliderPosition(
                currentValue,
                minimum,
                maximum,
                scale,
                hasZeroDetent,
                positiveMinimum,
                zeroDetent));
            string initialText = FormatNumericInput(currentValue, decimals);
            field.SetValueWithoutNotify(initialText);

            var group = WithDescription(row, description);
            group.AddToClassList("rs-numeric-slider-control");
            group.AddToClassList(NumericCategoryClass(options.Category));
            group.tooltip = description;

            Action<float, bool> commit = (requestedValue, correctedInput) =>
            {
                float nextValue = ClampNumericValue(
                    requestedValue,
                    hardMinimum,
                    hardMaximum,
                    valueStep);

                bool changed = nextValue != currentValue;
                currentValue = nextValue;
                slider.SetValueWithoutNotify(NumericValueToSliderPosition(
                    currentValue,
                    minimum,
                    maximum,
                    scale,
                    hasZeroDetent,
                    positiveMinimum,
                    zeroDetent));
                string formattedValue = FormatNumericInput(currentValue, decimals);
                field.SetValueWithoutNotify(formattedValue);
                field.EnableInClassList("rs-numeric-input-corrected", correctedInput);
                if (changed) onChange?.Invoke(currentValue);
            };

            slider.RegisterValueChangedCallback(evt =>
            {
                float nextValue = SliderPositionToNumericValue(
                    evt.newValue,
                    minimum,
                    maximum,
                    scale,
                    hasZeroDetent,
                    positiveMinimum,
                    zeroDetent);
                commit(nextValue, false);
            });

            field.RegisterValueChangedCallback(evt =>
            {
                if (!TryParseNumericInput(evt.newValue, out float parsedValue))
                {
                    field.EnableInClassList("rs-numeric-input-corrected", true);
                    string formattedValue = FormatNumericInput(currentValue, decimals);
                    field.SetValueWithoutNotify(formattedValue);
                    return;
                }

                bool corrected = !IsFinite(parsedValue)
                    || parsedValue < hardMinimum
                    || parsedValue > hardMaximum;
                commit(parsedValue, corrected);
            });

            // TextField keeps unparseable text until focus changes. Reapplying the last
            // valid model value guarantees invalid partial input never remains visible.
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                string formattedValue = FormatNumericInput(currentValue, decimals);
                field.SetValueWithoutNotify(formattedValue);
            });

            row.Add(sign);
            row.Add(name);
            row.Add(slider);
            row.Add(field);
            row.Add(unitLabel);

            if (options.ResetValue.HasValue)
            {
                float resetValue = options.ResetValue.Value;
                var reset = new Button(() =>
                {
                    bool corrected = !IsFinite(resetValue)
                        || resetValue < hardMinimum
                        || resetValue > hardMaximum;
                    commit(resetValue, corrected);
                    onReset?.Invoke();
                })
                {
                    text = "Reset",
                    tooltip = "Restore this parameter to the selected preset value."
                };
                reset.AddToClassList("rs-numeric-reset");
                row.Add(reset);
            }

            return group;
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

        /// <summary>Formats editable values compactly without hiding small magnitudes.</summary>
        static string FormatNumericInput(float value, int decimals)
        {
            int significantDigits = Math.Max(1, Math.Min(9, decimals + 1));
            return value.ToString($"G{significantDigits}", CultureInfo.InvariantCulture);
        }

        /// <summary>Accepts the user's locale as well as invariant decimal/scientific input.</summary>
        static bool TryParseNumericInput(string text, out float value)
        {
            const NumberStyles styles = NumberStyles.Float;
            return float.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
                || float.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Rejects invalid descriptor ranges early, where they are easy to fix.</summary>
        static void ValidateNumericSliderOptions(NumericSliderFieldOptions options)
        {
            if (!IsFinite(options.Minimum) || !IsFinite(options.Maximum) || options.Maximum <= options.Minimum)
                throw new ArgumentOutOfRangeException(nameof(options), "Numeric slider Maximum must be finite and greater than Minimum.");

            if (float.IsInfinity(options.HardMinimum) || float.IsInfinity(options.HardMaximum))
                throw new ArgumentOutOfRangeException(nameof(options), "Numeric slider hard limits cannot be infinite.");

            if (!IsFinite(options.ValueStep) || options.ValueStep < 0f)
                throw new ArgumentOutOfRangeException(nameof(options), "ValueStep must be finite and nonnegative.");

            float hardMinimum = IsFinite(options.HardMinimum) ? options.HardMinimum : options.Minimum;
            float hardMaximum = IsFinite(options.HardMaximum) ? options.HardMaximum : options.Maximum;
            if (hardMinimum > options.Minimum || hardMaximum < options.Maximum || hardMaximum <= hardMinimum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "HardMinimum/HardMaximum must contain the complete slider range.");
            }

            if (options.Scale != NumericSliderScale.Logarithmic) return;

            if (hardMinimum < 0f || options.Minimum < 0f || options.Maximum <= 0f)
                throw new ArgumentOutOfRangeException(nameof(options), "Logarithmic sliders require nonnegative hard/slider minima and Maximum > 0.");

            if (IsFinite(options.SmallestPositiveValue)
                && (options.SmallestPositiveValue <= 0f || options.SmallestPositiveValue > options.Maximum))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "SmallestPositiveValue must be positive and no larger than Maximum.");
            }

            if (float.IsInfinity(options.SmallestPositiveValue))
                throw new ArgumentOutOfRangeException(nameof(options), "SmallestPositiveValue cannot be infinite.");

            if (options.Minimum == 0f && !IsFinite(options.ZeroDetentFraction))
                throw new ArgumentOutOfRangeException(nameof(options), "ZeroDetentFraction must be finite.");
        }

        /// <summary>Finds the positive floor used by logarithmic sliders.</summary>
        static float ResolvePositiveMinimum(NumericSliderFieldOptions options)
        {
            if (options.Scale != NumericSliderScale.Logarithmic) return options.Minimum;
            if (options.Minimum > 0f) return options.Minimum;
            if (IsFinite(options.SmallestPositiveValue)) return options.SmallestPositiveValue;
            return Math.Max(options.Maximum * 0.0001f, float.Epsilon);
        }

        /// <summary>Clamps model input to the keyboard range, including NaN/infinity.</summary>
        static float ClampNumericValue(
            float value,
            float minimum,
            float maximum,
            float valueStep)
        {
            if (!IsFinite(value)) return minimum;
            float clamped = Clamp(value, minimum, maximum);
            if (valueStep <= 0f) return clamped;
            double steps = Math.Round((clamped - minimum) / valueStep, MidpointRounding.AwayFromZero);
            return Clamp((float)(minimum + steps * valueStep), minimum, maximum);
        }

        /// <summary>Maps a model-space value onto the slider's normalized 0..1 range.</summary>
        static float NumericValueToSliderPosition(
            float value,
            float minimum,
            float maximum,
            NumericSliderScale scale,
            bool hasZeroDetent,
            float positiveMinimum,
            float zeroDetent)
        {
            float linearPosition = Clamp((value - minimum) / (maximum - minimum), 0f, 1f);
            if (scale == NumericSliderScale.Linear) return linearPosition;
            if (scale == NumericSliderScale.Quadratic) return (float)Math.Sqrt(linearPosition);

            if (hasZeroDetent && value <= 0f) return 0f;
            double logMin = Math.Log10(positiveMinimum);
            double logMax = Math.Log10(maximum);
            float logarithmicPosition = Math.Abs(logMax - logMin) < double.Epsilon
                ? 1f
                : (float)((Math.Log10(Math.Max(value, positiveMinimum)) - logMin) / (logMax - logMin));
            return hasZeroDetent
                ? zeroDetent + (1f - zeroDetent) * Clamp(logarithmicPosition, 0f, 1f)
                : Clamp(logarithmicPosition, 0f, 1f);
        }

        /// <summary>Maps a normalized slider position back into model units.</summary>
        static float SliderPositionToNumericValue(
            float position,
            float minimum,
            float maximum,
            NumericSliderScale scale,
            bool hasZeroDetent,
            float positiveMinimum,
            float zeroDetent)
        {
            position = Clamp(position, 0f, 1f);
            if (scale == NumericSliderScale.Linear)
                return minimum + (maximum - minimum) * position;
            if (scale == NumericSliderScale.Quadratic)
                return minimum + (maximum - minimum) * position * position;
            if (hasZeroDetent && position < zeroDetent) return 0f;

            float logarithmicPosition = hasZeroDetent
                ? (position - zeroDetent) / (1f - zeroDetent)
                : position;
            double logMin = Math.Log10(positiveMinimum);
            double logMax = Math.Log10(maximum);
            return (float)Math.Pow(10d, logMin + (logMax - logMin) * logarithmicPosition);
        }

        static string NumericSignText(NumericSliderSign sign)
        {
            switch (sign)
            {
                case NumericSliderSign.Positive: return "+";
                case NumericSliderSign.Negative: return "-";
                default: return string.Empty;
            }
        }

        static string NumericSignClass(NumericSliderSign sign)
        {
            switch (sign)
            {
                case NumericSliderSign.Positive: return "rs-numeric-sign-positive";
                case NumericSliderSign.Negative: return "rs-numeric-sign-negative";
                default: return "rs-numeric-sign-neutral";
            }
        }

        static string NumericCategoryClass(NumericSliderCategory category)
        {
            switch (category)
            {
                case NumericSliderCategory.Rate: return "rs-numeric-category-rate";
                case NumericSliderCategory.Event: return "rs-numeric-category-event";
                case NumericSliderCategory.Terminal: return "rs-numeric-category-terminal";
                case NumericSliderCategory.Threshold: return "rs-numeric-category-threshold";
                default: return "rs-numeric-category-default";
            }
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
