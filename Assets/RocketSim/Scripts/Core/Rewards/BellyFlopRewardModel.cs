// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/BellyFlopRewardModel.cs
// Purpose: Defines belly-flop reward shaping: broadside high-altitude descent,
// low-altitude flip to upright, target centering, calm motion, and touchdown result.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Scores belly-flop recovery by rewarding broadside attitude at high
        /// altitude, transition to upright near the target, and controlled terminal landing.
        /// </summary>
        static RewardDecision BellyFlop(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float reentryPhase = Mathf.Clamp01((ctx.altitude - 55f) / 45f);
            float landingPhase = 1f - reentryPhase;
            float bellyAttitude = 1f - Mathf.Abs(t.upDot);

            float reward =
                0.08f * f.bellyAttitude * reentryPhase * bellyAttitude +
                0.08f * f.uprightness * landingPhase * t.upright01 +
                0.08f * f.targetPrecision * Exp01(t.planarDistance, 15f) +
                0.05f * f.speed * landingPhase * Exp01(t.speed, 6f) +
                0.04f * f.rotation * Exp01(t.angularRateDegS, 70f) -
                0.0015f * f.targetPrecision * t.planarDistance -
                0.0010f * f.speed * landingPhase * t.speed -
                0.0006f * f.rotation * t.angularRateDegS -
                0.0020f * f.controlEffort * t.controlEffort;

            if (ctx.altitude > 40f && t.upDot > 0.85f)
                reward -= 0.04f * f.bellyAttitude;

            if (ctx.altitude <= ctx.terminalAltitude)
            {
                bool good = t.upDot > 0.90f &&
                            t.planarDistance < 8f &&
                            t.speed < 5f &&
                            t.angularRateDegS < 45f;
                return RewardDecision.Terminate(reward, Terminal(good ? 12f : -10f, f));
            }

            if (ctx.fuelKg <= 0f || t.planarDistance > 150f)
                return RewardDecision.Terminate(reward, Terminal(-10f, f));

            return RewardDecision.Continue(reward);
        }

    }
}
