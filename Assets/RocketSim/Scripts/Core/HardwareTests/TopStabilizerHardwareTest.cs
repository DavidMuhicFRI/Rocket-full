// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/TopStabilizerHardwareTest.cs
// Purpose: Exercises RCS pitch, yaw, and roll commands as a simple upper-stage
// attitude-control smoke test and checks for angular response.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Exercises RCS pitch, yaw, and roll commands as a top-stabilizer check
        /// and passes if angular response is measurable.
        /// </summary>
        void DriveTopStabilizerTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 1f && _elapsed < 2.5f)
                ApplyRcsPitchCommand(1f);
            else if (_elapsed >= 2.5f && _elapsed < 4f)
                ApplyRcsPitchCommand(-1f);
            else if (_elapsed >= 4f && _elapsed < 5.5f)
                ApplyRcsYawCommand(1f);
            else if (_elapsed >= 5.5f && _elapsed < 7f)
                ApplyRcsRollCommand(1f);
            else if (_elapsed >= 8f)
            {
                bool responded = _maxAngularRateDegS > 0.25f;
                Complete(
                    responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    responded
                        ? $"Top stabilizer test passed: RCS angular response {_maxAngularRateDegS:F2} deg/s."
                        : $"Top stabilizer response weak: RCS angular response {_maxAngularRateDegS:F2} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        sealed class TopStabilizerHardwareTestCase : ControllerBackedHardwareTest
        {
            /// <summary>Connects this test adapter to the shared controller state.</summary>
            public TopStabilizerHardwareTestCase(HardwareTestController controller) : base(controller) { }
            public override RocketHardwareTestType TestType => RocketHardwareTestType.TopStabilizers;
            /// <summary>
            /// Advances the scripted top-stabilizer hardware test by one physics step.
            /// </summary>
            public override void Step(float dt) => Controller.DriveTopStabilizerTest();
        }
    }
}
