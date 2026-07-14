// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs
// Purpose: Selects the active scenario reward function and provides small shared
// helpers for smooth proximity scores and terminal reward scaling.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        /// <summary>
        /// Dispatches reward evaluation to the active scenario model after
        /// clamping reward multipliers to valid training-safe ranges.
        /// </summary>
        public static RewardDecision Evaluate(ScenarioType scenario, RewardTerms t, RewardRuntimeContext ctx, ScenarioRewardFactors factors)
        {
            factors ??= new ScenarioRewardFactors();
            factors.Clamp();

            return scenario switch
            {
                ScenarioType.Landing => Landing(t, ctx, factors),
                ScenarioType.Hover => Hover(t, ctx, factors),
                ScenarioType.HoverTracking => HoverTracking(t, ctx, factors),
                ScenarioType.Takeoff => Takeoff(t, ctx, factors),
                ScenarioType.BellyFlop => BellyFlop(t, ctx, factors),
                _ => RewardDecision.None
            };
        }

        /// <summary>
        /// Converts an error magnitude into a smooth 0..1 proximity score with
        /// exponential falloff.
        /// </summary>
        static float Exp01(float value, float scale)
        {
            return Mathf.Exp(-Mathf.Abs(value) / Mathf.Max(scale, 0.0001f));
        }

        /// <summary>
        /// Applies the configured terminal multiplier to a success or failure
        /// reward before ending the episode.
        /// </summary>
        static float Terminal(float value, ScenarioRewardFactors f)
        {
            return value * f.terminal;
        }
    }


}
