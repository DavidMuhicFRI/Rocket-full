// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/RocketAgentSchema.cs
// Purpose: Configures ML-Agents behavior parameters so action and observation spaces match the selected hardware.
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
        public const int BaseObservationCount = 21;

        /// <summary>
        /// Calculates the vector observation size from the base state plus
        /// hardware-dependent engine, fin, and RCS observation slots.
        /// </summary>
        public static int ObservationSize(RocketPartsConfig parts)
        {
            int engineCount = parts.GetIndependentEngineCount();
            int finCount = parts.GetFinCount();
            int rcsCount = parts.GetRCSCount();
            return 3 * engineCount + finCount + rcsCount + BaseObservationCount;
        }

        /// <summary>
        /// Calculates the continuous action count for throttle/gimbal commands,
        /// fin deflections, and thresholded RCS valve requests.
        /// </summary>
        public static int ContinuousActionSize(RocketPartsConfig parts)
        {
            int engineCount = parts.GetIndependentEngineCount();
            int finCount = parts.GetFinCount();
            int rcsCount = parts.GetRCSCount();
            return 3 * engineCount + finCount + rcsCount;
        }

        /// <summary>
        /// Keeps the policy in a single continuous action space. RCS valves are
        /// thresholded continuous channels, not discrete branches, because the
        /// ML-Agents CUDA trainer path has a known categorical-device mismatch.
        /// </summary>
        public static int[] DiscreteActionBranches(RocketPartsConfig parts)
        {
            return new int[0];
        }

        /// <summary>
        /// Writes the ML-Agents behavior parameters so the policy action and
        /// observation spaces match the current rocket hardware, then selects training or inference mode.
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
            }
            else
            {
                behavior.BehaviorType = Unity.MLAgents.Policies.BehaviorType.Default;
                behavior.Model = null;
            }
        }
    }
}
