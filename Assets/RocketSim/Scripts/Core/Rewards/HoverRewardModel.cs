// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/HoverRewardModel.cs
// Purpose: Defines fixed-hover and moving-target hover rewards, sharing one
// stability baseline and adding approach/settle terms for target tracking.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        // One restart costs less than one second of accurate hover shaping and
        // far less than a terminal failure. It therefore discourages needless
        // cycling without preventing pulse control when minimum thrust is too high.
        public const float HoverEngineRestartPenalty = 0.25f;

        /// <summary>
        /// Scores fixed-position hover by rewarding altitude hold, centering,
        /// uprightness, low speed, and calm attitude until a terminal condition occurs.
        /// </summary>
        static RewardDecision Hover(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float reward = HoverBaselineReward(t, f);
            float restartEvent = HoverRestartEvent(ctx, f);

            if (ctx.altitude < ctx.terminalAltitude)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverGroundImpact);
            if (t.upDot < 0.45f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverUnsafeAttitude);
            if (ctx.fuelKg <= 0f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverFuelDepleted);
            if (t.planarDistance > 80f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverTooFarFromTarget);
            if (ctx.altitude > 200f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverAboveAltitudeLimit);

            return RewardDecision.Continue(reward, restartEvent);
        }

        /// <summary>
        /// Scores moving-target hover by combining baseline hover stability with
        /// approach shaping before capture and settle shaping inside the hover radius.
        /// </summary>
        static RewardDecision HoverTracking(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float settleRadius = Mathf.Max(ctx.hoverTrackSettleRadius, 1f);
            bool hoverPhase = t.planarDistance <= settleRadius;

            float reward = HoverBaselineReward(t, f);
            float restartEvent = HoverRestartEvent(ctx, f);
            if (hoverPhase)
            {
                float tightCenter01 = Exp01(t.planarDistance, Mathf.Max(2f, settleRadius * 0.5f));
                float horizontalCalm01 = Exp01(t.planarSpeed, 1.8f);
                float verticalCalm01 = Exp01(Mathf.Abs(t.verticalSpeed), 1.4f);
                float rotationalCalm01 = Exp01(t.angularRateDegS, 28f);

                reward +=
                    0.045f * f.settle * tightCenter01 +
                    0.035f * f.speed * horizontalCalm01 +
                    0.025f * f.verticalSpeed * verticalCalm01 +
                    0.020f * f.rotation * rotationalCalm01 +
                    0.020f * f.settle * tightCenter01 * horizontalCalm01 * verticalCalm01 * t.upright01 -
                    0.0005f * f.speed * t.planarSpeed -
                    0.0004f * f.verticalSpeed * Mathf.Abs(t.verticalSpeed);
            }
            else
            {
                float distanceBeyondHover = Mathf.Max(0f, t.planarDistance - settleRadius);
                float approachSpeed01 = Mathf.Clamp01(t.horizontalClosureRate / 5f);
                float movingAwaySpeed = Mathf.Max(0f, -t.horizontalClosureRate);
                float direction01 = t.planarSpeed > 0.1f
                    ? Mathf.Clamp01((t.horizontalClosureRate / Mathf.Max(t.planarSpeed, 0.001f) + 1f) * 0.5f)
                    : 0f;
                float usefulSpeed01 = Mathf.Clamp01(t.planarSpeed / 5f);
                float farFromHover01 = Mathf.Clamp01(distanceBeyondHover / Mathf.Max(settleRadius, 1f));
                float overspeedPenalty = Mathf.Clamp01(Mathf.Max(0f, t.planarSpeed - 8f) / 6f);

                reward +=
                    0.085f * f.tracking * approachSpeed01 +
                    0.040f * f.tracking * direction01 +
                    0.020f * f.speed * usefulSpeed01 -
                    0.045f * f.tracking * Mathf.Clamp01(movingAwaySpeed / 4f) -
                    0.025f * f.tracking * farFromHover01 * (1f - usefulSpeed01) -
                    0.020f * f.speed * overspeedPenalty;
            }

            if (ctx.altitude < ctx.terminalAltitude)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverGroundImpact);
            if (t.upDot < 0.45f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverUnsafeAttitude);
            if (ctx.fuelKg <= 0f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverFuelDepleted);
            if (t.planarDistance > 90f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverTooFarFromTarget);
            if (ctx.altitude > 200f)
                return HoverFailure(reward, restartEvent, f, EpisodeTerminationReason.HoverAboveAltitudeLimit);

            return RewardDecision.Continue(reward, restartEvent);
        }

        /// <summary>
        /// Applies an instantaneous cost only when an engine ignites after a
        /// previous shutdown. The initial hover ignition is supplied by the
        /// scenario and does not count as a restart.
        /// </summary>
        static float HoverRestartEvent(RewardRuntimeContext ctx, ScenarioRewardFactors f) =>
            -HoverEngineRestartPenalty * f.engineRestart * Mathf.Max(0, ctx.engineRestartsThisStep);

        /// <summary>Builds one categorized fixed/tracking-hover failure.</summary>
        static RewardDecision HoverFailure(
            float shapingReward,
            float restartEvent,
            ScenarioRewardFactors factors,
            EpisodeTerminationReason reason) =>
            RewardDecision.Terminate(
                shapingReward,
                Terminal(-10f, factors),
                eventReward: restartEvent,
                terminationReason: reason);

        /// <summary>
        /// Calculates the shared hover shaping terms used by both stationary
        /// hover and moving-target hover scenarios.
        /// </summary>
        static float HoverBaselineReward(RewardTerms t, ScenarioRewardFactors f)
        {
            return
                0.12f * f.altitude * Exp01(Mathf.Abs(t.verticalError), 5f) +
                0.12f * f.targetPrecision * Exp01(t.planarDistance, 8f) +
                0.07f * f.uprightness * t.upright01 +
                0.06f * f.speed * Exp01(t.speed, 4f) +
                0.04f * f.rotation * Exp01(t.angularRateDegS, 35f) -
                0.0015f * f.speed * t.speed -
                0.0008f * f.rotation * t.angularRateDegS -
                0.0025f * f.controlEffort * t.controlEffort;
        }

    }
}
