// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.LandingCapture.cs
// Purpose: Tracks the generated non-physical chopstick target, capture envelope,
// and stable kinematic capture state.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Ensures the reusable-booster catch reference exists and follows the
        /// configured grid-fin station. Existing prefabs reuse the legacy top
        /// control point through FormerlySerializedAs on the field.
        /// </summary>
        void ConfigureCatchFrame()
        {
            if (!catchFrame)
            {
                catchFrame = transform.Find("CatchFrame") ?? transform.Find("Top_Control_Point");
                if (!catchFrame)
                {
                    var frameObject = new GameObject("CatchFrame");
                    catchFrame = frameObject.transform;
                    catchFrame.SetParent(transform, false);
                }
            }

            catchFrame.name = "CatchFrame";
            if (catchFrame.parent == transform)
            {
                catchFrame.localPosition = new Vector3(0f, cfg.finLocalY, 0f);
                catchFrame.localRotation = Quaternion.identity;
            }
            else
            {
                catchFrame.position = transform.TransformPoint(0f, cfg.finLocalY, 0f);
                catchFrame.rotation = transform.rotation;
            }
        }

        /// <summary>Returns the active scenario guidance point in world space.</summary>
        Vector3 ScenarioReferenceWorldPosition()
        {
            return envConfig != null && envConfig.scenario == ScenarioType.Landing && catchFrame
                ? catchFrame.position
                : transform.position;
        }

        /// <summary>Returns the active scenario guidance point in training-area local space.</summary>
        Vector3 ScenarioReferenceLocalPosition()
        {
            Vector3 worldPosition = ScenarioReferenceWorldPosition();
            return transform.parent ? transform.parent.InverseTransformPoint(worldPosition) : worldPosition;
        }

        /// <summary>
        /// Returns velocity at the active guidance point. For landing this adds
        /// the rotational velocity of the upper catch frame to root translation.
        /// </summary>
        Vector3 ScenarioReferenceVelocity()
        {
            if (envConfig != null && envConfig.scenario == ScenarioType.Landing && catchFrame && rb)
                return rb.GetPointVelocity(catchFrame.position);
            return rb ? rb.linearVelocity : Vector3.zero;
        }

        /// <summary>
        /// Positions the rocket root so its catch frame, rather than its engine
        /// plane, starts at the requested landing-scenario coordinate.
        /// </summary>
        void PlaceLandingCatchFrameAtLocalPosition(Vector3 desiredCatchPosition)
        {
            ConfigureCatchFrame();
            Vector3 worldOffset = catchFrame.position - transform.position;
            Vector3 localOffset = transform.parent
                ? transform.parent.InverseTransformVector(worldOffset)
                : worldOffset;
            transform.localPosition = desiredCatchPosition - localOffset;
        }

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
        /// Reconfigures the generated visual platform and logical capture box
        /// from the episode's frozen curriculum profile.
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
                ActiveLandingProfile.platformHalfSize);
        }

        /// <summary>Clears per-episode capture and stability state.</summary>
        void ResetLandingPlatformState()
        {
            _landingPlatformInsideCapture = false;
            _landingPlatformStable = false;
            _landingPlatformStableTime = 0f;
        }

        /// <summary>
        /// Updates capture-envelope membership and stable-hold time using only
        /// kinematics. The target never applies collision forces to the rocket.
        /// </summary>
        void UpdateLandingPlatformState(float dt)
        {
            if (envConfig == null ||
                envConfig.scenario != ScenarioType.Landing ||
                !envConfig.landingPlatformEnabled ||
                !_landingPlatform)
            {
                _landingPlatformInsideCapture = false;
                _landingPlatformStable = false;
                _landingPlatformStableTime = 0f;
                return;
            }

            _landingPlatformInsideCapture = _landingPlatform.ContainsWorldPoint(ScenarioReferenceWorldPosition());
            bool platformReady = LandingPlatformKinematicsReady();

            if (platformReady)
                _landingPlatformStableTime += Mathf.Max(0f, dt);
            else
                _landingPlatformStableTime = 0f;

            _landingPlatformStable = platformReady &&
                _landingPlatformStableTime >= Mathf.Max(0f, ActiveLandingProfile.platformStableHoldTime);
        }

        /// <summary>
        /// Returns whether the rocket currently satisfies all capture-platform
        /// kinematic limits while inside the logical platform envelope.
        /// </summary>
        bool LandingPlatformKinematicsReady()
        {
            if (!_landingPlatformInsideCapture)
                return false;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            LandingCurriculumProfile landing = ActiveLandingProfile;
            float tiltLimitDeg = Mathf.Min(
                landing.successMaxTiltDeg,
                Mathf.Acos(0.94f) * Mathf.Rad2Deg);

            return LandingCaptureEvaluator.IsKinematicallyReady(
                terms,
                Vector3.Angle(transform.up, Vector3.up),
                tiltLimitDeg,
                landing.successRadius,
                landing.successMaxSpeed,
                landing.successMaxVerticalSpeed,
                landing.successMaxHorizontalSpeed,
                landing.successMaxAngularRateDegS,
                landing.successMaxYawErrorDeg);
        }
    }
}
