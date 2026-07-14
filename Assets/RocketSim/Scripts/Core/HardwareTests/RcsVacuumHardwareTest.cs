// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/RcsVacuumHardwareTest.cs
// Purpose: Fires positive, negative, and damping RCS patterns in near-vacuum
// conditions and verifies measurable pitch, yaw, and roll response.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Runs pitch, yaw, and roll RCS commands in near-vacuum conditions and
        /// passes if all axes show angular response.
        /// </summary>
        void DriveRcsVacuumTest()
        {
            ClearCommandBuffers();
            float rollAmp = 1.0f;
            float tiltAmp = 0.9f;

            if (_elapsed >= 0.75f && _elapsed < 2.75f)
                ApplyRcsAxisCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 2.75f && _elapsed < 3.75f)
                ApplyRcsDampingCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 3.75f && _elapsed < 5.75f)
                ApplyRcsAxisCommand(Vector3.up, -rollAmp);
            else if (_elapsed >= 5.75f && _elapsed < 6.75f)
                ApplyRcsDampingCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 6.75f && _elapsed < 8.5f)
                ApplyRcsAxisCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 8.5f && _elapsed < 9.5f)
                ApplyRcsDampingCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 9.5f && _elapsed < 11.25f)
                ApplyRcsAxisCommand(Vector3.right, -tiltAmp);
            else if (_elapsed >= 11.25f && _elapsed < 12.25f)
                ApplyRcsDampingCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 12.25f && _elapsed < 14f)
                ApplyRcsAxisCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 14f && _elapsed < 15f)
                ApplyRcsDampingCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 15f && _elapsed < 16.75f)
                ApplyRcsAxisCommand(Vector3.forward, -tiltAmp);
            else if (_elapsed >= 16.75f && _elapsed < 18f)
                ApplyRcsDampingCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 19f)
            {
                bool vacuum = _maxDynamicPressure < 1f;
                bool roll = _maxLocalRateYDegS > 1.0f;
                bool pitch = _maxLocalRateXDegS > 0.5f;
                bool yaw = _maxLocalRateZDegS > 0.5f;

                bool passed = vacuum && pitch && yaw && roll;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"RCS vacuum passed: pitch {_maxLocalRateXDegS:F1}, yaw {_maxLocalRateZDegS:F1}, roll {_maxLocalRateYDegS:F1} deg/s."
                        : $"RCS vacuum weak: q max {_maxDynamicPressure:F2} Pa, pitch {_maxLocalRateXDegS:F1}, yaw {_maxLocalRateZDegS:F1}, roll {_maxLocalRateYDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }
        /// <summary>
        /// Converts current local angular rate into an opposing RCS command on
        /// the requested axis.
        /// </summary>
        void ApplyRcsDampingCommand(Vector3 localAxis, float limit)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;
            float signedRate = Vector3.Dot(localRate, localAxis.normalized);
            float command = Mathf.Clamp(-signedRate * 0.01f, -limit, limit);
            ApplyRcsAxisCommand(localAxis, command);
        }
        /// <summary>
        /// Routes a local pitch, yaw, or roll command to the matching RCS jet pattern.
        /// </summary>
        void ApplyRcsAxisCommand(Vector3 localAxis, float command)
        {
            if (Mathf.Abs(localAxis.y) > 0.9f)
            {
                ApplyRcsRollCommand(command);
                return;
            }

            if (Mathf.Abs(localAxis.x) > 0.9f)
            {
                ApplyRcsPitchCommand(command);
                return;
            }

            ApplyRcsYawCommand(command);
        }
        /// <summary>
        /// Fires opposing tangential jets to create a pitch couple without roll.
        /// </summary>
        void ApplyRcsPitchCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            // Opposite tangential nozzles produce a pure pitch couple while
            // cancelling their roll moments.
            SetRcsJet(0, command >= 0f
                ? RcsComponent.RcsNozzle.TangentialNegative
                : RcsComponent.RcsNozzle.TangentialPositive, 1f);
            SetRcsJet(1, command >= 0f
                ? RcsComponent.RcsNozzle.TangentialPositive
                : RcsComponent.RcsNozzle.TangentialNegative, 1f);
        }
        /// <summary>
        /// Fires the outboard jet on the appropriate pod to create yaw torque.
        /// </summary>
        void ApplyRcsYawCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            // RCS0 sits on +X and yaws +Z when firing outboard.
            // RCS1 sits on -X and yaws -Z when firing outboard.
            SetRcsJet(command >= 0f ? 0 : 1, RcsComponent.RcsNozzle.Outboard, 1f);
        }
        /// <summary>
        /// Fires matching tangential jets on both pods to create roll torque.
        /// </summary>
        void ApplyRcsRollCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            var nozzle = command >= 0f
                ? RcsComponent.RcsNozzle.TangentialPositive
                : RcsComponent.RcsNozzle.TangentialNegative;

            for (int podIndex = 0; podIndex < RcsComponent.FalconPodCount; podIndex++)
                SetRcsJet(podIndex, nozzle, 1f);
        }
        /// <summary>
        /// Writes a clamped command into one flat RCS jet slot while preserving
        /// any stronger command already assigned this step.
        /// </summary>
        void SetRcsJet(int podIndex, RcsComponent.RcsNozzle nozzle, float command)
        {
            if (_rcs == null) return;

            int jetIndex = RcsComponent.JetIndex(podIndex, nozzle);
            if (jetIndex < 0 || jetIndex >= _rcs.Length) return;

            _rcs[jetIndex] = Mathf.Max(_rcs[jetIndex], Mathf.Clamp01(command));
        }

        sealed class RcsVacuumHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public RcsVacuumHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.RcsVacuum;
            /// <summary>
            /// Advances the scripted vacuum RCS hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveRcsVacuumTest();
        }
    }
}
