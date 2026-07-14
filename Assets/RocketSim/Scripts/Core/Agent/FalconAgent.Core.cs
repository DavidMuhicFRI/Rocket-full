// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Core.cs
// Purpose: Runs the main FalconAgent lifecycle: initialization, episode reset, physics step orchestration, rewards, and episode ending.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Random = UnityEngine.Random;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Assigns the training-area slot used to separate telemetry and
        /// curriculum accounting across parallel agent instances.
        /// </summary>
        public void SetAreaIndex(int idx) => _areaIndex = idx;

        /// <summary>
        /// Re-reads hardware physics from the assembly, resizes actuator buffers,
        /// and refreshes visuals/platform geometry after a parts change.
        /// </summary>
        public void RefreshHardwareConfig(bool resetActuators = true)
        {
            RefreshConfig();
            EnsureActuatorBuffers(resetActuators);
            if (resetActuators)
                ClearRcsCommands();
            if (rb)
                UpdateMassProperties();
            UpdateLandingPlatformGeometry();
            UpdateThrusterVisuals();
        }

        /// <summary>
        /// Initializes ML-Agents runtime state, Rigidbody settings, actuator
        /// buffers, physics config, and initial wind before the first episode.
        /// </summary>
        public override void Initialize()
        {
            _episode = 0;
            _hasEpisodeStarted = false;
            _currentEpisodeCompleted = false;
            rb.maxAngularVelocity = 30f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.useGravity = true;

            RefreshConfig();
            EnsureActuatorBuffers(true);
            
            wind = targetWind = RandomWind();
        }

        /// <summary>
        /// Resets the rocket and scenario state at the start of a new episode.
        /// </summary>
        public override void OnEpisodeBegin()
        {
            if (_hasEpisodeStarted)
            {
                if (!_currentEpisodeCompleted && _step > 0)
                {
                    LogLandingEpisodeEnd(EpisodeTerminationReason.LandingMaxStepOrExternalReset);
                    TelemetryLogger.Instance?.CompleteEpisode(_areaIndex, _episode);
                    NotifyEpisodeCompleted();
                }

                _episode++;
            }
            else
            {
                _episode = 0;
                _hasEpisodeStarted = true;
            }

            _step = 0;
            _currentEpisodeCompleted = false;
            _telemetryLoggedThisStep = false;
            _episodeEndedThisStep    = false;
            _landingEpisodeEndLogged = false;
            _hoverTrackStableTime    = 0f;
            _hoverTrackSegmentElapsedTime = 0f;
            _hoverTrackEpisodeCaptures = 0;
            _landingEpisodeSucceeded = false;
            ResetLandingPlatformState();

            RefreshConfig();
            EnsureActuatorBuffers(true);

            if (_hardwareTestMode)
            {
                _currentEpisodeCompleted = true;
                _telemetryLoggedThisStep = true;
                return;
            }

            ConfigureEpisodeFaults();
            
            fuel = cfg.startFuelMass > 0f ? cfg.startFuelMass : cfg.maxFuelMass;
            rcsPropellant = cfg.hasRCS ? cfg.rcsPropellantMass : 0f;
            UpdateMassProperties();
            for (int i = 0; i < cfg.finCount; i++) finAngles[i] = targetFinAngles[i] = 0f;
            for (int i = 0; i < cfg.independentEngineCount; i++) throttle[i] = targetThrottle[i] = commandedThrottle[i] = 0f;
            for (int i = 0; i < cfg.independentEngineCount; i++) gimbal[i] = targetGimbal[i] = Vector2.zero;
            assembly.fins?.ApplyDeflections(finAngles);
            UpdateThrusterVisuals();

            ClearRcsCommands();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            wind = targetWind = RandomWind();
            SpawnForScenario();
            RandomizeTarget();
            UpdateLandingPlatformGeometry();
            UpdateLandingPlatformState(0f);
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        /// <summary>
        /// Adds the current rocket state to the ML-Agents observation vector.
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            // 1. Relative position to target pad (3)
            sensor.AddObservation((targetPad.localPosition - transform.localPosition) / 100f);

            // 2. Linear velocity (3)
            sensor.AddObservation(rb.linearVelocity / 50f);

            // 3. Angular velocity (3)
            sensor.AddObservation(rb.angularVelocity / 10f);

            // 4. Orientation — full up vector (3)
            sensor.AddObservation(transform.up);

            // 5. Actuator state (engine control channels * 3 + fins + RCS jets)
            for (int i = 0; i < cfg.independentEngineCount; i++) sensor.AddObservation(throttle[i]);
            for (int i = 0; i < cfg.independentEngineCount; i++) sensor.AddObservation(gimbal[i] / Mathf.Max(cfg.maxGimbal, 1f));
            if (cfg.hasFins) for (int i = 0; i < cfg.finCount; i++) sensor.AddObservation(finAngles[i] / Mathf.Max(cfg.maxFinAngle, 1f));
            if (cfg.hasRCS) for (int i = 0; i < cfg.rcsJetCount; i++) sensor.AddObservation(RcsJetCommand(i));

            // 6. Fuel fraction (1)
            sensor.AddObservation(fuel / Mathf.Max(cfg.startFuelMass, 1f));

            // 7. Normalized altitude (1)
            sensor.AddObservation(transform.localPosition.y / 250f);

            // 8. Angle of attack (1)
            sensor.AddObservation(aoaDeg / 90f);

            // 9. Dynamic pressure — normalized at q = 5 kPa ≈ 90 m/s (1)
            sensor.AddObservation(Mathf.Clamp01(q / 5000f));

            // 10. Wind in rocket-local frame (3)
            sensor.AddObservation(
                transform.InverseTransformDirection(wind) /
                Mathf.Max(envConfig.windSpeed, 1f));

            float headingErrorRad = SignedHeadingErrorDeg() * Mathf.Deg2Rad;
            sensor.AddObservation(Mathf.Sin(headingErrorRad));
            sensor.AddObservation(Mathf.Cos(headingErrorRad));
        }

        /// <summary>
        /// Fills the action buffers with manual or fallback control commands.
        /// </summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            actionsOut.ContinuousActions.Clear();
            actionsOut.DiscreteActions.Clear();
        }

        /// <summary>
        /// Enables or disables hardware-test mode, which lets test controllers
        /// drive actuators manually without normal episode reward/reset behavior.
        /// </summary>
        public void SetHardwareTestMode(bool enabled)
        {
            _hardwareTestMode = enabled;
            if (!enabled) ClearManualControl();
        }

        /// <summary>
        /// Places the rocket at a deterministic test pose, resets propellant and
        /// actuator state, and keeps ML-Agents episode bookkeeping out of the test.
        /// </summary>
        public void ResetForHardwareTest(
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            SetHardwareTestMode(true);
            RefreshConfig();
            EnsureActuatorBuffers(true);

            _step = 0;
            _stepReward = 0f;
            _telemetryLoggedThisStep = false;
            _episodeEndedThisStep = false;
            _hoverTrackTargetReachedThisStep = false;
            _hoverTrackEpisodeCaptures = 0;
            _landingEpisodeSucceeded = false;
            _currentEpisodeCompleted = true;
            _hoverTrackStableTime = 0f;
            _hoverTrackSegmentElapsedTime = 0f;
            ResetLandingPlatformState();

            fuel = Mathf.Clamp(cfg.startFuelMass, 0f, cfg.maxFuelMass);
            rcsPropellant = cfg.hasRCS ? cfg.rcsPropellantMass : 0f;
            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            rb.linearVelocity = linearVelocity;
            rb.angularVelocity = angularVelocity;
            Physics.SyncTransforms();

            wind = targetWind = RandomWind();
            q = 0f;
            aoaDeg = 0f;
            ClearRcsCommands();

            assembly.fins?.ApplyDeflections(finAngles);
            UpdateMassProperties();
            UpdateThrusterVisuals();
            UpdateLandingPlatformGeometry();
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        /// <summary>
        /// Runs physics-step simulation logic at Unity fixed timestep intervals.
        /// </summary>
        void FixedUpdate()
        {
            _step++;
            _stepReward = 0f;
            _telemetryLoggedThisStep = false;
            _episodeEndedThisStep    = false;
            _hoverTrackTargetReachedThisStep = false;
            _hoverTrackSegmentElapsedTime += Time.fixedDeltaTime;
            _episodeElapsedSeconds += Time.fixedDeltaTime;

            if (_manualControlActive)
                ApplyManualControlOverride();

            UpdateMassProperties();
            StepActuators();
            StepWind();
            float thrustForceMag = ApplyEngines();
            
            // ── Compute aero forces and capture magnitudes for sensor package ──
            float lateralForceMag, axialForceMag;
            
            ApplyAerodynamics(out lateralForceMag, out axialForceMag);
            
            // ── Tick sensors AFTER aero so heat/stress values are current ─────
            float sensorAltitude = Mathf.Max(0f, transform.localPosition.y);
            float rho = AtmosphereModel.AirDensity(sensorAltitude, Rho0, HScale, envConfig.AirDensityMultiplier);
            float speed = (rb.linearVelocity - wind).magnitude;
            float lever = Mathf.Abs(cfg.cpLocalY - rb.centerOfMass.y);

            _sensors.Tick(rb, transform, rho, speed,
                lateralForceMag, axialForceMag + thrustForceMag,
                lever, cfg.radius);
            
            ApplyRcs();
            UpdateMassProperties();
            UpdateLandingPlatformGeometry();
            UpdateLandingPlatformState(Time.fixedDeltaTime);

            if (!_hardwareTestMode)
            {
                CalculateRewards();
                _hoverTrackTargetReachedThisStep = UpdateHoverTrackSuccess();
                if (_hoverTrackTargetReachedThisStep && !_episodeEndedThisStep)
                    AddReward(HoverTrackCycleCompleteReward);

                if (!_telemetryLoggedThisStep)
                    LogTelemetry();

                if (envConfig.moveTargetEnabled &&
                    envConfig.scenario == ScenarioType.HoverTracking &&
                    !_episodeEndedThisStep &&
                    _hoverTrackTargetReachedThisStep)
                {
                    _hoverTrackEpisodeCaptures++;
                    assembly.GetComponentInParent<TrainingAreaManager>()?.NotifyHoverTrackTargetReached();
                    RandomizeTarget();
                    _hoverTrackStableTime = 0f;
                }
            }
        }

        /// <summary>
        /// Measures reward terms, evaluates the active scenario reward model,
        /// applies shaping/terminal rewards, and ends the episode when required.
        /// </summary>
        void CalculateRewards()
        {
            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            RewardRuntimeContext context = BuildRewardRuntimeContext();

            RewardDecision decision = RocketRewardModel.Evaluate(
                envConfig.scenario,
                terms,
                context,
                envConfig.GetRewardFactors(envConfig.scenario));
            AddReward(decision.shapingReward);

            if (decision.hasTerminalReward)
                SetReward(decision.terminalReward);

            if (decision.successTerminal && envConfig.scenario == ScenarioType.Landing)
                _landingEpisodeSucceeded = true;

            if (decision.endEpisode)
            {
                LogLandingEpisodeEnd(decision.terminationReason, terms, context, decision.terminalReward);
                EndEpisode();
            }
        }

        /// <summary>
        /// Adds a reward through ML-Agents while also accumulating the same
        /// amount for the per-step telemetry row.
        /// </summary>
        public new void AddReward(float reward)
        {
            _stepReward += reward;
            base.AddReward(reward);
        }

        /// <summary>
        /// Replaces the current ML-Agents reward and telemetry step reward,
        /// used for terminal reward overrides.
        /// </summary>
        public new void SetReward(float reward)
        {
            _stepReward = reward;
            base.SetReward(reward);
        }

        /// <summary>
        /// Ends the current ML-Agents episode once, logs the final telemetry
        /// row, flushes the episode summary, and notifies curriculum owners.
        /// </summary>
        public new void EndEpisode()
        {
            if (_episodeEndedThisStep) return;

            LogLandingEpisodeEnd(EpisodeTerminationReason.LandingExternalEndRequest);
            LogTelemetry();
            TelemetryLogger.Instance?.CompleteEpisode(_areaIndex, _episode);
            _currentEpisodeCompleted = true;
            NotifyEpisodeCompleted();
            _telemetryLoggedThisStep = true;
            _episodeEndedThisStep    = true;

            base.EndEpisode();

            // Some ML-Agents versions reset immediately; keep this physics step
            // marked as logged even if OnEpisodeBegin has already run.
            _telemetryLoggedThisStep = true;
            _episodeEndedThisStep    = true;
        }

        /// <summary>
        /// Notifies the owning TrainingAreaManager that a real episode ended so
        /// shared curriculum counters can advance.
        /// </summary>
        void NotifyEpisodeCompleted()
        {
            // Curriculum progresses after a real completed/reset episode, not at
            // the first OnEpisodeBegin bootstrap.
            bool successfulEpisode = envConfig.scenario switch
            {
                ScenarioType.HoverTracking => _hoverTrackEpisodeCaptures > 0,
                ScenarioType.Landing => _landingEpisodeSucceeded,
                _ => false
            };
            assembly.GetComponentInParent<TrainingAreaManager>()?.NotifyEpisodeEnd(successfulEpisode);
        }

        /// <summary>
        /// Caches the immutable physics snapshot built from the current rocket
        /// assembly, falling back to static Falcon 9 values when no assembly exists.
        /// </summary>
        void RefreshConfig()
        {
            cfg = assembly ? assembly.GetPhysicsConfig() : RocketAssembly.Falcon9StaticFallback;
        }

        /// <summary>
        /// Packages mutable runtime thresholds and platform state needed by the
        /// scenario reward models for the current physics step.
        /// </summary>
        RewardRuntimeContext BuildRewardRuntimeContext()
        {
            return new RewardRuntimeContext(
                transform.localPosition.y,
                ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig),
                fuel,
                envConfig.hoverTrackSettleRadius,
                envConfig.ActiveLandingFailureAltitude,
                envConfig.CurrentLandingSuccessRadius,
                envConfig.CurrentLandingSuccessMaxSpeed,
                envConfig.CurrentLandingSuccessMaxVerticalSpeed,
                envConfig.CurrentLandingSuccessMaxHorizontalSpeed,
                envConfig.CurrentLandingSuccessMaxTiltDeg,
                envConfig.CurrentLandingSuccessMaxAngularRateDegS,
                envConfig.CurrentLandingSuccessMaxYawErrorDeg,
                envConfig.CurrentLandingPlatformRequired,
                envConfig.CurrentLandingPlatformPhysicalActive,
                _landingPlatformInsideCapture,
                _landingPlatformStable,
                _landingPlatformStableTime,
                envConfig.CurrentLandingPlatformStableHoldTime,
                envConfig.CurrentLandingPlatformHalfSize);
        }

        /// <summary>
        /// Logs a landing episode end using freshly measured terms when an
        /// external reset or max-step event ends the episode.
        /// </summary>
        void LogLandingEpisodeEnd(EpisodeTerminationReason reason)
        {
            if (_landingEpisodeEndLogged || envConfig == null || envConfig.scenario != ScenarioType.Landing)
                return;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            LogLandingEpisodeEnd(reason, terms, BuildRewardRuntimeContext(), float.NaN);
        }

        /// <summary>
        /// Emits a detailed landing diagnostic line once per episode, including
        /// terminal reason, success checks, curriculum progress, and platform state.
        /// </summary>
        void LogLandingEpisodeEnd(
            EpisodeTerminationReason reason,
            RewardTerms terms,
            RewardRuntimeContext context,
            float terminalReward)
        {
            if (_landingEpisodeEndLogged || envConfig == null || envConfig.scenario != ScenarioType.Landing)
                return;

            _landingEpisodeEndLogged = true;

            float tiltDeg = Mathf.Acos(Mathf.Clamp(terms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            float effectiveTiltLimit = Mathf.Min(
                context.landingSuccessMaxTiltDeg,
                Mathf.Acos(0.94f) * Mathf.Rad2Deg);
            bool reachedCaptureAltitude = context.altitude <= context.terminalAltitude;
            bool successfulTouchdown = reason == EpisodeTerminationReason.LandingSuccessfulTouchdown;
            bool uprightPass = tiltDeg < effectiveTiltLimit;
            bool targetPass = terms.planarDistance < context.landingSuccessRadius;
            bool totalSpeedPass = terms.speed < context.landingSuccessMaxSpeed;
            bool verticalSpeedPass = Mathf.Abs(terms.verticalSpeed) < context.landingSuccessMaxVerticalSpeed;
            bool horizontalSpeedPass = terms.planarSpeed < context.landingSuccessMaxHorizontalSpeed;
            bool angularRatePass = terms.angularRateDegS < context.landingSuccessMaxAngularRateDegS;
            bool headingPass = terms.yawErrorDeg < context.landingSuccessMaxYawErrorDeg;
            bool platformPass = !context.landingPlatformRequired || context.landingPlatformStable;

            string terminalRewardText = float.IsNaN(terminalReward) ? "n/a" : terminalReward.ToString("F2");
            string touchdownChecks = reachedCaptureAltitude
                ? $" checks=[upright:{PassFail(uprightPass)}, target:{PassFail(targetPass)}, " +
                  $"totalSpeed:{PassFail(totalSpeedPass)}, verticalSpeed:{PassFail(verticalSpeedPass)}, " +
                  $"horizontalSpeed:{PassFail(horizontalSpeedPass)}, angularRate:{PassFail(angularRatePass)}, " +
                  $"heading:{PassFail(headingPass)}, platform:{PassFail(platformPass)}]"
                : string.Empty;
            string platformStatus =
                $" platform=[enabled:{envConfig.landingPlatformEnabled}, " +
                $"required:{context.landingPlatformRequired}, physical:{context.landingPlatformPhysicalActive}, " +
                $"inside:{context.landingPlatformInsideCapture}, stable:{context.landingPlatformStable}, " +
                $"stableTime:{context.landingPlatformStableTime:F2}/{context.landingPlatformStableHoldTime:F2}s, " +
                $"halfSize:{context.landingPlatformHalfSize:F2}m, " +
                $"contacts:{_landingPlatformPhysicalContactCount}, lastContactSpeed:{_landingPlatformLastContactSpeed:F2}m/s]";

            Debug.Log(
                $"[LandingEpisodeEnd] area={_areaIndex} episode={_episode} step={_step} " +
                $"duration={_step * Time.fixedDeltaTime:F2}s reason={reason} success={successfulTouchdown} " +
                $"terminalAltitudeReached={reachedCaptureAltitude} altitude={context.altitude:F2}m " +
                $"terminalAltitude={context.terminalAltitude:F2}m failureAltitude={context.landingFailureAltitude:F2}m " +
                $"distance3D={terms.distance3D:F2}m planarDistance={terms.planarDistance:F2}m " +
                $"uprightness={terms.upDot:F3} tilt={tiltDeg:F2}deg totalSpeed={terms.speed:F2}m/s " +
                $"verticalSpeed={terms.verticalSpeed:F2}m/s horizontalSpeed={terms.planarSpeed:F2}m/s " +
                $"angularRate={terms.angularRateDegS:F2}deg/s yawError={terms.yawErrorDeg:F2}deg " +
                $"fuel={context.fuelKg:F2}kg " +
                $"terminalReward={terminalRewardText} curriculumEnabled={envConfig.landingCurriculumEnabled} " +
                $"curriculum={envConfig.landingCurriculumProgress * 100f:F4}% " +
                $"curriculumEpisodes={envConfig.landingCurriculumEpisodeCount} " +
                $"curriculumSuccesses={envConfig.landingCurriculumSuccessfulEpisodes} " +
                $"recentSuccessRate={envConfig.LandingCurriculumSuccessRate * 100f:F1}% " +
                $"touchdownLimits=[planar<{context.landingSuccessRadius:F2}m, " +
                $"totalSpeed<{context.landingSuccessMaxSpeed:F2}m/s, " +
                $"absVerticalSpeed<{context.landingSuccessMaxVerticalSpeed:F2}m/s, " +
                $"horizontalSpeed<{context.landingSuccessMaxHorizontalSpeed:F2}m/s, " +
                $"tilt<{effectiveTiltLimit:F2}deg, " +
                $"angularRate<{context.landingSuccessMaxAngularRateDegS:F2}deg/s, " +
                $"yawError<{context.landingSuccessMaxYawErrorDeg:F2}deg]" +
                platformStatus +
                touchdownChecks);
        }

    }
}
