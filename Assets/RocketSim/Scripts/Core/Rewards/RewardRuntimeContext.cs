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
        public readonly float fuelKg;

        // Chopstick capture state.
        public readonly bool landingPlatformInsideCapture;
        public readonly bool landingPlatformStable;
        public readonly bool landingPlatformBecameStable;
        public readonly float landingPlatformStableTime;

        // Physical leg-contact state.
        public readonly bool legTouchdownStarted;
        public readonly bool legFirstContactThisStep;
        public readonly int legFeetOnPad;
        public readonly bool legFootOutsidePad;
        public readonly bool legStructuralStrike;
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
            bool legFirstContactThisStep = false,
            int legFeetOnPad = 0,
            bool legFootOutsidePad = false,
            bool legStructuralStrike = false,
            bool legStable = false,
            bool legBecameStable = false,
            float legStableTime = 0f,
            float legFirstContactSpeed = 0f,
            float legFirstContactVerticalSpeed = 0f,
            float legFirstContactHorizontalSpeed = 0f,
            float legFirstContactTiltDeg = 0f,
            float legFirstContactAngularRateDegS = 0f,
            bool legExcessiveRebound = false)
        {
            this.altitude = altitude;
            this.terminalAltitude = terminalAltitude;
            this.episodeStartAltitude = episodeStartAltitude;
            this.gravityMagnitude = gravityMagnitude;
            this.episodeElapsedSeconds = episodeElapsedSeconds;
            this.curriculumDifficulty01 = curriculumDifficulty01;
            this.fuelKg = fuelKg;
            this.landingPlatformInsideCapture = landingPlatformInsideCapture;
            this.landingPlatformStable = landingPlatformStable;
            this.landingPlatformBecameStable = landingPlatformBecameStable;
            this.landingPlatformStableTime = landingPlatformStableTime;
            this.engineRestartsThisStep = engineRestartsThisStep;
            this.hoverTrackTargetCapturedThisStep = hoverTrackTargetCapturedThisStep;
            this.hoverTrackEpisodeCaptures = hoverTrackEpisodeCaptures;
            this.legTouchdownStarted = legTouchdownStarted;
            this.legFirstContactThisStep = legFirstContactThisStep;
            this.legFeetOnPad = legFeetOnPad;
            this.legFootOutsidePad = legFootOutsidePad;
            this.legStructuralStrike = legStructuralStrike;
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
