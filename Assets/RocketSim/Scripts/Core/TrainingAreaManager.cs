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

        int _totalEpisodes;
        HardwareTestController _hardwareTests;

        public IReadOnlyList<FalconAgent> Agents => _agents;
        public IReadOnlyList<RocketAssembly> Assemblies => _assemblies;
        public HardwareTestController HardwareTests => _hardwareTests;
        public string HardwareTestStatus => _hardwareTests != null ? _hardwareTests.Status : "No hardware test running.";

        void Awake()
        {
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

        void SpawnDummyAreas()
        {
            var go = Instantiate(dummyAreaPrefab, new Vector3(0f, 0f, 0f), Quaternion.identity, transform);
            go.name = "DummyArea";
            var dummyAssembly = go.GetComponentInChildren<RocketAssembly>();
            if (dummyAssembly)
            {
                _dummyAssemblies.Add(dummyAssembly);
                dummyAssembly.ApplyPartsConfig(partsConfig); // sync initial config
            }
        }

        public void SpawnAreas()
        {
            ClearSpawnedAreas();
            ConfigureTelemetrySlots();
            envConfig.ApplyHoverTrackCurriculum(instanceCount);

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
                    ModelAsset modelAsset = Resources.Load<ModelAsset>($"Models/{envConfig.runId}");
                    ConfigureAgent(agent, 0, modelAsset);

                    if (modelAsset)
                        Debug.Log($"[InferenceEngine] Loaded model: Models/{envConfig.runId}");
                    else
                        Debug.LogError($"[TrainingAreaManager] Model not found in Resources/Models/{envConfig.runId} " +
                                       $"— ensure the .onnx is at Assets/RocketSim/Resources/Models/{envConfig.runId}.onnx");
                }
                go.SetActive(true);
            }

            var cam = FindAnyObjectByType<RocketCameraController>();
            if (cam) cam.OnAgentsReady(); // call RefreshLabel + snap to first rocket
        }

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

        void EnsureHardwareTestController()
        {
            if (_hardwareTests == null)
                _hardwareTests = GetComponent<HardwareTestController>() ?? gameObject.AddComponent<HardwareTestController>();
            _hardwareTests.manager = this;
        }

        public void StopHardwareTest()
        {
            _hardwareTests?.Stop();
        }

        FalconAgent SpawnHardwareTestArea(bool clearExisting = true)
        {
            if (trainingAreaPrefab == null)
            {
                Debug.LogError("[TrainingAreaManager] trainingAreaPrefab not assigned.");
                return null;
            }

            if (clearExisting)
                ClearSpawnedAreas();
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
            foreach (var asm in _assemblies)
                if (asm) asm.ApplyPartsConfig(partsConfig);

            foreach (var asm in _dummyAssemblies)
                if (asm) asm.ApplyPartsConfig(partsConfig);
        }

        public void NotifyEpisodeEnd() // FalconAgent calls this instead of mutating envConfig
        {
            if (envConfig.scenario != ScenarioType.HoverTracking) return;

            _totalEpisodes++;
        }

        public void NotifyHoverTrackTargetReached()
        {
            if (envConfig.scenario != ScenarioType.HoverTracking) return;
            if (envConfig.behaviorType != BehaviorType.Training) return;

            envConfig.AdvanceHoverTrackCurriculum(instanceCount);
        }

        void ConfigureTelemetrySlots()
        {
            telemetryConfig.activeEngineCount = partsConfig.GetEngineCount();
            telemetryConfig.independentEngines = partsConfig.independentEngines;
            telemetryConfig.activeFinCount = partsConfig.GetFinCount();
        }

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
        }
    }

}
