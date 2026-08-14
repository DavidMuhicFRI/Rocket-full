// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Rewards.cs
// Purpose: Builds the complete, scenario-aware training objective editor from
// the same reward, shaping, and termination catalogs used by the runtime.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>
        /// Builds actual reward magnitudes, advanced shaping geometry, and
        /// scenario-specific episode endings. Zero-valued applicable signals
        /// remain visible so a user can enable them without changing code.
        /// </summary>
        void BuildRewardsTab(VisualElement root)
        {
            root.Clear();
            if (envConfig == null)
            {
                root.Add(BuildGroupDetail("The environment configuration is not connected."));
                return;
            }

            ScenarioType scenario = envConfig.scenario;
            ScenarioObjectiveConfig objective = envConfig.GetTrainingObjective(scenario);
            objective.EnsureObjects(scenario);
            ScenarioObjectiveConfig defaults = ScenarioObjectiveConfig.CreateDefault(scenario);

            root.Add(UIHelper.SectionLabel("Training Objective"));
            var scenarioLabel = new Label($"Editing: {ScenarioDisplayName(scenario)}");
            scenarioLabel.AddToClassList("rs-objective-scenario");
            root.Add(scenarioLabel);
            root.Add(BuildGroupDetail(
                "Values are absolute reward magnitudes, not multipliers. Green '+' rows add reward; " +
                "red '-' rows subtract a nonnegative cost. Enter 0 to disable any signal."));

            var validation = new Label();
            validation.AddToClassList("rs-objective-validation");
            root.Add(validation);

            BuildRewardPresetSection(root, scenario, objective);

            string basePreset = ValidBasePresetName(scenario, objective.basePresetName);
            RewardParameters presetValues = RewardPresetCatalog.CreateValues(scenario, basePreset);

            BuildRewardGroup(root, "Continuous rewards (+)", scenario, objective, presetValues,
                descriptor => descriptor.cadence == RewardCadence.PerSecond &&
                              descriptor.sign == RewardParameterSign.Reward,
                validation);
            BuildRewardGroup(root, "Continuous costs (-)", scenario, objective, presetValues,
                descriptor => descriptor.cadence == RewardCadence.PerSecond &&
                              descriptor.sign == RewardParameterSign.Cost,
                validation);
            BuildRewardGroup(root, "One-off events", scenario, objective, presetValues,
                descriptor => descriptor.cadence == RewardCadence.Event,
                validation);
            BuildRewardGroup(root, "Terminal outcomes", scenario, objective, presetValues,
                descriptor => descriptor.cadence == RewardCadence.Terminal,
                validation);

            BuildShapingSection(root, scenario, objective, defaults, validation);
            BuildTerminationSection(root, scenario, objective, defaults, validation);
            RefreshObjectiveValidation(validation, scenario, objective);
        }

        /// <summary>Builds named reward vectors and the full-objective reset action.</summary>
        void BuildRewardPresetSection(
            VisualElement root,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective)
        {
            root.Add(UIHelper.SectionLabel("Reward Preset"));

            var presetRow = new VisualElement();
            presetRow.AddToClassList("rs-reward-preset-row");
            foreach (string presetName in RewardPresetCatalog.ForScenario(scenario))
            {
                string capturedPreset = presetName;
                var button = new Button(() =>
                {
                    envConfig.ApplyRewardPreset(scenario, capturedPreset);
                    RefreshStartButton();
                    BuildRewardsTab(root);
                })
                {
                    text = capturedPreset,
                    userData = capturedPreset,
                    tooltip = "Replace every applicable reward magnitude with this complete preset vector."
                };
                button.AddToClassList("rs-reward-preset-btn");
                presetRow.Add(button);
            }
            root.Add(presetRow);

            var statusRow = new VisualElement();
            statusRow.AddToClassList("rs-objective-preset-status-row");
            var status = new Label();
            status.AddToClassList("rs-objective-preset-status");
            statusRow.Add(status);

            var resetAll = new Button(() =>
            {
                envConfig.EnsureTrainingObjective().ResetScenario(scenario);
                ApplyObjectiveDerivedState(scenario);
                BuildRewardsTab(root);
            })
            {
                text = "Reset full objective",
                tooltip = "Restore Balanced rewards plus default shaping and termination settings for this task."
            };
            resetAll.AddToClassList("rs-objective-reset-btn");
            statusRow.Add(resetAll);
            root.Add(statusRow);

            RefreshRewardPresetDisplay(presetRow, status, objective);
        }

        /// <summary>Builds one cadence/sign group directly from the reward catalog.</summary>
        void BuildRewardGroup(
            VisualElement root,
            string heading,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            RewardParameters presetValues,
            Func<RewardParameterDescriptor, bool> include,
            Label validation)
        {
            var descriptors = new List<RewardParameterDescriptor>();
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.ForScenario(scenario))
                if (include(descriptor)) descriptors.Add(descriptor);
            if (descriptors.Count == 0) return;

            root.Add(UIHelper.SectionLabel(heading));
            foreach (RewardParameterDescriptor descriptor in descriptors)
            {
                RewardParameterDescriptor captured = descriptor;
                float resetValue = captured.GetValue(presetValues);
                var options = RewardSliderOptions(captured, resetValue);
                root.Add(UIHelper.NumericSliderField(
                    captured.label,
                    captured.GetValue(objective.rewards),
                    options,
                    value =>
                    {
                        captured.SetValue(objective.rewards, value);
                        UpdateRewardPresetState(objective, scenario, presetValues);
                        RefreshRewardPresetControls(root, objective);
                        RefreshObjectiveValidation(validation, scenario, objective);
                        RefreshStartButton();
                    },
                    captured.description,
                    () => RefreshRewardPresetControls(root, objective)));
            }
        }

        /// <summary>Creates descriptor-driven formatting and slider behavior for a reward row.</summary>
        static UIHelper.NumericSliderFieldOptions RewardSliderOptions(
            RewardParameterDescriptor descriptor,
            float resetValue)
        {
            var options = new UIHelper.NumericSliderFieldOptions(
                descriptor.sliderMinimum,
                descriptor.sliderMaximum)
            {
                HardMinimum = descriptor.hardMinimum,
                HardMaximum = descriptor.hardMaximum,
                Scale = ToUiScale(descriptor.scaleMode),
                SmallestPositiveValue = SmallestPositiveSliderValue(descriptor.sliderMaximum),
                Decimals = descriptor.cadence == RewardCadence.PerSecond ? 6 :
                    descriptor.cadence == RewardCadence.Event ? 4 : 3,
                Unit = descriptor.unit,
                Sign = descriptor.sign == RewardParameterSign.Reward
                    ? UIHelper.NumericSliderSign.Positive
                    : UIHelper.NumericSliderSign.Negative,
                Category = descriptor.cadence == RewardCadence.PerSecond
                    ? UIHelper.NumericSliderCategory.Rate
                    : descriptor.cadence == RewardCadence.Event
                        ? UIHelper.NumericSliderCategory.Event
                        : UIHelper.NumericSliderCategory.Terminal,
                ResetValue = resetValue
            };
            return options;
        }

        /// <summary>
        /// Keeps the source preset highlighted only while the complete reward
        /// vector still matches it. The persistent base name remains available
        /// for every per-row Reset button after the vector becomes Custom.
        /// </summary>
        static void UpdateRewardPresetState(
            ScenarioObjectiveConfig objective,
            ScenarioType scenario,
            RewardParameters presetValues)
        {
            objective.MarkCustom();
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.ForScenario(scenario))
            {
                if (!ApproximatelyEqual(
                        descriptor.GetValue(objective.rewards),
                        descriptor.GetValue(presetValues)))
                    return;
            }
            objective.presetName = objective.basePresetName;
        }

        /// <summary>Updates preset buttons and status text without rebuilding sliders during a drag.</summary>
        static void RefreshRewardPresetControls(VisualElement tabRoot, ScenarioObjectiveConfig objective)
        {
            var row = tabRoot.Q<VisualElement>(className: "rs-reward-preset-row");
            var status = tabRoot.Q<Label>(className: "rs-objective-preset-status");
            RefreshRewardPresetDisplay(row, status, objective);
        }

        static void RefreshRewardPresetDisplay(
            VisualElement row,
            Label status,
            ScenarioObjectiveConfig objective)
        {
            if (row != null)
            {
                foreach (VisualElement child in row.Children())
                    if (child is Button { userData: string presetName } button)
                        button.EnableInClassList(
                            "rs-reward-preset-btn-active",
                            string.Equals(presetName, objective.presetName, StringComparison.Ordinal));
            }

            if (status != null)
            {
                status.text = objective.presetName == "Custom"
                    ? $"Custom (row resets use {objective.basePresetName})"
                    : $"Active: {objective.presetName}";
                status.EnableInClassList("rs-objective-preset-custom", objective.presetName == "Custom");
            }
        }

        /// <summary>Builds feature geometry separately because presets never change it.</summary>
        void BuildShapingSection(
            VisualElement root,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            ScenarioObjectiveConfig defaults,
            Label validation)
        {
            var foldout = UIHelper.Foldout("Advanced shaping scales and targets", false);
            var explanation = new Label(
                "These values change feature falloff, normalization, and target curves. " +
                "They do not change the maximum reward magnitude and are not modified by reward presets.");
            explanation.AddToClassList("rs-section-desc");
            foldout.Add(explanation);

            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.ForScenario(scenario))
            {
                ShapingParameterDescriptor captured = descriptor;
                float resetValue = captured.GetValue(defaults.shaping);
                var options = new UIHelper.NumericSliderFieldOptions(
                    captured.sliderMinimum,
                    captured.sliderMaximum)
                {
                    HardMinimum = captured.hardMinimum,
                    HardMaximum = captured.hardMaximum,
                    Scale = ToUiScale(captured.scaleMode),
                    SmallestPositiveValue = SmallestPositiveSliderValue(captured.sliderMaximum),
                    Decimals = DecimalPlacesForRange(captured.sliderMaximum),
                    Unit = captured.unit,
                    Sign = UIHelper.NumericSliderSign.Neutral,
                    Category = UIHelper.NumericSliderCategory.Threshold,
                    ResetValue = resetValue
                };
                foldout.Add(UIHelper.NumericSliderField(
                    captured.label,
                    captured.GetValue(objective.shaping),
                    options,
                    value =>
                    {
                        captured.SetValue(objective.shaping, value);
                        RefreshObjectiveValidation(validation, scenario, objective);
                        RefreshStartButton();
                    },
                    captured.description));
            }
            root.Add(foldout);
        }

        /// <summary>Builds only termination rules applicable to the selected scenario.</summary>
        void BuildTerminationSection(
            VisualElement root,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            ScenarioObjectiveConfig defaults,
            Label validation)
        {
            root.Add(UIHelper.SectionLabel("Termination Criteria"));
            string terminationHelp =
                "A disabled rule does not end the episode but keeps its thresholds. Initial and Full values are " +
                "interpolated by the episode's immutable curriculum difficulty.";
            if (scenario == ScenarioType.ChopstickLanding ||
                scenario == ScenarioType.LegLanding ||
                scenario == ScenarioType.HoverTracking)
                terminationHelp +=
                    " Success criteria also define the linked stable-capture, stable-touchdown, or target-capture event, " +
                    "so they remain active while that event or task mechanic is active.";
            root.Add(BuildGroupDetail(
                terminationHelp));

            var renderedThresholds = new HashSet<string>(StringComparer.Ordinal);
            BuildTerminationRuleGroup(root, "Success and task outcomes", scenario, objective, defaults,
                validation, renderedThresholds,
                kind => kind == TerminationRuleKind.Success || kind == TerminationRuleKind.SuccessOrFailure);
            BuildTerminationRuleGroup(root, "Physical and safety failures", scenario, objective, defaults,
                validation, renderedThresholds, kind => kind == TerminationRuleKind.Failure);
            BuildTerminationRuleGroup(root, "Episode limits", scenario, objective, defaults,
                validation, renderedThresholds, kind => kind == TerminationRuleKind.Limit);
        }

        void BuildTerminationRuleGroup(
            VisualElement root,
            string heading,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            ScenarioObjectiveConfig defaults,
            Label validation,
            HashSet<string> renderedThresholds,
            Func<TerminationRuleKind, bool> include)
        {
            var rules = new List<TerminationRuleDescriptor>();
            foreach (TerminationRuleDescriptor rule in TerminationRuleCatalog.ForScenario(scenario))
                if (include(rule.kind)) rules.Add(rule);
            if (rules.Count == 0) return;

            var groupHeading = new Label(heading.ToUpperInvariant());
            groupHeading.AddToClassList("rs-termination-group-heading");
            root.Add(groupHeading);

            foreach (TerminationRuleDescriptor rule in rules)
                root.Add(BuildTerminationRuleCard(
                    root, scenario, objective, defaults, validation, renderedThresholds, rule));
        }

        /// <summary>Builds one toggle, its unique thresholds, and linked terminal signals.</summary>
        VisualElement BuildTerminationRuleCard(
            VisualElement tabRoot,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            ScenarioObjectiveConfig defaults,
            Label validation,
            HashSet<string> renderedThresholds,
            TerminationRuleDescriptor rule)
        {
            var card = new VisualElement();
            card.AddToClassList("rs-termination-rule");
            card.AddToClassList(TerminationKindClass(rule.kind));

            var header = new VisualElement();
            header.AddToClassList("rs-termination-rule-header");
            var enabled = new Toggle(rule.label)
            {
                value = rule.GetEnabled(objective.terminations),
                tooltip = rule.description
            };
            enabled.AddToClassList("rs-termination-rule-toggle");
            header.Add(enabled);

            var kind = new Label(TerminationKindText(rule.kind));
            kind.AddToClassList("rs-termination-kind");
            header.Add(kind);

            var resetRule = new Button(() =>
            {
                ResetTerminationRule(rule, objective.terminations, defaults.terminations);
                ApplyObjectiveDerivedState(scenario);
                BuildRewardsTab(tabRoot);
            })
            {
                text = "Reset",
                tooltip = "Restore this rule and all of its thresholds to the task defaults."
            };
            resetRule.AddToClassList("rs-termination-reset");
            header.Add(resetRule);
            card.Add(header);

            var description = new Label(rule.description);
            description.AddToClassList("rs-termination-description");
            card.Add(description);

            string outcomeText = LinkedOutcomeText(rule);
            if (!string.IsNullOrEmpty(outcomeText))
            {
                var outcomes = new Label(outcomeText);
                outcomes.AddToClassList("rs-termination-outcomes");
                card.Add(outcomes);
            }

            var thresholdRoot = new VisualElement();
            thresholdRoot.AddToClassList("rs-termination-thresholds");
            var sharedNames = new List<string>();
            foreach (TerminationThresholdDescriptor threshold in rule.thresholds)
            {
                if (!renderedThresholds.Add(threshold.key))
                {
                    sharedNames.Add(threshold.label);
                    continue;
                }
                BuildTerminationThreshold(
                    thresholdRoot, scenario, objective, defaults, validation, threshold);
            }

            if (sharedNames.Count > 0)
            {
                var shared = new Label("Uses shared criteria configured above: " + string.Join(", ", sharedNames));
                shared.AddToClassList("rs-termination-shared-note");
                thresholdRoot.Add(shared);
            }
            card.Add(thresholdRoot);

            card.EnableInClassList(
                "rs-termination-rule-disabled",
                !rule.GetEnabled(objective.terminations));

            enabled.RegisterValueChangedCallback(evt =>
            {
                rule.SetEnabled(objective.terminations, evt.newValue);
                card.EnableInClassList("rs-termination-rule-disabled", !evt.newValue);
                ApplyObjectiveDerivedState(scenario);
                RefreshObjectiveValidation(validation, scenario, objective);
            });
            return card;
        }

        /// <summary>Builds scalar, integer, Boolean, or curriculum endpoint controls.</summary>
        void BuildTerminationThreshold(
            VisualElement root,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            ScenarioObjectiveConfig defaults,
            Label validation,
            TerminationThresholdDescriptor threshold)
        {
            if (threshold.valueKind == TerminationValueKind.Boolean)
            {
                root.Add(BuildDescribedTerminationToggle(
                    threshold.label,
                    threshold.GetBoolean(objective.terminations),
                    value =>
                    {
                        threshold.SetBoolean(objective.terminations, value);
                        ApplyObjectiveDerivedState(scenario);
                        RefreshObjectiveValidation(validation, scenario, objective);
                    },
                    threshold.description));
                return;
            }

            if (threshold.usesDifficultyRange)
            {
                root.Add(BuildTerminationNumber(
                    threshold.label + " - Initial",
                    threshold.GetInitialValue(objective.terminations),
                    threshold.GetInitialValue(defaults.terminations),
                    threshold,
                    value => threshold.SetInitialValue(objective.terminations, value),
                    threshold.description + " This endpoint is used at curriculum difficulty 0.",
                    scenario,
                    objective,
                    validation));
                root.Add(BuildTerminationNumber(
                    threshold.label + " - Full",
                    threshold.GetFullValue(objective.terminations),
                    threshold.GetFullValue(defaults.terminations),
                    threshold,
                    value => threshold.SetFullValue(objective.terminations, value),
                    threshold.description + " This endpoint is used at curriculum difficulty 1.",
                    scenario,
                    objective,
                    validation));
                return;
            }

            root.Add(BuildTerminationNumber(
                threshold.label,
                threshold.GetInitialValue(objective.terminations),
                threshold.GetInitialValue(defaults.terminations),
                threshold,
                value => threshold.SetInitialValue(objective.terminations, value),
                threshold.description,
                scenario,
                objective,
                validation));
        }

        VisualElement BuildTerminationNumber(
            string label,
            float value,
            float resetValue,
            TerminationThresholdDescriptor threshold,
            Action<float> setValue,
            string description,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            Label validation)
        {
            var options = new UIHelper.NumericSliderFieldOptions(
                threshold.sliderMinimum,
                threshold.sliderMaximum)
            {
                HardMinimum = threshold.hardMinimum,
                HardMaximum = threshold.hardMaximum,
                Scale = ToUiScale(threshold.scaleMode),
                SmallestPositiveValue = SmallestPositiveSliderValue(threshold.sliderMaximum),
                Decimals = threshold.valueKind == TerminationValueKind.Integer
                    ? 0
                    : DecimalPlacesForRange(threshold.sliderMaximum),
                Unit = threshold.unit,
                Sign = UIHelper.NumericSliderSign.Neutral,
                Category = UIHelper.NumericSliderCategory.Threshold,
                ResetValue = resetValue,
                ValueStep = threshold.valueKind == TerminationValueKind.Integer ? 1f : 0f
            };
            return UIHelper.NumericSliderField(
                label,
                value,
                options,
                nextValue =>
                {
                    setValue(nextValue);
                    ApplyObjectiveDerivedState(scenario);
                    RefreshObjectiveValidation(validation, scenario, objective);
                    RefreshStartButton();
                },
                description);
        }

        static VisualElement BuildDescribedTerminationToggle(
            string label,
            bool value,
            Action<bool> onChange,
            string description)
        {
            var group = new VisualElement();
            group.AddToClassList("rs-described-control");
            group.AddToClassList("rs-termination-boolean");
            group.Add(UIHelper.Toggle(label, value, onChange));
            var help = new Label(description);
            help.AddToClassList("rs-control-description");
            group.Add(help);
            return group;
        }

        static void ResetTerminationRule(
            TerminationRuleDescriptor rule,
            TerminationParameters target,
            TerminationParameters defaults)
        {
            rule.SetEnabled(target, rule.GetEnabled(defaults));
            foreach (TerminationThresholdDescriptor threshold in rule.thresholds)
            {
                if (threshold.valueKind == TerminationValueKind.Boolean)
                {
                    threshold.SetBoolean(target, threshold.GetBoolean(defaults));
                    continue;
                }
                threshold.SetInitialValue(target, threshold.GetInitialValue(defaults));
                threshold.SetFullValue(target, threshold.GetFullValue(defaults));
            }
        }

        /// <summary>Refreshes cached curriculum values derived from termination endpoints.</summary>
        void ApplyObjectiveDerivedState(ScenarioType scenario)
        {
            if (scenario == ScenarioType.HoverTracking)
                envConfig.ApplyHoverTrackCurriculum();
            if (scenario.IsLanding())
                envConfig.ApplyActiveLandingCurriculum();
            RefreshStartButton();
        }

        /// <summary>Displays actionable configuration concerns next to the editor.</summary>
        static void RefreshObjectiveValidation(
            Label label,
            ScenarioType scenario,
            ScenarioObjectiveConfig objective)
        {
            if (label == null || objective == null) return;
            ObjectiveValidationResult result = ObjectiveValidator.Validate(scenario, objective);
            label.EnableInClassList("rs-objective-validation-error", result.HasErrors);
            label.EnableInClassList(
                "rs-objective-validation-warning",
                !result.HasErrors && result.HasWarnings);
            label.EnableInClassList("rs-objective-validation-ok", result.issues.Count == 0);
            if (result.issues.Count == 0)
            {
                label.text = "Objective check: no configuration issues found.";
                return;
            }

            var lines = new List<string>();
            foreach (ObjectiveValidationIssue issue in result.issues)
            {
                string prefix = issue.severity == ObjectiveValidationSeverity.Error
                    ? "ERROR"
                    : "WARNING";
                lines.Add($"[{prefix}] {issue.message}");
            }
            label.text = "Objective check:\n- " + string.Join("\n- ", lines);
        }

        static string LinkedOutcomeText(TerminationRuleDescriptor rule)
        {
            var lines = new List<string>();
            if (rule.criteriaEventParameter != RewardParameterId.None)
            {
                RewardParameterDescriptor eventDescriptor =
                    RewardParameterCatalog.Find(rule.criteriaEventParameter);
                if (eventDescriptor != null)
                    lines.Add("Criteria also define event row above: + " + eventDescriptor.label);
            }

            var names = new List<string>();
            foreach (RewardParameterId id in rule.terminalParameters)
            {
                RewardParameterDescriptor descriptor = RewardParameterCatalog.Find(id);
                if (descriptor == null) continue;
                names.Add((descriptor.sign == RewardParameterSign.Reward ? "+ " : "- ") + descriptor.label);
            }
            if (names.Count > 0)
                lines.Add("Terminal outcome rows above: " + string.Join(", ", names));
            return string.Join("\n", lines);
        }

        static UIHelper.NumericSliderScale ToUiScale(NumericScaleMode scaleMode) =>
            scaleMode switch
            {
                NumericScaleMode.LogarithmicWithZero => UIHelper.NumericSliderScale.Logarithmic,
                NumericScaleMode.QuadraticWithZero => UIHelper.NumericSliderScale.Quadratic,
                _ => UIHelper.NumericSliderScale.Linear
            };

        static float SmallestPositiveSliderValue(float maximum) =>
            Mathf.Max(maximum * 0.00001f, 0.000001f);

        static int DecimalPlacesForRange(float maximum)
        {
            if (maximum <= 1f) return 4;
            if (maximum <= 10f) return 3;
            if (maximum <= 100f) return 2;
            return 1;
        }

        static bool ApproximatelyEqual(float left, float right)
        {
            float scale = Mathf.Max(1f, Mathf.Abs(left), Mathf.Abs(right));
            return Mathf.Abs(left - right) <= scale * 0.000001f;
        }

        static string ValidBasePresetName(ScenarioType scenario, string requested)
        {
            foreach (string name in RewardPresetCatalog.ForScenario(scenario))
                if (string.Equals(name, requested, StringComparison.Ordinal)) return name;
            return RewardPresetCatalog.BalancedPresetName;
        }

        static string ScenarioDisplayName(ScenarioType scenario)
        {
            foreach (ScenarioDefinition definition in ScenarioCatalog.All)
                if (definition.Type == scenario) return definition.DisplayName;
            return scenario.ToString();
        }

        static string TerminationKindText(TerminationRuleKind kind) => kind switch
        {
            TerminationRuleKind.Success => "SUCCESS",
            TerminationRuleKind.Failure => "FAILURE",
            TerminationRuleKind.Limit => "LIMIT",
            TerminationRuleKind.SuccessOrFailure => "OUTCOME",
            _ => string.Empty
        };

        static string TerminationKindClass(TerminationRuleKind kind) => kind switch
        {
            TerminationRuleKind.Success => "rs-termination-success",
            TerminationRuleKind.Failure => "rs-termination-failure",
            TerminationRuleKind.Limit => "rs-termination-limit",
            TerminationRuleKind.SuccessOrFailure => "rs-termination-outcome",
            _ => "rs-termination-limit"
        };
    }
}
