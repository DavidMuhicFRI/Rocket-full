// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Spawning.cs
// Purpose: Chooses feasible spawn positions, velocities, and orientations for each task.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Places the rocket in a randomized training state for the selected scenario.
        /// </summary>
        void SpawnForScenario()
        {
            if (envConfig.behaviorType == BehaviorType.Inference)
            {
                if (envConfig.IsStandardEvaluation)
                {
                    if (envConfig.scenario.IsLanding())
                        SpawnLanding(ActiveLandingProfile);
                    else
                        SpawnHoverTrainingProfile();
                    return;
                }

                SpawnForInference();
                return;
            }

            switch (envConfig.scenario)
            {
                case ScenarioType.ChopstickLanding:
                case ScenarioType.LegLanding:
                    SpawnLanding(ActiveLandingProfile);
                    break;

                case ScenarioType.Hover:
                case ScenarioType.HoverTracking:
                    SpawnHoverTrainingProfile();
                    break;
            }
        }

        /// <summary>
        /// Uses the exact initial-state distribution used by hover training.
        /// Standard evaluation deliberately shares this path instead of the
        /// easier manual-inference defaults.
        /// </summary>
        void SpawnHoverTrainingProfile()
        {
            float rx = RandomRange(-5f, 5f);
            float rz = RandomRange(-5f, 5f);
            float altitude = envConfig.scenario == ScenarioType.HoverTracking ? ScenarioCatalog.HoverTrackingStartAltitude : ScenarioCatalog.HoverStartAltitude;
            transform.localPosition = new Vector3(rx, altitude, rz);
            transform.localRotation = Quaternion.Euler(RandomRange(-1f, 1f), 0f, RandomRange(-1f, 1f));
            Vector3 linearVelocity = envConfig.scenario == ScenarioType.Hover
                ? new Vector3(RandomRange(-2f, 2f), 0f, RandomRange(-2f, 2f))
                : Vector3.zero;
            StageEpisodeMotion(linearVelocity, Vector3.zero);
        }

        /// <summary>
        /// Places the rocket using the selected inference spawn profile.
        /// </summary>
        void SpawnForInference()
        {
            InferenceSpawnProfile profile = envConfig.GetInferenceSpawnProfile(envConfig.scenario);
            profile.Clamp();

            float altitude = SampleProfile(profile, profile.altitudeMin, profile.altitudeMax);
            if (envConfig.scenario.IsLanding())
                altitude = Mathf.Max(altitude, ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y + 15f);

            Vector2 offset = profile.randomizeEachEpisode ? RandomInsideUnitCircle() * profile.horizontalOffsetMax : Vector2.right * profile.horizontalOffsetMax;

            float planarSpeed = SampleProfile(profile, profile.horizontalSpeedMin, profile.horizontalSpeedMax);
            Vector2 planarDirection = profile.randomizeEachEpisode ? RandomInsideUnitCircle().normalized : Vector2.right;
            if (planarDirection.sqrMagnitude < 0.0001f)
                planarDirection = Vector2.right;

            float yaw = SampleProfile(profile, profile.yawMinDeg, profile.yawMaxDeg);
            float tiltX = profile.baseTiltDeg;
            float tiltZ = 0f;
            if (profile.randomizeEachEpisode)
            {
                Vector2 tiltVariation = RandomInsideUnitCircle() * profile.tiltVariationDeg;
                tiltX += tiltVariation.x;
                tiltZ = tiltVariation.y;
            }

            transform.localRotation = Quaternion.Euler(tiltX, yaw, tiltZ);
            float verticalSpeed = SampleProfile(profile, profile.verticalSpeedMin, profile.verticalSpeedMax);

            if (envConfig.scenario.IsLanding())
            {
                float terminalAltitude = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y;
                float availableAltitude = altitude - terminalAltitude;
                LandingFeasibilityInputs(out float vehicleMass, out float maxThrust,
                    out int activeEngineCount, out float maxGimbalDeg, out float startupDelay);
                float gravity = Mathf.Abs(Physics.gravity.y);
                float netUpwardAcceleration = LandingFeasibility.NetUpwardAcceleration(maxThrust, activeEngineCount, vehicleMass, gravity);
                float maxDownwardSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(availableAltitude, netUpwardAcceleration, startupDelay, gravity);
                verticalSpeed = -Mathf.Min(Mathf.Max(0f, -verticalSpeed), maxDownwardSpeed);

                LandingFeasibility.HorizontalEnvelope(availableAltitude, -verticalSpeed, maxThrust, activeEngineCount, vehicleMass, maxGimbalDeg, startupDelay, gravity, out float maxHorizontalSpeed, out float maxHorizontalOffset);
                planarSpeed = Mathf.Min(planarSpeed, maxHorizontalSpeed);
                offset = Vector2.ClampMagnitude(offset, maxHorizontalOffset);
                PlaceLandingReferenceAtLocalPosition(new Vector3(offset.x, altitude, offset.y));
            }
            else
            {
                transform.localPosition = new Vector3(offset.x, altitude, offset.y);
            }

            Vector3 linearVelocity = new Vector3(
                planarDirection.x * planarSpeed,
                verticalSpeed,
                planarDirection.y * planarSpeed);

            Vector3 angularVelocity;
            if (profile.randomizeEachEpisode && profile.angularSpeedMaxDegS > 0f)
            {
                Vector3 axis = RandomUnitVector3();
                float speedDegS = RandomRange(0f, profile.angularSpeedMaxDegS);
                angularVelocity = axis * speedDegS * Mathf.Deg2Rad;
            }
            else
            {
                angularVelocity = Vector3.zero;
            }

            StageEpisodeMotion(linearVelocity, angularVelocity);
        }

        /// <summary>
        /// Samples one feasible landing start from an immutable difficulty profile.
        /// </summary>
        void SpawnLanding(LandingCurriculumProfile landingProfile)
        {
            float landingTerminalAltitude = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y;
            float landingAltitudeMin = Mathf.Min(landingProfile.spawnAltitudeMin, landingProfile.spawnAltitudeMax);
            float landingAltitudeMax = Mathf.Max(landingProfile.spawnAltitudeMin, landingProfile.spawnAltitudeMax);
            landingAltitudeMin = Mathf.Max(landingAltitudeMin, landingTerminalAltitude + 15f);
            landingAltitudeMax = Mathf.Max(landingAltitudeMax, landingAltitudeMin + 10f);
            float landingVerticalSpeedMin = Mathf.Min(landingProfile.verticalSpeedMin, landingProfile.verticalSpeedMax);
            float landingVerticalSpeedMax = Mathf.Max(landingProfile.verticalSpeedMin, landingProfile.verticalSpeedMax);
            float landingAltitude = RandomRange(landingAltitudeMin, landingAltitudeMax);
            float availableAltitude = landingAltitude - landingTerminalAltitude;
            LandingFeasibilityInputs(out float vehicleMass, out float maxThrust, out int activeEngineCount, out float maxGimbalDeg, out float startupDelay);
            float gravity = Mathf.Abs(Physics.gravity.y);
            float netUpwardAcceleration = LandingFeasibility.NetUpwardAcceleration(maxThrust, activeEngineCount, vehicleMass, gravity);
            float recoverableDownwardSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(availableAltitude, netUpwardAcceleration, startupDelay, gravity);
            float feasibleDownwardSpeedMin = Mathf.Min(Mathf.Max(0f, landingVerticalSpeedMin), recoverableDownwardSpeed);
            float feasibleDownwardSpeedMax = Mathf.Min(Mathf.Max(feasibleDownwardSpeedMin, landingVerticalSpeedMax), recoverableDownwardSpeed);
            float landingDownwardSpeed = RandomRange(feasibleDownwardSpeedMin, feasibleDownwardSpeedMax);

            LandingFeasibility.HorizontalEnvelope(availableAltitude, landingDownwardSpeed, maxThrust, activeEngineCount, vehicleMass, maxGimbalDeg, startupDelay, gravity, out float recoverableHorizontalSpeed, out float recoverableHorizontalOffset);
            float landingOffsetLimit = Mathf.Min(landingProfile.spawnRadius, recoverableHorizontalOffset);
            float landingHorizontalSpeedLimit = Mathf.Min(landingProfile.horizontalSpeedMax, recoverableHorizontalSpeed);
            Vector2 landingOffset = RandomInsideUnitCircle() * landingOffsetLimit;
            Vector2 landingHorizontalVelocity = RandomInsideUnitCircle() * landingHorizontalSpeedLimit;
            float landingTiltRange = Mathf.Max(0f, landingProfile.spawnTiltRangeDeg);
            float landingAngularSpeedMax = Mathf.Max(0f, landingProfile.angularSpeedMaxDegS);

            transform.localRotation = Quaternion.Euler(
                RandomRange(-landingTiltRange, landingTiltRange),
                (envConfig.scenario == ScenarioType.ChopstickLanding ? envConfig.landingTargetYawDeg : 0f) + RandomRange(-landingProfile.spawnYawRangeDeg, landingProfile.spawnYawRangeDeg),
                RandomRange(-landingTiltRange, landingTiltRange));
            PlaceLandingReferenceAtLocalPosition(new Vector3(landingOffset.x, landingAltitude, landingOffset.y));
            Vector3 linearVelocity = new(
                landingHorizontalVelocity.x,
                -landingDownwardSpeed,
                landingHorizontalVelocity.y);
            Vector3 angularVelocity;
            if (landingAngularSpeedMax > 0f)
            {
                Vector3 angularAxis = RandomUnitVector3();
                angularVelocity = angularAxis * RandomRange(0f, landingAngularSpeedMax) * Mathf.Deg2Rad;
            }
            else
            {
                angularVelocity = Vector3.zero;
            }

            StageEpisodeMotion(linearVelocity, angularVelocity);
        }

        /// <summary>
        /// Stages the sampled start motion while the body is kinematic. Unity
        /// discards Rigidbody velocity writes on a kinematic body, so the
        /// lifecycle applies these values immediately after reactivation.
        /// </summary>
        void StageEpisodeMotion(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            _stagedEpisodeLinearVelocity = linearVelocity;
            _stagedEpisodeAngularVelocity = angularVelocity;
            _hasStagedEpisodeMotion = true;

            // Direct spawn invocations used by tooling may run with an
            // already-dynamic body. Apply immediately in that case while the
            // normal episode path continues through the reactivation gate.
            if (!rb || rb.isKinematic) return;
            rb.linearVelocity = linearVelocity;
            rb.angularVelocity = angularVelocity;
            _hasStagedEpisodeMotion = false;
        }

        /// <summary>Places the scenario-specific catch or feet guidance frame.</summary>
        void PlaceLandingReferenceAtLocalPosition(Vector3 desiredPosition)
        {
            if (envConfig.scenario == ScenarioType.LegLanding)
                PlaceLegFeetFrameAtLocalPosition(desiredPosition);
            else
                PlaceLandingCatchFrameAtLocalPosition(desiredPosition);
        }

        /// <summary>
        /// Supplies the selected vehicle's live mass and actuator authority to
        /// the feasibility sampler so custom vehicles receive reachable starts.
        /// </summary>
        void LandingFeasibilityInputs(
            out float vehicleMass,
            out float maxThrust,
            out int activeEngineCount,
            out float maxGimbalDeg,
            out float startupDelay)
        {
            vehicleMass = cfg.dryMass + fuel + rcsPropellant;
            maxThrust = cfg.maxThrust;
            activeEngineCount = cfg.activeEngineCount;
            maxGimbalDeg = cfg.maxGimbal;
            startupDelay = cfg.engineStartupDelay;
        }

        float SampleProfile(InferenceSpawnProfile profile, float a, float b)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            return profile.randomizeEachEpisode ? RandomRange(min, max) : (min + max) * 0.5f;
        }
    }
}
