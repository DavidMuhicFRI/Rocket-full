// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/ThrusterHardwareTest.cs
// Purpose: Performs a short engine firing probe and checks that thrust causes
// plausible fuel use and motion for the configured thrust-to-weight ratio.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Fires the configured engines in a short static/liftoff probe and
        /// reports whether thrust, fuel use, and TWR are plausible.
        /// </summary>
        void DriveThrusterTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            float throttleCommand = 0f;
            if (_elapsed >= 0.75f && _elapsed < 2.5f)
                throttleCommand = Mathf.InverseLerp(0.75f, 2.5f, _elapsed);
            else if (_elapsed >= 2.5f && _elapsed < 7.5f)
                throttleCommand = 1f;

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            if (_elapsed >= 8.5f)
            {
                float altitudeGain = _maxAltitude - _initialAltitude;
                float fuelUsed = Mathf.Max(0f, _fuelStart - Agent.GetFuel);

                if (_fuelStart <= 1f || fuelUsed <= 0.1f)
                {
                    Complete(RocketHardwareTestOutcome.Failed,
                        "Thruster test failed: no usable propellant was available in the current rocket config.");
                }
                else if (_twr < 1.05f)
                {
                    Complete(RocketHardwareTestOutcome.Limited,
                        $"Thrusters fired, but lift-off is not expected at TWR {_twr:F2}. Fuel used {fuelUsed:F1} kg.");
                }
                else
                {
                    bool lifted = altitudeGain > 5f && _maxVerticalSpeed > 1f;
                    Complete(
                        lifted ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                        lifted
                            ? $"Thruster test passed: altitude gain {altitudeGain:F1} m, max V {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}."
                            : $"Thruster response weak: altitude gain {altitudeGain:F1} m, max V {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}.");
                }
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        sealed class ThrusterHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public ThrusterHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.Thrusters;
            /// <summary>
            /// Advances the scripted thruster hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveThrusterTest();
        }
    }
}
