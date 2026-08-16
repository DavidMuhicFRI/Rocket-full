// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketAssembly.cs
// Purpose: Coordinates the configured components that make up one scene rocket.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Scene adapter between a vehicle configuration and its components. Each
    /// component owns its settings and visuals; physics derivation lives in
    /// RocketPhysicsConfigFactory.
    /// </summary>
    public class RocketAssembly : MonoBehaviour
    {
        [Header("Components — wire inside prefab")]
        public BodyComponent body;
        public ThrusterComponent thrusters;
        public FinComponent fins;
        public RcsComponent rcs;
        public LandingLegComponent landingLegs;

        /// <summary>Returns the serialized landing-gear component.</summary>
        public LandingLegComponent LandingLegs
        {
            get
            {
                if (!landingLegs)
                    landingLegs = GetComponentInChildren<LandingLegComponent>(true);
                return landingLegs;
            }
        }

        /// <summary>Pushes one vehicle configuration to its matching components.</summary>
        public void ApplyPartsConfig(RocketPartsConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[RocketAssembly] Cannot apply a null vehicle configuration.", this);
                return;
            }

            body?.ApplyConfiguration(config);
            float radius = body ? body.radius : config.bodyRadius;
            float height = body ? body.height : config.bodyHeight;
            thrusters?.ApplyConfiguration(config, radius);
            fins?.ApplyConfiguration(config, radius, height);
            rcs?.ApplyConfiguration(config, radius, height);
        }

        /// <summary>Captures the immutable physical constants used by an episode.</summary>
        public RocketPhysicsConfig GetPhysicsConfig() =>
            RocketPhysicsConfigFactory.Create(body, thrusters, fins, rcs);
    }
}
