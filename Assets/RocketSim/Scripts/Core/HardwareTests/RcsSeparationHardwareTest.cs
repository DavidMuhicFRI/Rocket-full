// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/RcsSeparationHardwareTest.cs
// Purpose: Simulates a disturbed post-separation rocket in thin air and checks
// whether RCS can reorient it toward a target and settle angular motion.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Simulates post-separation reorientation and passes if RCS reduces
        /// pointing error and angular rate in thin air.
        /// </summary>
        void DriveRcsSeparationTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 0.75f && _elapsed < 4f)
            {
                ApplyRcsRateDampingCommand(0.95f, 0.08f);
            }
            else if (_elapsed >= 4f && _elapsed < 25f)
            {
                ApplyRcsPointingCommand(_separationTargetUp, 2.4f, 0.035f, 1f, 0.65f);
            }
            else if (_elapsed >= 25f && _elapsed < 35f)
            {
                ApplyRcsPointingCommand(_separationTargetUp, 0.85f, 0.085f, 0.65f, 0.55f);
            }
            else if (_elapsed >= 36f)
            {
                bool thinAir = _maxDynamicPressure < 250f;
                bool initialDisturbance = _initialAngularRateDegS > 0.5f;
                bool reoriented = _separationInitialPointingErrorDeg - _minSeparationPointingErrorDeg > 45f &&
                                  _finalSeparationPointingErrorDeg < 30f;
                bool settled = _finalSeparationAngularRateDegS < 10f;

                bool passed = thinAir && initialDisturbance && reoriented && settled;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"Stage separation RCS passed: target error {_finalSeparationPointingErrorDeg:F1} deg, rate {_finalSeparationAngularRateDegS:F1} deg/s, q max {_maxDynamicPressure:F1} Pa."
                        : $"Stage separation RCS weak: target error {_finalSeparationPointingErrorDeg:F1} deg, best {_minSeparationPointingErrorDeg:F1} deg, rate {_finalSeparationAngularRateDegS:F1} deg/s, q max {_maxDynamicPressure:F1} Pa.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }
        /// <summary>
        /// Converts world-space pointing error plus local rate damping into RCS
        /// pitch, yaw, and roll commands.
        /// </summary>
        void ApplyRcsPointingCommand(
            Vector3 desiredWorldUp,
            float pointingGain,
            float rateDampingGain,
            float tiltLimit,
            float rollLimit)
        {
            if (desiredWorldUp.sqrMagnitude < 0.1f) return;

            Vector3 currentUp = Agent.transform.up;
            Vector3 targetUp = desiredWorldUp.normalized;
            Vector3 cross = Vector3.Cross(currentUp, targetUp);
            float errorAngleRad = Vector3.Angle(currentUp, targetUp) * Mathf.Deg2Rad;
            Vector3 errorWorld = cross.sqrMagnitude > 0.000001f
                ? cross.normalized * errorAngleRad
                : Vector3.zero;
            Vector3 localError = Agent.transform.InverseTransformDirection(errorWorld);
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;

            float pitch = Mathf.Clamp(localError.x * pointingGain - localRate.x * rateDampingGain, -tiltLimit, tiltLimit);
            float yaw = Mathf.Clamp(localError.z * pointingGain - localRate.z * rateDampingGain, -tiltLimit, tiltLimit);
            float roll = Mathf.Clamp(-localRate.y * rateDampingGain, -rollLimit, rollLimit);

            ApplyRcsAxisCommand(Vector3.right, pitch);
            ApplyRcsAxisCommand(Vector3.forward, yaw);
            ApplyRcsAxisCommand(Vector3.up, roll);
        }
        /// <summary>
        /// Writes RCS commands that oppose local angular velocity on all axes.
        /// </summary>
        void ApplyRcsRateDampingCommand(float limit, float rateDampingGain)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;

            ApplyRcsAxisCommand(Vector3.right, Mathf.Clamp(-localRate.x * rateDampingGain, -limit, limit));
            ApplyRcsAxisCommand(Vector3.up, Mathf.Clamp(-localRate.y * rateDampingGain, -limit, limit));
            ApplyRcsAxisCommand(Vector3.forward, Mathf.Clamp(-localRate.z * rateDampingGain, -limit, limit));
        }
        /// <summary>
        /// Chooses a retrograde-facing target up vector from the separation velocity.
        /// </summary>
        static Vector3 ComputeSeparationTargetUp(Vector3 separationVelocity)
        {
            Vector3 retrograde = -separationVelocity;
            if (retrograde.sqrMagnitude < 1f)
                retrograde = new Vector3(-1f, -0.1f, 0f);

            return retrograde.normalized;
        }

        sealed class RcsSeparationHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public RcsSeparationHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.RcsSeparation;
            /// <summary>
            /// Advances the scripted stage-separation RCS hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveRcsSeparationTest();
        }
    }
}
