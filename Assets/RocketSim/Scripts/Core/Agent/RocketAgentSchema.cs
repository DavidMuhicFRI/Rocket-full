// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/RocketAgentSchema.cs
// Purpose: Builds the ML-Agents interface required by the selected vehicle.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using Unity.InferenceEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

namespace RocketSim
{
    /// <summary>
    /// Single source of truth for the ML-Agents vector observation and action sizes.
    /// Keep this in lockstep with FalconAgent.CollectObservations() and
    /// FalconAgent.OnActionReceived().
    /// </summary>
    public static class RocketAgentSchema
    {
        public const int LandingFootObservationCount = 4;
        public const int BaseObservationCount = 21 + LandingFootObservationCount;
        public const int EngineTimingObservationCount = 5;
        public const int EngineObservationCount = 3 + EngineTimingObservationCount;
        public const int CanonicalDecisionPeriod = 3;

        public const int ThrottleActionOffset = 0;

        /// <summary>
        /// Returns the fixed flight-state observations plus only the actuator
        /// state channels that exist on the selected vehicle.
        /// </summary>
        public static int ObservationSize(RocketPartsConfig parts)
        {
            if (parts == null) return BaseObservationCount;

            return BaseObservationCount +
                   EngineObservationCount * parts.GetIndependentEngineCount() +
                   parts.GetFinCount() +
                   parts.GetRCSCount();
        }

        /// <summary>
        /// Returns throttle plus two-axis gimbal control for every engine
        /// command channel, followed by the enabled fin and RCS channels.
        /// </summary>
        public static int ContinuousActionSize(RocketPartsConfig parts)
        {
            if (parts == null) return 0;

            return 3 * parts.GetIndependentEngineCount() +
                   parts.GetFinCount() +
                   parts.GetRCSCount();
        }

        /// <summary>First two-axis gimbal command after all throttle commands.</summary>
        public static int GimbalActionOffset(int engineChannelCount) => engineChannelCount;

        /// <summary>First fin command after throttle and gimbal commands.</summary>
        public static int FinActionOffset(int engineChannelCount) => 3 * engineChannelCount;

        /// <summary>First RCS valve command after every engine and fin command.</summary>
        public static int RcsActionOffset(int engineChannelCount, int finCount) =>
            FinActionOffset(engineChannelCount) + finCount;

        /// <summary>
        /// Keeps the policy in a single continuous action space. RCS valves are
        /// binary actuators driven by continuous commands above a neutral
        /// dead-zone threshold, not discrete branches, because the
        /// ML-Agents CUDA trainer path has a known categorical-device mismatch.
        /// </summary>
        public static int[] DiscreteActionBranches(RocketPartsConfig parts)
        {
            return new int[0];
        }

        /// <summary>
        /// Writes the vehicle-sized ML-Agents behavior parameters, then selects
        /// training or deterministic inference mode.
        /// </summary>
        public static void ConfigureBehavior(
            BehaviorParameters behavior,
            RocketPartsConfig parts,
            BehaviorType mode,
            ModelAsset model = null)
        {
            if (!behavior) return;

            behavior.BrainParameters.VectorObservationSize = ObservationSize(parts);
            behavior.BrainParameters.ActionSpec = new ActionSpec(
                ContinuousActionSize(parts),
                DiscreteActionBranches(parts));

            if (mode == BehaviorType.Inference)
            {
                behavior.BehaviorType = Unity.MLAgents.Policies.BehaviorType.InferenceOnly;
                behavior.Model = model;
                // Thesis evaluation uses the policy mean rather than sampling
                // fresh action noise, so every model sees the same deterministic
                // policy and environment-seed combination.
                behavior.DeterministicInference = true;
            }
            else
            {
                behavior.BehaviorType = Unity.MLAgents.Policies.BehaviorType.Default;
                behavior.Model = null;
                behavior.DeterministicInference = false;
            }
        }

        /// <summary>
        /// Applies the one authoritative control frequency used by training and
        /// evaluation, overriding stale values inherited through Unity prefabs.
        /// </summary>
        public static void ConfigureDecisionRequester(Unity.MLAgents.DecisionRequester requester)
        {
            if (!requester) return;
            requester.DecisionPeriod = CanonicalDecisionPeriod;
            requester.TakeActionsBetweenDecisions = true;
        }
    }
}
