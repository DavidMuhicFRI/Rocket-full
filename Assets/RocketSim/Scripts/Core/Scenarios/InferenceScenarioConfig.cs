// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Scenarios/InferenceScenarioConfig.cs
// Purpose: Stores editable inference spawn profiles for each supported scenario.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Separates reproducible thesis evaluation from the editable visual
    /// sandbox. StandardEvaluation deliberately ignores manual spawn ranges.
    /// </summary>
    public enum InferencePurpose
    {
        StandardEvaluation,
        ManualInference
    }

    /// <summary>
    /// Settings owned by the evaluator rather than by a trained run. Keeping
    /// the seed and episode count here lets the panel reuse one common test
    /// suite while it loads different trained models.
    /// </summary>
    [Serializable]
    public class EvaluationConfig
    {
        public const int DefaultEpisodeCount = 200;
        public const int DefaultEvaluationSeed = 20257;

        [Min(1)] public int episodeCount = DefaultEpisodeCount;
        [Min(0)] public int seed = DefaultEvaluationSeed;

        /// <summary>Clamps evaluator inputs before a session starts.</summary>
        public void Clamp()
        {
            episodeCount = Mathf.Clamp(episodeCount, 1, 10_000);
            seed = Mathf.Max(0, seed);
        }
    }

    [Serializable]
    public class InferenceScenarioConfig
    {
        // The old `landing` field is kept for serialized-data compatibility;
        // its behavior was always the chopstick-catch task.
        public InferenceSpawnProfile landing = new();
        public InferenceSpawnProfile legLanding = new();
        public InferenceSpawnProfile hover = new();
        public InferenceSpawnProfile hoverTracking = new();
        public InferenceSpawnProfile takeoff = new();
        public InferenceSpawnProfile bellyFlop = new();

        /// <summary>
        /// Returns the spawn profile for the selected inference scenario and
        /// initializes defaults when serialized data is missing.
        /// </summary>
        public InferenceSpawnProfile ForScenario(ScenarioType scenario)
        {
            landing ??= new InferenceSpawnProfile();
            legLanding ??= new InferenceSpawnProfile();
            hover ??= new InferenceSpawnProfile();
            hoverTracking ??= new InferenceSpawnProfile();
            takeoff ??= new InferenceSpawnProfile();
            bellyFlop ??= new InferenceSpawnProfile();

            ScenarioType resolvedScenario = ScenarioCatalog.Get(scenario).Type;
            InferenceSpawnProfile profile = resolvedScenario switch
            {
                ScenarioType.ChopstickLanding => landing,
                ScenarioType.LegLanding => legLanding,
                ScenarioType.Hover => hover,
                ScenarioType.HoverTracking => hoverTracking,
                ScenarioType.Takeoff => takeoff,
                ScenarioType.BellyFlop => bellyFlop,
                _ => landing
            };
            profile.EnsureInitialized(resolvedScenario);
            return profile;
        }
    }

    [Serializable]
    public class InferenceSpawnProfile
    {
        public bool initialized;
        public bool randomizeEachEpisode = true;
        public float altitudeMin;
        public float altitudeMax;
        public float horizontalOffsetMax;
        public float verticalSpeedMin;
        public float verticalSpeedMax;
        public float horizontalSpeedMin;
        public float horizontalSpeedMax;
        public float baseTiltDeg;
        public float tiltVariationDeg;
        public float yawMinDeg;
        public float yawMaxDeg;
        public float angularSpeedMaxDegS;

        /// <summary>
        /// Fills a newly-created profile with scenario-appropriate spawn ranges
        /// before inference episodes sample from it.
        /// </summary>
        public void EnsureInitialized(ScenarioType scenario)
        {
            if (initialized) return;

            ApplyDefaults(ScenarioCatalog.Get(scenario).InferenceSpawn);
            initialized = true;
        }

        /// <summary>
        /// Restores the catalog's immutable spawn distribution. Standard
        /// evaluation calls this so prior manual-inference edits cannot leak
        /// into a controlled benchmark.
        /// </summary>
        public void ResetToDefaults(ScenarioType scenario)
        {
            ApplyDefaults(ScenarioCatalog.Get(scenario).InferenceSpawn);
            initialized = true;
        }

        /// <summary>
        /// Copies catalog defaults into this editable profile. Yaw starts with
        /// the full -180 to +180 degree range because heading is scenario-neutral.
        /// </summary>
        void ApplyDefaults(InferenceSpawnDefaults defaults)
        {
            randomizeEachEpisode = true;
            altitudeMin = defaults.AltitudeMin;
            altitudeMax = defaults.AltitudeMax;
            horizontalOffsetMax = defaults.HorizontalOffsetMax;
            verticalSpeedMin = defaults.VerticalSpeedMin;
            verticalSpeedMax = defaults.VerticalSpeedMax;
            horizontalSpeedMin = defaults.HorizontalSpeedMin;
            horizontalSpeedMax = defaults.HorizontalSpeedMax;
            baseTiltDeg = defaults.BaseTiltDeg;
            tiltVariationDeg = defaults.TiltVariationDeg;
            yawMinDeg = -180f;
            yawMaxDeg = 180f;
            angularSpeedMaxDegS = defaults.AngularSpeedMaxDegS;
        }

        /// <summary>
        /// Clamps all editable spawn ranges to broad physical limits before
        /// they are used to randomize inference starts.
        /// </summary>
        public void Clamp()
        {
            altitudeMin = Mathf.Clamp(altitudeMin, 0f, 1500f);
            altitudeMax = Mathf.Clamp(altitudeMax, 0f, 1500f);
            horizontalOffsetMax = Mathf.Clamp(horizontalOffsetMax, 0f, 200f);
            verticalSpeedMin = Mathf.Clamp(verticalSpeedMin, -150f, 150f);
            verticalSpeedMax = Mathf.Clamp(verticalSpeedMax, -150f, 150f);
            horizontalSpeedMin = Mathf.Clamp(horizontalSpeedMin, 0f, 50f);
            horizontalSpeedMax = Mathf.Clamp(horizontalSpeedMax, 0f, 50f);
            baseTiltDeg = Mathf.Clamp(baseTiltDeg, 0f, 180f);
            tiltVariationDeg = Mathf.Clamp(tiltVariationDeg, 0f, 45f);
            yawMinDeg = Mathf.Clamp(yawMinDeg, -180f, 180f);
            yawMaxDeg = Mathf.Clamp(yawMaxDeg, -180f, 180f);
            angularSpeedMaxDegS = Mathf.Clamp(angularSpeedMaxDegS, 0f, 90f);
        }

    }

}
