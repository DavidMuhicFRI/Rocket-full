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
        public readonly float fuelKg;
        public readonly float hoverTrackSettleRadius;
        public readonly float landingFailureAltitude;
        public readonly float landingSuccessRadius;
        public readonly float landingSuccessMaxSpeed;
        public readonly float landingSuccessMaxVerticalSpeed;
        public readonly float landingSuccessMaxHorizontalSpeed;
        public readonly float landingSuccessMaxTiltDeg;
        public readonly float landingSuccessMaxAngularRateDegS;
        public readonly float landingSuccessMaxYawErrorDeg;
        public readonly bool landingPlatformRequired;
        public readonly bool landingPlatformPhysicalActive;
        public readonly bool landingPlatformInsideCapture;
        public readonly bool landingPlatformStable;
        public readonly float landingPlatformStableTime;
        public readonly float landingPlatformStableHoldTime;
        public readonly float landingPlatformHalfSize;

        /// <summary>Stores the live scenario thresholds used for this evaluation.</summary>
        public RewardRuntimeContext(
            float altitude,
            float terminalAltitude,
            float fuelKg,
            float hoverTrackSettleRadius,
            float landingFailureAltitude,
            float landingSuccessRadius,
            float landingSuccessMaxSpeed,
            float landingSuccessMaxVerticalSpeed,
            float landingSuccessMaxHorizontalSpeed,
            float landingSuccessMaxTiltDeg,
            float landingSuccessMaxAngularRateDegS,
            float landingSuccessMaxYawErrorDeg,
            bool landingPlatformRequired,
            bool landingPlatformPhysicalActive,
            bool landingPlatformInsideCapture,
            bool landingPlatformStable,
            float landingPlatformStableTime,
            float landingPlatformStableHoldTime,
            float landingPlatformHalfSize)
        {
            this.altitude = altitude;
            this.terminalAltitude = terminalAltitude;
            this.fuelKg = fuelKg;
            this.hoverTrackSettleRadius = hoverTrackSettleRadius;
            this.landingFailureAltitude = landingFailureAltitude;
            this.landingSuccessRadius = landingSuccessRadius;
            this.landingSuccessMaxSpeed = landingSuccessMaxSpeed;
            this.landingSuccessMaxVerticalSpeed = landingSuccessMaxVerticalSpeed;
            this.landingSuccessMaxHorizontalSpeed = landingSuccessMaxHorizontalSpeed;
            this.landingSuccessMaxTiltDeg = landingSuccessMaxTiltDeg;
            this.landingSuccessMaxAngularRateDegS = landingSuccessMaxAngularRateDegS;
            this.landingSuccessMaxYawErrorDeg = landingSuccessMaxYawErrorDeg;
            this.landingPlatformRequired = landingPlatformRequired;
            this.landingPlatformPhysicalActive = landingPlatformPhysicalActive;
            this.landingPlatformInsideCapture = landingPlatformInsideCapture;
            this.landingPlatformStable = landingPlatformStable;
            this.landingPlatformStableTime = landingPlatformStableTime;
            this.landingPlatformStableHoldTime = landingPlatformStableHoldTime;
            this.landingPlatformHalfSize = landingPlatformHalfSize;
        }
    }

}
