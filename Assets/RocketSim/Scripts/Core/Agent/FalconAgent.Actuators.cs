// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Actuators.cs
// Purpose: Converts policy or manual commands into engine, gimbal, grid-fin, and RCS actuator states.
// Main flow: ML/manual command -> safe target values -> actuator timing and
// slew limits -> physical force application -> fuel use and visual feedback.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Receives ML-Agents actions and converts them into actuator commands.
        /// </summary>
        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_manualControlActive) return;

            var act = actions.ContinuousActions;
            var actionIndex = 0;
            // Throttle: map [-1,+1] → [0,1] → [minThrottle, 1] or off
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                float raw = (act[actionIndex++] + 1f) * 0.5f;
                bool engineLit = EngineIsIgnited(i);
                float threshold = engineLit ? EngineShutdownThreshold : EngineIgnitionThreshold;
                commandedThrottle[i] = raw > threshold ? Mathf.Lerp(cfg.minThrottle, 1f, raw) : 0f;
            }

            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                var x = act[actionIndex++] * cfg.maxGimbal;
                var y = act[actionIndex++] * cfg.maxGimbal;
                targetGimbal[i] = ClampGimbalCone(new Vector2(x, y));
            }

            if (cfg.hasFins)
            {
                for (int i = 0; i < cfg.finCount; i++) targetFinAngles[i] = act[actionIndex++] * cfg.maxFinAngle;
            }
            else
            {
                for (int i = 0; i < cfg.finCount; i++) targetFinAngles[i] = 0f;
            }

            if (cfg.hasRCS)
            {
                for (int i = 0; i < cfg.rcsJetCount; i++)
                    rcsValveRequests[i] = actionIndex < act.Length && act[actionIndex++] > 0f ? 1f : 0f;
            }
            else
            {
                ClearRcsCommands();
            }

            UpdateThrusterVisuals();
        }

        /// <summary>
        /// Stores clamped manual engine, gimbal, fin, and RCS commands so
        /// hardware tests can override policy actions during FixedUpdate.
        /// </summary>
        public void SetManualControl(
            float[] throttle01,
            Vector2[] gimbalDeg,
            float[] finDeflectionsDeg,
            float[] rcsValveCommands)
        {
            EnsureActuatorBuffers(false);
            _manualControlActive = true;

            for (int i = 0; i < _manualThrottle.Length; i++)
            {
                float command = throttle01 != null && i < throttle01.Length ? Mathf.Clamp01(throttle01[i]) : 0f;
                _manualThrottle[i] = command > 0.01f ? Mathf.Max(cfg.minThrottle, command) : 0f;
            }

            for (int i = 0; i < _manualGimbal.Length; i++)
            {
                Vector2 command = gimbalDeg != null && i < gimbalDeg.Length ? gimbalDeg[i] : Vector2.zero;
                _manualGimbal[i] = ClampGimbalCone(new Vector2(
                    Mathf.Clamp(command.x, -cfg.maxGimbal, cfg.maxGimbal),
                    Mathf.Clamp(command.y, -cfg.maxGimbal, cfg.maxGimbal)));
            }

            for (int i = 0; i < _manualFinAngles.Length; i++)
            {
                float command = finDeflectionsDeg != null && i < finDeflectionsDeg.Length ? finDeflectionsDeg[i] : 0f;
                _manualFinAngles[i] = Mathf.Clamp(command, -cfg.maxFinAngle, cfg.maxFinAngle);
            }

            for (int i = 0; i < _manualRcsValveRequests.Length; i++)
            {
                _manualRcsValveRequests[i] = rcsValveCommands != null &&
                                    i < rcsValveCommands.Length &&
                                    rcsValveCommands[i] > 0.001f
                    ? 1f
                    : 0f;
            }
        }

        /// <summary>
        /// Disables manual override mode and resets stored manual actuator targets.
        /// </summary>
        public void ClearManualControl()
        {
            _manualControlActive = false;
            if (_manualThrottle != null) for (int i = 0; i < _manualThrottle.Length; i++) _manualThrottle[i] = 0f;
            if (_manualGimbal != null) for (int i = 0; i < _manualGimbal.Length; i++) _manualGimbal[i] = Vector2.zero;
            if (_manualFinAngles != null) for (int i = 0; i < _manualFinAngles.Length; i++) _manualFinAngles[i] = 0f;
            if (_manualRcsValveRequests != null) for (int i = 0; i < _manualRcsValveRequests.Length; i++) _manualRcsValveRequests[i] = 0f;
            if (commandedThrottle != null) for (int i = 0; i < commandedThrottle.Length; i++) commandedThrottle[i] = 0f;
            if (targetThrottle != null) for (int i = 0; i < targetThrottle.Length; i++) targetThrottle[i] = 0f;
            if (targetGimbal != null) for (int i = 0; i < targetGimbal.Length; i++) targetGimbal[i] = Vector2.zero;
            if (targetFinAngles != null) for (int i = 0; i < targetFinAngles.Length; i++) targetFinAngles[i] = 0f;
            ClearRcsCommands();
        }

        /// <summary>
        /// Advances engine state machines and slews throttle, gimbal, and fin
        /// state toward their targets at configured actuator rates.
        /// </summary>
        void StepActuators()
        {
            float dt = Time.fixedDeltaTime;
            StepEngineStateMachines(dt);

            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                throttle[i] = Mathf.MoveTowards(throttle[i], targetThrottle[i], cfg.throttleSpoolRate * dt);
                float gimbalSpeedScale = 1f - ActiveFaultSeverity(RocketFaultType.GimbalJam, i);
                gimbal[i] = Vector2.MoveTowards(gimbal[i], targetGimbal[i], cfg.gimbalSlewRate * gimbalSpeedScale * dt);
            }
            for (int i = 0; i < cfg.finCount; i++) finAngles[i] = Mathf.MoveTowards(finAngles[i], targetFinAngles[i], cfg.finSlewRate * dt);

            assembly.fins?.ApplyDeflections(finAngles);
            UpdateThrusterVisuals();
        }

        /// <summary>
        /// Updates per-engine startup, shutdown, minimum-run, and restart-cooldown
        /// state before throttle is allowed to spool.
        /// </summary>
        void StepEngineStateMachines(float dt)
        {
            if (commandedThrottle == null || targetThrottle == null || engineStates == null)
                return;

            EngineActuatorStateMachine.StepAll(
                dt,
                cfg.independentEngineCount,
                commandedThrottle,
                targetThrottle,
                throttle,
                engineStates,
                engineStateTimers,
                engineRunTimes,
                engineOffTimes,
                EngineTimingConfig.FromPhysicsConfig(cfg),
                EngineCommandEpsilon,
                EngineShutdownThreshold);
        }

        /// <summary>
        /// Returns whether an engine command channel is in any non-off run state.
        /// </summary>
        bool EngineIsIgnited(int index)
        {
            return engineStates != null &&
                   index >= 0 &&
                   index < engineStates.Length &&
                   engineStates[index] != EngineRunState.Off;
        }

        /// <summary>
        /// Clamps the gimbal cone value to safe limits.
        /// </summary>
        Vector2 ClampGimbalCone(Vector2 commandDeg)
        {
            float limit = Mathf.Max(0f, cfg.maxGimbal);
            return commandDeg.sqrMagnitude > limit * limit
                ? commandDeg.normalized * limit
                : commandDeg;
        }

        /// <summary>
        /// Copies stored manual commands into the normal actuator targets so
        /// the rest of the actuator/physics pipeline stays unchanged.
        /// </summary>
        void ApplyManualControlOverride()
        {
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                commandedThrottle[i] = _manualThrottle != null && i < _manualThrottle.Length ? _manualThrottle[i] : 0f;
                targetGimbal[i] = _manualGimbal != null && i < _manualGimbal.Length ? _manualGimbal[i] : Vector2.zero;
            }

            for (int i = 0; i < cfg.finCount; i++)
                targetFinAngles[i] = _manualFinAngles != null && i < _manualFinAngles.Length ? _manualFinAngles[i] : 0f;

            if (cfg.hasRCS)
            {
                for (int i = 0; i < rcsValveRequests.Length; i++)
                    rcsValveRequests[i] = _manualRcsValveRequests != null && i < _manualRcsValveRequests.Length ? _manualRcsValveRequests[i] : 0f;
            }
            else
            {
                ClearRcsCommands();
            }
        }

        /// <summary>
        /// Applies RCS thruster forces and consumes RCS propellant.
        /// </summary>
        void ApplyRcs()
        {
            if (!cfg.hasRCS || !assembly || !assembly.rcs)
                return;

            if (rcsValveRequests == null || rcsValveStates == null || rcsValveRequests.Length == 0)
            {
                assembly.rcs.ShowCommands(null);
                return;
            }

            int jetCount = Mathf.Min(rcsValveRequests.Length, cfg.rcsJetCount);
            float dt = Time.fixedDeltaTime;
            StepRcsValves(jetCount, dt);

            float requestedPropellant = 0f;
            for (int i = 0; i < jetCount; i++)
            {
                float requestedCommand = EffectiveRcsValveCommand(i);
                requestedPropellant += requestedCommand * cfg.rcsThrust /
                                       (Mathf.Max(cfg.rcsSpecificImpulse, 1f) * G0) * dt;
            }

            float propellantScale = requestedPropellant > 0f ? Mathf.Clamp01(rcsPropellant / requestedPropellant) : 0f;
            float consumedPropellant = 0f;
            float[] displayedCommands = new float[jetCount];

            for (int i = 0; i < jetCount; i++)
            {
                float command = EffectiveRcsValveCommand(i) * propellantScale;
                displayedCommands[i] = command;
                if (command <= 0.001f) continue;

                Vector3 force = assembly.rcs.WorldForceDirectionForJet(i) * (command * cfg.rcsThrust);
                rb.AddForceAtPosition(force, assembly.rcs.WorldPositionForJet(i));
                consumedPropellant += command * cfg.rcsThrust / (Mathf.Max(cfg.rcsSpecificImpulse, 1f) * G0) * dt;
            }

            assembly.rcs.ShowCommands(displayedCommands);
            rcsPropellant = Mathf.Max(0f, rcsPropellant - consumedPropellant);
        }

        /// <summary>
        /// Returns the final command for one RCS jet after applying stuck-open
        /// and stuck-closed faults. A closed fault reduces the requested command;
        /// an open fault provides a minimum command even when the policy requests zero.
        /// </summary>
        float EffectiveRcsValveCommand(int index)
        {
            float command = rcsValveStates[index];
            float stuckClosed = ActiveFaultSeverity(RocketFaultType.RcsStuckClosed, index);
            float stuckOpen = ActiveFaultSeverity(RocketFaultType.RcsStuckOpen, index);
            return Mathf.Max(command * (1f - stuckClosed), stuckOpen);
        }

        /// <summary>
        /// Advances RCS valve latch state for the active jets using the configured
        /// minimum pulse duration.
        /// </summary>
        void StepRcsValves(int jetCount, float dt)
        {
            RcsValveBank.Step(rcsValveRequests, rcsValveStates, rcsPulseTimeRemaining, jetCount, dt, cfg.rcsMinimumPulseDuration);
        }

        /// <summary>
        /// Allocates or resizes all actuator, manual-control, and engine-state
        /// arrays to match the current hardware schema.
        /// </summary>
        void EnsureActuatorBuffers(bool resetValues)
        {
            int engineCount = Mathf.Max(1, cfg.independentEngineCount);
            int finCount = Mathf.Max(0, cfg.finCount);
            int rcsJetCount = cfg.hasRCS ? Mathf.Max(0, cfg.rcsJetCount) : 0;

            if (throttle == null || throttle.Length != engineCount) throttle = new float[engineCount];
            if (commandedThrottle == null || commandedThrottle.Length != engineCount) commandedThrottle = new float[engineCount];
            if (targetThrottle == null || targetThrottle.Length != engineCount) targetThrottle = new float[engineCount];
            if (gimbal == null || gimbal.Length != engineCount) gimbal = new Vector2[engineCount];
            if (targetGimbal == null || targetGimbal.Length != engineCount) targetGimbal = new Vector2[engineCount];
            if (_manualThrottle == null || _manualThrottle.Length != engineCount) _manualThrottle = new float[engineCount];
            if (_manualGimbal == null || _manualGimbal.Length != engineCount) _manualGimbal = new Vector2[engineCount];
            if (engineStates == null || engineStates.Length != engineCount) engineStates = new EngineRunState[engineCount];
            if (engineStateTimers == null || engineStateTimers.Length != engineCount) engineStateTimers = new float[engineCount];
            if (engineRunTimes == null || engineRunTimes.Length != engineCount) engineRunTimes = new float[engineCount];
            if (engineOffTimes == null || engineOffTimes.Length != engineCount) engineOffTimes = new float[engineCount];

            if (finAngles == null || finAngles.Length != finCount) finAngles = new float[finCount];
            if (targetFinAngles == null || targetFinAngles.Length != finCount) targetFinAngles = new float[finCount];
            if (_manualFinAngles == null || _manualFinAngles.Length != finCount) _manualFinAngles = new float[finCount];
            if (rcsValveRequests == null || rcsValveRequests.Length != rcsJetCount) rcsValveRequests = new float[rcsJetCount];
            if (rcsValveStates == null || rcsValveStates.Length != rcsJetCount) rcsValveStates = new float[rcsJetCount];
            if (rcsPulseTimeRemaining == null || rcsPulseTimeRemaining.Length != rcsJetCount) rcsPulseTimeRemaining = new float[rcsJetCount];
            if (_manualRcsValveRequests == null || _manualRcsValveRequests.Length != rcsJetCount) _manualRcsValveRequests = new float[rcsJetCount];

            if (!resetValues) return;

            float restartCooldown = EngineTimingConfig.FromPhysicsConfig(cfg).restartCooldown;
            for (int i = 0; i < engineCount; i++)
            {
                throttle[i] = commandedThrottle[i] = targetThrottle[i] = _manualThrottle[i] = 0f;
                gimbal[i] = targetGimbal[i] = _manualGimbal[i] = Vector2.zero;
                engineStates[i] = EngineRunState.Off;
                engineStateTimers[i] = 0f;
                engineRunTimes[i] = 0f;
                engineOffTimes[i] = restartCooldown;
            }

            for (int i = 0; i < finCount; i++)
                finAngles[i] = targetFinAngles[i] = _manualFinAngles[i] = 0f;

            for (int i = 0; i < rcsJetCount; i++)
            {
                rcsValveRequests[i] = 0f;
                rcsValveStates[i] = 0f;
                rcsPulseTimeRemaining[i] = 0f;
                _manualRcsValveRequests[i] = 0f;
            }
        }

        /// <summary>
        /// Returns the latched valve state for one RCS jet for observations and telemetry.
        /// </summary>
        float RcsJetCommand(int index)
        {
            return rcsValveStates != null && index >= 0 && index < rcsValveStates.Length
                ? rcsValveStates[index]
                : 0f;
        }

        /// <summary>
        /// Clears all RCS requests, valve states, pulse timers, and visual plumes.
        /// </summary>
        void ClearRcsCommands()
        {
            if (rcsValveRequests != null)
                for (int i = 0; i < rcsValveRequests.Length; i++) rcsValveRequests[i] = 0f;
            if (rcsValveStates != null)
                for (int i = 0; i < rcsValveStates.Length; i++) rcsValveStates[i] = 0f;
            if (rcsPulseTimeRemaining != null)
                for (int i = 0; i < rcsPulseTimeRemaining.Length; i++) rcsPulseTimeRemaining[i] = 0f;

            assembly?.rcs?.ShowCommands(null);
        }

        /// <summary>
        /// Updates engine gimbal transforms and flame visuals from current throttle and gimbal state.
        /// </summary>
        void UpdateThrusterVisuals()
        {
            if (!assembly || !assembly.thrusters) return;

            assembly.thrusters.ApplyActuatorVisuals(
                throttle,
                gimbal,
                minFlameWidth,
                maxFlameWidth,
                minFlameLength,
                maxFlameLength,
                Time.time);
        }

    }
}
