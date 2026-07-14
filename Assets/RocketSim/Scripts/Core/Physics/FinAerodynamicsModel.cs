// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Physics/FinAerodynamicsModel.cs
// Purpose: Calculates approximate lift and drag from one grid fin using its
// position, deflection, air-relative velocity, density, and area.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class FinAerodynamicsModel
    {
        /// <summary>
        /// Calculates the world-space lift and drag force for one grid fin from
        /// local deflection, air density, and air-relative velocity.
        /// </summary>
        public static Vector3 CalculateForce(
            Transform rocketTransform,
            Transform finTransform,
            float finAngleDeg,
            float rho,
            Vector3 effVelWorld,
            float finArea,
            float liftScale)
        {
            // Build a frame for this fin: radial points away from the body and
            // tangent points around it. Steering force acts mainly along tangent.
            Vector3 radialWorld = Vector3.ProjectOnPlane(finTransform.position - rocketTransform.position, rocketTransform.up);
            if (radialWorld.sqrMagnitude < 0.000001f)
            {
                radialWorld = Vector3.ProjectOnPlane(finTransform.right, rocketTransform.up);
            }
            radialWorld = radialWorld.normalized;

            Vector3 tangentWorld = Vector3.Cross(rocketTransform.up, radialWorld).normalized;

            float vSpine = Vector3.Dot(effVelWorld, rocketTransform.up);
            float vTangent = Vector3.Dot(effVelWorld, tangentWorld);

            float deflectionRad = finAngleDeg * Mathf.Deg2Rad;
            float sideslipRad = Mathf.Abs(vSpine) > 0.5f
                ? Mathf.Atan2(vTangent, Mathf.Abs(vSpine))
                : 0f;
            float effectiveAoARad = deflectionRad + sideslipRad;

            float qFin = 0.5f * rho * vSpine * Mathf.Abs(vSpine);

            // Lift rises with angle at first, then fades after roughly 23 degrees
            // to approximate stall instead of allowing unlimited steering force.
            float clRaw = 2f * Mathf.PI * Mathf.Sin(effectiveAoARad);
            float stallFade = Mathf.Clamp01(1f - (Mathf.Abs(effectiveAoARad) - 0.4f) / 0.3f);
            float cl = liftScale * clRaw * stallFade;

            Vector3 liftDir = Mathf.Sign(qFin) * tangentWorld;
            Vector3 liftForce = liftDir * (Mathf.Abs(qFin) * finArea * cl);

            float cdInduced = 0.05f + 1.8f * Mathf.Sin(effectiveAoARad) * Mathf.Sin(effectiveAoARad);
            float dragMag = Mathf.Abs(qFin) * finArea * cdInduced;
            Vector3 dragForce = -rocketTransform.up * (Mathf.Sign(vSpine) * dragMag);

            return liftForce + dragForce;
        }
    }
}
