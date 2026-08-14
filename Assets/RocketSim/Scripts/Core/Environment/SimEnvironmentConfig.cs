// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Environment/SimEnvironmentConfig.cs
// Purpose: Stores user-editable scenario, environment randomization, reward,
// curriculum, and behavior settings.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

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
        Stormy,
        Windy
    }

    /// <summary>
    /// Actuator faults supported by the first, deliberately small fault model.
    /// Sensor and structural faults can be added later without changing the panel workflow.
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

    [Serializable]
    public class RocketFaultConfig
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

        /// <summary>
        /// Repairs fault settings after loading or editing them. This keeps
        /// probabilities and severities between zero and one, keeps the maximum
        /// severity above the minimum, and prevents negative timing or indices.
        /// </summary>
        public void Clamp()
        {
            faultyEpisodeProbability = Mathf.Clamp01(faultyEpisodeProbability);
            trainingSeverityMin = Mathf.Clamp01(trainingSeverityMin);
            trainingSeverityMax = Mathf.Clamp01(trainingSeverityMax);
            if (trainingSeverityMax < trainingSeverityMin)
                trainingSeverityMax = trainingSeverityMin;
            maxSimultaneousFaults = Mathf.Clamp(maxSimultaneousFaults, 1, 3);
            evaluationTargetIndex = Mathf.Max(0, evaluationTargetIndex);
            evaluationOnsetSeconds = Mathf.Max(0f, evaluationOnsetSeconds);
            evaluationDurationSeconds = Mathf.Max(0f, evaluationDurationSeconds);
            evaluationSeverity = Mathf.Clamp01(evaluationSeverity);
        }
    }

    /// <summary>
    /// Shared environment/run state. The same instance is assigned to every
    /// FalconAgent in a training batch so UI changes are applied consistently.
    /// </summary>
    [Serializable]
    public partial class SimEnvironmentConfig
    {
        [Header("Wind")] public bool windEnabled = false;
        public float windSpeed = 0f;
        public float windGustAmplitude = 0f;
        public float windChangeRate = 0f;
        [Range(0f, 360f)] public float windDirectionDeg;
        public bool randomizeWindDirectionEachEpisode = true;
        [Range(0f, 2f)] public float gustFrequencyHz = 0.25f;
        [HideInInspector] public float savedWindSpeed = DefaultWindSpeed;
        [HideInInspector] public float savedWindGustAmplitude = DefaultWindGustAmplitude;
        [HideInInspector] public float savedWindChangeRate = DefaultWindChangeRate;

        public const float DefaultWindSpeed = 8f;
        public const float DefaultWindGustAmplitude = 5f;
        public const float DefaultWindChangeRate = 0.25f;

        [Header("Environment Domain Randomization")] public WeatherType weather = WeatherType.Clear;
        [Range(0.5f, 1.5f)] public float airDensityMultiplier = 1f;

        [Header("Scenario")] public ScenarioType scenario = ScenarioType.ChopstickLanding;
        [HideInInspector] public InferenceScenarioConfig inferenceScenarios = new();
        [HideInInspector] public InferencePurpose inferencePurpose = InferencePurpose.StandardEvaluation;
        [HideInInspector] public EvaluationConfig evaluation = new();
        [Header("Training Objective")] public TrainingObjectiveConfig trainingObjective = new();
        [Header("Faults")] public RocketFaultConfig faults = new();

        [Header("Run Config")] public string runId = "ReusableBoosterDefaultRun";
        public BehaviorType behaviorType = BehaviorType.Training;
        [Tooltip("Seeds episode spawn, target, wind, and evaluation randomization independently for each area.")]
        public int environmentSeed = 1;

        public float AirDensityMultiplier => Mathf.Clamp(airDensityMultiplier, 0.5f, 1.5f);
        public float EffectiveGustAmp => Mathf.Max(0f, windGustAmplitude);
        public bool IsStandardEvaluation =>
            behaviorType == BehaviorType.Inference &&
            inferencePurpose == InferencePurpose.StandardEvaluation;

        /// <summary>
        /// Applies a named environment preset by writing the same explicit wind
        /// and density values that remain editable below the preset buttons.
        /// </summary>
        public void ApplyWeatherPreset(WeatherType preset)
        {
            weather = preset;
            switch (preset)
            {
                case WeatherType.Clear:
                    windEnabled = false;
                    windSpeed = 0f;
                    windGustAmplitude = 0f;
                    windChangeRate = 0f;
                    gustFrequencyHz = 0f;
                    airDensityMultiplier = 1f;
                    break;
                case WeatherType.Cloudy:
                    windEnabled = true;
                    windSpeed = 5f;
                    windGustAmplitude = 2f;
                    windChangeRate = 0.20f;
                    gustFrequencyHz = 0.15f;
                    airDensityMultiplier = 1f;
                    break;
                case WeatherType.Rainy:
                    windEnabled = true;
                    windSpeed = 8f;
                    windGustAmplitude = 4f;
                    windChangeRate = 0.30f;
                    gustFrequencyHz = 0.25f;
                    airDensityMultiplier = 1.01f;
                    break;
                case WeatherType.Windy:
                    windEnabled = true;
                    windSpeed = 14f;
                    windGustAmplitude = 6f;
                    windChangeRate = 0.40f;
                    gustFrequencyHz = 0.35f;
                    airDensityMultiplier = 1f;
                    break;
                case WeatherType.Stormy:
                    windEnabled = true;
                    windSpeed = 22f;
                    windGustAmplitude = 12f;
                    windChangeRate = 0.65f;
                    gustFrequencyHz = 0.70f;
                    airDensityMultiplier = 1.02f;
                    break;
            }

            if (windSpeed > 0f) savedWindSpeed = windSpeed;
            if (windGustAmplitude > 0f) savedWindGustAmplitude = windGustAmplitude;
            if (windChangeRate > 0f) savedWindChangeRate = windChangeRate;
        }

        /// <summary>
        /// Toggles wind while preserving the last non-zero wind settings so the
        /// UI can disable wind without losing the user's configured values.
        /// </summary>
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

        /// <summary>
        /// Updates the wind speed and remembers it as the restore value while
        /// wind is enabled.
        /// </summary>
        public void SetWindSpeed(float value)
        {
            windSpeed = value;
            if (value > 0f) savedWindSpeed = value;
        }

        /// <summary>
        /// Updates gust amplitude and remembers it as the restore value while
        /// wind is enabled.
        /// </summary>
        public void SetWindGustAmplitude(float value)
        {
            windGustAmplitude = value;
            if (value > 0f) savedWindGustAmplitude = value;
        }

        /// <summary>
        /// Updates wind variation rate and remembers it as the restore value
        /// while wind is enabled.
        /// </summary>
        public void SetWindChangeRate(float value)
        {
            windChangeRate = value;
            if (value > 0f) savedWindChangeRate = value;
        }

        /// <summary>
        /// Returns the scenario-specific inference spawn profile and guarantees
        /// that the requested profile is ready for use.
        /// </summary>
        public InferenceSpawnProfile GetInferenceSpawnProfile(ScenarioType scenarioType)
        {
            inferenceScenarios ??= new InferenceScenarioConfig();
            return inferenceScenarios.ForScenario(scenarioType);
        }

        /// <summary>
        /// Returns validated evaluator settings, creating them when needed.
        /// </summary>
        public EvaluationConfig EnsureEvaluationConfig()
        {
            evaluation ??= new EvaluationConfig();
            evaluation.Clamp();
            return evaluation;
        }

        /// <summary>
        /// Applies common deterministic evaluator settings. Landing tasks are
        /// additionally forced to their independent full-difficulty profiles;
        /// hover retains its natural fuel/failure endpoint for trajectory analysis.
        /// </summary>
        public void PrepareStandardEvaluation()
        {
            if (!IsStandardEvaluation) return;

            EvaluationConfig cfg = EnsureEvaluationConfig();
            environmentSeed = cfg.seed;
            ApplyWeatherPreset(WeatherType.Clear);
            airDensityMultiplier = 1f;

            faults ??= new RocketFaultConfig();
            faults.evaluationFaultEnabled = false;
            faults.allowDuringTraining = false;

            if (!scenario.IsLanding())
            {
                // Hover evaluation intentionally keeps the natural fuel/failure
                // endpoint, but its initial-state distribution must still be
                // fixed rather than inherited from Manual Inference edits.
                if (scenario == ScenarioType.Hover || scenario == ScenarioType.HoverTracking)
                    GetInferenceSpawnProfile(scenario).ResetToDefaults(scenario);
                return;
            }

            if (scenario == ScenarioType.ChopstickLanding)
            {
                landingCatchAltitude = DefaultLandingCatchAltitude;
                landingTargetYawDeg = 0f;
                landingPlatformEnabled = true;
                landingPlatformHalfSizeFull = LandingDefaultPlatformHalfSizeFull;
                landingCurriculumMode = LandingCurriculumMode.FixedFullDifficulty;
                landingCurriculumLinearProgress = 1f;
                landingCurriculumPeakLinearProgress = 1f;
            }
            else
            {
                legLandingCurriculumMode = LandingCurriculumMode.FixedFullDifficulty;
                legLandingCurriculumLinearProgress = 1f;
                legLandingCurriculumPeakLinearProgress = 1f;
            }
            ApplyActiveLandingCurriculum();
        }

        /// <summary>
        /// Ensures the complete reward, shaping, and termination objective exists.
        /// Missing scenario objects are recreated without interpreting intentional
        /// zero values as absent configuration.
        /// </summary>
        public TrainingObjectiveConfig EnsureTrainingObjective()
        {
            trainingObjective ??= new TrainingObjectiveConfig();
            trainingObjective.EnsureDefaults();
            return trainingObjective;
        }

        /// <summary>
        /// Returns the selected scenario's complete objective.
        /// </summary>
        public ScenarioObjectiveConfig GetTrainingObjective(ScenarioType scenarioType)
        {
            return EnsureTrainingObjective().ForScenario(scenarioType);
        }

        /// <summary>
        /// Replaces one scenario's complete reward vector with a named preset.
        /// </summary>
        public void ApplyRewardPreset(ScenarioType scenarioType, string presetName)
        {
            EnsureTrainingObjective().ApplyPreset(scenarioType, presetName);
        }
    }


}
