// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardRuntimeContext.cs
// Purpose: Carries live episode/contact state into the pure reward evaluator.
// Tunable thresholds belong to ScenarioObjectiveConfig, never in this struct.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>
    /// Immutable per-step state assembled by FalconAgent. It deliberately holds
    /// measurements and event flags only; reward magnitudes, shaping scales, and
    /// termination thresholds come exclusively from the scenario objective.
    /// </summary>
    public readonly struct RewardRuntimeContext
    {
        public readonly float altitude;
        public readonly float terminalAltitude;
        public readonly float episodeStartAltitude;
        public readonly float gravityMagnitude;
        public readonly float episodeElapsedSeconds;
        public readonly float curriculumDifficulty01;
        public readonly float landingCriteriaDifficulty01;
        public readonly float fuelKg;
        public readonly float fuelFraction01;
        public readonly int episodeEngineRestartCount;
        public readonly int episodeEngineFirstIgnitionCount;
        public readonly float legLandingCenteringProgressRate;
        public readonly float legFootSupportProgress01;

        // Chopstick capture state.
        public readonly bool landingPlatformInsideCapture;
        public readonly bool landingPlatformStable;
        public readonly bool landingPlatformBecameStable;
        public readonly float landingPlatformStableTime;

        // Physical leg-contact state.
        public readonly bool legTouchdownStarted;
        public readonly bool legImpactStarted;
        public readonly bool legOffPadImpact;
        public readonly bool legFirstContactThisStep;
        public readonly int legFeetOnPad;
        public readonly bool legFootOutsidePad;
        public readonly bool legStructuralStrike;
        public readonly bool legPropulsionOff;
        public readonly bool legSupportSettled;
        public readonly float legSupportSettledTime;
        public readonly bool legStable;
        public readonly bool legBecameStable;
        public readonly float legStableTime;
        public readonly float legFirstContactSpeed;
        public readonly float legFirstContactVerticalSpeed;
        public readonly float legFirstContactHorizontalSpeed;
        public readonly float legFirstContactTiltDeg;
        public readonly float legFirstContactAngularRateDegS;
        public readonly bool legExcessiveRebound;

        // Hover events. The capture count includes a capture reported this step.
        public readonly int engineRestartsThisStep;
        public readonly bool hoverTrackTargetCapturedThisStep;
        public readonly int hoverTrackEpisodeCaptures;

        public RewardRuntimeContext(
            float altitude,
            float terminalAltitude,
            float episodeStartAltitude,
            float gravityMagnitude,
            float episodeElapsedSeconds,
            float curriculumDifficulty01,
            float fuelKg,
            bool landingPlatformInsideCapture = false,
            bool landingPlatformStable = false,
            bool landingPlatformBecameStable = false,
            float landingPlatformStableTime = 0f,
            int engineRestartsThisStep = 0,
            bool hoverTrackTargetCapturedThisStep = false,
            int hoverTrackEpisodeCaptures = 0,
            bool legTouchdownStarted = false,
            bool legImpactStarted = false,
            bool legOffPadImpact = false,
            bool legFirstContactThisStep = false,
            int legFeetOnPad = 0,
            bool legFootOutsidePad = false,
            bool legStructuralStrike = false,
            bool legPropulsionOff = false,
            bool legSupportSettled = false,
            float legSupportSettledTime = 0f,
            bool legStable = false,
            bool legBecameStable = false,
            float legStableTime = 0f,
            float legFirstContactSpeed = 0f,
            float legFirstContactVerticalSpeed = 0f,
            float legFirstContactHorizontalSpeed = 0f,
            float legFirstContactTiltDeg = 0f,
            float legFirstContactAngularRateDegS = 0f,
            bool legExcessiveRebound = false,
            float landingCriteriaDifficulty01 = -1f,
            float fuelFraction01 = 0f,
            float legLandingCenteringProgressRate = 0f,
            float legFootSupportProgress01 = 0f,
            int episodeEngineRestartCount = 0,
            int episodeEngineFirstIgnitionCount = 0)
        {
            this.altitude = altitude;
            this.terminalAltitude = terminalAltitude;
            this.episodeStartAltitude = episodeStartAltitude;
            this.gravityMagnitude = gravityMagnitude;
            this.episodeElapsedSeconds = episodeElapsedSeconds;
            this.curriculumDifficulty01 = curriculumDifficulty01;
            this.landingCriteriaDifficulty01 = landingCriteriaDifficulty01 < 0f
                ? curriculumDifficulty01
                : landingCriteriaDifficulty01;
            this.fuelKg = fuelKg;
            this.fuelFraction01 = fuelFraction01;
            this.episodeEngineRestartCount = episodeEngineRestartCount;
            this.episodeEngineFirstIgnitionCount = episodeEngineFirstIgnitionCount;
            this.legLandingCenteringProgressRate = legLandingCenteringProgressRate;
            this.legFootSupportProgress01 = legFootSupportProgress01;
            this.landingPlatformInsideCapture = landingPlatformInsideCapture;
            this.landingPlatformStable = landingPlatformStable;
            this.landingPlatformBecameStable = landingPlatformBecameStable;
            this.landingPlatformStableTime = landingPlatformStableTime;
            this.engineRestartsThisStep = engineRestartsThisStep;
            this.hoverTrackTargetCapturedThisStep = hoverTrackTargetCapturedThisStep;
            this.hoverTrackEpisodeCaptures = hoverTrackEpisodeCaptures;
            this.legTouchdownStarted = legTouchdownStarted;
            this.legImpactStarted = legImpactStarted;
            this.legOffPadImpact = legOffPadImpact;
            this.legFirstContactThisStep = legFirstContactThisStep;
            this.legFeetOnPad = legFeetOnPad;
            this.legFootOutsidePad = legFootOutsidePad;
            this.legStructuralStrike = legStructuralStrike;
            this.legPropulsionOff = legPropulsionOff;
            this.legSupportSettled = legSupportSettled;
            this.legSupportSettledTime = legSupportSettledTime;
            this.legStable = legStable;
            this.legBecameStable = legBecameStable;
            this.legStableTime = legStableTime;
            this.legFirstContactSpeed = legFirstContactSpeed;
            this.legFirstContactVerticalSpeed = legFirstContactVerticalSpeed;
            this.legFirstContactHorizontalSpeed = legFirstContactHorizontalSpeed;
            this.legFirstContactTiltDeg = legFirstContactTiltDeg;
            this.legFirstContactAngularRateDegS = legFirstContactAngularRateDegS;
            this.legExcessiveRebound = legExcessiveRebound;
        }
    }
}
