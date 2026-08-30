// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Physics/RocketMassProperties.cs
// Purpose: Estimates total mass, vertical center of mass, and rotational inertia
// as main fuel and RCS propellant are consumed during an episode.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal readonly struct RocketMassState
    {
        public readonly float totalMass;
        public readonly Vector3 centerOfMass;
        public readonly Vector3 inertiaTensor;
        public readonly Quaternion inertiaTensorRotation;

        /// <summary>
        /// Stores the mass, center of mass, and inertia tensor that Unity should apply to the rocket Rigidbody for the current propellant load.
        /// </summary>
        public RocketMassState(float totalMass, Vector3 centerOfMass, Vector3 inertiaTensor, Quaternion inertiaTensorRotation)
        {
            this.totalMass = totalMass;
            this.centerOfMass = centerOfMass;
            this.inertiaTensor = inertiaTensor;
            this.inertiaTensorRotation = inertiaTensorRotation;
        }
    }

    internal static class RocketMassProperties
    {
        /// <summary>
        /// Estimates total mass, vertical center of mass, and inertia tensor by distributing dry mass, fuel, fins, engines, and RCS propellant along the body.
        /// </summary>
        public static RocketMassState Calculate(RocketPhysicsConfig cfg, float fuel, float rcsPropellant)
        {
            float total = cfg.dryMass + fuel + rcsPropellant;

            float l = cfg.length;
            float r = cfg.radius;
            float bot = 0f;

            float engMass = Mathf.Clamp(cfg.engineDryMass, 0f, cfg.dryMass);
            float finMass = Mathf.Clamp(cfg.finDryMass, 0f, cfg.dryMass - engMass);
            float rcsDryMass = Mathf.Clamp(cfg.rcsDryMass, 0f, cfg.dryMass - engMass - finMass);
            float structMass = Mathf.Max(0f, cfg.dryMass - engMass - finMass - rcsDryMass);

            float engY = bot;
            float structY = l * 0.45f;
            float finY = Mathf.Clamp(cfg.finLocalY, bot, l);
            float rcsY = Mathf.Clamp(cfg.rcsLocalY, bot, l);
            float fuelY = l * 0.56f;

            float comY = (engMass * engY +
                          structMass * structY +
                          finMass * finY +
                          rcsDryMass * rcsY +
                          rcsPropellant * rcsY +
                          fuel * fuelY) / total;

            // Each component contributes a simple shape inertia plus the
            // parallel-axis term caused by its distance from the combined center.
            float iEngine = engMass * (engY - comY) * (engY - comY);

            float structLength = l * 0.90f;
            float iStruct = (structMass / 12f) * (3f * r * r + structLength * structLength) + structMass * (structY - comY) * (structY - comY);

            float iFins = finMass * (finY - comY) * (finY - comY);
            float iRcs = (rcsDryMass + rcsPropellant) * (rcsY - comY) * (rcsY - comY);

            float fuelLength = l * 0.60f;
            float iFuel = (fuel / 12f) * (3f * r * r + fuelLength * fuelLength) + fuel * (fuelY - comY) * (fuelY - comY);

            float iPitch = iEngine + iStruct + iFins + iRcs + iFuel;
            float iSpin = 0.5f * total * r * r;

            return new RocketMassState(total, new Vector3(0f, comY, 0f), new Vector3(iPitch, iSpin, iPitch), Quaternion.identity);
        }
    }
}
