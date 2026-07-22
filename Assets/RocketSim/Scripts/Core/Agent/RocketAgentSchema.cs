// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/RocketAgentSchema.cs
// Purpose: Configures one experiment-safe ML-Agents interface shared by every
// hardware ablation and transfer-learning scenario.
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
        public const int MaxEngineChannels = 9;
        public const int MaxFinChannels = 4;
        public const int MaxRcsChannels = RcsComponent.JetCount;
        public const int LandingFootObservationCount = 4;
        public const int BaseObservationCount = 21 + LandingFootObservationCount;
        public const int EngineTimingObservationCount = 5;
        public const int CanonicalDecisionPeriod = 3;

        public const int ThrottleActionOffset = 0;
        public const int GimbalActionOffset = ThrottleActionOffset + MaxEngineChannels;
        public const int FinActionOffset = GimbalActionOffset + 2 * MaxEngineChannels;
        public const int RcsActionOffset = FinActionOffset + MaxFinChannels;
        public const int CanonicalContinuousActionCount = RcsActionOffset + MaxRcsChannels;

        public const int CanonicalObservationCount =
            (3 + EngineTimingObservationCount) * MaxEngineChannels +
            MaxFinChannels + MaxRcsChannels + BaseObservationCount;

        /// <summary>
        /// Returns the fixed vector size used by every ablation. Missing
        /// hardware writes zero-valued slots instead of changing policy shape.
        /// </summary>
        public static int ObservationSize(RocketPartsConfig parts)
        {
            return CanonicalObservationCount;
        }

        /// <summary>
        /// Returns the fixed action count. Commands addressed to unavailable
        /// actuator slots are consumed but deliberately become no-ops.
        /// </summary>
        public static int ContinuousActionSize(RocketPartsConfig parts)
        {
            return CanonicalContinuousActionCount;
        }

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
        /// Writes the canonical ML-Agents behavior parameters, then selects
        /// training or deterministic inference mode. Hardware availability is
        /// represented inside the fixed schema rather than by resizing it.
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
