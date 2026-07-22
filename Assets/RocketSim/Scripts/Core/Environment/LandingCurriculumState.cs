// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Environment/LandingCurriculumState.cs
// Purpose: Defines the shared landing-profile curve and tracks the legacy
// chopstick task's independent continuous curriculum state.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.Serialization;

namespace RocketSim
{
    /// <summary>
    /// Selects the curriculum policy used by landing training. Adaptive is the
    /// default experiment condition; the other modes provide controlled
    /// comparison groups without changing the task implementation.
    /// </summary>
    public enum LandingCurriculumMode
    {
        Adaptive,
        Monotonic,
        FixedFullDifficulty
    }

    /// <summary>
    /// Immutable per-episode landing thresholds. A snapshot prevents one
    /// parallel agent's episode from changing when another agent updates the
    /// shared curriculum state.
    /// </summary>
    public readonly struct LandingCurriculumProfile
    {
        public readonly float difficulty01;
        public readonly float spawnAltitudeMin;
        public readonly float spawnAltitudeMax;
        public readonly float spawnRadius;
        public readonly float verticalSpeedMin;
        public readonly float verticalSpeedMax;
        public readonly float horizontalSpeedMax;
        public readonly float spawnTiltRangeDeg;
        public readonly float angularSpeedMaxDegS;
        public readonly float spawnYawRangeDeg;
        public readonly float successRadius;
        public readonly float successMaxSpeed;
        public readonly float successMaxVerticalSpeed;
        public readonly float successMaxHorizontalSpeed;
        public readonly float successMaxTiltDeg;
        public readonly float successMaxAngularRateDegS;
        public readonly float successMaxYawErrorDeg;
        public readonly float platformHalfSize;
        public readonly float platformStableHoldTime;

        public LandingCurriculumProfile(
            float difficulty01,
            float spawnAltitudeMin,
            float spawnAltitudeMax,
            float spawnRadius,
            float verticalSpeedMin,
            float verticalSpeedMax,
            float horizontalSpeedMax,
            float spawnTiltRangeDeg,
            float angularSpeedMaxDegS,
            float spawnYawRangeDeg,
            float successRadius,
            float successMaxSpeed,
            float successMaxVerticalSpeed,
            float successMaxHorizontalSpeed,
            float successMaxTiltDeg,
            float successMaxAngularRateDegS,
            float successMaxYawErrorDeg,
            float platformHalfSize,
            float platformStableHoldTime)
        {
            this.difficulty01 = difficulty01;
            this.spawnAltitudeMin = spawnAltitudeMin;
            this.spawnAltitudeMax = spawnAltitudeMax;
            this.spawnRadius = spawnRadius;
            this.verticalSpeedMin = verticalSpeedMin;
            this.verticalSpeedMax = verticalSpeedMax;
            this.horizontalSpeedMax = horizontalSpeedMax;
            this.spawnTiltRangeDeg = spawnTiltRangeDeg;
            this.angularSpeedMaxDegS = angularSpeedMaxDegS;
            this.spawnYawRangeDeg = spawnYawRangeDeg;
            this.successRadius = successRadius;
            this.successMaxSpeed = successMaxSpeed;
            this.successMaxVerticalSpeed = successMaxVerticalSpeed;
            this.successMaxHorizontalSpeed = successMaxHorizontalSpeed;
            this.successMaxTiltDeg = successMaxTiltDeg;
            this.successMaxAngularRateDegS = successMaxAngularRateDegS;
            this.successMaxYawErrorDeg = successMaxYawErrorDeg;
            this.platformHalfSize = platformHalfSize;
            this.platformStableHoldTime = platformStableHoldTime;
        }
    }

