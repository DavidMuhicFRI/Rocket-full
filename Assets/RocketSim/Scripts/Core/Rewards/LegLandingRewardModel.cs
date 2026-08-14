// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/LegLandingRewardModel.cs
// Purpose: Scores powered leg landing and evaluates configurable, contact-driven
// outcomes without prescribing an engine-on or throttle schedule.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        static RewardDecision LegLanding(
            RewardTerms t,
            RewardRuntimeContext ctx,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions)
        {
            RewardParameters r = objective.rewards;
            RewardShapingParameters s = objective.shaping;
            TerminationParameters end = objective.terminations;

            float heightAbovePad = Mathf.Max(0f, ctx.altitude - ctx.terminalAltitude);
            float gravity = SafeScale(ctx.gravityMagnitude);
            float desiredDescentSpeed =
                Mathf.Sqrt(2f * gravity * heightAbovePad) * s.landingBallisticDescentFraction;
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
            float nearPad01 = Exp01(heightAbovePad, s.landingNearTargetAltitudeFalloffM);
            float planarSpeedError01 = Mathf.Clamp01(
                t.planarSpeed / SafeScale(s.landingPlanarSpeedScaleMps));
            float angularRateError01 = Mathf.Clamp01(
                t.angularRateDegS / SafeScale(s.landingAngularRateScaleDegS));
            float yawSpinError01 = Mathf.Clamp01(
                Mathf.Abs(t.yawRateDegS) / SafeScale(s.landingYawSpinScaleDegS));

            float shapingRate = 0f;
            shapingRate += RewardRate(contributions, RewardParameterId.LandingGoalClosureRewardRate,
                r.landingGoalClosureRewardRate, signedClosure);
            shapingRate += CostRate(contributions, RewardParameterId.LandingDescentProfileErrorCostRate,
                r.landingDescentProfileErrorCostRate, descentError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingPlanarDistanceCostRate,
                r.landingPlanarDistanceCostRate, planarError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingUprightErrorCostRate,
                r.landingUprightErrorCostRate, uprightError01);
            // The body-axis spin cost remains independent from total angular
            // rate so the policy cannot exploit fins to spin around vertical.
            shapingRate += CostRate(contributions, RewardParameterId.LandingYawSpinCostRate,
                r.landingYawSpinCostRate, yawSpinError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingNearTargetPlanarSpeedCostRate,
                r.landingNearTargetPlanarSpeedCostRate, nearPad01 * planarSpeedError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingNearTargetAngularRateCostRate,
                r.landingNearTargetAngularRateCostRate, nearPad01 * angularRateError01);
            shapingRate += CostRate(contributions, RewardParameterId.ControlEffortCostRate,
                r.controlEffortCostRate, Mathf.Clamp01(t.controlEffort));
            shapingRate += CostRate(contributions, RewardParameterId.TimeCostRate,
                r.timeCostRate, 1f);

            float eventReward = EventReward(
                contributions,
                RewardParameterId.FirstFootContactReward,
                r.firstFootContactReward,
                ctx.legFirstContactThisStep ? 1f : 0f);
            eventReward += EventReward(
                contributions,
                RewardParameterId.StableTouchdownReward,
                r.stableTouchdownReward,
                ctx.legBecameStable ? 1f : 0f);

            // Contact damage is checked before success, followed by airborne
            // safety/escape rules. This ordering resolves simultaneous events
            // consistently and prevents a structural strike being labeled safe.
            if (end.legStructuralStrikeEnabled && ctx.legStructuralStrike)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegStructuralStrikeCost,
                        r.legStructuralStrikeCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingStructuralStrike);

            if (end.legFootOutsidePadEnabled && ctx.legFootOutsidePad)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegFootOutsidePadCost,
                        r.legFootOutsidePadCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingFootOutsidePad);

            if (end.legHardFirstContactEnabled &&
                ctx.legFirstContactThisStep &&
                !FirstLegContactWithinLimits(ctx, end))
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegHardTouchdownCost,
                        r.legHardTouchdownCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingHardTouchdown);

            // Rebound combines configured rise and all-feet-contact-loss limits
            // in the contact evaluator. The flag is sticky for the episode.
            if (end.legExcessiveReboundEnabled && ctx.legExcessiveRebound)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegExcessiveReboundCost,
                        r.legExcessiveReboundCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingExcessiveRebound);

            if (end.legStableTouchdownEnabled && LegTouchdownWithinLimits(t, ctx, end))
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalReward(contributions, RewardParameterId.LegSuccessfulTouchdownReward,
                        r.legSuccessfulTouchdownReward),
                    successTerminal: true,
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingSuccessfulTouchdown);

            if (end.unsafeAttitudeEnabled && TiltDegrees(t.upDot) > end.maximumTiltDeg)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegUnsafeAttitudeCost,
                        r.legUnsafeAttitudeCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingUnsafeAttitude);

            if (end.planarFlyawayEnabled && t.planarDistance > end.maximumPlanarDistanceM)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegTooFarFromTargetCost,
                        r.legTooFarFromTargetCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingTooFarFromTarget);

            bool fuelFailureAllowed = !ctx.legTouchdownStarted ||
                                      !end.legAllowFuelDepletionAfterContact;
            if (end.fuelDepletionEnabled && fuelFailureAllowed && ctx.fuelKg <= end.minimumFuelKg)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegFuelDepletedCost,
                        r.legFuelDepletedCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingFuelDepleted);

            if (end.altitudeCeilingEnabled &&
                ctx.altitude > ctx.episodeStartAltitude + end.maximumAltitudeAboveStartM)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegAboveAltitudeLimitCost,
                        r.legAboveAltitudeLimitCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingAboveAltitudeLimit);

            if (end.legMissedPadEnabled &&
                !ctx.legTouchdownStarted &&
                ctx.altitude < ctx.terminalAltitude - end.legMissedPadDepthM)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegMissedPadCost,
                        r.legMissedPadCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingMissedPad);

            if (end.timeLimitEnabled && ctx.episodeElapsedSeconds >= end.maximumEpisodeSeconds)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegTimeLimitCost,
                        r.legTimeLimitCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingTimeLimit);

            return RewardDecision.Continue(shapingRate, eventReward);
        }

        static bool FirstLegContactWithinLimits(
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            float difficulty = ctx.curriculumDifficulty01;
            return ctx.legFirstContactSpeed <= end.landingSuccessMaxTotalSpeedMps.At(difficulty) &&
                   Mathf.Abs(ctx.legFirstContactVerticalSpeed) <=
                       end.landingSuccessMaxVerticalSpeedMps.At(difficulty) &&
                   ctx.legFirstContactHorizontalSpeed <=
                       end.landingSuccessMaxHorizontalSpeedMps.At(difficulty) &&
                   ctx.legFirstContactTiltDeg <= end.landingSuccessMaxTiltDeg.At(difficulty) &&
                   ctx.legFirstContactAngularRateDegS <=
                       end.landingSuccessMaxAngularRateDegS.At(difficulty);
        }

        static bool LegTouchdownWithinLimits(
            RewardTerms t,
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            float difficulty = ctx.curriculumDifficulty01;
            return ctx.legStable &&
                   ctx.legFeetOnPad >= end.legMinimumStableFeet &&
                   ctx.legStableTime >= end.landingStableHoldSeconds.At(difficulty) &&
                   t.planarDistance <= end.landingSuccessRadiusM.At(difficulty) &&
                   t.speed <= end.landingSuccessMaxTotalSpeedMps.At(difficulty) &&
                   Mathf.Abs(t.verticalSpeed) <= end.landingSuccessMaxVerticalSpeedMps.At(difficulty) &&
                   t.planarSpeed <= end.landingSuccessMaxHorizontalSpeedMps.At(difficulty) &&
                   TiltDegrees(t.upDot) <= end.landingSuccessMaxTiltDeg.At(difficulty) &&
                   t.angularRateDegS <= end.landingSuccessMaxAngularRateDegS.At(difficulty);
        }
    }
}
