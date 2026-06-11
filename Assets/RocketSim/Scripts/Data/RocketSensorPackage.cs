using UnityEngine;

namespace RocketSim
{
    /// Computes physically-grounded sensor readings every FixedUpdate.
    /// Attach to the same GameObject as FalconAgent, or let FalconAgent
    /// call Tick() directly.
    public class RocketSensorPackage
    {
        // ── Outputs (read by FalconAgent for observations + telemetry) ──────
        
        /// Full attitude quaternion in world space
        public Quaternion Orientation     { get; private set; }
        
        /// Euler angles (degrees) — roll/pitch/yaw for human-readable logging
        public Vector3    EulerAngles     { get; private set; }

        /// Linear acceleration in ROCKET-LOCAL frame, gravity subtracted.
        /// This is exactly what an IMU accelerometer measures (specific force).
        /// Units: m/s²  — divide by 9.81 to get G.
        public Vector3    SpecificForce   { get; private set; }

        /// G-force magnitude (scalar) — what the airframe "feels"
        public float      GMagnitude      { get; private set; }

        /// Angular acceleration in local frame (rad/s²) — gyro derivative
        public Vector3    AngularAccel    { get; private set; }

        /// Aerodynamic heat flux (W/m²), simplified Stanton number model:
        ///   Q_dot = k · ρ · v³
        /// k ≈ 1.83e-4 for steel/aluminium nose (empirical constant)
        public float      HeatFlux        { get; private set; }

        /// Peak heat flux seen this episode (for logging / reward shaping)
        public float      PeakHeatFlux    { get; private set; }

        /// Bending stress at the base of the airframe (Pa), caused by the
        /// lateral aerodynamic force acting at the CP lever arm.
        /// σ = M · c / I  where M = F_lat * lever, c = r, I ≈ π · r³ · t_wall
        public float      BendingStress   { get; private set; }

        /// Axial stress (Pa) — thrust + axial drag / cross-sectional area
        public float      AxialStress     { get; private set; }

        // ── Private state ────────────────────────────────────────────────────
        Vector3 _prevAngularVel;
        Vector3 _prevLinearVel;
        bool    _first = true;

        const float k_stanton = 1.83e-4f; // empirical aero-heating constant

        // ── Called every FixedUpdate from FalconAgent ─────────────────────
        public void Tick(
            Rigidbody rb,
            Transform  t,
            float      rho,
            float      speed,
            float      lateralForceMag,  // |F_lateral| from aero this step
            float      axialForceMag,    // thrust + axial aero load magnitude
            float      cpLeverArm,       // distance CP to base (metres)
            float      radius,
            bool       episodeStart = false)
        {
            float dt = Time.fixedDeltaTime;

            if (episodeStart)
            {
                _first = true;
                PeakHeatFlux = 0f;
                SpecificForce = t.InverseTransformDirection(-Physics.gravity);
                GMagnitude = SpecificForce.magnitude / 9.81f;
                AngularAccel = Vector3.zero;
                _prevLinearVel = rb.linearVelocity;
                _prevAngularVel = rb.angularVelocity;
            }

            // ── Orientation ────────────────────────────────────────────────
            Orientation = t.rotation;
            EulerAngles = t.rotation.eulerAngles;

            // ── Specific force (accelerometer) ─────────────────────────────
            // Δv/Δt in world space, then subtract gravity, transform to local.
            // On the first step there's no previous velocity so we skip.
            if (!_first)
            {
                Vector3 worldAccel = (rb.linearVelocity - _prevLinearVel) / dt;
                // IMU measures specific force = total accel - gravity
                Vector3 specificWorld = worldAccel - Physics.gravity;
                SpecificForce = t.InverseTransformDirection(specificWorld);
                GMagnitude    = SpecificForce.magnitude / 9.81f;   // in G
            }
            _prevLinearVel = rb.linearVelocity;

            // ── Angular acceleration (rate gyro derivative) ────────────────
            if (!_first)
                AngularAccel = (rb.angularVelocity - _prevAngularVel) / dt;
            _prevAngularVel = rb.angularVelocity;

            _first = false;

            // ── Aerodynamic heating — simplified Stanton model ─────────────
            // Real formula: Q = C_H · ρ · v³  (W/m²)
            // Only significant above ~Mach 0.3 (~100 m/s); below that rounding
            // noise dominates so we gate it.
            HeatFlux = speed > 80f
                ? k_stanton * rho * speed * speed * speed
                : 0f;
            if (HeatFlux > PeakHeatFlux) PeakHeatFlux = HeatFlux;

            // ── Structural stress ──────────────────────────────────────────
            // Bending: σ_bend = M·c / I_area
            //   M = lateral aero force × lever arm from base to CP
            //   c = outer radius
            //   I_area ≈ π · r³ · t_wall  (thin-walled tube approximation)
            float wallThickness = Mathf.Clamp(radius * 0.012f, 0.01f, 0.08f);
            float Iarea = Mathf.PI * radius * radius * radius * wallThickness; // m⁴
            float M     = lateralForceMag * cpLeverArm;       // N·m
            BendingStress = Iarea > 0f ? (M * radius) / Iarea : 0f;  // Pa

            // Axial: σ_axial = F / A
            float crossSection = Mathf.PI * radius * radius;
            AxialStress = crossSection > 0f ? axialForceMag / crossSection : 0f;
        }
    }
}
