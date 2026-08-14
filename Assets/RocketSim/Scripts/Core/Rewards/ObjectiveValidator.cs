// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ObjectiveValidator.cs
// Purpose: Pure validation shared by the UI and training launcher. Validation
// reports problems but never clamps or mutates the configured objective.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace RocketSim
{
    public enum ObjectiveValidationSeverity
    {
        Warning,
        Error
    }

    public sealed class ObjectiveValidationIssue
    {
        public ObjectiveValidationSeverity severity { get; }
        public string code { get; }
        public string message { get; }
        public string parameterKey { get; }

        public ObjectiveValidationIssue(
            ObjectiveValidationSeverity severity,
            string code,
            string message,
            string parameterKey = null)
        {
            this.severity = severity;
            this.code = code;
            this.message = message;
            this.parameterKey = parameterKey;
        }
    }

    public sealed class ObjectiveValidationResult
    {
        readonly List<ObjectiveValidationIssue> _issues;

        internal ObjectiveValidationResult(List<ObjectiveValidationIssue> issues) =>
            _issues = issues ?? new List<ObjectiveValidationIssue>();

        public IReadOnlyList<ObjectiveValidationIssue> issues => _issues;
        public bool HasErrors => Contains(ObjectiveValidationSeverity.Error);
        public bool HasWarnings => Contains(ObjectiveValidationSeverity.Warning);
        public bool IsValid => !HasErrors;

        bool Contains(ObjectiveValidationSeverity severity)
        {
            for (int i = 0; i < _issues.Count; i++)
                if (_issues[i].severity == severity) return true;
            return false;
        }
    }

    public static class ObjectiveValidator
    {
        public static ObjectiveValidationResult Validate(
            ScenarioType scenario,
            ScenarioObjectiveConfig objective)
        {
            var issues = new List<ObjectiveValidationIssue>();
            if (!Enum.IsDefined(typeof(ScenarioType), scenario))
            {
                issues.Add(Error(
                    "scenario.unsupported",
                    $"Scenario value {(int)scenario} is not part of the current schema."));
                return new ObjectiveValidationResult(issues);
            }
            if (objective == null)
            {
                issues.Add(Error("objective.missing", "The selected scenario has no training objective."));
                return new ObjectiveValidationResult(issues);
            }
            if (objective.rewards == null)
                issues.Add(Error("rewards.missing", "Reward parameters are missing."));
            if (objective.shaping == null)
                issues.Add(Error("shaping.missing", "Reward shaping parameters are missing."));
            if (objective.terminations == null)
                issues.Add(Error("termination.missing", "Termination parameters are missing."));
            if (issues.Count > 0) return new ObjectiveValidationResult(issues);

            ValidateRewards(scenario, objective.rewards, issues);
            ValidateShaping(scenario, objective.shaping, issues);
            ValidateTermination(scenario, objective, issues);
            return new ObjectiveValidationResult(issues);
        }

        static void ValidateRewards(
            ScenarioType scenario,
            RewardParameters parameters,
            List<ObjectiveValidationIssue> issues)
        {
            bool anyApplicableValue = false;
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
            {
                float value = descriptor.GetValue(parameters);
                if (!Finite(value))
                {
                    issues.Add(Error("reward.not_finite", $"{descriptor.label} must be finite.", descriptor.key));
                    continue;
                }

                if (!descriptor.AppliesTo(scenario))
                {
                    if (value != 0f)
                        issues.Add(Warning(
                            "reward.inapplicable_nonzero",
                            $"{descriptor.label} does not apply to {scenario} and should remain zero.",
                            descriptor.key));
                    continue;
                }

                anyApplicableValue |= value != 0f;
                if (value < descriptor.hardMinimum || value > descriptor.hardMaximum)
                    issues.Add(Error(
                        "reward.out_of_range",
                        $"{descriptor.label} must be between {descriptor.hardMinimum:g} and {descriptor.hardMaximum:g}.",
                        descriptor.key));
            }

            if (!anyApplicableValue)
                issues.Add(Warning(
                    "reward.all_zero",
                    "Every applicable reward magnitude is zero; PPO will receive no objective signal."));
        }

        static void ValidateShaping(
            ScenarioType scenario,
            RewardShapingParameters parameters,
            List<ObjectiveValidationIssue> issues)
        {
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.ForScenario(scenario))
            {
                float value = descriptor.GetValue(parameters);
                if (!Finite(value))
                {
                    issues.Add(Error("shaping.not_finite", $"{descriptor.label} must be finite.", descriptor.key));
                    continue;
                }
                if (value < descriptor.hardMinimum || value > descriptor.hardMaximum)
                    issues.Add(Error(
                        "shaping.out_of_range",
                        $"{descriptor.label} must be between {descriptor.hardMinimum:g} and {descriptor.hardMaximum:g}.",
                        descriptor.key));
            }
        }

        static void ValidateTermination(
            ScenarioType scenario,
            ScenarioObjectiveConfig objective,
            List<ObjectiveValidationIssue> issues)
        {
            TerminationParameters parameters = objective.terminations;
            bool anyEnabled = false;
            bool anySuccessEnabled = false;
            var checkedFiniteThresholds = new HashSet<string>(StringComparer.Ordinal);
            var checkedActiveThresholds = new HashSet<string>(StringComparer.Ordinal);

            foreach (TerminationRuleDescriptor rule in TerminationRuleCatalog.ForScenario(scenario))
            {
                bool enabled = rule.GetEnabled(parameters);
                anyEnabled |= enabled;
                anySuccessEnabled |= enabled &&
                    (rule.kind == TerminationRuleKind.Success ||
                     rule.kind == TerminationRuleKind.SuccessOrFailure);

                // Success criteria can also define one-off events. Those ranges
                // remain active while their event signal is nonzero; moving-target
                // captures remain active unconditionally because they also drive
                // target movement and curriculum accounting.
                bool linkedCriteriaAreActive = rule.criteriaAlwaysActive;
                if (!linkedCriteriaAreActive &&
                    rule.criteriaEventParameter != RewardParameterId.None)
                {
                    RewardParameterDescriptor eventParameter =
                        RewardParameterCatalog.Find(rule.criteriaEventParameter);
                    linkedCriteriaAreActive = eventParameter != null &&
                        eventParameter.GetValue(objective.rewards) != 0f;
                }
                foreach (TerminationThresholdDescriptor threshold in rule.thresholds)
                {
                    // Finite data is required even for disabled rules. Range
                    // bounds apply only while a rule or linked event uses it.
                    if (checkedFiniteThresholds.Add(threshold.key))
                        ValidateThresholdFinite(threshold, parameters, issues);
                    bool thresholdIsActive = enabled ||
                        (linkedCriteriaAreActive && threshold.appliesToLinkedEvent);
                    if (thresholdIsActive && checkedActiveThresholds.Add(threshold.key))
                        ValidateThresholdRange(threshold, parameters, issues);
                }

                if (!enabled) continue;
                for (int i = 0; i < rule.terminalParameters.Count; i++)
                {
                    RewardParameterDescriptor terminal =
                        RewardParameterCatalog.Find(rule.terminalParameters[i]);
                    if (terminal != null && terminal.GetValue(objective.rewards) == 0f)
                        issues.Add(Warning(
                            "termination.zero_outcome",
                            $"{rule.label} is enabled but its {terminal.label} magnitude is zero.",
                            terminal.key));
                }
            }

            if (!anyEnabled)
                issues.Add(Error(
                    "termination.none_enabled",
                    "Enable at least one termination rule. The agent has no external MaxStep fallback, so an objective with every rule disabled would never reset its episodes."));

            if ((scenario == ScenarioType.ChopstickLanding || scenario == ScenarioType.LegLanding) &&
                !anySuccessEnabled)
                issues.Add(Warning(
                    "termination.no_success_rule",
                    "No landing success rule is enabled, so episodes cannot report a successful landing."));

            if (scenario == ScenarioType.HoverTracking &&
                parameters.trackingCaptureGoalEnabled &&
                parameters.trackingRequiredCaptures < 1)
                issues.Add(Error(
                    "termination.capture_count",
                    "Capture-count success requires at least one target capture.",
                    "tracking.required_captures"));

            WarnIfCurriculumDirectionUnexpected(
                scenario, parameters.landingSuccessRadiusM,
                "landing.success_radius_m", "Landing success radius", issues, fullShouldBeLarger: false);
            WarnIfCurriculumDirectionUnexpected(
                scenario, parameters.landingStableHoldSeconds,
                "landing.stable_hold_seconds", "Stable-hold duration", issues, fullShouldBeLarger: true);
        }

        static void ValidateThresholdFinite(
            TerminationThresholdDescriptor descriptor,
            TerminationParameters parameters,
            List<ObjectiveValidationIssue> issues)
        {
            if (descriptor.valueKind == TerminationValueKind.Boolean) return;
            ValidateThresholdEndpointFinite(
                descriptor, descriptor.GetInitialValue(parameters), "initial", issues);
            if (descriptor.usesDifficultyRange)
                ValidateThresholdEndpointFinite(
                    descriptor, descriptor.GetFullValue(parameters), "full", issues);
        }

        static void ValidateThresholdRange(
            TerminationThresholdDescriptor descriptor,
            TerminationParameters parameters,
            List<ObjectiveValidationIssue> issues)
        {
            if (descriptor.valueKind == TerminationValueKind.Boolean) return;
            ValidateThresholdEndpointRange(
                descriptor, descriptor.GetInitialValue(parameters), "initial", issues);
            if (descriptor.usesDifficultyRange)
                ValidateThresholdEndpointRange(
                    descriptor, descriptor.GetFullValue(parameters), "full", issues);
        }

        static void ValidateThresholdEndpointFinite(
            TerminationThresholdDescriptor descriptor,
            float value,
            string endpoint,
            List<ObjectiveValidationIssue> issues)
        {
            if (!Finite(value))
            {
                issues.Add(Error(
                    "termination.not_finite",
                    $"{descriptor.label} ({endpoint}) must be finite.", descriptor.key));
            }
        }

        static void ValidateThresholdEndpointRange(
            TerminationThresholdDescriptor descriptor,
            float value,
            string endpoint,
            List<ObjectiveValidationIssue> issues)
        {
            if (Finite(value) &&
                (value < descriptor.hardMinimum || value > descriptor.hardMaximum))
                issues.Add(Error(
                    "termination.out_of_range",
                    $"{descriptor.label} ({endpoint}) must be between {descriptor.hardMinimum:g} and {descriptor.hardMaximum:g}.",
                    descriptor.key));
        }

        static void WarnIfCurriculumDirectionUnexpected(
            ScenarioType scenario,
            DifficultyRange range,
            string key,
            string label,
            List<ObjectiveValidationIssue> issues,
            bool fullShouldBeLarger)
        {
            if (scenario != ScenarioType.ChopstickLanding && scenario != ScenarioType.LegLanding) return;
            bool reversed = fullShouldBeLarger ? range.full < range.initial : range.full > range.initial;
            if (reversed)
                issues.Add(Warning(
                    "termination.curriculum_direction",
                    $"{label} becomes easier at full difficulty. This is allowed but may be unintended.", key));
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static ObjectiveValidationIssue Error(string code, string message, string key = null) =>
            new(ObjectiveValidationSeverity.Error, code, message, key);
        static ObjectiveValidationIssue Warning(string code, string message, string key = null) =>
            new(ObjectiveValidationSeverity.Warning, code, message, key);
    }
}
