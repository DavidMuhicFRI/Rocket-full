// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/TerminationRuleCatalog.cs
// Purpose: Declares scenario-aware episode termination metadata and fresh
// defaults. The evaluator and UI share these bindings, avoiding parallel lists.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    public enum TerminationRuleId
    {
        ChopstickCapturePlane,
        ChopstickUnsafeAttitude,
        ChopstickPlanarFlyaway,
        ChopstickFuelDepletion,
        ChopstickAltitudeCeiling,
        ChopstickTimeLimit,
        LegStableTouchdown,
        LegHardFirstContact,
        LegStructuralStrike,
        LegFootOutsidePad,
        LegExcessiveRebound,
        LegMissedPad,
        LegUnsafeAttitude,
        LegPlanarFlyaway,
        LegFuelDepletion,
        LegAltitudeCeiling,
        LegTimeLimit,
        HoverGroundImpact,
        HoverUnsafeAttitude,
        HoverPlanarFlyaway,
        HoverFuelDepletion,
        HoverAltitudeCeiling,
        HoverTimeLimit,
        TrackingCaptureGoal
    }

    public enum TerminationRuleKind
    {
        Success,
        Failure,
        Limit,
        SuccessOrFailure
    }

    public enum TerminationValueKind
    {
        Scalar,
        Integer,
        Boolean,
        DifficultyRange
    }

    /// <summary>One numeric or Boolean control nested inside a termination row.</summary>
    public sealed class TerminationThresholdDescriptor
    {
        readonly Func<TerminationParameters, float> _getInitial;
        readonly Func<TerminationParameters, float> _getFull;
        readonly Action<TerminationParameters, float> _setInitial;
        readonly Action<TerminationParameters, float> _setFull;

        public string key { get; }
        public string label { get; }
        public string description { get; }
        public string unit { get; }
        public TerminationValueKind valueKind { get; }
        public NumericScaleMode scaleMode { get; }
        public float sliderMinimum { get; }
        public float sliderMaximum { get; }
        public float hardMinimum { get; }
        public float hardMaximum { get; }
        public bool appliesToLinkedEvent { get; }
        public bool usesDifficultyRange => valueKind == TerminationValueKind.DifficultyRange;

        internal TerminationThresholdDescriptor(
            string key, string label, string description, string unit,
            TerminationValueKind valueKind, float sliderMinimum, float sliderMaximum,
            float hardMinimum, float hardMaximum,
            Func<TerminationParameters,float> getInitial,
            Func<TerminationParameters,float> getFull,
            Action<TerminationParameters,float> setInitial,
            Action<TerminationParameters,float> setFull,
            NumericScaleMode scaleMode = NumericScaleMode.Linear,
            bool appliesToLinkedEvent = true)
        {
            this.key = key;
            this.label = label;
            this.description = description;
            this.unit = unit;
            this.valueKind = valueKind;
            this.sliderMinimum = sliderMinimum;
            this.sliderMaximum = sliderMaximum;
            this.hardMinimum = hardMinimum;
            this.hardMaximum = hardMaximum;
            this.scaleMode = scaleMode;
            this.appliesToLinkedEvent = appliesToLinkedEvent;
            _getInitial = getInitial;
            _getFull = getFull;
            _setInitial = setInitial;
            _setFull = setFull;
        }

        public float GetInitialValue(TerminationParameters parameters) => _getInitial(parameters);
        public float GetFullValue(TerminationParameters parameters) => _getFull(parameters);
        public float GetEffectiveValue(TerminationParameters parameters, float difficulty01) =>
            Mathf.Lerp(GetInitialValue(parameters), GetFullValue(parameters), Mathf.Clamp01(difficulty01));
        public void SetInitialValue(TerminationParameters parameters, float value) => _setInitial(parameters, value);
        public void SetFullValue(TerminationParameters parameters, float value) => _setFull(parameters, value);
        public bool GetBoolean(TerminationParameters parameters) => GetInitialValue(parameters) > 0.5f;
        public void SetBoolean(TerminationParameters parameters, bool value)
        {
            SetInitialValue(parameters, value ? 1f : 0f);
            SetFullValue(parameters, value ? 1f : 0f);
        }
    }

    /// <summary>Metadata and typed bindings for one scenario termination rule.</summary>
    public sealed class TerminationRuleDescriptor
    {
        readonly Func<TerminationParameters, bool> _getEnabled;
        readonly Action<TerminationParameters, bool> _setEnabled;

        public TerminationRuleId id { get; }
        public string key { get; }
        public string label { get; }
        public string description { get; }
        public TerminationRuleKind kind { get; }
        public ObjectiveScenarioMask scenarios { get; }
        public IReadOnlyList<RewardParameterId> terminalParameters { get; }
        public IReadOnlyList<TerminationThresholdDescriptor> thresholds { get; }
        public RewardParameterId criteriaEventParameter { get; }
        public bool criteriaAlwaysActive { get; }

        internal TerminationRuleDescriptor(
            TerminationRuleId id, string key, string label, string description,
            TerminationRuleKind kind, ObjectiveScenarioMask scenarios,
            Func<TerminationParameters,bool> getEnabled,
            Action<TerminationParameters,bool> setEnabled,
            RewardParameterId[] terminalParameters,
            TerminationThresholdDescriptor[] thresholds,
            RewardParameterId criteriaEventParameter,
            bool criteriaAlwaysActive)
        {
            this.id = id;
            this.key = key;
            this.label = label;
            this.description = description;
            this.kind = kind;
            this.scenarios = scenarios;
            this.terminalParameters = terminalParameters ?? Array.Empty<RewardParameterId>();
            this.thresholds = thresholds ?? Array.Empty<TerminationThresholdDescriptor>();
            this.criteriaEventParameter = criteriaEventParameter;
            this.criteriaAlwaysActive = criteriaAlwaysActive;
            _getEnabled = getEnabled;
            _setEnabled = setEnabled;
        }

        public bool GetEnabled(TerminationParameters parameters) => _getEnabled(parameters);
        public void SetEnabled(TerminationParameters parameters, bool value) => _setEnabled(parameters, value);
        public bool AppliesTo(ScenarioType scenario) => scenarios.Includes(scenario);
    }

    public static class TerminationRuleCatalog
    {
        static readonly TerminationThresholdDescriptor MaxTilt = Scalar(
            "common.maximum_tilt_deg", "Maximum tilt", "Ends the episode beyond this tilt from upright.", "deg", 1f, 180f,
            p => p.maximumTiltDeg, (p,v) => p.maximumTiltDeg = v);
        static readonly TerminationThresholdDescriptor MaxPlanarDistance = Scalar(
            "common.maximum_planar_distance_m", "Maximum horizontal distance", "Largest recoverable horizontal target error.", "m", 1f, 500f,
            p => p.maximumPlanarDistanceM, (p,v) => p.maximumPlanarDistanceM = v);
        static readonly TerminationThresholdDescriptor MaxAltitudeAboveStart = Scalar(
            "landing.maximum_altitude_above_start_m", "Altitude above start", "Allowed climb above the episode start altitude.", "m", 1f, 500f,
            p => p.maximumAltitudeAboveStartM, (p,v) => p.maximumAltitudeAboveStartM = v);
        static readonly TerminationThresholdDescriptor HoverMaxAltitude = Scalar(
            "hover.maximum_altitude_m", "Maximum altitude", "Absolute local altitude ceiling.", "m", 1f, 1000f,
            p => p.hoverMaximumAltitudeM, (p,v) => p.hoverMaximumAltitudeM = v);
        static readonly TerminationThresholdDescriptor MinFuel = Scalar(
            "common.minimum_fuel_kg", "Minimum usable fuel", "Fuel level at or below which the task ends.", "kg", 0f, 10000f,
            p => p.minimumFuelKg, (p,v) => p.minimumFuelKg = v);
        static readonly TerminationThresholdDescriptor TimeLimit = Scalar(
            "common.maximum_episode_seconds", "Episode time limit", "Maximum simulated duration before timeout.", "s", 1f, 600f,
            p => p.maximumEpisodeSeconds, (p,v) => p.maximumEpisodeSeconds = v);

        static readonly TerminationThresholdDescriptor LandingRadius = Range(
            "landing.success_radius_m", "Target radius", "Maximum horizontal error for successful landing.", "m", 0.1f, 100f,
            p => p.landingSuccessRadiusM, (p,v) => p.landingSuccessRadiusM = v);
        static readonly TerminationThresholdDescriptor LandingTotalSpeed = Range(
            "landing.success_max_total_speed_mps", "Maximum total speed", "Total speed limit for safe contact/capture.", "m/s", 0.1f, 100f,
            p => p.landingSuccessMaxTotalSpeedMps, (p,v) => p.landingSuccessMaxTotalSpeedMps = v);
        static readonly TerminationThresholdDescriptor LandingVerticalSpeed = Range(
            "landing.success_max_vertical_speed_mps", "Maximum vertical speed", "Absolute vertical-speed limit for safe contact/capture.", "m/s", 0.1f, 100f,
            p => p.landingSuccessMaxVerticalSpeedMps, (p,v) => p.landingSuccessMaxVerticalSpeedMps = v);
        static readonly TerminationThresholdDescriptor LandingHorizontalSpeed = Range(
            "landing.success_max_horizontal_speed_mps", "Maximum horizontal speed", "Horizontal-speed limit for safe contact/capture.", "m/s", 0.1f, 100f,
            p => p.landingSuccessMaxHorizontalSpeedMps, (p,v) => p.landingSuccessMaxHorizontalSpeedMps = v);
        static readonly TerminationThresholdDescriptor LandingTilt = Range(
            "landing.success_max_tilt_deg", "Maximum landing tilt", "Tilt limit inside the success envelope.", "deg", 0.1f, 90f,
            p => p.landingSuccessMaxTiltDeg, (p,v) => p.landingSuccessMaxTiltDeg = v);
        static readonly TerminationThresholdDescriptor LandingAngularRate = Range(
            "landing.success_max_angular_rate_deg_s", "Maximum angular rate", "Angular-speed limit inside the success envelope.", "deg/s", 0.1f, 360f,
            p => p.landingSuccessMaxAngularRateDegS, (p,v) => p.landingSuccessMaxAngularRateDegS = v);
        static readonly TerminationThresholdDescriptor LandingYawError = Range(
            "landing.success_max_yaw_error_deg", "Maximum yaw error", "Heading-error limit for chopstick capture.", "deg", 0.1f, 180f,
            p => p.landingSuccessMaxYawErrorDeg, (p,v) => p.landingSuccessMaxYawErrorDeg = v);
        static readonly TerminationThresholdDescriptor LandingStableHold = Range(
            "landing.stable_hold_seconds", "Stable hold", "Continuous stable time required before success.", "s", 0f, 10f,
            p => p.landingStableHoldSeconds, (p,v) => p.landingStableHoldSeconds = v);
        static readonly TerminationThresholdDescriptor RequireStablePlatform = Boolean(
            "chopstick.require_stable_platform", "Require stable capture", "Requires the configured stable hold before capture succeeds.",
            p => p.chopstickRequireStablePlatform, (p,v) => p.chopstickRequireStablePlatform = v);

        static readonly TerminationThresholdDescriptor MinimumStableFeet = Integer(
            "leg.minimum_stable_feet", "Minimum feet on pad", "Number of feet required for stable touchdown.", "feet", 1, 4,
            p => p.legMinimumStableFeet, (p,v) => p.legMinimumStableFeet = v);
        static readonly TerminationThresholdDescriptor ReboundRise = Scalar(
            "leg.maximum_rebound_rise_m", "Maximum rebound rise", "Maximum upward foot-frame travel after first contact.", "m", 0f, 10f,
            p => p.legMaximumReboundRiseM, (p,v) => p.legMaximumReboundRiseM = v);
        static readonly TerminationThresholdDescriptor ContactLoss = Scalar(
            "leg.maximum_contact_loss_seconds", "Maximum all-feet contact loss", "Maximum continuous time with no foot on the pad after contact.", "s", 0f, 5f,
            p => p.legMaximumAllFeetContactLossSeconds, (p,v) => p.legMaximumAllFeetContactLossSeconds = v);
        static readonly TerminationThresholdDescriptor MissedPadDepth = Scalar(
            "leg.missed_pad_depth_m", "Missed-pad depth", "Allowed fall below the foot-contact plane without pad contact.", "m", 0f, 20f,
            p => p.legMissedPadDepthM, (p,v) => p.legMissedPadDepthM = v);
        static readonly TerminationThresholdDescriptor AllowFuelAfterContact = Boolean(
            "leg.allow_fuel_depletion_after_contact", "Allow depletion after contact", "Does not fail fuel depletion once touchdown has started.",
            p => p.legAllowFuelDepletionAfterContact, (p,v) => p.legAllowFuelDepletionAfterContact = v);

        static readonly TerminationThresholdDescriptor GroundClearance = Scalar(
            "hover.ground_clearance_m", "Ground clearance", "Ends below the ground plane plus this clearance.", "m", 0f, 50f,
            p => p.hoverGroundClearanceM, (p,v) => p.hoverGroundClearanceM = v);
        static readonly TerminationThresholdDescriptor RequiredCaptures = Integer(
            "tracking.required_captures", "Required captures", "Number of target captures required for terminal success.", "captures", 1, 100,
            p => p.trackingRequiredCaptures, (p,v) => p.trackingRequiredCaptures = v,
            appliesToLinkedEvent: false);
        static readonly TerminationThresholdDescriptor TrackingRadius = Range(
            "tracking.capture_radius_m", "Capture radius", "Maximum horizontal error while capturing a moving target.", "m", 0.1f, 100f,
            p => p.trackingCaptureRadiusM, (p,v) => p.trackingCaptureRadiusM = v);
        static readonly TerminationThresholdDescriptor TrackingVerticalError = Range(
            "tracking.capture_max_vertical_error_m", "Maximum altitude error", "Absolute altitude-error limit during target capture.", "m", 0.1f, 100f,
            p => p.trackingCaptureMaxVerticalErrorM, (p,v) => p.trackingCaptureMaxVerticalErrorM = v);
        static readonly TerminationThresholdDescriptor TrackingHorizontalSpeed = Range(
            "tracking.capture_max_horizontal_speed_mps", "Maximum horizontal speed", "Horizontal-speed limit during target capture.", "m/s", 0.1f, 50f,
            p => p.trackingCaptureMaxHorizontalSpeedMps, (p,v) => p.trackingCaptureMaxHorizontalSpeedMps = v);
        static readonly TerminationThresholdDescriptor TrackingVerticalSpeed = Range(
            "tracking.capture_max_vertical_speed_mps", "Maximum vertical speed", "Absolute vertical-speed limit during target capture.", "m/s", 0.1f, 50f,
            p => p.trackingCaptureMaxVerticalSpeedMps, (p,v) => p.trackingCaptureMaxVerticalSpeedMps = v);
        static readonly TerminationThresholdDescriptor TrackingTilt = Range(
            "tracking.capture_max_tilt_deg", "Maximum capture tilt", "Tilt limit during target capture.", "deg", 0.1f, 90f,
            p => p.trackingCaptureMaxTiltDeg, (p,v) => p.trackingCaptureMaxTiltDeg = v);
        static readonly TerminationThresholdDescriptor TrackingAngularRate = Range(
            "tracking.capture_max_angular_rate_deg_s", "Maximum angular rate", "Angular-speed limit during target capture.", "deg/s", 0.1f, 360f,
            p => p.trackingCaptureMaxAngularRateDegS, (p,v) => p.trackingCaptureMaxAngularRateDegS = v);
        static readonly TerminationThresholdDescriptor TrackingHold = Range(
            "tracking.capture_hold_seconds", "Capture hold", "Continuous settled time required to capture a target.", "s", 0f, 20f,
            p => p.trackingCaptureHoldSeconds, (p,v) => p.trackingCaptureHoldSeconds = v);

        static readonly TerminationRuleDescriptor[] Descriptors =
        {
            RuleWithEventCriteria(TerminationRuleId.ChopstickCapturePlane, "chopstick.capture_plane", "Capture-plane outcome",
                "Crossing the capture plane ends in success or failed capture.", TerminationRuleKind.SuccessOrFailure,
                ObjectiveScenarioMask.ChopstickLanding, p => p.chopstickCapturePlaneEnabled, (p,v) => p.chopstickCapturePlaneEnabled = v,
                new[]{RewardParameterId.ChopstickSuccessfulCaptureReward, RewardParameterId.ChopstickFailedCaptureCost},
                RewardParameterId.StableCaptureReward, criteriaAlwaysActive: false,
                LandingRadius, LandingTotalSpeed, LandingVerticalSpeed, LandingHorizontalSpeed, LandingTilt,
                LandingAngularRate, LandingYawError, LandingStableHold, RequireStablePlatform),
            CommonFailure(TerminationRuleId.ChopstickUnsafeAttitude, "chopstick.unsafe_attitude", "Unsafe attitude",
                ObjectiveScenarioMask.ChopstickLanding, p => p.unsafeAttitudeEnabled, (p,v) => p.unsafeAttitudeEnabled = v,
                RewardParameterId.ChopstickUnsafeAttitudeCost, MaxTilt),
            CommonFailure(TerminationRuleId.ChopstickPlanarFlyaway, "chopstick.planar_flyaway", "Horizontal flyaway",
                ObjectiveScenarioMask.ChopstickLanding, p => p.planarFlyawayEnabled, (p,v) => p.planarFlyawayEnabled = v,
                RewardParameterId.ChopstickTooFarFromTargetCost, MaxPlanarDistance),
            CommonFailure(TerminationRuleId.ChopstickFuelDepletion, "chopstick.fuel_depletion", "Fuel depletion",
                ObjectiveScenarioMask.ChopstickLanding, p => p.fuelDepletionEnabled, (p,v) => p.fuelDepletionEnabled = v,
                RewardParameterId.ChopstickFuelDepletedCost, MinFuel),
            CommonLimit(TerminationRuleId.ChopstickAltitudeCeiling, "chopstick.altitude_ceiling", "Altitude escape",
                ObjectiveScenarioMask.ChopstickLanding, p => p.altitudeCeilingEnabled, (p,v) => p.altitudeCeilingEnabled = v,
                RewardParameterId.ChopstickAboveAltitudeLimitCost, MaxAltitudeAboveStart),
            CommonLimit(TerminationRuleId.ChopstickTimeLimit, "chopstick.time_limit", "Time limit",
                ObjectiveScenarioMask.ChopstickLanding, p => p.timeLimitEnabled, (p,v) => p.timeLimitEnabled = v,
                RewardParameterId.ChopstickTimeLimitCost, TimeLimit),

            RuleWithEventCriteria(TerminationRuleId.LegStableTouchdown, "leg.stable_touchdown", "Stable touchdown",
                "Ends successfully after safe, stable multi-foot support.", TerminationRuleKind.Success,
                ObjectiveScenarioMask.LegLanding, p => p.legStableTouchdownEnabled, (p,v) => p.legStableTouchdownEnabled = v,
                new[]{RewardParameterId.LegSuccessfulTouchdownReward},
                RewardParameterId.StableTouchdownReward, criteriaAlwaysActive: false,
                LandingRadius, LandingTotalSpeed, LandingVerticalSpeed, LandingHorizontalSpeed, LandingTilt,
                LandingAngularRate, LandingStableHold, MinimumStableFeet),
            Rule(TerminationRuleId.LegHardFirstContact, "leg.hard_first_contact", "Hard first contact",
                "Ends when first-foot contact exceeds any configured motion limit.", TerminationRuleKind.Failure,
                ObjectiveScenarioMask.LegLanding, p => p.legHardFirstContactEnabled, (p,v) => p.legHardFirstContactEnabled = v,
                new[]{RewardParameterId.LegHardTouchdownCost}, LandingTotalSpeed, LandingVerticalSpeed,
                LandingHorizontalSpeed, LandingTilt, LandingAngularRate),
            SimpleFailure(TerminationRuleId.LegStructuralStrike, "leg.structural_strike", "Structural strike",
                p => p.legStructuralStrikeEnabled, (p,v) => p.legStructuralStrikeEnabled = v, RewardParameterId.LegStructuralStrikeCost),
            SimpleFailure(TerminationRuleId.LegFootOutsidePad, "leg.foot_outside_pad", "Foot outside pad",
                p => p.legFootOutsidePadEnabled, (p,v) => p.legFootOutsidePadEnabled = v, RewardParameterId.LegFootOutsidePadCost),
            Rule(TerminationRuleId.LegExcessiveRebound, "leg.excessive_rebound", "Excessive rebound",
                "Ends after excessive rebound or sustained loss of every foot contact.", TerminationRuleKind.Failure,
                ObjectiveScenarioMask.LegLanding, p => p.legExcessiveReboundEnabled, (p,v) => p.legExcessiveReboundEnabled = v,
                new[]{RewardParameterId.LegExcessiveReboundCost}, ReboundRise, ContactLoss),
            Rule(TerminationRuleId.LegMissedPad, "leg.missed_pad", "Missed pad",
                "Ends below the contact plane when no foot has touched the pad.", TerminationRuleKind.Failure,
                ObjectiveScenarioMask.LegLanding, p => p.legMissedPadEnabled, (p,v) => p.legMissedPadEnabled = v,
                new[]{RewardParameterId.LegMissedPadCost}, MissedPadDepth),
            CommonFailure(TerminationRuleId.LegUnsafeAttitude, "leg.unsafe_attitude", "Unsafe attitude",
                ObjectiveScenarioMask.LegLanding, p => p.unsafeAttitudeEnabled, (p,v) => p.unsafeAttitudeEnabled = v,
                RewardParameterId.LegUnsafeAttitudeCost, MaxTilt),
            CommonFailure(TerminationRuleId.LegPlanarFlyaway, "leg.planar_flyaway", "Horizontal flyaway",
                ObjectiveScenarioMask.LegLanding, p => p.planarFlyawayEnabled, (p,v) => p.planarFlyawayEnabled = v,
                RewardParameterId.LegTooFarFromTargetCost, MaxPlanarDistance),
            Rule(TerminationRuleId.LegFuelDepletion, "leg.fuel_depletion", "Fuel depletion",
                "Ends on depleted fuel, optionally only before first contact.", TerminationRuleKind.Failure,
                ObjectiveScenarioMask.LegLanding, p => p.fuelDepletionEnabled, (p,v) => p.fuelDepletionEnabled = v,
                new[]{RewardParameterId.LegFuelDepletedCost}, MinFuel, AllowFuelAfterContact),
            CommonLimit(TerminationRuleId.LegAltitudeCeiling, "leg.altitude_ceiling", "Altitude escape",
                ObjectiveScenarioMask.LegLanding, p => p.altitudeCeilingEnabled, (p,v) => p.altitudeCeilingEnabled = v,
                RewardParameterId.LegAboveAltitudeLimitCost, MaxAltitudeAboveStart),
            CommonLimit(TerminationRuleId.LegTimeLimit, "leg.time_limit", "Time limit",
                ObjectiveScenarioMask.LegLanding, p => p.timeLimitEnabled, (p,v) => p.timeLimitEnabled = v,
                RewardParameterId.LegTimeLimitCost, TimeLimit),

            Rule(TerminationRuleId.HoverGroundImpact, "hover.ground_impact", "Ground impact",
                "Ends below the configured ground-clearance plane.", TerminationRuleKind.Failure,
                ObjectiveScenarioMask.BothHoverTasks, p => p.hoverGroundImpactEnabled, (p,v) => p.hoverGroundImpactEnabled = v,
                new[]{RewardParameterId.HoverGroundImpactCost}, GroundClearance),
            CommonFailure(TerminationRuleId.HoverUnsafeAttitude, "hover.unsafe_attitude", "Unsafe attitude",
                ObjectiveScenarioMask.BothHoverTasks, p => p.unsafeAttitudeEnabled, (p,v) => p.unsafeAttitudeEnabled = v,
                RewardParameterId.HoverUnsafeAttitudeCost, MaxTilt),
            CommonFailure(TerminationRuleId.HoverPlanarFlyaway, "hover.planar_flyaway", "Horizontal flyaway",
                ObjectiveScenarioMask.BothHoverTasks, p => p.planarFlyawayEnabled, (p,v) => p.planarFlyawayEnabled = v,
                RewardParameterId.HoverTooFarFromTargetCost, MaxPlanarDistance),
            CommonFailure(TerminationRuleId.HoverFuelDepletion, "hover.fuel_depletion", "Fuel depletion",
                ObjectiveScenarioMask.BothHoverTasks, p => p.fuelDepletionEnabled, (p,v) => p.fuelDepletionEnabled = v,
                RewardParameterId.HoverFuelDepletedCost, MinFuel),
            CommonLimit(TerminationRuleId.HoverAltitudeCeiling, "hover.altitude_ceiling", "Altitude limit",
                ObjectiveScenarioMask.BothHoverTasks, p => p.altitudeCeilingEnabled, (p,v) => p.altitudeCeilingEnabled = v,
                RewardParameterId.HoverAboveAltitudeLimitCost, HoverMaxAltitude),
            CommonLimit(TerminationRuleId.HoverTimeLimit, "hover.time_limit", "Time limit",
                ObjectiveScenarioMask.BothHoverTasks, p => p.timeLimitEnabled, (p,v) => p.timeLimitEnabled = v,
                RewardParameterId.HoverTimeLimitCost, TimeLimit),
            RuleWithEventCriteria(TerminationRuleId.TrackingCaptureGoal, "tracking.capture_goal", "Capture-count goal",
                "Ends successfully when the requested number of moving targets has been captured.", TerminationRuleKind.Success,
                ObjectiveScenarioMask.HoverTracking, p => p.trackingCaptureGoalEnabled, (p,v) => p.trackingCaptureGoalEnabled = v,
                new[]{RewardParameterId.TrackingCaptureGoalReward},
                RewardParameterId.TrackingTargetCaptureReward, criteriaAlwaysActive: true,
                RequiredCaptures, TrackingRadius,
                TrackingVerticalError, TrackingHorizontalSpeed, TrackingVerticalSpeed, TrackingTilt,
                TrackingAngularRate, TrackingHold)
        };

        public static IReadOnlyList<TerminationRuleDescriptor> All => Descriptors;

        public static IEnumerable<TerminationRuleDescriptor> ForScenario(ScenarioType scenario)
        {
            foreach (TerminationRuleDescriptor descriptor in Descriptors)
                if (descriptor.AppliesTo(scenario))
                    yield return descriptor;
        }

        public static TerminationRuleDescriptor Find(TerminationRuleId id)
        {
            foreach (TerminationRuleDescriptor descriptor in Descriptors)
                if (descriptor.id == id) return descriptor;
            return null;
        }

        static TerminationRuleDescriptor Rule(
            TerminationRuleId id, string key, string label, string description,
            TerminationRuleKind kind, ObjectiveScenarioMask scenarios,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set,
            RewardParameterId[] terminalParameters, params TerminationThresholdDescriptor[] thresholds) =>
            new(id, key, label, description, kind, scenarios, get, set, terminalParameters, thresholds,
                RewardParameterId.None, criteriaAlwaysActive: false);

        static TerminationRuleDescriptor RuleWithEventCriteria(
            TerminationRuleId id, string key, string label, string description,
            TerminationRuleKind kind, ObjectiveScenarioMask scenarios,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set,
            RewardParameterId[] terminalParameters,
            RewardParameterId criteriaEventParameter,
            bool criteriaAlwaysActive,
            params TerminationThresholdDescriptor[] thresholds) =>
            new(id, key, label, description, kind, scenarios, get, set, terminalParameters, thresholds,
                criteriaEventParameter, criteriaAlwaysActive);

        static TerminationRuleDescriptor CommonFailure(
            TerminationRuleId id, string key, string label, ObjectiveScenarioMask scenarios,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set,
            RewardParameterId terminal, params TerminationThresholdDescriptor[] thresholds) =>
            Rule(id, key, label, "Ends the episode when this safety condition is violated.",
                TerminationRuleKind.Failure, scenarios, get, set, new[]{terminal}, thresholds);

        static TerminationRuleDescriptor CommonLimit(
            TerminationRuleId id, string key, string label, ObjectiveScenarioMask scenarios,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set,
            RewardParameterId terminal, params TerminationThresholdDescriptor[] thresholds) =>
            Rule(id, key, label, "Ends the episode when this configured limit is reached.",
                TerminationRuleKind.Limit, scenarios, get, set, new[]{terminal}, thresholds);

        static TerminationRuleDescriptor SimpleFailure(
            TerminationRuleId id, string key, string label,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set,
            RewardParameterId terminal) =>
            Rule(id, key, label, "Ends the episode when this physical contact failure occurs.",
                TerminationRuleKind.Failure, ObjectiveScenarioMask.LegLanding,
                get, set, new[]{terminal});

        static TerminationThresholdDescriptor Scalar(
            string key, string label, string description, string unit, float min, float max,
            Func<TerminationParameters,float> get, Action<TerminationParameters,float> set) =>
            new(key, label, description, unit, TerminationValueKind.Scalar,
                min, max, min, max * 10f, get, get, set, set,
                min > 0f ? NumericScaleMode.LogarithmicWithZero : NumericScaleMode.Linear);

        static TerminationThresholdDescriptor Integer(
            string key, string label, string description, string unit, int min, int max,
            Func<TerminationParameters,int> get, Action<TerminationParameters,int> set,
            bool appliesToLinkedEvent = true) =>
            new(key, label, description, unit, TerminationValueKind.Integer,
                min, max, min, max, p => get(p), p => get(p),
                (p,v) => set(p, Mathf.RoundToInt(v)), (p,v) => set(p, Mathf.RoundToInt(v)),
                appliesToLinkedEvent: appliesToLinkedEvent);

        static TerminationThresholdDescriptor Boolean(
            string key, string label, string description,
            Func<TerminationParameters,bool> get, Action<TerminationParameters,bool> set) =>
            new(key, label, description, string.Empty, TerminationValueKind.Boolean,
                0f, 1f, 0f, 1f, p => get(p) ? 1f : 0f, p => get(p) ? 1f : 0f,
                (p,v) => set(p, v > 0.5f), (p,v) => set(p, v > 0.5f));

        static TerminationThresholdDescriptor Range(
            string key, string label, string description, string unit, float min, float max,
            Func<TerminationParameters,DifficultyRange> get,
            Action<TerminationParameters,DifficultyRange> set) =>
            new(key, label, description, unit, TerminationValueKind.DifficultyRange,
                min, max, min, max * 10f,
                p => get(p).initial, p => get(p).full,
                (p,v) => { DifficultyRange range = get(p); range.initial = v; set(p, range); },
                (p,v) => { DifficultyRange range = get(p); range.full = v; set(p, range); },
                min > 0f ? NumericScaleMode.LogarithmicWithZero : NumericScaleMode.Linear);
    }

    /// <summary>Complete fresh defaults for each supported scenario.</summary>
    public static class TerminationDefaults
    {
        const float LandingAirborneMaximumTiltDeg = 69.5127f; // acos(0.35), expressed for the UI.
        const float HoverMaximumTiltDeg = 63.2563f; // acos(0.45), expressed for the UI.

        public static void Apply(TerminationParameters t, ScenarioType scenario)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            t.Clear();

            if (scenario == ScenarioType.ChopstickLanding || scenario == ScenarioType.LegLanding)
            {
                ApplyLandingCommon(t);
                if (scenario == ScenarioType.ChopstickLanding)
                {
                    t.chopstickCapturePlaneEnabled = true;
                    t.chopstickRequireStablePlatform = true;
                    t.landingStableHoldSeconds = new DifficultyRange(0.15f, 0.45f);
                }
                else
                {
                    t.legStructuralStrikeEnabled = true;
                    t.legFootOutsidePadEnabled = true;
                    t.legHardFirstContactEnabled = true;
                    t.legExcessiveReboundEnabled = true;
                    t.legStableTouchdownEnabled = true;
                    t.legMissedPadEnabled = true;
                    t.legMissedPadDepthM = 2f;
                    t.legMinimumStableFeet = 3;
                    t.legMaximumReboundRiseM = 0.5f;
                    t.legMaximumAllFeetContactLossSeconds = 0.25f;
                    t.legAllowFuelDepletionAfterContact = true;
                    t.landingStableHoldSeconds = new DifficultyRange(0.25f, 1f);
                }
                return;
            }

            if (scenario != ScenarioType.Hover && scenario != ScenarioType.HoverTracking)
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unsupported objective scenario.");

            t.hoverGroundImpactEnabled = true;
            t.hoverGroundClearanceM = 0f;
            t.unsafeAttitudeEnabled = true;
            t.maximumTiltDeg = HoverMaximumTiltDeg;
            t.planarFlyawayEnabled = true;
            t.maximumPlanarDistanceM = scenario == ScenarioType.HoverTracking ? 90f : 80f;
            t.fuelDepletionEnabled = true;
            t.minimumFuelKg = 0f;
            t.altitudeCeilingEnabled = true;
            t.hoverMaximumAltitudeM = 200f;
            t.timeLimitEnabled = false;
            // Disabled rules still retain an immediately usable threshold so
            // enabling the rule never reveals a display/model mismatch.
            t.maximumEpisodeSeconds = 120f;

            if (scenario != ScenarioType.HoverTracking) return;
            t.trackingCaptureGoalEnabled = false;
            t.trackingRequiredCaptures = 1;
            t.trackingCaptureRadiusM = new DifficultyRange(10f, 2.5f);
            t.trackingCaptureMaxVerticalErrorM = new DifficultyRange(3f, 3f);
            t.trackingCaptureMaxHorizontalSpeedMps = new DifficultyRange(3f, 0.8f);
            t.trackingCaptureMaxVerticalSpeedMps = new DifficultyRange(3f, 0.8f);
            t.trackingCaptureMaxTiltDeg = new DifficultyRange(15f, 5f);
            t.trackingCaptureMaxAngularRateDegS = new DifficultyRange(25f, 25f);
            t.trackingCaptureHoldSeconds = new DifficultyRange(0.5f, 2f);
        }

        static void ApplyLandingCommon(TerminationParameters t)
        {
            t.unsafeAttitudeEnabled = true;
            t.maximumTiltDeg = LandingAirborneMaximumTiltDeg;
            t.planarFlyawayEnabled = true;
            t.maximumPlanarDistanceM = 150f;
            t.altitudeCeilingEnabled = true;
            t.maximumAltitudeAboveStartM = 100f;
            t.fuelDepletionEnabled = true;
            t.minimumFuelKg = 0f;
            t.timeLimitEnabled = true;
            t.maximumEpisodeSeconds = 120f;
            t.landingSuccessRadiusM = new DifficultyRange(8f, 2f);
            t.landingSuccessMaxTotalSpeedMps = new DifficultyRange(7f, 2.5f);
            t.landingSuccessMaxVerticalSpeedMps = new DifficultyRange(5f, 2f);
            t.landingSuccessMaxHorizontalSpeedMps = new DifficultyRange(5f, 1f);
            t.landingSuccessMaxTiltDeg = new DifficultyRange(20f, 5f);
            t.landingSuccessMaxAngularRateDegS = new DifficultyRange(50f, 25f);
            t.landingSuccessMaxYawErrorDeg = new DifficultyRange(30f, 10f);
        }
    }
}
