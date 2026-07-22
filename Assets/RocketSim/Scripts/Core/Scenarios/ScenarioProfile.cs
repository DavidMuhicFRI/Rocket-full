// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Scenarios/ScenarioProfile.cs
// Purpose: Provides scenario-specific default positions, goals, terminal altitudes, and fuel profiles.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Scenario-level constants that must stay consistent between rewards,
    /// telemetry analysis, spawning, and UI defaults.
    /// </summary>
    public static class ScenarioProfile
    {
        public const float ChopstickLandingStartFuelFraction = ScenarioCatalog.ChopstickLandingStartFuelFraction;
        public const float LegLandingStartFuelFraction = ScenarioCatalog.LegLandingStartFuelFraction;
        public const float LandingStartFuelFraction = ChopstickLandingStartFuelFraction;
        public const float HoverStartFuelFraction = ScenarioCatalog.HoverStartFuelFraction;
        public const float HoverTrackingStartFuelFraction = ScenarioCatalog.HoverTrackingStartFuelFraction;
        public const float TakeoffStartFuelFraction = ScenarioCatalog.TakeoffStartFuelFraction;
        public const float BellyFlopStartFuelFraction = ScenarioCatalog.BellyFlopStartFuelFraction;

        /// <summary>
        /// Returns whether the scenario uses a target pad that moves during an episode.
        /// </summary>
        public static bool UsesMovingTarget(ScenarioType scenario) => ScenarioCatalog.Get(scenario).UsesMovingTarget;
        
        /// <summary>
        /// Returns the fraction of maximum fuel capacity used when initializing
        /// the selected scenario.
        /// </summary>
        public static float StartFuelFraction(ScenarioType scenario) => ScenarioCatalog.Get(scenario).StartFuelFraction;

        /// <summary>
        /// Converts the scenario fuel fraction into a clamped fuel mass for the
        /// current hardware capacity.
        /// </summary>
        public static float StartFuelMass(ScenarioType scenario, float maxFuelCapacity)
        {
            float capacity = Mathf.Max(0f, maxFuelCapacity);
            return Mathf.Clamp(StartFuelFraction(scenario) * capacity, 0f, capacity);
        }

        /// <summary>
        /// Returns the default target altitude for a scenario when no environment
        /// config overrides are needed.
        /// </summary>
        public static float GoalAltitude(ScenarioType scenario)
        {
            return GoalAltitude(scenario, null);
        }

        /// <summary>
        /// Returns the scenario target altitude, using landing catch altitude
        /// from the environment config for chopstick-style landing.
        /// </summary>
        public static float GoalAltitude(ScenarioType scenario, SimEnvironmentConfig envConfig)
        {
            ScenarioDefinition definition = ScenarioCatalog.Get(scenario);
            return definition.Type == ScenarioType.ChopstickLanding
                ? LandingCatchAltitude(envConfig)
                : definition.DefaultGoalAltitude;
        }

        /// <summary>
        /// Returns the local target position for the scenario using the target
        /// pad's planar location and default altitude rules.
        /// </summary>
        public static Vector3 GoalPosition(ScenarioType scenario, Transform targetPad)
        {
            return GoalPosition(scenario, targetPad, null);
        }

        /// <summary>
        /// Returns the local target position for the scenario using the target
        /// pad's planar location and environment-aware altitude rules.
        /// </summary>
        public static Vector3 GoalPosition(ScenarioType scenario, Transform targetPad, SimEnvironmentConfig envConfig)
        {
            if (scenario == ScenarioType.LegLanding &&
                LandingPadSurface.TryGetLocalTopCenter(targetPad, out Vector3 padTopCenter))
                return padTopCenter;

            Vector3 target = targetPad ? targetPad.localPosition : Vector3.zero;
            return new Vector3(target.x, GoalAltitude(scenario, envConfig), target.z);
        }

        /// <summary>
        /// Returns the altitude threshold used by terminal checks for the scenario.
        /// </summary>
        public static float TerminalAltitude(ScenarioType scenario, SimEnvironmentConfig envConfig)
        {
            return ScenarioCatalog.Get(scenario).Type == ScenarioType.ChopstickLanding
                ? LandingCatchAltitude(envConfig)
                : ScenarioCatalog.DefaultTerminalAltitude;
        }

        /// <summary>
        /// Returns a valid landing catch altitude, falling back to the default
        /// when the environment config is missing or invalid.
        /// </summary>
        static float LandingCatchAltitude(SimEnvironmentConfig envConfig) => Mathf.Max(0.5f, envConfig?.landingCatchAltitude ?? SimEnvironmentConfig.DefaultLandingCatchAltitude);
    }
}
