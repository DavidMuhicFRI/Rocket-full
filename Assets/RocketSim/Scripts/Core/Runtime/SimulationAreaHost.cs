// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Runtime/SimulationAreaHost.cs
// Purpose: Hosts preview and active simulation areas and wires agents to one frozen runtime session.
// Main flow: panel edits draft -> coordinator freezes a snapshot -> this host
// creates isolated areas from a runtime copy -> Stop restores the draft preview.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UI;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace RocketSim
{
    // Spawns copies of TrainingArea.prefab and wires each agent to one runtime
    // configuration. The editable draft is never handed to active agents.
    //
    // [DefaultExecutionOrder(-10)] ensures Awake runs before ConfigBridge.Start().
    [DefaultExecutionOrder(-10)]
    public class SimulationAreaHost : MonoBehaviour
    {
        [Header("Prefab")] public GameObject trainingAreaPrefab;

        public GameObject dummyAreaPrefab;

        [Header("Grid")] [Range(1, 64)] public int instanceCount = 16;
        [Range(250f, 2000f)] public float spacing = 500f;

        [Header("Initial Simulation Session")]
        [SerializeField] SimulationSessionConfig initialSession = new();

        /// <summary>
        /// Canonical mutable setup used by the configuration UI.
        /// </summary>
        public SimulationSessionDraft SessionDraft { get; private set; }
        SimulationSessionConfig CurrentSession =>
            _runtimeSession ?? SessionDraft?.Config ?? initialSession;
        public RocketPartsConfig partsConfig => CurrentSession.vehicle;
        public SimEnvironmentConfig envConfig => CurrentSession.environment;
        public MLAgentsConfig mlConfig => CurrentSession.learning;
        public TelemetryConfig telemetryConfig => CurrentSession.telemetry;

        readonly List<RocketAssembly> _assemblies = new();
        readonly List<FalconAgent> _agents = new();

        int _totalEpisodes;
        bool _activeRunSpawned;
        SimulationSessionConfig _runtimeSession;
        HardwareTestController _hardwareTests;
        VehiclePreviewController _preview;

        public IReadOnlyList<FalconAgent> Agents => _agents;
        public IReadOnlyList<RocketAssembly> Assemblies => _assemblies;
        public HardwareTestController HardwareTests => _hardwareTests;
        public string HardwareTestStatus => _hardwareTests != null ? _hardwareTests.Status : "No hardware test running.";

        /// <summary>
        /// Normalizes shared hardware, validates prefab references, and creates
        /// the frozen preview area before ConfigBridge connects the panel.
        /// </summary>
        void Awake()
        {
            initialSession ??= new SimulationSessionConfig();
            initialSession.EnsureSections();
            partsConfig.NormalizeSelectedPreset();
            ApplyCurrentScenarioHardwareDefaults();
            SessionDraft = new SimulationSessionDraft(initialSession);
            SessionDraft.Changed += OnSessionDraftChanged;
            _preview = new VehiclePreviewController(transform, dummyAreaPrefab);

            if (trainingAreaPrefab == null)
            {
                Debug.LogError("[SimulationAreaHost] trainingAreaPrefab not assigned.");
            }

            if (dummyAreaPrefab == null)
            {
                Debug.LogError("[SimulationAreaHost] dummyAreaPrefab not assigned.");
            }
            else _preview.Spawn(partsConfig);
        }

        /// <summary>
        /// Applies only the preview work affected by a panel edit. Active runs
        /// are frozen snapshots, so draft edits never modify their agents.
        /// </summary>
        void OnSessionDraftChanged(SessionChangeKind change)
        {
            if (_runtimeSession != null) return;

            if (change == SessionChangeKind.All ||
                change == SessionChangeKind.Vehicle ||
                change == SessionChangeKind.Task)
                ApplyPartsConfigToAll();
        }

        /// <summary>
        /// Installs a private runtime copy before an active area is created.
        /// The coordinator is the only caller allowed to cross this boundary.
        /// </summary>
        public void PrepareRuntime(SimulationSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new System.ArgumentNullException(nameof(snapshot));
            if (_activeRunSpawned)
                throw new System.InvalidOperationException("Stop the active simulation before preparing another runtime.");

            _runtimeSession = snapshot.CreateRuntimeConfig();
        }

        /// <summary>
        /// Drops a prepared runtime when launch fails before areas are active.
        /// The existing preview then points back to the editable draft.
        /// </summary>
        public void CancelPreparedRuntime()
        {
            if (_activeRunSpawned) return;
            _runtimeSession = null;
            ApplyPartsConfigToAll();
        }

        /// <summary>
        /// Creates the active training or inference rocket areas from the current config.
        /// </summary>
        public bool SpawnAreas()
        {
            ClearSpawnedAreas();
            if (!trainingAreaPrefab)
            {
                Debug.LogError("[SimulationAreaHost] Cannot start: training area prefab is missing.");
                return false;
            }

            PrepareTelemetrySchema();
            envConfig.ApplyHoverTrackCurriculum();
            envConfig.ApplyActiveLandingCurriculum();

            // Inference should not trigger ML-Agents' editor trainer handshake
            // on port 5004. This must be set before the Agent enables and the
            // Academy lazily initializes.
            CommunicatorFactory.Enabled = envConfig.behaviorType == BehaviorType.Training;

            if (envConfig.behaviorType == BehaviorType.Training)
            {
                int side = Mathf.CeilToInt(Mathf.Sqrt(instanceCount));
                int idx = 0;

                for (int row = 0; row < side && idx < instanceCount; row++)
                for (int col = 0; col < side && idx < instanceCount; col++, idx++)
                {
                    Vector3 offset = new Vector3(col * spacing, 0f, row * spacing);
                    trainingAreaPrefab.SetActive(false);
                    var go = Instantiate(trainingAreaPrefab, offset, Quaternion.identity, transform);
                    trainingAreaPrefab.SetActive(true);
                    go.name = $"TrainingArea_{idx}";

                    var asm = go.GetComponentInChildren<RocketAssembly>();
                    var agent = go.GetComponentInChildren<FalconAgent>();

                    if (asm)
                    {
                        _assemblies.Add(asm);
                        asm.ApplyPartsConfig(partsConfig); // sync initial config

                        // Only objective errors returned by the validator stop
                        // startup. Conservative reachability findings remain
                        // warnings and therefore do not block an experiment.
                        if (idx == 0 && !SimulatorPreflightValidator.ValidateAndLog(asm.GetPhysicsConfig(), envConfig))
                        {
                            Debug.LogError("[SimulationAreaHost] Objective preflight errors prevented agent startup.");
                            ClearSpawnedAreas();
                            return false;
                        }
                    }

                    if (agent)
                    {
                        ConfigureAgent(agent, idx, null);
                    }

                    go.SetActive(true);
                }
            }
            else
            {
                // Inference mode - spawn single area
                Vector3 offset = Vector3.zero;
                trainingAreaPrefab.SetActive(false);
                var go = Instantiate(trainingAreaPrefab, offset, Quaternion.identity, transform);
                trainingAreaPrefab.SetActive(true);
                go.name = "InferenceArea";

                var asm = go.GetComponentInChildren<RocketAssembly>();
                var agent = go.GetComponentInChildren<FalconAgent>();

                if (asm)
                {
                    _assemblies.Add(asm);
                    asm.ApplyPartsConfig(partsConfig);
                    if (!SimulatorPreflightValidator.ValidateAndLog(asm.GetPhysicsConfig(), envConfig))
                    {
                        Debug.LogError("[SimulationAreaHost] Objective preflight errors prevented inference startup.");
                        ClearSpawnedAreas();
                        return false;
                    }
                }

                if (agent)
                {
                        if (!SimulationRunService.TryValidatePolicySchema(
                            envConfig.runId,
                            partsConfig,
                            envConfig.scenario,
                            out string compatibilityError))
                    {
                        Debug.LogError($"[SimulationAreaHost] {compatibilityError}");
                        ClearSpawnedAreas();
                        return false;
                    }

                    ModelAsset modelAsset = ModelRepository.LoadModel(envConfig.runId);
                    if (!modelAsset)
                    {
                        Debug.LogError(ModelRepository.MissingModelMessage(envConfig.runId));
                        ClearSpawnedAreas();
                        return false;
                    }
                    ConfigureAgent(agent, 0, modelAsset);

                    Debug.Log($"[InferenceEngine] Loaded model: {ModelRepository.ResourcePath(envConfig.runId)}");
                }
                go.SetActive(true);
            }

            if (_assemblies.Count == 0 || _agents.Count == 0)
            {
                Debug.LogError("[SimulationAreaHost] Cannot start: the area prefab must contain both RocketAssembly and FalconAgent.");
                ClearSpawnedAreas();
                return false;
            }

            var cam = FindAnyObjectByType<RocketCameraController>();
            if (cam) cam.OnAgentsReady(); // call RefreshLabel + snap to first rocket
            _activeRunSpawned = _agents.Count > 0;
            return _activeRunSpawned;
        }

        /// <summary>
        /// Spawns a single isolated hardware-test area and starts the selected
        /// scripted hardware probe.
        /// </summary>
        public void RunHardwareTest(RocketHardwareTestType testType)
        {
            EnsureHardwareTestController();
            _hardwareTests.Stop();
            _hardwareTests.Prepare(testType);
            var agent = SpawnHardwareTestArea();
            if (!agent)
            {
                _hardwareTests.FailToStart(testType, "Hardware test failed: no rocket agent was found in the spawned test area.");
                return;
            }

            _hardwareTests.Begin(testType, agent);
        }

        /// <summary>
        /// Lazily attaches the shared hardware-test controller to this manager.
        /// </summary>
        void EnsureHardwareTestController()
        {
            if (_hardwareTests == null)
                _hardwareTests = GetComponent<HardwareTestController>() ?? gameObject.AddComponent<HardwareTestController>();
            _hardwareTests.manager = this;
        }

        /// <summary>
        /// Stops any active hardware test through the shared hardware-test controller.
        /// </summary>
        public void StopHardwareTest()
        {
            _hardwareTests?.Stop();
        }

        /// <summary>
        /// Removes training/inference agents and restores the frozen preview
        /// rocket. The panel calls this when the user presses Stop.
        /// </summary>
        public void StopActiveRunAndShowPreview()
        {
            StopHardwareTest();
            PersistCurriculumState();
            TelemetryLogger.Instance?.DisableLogging();
            CommunicatorFactory.Enabled = false;
            ClearSpawnedAreas();
            _activeRunSpawned = false;
            _runtimeSession = null;
            if (dummyAreaPrefab != null)
                _preview.Spawn(partsConfig);
        }

        /// <summary>
        /// Creates one isolated agent for scripted hardware testing. It disables
        /// the Python communicator and telemetry, forces HeuristicOnly behavior,
        /// and enables manual hardware-test commands instead of policy actions.
        /// </summary>
        FalconAgent SpawnHardwareTestArea(bool clearExisting = true)
        {
            if (trainingAreaPrefab == null)
            {
                Debug.LogError("[SimulationAreaHost] trainingAreaPrefab not assigned.");
                return null;
            }

            if (clearExisting)
                ClearSpawnedAreas();
            PrepareTelemetrySchema();
            CommunicatorFactory.Enabled = false;
            TelemetryLogger.Instance?.DisableLogging();

            trainingAreaPrefab.SetActive(false);
            var go = Instantiate(trainingAreaPrefab, Vector3.zero, Quaternion.identity, transform);
            trainingAreaPrefab.SetActive(true);
            go.name = "HardwareTestArea";

            var asm = go.GetComponentInChildren<RocketAssembly>(true);
            var agent = go.GetComponentInChildren<FalconAgent>(true);

            if (asm)
            {
                _assemblies.Add(asm);
                asm.ApplyPartsConfig(partsConfig);
            }

            if (agent)
            {
                ConfigureAgent(agent, 0, null, BehaviorType.Training);
                var behavior = agent.GetComponent<BehaviorParameters>();
                if (behavior)
                {
                    behavior.BehaviorType = Unity.MLAgents.Policies.BehaviorType.HeuristicOnly;
                    behavior.Model = null;
                }
                agent.SetHardwareTestMode(true);
            }

            go.SetActive(true);

            var cam = FindAnyObjectByType<RocketCameraController>();
            if (cam) cam.OnAgentsReady();

            return agent;
        }

        // Called by RightConfigPanel whenever a part of the slider or toggle changes.
        // Pushes updated dimensions / enabled-state to every training area.
        public void ApplyPartsConfigToAll()
        {
            PrepareTelemetrySchema();

            foreach (var asm in _assemblies)
                if (asm) asm.ApplyPartsConfig(partsConfig);

            foreach (var agent in _agents)
                RefreshAgentAfterPartsChange(agent);

            _preview.Apply(partsConfig);
        }

        /// <summary>
        /// Applies scenario fuel and burn-group defaults to the shared hardware config.
        /// </summary>
        public void ApplyCurrentScenarioHardwareDefaults()
        {
            partsConfig?.ApplyScenarioHardwareDefaults(envConfig?.scenario ?? ScenarioType.ChopstickLanding);
        }

        /// <summary>
        /// Handles a notification that episode end happened and records the
        /// result for any active standardized curriculum.
        /// </summary>
        public void NotifyEpisodeEnd(bool successfulEpisode, bool includeInCurriculumEstimate = true)
        {
            _totalEpisodes++;

            if (envConfig.behaviorType != BehaviorType.Training) return;

            if (envConfig.scenario == ScenarioType.HoverTracking)
            {
                envConfig.RecordHoverTrackCurriculumEpisode(successfulEpisode, instanceCount);
                PersistCurriculumStateIfBatchComplete();
                return;
            }

            if (envConfig.scenario.IsLanding())
            {
                if (includeInCurriculumEstimate)
                    envConfig.RecordLandingCurriculumEpisode(successfulEpisode, instanceCount);
                PersistCurriculumStateIfBatchComplete();
            }
        }

        /// <summary>
        /// Saves curriculum progress after roughly one parallel-area batch so
        /// resume state stays current without writing once per physics episode.
        /// </summary>
        void PersistCurriculumStateIfBatchComplete()
        {
            if (_totalEpisodes % Mathf.Max(1, instanceCount) == 0)
                PersistCurriculumState();
        }

        /// <summary>Saves the shared training environment when a run is active.</summary>
        void PersistCurriculumState()
        {
            if (!_activeRunSpawned || envConfig == null || envConfig.behaviorType != BehaviorType.Training)
                return;

            try
            {
                SimulationRunService.SaveRuntimeState(envConfig.runId, envConfig);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SimulationAreaHost] Could not persist curriculum state: {ex.Message}");
            }
        }

        void OnApplicationQuit() => PersistCurriculumState();

        void OnDestroy()
        {
            if (SessionDraft != null)
                SessionDraft.Changed -= OnSessionDraftChanged;
            PersistCurriculumState();
        }

        /// <summary>
        /// Handles a notification that hover track target reached happened.
        /// </summary>
        public void NotifyHoverTrackTargetReached()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking) return;
            if (envConfig.behaviorType != BehaviorType.Training) return;

            envConfig.hoverTrackCurriculumSuccesses++;
        }

        /// <summary>
        /// Applies the selected scenario's hardware defaults and updates the
        /// telemetry schema before a logger creates its CSV columns. Calling
        /// this again while spawning is harmless and keeps direct manager users
        /// on the same path as launches started through the UI.
        /// </summary>
        public void PrepareTelemetrySchema()
        {
            ApplyCurrentScenarioHardwareDefaults();

            telemetryConfig.activeEngineCount = partsConfig.GetActiveEngineCount();
            telemetryConfig.independentEngines = partsConfig.independentEngines;
            telemetryConfig.activeFinCount = partsConfig.GetFinCount();
            telemetryConfig.scenario = envConfig.scenario;
        }

        /// <summary>
        /// Wires shared config, area index, behavior parameters, and optional
        /// inference model onto a spawned FalconAgent.
        /// </summary>
        void ConfigureAgent(FalconAgent agent, int areaIndex, ModelAsset modelAsset, BehaviorType? behaviorOverride = null)
        {
            if (!agent) return;

            _agents.Add(agent);
            agent.envConfig = envConfig;
            agent.SetAreaIndex(areaIndex);

            RocketAgentSchema.ConfigureBehavior(
                agent.GetComponent<BehaviorParameters>(),
                partsConfig,
                envConfig.scenario,
                behaviorOverride ?? envConfig.behaviorType,
                modelAsset);
            RocketAgentSchema.ConfigureDecisionRequester(agent.GetComponent<DecisionRequester>());
        }

        /// <summary>
        /// Reconfigures an existing agent after hardware changes so ML-Agents
        /// schema and runtime actuator buffers stay in sync.
        /// </summary>
        void RefreshAgentAfterPartsChange(FalconAgent agent)
        {
            if (!agent) return;

            if (!agent.HardwareTestMode)
            {
                var behavior = agent.GetComponent<BehaviorParameters>();
                RocketAgentSchema.ConfigureBehavior(
                    behavior,
                    partsConfig,
                    envConfig.scenario,
                    envConfig.behaviorType,
                    behavior ? behavior.Model : null);
                RocketAgentSchema.ConfigureDecisionRequester(agent.GetComponent<DecisionRequester>());
            }

            agent.RefreshHardwareConfig();
        }

        /// <summary>
        /// Destroys spawned runtime areas and clears cached agent, assembly, and dummy-preview lists.
        /// </summary>
        void ClearSpawnedAreas()
        {
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            _assemblies.Clear();
            _agents.Clear();
            _preview?.ClearReferences();
        }
    }

}
