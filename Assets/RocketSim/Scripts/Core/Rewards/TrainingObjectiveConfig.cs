// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/TrainingObjectiveConfig.cs
// Purpose: Stores the complete, user-editable objective for each supported task.
// Reward values are absolute magnitudes; no hidden multiplier layer exists.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Root configuration serialized with the environment and training run.
    /// Each task owns an independent objective so editing hover never changes
    /// the landing tasks. This is intentionally a clean schema with no legacy
    /// aliases or migration rules.
    /// </summary>
    [Serializable]
    public sealed class TrainingObjectiveConfig
    {
        public ScenarioObjectiveConfig chopstickLanding =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
        public ScenarioObjectiveConfig legLanding =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
        public ScenarioObjectiveConfig hover =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
        public ScenarioObjectiveConfig hoverTracking =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);

        /// <summary>
        /// Recreates only missing task objects. An all-zero objective is valid
        /// and is therefore never interpreted as uninitialized data.
        /// </summary>
        public void EnsureDefaults()
        {
            chopstickLanding ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
            legLanding ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            hover ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            hoverTracking ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);

            chopstickLanding.EnsureObjects(ScenarioType.ChopstickLanding);
            legLanding.EnsureObjects(ScenarioType.LegLanding);
            hover.EnsureObjects(ScenarioType.Hover);
            hoverTracking.EnsureObjects(ScenarioType.HoverTracking);
        }

        /// <summary>Returns the objective owned by a supported task.</summary>
        public ScenarioObjectiveConfig ForScenario(ScenarioType scenario)
        {
            EnsureDefaults();
            return scenario switch
            {
                ScenarioType.ChopstickLanding => chopstickLanding,
                ScenarioType.LegLanding => legLanding,
                ScenarioType.Hover => hover,
                ScenarioType.HoverTracking => hoverTracking,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario), scenario, "The scenario has no reward objective.")
            };
        }

        /// <summary>
        /// Replaces every reward magnitude for one task with the selected
        /// complete preset. Shaping and termination settings remain untouched.
        /// </summary>
        public void ApplyPreset(ScenarioType scenario, string presetName) =>
            RewardPresetCatalog.Apply(ForScenario(scenario), scenario, presetName);

        /// <summary>Restores rewards, shaping, and termination settings.</summary>
        public void ResetScenario(ScenarioType scenario)
        {
            ScenarioObjectiveConfig replacement = ScenarioObjectiveConfig.CreateDefault(scenario);
            ForScenario(scenario).CopyFrom(replacement);
        }
    }

    /// <summary>
    /// One task's complete training objective. Reward magnitudes, shaping
    /// geometry, and episode-ending rules are separate by design.
    /// </summary>
    [Serializable]
    public sealed class ScenarioObjectiveConfig
    {
        public string presetName = RewardPresetCatalog.BalancedPresetName;
        public string basePresetName = RewardPresetCatalog.BalancedPresetName;
        public RewardParameters rewards = new();
        public RewardShapingParameters shaping = new();
        public TerminationParameters terminations = new();

        public static ScenarioObjectiveConfig CreateDefault(ScenarioType scenario)
        {
            var objective = new ScenarioObjectiveConfig();
            RewardPresetCatalog.Apply(objective, scenario, RewardPresetCatalog.BalancedPresetName);
            RewardShapingDefaults.Apply(objective.shaping, scenario);
            TerminationDefaults.Apply(objective.terminations, scenario);
            return objective;
        }

        /// <summary>
        /// Creates only missing child objects. Existing values, including an
        /// intentional all-zero reward vector, are preserved.
        /// </summary>
        public void EnsureObjects(ScenarioType scenario)
        {
            rewards ??= new RewardParameters();
            if (shaping == null)
            {
                shaping = new RewardShapingParameters();
                RewardShapingDefaults.Apply(shaping, scenario);
            }
            if (terminations == null)
            {
                terminations = new TerminationParameters();
                TerminationDefaults.Apply(terminations, scenario);
            }
            if (string.IsNullOrWhiteSpace(presetName))
                presetName = "Custom";
            if (string.IsNullOrWhiteSpace(basePresetName))
                basePresetName = RewardPresetCatalog.BalancedPresetName;
        }

        /// <summary>Deep-copies another objective into this serialized object.</summary>
        public void CopyFrom(ScenarioObjectiveConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            rewards ??= new RewardParameters();
            shaping ??= new RewardShapingParameters();
            terminations ??= new TerminationParameters();
            rewards.CopyFrom(source.rewards);
            shaping.CopyFrom(source.shaping);
            terminations.CopyFrom(source.terminations);
            presetName = source.presetName;
            basePresetName = source.basePresetName;
        }

        /// <summary>Marks the reward vector as no longer matching a preset.</summary>
        public void MarkCustom() => presetName = "Custom";
    }

    /// <summary>
    /// Absolute, nonnegative reward magnitudes. A field ending in <c>Rate</c>
    /// is integrated over simulated seconds. Event and terminal fields are
    /// applied once. Costs are stored as positive magnitudes and negated by the
    /// evaluator, eliminating ambiguous double-negative configuration.
    /// </summary>
    [Serializable]
    public sealed class RewardParameters
    {
        // Landing guidance rates.
        public float landingGoalClosureRewardRate;
        public float landingDescentProfileErrorCostRate;
        public float landingPlanarDistanceCostRate;
        public float landingUprightErrorCostRate;
        public float landingNearTargetPlanarSpeedCostRate;
        public float landingNearTargetAngularRateCostRate;
        public float landingYawErrorCostRate;
        public float landingYawSpinCostRate;
        public float controlEffortCostRate;
        public float timeCostRate;

        // Landing one-off events.
        public float stableCaptureReward;
        public float firstFootContactReward;
        public float stableTouchdownReward;

        // Hover stability rates and event costs.
        public float hoverAltitudeProximityRewardRate;
        public float hoverPlanarProximityRewardRate;
        public float hoverUprightRewardRate;
        public float hoverSpeedCalmRewardRate;
        public float hoverRotationCalmRewardRate;
        public float hoverLinearSpeedCostRate;
        public float hoverAngularRateCostRate;
        public float engineRestartCost;

        // Moving-target hover rates and capture events.
        public float trackingSettleCenterRewardRate;
        public float trackingSettleHorizontalCalmRewardRate;
        public float trackingSettleVerticalCalmRewardRate;
        public float trackingSettleRotationCalmRewardRate;
        public float trackingSettleCompositeRewardRate;
        public float trackingSettlePlanarSpeedCostRate;
        public float trackingSettleVerticalSpeedCostRate;
        public float trackingApproachClosureRewardRate;
        public float trackingApproachDirectionRewardRate;
        public float trackingUsefulSpeedRewardRate;
        public float trackingMovingAwayCostRate;
        public float trackingLoiteringCostRate;
        public float trackingOverspeedCostRate;
        public float trackingTargetCaptureReward;

        // Chopstick terminal outcomes.
        public float chopstickSuccessfulCaptureReward;
        public float chopstickFailedCaptureCost;
        public float chopstickUnsafeAttitudeCost;
        public float chopstickTooFarFromTargetCost;
        public float chopstickFuelDepletedCost;
        public float chopstickAboveAltitudeLimitCost;
        public float chopstickTimeLimitCost;

        // Leg-landing terminal outcomes.
        public float legSuccessfulTouchdownReward;
        public float legHardTouchdownCost;
        public float legStructuralStrikeCost;
        public float legFootOutsidePadCost;
        public float legExcessiveReboundCost;
        public float legUnsafeAttitudeCost;
        public float legTooFarFromTargetCost;
        public float legFuelDepletedCost;
        public float legAboveAltitudeLimitCost;
        public float legMissedPadCost;
        public float legTimeLimitCost;

        // Hover and hover-tracking terminal outcomes.
        public float hoverGroundImpactCost;
        public float hoverUnsafeAttitudeCost;
        public float hoverFuelDepletedCost;
        public float hoverTooFarFromTargetCost;
        public float hoverAboveAltitudeLimitCost;
        public float hoverTimeLimitCost;
        public float trackingCaptureGoalReward;

        /// <summary>Sets every reward, cost, event, and terminal signal to zero.</summary>
        public void Clear()
        {
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                descriptor.SetValue(this, 0f);
        }

        /// <summary>Deep-copies every catalogued value.</summary>
        public void CopyFrom(RewardParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                descriptor.SetValue(this, descriptor.GetValue(source));
        }
    }

    /// <summary>
    /// Tunable geometry used to normalize reward features. These values change
    /// the shape of a signal but never its maximum reward magnitude.
    /// </summary>
    [Serializable]
    public sealed class RewardShapingParameters
    {
        public float landingBallisticDescentFraction;
        public float landingDescentErrorMinimumScaleMps;
        public float landingClosureMinimumScaleMps;
        public float landingPlanarDistanceFalloffM;
        public float landingNearTargetAltitudeFalloffM;
        public float landingPlanarSpeedScaleMps;
        public float landingAngularRateScaleDegS;
        public float landingYawErrorScaleDeg;
        public float landingYawSpinScaleDegS;

        public float hoverAltitudeFalloffM;
        public float hoverPlanarFalloffM;
        public float hoverSpeedFalloffMps;
        public float hoverAngularRateFalloffDegS;

        public float trackingTightCenterRadiusFraction;
        public float trackingTightCenterMinimumFalloffM;
        public float trackingHorizontalCalmFalloffMps;
        public float trackingVerticalCalmFalloffMps;
        public float trackingRotationCalmFalloffDegS;
        public float trackingApproachSpeedScaleMps;
        public float trackingDirectionMinimumSpeedMps;
        public float trackingMovingAwaySpeedScaleMps;
        public float trackingFarDistanceScaleMultiplier;
        public float trackingUsefulSpeedScaleMps;
        public float trackingOverspeedThresholdMps;
        public float trackingOverspeedRangeMps;

        public void Clear()
        {
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                descriptor.SetValue(this, 0f);
        }

        public void CopyFrom(RewardShapingParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                descriptor.SetValue(this, descriptor.GetValue(source));
        }
    }

    /// <summary>
    /// An initial/full-difficulty pair. The value used by an episode is linearly
    /// interpolated from its immutable curriculum difficulty snapshot.
    /// </summary>
    [Serializable]
    public struct DifficultyRange
    {
        public float initial;
        public float full;

        public DifficultyRange(float initial, float full)
        {
            this.initial = initial;
            this.full = full;
        }

        public readonly float At(float difficulty01) =>
            Mathf.Lerp(initial, full, Mathf.Clamp01(difficulty01));
    }

    /// <summary>
    /// Scenario-specific episode-ending switches and thresholds. The same plain
    /// object type is used for serialization, while TerminationRuleCatalog hides
    /// rules that do not apply to the selected task.
    /// </summary>
    [Serializable]
    public sealed class TerminationParameters
    {
        // Common safety/limit rules. Each scenario owns a separate object.
        public bool unsafeAttitudeEnabled;
        public float maximumTiltDeg;
        public bool planarFlyawayEnabled;
        public float maximumPlanarDistanceM;
        public bool altitudeCeilingEnabled;
        public float maximumAltitudeAboveStartM;
        public float hoverMaximumAltitudeM;
        public bool fuelDepletionEnabled;
        public float minimumFuelKg;
        public bool timeLimitEnabled;
        public float maximumEpisodeSeconds;

        // Landing success/contact criteria. These interpolate with curriculum.
        public DifficultyRange landingSuccessRadiusM;
        public DifficultyRange landingSuccessMaxTotalSpeedMps;
        public DifficultyRange landingSuccessMaxVerticalSpeedMps;
        public DifficultyRange landingSuccessMaxHorizontalSpeedMps;
        public DifficultyRange landingSuccessMaxTiltDeg;
        public DifficultyRange landingSuccessMaxAngularRateDegS;
        public DifficultyRange landingSuccessMaxYawErrorDeg;
        public DifficultyRange landingStableHoldSeconds;

        // Chopstick capture.
        public bool chopstickCapturePlaneEnabled;
        public bool chopstickRequireStablePlatform;

        // Physical leg landing.
        public bool legStructuralStrikeEnabled;
        public bool legFootOutsidePadEnabled;
        public bool legHardFirstContactEnabled;
        public bool legExcessiveReboundEnabled;
        public bool legStableTouchdownEnabled;
        public bool legMissedPadEnabled;
        public float legMissedPadDepthM;
        public int legMinimumStableFeet;
        public float legMaximumReboundRiseM;
        public float legMaximumAllFeetContactLossSeconds;
        public bool legAllowFuelDepletionAfterContact;

        // Fixed and moving-target hover.
        public bool hoverGroundImpactEnabled;
        public float hoverGroundClearanceM;
        public bool trackingCaptureGoalEnabled;
        public int trackingRequiredCaptures;
        public DifficultyRange trackingCaptureRadiusM;
        public DifficultyRange trackingCaptureMaxVerticalErrorM;
        public DifficultyRange trackingCaptureMaxHorizontalSpeedMps;
        public DifficultyRange trackingCaptureMaxVerticalSpeedMps;
        public DifficultyRange trackingCaptureMaxTiltDeg;
        public DifficultyRange trackingCaptureMaxAngularRateDegS;
        public DifficultyRange trackingCaptureHoldSeconds;

        public void Clear()
        {
            unsafeAttitudeEnabled = false;
            maximumTiltDeg = 0f;
            planarFlyawayEnabled = false;
            maximumPlanarDistanceM = 0f;
            altitudeCeilingEnabled = false;
            maximumAltitudeAboveStartM = 0f;
            hoverMaximumAltitudeM = 0f;
            fuelDepletionEnabled = false;
            minimumFuelKg = 0f;
            timeLimitEnabled = false;
            maximumEpisodeSeconds = 0f;
            landingSuccessRadiusM = default;
            landingSuccessMaxTotalSpeedMps = default;
            landingSuccessMaxVerticalSpeedMps = default;
            landingSuccessMaxHorizontalSpeedMps = default;
            landingSuccessMaxTiltDeg = default;
            landingSuccessMaxAngularRateDegS = default;
            landingSuccessMaxYawErrorDeg = default;
            landingStableHoldSeconds = default;
            chopstickCapturePlaneEnabled = false;
            chopstickRequireStablePlatform = false;
            legStructuralStrikeEnabled = false;
            legFootOutsidePadEnabled = false;
            legHardFirstContactEnabled = false;
            legExcessiveReboundEnabled = false;
            legStableTouchdownEnabled = false;
            legMissedPadEnabled = false;
            legMissedPadDepthM = 0f;
            legMinimumStableFeet = 0;
            legMaximumReboundRiseM = 0f;
            legMaximumAllFeetContactLossSeconds = 0f;
            legAllowFuelDepletionAfterContact = false;
            hoverGroundImpactEnabled = false;
            hoverGroundClearanceM = 0f;
            trackingCaptureGoalEnabled = false;
            trackingRequiredCaptures = 0;
            trackingCaptureRadiusM = default;
            trackingCaptureMaxVerticalErrorM = default;
            trackingCaptureMaxHorizontalSpeedMps = default;
            trackingCaptureMaxVerticalSpeedMps = default;
            trackingCaptureMaxTiltDeg = default;
            trackingCaptureMaxAngularRateDegS = default;
            trackingCaptureHoldSeconds = default;
        }

        public void CopyFrom(TerminationParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            unsafeAttitudeEnabled = source.unsafeAttitudeEnabled;
            maximumTiltDeg = source.maximumTiltDeg;
            planarFlyawayEnabled = source.planarFlyawayEnabled;
            maximumPlanarDistanceM = source.maximumPlanarDistanceM;
            altitudeCeilingEnabled = source.altitudeCeilingEnabled;
            maximumAltitudeAboveStartM = source.maximumAltitudeAboveStartM;
            hoverMaximumAltitudeM = source.hoverMaximumAltitudeM;
            fuelDepletionEnabled = source.fuelDepletionEnabled;
            minimumFuelKg = source.minimumFuelKg;
            timeLimitEnabled = source.timeLimitEnabled;
            maximumEpisodeSeconds = source.maximumEpisodeSeconds;
            landingSuccessRadiusM = source.landingSuccessRadiusM;
            landingSuccessMaxTotalSpeedMps = source.landingSuccessMaxTotalSpeedMps;
            landingSuccessMaxVerticalSpeedMps = source.landingSuccessMaxVerticalSpeedMps;
            landingSuccessMaxHorizontalSpeedMps = source.landingSuccessMaxHorizontalSpeedMps;
            landingSuccessMaxTiltDeg = source.landingSuccessMaxTiltDeg;
            landingSuccessMaxAngularRateDegS = source.landingSuccessMaxAngularRateDegS;
            landingSuccessMaxYawErrorDeg = source.landingSuccessMaxYawErrorDeg;
            landingStableHoldSeconds = source.landingStableHoldSeconds;
            chopstickCapturePlaneEnabled = source.chopstickCapturePlaneEnabled;
            chopstickRequireStablePlatform = source.chopstickRequireStablePlatform;
            legStructuralStrikeEnabled = source.legStructuralStrikeEnabled;
            legFootOutsidePadEnabled = source.legFootOutsidePadEnabled;
            legHardFirstContactEnabled = source.legHardFirstContactEnabled;
            legExcessiveReboundEnabled = source.legExcessiveReboundEnabled;
            legStableTouchdownEnabled = source.legStableTouchdownEnabled;
            legMissedPadEnabled = source.legMissedPadEnabled;
            legMissedPadDepthM = source.legMissedPadDepthM;
            legMinimumStableFeet = source.legMinimumStableFeet;
            legMaximumReboundRiseM = source.legMaximumReboundRiseM;
            legMaximumAllFeetContactLossSeconds = source.legMaximumAllFeetContactLossSeconds;
            legAllowFuelDepletionAfterContact = source.legAllowFuelDepletionAfterContact;
            hoverGroundImpactEnabled = source.hoverGroundImpactEnabled;
            hoverGroundClearanceM = source.hoverGroundClearanceM;
            trackingCaptureGoalEnabled = source.trackingCaptureGoalEnabled;
            trackingRequiredCaptures = source.trackingRequiredCaptures;
            trackingCaptureRadiusM = source.trackingCaptureRadiusM;
            trackingCaptureMaxVerticalErrorM = source.trackingCaptureMaxVerticalErrorM;
            trackingCaptureMaxHorizontalSpeedMps = source.trackingCaptureMaxHorizontalSpeedMps;
            trackingCaptureMaxVerticalSpeedMps = source.trackingCaptureMaxVerticalSpeedMps;
            trackingCaptureMaxTiltDeg = source.trackingCaptureMaxTiltDeg;
            trackingCaptureMaxAngularRateDegS = source.trackingCaptureMaxAngularRateDegS;
            trackingCaptureHoldSeconds = source.trackingCaptureHoldSeconds;
        }
    }
}
