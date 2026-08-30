// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Physics/AtmosphereModel.cs
// Purpose: Provides the deliberately simple exponential atmosphere and converts
// pressure into altitude-dependent engine thrust and efficiency.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class AtmosphereModel
    {
        /// <summary>
        /// Returns normalized ambient pressure at altitude using the exponential atmosphere model.
        /// </summary>
        public static float PressureRatio(float altitude, float scaleHeight)
        {
            altitude = Mathf.Max(0f, altitude);
            return Mathf.Clamp01(Mathf.Exp(-altitude / scaleHeight));
        }

        /// <summary>
        /// Returns air density at altitude after applying the weather density multiplier
        /// </summary>
        public static float AirDensity(float altitude, float seaLevelDensity, float scaleHeight, float multiplier)
        {
            altitude = Mathf.Max(0f, altitude);
            return seaLevelDensity * multiplier * Mathf.Exp(-altitude / scaleHeight);
        }

        /// <summary>
        /// Interpolates engine thrust between vacuum and sea-level behavior using the current pressure ratio.
        /// </summary>
        public static float ThrustScale(float pressureRatio, float vacuumThrustMultiplier) => Mathf.Lerp(vacuumThrustMultiplier, 1f, pressureRatio);

        /// <summary>
        /// Interpolates specific impulse between vacuum and sea-level behavior using the current pressure ratio.
        /// </summary>
        public static float SpecificImpulse(float seaLevelIsp, float pressureRatio, float vacuumIspMultiplier)
        {
            seaLevelIsp = Mathf.Max(seaLevelIsp, 1f);
            return seaLevelIsp * Mathf.Lerp(vacuumIspMultiplier, 1f, pressureRatio);
        }
    }
}
