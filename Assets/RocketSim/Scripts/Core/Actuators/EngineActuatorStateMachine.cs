// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Actuators/EngineActuatorStateMachine.cs
// Purpose: Contains the engine actuator run states, timing config, and state-machine stepping used by FalconAgent.Actuators.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Calculates the throttle that balances vehicle weight with the engines
    /// selected for an already-running hover start. This establishes a physical
    /// initial condition; it is not exposed to, or used by, the reward model.
    /// </summary>
    public static class HoverThrustInitialization
    {
        public static float EquilibriumThrottle(
            float vehicleMassKg,
            float gravityMagnitude,
            float effectiveMaxThrustPerEngineN,
            int runningEngineCount,
            float minimumThrottle)
        {
            float availableThrust = Mathf.Max(0f, effectiveMaxThrustPerEngineN) * Mathf.Max(0, runningEngineCount);
            if (availableThrust <= 0f)
                return 0f;

            float requiredThrottle = Mathf.Max(0f, vehicleMassKg) * Mathf.Max(0f, gravityMagnitude) / availableThrust;
            return Mathf.Clamp(requiredThrottle, Mathf.Clamp01(minimumThrottle), 1f);
        }
    }

    internal enum EngineRunState
    {
        Off,
        Starting,
        Running,
        Shutdown
    }

    internal readonly struct EngineTimingConfig
    {
        public readonly float minThrottle;
        public readonly float startupDelay;
        public readonly float shutdownTransient;
        public readonly float minimumRunTime;
        public readonly float restartCooldown;

        EngineTimingConfig(float minThrottle, float startupDelay, float shutdownTransient, float minimumRunTime, float restartCooldown)
        {
            this.minThrottle = minThrottle;
            this.startupDelay = startupDelay;
            this.shutdownTransient = shutdownTransient;
            this.minimumRunTime = minimumRunTime;
            this.restartCooldown = restartCooldown;
        }

        /// <summary>
        /// Copies engine timing values from the immutable physics config and
        /// clamps them before the per-engine state machines consume them.
        /// </summary>
        public static EngineTimingConfig FromPhysicsConfig(RocketPhysicsConfig cfg)
        {
            return new EngineTimingConfig(
                cfg.minThrottle,
                Mathf.Clamp(cfg.engineStartupDelay, 0f, 5f),
                Mathf.Clamp(cfg.engineShutdownTransient, 0f, 2f),
                Mathf.Clamp(cfg.engineMinimumRunTime, 0f, 10f),
                Mathf.Clamp(cfg.engineRestartCooldown, 0f, 10f));
        }
    }

    internal static class EngineActuatorStateMachine
    {
        /// <summary>
        /// Advances all engine command channels by one simulation step.
        /// </summary>
        public static void StepAll(
            float dt,
            int engineCount,
            float[] commandedThrottle,
            float[] targetThrottle,
            float[] actualThrottle,
            EngineRunState[] states,
            float[] stateTimers,
            float[] runTimes,
            float[] offTimes,
            EngineTimingConfig timing,
            float commandEpsilon,
            float shutdownThreshold)
        {
            for (int i = 0; i < engineCount; i++)
            {
                float requestedThrottle = i < commandedThrottle.Length ? commandedThrottle[i] : 0f;
                bool wantsThrust = requestedThrottle > commandEpsilon;

                switch (states[i])
                {
                    case EngineRunState.Off:
                        targetThrottle[i] = 0f;
                        runTimes[i] = 0f;
                        offTimes[i] += dt;

                        if (wantsThrust && offTimes[i] >= timing.restartCooldown)
                        {
                            states[i] = EngineRunState.Starting;
                            stateTimers[i] = timing.startupDelay;
                            offTimes[i] = 0f;
                        }
                        break;

                    case EngineRunState.Starting:
                        targetThrottle[i] = 0f;
                        stateTimers[i] -= dt;
                        if (stateTimers[i] <= 0f)
                        {
                            states[i] = EngineRunState.Running;
                            runTimes[i] = 0f;
                            targetThrottle[i] = Mathf.Max(timing.minThrottle, requestedThrottle);
                        }
                        break;

                    case EngineRunState.Running:
                        runTimes[i] += dt;
                        if (wantsThrust)
                        {
                            targetThrottle[i] = Mathf.Max(timing.minThrottle, requestedThrottle);
                        }
                        else if (runTimes[i] < timing.minimumRunTime)
                        {
                            targetThrottle[i] = timing.minThrottle;
                        }
                        else
                        {
                            targetThrottle[i] = 0f;
                            states[i] = EngineRunState.Shutdown;
                            stateTimers[i] = timing.shutdownTransient;
                        }
                        break;

                    case EngineRunState.Shutdown:
                        targetThrottle[i] = 0f;
                        stateTimers[i] -= dt;
                        if (stateTimers[i] <= 0f && actualThrottle[i] <= shutdownThreshold)
                        {
                            states[i] = EngineRunState.Off;
                            offTimes[i] = 0f;
                            runTimes[i] = 0f;
                        }
                        break;
                }
            }
        }
    }
}
