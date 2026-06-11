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
        public float startFuelMass = 40000f;

        // ── Reference geometry (Falcon 9 default) ─────────────────────────────
        const float BaseR = 1.83f, BaseH = 41.2f;

        // ── Computed geometry (read by RocketAssembly.GetPhysicsConfig) ───────
        public float AxialArea     => Mathf.PI * radius * radius;
        public float LateralArea   => 2f * radius * height;

        // Approximate centre of pressure measured from the booster base plane.
        public float CPLocalY => height * 0.65f;

        // Dry mass scales with outer surface area (structural mass proxy)
        public float DryMass
        {
            get {
                float r = radius;
                float cur  = 2f * Mathf.PI * r * (height + r);
                float @ref = 2f * Mathf.PI * BaseR * (BaseH + BaseR);
                return baseDryMass * (cur / @ref);
            }
        }

        // Max fuel scales with cylinder volume (tank volume proxy)
        public float MaxFuelCapacity =>
            baseFuelMass * (Mathf.PI * radius * radius * height)
                         / (Mathf.PI * BaseR   * BaseR   * BaseH);

        // ── Apply to transform ────────────────────────────────────────────────
        // Unity's default cylinder is 1 m radius, 2 m tall at scale (1,1,1).
        // The agent root is the booster base plane, so the body centre is height / 2.
        public void ApplyDimensions()
        {
            transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            ChangePosition();
        }

        public void ChangePosition()
        {
            transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }
        

        void Awake() => ApplyDimensions();
    }
}
