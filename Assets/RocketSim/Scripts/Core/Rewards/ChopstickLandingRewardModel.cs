// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ChopstickLandingRewardModel.cs
// Purpose: Defines chopstick-catch reward shaping for vertical descent profile, target
// centering, uprightness, chopstick yaw alignment, stable hold, and final capture.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Scores chopstick-style landing with signed progress and bounded
        /// error costs. Static positive state bonuses are intentionally avoided
        /// so hovering cannot accumulate reward without making progress.
        /// </summary>
        static RewardDecision ChopstickLanding(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float heightAboveTouchdown = Mathf.Max(0f, ctx.altitude - ctx.terminalAltitude);
            // Gravity is supplied by the agent's immutable runtime snapshot so
            // this reward equation has no hidden dependency on global physics.
            float gravity = Mathf.Max(0.01f, ctx.gravityMagnitude);
            float touchdownSpeedScale = Mathf.Sqrt(gravity * 0.5f);
            // A fraction of ballistic free-fall speed provides one continuous
            // descent target that naturally approaches zero at the catch plane.
            float desiredDescentSpeed = Mathf.Sqrt(2f * gravity * heightAboveTouchdown) * 0.30f;
            float desiredVerticalSpeed = -desiredDescentSpeed;
            float descentError01 = Mathf.Clamp01(
                Mathf.Abs(t.verticalSpeed - desiredVerticalSpeed) /
                Mathf.Max(5f, desiredDescentSpeed));
            float closureScale = Mathf.Max(5f, desiredDescentSpeed);
            float signedClosure = Mathf.Clamp(t.goalClosureRate / closureScale, -1f, 1f);

            // These are fixed physical scales, not curriculum-dependent success
            // limits. Difficulty therefore changes the task, not reward units.
            float lateralError01 = 1f - Mathf.Exp(-Mathf.Max(0f, t.planarDistance) / 25f);
            float attitudeError01 = 1f - Mathf.Clamp01(t.upright01);
            float nearCatch01 = Mathf.Exp(-heightAboveTouchdown / 50f);
            float lateralSpeedError01 = Mathf.Clamp01(t.planarSpeed / 5f);
            float yawError01 = Mathf.Clamp01(t.yawErrorDeg / 45f);
            float angularRateError01 = Mathf.Clamp01(t.angularRateDegS / 60f);
            float controlEffort01 = Mathf.Clamp01(t.controlEffort);

            float reward =
                0.060f * f.descentDrive * signedClosure -
                0.040f * f.descentDrive * descentError01 -
                0.025f * f.targetPrecision * lateralError01 -
                0.020f * f.uprightness * attitudeError01 -
                nearCatch01 * (
                    0.025f * f.speed * lateralSpeedError01 +
                    0.015f * f.orientation * yawError01 +
                    0.015f * f.rotation * angularRateError01) -
                0.005f * f.controlEffort * controlEffort01 -
                0.005f * f.timePressure;

            // Reaching the full stable-hold requirement is an event, not a
            // per-second state bonus. It can therefore be earned only once.
            float stableCaptureEvent = ctx.landingPlatformBecameStable
                ? 1f * f.settle
                : 0f;

            if (t.upDot < 0.35f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-5f, f),
                    eventReward: stableCaptureEvent,
                    terminationReason: EpisodeTerminationReason.ChopstickUnsafeAttitude);

            // High-altitude landing starts are intentionally far from the
            // catch point vertically. Only horizontal flyaway is unrecoverable
            // here; the independent failure-altitude guard handles upward escape.
            if (t.planarDistance > 150f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-5f, f),
                    eventReward: stableCaptureEvent,
                    terminationReason: EpisodeTerminationReason.ChopstickTooFarFromTarget);

            if (ctx.fuelKg <= 0f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-5f, f),
                    eventReward: stableCaptureEvent,
                    terminationReason: EpisodeTerminationReason.ChopstickFuelDepleted);

            if (ctx.altitude > ctx.landingFlyawayAltitude)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-5f, f),
                    eventReward: stableCaptureEvent,
                    terminationReason: EpisodeTerminationReason.ChopstickAboveAltitudeLimit);

            if (ctx.altitude <= ctx.terminalAltitude)
            {
                float touchdownSpeedLimit = ctx.landingSuccessMaxSpeed > 0f
                    ? ctx.landingSuccessMaxSpeed
                    : touchdownSpeedScale * 1.6f;
                float touchdownVerticalSpeedLimit = ctx.landingSuccessMaxVerticalSpeed > 0f
                    ? ctx.landingSuccessMaxVerticalSpeed
                    : touchdownSpeedScale * 1.35f;
                float touchdownHorizontalSpeedLimit = ctx.landingSuccessMaxHorizontalSpeed > 0f
                    ? ctx.landingSuccessMaxHorizontalSpeed
                    : touchdownSpeedScale;
                float successTiltDot = Mathf.Cos(ctx.landingSuccessMaxTiltDeg * Mathf.Deg2Rad);
                bool headingGood = t.yawErrorDeg < ctx.landingSuccessMaxYawErrorDeg;
                bool platformGood = !ctx.landingPlatformRequired || ctx.landingPlatformStable;
                bool good = t.upDot > 0.94f &&
                            t.upDot > successTiltDot &&
                            t.planarDistance < ctx.landingSuccessRadius &&
                            t.speed < touchdownSpeedLimit &&
                            Mathf.Abs(t.verticalSpeed) < touchdownVerticalSpeedLimit &&
                            t.planarSpeed < touchdownHorizontalSpeedLimit &&
                            t.angularRateDegS < ctx.landingSuccessMaxAngularRateDegS &&
                            headingGood &&
                            platformGood;
                return RewardDecision.Terminate(
                    reward,
                    Terminal(good ? 10f : -5f, f),
                    good,
                    stableCaptureEvent,
                    good
                        ? EpisodeTerminationReason.ChopstickSuccessfulCapture
                        : EpisodeTerminationReason.ChopstickFailedCapture);
            }

            if (ctx.landingMaxEpisodeSeconds > 0f &&
                ctx.episodeElapsedSeconds >= ctx.landingMaxEpisodeSeconds)
            {
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-5f, f),
                    eventReward: stableCaptureEvent,
                    terminationReason: EpisodeTerminationReason.ChopstickTimeLimit);
            }

            return RewardDecision.Continue(reward, stableCaptureEvent);
        }

    }
}
