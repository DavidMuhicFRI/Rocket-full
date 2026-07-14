// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Physics.cs
// Purpose: Applies rocket forces, aerodynamics, thrust, wind, and mass-property updates during physics steps.
// Main flow: update wind and atmosphere -> apply thrust/RCS/aerodynamics ->
// apply active faults -> consume propellant -> refresh mass and inertia.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class FalconAgent
    {
        /// <summary>
        /// Smoothly moves wind toward its target and occasionally selects a new
        /// gust-adjusted target when wind is enabled.
        /// </summary>
        void StepWind()
        {
            if (!envConfig.windEnabled)
            {
                wind = targetWind = Vector3.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            float windAlpha = 1f - Mathf.Exp(-Mathf.Max(0f, envConfig.windChangeRate) * dt);
            wind = Vector3.Lerp(wind, targetWind, windAlpha);

            float gustChance = 1f - Mathf.Exp(-Mathf.Max(0f, envConfig.gustFrequencyHz) * dt);
            if (_episodeRandom.Chance(gustChance))
            {
                float g = envConfig.EffectiveGustAmp;
                targetWind = _episodePrevailingWind + RandomPlanarVector(g);
            }
        }

        /// <summary>
        /// Selects one prevailing horizontal wind vector for this episode. Gusts
        /// perturb this cached vector without silently changing its mean direction.
        /// </summary>
        void ResetWindForEpisode()
        {
            if (!envConfig.windEnabled || envConfig.windSpeed <= 0f)
            {
                _episodePrevailingWind = wind = targetWind = Vector3.zero;
                return;
            }

            float directionDeg = envConfig.randomizeWindDirectionEachEpisode
                ? RandomRange(0f, 360f)
                : envConfig.windDirectionDeg;
            float directionRad = directionDeg * Mathf.Deg2Rad;
            _episodePrevailingWind = new Vector3(
                Mathf.Sin(directionRad),
                0f,
                Mathf.Cos(directionRad)) * envConfig.windSpeed;
            wind = targetWind = _episodePrevailingWind;
        }

        /// <summary>
        /// Converts the rocket altitude into a sea-level pressure ratio used by engine and aerodynamic calculations.
        /// </summary>
        float AtmosphericPressureRatio() =>
            AtmosphereModel.PressureRatio(transform.localPosition.y, HScale);

        /// <summary>
        /// Calculates the altitude-based thrust multiplier so vacuum engines gain performance as pressure drops.
        /// </summary>
        float CurrentEngineThrustScale() =>
            AtmosphereModel.ThrustScale(AtmosphericPressureRatio(), VacuumThrustMultiplier);

        /// <summary>
        /// Calculates effective specific impulse for this altitude so fuel burn matches the pressure-scaled engine model.
        /// </summary>
        float CurrentEngineSpecificImpulse() =>
            AtmosphereModel.SpecificImpulse(cfg.specificImpulse, AtmosphericPressureRatio(), VacuumIspMultiplier);

        /// <summary>
        /// Applies main-engine thrust and burns fuel for this physics step.
        /// </summary>
        float ApplyEngines()
        {
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
                    float faultScale = 1f - ActiveFaultSeverity(RocketFaultType.EngineThrustLoss, arrayIndex);
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
                    float faultScale = 1f - ActiveFaultSeverity(RocketFaultType.EngineThrustLoss, arrayIndex);
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
            float bodyAxisAngleDeg = speed > 0.5f
                ? Vector3.Angle(transform.up, effVel.normalized)
                : 0f;
            aoaDeg = Mathf.Min(bodyAxisAngleDeg, 180f - bodyAxisAngleDeg);

            // Axial drag, signed so it opposes motion during ascent and descent.
            float axialDrag = 0.5f * rho * 0.30f * cfg.A_axial * vAxial * Mathf.Abs(vAxial);
            axialForceMag += Mathf.Abs(axialDrag);
            rb.AddForce(-transform.up * axialDrag);

            Vector3 cpWorld = transform.TransformPoint(new Vector3(0f, cfg.cpLocalY, 0f));

            // Broadside drag at the centre of pressure.
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

            if (cfg.hasFins && assembly.fins != null && assembly.fins.gameObject.activeSelf)
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
        /// Calculates and applies aerodynamic force for one active fin at that
        /// fin's transform position.
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

            force *= 1f - ActiveFaultSeverity(RocketFaultType.FinEffectivenessLoss, index);

            rb.AddForceAtPosition(force, finTransform.position);
        }

        /// <summary>
        /// Selects repeatable episode faults from the panel configuration.
        /// Training uses the configured seed, area and episode; inference uses
        /// exactly the chosen fault so comparisons can be reproduced.
        /// </summary>
        void ConfigureEpisodeFaults()
        {
            _episodeFaultCount = 0;
            _episodeElapsedSeconds = 0f;
            var faults = envConfig?.faults;
            if (faults == null) return;
            faults.Clamp();

            if (envConfig.behaviorType == BehaviorType.Inference)
            {
                if (faults.evaluationFaultEnabled && faults.evaluationFaultType != RocketFaultType.None)
                    AddEpisodeFault(faults.evaluationFaultType, faults.evaluationTargetIndex, faults.evaluationSeverity);
                return;
            }

            if (!faults.allowDuringTraining) return;
            var random = new DeterministicRandom(
                DeterministicRandom.EpisodeSeed(faults.seed, _areaIndex, _episode, stream: 17));
            if (!random.Chance(faults.faultyEpisodeProbability)) return;

            var candidates = new RocketFaultType[5];
            int candidateCount = 0;
            if (faults.allowEngineThrustLoss) candidates[candidateCount++] = RocketFaultType.EngineThrustLoss;
            if (faults.allowGimbalJam) candidates[candidateCount++] = RocketFaultType.GimbalJam;
            if (faults.allowFinEffectivenessLoss && cfg.hasFins) candidates[candidateCount++] = RocketFaultType.FinEffectivenessLoss;
            if (faults.allowRcsStuckClosed && cfg.hasRCS) candidates[candidateCount++] = RocketFaultType.RcsStuckClosed;
            if (faults.allowRcsStuckOpen && cfg.hasRCS) candidates[candidateCount++] = RocketFaultType.RcsStuckOpen;
            if (candidateCount == 0) return;

            // Shuffle only the populated part of the small candidate array.
            for (int i = candidateCount - 1; i > 0; i--)
            {
                int swapIndex = random.Range(0, i + 1);
                (candidates[i], candidates[swapIndex]) = (candidates[swapIndex], candidates[i]);
            }

            int maximumCount = Mathf.Min(faults.maxSimultaneousFaults, candidateCount);
            int count = random.Range(1, maximumCount + 1);
            for (int i = 0; i < count; i++)
            {
                RocketFaultType type = candidates[i];
                int targetCount = FaultTargetCount(type);
                int target = targetCount > 0 ? random.Range(0, targetCount) : 0;
                float severity = random.Range(faults.trainingSeverityMin, faults.trainingSeverityMax);
                AddEpisodeFault(type, target, severity);
            }
        }

        /// <summary>
        /// Stores one fault in the fixed-size per-episode arrays. The target is
        /// clamped to an existing actuator so an invalid saved index cannot break
        /// the physics loop.
        /// </summary>
        void AddEpisodeFault(RocketFaultType type, int target, float severity)
        {
            if (_episodeFaultCount >= _episodeFaultTypes.Length) return;
            int targetCount = Mathf.Max(1, FaultTargetCount(type));
            _episodeFaultTypes[_episodeFaultCount] = type;
            _episodeFaultTargets[_episodeFaultCount] = Mathf.Clamp(target, 0, targetCount - 1);
            _episodeFaultSeverities[_episodeFaultCount] = Mathf.Clamp01(severity);
            _episodeFaultCount++;
            Debug.Log($"[Fault] area={_areaIndex} episode={_episode} type={type} target={target} severity={severity:F2}");
        }

        /// <summary>
        /// Returns how many hardware items can be targeted by a fault type.
        /// Engine faults target command channels, fin faults target active fins,
        /// and RCS faults target individual jets.
        /// </summary>
        int FaultTargetCount(RocketFaultType type)
        {
            return type switch
            {
                RocketFaultType.EngineThrustLoss => cfg.independentEngineCount,
                RocketFaultType.GimbalJam => cfg.independentEngineCount,
                RocketFaultType.FinEffectivenessLoss => cfg.finCount,
                RocketFaultType.RcsStuckClosed => cfg.rcsJetCount,
                RocketFaultType.RcsStuckOpen => cfg.rcsJetCount,
                _ => 0
            };
        }

        /// <summary>
        /// Finds the strongest currently active matching fault. Training faults
        /// remain active for the full episode. Evaluation faults additionally
        /// respect their configured start time and optional duration.
        /// </summary>
        float ActiveFaultSeverity(RocketFaultType type, int target)
        {
            float severity = 0f;
            var faults = envConfig?.faults;
            for (int i = 0; i < _episodeFaultCount; i++)
            {
                if (_episodeFaultTypes[i] != type || _episodeFaultTargets[i] != target) continue;
                if (envConfig.behaviorType == BehaviorType.Inference && faults != null)
                {
                    if (_episodeElapsedSeconds < faults.evaluationOnsetSeconds) continue;
                    if (faults.evaluationDurationSeconds > 0f &&
                        _episodeElapsedSeconds > faults.evaluationOnsetSeconds + faults.evaluationDurationSeconds) continue;
                }
                severity = Mathf.Max(severity, _episodeFaultSeverities[i]);
            }
            return severity;
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
