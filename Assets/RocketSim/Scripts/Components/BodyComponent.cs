// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/BodyComponent.cs
// Purpose: Applies editable body dimensions to the scene mesh and derives the
// aerodynamic areas, structural-mass estimate, and fuel capacity from that size.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public class BodyComponent : MonoBehaviour
    {
        [Header("Dimensions")]
        public float radius       = 1.83f;
        public float height       = 41.2f;
        public float baseDryMass  = 22200f;
        public float baseFuelMass = 400000f;
        public float startFuelMass = 32000f;

        // ── Reference geometry (Falcon 9 default) ─────────────────────────────
        const float BaseR = RocketPartsConfig.ReferenceBodyRadiusM;
        const float BaseH = RocketPartsConfig.ReferenceBodyHeightM;

        // ── Computed geometry (read by RocketAssembly.GetPhysicsConfig) ───────
        public float AxialArea         => Mathf.PI * radius * radius;
        public float ProjectedSideArea => 2f * radius * height;
        
        // Grid fins and base drag are modeled separately, so do not move this upward to emulate the whole vehicle aerodynamic center.
        public float CPLocalY => height * 0.50f;

        /// <summary>
        /// Estimated dry mass.
        /// Scales with outer surface area as a simple structural-mass approximation;
        /// </summary>
        public float DryMass
        {
            get {
                float r = radius;
                float cur  = 2f * Mathf.PI * r * (height + r);
                float @ref = 2f * Mathf.PI * BaseR * (BaseH + BaseR);
                return baseDryMass * (cur / @ref);
            }
        }

        /// <summary>
        /// Estimated fuel capacity. Scales with cylinder volume as a simple tank-capacity approximation.
        /// </summary>
        public float MaxFuelCapacity => baseFuelMass * (Mathf.PI * radius * radius * height) / (Mathf.PI * BaseR   * BaseR   * BaseH);

        /// <summary>
        /// Copies body settings from a session and updates the scene mesh.
        /// </summary>
        public void ApplyConfiguration(RocketPartsConfig config)
        {
            if (config == null) return;

            radius = config.bodyRadius;
            height = config.bodyHeight;
            baseDryMass = config.baseDryMass;
            baseFuelMass = config.baseFuelMass;
            startFuelMass = config.startFuelMass;
            ApplyDimensions();
        }
        
        /// <summary>
        /// Resizes the cylinder mesh to the configured radius and height, keeps bottom aligned with the rocket-root.
        /// </summary>
        public void ApplyDimensions()
        {
            transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            ChangePosition();
        }

        /// <summary>
        /// Moves the cylinder mesh to keep the bottom aligned with the rocket-root.
        /// </summary>
        public void ChangePosition()
        {
            transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }
        

        /// <summary>
        /// Applies serialized body dimensions when the component enters the scene.
        /// </summary>
        void Awake() => ApplyDimensions();
    }
}