    public partial class SimEnvironmentConfig
    {
        [Header("Landing Curriculum")]
        [HideInInspector] public LandingCurriculumMode landingCurriculumMode = LandingCurriculumMode.Adaptive;
        [HideInInspector] public bool landingCurriculumEnabled = true;
        [HideInInspector] public int landingCurriculumSuccesses;
        [HideInInspector] public int landingCurriculumEpisodeCount;
        [HideInInspector] public int landingCurriculumSuccessfulEpisodes;
        [HideInInspector] public int landingCurriculumBatchEpisodeCount;
        [HideInInspector] public float landingCurriculumRecentSuccessRate;
        [HideInInspector] public float landingCurriculumLinearProgress;
        [HideInInspector] public float landingCurriculumProgress;
        [HideInInspector] public float landingCurriculumPeakLinearProgress;
        [FormerlySerializedAs("landingCurriculumSuccessesToMostlyHard")]
        [FormerlySerializedAs("landingCurriculumSuccessesToFullDifficulty")]
        [HideInInspector] public float landingCurriculumBatchesToFullDifficulty = LandingDefaultCurriculumBatchesToFullDifficulty;
        [HideInInspector] public float landingCurriculumPromotionSuccessRate = LandingDefaultPromotionSuccessRate;
        [HideInInspector] public float landingCurriculumRetreatSuccessRate = LandingDefaultRetreatSuccessRate;
        [HideInInspector] public float landingCurriculumRetreatSpeedMultiplier = LandingDefaultRetreatSpeedMultiplier;
        [HideInInspector] public float landingCurriculumMaximumRetreat = LandingDefaultMaximumRetreat;
        [HideInInspector] public float landingCurriculumMaximumStepPerBatch = LandingDefaultMaximumStepPerBatch;
        [HideInInspector] public float landingCurriculumEasierReplayProbability = LandingDefaultEasierReplayProbability;
        [HideInInspector] public float landingCurriculumEasierReplayOffset = LandingDefaultEasierReplayOffset;
        [HideInInspector] public float landingTargetYawDeg;
        [HideInInspector] public float landingCatchAltitude = DefaultLandingCatchAltitude;
        [HideInInspector] public bool landingPlatformEnabled = true;
        [HideInInspector] public float landingPlatformHalfSizeInitial = LandingDefaultPlatformHalfSizeInitial;
        [HideInInspector] public float landingPlatformHalfSizeFull = LandingDefaultPlatformHalfSizeFull;
        [HideInInspector] public float landingPlatformStableHoldInitial = LandingDefaultPlatformStableHoldInitial;
        [HideInInspector] public float landingPlatformStableHoldFull = LandingDefaultPlatformStableHoldFull;
        [HideInInspector] public float landingMaxEpisodeSeconds = LandingDefaultMaxEpisodeSeconds;

