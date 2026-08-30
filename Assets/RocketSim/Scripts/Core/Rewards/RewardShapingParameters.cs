// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardShapingParameters.cs
// Purpose: Defines feature scales used by reward equations.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Geometry and scale values that normalize reward features without
    /// changing the configured maximum reward magnitudes.
    /// </summary>
    [Serializable]
    public sealed class RewardShapingParameters
    {
        public float landingBallisticDescentFraction;
        public float landingDescentErrorMinimumScaleMps;
        public float landingClosureMinimumScaleMps;
        public float landingPlanarDistanceFalloffM;
        public float landingNearTargetAltitudeFalloffM;
        public float landingPlanarSpeedScaleMps;
        public float landingVerticalSpeedExcessScaleMps;
        public float landingAngularRateScaleDegS;
        public float landingUpwardVelocityToleranceMps;
        public float landingUpwardVelocityScaleMps;
        public float legFuelEfficiencyStartDifficulty;
        public float legFuelEfficiencyFullDifficulty;
        public float legFuelEfficiencyBudgetFraction;
        public float legMissionEfficiencyBudgetFullFraction;
        public float legRestartEquivalentFuelFraction;
        public float legAdditionalEngineIgnitionEquivalentFuelFraction;
        public float legTouchdownQualityRewardFraction;
        public float landingYawErrorScaleDeg;
        public float landingYawSpinScaleDegS;

        public float hoverAltitudeFalloffM;
        public float hoverPlanarFalloffM;
        public float hoverSpeedFalloffMps;
        public float hoverAngularRateFalloffDegS;

        public float trackingTightCenterRadiusFraction;
        public float trackingTightCenterMinimumFalloffM;
        public float trackingHorizontalCalmFalloffMps;
        public float trackingVerticalCalmFalloffMps;
        public float trackingRotationCalmFalloffDegS;
        public float trackingApproachSpeedScaleMps;
        public float trackingDirectionMinimumSpeedMps;
        public float trackingMovingAwaySpeedScaleMps;
        public float trackingFarDistanceScaleMultiplier;
        public float trackingUsefulSpeedScaleMps;
        public float trackingOverspeedThresholdMps;
        public float trackingOverspeedRangeMps;

        /// <summary>Sets every catalogued shaping value to zero.</summary>
        public void Clear()
        {
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                descriptor.SetValue(this, 0f);
        }

        /// <summary>Deep-copies every catalogued shaping value.</summary>
        public void CopyFrom(RewardShapingParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                descriptor.SetValue(this, descriptor.GetValue(source));
        }
    }
}
