// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/TelemetryMetricCatalog.cs
// Purpose: Defines the single ordered list of available telemetry columns,
// including the config toggle and TelemetryRow field behind each column.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    internal static class TelemetryMetricCatalog
    {
        /// <summary>
        /// Filters the complete telemetry catalog against the current logging
        /// toggles to produce the CSV column set for this run.
        /// </summary>
        public static List<TelemetryMetricDescriptor> Build(TelemetryConfig cfg)
        {
            var metrics = new List<TelemetryMetricDescriptor>();
            foreach (var metric in MetricCatalog)
                if (metric.IsEnabled(cfg))
                    metrics.Add(metric);

            return metrics;
        }

        /// <summary>
        /// Counts enabled telemetry metrics using the same catalog that builds
        /// CSV headers, so UI estimates cannot drift from logger output.
        /// </summary>
        public static int CountEnabled(TelemetryConfig cfg)
        {
            int count = 0;
            foreach (var metric in MetricCatalog)
                if (metric.IsEnabled(cfg))
                    count++;
            return count;
        }

        // Single source of truth for CSV column order and row extraction.
        static readonly TelemetryMetricDescriptor[] MetricCatalog =
        {
            new("Goal_DistanceToGoal3D_m", cfg => cfg.logGoalMetrics, r => r.goal_distance3D),
            new("Goal_HorizontalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => r.goal_planarDistance),
            new("Goal_VerticalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => r.goal_verticalError),
            // Keep the signed vertical error and absolute distance as separate columns.
            new("Goal_AbsoluteVerticalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => Mathf.Abs(r.goal_verticalError)),
            new("Track_IsHoverPhase01", cfg => cfg.logGoalMetrics, r => r.track_phaseHover01),
            new("Track_IsHoverReady01", cfg => cfg.logGoalMetrics, r => r.track_hoverReady01),
            new("Track_StableHoverTime_s", cfg => cfg.logGoalMetrics, r => r.track_stableTime),
            new("Track_TargetCaptured01", cfg => cfg.logGoalMetrics, r => r.track_targetReached01),
            new("Track_HoverRadius_m", cfg => cfg.logGoalMetrics, r => r.track_settleRadius),
            new("Track_CurriculumDifficulty01", cfg => cfg.logGoalMetrics, r => r.track_curriculumProgress),
            new("Track_TargetJumpDistance_m", cfg => cfg.logGoalMetrics, r => r.track_segmentStartDistance),
            new("Track_TargetElapsedTime_s", cfg => cfg.logGoalMetrics, r => r.track_segmentElapsedTime),
            new("Track_TravelProgress01", cfg => cfg.logGoalMetrics, r => r.track_travelProgress01),
            new("Track_NormalizedTravelRate_1ps", cfg => cfg.logGoalMetrics, r => r.track_travelProgressRate),
            new("Track_DirectionAccuracy01", cfg => cfg.logGoalMetrics, r => r.track_directionEfficiency01),
            new("Track_StabilizationQuality01", cfg => cfg.logGoalMetrics, r => r.track_settleQuality01),
            new("Landing_PlatformRequired01", cfg => cfg.logGoalMetrics, r => r.landing_platformRequired01),
            new("Landing_PlatformInsideCapture01", cfg => cfg.logGoalMetrics, r => r.landing_platformInsideCapture01),
            new("Landing_PlatformStable01", cfg => cfg.logGoalMetrics, r => r.landing_platformStable01),
            new("Landing_PlatformStableTime_s", cfg => cfg.logGoalMetrics, r => r.landing_platformStableTime),
            new("Landing_PlatformHalfSize_m", cfg => cfg.logGoalMetrics, r => r.landing_platformHalfSize),
            new("State_RocketAltitude_m", cfg => cfg.logGoalMetrics, r => r.state_altitude),
            new("Nav_BearingToGoalDeg", cfg => cfg.logGoalMetrics, r => r.nav_targetBearingDeg),
            new("Nav_HorizontalVelocityBearingDeg", cfg => cfg.logVelocityMetrics, r => r.nav_velocityBearingDeg),
            new("Nav_VelocityDirectionErrorToGoalDeg", cfg => cfg.logVelocityMetrics, r => r.nav_velocityTargetErrorDeg),
            new("Nav_VelocityAlignmentToGoal", cfg => cfg.logVelocityMetrics, r => r.nav_goalAlignment),
            new("Nav_GimbalBearingDeg", cfg => cfg.logControlMetrics, r => r.nav_gimbalBearingDeg),
            new("Nav_GimbalDirectionErrorToGoalDeg", cfg => cfg.logControlMetrics, r => r.nav_gimbalTargetErrorDeg),

            new("Att_TiltDeg", cfg => cfg.logAttitudeMetrics, r => r.att_tiltDeg),
            new("Att_Uprightness", cfg => cfg.logAttitudeMetrics, r => r.att_uprightness),
            new("Att_RollDeg", cfg => cfg.logAttitudeMetrics, r => r.att_rollDeg),
            new("Att_PitchDeg", cfg => cfg.logAttitudeMetrics, r => r.att_pitchDeg),
            new("Att_AngularRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_angularRateDegS),
            new("Att_TiltRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_tiltRateDegS),
            new("Att_PitchRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_pitchRateDegS),
            new("Att_YawRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_yawRateDegS),
            new("Att_RollRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_rollRateDegS),
            new("Aero_AoaDeg", cfg => cfg.logAttitudeMetrics, r => r.phys_aoaDeg),

            new("Vel_Speed3D_mps", cfg => cfg.logVelocityMetrics, r => r.vel_speed3D),
            new("Vel_HorizontalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.vel_planarSpeed),
            new("Vel_VerticalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.vel_verticalSpeed),
            new("Vel_ClosureSpeedToGoal_mps", cfg => cfg.logVelocityMetrics, r => r.vel_goalClosureRate),
            new("Vel_HorizontalClosureSpeedToGoal_mps", cfg => cfg.logVelocityMetrics, r => r.vel_horizontalClosureRate),
            new("Vel_AirRelativeSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.phys_speed),

            new("Ctrl_MeanThrottle01", cfg => cfg.logControlMetrics, r => r.ctrl_throttleMean),
            new("Ctrl_MaxThrottle01", cfg => cfg.logControlMetrics, r => r.ctrl_throttleMax),
            new("Ctrl_MeanGimbalDeflectionDeg", cfg => cfg.logControlMetrics, r => r.ctrl_gimbalMeanAbsDeg),
            new("Ctrl_MeanFinDeflectionDeg", cfg => cfg.logControlMetrics, r => r.ctrl_finMeanAbsDeg),
            new("Ctrl_RcsActiveFraction01", cfg => cfg.logControlMetrics, r => r.ctrl_rcsActiveFraction),
            new("RCS_PropellantRemainingKg", cfg => cfg.logControlMetrics, r => r.rcs_propellantKg),
            new("RCS_PropellantRemainingFraction01", cfg => cfg.logControlMetrics, r => r.rcs_propellantFraction),
            new("Fuel_RemainingFraction01", cfg => cfg.logControlMetrics, r => r.fuel_fraction),
            new("Fuel_UsedKg", cfg => cfg.logControlMetrics, r => r.fuel_usedKg),

            new("Load_LoadFactorG", cfg => cfg.logAeroLoadMetrics, r => r.load_gForce),
            new("Load_AngularAccelerationDegS2", cfg => cfg.logAeroLoadMetrics, r => r.load_angularAccelDegS2),
            new("Load_DynamicPressurePa", cfg => cfg.logAeroLoadMetrics, r => r.phys_dynPressure),
            new("Thermal_HeatFluxWm2", cfg => cfg.logAeroLoadMetrics, r => r.sen.HeatFlux),
            new("Thermal_PeakHeatFluxWm2", cfg => cfg.logAeroLoadMetrics, r => r.sen.PeakHeatFlux),
            new("Stress_BendingPa", cfg => cfg.logAeroLoadMetrics, r => r.sen.BendingStress),
            new("Stress_AxialPa", cfg => cfg.logAeroLoadMetrics, r => r.sen.AxialStress),

            new("Env_WindSpeed_mps", cfg => cfg.logEnvironmentMetrics, r => r.env_windSpeed),
            new("Env_HorizontalWindSpeed_mps", cfg => cfg.logEnvironmentMetrics, r => r.env_windPlanarSpeed),
            new("Env_WindVelocityAlignment", cfg => cfg.logEnvironmentMetrics, r => r.env_windAlignment),
            new("Env_NormalizedDynamicPressure01", cfg => cfg.logEnvironmentMetrics, r => r.obs_dynPressNorm),

            new("Reward_StepReward", cfg => cfg.logRewardMetrics, r => r.stepReward),
        };
    }
}
