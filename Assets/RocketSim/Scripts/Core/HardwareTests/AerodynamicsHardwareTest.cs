// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/AerodynamicsHardwareTest.cs
// Purpose: Runs a high-speed fall probe that checks dynamic pressure, lateral
// damping, attitude response, and numerical stability of the aerodynamic model.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Runs a high-speed falling test and passes only if dynamic pressure is
        /// sane, lateral velocity damps, and attitude responds without instability.
        /// </summary>
        void DriveAerodynamicsTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 12f || Agent.transform.localPosition.y < 250f)
            {
                float lateralDrop = _initialLateralSpeed - _minLateralSpeed;
                bool hadAirflow = _maxDynamicPressure > 3000f;
                bool qSane = _maxDynamicPressure < 250000f;
                bool lateralDamped = _initialLateralSpeed > 5f &&
                                      lateralDrop > Mathf.Max(8f, _initialLateralSpeed * 0.2f);
                bool attitudeResponded = _maxAngularRateDegS > 0.2f;
                bool stayedFinite = IsFinite(Agent.rb.linearVelocity) &&
                                    Agent.rb.linearVelocity.magnitude < 900f;

                bool passed = hadAirflow && qSane && lateralDamped && attitudeResponded && stayedFinite;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"Aerodynamics passed: q max {_maxDynamicPressure:F0} Pa, lateral speed damped {lateralDrop:F1} m/s, attitude response {_maxAngularRateDegS:F1} deg/s."
                        : $"Aerodynamics suspicious: q max {_maxDynamicPressure:F0} Pa, lateral damping {lateralDrop:F1} m/s, attitude response {_maxAngularRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        sealed class AerodynamicsHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public AerodynamicsHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.Aerodynamics;
            /// <summary>
            /// Advances the scripted aerodynamics hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveAerodynamicsTest();
        }
    }
}
