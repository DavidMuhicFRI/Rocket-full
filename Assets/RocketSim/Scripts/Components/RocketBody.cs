// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RocketBody.cs
// Purpose: Applies editable body dimensions to the scene mesh and derives the
// aerodynamic areas, structural-mass estimate, and fuel capacity from that size.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public class RocketBody : MonoBehaviour
    {
        [Header("Dimensions — written by RocketAssembly.ApplyPartsConfig")]
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

        // Bare-body lateral drag acts near the projected side-area centroid.
        // Grid fins and base drag are modeled separately, so do not move this
        // upward to emulate the whole vehicle aerodynamic center.
        public float CPLocalY => height * 0.50f;

        /// <summary>
        /// Estimated dry mass. It scales with outer surface area as a simple
        /// structural-mass approximation; it is not a detailed material model.
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
        /// Estimated fuel capacity. It scales with cylinder volume as a simple
        /// tank-capacity approximation.
        /// </summary>
        public float MaxFuelCapacity =>
            baseFuelMass * (Mathf.PI * radius * radius * height)
                         / (Mathf.PI * BaseR   * BaseR   * BaseH);

        // ── Apply to transform ────────────────────────────────────────────────
        // Unity's default cylinder is 1 m radius, 2 m tall at scale (1,1,1).
        // The agent root is the booster base plane, so the body centre is height / 2.
        /// <summary>
        /// Resizes Unity's default cylinder mesh to the configured radius and
        /// height, then keeps its bottom aligned with the rocket-root origin.
        /// Unity's cylinder is two units tall at scale one, hence height * 0.5.
        /// </summary>
        public void ApplyDimensions()
        {
            transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            ChangePosition();
        }

        /// <summary>
        /// Keeps the cylinder mesh centered above the booster-base origin after
        /// radius or height changes, matching the coordinate frame used by the agent.
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
