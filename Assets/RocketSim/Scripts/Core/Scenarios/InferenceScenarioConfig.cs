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
    [Serializable]
    public class InferenceScenarioConfig
    {
        public InferenceSpawnProfile landing = new();
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
            hover ??= new InferenceSpawnProfile();
            hoverTracking ??= new InferenceSpawnProfile();
            takeoff ??= new InferenceSpawnProfile();
            bellyFlop ??= new InferenceSpawnProfile();

            ScenarioType resolvedScenario = ScenarioCatalog.Get(scenario).Type;
            InferenceSpawnProfile profile = resolvedScenario switch
            {
                ScenarioType.Landing => landing,
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
            altitudeMin = Mathf.Clamp(altitudeMin, 0f, 500f);
            altitudeMax = Mathf.Clamp(altitudeMax, 0f, 500f);
            horizontalOffsetMax = Mathf.Clamp(horizontalOffsetMax, 0f, 100f);
            verticalSpeedMin = Mathf.Clamp(verticalSpeedMin, -100f, 100f);
            verticalSpeedMax = Mathf.Clamp(verticalSpeedMax, -100f, 100f);
            horizontalSpeedMin = Mathf.Clamp(horizontalSpeedMin, 0f, 30f);
            horizontalSpeedMax = Mathf.Clamp(horizontalSpeedMax, 0f, 30f);
            baseTiltDeg = Mathf.Clamp(baseTiltDeg, 0f, 180f);
            tiltVariationDeg = Mathf.Clamp(tiltVariationDeg, 0f, 45f);
            yawMinDeg = Mathf.Clamp(yawMinDeg, -180f, 180f);
            yawMaxDeg = Mathf.Clamp(yawMaxDeg, -180f, 180f);
            angularSpeedMaxDegS = Mathf.Clamp(angularSpeedMaxDegS, 0f, 90f);
        }

        /// <summary>
        /// Samples within a configured range when randomization is enabled, or
        /// returns the midpoint for deterministic inference starts.
        /// </summary>
        public float Sample(float a, float b)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            return randomizeEachEpisode ? UnityEngine.Random.Range(min, max) : (min + max) * 0.5f;
        }
    }

}
