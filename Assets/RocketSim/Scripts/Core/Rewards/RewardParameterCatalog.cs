// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardParameterCatalog.cs
// Purpose: Declares stable metadata and typed accessors for every editable
// reward magnitude. UI and telemetry consume this catalog instead of duplicating
// scenario-specific lists or using reflection over serialized fields.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace RocketSim
{
    [Flags]
    public enum ObjectiveScenarioMask
    {
        None = 0,
        ChopstickLanding = 1 << 0,
        LegLanding = 1 << 1,
        Hover = 1 << 2,
        HoverTracking = 1 << 3,
        BothLandings = ChopstickLanding | LegLanding,
        BothHoverTasks = Hover | HoverTracking,
        All = BothLandings | BothHoverTasks
    }

    public enum NumericScaleMode
    {
        Linear,
        LogarithmicWithZero,
        QuadraticWithZero
    }

    public enum RewardParameterSign
    {
        Reward,
        Cost
    }

    public enum RewardCadence
    {
        PerSecond,
        Event,
        Terminal
    }

    public enum RewardParameterCategory
    {
        LandingGuidance,
        HoverStability,
        TargetTracking,
        Control,
        Events,
        TerminalOutcomes
    }

    /// <summary>
    /// Stable identifiers are used by telemetry and tests. Values may be added
    /// at the end; display strings belong in descriptors, not in this enum.
    /// </summary>
    public enum RewardParameterId
    {
        None = 0,
        LandingGoalClosureRewardRate,
        LandingDescentProfileErrorCostRate,
        LandingPlanarDistanceCostRate,
        LandingUprightErrorCostRate,
        LandingNearTargetPlanarSpeedCostRate,
        LandingNearTargetAngularRateCostRate,
        LandingYawErrorCostRate,
        LandingYawSpinCostRate,
        ControlEffortCostRate,
        TimeCostRate,
        StableCaptureReward,
        FirstFootContactReward,
        StableTouchdownReward,
        HoverAltitudeProximityRewardRate,
        HoverPlanarProximityRewardRate,
        HoverUprightRewardRate,
        HoverSpeedCalmRewardRate,
        HoverRotationCalmRewardRate,
        HoverLinearSpeedCostRate,
        HoverAngularRateCostRate,
        EngineRestartCost,
        TrackingSettleCenterRewardRate,
        TrackingSettleHorizontalCalmRewardRate,
        TrackingSettleVerticalCalmRewardRate,
        TrackingSettleRotationCalmRewardRate,
        TrackingSettleCompositeRewardRate,
        TrackingSettlePlanarSpeedCostRate,
        TrackingSettleVerticalSpeedCostRate,
        TrackingApproachClosureRewardRate,
        TrackingApproachDirectionRewardRate,
        TrackingUsefulSpeedRewardRate,
        TrackingMovingAwayCostRate,
        TrackingLoiteringCostRate,
        TrackingOverspeedCostRate,
        TrackingTargetCaptureReward,
        ChopstickSuccessfulCaptureReward,
        ChopstickFailedCaptureCost,
        ChopstickUnsafeAttitudeCost,
        ChopstickTooFarFromTargetCost,
        ChopstickFuelDepletedCost,
        ChopstickAboveAltitudeLimitCost,
        ChopstickTimeLimitCost,
        LegSuccessfulTouchdownReward,
        LegHardTouchdownCost,
        LegStructuralStrikeCost,
        LegFootOutsidePadCost,
        LegExcessiveReboundCost,
        LegUnsafeAttitudeCost,
        LegTooFarFromTargetCost,
        LegFuelDepletedCost,
        LegAboveAltitudeLimitCost,
        LegMissedPadCost,
        LegTimeLimitCost,
        HoverGroundImpactCost,
        HoverUnsafeAttitudeCost,
        HoverFuelDepletedCost,
        HoverTooFarFromTargetCost,
        HoverAboveAltitudeLimitCost,
        HoverTimeLimitCost,
        TrackingCaptureGoalReward,
        LandingNearTargetVerticalSpeedCostRate,
        LandingUpwardVelocityCostRate,
        LegSuccessfulFuelEfficiencyReward,
        LandingPlanarSpeedCostRate,
        LandingAngularRateCostRate,
        LandingReadinessProgressRewardRate,
        LegImpactSeverityCost
    }

    /// <summary>Complete UI/runtime metadata for one reward magnitude.</summary>
    public sealed class RewardParameterDescriptor
    {
        readonly Func<RewardParameters, float> _getter;
        readonly Action<RewardParameters, float> _setter;

        public RewardParameterId id { get; }
        public string key { get; }
        public string label { get; }
        public string description { get; }
        public string unit { get; }
        public RewardParameterSign sign { get; }
        public RewardCadence cadence { get; }
        public RewardParameterCategory category { get; }
        public ObjectiveScenarioMask scenarios { get; }
        public NumericScaleMode scaleMode { get; }
        public float sliderMinimum { get; }
        public float sliderMaximum { get; }
        public float hardMinimum { get; }
        public float hardMaximum { get; }

        internal RewardParameterDescriptor(
            RewardParameterId id,
            string key,
            string label,
            string description,
            RewardParameterSign sign,
            RewardCadence cadence,
            RewardParameterCategory category,
            ObjectiveScenarioMask scenarios,
            float sliderMaximum,
            Func<RewardParameters, float> getter,
            Action<RewardParameters, float> setter,
            string unit = "reward",
            float sliderMinimum = 0f,
            float hardMaximum = 100f,
            NumericScaleMode scaleMode = NumericScaleMode.QuadraticWithZero)
        {
            this.id = id;
            this.key = key;
            this.label = label;
            this.description = description;
            this.sign = sign;
            this.cadence = cadence;
            this.category = category;
            this.scenarios = scenarios;
            this.sliderMinimum = sliderMinimum;
            this.sliderMaximum = sliderMaximum;
            hardMinimum = 0f;
            this.hardMaximum = hardMaximum;
            this.scaleMode = scaleMode;
            this.unit = cadence == RewardCadence.PerSecond ? "reward/s" : unit;
            _getter = getter;
            _setter = setter;
        }

        public float GetValue(RewardParameters parameters) => _getter(parameters);
        public void SetValue(RewardParameters parameters, float value) => _setter(parameters, value);
        public bool AppliesTo(ScenarioType scenario) => scenarios.Includes(scenario);
    }

    public static class RewardParameterCatalog
    {
        const float RateSliderMaximum = 0.25f;
        const float EventSliderMaximum = 5f;
        const float TerminalSliderMaximum = 25f;

        static RewardParameterDescriptor R(
            RewardParameterId id, string key, string label, string description,
            RewardParameterSign sign, RewardCadence cadence,
            RewardParameterCategory category, ObjectiveScenarioMask scenarios,
            Func<RewardParameters, float> get, Action<RewardParameters, float> set,
            float sliderMaximum = RateSliderMaximum) =>
            new(id, key, label, description, sign, cadence, category, scenarios,
                sliderMaximum, get, set);

        static readonly RewardParameterDescriptor[] Descriptors =
        {
            R(RewardParameterId.LandingGoalClosureRewardRate, "landing.goal_closure_reward_rate",
                "Goal closure", "Rewards signed progress toward the landing target.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingGoalClosureRewardRate, (p,v) => p.landingGoalClosureRewardRate = v),
            R(RewardParameterId.LandingDescentProfileErrorCostRate, "landing.descent_profile_error_cost_rate",
                "Descent-profile error", "Costs deviation from the configured ballistic descent-speed curve.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingDescentProfileErrorCostRate, (p,v) => p.landingDescentProfileErrorCostRate = v),
            R(RewardParameterId.LandingPlanarDistanceCostRate, "landing.planar_distance_cost_rate",
                "Horizontal target error", "Costs normalized horizontal distance from the landing target.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingPlanarDistanceCostRate, (p,v) => p.landingPlanarDistanceCostRate = v),
            R(RewardParameterId.LandingUprightErrorCostRate, "landing.upright_error_cost_rate",
                "Upright error", "Costs deviation from an upright attitude.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingUprightErrorCostRate, (p,v) => p.landingUprightErrorCostRate = v),
            R(RewardParameterId.LandingNearTargetPlanarSpeedCostRate, "landing.near_target_planar_speed_cost_rate",
                "Near-target horizontal speed", "Costs horizontal speed increasingly as the vehicle approaches touchdown height.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingNearTargetPlanarSpeedCostRate, (p,v) => p.landingNearTargetPlanarSpeedCostRate = v),
            R(RewardParameterId.LandingNearTargetVerticalSpeedCostRate, "leg.near_target_vertical_speed_cost_rate",
                "Near-target vertical speed", "Costs only vertical speed above the active touchdown limit, faded in near the pad.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingNearTargetVerticalSpeedCostRate, (p,v) => p.landingNearTargetVerticalSpeedCostRate = v),
            R(RewardParameterId.LandingNearTargetAngularRateCostRate, "landing.near_target_angular_rate_cost_rate",
                "Near-target angular rate", "Costs total rotation increasingly near touchdown height.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.BothLandings, p => p.landingNearTargetAngularRateCostRate, (p,v) => p.landingNearTargetAngularRateCostRate = v),
            R(RewardParameterId.LandingUpwardVelocityCostRate, "leg.upward_velocity_cost_rate",
                "Upward velocity", "Costs sustained upward velocity above a configurable tolerance without penalizing downward deceleration.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingUpwardVelocityCostRate, (p,v) => p.landingUpwardVelocityCostRate = v, 2f),
            R(RewardParameterId.LandingPlanarSpeedCostRate, "leg.planar_speed_cost_rate",
                "Horizontal speed", "Costs horizontal speed throughout the descent, without prescribing a horizontal trajectory or engine schedule.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingPlanarSpeedCostRate, (p,v) => p.landingPlanarSpeedCostRate = v),
            R(RewardParameterId.LandingAngularRateCostRate, "leg.angular_rate_cost_rate",
                "Angular rate", "Costs total rotation throughout the descent so deliberate tumbling is never a cheap escape.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingAngularRateCostRate, (p,v) => p.landingAngularRateCostRate = v),
            R(RewardParameterId.LandingReadinessProgressRewardRate, "leg.landing_readiness_progress_reward_rate",
                "Centering progress", "Rewards progress and costs regress in a bounded all-altitude pad-center potential. Descent, attitude, and engine choice are graded separately.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingReadinessProgressRewardRate, (p,v) => p.landingReadinessProgressRewardRate = v, 10f),
            R(RewardParameterId.LandingYawErrorCostRate, "landing.yaw_error_cost_rate",
                "Catch heading error", "Costs yaw misalignment near the chopstick capture plane.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.ChopstickLanding, p => p.landingYawErrorCostRate, (p,v) => p.landingYawErrorCostRate = v),
            R(RewardParameterId.LandingYawSpinCostRate, "landing.yaw_spin_cost_rate",
                "Body-axis spin", "Costs rotation about the rocket's vertical body axis.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.LandingGuidance,
                ObjectiveScenarioMask.LegLanding, p => p.landingYawSpinCostRate, (p,v) => p.landingYawSpinCostRate = v),
            R(RewardParameterId.ControlEffortCostRate, "common.control_effort_cost_rate",
                "Control effort", "Costs throttle, gimbal, fin, and RCS effort measured by the agent.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.Control,
                ObjectiveScenarioMask.All, p => p.controlEffortCostRate, (p,v) => p.controlEffortCostRate = v),
            R(RewardParameterId.TimeCostRate, "landing.time_cost_rate",
                "Elapsed time", "Constant per-second pressure against hovering or delaying an inevitable outcome.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.Control,
                ObjectiveScenarioMask.BothLandings, p => p.timeCostRate, (p,v) => p.timeCostRate = v),

            R(RewardParameterId.StableCaptureReward, "chopstick.stable_capture_reward",
                "Stable capture", "One-off reward when the chopstick capture first becomes stable.",
                RewardParameterSign.Reward, RewardCadence.Event, RewardParameterCategory.Events,
                ObjectiveScenarioMask.ChopstickLanding, p => p.stableCaptureReward, (p,v) => p.stableCaptureReward = v, EventSliderMaximum),
            R(RewardParameterId.FirstFootContactReward, "leg.first_foot_contact_reward",
                "First foot contact", "One-off reward when a landing foot first touches the pad.",
                RewardParameterSign.Reward, RewardCadence.Event, RewardParameterCategory.Events,
                ObjectiveScenarioMask.LegLanding, p => p.firstFootContactReward, (p,v) => p.firstFootContactReward = v, EventSliderMaximum),
            R(RewardParameterId.StableTouchdownReward, "leg.stable_touchdown_reward",
                "Stable support", "One-off reward when four-foot, propulsion-off stable support is first established.",
                RewardParameterSign.Reward, RewardCadence.Event, RewardParameterCategory.Events,
                ObjectiveScenarioMask.LegLanding, p => p.stableTouchdownReward, (p,v) => p.stableTouchdownReward = v, EventSliderMaximum),

            R(RewardParameterId.HoverAltitudeProximityRewardRate, "hover.altitude_proximity_reward_rate",
                "Altitude proximity", "Rewards proximity to the commanded hover altitude.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverAltitudeProximityRewardRate, (p,v) => p.hoverAltitudeProximityRewardRate = v),
            R(RewardParameterId.HoverPlanarProximityRewardRate, "hover.planar_proximity_reward_rate",
                "Horizontal proximity", "Rewards proximity to the horizontal hover target.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverPlanarProximityRewardRate, (p,v) => p.hoverPlanarProximityRewardRate = v),
            R(RewardParameterId.HoverUprightRewardRate, "hover.upright_reward_rate",
                "Upright attitude", "Rewards an upright rocket during hover.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverUprightRewardRate, (p,v) => p.hoverUprightRewardRate = v),
            R(RewardParameterId.HoverSpeedCalmRewardRate, "hover.speed_calm_reward_rate",
                "Low total speed", "Rewards low total linear speed.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverSpeedCalmRewardRate, (p,v) => p.hoverSpeedCalmRewardRate = v),
            R(RewardParameterId.HoverRotationCalmRewardRate, "hover.rotation_calm_reward_rate",
                "Low angular rate", "Rewards calm rotational motion.",
                RewardParameterSign.Reward, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverRotationCalmRewardRate, (p,v) => p.hoverRotationCalmRewardRate = v),
            R(RewardParameterId.HoverLinearSpeedCostRate, "hover.linear_speed_cost_rate",
                "Linear speed", "Unbounded cost proportional to total linear speed.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverLinearSpeedCostRate, (p,v) => p.hoverLinearSpeedCostRate = v),
            R(RewardParameterId.HoverAngularRateCostRate, "hover.angular_rate_cost_rate",
                "Angular rate", "Unbounded cost proportional to angular speed in degrees per second.",
                RewardParameterSign.Cost, RewardCadence.PerSecond, RewardParameterCategory.HoverStability,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverAngularRateCostRate, (p,v) => p.hoverAngularRateCostRate = v),
            R(RewardParameterId.EngineRestartCost, "hover.engine_restart_cost",
                "Engine restart", "One-off cost for each ignition after a previous shutdown.",
                RewardParameterSign.Cost, RewardCadence.Event, RewardParameterCategory.Control,
                ObjectiveScenarioMask.BothHoverTasks, p => p.engineRestartCost, (p,v) => p.engineRestartCost = v, EventSliderMaximum),

            R(RewardParameterId.TrackingSettleCenterRewardRate, "tracking.settle_center_reward_rate", "Settle centering",
                "Rewards tight centering while inside the capture radius.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleCenterRewardRate, (p,v) => p.trackingSettleCenterRewardRate = v),
            R(RewardParameterId.TrackingSettleHorizontalCalmRewardRate, "tracking.settle_horizontal_calm_reward_rate", "Settle horizontal calm",
                "Rewards low horizontal speed while settling.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleHorizontalCalmRewardRate, (p,v) => p.trackingSettleHorizontalCalmRewardRate = v),
            R(RewardParameterId.TrackingSettleVerticalCalmRewardRate, "tracking.settle_vertical_calm_reward_rate", "Settle vertical calm",
                "Rewards low vertical speed while settling.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleVerticalCalmRewardRate, (p,v) => p.trackingSettleVerticalCalmRewardRate = v),
            R(RewardParameterId.TrackingSettleRotationCalmRewardRate, "tracking.settle_rotation_calm_reward_rate", "Settle rotation calm",
                "Rewards low angular speed while settling.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleRotationCalmRewardRate, (p,v) => p.trackingSettleRotationCalmRewardRate = v),
            R(RewardParameterId.TrackingSettleCompositeRewardRate, "tracking.settle_composite_reward_rate", "Complete settled state",
                "Rewards simultaneous centering, calm motion, and upright attitude.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleCompositeRewardRate, (p,v) => p.trackingSettleCompositeRewardRate = v),
            R(RewardParameterId.TrackingSettlePlanarSpeedCostRate, "tracking.settle_planar_speed_cost_rate", "Settle horizontal speed",
                "Costs residual horizontal speed inside the capture radius.", RewardParameterSign.Cost, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettlePlanarSpeedCostRate, (p,v) => p.trackingSettlePlanarSpeedCostRate = v),
            R(RewardParameterId.TrackingSettleVerticalSpeedCostRate, "tracking.settle_vertical_speed_cost_rate", "Settle vertical speed",
                "Costs residual vertical speed inside the capture radius.", RewardParameterSign.Cost, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingSettleVerticalSpeedCostRate, (p,v) => p.trackingSettleVerticalSpeedCostRate = v),
            R(RewardParameterId.TrackingApproachClosureRewardRate, "tracking.approach_closure_reward_rate", "Approach closure",
                "Rewards horizontal speed toward a target outside the capture radius.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingApproachClosureRewardRate, (p,v) => p.trackingApproachClosureRewardRate = v),
            R(RewardParameterId.TrackingApproachDirectionRewardRate, "tracking.approach_direction_reward_rate", "Approach direction",
                "Rewards velocity aligned with the target direction.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingApproachDirectionRewardRate, (p,v) => p.trackingApproachDirectionRewardRate = v),
            R(RewardParameterId.TrackingUsefulSpeedRewardRate, "tracking.useful_speed_reward_rate", "Useful travel speed",
                "Rewards purposeful travel while outside the capture radius.", RewardParameterSign.Reward, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingUsefulSpeedRewardRate, (p,v) => p.trackingUsefulSpeedRewardRate = v),
            R(RewardParameterId.TrackingMovingAwayCostRate, "tracking.moving_away_cost_rate", "Moving away",
                "Costs horizontal motion away from the target.", RewardParameterSign.Cost, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingMovingAwayCostRate, (p,v) => p.trackingMovingAwayCostRate = v),
            R(RewardParameterId.TrackingLoiteringCostRate, "tracking.loitering_cost_rate", "Far-target loitering",
                "Costs staying slow while well outside the capture radius.", RewardParameterSign.Cost, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingLoiteringCostRate, (p,v) => p.trackingLoiteringCostRate = v),
            R(RewardParameterId.TrackingOverspeedCostRate, "tracking.overspeed_cost_rate", "Approach overspeed",
                "Costs travel above the configured useful approach-speed window.", RewardParameterSign.Cost, RewardCadence.PerSecond,
                RewardParameterCategory.TargetTracking, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingOverspeedCostRate, (p,v) => p.trackingOverspeedCostRate = v),
            R(RewardParameterId.TrackingTargetCaptureReward, "tracking.target_capture_reward", "Target capture",
                "One-off reward each time a moving hover target is captured.", RewardParameterSign.Reward, RewardCadence.Event,
                RewardParameterCategory.Events, ObjectiveScenarioMask.HoverTracking,
                p => p.trackingTargetCaptureReward, (p,v) => p.trackingTargetCaptureReward = v, EventSliderMaximum),

            // Each terminal reason has its own magnitude so users can distinguish
            // a recoverable miss from a severe safety violation.
            TerminalReward(RewardParameterId.ChopstickSuccessfulCaptureReward, "chopstick.terminal.success_reward", "Successful capture",
                "Reward for satisfying every enabled capture condition.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickSuccessfulCaptureReward, (p,v) => p.chopstickSuccessfulCaptureReward = v),
            TerminalCost(RewardParameterId.ChopstickFailedCaptureCost, "chopstick.terminal.failed_capture_cost", "Failed capture",
                "Cost for crossing the capture plane outside the success envelope.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickFailedCaptureCost, (p,v) => p.chopstickFailedCaptureCost = v),
            TerminalCost(RewardParameterId.ChopstickUnsafeAttitudeCost, "chopstick.terminal.unsafe_attitude_cost", "Unsafe attitude",
                "Cost for exceeding the airborne tilt limit.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickUnsafeAttitudeCost, (p,v) => p.chopstickUnsafeAttitudeCost = v),
            TerminalCost(RewardParameterId.ChopstickTooFarFromTargetCost, "chopstick.terminal.flyaway_cost", "Horizontal flyaway",
                "Cost for exceeding the horizontal distance limit.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickTooFarFromTargetCost, (p,v) => p.chopstickTooFarFromTargetCost = v),
            TerminalCost(RewardParameterId.ChopstickFuelDepletedCost, "chopstick.terminal.fuel_depleted_cost", "Fuel depleted",
                "Cost for exhausting usable fuel.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickFuelDepletedCost, (p,v) => p.chopstickFuelDepletedCost = v),
            TerminalCost(RewardParameterId.ChopstickAboveAltitudeLimitCost, "chopstick.terminal.altitude_limit_cost", "Altitude escape",
                "Cost for climbing too far above the episode's start altitude.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickAboveAltitudeLimitCost, (p,v) => p.chopstickAboveAltitudeLimitCost = v),
            TerminalCost(RewardParameterId.ChopstickTimeLimitCost, "chopstick.terminal.time_limit_cost", "Time limit",
                "Cost for reaching the configured episode time limit.", ObjectiveScenarioMask.ChopstickLanding,
                p => p.chopstickTimeLimitCost, (p,v) => p.chopstickTimeLimitCost = v),

            TerminalReward(RewardParameterId.LegSuccessfulTouchdownReward, "leg.terminal.success_reward", "Successful touchdown",
                "Reward for sustained, safe four-foot support with propulsion off; touchdown quality and center accuracy can scale this reward.", ObjectiveScenarioMask.LegLanding,
                p => p.legSuccessfulTouchdownReward, (p,v) => p.legSuccessfulTouchdownReward = v),
            // Keep the serialized field, stable identifier, and telemetry key
            // for compatibility; the feature now scores complete mission
            // efficiency rather than clipping on fuel alone.
            TerminalReward(RewardParameterId.LegSuccessfulFuelEfficiencyReward, "leg.terminal.fuel_efficiency_reward", "Successful mission efficiency",
                "Maximum success-only bonus for low propellant use and economical engine switching. Failed attempts never receive this bonus or a switching cost.", ObjectiveScenarioMask.LegLanding,
                p => p.legSuccessfulFuelEfficiencyReward, (p,v) => p.legSuccessfulFuelEfficiencyReward = v),
            TerminalCost(RewardParameterId.LegHardTouchdownCost, "leg.terminal.hard_touchdown_cost", "Hard touchdown",
                "Cost for first-foot contact outside the configured motion envelope.", ObjectiveScenarioMask.LegLanding,
                p => p.legHardTouchdownCost, (p,v) => p.legHardTouchdownCost = v),
            TerminalCost(RewardParameterId.LegImpactSeverityCost, "leg.terminal.impact_severity_cost", "Impact severity",
                "Maximum additional contact-failure cost as impact motion exceeds the active touchdown envelope; applied to hard, outside-pad, structural, and rebound outcomes.", ObjectiveScenarioMask.LegLanding,
                p => p.legImpactSeverityCost, (p,v) => p.legImpactSeverityCost = v),
            TerminalCost(RewardParameterId.LegStructuralStrikeCost, "leg.terminal.structural_strike_cost", "Structural strike",
                "Cost when the body or a non-foot landing-gear part hits the pad.", ObjectiveScenarioMask.LegLanding,
                p => p.legStructuralStrikeCost, (p,v) => p.legStructuralStrikeCost = v),
            TerminalCost(RewardParameterId.LegFootOutsidePadCost, "leg.terminal.foot_outside_pad_cost", "Foot outside pad",
                "Cost when a foot contacts outside the usable pad.", ObjectiveScenarioMask.LegLanding,
                p => p.legFootOutsidePadCost, (p,v) => p.legFootOutsidePadCost = v),
            TerminalCost(RewardParameterId.LegExcessiveReboundCost, "leg.terminal.excessive_rebound_cost", "Excessive rebound",
                "Cost for excessive upward rebound or sustained loss of all foot contacts.", ObjectiveScenarioMask.LegLanding,
                p => p.legExcessiveReboundCost, (p,v) => p.legExcessiveReboundCost = v),
            TerminalCost(RewardParameterId.LegUnsafeAttitudeCost, "leg.terminal.unsafe_attitude_cost", "Unsafe attitude",
                "Cost for exceeding the airborne tilt limit.", ObjectiveScenarioMask.LegLanding,
                p => p.legUnsafeAttitudeCost, (p,v) => p.legUnsafeAttitudeCost = v),
            TerminalCost(RewardParameterId.LegTooFarFromTargetCost, "leg.terminal.flyaway_cost", "Horizontal flyaway",
                "Cost for exceeding the horizontal distance limit.", ObjectiveScenarioMask.LegLanding,
                p => p.legTooFarFromTargetCost, (p,v) => p.legTooFarFromTargetCost = v),
            TerminalCost(RewardParameterId.LegFuelDepletedCost, "leg.terminal.fuel_depleted_cost", "Fuel depleted",
                "Cost for exhausting fuel before supported contact.", ObjectiveScenarioMask.LegLanding,
                p => p.legFuelDepletedCost, (p,v) => p.legFuelDepletedCost = v),
            TerminalCost(RewardParameterId.LegAboveAltitudeLimitCost, "leg.terminal.altitude_limit_cost", "Altitude escape",
                "Cost for climbing too far above the episode's start altitude.", ObjectiveScenarioMask.LegLanding,
                p => p.legAboveAltitudeLimitCost, (p,v) => p.legAboveAltitudeLimitCost = v),
            TerminalCost(RewardParameterId.LegMissedPadCost, "leg.terminal.missed_pad_cost", "Missed pad",
                "Cost for falling below the foot-contact plane without touching the pad.", ObjectiveScenarioMask.LegLanding,
                p => p.legMissedPadCost, (p,v) => p.legMissedPadCost = v),
            TerminalCost(RewardParameterId.LegTimeLimitCost, "leg.terminal.time_limit_cost", "Time limit",
                "Cost for reaching the configured episode time limit.", ObjectiveScenarioMask.LegLanding,
                p => p.legTimeLimitCost, (p,v) => p.legTimeLimitCost = v),

            TerminalCost(RewardParameterId.HoverGroundImpactCost, "hover.terminal.ground_impact_cost", "Ground impact",
                "Cost for crossing the configured ground clearance.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverGroundImpactCost, (p,v) => p.hoverGroundImpactCost = v),
            TerminalCost(RewardParameterId.HoverUnsafeAttitudeCost, "hover.terminal.unsafe_attitude_cost", "Unsafe attitude",
                "Cost for exceeding the hover tilt limit.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverUnsafeAttitudeCost, (p,v) => p.hoverUnsafeAttitudeCost = v),
            TerminalCost(RewardParameterId.HoverFuelDepletedCost, "hover.terminal.fuel_depleted_cost", "Fuel depleted",
                "Cost for exhausting usable fuel.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverFuelDepletedCost, (p,v) => p.hoverFuelDepletedCost = v),
            TerminalCost(RewardParameterId.HoverTooFarFromTargetCost, "hover.terminal.flyaway_cost", "Horizontal flyaway",
                "Cost for exceeding the hover target distance limit.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverTooFarFromTargetCost, (p,v) => p.hoverTooFarFromTargetCost = v),
            TerminalCost(RewardParameterId.HoverAboveAltitudeLimitCost, "hover.terminal.altitude_limit_cost", "Altitude limit",
                "Cost for exceeding the absolute hover altitude limit.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverAboveAltitudeLimitCost, (p,v) => p.hoverAboveAltitudeLimitCost = v),
            TerminalCost(RewardParameterId.HoverTimeLimitCost, "hover.terminal.time_limit_cost", "Time limit",
                "Cost for reaching an optional hover episode time limit.", ObjectiveScenarioMask.BothHoverTasks,
                p => p.hoverTimeLimitCost, (p,v) => p.hoverTimeLimitCost = v),
            TerminalReward(RewardParameterId.TrackingCaptureGoalReward, "tracking.terminal.capture_goal_reward", "Capture goal",
                "Reward for reaching the configured number of target captures.", ObjectiveScenarioMask.HoverTracking,
                p => p.trackingCaptureGoalReward, (p,v) => p.trackingCaptureGoalReward = v)
        };

        public static IReadOnlyList<RewardParameterDescriptor> All => Descriptors;
        public static int ParameterCount => Enum.GetValues(typeof(RewardParameterId)).Length;

        public static IEnumerable<RewardParameterDescriptor> ForScenario(ScenarioType scenario)
        {
            foreach (RewardParameterDescriptor descriptor in Descriptors)
                if (descriptor.AppliesTo(scenario))
                    yield return descriptor;
        }

        public static RewardParameterDescriptor Find(RewardParameterId id)
        {
            foreach (RewardParameterDescriptor descriptor in Descriptors)
                if (descriptor.id == id)
                    return descriptor;
            return null;
        }

        static RewardParameterDescriptor TerminalReward(
            RewardParameterId id, string key, string label, string description,
            ObjectiveScenarioMask scenarios, Func<RewardParameters,float> get,
            Action<RewardParameters,float> set) =>
            R(id, key, label, description, RewardParameterSign.Reward,
                RewardCadence.Terminal, RewardParameterCategory.TerminalOutcomes,
                scenarios, get, set, TerminalSliderMaximum);

        static RewardParameterDescriptor TerminalCost(
            RewardParameterId id, string key, string label, string description,
            ObjectiveScenarioMask scenarios, Func<RewardParameters,float> get,
            Action<RewardParameters,float> set) =>
            R(id, key, label, description, RewardParameterSign.Cost,
                RewardCadence.Terminal, RewardParameterCategory.TerminalOutcomes,
                scenarios, get, set, TerminalSliderMaximum);
    }

    public static class ObjectiveScenarioMaskExtensions
    {
        public static bool Includes(this ObjectiveScenarioMask mask, ScenarioType scenario)
        {
            ObjectiveScenarioMask requested = scenario switch
            {
                ScenarioType.ChopstickLanding => ObjectiveScenarioMask.ChopstickLanding,
                ScenarioType.LegLanding => ObjectiveScenarioMask.LegLanding,
                ScenarioType.Hover => ObjectiveScenarioMask.Hover,
                ScenarioType.HoverTracking => ObjectiveScenarioMask.HoverTracking,
                _ => ObjectiveScenarioMask.None
            };
            return requested != ObjectiveScenarioMask.None && (mask & requested) != 0;
        }
    }
}
