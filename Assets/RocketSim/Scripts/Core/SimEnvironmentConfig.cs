using System;
using UnityEngine;

namespace RocketSim
{
    public enum BehaviorType
    {
        Training,
        Inference
    }

    public enum WeatherType
    {
        Clear,
        Cloudy,
        Rainy,
        Stormy
    }

    public enum ScenarioType
    {
        Landing,
        Hover,
        HoverTracking,
        Takeoff,
        BellyFlop
    }

    /// <summary>
    /// Shared environment/run state. The same instance is assigned to every
    /// FalconAgent in a training batch so UI changes are applied consistently.
    /// </summary>
    [Serializable]
    public class SimEnvironmentConfig
    {
        [Header("Wind")] public bool windEnabled = false;
        public float windSpeed = 0f;
        public float windGustAmplitude = 0f;
        public float windChangeRate = 0f;
        [HideInInspector] public float savedWindSpeed = DefaultWindSpeed;
        [HideInInspector] public float savedWindGustAmplitude = DefaultWindGustAmplitude;
        [HideInInspector] public float savedWindChangeRate = DefaultWindChangeRate;

        public const float DefaultWindSpeed = 8f;
        public const float DefaultWindGustAmplitude = 5f;
        public const float DefaultWindChangeRate = 0.25f;

        [Header("Weather")] public WeatherType weather = WeatherType.Clear;

        [Header("Scenario")] public ScenarioType scenario = ScenarioType.Landing;
        [HideInInspector]
        public bool moveTargetEnabled = false;
        [HideInInspector]
        public float targetMoveInterval = 35f;
        [HideInInspector]
        public float targetMoveRadius = 12f;
        [HideInInspector]
        public float hoverTrackSettleRadius = 6f;
        [HideInInspector]
        public float hoverTrackSuccessHoldTime = 1.0f;
        [HideInInspector]
        public float hoverTrackSuccessMaxSpeed = 1.5f;
        [HideInInspector]
        public float hoverTrackSuccessMaxTiltDeg = 8f;
        [HideInInspector] public int hoverTrackCurriculumSuccesses;
        [HideInInspector] public float hoverTrackCurriculumProgress;

        public const float HoverTrackStartMoveRadius = 16f;
        public const float HoverTrackEndMoveRadius = 40f;
        public const float HoverTrackStartSettleRadius = 10f;
        public const float HoverTrackEndSettleRadius = 3f;
        public const float HoverTrackStartHoldTime = 0.5f;
        public const float HoverTrackEndHoldTime = 1.5f;
        public const float HoverTrackStartMaxSpeed = 3f;
        public const float HoverTrackEndMaxSpeed = 1f;
        public const float HoverTrackStartMaxTiltDeg = 15f;
        public const float HoverTrackEndMaxTiltDeg = 6f;
        const float HoverTrackCurriculumBatchesToMostlyHard = 120f;

        [Header("Run Config")] public string runId = "Falcon9DefaultRun";
        public BehaviorType behaviorType = BehaviorType.Training;

        public float AirDensityMultiplier => weather switch
        {
            WeatherType.Clear => 1.000f,
            WeatherType.Cloudy => 1.020f,
            WeatherType.Rainy => 1.050f,
            WeatherType.Stormy => 1.080f,
            _ => 1.000f
        };

        public float EffectiveGustAmp => weather switch
        {
            WeatherType.Stormy => windGustAmplitude * 2.5f,
            WeatherType.Rainy => windGustAmplitude * 1.5f,
            _ => windGustAmplitude
        };

        public void SetWindEnabled(bool enabled)
        {
            if (enabled)
            {
                windSpeed = savedWindSpeed > 0f ? savedWindSpeed : DefaultWindSpeed;
                windGustAmplitude = savedWindGustAmplitude > 0f ? savedWindGustAmplitude : DefaultWindGustAmplitude;
                windChangeRate = savedWindChangeRate > 0f ? savedWindChangeRate : DefaultWindChangeRate;
                windEnabled = true;
                return;
            }

            if (windSpeed > 0f) savedWindSpeed = windSpeed;
            if (windGustAmplitude > 0f) savedWindGustAmplitude = windGustAmplitude;
            if (windChangeRate > 0f) savedWindChangeRate = windChangeRate;

            windSpeed = 0f;
            windGustAmplitude = 0f;
            windChangeRate = 0f;
            windEnabled = false;
        }

        public void SetWindSpeed(float value)
        {
            windSpeed = value;
            if (windEnabled && value > 0f) savedWindSpeed = value;
        }

        public void SetWindGustAmplitude(float value)
        {
            windGustAmplitude = value;
            if (windEnabled && value > 0f) savedWindGustAmplitude = value;
        }

        public void SetWindChangeRate(float value)
        {
            windChangeRate = value;
            if (windEnabled && value > 0f) savedWindChangeRate = value;
        }

        public void ResetHoverTrackCurriculum()
        {
            hoverTrackCurriculumSuccesses = 0;
            ApplyHoverTrackCurriculum(1);
        }

        public void AdvanceHoverTrackCurriculum(int activeAreaCount)
        {
            hoverTrackCurriculumSuccesses++;
            ApplyHoverTrackCurriculum(activeAreaCount);
        }

        public void ApplyHoverTrackCurriculum(int activeAreaCount)
        {
            if (scenario != ScenarioType.HoverTracking) return;

            moveTargetEnabled = true;

            float batchSuccesses = hoverTrackCurriculumSuccesses / Mathf.Max(1f, activeAreaCount);
            float rawProgress = 1f - Mathf.Exp(-batchSuccesses / HoverTrackCurriculumBatchesToMostlyHard);
            hoverTrackCurriculumProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rawProgress));

            targetMoveRadius = Mathf.Lerp(HoverTrackStartMoveRadius, HoverTrackEndMoveRadius, hoverTrackCurriculumProgress);
            hoverTrackSettleRadius = Mathf.Lerp(HoverTrackStartSettleRadius, HoverTrackEndSettleRadius, hoverTrackCurriculumProgress);
            hoverTrackSuccessHoldTime = Mathf.Lerp(HoverTrackStartHoldTime, HoverTrackEndHoldTime, hoverTrackCurriculumProgress);
            hoverTrackSuccessMaxSpeed = Mathf.Lerp(HoverTrackStartMaxSpeed, HoverTrackEndMaxSpeed, hoverTrackCurriculumProgress);
            hoverTrackSuccessMaxTiltDeg = Mathf.Lerp(HoverTrackStartMaxTiltDeg, HoverTrackEndMaxTiltDeg, hoverTrackCurriculumProgress);
        }
    }
}