        public const float DefaultLandingCatchAltitude = 60f;
        public const float LandingDefaultCurriculumBatchesToFullDifficulty = 360f;
        public const float LandingDefaultPromotionSuccessRate = 0.80f;
        public const float LandingDefaultRetreatSuccessRate = 0.50f;
        public const float LandingDefaultRetreatSpeedMultiplier = 0.50f;
        public const float LandingDefaultMaximumRetreat = 0.20f;
        public const float LandingDefaultMaximumStepPerBatch = 0.02f;
        public const float LandingDefaultEasierReplayProbability = 0.15f;
        public const float LandingDefaultEasierReplayOffset = 0.20f;
        // Landing altitudes describe the catch-frame height, not the engine plane.
        public const float LandingInitialSpawnAltitudeMin = 120f;
        public const float LandingInitialSpawnAltitudeMax = 220f;
        public const float LandingFullSpawnAltitudeMin = 300f;
        public const float LandingFullSpawnAltitudeMax = 1000f;
        public const float LandingInitialSpawnRadius = 8f;
        public const float LandingFullSpawnRadius = 100f;
        public const float LandingInitialVerticalSpeedMin = 5f;
        public const float LandingInitialVerticalSpeedMax = 20f;
        public const float LandingFullVerticalSpeedMin = 20f;
        public const float LandingFullVerticalSpeedMax = 120f;
        public const float LandingInitialHorizontalSpeedMax = 1f;
        public const float LandingFullHorizontalSpeedMax = 25f;
        public const float LandingInitialSpawnTiltRangeDeg = 3f;
        public const float LandingFullSpawnTiltRangeDeg = 18f;
        public const float LandingInitialAngularSpeedMaxDegS = 0f;
        public const float LandingFullAngularSpeedMaxDegS = 55f;
        public const float LandingInitialSpawnYawRangeDeg = 30f;
        public const float LandingFullSpawnYawRangeDeg = 180f;
        public const float LandingInitialSuccessRadius = 8f;
        public const float LandingFullSuccessRadius = 2f;
        public const float LandingInitialSuccessMaxSpeed = 7f;
        public const float LandingFullSuccessMaxSpeed = 2.5f;
        public const float LandingInitialSuccessMaxVerticalSpeed = 5f;
        public const float LandingFullSuccessMaxVerticalSpeed = 2f;
        public const float LandingInitialSuccessMaxHorizontalSpeed = 5f;
        public const float LandingFullSuccessMaxHorizontalSpeed = 1f;
        public const float LandingInitialSuccessMaxTiltDeg = 20f;
        public const float LandingFullSuccessMaxTiltDeg = 5f;
        public const float LandingInitialSuccessMaxAngularRateDegS = 50f;
        public const float LandingFullSuccessMaxAngularRateDegS = 25f;
        public const float LandingInitialSuccessMaxYawErrorDeg = 30f;
        public const float LandingFullSuccessMaxYawErrorDeg = 10f;
        public const float LandingDefaultPlatformHalfSizeInitial = 8f;
        public const float LandingDefaultPlatformHalfSizeFull = 3f;
        public const float LandingDefaultPlatformStableHoldInitial = 0.15f;
        public const float LandingDefaultPlatformStableHoldFull = 0.45f;
        public const float LandingDefaultMaxEpisodeSeconds = 120f;

        public float LandingCurriculumSuccessRate =>
            landingCurriculumEpisodeCount > 0 ? Mathf.Clamp01(landingCurriculumRecentSuccessRate) : 0f;

