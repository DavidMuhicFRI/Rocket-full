// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardPresetCatalog.cs
// Purpose: Complete named vectors of absolute reward magnitudes.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace RocketSim
{
    /// <summary>
    /// Built-in presets are starting points, not runtime multipliers. Applying
    /// one clears every reward field and writes a complete vector; fields that
    /// do not apply remain explicit zeroes.
    /// </summary>
    public static class RewardPresetCatalog
    {
        public const string BalancedPresetName = "Balanced";

        static readonly string[] ChopstickPresets =
        {
            BalancedPresetName, "Chopstick Catch", "Descent Focus", "Soft Landing",
            "Upright Focus", "Target Precision", "Efficient Control"
        };
        static readonly string[] LegPresets =
        {
            BalancedPresetName, "Soft Landing", "Descent Focus", "Upright Focus",
            "Target Precision", "Efficient Control"
        };
        static readonly string[] HoverPresets =
        {
            BalancedPresetName, "Stable Hover", "Speed Discipline", "Target Precision",
            "Efficient Control"
        };
        static readonly string[] TrackingPresets =
        {
            BalancedPresetName, "Track And Settle", "Fast Approach", "Stable Hover",
            "Speed Discipline", "Efficient Control"
        };

        public static IReadOnlyList<string> ForScenario(ScenarioType scenario) => scenario switch
        {
            ScenarioType.ChopstickLanding => ChopstickPresets,
            ScenarioType.LegLanding => LegPresets,
            ScenarioType.Hover => HoverPresets,
            ScenarioType.HoverTracking => TrackingPresets,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unsupported objective scenario.")
        };

        public static void Apply(
            ScenarioObjectiveConfig objective,
            ScenarioType scenario,
            string presetName)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            if (string.IsNullOrWhiteSpace(presetName))
                throw new ArgumentException("A preset name is required.", nameof(presetName));

            RewardParameters values = CreateValues(scenario, presetName);
            objective.rewards ??= new RewardParameters();
            objective.rewards.CopyFrom(values);
            objective.basePresetName = presetName;
            objective.presetName = presetName;
        }

        /// <summary>Creates a detached vector, useful for per-row preset reset.</summary>
        public static RewardParameters CreateValues(ScenarioType scenario, string presetName)
        {
            if (!Contains(ForScenario(scenario), presetName))
                throw new ArgumentException(
                    $"Preset '{presetName}' is not defined for {scenario}.", nameof(presetName));

            var p = new RewardParameters();
            p.Clear();
            ApplyBalanced(p, scenario);
            ApplyNamedVariant(p, scenario, presetName);
            return p;
        }

        public static float GetPresetValue(
            ScenarioType scenario,
            string presetName,
            RewardParameterId parameterId)
        {
            RewardParameterDescriptor descriptor = RewardParameterCatalog.Find(parameterId)
                ?? throw new ArgumentOutOfRangeException(nameof(parameterId), parameterId, "Unknown reward parameter.");
            return descriptor.GetValue(CreateValues(scenario, presetName));
        }

        static bool Contains(IReadOnlyList<string> values, string requested)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], requested, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static void ApplyBalanced(RewardParameters p, ScenarioType scenario)
        {
            switch (scenario)
            {
                case ScenarioType.ChopstickLanding:
                    ApplyLandingGuidance(p);
                    p.landingYawErrorCostRate = 0.015f;
                    p.stableCaptureReward = 1f;
                    p.chopstickSuccessfulCaptureReward = 10f;
                    p.chopstickFailedCaptureCost = 5f;
                    p.chopstickUnsafeAttitudeCost = 5f;
                    p.chopstickTooFarFromTargetCost = 5f;
                    p.chopstickFuelDepletedCost = 5f;
                    p.chopstickAboveAltitudeLimitCost = 5f;
                    p.chopstickTimeLimitCost = 5f;
                    return;

                case ScenarioType.LegLanding:
                    // Horizontal progress is evaluated independently from the
                    // ballistic descent profile in the leg reward model. This
                    // prevents falling toward the deck from masquerading as
                    // successful navigation toward its center.
                    p.landingGoalClosureRewardRate = 0.500f;
                    // L10's difficult-band failures were already far too fast
                    // by 100 m and violated the vertical contact envelope much
                    // more often than attitude or pad limits. Strengthen the
                    // existing descent target without rewarding ascent or
                    // prescribing an ignition schedule.
                    p.landingDescentProfileErrorCostRate = 0.150f;
                    p.landingPlanarDistanceCostRate = 0.040f;
                    p.landingUprightErrorCostRate = 0.150f;
                    p.landingYawSpinCostRate = 0.025f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.040f;
                    p.landingNearTargetAngularRateCostRate = 0.100f;
                    p.landingNearTargetVerticalSpeedCostRate = 0.120f;
                    p.landingAngularRateCostRate = 0.010f;
                    // A bounded center-only potential provides a clear planar
                    // navigation signal at every altitude. Approach quality is
                    // handled by the independent speed, tilt, and rate terms so
                    // passive descent cannot manufacture positive progress.
                    p.landingReadinessProgressRewardRate = 4f;
                    // Upward motion is always contrary to this descent task.
                    // Penalize the state, not any particular engine choice,
                    // so the policy keeps full three-engine authority.
                    p.landingUpwardVelocityCostRate = 0.300f;
                    p.controlEffortCostRate = 0.003f;
                    // Do not pay the policy to end a failed attempt quickly.
                    // Mission efficiency is scored only after a legal touchdown.
                    p.timeCostRate = 0f;

                    // Merely touching is not success. The useful milestone is
                    // a sustained four-foot, propulsion-off stable state.
                    p.firstFootContactReward = 0f;
                    p.stableTouchdownReward = 2f;
                    p.legSuccessfulTouchdownReward = 30f;
                    // This remains success-only: failed attempts cannot earn a
                    // reward by refusing to burn. It is large enough to separate
                    // a smooth one-to-three-engine mission from L10-style PWM,
                    // while touchdown quality still dominates the objective.
                    p.legSuccessfulFuelEfficiencyReward = 6f;

                    // A normal L6 hard arrival cost only about -12, making a
                    // reliable crash much cheaper than a -50 escape. Put the
                    // active touchdown boundary at -25 and grade increasingly
                    // unsafe contact up to -45, still below non-contact escape.
                    p.legHardTouchdownCost = 25f;
                    p.legImpactSeverityCost = 20f;
                    p.legStructuralStrikeCost = 25f;
                    p.legFootOutsidePadCost = 25f;
                    p.legExcessiveReboundCost = 25f;
                    p.legUnsafeAttitudeCost = 16f;
                    // Airborne escape must remain worse than a physical
                    // landing attempt even after the policy accounts for the
                    // per-second cost it avoids by resetting early.
                    p.legTooFarFromTargetCost = 50f;
                    p.legFuelDepletedCost = 50f;
                    p.legAboveAltitudeLimitCost = 50f;
                    p.legMissedPadCost = 50f;
                    p.legTimeLimitCost = 50f;
                    return;

                case ScenarioType.Hover:
                    ApplyHoverBaseline(p);
                    ApplyHoverFailures(p);
                    return;

                case ScenarioType.HoverTracking:
                    ApplyHoverBaseline(p);
                    ApplyHoverFailures(p);
                    p.trackingSettleCenterRewardRate = 0.045f;
                    p.trackingSettleHorizontalCalmRewardRate = 0.035f;
                    p.trackingSettleVerticalCalmRewardRate = 0.025f;
                    p.trackingSettleRotationCalmRewardRate = 0.020f;
                    p.trackingSettleCompositeRewardRate = 0.020f;
                    p.trackingSettlePlanarSpeedCostRate = 0.0005f;
                    p.trackingSettleVerticalSpeedCostRate = 0.0004f;
                    p.trackingApproachClosureRewardRate = 0.085f;
                    p.trackingApproachDirectionRewardRate = 0.040f;
                    p.trackingUsefulSpeedRewardRate = 0.020f;
                    p.trackingMovingAwayCostRate = 0.045f;
                    p.trackingLoiteringCostRate = 0.025f;
                    p.trackingOverspeedCostRate = 0.020f;
                    p.trackingTargetCaptureReward = 4f;
                    // Capture-count termination is disabled by default, so its
                    // terminal signal is explicitly zero until the user sets it.
                    p.trackingCaptureGoalReward = 0f;
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unsupported objective scenario.");
            }
        }

        static void ApplyLandingGuidance(RewardParameters p)
        {
            p.landingGoalClosureRewardRate = 0.060f;
            p.landingDescentProfileErrorCostRate = 0.040f;
            p.landingPlanarDistanceCostRate = 0.025f;
            p.landingUprightErrorCostRate = 0.020f;
            p.landingNearTargetPlanarSpeedCostRate = 0.025f;
            p.landingNearTargetAngularRateCostRate = 0.015f;
            p.controlEffortCostRate = 0.005f;
            p.timeCostRate = 0.100f;
        }

        static void ApplyHoverBaseline(RewardParameters p)
        {
            p.hoverAltitudeProximityRewardRate = 0.120f;
            p.hoverPlanarProximityRewardRate = 0.120f;
            p.hoverUprightRewardRate = 0.070f;
            p.hoverSpeedCalmRewardRate = 0.060f;
            p.hoverRotationCalmRewardRate = 0.040f;
            p.hoverLinearSpeedCostRate = 0.0015f;
            p.hoverAngularRateCostRate = 0.0008f;
            p.controlEffortCostRate = 0.0025f;
            p.engineRestartCost = 0.25f;
        }

        static void ApplyHoverFailures(RewardParameters p)
        {
            p.hoverGroundImpactCost = 10f;
            p.hoverUnsafeAttitudeCost = 10f;
            p.hoverFuelDepletedCost = 10f;
            p.hoverTooFarFromTargetCost = 10f;
            p.hoverAboveAltitudeLimitCost = 10f;
            // Optional time-limit termination is disabled in the defaults.
            p.hoverTimeLimitCost = 0f;
        }

        static void ApplyNamedVariant(RewardParameters p, ScenarioType scenario, string name)
        {
            if (name == BalancedPresetName) return;

            switch (scenario)
            {
                case ScenarioType.ChopstickLanding:
                    ApplyChopstickVariant(p, name);
                    break;
                case ScenarioType.LegLanding:
                    ApplyLegVariant(p, name);
                    break;
                case ScenarioType.Hover:
                    ApplyHoverVariant(p, name);
                    break;
                case ScenarioType.HoverTracking:
                    ApplyTrackingVariant(p, name);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unsupported objective scenario.");
            }
        }

        static void ApplyChopstickVariant(RewardParameters p, string name)
        {
            switch (name)
            {
                case "Chopstick Catch":
                    p.landingPlanarDistanceCostRate = 0.0425f;
                    p.landingYawErrorCostRate = 0.02475f;
                    p.landingUprightErrorCostRate = 0.025f;
                    p.landingNearTargetAngularRateCostRate = 0.018f;
                    p.landingGoalClosureRewardRate = 0.066f;
                    p.landingDescentProfileErrorCostRate = 0.044f;
                    break;
                case "Descent Focus":
                    p.landingGoalClosureRewardRate = 0.102f;
                    p.landingDescentProfileErrorCostRate = 0.068f;
                    p.landingPlanarDistanceCostRate = 0.02875f;
                    p.landingUprightErrorCostRate = 0.022f;
                    break;
                case "Soft Landing":
                    ApplySoftLanding(p, includeYawSpin: false);
                    break;
                case "Upright Focus":
                    p.landingUprightErrorCostRate = 0.034f;
                    p.landingNearTargetAngularRateCostRate = 0.01875f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.0275f;
                    break;
                case "Target Precision":
                    p.landingPlanarDistanceCostRate = 0.04125f;
                    p.stableCaptureReward = 1.25f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.0275f;
                    break;
                case "Efficient Control":
                    p.controlEffortCostRate = 0.009f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.02875f;
                    p.landingNearTargetAngularRateCostRate = 0.01725f;
                    break;
            }
        }

        static void ApplyLegVariant(RewardParameters p, string name)
        {
            switch (name)
            {
                case "Soft Landing":
                    p.landingDescentProfileErrorCostRate = 0.065f;
                    p.landingUprightErrorCostRate = 0.050f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.055f;
                    p.landingNearTargetAngularRateCostRate = 0.045f;
                    p.landingNearTargetVerticalSpeedCostRate = 0.120f;
                    p.landingAngularRateCostRate = 0.015f;
                    break;
                case "Descent Focus":
                    p.landingGoalClosureRewardRate = 0.135f;
                    p.landingDescentProfileErrorCostRate = 0.080f;
                    p.landingNearTargetVerticalSpeedCostRate = 0.100f;
                    break;
                case "Upright Focus":
                    p.landingUprightErrorCostRate = 0.065f;
                    p.landingNearTargetAngularRateCostRate = 0.050f;
                    p.landingAngularRateCostRate = 0.020f;
                    p.landingYawSpinCostRate = 0.040f;
                    break;
                case "Target Precision":
                    p.landingGoalClosureRewardRate = 0.150f;
                    p.landingPlanarDistanceCostRate = 0.065f;
                    p.stableTouchdownReward = 2.5f;
                    p.landingNearTargetPlanarSpeedCostRate = 0.050f;
                    break;
                case "Efficient Control":
                    p.controlEffortCostRate = 0.006f;
                    p.legSuccessfulFuelEfficiencyReward = 5f;
                    break;
            }
        }

        static void ApplySoftLanding(RewardParameters p, bool includeYawSpin)
        {
            p.landingUprightErrorCostRate = 0.026f;
            p.landingPlanarDistanceCostRate = 0.030f;
            p.landingNearTargetAngularRateCostRate = 0.018f;
            if (includeYawSpin) p.landingYawSpinCostRate = 0.048f;
            p.landingGoalClosureRewardRate = 0.066f;
            p.landingDescentProfileErrorCostRate = 0.044f;
        }

        static void ApplyHoverVariant(RewardParameters p, string name)
        {
            switch (name)
            {
                case "Stable Hover":
                    ApplyStableHover(p);
                    break;
                case "Speed Discipline":
                    p.hoverSpeedCalmRewardRate = 0.102f;
                    p.hoverLinearSpeedCostRate = 0.00255f;
                    break;
                case "Target Precision":
                    p.hoverPlanarProximityRewardRate = 0.198f;
                    p.hoverAltitudeProximityRewardRate = 0.150f;
                    p.hoverSpeedCalmRewardRate = 0.066f;
                    p.hoverLinearSpeedCostRate = 0.00165f;
                    break;
                case "Efficient Control":
                    p.controlEffortCostRate = 0.0045f;
                    p.hoverSpeedCalmRewardRate = 0.069f;
                    p.hoverLinearSpeedCostRate = 0.001725f;
                    p.hoverRotationCalmRewardRate = 0.046f;
                    p.hoverAngularRateCostRate = 0.00092f;
                    break;
            }
        }

        static void ApplyTrackingVariant(RewardParameters p, string name)
        {
            switch (name)
            {
                case "Track And Settle":
                    p.hoverSpeedCalmRewardRate = 0.072f;
                    p.hoverLinearSpeedCostRate = 0.0018f;
                    p.trackingApproachClosureRewardRate = 0.1275f;
                    p.trackingApproachDirectionRewardRate = 0.060f;
                    p.trackingMovingAwayCostRate = 0.0675f;
                    p.trackingLoiteringCostRate = 0.0375f;
                    p.trackingSettleCenterRewardRate = 0.06525f;
                    p.trackingSettleCompositeRewardRate = 0.029f;
                    p.trackingSettleHorizontalCalmRewardRate = 0.042f;
                    p.trackingSettlePlanarSpeedCostRate = 0.0006f;
                    p.trackingUsefulSpeedRewardRate = 0.024f;
                    p.trackingOverspeedCostRate = 0.024f;
                    p.trackingSettleVerticalCalmRewardRate = 0.02875f;
                    p.trackingSettleVerticalSpeedCostRate = 0.00046f;
                    break;
                case "Fast Approach":
                    p.hoverSpeedCalmRewardRate = 0.054f;
                    p.hoverLinearSpeedCostRate = 0.00135f;
                    p.trackingApproachClosureRewardRate = 0.1445f;
                    p.trackingApproachDirectionRewardRate = 0.068f;
                    p.trackingMovingAwayCostRate = 0.0765f;
                    p.trackingLoiteringCostRate = 0.0425f;
                    p.trackingSettleCenterRewardRate = 0.0405f;
                    p.trackingSettleCompositeRewardRate = 0.018f;
                    p.trackingSettleHorizontalCalmRewardRate = 0.0315f;
                    p.trackingSettlePlanarSpeedCostRate = 0.00045f;
                    p.trackingUsefulSpeedRewardRate = 0.018f;
                    p.trackingOverspeedCostRate = 0.018f;
                    p.trackingSettleVerticalCalmRewardRate = 0.0225f;
                    p.trackingSettleVerticalSpeedCostRate = 0.00036f;
                    break;
                case "Stable Hover":
                    ApplyStableHover(p);
                    p.trackingSettleHorizontalCalmRewardRate = 0.04725f;
                    p.trackingSettlePlanarSpeedCostRate = 0.000675f;
                    p.trackingUsefulSpeedRewardRate = 0.027f;
                    p.trackingOverspeedCostRate = 0.027f;
                    p.trackingSettleRotationCalmRewardRate = 0.026f;
                    break;
                case "Speed Discipline":
                    p.hoverSpeedCalmRewardRate = 0.102f;
                    p.hoverLinearSpeedCostRate = 0.00255f;
                    p.trackingApproachClosureRewardRate = 0.0765f;
                    p.trackingApproachDirectionRewardRate = 0.036f;
                    p.trackingMovingAwayCostRate = 0.0405f;
                    p.trackingLoiteringCostRate = 0.0225f;
                    p.trackingSettleCenterRewardRate = 0.05625f;
                    p.trackingSettleCompositeRewardRate = 0.025f;
                    p.trackingSettleHorizontalCalmRewardRate = 0.0595f;
                    p.trackingSettlePlanarSpeedCostRate = 0.00085f;
                    p.trackingUsefulSpeedRewardRate = 0.034f;
                    p.trackingOverspeedCostRate = 0.034f;
                    p.trackingSettleVerticalCalmRewardRate = 0.040f;
                    p.trackingSettleVerticalSpeedCostRate = 0.00064f;
                    break;
                case "Efficient Control":
                    ApplyHoverVariant(p, name);
                    p.trackingSettleHorizontalCalmRewardRate = 0.04025f;
                    p.trackingSettlePlanarSpeedCostRate = 0.000575f;
                    p.trackingUsefulSpeedRewardRate = 0.023f;
                    p.trackingOverspeedCostRate = 0.023f;
                    p.trackingSettleRotationCalmRewardRate = 0.023f;
                    break;
            }
        }

        static void ApplyStableHover(RewardParameters p)
        {
            p.hoverAltitudeProximityRewardRate = 0.174f;
            p.hoverPlanarProximityRewardRate = 0.150f;
            p.hoverUprightRewardRate = 0.091f;
            p.hoverSpeedCalmRewardRate = 0.081f;
            p.hoverLinearSpeedCostRate = 0.002025f;
            p.hoverRotationCalmRewardRate = 0.052f;
            p.hoverAngularRateCostRate = 0.00104f;
        }
    }
}
