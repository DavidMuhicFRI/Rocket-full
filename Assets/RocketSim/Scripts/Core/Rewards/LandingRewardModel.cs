// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/LandingRewardModel.cs
// Purpose: Defines landing reward shaping for vertical descent profile, target
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
        /// Scores chopstick-style landing by shaping descent profile, target
        /// centering, uprightness, yaw alignment, platform hold, and terminal success.
        /// </summary>
        static RewardDecision Landing(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float heightAboveTouchdown = Mathf.Max(0f, ctx.altitude - ctx.terminalAltitude);
            float gravity = Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
            float touchdownSpeedScale = Mathf.Sqrt(gravity * 0.5f);
            // Use a fraction of ballistic free-fall speed as a smooth descent
            // target: faster when high, automatically approaching zero near capture.
            float desiredDescentSpeed = Mathf.Sqrt(2f * gravity * heightAboveTouchdown) * 0.30f;
            float desiredVerticalSpeed = -desiredDescentSpeed;
            float speedTolerance = Mathf.Max(touchdownSpeedScale, desiredDescentSpeed * 0.45f);
            float verticalSpeedProfile01 = Exp01(t.verticalSpeed - desiredVerticalSpeed, speedTolerance);
            float verticalSpeedProfile = verticalSpeedProfile01 * 2f - 1f;
            float closureScale = Mathf.Max(touchdownSpeedScale, desiredDescentSpeed + speedTolerance);
            float goalClosure01 = Mathf.Clamp01(t.goalClosureRate / closureScale);
            float wrongWay01 = Mathf.Clamp01(Mathf.Max(0f, -t.goalClosureRate) / closureScale);
            float nearGround01 = 1f - Mathf.Clamp01(heightAboveTouchdown / 50f);
            float centerPrecision01 = Exp01(t.planarDistance, Mathf.Max(0.5f, ctx.landingSuccessRadius * 0.5f));
            float headingPrecision01 = Exp01(t.yawErrorDeg, Mathf.Max(1f, ctx.landingSuccessMaxYawErrorDeg));
            float platformHold01 = ctx.landingPlatformRequired
                ? Mathf.Clamp01(ctx.landingPlatformStableTime / Mathf.Max(0.01f, ctx.landingPlatformStableHoldTime))
                : 0f;

            float reward =
                0.10f * f.targetPrecision * Exp01(t.planarDistance, 12f) +
                0.10f * f.targetPrecision * nearGround01 * centerPrecision01 +
                0.06f * f.uprightness * t.upright01 +
                0.04f * f.rotation * Exp01(t.angularRateDegS, 60f) +
                0.06f * f.orientation * nearGround01 * headingPrecision01 * t.upright01 +
                0.16f * f.descentDrive * verticalSpeedProfile +
                0.05f * f.descentDrive * goalClosure01 -
                0.05f * f.descentDrive * wrongWay01 -
                0.0020f * f.targetPrecision * t.planarDistance -
                0.0007f * f.rotation * t.angularRateDegS -
                0.0030f * f.controlEffort * t.controlEffort -
                0.0060f;

            if (ctx.landingPlatformRequired)
            {
                reward +=
                    0.04f * f.targetPrecision * nearGround01 * (ctx.landingPlatformInsideCapture ? 1f : -0.25f) +
                    0.04f * f.settle * nearGround01 * platformHold01;
            }

            if (t.upDot < 0.35f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-35f, f),
                    terminationReason: EpisodeTerminationReason.LandingUnsafeAttitude);

            // High-altitude landing starts are intentionally far from the
            // catch point vertically. Only horizontal flyaway is unrecoverable
            // here; the independent failure-altitude guard handles upward escape.
            if (t.planarDistance > 150f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-35f, f),
                    terminationReason: EpisodeTerminationReason.LandingTooFarFromTarget);

            if (ctx.fuelKg <= 0f)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-35f, f),
                    terminationReason: EpisodeTerminationReason.LandingFuelDepleted);

            if (ctx.altitude > ctx.landingFailureAltitude)
                return RewardDecision.Terminate(
                    reward,
                    Terminal(-30f, f),
                    terminationReason: EpisodeTerminationReason.LandingAboveAltitudeLimit);

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
                float precisionBonus = 12f * centerPrecision01 + 8f * headingPrecision01;
                return RewardDecision.Terminate(
                    reward,
                    Terminal(good ? 35f + precisionBonus : -25f, f),
                    good,
                    good
                        ? EpisodeTerminationReason.LandingSuccessfulTouchdown
                        : EpisodeTerminationReason.LandingFailedTouchdown);
            }

            return RewardDecision.Continue(reward);
        }

    }
}