        /// <summary>Resets all landing curriculum progress and batch state.</summary>
        public void ResetLandingCurriculum()
        {
            landingCurriculumSuccesses = 0;
            landingCurriculumEpisodeCount = 0;
            landingCurriculumSuccessfulEpisodes = 0;
            landingCurriculumBatchEpisodeCount = 0;
            landingCurriculumRecentSuccessRate = 0f;
            landingCurriculumLinearProgress = 0f;
            landingCurriculumPeakLinearProgress = 0f;
            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Records one landing success for legacy callers. Completed episodes,
        /// not this diagnostic counter, control curriculum difficulty.
        /// </summary>
        public void AdvanceLandingCurriculum(int activeAreaCount)
        {
            landingCurriculumSuccesses++;
            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Records one completed episode. Difficulty changes only after one
        /// parallel-area-sized batch, so area count cannot change update speed.
        /// </summary>
        public void RecordLandingCurriculumEpisode(bool successfulEpisode, int activeAreaCount)
        {
            if (scenario == ScenarioType.LegLanding)
            {
                RecordLegLandingCurriculumEpisode(successfulEpisode, activeAreaCount);
                return;
            }
            if (scenario != ScenarioType.ChopstickLanding) return;

            EnsureLandingCurriculumDefaults();
            landingCurriculumEpisodeCount++;
            landingCurriculumBatchEpisodeCount++;
            if (successfulEpisode)
            {
                landingCurriculumSuccesses++;
                landingCurriculumSuccessfulEpisodes++;
            }

            landingCurriculumRecentSuccessRate = CurriculumSuccessRateAfterEpisode(
                landingCurriculumRecentSuccessRate,
                landingCurriculumSuccessfulEpisodes,
                landingCurriculumEpisodeCount,
                successfulEpisode);

            int batchSize = Mathf.Max(1, activeAreaCount);
            if (landingCurriculumMode != LandingCurriculumMode.FixedFullDifficulty &&
                landingCurriculumBatchEpisodeCount >= batchSize)
            {
                float delta = LandingCurriculumProgressDelta(landingCurriculumRecentSuccessRate);
                if (landingCurriculumMode == LandingCurriculumMode.Monotonic)
                    delta = Mathf.Max(0f, delta);

                float retreatFloor = landingCurriculumMode == LandingCurriculumMode.Adaptive
                    ? Mathf.Max(0f, landingCurriculumPeakLinearProgress - landingCurriculumMaximumRetreat)
                    : landingCurriculumLinearProgress;
                landingCurriculumLinearProgress = Mathf.Clamp(
                    landingCurriculumLinearProgress + delta,
                    retreatFloor,
                    1f);
                landingCurriculumPeakLinearProgress = Mathf.Max(
                    landingCurriculumPeakLinearProgress,
                    landingCurriculumLinearProgress);
                landingCurriculumBatchEpisodeCount = 0;
            }

            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Applies the selected comparison condition. Curriculum modes use a
        /// smooth continuous curve; the fixed baseline always uses d=1.
        /// </summary>
        public void ApplyLandingCurriculum()
        {
            if (scenario != ScenarioType.ChopstickLanding) return;

            EnsureLandingCurriculumDefaults();
            landingCurriculumEnabled = landingCurriculumMode != LandingCurriculumMode.FixedFullDifficulty;
            landingCurriculumProgress = landingCurriculumEnabled
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(landingCurriculumLinearProgress))
                : 1f;
        }

        /// <summary>
        /// Produces the complete landing task at one normalized difficulty.
        /// Agents keep this snapshot for their whole episode.
        /// </summary>
        public LandingCurriculumProfile GetLandingCurriculumProfile(float difficulty01)
        {
            float d = Mathf.Clamp01(difficulty01);
            float Value(float initialValue, float fullValue) => Mathf.Lerp(initialValue, fullValue, d);

            return new LandingCurriculumProfile(
                d,
                Value(LandingInitialSpawnAltitudeMin, LandingFullSpawnAltitudeMin),
                Value(LandingInitialSpawnAltitudeMax, LandingFullSpawnAltitudeMax),
                Value(LandingInitialSpawnRadius, LandingFullSpawnRadius),
                Value(LandingInitialVerticalSpeedMin, LandingFullVerticalSpeedMin),
                Value(LandingInitialVerticalSpeedMax, LandingFullVerticalSpeedMax),
                Value(LandingInitialHorizontalSpeedMax, LandingFullHorizontalSpeedMax),
                Value(LandingInitialSpawnTiltRangeDeg, LandingFullSpawnTiltRangeDeg),
                Value(LandingInitialAngularSpeedMaxDegS, LandingFullAngularSpeedMaxDegS),
                Value(LandingInitialSpawnYawRangeDeg, LandingFullSpawnYawRangeDeg),
                Value(LandingInitialSuccessRadius, LandingFullSuccessRadius),
                Value(LandingInitialSuccessMaxSpeed, LandingFullSuccessMaxSpeed),
                Value(LandingInitialSuccessMaxVerticalSpeed, LandingFullSuccessMaxVerticalSpeed),
                Value(LandingInitialSuccessMaxHorizontalSpeed, LandingFullSuccessMaxHorizontalSpeed),
                Value(LandingInitialSuccessMaxTiltDeg, LandingFullSuccessMaxTiltDeg),
                Value(LandingInitialSuccessMaxAngularRateDegS, LandingFullSuccessMaxAngularRateDegS),
                Value(LandingInitialSuccessMaxYawErrorDeg, LandingFullSuccessMaxYawErrorDeg),
                Value(Mathf.Max(0.5f, landingPlatformHalfSizeInitial), Mathf.Max(0.5f, landingPlatformHalfSizeFull)),
                Value(Mathf.Max(0f, landingPlatformStableHoldInitial), Mathf.Max(0f, landingPlatformStableHoldFull)));
        }

        /// <summary>
        /// Chooses the episode's fixed task profile. A small fraction replays a
        /// nearby easier profile to reduce forgetting without changing global d.
        /// </summary>
        public float LandingEpisodeDifficulty(bool useEasierReplay)
        {
            float progress = ActiveLandingCurriculumProgress;
            if (!ActiveLandingCurriculumEnabled || !useEasierReplay)
                return progress;
            return Mathf.Max(0f, progress - ActiveLandingReplayOffset);
        }

        public float CurrentLandingSpawnAltitudeMin => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).spawnAltitudeMin;
        public float CurrentLandingSpawnAltitudeMax => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).spawnAltitudeMax;
        public float CurrentLandingSpawnRadius => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).spawnRadius;
        public float CurrentLandingVerticalSpeedMin => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).verticalSpeedMin;
        public float CurrentLandingVerticalSpeedMax => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).verticalSpeedMax;
        public float CurrentLandingHorizontalSpeedMax => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).horizontalSpeedMax;
        public float CurrentLandingSpawnTiltRangeDeg => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).spawnTiltRangeDeg;
        public float CurrentLandingAngularSpeedMaxDegS => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).angularSpeedMaxDegS;
        public float CurrentLandingSpawnYawRangeDeg => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).spawnYawRangeDeg;
        public float CurrentLandingSuccessRadius => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successRadius;
        public float CurrentLandingSuccessMaxSpeed => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxSpeed;
        public float CurrentLandingSuccessMaxVerticalSpeed => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxVerticalSpeed;
        public float CurrentLandingSuccessMaxHorizontalSpeed => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxHorizontalSpeed;
        public float CurrentLandingSuccessMaxTiltDeg => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxTiltDeg;
        public float CurrentLandingSuccessMaxAngularRateDegS => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxAngularRateDegS;
        public float CurrentLandingSuccessMaxYawErrorDeg => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).successMaxYawErrorDeg;
        public float CurrentLandingPlatformHalfSize => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).platformHalfSize;
        public float CurrentLandingPlatformStableHoldTime => GetActiveLandingCurriculumProfile(ActiveLandingCurriculumProgress).platformStableHoldTime;
        // The capture envelope exists from d=0 onward and never becomes a
        // collision surface. Only its size and required stable time change.
        public bool CurrentLandingPlatformRequired => scenario == ScenarioType.ChopstickLanding && landingPlatformEnabled;

        /// <summary>Returns one bounded hysteretic difficulty change per batch.</summary>
        float LandingCurriculumProgressDelta(float successRate)
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
            {
                float pressure = Mathf.InverseLerp(landingCurriculumPromotionSuccessRate, 1f, rate);
                return baseStep * pressure;
            }

            if (rate < landingCurriculumRetreatSuccessRate)
            {
                float pressure = (landingCurriculumRetreatSuccessRate - rate) /
                                 Mathf.Max(0.01f, landingCurriculumRetreatSuccessRate);
                return -baseStep * landingCurriculumRetreatSpeedMultiplier * Mathf.Clamp01(pressure);
            }

            return 0f;
        }

        /// <summary>Backfills and clamps settings loaded from older configs.</summary>
        void EnsureLandingCurriculumDefaults()
        {
            if (landingCurriculumBatchesToFullDifficulty <= 0f)
                landingCurriculumBatchesToFullDifficulty = LandingDefaultCurriculumBatchesToFullDifficulty;
            if (landingCatchAltitude <= 0f)
                landingCatchAltitude = DefaultLandingCatchAltitude;
            curriculumDifficultyIncreaseSpeed = Mathf.Clamp(
                curriculumDifficultyIncreaseSpeed <= 0f ? DefaultCurriculumDifficultyIncreaseSpeed : curriculumDifficultyIncreaseSpeed,
                MinCurriculumDifficultyIncreaseSpeed,
                MaxCurriculumDifficultyIncreaseSpeed);

            landingCurriculumPromotionSuccessRate = landingCurriculumPromotionSuccessRate <= 0f
                ? LandingDefaultPromotionSuccessRate
                : Mathf.Clamp(landingCurriculumPromotionSuccessRate, 0.51f, 0.99f);
            landingCurriculumRetreatSuccessRate = landingCurriculumRetreatSuccessRate <= 0f
                ? LandingDefaultRetreatSuccessRate
                : Mathf.Clamp(landingCurriculumRetreatSuccessRate, 0.01f, landingCurriculumPromotionSuccessRate - 0.01f);
            landingCurriculumRetreatSpeedMultiplier = landingCurriculumRetreatSpeedMultiplier <= 0f
                ? LandingDefaultRetreatSpeedMultiplier
                : Mathf.Clamp(landingCurriculumRetreatSpeedMultiplier, 0.05f, 1f);
            landingCurriculumMaximumRetreat = landingCurriculumMaximumRetreat <= 0f
                ? LandingDefaultMaximumRetreat
                : Mathf.Clamp01(landingCurriculumMaximumRetreat);
            landingCurriculumMaximumStepPerBatch = landingCurriculumMaximumStepPerBatch <= 0f
                ? LandingDefaultMaximumStepPerBatch
                : Mathf.Clamp(landingCurriculumMaximumStepPerBatch, 0.001f, 0.10f);
            landingCurriculumEasierReplayProbability = landingCurriculumEasierReplayProbability <= 0f
                ? LandingDefaultEasierReplayProbability
                : Mathf.Clamp01(landingCurriculumEasierReplayProbability);
            landingCurriculumEasierReplayOffset = landingCurriculumEasierReplayOffset <= 0f
                ? LandingDefaultEasierReplayOffset
                : Mathf.Clamp01(landingCurriculumEasierReplayOffset);

            if (landingCurriculumEpisodeCount <= 0 && landingCurriculumSuccesses > 0)
            {
                landingCurriculumEpisodeCount = landingCurriculumSuccesses;
                landingCurriculumSuccessfulEpisodes = landingCurriculumSuccesses;
                landingCurriculumRecentSuccessRate = 1f;
            }

            if (landingCurriculumLinearProgress <= 0f && landingCurriculumProgress > 0f)
                landingCurriculumLinearProgress = landingCurriculumProgress;
            if (landingCurriculumLinearProgress <= 0f && landingCurriculumSuccesses > 0)
                landingCurriculumLinearProgress = Mathf.Clamp01(
                    landingCurriculumSuccesses / Mathf.Max(1f, landingCurriculumBatchesToFullDifficulty));

            landingCurriculumLinearProgress = Mathf.Clamp01(landingCurriculumLinearProgress);
            landingCurriculumPeakLinearProgress = Mathf.Max(
                Mathf.Clamp01(landingCurriculumPeakLinearProgress),
                landingCurriculumLinearProgress);
            landingCurriculumBatchEpisodeCount = Mathf.Max(0, landingCurriculumBatchEpisodeCount);

            if (landingPlatformHalfSizeInitial <= 0f)
                landingPlatformHalfSizeInitial = LandingDefaultPlatformHalfSizeInitial;
            if (landingPlatformHalfSizeFull <= 0f)
                landingPlatformHalfSizeFull = LandingDefaultPlatformHalfSizeFull;
            if (landingPlatformStableHoldInitial < 0f)
                landingPlatformStableHoldInitial = LandingDefaultPlatformStableHoldInitial;
            if (landingPlatformStableHoldFull < 0f)
                landingPlatformStableHoldFull = LandingDefaultPlatformStableHoldFull;
            if (landingMaxEpisodeSeconds <= 0f)
                landingMaxEpisodeSeconds = LandingDefaultMaxEpisodeSeconds;
            landingMaxEpisodeSeconds = Mathf.Clamp(landingMaxEpisodeSeconds, 30f, 600f);
        }
    }
}
