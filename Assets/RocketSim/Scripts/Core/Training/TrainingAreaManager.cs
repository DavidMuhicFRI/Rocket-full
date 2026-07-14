// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingAreaManager.cs
// Purpose: Owns shared configs, spawns training/inference areas, applies part changes, and starts hardware tests.
// Main flow: the panel edits shared configs -> this manager creates isolated
// areas -> every spawned agent receives the same configs -> Stop restores the preview.
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
    // Spawns N copies of TrainingArea.prefab in a square grid.
    // Holds the THREE shared config objects that ConfigBridge later passes to
    // RightConfigPanel.  All agents share the same SimEnvironmentConfig reference
    // — the panel writes once and every agent reads it automatically.
    //
    // [DefaultExecutionOrder(-10)] ensures Awake runs before ConfigBridge.Start().
    [DefaultExecutionOrder(-10)]
    public class TrainingAreaManager : MonoBehaviour
    {
        [Header("Prefab")] public GameObject trainingAreaPrefab;

        public GameObject dummyAreaPrefab;

        [Header("Grid")] [Range(1, 64)] public int instanceCount = 16;
        [Range(250f, 2000f)] public float spacing = 500f;

        [Header("Shared Configs — written by panel, read by all agents")]
        public RocketPartsConfig partsConfig = new();

        public TelemetryConfig telemetryConfig = new();
        public SimEnvironmentConfig envConfig = new();
        public MLAgentsConfig mlConfig = new();

        readonly List<RocketAssembly> _assemblies = new();
        readonly List<FalconAgent> _agents = new();

        readonly List<RocketAssembly> _dummyAssemblies = new();
        readonly Dictionary<Rigidbody, PreviewRigidbodyPose> _dummyRigidbodyPoses = new();

        int _totalEpisodes;
        bool _activeRunSpawned;
        HardwareTestController _hardwareTests;

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
            partsConfig.NormalizeSelectedPreset();
            ApplyCurrentScenarioHardwareDefaults();

            if (trainingAreaPrefab == null)
            {
                Debug.LogError("[TrainingAreaManager] trainingAreaPrefab not assigned.");
            }

            if (dummyAreaPrefab == null)
            {
                Debug.LogError("[TrainingAreaManager] dummyAreaPrefab not assigned.");
            }
            else SpawnDummyAreas();
        }

        /// <summary>
        /// Spawns the non-training preview rocket and freezes its physics so UI
        /// hardware edits can be inspected without running an agent.
        /// </summary>
        void SpawnDummyAreas()
        {
            ApplyCurrentScenarioHardwareDefaults();
            var go = Instantiate(dummyAreaPrefab, new Vector3(0f, 0f, 0f), Quaternion.identity, transform);
            go.name = "DummyArea";
            var dummyAssembly = go.GetComponentInChildren<RocketAssembly>();
            if (dummyAssembly)
            {
                _dummyAssemblies.Add(dummyAssembly);
                dummyAssembly.ApplyPartsConfig(partsConfig); // sync initial config
            }

            LockDummyPhysics(go, capturePose: true);
        }

        /// <summary>
        /// Creates the active training or inference rocket areas from the current config.
        /// </summary>
        public void SpawnAreas()
        {
            ClearSpawnedAreas();
            ApplyCurrentScenarioHardwareDefaults();
            ConfigureTelemetrySlots();
            envConfig.ApplyHoverTrackCurriculum();
            envConfig.ApplyLandingCurriculum();

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
                }

                if (agent)
                {
                    ModelAsset modelAsset = ModelRepository.LoadModel(envConfig.runId);
                    ConfigureAgent(agent, 0, modelAsset);

                    if (modelAsset)
                        Debug.Log($"[InferenceEngine] Loaded model: {ModelRepository.ResourcePath(envConfig.runId)}");
                    else
                        Debug.LogError(ModelRepository.MissingModelMessage(envConfig.runId));
                }
                go.SetActive(true);
            }

            if (_assemblies.Count > 0 && _assemblies[0])
                SimulatorPreflightValidator.ValidateAndLog(_assemblies[0].GetPhysicsConfig(), envConfig);

            var cam = FindAnyObjectByType<RocketCameraController>();
            if (cam) cam.OnAgentsReady(); // call RefreshLabel + snap to first rocket
            _activeRunSpawned = _agents.Count > 0;
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
            if (dummyAreaPrefab != null)
                SpawnDummyAreas();
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
                Debug.LogError("[TrainingAreaManager] trainingAreaPrefab not assigned.");
                return null;
            }

            if (clearExisting)
                ClearSpawnedAreas();
            ApplyCurrentScenarioHardwareDefaults();
            ConfigureTelemetrySlots();
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
            ApplyCurrentScenarioHardwareDefaults();
            ConfigureTelemetrySlots();

            foreach (var asm in _assemblies)
                if (asm) asm.ApplyPartsConfig(partsConfig);

            foreach (var agent in _agents)
                RefreshAgentAfterPartsChange(agent);

            foreach (var asm in _dummyAssemblies)
            {
                if (!asm) continue;

                asm.ApplyPartsConfig(partsConfig);
                LockDummyPhysics(asm.gameObject, capturePose: false);
            }
        }

        /// <summary>
        /// Restores and freezes all dummy-area rigidbodies so preview rockets
        /// remain still after hardware changes.
        /// </summary>
        void LockDummyPhysics(GameObject root, bool capturePose)
        {
            if (!root) return;

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (capturePose || !_dummyRigidbodyPoses.ContainsKey(rb))
                {
                    _dummyRigidbodyPoses[rb] = new PreviewRigidbodyPose
                    {
                        localPosition = rb.transform.localPosition,
                        localRotation = rb.transform.localRotation
                    };
                }

                var pose = _dummyRigidbodyPoses[rb];
                rb.transform.SetLocalPositionAndRotation(pose.localPosition, pose.localRotation);
                // Unity rejects velocity writes while a body is kinematic.
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                rb.Sleep();
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Applies scenario fuel and burn-group defaults to the shared hardware config.
        /// </summary>
        public void ApplyCurrentScenarioHardwareDefaults()
        {
            partsConfig?.ApplyScenarioHardwareDefaults(envConfig?.scenario ?? ScenarioType.Landing);
        }

        /// <summary>
        /// Handles a notification that episode end happened and records the
        /// result for any active standardized curriculum.
        /// </summary>
        public void NotifyEpisodeEnd(bool successfulEpisode)
        {
            _totalEpisodes++;

            if (envConfig.behaviorType != BehaviorType.Training) return;

            if (envConfig.scenario == ScenarioType.HoverTracking)
            {
                envConfig.RecordHoverTrackCurriculumEpisode(successfulEpisode, instanceCount);
                PersistCurriculumStateIfBatchComplete();
                return;
            }

            if (envConfig.scenario == ScenarioType.Landing)
            {
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
                TrainingRunRepository.SaveEnvironmentState(envConfig.runId, envConfig);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TrainingAreaManager] Could not persist curriculum state: {ex.Message}");
            }
        }

        void OnApplicationQuit() => PersistCurriculumState();

        void OnDestroy() => PersistCurriculumState();

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
        /// Updates telemetry schema counts from the current hardware config so
        /// CSV rows match active engines, fins, and engine grouping.
        /// </summary>
        void ConfigureTelemetrySlots()
        {
            telemetryConfig.activeEngineCount = partsConfig.GetActiveEngineCount();
            telemetryConfig.independentEngines = partsConfig.independentEngines;
            telemetryConfig.activeFinCount = partsConfig.GetFinCount();
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
                behaviorOverride ?? envConfig.behaviorType,
                modelAsset);
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
                    envConfig.behaviorType,
                    behavior ? behavior.Model : null);
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
            _dummyAssemblies.Clear();
            _dummyRigidbodyPoses.Clear();
        }

        struct PreviewRigidbodyPose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
    }

}
