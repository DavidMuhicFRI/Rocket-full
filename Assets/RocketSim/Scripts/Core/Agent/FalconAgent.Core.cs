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

            wind = targetWind = Vector3.zero;
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

            ResetWindForEpisode();
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

            // 11. Four fixed landing-foot contact slots. They remain zero in
            // non-leg scenarios so Hover -> Landing models stay shape-compatible.
            for (int i = 0; i < RocketAgentSchema.LandingFootObservationCount; i++)
                sensor.AddObservation(envConfig.scenario == ScenarioType.LegLanding && IsLandingFootOnPad(i) ? 1f : 0f);
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
            ResetWindForEpisode();
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
                envConfig.GetTrainingObjective(envConfig.scenario),
                _rewardContributions);
            // Dense shaping terms describe a reward rate. Integrating that rate
            // over simulated time keeps return magnitude independent of the
            // FixedUpdate frequency while terminal/event rewards remain one-offs.
            AddReward(decision.shapingRate * Time.fixedDeltaTime);

            if (decision.eventReward != 0f)
                AddReward(decision.eventReward);

            if (decision.hasTerminalReward)
                AddReward(decision.terminalReward);

            if (decision.successTerminal)
            {
                _objectiveSuccessTerminalReached = true;
                if (envConfig.scenario.IsLanding())
                    _landingEpisodeSucceeded = true;
            }

            if (decision.endEpisode)
            {
                _episodeTerminationReason = decision.terminationReason;
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
        /// Captures stable episode outcome fields used by both training and
        /// evaluation summaries before ML-Agents immediately resets the agent.
        /// </summary>
        TelemetryEpisodeOutcome BuildTelemetryEpisodeOutcome()
        {
            DecisionRequester decisionRequester = GetComponent<DecisionRequester>();
            RewardTerms finalTerms = envConfig != null
                ? MeasureRewardTerms(ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig))
                : default;
            float finalTiltDeg = Mathf.Acos(Mathf.Clamp(finalTerms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            bool success = envConfig != null && envConfig.scenario switch
            {
                ScenarioType.HoverTracking => WasHoverTrackingEpisodeSuccessful(),
                ScenarioType.ChopstickLanding => _landingEpisodeSucceeded,
                ScenarioType.LegLanding => _landingEpisodeSucceeded,
                _ => false
            };
            float globalDifficulty = envConfig == null ? 0f : envConfig.scenario switch
            {
                ScenarioType.HoverTracking => envConfig.hoverTrackCurriculumProgress,
                ScenarioType.ChopstickLanding => envConfig.landingCurriculumProgress,
                ScenarioType.LegLanding => envConfig.legLandingCurriculumProgress,
                _ => 0f
            };
            float episodeDifficulty = envConfig != null && envConfig.scenario.IsLanding()
                ? ActiveLandingProfile.difficulty01
                : globalDifficulty;

            return new TelemetryEpisodeOutcome
            {
                completed = true,
                success = success,
                terminationReason = _episodeTerminationReason,
                environmentSeed = envConfig != null ? envConfig.environmentSeed : 0,
                episodeSeed = _episodeSeed,
                curriculumGlobalDifficulty01 = globalDifficulty,
                curriculumDifficulty01 = episodeDifficulty,
                curriculumReplay = envConfig != null &&
                                   envConfig.scenario.IsLanding() &&
                                   _landingEpisodeUsesEasierReplay,
                scenario = envConfig != null ? envConfig.scenario : ScenarioType.ChopstickLanding,
                landingStartAltitude = envConfig != null && envConfig.scenario.IsLanding()
                    ? _landingEpisodeStartAltitude
                    : 0f,
                landingFlyawayAltitude = envConfig != null && envConfig.scenario.IsLanding()
                    ? _landingEpisodeFlyawayAltitude
                    : 0f,
                initialPlanarDistanceM = _episodeInitialPlanarDistance,
                initialYawErrorDeg = _episodeInitialYawErrorDeg,
                initialSpeedMps = _episodeInitialSpeed,
                initialVerticalSpeedMps = _episodeInitialVerticalSpeed,
                initialHorizontalSpeedMps = _episodeInitialHorizontalSpeed,
                initialTiltDeg = _episodeInitialTiltDeg,
                initialAngularRateDegS = _episodeInitialAngularRateDegS,
                initialFuelKg = _episodeInitialFuelKg,
                initialVehicleMassKg = _episodeInitialVehicleMassKg,
                minimumCommandableNonzeroThrustToWeight = _episodeMinimumCommandableNonzeroThrustToWeight,
                allEnginesMinimumThrustToWeight = _episodeAllEnginesMinimumThrustToWeight,
                allEnginesMaximumThrustToWeight = _episodeAllEnginesMaximumThrustToWeight,
                durationSeconds = _episodeElapsedSeconds,
                fixedDeltaTimeSeconds = Time.fixedDeltaTime,
                decisionPeriod = decisionRequester ? decisionRequester.DecisionPeriod : 1,
                finalPlanarDistanceM = finalTerms.planarDistance,
                finalYawErrorDeg = finalTerms.yawErrorDeg,
                finalSpeedMps = finalTerms.speed,
                finalVerticalSpeedMps = finalTerms.verticalSpeed,
                finalHorizontalSpeedMps = finalTerms.planarSpeed,
                finalTiltDeg = finalTiltDeg,
                finalAngularRateDegS = finalTerms.angularRateDegS,
                fuelUsedKg = Mathf.Max(0f, cfg.startFuelMass - fuel),
                rcsPropellantUsedKg = Mathf.Max(0f, cfg.rcsPropellantMass - rcsPropellant),
                engineRestartCount = _episodeEngineRestartCount,
                legTouchdownOccurred = _legTouchdownStarted,
                legFeetOnPad = LegLandingContactEvaluator.CountFeet(_legFootMask),
                legFootOutsidePad = _legFootOutsidePad,
                legStructuralStrike = _legStructuralStrike,
                legTouchdownTimeSeconds = _legTouchdownTime,
                legFirstContactSpeedMps = _legFirstContactSpeed,
                legFirstContactVerticalSpeedMps = _legFirstContactVerticalSpeed,
                legFirstContactHorizontalSpeedMps = _legFirstContactHorizontalSpeed,
                legFirstContactTiltDeg = _legFirstContactTiltDeg,
                legFirstContactAngularRateDegS = _legFirstContactAngularRateDegS,
                legStableHoldSeconds = _legStableTime,
                legMaximumContactImpulseNs = _legMaximumContactImpulseNs,
                legMaximumReboundHeightM = _legMaximumReboundHeightM
            };
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
            cfg = assembly ? assembly.GetPhysicsConfig() : RocketAssembly.Falcon9StaticFallback;
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

        /// <summary>
        /// Packages live measurements and one-step events for the pure reward
        /// evaluator. All tunable values remain in the scenario objective.
        /// </summary>
        RewardRuntimeContext BuildRewardRuntimeContext()
        {
            float terminalAltitude = envConfig.scenario.IsLanding()
                ? ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y
                : ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig);
            return new RewardRuntimeContext(
                altitude: ScenarioReferenceLocalPosition().y,
                terminalAltitude: terminalAltitude,
                episodeStartAltitude: _landingEpisodeStartAltitude,
                gravityMagnitude: Mathf.Abs(Physics.gravity.y),
                episodeElapsedSeconds: _episodeElapsedSeconds,
                curriculumDifficulty01: _objectiveDifficulty01,
                fuelKg: fuel,
                landingPlatformInsideCapture: _landingPlatformInsideCapture,
                landingPlatformStable: _landingPlatformStable,
                landingPlatformBecameStable: _landingPlatformBecameStable,
                landingPlatformStableTime: _landingPlatformStableTime,
                engineRestartsThisStep: _engineRestartsThisStep,
                hoverTrackTargetCapturedThisStep: _hoverTrackTargetReachedThisStep,
                hoverTrackEpisodeCaptures: _hoverTrackEpisodeCaptures,
                legTouchdownStarted: _legTouchdownStarted,
                legFirstContactThisStep: _legFirstContactThisStep,
                legFeetOnPad: LegLandingContactEvaluator.CountFeet(_legFootMask),
                legFootOutsidePad: _legFootOutsidePad,
                legStructuralStrike: _legStructuralStrike,
                legStable: _legStable,
                legBecameStable: _legBecameStable,
                legStableTime: _legStableTime,
                legFirstContactSpeed: _legFirstContactSpeed,
                legFirstContactVerticalSpeed: _legFirstContactVerticalSpeed,
                legFirstContactHorizontalSpeed: _legFirstContactHorizontalSpeed,
                legFirstContactTiltDeg: _legFirstContactTiltDeg,
                legFirstContactAngularRateDegS: _legFirstContactAngularRateDegS,
                legExcessiveRebound: _legExcessiveRebound);
        }

        /// <summary>
        /// Logs a landing episode end using freshly measured terms when an
        /// external reset or max-step event ends the episode.
        /// </summary>
        void LogLandingEpisodeEnd(EpisodeTerminationReason reason)
        {
            if (_landingEpisodeEndLogged || envConfig == null || !envConfig.scenario.IsLanding())
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
            if (_landingEpisodeEndLogged || envConfig == null || !envConfig.scenario.IsLanding())
                return;

            _landingEpisodeEndLogged = true;

            TerminationParameters termination =
                envConfig.GetTrainingObjective(envConfig.scenario).terminations;
            float difficulty = context.curriculumDifficulty01;
            float successRadius = termination.landingSuccessRadiusM.At(difficulty);
            float successMaxTotalSpeed = termination.landingSuccessMaxTotalSpeedMps.At(difficulty);
            float successMaxVerticalSpeed = termination.landingSuccessMaxVerticalSpeedMps.At(difficulty);
            float successMaxHorizontalSpeed = termination.landingSuccessMaxHorizontalSpeedMps.At(difficulty);
            float successMaxTilt = termination.landingSuccessMaxTiltDeg.At(difficulty);
            float successMaxAngularRate = termination.landingSuccessMaxAngularRateDegS.At(difficulty);
            float successMaxYawError = termination.landingSuccessMaxYawErrorDeg.At(difficulty);
            float stableHoldSeconds = termination.landingStableHoldSeconds.At(difficulty);
            float tiltDeg = Mathf.Acos(Mathf.Clamp(terms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            bool isChopstick = envConfig.scenario == ScenarioType.ChopstickLanding;
            bool reachedTargetAltitude = context.altitude <= context.terminalAltitude;
            bool successfulTouchdown = reason == EpisodeTerminationReason.ChopstickSuccessfulCapture ||
                                       reason == EpisodeTerminationReason.LegLandingSuccessfulTouchdown;
            bool uprightPass = tiltDeg <= successMaxTilt;
            bool targetPass = terms.planarDistance <= successRadius;
            bool totalSpeedPass = terms.speed <= successMaxTotalSpeed;
            bool verticalSpeedPass = Mathf.Abs(terms.verticalSpeed) <= successMaxVerticalSpeed;
            bool horizontalSpeedPass = terms.planarSpeed <= successMaxHorizontalSpeed;
            bool angularRatePass = terms.angularRateDegS <= successMaxAngularRate;
            bool headingPass = Mathf.Abs(terms.yawErrorDeg) <= successMaxYawError;
            bool platformPass = !termination.chopstickRequireStablePlatform ||
                                (context.landingPlatformInsideCapture &&
                                 context.landingPlatformStable &&
                                 context.landingPlatformStableTime >= stableHoldSeconds);

            string terminalRewardText = float.IsNaN(terminalReward) ? "n/a" : terminalReward.ToString("F2");
            string touchdownChecks = string.Empty;
            if (reachedTargetAltitude || context.legTouchdownStarted)
            {
                touchdownChecks = isChopstick
                    ? $" checks=[upright:{PassFail(uprightPass)}, target:{PassFail(targetPass)}, " +
                      $"totalSpeed:{PassFail(totalSpeedPass)}, verticalSpeed:{PassFail(verticalSpeedPass)}, " +
                      $"horizontalSpeed:{PassFail(horizontalSpeedPass)}, angularRate:{PassFail(angularRatePass)}, " +
                      $"heading:{PassFail(headingPass)}, platform:{PassFail(platformPass)}]"
                    : $" checks=[upright:{PassFail(uprightPass)}, target:{PassFail(targetPass)}, " +
                      $"totalSpeed:{PassFail(totalSpeedPass)}, verticalSpeed:{PassFail(verticalSpeedPass)}, " +
                      $"horizontalSpeed:{PassFail(horizontalSpeedPass)}, angularRate:{PassFail(angularRatePass)}, " +
                      $"feet:{PassFail(context.legFeetOnPad >= termination.legMinimumStableFeet)}, " +
                      $"insidePad:{PassFail(!context.legFootOutsidePad)}, " +
                      $"noStrike:{PassFail(!context.legStructuralStrike)}, stable:{PassFail(context.legStable)}]";
            }
            string platformStatus = isChopstick
                ? $" chopstick=[enabled:{envConfig.landingPlatformEnabled}, " +
                  $"required:{termination.chopstickRequireStablePlatform}, simulated:true, " +
                  $"inside:{context.landingPlatformInsideCapture}, stable:{context.landingPlatformStable}, " +
                  $"stableTime:{context.landingPlatformStableTime:F2}/{stableHoldSeconds:F2}s, " +
                  $"halfSize:{ActiveLandingProfile.platformHalfSize:F2}m]"
                : $" feet=[onPad:{context.legFeetOnPad}/4, outside:{context.legFootOutsidePad}, " +
                  $"structuralStrike:{context.legStructuralStrike}, stable:{context.legStable}, " +
                  $"stableTime:{context.legStableTime:F2}/{stableHoldSeconds:F2}s]";

            Debug.Log(
                $"[LandingEpisodeEnd] area={_areaIndex} episode={_episode} step={_step} " +
                $"duration={_step * Time.fixedDeltaTime:F2}s reason={reason} success={successfulTouchdown} " +
                $"targetAltitudeReached={reachedTargetAltitude} altitude={context.altitude:F2}m " +
                $"terminalAltitude={context.terminalAltitude:F2}m startAltitude={_landingEpisodeStartAltitude:F2}m " +
                $"flyawayAltitude={_landingEpisodeFlyawayAltitude:F2}m " +
                $"distance3D={terms.distance3D:F2}m planarDistance={terms.planarDistance:F2}m " +
                $"uprightness={terms.upDot:F3} tilt={tiltDeg:F2}deg totalSpeed={terms.speed:F2}m/s " +
                $"verticalSpeed={terms.verticalSpeed:F2}m/s horizontalSpeed={terms.planarSpeed:F2}m/s " +
                $"angularRate={terms.angularRateDegS:F2}deg/s yawError={terms.yawErrorDeg:F2}deg " +
                $"fuel={context.fuelKg:F2}kg " +
                $"terminalReward={terminalRewardText} curriculumEnabled={envConfig.ActiveLandingCurriculumEnabled} " +
                $"curriculumMode={envConfig.ActiveLandingCurriculumMode} " +
                $"globalCurriculum={envConfig.ActiveLandingCurriculumProgress * 100f:F4}% " +
                $"episodeDifficulty={ActiveLandingProfile.difficulty01 * 100f:F4}% " +
                $"easierReplay={_landingEpisodeUsesEasierReplay} " +
                $"curriculumEpisodes={envConfig.ActiveLandingCurriculumEpisodeCount} " +
                $"curriculumSuccesses={envConfig.ActiveLandingCurriculumSuccessfulEpisodes} " +
                $"recentSuccessRate={envConfig.ActiveLandingCurriculumSuccessRate * 100f:F1}% " +
                $"touchdownLimits=[planar<={successRadius:F2}m, " +
                $"totalSpeed<={successMaxTotalSpeed:F2}m/s, " +
                $"absVerticalSpeed<={successMaxVerticalSpeed:F2}m/s, " +
                $"horizontalSpeed<={successMaxHorizontalSpeed:F2}m/s, " +
                $"tilt<={successMaxTilt:F2}deg, " +
                $"angularRate<={successMaxAngularRate:F2}deg/s" +
                (isChopstick ? $", absYawError<={successMaxYawError:F2}deg]" : "]") +
                platformStatus +
                touchdownChecks);
        }

        /// <summary>
        /// Converts an external reset/end into the active landing task's
        /// scenario-specific terminal category. Non-landing callers retain None.
        /// </summary>
        EpisodeTerminationReason DefaultExternalTerminationReason(bool maxStepOrReset)
        {
            if (envConfig != null && envConfig.scenario == ScenarioType.LegLanding)
                return maxStepOrReset
                    ? EpisodeTerminationReason.LegLandingMaxStepOrExternalReset
                    : EpisodeTerminationReason.LegLandingExternalEndRequest;
            if (envConfig != null && envConfig.scenario == ScenarioType.ChopstickLanding)
                return maxStepOrReset
                    ? EpisodeTerminationReason.ChopstickMaxStepOrExternalReset
                    : EpisodeTerminationReason.ChopstickExternalEndRequest;
            if (envConfig != null &&
                (envConfig.scenario == ScenarioType.Hover || envConfig.scenario == ScenarioType.HoverTracking))
                return maxStepOrReset
                    ? EpisodeTerminationReason.HoverMaxStepOrExternalReset
                    : EpisodeTerminationReason.HoverExternalEndRequest;
            return EpisodeTerminationReason.None;
        }

    }
}
