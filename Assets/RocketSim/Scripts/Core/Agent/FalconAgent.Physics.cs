// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Physics.cs
// Purpose: Applies rocket forces, aerodynamics, thrust, and mass-property updates during physics steps.
// Main flow: read environment/actuator state -> apply thrust/RCS/aerodynamics -> consume propellant -> refresh mass and inertia.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class FalconAgent
    {
        /// <summary>
        /// Converts the rocket altitude into a sea-level pressure ratio used by engine and aerodynamic calculations.
        /// </summary>
        float AtmosphericPressureRatio() => AtmosphereModel.PressureRatio(transform.localPosition.y, HScale);

        /// <summary>
        /// Calculates the altitude-based thrust multiplier so vacuum engines gain performance as pressure drops.
        /// </summary>
        float CurrentEngineThrustScale() => AtmosphereModel.ThrustScale(AtmosphericPressureRatio(), VacuumThrustMultiplier);

        /// <summary>
        /// Calculates effective specific impulse for this altitude so fuel burn matches the pressure-scaled engine model.
        /// </summary>
        float CurrentEngineSpecificImpulse() => AtmosphereModel.SpecificImpulse(cfg.specificImpulse, AtmosphericPressureRatio(), VacuumIspMultiplier);

        /// <summary>
        /// Applies main-engine thrust and burns fuel for this physics step.
        /// </summary>
        float ApplyEngines()
        {
            if (_legLandingPropulsionLocked) return 0f;
            if (fuel <= 0f || !assembly || !assembly.thrusters) return 0f;

            int installedEngineIndex = 0;
            int burnEngineIndex = 0;
            int thrusterCount = assembly.thrusters.transform.childCount;
            float requestedFuel = 0f;
            float dt = Time.fixedDeltaTime;
            float thrustScale = CurrentEngineThrustScale();
            float engineIsp = CurrentEngineSpecificImpulse();

            for (int i = 0; i < thrusterCount; i++)
            {
                Transform engine = assembly.thrusters.transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                if (!EngineBurnGroups.IncludesEngine(cfg.engineLayout, cfg.octawebBurnGroup, installedEngineIndex))
                {
                    installedEngineIndex++;
                    continue;
                }

                int arrayIndex = cfg.independentEngines ? burnEngineIndex : 0;
                if (arrayIndex < throttle.Length)
                {
                    float faultScale = 1f - _episodeFaults.Severity(RocketFaultType.EngineThrustLoss, arrayIndex, _episodeElapsedSeconds);
                    float forceMag = throttle[arrayIndex] * cfg.maxThrust * thrustScale * faultScale;
                    requestedFuel += forceMag / (engineIsp * G0) * dt;
                }

                burnEngineIndex++;
                installedEngineIndex++;
            }

            if (requestedFuel <= 0f) return 0f;

            float fuelScale = Mathf.Clamp01(fuel / requestedFuel);
            float consumedFuel = 0f;
            float thrustForceMag = 0f;
            installedEngineIndex = 0;
            burnEngineIndex = 0;

            for (int i = 0; i < thrusterCount; i++)
            {
                Transform engine = assembly.thrusters.transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                if (!EngineBurnGroups.IncludesEngine(cfg.engineLayout, cfg.octawebBurnGroup, installedEngineIndex))
                {
                    installedEngineIndex++;
                    continue;
                }

                int arrayIndex = cfg.independentEngines ? burnEngineIndex : 0;
                if (arrayIndex < throttle.Length && throttle[arrayIndex] > 0f)
                {
                    Vector3 localDir = Quaternion.Euler(gimbal[arrayIndex].x, 0f, gimbal[arrayIndex].y) * Vector3.up;
                    Vector3 worldDir = transform.TransformDirection(localDir);
                    float effectiveThrottle = throttle[arrayIndex] * fuelScale;
                    float faultScale = 1f - _episodeFaults.Severity(RocketFaultType.EngineThrustLoss, arrayIndex, _episodeElapsedSeconds);
                    float forceMag = effectiveThrottle * cfg.maxThrust * thrustScale * faultScale;

                    rb.AddForceAtPosition(worldDir * forceMag, engine.position);
                    thrustForceMag += forceMag;
                    consumedFuel += forceMag / (engineIsp * G0) * dt;
                }

                burnEngineIndex++;
                installedEngineIndex++;
            }

            fuel = Mathf.Max(0f, fuel - consumedFuel);
            return thrustForceMag;
        }

        /// <summary>
        /// Applies body drag, base drag, and grid-fin aerodynamic forces.
        /// </summary>
        void ApplyAerodynamics(out float lateralForceMag, out float axialForceMag)
        {
            lateralForceMag = 0f;
            axialForceMag = 0f;

            float altitude = Mathf.Max(0f, transform.localPosition.y);
            float rho = AtmosphereModel.AirDensity(altitude, Rho0, HScale, envConfig.AirDensityMultiplier);

            Vector3 effVel = rb.linearVelocity - wind;
            float speed = effVel.magnitude;

            Vector3 localVel = transform.InverseTransformDirection(effVel);
            float vAxial = localVel.y;
            Vector3 vLatLocal = new Vector3(localVel.x, 0f, localVel.z);
            float vLat = vLatLocal.magnitude;

            q = 0.5f * rho * speed * speed;
            float bodyAxisAngleDeg = speed > 0.5f ? Vector3.Angle(transform.up, effVel.normalized) : 0f;
            aoaDeg = Mathf.Min(bodyAxisAngleDeg, 180f - bodyAxisAngleDeg);

            // Axial drag, signed so it opposes motion during ascent and descent.
            float axialDrag = 0.5f * rho * 0.30f * cfg.A_axial * vAxial * Mathf.Abs(vAxial);
            axialForceMag += Mathf.Abs(axialDrag);
            rb.AddForce(-transform.up * axialDrag);

            Vector3 cpWorld = transform.TransformPoint(new Vector3(0f, cfg.cpLocalY, 0f));

            // Broadside drag at the center of pressure.
            if (vLat > 0.01f)
            {
                float lateralDrag = 0.5f * rho * 1.0f * cfg.A_projectedSide * vLat * vLat;
                lateralForceMag += lateralDrag;
                Vector3 lateralDir = -transform.TransformDirection(vLatLocal.normalized);
                rb.AddForceAtPosition(lateralDir * lateralDrag, cpWorld);
            }

            // Base drag is active when descending tail-first.
            if (vAxial < -0.1f)
            {
                float baseDragMag = 0.5f * rho * 0.12f * cfg.A_axial * vAxial * vAxial;
                axialForceMag += baseDragMag;
                rb.AddForce(transform.up * baseDragMag);
            }

            if (cfg.hasFins && assembly.fins && assembly.fins.gameObject.activeSelf)
            {
                int activeFinIndex = 0;
                for (int i = 0; i < assembly.fins.transform.childCount; i++)
                {
                    Transform finTransform = assembly.fins.transform.GetChild(i);
                    if (!finTransform.gameObject.activeSelf) continue;
                    if (activeFinIndex >= cfg.finCount) break;

                    ApplySingleFin(activeFinIndex, finTransform, rho, effVel);
                    activeFinIndex++;
                }
            }
        }

        /// <summary>
        /// Calculates and applies aerodynamic force for one active fin.
        /// </summary>
        void ApplySingleFin(int index, Transform finTransform, float rho, Vector3 effVelWorld)
        {
            Vector3 force = FinAerodynamicsModel.CalculateForce(
                transform,
                finTransform,
                finAngles[index],
                rho,
                effVelWorld,
                cfg.A_fin,
                cfg.finLiftScale);

            force *= 1f - _episodeFaults.Severity(RocketFaultType.FinEffectivenessLoss, index, _episodeElapsedSeconds);

            rb.AddForceAtPosition(force, finTransform.position);
        }

        /// <summary>
        /// Updates mass, center of mass, and inertia from remaining propellant.
        /// </summary>
        void UpdateMassProperties()
        {
            RocketMassState mass = RocketMassProperties.Calculate(cfg, fuel, rcsPropellant);
            rb.mass = mass.totalMass;
            rb.centerOfMass = mass.centerOfMass;
            rb.inertiaTensor = mass.inertiaTensor;
            rb.inertiaTensorRotation = mass.inertiaTensorRotation;
        }
    }
}
