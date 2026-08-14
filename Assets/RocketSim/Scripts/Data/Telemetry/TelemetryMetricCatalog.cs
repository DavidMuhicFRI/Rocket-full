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

            if (cfg.logRewardBreakdown)
                AppendRewardBreakdown(metrics, cfg.scenario);

            // Aggregate actuator values are convenient for plots, but an
            // ablation study also needs to show whether individual engines or
            // fins were actually used. The run-specific slot count prevents
            // irrelevant all-zero columns on smaller vehicles.
            if (cfg.logControlMetrics)
            {
                int engineSlots = Mathf.Clamp(cfg.LoggedEngineSlots, 0, TelemetryConfig.MaxEngines);
                for (int i = 0; i < engineSlots; i++)
                {
                    int slot = i;
                    metrics.Add(new TelemetryMetricDescriptor(
                        $"Ctrl_Engine{slot + 1}_Throttle01",
                        _ => true,
                        row => Read(row.act_throttle, slot)));
                    metrics.Add(new TelemetryMetricDescriptor(
                        $"Ctrl_Engine{slot + 1}_GimbalXDeg",
                        _ => true,
                        row => ReadX(row.act_gimbal, slot)));
                    metrics.Add(new TelemetryMetricDescriptor(
                        $"Ctrl_Engine{slot + 1}_GimbalZDeg",
                        _ => true,
                        row => ReadY(row.act_gimbal, slot)));
                }

                int finSlots = Mathf.Clamp(cfg.activeFinCount, 0, TelemetryConfig.MaxFins);
                for (int i = 0; i < finSlots; i++)
                {
                    int slot = i;
                    metrics.Add(new TelemetryMetricDescriptor(
                        $"Ctrl_Fin{slot + 1}_DeflectionDeg",
                        _ => true,
                        row => Read(row.act_fins, slot)));
                }
            }

            return metrics;
        }

        /// <summary>
        /// Adds diagnostics only for parameters that can affect the selected
        /// scenario. Schema creation happens once per run, so these closures do
        /// not allocate in the physics or telemetry logging loops.
        /// </summary>
        static void AppendRewardBreakdown(
            ICollection<TelemetryMetricDescriptor> metrics,
            ScenarioType scenario)
        {
            IReadOnlyList<RewardParameterDescriptor> descriptors = RewardParameterCatalog.All;
            for (int i = 0; i < descriptors.Count; i++)
            {
                RewardParameterDescriptor descriptor = descriptors[i];
                if (!descriptor.AppliesTo(scenario)) continue;

                RewardParameterId id = descriptor.id;
                string prefix = $"RewardTerm_{descriptor.key.Replace('.', '_')}";
                metrics.Add(new TelemetryMetricDescriptor(
                    $"{prefix}_RawFeature",
                    _ => true,
                    row => row.rewardContributions?.GetRawFeature(id) ?? 0f));
                metrics.Add(new TelemetryMetricDescriptor(
                    $"{prefix}_SignedCoefficient",
                    _ => true,
                    row => row.rewardContributions?.GetSignedCoefficient(id) ?? 0f));
                metrics.Add(new TelemetryMetricDescriptor(
                    $"{prefix}_SignedContribution",
                    _ => true,
                    row => row.rewardContributions?.GetSignedContribution(id) ?? 0f));
            }
        }

        /// <summary>
        /// Counts enabled telemetry metrics using the same catalog that builds
        /// CSV headers, so UI estimates cannot drift from logger output.
        /// </summary>
        public static int CountEnabled(TelemetryConfig cfg)
        {
            return Build(cfg).Count;
        }

        static float Read(float[] values, int index) =>
            values != null && index >= 0 && index < values.Length ? values[index] : 0f;

        static float ReadX(Vector2[] values, int index) =>
            values != null && index >= 0 && index < values.Length ? values[index].x : 0f;

        static float ReadY(Vector2[] values, int index) =>
            values != null && index >= 0 && index < values.Length ? values[index].y : 0f;

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
            new("Chopstick_PlatformRequired01", cfg => cfg.logGoalMetrics, r => r.chopstick_platformRequired01),
            new("Chopstick_InsideCapture01", cfg => cfg.logGoalMetrics, r => r.chopstick_platformInsideCapture01),
            new("Chopstick_StableCapture01", cfg => cfg.logGoalMetrics, r => r.chopstick_platformStable01),
            new("Chopstick_StableTime_s", cfg => cfg.logGoalMetrics, r => r.chopstick_platformStableTime),
            new("Chopstick_HalfSize_m", cfg => cfg.logGoalMetrics, r => r.chopstick_platformHalfSize),
            new("Leg_TouchdownStarted01", cfg => cfg.logGoalMetrics, r => r.leg_touchdownStarted01),
            new("Leg_FirstContactEvent01", cfg => cfg.logGoalMetrics, r => r.leg_firstContactEvent01),
            new("Leg_FeetOnPad", cfg => cfg.logGoalMetrics, r => r.leg_feetOnPad),
            new("Leg_Foot1OnPad01", cfg => cfg.logGoalMetrics, r => r.leg_foot1OnPad01),
            new("Leg_Foot2OnPad01", cfg => cfg.logGoalMetrics, r => r.leg_foot2OnPad01),
            new("Leg_Foot3OnPad01", cfg => cfg.logGoalMetrics, r => r.leg_foot3OnPad01),
            new("Leg_Foot4OnPad01", cfg => cfg.logGoalMetrics, r => r.leg_foot4OnPad01),
            new("Leg_FootOutsidePad01", cfg => cfg.logGoalMetrics, r => r.leg_footOutsidePad01),
            new("Leg_StructuralStrike01", cfg => cfg.logGoalMetrics, r => r.leg_structuralStrike01),
            new("Leg_StableLanding01", cfg => cfg.logGoalMetrics, r => r.leg_stable01),
            new("Leg_StableTime_s", cfg => cfg.logGoalMetrics, r => r.leg_stableTime),
            new("Leg_FirstContactSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.leg_firstContactSpeed),
            new("Leg_FirstContactVerticalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.leg_firstContactVerticalSpeed),
            new("Leg_FirstContactHorizontalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.leg_firstContactHorizontalSpeed),
            new("Leg_FirstContactTilt_deg", cfg => cfg.logAttitudeMetrics, r => r.leg_firstContactTiltDeg),
            new("Leg_FirstContactAngularRate_deg_s", cfg => cfg.logAttitudeMetrics, r => r.leg_firstContactAngularRateDegS),
            new("Leg_MaxContactImpulse_Ns", cfg => cfg.logAeroLoadMetrics, r => r.leg_maxContactImpulseNs),
            new("Leg_MaxReboundHeight_m", cfg => cfg.logGoalMetrics, r => r.leg_maxReboundHeightM),
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
            new("Ctrl_ThrottleSaturatedFraction01", cfg => cfg.logControlMetrics, r => r.ctrl_throttleSaturatedFraction),
            new("Ctrl_GimbalSaturatedFraction01", cfg => cfg.logControlMetrics, r => r.ctrl_gimbalSaturatedFraction),
            new("Ctrl_FinSaturatedFraction01", cfg => cfg.logControlMetrics, r => r.ctrl_finSaturatedFraction),
            new("Ctrl_EngineRestartEvents", cfg => cfg.logControlMetrics, r => r.ctrl_engineRestartEvents),
            new("Ctrl_EngineRestartCount", cfg => cfg.logControlMetrics, r => r.ctrl_engineRestartCount),
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
            new("Reward_ShapingRatePerSecond", cfg => cfg.logRewardMetrics,
                r => r.rewardContributions?.shapingRate ?? 0f),
            new("Reward_Event", cfg => cfg.logRewardMetrics,
                r => r.rewardContributions?.eventReward ?? 0f),
            new("Reward_Terminal", cfg => cfg.logRewardMetrics,
                r => r.rewardContributions?.terminalReward ?? 0f),
        };
    }
}
