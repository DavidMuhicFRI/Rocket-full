using System;
using UnityEngine;

namespace RocketSim
{
    public static class RocketRewardModel
    {
        public static RewardDecision Evaluate(ScenarioType scenario, RewardTerms t, RewardRuntimeContext ctx)
        {
            return scenario switch
            {
                ScenarioType.Landing => Landing(t, ctx),
                ScenarioType.Hover => Hover(t, ctx),
                ScenarioType.HoverTracking => HoverTracking(t, ctx),
                ScenarioType.Takeoff => Takeoff(t, ctx),
                ScenarioType.BellyFlop => BellyFlop(t, ctx),
                _ => RewardDecision.None
            };
        }

        static RewardDecision Landing(RewardTerms t, RewardRuntimeContext ctx)
        {
            float landingBand = Mathf.Clamp01(1f - Mathf.Abs(t.verticalError) / 30f);

            float reward =
                0.10f * Exp01(t.planarDistance, 12f) +
                0.06f * t.upright01 +
                0.04f * Exp01(t.angularRateDegS, 60f) +
                0.04f * Exp01(t.speed, 30f) +
                0.08f * landingBand * Exp01(t.speed, 5f) +
                0.08f * landingBand * Exp01(Mathf.Abs(t.verticalSpeed), 3f) -
                0.0020f * t.planarDistance -
                0.0015f * t.speed -
                0.0007f * t.angularRateDegS -
                0.0030f * t.controlEffort;

            if (t.upDot < 0.35f || t.distance3D > 150f || ctx.fuelKg <= 0f)
                return RewardDecision.Terminate(reward, -10f);

            if (ctx.altitude <= ctx.groundClearance)
            {
                bool good = t.upDot > 0.94f &&
                            t.planarDistance < 5f &&
                            t.speed < 4f &&
                            Mathf.Abs(t.verticalSpeed) < 3f &&
                            t.angularRateDegS < 35f;
                return RewardDecision.Terminate(reward, good ? 12f : -6f);
            }

            return RewardDecision.Continue(reward);
        }

        static RewardDecision Hover(RewardTerms t, RewardRuntimeContext ctx)
        {
            float reward = HoverBaselineReward(t);

            if (ctx.altitude < ctx.groundClearance ||
                t.upDot < 0.45f ||
                ctx.fuelKg <= 0f ||
                t.planarDistance > 80f ||
                ctx.altitude > 200f)
                return RewardDecision.Terminate(reward, -10f);

            return RewardDecision.Continue(reward);
        }

        static RewardDecision HoverTracking(RewardTerms t, RewardRuntimeContext ctx)
        {
            float settleRadius = Mathf.Max(ctx.hoverTrackSettleRadius, 1f);
            bool hoverPhase = t.planarDistance <= settleRadius;

            float reward = HoverBaselineReward(t);
            if (hoverPhase)
            {
                float tightCenter01 = Exp01(t.planarDistance, Mathf.Max(2f, settleRadius * 0.5f));
                float horizontalCalm01 = Exp01(t.planarSpeed, 1.8f);
                float verticalCalm01 = Exp01(Mathf.Abs(t.verticalSpeed), 1.4f);
                float rotationalCalm01 = Exp01(t.angularRateDegS, 28f);

                reward +=
                    0.045f * tightCenter01 +
                    0.035f * horizontalCalm01 +
                    0.025f * verticalCalm01 +
                    0.020f * rotationalCalm01 +
                    0.020f * tightCenter01 * horizontalCalm01 * verticalCalm01 * t.upright01 -
                    0.0005f * t.planarSpeed -
                    0.0004f * Mathf.Abs(t.verticalSpeed);
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
                    0.085f * approachSpeed01 +
                    0.040f * direction01 +
                    0.020f * usefulSpeed01 -
                    0.045f * Mathf.Clamp01(movingAwaySpeed / 4f) -
                    0.025f * farFromHover01 * (1f - usefulSpeed01) -
                    0.020f * overspeedPenalty;
            }

            if (ctx.altitude < ctx.groundClearance ||
                t.upDot < 0.45f ||
                ctx.fuelKg <= 0f ||
                t.planarDistance > 90f ||
                ctx.altitude > 200f)
                return RewardDecision.Terminate(reward, -10f);

            return RewardDecision.Continue(reward);
        }

