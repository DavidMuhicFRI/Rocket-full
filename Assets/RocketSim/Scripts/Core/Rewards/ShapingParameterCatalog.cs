// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ShapingParameterCatalog.cs
// Purpose: Metadata, typed bindings, and defaults for reward feature geometry.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace RocketSim
{
    public enum ShapingParameterId
    {
        LandingBallisticDescentFraction,
        LandingDescentErrorMinimumScaleMps,
        LandingClosureMinimumScaleMps,
        LandingPlanarDistanceFalloffM,
        LandingNearTargetAltitudeFalloffM,
        LandingPlanarSpeedScaleMps,
        LandingAngularRateScaleDegS,
        LandingYawErrorScaleDeg,
        LandingYawSpinScaleDegS,
        HoverAltitudeFalloffM,
        HoverPlanarFalloffM,
        HoverSpeedFalloffMps,
        HoverAngularRateFalloffDegS,
        TrackingTightCenterRadiusFraction,
        TrackingTightCenterMinimumFalloffM,
        TrackingHorizontalCalmFalloffMps,
        TrackingVerticalCalmFalloffMps,
        TrackingRotationCalmFalloffDegS,
        TrackingApproachSpeedScaleMps,
        TrackingDirectionMinimumSpeedMps,
        TrackingMovingAwaySpeedScaleMps,
        TrackingFarDistanceScaleMultiplier,
        TrackingUsefulSpeedScaleMps,
        TrackingOverspeedThresholdMps,
        TrackingOverspeedRangeMps,
        LandingVerticalSpeedExcessScaleMps,
        LandingUpwardVelocityToleranceMps,
        LandingUpwardVelocityScaleMps,
        LegFuelEfficiencyStartDifficulty,
        LegFuelEfficiencyFullDifficulty,
        LegFuelEfficiencyBudgetFraction,
        LegTouchdownQualityRewardFraction,
        LegMissionEfficiencyBudgetFullFraction,
        LegRestartEquivalentFuelFraction,
        LegAdditionalEngineIgnitionEquivalentFuelFraction
    }

    public sealed class ShapingParameterDescriptor
    {
        readonly Func<RewardShapingParameters, float> _getter;
        readonly Action<RewardShapingParameters, float> _setter;

        public ShapingParameterId id { get; }
        public string key { get; }
        public string label { get; }
        public string description { get; }
        public string unit { get; }
        public ObjectiveScenarioMask scenarios { get; }
        public NumericScaleMode scaleMode { get; }
        public float sliderMinimum { get; }
        public float sliderMaximum { get; }
        public float hardMinimum { get; }
        public float hardMaximum { get; }

        internal ShapingParameterDescriptor(
            ShapingParameterId id, string key, string label, string description,
            string unit, ObjectiveScenarioMask scenarios, float sliderMinimum,
            float sliderMaximum, float hardMinimum, float hardMaximum,
            Func<RewardShapingParameters,float> getter,
            Action<RewardShapingParameters,float> setter,
            NumericScaleMode scaleMode = NumericScaleMode.LogarithmicWithZero)
        {
            this.id = id;
            this.key = key;
            this.label = label;
            this.description = description;
            this.unit = unit;
            this.scenarios = scenarios;
            this.sliderMinimum = sliderMinimum;
            this.sliderMaximum = sliderMaximum;
            this.hardMinimum = hardMinimum;
            this.hardMaximum = hardMaximum;
            this.scaleMode = scaleMode;
            _getter = getter;
            _setter = setter;
        }

        public float GetValue(RewardShapingParameters parameters) => _getter(parameters);
        public void SetValue(RewardShapingParameters parameters, float value) => _setter(parameters, value);
        public bool AppliesTo(ScenarioType scenario) => scenarios.Includes(scenario);
    }

    public static class ShapingParameterCatalog
    {
        static ShapingParameterDescriptor D(
            ShapingParameterId id, string key, string label, string description,
            string unit, ObjectiveScenarioMask scenarios, float min, float max,
            Func<RewardShapingParameters,float> get,
            Action<RewardShapingParameters,float> set,
            float hardMax = 1000f,
            NumericScaleMode scale = NumericScaleMode.LogarithmicWithZero) =>
            new(id, key, label, description, unit, scenarios, min, max,
                min, hardMax, get, set, scale);

        static readonly ShapingParameterDescriptor[] Descriptors =
        {
            D(ShapingParameterId.LandingBallisticDescentFraction, "landing.shape.ballistic_descent_fraction",
                "Ballistic descent fraction", "Fraction of local free-fall speed used as the target descent curve.", "ratio",
                ObjectiveScenarioMask.BothLandings, 0.01f, 1f,
                p => p.landingBallisticDescentFraction, (p,v) => p.landingBallisticDescentFraction = v,
                2f, NumericScaleMode.Linear),
            D(ShapingParameterId.LandingDescentErrorMinimumScaleMps, "landing.shape.descent_error_minimum_scale_mps",
                "Minimum descent-error scale", "Prevents the normalized descent error becoming singular near the ground.", "m/s",
                ObjectiveScenarioMask.BothLandings, 0.1f, 20f,
                p => p.landingDescentErrorMinimumScaleMps, (p,v) => p.landingDescentErrorMinimumScaleMps = v),
            D(ShapingParameterId.LandingClosureMinimumScaleMps, "landing.shape.closure_minimum_scale_mps",
                "Minimum closure scale", "Minimum speed scale used to normalize signed progress toward the goal.", "m/s",
                ObjectiveScenarioMask.BothLandings, 0.1f, 20f,
                p => p.landingClosureMinimumScaleMps, (p,v) => p.landingClosureMinimumScaleMps = v),
            D(ShapingParameterId.LandingPlanarDistanceFalloffM, "landing.shape.planar_distance_falloff_m",
                "Horizontal-error falloff", "Distance over which horizontal target error approaches its maximum cost.", "m",
                ObjectiveScenarioMask.BothLandings, 0.1f, 100f,
                p => p.landingPlanarDistanceFalloffM, (p,v) => p.landingPlanarDistanceFalloffM = v),
            D(ShapingParameterId.LandingNearTargetAltitudeFalloffM, "landing.shape.near_target_altitude_falloff_m",
                "Near-target altitude falloff", "Altitude scale that fades in touchdown speed and rotation costs.", "m",
                ObjectiveScenarioMask.BothLandings, 0.1f, 200f,
                p => p.landingNearTargetAltitudeFalloffM, (p,v) => p.landingNearTargetAltitudeFalloffM = v),
            D(ShapingParameterId.LandingPlanarSpeedScaleMps, "landing.shape.planar_speed_scale_mps",
                "Horizontal-speed scale", "Horizontal speed represented by a fully saturated touchdown-speed feature.", "m/s",
                ObjectiveScenarioMask.BothLandings, 0.1f, 50f,
                p => p.landingPlanarSpeedScaleMps, (p,v) => p.landingPlanarSpeedScaleMps = v),
            D(ShapingParameterId.LandingVerticalSpeedExcessScaleMps, "leg.shape.vertical_speed_excess_scale_mps",
                "Vertical overspeed scale", "Vertical speed above the active touchdown limit represented by a saturated near-pad feature.", "m/s",
                ObjectiveScenarioMask.LegLanding, 0.1f, 30f,
                p => p.landingVerticalSpeedExcessScaleMps, (p,v) => p.landingVerticalSpeedExcessScaleMps = v),
            D(ShapingParameterId.LandingAngularRateScaleDegS, "landing.shape.angular_rate_scale_deg_s",
                "Angular-rate scale", "Angular speed represented by a fully saturated landing rotation feature.", "deg/s",
                ObjectiveScenarioMask.BothLandings, 1f, 180f,
                p => p.landingAngularRateScaleDegS, (p,v) => p.landingAngularRateScaleDegS = v),
            D(ShapingParameterId.LandingUpwardVelocityToleranceMps, "leg.shape.upward_velocity_tolerance_mps",
                "Upward-speed tolerance", "Upward speed allowed before the climb cost begins.", "m/s",
                ObjectiveScenarioMask.LegLanding, 0f, 10f,
                p => p.landingUpwardVelocityToleranceMps, (p,v) => p.landingUpwardVelocityToleranceMps = v,
                50f, NumericScaleMode.Linear),
            D(ShapingParameterId.LandingUpwardVelocityScaleMps, "leg.shape.upward_velocity_scale_mps",
                "Upward-speed scale", "Upward speed above tolerance represented by a saturated climb feature.", "m/s",
                ObjectiveScenarioMask.LegLanding, 0.1f, 50f,
                p => p.landingUpwardVelocityScaleMps, (p,v) => p.landingUpwardVelocityScaleMps = v),
            D(ShapingParameterId.LegFuelEfficiencyStartDifficulty, "leg.shape.fuel_efficiency_start_difficulty",
                "Mission-efficiency start", "Curriculum difficulty below which successful landings receive no mission-efficiency bonus.", "ratio",
                ObjectiveScenarioMask.LegLanding, 0f, 1f,
                p => p.legFuelEfficiencyStartDifficulty, (p,v) => p.legFuelEfficiencyStartDifficulty = v,
                1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegFuelEfficiencyFullDifficulty, "leg.shape.fuel_efficiency_full_difficulty",
                "Mission-efficiency full", "Curriculum difficulty at which the complete successful-landing mission-efficiency bonus is active.", "ratio",
                ObjectiveScenarioMask.LegLanding, 0f, 1f,
                p => p.legFuelEfficiencyFullDifficulty, (p,v) => p.legFuelEfficiencyFullDifficulty = v,
                1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegFuelEfficiencyBudgetFraction, "leg.shape.fuel_efficiency_budget_fraction",
                "Initial mission-cost scale", "Exponential cost scale at initial difficulty. Mission cost is fuel used plus equivalent-fuel charges for switching engines; this is a smooth normalization scale, not a hard budget.", "ratio",
                ObjectiveScenarioMask.LegLanding, 0.01f, 0.50f,
                p => p.legFuelEfficiencyBudgetFraction, (p,v) => p.legFuelEfficiencyBudgetFraction = v,
                1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegTouchdownQualityRewardFraction, "leg.shape.touchdown_quality_reward_fraction",
                "Quality-weighted success", "Fraction of the successful-touchdown reward scaled by continuous first-contact speed, uprightness, and rotation quality. The remainder is guaranteed by completing a legal landing.", "ratio",
                ObjectiveScenarioMask.LegLanding, 0f, 1f,
                p => p.legTouchdownQualityRewardFraction, (p,v) => p.legTouchdownQualityRewardFraction = v,
                1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegMissionEfficiencyBudgetFullFraction, "leg.shape.mission_efficiency_cost_scale_full_fraction",
                "Full mission-cost scale", "Exponential mission-cost scale at full curriculum difficulty. A larger value normalizes for longer and harder trajectories without changing the incentive to reduce cost.", "ratio",
                ObjectiveScenarioMask.LegLanding, 0.01f, 0.50f,
                p => p.legMissionEfficiencyBudgetFullFraction, (p,v) => p.legMissionEfficiencyBudgetFullFraction = v,
                1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegRestartEquivalentFuelFraction, "leg.shape.restart_equivalent_fuel_fraction",
                "Restart equivalent fuel", "Equivalent fraction of starting fuel added to mission cost for each ignition after that engine channel previously shut down.", "ratio/restart",
                ObjectiveScenarioMask.LegLanding, 0f, 0.02f,
                p => p.legRestartEquivalentFuelFraction, (p,v) => p.legRestartEquivalentFuelFraction = v,
                0.1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LegAdditionalEngineIgnitionEquivalentFuelFraction, "leg.shape.additional_engine_ignition_equivalent_fuel_fraction",
                "Additional first-ignition fuel", "Small equivalent-fuel charge for each additional engine channel's first ignition. This keeps a three-engine braking burn cheap compared with repeated relights.", "ratio/engine",
                ObjectiveScenarioMask.LegLanding, 0f, 0.005f,
                p => p.legAdditionalEngineIgnitionEquivalentFuelFraction, (p,v) => p.legAdditionalEngineIgnitionEquivalentFuelFraction = v,
                0.1f, NumericScaleMode.Linear),
            D(ShapingParameterId.LandingYawErrorScaleDeg, "landing.shape.yaw_error_scale_deg",
                "Yaw-error scale", "Heading error represented by a fully saturated chopstick yaw feature.", "deg",
                ObjectiveScenarioMask.ChopstickLanding, 1f, 180f,
                p => p.landingYawErrorScaleDeg, (p,v) => p.landingYawErrorScaleDeg = v),
            D(ShapingParameterId.LandingYawSpinScaleDegS, "landing.shape.yaw_spin_scale_deg_s",
                "Body-axis spin scale", "Body-axis spin represented by a fully saturated leg-landing spin feature.", "deg/s",
                ObjectiveScenarioMask.LegLanding, 1f, 180f,
                p => p.landingYawSpinScaleDegS, (p,v) => p.landingYawSpinScaleDegS = v),

            D(ShapingParameterId.HoverAltitudeFalloffM, "hover.shape.altitude_falloff_m",
                "Altitude falloff", "Exponential falloff distance for altitude proximity.", "m",
                ObjectiveScenarioMask.BothHoverTasks, 0.1f, 50f,
                p => p.hoverAltitudeFalloffM, (p,v) => p.hoverAltitudeFalloffM = v),
            D(ShapingParameterId.HoverPlanarFalloffM, "hover.shape.planar_falloff_m",
                "Horizontal falloff", "Exponential falloff distance for target proximity.", "m",
                ObjectiveScenarioMask.BothHoverTasks, 0.1f, 100f,
                p => p.hoverPlanarFalloffM, (p,v) => p.hoverPlanarFalloffM = v),
            D(ShapingParameterId.HoverSpeedFalloffMps, "hover.shape.speed_falloff_mps",
                "Speed falloff", "Exponential falloff speed for calm-motion reward.", "m/s",
                ObjectiveScenarioMask.BothHoverTasks, 0.1f, 30f,
                p => p.hoverSpeedFalloffMps, (p,v) => p.hoverSpeedFalloffMps = v),
            D(ShapingParameterId.HoverAngularRateFalloffDegS, "hover.shape.angular_rate_falloff_deg_s",
                "Angular-rate falloff", "Exponential falloff rate for rotational-calm reward.", "deg/s",
                ObjectiveScenarioMask.BothHoverTasks, 1f, 180f,
                p => p.hoverAngularRateFalloffDegS, (p,v) => p.hoverAngularRateFalloffDegS = v),

            D(ShapingParameterId.TrackingTightCenterRadiusFraction, "tracking.shape.tight_center_radius_fraction",
                "Tight-center radius fraction", "Sets tight-center falloff relative to the active capture radius.", "ratio",
                ObjectiveScenarioMask.HoverTracking, 0.01f, 2f,
                p => p.trackingTightCenterRadiusFraction, (p,v) => p.trackingTightCenterRadiusFraction = v,
                10f, NumericScaleMode.Linear),
            D(ShapingParameterId.TrackingTightCenterMinimumFalloffM, "tracking.shape.tight_center_minimum_falloff_m",
                "Minimum tight-center falloff", "Lower bound for tight-center reward falloff.", "m",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 20f,
                p => p.trackingTightCenterMinimumFalloffM, (p,v) => p.trackingTightCenterMinimumFalloffM = v),
            D(ShapingParameterId.TrackingHorizontalCalmFalloffMps, "tracking.shape.horizontal_calm_falloff_mps",
                "Horizontal-calm falloff", "Exponential horizontal-speed scale while settling.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 20f,
                p => p.trackingHorizontalCalmFalloffMps, (p,v) => p.trackingHorizontalCalmFalloffMps = v),
            D(ShapingParameterId.TrackingVerticalCalmFalloffMps, "tracking.shape.vertical_calm_falloff_mps",
                "Vertical-calm falloff", "Exponential vertical-speed scale while settling.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 20f,
                p => p.trackingVerticalCalmFalloffMps, (p,v) => p.trackingVerticalCalmFalloffMps = v),
            D(ShapingParameterId.TrackingRotationCalmFalloffDegS, "tracking.shape.rotation_calm_falloff_deg_s",
                "Settle rotation falloff", "Exponential angular-speed scale while settling.", "deg/s",
                ObjectiveScenarioMask.HoverTracking, 1f, 180f,
                p => p.trackingRotationCalmFalloffDegS, (p,v) => p.trackingRotationCalmFalloffDegS = v),
            D(ShapingParameterId.TrackingApproachSpeedScaleMps, "tracking.shape.approach_speed_scale_mps",
                "Approach closure scale", "Closure speed represented by a saturated approach feature.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 30f,
                p => p.trackingApproachSpeedScaleMps, (p,v) => p.trackingApproachSpeedScaleMps = v),
            D(ShapingParameterId.TrackingDirectionMinimumSpeedMps, "tracking.shape.direction_minimum_speed_mps",
                "Direction minimum speed", "Minimum speed required before velocity direction is meaningful.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.001f, 5f,
                p => p.trackingDirectionMinimumSpeedMps, (p,v) => p.trackingDirectionMinimumSpeedMps = v),
            D(ShapingParameterId.TrackingMovingAwaySpeedScaleMps, "tracking.shape.moving_away_speed_scale_mps",
                "Moving-away scale", "Away speed represented by a saturated moving-away feature.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 30f,
                p => p.trackingMovingAwaySpeedScaleMps, (p,v) => p.trackingMovingAwaySpeedScaleMps = v),
            D(ShapingParameterId.TrackingFarDistanceScaleMultiplier, "tracking.shape.far_distance_scale_multiplier",
                "Far-distance scale", "Multiplies capture radius when normalizing distance beyond the settle region.", "ratio",
                ObjectiveScenarioMask.HoverTracking, 0.01f, 10f,
                p => p.trackingFarDistanceScaleMultiplier, (p,v) => p.trackingFarDistanceScaleMultiplier = v,
                100f, NumericScaleMode.Linear),
            D(ShapingParameterId.TrackingUsefulSpeedScaleMps, "tracking.shape.useful_speed_scale_mps",
                "Useful-speed scale", "Travel speed represented by a saturated useful-speed feature.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 30f,
                p => p.trackingUsefulSpeedScaleMps, (p,v) => p.trackingUsefulSpeedScaleMps = v),
            D(ShapingParameterId.TrackingOverspeedThresholdMps, "tracking.shape.overspeed_threshold_mps",
                "Overspeed threshold", "Approach speed at which the overspeed cost begins.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0f, 30f,
                p => p.trackingOverspeedThresholdMps, (p,v) => p.trackingOverspeedThresholdMps = v,
                100f, NumericScaleMode.Linear),
            D(ShapingParameterId.TrackingOverspeedRangeMps, "tracking.shape.overspeed_range_mps",
                "Overspeed saturation range", "Speed above the threshold required to saturate the overspeed feature.", "m/s",
                ObjectiveScenarioMask.HoverTracking, 0.1f, 30f,
                p => p.trackingOverspeedRangeMps, (p,v) => p.trackingOverspeedRangeMps = v)
        };

        public static IReadOnlyList<ShapingParameterDescriptor> All => Descriptors;

        public static IEnumerable<ShapingParameterDescriptor> ForScenario(ScenarioType scenario)
        {
            foreach (ShapingParameterDescriptor descriptor in Descriptors)
                if (descriptor.AppliesTo(scenario))
                    yield return descriptor;
        }
    }

    /// <summary>Balanced reward feature geometry, independent of reward presets.</summary>
    public static class RewardShapingDefaults
    {
        public static void Apply(RewardShapingParameters shaping, ScenarioType scenario)
        {
            if (shaping == null) throw new ArgumentNullException(nameof(shaping));
            shaping.Clear();

            if (scenario == ScenarioType.ChopstickLanding || scenario == ScenarioType.LegLanding)
            {
                shaping.landingBallisticDescentFraction = 0.30f;
                shaping.landingDescentErrorMinimumScaleMps = 5f;
                shaping.landingClosureMinimumScaleMps = 5f;
                shaping.landingPlanarDistanceFalloffM = 25f;
                shaping.landingNearTargetAltitudeFalloffM = 50f;
                shaping.landingPlanarSpeedScaleMps = 5f;
                shaping.landingAngularRateScaleDegS = 60f;
                if (scenario == ScenarioType.ChopstickLanding)
                    shaping.landingYawErrorScaleDeg = 45f;
                else
                {
                    // Terminal quality fades in before touchdown while
                    // horizontal navigation remains a separate all-altitude
                    // objective in the leg reward model.
                    shaping.landingNearTargetAltitudeFalloffM = 75f;
                    shaping.landingVerticalSpeedExcessScaleMps = 5f;
                    shaping.landingUpwardVelocityToleranceMps = 0.5f;
                    shaping.landingUpwardVelocityScaleMps = 3f;
                    // This is a small success-only tie-breaker, never an action
                    // restriction or a reward available to failed attempts.
                    // Fuel dominates. Restarts are appreciably costly, while
                    // lighting the remaining braking engines once is cheap.
                    shaping.legFuelEfficiencyStartDifficulty = 0f;
                    shaping.legFuelEfficiencyFullDifficulty = 0f;
                    shaping.legFuelEfficiencyBudgetFraction = 0.20f;
                    shaping.legMissionEfficiencyBudgetFullFraction = 0.28f;
                    shaping.legRestartEquivalentFuelFraction = 0.003f;
                    shaping.legAdditionalEngineIgnitionEquivalentFuelFraction = 0.0005f;
                    // A legal boundary landing retains 25% of the success
                    // reward; the remaining 75% grades smoothness and center.
                    shaping.legTouchdownQualityRewardFraction = 0.75f;
                    shaping.landingYawSpinScaleDegS = 20f;
                }
                return;
            }

            if (scenario != ScenarioType.Hover && scenario != ScenarioType.HoverTracking)
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unsupported objective scenario.");

            shaping.hoverAltitudeFalloffM = 5f;
            shaping.hoverPlanarFalloffM = 8f;
            shaping.hoverSpeedFalloffMps = 4f;
            shaping.hoverAngularRateFalloffDegS = 35f;

            if (scenario != ScenarioType.HoverTracking) return;
            shaping.trackingTightCenterRadiusFraction = 0.5f;
            shaping.trackingTightCenterMinimumFalloffM = 2f;
            shaping.trackingHorizontalCalmFalloffMps = 1.8f;
            shaping.trackingVerticalCalmFalloffMps = 1.4f;
            shaping.trackingRotationCalmFalloffDegS = 28f;
            shaping.trackingApproachSpeedScaleMps = 5f;
            shaping.trackingDirectionMinimumSpeedMps = 0.1f;
            shaping.trackingMovingAwaySpeedScaleMps = 4f;
            shaping.trackingFarDistanceScaleMultiplier = 1f;
            shaping.trackingUsefulSpeedScaleMps = 5f;
            shaping.trackingOverspeedThresholdMps = 8f;
            shaping.trackingOverspeedRangeMps = 6f;
        }
    }
}
