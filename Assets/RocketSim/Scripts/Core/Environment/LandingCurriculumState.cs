// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Environment/LandingCurriculumState.cs
// Purpose: Tracks landing-training success and exposes the current interpolated
// spawn ranges, capture limits, and chopstick-platform difficulty.
// Beginner and full-difficulty constants live together here for easy comparison.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.Serialization;

namespace RocketSim
{
    public partial class SimEnvironmentConfig
    {
        [Header("Landing Curriculum")]
        [HideInInspector] public bool landingCurriculumEnabled = true;
        [HideInInspector] public int landingCurriculumSuccesses;
        [HideInInspector] public int landingCurriculumEpisodeCount;
        [HideInInspector] public int landingCurriculumSuccessfulEpisodes;
        [HideInInspector] public float landingCurriculumRecentSuccessRate;
        [HideInInspector] public float landingCurriculumLinearProgress;
        [HideInInspector] public float landingCurriculumProgress;
        [FormerlySerializedAs("landingCurriculumSuccessesToMostlyHard")]
        [HideInInspector] public float landingCurriculumSuccessesToFullDifficulty = LandingDefaultCurriculumSuccessesToFullDifficulty;
        [HideInInspector] public float landingTargetYawDeg;
        [HideInInspector] public float landingCatchAltitude = DefaultLandingCatchAltitude;
        [HideInInspector] public bool landingPlatformEnabled = true;
        [HideInInspector] public float landingPlatformTriggerProgressStart = LandingDefaultPlatformTriggerProgressStart;
        [HideInInspector] public float landingPlatformPhysicalProgressStart = LandingDefaultPlatformPhysicalProgressStart;
        [HideInInspector] public float landingPlatformHalfSizeInitial = LandingDefaultPlatformHalfSizeInitial;
        [HideInInspector] public float landingPlatformHalfSizeFull = LandingDefaultPlatformHalfSizeFull;
        [HideInInspector] public float landingPlatformStableHoldInitial = LandingDefaultPlatformStableHoldInitial;
        [HideInInspector] public float landingPlatformStableHoldFull = LandingDefaultPlatformStableHoldFull;

        public const float DefaultLandingCatchAltitude = 60f;
        public const float LandingDefaultCurriculumSuccessesToFullDifficulty = 360f;
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
        public const float LandingDefaultPlatformTriggerProgressStart = 0.25f;
        public const float LandingDefaultPlatformPhysicalProgressStart = 0.65f;
        public const float LandingDefaultPlatformHalfSizeInitial = 8f;
        public const float LandingDefaultPlatformHalfSizeFull = 3f;
        public const float LandingDefaultPlatformStableHoldInitial = 0.15f;
        public const float LandingDefaultPlatformStableHoldFull = 0.45f;

        public float LandingCurriculumSuccessRate =>
            landingCurriculumEpisodeCount > 0 ? Mathf.Clamp01(landingCurriculumRecentSuccessRate) : 0f;

