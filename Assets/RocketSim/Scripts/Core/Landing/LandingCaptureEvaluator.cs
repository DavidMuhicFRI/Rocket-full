// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LandingCaptureEvaluator.cs
// Purpose: Performs the final all-limits kinematic check for chopstick capture:
// position, total/axis speed, tilt, angular rate, uprightness, and yaw error.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class LandingCaptureEvaluator
    {
        /// <summary>
        /// Returns whether landing state is inside every configured success
        /// limit for capture, speed, tilt, angular rate, and yaw alignment.
        /// </summary>
        public static bool IsKinematicallyReady(
            RewardTerms terms,
            float tiltDeg,
            float tiltLimitDeg,
            float successRadius,
            float maxSpeed,
            float maxVerticalSpeed,
            float maxHorizontalSpeed,
            float maxAngularRateDegS,
            float maxYawErrorDeg)
        {
            return terms.upDot > 0.94f &&
                   tiltDeg < tiltLimitDeg &&
                   terms.planarDistance < successRadius &&
                   terms.speed < maxSpeed &&
                   Mathf.Abs(terms.verticalSpeed) < maxVerticalSpeed &&
                   terms.planarSpeed < maxHorizontalSpeed &&
                   terms.angularRateDegS < maxAngularRateDegS &&
                   terms.yawErrorDeg < maxYawErrorDeg;
        }
    }
}
