// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Curriculum/Tasks/HoverTrackingCurriculum.cs
// Purpose: Tracks hover-target training success and continuously turns that
// success into harder target movement while objective thresholds interpolate
// independently from the same normalized difficulty value.
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
        [HideInInspector] public bool hoverTrackCurriculumEnabled = true;
        [HideInInspector] public int hoverTrackCurriculumSuccesses;
        [HideInInspector] public int hoverTrackCurriculumEpisodeCount;
        [HideInInspector] public int hoverTrackCurriculumSuccessfulEpisodes;
        [HideInInspector] public float hoverTrackCurriculumRecentSuccessRate;
        [HideInInspector] public float hoverTrackCurriculumLinearProgress;
        [HideInInspector] public float hoverTrackCurriculumProgress;
        [HideInInspector] public float hoverTrackStartMoveRadius = HoverTrackStartMoveRadius;
        [HideInInspector] public float hoverTrackEndMoveRadius = HoverTrackEndMoveRadius;
        [HideInInspector] public float hoverTrackCurriculumBatchesToMostlyHard = HoverTrackDefaultCurriculumBatchesToMostlyHard;
        [HideInInspector] public float curriculumDifficultyIncreaseSpeed = DefaultCurriculumDifficultyIncreaseSpeed;

        public const float HoverTrackStartMoveRadius = 16f;
        public const float HoverTrackEndMoveRadius = 50f;
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
        /// Converts accumulated hover-track success into target movement
        /// difficulty. Capture thresholds live only in the training objective.
        /// </summary>
        public void ApplyHoverTrackCurriculum()
        {
            if (scenario != ScenarioType.HoverTracking) return;

            EnsureHoverTrackCurriculumDefaults();
            hoverTrackCurriculumEnabled = true;
            moveTargetEnabled = true;

            hoverTrackCurriculumProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hoverTrackCurriculumLinearProgress));

            targetMoveRadius = Mathf.Lerp(hoverTrackStartMoveRadius, hoverTrackEndMoveRadius, hoverTrackCurriculumProgress);
        }

        /// <summary>
        /// Restores required hover-track ranges and clamps curriculum settings
        /// before difficulty interpolation runs.
        /// </summary>
        void EnsureHoverTrackCurriculumDefaults()
        {
            bool missingRanges = hoverTrackStartMoveRadius <= 0f && hoverTrackEndMoveRadius <= 0f;

            if (missingRanges)
            {
                hoverTrackCurriculumEnabled = true;
                hoverTrackStartMoveRadius = HoverTrackStartMoveRadius;
                hoverTrackEndMoveRadius = HoverTrackEndMoveRadius;
            }

            if (hoverTrackCurriculumBatchesToMostlyHard <= 0f)
                hoverTrackCurriculumBatchesToMostlyHard = HoverTrackDefaultCurriculumBatchesToMostlyHard;
            curriculumDifficultyIncreaseSpeed = Mathf.Clamp(
                curriculumDifficultyIncreaseSpeed <= 0f ? DefaultCurriculumDifficultyIncreaseSpeed : curriculumDifficultyIncreaseSpeed,
                MinCurriculumDifficultyIncreaseSpeed,
                MaxCurriculumDifficultyIncreaseSpeed);
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
