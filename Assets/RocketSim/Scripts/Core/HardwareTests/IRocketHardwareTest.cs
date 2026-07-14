// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/IRocketHardwareTest.cs
// Purpose: Defines the minimal lifecycle shared by every scripted hardware test
// so the controller can prepare, step, stop, and report them uniformly.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>Common adapter interface used by HardwareTestController.</summary>
    public interface IRocketHardwareTest
    {
        RocketHardwareTestType TestType { get; }
        RocketHardwareTestOutcome Outcome { get; }
        string Status { get; }

        /// <summary>Performs optional setup after a test agent is available.</summary>
        void Prepare(FalconAgent agent);
        /// <summary>Advances the scripted test by one fixed physics step.</summary>
        void Step(float dt);
        /// <summary>Performs optional cleanup when a test ends or is cancelled.</summary>
        void Stop();
    }
}
