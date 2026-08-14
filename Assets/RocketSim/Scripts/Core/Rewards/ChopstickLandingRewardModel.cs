// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ChopstickLandingRewardModel.cs
// Purpose: Scores chopstick descent/capture entirely from the selected task
// objective. There are no hidden reward weights or termination thresholds.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Rewards signed target closure and costs deviations from the desired
        /// ballistic descent profile. Positive state bonuses are avoided so the
        /// vehicle cannot profit simply by hovering indefinitely.
        /// </summary>
        static RewardDecision ChopstickLanding(
            RewardTerms t,
            RewardRuntimeContext ctx,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions)
        {
            RewardParameters r = objective.rewards;
            RewardShapingParameters s = objective.shaping;
            TerminationParameters end = objective.terminations;

            float heightAboveCapture = Mathf.Max(0f, ctx.altitude - ctx.terminalAltitude);
            float gravity = SafeScale(ctx.gravityMagnitude);
            float desiredDescentSpeed =
                Mathf.Sqrt(2f * gravity * heightAboveCapture) * s.landingBallisticDescentFraction;
            float desiredVerticalSpeed = -desiredDescentSpeed;
            float descentErrorScale = Mathf.Max(
                SafeScale(s.landingDescentErrorMinimumScaleMps),
                desiredDescentSpeed);
            float closureScale = Mathf.Max(
                SafeScale(s.landingClosureMinimumScaleMps),
                desiredDescentSpeed);

            float descentError01 = Mathf.Clamp01(
                Mathf.Abs(t.verticalSpeed - desiredVerticalSpeed) / descentErrorScale);
            float signedClosure = Mathf.Clamp(t.goalClosureRate / closureScale, -1f, 1f);
            float planarError01 = 1f - Exp01(
                Mathf.Max(0f, t.planarDistance), s.landingPlanarDistanceFalloffM);
            float uprightError01 = 1f - Mathf.Clamp01(t.upright01);
            float nearCapture01 = Exp01(heightAboveCapture, s.landingNearTargetAltitudeFalloffM);
            float planarSpeedError01 = Mathf.Clamp01(
                t.planarSpeed / SafeScale(s.landingPlanarSpeedScaleMps));
            float angularRateError01 = Mathf.Clamp01(
                t.angularRateDegS / SafeScale(s.landingAngularRateScaleDegS));
            float yawError01 = Mathf.Clamp01(
                Mathf.Abs(t.yawErrorDeg) / SafeScale(s.landingYawErrorScaleDeg));

            float shapingRate = 0f;
            shapingRate += RewardRate(contributions, RewardParameterId.LandingGoalClosureRewardRate,
                r.landingGoalClosureRewardRate, signedClosure);
            shapingRate += CostRate(contributions, RewardParameterId.LandingDescentProfileErrorCostRate,
                r.landingDescentProfileErrorCostRate, descentError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingPlanarDistanceCostRate,
                r.landingPlanarDistanceCostRate, planarError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingUprightErrorCostRate,
                r.landingUprightErrorCostRate, uprightError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingNearTargetPlanarSpeedCostRate,
                r.landingNearTargetPlanarSpeedCostRate, nearCapture01 * planarSpeedError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingNearTargetAngularRateCostRate,
                r.landingNearTargetAngularRateCostRate, nearCapture01 * angularRateError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingYawErrorCostRate,
                r.landingYawErrorCostRate, nearCapture01 * yawError01);
            shapingRate += CostRate(contributions, RewardParameterId.ControlEffortCostRate,
                r.controlEffortCostRate, Mathf.Clamp01(t.controlEffort));
            shapingRate += CostRate(contributions, RewardParameterId.TimeCostRate,
                r.timeCostRate, 1f);

            float eventReward = EventReward(
                contributions,
                RewardParameterId.StableCaptureReward,
                r.stableCaptureReward,
                ctx.landingPlatformBecameStable ? 1f : 0f);

            // Failure priority is deterministic. Irrecoverable/safety outcomes
            // win over a simultaneous capture or time-limit boundary.
            if (end.unsafeAttitudeEnabled && TiltDegrees(t.upDot) > end.maximumTiltDeg)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.ChopstickUnsafeAttitudeCost,
                        r.chopstickUnsafeAttitudeCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.ChopstickUnsafeAttitude);

            if (end.planarFlyawayEnabled && t.planarDistance > end.maximumPlanarDistanceM)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.ChopstickTooFarFromTargetCost,
                        r.chopstickTooFarFromTargetCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.ChopstickTooFarFromTarget);

            if (end.fuelDepletionEnabled && ctx.fuelKg <= end.minimumFuelKg)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.ChopstickFuelDepletedCost,
                        r.chopstickFuelDepletedCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.ChopstickFuelDepleted);

            if (end.altitudeCeilingEnabled &&
                ctx.altitude > ctx.episodeStartAltitude + end.maximumAltitudeAboveStartM)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.ChopstickAboveAltitudeLimitCost,
                        r.chopstickAboveAltitudeLimitCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.ChopstickAboveAltitudeLimit);

            if (end.chopstickCapturePlaneEnabled && ctx.altitude <= ctx.terminalAltitude)
            {
                bool success = ChopstickCaptureWithinLimits(t, ctx, end);
                return RewardDecision.Terminate(
                    shapingRate,
                    success
                        ? TerminalReward(contributions, RewardParameterId.ChopstickSuccessfulCaptureReward,
                            r.chopstickSuccessfulCaptureReward)
                        : TerminalCost(contributions, RewardParameterId.ChopstickFailedCaptureCost,
                            r.chopstickFailedCaptureCost),
                    successTerminal: success,
                    eventReward: eventReward,
                    terminationReason: success
                        ? EpisodeTerminationReason.ChopstickSuccessfulCapture
                        : EpisodeTerminationReason.ChopstickFailedCapture);
            }

            if (end.timeLimitEnabled && ctx.episodeElapsedSeconds >= end.maximumEpisodeSeconds)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.ChopstickTimeLimitCost,
                        r.chopstickTimeLimitCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.ChopstickTimeLimit);

            return RewardDecision.Continue(shapingRate, eventReward);
        }

        static bool ChopstickCaptureWithinLimits(
            RewardTerms t,
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            float difficulty = ctx.curriculumDifficulty01;
            bool platformReady = !end.chopstickRequireStablePlatform ||
                                 (ctx.landingPlatformInsideCapture &&
                                  ctx.landingPlatformStable &&
                                  ctx.landingPlatformStableTime >=
                                      end.landingStableHoldSeconds.At(difficulty));

            return platformReady &&
                   t.planarDistance <= end.landingSuccessRadiusM.At(difficulty) &&
                   t.speed <= end.landingSuccessMaxTotalSpeedMps.At(difficulty) &&
                   Mathf.Abs(t.verticalSpeed) <= end.landingSuccessMaxVerticalSpeedMps.At(difficulty) &&
                   t.planarSpeed <= end.landingSuccessMaxHorizontalSpeedMps.At(difficulty) &&
                   TiltDegrees(t.upDot) <= end.landingSuccessMaxTiltDeg.At(difficulty) &&
                   t.angularRateDegS <= end.landingSuccessMaxAngularRateDegS.At(difficulty) &&
                   Mathf.Abs(t.yawErrorDeg) <= end.landingSuccessMaxYawErrorDeg.At(difficulty);
        }
    }
}
