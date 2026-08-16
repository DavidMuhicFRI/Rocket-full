// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/LandingFeasibility.cs
// Purpose: Provides conservative reachability estimates for landing starts.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public static class LandingFeasibility
    {
        public const float DefaultUsableThrustFraction = 0.82f;
        public const float DefaultDistanceReserveFraction = 0.80f;
        public const float MinimumAltitudeMarginM = 8f;

        /// <summary>Returns usable upward acceleration after gravity.</summary>
        public static float NetUpwardAcceleration(
            float thrustPerEngineN,
            int activeEngineCount,
            float vehicleMassKg,
            float gravity,
            float usableThrustFraction = DefaultUsableThrustFraction)
        {
            float mass = Mathf.Max(vehicleMassKg, 1f);
            float thrust = Mathf.Max(0f, thrustPerEngineN) * Mathf.Max(0, activeEngineCount);
            float grossAcceleration = thrust * Mathf.Clamp01(usableThrustFraction) / mass;
            return Mathf.Max(0f, grossAcceleration - Mathf.Max(0f, gravity));
        }

        /// <summary>
        /// Estimates distance consumed by ignition delay and a constant maximum
        /// deceleration burn from the supplied downward speed.
        /// </summary>
        public static float RequiredStoppingDistance(
            float downwardSpeed,
            float netUpwardAcceleration,
            float ignitionDelay,
            float gravity)
        {
            float speed = Mathf.Max(0f, downwardSpeed);
            float delay = Mathf.Max(0f, ignitionDelay);
            float g = Mathf.Max(0f, gravity);
            float acceleration = Mathf.Max(0.001f, netUpwardAcceleration);

            float delayDistance = speed * delay + 0.5f * g * delay * delay;
            float speedAfterDelay = speed + g * delay;
            float brakingDistance = speedAfterDelay * speedAfterDelay / (2f * acceleration);
            return delayDistance + brakingDistance;
        }

        /// <summary>Finds the fastest downward start that fits the reserved altitude.</summary>
        public static float MaxRecoverableDownwardSpeed(
            float availableAltitude,
            float netUpwardAcceleration,
            float ignitionDelay,
            float gravity,
            float distanceReserveFraction = DefaultDistanceReserveFraction)
        {
            float usableDistance = Mathf.Max(0f, availableAltitude - MinimumAltitudeMarginM) *
                                   Mathf.Clamp01(distanceReserveFraction);
            if (usableDistance <= 0f || netUpwardAcceleration <= 0f)
                return 0f;

            float low = 0f;
            float high = 300f;
            for (int i = 0; i < 28; i++)
            {
                float mid = (low + high) * 0.5f;
                if (RequiredStoppingDistance(mid, netUpwardAcceleration, ignitionDelay, gravity) <= usableDistance)
                    low = mid;
                else
                    high = mid;
            }

            return low;
        }

        /// <summary>Estimates conservative lateral speed and offset authority.</summary>
        public static void HorizontalEnvelope(
            float availableAltitude,
            float downwardSpeed,
            float thrustPerEngineN,
            int activeEngineCount,
            float vehicleMassKg,
            float maxGimbalDeg,
            float ignitionDelay,
            float gravity,
            out float maxCorrectableSpeed,
            out float maxCorrectableOffset)
        {
            float mass = Mathf.Max(vehicleMassKg, 1f);
            float thrustAcceleration = Mathf.Max(0f, thrustPerEngineN) * Mathf.Max(0, activeEngineCount) /
                                       mass * DefaultUsableThrustFraction;
            float lateralAcceleration = thrustAcceleration * Mathf.Sin(Mathf.Max(0f, maxGimbalDeg) * Mathf.Deg2Rad);
            float g = Mathf.Max(0.001f, gravity);
            float speed = Mathf.Max(0f, downwardSpeed);
            float altitude = Mathf.Max(0f, availableAltitude);

            // Ballistic time to the target plane is safer than altitude/speed,
            // which becomes unrealistically large when sampled speed is small.
            float flightTime = (Mathf.Sqrt(speed * speed + 2f * g * altitude) - speed) / g;
            float controlTime = Mathf.Max(0f, flightTime - Mathf.Max(0f, ignitionDelay));

            // Reserve half the theoretical authority for vertical and attitude control.
            maxCorrectableSpeed = lateralAcceleration * controlTime * 0.5f;
            maxCorrectableOffset = 0.5f * lateralAcceleration * controlTime * controlTime * 0.5f;
        }
    }
}
