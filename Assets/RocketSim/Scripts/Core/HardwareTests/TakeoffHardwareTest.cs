// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/TakeoffHardwareTest.cs
// Purpose: Ramps the configured engines to full throttle and checks liftoff,
// climb rate, fuel use, thrust-to-weight ratio, and basic upright stability.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Ramps engines to full throttle and passes if the rocket climbs with
        /// sufficient vertical speed while staying reasonably upright.
        /// </summary>
        void DriveTakeoffTest()
        {
            ClearCommandBuffers();

            float throttleCommand = 0f;
            if (_elapsed >= 1.2f && _elapsed < 3f)
                throttleCommand = Mathf.InverseLerp(1.2f, 3f, _elapsed);
            else if (_elapsed >= 3f && _elapsed < 13f)
                throttleCommand = 1f;

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            if (_elapsed >= 13f)
            {
                float altitudeGain = _maxAltitude - _initialAltitude;
                float fuelUsed = Mathf.Max(0f, _fuelStart - Agent.GetFuel);
                float currentTilt = Vector3.Angle(Agent.transform.up, Vector3.up);

                if (_fuelStart <= 1f || fuelUsed <= 0.1f)
                {
                    Complete(RocketHardwareTestOutcome.Failed,
                        "Takeoff failed: no usable propellant was available in the current rocket config.");
                }
                else if (_twr < 1.05f)
                {
                    Complete(RocketHardwareTestOutcome.Limited,
                        $"Takeoff limited: configured liftoff TWR is {_twr:F2}, so the rocket cannot leave the pad cleanly.");
                }
                else
                {
                    bool lifted = altitudeGain > 80f && _maxVerticalSpeed > 12f && currentTilt < 20f;
                    Complete(
                        lifted ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                        lifted
                            ? $"Takeoff passed: climb {altitudeGain:F1} m, max VY {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}."
                            : $"Takeoff weak: climb {altitudeGain:F1} m, max VY {_maxVerticalSpeed:F1} m/s, tilt {currentTilt:F1} deg, TWR {_twr:F2}.");
                }
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        sealed class TakeoffHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public TakeoffHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.Takeoff;
            /// <summary>
            /// Advances the scripted takeoff hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveTakeoffTest();
        }
    }
}
