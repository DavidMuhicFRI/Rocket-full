// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/TelemetryConfig.cs
// Purpose: Stores which families of telemetry columns are enabled and how many
// engine/fin slots the current rocket needs in its run-specific CSV schema.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    [Serializable]
    /// <summary>
    /// Shared by the panel, manager, metric catalog, and logger. Identity columns
    /// are always written; the toggles control optional measurement groups.
    /// </summary>
    public class TelemetryConfig
    {
        // ── Log groups ────────────────────────────────────────────────────────
        // Identity (WallTime / AreaIndex / Episode / Step) is ALWAYS written
        
        public bool logGoalMetrics        = true;  // scenario objective distance/altitude error
        public bool logAttitudeMetrics    = true;  // tilt, uprightness, roll/pitch, angular rates, AoA
        public bool logVelocityMetrics    = true;  // speed, planar/vertical motion, closure rate
        public bool logControlMetrics     = true;  // throttle, gimbal, fins, fuel
        public bool logAeroLoadMetrics    = true;  // dynamic pressure, G, heat, stress
        public bool logEnvironmentMetrics = false; // wind/gust scalars
        public bool logRewardMetrics      = true;  // scalar reward accumulated this step

        [Range(1, 100)]
        [Tooltip("Writes one detailed training trajectory row every N physics steps. Episode statistics still consume every physics step, and evaluation always logs every step.")]
        public int trainingStepLogInterval = 10;

        // ── Set by TrainingAreaManager at Launch from partsConfig ─────────────
        [HideInInspector] public int  activeEngineCount   = 1;
        [HideInInspector] public bool independentEngines  = false;
        [HideInInspector] public int  activeFinCount      = 4;

        public const int MaxEngines = 9;
        public const int MaxFins    = 4;

        /// <summary>
        /// Number of engine columns needed. Shared control logs one channel;
        /// independent control logs each active engine.
        /// </summary>
        public int LoggedEngineSlots => independentEngines ? activeEngineCount : 1;

        /// <summary>
        /// Sanitized trajectory sampling interval used only for training CSVs.
        /// Episode accumulators always see every simulation step.
        /// </summary>
        public int TrainingStepLogInterval => Mathf.Clamp(trainingStepLogInterval, 1, 100);
    }
}
