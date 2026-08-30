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
            // Leg landing separates navigation from descent. Using 3D closure
            // here lets passive vertical fall dominate the signal and teaches
            // the policy that it can ignore horizontal error.
            float closureScale = SafeScale(s.landingClosureMinimumScaleMps);

            float descentError01 = Mathf.Clamp01(
                Mathf.Abs(t.verticalSpeed - desiredVerticalSpeed) / descentErrorScale);
            float signedHorizontalClosure = Mathf.Clamp(
                t.horizontalClosureRate / closureScale,
                -1f,
                1f);
            float planarError01 = 1f - Exp01(
                Mathf.Max(0f, t.planarDistance), s.landingPlanarDistanceFalloffM);
            float nearPad01 = Exp01(heightAbovePad, s.landingNearTargetAltitudeFalloffM);
            // 1-upDot is almost flat at the small angles that determine a
            // one-foot versus four-foot Falcon landing. Grade tilt directly,
            // with a near-pad gate so useful high-altitude steering is not
            // punished like a tilted touchdown approach.
            float uprightTiltScaleDeg = Mathf.Max(
                6f,
                2f * end.landingSuccessMaxTiltDeg.At(ctx.landingCriteriaDifficulty01));
            float uprightError01 = nearPad01 * Mathf.Clamp01(
                TiltDegrees(t.upDot) / uprightTiltScaleDeg);
            float planarSpeedError01 = Mathf.Clamp01(
                t.planarSpeed / SafeScale(s.landingPlanarSpeedScaleMps));
            float angularRateError01 = Mathf.Clamp01(
                t.angularRateDegS / SafeScale(s.landingAngularRateScaleDegS));
            float verticalSpeedLimit =
                end.landingSuccessMaxVerticalSpeedMps.At(ctx.landingCriteriaDifficulty01);
            float verticalSpeedExcess01 = Mathf.Clamp01(
                (Mathf.Abs(t.verticalSpeed) - verticalSpeedLimit) /
                SafeScale(s.landingVerticalSpeedExcessScaleMps));
            float upwardVelocityExcess01 = Mathf.Clamp01(
                (t.verticalSpeed - s.landingUpwardVelocityToleranceMps) /
                SafeScale(s.landingUpwardVelocityScaleMps));
            float yawSpinError01 = Mathf.Clamp01(
                Mathf.Abs(t.yawRateDegS) / SafeScale(s.landingYawSpinScaleDegS));

            float shapingRate = 0f;
            shapingRate += RewardRate(contributions, RewardParameterId.LandingGoalClosureRewardRate,
                r.landingGoalClosureRewardRate, signedHorizontalClosure);
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
            // Near-target channels remain independently editable so vertical,
            // planar, and rotational flare behavior can be tuned separately.
            shapingRate += CostRate(contributions, RewardParameterId.LandingNearTargetVerticalSpeedCostRate,
                r.landingNearTargetVerticalSpeedCostRate, nearPad01 * verticalSpeedExcess01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingUpwardVelocityCostRate,
                r.landingUpwardVelocityCostRate, upwardVelocityExcess01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingPlanarSpeedCostRate,
                r.landingPlanarSpeedCostRate, planarSpeedError01);
            shapingRate += CostRate(contributions, RewardParameterId.LandingAngularRateCostRate,
                r.landingAngularRateCostRate, angularRateError01);
            shapingRate += RewardRate(contributions, RewardParameterId.LandingReadinessProgressRewardRate,
                r.landingReadinessProgressRewardRate,
                ctx.legImpactStarted ? 0f : ctx.legLandingCenteringProgressRate);
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
            // An off-pad collision is a missed landing, not a pad-touchdown
            // quality event. Keep its fixed escape cost so a hard terrain hit
            // cannot become more expensive than intentionally flying away.
            if (end.legMissedPadEnabled && ctx.legOffPadImpact)
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegMissedPadCost,
                        r.legMissedPadCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingMissedPad);

            if (end.legStructuralStrikeEnabled && ctx.legStructuralStrike)
                return RewardDecision.Terminate(
                    shapingRate,
                    LegContactFailureCost(contributions, RewardParameterId.LegStructuralStrikeCost,
                        r.legStructuralStrikeCost, r.legImpactSeverityCost, ctx, end),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingStructuralStrike);

            if (end.legFootOutsidePadEnabled && ctx.legFootOutsidePad)
                return RewardDecision.Terminate(
                    shapingRate,
                    LegContactFailureCost(contributions, RewardParameterId.LegFootOutsidePadCost,
                        r.legFootOutsidePadCost, r.legImpactSeverityCost, ctx, end),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingFootOutsidePad);

            if (end.legHardFirstContactEnabled &&
                ctx.legFirstContactThisStep &&
                !FirstLegContactWithinLimits(ctx, end))
                return RewardDecision.Terminate(
                    shapingRate,
                    LegContactFailureCost(contributions, RewardParameterId.LegHardTouchdownCost,
                        r.legHardTouchdownCost, r.legImpactSeverityCost, ctx, end),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingHardTouchdown);

            // Rebound combines configured rise and all-feet-contact-loss limits
            // in the contact evaluator. The flag is sticky for the episode.
            if (end.legExcessiveReboundEnabled && ctx.legExcessiveRebound)
                return RewardDecision.Terminate(
                    shapingRate,
                    LegContactFailureCost(contributions, RewardParameterId.LegExcessiveReboundCost,
                        r.legExcessiveReboundCost, r.legImpactSeverityCost, ctx, end),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingExcessiveRebound);

            if (end.legStableTouchdownEnabled && LegTouchdownWithinLimits(t, ctx, end))
            {
                float efficiencyDifficulty = LegEfficiencyDifficulty01(ctx, s);
                float touchdownQualityFeature = Mathf.Lerp(
                    1f,
                    LegTouchdownQuality01(t, ctx, end),
                    Mathf.Clamp01(s.legTouchdownQualityRewardFraction));
                float missionEfficiency01 = LegMissionEfficiency01(ctx, s);
                float terminalReward = TerminalFeatureReward(
                    contributions,
                    RewardParameterId.LegSuccessfulTouchdownReward,
                    r.legSuccessfulTouchdownReward,
                    touchdownQualityFeature);
                terminalReward += TerminalFeatureReward(
                    contributions,
                    RewardParameterId.LegSuccessfulFuelEfficiencyReward,
                    r.legSuccessfulFuelEfficiencyReward,
                    missionEfficiency01 * efficiencyDifficulty);
                return RewardDecision.Terminate(
                    shapingRate,
                    terminalReward,
                    successTerminal: true,
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingSuccessfulTouchdown);
            }

            // Once the vehicle has calmly settled on all required feet with
            // propulsion off, no control authority remains to correct center
            // error. Resolve that state as a missed pad instead of discounting
            // the same failure until the 60-second global timeout.
            if (end.legMissedPadEnabled &&
                ctx.legSupportSettled &&
                t.planarDistance > end.landingSuccessRadiusM.At(ctx.landingCriteriaDifficulty01))
                return RewardDecision.Terminate(
                    shapingRate,
                    TerminalCost(contributions, RewardParameterId.LegMissedPadCost,
                        r.legMissedPadCost),
                    eventReward: eventReward,
                    terminationReason: EpisodeTerminationReason.LegLandingMissedPad);

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
            float difficulty = ctx.landingCriteriaDifficulty01;
            return ctx.legFirstContactSpeed <= end.landingSuccessMaxTotalSpeedMps.At(difficulty) &&
                   Mathf.Abs(ctx.legFirstContactVerticalSpeed) <=
                       end.landingSuccessMaxVerticalSpeedMps.At(difficulty) &&
                   ctx.legFirstContactHorizontalSpeed <=
                       end.landingSuccessMaxHorizontalSpeedMps.At(difficulty) &&
                   ctx.legFirstContactTiltDeg <= end.landingSuccessMaxTiltDeg.At(difficulty) &&
                   ctx.legFirstContactAngularRateDegS <=
                       end.landingSuccessMaxAngularRateDegS.At(difficulty);
        }

        /// <summary>
        /// Scores the captured pre-solver impact continuously inside the legal
        /// touchdown envelope. The worst translational ratio controls motion
        /// quality, while uprightness is a separate multiplicative gate so a
        /// tilted one-leg arrival cannot look equivalent to an upright landing.
        /// </summary>
        public static float LegFirstContactQuality01(
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            if (!ctx.legImpactStarted || end == null)
                return 0f;

            float difficulty = ctx.landingCriteriaDifficulty01;
            float translationRatio = ctx.legFirstContactSpeed /
                                     SafeScale(end.landingSuccessMaxTotalSpeedMps.At(difficulty));
            translationRatio = Mathf.Max(
                translationRatio,
                Mathf.Abs(ctx.legFirstContactVerticalSpeed) /
                SafeScale(end.landingSuccessMaxVerticalSpeedMps.At(difficulty)));
            translationRatio = Mathf.Max(
                translationRatio,
                ctx.legFirstContactHorizontalSpeed /
                SafeScale(end.landingSuccessMaxHorizontalSpeedMps.At(difficulty)));

            float uprightRatio = ctx.legFirstContactTiltDeg /
                                 SafeScale(end.landingSuccessMaxTiltDeg.At(difficulty));
            float angularRatio = ctx.legFirstContactAngularRateDegS /
                                 SafeScale(end.landingSuccessMaxAngularRateDegS.At(difficulty));
            float translationQuality = 1f - Mathf.SmoothStep(
                0f, 1f, Mathf.Clamp01(translationRatio));
            float uprightQuality = 1f - Mathf.SmoothStep(
                0f, 1f, Mathf.Clamp01(uprightRatio));
            float angularQuality = 1f - Mathf.SmoothStep(
                0f, 1f, Mathf.Clamp01(angularRatio));

            // Angular-rate quality is deliberately a gentler factor than
            // uprightness at impact. Translation and uprightness define most
            // of the touchdown score; rotation still separates calm contacts.
            return Mathf.Clamp01(
                translationQuality *
                uprightQuality *
                Mathf.Lerp(0.75f, 1f, angularQuality));
        }

        /// <summary>
        /// Grades a legal landing by both first-impact smoothness and final
        /// center accuracy. The geometric mean requires both qualities while
        /// avoiding an unnecessarily sharp product near the success boundary.
        /// </summary>
        public static float LegTouchdownQuality01(
            RewardTerms terms,
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            if (end == null) return 0f;
            float difficulty = ctx.landingCriteriaDifficulty01;
            float successRadius = SafeScale(end.landingSuccessRadiusM.At(difficulty));
            float centerRatio = Mathf.Clamp01(
                Mathf.Max(0f, terms.planarDistance) / successRadius);
            float centerQuality = 1f - Mathf.SmoothStep(0f, 1f, centerRatio);
            float impactQuality = LegFirstContactQuality01(ctx, end);
            return Mathf.Sqrt(Mathf.Clamp01(centerQuality * impactQuality));
        }

        /// <summary>
        /// Provides a bounded, deliberately nonlinear support diagnostic. Three
        /// feet form a useful tripod, but the default success contract still
        /// requires all four physical feet to remain supported.
        /// </summary>
        public static float LegFootSupportQuality01(int feetOnPad)
        {
            if (feetOnPad <= 0) return 0f;
            if (feetOnPad == 1) return 0.10f;
            if (feetOnPad == 2) return 0.35f;
            if (feetOnPad == 3) return 0.90f;
            return 1f;
        }

        /// <summary>
        /// Converts propellant use and engine switching into one physically
        /// interpretable episode cost. Fuel remains the dominant term. A
        /// restart is deliberately much more expensive than the first ignition
        /// of another engine channel, so a one-to-three-engine braking sequence
        /// is cheap while repeated shutdown/relight PWM is not.
        /// </summary>
        public static float LegMissionCostFraction(
            RewardRuntimeContext ctx,
            RewardShapingParameters shaping)
        {
            if (shaping == null) return 0f;

            float fuelUsedFraction = Mathf.Clamp01(1f - ctx.fuelFraction01);
            float restartCost = Mathf.Max(0, ctx.episodeEngineRestartCount) *
                                Mathf.Max(0f, shaping.legRestartEquivalentFuelFraction);
            int additionalFirstIgnitions = Mathf.Max(
                0,
                ctx.episodeEngineFirstIgnitionCount - 1);
            float additionalIgnitionCost = additionalFirstIgnitions * Mathf.Max(
                0f,
                shaping.legAdditionalEngineIgnitionEquivalentFuelFraction);
            return fuelUsedFraction + restartCost + additionalIgnitionCost;
        }

        /// <summary>
        /// Scores successful mission efficiency with a smooth exponential
        /// curve. Unlike the previous clipped budget score, every reduction in
        /// fuel or switching cost remains useful even above the nominal scale.
        /// The larger full-difficulty scale accounts for longer, harder starts.
        /// </summary>
        public static float LegMissionEfficiency01(
            RewardRuntimeContext ctx,
            RewardShapingParameters shaping)
        {
            if (shaping == null) return 0f;

            float difficulty = Mathf.Clamp01(ctx.curriculumDifficulty01);
            float missionCostScale = Mathf.Lerp(
                shaping.legFuelEfficiencyBudgetFraction,
                shaping.legMissionEfficiencyBudgetFullFraction,
                difficulty);
            return Mathf.Exp(
                -LegMissionCostFraction(ctx, shaping) /
                SafeScale(missionCostScale));
        }

        static float LegEfficiencyDifficulty01(
            RewardRuntimeContext ctx,
            RewardShapingParameters shaping)
        {
            if (shaping.legFuelEfficiencyFullDifficulty <=
                shaping.legFuelEfficiencyStartDifficulty + NumericalEpsilon)
                return ctx.curriculumDifficulty01 >= shaping.legFuelEfficiencyFullDifficulty
                    ? 1f
                    : 0f;

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                shaping.legFuelEfficiencyStartDifficulty,
                shaping.legFuelEfficiencyFullDifficulty,
                ctx.curriculumDifficulty01));
        }

        static float LegContactFailureCost(
            RewardContributionBuffer contributions,
            RewardParameterId baseCostId,
            float baseCost,
            float maximumSeverityCost,
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            float value = TerminalCost(contributions, baseCostId, baseCost);
            value += TerminalFeatureCost(
                contributions,
                RewardParameterId.LegImpactSeverityCost,
                maximumSeverityCost,
                LegImpactSeverity01(ctx, end));
            return value;
        }

        static float LegImpactSeverity01(
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            if (!ctx.legImpactStarted) return 0f;

            float difficulty = ctx.landingCriteriaDifficulty01;
            float ratio = ctx.legFirstContactSpeed /
                          SafeScale(end.landingSuccessMaxTotalSpeedMps.At(difficulty));
            ratio = Mathf.Max(ratio, Mathf.Abs(ctx.legFirstContactVerticalSpeed) /
                SafeScale(end.landingSuccessMaxVerticalSpeedMps.At(difficulty)));
            ratio = Mathf.Max(ratio, ctx.legFirstContactHorizontalSpeed /
                SafeScale(end.landingSuccessMaxHorizontalSpeedMps.At(difficulty)));
            ratio = Mathf.Max(ratio, ctx.legFirstContactTiltDeg /
                SafeScale(end.landingSuccessMaxTiltDeg.At(difficulty)));
            ratio = Mathf.Max(ratio, ctx.legFirstContactAngularRateDegS /
                SafeScale(end.landingSuccessMaxAngularRateDegS.At(difficulty)));

            // The extra cost begins at the active success boundary and reaches
            // its cap at three times that boundary. L6's typical 1.5x arrivals
            // were otherwise almost indistinguishable from a marginal miss,
            // making a repeatable hard impact an economically attractive policy.
            return Mathf.Clamp01((ratio - 1f) / 2f);
        }

        static bool LegTouchdownWithinLimits(
            RewardTerms t,
            RewardRuntimeContext ctx,
            TerminationParameters end)
        {
            float difficulty = ctx.landingCriteriaDifficulty01;
            return ctx.legStable &&
                   ctx.legPropulsionOff &&
                   ctx.legFeetOnPad >= Mathf.Clamp(
                       Mathf.RoundToInt(Mathf.Lerp(
                           end.legInitialMinimumStableFeet,
                           end.legMinimumStableFeet,
                           difficulty)),
                       1,
                       LandingLegComponent.LegCount) &&
                   ctx.legStableTime >= end.landingStableHoldSeconds.At(difficulty) &&
                   t.planarDistance <= end.landingSuccessRadiusM.At(difficulty) &&
                   t.speed <= end.landingSuccessMaxTotalSpeedMps.At(difficulty) &&
                   Mathf.Abs(t.verticalSpeed) <= end.landingSuccessMaxVerticalSpeedMps.At(difficulty) &&
                   t.planarSpeed <= end.landingSuccessMaxHorizontalSpeedMps.At(difficulty) &&
                   TiltDegrees(t.upDot) <= end.landingSuccessMaxTiltDeg.At(difficulty) &&
                   t.angularRateDegS <= end.landingSuccessMaxAngularRateDegS.At(difficulty);
        }

        /// <summary>
        /// Bounded all-altitude potential for moving toward the pad center.
        /// Descent, planar speed, tilt, and rotation are deliberately excluded:
        /// their independent reward channels must not rise merely because the
        /// altitude gate grows during passive fall.
        /// </summary>
        public static float LegLandingCenteringPotential(
            RewardTerms t,
            RewardShapingParameters shaping)
        {
            if (shaping == null) return 0f;
            return Exp01(
                Mathf.Max(0f, t.planarDistance),
                shaping.landingPlanarDistanceFalloffM);
        }
    }
}
