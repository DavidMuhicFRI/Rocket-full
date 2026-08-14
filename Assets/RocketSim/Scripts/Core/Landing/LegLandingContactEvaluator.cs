// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LegLandingContactEvaluator.cs
// Purpose: Contains pure, testable rules for safe first contact and stable
// multi-foot pad contact.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static class LegLandingContactEvaluator
    {
        public static int CountFeet(int footMask)
        {
            int count = 0;
            for (int i = 0; i < LandingLegAssembly.LegCount; i++)
                if ((footMask & (1 << i)) != 0)
                    count++;
            return count;
        }

        public static bool IsFirstContactSafe(
            float totalSpeed,
            float verticalSpeed,
            float horizontalSpeed,
            float tiltDeg,
            float angularRateDegS,
            LandingCurriculumProfile profile)
        {
            return totalSpeed <= profile.successMaxSpeed &&
                   Mathf.Abs(verticalSpeed) <= profile.successMaxVerticalSpeed &&
                   horizontalSpeed <= profile.successMaxHorizontalSpeed &&
                   tiltDeg <= profile.successMaxTiltDeg &&
                   angularRateDegS <= profile.successMaxAngularRateDegS;
        }

        /// <summary>
        /// Rejects a touchdown that launches the feet back into the air. The
        /// height is upward travel relative to first contact, not absolute pad
        /// clearance, so an initially tilted foot contact is not misclassified.
        /// </summary>
        public static bool IsExcessiveRebound(
            float reboundRiseM,
            float allFeetContactLossSeconds,
            float maximumReboundRiseM,
            float maximumAllFeetContactLossSeconds)
        {
            return reboundRiseM > Mathf.Max(0f, maximumReboundRiseM) ||
                   allFeetContactLossSeconds > Mathf.Max(0f, maximumAllFeetContactLossSeconds);
        }

        public static bool IsStableCandidate(
            int footMask,
            bool footOutsidePad,
            bool structuralStrike,
            RewardTerms terms,
            float tiltDeg,
            LandingCurriculumProfile profile,
            int minimumStableFeet)
        {
            int requiredFeet = Mathf.Clamp(minimumStableFeet, 1, LandingLegAssembly.LegCount);
            return CountFeet(footMask) >= requiredFeet &&
                   !footOutsidePad &&
                   !structuralStrike &&
                   terms.planarDistance <= profile.successRadius &&
                   terms.speed <= profile.successMaxSpeed &&
                   Mathf.Abs(terms.verticalSpeed) <= profile.successMaxVerticalSpeed &&
                   terms.planarSpeed <= profile.successMaxHorizontalSpeed &&
                   tiltDeg <= profile.successMaxTiltDeg &&
                   terms.angularRateDegS <= profile.successMaxAngularRateDegS;
        }
    }
}
