// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.ChopstickLanding.cs
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
        /// configured grid-fin station.
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
            if (envConfig != null && envConfig.scenario == ScenarioType.ChopstickLanding && catchFrame)
                return catchFrame.position;
            if (envConfig != null && envConfig.scenario == ScenarioType.LegLanding && _landingLegs && _landingLegs.FeetFrame)
                return _landingLegs.FeetFrame.position;
            return transform.position;
        }

        /// <summary>Returns the active scenario guidance point in training-area local space.</summary>
        Vector3 ScenarioReferenceLocalPosition()
        {
            Vector3 worldPosition = ScenarioReferenceWorldPosition();
            return transform.parent ? transform.parent.InverseTransformPoint(worldPosition) : worldPosition;
        }

        /// <summary>
        /// Returns velocity at the active guidance point. Landing scenarios use
        /// point velocity at CatchFrame or FeetFrame, including body rotation.
        /// </summary>
        Vector3 ScenarioReferenceVelocity()
        {
            if (envConfig != null && envConfig.scenario == ScenarioType.ChopstickLanding && catchFrame && rb)
                return rb.GetPointVelocity(catchFrame.position);
            if (envConfig != null && envConfig.scenario == ScenarioType.LegLanding &&
                _landingLegs && _landingLegs.FeetFrame && rb)
                return rb.GetPointVelocity(_landingLegs.FeetFrame.position);
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
        /// Creates or finds the generated chopstick capture platform near the
        /// target pad when the catch scenario needs it.
        /// </summary>
        void EnsureChopstickPlatform()
        {
            if (_chopstickPlatform || !targetPad)
                return;

            Transform parent = targetPad.parent ? targetPad.parent : transform.parent;
            if (!parent)
                return;

            _chopstickPlatform = ChopstickCatchPlatformFactory.Ensure(parent);
        }

        /// <summary>
        /// Reconfigures the generated visual platform and logical capture box
        /// from the episode's frozen curriculum profile.
        /// </summary>
        void UpdateChopstickPlatformGeometry()
        {
            if (envConfig == null || envConfig.scenario != ScenarioType.ChopstickLanding || !envConfig.landingPlatformEnabled)
            {
                if (_chopstickPlatform)
                    _chopstickPlatform.DisablePlatform();
                return;
            }

            EnsureChopstickPlatform();
            if (!_chopstickPlatform || !targetPad)
                return;

            Vector3 localCenter = new(
                targetPad.localPosition.x,
                ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig),
                targetPad.localPosition.z);

            _chopstickPlatform.Configure(
                localCenter,
                envConfig.landingTargetYawDeg,
                ActiveLandingProfile.platformHalfSize);
        }

        /// <summary>Clears per-episode capture and stability state.</summary>
        void ResetChopstickPlatformState()
        {
            _landingPlatformInsideCapture = false;
            _landingPlatformStable = false;
            _landingPlatformBecameStable = false;
            _landingPlatformStableTime = 0f;
            _landingEpisodeStartAltitude = 0f;
            _landingEpisodeFlyawayAltitude = 0f;
        }

        /// <summary>
        /// Captures the sampled CatchFrame start height and the configured
        /// upward escape ceiling used by diagnostics for this episode.
        /// </summary>
        void CaptureLandingEpisodeStartAltitude()
        {
            if (envConfig == null || !envConfig.scenario.IsLanding())
            {
                _landingEpisodeStartAltitude = 0f;
                _landingEpisodeFlyawayAltitude = 0f;
                return;
            }

            _landingEpisodeStartAltitude = ScenarioReferenceLocalPosition().y;
            TerminationParameters termination =
                envConfig.GetTrainingObjective(envConfig.scenario).terminations;
            _landingEpisodeFlyawayAltitude =
                _landingEpisodeStartAltitude + termination.maximumAltitudeAboveStartM;
        }

        /// <summary>
        /// Updates capture-envelope membership and stable-hold time using only
        /// kinematics. The target never applies collision forces to the rocket.
        /// </summary>
        void UpdateChopstickPlatformState(float dt)
        {
            _landingPlatformBecameStable = false;
            if (envConfig == null ||
                envConfig.scenario != ScenarioType.ChopstickLanding ||
                !envConfig.landingPlatformEnabled ||
                !_chopstickPlatform)
            {
                _landingPlatformInsideCapture = false;
                _landingPlatformStable = false;
                _landingPlatformStableTime = 0f;
                return;
            }

            bool wasStable = _landingPlatformStable;
            _landingPlatformInsideCapture = _chopstickPlatform.ContainsWorldPoint(ScenarioReferenceWorldPosition());
            bool platformReady = ChopstickPlatformKinematicsReady();

            if (platformReady)
                _landingPlatformStableTime += Mathf.Max(0f, dt);
            else
                _landingPlatformStableTime = 0f;

            float requiredHold = envConfig
                .GetTrainingObjective(ScenarioType.ChopstickLanding)
                .terminations.landingStableHoldSeconds.At(_objectiveDifficulty01);
            _landingPlatformStable = platformReady &&
                _landingPlatformStableTime >= Mathf.Max(0f, requiredHold);
            _landingPlatformBecameStable = !wasStable && _landingPlatformStable;
        }

        /// <summary>
        /// Returns whether the rocket currently satisfies all capture-platform
        /// kinematic limits while inside the logical platform envelope.
        /// </summary>
        bool ChopstickPlatformKinematicsReady()
        {
            if (!_landingPlatformInsideCapture)
                return false;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            LandingCurriculumProfile landing = ActiveLandingProfile;

            return ChopstickCaptureEvaluator.IsKinematicallyReady(
                terms,
                Vector3.Angle(transform.up, Vector3.up),
                landing.successMaxTiltDeg,
                landing.successRadius,
                landing.successMaxSpeed,
                landing.successMaxVerticalSpeed,
                landing.successMaxHorizontalSpeed,
                landing.successMaxAngularRateDegS,
                landing.successMaxYawErrorDeg);
        }
    }
}
