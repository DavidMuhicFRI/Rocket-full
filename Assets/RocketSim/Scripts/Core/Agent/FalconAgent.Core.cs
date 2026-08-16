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
            UpdateChopstickPlatformGeometry();
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
            _defaultSolverIterations = rb.solverIterations;
            _defaultSolverVelocityIterations = rb.solverVelocityIterations;
            _defaultCollisionDetectionMode = rb.collisionDetectionMode;
            rb.maxAngularVelocity = 30f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.useGravity = true;

            RefreshConfig();
            EnsureActuatorBuffers(true);

            _windEnvironment.Clear();
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
                    _episodeTerminationReason = DefaultExternalTerminationReason(maxStepOrReset: true);
                    LogLandingEpisodeEnd(_episodeTerminationReason);
                    TelemetryLogger.Instance?.CompleteEpisode(
                        _areaIndex,
                        _episode,
                        BuildTelemetryEpisodeOutcome());
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
            _hoverTrackCaptureLatched = false;
            _hoverTrackSegmentElapsedTime = 0f;
            _hoverTrackEpisodeCaptures = 0;
            _landingEpisodeSucceeded = false;
            _objectiveSuccessTerminalReached = false;
            _episodeTerminationReason = EpisodeTerminationReason.None;
            _rewardContributions.Clear();
            _engineRestartsThisStep = 0;
            _episodeEngineRestartCount = 0;
            ResetChopstickPlatformState();
            ResetLegLandingState();

            RefreshConfig();
            EnsureActuatorBuffers(true);
            ResetEpisodeRandom();
            PrepareLandingEpisodeProfile();
            CaptureObjectiveDifficulty();

            if (_hardwareTestMode)
            {
                _currentEpisodeCompleted = true;
                _telemetryLoggedThisStep = true;
                return;
            }

            _episodeElapsedSeconds = 0f;
            _episodeFaults.BeginEpisode(
                envConfig?.faults,
                envConfig != null ? envConfig.behaviorType : BehaviorType.Training,
                cfg,
                _areaIndex,
                _episode);
            
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

            _windEnvironment.BeginEpisode(envConfig, ref _episodeRandom);
            SpawnForScenario();
            InitializeHoverEngineAtEquilibrium();
            CaptureLandingEpisodeStartAltitude();
            RandomizeTarget();
            UpdateChopstickPlatformGeometry();
            UpdateChopstickPlatformState(0f);
            UpdateLegLandingState(0f);
            CaptureEpisodeInitialTelemetry();
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        /// <summary>
        /// Adds the current rocket state to the ML-Agents observation vector.
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();
            Vector3 guidanceVelocity = ScenarioReferenceVelocity();
            Vector3 goalPosition = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig);

            // 1. Relative position from the active guidance point to its target (3).
            // Chopstick landing uses CatchFrame; leg landing uses FeetFrame;
            // other scenarios use the root/base frame.
            sensor.AddObservation((goalPosition - guidancePosition) / 100f);

            // 2. Linear velocity (3)
            sensor.AddObservation(guidanceVelocity / 50f);

            // 3. Angular velocity (3)
            sensor.AddObservation(rb.angularVelocity / 10f);

            // 4. Orientation — full up vector (3)
            sensor.AddObservation(transform.up);

            // 5. Actuator state (throttle/gimbal, engine mode and timing,
            // fins, and RCS jets). Engine timing makes the actuator dynamics
            // Markov for a feed-forward policy during low-mass pulse control.
            for (int i = 0; i < cfg.independentEngineCount; i++)
                sensor.AddObservation(throttle[i]);
            for (int i = 0; i < cfg.independentEngineCount; i++)
                sensor.AddObservation(gimbal[i] / Mathf.Max(cfg.maxGimbal, 1f));
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                sensor.AddObservation(engineStates[i] == EngineRunState.Off ? 1f : 0f);
                sensor.AddObservation(engineStates[i] == EngineRunState.Starting ? 1f : 0f);
                sensor.AddObservation(engineStates[i] == EngineRunState.Running ? 1f : 0f);
                sensor.AddObservation(engineStates[i] == EngineRunState.Shutdown ? 1f : 0f);
                sensor.AddObservation(EngineConstraintTimeRemaining01(i));
            }
            for (int i = 0; i < cfg.finCount; i++)
                sensor.AddObservation(finAngles[i] / Mathf.Max(cfg.maxFinAngle, 1f));
            for (int i = 0; i < cfg.rcsJetCount; i++)
                sensor.AddObservation(RcsJetCommand(i));

            // 6. Fuel fraction (1)
            sensor.AddObservation(fuel / Mathf.Max(cfg.startFuelMass, 1f));

            // 7. Normalized altitude (1)
            sensor.AddObservation(guidancePosition.y / 250f);

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

            // 11. Leg touchdown policies need one contact bit per foot. Other
            // tasks omit these channels entirely instead of padding the model.
            if (envConfig.scenario == ScenarioType.LegLanding)
                for (int i = 0; i < RocketAgentSchema.LandingFootObservationCount; i++)
                    sensor.AddObservation(IsLandingFootOnPad(i) ? 1f : 0f);
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
            _objectiveSuccessTerminalReached = false;
            _currentEpisodeCompleted = true;
            _hoverTrackStableTime = 0f;
            _hoverTrackCaptureLatched = false;
            _hoverTrackSegmentElapsedTime = 0f;
            _rewardContributions.Clear();
            ResetChopstickPlatformState();
            ResetLegLandingState();

            fuel = Mathf.Clamp(cfg.startFuelMass, 0f, cfg.maxFuelMass);
            rcsPropellant = cfg.hasRCS ? cfg.rcsPropellantMass : 0f;
            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            rb.linearVelocity = linearVelocity;
            rb.angularVelocity = angularVelocity;
            Physics.SyncTransforms();

            ResetEpisodeRandom();
            CaptureObjectiveDifficulty();
            _windEnvironment.BeginEpisode(envConfig, ref _episodeRandom);
            q = 0f;
            aoaDeg = 0f;
            ClearRcsCommands();

            assembly.fins?.ApplyDeflections(finAngles);
            UpdateMassProperties();
            UpdateThrusterVisuals();
            UpdateChopstickPlatformGeometry();
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        /// <summary>
        /// Runs physics-step simulation logic at Unity fixed timestep intervals.
        /// </summary>
        void FixedUpdate()
        {
            _step++;
            CommitLegLandingContactFrame();
            _stepReward = 0f;
            _telemetryLoggedThisStep = false;
            _episodeEndedThisStep    = false;
            _hoverTrackTargetReachedThisStep = false;
            _engineRestartsThisStep = 0;
            _hoverTrackSegmentElapsedTime += Time.fixedDeltaTime;
            _episodeElapsedSeconds += Time.fixedDeltaTime;

            if (_manualControlActive)
                ApplyManualControlOverride();

            UpdateMassProperties();
            StepActuators();
            _windEnvironment.Step(envConfig, ref _episodeRandom, Time.fixedDeltaTime);
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
            UpdateChopstickPlatformGeometry();
            UpdateChopstickPlatformState(Time.fixedDeltaTime);
            UpdateLegLandingState(Time.fixedDeltaTime);

            if (!_hardwareTestMode)
            {
                _hoverTrackTargetReachedThisStep = UpdateHoverTrackSuccess();
                if (_hoverTrackTargetReachedThisStep)
                    _hoverTrackEpisodeCaptures++;

                // Capture state is part of the objective input, so it must be
                // finalized before reward and termination evaluation.
                CalculateRewards();

                if (!_telemetryLoggedThisStep)
                    LogTelemetry();

                if (envConfig.scenario == ScenarioType.HoverTracking &&
                    _hoverTrackTargetReachedThisStep)
                {
                    assembly.GetComponentInParent<SimulationAreaHost>()?.NotifyHoverTrackTargetReached();

                    if (envConfig.moveTargetEnabled && !_episodeEndedThisStep)
                    {
                        RandomizeTarget();
                    }
                }
            }
        }

        /// <summary>
        /// Ends the current ML-Agents episode once, logs the final telemetry
        /// row, flushes the episode summary, and notifies curriculum owners.
        /// </summary>
        public new void EndEpisode()
        {
            if (_episodeEndedThisStep) return;

            if (_episodeTerminationReason == EpisodeTerminationReason.None)
                _episodeTerminationReason = DefaultExternalTerminationReason(maxStepOrReset: false);

            LogLandingEpisodeEnd(_episodeTerminationReason);
            LogTelemetry(forceStepWrite: true);
            TelemetryLogger.Instance?.CompleteEpisode(
                _areaIndex,
                _episode,
                BuildTelemetryEpisodeOutcome());
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
        /// Notifies the owning SimulationAreaHost that a real episode ended so
        /// shared curriculum counters can advance.
        /// </summary>
        void NotifyEpisodeCompleted()
        {
            // Curriculum progresses after a real completed/reset episode, not at
            // the first OnEpisodeBegin bootstrap.
            bool successfulEpisode = envConfig.scenario switch
            {
                ScenarioType.HoverTracking => WasHoverTrackingEpisodeSuccessful(),
                ScenarioType.ChopstickLanding => _landingEpisodeSucceeded,
                ScenarioType.LegLanding => _landingEpisodeSucceeded,
                _ => false
            };
            bool includeInCurriculumEstimate =
                !envConfig.scenario.IsLanding() || !_landingEpisodeUsesEasierReplay;
            assembly.GetComponentInParent<SimulationAreaHost>()?.NotifyEpisodeEnd(
                successfulEpisode,
                includeInCurriculumEstimate);
        }

        /// <summary>
        /// A configured capture-count goal requires reaching that terminal goal.
        /// Without the goal rule, one capture remains the curriculum success unit.
        /// </summary>
        bool WasHoverTrackingEpisodeSuccessful()
        {
            if (envConfig == null || envConfig.scenario != ScenarioType.HoverTracking)
                return false;

            TerminationParameters termination =
                envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            return termination.trackingCaptureGoalEnabled
                ? _objectiveSuccessTerminalReached
                : _hoverTrackEpisodeCaptures > 0;
        }

        /// <summary>
        /// Caches the immutable physics snapshot built from the current rocket
        /// assembly, falling back to static Falcon 9 values when no assembly exists.
        /// </summary>
        void RefreshConfig()
        {
            cfg = assembly ? assembly.GetPhysicsConfig() : RocketPhysicsConfigFactory.Falcon9Fallback;
            ConfigureCatchFrame();
            ConfigureLegLandingHardware();
        }

        /// <summary>
        /// Freezes the curriculum difficulty used by this episode's objective.
        /// Shared curriculum state may advance while other parallel agents are
        /// still running, so reward and termination thresholds must not drift.
        /// </summary>
        void CaptureObjectiveDifficulty()
        {
            _objectiveDifficulty01 = envConfig == null ? 0f : envConfig.scenario switch
            {
                ScenarioType.ChopstickLanding => ActiveLandingProfile.difficulty01,
                ScenarioType.LegLanding => ActiveLandingProfile.difficulty01,
                ScenarioType.HoverTracking => envConfig.hoverTrackCurriculumProgress,
                _ => 0f
            };
        }

    }
}
