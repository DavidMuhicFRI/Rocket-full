// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Curriculum/Tasks/LegLandingCurriculum.cs
// Purpose: Gives physical leg landing independent curriculum progress while
// reusing the landing profile shape and adaptive/monotonic comparison modes.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class SimEnvironmentConfig
    {
        [Header("Leg Landing Curriculum")]
        [HideInInspector] public LandingCurriculumMode legLandingCurriculumMode = LandingCurriculumMode.Adaptive;
        [HideInInspector] public bool legLandingCurriculumEnabled = true;
        [HideInInspector] public int legLandingCurriculumSuccesses;
        [HideInInspector] public int legLandingCurriculumEpisodeCount;
        [HideInInspector] public int legLandingCurriculumSuccessfulEpisodes;
        [HideInInspector] public int legLandingCurriculumBatchEpisodeCount;
        [HideInInspector] public float legLandingCurriculumRecentSuccessRate;
        [HideInInspector] public float legLandingCurriculumLinearProgress;
        [HideInInspector] public float legLandingCurriculumProgress;
        [HideInInspector] public float legLandingCurriculumPeakLinearProgress;
        // Pad geometry is deliberately not a success criterion. The large,
        // fixed deck supplies ground contact while horizontal centre error
        // decides whether the landing counts.
        public const float LegLandingPadHalfSizeM = 25f;
        // The first stage teaches a short, already-upright approach while
        // retaining enough offset to require lateral navigation from the
        // beginning. Difficulty then expands altitude,
        // offset, velocity, tilt, and rotation together. The feasibility
        // sampler continues clipping starts to the selected vehicle.
        public const float LegLandingInitialSpawnAltitudeMin = 120f;
        public const float LegLandingInitialSpawnAltitudeMax = 180f;
        public const float LegLandingFullSpawnAltitudeMin = 400f;
        public const float LegLandingFullSpawnAltitudeMax = 1000f;
        public const float LegLandingInitialSpawnRadius = 8f;
        public const float LegLandingFullSpawnRadius = 60f;
        public const float LegLandingInitialVerticalSpeedMin = 10f;
        public const float LegLandingInitialVerticalSpeedMax = 18f;
        public const float LegLandingFullVerticalSpeedMin = 30f;
        public const float LegLandingFullVerticalSpeedMax = 70f;
        public const float LegLandingInitialHorizontalSpeedMax = 1f;
        public const float LegLandingFullHorizontalSpeedMax = 12f;
        public const float LegLandingInitialSpawnTiltRangeDeg = 1f;
        public const float LegLandingFullSpawnTiltRangeDeg = 8f;
        public const float LegLandingInitialAngularSpeedMaxDegS = 2f;
        public const float LegLandingFullAngularSpeedMaxDegS = 12f;

        public bool IsLandingScenario => scenario.IsLanding();
        public float ActiveLandingCurriculumProgress => scenario == ScenarioType.LegLanding
            ? legLandingCurriculumProgress
            : landingCurriculumProgress;
        public bool ActiveLandingCurriculumEnabled => scenario == ScenarioType.LegLanding
            ? legLandingCurriculumEnabled
            : landingCurriculumEnabled;
        public LandingCurriculumMode ActiveLandingCurriculumMode
        {
            get => scenario == ScenarioType.LegLanding ? legLandingCurriculumMode : landingCurriculumMode;
            set
            {
                if (scenario == ScenarioType.LegLanding) legLandingCurriculumMode = value;
                else landingCurriculumMode = value;
            }
        }
        public float ActiveLandingPromotionSuccessRate => landingCurriculumPromotionSuccessRate;
        public float ActiveLandingRetreatSuccessRate => landingCurriculumRetreatSuccessRate;
        public float ActiveLandingReplayProbability => landingCurriculumEasierReplayProbability;
        public float ActiveLandingReplayOffset => landingCurriculumEasierReplayOffset;
        public int ActiveLandingCurriculumEpisodeCount => scenario == ScenarioType.LegLanding
            ? legLandingCurriculumEpisodeCount
            : landingCurriculumEpisodeCount;
        public int ActiveLandingCurriculumSuccessfulEpisodes => scenario == ScenarioType.LegLanding
            ? legLandingCurriculumSuccessfulEpisodes
            : landingCurriculumSuccessfulEpisodes;
        public float ActiveLandingCurriculumSuccessRate => scenario == ScenarioType.LegLanding
            ? (legLandingCurriculumEpisodeCount > 0 ? Mathf.Clamp01(legLandingCurriculumRecentSuccessRate) : 0f)
            : LandingCurriculumSuccessRate;

        /// <summary>Returns the active landing task's immutable episode profile.</summary>
        public LandingCurriculumProfile GetActiveLandingCurriculumProfile(float difficulty01) =>
            scenario == ScenarioType.LegLanding
                ? GetLegLandingCurriculumProfile(difficulty01)
                : GetLandingCurriculumProfile(difficulty01);

        /// <summary>
        /// Teaches a centered short approach before expanding to the complete
        /// descent envelope. Heading remains irrelevant; success tightens to a
        /// one-second, four-foot, propulsion-off stable hold.
        /// </summary>
        public LandingCurriculumProfile GetLegLandingCurriculumProfile(float difficulty01)
        {
            float d = Mathf.Clamp01(difficulty01);
            float Value(float initialValue, float fullValue) =>
                Mathf.Lerp(initialValue, fullValue, d);
            TerminationParameters termination =
                GetTrainingObjective(ScenarioType.LegLanding).terminations;

            return new LandingCurriculumProfile(
                d,
                Value(LegLandingInitialSpawnAltitudeMin, LegLandingFullSpawnAltitudeMin),
                Value(LegLandingInitialSpawnAltitudeMax, LegLandingFullSpawnAltitudeMax),
                Value(LegLandingInitialSpawnRadius, LegLandingFullSpawnRadius),
                Value(LegLandingInitialVerticalSpeedMin, LegLandingFullVerticalSpeedMin),
                Value(LegLandingInitialVerticalSpeedMax, LegLandingFullVerticalSpeedMax),
                Value(LegLandingInitialHorizontalSpeedMax, LegLandingFullHorizontalSpeedMax),
                Value(LegLandingInitialSpawnTiltRangeDeg, LegLandingFullSpawnTiltRangeDeg),
                Value(LegLandingInitialAngularSpeedMaxDegS, LegLandingFullAngularSpeedMaxDegS),
                180f,
                termination.landingSuccessRadiusM.At(d),
                termination.landingSuccessMaxTotalSpeedMps.At(d),
                termination.landingSuccessMaxVerticalSpeedMps.At(d),
                termination.landingSuccessMaxHorizontalSpeedMps.At(d),
                termination.landingSuccessMaxTiltDeg.At(d),
                termination.landingSuccessMaxAngularRateDegS.At(d),
                180f,
                LegLandingPadHalfSizeM,
                termination.landingStableHoldSeconds.At(d),
                d,
                Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(
                        termination.legInitialMinimumStableFeet,
                        termination.legMinimumStableFeet,
                        d)),
                    1,
                    LandingLegComponent.LegCount));
        }

        public void ResetActiveLandingCurriculum()
        {
            if (scenario == ScenarioType.LegLanding)
            {
                legLandingCurriculumSuccesses = 0;
                legLandingCurriculumEpisodeCount = 0;
                legLandingCurriculumSuccessfulEpisodes = 0;
                legLandingCurriculumBatchEpisodeCount = 0;
                legLandingCurriculumRecentSuccessRate = 0f;
                legLandingCurriculumLinearProgress = 0f;
                legLandingCurriculumPeakLinearProgress = 0f;
                ApplyLegLandingCurriculum();
                return;
            }

            ResetLandingCurriculum();
        }

        public void ApplyActiveLandingCurriculum()
        {
            if (scenario == ScenarioType.LegLanding) ApplyLegLandingCurriculum();
            else if (scenario == ScenarioType.ChopstickLanding) ApplyLandingCurriculum();
        }

        public void RecordLegLandingCurriculumEpisode(bool successfulEpisode, int activeAreaCount)
        {
            if (scenario != ScenarioType.LegLanding) return;

            EnsureLegLandingCurriculumDefaults();
            legLandingCurriculumEpisodeCount++;
            legLandingCurriculumBatchEpisodeCount++;
            if (successfulEpisode)
            {
                legLandingCurriculumSuccesses++;
                legLandingCurriculumSuccessfulEpisodes++;
            }

            legLandingCurriculumRecentSuccessRate = CurriculumSuccessRateAfterEpisode(
                legLandingCurriculumRecentSuccessRate,
                legLandingCurriculumSuccessfulEpisodes,
                legLandingCurriculumEpisodeCount,
                successfulEpisode);

            int batchSize = Mathf.Max(1, activeAreaCount);
            if (legLandingCurriculumMode != LandingCurriculumMode.FixedFullDifficulty &&
                legLandingCurriculumBatchEpisodeCount >= batchSize)
            {
                float delta = LegLandingCurriculumProgressDelta(legLandingCurriculumRecentSuccessRate);
                if (legLandingCurriculumMode == LandingCurriculumMode.Monotonic)
                    delta = Mathf.Max(0f, delta);

                float retreatFloor = legLandingCurriculumMode == LandingCurriculumMode.Adaptive
                    ? Mathf.Max(0f, legLandingCurriculumPeakLinearProgress - landingCurriculumMaximumRetreat)
                    : legLandingCurriculumLinearProgress;
                legLandingCurriculumLinearProgress = Mathf.Clamp(
                    legLandingCurriculumLinearProgress + delta,
                    retreatFloor,
                    1f);
                legLandingCurriculumPeakLinearProgress = Mathf.Max(
                    legLandingCurriculumPeakLinearProgress,
                    legLandingCurriculumLinearProgress);
                legLandingCurriculumBatchEpisodeCount = 0;
            }

            ApplyLegLandingCurriculum();
        }

        void ApplyLegLandingCurriculum()
        {
            if (scenario != ScenarioType.LegLanding) return;
            EnsureLegLandingCurriculumDefaults();
            legLandingCurriculumEnabled = legLandingCurriculumMode != LandingCurriculumMode.FixedFullDifficulty;
            legLandingCurriculumProgress = legLandingCurriculumEnabled
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(legLandingCurriculumLinearProgress))
                : 1f;
        }

        float LegLandingCurriculumProgressDelta(float successRate)
        {
            float baseStep = Mathf.Min(
                landingCurriculumMaximumStepPerBatch,
                Mathf.Clamp(
                    curriculumDifficultyIncreaseSpeed,
                    MinCurriculumDifficultyIncreaseSpeed,
                    MaxCurriculumDifficultyIncreaseSpeed) /
                Mathf.Max(1f, landingCurriculumBatchesToFullDifficulty));

            float rate = Mathf.Clamp01(successRate);
            if (rate > landingCurriculumPromotionSuccessRate)
                return baseStep * Mathf.InverseLerp(landingCurriculumPromotionSuccessRate, 1f, rate);
            if (rate < landingCurriculumRetreatSuccessRate)
            {
                float pressure = (landingCurriculumRetreatSuccessRate - rate) /
                                 Mathf.Max(0.01f, landingCurriculumRetreatSuccessRate);
                return -baseStep * landingCurriculumRetreatSpeedMultiplier * Mathf.Clamp01(pressure);
            }
            return 0f;
        }

        void EnsureLegLandingCurriculumDefaults()
        {
            // Shared hysteresis/replay controls use the same validated values as
            // chopstick landing, while progress and stable-hold state are separate.
            if (landingCurriculumBatchesToFullDifficulty <= 0f)
                landingCurriculumBatchesToFullDifficulty = LandingDefaultCurriculumBatchesToFullDifficulty;
            if (landingCurriculumMaximumStepPerBatch <= 0f)
                landingCurriculumMaximumStepPerBatch = LandingDefaultMaximumStepPerBatch;
            if (landingCurriculumPromotionSuccessRate <= 0f)
                landingCurriculumPromotionSuccessRate = LandingDefaultPromotionSuccessRate;
            if (landingCurriculumRetreatSuccessRate <= 0f)
                landingCurriculumRetreatSuccessRate = LandingDefaultRetreatSuccessRate;
            if (landingCurriculumRetreatSpeedMultiplier <= 0f)
                landingCurriculumRetreatSpeedMultiplier = LandingDefaultRetreatSpeedMultiplier;
            if (landingCurriculumMaximumRetreat <= 0f)
                landingCurriculumMaximumRetreat = LandingDefaultMaximumRetreat;
            if (landingCurriculumEasierReplayProbability <= 0f)
                landingCurriculumEasierReplayProbability = LandingDefaultEasierReplayProbability;
            if (landingCurriculumEasierReplayOffset <= 0f)
                landingCurriculumEasierReplayOffset = LandingDefaultEasierReplayOffset;
            legLandingCurriculumLinearProgress = Mathf.Clamp01(legLandingCurriculumLinearProgress);
            legLandingCurriculumPeakLinearProgress = Mathf.Max(
                Mathf.Clamp01(legLandingCurriculumPeakLinearProgress),
                legLandingCurriculumLinearProgress);
            legLandingCurriculumBatchEpisodeCount = Mathf.Max(0, legLandingCurriculumBatchEpisodeCount);
        }
    }
}
