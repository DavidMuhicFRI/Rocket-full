using System;
using UnityEngine;

namespace RocketSim
{
    [Serializable]
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

        // ── Set by TrainingAreaManager at Launch from partsConfig ─────────────
        [HideInInspector] public int  activeEngineCount   = 1;
        [HideInInspector] public bool independentEngines  = false;
        [HideInInspector] public int  activeFinCount      = 4;

        public const int MaxEngines = 9;
        public const int MaxFins    = 4;

        public int LoggedEngineSlots => independentEngines ? activeEngineCount : 1;
    }
}
