// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/LegLandingRewardModel.cs
// Purpose: Defines powered leg-landing shaping and contact-driven terminal
// outcomes without rewarding a prescribed throttle setting.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Reuses the continuous descent/centering baseline from chopstick
        /// landing, removes heading alignment, and lets real foot contact—not an
        /// altitude crossing—decide touchdown success or failure.
        /// </summary>
        static RewardDecision LegLanding(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float heightAbovePad = Mathf.Max(0f, ctx.altitude - ctx.terminalAltitude);
            float gravity = Mathf.Max(0.01f, ctx.gravityMagnitude);
            float desiredDescentSpeed = Mathf.Sqrt(2f * gravity * heightAbovePad) * 0.30f;
            float desiredVerticalSpeed = -desiredDescentSpeed;
            float descentError01 = Mathf.Clamp01(
                Mathf.Abs(t.verticalSpeed - desiredVerticalSpeed) /
                Mathf.Max(5f, desiredDescentSpeed));
            float signedClosure = Mathf.Clamp(
                t.goalClosureRate / Mathf.Max(5f, desiredDescentSpeed),
                -1f,
                1f);
            float lateralError01 = 1f - Mathf.Exp(-Mathf.Max(0f, t.planarDistance) / 25f);
            float attitudeError01 = 1f - Mathf.Clamp01(t.upright01);
            float nearPad01 = Mathf.Exp(-heightAbovePad / 50f);
            float lateralSpeedError01 = Mathf.Clamp01(t.planarSpeed / 5f);
            float angularRateError01 = Mathf.Clamp01(t.angularRateDegS / 60f);

            float reward =
                0.060f * f.descentDrive * signedClosure -
                0.040f * f.descentDrive * descentError01 -
                0.025f * f.targetPrecision * lateralError01 -
                0.020f * f.uprightness * attitudeError01 -
                nearPad01 * (
                    0.025f * f.speed * lateralSpeedError01 +
                    0.015f * f.rotation * angularRateError01) -
                0.005f * f.controlEffort * Mathf.Clamp01(t.controlEffort) -
                0.005f * f.timePressure;

            float contactEvent = ctx.legFirstContactThisStep ? 0.25f * f.settle : 0f;
            if (ctx.legBecameStable)
                contactEvent += 1f * f.settle;

            if (ctx.legStructuralStrike)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingStructuralStrike);

            if (ctx.legFootOutsidePad)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingFootOutsidePad);

            if (ctx.legFirstContactThisStep &&
                !FirstLegContactWithinLimits(ctx))
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingHardTouchdown);

            if (ctx.legStable)
                return RewardDecision.Terminate(
                    reward, Terminal(10f, f), successTerminal: true,
                    eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingSuccessfulTouchdown);

            if (t.upDot < 0.35f)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingUnsafeAttitude);

            if (t.planarDistance > 150f)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingTooFarFromTarget);

            // Fuel depletion during the short settling hold is allowed: the
            // vehicle may already be safely supported by its feet.
            if (ctx.fuelKg <= 0f && !ctx.legTouchdownStarted)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingFuelDepleted);

            if (ctx.altitude > ctx.landingFlyawayAltitude)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingAboveAltitudeLimit);

            // Falling more than two metres below the measured foot-contact
            // plane without any pad contact means the pad was missed.
            if (!ctx.legTouchdownStarted && ctx.altitude < ctx.terminalAltitude - 2f)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingMissedPad);

            if (ctx.landingMaxEpisodeSeconds > 0f &&
                ctx.episodeElapsedSeconds >= ctx.landingMaxEpisodeSeconds)
                return RewardDecision.Terminate(
                    reward, Terminal(-5f, f), eventReward: contactEvent,
                    terminationReason: EpisodeTerminationReason.LegLandingTimeLimit);

            return RewardDecision.Continue(reward, contactEvent);
        }

        static bool FirstLegContactWithinLimits(RewardRuntimeContext ctx)
        {
            return ctx.legFirstContactSpeed < ctx.landingSuccessMaxSpeed &&
                   Mathf.Abs(ctx.legFirstContactVerticalSpeed) < ctx.landingSuccessMaxVerticalSpeed &&
                   ctx.legFirstContactHorizontalSpeed < ctx.landingSuccessMaxHorizontalSpeed &&
                   ctx.legFirstContactTiltDeg < ctx.landingSuccessMaxTiltDeg &&
                   ctx.legFirstContactAngularRateDegS < ctx.landingSuccessMaxAngularRateDegS;
        }
    }
}
