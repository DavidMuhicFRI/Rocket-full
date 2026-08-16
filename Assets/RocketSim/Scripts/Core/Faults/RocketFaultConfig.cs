// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Faults/RocketFaultConfig.cs
// Purpose: Defines user-editable actuator faults without mixing them into the
// general environment configuration file.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Actuator faults supported by the simulator. Sensor and structural faults
    /// can be added later without changing the episode-fault runtime contract.
    /// </summary>
    public enum RocketFaultType
    {
        None,
        EngineThrustLoss,
        GimbalJam,
        FinEffectivenessLoss,
        RcsStuckClosed,
        RcsStuckOpen
    }

    /// <summary>Serializable settings edited by the Faults panel.</summary>
    [Serializable]
    public sealed class RocketFaultConfig
    {
        [Header("Training Faults")] public bool allowDuringTraining;
        [Range(0f, 1f)] public float faultyEpisodeProbability = 0.20f;
        [Range(0f, 1f)] public float trainingSeverityMin = 0.20f;
        [Range(0f, 1f)] public float trainingSeverityMax = 0.60f;
        public bool allowEngineThrustLoss = true;
        public bool allowGimbalJam = true;
        public bool allowFinEffectivenessLoss;
        public bool allowRcsStuckClosed;
        public bool allowRcsStuckOpen;
        [Range(1, 3)] public int maxSimultaneousFaults = 1;

        [Header("Evaluation Fault")] public bool evaluationFaultEnabled;
        public RocketFaultType evaluationFaultType = RocketFaultType.EngineThrustLoss;
        [Min(0)] public int evaluationTargetIndex;
        [Min(0f)] public float evaluationOnsetSeconds = 5f;
        [Tooltip("Zero means the fault remains active until the episode ends.")]
        [Min(0f)] public float evaluationDurationSeconds;
        [Range(0f, 1f)] public float evaluationSeverity = 0.50f;

        public int seed = 12345;

        /// <summary>Clamps values loaded from JSON or edited in the UI.</summary>
        public void Clamp()
        {
            faultyEpisodeProbability = Mathf.Clamp01(faultyEpisodeProbability);
            trainingSeverityMin = Mathf.Clamp01(trainingSeverityMin);
            trainingSeverityMax = Mathf.Max(trainingSeverityMin, Mathf.Clamp01(trainingSeverityMax));
            maxSimultaneousFaults = Mathf.Clamp(maxSimultaneousFaults, 1, 3);
            evaluationTargetIndex = Mathf.Max(0, evaluationTargetIndex);
            evaluationOnsetSeconds = Mathf.Max(0f, evaluationOnsetSeconds);
            evaluationDurationSeconds = Mathf.Max(0f, evaluationDurationSeconds);
            evaluationSeverity = Mathf.Clamp01(evaluationSeverity);
        }
    }
}
