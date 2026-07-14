// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Environment/HoverTrackCurriculumConfig.cs
// Purpose: Tracks hover-target training success and continuously turns that
// success into harder movement distance, settling, speed, tilt, and hold limits.
// Difficulty is normalized by parallel area count so hardware does not change the experiment.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class SimEnvironmentConfig
    {
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
        [HideInInspector] public bool hoverTrackCurriculumEnabled = true;
        [HideInInspector] public int hoverTrackCurriculumSuccesses;
        [HideInInspector] public int hoverTrackCurriculumEpisodeCount;
        [HideInInspector] public int hoverTrackCurriculumSuccessfulEpisodes;
        [HideInInspector] public float hoverTrackCurriculumRecentSuccessRate;
        [HideInInspector] public float hoverTrackCurriculumLinearProgress;
        [HideInInspector] public float hoverTrackCurriculumProgress;
        [HideInInspector] public float hoverTrackStartMoveRadius = HoverTrackStartMoveRadius;
        [HideInInspector] public float hoverTrackEndMoveRadius = HoverTrackEndMoveRadius;
        [HideInInspector] public float hoverTrackStartSettleRadius = HoverTrackStartSettleRadius;
        [HideInInspector] public float hoverTrackEndSettleRadius = HoverTrackEndSettleRadius;
        [HideInInspector] public float hoverTrackStartHoldTime = HoverTrackStartHoldTime;
        [HideInInspector] public float hoverTrackEndHoldTime = HoverTrackEndHoldTime;
        [HideInInspector] public float hoverTrackStartMaxSpeed = HoverTrackStartMaxSpeed;
        [HideInInspector] public float hoverTrackEndMaxSpeed = HoverTrackEndMaxSpeed;
        [HideInInspector] public float hoverTrackStartMaxTiltDeg = HoverTrackStartMaxTiltDeg;
        [HideInInspector] public float hoverTrackEndMaxTiltDeg = HoverTrackEndMaxTiltDeg;
        [HideInInspector] public float hoverTrackCurriculumBatchesToMostlyHard = HoverTrackDefaultCurriculumBatchesToMostlyHard;
        [HideInInspector] public float curriculumDifficultyIncreaseSpeed = DefaultCurriculumDifficultyIncreaseSpeed;

        public const float HoverTrackStartMoveRadius = 16f;
        public const float HoverTrackEndMoveRadius = 50f;
        public const float HoverTrackStartSettleRadius = 10f;
        public const float HoverTrackEndSettleRadius = 2.5f;
        public const float HoverTrackStartHoldTime = 0.5f;
        public const float HoverTrackEndHoldTime = 2f;
        public const float HoverTrackStartMaxSpeed = 3f;
        public const float HoverTrackEndMaxSpeed = 0.8f;
        public const float HoverTrackStartMaxTiltDeg = 15f;
        public const float HoverTrackEndMaxTiltDeg = 5f;
        public const float HoverTrackDefaultCurriculumBatchesToMostlyHard = 120f;
        public const float DefaultCurriculumDifficultyIncreaseSpeed = 1f;
        public const float MinCurriculumDifficultyIncreaseSpeed = 0.25f;
        public const float MaxCurriculumDifficultyIncreaseSpeed = 4f;
        public const float CurriculumSuccessRateWindowEpisodes = 32f;
        public const float CurriculumAdvanceRateFloor = 0.55f;
        public const float CurriculumAdvanceRateCeiling = 0.85f;

        public float HoverTrackCurriculumSuccessRate =>
            hoverTrackCurriculumEpisodeCount > 0 ? Mathf.Clamp01(hoverTrackCurriculumRecentSuccessRate) : 0f;

        /// <summary>
        /// Resets the hover track curriculum state back to its episode/default values.
        /// </summary>
        public void ResetHoverTrackCurriculum()
        {
            hoverTrackCurriculumSuccesses = 0;
            hoverTrackCurriculumEpisodeCount = 0;
            hoverTrackCurriculumSuccessfulEpisodes = 0;
            hoverTrackCurriculumRecentSuccessRate = 0f;
            hoverTrackCurriculumLinearProgress = 0f;
            ApplyHoverTrackCurriculum();
        }

        /// <summary>
        /// Records one hover-track pad capture for HUD diagnostics. Difficulty
        /// advances only from completed-episode success rate.
        /// </summary>
        public void AdvanceHoverTrackCurriculum()
        {
            hoverTrackCurriculumSuccesses++;
            ApplyHoverTrackCurriculum();
        }

        /// <summary>
        /// Records one completed hover-track episode and advances continuous
        /// difficulty from the recent average successful-episode rate.
        /// </summary>
        public void RecordHoverTrackCurriculumEpisode(bool successfulEpisode, int activeAreaCount)
        {
            if (scenario != ScenarioType.HoverTracking) return;

            EnsureHoverTrackCurriculumDefaults();
            hoverTrackCurriculumEnabled = true;
            hoverTrackCurriculumEpisodeCount++;
            if (successfulEpisode)
                hoverTrackCurriculumSuccessfulEpisodes++;

            hoverTrackCurriculumRecentSuccessRate = CurriculumSuccessRateAfterEpisode(
                hoverTrackCurriculumRecentSuccessRate,
                hoverTrackCurriculumSuccessfulEpisodes,
                hoverTrackCurriculumEpisodeCount,
                successfulEpisode);

            hoverTrackCurriculumLinearProgress = Mathf.Clamp01(
                hoverTrackCurriculumLinearProgress +
                CurriculumProgressDelta(
                    hoverTrackCurriculumRecentSuccessRate,
                    hoverTrackCurriculumBatchesToMostlyHard,
                    activeAreaCount));

            ApplyHoverTrackCurriculum();
        }

        /// <summary>
        /// Converts accumulated hover-track successes into target movement,
        /// settle radius, hold time, speed, and tilt thresholds.
        /// </summary>
        public void ApplyHoverTrackCurriculum()
        {
            if (scenario != ScenarioType.HoverTracking) return;

            EnsureHoverTrackCurriculumDefaults();
            hoverTrackCurriculumEnabled = true;
            moveTargetEnabled = true;

            hoverTrackCurriculumProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hoverTrackCurriculumLinearProgress));

            targetMoveRadius = Mathf.Lerp(hoverTrackStartMoveRadius, hoverTrackEndMoveRadius, hoverTrackCurriculumProgress);
            hoverTrackSettleRadius = Mathf.Lerp(hoverTrackStartSettleRadius, hoverTrackEndSettleRadius, hoverTrackCurriculumProgress);
            hoverTrackSuccessHoldTime = Mathf.Lerp(hoverTrackStartHoldTime, hoverTrackEndHoldTime, hoverTrackCurriculumProgress);
            hoverTrackSuccessMaxSpeed = Mathf.Lerp(hoverTrackStartMaxSpeed, hoverTrackEndMaxSpeed, hoverTrackCurriculumProgress);
            hoverTrackSuccessMaxTiltDeg = Mathf.Lerp(hoverTrackStartMaxTiltDeg, hoverTrackEndMaxTiltDeg, hoverTrackCurriculumProgress);
        }

        /// <summary>
        /// Backfills hover-track curriculum ranges for older serialized configs
        /// before difficulty interpolation runs.
        /// </summary>
        void EnsureHoverTrackCurriculumDefaults()
        {
            bool missingRanges =
                hoverTrackStartMoveRadius <= 0f &&
                hoverTrackEndMoveRadius <= 0f &&
                hoverTrackStartSettleRadius <= 0f &&
                hoverTrackEndSettleRadius <= 0f &&
                hoverTrackStartHoldTime <= 0f &&
                hoverTrackEndHoldTime <= 0f &&
                hoverTrackStartMaxSpeed <= 0f &&
                hoverTrackEndMaxSpeed <= 0f &&
                hoverTrackStartMaxTiltDeg <= 0f &&
                hoverTrackEndMaxTiltDeg <= 0f;

            if (missingRanges)
            {
                hoverTrackCurriculumEnabled = true;
                hoverTrackStartMoveRadius = HoverTrackStartMoveRadius;
                hoverTrackEndMoveRadius = HoverTrackEndMoveRadius;
                hoverTrackStartSettleRadius = HoverTrackStartSettleRadius;
                hoverTrackEndSettleRadius = HoverTrackEndSettleRadius;
                hoverTrackStartHoldTime = HoverTrackStartHoldTime;
                hoverTrackEndHoldTime = HoverTrackEndHoldTime;
                hoverTrackStartMaxSpeed = HoverTrackStartMaxSpeed;
                hoverTrackEndMaxSpeed = HoverTrackEndMaxSpeed;
                hoverTrackStartMaxTiltDeg = HoverTrackStartMaxTiltDeg;
                hoverTrackEndMaxTiltDeg = HoverTrackEndMaxTiltDeg;
            }

            if (hoverTrackCurriculumBatchesToMostlyHard <= 0f)
                hoverTrackCurriculumBatchesToMostlyHard = HoverTrackDefaultCurriculumBatchesToMostlyHard;
            curriculumDifficultyIncreaseSpeed = Mathf.Clamp(
                curriculumDifficultyIncreaseSpeed <= 0f ? DefaultCurriculumDifficultyIncreaseSpeed : curriculumDifficultyIncreaseSpeed,
                MinCurriculumDifficultyIncreaseSpeed,
                MaxCurriculumDifficultyIncreaseSpeed);
            if (hoverTrackCurriculumEpisodeCount <= 0 && hoverTrackCurriculumSuccesses > 0)
            {
                hoverTrackCurriculumEpisodeCount = hoverTrackCurriculumSuccesses;
                hoverTrackCurriculumSuccessfulEpisodes = hoverTrackCurriculumSuccesses;
                hoverTrackCurriculumRecentSuccessRate = 1f;
            }

            if (hoverTrackCurriculumLinearProgress <= 0f && hoverTrackCurriculumProgress > 0f)
                hoverTrackCurriculumLinearProgress = hoverTrackCurriculumProgress;

            hoverTrackCurriculumLinearProgress = Mathf.Clamp01(hoverTrackCurriculumLinearProgress);
        }

        /// <summary>
        /// Updates the recent success rate using a cumulative average at startup
        /// and an exponential moving average once enough episodes exist.
        /// </summary>
        float CurriculumSuccessRateAfterEpisode(
            float previousRate,
            int successfulEpisodes,
            int episodeCount,
            bool successfulEpisode)
        {
            if (episodeCount <= 0) return 0f;
            if (episodeCount <= CurriculumSuccessRateWindowEpisodes)
                return Mathf.Clamp01(successfulEpisodes / Mathf.Max(1f, episodeCount));

            float alpha = 2f / (CurriculumSuccessRateWindowEpisodes + 1f);
            return Mathf.Lerp(previousRate, successfulEpisode ? 1f : 0f, alpha);
        }

        /// <summary>
        /// Converts recent success rate into normalized progress gain, scaled by
        /// the active-area count so parallel batches do not advance faster just
        /// because more agents are running.
        /// </summary>
        float CurriculumProgressDelta(float successRate, float batchesToFullDifficulty, int activeAreaCount)
        {
            float successPressure = Mathf.InverseLerp(
                CurriculumAdvanceRateFloor,
                CurriculumAdvanceRateCeiling,
                Mathf.Clamp01(successRate));
            return successPressure *
                   Mathf.Clamp(curriculumDifficultyIncreaseSpeed, MinCurriculumDifficultyIncreaseSpeed, MaxCurriculumDifficultyIncreaseSpeed) /
                   Mathf.Max(1f, batchesToFullDifficulty) /
                   Mathf.Max(1f, activeAreaCount);
        }

    }
}
