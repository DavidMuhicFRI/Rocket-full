// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Guidance.cs
// Purpose: Measures heading and manages fixed or moving task targets.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>Returns signed horizontal heading error from the task target yaw.</summary>
        float SignedHeadingErrorDeg()
        {
            Vector3 currentHeading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (currentHeading.sqrMagnitude < 0.0001f)
                currentHeading = Vector3.ProjectOnPlane(transform.right, Vector3.up);

            Vector3 targetHeading = Quaternion.Euler(0f, envConfig.landingTargetYawDeg, 0f) * Vector3.forward;
            return Vector3.SignedAngle(targetHeading, currentHeading.normalized, Vector3.up);
        }

        /// <summary>Updates hover-track stable time and reports a newly captured target.</summary>
        bool UpdateHoverTrackSuccess()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking)
            {
                _hoverTrackStableTime = 0f;
                _hoverTrackCaptureLatched = false;
                return false;
            }

            if (!IsHoverTrackHoverReady())
            {
                _hoverTrackStableTime = 0f;
                _hoverTrackCaptureLatched = false;
                return false;
            }
            if (_hoverTrackCaptureLatched) return false;

            _hoverTrackStableTime += Time.fixedDeltaTime;
            TerminationParameters criteria = envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            float holdSeconds = criteria.trackingCaptureHoldSeconds.At(_objectiveDifficulty01);
            if (_hoverTrackStableTime < Mathf.Max(0f, holdSeconds)) return false;

            _hoverTrackCaptureLatched = true;
            return true;
        }

        /// <summary>Checks the complete hover-target settle window.</summary>
        bool IsHoverTrackHoverReady()
        {
            Vector3 goal = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig);
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();
            Vector3 guidanceVelocity = ScenarioReferenceVelocity();
            Vector3 error = guidancePosition - goal;
            Vector2 horizontalError = new(error.x, error.z);
            Vector2 horizontalVelocity = new(guidanceVelocity.x, guidanceVelocity.z);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            TerminationParameters criteria = envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            float difficulty = _objectiveDifficulty01;
            float settleRadius = Mathf.Max(criteria.trackingCaptureRadiusM.At(difficulty), 0.01f);
            float maxVerticalError = Mathf.Max(criteria.trackingCaptureMaxVerticalErrorM.At(difficulty), 0.01f);
            float maxHorizontalSpeed = Mathf.Max(criteria.trackingCaptureMaxHorizontalSpeedMps.At(difficulty), 0.01f);
            float maxVerticalSpeed = Mathf.Max(criteria.trackingCaptureMaxVerticalSpeedMps.At(difficulty), 0.01f);
            float maxTilt = Mathf.Max(criteria.trackingCaptureMaxTiltDeg.At(difficulty), 0.01f);
            float maxAngularRate = Mathf.Max(criteria.trackingCaptureMaxAngularRateDegS.At(difficulty), 0.01f);

            return horizontalError.magnitude <= settleRadius &&
                   Mathf.Abs(error.y) <= maxVerticalError &&
                   horizontalVelocity.magnitude <= maxHorizontalSpeed &&
                   Mathf.Abs(guidanceVelocity.y) <= maxVerticalSpeed &&
                   Vector3.Angle(transform.up, Vector3.up) <= maxTilt &&
                   localAngularVelocity.magnitude <= maxAngularRate;
        }

        /// <summary>Scores hover-target settling quality from zero to one.</summary>
        float HoverTrackSettleQuality(
            float horizontalError,
            float verticalError,
            float horizontalSpeed,
            float verticalSpeed,
            float tiltDeg,
            float angularRateDegS)
        {
            TerminationParameters criteria = envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            float difficulty = _objectiveDifficulty01;
            float settleRadius = Mathf.Max(criteria.trackingCaptureRadiusM.At(difficulty), 0.01f);
            float maxVerticalError = Mathf.Max(criteria.trackingCaptureMaxVerticalErrorM.At(difficulty), 0.01f);
            float maxHorizontalSpeed = Mathf.Max(criteria.trackingCaptureMaxHorizontalSpeedMps.At(difficulty), 0.01f);
            float maxVerticalSpeed = Mathf.Max(criteria.trackingCaptureMaxVerticalSpeedMps.At(difficulty), 0.01f);
            float maxTilt = Mathf.Max(criteria.trackingCaptureMaxTiltDeg.At(difficulty), 0.01f);
            float maxAngularRate = Mathf.Max(criteria.trackingCaptureMaxAngularRateDegS.At(difficulty), 0.01f);

            float horizontalPosition = 1f - Mathf.Clamp01(horizontalError / settleRadius);
            float verticalPosition = 1f - Mathf.Clamp01(Mathf.Abs(verticalError) / maxVerticalError);
            float horizontalCalm = 1f - Mathf.Clamp01(horizontalSpeed / maxHorizontalSpeed);
            float verticalCalm = 1f - Mathf.Clamp01(Mathf.Abs(verticalSpeed) / maxVerticalSpeed);
            float attitude = 1f - Mathf.Clamp01(tiltDeg / maxTilt);
            float rotationCalm = 1f - Mathf.Clamp01(angularRateDegS / maxAngularRate);
            return (horizontalPosition + verticalPosition + horizontalCalm + verticalCalm + attitude + rotationCalm) / 6f;
        }

        /// <summary>Initializes the fixed or moving target for a new episode.</summary>
        void InitializeTargetForEpisode()
        {
            if (!targetPad) return;

            if (envConfig.scenario == ScenarioType.HoverTracking)
            {
                PlaceNewHoverTrackingTarget();
                return;
            }

            Vector3 target = targetPad.localPosition;
            target.x = 0f;
            target.z = 0f;
            targetPad.localPosition = target;
            ResetHoverTrackSegment();
        }

        /// <summary>Selects a sufficiently distant target after a capture.</summary>
        void MoveTargetAfterCapture()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking || !targetPad)
                return;

            PlaceNewHoverTrackingTarget();
        }

        /// <summary>
        /// Spawns a target far enough from the rocket that it must move to reach it (acquisition on start bug)
        /// </summary>
        void PlaceNewHoverTrackingTarget()
        {
            if (!targetPad) return;

            float radius = envConfig.IsStandardEvaluation ? envConfig.HoverTrackMoveRadiusAt(_objectiveDifficulty01) : Mathf.Max(0f, envConfig.targetMoveRadius);
            TerminationParameters criteria = envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            float settleRadius = Mathf.Max(criteria.trackingCaptureRadiusM.At(_objectiveDifficulty01), 0.01f);
            Vector3 selected = targetPad.localPosition;
            float minimumTravel = Mathf.Min(radius, Mathf.Max(settleRadius * 1.2f, settleRadius + 1f));
            float bestDistance = -1f;
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = RandomInsideUnitCircle() * radius;
                Vector3 candidate = new(offset.x, targetPad.localPosition.y, offset.y);
                Vector2 fromRocket = new(candidate.x - guidancePosition.x, candidate.z - guidancePosition.z);
                float distance = fromRocket.magnitude;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    selected = candidate;
                }
                if (distance >= minimumTravel) break;
            }

            targetPad.localPosition = selected;
            ResetHoverTrackSegment();
        }

        void ResetHoverTrackSegment()
        {
            _hoverTrackSegmentElapsedTime = 0f;
            _hoverTrackStableTime = 0f;
            _hoverTrackCaptureLatched = false;
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();
            _hoverTrackSegmentStartDistance = new Vector2(targetPad.localPosition.x - guidancePosition.x, targetPad.localPosition.z - guidancePosition.z).magnitude;
        }
    }
}
