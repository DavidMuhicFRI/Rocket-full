// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Actuators/RcsActuatorStateMachine.cs
// Purpose: Converts momentary binary RCS requests into valve states that remain
// open for at least the configured minimum pulse duration.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class RcsActuatorStateMachine
    {
        /// <summary>
        /// Advances the RCS states for one physics step.
        /// </summary>
        public static void Step(
            float[] requests,
            float[] states,
            float[] pulseTimeRemaining,
            int jetCount,
            float dt,
            float configuredMinimumPulse)
        {
            float minimumPulse = Mathf.Max(configuredMinimumPulse, dt);

            for (int i = 0; i < jetCount; i++)
            {
                bool requestedOpen = requests[i] >= 0.5f;
                bool wasOpen = states[i] >= 0.5f;

                if (requestedOpen)
                {
                    if (!wasOpen)
                        pulseTimeRemaining[i] = minimumPulse;
                    states[i] = 1f;
                }
                else
                {
                    states[i] = pulseTimeRemaining[i] > 0f ? 1f : 0f;
                }

                if (states[i] > 0f)
                    pulseTimeRemaining[i] = Mathf.Max(0f, pulseTimeRemaining[i] - dt);
            }
        }
    }
}
