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
        public const int BaseObservationCount = 19;

        public static int ObservationSize(RocketPartsConfig parts)
        {
            int engineCount = parts.GetIndependentEngineCount();
            int finCount = parts.GetFinCount();
            int rcsCount = parts.GetRCSCount();
            return 3 * engineCount + finCount + rcsCount + BaseObservationCount;
        }

        public static int ActionSize(RocketPartsConfig parts)
        {
            int engineCount = parts.GetIndependentEngineCount();
            int finCount = parts.GetFinCount();
            int rcsCount = parts.GetRCSCount();
            return 3 * engineCount + finCount + rcsCount;
        }

        public static void ConfigureBehavior(
            BehaviorParameters behavior,
            RocketPartsConfig parts,
            BehaviorType mode,
            ModelAsset model = null)
        {
            if (!behavior) return;

            behavior.BrainParameters.VectorObservationSize = ObservationSize(parts);
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(ActionSize(parts));

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