        /// <summary>
        /// Resets the landing curriculum state back to its episode/default values.
        /// </summary>
        public void ResetLandingCurriculum()
        {
            landingCurriculumSuccesses = 0;
            landingCurriculumEpisodeCount = 0;
            landingCurriculumSuccessfulEpisodes = 0;
            landingCurriculumRecentSuccessRate = 0f;
            landingCurriculumLinearProgress = 0f;
            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Records one landing success for legacy callers. New curriculum
        /// progress uses completed episode success rate.
        /// </summary>
        public void AdvanceLandingCurriculum(int activeAreaCount)
        {
            landingCurriculumSuccesses++;
            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Records one completed landing episode and advances continuous
        /// difficulty from the recent average successful-episode rate.
        /// </summary>
        public void RecordLandingCurriculumEpisode(bool successfulEpisode, int activeAreaCount)
        {
            if (scenario != ScenarioType.Landing) return;

            EnsureLandingCurriculumDefaults();
            landingCurriculumEnabled = true;
            landingCurriculumEpisodeCount++;
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

            landingCurriculumLinearProgress = Mathf.Clamp01(
                landingCurriculumLinearProgress +
                CurriculumProgressDelta(
                    landingCurriculumRecentSuccessRate,
                    landingCurriculumSuccessesToFullDifficulty,
                    activeAreaCount));

            ApplyLandingCurriculum();
        }

        /// <summary>
        /// Converts accumulated landing successes into a normalized difficulty
        /// value used by spawn, success, and chopstick platform thresholds.
        /// </summary>
        public void ApplyLandingCurriculum()
        {
            if (scenario != ScenarioType.Landing) return;

            EnsureLandingCurriculumDefaults();
            landingCurriculumEnabled = true;

            landingCurriculumProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(landingCurriculumLinearProgress));
        }

        /// <summary>
        /// Interpolates a landing threshold between its beginner and full
        /// difficulty values using current curriculum progress.
        /// </summary>
        float LandingDifficultyValue(float initialValue, float fullDifficultyValue) =>
            Mathf.Lerp(initialValue, fullDifficultyValue, landingCurriculumProgress);

        /// <summary>
        /// Current minimum spawn altitude for landing episodes.
        /// </summary>
        public float CurrentLandingSpawnAltitudeMin => LandingDifficultyValue(LandingInitialSpawnAltitudeMin, LandingFullSpawnAltitudeMin);
        /// <summary>
        /// Current maximum spawn altitude for landing episodes.
        /// </summary>
        public float CurrentLandingSpawnAltitudeMax => LandingDifficultyValue(LandingInitialSpawnAltitudeMax, LandingFullSpawnAltitudeMax);
        /// <summary>
        /// Current maximum horizontal spawn offset from the landing target.
        /// </summary>
        public float CurrentLandingSpawnRadius => LandingDifficultyValue(LandingInitialSpawnRadius, LandingFullSpawnRadius);
        /// <summary>
        /// Current minimum downward spawn speed for landing episodes.
        /// </summary>
        public float CurrentLandingVerticalSpeedMin => LandingDifficultyValue(LandingInitialVerticalSpeedMin, LandingFullVerticalSpeedMin);
        /// <summary>
        /// Current maximum downward spawn speed for landing episodes.
        /// </summary>
        public float CurrentLandingVerticalSpeedMax => LandingDifficultyValue(LandingInitialVerticalSpeedMax, LandingFullVerticalSpeedMax);
        /// <summary>
        /// Current maximum horizontal spawn speed for landing episodes.
        /// </summary>
        public float CurrentLandingHorizontalSpeedMax => LandingDifficultyValue(LandingInitialHorizontalSpeedMax, LandingFullHorizontalSpeedMax);
        /// <summary>
        /// Current random pitch/roll range applied when spawning landing episodes.
        /// </summary>
        public float CurrentLandingSpawnTiltRangeDeg => LandingDifficultyValue(LandingInitialSpawnTiltRangeDeg, LandingFullSpawnTiltRangeDeg);
        /// <summary>
        /// Current maximum initial angular speed for landing episodes.
        /// </summary>
        public float CurrentLandingAngularSpeedMaxDegS => LandingDifficultyValue(LandingInitialAngularSpeedMaxDegS, LandingFullAngularSpeedMaxDegS);
        /// <summary>
        /// Current random yaw range applied when spawning landing episodes.
        /// </summary>
        public float CurrentLandingSpawnYawRangeDeg => LandingDifficultyValue(LandingInitialSpawnYawRangeDeg, LandingFullSpawnYawRangeDeg);
        /// <summary>
        /// Current failure ceiling, kept above the active spawn altitude range.
        /// </summary>
        public float CurrentLandingFailureAltitude => Mathf.Max(240f, CurrentLandingSpawnAltitudeMax + 100f);
        public float ActiveLandingFailureAltitude
        {
            get
            {
                if (behaviorType != BehaviorType.Inference)
                    return CurrentLandingFailureAltitude;

                InferenceSpawnProfile profile = GetInferenceSpawnProfile(ScenarioType.Landing);
                return Mathf.Max(240f, Mathf.Max(profile.altitudeMin, profile.altitudeMax) + 100f);
            }
        }
        /// <summary>
        /// Current horizontal radius allowed for a successful landing capture.
        /// </summary>
        public float CurrentLandingSuccessRadius => LandingDifficultyValue(LandingInitialSuccessRadius, LandingFullSuccessRadius);
        /// <summary>
        /// Current total-speed limit allowed for landing success.
        /// </summary>
        public float CurrentLandingSuccessMaxSpeed => LandingDifficultyValue(LandingInitialSuccessMaxSpeed, LandingFullSuccessMaxSpeed);
        /// <summary>
        /// Current vertical-speed limit allowed for landing success.
        /// </summary>
        public float CurrentLandingSuccessMaxVerticalSpeed => LandingDifficultyValue(LandingInitialSuccessMaxVerticalSpeed, LandingFullSuccessMaxVerticalSpeed);
        /// <summary>
        /// Current horizontal-speed limit allowed for landing success.
        /// </summary>
        public float CurrentLandingSuccessMaxHorizontalSpeed => LandingDifficultyValue(LandingInitialSuccessMaxHorizontalSpeed, LandingFullSuccessMaxHorizontalSpeed);
        /// <summary>
        /// Current tilt limit allowed for landing success.
        /// </summary>
        public float CurrentLandingSuccessMaxTiltDeg => LandingDifficultyValue(LandingInitialSuccessMaxTiltDeg, LandingFullSuccessMaxTiltDeg);
        /// <summary>
        /// Current angular-rate limit allowed for landing success.
        /// </summary>
        public float CurrentLandingSuccessMaxAngularRateDegS => LandingDifficultyValue(LandingInitialSuccessMaxAngularRateDegS, LandingFullSuccessMaxAngularRateDegS);
        /// <summary>
        /// Current yaw-alignment limit allowed for chopstick capture success.
        /// </summary>
        public float CurrentLandingSuccessMaxYawErrorDeg => LandingDifficultyValue(LandingInitialSuccessMaxYawErrorDeg, LandingFullSuccessMaxYawErrorDeg);
        public float CurrentLandingPlatformHalfSize => LandingDifficultyValue(
            Mathf.Max(0.5f, landingPlatformHalfSizeInitial),
            Mathf.Max(0.5f, landingPlatformHalfSizeFull));
        public float CurrentLandingPlatformStableHoldTime => LandingDifficultyValue(
            Mathf.Max(0f, landingPlatformStableHoldInitial),
            Mathf.Max(0f, landingPlatformStableHoldFull));
        public bool CurrentLandingPlatformTriggerActive =>
            landingPlatformEnabled &&
            landingCurriculumProgress >= Mathf.Clamp01(landingPlatformTriggerProgressStart);
        public bool CurrentLandingPlatformPhysicalActive =>
            landingPlatformEnabled &&
            landingCurriculumProgress >= Mathf.Clamp01(landingPlatformPhysicalProgressStart);
        public bool CurrentLandingPlatformRequired => CurrentLandingPlatformTriggerActive;


        /// <summary>
        /// Backfills and clamps landing curriculum platform settings for older
        /// serialized configs before derived thresholds are read.
        /// </summary>
        void EnsureLandingCurriculumDefaults()
        {
            if (landingCurriculumSuccessesToFullDifficulty <= 0f)
                landingCurriculumSuccessesToFullDifficulty = LandingDefaultCurriculumSuccessesToFullDifficulty;
            if (landingCatchAltitude <= 0f)
                landingCatchAltitude = DefaultLandingCatchAltitude;
            curriculumDifficultyIncreaseSpeed = Mathf.Clamp(
                curriculumDifficultyIncreaseSpeed <= 0f ? DefaultCurriculumDifficultyIncreaseSpeed : curriculumDifficultyIncreaseSpeed,
                MinCurriculumDifficultyIncreaseSpeed,
                MaxCurriculumDifficultyIncreaseSpeed);
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
                    landingCurriculumSuccesses / Mathf.Max(1f, landingCurriculumSuccessesToFullDifficulty));

            landingCurriculumLinearProgress = Mathf.Clamp01(landingCurriculumLinearProgress);

            landingPlatformTriggerProgressStart = Mathf.Clamp01(landingPlatformTriggerProgressStart);
            landingPlatformPhysicalProgressStart = Mathf.Clamp01(landingPlatformPhysicalProgressStart);
            if (landingPlatformHalfSizeInitial <= 0f)
                landingPlatformHalfSizeInitial = LandingDefaultPlatformHalfSizeInitial;
            if (landingPlatformHalfSizeFull <= 0f)
                landingPlatformHalfSizeFull = LandingDefaultPlatformHalfSizeFull;
            if (landingPlatformStableHoldInitial < 0f)
                landingPlatformStableHoldInitial = LandingDefaultPlatformStableHoldInitial;
            if (landingPlatformStableHoldFull < 0f)
                landingPlatformStableHoldFull = LandingDefaultPlatformStableHoldFull;
        }

    }
}
