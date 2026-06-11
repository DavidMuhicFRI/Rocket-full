using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Scenario-level constants that must stay consistent between rewards,
    /// telemetry analysis, spawning, and UI defaults.
    /// </summary>
    public static class ScenarioProfile
    {
        public static bool UsesMovingTarget(ScenarioType scenario)
        {
            return scenario == ScenarioType.HoverTracking;
        }

        public static float GoalAltitude(ScenarioType scenario, float rocketLength)
        {
            return scenario switch
            {
                ScenarioType.Hover => 30f,
                ScenarioType.HoverTracking => 30f,
                ScenarioType.Takeoff => 120f,
                _ => 0.5f
            };
        }

        public static Vector3 GoalPosition(ScenarioType scenario, Transform targetPad, float rocketLength)
        {
            Vector3 target = targetPad ? targetPad.localPosition : Vector3.zero;
            return new Vector3(target.x, GoalAltitude(scenario, rocketLength), target.z);
        }
    }
}
