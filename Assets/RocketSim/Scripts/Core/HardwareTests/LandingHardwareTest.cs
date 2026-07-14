// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/LandingHardwareTest.cs
// Purpose: Runs a simple straight-engine landing burn and checks whether the
// configured thrust, fuel, and mass can produce a controlled vertical touchdown.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Runs a straight-engine landing-burn test and judges whether thrust
        /// can slow the vehicle to a safe vertical touchdown.
        /// </summary>
        void DriveLandingTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            if (_elapsed > 0.5f && _twr < 1.05f)
            {
                Complete(RocketHardwareTestOutcome.Limited,
                    $"Landing limited: configured landing-burn TWR is {_twr:F2}, so thrust cannot overcome gravity.");
                return;
            }

            if (_fuelStart <= 1f)
            {
                Complete(RocketHardwareTestOutcome.Failed,
                    "Landing failed: no usable propellant was available in the current rocket config.");
                return;
            }

            float targetAltitude = LandingTargetAltitude(cfg);
            Vector3 position = Agent.transform.localPosition;
            float altitude = Mathf.Max(0f, position.y - targetAltitude);
            float mass = Mathf.Max(1f, cfg.dryMass + Agent.GetFuel);
            float maxThrustAccel = cfg.maxThrust * ActiveEngineCount() / mass;
            float maxNetUpAccel = maxThrustAccel - 9.80665f;
            float brakeAccel = Mathf.Max(1f, maxNetUpAccel * 0.65f);
            float targetVy = -Mathf.Clamp(
                Mathf.Sqrt(2f * brakeAccel * Mathf.Max(altitude, 1f)),
                2f,
                78f);

            float vy = Agent.rb.linearVelocity.y;
            float desiredNetUpAccel = (targetVy - vy) * 0.45f;
            float desiredThrustAccel = Mathf.Clamp(9.80665f + desiredNetUpAccel, 0f, maxThrustAccel);
            float throttleCommand = maxThrustAccel > 0.1f ? desiredThrustAccel / maxThrustAccel : 0f;

            if (altitude < 80f && vy < -18f)
                throttleCommand = Mathf.Max(throttleCommand, 0.85f);

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            float tilt = Vector3.Angle(Agent.transform.up, Vector3.up);

            if (altitude <= 2f && _elapsed > 4f)
            {
                bool landed = Mathf.Abs(vy) < 6f && tilt < 8f;
                Complete(
                    landed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    landed
                        ? $"Landing passed: straight-engine touchdown VY {vy:F1} m/s, tilt {tilt:F1} deg."
                        : $"Landing failed at touchdown: straight-engine VY {vy:F1} m/s, tilt {tilt:F1} deg.");
                return;
            }

            if (position.y < targetAltitude - 8f || _elapsed >= 36f)
            {
                Complete(RocketHardwareTestOutcome.Failed,
                    $"Landing failed: altitude {altitude:F1} m, straight-engine VY {vy:F1} m/s, TWR {_twr:F2}.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }
        /// <summary>
        /// Returns the target touchdown altitude used by the simplified landing test.
        /// </summary>
        float LandingTargetAltitude(RocketPhysicsConfig cfg)
        {
            return BaseGroundClearance;
        }

        sealed class LandingHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public LandingHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.Landing;
            /// <summary>
            /// Advances the scripted landing hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveLandingTest();
        }
    }
}
