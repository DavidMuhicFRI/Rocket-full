using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Random = UnityEngine.Random;

namespace RocketSim
{
    // ============================================================================
    //  FalconAgent — Falcon 9 landing simulation (ML-Agents)
    //
    //  Physics constants come from RocketAssembly.GetPhysicsConfig()
    //
    //  envConfig is a SHARED reference set by TrainingAreaManager at spawn time.
    //  The right-side panel writes to the same object — all agents see changes.
    //
    // ============================================================================
    public class FalconAgent : Agent
    {
        [Header("References — wire inside prefab")]
        public Rigidbody rb;

        public RocketAssembly assembly;
        public Transform targetPad;

        [Header("Configs — assigned by TrainingAreaManager via ConfigBridge")]
        public SimEnvironmentConfig envConfig = new();
        
        // Sensors
        RocketSensorPackage _sensors = new();
        
        // Telemetry identity
        int _areaIndex;   // set by TrainingAreaManager after spawn
        int _episode;
        int _step;
        float _stepReward;   // accumulator so we can log reward per step
        bool _hasEpisodeStarted;
        bool _currentEpisodeCompleted;
        bool _telemetryLoggedThisStep;
        bool _episodeEndedThisStep;
        bool _hoverTrackTargetReachedThisStep;
        
        public void SetAreaIndex(int idx) => _areaIndex = idx;

        [Header("Flame Visual")] public float minFlameWidth = 1.5f;
        public float maxFlameWidth = 4f;
        public float minFlameLength = 2f;
        public float maxFlameLength = 20f;

        // ── Physics config ───────────────────────────
        RocketPhysicsConfig cfg;
        
        // ── Engine ─────────────────────────────────────────────────
        Vector2[] targetGimbal, gimbal;
        float[] targetThrottle, throttle;
        private float fuel;
        private float rcsPropellant;

        // Fins 
        float[] finAngles;
        float[] targetFinAngles;

        // RCS
        float[] rcsJets;

        // ── Wind ──────────────────────────────────────────────────────────────
        Vector3 wind, targetWind;
        bool _hardwareTestMode;
        bool _manualControlActive;
        float[] _manualThrottle;
        Vector2[] _manualGimbal;
        float[] _manualFinAngles;
        float[] _manualRcsJets;

        // ── Cached for observations / debug ───────────────────────────────────
        float q; // dynamic pressure
        float aoaDeg; // angle of attack
        float _hoverTrackStableTime;
        float _hoverTrackSegmentStartDistance;
        float _hoverTrackSegmentElapsedTime;

        const float Rho0 = 1.225f; // ISA sea-level density (kg/m³)
        const float HScale = 8500f; // ISA scale height (m)
        const float G0 = 9.80665f;
        const float RcsSpecificImpulse = 70f; // cold-gas nitrogen ballpark, seconds
        const float BaseGroundClearance = 0.5f;
        const float HoverTrackCycleCompleteReward = 4.0f;
        const float GustEventRateHz = 0.25f;
        const float EngineIgnitionThreshold = 0.08f;
        const float EngineShutdownThreshold = 0.03f;
        const float VacuumThrustMultiplier = 1.08f;
        const float VacuumIspMultiplier = 1.10f;

