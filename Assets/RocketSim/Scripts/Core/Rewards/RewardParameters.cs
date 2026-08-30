// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardParameters.cs
// Purpose: Defines the user-editable magnitudes for all reward signals.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Absolute nonnegative magnitudes. Rates are integrated over simulated
    /// seconds; event and terminal fields are applied once. Costs are stored as
    /// positive values and negated by the evaluator.
    /// </summary>
    [Serializable]
    public sealed class RewardParameters
    {
        // Landing guidance rates.
        public float landingGoalClosureRewardRate;
        public float landingDescentProfileErrorCostRate;
        public float landingPlanarDistanceCostRate;
        public float landingUprightErrorCostRate;
        public float landingNearTargetPlanarSpeedCostRate;
        public float landingNearTargetVerticalSpeedCostRate;
        public float landingNearTargetAngularRateCostRate;
        public float landingUpwardVelocityCostRate;
        public float landingPlanarSpeedCostRate;
        public float landingAngularRateCostRate;
        public float landingYawErrorCostRate;
        public float landingYawSpinCostRate;
        public float landingReadinessProgressRewardRate;
        public float controlEffortCostRate;
        public float timeCostRate;

        // Landing one-off events.
        public float stableCaptureReward;
        public float firstFootContactReward;
        public float stableTouchdownReward;

        // Hover stability rates and event costs.
        public float hoverAltitudeProximityRewardRate;
        public float hoverPlanarProximityRewardRate;
        public float hoverUprightRewardRate;
        public float hoverSpeedCalmRewardRate;
        public float hoverRotationCalmRewardRate;
        public float hoverLinearSpeedCostRate;
        public float hoverAngularRateCostRate;
        public float engineRestartCost;

        // Moving-target hover rates and capture events.
        public float trackingSettleCenterRewardRate;
        public float trackingSettleHorizontalCalmRewardRate;
        public float trackingSettleVerticalCalmRewardRate;
        public float trackingSettleRotationCalmRewardRate;
        public float trackingSettleCompositeRewardRate;
        public float trackingSettlePlanarSpeedCostRate;
        public float trackingSettleVerticalSpeedCostRate;
        public float trackingApproachClosureRewardRate;
        public float trackingApproachDirectionRewardRate;
        public float trackingUsefulSpeedRewardRate;
        public float trackingMovingAwayCostRate;
        public float trackingLoiteringCostRate;
        public float trackingOverspeedCostRate;
        public float trackingTargetCaptureReward;

        // Chopstick terminal outcomes.
        public float chopstickSuccessfulCaptureReward;
        public float chopstickFailedCaptureCost;
        public float chopstickUnsafeAttitudeCost;
        public float chopstickTooFarFromTargetCost;
        public float chopstickFuelDepletedCost;
        public float chopstickAboveAltitudeLimitCost;
        public float chopstickTimeLimitCost;

        // Leg-landing terminal outcomes.
        public float legSuccessfulTouchdownReward;
        public float legSuccessfulFuelEfficiencyReward;
        public float legHardTouchdownCost;
        public float legImpactSeverityCost;
        public float legStructuralStrikeCost;
        public float legFootOutsidePadCost;
        public float legExcessiveReboundCost;
        public float legUnsafeAttitudeCost;
        public float legTooFarFromTargetCost;
        public float legFuelDepletedCost;
        public float legAboveAltitudeLimitCost;
        public float legMissedPadCost;
        public float legTimeLimitCost;

        // Hover and hover-tracking terminal outcomes.
        public float hoverGroundImpactCost;
        public float hoverUnsafeAttitudeCost;
        public float hoverFuelDepletedCost;
        public float hoverTooFarFromTargetCost;
        public float hoverAboveAltitudeLimitCost;
        public float hoverTimeLimitCost;
        public float trackingCaptureGoalReward;

        /// <summary>Sets every catalogued signal to zero.</summary>
        public void Clear()
        {
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                descriptor.SetValue(this, 0f);
        }

        /// <summary>Deep-copies every catalogued signal.</summary>
        public void CopyFrom(RewardParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                descriptor.SetValue(this, descriptor.GetValue(source));
        }
    }
}
