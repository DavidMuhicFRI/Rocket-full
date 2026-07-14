// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/TakeoffRewardModel.cs
// Purpose: Defines takeoff reward shaping for upward progress, climb speed,
// uprightness, low drift, calm rotation, control efficiency, and ascent completion.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Scores takeoff by rewarding altitude progress, upward velocity,
        /// uprightness, pad alignment, and calm attitude until ascent success or failure.
        /// </summary>
        static RewardDecision Takeoff(RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors f)
        {
            float altitudeProgress = Mathf.Clamp01((ctx.altitude - ctx.terminalAltitude) / 100f);
            float upwardVelocity01 = Mathf.Clamp01(t.verticalSpeed / 20f);

            float reward =
                0.10f * f.progress * altitudeProgress +
                0.08f * f.verticalSpeed * upwardVelocity01 +
                0.08f * f.uprightness * t.upright01 +
                0.08f * f.targetPrecision * Exp01(t.planarDistance, 10f) +
                0.04f * f.rotation * Exp01(t.angularRateDegS, 45f) -
                0.0015f * f.targetPrecision * t.planarDistance -
                0.0008f * f.rotation * t.angularRateDegS -
                0.0020f * f.controlEffort * t.controlEffort;

            if (t.upDot < 0.45f || t.planarDistance > 80f)
                return RewardDecision.Terminate(reward, Terminal(-10f, f));

            if (ctx.altitude > 120f)
            {
                bool good = t.upDot > 0.90f && t.planarDistance < 12f && t.angularRateDegS < 45f;
                return RewardDecision.Terminate(reward, Terminal(good ? 10f : 4f, f));
            }

            if (ctx.fuelKg <= 0f)
                return RewardDecision.Terminate(reward, Terminal(-5f, f));

            return RewardDecision.Continue(reward);
        }

    }
}
