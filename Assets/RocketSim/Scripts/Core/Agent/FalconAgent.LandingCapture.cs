// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.LandingCapture.cs
// Purpose: Tracks the generated chopstick landing platform, capture envelope, and platform contact diagnostics.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Random = UnityEngine.Random;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Creates or finds the generated landing capture platform near the
        /// target pad when landing mode needs it.
        /// </summary>
        void EnsureLandingPlatform()
        {
            if (_landingPlatform || !targetPad)
                return;

            Transform parent = targetPad.parent ? targetPad.parent : transform.parent;
            if (!parent)
                return;

            _landingPlatform = LandingPlatformFactory.Ensure(parent);
        }

        /// <summary>
        /// Reconfigures the generated landing platform geometry from current scenario and curriculum thresholds.
        /// </summary>
        void UpdateLandingPlatformGeometry()
        {
            if (envConfig == null || envConfig.scenario != ScenarioType.Landing || !envConfig.landingPlatformEnabled)
            {
                if (_landingPlatform)
                    _landingPlatform.DisablePlatform();
                return;
            }

            EnsureLandingPlatform();
            if (!_landingPlatform || !targetPad)
                return;

            Vector3 localCenter = new(
                targetPad.localPosition.x,
                ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig),
                targetPad.localPosition.z);

            _landingPlatform.Configure(
                localCenter,
                envConfig.landingTargetYawDeg,
                envConfig.CurrentLandingPlatformHalfSize,
                envConfig.CurrentLandingPlatformTriggerActive,
                envConfig.CurrentLandingPlatformPhysicalActive);
        }

        /// <summary>
        /// Clears per-episode capture, stability, and physical-contact diagnostics
        /// for the generated landing platform.
        /// </summary>
        void ResetLandingPlatformState()
        {
            _landingPlatformInsideCapture = false;
            _landingPlatformStable = false;
            _landingPlatformStableTime = 0f;
            _landingPlatformPhysicalContactCount = 0;
            _landingPlatformLastContactSpeed = 0f;
        }

        /// <summary>
        /// Updates capture-envelope membership and stable-hold time for the
        /// landing platform using the current rocket kinematics.
        /// </summary>
        void UpdateLandingPlatformState(float dt)
        {
            if (envConfig == null ||
                envConfig.scenario != ScenarioType.Landing ||
                !envConfig.landingPlatformEnabled ||
                !envConfig.CurrentLandingPlatformTriggerActive ||
                !_landingPlatform)
            {
                _landingPlatformInsideCapture = false;
                _landingPlatformStable = false;
                _landingPlatformStableTime = 0f;
                return;
            }

            _landingPlatformInsideCapture = _landingPlatform.ContainsWorldPoint(transform.position);
            bool platformReady = LandingPlatformKinematicsReady();

            if (platformReady)
                _landingPlatformStableTime += Mathf.Max(0f, dt);
            else
                _landingPlatformStableTime = 0f;

            _landingPlatformStable = platformReady &&
                _landingPlatformStableTime >= Mathf.Max(0f, envConfig.CurrentLandingPlatformStableHoldTime);
        }

        /// <summary>
        /// Returns whether the rocket currently satisfies all capture-platform
        /// kinematic limits while inside the platform envelope.
        /// </summary>
        bool LandingPlatformKinematicsReady()
        {
            if (!_landingPlatformInsideCapture)
                return false;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            float tiltLimitDeg = Mathf.Min(
                envConfig.CurrentLandingSuccessMaxTiltDeg,
                Mathf.Acos(0.94f) * Mathf.Rad2Deg);

            return LandingCaptureEvaluator.IsKinematicallyReady(
                terms,
                Vector3.Angle(transform.up, Vector3.up),
                tiltLimitDeg,
                envConfig.CurrentLandingSuccessRadius,
                envConfig.CurrentLandingSuccessMaxSpeed,
                envConfig.CurrentLandingSuccessMaxVerticalSpeed,
                envConfig.CurrentLandingSuccessMaxHorizontalSpeed,
                envConfig.CurrentLandingSuccessMaxAngularRateDegS,
                envConfig.CurrentLandingSuccessMaxYawErrorDeg);
        }

        /// <summary>
        /// Records the start of a physical contact with the active landing
        /// platform surface for landing diagnostics.
        /// </summary>
        void OnCollisionEnter(Collision collision)
        {
            if (!TryGetLandingPlatformPart(collision.collider, out var part) || !part.physicalSurface)
                return;

            _landingPlatformPhysicalContactCount++;
            _landingPlatformLastContactSpeed = Mathf.Max(
                _landingPlatformLastContactSpeed,
                collision.relativeVelocity.magnitude);
        }

        /// <summary>
        /// Records or handles an ongoing collision.
        /// </summary>
        void OnCollisionStay(Collision collision)
        {
            if (!TryGetLandingPlatformPart(collision.collider, out var part) || !part.physicalSurface)
                return;

            _landingPlatformLastContactSpeed = Mathf.Max(
                _landingPlatformLastContactSpeed,
                collision.relativeVelocity.magnitude);
        }

        /// <summary>
        /// Records the end of a physical contact with the active landing
        /// platform surface for landing diagnostics.
        /// </summary>
        void OnCollisionExit(Collision collision)
        {
            if (!TryGetLandingPlatformPart(collision.collider, out var part) || !part.physicalSurface)
                return;

            _landingPlatformPhysicalContactCount = Mathf.Max(0, _landingPlatformPhysicalContactCount - 1);
        }

        /// <summary>
        /// Attempts to get landing platform part and reports whether it succeeded.
        /// </summary>
        bool TryGetLandingPlatformPart(Collider collider, out LandingPlatformPart part)
        {
            part = collider ? collider.GetComponentInParent<LandingPlatformPart>() : null;
            return part && part.platform && part.platform == _landingPlatform;
        }

    }
}