        static RewardDecision Takeoff(RewardTerms t, RewardRuntimeContext ctx)
        {
            float altitudeProgress = Mathf.Clamp01((ctx.altitude - ctx.groundClearance) / 100f);
            float upwardVelocity01 = Mathf.Clamp01(t.verticalSpeed / 20f);

            float reward =
                0.10f * altitudeProgress +
                0.08f * upwardVelocity01 +
                0.08f * t.upright01 +
                0.08f * Exp01(t.planarDistance, 10f) +
                0.04f * Exp01(t.angularRateDegS, 45f) -
                0.0015f * t.planarDistance -
                0.0008f * t.angularRateDegS -
                0.0020f * t.controlEffort;

            if (t.upDot < 0.45f || t.planarDistance > 80f)
                return RewardDecision.Terminate(reward, -10f);

            if (ctx.altitude > 120f)
            {
                bool good = t.upDot > 0.90f && t.planarDistance < 12f && t.angularRateDegS < 45f;
                return RewardDecision.Terminate(reward, good ? 10f : 4f);
            }

            if (ctx.fuelKg <= 0f)
                return RewardDecision.Terminate(reward, -5f);

            return RewardDecision.Continue(reward);
        }

        static RewardDecision BellyFlop(RewardTerms t, RewardRuntimeContext ctx)
        {
            float reentryPhase = Mathf.Clamp01((ctx.altitude - 55f) / 45f);
            float landingPhase = 1f - reentryPhase;
            float bellyAttitude = 1f - Mathf.Abs(t.upDot);

            float reward =
                0.08f * reentryPhase * bellyAttitude +
                0.08f * landingPhase * t.upright01 +
                0.08f * Exp01(t.planarDistance, 15f) +
                0.05f * landingPhase * Exp01(t.speed, 6f) +
                0.04f * Exp01(t.angularRateDegS, 70f) -
                0.0015f * t.planarDistance -
                0.0010f * landingPhase * t.speed -
                0.0006f * t.angularRateDegS -
                0.0020f * t.controlEffort;

            if (ctx.altitude > 40f && t.upDot > 0.85f)
                reward -= 0.04f;

            if (ctx.altitude <= ctx.groundClearance)
            {
                bool good = t.upDot > 0.90f &&
                            t.planarDistance < 8f &&
                            t.speed < 5f &&
                            t.angularRateDegS < 45f;
                return RewardDecision.Terminate(reward, good ? 12f : -10f);
            }

            if (ctx.fuelKg <= 0f || t.planarDistance > 150f)
                return RewardDecision.Terminate(reward, -10f);

            return RewardDecision.Continue(reward);
        }

        static float Exp01(float value, float scale)
        {
            return Mathf.Exp(-Mathf.Abs(value) / Mathf.Max(scale, 0.0001f));
        }

        static float HoverBaselineReward(RewardTerms t)
        {
            return
                0.12f * Exp01(Mathf.Abs(t.verticalError), 5f) +
                0.12f * Exp01(t.planarDistance, 8f) +
                0.07f * t.upright01 +
                0.06f * Exp01(t.speed, 4f) +
                0.04f * Exp01(t.angularRateDegS, 35f) -
                0.0015f * t.speed -
                0.0008f * t.angularRateDegS -
                0.0025f * t.controlEffort;
        }
    }

    public readonly struct RewardDecision
    {
        public readonly float shapingReward;
        public readonly bool endEpisode;
        public readonly bool hasTerminalReward;
        public readonly float terminalReward;

        RewardDecision(float shapingReward, bool endEpisode, bool hasTerminalReward, float terminalReward)
        {
            this.shapingReward = shapingReward;
            this.endEpisode = endEpisode;
            this.hasTerminalReward = hasTerminalReward;
            this.terminalReward = terminalReward;
        }

        public static RewardDecision None => new(0f, false, false, 0f);
        public static RewardDecision Continue(float shapingReward) => new(shapingReward, false, false, 0f);
        public static RewardDecision Terminate(float shapingReward, float terminalReward) => new(shapingReward, true, true, terminalReward);
    }

    public readonly struct RewardRuntimeContext
    {
        public readonly float altitude;
        public readonly float groundClearance;
        public readonly float fuelKg;
        public readonly float hoverTrackSettleRadius;

        public RewardRuntimeContext(
            float altitude,
            float groundClearance,
            float fuelKg,
            float hoverTrackSettleRadius)
        {
            this.altitude = altitude;
            this.groundClearance = groundClearance;
            this.fuelKg = fuelKg;
            this.hoverTrackSettleRadius = hoverTrackSettleRadius;
        }
    }

    public struct RewardTerms
    {
        public float distance3D;
        public float planarDistance;
        public float verticalError;
        public float speed;
        public float planarSpeed;
        public float verticalSpeed;
        public float goalClosureRate;
        public float horizontalClosureRate;
        public float upDot;
        public float upright01;
        public float angularRateDegS;
        public float controlEffort;
    }
}
