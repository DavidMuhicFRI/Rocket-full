// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Scenarios/ScenarioCatalog.cs
// Purpose: Defines scenario identities plus shared scenario metadata and defaults.
// This is the single lookup table for scenario names, starting fuel, goal
// altitude, moving-target behavior, and default inference spawn ranges.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace RocketSim
{
    public enum ScenarioType
    {
        // Explicit values protect saved JsonUtility/Unity data when scenarios
        // are renamed or inserted. Value zero was the old generic Landing task;
        // that implementation was chopstick-specific, so it remains the catch.
        ChopstickLanding = 0,
        Hover = 1,
        HoverTracking = 2,
        Takeoff = 3,
        BellyFlop = 4,
        LegLanding = 5
    }

    /// <summary>
    /// Keeps landing-family checks readable without hiding which task owns
    /// chopstick-only or foot-contact-only behavior.
    /// </summary>
    public static class ScenarioTypeExtensions
    {
        public static bool IsLanding(this ScenarioType scenario) =>
            scenario == ScenarioType.ChopstickLanding || scenario == ScenarioType.LegLanding;

        public static bool IsChopstickLanding(this ScenarioType scenario) =>
            scenario == ScenarioType.ChopstickLanding;

        public static bool IsLegLanding(this ScenarioType scenario) =>
            scenario == ScenarioType.LegLanding;

        /// <summary>
        /// Returns whether the scenario has a fixed, seeded evaluator contract.
        /// Fixed hover is evaluated until fuel depletion or an existing failure
        /// terminal; the two landing tasks use their full-difficulty benchmark.
        /// </summary>
        public static bool SupportsStandardEvaluation(this ScenarioType scenario) =>
            scenario.IsLanding() || scenario == ScenarioType.Hover;
    }

    public readonly struct ScenarioDefinition
    {
        /// <summary>
        /// Creates one immutable scenario description used by both runtime code
        /// and the configuration panel.
        /// </summary>
        public ScenarioDefinition(
            ScenarioType type,
            string displayName,
            string description,
            float startFuelFraction,
            float defaultGoalAltitude,
            bool usesMovingTarget,
            InferenceSpawnDefaults inferenceSpawn)
        {
            Type = type;
            DisplayName = displayName;
            Description = description;
            StartFuelFraction = startFuelFraction;
            DefaultGoalAltitude = defaultGoalAltitude;
            UsesMovingTarget = usesMovingTarget;
            InferenceSpawn = inferenceSpawn;
        }

        public ScenarioType Type { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public float StartFuelFraction { get; }
        public float DefaultGoalAltitude { get; }
        public bool UsesMovingTarget { get; }
        public InferenceSpawnDefaults InferenceSpawn { get; }

        /// <summary>
        /// Allows UI code to iterate definitions as the simple tuple
        /// (type, display name, description) without copying the stored data.
        /// </summary>
        public void Deconstruct(out ScenarioType type, out string displayName, out string description)
        {
            type = Type;
            displayName = DisplayName;
            description = Description;
        }
    }

    public readonly struct InferenceSpawnDefaults
    {
        /// <summary>
        /// Stores the initial position, velocity, tilt, and rotation ranges used
        /// the first time an inference profile is opened.
        /// </summary>
        public InferenceSpawnDefaults(
            float altitudeMin,
            float altitudeMax,
            float horizontalOffsetMax,
            float verticalSpeedMin,
            float verticalSpeedMax,
            float horizontalSpeedMin,
            float horizontalSpeedMax,
            float baseTiltDeg,
            float tiltVariationDeg,
            float angularSpeedMaxDegS)
        {
            AltitudeMin = altitudeMin;
            AltitudeMax = altitudeMax;
            HorizontalOffsetMax = horizontalOffsetMax;
            VerticalSpeedMin = verticalSpeedMin;
            VerticalSpeedMax = verticalSpeedMax;
            HorizontalSpeedMin = horizontalSpeedMin;
            HorizontalSpeedMax = horizontalSpeedMax;
            BaseTiltDeg = baseTiltDeg;
            TiltVariationDeg = tiltVariationDeg;
            AngularSpeedMaxDegS = angularSpeedMaxDegS;
        }

        public float AltitudeMin { get; }
        public float AltitudeMax { get; }
        public float HorizontalOffsetMax { get; }
        public float VerticalSpeedMin { get; }
        public float VerticalSpeedMax { get; }
        public float HorizontalSpeedMin { get; }
        public float HorizontalSpeedMax { get; }
        public float BaseTiltDeg { get; }
        public float TiltVariationDeg { get; }
        public float AngularSpeedMaxDegS { get; }

    }

    public static class ScenarioCatalog
    {
        public const float ChopstickLandingStartFuelFraction = 0.08f;
        public const float LegLandingStartFuelFraction = 0.08f;
        // Retained as a source-compatible generic default for older helpers.
        public const float LandingStartFuelFraction = ChopstickLandingStartFuelFraction;
        public const float HoverStartFuelFraction = 0.1f;
        public const float HoverTrackingStartFuelFraction = 0.1f;
        public const float HoverStartAltitude = 80f;
        public const float HoverTrackingStartAltitude = 80f;
        public const float TakeoffStartFuelFraction = 1.00f;
        public const float BellyFlopStartFuelFraction = 0.12f;

        public const float HoverGoalAltitude = 30f;
        public const float HoverTrackingGoalAltitude = 30f;
        public const float TakeoffGoalAltitude = 120f;
        public const float BellyFlopGoalAltitude = 0.5f;

        public const float DefaultTerminalAltitude = 0.5f;

        static readonly ScenarioDefinition[] Entries =
        {
            new(
                ScenarioType.ChopstickLanding,
                "Chopstick Catch Landing",
                "Guide a reusable booster into the tower's kinematic catch envelope",
                ChopstickLandingStartFuelFraction,
                SimEnvironmentConfig.DefaultLandingCatchAltitude,
                false,
                new InferenceSpawnDefaults(250f, 500f, 50f, -70f, -25f, 0f, 12f, 0f, 8f, 20f)),
            new(
                ScenarioType.LegLanding,
                "Falcon 9 Leg Landing",
                "Perform a powered landing on four deployed feet and remain stable on the pad",
                LegLandingStartFuelFraction,
                0f,
                false,
                new InferenceSpawnDefaults(250f, 500f, 50f, -70f, -25f, 0f, 12f, 0f, 8f, 20f)),
            new(
                ScenarioType.Hover,
                "Hover",
                "Hold altitude and balance",
                HoverStartFuelFraction,
                HoverGoalAltitude,
                false,
                new InferenceSpawnDefaults(25f, 35f, 8f, -2f, 2f, 0f, 3f, 0f, 5f, 8f)),
            new(
                ScenarioType.HoverTracking,
                "Hover Track",
                "Follow moving pad at fixed altitude",
                HoverTrackingStartFuelFraction,
                HoverTrackingGoalAltitude,
                true,
                new InferenceSpawnDefaults(25f, 35f, 8f, -2f, 2f, 0f, 3f, 0f, 5f, 8f)),
            new(
                ScenarioType.Takeoff,
                "Takeoff",
                "Lift off",
                TakeoffStartFuelFraction,
                TakeoffGoalAltitude,
                false,
                new InferenceSpawnDefaults(0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, 2f, 2f)),
            new(
                ScenarioType.BellyFlop,
                "Belly Flop",
                "Reorient from horizontal",
                BellyFlopStartFuelFraction,
                BellyFlopGoalAltitude,
                false,
                new InferenceSpawnDefaults(90f, 140f, 15f, -22f, -10f, 0f, 5f, 85f, 7f, 15f)),
        };

        public static readonly IReadOnlyList<ScenarioDefinition> All = Array.AsReadOnly(Entries);

        /// <summary>
        /// Returns metadata for a scenario. Unknown enum values safely fall back
        /// to ChopstickLanding so callers always receive a valid definition.
        /// </summary>
        public static ScenarioDefinition Get(ScenarioType scenario)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Type == scenario)
                    return Entries[i];
            }

            return Entries[0];
        }
    }
}