        //public getters for HUD
        public int GetEngineCount => throttle?.Length ?? 0;
        public float GetCurrentThrottle(int index) =>
            throttle != null && index >= 0 && index < throttle.Length ? throttle[index] : 0f;
        public float GetFuel => fuel;
        public float GetGimbalX(int index) =>
            gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].x : 0f;
        public float GetGimbalZ(int index) =>
            gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].y : 0f;
        public RocketPhysicsConfig CurrentPhysicsConfig => cfg;
        public float DynamicPressure => q;
        public float AngleOfAttackDeg => aoaDeg;
        public bool HardwareTestMode => _hardwareTestMode;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

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

        public override void OnEpisodeBegin()
        {
            if (_hasEpisodeStarted)
            {
                if (!_currentEpisodeCompleted && _step > 0)
                {
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
            _hoverTrackStableTime    = 0f;
            _hoverTrackSegmentElapsedTime = 0f;

            RefreshConfig();
            EnsureActuatorBuffers(true);

            if (_hardwareTestMode)
            {
                _currentEpisodeCompleted = true;
                _telemetryLoggedThisStep = true;
                return;
            }
            
            fuel = cfg.startFuelMass > 0f ? cfg.startFuelMass : cfg.maxFuelMass;
            rcsPropellant = cfg.hasRCS ? cfg.rcsPropellantMass : 0f;
            UpdateMassProperties();
            for (int i = 0; i < cfg.finCount; i++) finAngles[i] = targetFinAngles[i] = 0f;
            for (int i = 0; i < cfg.independentEngineCount; i++) throttle[i] = targetThrottle[i] = 0f;
            for (int i = 0; i < cfg.independentEngineCount; i++) gimbal[i] = targetGimbal[i] = Vector2.zero;
            assembly.fins?.ApplyDeflections(finAngles);
            UpdateFlame();

            ClearRcsCommands();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            wind = targetWind = RandomWind();
            SpawnForScenario();
            RandomizeTarget();
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        // =====================================================================
        //  Observations
        // =====================================================================

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

            // 5. Actuator state (cfg.independentEngineCount * 3 + _finCount + RCS jets)
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
        }
        // number of observations: 19 + cfg.independentEngineCount * 3 + _finCount + _rcsJetCount

        // =====================================================================
        //  Actions (continuous, all in [-1, +1])
        // =====================================================================

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_manualControlActive) return;

            var act = actions.ContinuousActions;
            var actionIndex = 0;
            // Throttle: map [-1,+1] → [0,1] → [minThrottle, 1] or off
            float[] raw = new float[cfg.independentEngineCount];
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                raw[i] = (act[actionIndex++] + 1f) * 0.5f;
                bool engineLit = targetThrottle[i] > 0f || throttle[i] > cfg.minThrottle * 0.5f;
                float threshold = engineLit ? EngineShutdownThreshold : EngineIgnitionThreshold;
                targetThrottle[i] = raw[i] > threshold ? Mathf.Lerp(cfg.minThrottle, 1f, raw[i]) : 0f;
            }

            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                var x = act[actionIndex++] * cfg.maxGimbal;
                var y = act[actionIndex++] * cfg.maxGimbal;
                targetGimbal[i] = new Vector2(x, y);
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
                {
                    float command = Mathf.Clamp01(act[actionIndex++]);
                    rcsJets[i] = command > 0.03f ? command : 0f;
                }
            }
            else
            {
                ClearRcsCommands();
            }

            UpdateFlame();
        }
        // number of actions: cfg.independentEngineCount * 3 + _finCount + _rcsJetCount

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            actionsOut.ContinuousActions.Clear();
        }

        public void SetHardwareTestMode(bool enabled)
        {
            _hardwareTestMode = enabled;
            if (!enabled) ClearManualControl();
        }

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
            _currentEpisodeCompleted = true;
            _hoverTrackStableTime = 0f;
            _hoverTrackSegmentElapsedTime = 0f;

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
            UpdateFlame();
            _sensors.Tick(rb, transform, 0f, 0f, 0f, 0f, 0f, cfg.radius, episodeStart: true);
        }

        public void SetManualControl(
            float[] throttle01,
            Vector2[] gimbalDeg,
            float[] finDeflectionsDeg,
            float[] rcsCommands01)
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
                _manualGimbal[i] = new Vector2(
                    Mathf.Clamp(command.x, -cfg.maxGimbal, cfg.maxGimbal),
                    Mathf.Clamp(command.y, -cfg.maxGimbal, cfg.maxGimbal));
            }

            for (int i = 0; i < _manualFinAngles.Length; i++)
            {
                float command = finDeflectionsDeg != null && i < finDeflectionsDeg.Length ? finDeflectionsDeg[i] : 0f;
                _manualFinAngles[i] = Mathf.Clamp(command, -cfg.maxFinAngle, cfg.maxFinAngle);
            }

            for (int i = 0; i < _manualRcsJets.Length; i++)
            {
                _manualRcsJets[i] = rcsCommands01 != null && i < rcsCommands01.Length
                    ? Mathf.Clamp01(rcsCommands01[i])
                    : 0f;
            }
        }

        public void ClearManualControl()
        {
            _manualControlActive = false;
            if (_manualThrottle != null) for (int i = 0; i < _manualThrottle.Length; i++) _manualThrottle[i] = 0f;
            if (_manualGimbal != null) for (int i = 0; i < _manualGimbal.Length; i++) _manualGimbal[i] = Vector2.zero;
            if (_manualFinAngles != null) for (int i = 0; i < _manualFinAngles.Length; i++) _manualFinAngles[i] = 0f;
            if (_manualRcsJets != null) for (int i = 0; i < _manualRcsJets.Length; i++) _manualRcsJets[i] = 0f;
            if (targetThrottle != null) for (int i = 0; i < targetThrottle.Length; i++) targetThrottle[i] = 0f;
            if (targetGimbal != null) for (int i = 0; i < targetGimbal.Length; i++) targetGimbal[i] = Vector2.zero;
            if (targetFinAngles != null) for (int i = 0; i < targetFinAngles.Length; i++) targetFinAngles[i] = 0f;
            ClearRcsCommands();
        }

        // =====================================================================
        //  FixedUpdate
        // =====================================================================

        void FixedUpdate()
        {
            _step++;
            _stepReward = 0f;
            _telemetryLoggedThisStep = false;
            _episodeEndedThisStep    = false;
            _hoverTrackTargetReachedThisStep = false;
            _hoverTrackSegmentElapsedTime += Time.fixedDeltaTime;

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
            float rho   = Rho0 * envConfig.AirDensityMultiplier *
                          Mathf.Exp(-sensorAltitude / HScale);
            float speed = (rb.linearVelocity - wind).magnitude;
            float lever = Mathf.Abs(cfg.cpLocalY - rb.centerOfMass.y);

            _sensors.Tick(rb, transform, rho, speed,
                lateralForceMag, axialForceMag + thrustForceMag,
                lever, cfg.radius);
            
            ApplyRcs();
            UpdateMassProperties();

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
                    assembly.GetComponentInParent<TrainingAreaManager>()?.NotifyHoverTrackTargetReached();
                    RandomizeTarget();
                    _hoverTrackStableTime = 0f;
                }
            }
        }

        // =====================================================================
        //  Actuator slew-rate limiting
        // =====================================================================

        void StepActuators()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                throttle[i] = Mathf.MoveTowards(throttle[i], targetThrottle[i], cfg.throttleSpoolRate * dt);
                gimbal[i] = Vector2.MoveTowards(gimbal[i], targetGimbal[i], cfg.gimbalSlewRate * dt);
            }
            for (int i = 0; i < cfg.finCount; i++) finAngles[i] = Mathf.MoveTowards(finAngles[i], targetFinAngles[i], cfg.finSlewRate * dt);

            assembly.fins?.ApplyDeflections(finAngles);
            UpdateFlame();
        }

        void ApplyManualControlOverride()
        {
            for (int i = 0; i < cfg.independentEngineCount; i++)
            {
                targetThrottle[i] = _manualThrottle != null && i < _manualThrottle.Length ? _manualThrottle[i] : 0f;
                targetGimbal[i] = _manualGimbal != null && i < _manualGimbal.Length ? _manualGimbal[i] : Vector2.zero;
            }

            for (int i = 0; i < cfg.finCount; i++)
                targetFinAngles[i] = _manualFinAngles != null && i < _manualFinAngles.Length ? _manualFinAngles[i] : 0f;

            if (cfg.hasRCS)
            {
                for (int i = 0; i < rcsJets.Length; i++)
                    rcsJets[i] = _manualRcsJets != null && i < _manualRcsJets.Length ? _manualRcsJets[i] : 0f;
            }
            else
            {
                ClearRcsCommands();
            }
        }

        // =====================================================================
        //  Wind
        // =====================================================================

        void StepWind()
        {
            if (!envConfig.windEnabled)
            {
                wind = targetWind = Vector3.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            float windAlpha = 1f - Mathf.Exp(-Mathf.Max(0f, envConfig.windChangeRate) * dt);
            wind = Vector3.Lerp(wind, targetWind, windAlpha);

            float gustChance = 1f - Mathf.Exp(-GustEventRateHz * dt);
            if (Random.value < gustChance)
            {
                float g = envConfig.EffectiveGustAmp;
                targetWind = RandomWind() + RandomPlanarVector(g);
            }
        }

        Vector3 RandomWind()
        {
            if (!envConfig.windEnabled) return Vector3.zero;

            return RandomPlanarVector(envConfig.windSpeed);
        }

        static Vector3 RandomPlanarVector(float maxMagnitude)
        {
            if (maxMagnitude <= 0f) return Vector3.zero;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float magnitude = Random.Range(0f, maxMagnitude);
            return new Vector3(Mathf.Cos(angle) * magnitude, 0f, Mathf.Sin(angle) * magnitude);
        }

        float AtmosphericPressureRatio()
        {
            float altitude = Mathf.Max(0f, transform.localPosition.y);
            return Mathf.Clamp01(Mathf.Exp(-altitude / HScale));
        }

        float CurrentEngineThrustScale()
        {
            return Mathf.Lerp(VacuumThrustMultiplier, 1f, AtmosphericPressureRatio());
        }

        float CurrentEngineSpecificImpulse()
        {
            float seaLevelIsp = Mathf.Max(cfg.specificImpulse, 1f);
            return seaLevelIsp * Mathf.Lerp(VacuumIspMultiplier, 1f, AtmosphericPressureRatio());
        }

        // =====================================================================
        //  Engine physics
        // =====================================================================

        float ApplyEngines()
        {
            if (fuel <= 0f || !assembly || !assembly.thrusters) return 0f;
            int activeIndex = 0;
            var thrusterCount = assembly.thrusters.transform.childCount;
            float requestedFuel = 0f;
            float dt = Time.fixedDeltaTime;
            float thrustScale = CurrentEngineThrustScale();
            float engineIsp = CurrentEngineSpecificImpulse();

            for (int i = 0; i < thrusterCount; i++)
            {
                Transform engine = assembly.thrusters.transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                var arrayIndex = cfg.independentEngines ? activeIndex : 0;
                if (arrayIndex < throttle.Length)
                {
                    float forceMag = throttle[arrayIndex] * cfg.maxThrust * thrustScale;
                    requestedFuel += forceMag / (engineIsp * G0) * dt;
                }

                activeIndex++;
            }

            if (requestedFuel <= 0f) return 0f;

            float fuelScale = Mathf.Clamp01(fuel / requestedFuel);
            float consumedFuel = 0f;
            float thrustForceMag = 0f;
            activeIndex = 0;

            for (int i = 0; i < thrusterCount; i++)
            {
                Transform engine = assembly.thrusters.transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                var arrayIndex = cfg.independentEngines ? activeIndex : 0;

                if (arrayIndex < throttle.Length && throttle[arrayIndex] > 0f)
                {
                    Vector3 localDir = Quaternion.Euler(gimbal[arrayIndex].x, 0f, gimbal[arrayIndex].y) * Vector3.up;
                    Vector3 worldDir = transform.TransformDirection(localDir);
                    float effectiveThrottle = throttle[arrayIndex] * fuelScale;
                    float forceMag = effectiveThrottle * cfg.maxThrust * thrustScale;

                    rb.AddForceAtPosition(worldDir * forceMag, engine.position);
                    thrustForceMag += forceMag;
                    consumedFuel += forceMag / (engineIsp * G0) * dt;
                }
                activeIndex++;
            }

            fuel = Mathf.Max(0f, fuel - consumedFuel);
            return thrustForceMag;
        }

        // =====================================================================
        //  Aerodynamics
        // =====================================================================

        // =====================================================================
        //  Aerodynamics
        //  Combines best-of-both implementations:
        //  - Per-fin sideslip (Doc 1): accurate crosswind / lateral-velocity response
        //  - Stall fade (Doc 1): lift collapses correctly beyond ~35°
        //  - Base drag (Doc 1): engine-bell wake, critical during entry burn
        //  - Correct pitching moment sign (both)
        //  - All fin forces applied AT the fin world-position (Doc 1): correct torque
        //  - activeSelf guard on fins group (Doc 1)
        // =====================================================================

        void ApplyAerodynamics(out float lateralForceMag, out float axialForceMag)
        {
            lateralForceMag = 0f;
            axialForceMag   = 0f;
            float altitude = Mathf.Max(0f, transform.localPosition.y);
        
            // ISA exponential atmosphere, scaled by weather
            float rho = Rho0 * envConfig.AirDensityMultiplier *
                        Mathf.Exp(-altitude / HScale);
        
            // Effective velocity = rocket velocity minus wind (wind is in world space)
            Vector3 effVel = rb.linearVelocity - wind;
            float   speed  = effVel.magnitude;
        
            // Decompose into rocket-local frame
            Vector3 localVel  = transform.InverseTransformDirection(effVel);
            float   vAxial    = localVel.y;                                   // along spine
            Vector3 vLatLocal = new Vector3(localVel.x, 0f, localVel.z);     // perpendicular to spine
            float   vLat      = vLatLocal.magnitude;
        
            q      = 0.5f * rho * speed * speed;                             // true dynamic pressure
            float bodyAxisAngleDeg = speed > 0.5f
                ? Vector3.Angle(transform.up, effVel.normalized)
                : 0f;
            aoaDeg = Mathf.Min(bodyAxisAngleDeg, 180f - bodyAxisAngleDeg);
        
            // ── 1. Axial drag ──────────────────────────────────────────────────────
            // Cd ≈ 0.30 for a cylinder nose-on. Signed so it opposes motion in both
            // directions (ascent AND descent).
            float axialDrag = 0.5f * rho * 0.30f * cfg.A_axial * vAxial * Mathf.Abs(vAxial);
            axialForceMag += Mathf.Abs(axialDrag); 
            rb.AddForce(-transform.up * axialDrag);

            Vector3 cpWorld = transform.TransformPoint(new Vector3(0f, cfg.cpLocalY, 0f));
        
            // ── 2. Lateral (broadside) drag ────────────────────────────────────────
            // Cd ≈ 1.0 for a cylinder side-on. ~41× stronger per unit v² than axial
            // because A_lateral >> A_axial and Cd are higher.
            if (vLat > 0.01f)
            {
                float lateralDrag = 0.5f * rho * 1.0f * cfg.A_lateral * vLat * vLat;
                lateralForceMag += lateralDrag;
                Vector3 lateralDir = -transform.TransformDirection(vLatLocal.normalized);
                rb.AddForceAtPosition(lateralDir * lateralDrag, cpWorld);
            }
        
            // ── 3. Base drag — low-pressure wake behind engine bells ───────────────
            // Contributes ~10-15% of total drag at speed. Especially significant
            // during the entry burn when the base faces the velocity vector.
            // Only active when the base is leading (descending tail-first, vAxial < 0).
            if (vAxial < -0.1f)
            {
                float baseDragMag = 0.5f * rho * 0.12f * cfg.A_axial * vAxial * vAxial;
                axialForceMag += baseDragMag;
                rb.AddForce(transform.up * baseDragMag); // tail-first descent: force points toward the nose
            }
        
            // ── 4. Destabilising pitching moments at Centre of Pressure ────────────
            // CP sits above CoM → any AoA creates a moment that increases AoA further.
            // Real boosters are open-loop unstable. Cn_α = 2 /rad (slender-body theory,
            // valid up to ~15° AoA; we apply it at all AoA as a reasonable approximation
            // for a sim since the fins/gimbal will correct large AoA before it matters).
            // Body side force is already applied at CP above. Keeping a separate
            // slender-body normal force here double-counts lateral aerodynamics.
        
            // ── 5. Grid fins ───────────────────────────────────────────────────────
            // Guard activeSelf on the fins GROUP — fins may be retracted mid-flight.
            if (cfg.hasFins && assembly.fins != null && assembly.fins.gameObject.activeSelf)
            {
                int activeFinIndex = 0;
                for (int i = 0; i < assembly.fins.transform.childCount; i++)
                {
                    Transform finTransform = assembly.fins.transform.GetChild(i);
                    if (!finTransform.gameObject.activeSelf) continue;
                    if (activeFinIndex >= cfg.finCount) break;
        
                    ApplySingleFin(activeFinIndex, finTransform, rho, effVel);
                    activeFinIndex++;
                }
            }
        }
        
        void ApplySingleFin(int index, Transform finTransform, float rho, Vector3 effVelWorld)
        {
            // ── Project velocity onto this fin's control plane ─────────────────────
            // Each fin deflects around the axis tangent to the rocket circumference.
            // We need three orthogonal directions: spine (up), radial, tangent.
        
            // Radial: outward from rocket centreline, in the horizontal plane only
            // (y-component zeroed so it is always perpendicular to the spine)
            Vector3 radialWorld = Vector3.ProjectOnPlane(
                finTransform.position - transform.position,
                transform.up);
            if (radialWorld.sqrMagnitude < 0.000001f)
                radialWorld = Vector3.ProjectOnPlane(finTransform.right, transform.up);
            radialWorld = radialWorld.normalized;
        
            // Tangent: the axis the fin rotates around
            Vector3 tangentWorld = Vector3.Cross(transform.up, radialWorld).normalized;
        
            // Velocity components in world space
            float vSpine   = Vector3.Dot(effVelWorld, transform.up);    // axial component
            float vTangent = Vector3.Dot(effVelWorld, tangentWorld);    // sweeps across fin face
        
            // ── Effective angle of attack seen by this fin ─────────────────────────
            // = commanded deflection PLUS the sideslip induced by lateral/crosswind
            // velocity sweeping across the fin face. Ignoring sideslip (Doc 2's flaw)
            // means fins feel no crosswind, which badly underestimates their authority.
            float deflectionRad   = finAngles[index] * Mathf.Deg2Rad;
            float sideslipRad     = Mathf.Abs(vSpine) > 0.5f
                ? Mathf.Atan2(vTangent, Mathf.Abs(vSpine))
                : 0f;
            float effectiveAoARad = deflectionRad + sideslipRad;
        
            // Dynamic pressure as seen by the fin (sign preserved: fins work going up
            // AND coming down, but force direction flips with vSpine sign)
            float qFin = 0.5f * rho * vSpine * Mathf.Abs(vSpine);
        
            // ── Lift — thin airfoil theory with stall fade ─────────────────────────
            // Cl = 2π·sin(α) is the classical inviscid result for a thin plate.
            // stallFade ramps lift to zero between 23° (0.4 rad) and 40° (0.7 rad)
            // to approximate separation. Without this, fins generate unrealistic lift
            // at large deflections and the agent learns to exploit it.
            float clRaw    = 2f * Mathf.PI * Mathf.Sin(effectiveAoARad);
            float stallFade = Mathf.Clamp01(1f - (Mathf.Abs(effectiveAoARad) - 0.4f) / 0.3f);
            float cl        = cfg.finLiftScale * clRaw * stallFade;
        
            // Lift acts along the tangent direction (perp to both spine and radial)
            Vector3 liftDir   = Mathf.Sign(qFin) * tangentWorld;
            Vector3 liftForce = liftDir * (Mathf.Abs(qFin) * cfg.A_fin * cl);
        
            // ── Induced drag ───────────────────────────────────────────────────────
            // Baseline profile drag (0.05) + lift-induced drag (scales with sin²α).
            // Applied AT the fin position so it also contributes a pitching moment —
            // this is physically correct and was missing in Doc 2.
            float   cdInduced = 0.05f + 1.8f * Mathf.Sin(effectiveAoARad) * Mathf.Sin(effectiveAoARad);
            float   dragMag    = Mathf.Abs(qFin) * cfg.A_fin * cdInduced;
            Vector3 dragForce  = -transform.up * (Mathf.Sign(vSpine) * dragMag);
        
            // ── Apply combined force at the actual fin world position ──────────────
            // Using the fin's real Transform.position (not the parent group's Y — see
            // the bug in the old ApplySingleFin) ensures the moment arm is correct.
            rb.AddForceAtPosition(liftForce + dragForce, finTransform.position);
        }

        // =====================================================================
        //  RCS — cold gas nitrogen thrusters
        // =====================================================================

        void ApplyRcs()
        {
            if (!cfg.hasRCS || !assembly || !assembly.rcs)
                return;

            if (rcsJets == null || rcsJets.Length == 0)
            {
                assembly.rcs.ShowCommands(null);
                return;
            }

            int jetCount = Mathf.Min(rcsJets.Length, cfg.rcsJetCount);
            float requestedPropellant = 0f;
            float dt = Time.fixedDeltaTime;

            for (int i = 0; i < jetCount; i++)
            {
                float command = Mathf.Clamp01(rcsJets[i]);
                requestedPropellant += command * cfg.rcsThrust / (RcsSpecificImpulse * G0) * dt;
            }

            float propellantScale = requestedPropellant > 0f ? Mathf.Clamp01(rcsPropellant / requestedPropellant) : 0f;
            float consumedPropellant = 0f;
            float[] displayedCommands = propellantScale >= 0.999f ? rcsJets : new float[jetCount];

            for (int i = 0; i < jetCount; i++)
            {
                float command = Mathf.Clamp01(rcsJets[i]) * propellantScale;
                if (displayedCommands != rcsJets)
                    displayedCommands[i] = command;
                if (command <= 0.001f) continue;

                Vector3 force = assembly.rcs.WorldForceDirectionForJet(i) * (command * cfg.rcsThrust);
                rb.AddForceAtPosition(force, assembly.rcs.WorldPositionForJet(i));
                consumedPropellant += command * cfg.rcsThrust / (RcsSpecificImpulse * G0) * dt;
            }

            assembly.rcs.ShowCommands(displayedCommands);
            rcsPropellant = Mathf.Max(0f, rcsPropellant - consumedPropellant);
        }

        // =====================================================================
        //  Mass properties — updated every FixedUpdate as fuel burns
        // =====================================================================

        void UpdateMassProperties()
        {
            float total = cfg.dryMass + fuel + rcsPropellant;
            rb.mass = total;
        
            float l   = cfg.length;
            float r   = cfg.radius;
            float bot = 0f;  // local Y of the base plane
        
            // ── Component masses ───────────────────────────────────────────────────
            float engMass = Mathf.Clamp(cfg.engineDryMass, 0f, cfg.dryMass);
            float finMass = Mathf.Clamp(cfg.finDryMass, 0f, cfg.dryMass - engMass);
            float rcsDryMass = Mathf.Clamp(cfg.rcsDryMass, 0f, cfg.dryMass - engMass - finMass);
            float structMass = Mathf.Max(0f, cfg.dryMass - engMass - finMass - rcsDryMass);
        
            // ── Component centres of mass (local Y) ────────────────────────────────
            float engY    = bot;        // engine cluster sits at the very base
            float structY = l * 0.45f;  // airframe/tanks/interstage distributed along the stage
            float finY    = Mathf.Max(bot, l - 1.2f);
            float rcsY    = Mathf.Max(bot, l - 0.2f);
            float fuelY   = l * 0.56f;  // LOX + RP-1 tank stack average
        
            // ── Overall CoM — weighted average (physically correct) ────────────────
            // The old code used Mathf.Lerp(bot+0.10, bot+0.30, fuelFrac) which was a
            // rough heuristic. This computes it properly from the three-component split
            // so it shifts continuously and correctly as propellant burns off.
            float comY = (engMass * engY +
                          structMass * structY +
                          finMass * finY +
                          rcsDryMass * rcsY +
                          rcsPropellant * rcsY +
                          fuel * fuelY) / total;
            rb.centerOfMass = new Vector3(0f, comY, 0f);
        
            // ── Pitch / Yaw inertia (Ixx = Izz) about comY ────────────────────────
            // For each component:  I = I_own_CoM  +  m * (componentY - comY)²
            //                          (solid cyl)    (parallel-axis theorem)
            //
            // The old code lumped fuel into a single "body" term with the dry structure
            // and assumed both shared the geometric centre (y = 0). That underestimated
            // the inertia when the tanks are full, because the high fuel mass was placed
            // too close to the pivot.
        
            // Engine cluster — treated as a point mass (compact bell/manifold geometry)
            float I_eng = engMass * (engY - comY) * (engY - comY);
        
            // Dry structure — slender rod spanning the lower ~30 % of the stage
            // (octaweb, interstage, grid-fin attachment ring, landing legs)
            float l_struct  = l * 0.90f;
            float I_struct  = (structMass / 12f) * (3f * r * r + l_struct * l_struct)
                            + structMass * (structY - comY) * (structY - comY);

            float I_fins = finMass * (finY - comY) * (finY - comY);
            float I_rcs = (rcsDryMass + rcsPropellant) * (rcsY - comY) * (rcsY - comY);
        
            // Fuel — uniform cylinder in the upper tank section (~60 % of stage length)
            // Inertia shrinks continuously as propellant burns, correctly pulling CoM down
            float l_fuel  = l * 0.60f;
            float I_fuel  = (fuel / 12f) * (3f * r * r + l_fuel * l_fuel)
                          + fuel * (fuelY - comY) * (fuelY - comY);
        
            float I_pitch = I_eng + I_struct + I_fins + I_rcs + I_fuel;
        
            // ── Spin inertia (Iyy) — full-cylinder approximation ──────────────────
            // The solid-cylinder formula (½mr²) slightly overestimates because the
            // interior is hollow, but the error is small vs the overall r and is
            // consistent with treating the airframe as a structural tube + dense fluid.
            float I_spin = 0.5f * total * r * r;
        
            rb.inertiaTensor         = new Vector3(I_pitch, I_spin, I_pitch);
            rb.inertiaTensorRotation = Quaternion.identity;
        }

        // =====================================================================
        //  Scenario spawn
        // =====================================================================

        void SpawnForScenario()
        {
            float rx = Random.Range(-5f, 5f);
            float rz = Random.Range(-5f, 5f);
            switch (envConfig.scenario)
            {
                case ScenarioType.Landing:
                    transform.localPosition = new Vector3(rx, 60f, rz);
                    transform.localRotation = Quaternion.Euler(
                        Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                    rb.linearVelocity = Vector3.down * Random.Range(5f, 15f);
                    break;

                case ScenarioType.Hover:
                    transform.localPosition = new Vector3(rx, 30f, rz);
                    transform.localRotation = Quaternion.Euler(
                        Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                    rb.linearVelocity = new Vector3(
                        Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
                    break;

                case ScenarioType.HoverTracking:
                    transform.localPosition = new Vector3(rx, 30f, rz);
                    transform.localRotation = Quaternion.Euler(
                        Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                    break;

                case ScenarioType.Takeoff:
                    transform.localPosition = new Vector3(rx, BaseGroundClearance, rz);
                    transform.localRotation = Quaternion.identity;
                    rb.linearVelocity = Vector3.zero;
                    break;

                case ScenarioType.BellyFlop:
                    transform.localPosition = new Vector3(rx, 100f, rz);
                    transform.localRotation = Quaternion.Euler(
                        85f + Random.Range(-5f, 5f), 0f, 0f);
                    rb.linearVelocity = Vector3.down * Random.Range(8f, 18f);
                    break;
            }
        }

        // =====================================================================
        //  Rewards
        // =====================================================================

        void CalculateRewards()
        {
            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, cfg.length));
            var context = new RewardRuntimeContext(
                transform.localPosition.y,
                BaseGroundClearance,
                fuel,
                envConfig.hoverTrackSettleRadius);

            RewardDecision decision = RocketRewardModel.Evaluate(envConfig.scenario, terms, context);
            AddReward(decision.shapingReward);

            if (decision.hasTerminalReward)
                SetReward(decision.terminalReward);

            if (decision.endEpisode)
                EndEpisode();
        }
        
        public new void AddReward(float reward)
        {
            _stepReward += reward;
            base.AddReward(reward);
        }

        public new void SetReward(float reward)
        {
            _stepReward = reward;
            base.SetReward(reward);
        }

        public new void EndEpisode()
        {
            if (_episodeEndedThisStep) return;

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

        void NotifyEpisodeCompleted()
        {
            // Curriculum progresses after a real completed/reset episode, not at
            // the first OnEpisodeBegin bootstrap.
            assembly.GetComponentInParent<TrainingAreaManager>()?.NotifyEpisodeEnd();
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        void RefreshConfig()
        {
            cfg = assembly ? assembly.GetPhysicsConfig() : RocketAssembly.Falcon9StaticFallback;
        }

        void EnsureActuatorBuffers(bool resetValues)
        {
            int engineCount = Mathf.Max(1, cfg.independentEngineCount);
            int finCount = Mathf.Max(0, cfg.finCount);
            int rcsJetCount = cfg.hasRCS ? Mathf.Max(0, cfg.rcsJetCount) : 0;

            if (throttle == null || throttle.Length != engineCount) throttle = new float[engineCount];
            if (targetThrottle == null || targetThrottle.Length != engineCount) targetThrottle = new float[engineCount];
            if (gimbal == null || gimbal.Length != engineCount) gimbal = new Vector2[engineCount];
            if (targetGimbal == null || targetGimbal.Length != engineCount) targetGimbal = new Vector2[engineCount];
            if (_manualThrottle == null || _manualThrottle.Length != engineCount) _manualThrottle = new float[engineCount];
            if (_manualGimbal == null || _manualGimbal.Length != engineCount) _manualGimbal = new Vector2[engineCount];

            if (finAngles == null || finAngles.Length != finCount) finAngles = new float[finCount];
            if (targetFinAngles == null || targetFinAngles.Length != finCount) targetFinAngles = new float[finCount];
            if (_manualFinAngles == null || _manualFinAngles.Length != finCount) _manualFinAngles = new float[finCount];
            if (rcsJets == null || rcsJets.Length != rcsJetCount) rcsJets = new float[rcsJetCount];
            if (_manualRcsJets == null || _manualRcsJets.Length != rcsJetCount) _manualRcsJets = new float[rcsJetCount];

            if (!resetValues) return;

            for (int i = 0; i < engineCount; i++)
            {
                throttle[i] = targetThrottle[i] = _manualThrottle[i] = 0f;
                gimbal[i] = targetGimbal[i] = _manualGimbal[i] = Vector2.zero;
            }

            for (int i = 0; i < finCount; i++)
                finAngles[i] = targetFinAngles[i] = _manualFinAngles[i] = 0f;

            for (int i = 0; i < rcsJetCount; i++)
                rcsJets[i] = _manualRcsJets[i] = 0f;
        }

        float RcsJetCommand(int index)
        {
            return rcsJets != null && index >= 0 && index < rcsJets.Length ? rcsJets[index] : 0f;
        }

        void ClearRcsCommands()
        {
            if (rcsJets != null)
                for (int i = 0; i < rcsJets.Length; i++) rcsJets[i] = 0f;

            assembly?.rcs?.ShowCommands(null);
        }

        RewardTerms MeasureRewardTerms(Vector3 goal)
        {
            Vector3 error = transform.localPosition - goal;
            Vector2 planarError = new Vector2(error.x, error.z);
            Vector2 planarVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float upDot = Vector3.Dot(transform.up, Vector3.up);
            float goalClosureRate = error.sqrMagnitude > 0.0001f
                ? -Vector3.Dot(rb.linearVelocity, error.normalized)
                : 0f;
            float horizontalClosureRate = planarError.sqrMagnitude > 0.0001f
                ? -Vector2.Dot(planarVelocity, planarError.normalized)
                : 0f;

            return new RewardTerms
            {
                distance3D = error.magnitude,
                planarDistance = planarError.magnitude,
                verticalError = error.y,
                speed = rb.linearVelocity.magnitude,
                planarSpeed = planarVelocity.magnitude,
                verticalSpeed = rb.linearVelocity.y,
                goalClosureRate = goalClosureRate,
                horizontalClosureRate = horizontalClosureRate,
                upDot = upDot,
                upright01 = Mathf.Clamp01((upDot + 1f) * 0.5f),
                angularRateDegS = localAngularVelocity.magnitude,
                controlEffort = Mean(throttle) + 0.05f * MeanAbs(gimbal) + 0.02f * MeanAbs(finAngles)
            };
        }

        Vector2 TargetPlanar()
        {
            Vector3 target = targetPad ? targetPad.localPosition : Vector3.zero;
            return new Vector2(target.x, target.z);
        }

        bool UpdateHoverTrackSuccess()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking)
            {
                _hoverTrackStableTime = 0f;
                return false;
            }

            if (IsHoverTrackHoverReady())
                _hoverTrackStableTime += Time.fixedDeltaTime;
            else
                _hoverTrackStableTime = 0f;

            return _hoverTrackStableTime >= Mathf.Max(0f, envConfig.hoverTrackSuccessHoldTime);
        }

        bool IsHoverTrackHoverReady()
        {
            Vector3 goal = ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, cfg.length);
            Vector3 error = transform.localPosition - goal;
            Vector2 horizontalError = new Vector2(error.x, error.z);
            Vector2 horizontalVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float settleRadius = Mathf.Max(envConfig.hoverTrackSettleRadius, 0.5f);
            float maxSpeed = Mathf.Max(envConfig.hoverTrackSuccessMaxSpeed, 0.1f);
            float maxTilt = Mathf.Max(envConfig.hoverTrackSuccessMaxTiltDeg, 0.1f);

            return horizontalError.magnitude <= settleRadius &&
                   Mathf.Abs(error.y) <= 3f &&
                   horizontalVelocity.magnitude <= maxSpeed &&
                   Mathf.Abs(rb.linearVelocity.y) <= maxSpeed &&
                   Vector3.Angle(transform.up, Vector3.up) <= maxTilt &&
                   localAngularVelocity.magnitude <= 25f;
        }

        float HoverTrackSettleQuality(
            float horizontalError,
            float verticalError,
            float horizontalSpeed,
            float verticalSpeed,
            float tiltDeg,
            float angularRateDegS)
        {
            float settleRadius = Mathf.Max(envConfig.hoverTrackSettleRadius, 0.5f);
            float maxSpeed = Mathf.Max(envConfig.hoverTrackSuccessMaxSpeed, 0.1f);
            float maxTilt = Mathf.Max(envConfig.hoverTrackSuccessMaxTiltDeg, 0.1f);

            float horizontalPosition = 1f - Mathf.Clamp01(horizontalError / settleRadius);
            float verticalPosition = 1f - Mathf.Clamp01(Mathf.Abs(verticalError) / 3f);
            float horizontalCalm = 1f - Mathf.Clamp01(horizontalSpeed / maxSpeed);
            float verticalCalm = 1f - Mathf.Clamp01(Mathf.Abs(verticalSpeed) / maxSpeed);
            float attitude = 1f - Mathf.Clamp01(tiltDeg / maxTilt);
            float rotationCalm = 1f - Mathf.Clamp01(angularRateDegS / 25f);

            return (horizontalPosition + verticalPosition + horizontalCalm + verticalCalm + attitude + rotationCalm) / 6f;
        }

        void RandomizeTarget()
        {
            float r = envConfig.targetMoveRadius;
            Vector3 selected = targetPad.localPosition;
            float minTravelDistance = envConfig.scenario == ScenarioType.HoverTracking
                ? Mathf.Min(r, Mathf.Max(envConfig.hoverTrackSettleRadius * 1.2f, envConfig.hoverTrackSettleRadius + 1f))
                : 0f;
            float bestDistance = -1f;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 candidate = new Vector3(Random.Range(-r, r), targetPad.localPosition.y, Random.Range(-r, r));
                Vector2 fromRocket = new Vector2(
                    candidate.x - transform.localPosition.x,
                    candidate.z - transform.localPosition.z);
                float distance = fromRocket.magnitude;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    selected = candidate;
                }

                if (distance >= minTravelDistance)
                    break;
            }

            targetPad.localPosition = selected;
            _hoverTrackSegmentElapsedTime = 0f;
            _hoverTrackSegmentStartDistance = new Vector2(
                targetPad.localPosition.x - transform.localPosition.x,
                targetPad.localPosition.z - transform.localPosition.z).magnitude;
        }

        void UpdateFlame()
        {
            if (!assembly || !assembly.thrusters) return;

            var thrusterCount = assembly.thrusters.transform.childCount;
            var activeIndex = 0;

            for (int i = 0; i < thrusterCount; i++)
            {
                Transform engine = assembly.thrusters.transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                var index = cfg.independentEngines ? activeIndex : 0;
                float currentThrottle = throttle != null && index < throttle.Length ? throttle[index] : 0f;
                Vector2 currentGimbal = gimbal != null && index < gimbal.Length ? gimbal[index] : Vector2.zero;

                engine.localRotation = Quaternion.Euler(currentGimbal.x, 0f, currentGimbal.y);

                Transform flame = engine.Find("Flame_Effect");
                if (flame)
                {
                    flame.gameObject.SetActive(currentThrottle > 0.01f);
                    float pulse  = 1f + Mathf.Sin(Time.time * 45f + activeIndex * 1.7f) * 0.08f;
                    float width  = Mathf.Lerp(minFlameWidth,  maxFlameWidth,  currentThrottle) * pulse;
                    float length = Mathf.Lerp(minFlameLength, maxFlameLength, currentThrottle) * pulse;
                    flame.localScale = new Vector3(width, length, width);
                }
                activeIndex++;
            }
        }
        
        // ── LogTelemetry() ───────────────────────────────────────────────────────

        void LogTelemetry()
        {
            if (!TelemetryLogger.Instance) return;
            if (envConfig.behaviorType != BehaviorType.Training) return;

            Vector3 effVel = rb.linearVelocity - wind;
            Vector3 targetPosition = GetAnalysisTargetPosition();
            Vector3 error = transform.localPosition - targetPosition;
            Vector2 horizontalError = new Vector2(error.x, error.z);
            Vector2 horizontalVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);
            Vector2 horizontalTargetDir = horizontalError.sqrMagnitude > 0.0001f ? -horizontalError.normalized : Vector2.zero;
            Vector2 horizontalVelocityDir = horizontalVelocity.sqrMagnitude > 0.0001f ? horizontalVelocity.normalized : Vector2.zero;
            Vector2 meanGimbal = MeanVector(gimbal);
            float targetBearing = BearingDeg(horizontalTargetDir);
            float velocityBearing = BearingDeg(horizontalVelocityDir);
            float gimbalBearing = BearingDeg(meanGimbal);
            float goalAlignment = horizontalTargetDir.sqrMagnitude > 0f && horizontalVelocityDir.sqrMagnitude > 0f
                ? Vector2.Dot(horizontalVelocityDir, horizontalTargetDir)
                : 0f;
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
            Vector3 euler = transform.localEulerAngles;
            float upDot = Vector3.Dot(transform.up, Vector3.up);
            float startFuel = Mathf.Max(cfg.startFuelMass, 1f);
            float goalClosureRate = error.sqrMagnitude > 0.0001f
                ? -Vector3.Dot(rb.linearVelocity, error.normalized)
                : 0f;
            bool isHoverTracking = envConfig.scenario == ScenarioType.HoverTracking;
            float hoverTrackSettleRadius = Mathf.Max(envConfig.hoverTrackSettleRadius, 0.5f);
            bool hoverTrackHoverPhase = isHoverTracking && horizontalError.magnitude <= hoverTrackSettleRadius;
            bool hoverTrackReady = isHoverTracking && IsHoverTrackHoverReady();
            float horizontalClosureRate = horizontalError.sqrMagnitude > 0.0001f
                ? -Vector2.Dot(horizontalVelocity, horizontalError.normalized)
                : 0f;
            float directionEfficiency01 = horizontalVelocity.magnitude > 0.1f && horizontalError.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01((Vector2.Dot(horizontalVelocity.normalized, horizontalTargetDir) + 1f) * 0.5f)
                : 0.5f;
            float segmentDistance = Mathf.Max(_hoverTrackSegmentStartDistance, hoverTrackSettleRadius);
            float travelProgress01 = isHoverTracking
                ? Mathf.Clamp01(1f - horizontalError.magnitude / Mathf.Max(segmentDistance, 0.001f))
                : 0f;
            float travelProgressRate = isHoverTracking
                ? horizontalClosureRate / Mathf.Max(segmentDistance, 0.001f)
                : 0f;
            float settleQuality01 = isHoverTracking
                ? HoverTrackSettleQuality(horizontalError.magnitude, error.y, horizontalVelocity.magnitude,
                    rb.linearVelocity.y, Vector3.Angle(transform.up, Vector3.up), localAngularVelocity.magnitude)
                : 0f;

            var row = new TelemetryRow
            {
                areaIndex = _areaIndex,
                episode   = _episode,
                step      = _step,

                obs_relPos       = (targetPad.localPosition - transform.localPosition) / 100f,
                obs_vel          = rb.linearVelocity / 50f,
                obs_angVel       = rb.angularVelocity / 10f,
                obs_up           = transform.up,
                obs_fuelFrac     = fuel / Mathf.Max(cfg.startFuelMass, 1f),
                obs_altNorm      = transform.localPosition.y / 250f,
                obs_aoaDeg       = aoaDeg / 90f,
                obs_dynPressNorm = Mathf.Clamp01(q / 5000f),
                obs_windLocal    = transform.InverseTransformDirection(wind) /
                                   Mathf.Max(envConfig.windSpeed, 1f),

                act_throttle = throttle,
                act_gimbal   = gimbal,
                act_fins     = finAngles,

                sen = _sensors,

                phys_speed       = effVel.magnitude,
                phys_aoaDeg      = aoaDeg,
                phys_dynPressure = q,
                phys_altitude    = transform.localPosition.y,
                phys_fuelKg      = fuel,

                goal_distance3D       = error.magnitude,
                goal_planarDistance   = horizontalError.magnitude,
                goal_verticalError    = error.y,
                track_phaseHover01    = hoverTrackHoverPhase ? 1f : 0f,
                track_hoverReady01    = hoverTrackReady ? 1f : 0f,
                track_stableTime      = isHoverTracking ? _hoverTrackStableTime : 0f,
                track_targetReached01 = _hoverTrackTargetReachedThisStep ? 1f : 0f,
                track_settleRadius    = isHoverTracking ? hoverTrackSettleRadius : 0f,
                track_curriculumProgress = isHoverTracking ? envConfig.hoverTrackCurriculumProgress : 0f,
                track_segmentStartDistance = isHoverTracking ? _hoverTrackSegmentStartDistance : 0f,
                track_segmentElapsedTime = isHoverTracking ? _hoverTrackSegmentElapsedTime : 0f,
                track_travelProgress01 = travelProgress01,
                track_travelProgressRate = travelProgressRate,
                track_directionEfficiency01 = isHoverTracking ? directionEfficiency01 : 0f,
                track_settleQuality01 = settleQuality01,
                state_altitude        = transform.localPosition.y,
                nav_targetBearingDeg       = targetBearing,
                nav_velocityBearingDeg     = velocityBearing,
                nav_velocityTargetErrorDeg = SignedBearingError(velocityBearing, targetBearing),
                nav_goalAlignment          = goalAlignment,
                nav_gimbalBearingDeg       = gimbalBearing,
                nav_gimbalTargetErrorDeg   = SignedBearingError(gimbalBearing, targetBearing),
                att_tiltDeg           = Vector3.Angle(transform.up, Vector3.up),
                att_uprightness       = upDot,
                att_rollDeg           = NormalizeAngle(euler.z),
                att_pitchDeg          = NormalizeAngle(euler.x),
                att_angularRateDegS   = localAngularVelocity.magnitude,
                att_tiltRateDegS      = new Vector2(localAngularVelocity.x, localAngularVelocity.z).magnitude,
                att_pitchRateDegS     = localAngularVelocity.x,
                att_yawRateDegS       = localAngularVelocity.y,
                att_rollRateDegS      = localAngularVelocity.z,
                vel_speed3D           = rb.linearVelocity.magnitude,
                vel_planarSpeed       = horizontalVelocity.magnitude,
                vel_verticalSpeed     = rb.linearVelocity.y,
                vel_goalClosureRate   = goalClosureRate,
                vel_horizontalClosureRate = horizontalClosureRate,
                ctrl_throttleMean     = Mean(throttle),
                ctrl_throttleMax      = Max(throttle),
                ctrl_gimbalMeanAbsDeg = MeanAbs(gimbal),
                ctrl_finMeanAbsDeg    = MeanAbs(finAngles),
                fuel_fraction         = fuel / startFuel,
                fuel_usedKg           = Mathf.Max(0f, startFuel - fuel),
                load_gForce           = _sensors.GMagnitude,
                load_angularAccelDegS2 = _sensors.AngularAccel.magnitude * Mathf.Rad2Deg,
                env_windSpeed         = wind.magnitude,
                env_windPlanarSpeed   = new Vector2(wind.x, wind.z).magnitude,
                env_windAlignment     = effVel.sqrMagnitude > 0.0001f && wind.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(effVel.normalized, wind.normalized)
                    : 0f,

                stepReward = _stepReward
            };

            TelemetryLogger.Instance.Log(row);
        }

        Vector3 GetAnalysisTargetPosition()
        {
            return ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, cfg.length);
        }

        static float Mean(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        static float Max(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float max = values[0];
            for (int i = 1; i < values.Length; i++) max = Mathf.Max(max, values[i]);
            return max;
        }

        static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        static float BearingDeg(Vector2 value)
        {
            if (value.sqrMagnitude < 0.0001f) return 0f;

            // XZ-plane bearing: 0 = north/+Z, 90 = east/+X, -90 = west/-X.
            return NormalizeAngle(Mathf.Atan2(value.x, value.y) * Mathf.Rad2Deg);
        }

        static float SignedBearingError(float bearingDeg, float targetBearingDeg)
        {
            return NormalizeAngle(targetBearingDeg - bearingDeg);
        }

        static float MeanAbs(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += Mathf.Abs(values[i]);
            return sum / values.Length;
        }

        static float MeanAbs(Vector2[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
                sum += (Mathf.Abs(values[i].x) + Mathf.Abs(values[i].y)) * 0.5f;

            return sum / values.Length;
        }

        static Vector2 MeanVector(Vector2[] values)
        {
            if (values == null || values.Length == 0) return Vector2.zero;

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

    }
}
