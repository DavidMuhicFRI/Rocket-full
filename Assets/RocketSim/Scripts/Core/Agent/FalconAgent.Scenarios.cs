// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Scenarios.cs
// Purpose: Chooses spawn positions, velocities, targets, and helper values for each training or inference scenario.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
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
                SpawnForInference();
                return;
            }

            float rx = RandomRange(-5f, 5f);
            float rz = RandomRange(-5f, 5f);
            switch (envConfig.scenario)
            {
                case ScenarioType.Landing:
                    LandingCurriculumProfile landingProfile = ActiveLandingProfile;
                    float landingTerminalAltitude = ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig);
                    float landingAltitudeMin = Mathf.Min(landingProfile.spawnAltitudeMin, landingProfile.spawnAltitudeMax);
                    float landingAltitudeMax = Mathf.Max(landingProfile.spawnAltitudeMin, landingProfile.spawnAltitudeMax);
                    landingAltitudeMin = Mathf.Max(landingAltitudeMin, landingTerminalAltitude + 15f);
                    landingAltitudeMax = Mathf.Max(landingAltitudeMax, landingAltitudeMin + 10f);
                    float landingVerticalSpeedMin = Mathf.Min(landingProfile.verticalSpeedMin, landingProfile.verticalSpeedMax);
                    float landingVerticalSpeedMax = Mathf.Max(landingProfile.verticalSpeedMin, landingProfile.verticalSpeedMax);
                    float landingAltitude = RandomRange(
                        landingAltitudeMin,
                        landingAltitudeMax);
                    float availableAltitude = landingAltitude - landingTerminalAltitude;
                    float vehicleMass = cfg.dryMass + fuel + rcsPropellant;
                    float gravity = Mathf.Abs(Physics.gravity.y);
                    float netUpwardAcceleration = LandingFeasibility.NetUpwardAcceleration(
                        cfg.maxThrust,
                        cfg.activeEngineCount,
                        vehicleMass,
                        gravity);
                    float recoverableDownwardSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(
                        availableAltitude,
                        netUpwardAcceleration,
                        cfg.engineStartupDelay,
                        gravity);
                    float feasibleDownwardSpeedMin = Mathf.Min(
                        Mathf.Max(0f, landingVerticalSpeedMin),
                        recoverableDownwardSpeed);
                    float feasibleDownwardSpeedMax = Mathf.Min(
                        Mathf.Max(feasibleDownwardSpeedMin, landingVerticalSpeedMax),
                        recoverableDownwardSpeed);
                    float landingDownwardSpeed = RandomRange(
                        feasibleDownwardSpeedMin,
                        feasibleDownwardSpeedMax);

                    LandingFeasibility.HorizontalEnvelope(
                        availableAltitude,
                        landingDownwardSpeed,
                        cfg.maxThrust,
                        cfg.activeEngineCount,
                        vehicleMass,
                        cfg.maxGimbal,
                        cfg.engineStartupDelay,
                        gravity,
                        out float recoverableHorizontalSpeed,
                        out float recoverableHorizontalOffset);
                    float landingOffsetLimit = Mathf.Min(landingProfile.spawnRadius, recoverableHorizontalOffset);
                    float landingHorizontalSpeedLimit = Mathf.Min(
                        landingProfile.horizontalSpeedMax,
                        recoverableHorizontalSpeed);
                    Vector2 landingOffset = RandomInsideUnitCircle() * landingOffsetLimit;
                    Vector2 landingHorizontalVelocity = RandomInsideUnitCircle() * landingHorizontalSpeedLimit;
                    float landingTiltRange = Mathf.Max(0f, landingProfile.spawnTiltRangeDeg);
                    float landingAngularSpeedMax = Mathf.Max(0f, landingProfile.angularSpeedMaxDegS);

                    transform.localRotation = Quaternion.Euler(
                        RandomRange(-landingTiltRange, landingTiltRange),
                        envConfig.landingTargetYawDeg + RandomRange(
                            -landingProfile.spawnYawRangeDeg,
                            landingProfile.spawnYawRangeDeg),
                        RandomRange(-landingTiltRange, landingTiltRange));
                    PlaceLandingCatchFrameAtLocalPosition(new Vector3(landingOffset.x, landingAltitude, landingOffset.y));
                    rb.linearVelocity = new Vector3(
                        landingHorizontalVelocity.x,
                        -landingDownwardSpeed,
                        landingHorizontalVelocity.y);
                    if (landingAngularSpeedMax > 0f)
                    {
                        Vector3 angularAxis = RandomUnitVector3();
                        rb.angularVelocity = angularAxis * RandomRange(0f, landingAngularSpeedMax) * Mathf.Deg2Rad;
                    }
                    break;

                case ScenarioType.Hover:
                    transform.localPosition = new Vector3(rx, 30f, rz);
                    transform.localRotation = Quaternion.Euler(
                        RandomRange(-1f, 1f), 0f, RandomRange(-1f, 1f));
                    rb.linearVelocity = new Vector3(
                        RandomRange(-2f, 2f), 0f, RandomRange(-2f, 2f));
                    break;

                case ScenarioType.HoverTracking:
                    transform.localPosition = new Vector3(rx, 30f, rz);
                    transform.localRotation = Quaternion.Euler(
                        RandomRange(-1f, 1f), 0f, RandomRange(-1f, 1f));
                    break;

                case ScenarioType.Takeoff:
                    transform.localPosition = new Vector3(rx, BaseGroundClearance, rz);
                    transform.localRotation = Quaternion.identity;
                    rb.linearVelocity = Vector3.zero;
                    break;

                case ScenarioType.BellyFlop:
                    transform.localPosition = new Vector3(rx, 100f, rz);
                    transform.localRotation = Quaternion.Euler(
                        85f + RandomRange(-5f, 5f), 0f, 0f);
                    rb.linearVelocity = Vector3.down * RandomRange(8f, 18f);
                    break;
            }
        }

        /// <summary>
        /// Places the rocket using the selected inference spawn profile.
        /// </summary>
        void SpawnForInference()
        {
            InferenceSpawnProfile profile = envConfig.GetInferenceSpawnProfile(envConfig.scenario);
            profile.Clamp();

            float altitude = SampleProfile(profile, profile.altitudeMin, profile.altitudeMax);
            if (envConfig.scenario == ScenarioType.Takeoff && altitude <= 0f)
                altitude = BaseGroundClearance;
            if (envConfig.scenario == ScenarioType.Landing)
                altitude = Mathf.Max(altitude, ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig) + 15f);

            Vector2 offset = profile.randomizeEachEpisode
                ? RandomInsideUnitCircle() * profile.horizontalOffsetMax
                : Vector2.right * profile.horizontalOffsetMax;

            float planarSpeed = SampleProfile(profile, profile.horizontalSpeedMin, profile.horizontalSpeedMax);
            Vector2 planarDirection = profile.randomizeEachEpisode
                ? RandomInsideUnitCircle().normalized
                : Vector2.right;
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

            if (envConfig.scenario == ScenarioType.Landing)
            {
                float terminalAltitude = ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig);
                float availableAltitude = altitude - terminalAltitude;
                float vehicleMass = cfg.dryMass + fuel + rcsPropellant;
                float gravity = Mathf.Abs(Physics.gravity.y);
                float netUpwardAcceleration = LandingFeasibility.NetUpwardAcceleration(
                    cfg.maxThrust,
                    cfg.activeEngineCount,
                    vehicleMass,
                    gravity);
                float maxDownwardSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(
                    availableAltitude,
                    netUpwardAcceleration,
                    cfg.engineStartupDelay,
                    gravity);
                verticalSpeed = -Mathf.Min(Mathf.Max(0f, -verticalSpeed), maxDownwardSpeed);

                LandingFeasibility.HorizontalEnvelope(
                    availableAltitude,
                    -verticalSpeed,
                    cfg.maxThrust,
                    cfg.activeEngineCount,
                    vehicleMass,
                    cfg.maxGimbal,
                    cfg.engineStartupDelay,
                    gravity,
                    out float maxHorizontalSpeed,
                    out float maxHorizontalOffset);
                planarSpeed = Mathf.Min(planarSpeed, maxHorizontalSpeed);
                offset = Vector2.ClampMagnitude(offset, maxHorizontalOffset);
                PlaceLandingCatchFrameAtLocalPosition(new Vector3(offset.x, altitude, offset.y));
            }
            else
            {
                transform.localPosition = new Vector3(offset.x, altitude, offset.y);
            }

            rb.linearVelocity = new Vector3(
                planarDirection.x * planarSpeed,
                verticalSpeed,
                planarDirection.y * planarSpeed);

            if (profile.randomizeEachEpisode && profile.angularSpeedMaxDegS > 0f)
            {
                Vector3 axis = RandomUnitVector3();
                float speedDegS = RandomRange(0f, profile.angularSpeedMaxDegS);
                rb.angularVelocity = axis * speedDegS * Mathf.Deg2Rad;
            }
            else
            {
                rb.angularVelocity = Vector3.zero;
            }
        }

        float SampleProfile(InferenceSpawnProfile profile, float a, float b)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            return profile.randomizeEachEpisode ? RandomRange(min, max) : (min + max) * 0.5f;
        }

        /// <summary>
        /// Returns signed yaw error between the rocket's horizontal heading and
        /// the configured landing target yaw.
        /// </summary>
        float SignedHeadingErrorDeg()
        {
            Vector3 currentHeading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (currentHeading.sqrMagnitude < 0.0001f)
                currentHeading = Vector3.ProjectOnPlane(transform.right, Vector3.up);

            Vector3 targetHeading = Quaternion.Euler(0f, envConfig.landingTargetYawDeg, 0f) * Vector3.forward;
            return Vector3.SignedAngle(targetHeading, currentHeading.normalized, Vector3.up);
        }

        /// <summary>
        /// Returns the target pad position projected into the XZ plane.
        /// </summary>
        Vector2 TargetPlanar()
        {
            Vector3 target = targetPad ? targetPad.localPosition : Vector3.zero;
            return new Vector2(target.x, target.z);
        }

        /// <summary>
        /// Updates hover-track stable time and reports whether the moving target was captured this step.
        /// </summary>
        bool UpdateHoverTrackSuccess()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking)
            {
                _hoverTrackStableTime = 0f;
                return false;
            }

            if (IsHoverTrackHoverReady())
                _hoverTrackStableTime += Time.fixedDeltaTime;
            else
                _hoverTrackStableTime = 0f;

            return _hoverTrackStableTime >= Mathf.Max(0f, envConfig.hoverTrackSuccessHoldTime);
        }

        /// <summary>
        /// Returns whether the rocket is inside the moving-target settle window
        /// with low speed, small tilt, and calm angular rates.
        /// </summary>
        bool IsHoverTrackHoverReady()
        {
            Vector3 goal = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig);
            Vector3 error = transform.localPosition - goal;
            Vector2 horizontalError = new Vector2(error.x, error.z);
            Vector2 horizontalVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float settleRadius = Mathf.Max(envConfig.hoverTrackSettleRadius, 0.5f);
            float maxSpeed = Mathf.Max(envConfig.hoverTrackSuccessMaxSpeed, 0.1f);
            float maxTilt = Mathf.Max(envConfig.hoverTrackSuccessMaxTiltDeg, 0.1f);

            return horizontalError.magnitude <= settleRadius &&
                   Mathf.Abs(error.y) <= 3f &&
                   horizontalVelocity.magnitude <= maxSpeed &&
                   Mathf.Abs(rb.linearVelocity.y) <= maxSpeed &&
                   Vector3.Angle(transform.up, Vector3.up) <= maxTilt &&
                   localAngularVelocity.magnitude <= 25f;
        }

        /// <summary>
        /// Scores hover-track settling quality from position error, velocity,
        /// tilt, and angular rate, normalized to 0..1 for telemetry.
        /// </summary>
        float HoverTrackSettleQuality(
            float horizontalError,
            float verticalError,
            float horizontalSpeed,
            float verticalSpeed,
            float tiltDeg,
            float angularRateDegS)
        {
            float settleRadius = Mathf.Max(envConfig.hoverTrackSettleRadius, 0.5f);
            float maxSpeed = Mathf.Max(envConfig.hoverTrackSuccessMaxSpeed, 0.1f);
            float maxTilt = Mathf.Max(envConfig.hoverTrackSuccessMaxTiltDeg, 0.1f);

            float horizontalPosition = 1f - Mathf.Clamp01(horizontalError / settleRadius);
            float verticalPosition = 1f - Mathf.Clamp01(Mathf.Abs(verticalError) / 3f);
            float horizontalCalm = 1f - Mathf.Clamp01(horizontalSpeed / maxSpeed);
            float verticalCalm = 1f - Mathf.Clamp01(Mathf.Abs(verticalSpeed) / maxSpeed);
            float attitude = 1f - Mathf.Clamp01(tiltDeg / maxTilt);
            float rotationCalm = 1f - Mathf.Clamp01(angularRateDegS / 25f);

            return (horizontalPosition + verticalPosition + horizontalCalm + verticalCalm + attitude + rotationCalm) / 6f;
        }

        /// <summary>
        /// Selects the next hover-tracking target, forcing enough travel distance
        /// to make target transitions meaningful.
        /// </summary>
        void RandomizeTarget()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking)
            {
                Vector3 fixedTarget = targetPad.localPosition;
                fixedTarget.x = 0f;
                fixedTarget.z = 0f;
                targetPad.localPosition = fixedTarget;
                _hoverTrackSegmentElapsedTime = 0f;
                return;
            }

            float r = envConfig.targetMoveRadius;
            Vector3 selected = targetPad.localPosition;
            float minTravelDistance = envConfig.scenario == ScenarioType.HoverTracking
                ? Mathf.Min(r, Mathf.Max(envConfig.hoverTrackSettleRadius * 1.2f, envConfig.hoverTrackSettleRadius + 1f))
                : 0f;
            float bestDistance = -1f;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 candidate = new Vector3(RandomRange(-r, r), targetPad.localPosition.y, RandomRange(-r, r));
                Vector2 fromRocket = new Vector2(
                    candidate.x - transform.localPosition.x,
                    candidate.z - transform.localPosition.z);
                float distance = fromRocket.magnitude;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    selected = candidate;
                }

                if (distance >= minTravelDistance)
                    break;
            }

            targetPad.localPosition = selected;
            _hoverTrackSegmentElapsedTime = 0f;
            _hoverTrackSegmentStartDistance = new Vector2(
                targetPad.localPosition.x - transform.localPosition.x,
                targetPad.localPosition.z - transform.localPosition.z).magnitude;
        }

    }
}
