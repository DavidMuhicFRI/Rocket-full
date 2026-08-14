// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs
// Purpose: Dispatches the active task objective and provides allocation-free
// helpers that apply semantic reward/cost signs and record diagnostics.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    public static partial class RocketRewardModel
    {
        // This protects divisions and exponential falloffs from zero. It is a
        // numerical safety epsilon, not a user-facing reward or task parameter.
        const float NumericalEpsilon = 0.0001f;

        /// <summary>
        /// Evaluates one physics step from the complete objective for the active
        /// scenario. Objective objects are expected to have been validated when
        /// training starts; evaluation never mutates or silently clamps them.
        /// </summary>
        public static RewardDecision Evaluate(
            ScenarioType scenario,
            RewardTerms terms,
            RewardRuntimeContext context,
            ScenarioObjectiveConfig objective,
            RewardContributionBuffer contributions = null)
        {
            if (objective == null)
                throw new ArgumentNullException(nameof(objective));
            if (objective.rewards == null)
                throw new ArgumentException("The scenario objective has no reward parameters.", nameof(objective));
            if (objective.shaping == null)
                throw new ArgumentException("The scenario objective has no shaping parameters.", nameof(objective));
            if (objective.terminations == null)
                throw new ArgumentException("The scenario objective has no termination parameters.", nameof(objective));

            contributions?.BeginEvaluation(scenario, objective.rewards);

            return scenario switch
            {
                ScenarioType.ChopstickLanding => ChopstickLanding(terms, context, objective, contributions),
                ScenarioType.LegLanding => LegLanding(terms, context, objective, contributions),
                ScenarioType.Hover => Hover(terms, context, objective, contributions),
                ScenarioType.HoverTracking => HoverTracking(terms, context, objective, contributions),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario), scenario, "The scenario is not part of the current schema.")
            };
        }

        /// <summary>Converts an error magnitude into a smooth 0..1 proximity score.</summary>
        static float Exp01(float value, float scale) =>
            Mathf.Exp(-Mathf.Abs(value) / Mathf.Max(Mathf.Abs(scale), NumericalEpsilon));

        /// <summary>Returns a safe positive divisor without changing configured intent.</summary>
        static float SafeScale(float scale) => Mathf.Max(Mathf.Abs(scale), NumericalEpsilon);

        static float RewardRate(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude,
            float feature)
        {
            float value = magnitude * feature;
            contributions?.AddRate(id, feature, magnitude, value);
            return value;
        }

        static float CostRate(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude,
            float feature)
        {
            float value = -magnitude * feature;
            contributions?.AddRate(id, feature, -magnitude, value);
            return value;
        }

        static float EventReward(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude,
            float countOrFeature = 1f)
        {
            float value = magnitude * countOrFeature;
            contributions?.AddEvent(id, countOrFeature, magnitude, value);
            return value;
        }

        static float EventCost(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude,
            float countOrFeature = 1f)
        {
            float value = -magnitude * countOrFeature;
            contributions?.AddEvent(id, countOrFeature, -magnitude, value);
            return value;
        }

        static float TerminalReward(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude)
        {
            contributions?.AddTerminal(id, 1f, magnitude, magnitude);
            return magnitude;
        }

        static float TerminalCost(
            RewardContributionBuffer contributions,
            RewardParameterId id,
            float magnitude)
        {
            float value = -magnitude;
            contributions?.AddTerminal(id, 1f, -magnitude, value);
            return value;
        }

        static float TiltDegrees(float upDot) =>
            Mathf.Acos(Mathf.Clamp(upDot, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
