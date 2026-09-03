// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/HoverRewardModel.cs
// Purpose: Scores fixed and moving-target hover from explicit objective values,
// including capture events and an optional capture-count success terminal.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        static RewardDecision Hover(
            RewardTerms t,
            RewardRuntimeContext ctx,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions)
        {
            float shapingRate = HoverBaselineRate(t, objective, contributions);
            float eventReward = HoverRestartEvent(ctx, objective.rewards, contributions);

            if (TryHoverSafetyTermination(t, ctx, objective, contributions, shapingRate, eventReward, out RewardDecision failure))
                return failure;

            if (objective.terminations.timeLimitEnabled && ctx.episodeElapsedSeconds >= objective.terminations.maximumEpisodeSeconds)
                return RewardDecision.Terminate(shapingRate, TerminalCost(contributions, RewardParameterId.HoverTimeLimitCost, objective.rewards.hoverTimeLimitCost), eventReward: eventReward, terminationReason: EpisodeTerminationReason.HoverTimeLimit);
            return RewardDecision.Continue(shapingRate, eventReward);
        }

        static RewardDecision HoverTracking(
            RewardTerms t,
            RewardRuntimeContext ctx,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions)
        {
            RewardParameters r = objective.rewards;
            RewardShapingParameters s = objective.shaping;
            TerminationParameters end = objective.terminations;
            float captureRadius = SafeScale(end.trackingCaptureRadiusM.At(ctx.curriculumDifficulty01));
            bool settlePhase = t.planarDistance <= captureRadius;

            float shapingRate = HoverBaselineRate(t, objective, contributions);
            if (settlePhase)
            {
                float tightCenterFalloff = Mathf.Max(SafeScale(s.trackingTightCenterMinimumFalloffM), captureRadius * s.trackingTightCenterRadiusFraction);
                float tightCenter01 = Exp01(t.planarDistance, tightCenterFalloff);
                float horizontalCalm01 = Exp01(t.planarSpeed, s.trackingHorizontalCalmFalloffMps);
                float verticalCalm01 = Exp01(t.verticalSpeed, s.trackingVerticalCalmFalloffMps);
                float rotationCalm01 = Exp01(t.angularRateDegS, s.trackingRotationCalmFalloffDegS);

                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingSettleCenterRewardRate,
                    r.trackingSettleCenterRewardRate, tightCenter01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingSettleHorizontalCalmRewardRate,
                    r.trackingSettleHorizontalCalmRewardRate, horizontalCalm01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingSettleVerticalCalmRewardRate,
                    r.trackingSettleVerticalCalmRewardRate, verticalCalm01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingSettleRotationCalmRewardRate,
                    r.trackingSettleRotationCalmRewardRate, rotationCalm01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingSettleCompositeRewardRate,
                    r.trackingSettleCompositeRewardRate,
                    tightCenter01 * horizontalCalm01 * verticalCalm01 * t.upright01);
                shapingRate += CostRate(contributions,
                    RewardParameterId.TrackingSettlePlanarSpeedCostRate,
                    r.trackingSettlePlanarSpeedCostRate, t.planarSpeed);
                shapingRate += CostRate(contributions,
                    RewardParameterId.TrackingSettleVerticalSpeedCostRate,
                    r.trackingSettleVerticalSpeedCostRate, Mathf.Abs(t.verticalSpeed));
            }
            else
            {
                float distanceBeyondCapture = Mathf.Max(0f, t.planarDistance - captureRadius);
                float approachSpeed01 = Mathf.Clamp01(
                    t.horizontalClosureRate / SafeScale(s.trackingApproachSpeedScaleMps));
                float movingAwaySpeed = Mathf.Max(0f, -t.horizontalClosureRate);
                float direction01 = t.planarSpeed > s.trackingDirectionMinimumSpeedMps
                    ? Mathf.Clamp01(
                        (t.horizontalClosureRate / SafeScale(t.planarSpeed) + 1f) * 0.5f)
                    : 0f;
                float usefulSpeed01 = Mathf.Clamp01(
                    t.planarSpeed / SafeScale(s.trackingUsefulSpeedScaleMps));
                float farDistanceScale = SafeScale(
                    captureRadius * s.trackingFarDistanceScaleMultiplier);
                float farFromCapture01 = Mathf.Clamp01(distanceBeyondCapture / farDistanceScale);
                float overspeed01 = Mathf.Clamp01(
                    Mathf.Max(0f, t.planarSpeed - s.trackingOverspeedThresholdMps) /
                    SafeScale(s.trackingOverspeedRangeMps));

                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingApproachClosureRewardRate,
                    r.trackingApproachClosureRewardRate, approachSpeed01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingApproachDirectionRewardRate,
                    r.trackingApproachDirectionRewardRate, direction01);
                shapingRate += RewardRate(contributions,
                    RewardParameterId.TrackingUsefulSpeedRewardRate,
                    r.trackingUsefulSpeedRewardRate, usefulSpeed01);
                shapingRate += CostRate(contributions,
                    RewardParameterId.TrackingMovingAwayCostRate,
                    r.trackingMovingAwayCostRate,
                    Mathf.Clamp01(movingAwaySpeed /
                                  SafeScale(s.trackingMovingAwaySpeedScaleMps)));
                shapingRate += CostRate(contributions,
                    RewardParameterId.TrackingLoiteringCostRate,
                    r.trackingLoiteringCostRate, farFromCapture01 * (1f - usefulSpeed01));
                shapingRate += CostRate(contributions,
                    RewardParameterId.TrackingOverspeedCostRate,
                    r.trackingOverspeedCostRate, overspeed01);
            }

            float eventReward = HoverRestartEvent(ctx, r, contributions);
            eventReward += EventReward(
                contributions,
                RewardParameterId.TrackingTargetCaptureReward,
                r.trackingTargetCaptureReward,
                ctx.hoverTrackTargetCapturedThisStep ? 1f : 0f);

            if (TryHoverSafetyTermination(
                    t, ctx, objective, contributions, shapingRate, eventReward,
                    out RewardDecision failure))
                return failure;

            // A capture goal is evaluated only on a new capture event. Safety
            // failures above intentionally take precedence on the same step.
            if (end.trackingCaptureGoalEnabled &&
                ctx.hoverTrackTargetCapturedThisStep &&
                end.trackingRequiredCaptures > 0 &&
                ctx.hoverTrackEpisodeCaptures >= end.trackingRequiredCaptures)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalReward(contributions, RewardParameterId.TrackingCaptureGoalReward,
                        r.trackingCaptureGoalReward),
                    successTerminal: true,
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.HoverTrackingCaptureGoal);

            // A success on the exact limit boundary wins over the time limit.
            if (end.timeLimitEnabled && ctx.episodeElapsedSeconds >= end.maximumEpisodeSeconds)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.HoverTimeLimitCost,
                        r.hoverTimeLimitCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.HoverTimeLimit);

            return RewardDecision.Continue(shapingRate, eventReward);
        }

        static float HoverBaselineRate(
            RewardTerms t,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions)
        {
            RewardParameters r = objective.rewards;
            RewardShapingParameters s = objective.shaping;
            float altitudeProximity01 = Exp01(t.verticalError, s.hoverAltitudeFalloffM);
            float planarProximity01 = Exp01(t.planarDistance, s.hoverPlanarFalloffM);

            // HoverSimple4 exposed a reward-hacking path in the old additive
            // baseline: a calm, upright vehicle could earn most of the reward
            // indefinitely while remaining far above the commanded altitude.
            // Gate every positive term by joint target proximity so satisfying
            // only one position axis, or merely surviving elsewhere, cannot
            // amortize a later safety failure.  The geometric mean keeps a
            // usable gradient at the 50 m training spawn while still reaching
            // exactly one only at the complete hover target.
            float targetProximity01 = Mathf.Sqrt(altitudeProximity01 * planarProximity01);
            float rate = 0f;
            rate += RewardRate(contributions, RewardParameterId.HoverAltitudeProximityRewardRate,
                r.hoverAltitudeProximityRewardRate,
                altitudeProximity01 * targetProximity01);
            rate += RewardRate(contributions, RewardParameterId.HoverPlanarProximityRewardRate,
                r.hoverPlanarProximityRewardRate,
                planarProximity01 * targetProximity01);
            rate += RewardRate(contributions, RewardParameterId.HoverUprightRewardRate,
                r.hoverUprightRewardRate, t.upright01 * targetProximity01);
            rate += RewardRate(contributions, RewardParameterId.HoverSpeedCalmRewardRate,
                r.hoverSpeedCalmRewardRate,
                Exp01(t.speed, s.hoverSpeedFalloffMps) * targetProximity01);
            rate += RewardRate(contributions, RewardParameterId.HoverRotationCalmRewardRate,
                r.hoverRotationCalmRewardRate,
                Exp01(t.angularRateDegS, s.hoverAngularRateFalloffDegS) * targetProximity01);
            rate += CostRate(contributions, RewardParameterId.HoverLinearSpeedCostRate,
                r.hoverLinearSpeedCostRate, t.speed);
            rate += CostRate(contributions, RewardParameterId.HoverAngularRateCostRate,
                r.hoverAngularRateCostRate, t.angularRateDegS);
            rate += CostRate(contributions, RewardParameterId.ControlEffortCostRate,
                r.controlEffortCostRate, t.controlEffort);
            return rate;
        }

        static float HoverRestartEvent(
            RewardRuntimeContext ctx,
            RewardParameters rewards,
            RewardContributionBuffer contributions) =>
            EventCost(
                contributions,
                RewardParameterId.EngineRestartCost,
                rewards.engineRestartCost,
                Mathf.Max(0, ctx.engineRestartsThisStep));

        /// <summary>
        /// Evaluates shared hover safety rules in stable priority order:
        /// ground, attitude, fuel, horizontal escape, then altitude escape.
        /// </summary>
        static bool TryHoverSafetyTermination(
            RewardTerms t,
            RewardRuntimeContext ctx,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions,
            float shapingRate,
            float eventReward,
            out RewardDecision decision)
        {
            RewardParameters r = objective.rewards;
            TerminationParameters end = objective.terminations;

            if (end.hoverGroundImpactEnabled && ctx.altitude <= ctx.terminalAltitude + end.hoverGroundClearanceM)
            {
                decision = HoverFailure(shapingRate, eventReward, contributions,
                    RewardParameterId.HoverGroundImpactCost, r.hoverGroundImpactCost,
                    EpisodeTerminationReason.HoverGroundImpact);
                return true;
            }

            if (end.unsafeAttitudeEnabled && TiltDegrees(t.upDot) > end.maximumTiltDeg)
            {
                decision = HoverFailure(shapingRate, eventReward, contributions,
                    RewardParameterId.HoverUnsafeAttitudeCost, r.hoverUnsafeAttitudeCost,
                    EpisodeTerminationReason.HoverUnsafeAttitude);
                return true;
            }

            if (end.fuelDepletionEnabled && ctx.fuelKg <= end.minimumFuelKg)
            {
                decision = HoverFailure(shapingRate, eventReward, contributions,
                    RewardParameterId.HoverFuelDepletedCost, r.hoverFuelDepletedCost,
                    EpisodeTerminationReason.HoverFuelDepleted);
                return true;
            }

            if (end.planarFlyawayEnabled && t.planarDistance > end.maximumPlanarDistanceM)
            {
                decision = HoverFailure(shapingRate, eventReward, contributions,
                    RewardParameterId.HoverTooFarFromTargetCost, r.hoverTooFarFromTargetCost,
                    EpisodeTerminationReason.HoverTooFarFromTarget);
                return true;
            }

            if (end.altitudeCeilingEnabled && ctx.altitude > end.hoverMaximumAltitudeM)
            {
                decision = HoverFailure(shapingRate, eventReward, contributions,
                    RewardParameterId.HoverAboveAltitudeLimitCost, r.hoverAboveAltitudeLimitCost,
                    EpisodeTerminationReason.HoverAboveAltitudeLimit);
                return true;
            }

            decision = RewardDecision.None;
            return false;
        }

        static RewardDecision HoverFailure(
            float shapingRate,
            float eventReward,
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude,
            EpisodeTerminationReason reason) =>
            RewardDecision.Terminate(
                shapingRate,
                TerminalCost(contributions, id, magnitude),
                eventReward: eventReward,
                terminationReason: reason);
    }
}
