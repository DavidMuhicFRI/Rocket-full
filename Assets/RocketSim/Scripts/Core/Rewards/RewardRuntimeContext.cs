// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardRuntimeContext.cs
// Purpose: Carries scenario limits and live state that reward models need but
// that are not generic geometric/velocity RewardTerms.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>
    /// Immutable per-step context assembled by FalconAgent before reward
    /// evaluation. It keeps reward models independent from MonoBehaviour state.
    /// </summary>
    public readonly struct RewardRuntimeContext
    {
        public readonly float altitude;
        public readonly float terminalAltitude;
        public readonly float gravityMagnitude;
        public readonly float episodeElapsedSeconds;
        public readonly float landingMaxEpisodeSeconds;
        public readonly float fuelKg;
        public readonly float hoverTrackSettleRadius;
        public readonly float landingFlyawayAltitude;
        public readonly float landingSuccessRadius;
        public readonly float landingSuccessMaxSpeed;
        public readonly float landingSuccessMaxVerticalSpeed;
        public readonly float landingSuccessMaxHorizontalSpeed;
        public readonly float landingSuccessMaxTiltDeg;
        public readonly float landingSuccessMaxAngularRateDegS;
        public readonly float landingSuccessMaxYawErrorDeg;
        public readonly bool landingPlatformRequired;
        public readonly bool landingPlatformInsideCapture;
        public readonly bool landingPlatformStable;
        public readonly bool landingPlatformBecameStable;
        public readonly float landingPlatformStableTime;
        public readonly float landingPlatformStableHoldTime;
        public readonly float landingPlatformHalfSize;
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
        public readonly int engineRestartsThisStep;

        /// <summary>Stores the live scenario thresholds used for this evaluation.</summary>
        public RewardRuntimeContext(
            float altitude,
            float terminalAltitude,
            float gravityMagnitude,
            float episodeElapsedSeconds,
            float landingMaxEpisodeSeconds,
            float fuelKg,
            float hoverTrackSettleRadius,
            float landingFlyawayAltitude,
            float landingSuccessRadius,
            float landingSuccessMaxSpeed,
            float landingSuccessMaxVerticalSpeed,
            float landingSuccessMaxHorizontalSpeed,
            float landingSuccessMaxTiltDeg,
            float landingSuccessMaxAngularRateDegS,
            float landingSuccessMaxYawErrorDeg,
            bool landingPlatformRequired,
            bool landingPlatformInsideCapture,
            bool landingPlatformStable,
            bool landingPlatformBecameStable,
            float landingPlatformStableTime,
            float landingPlatformStableHoldTime,
            float landingPlatformHalfSize,
            int engineRestartsThisStep = 0,
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
            float legFirstContactAngularRateDegS = 0f)
        {
            this.altitude = altitude;
            this.terminalAltitude = terminalAltitude;
            this.gravityMagnitude = gravityMagnitude;
            this.episodeElapsedSeconds = episodeElapsedSeconds;
            this.landingMaxEpisodeSeconds = landingMaxEpisodeSeconds;
            this.fuelKg = fuelKg;
            this.hoverTrackSettleRadius = hoverTrackSettleRadius;
            this.landingFlyawayAltitude = landingFlyawayAltitude;
            this.landingSuccessRadius = landingSuccessRadius;
            this.landingSuccessMaxSpeed = landingSuccessMaxSpeed;
            this.landingSuccessMaxVerticalSpeed = landingSuccessMaxVerticalSpeed;
            this.landingSuccessMaxHorizontalSpeed = landingSuccessMaxHorizontalSpeed;
            this.landingSuccessMaxTiltDeg = landingSuccessMaxTiltDeg;
            this.landingSuccessMaxAngularRateDegS = landingSuccessMaxAngularRateDegS;
            this.landingSuccessMaxYawErrorDeg = landingSuccessMaxYawErrorDeg;
            this.landingPlatformRequired = landingPlatformRequired;
            this.landingPlatformInsideCapture = landingPlatformInsideCapture;
            this.landingPlatformStable = landingPlatformStable;
            this.landingPlatformBecameStable = landingPlatformBecameStable;
            this.landingPlatformStableTime = landingPlatformStableTime;
            this.landingPlatformStableHoldTime = landingPlatformStableHoldTime;
            this.landingPlatformHalfSize = landingPlatformHalfSize;
            this.engineRestartsThisStep = engineRestartsThisStep;
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
        }
    }

}
