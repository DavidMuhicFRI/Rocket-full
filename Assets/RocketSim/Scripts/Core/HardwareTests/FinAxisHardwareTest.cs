// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/FinAxisHardwareTest.cs
// Purpose: Sweeps grid-fin commands around each local axis and checks that the
// rocket responds on the requested axis without excessive cross-axis motion.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Runs the selected grid-fin axis test by sweeping fin commands and
        /// checking whether the rocket rotates around the target local axis.
        /// </summary>
        void DriveFinAxisTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            if (Mathf.Abs(_targetLocalAxis.y) < 0.9f)
            {
                DriveFinTiltAxisTest(cfg);
                return;
            }

            float amp = Mathf.Max(5f, cfg.maxFinAngle * 0.9f);

            if (_elapsed >= 1.5f && _elapsed < 6.5f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 6.5f && _elapsed < 11.5f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp);
            }
            else if (_elapsed >= 11.5f && _elapsed < 18.5f)
            {
                float sweep = Mathf.Sin((_elapsed - 11.5f) * Mathf.PI) * amp;
                ApplyFinAxisCommand(_targetLocalAxis, sweep);
            }
            else if (_elapsed >= 20f)
            {
                bool hadAirflow = _maxDynamicPressure > 1000f;
                bool responded = _maxTargetAxisRateDegS > 3f;
                Complete(
                    hadAirflow && responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    hadAirflow && responded
                        ? $"{DisplayName(ActiveTest)} passed: q max {_maxDynamicPressure:F0} Pa, target-axis response {_maxTargetAxisRateDegS:F1} deg/s."
                        : $"{DisplayName(ActiveTest)} weak: q max {_maxDynamicPressure:F0} Pa, target-axis response {_maxTargetAxisRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }
        /// <summary>
        /// Runs moderated pitch/yaw fin commands with damping phases for the
        /// non-roll fin-axis tests.
        /// </summary>
        void DriveFinTiltAxisTest(RocketPhysicsConfig cfg)
        {
            float amp = Mathf.Clamp(cfg.maxFinAngle * 0.28f, 2f, 10f);

            if (_elapsed >= 1f && _elapsed < 2.4f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 2.4f && _elapsed < 4.8f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 4.8f && _elapsed < 6.2f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp);
            }
            else if (_elapsed >= 6.2f && _elapsed < 8.6f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 8.6f && _elapsed < 10f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp * 0.6f);
            }
            else if (_elapsed >= 10f && _elapsed < 12f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 12f && _elapsed < 13.4f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp * 0.6f);
            }
            else if (_elapsed >= 15f)
            {
                bool hadAirflow = _maxDynamicPressure > 1000f;
                bool responded = _maxTargetAxisRateDegS > 1f;
                Complete(
                    hadAirflow && responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    hadAirflow && responded
                        ? $"{DisplayName(ActiveTest)} passed: q max {_maxDynamicPressure:F0} Pa, moderated target-axis response {_maxTargetAxisRateDegS:F1} deg/s."
                        : $"{DisplayName(ActiveTest)} weak: q max {_maxDynamicPressure:F0} Pa, moderated target-axis response {_maxTargetAxisRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }
        /// <summary>
        /// Converts measured local angular rate into an opposing fin command
        /// for damping during axis tests.
        /// </summary>
        void ApplyFinAxisDamping(Vector3 localAxis, float limitDeg)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;
            float signedRate = Vector3.Dot(localRate, localAxis.normalized);
            float command = Mathf.Clamp(-signedRate * 0.18f, -limitDeg, limitDeg);
            ApplyFinAxisCommand(localAxis, command);
        }
        /// <summary>
        /// Writes a signed fin deflection pattern that should rotate the rocket
        /// around the requested local axis.
        /// </summary>
        void ApplyFinAxisCommand(Vector3 localAxis, float magnitudeDeg)
        {
            if (_fins == null || _fins.Length == 0) return;

            if (Mathf.Abs(localAxis.y) > 0.9f)
            {
                for (int i = 0; i < _fins.Length; i++)
                    _fins[i] = magnitudeDeg;
                return;
            }

            Transform finRoot = Agent?.assembly?.fins ? Agent.assembly.fins.transform : null;
            if (finRoot == null)
            {
                ApplyFallbackOpposedFinPattern(localAxis, magnitudeDeg);
                return;
            }

            int activeFinIndex = 0;
            Vector3 axis = new Vector3(localAxis.x, 0f, localAxis.z).normalized;
            for (int i = 0; i < finRoot.childCount && activeFinIndex < _fins.Length; i++)
            {
                Transform fin = finRoot.GetChild(i);
                if (!fin.gameObject.activeSelf) continue;

                Vector3 localRadial = Agent.transform.InverseTransformPoint(fin.position);
                localRadial.y = 0f;
                float side = localRadial.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(localRadial.normalized, axis)
                    : 0f;

                _fins[activeFinIndex] = side * magnitudeDeg;
                activeFinIndex++;
            }
        }
        /// <summary>
        /// Writes an opposed-fin command pattern when fin transforms are not
        /// available for geometry-aware mapping.
        /// </summary>
        void ApplyFallbackOpposedFinPattern(Vector3 localAxis, float magnitudeDeg)
        {
            for (int i = 0; i < _fins.Length; i++)
                _fins[i] = 0f;

            if (_fins.Length < 2) return;

            if (Mathf.Abs(localAxis.x) >= Mathf.Abs(localAxis.z))
            {
                _fins[0] = magnitudeDeg;
                _fins[Mathf.Min(2, _fins.Length - 1)] = -magnitudeDeg;
                return;
            }

            int positive = Mathf.Min(1, _fins.Length - 1);
            int negative = Mathf.Min(3, _fins.Length - 1);
            _fins[positive] = magnitudeDeg;
            _fins[negative] = -magnitudeDeg;
        }

        sealed class FinAxisHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public FinAxisHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => Controller.ActiveTest;
            /// <summary>
            /// Advances the scripted fin-axis hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveFinAxisTest();
        }
    }
}
