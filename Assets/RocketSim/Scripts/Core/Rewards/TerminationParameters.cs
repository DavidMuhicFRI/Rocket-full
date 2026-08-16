// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/TerminationParameters.cs
// Purpose: Defines task success, failure, and safety rules.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Scenario-specific episode-ending switches and thresholds. The UI catalog
    /// shows only rules that apply to the selected task.
    /// </summary>
    [Serializable]
    public sealed class TerminationParameters
    {
        // Common safety and limit rules.
        public bool unsafeAttitudeEnabled;
        public float maximumTiltDeg;
        public bool planarFlyawayEnabled;
        public float maximumPlanarDistanceM;
        public bool altitudeCeilingEnabled;
        public float maximumAltitudeAboveStartM;
        public float hoverMaximumAltitudeM;
        public bool fuelDepletionEnabled;
        public float minimumFuelKg;
        public bool timeLimitEnabled;
        public float maximumEpisodeSeconds;

        // Landing success criteria interpolate with curriculum difficulty.
        public DifficultyRange landingSuccessRadiusM;
        public DifficultyRange landingSuccessMaxTotalSpeedMps;
        public DifficultyRange landingSuccessMaxVerticalSpeedMps;
        public DifficultyRange landingSuccessMaxHorizontalSpeedMps;
        public DifficultyRange landingSuccessMaxTiltDeg;
        public DifficultyRange landingSuccessMaxAngularRateDegS;
        public DifficultyRange landingSuccessMaxYawErrorDeg;
        public DifficultyRange landingStableHoldSeconds;

        // Chopstick capture.
        public bool chopstickCapturePlaneEnabled;
        public bool chopstickRequireStablePlatform;

        // Physical leg landing.
        public bool legStructuralStrikeEnabled;
        public bool legFootOutsidePadEnabled;
        public bool legHardFirstContactEnabled;
        public bool legExcessiveReboundEnabled;
        public bool legStableTouchdownEnabled;
        public bool legMissedPadEnabled;
        public float legMissedPadDepthM;
        public int legMinimumStableFeet;
        public float legMaximumReboundRiseM;
        public float legMaximumAllFeetContactLossSeconds;
        public bool legAllowFuelDepletionAfterContact;

        // Fixed and moving-target hover.
        public bool hoverGroundImpactEnabled;
        public float hoverGroundClearanceM;
        public bool trackingCaptureGoalEnabled;
        public int trackingRequiredCaptures;
        public DifficultyRange trackingCaptureRadiusM;
        public DifficultyRange trackingCaptureMaxVerticalErrorM;
        public DifficultyRange trackingCaptureMaxHorizontalSpeedMps;
        public DifficultyRange trackingCaptureMaxVerticalSpeedMps;
        public DifficultyRange trackingCaptureMaxTiltDeg;
        public DifficultyRange trackingCaptureMaxAngularRateDegS;
        public DifficultyRange trackingCaptureHoldSeconds;

        /// <summary>Disables and clears every termination rule.</summary>
        public void Clear()
        {
            unsafeAttitudeEnabled = false;
            maximumTiltDeg = 0f;
            planarFlyawayEnabled = false;
            maximumPlanarDistanceM = 0f;
            altitudeCeilingEnabled = false;
            maximumAltitudeAboveStartM = 0f;
            hoverMaximumAltitudeM = 0f;
            fuelDepletionEnabled = false;
            minimumFuelKg = 0f;
            timeLimitEnabled = false;
            maximumEpisodeSeconds = 0f;
            landingSuccessRadiusM = default;
            landingSuccessMaxTotalSpeedMps = default;
            landingSuccessMaxVerticalSpeedMps = default;
            landingSuccessMaxHorizontalSpeedMps = default;
            landingSuccessMaxTiltDeg = default;
            landingSuccessMaxAngularRateDegS = default;
            landingSuccessMaxYawErrorDeg = default;
            landingStableHoldSeconds = default;
            chopstickCapturePlaneEnabled = false;
            chopstickRequireStablePlatform = false;
            legStructuralStrikeEnabled = false;
            legFootOutsidePadEnabled = false;
            legHardFirstContactEnabled = false;
            legExcessiveReboundEnabled = false;
            legStableTouchdownEnabled = false;
            legMissedPadEnabled = false;
            legMissedPadDepthM = 0f;
            legMinimumStableFeet = 0;
            legMaximumReboundRiseM = 0f;
            legMaximumAllFeetContactLossSeconds = 0f;
            legAllowFuelDepletionAfterContact = false;
            hoverGroundImpactEnabled = false;
            hoverGroundClearanceM = 0f;
            trackingCaptureGoalEnabled = false;
            trackingRequiredCaptures = 0;
            trackingCaptureRadiusM = default;
            trackingCaptureMaxVerticalErrorM = default;
            trackingCaptureMaxHorizontalSpeedMps = default;
            trackingCaptureMaxVerticalSpeedMps = default;
            trackingCaptureMaxTiltDeg = default;
            trackingCaptureMaxAngularRateDegS = default;
            trackingCaptureHoldSeconds = default;
        }

        /// <summary>Deep-copies all termination switches and thresholds.</summary>
        public void CopyFrom(TerminationParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            unsafeAttitudeEnabled = source.unsafeAttitudeEnabled;
            maximumTiltDeg = source.maximumTiltDeg;
            planarFlyawayEnabled = source.planarFlyawayEnabled;
            maximumPlanarDistanceM = source.maximumPlanarDistanceM;
            altitudeCeilingEnabled = source.altitudeCeilingEnabled;
            maximumAltitudeAboveStartM = source.maximumAltitudeAboveStartM;
            hoverMaximumAltitudeM = source.hoverMaximumAltitudeM;
            fuelDepletionEnabled = source.fuelDepletionEnabled;
            minimumFuelKg = source.minimumFuelKg;
            timeLimitEnabled = source.timeLimitEnabled;
            maximumEpisodeSeconds = source.maximumEpisodeSeconds;
            landingSuccessRadiusM = source.landingSuccessRadiusM;
            landingSuccessMaxTotalSpeedMps = source.landingSuccessMaxTotalSpeedMps;
            landingSuccessMaxVerticalSpeedMps = source.landingSuccessMaxVerticalSpeedMps;
            landingSuccessMaxHorizontalSpeedMps = source.landingSuccessMaxHorizontalSpeedMps;
            landingSuccessMaxTiltDeg = source.landingSuccessMaxTiltDeg;
            landingSuccessMaxAngularRateDegS = source.landingSuccessMaxAngularRateDegS;
            landingSuccessMaxYawErrorDeg = source.landingSuccessMaxYawErrorDeg;
            landingStableHoldSeconds = source.landingStableHoldSeconds;
            chopstickCapturePlaneEnabled = source.chopstickCapturePlaneEnabled;
            chopstickRequireStablePlatform = source.chopstickRequireStablePlatform;
            legStructuralStrikeEnabled = source.legStructuralStrikeEnabled;
            legFootOutsidePadEnabled = source.legFootOutsidePadEnabled;
            legHardFirstContactEnabled = source.legHardFirstContactEnabled;
            legExcessiveReboundEnabled = source.legExcessiveReboundEnabled;
            legStableTouchdownEnabled = source.legStableTouchdownEnabled;
            legMissedPadEnabled = source.legMissedPadEnabled;
            legMissedPadDepthM = source.legMissedPadDepthM;
            legMinimumStableFeet = source.legMinimumStableFeet;
            legMaximumReboundRiseM = source.legMaximumReboundRiseM;
            legMaximumAllFeetContactLossSeconds = source.legMaximumAllFeetContactLossSeconds;
            legAllowFuelDepletionAfterContact = source.legAllowFuelDepletionAfterContact;
            hoverGroundImpactEnabled = source.hoverGroundImpactEnabled;
            hoverGroundClearanceM = source.hoverGroundClearanceM;
            trackingCaptureGoalEnabled = source.trackingCaptureGoalEnabled;
            trackingRequiredCaptures = source.trackingRequiredCaptures;
            trackingCaptureRadiusM = source.trackingCaptureRadiusM;
            trackingCaptureMaxVerticalErrorM = source.trackingCaptureMaxVerticalErrorM;
            trackingCaptureMaxHorizontalSpeedMps = source.trackingCaptureMaxHorizontalSpeedMps;
            trackingCaptureMaxVerticalSpeedMps = source.trackingCaptureMaxVerticalSpeedMps;
            trackingCaptureMaxTiltDeg = source.trackingCaptureMaxTiltDeg;
            trackingCaptureMaxAngularRateDegS = source.trackingCaptureMaxAngularRateDegS;
            trackingCaptureHoldSeconds = source.trackingCaptureHoldSeconds;
        }
    }
}
