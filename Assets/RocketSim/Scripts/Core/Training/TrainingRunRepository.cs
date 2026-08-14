// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingRunRepository.cs
// Purpose: Owns the on-disk run layout: scans saved runs, writes configuration
// snapshots, reloads them, and reports whether a run is complete enough to use.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RocketSim
{
    public sealed class RunConfigInfo
    {
        public ScenarioType scenario;
    }

    public sealed class TrainingRunConfigs
    {
        public SimEnvironmentConfig envConfig;
        public RocketPartsConfig partsConfig;
        public MLAgentsConfig mlConfig;
    }

    /// <summary>
    /// Best-effort snapshot of the external Python trainer environment. A probe
    /// failure is recorded instead of silently pretending that package versions
    /// are known; it does not prevent an otherwise valid trainer launch.
    /// </summary>
    [Serializable]
    public sealed class TrainingEnvironmentProvenance
    {
        public bool probeSucceeded;
        public string probeError = "not probed";
        public string condaEnvironmentName = "unknown";
        public string pythonExecutable = "unknown";
        public string pythonVersion = "unknown";
        public string mlAgentsPythonVersion = "unknown";
        public string pytorchVersion = "unknown";
        public string cudaRuntimeVersion = "unknown";
        public bool cudaAvailable;
        public string gpuName = "unknown";
    }

    /// <summary>
    /// Compact provenance record for grouping ablation runs without parsing
    /// Unity JSON or ML-Agents YAML during analysis.
    /// </summary>
    [Serializable]
    public sealed class TrainingRunManifest
    {
        /// <summary>
        /// Clean-break run schema for the configurable training-objective and
        /// immutable resume-contract framework. Runs written by another schema
        /// are deliberately excluded from resume, transfer, and inference lists
        /// instead of being guessed at.
        /// </summary>
        public const int CurrentSchemaVersion = 5;

        public int schemaVersion = CurrentSchemaVersion;
        public string runId;
        public string createdUtc;
        public string lastLaunchedUtc;
        public bool resumed;
        public string initializedFromRunId;
        public string scenario;
        public string hardwarePreset;
        public string engineLayout;
        public string octawebBurnGroup;
        public int installedEngineCount;
        public int activeEngineCount;
        public int engineControlChannels;
        public bool independentEngineControl;
        public int finCount;
        public bool finsEnabled;
        public bool rcsEnabled;
        public float startFuelFraction;
        public float startFuelMassKg;
        public float maximumThrustPerEngineN;
        public float minimumThrottle01;
        public float engineStartupDelaySeconds;
        public float engineMinimumRunTimeSeconds;
        public float engineRestartCooldownSeconds;
        public bool physicalLandingGearEnabled;
        public float landingGearFootprintDiameterM;
        public int landingRequiredStableFeet;
        public bool commonSingleEngineLandingEnvelope;
        public int vectorObservationSize;
        public int continuousActionSize;
        public string behaviorName;
        public string trainerType;
        public int trainerSeed;
        public int environmentSeed;
        public float fixedDeltaTimeSeconds;
        public int decisionPeriod;
        public int trainingStepLogInterval;
        public float initialVehicleMassKg;
        public float minimumCommandableNonzeroThrustToWeight;
        public float allActiveEnginesMinimumThrottleThrustToWeight;
        public float allActiveEnginesMaximumThrustToWeight;
        public string trainingObjectiveSha256;
        public string partsConfigSha256;
        public string mlAgentsConfigSha256;
        public string unityVersion;
        public string mlAgentsAssemblyVersion;
        public string operatingSystem;
        public string processorType;
        public int processorCount;
        public int systemMemorySizeMb;
        public string graphicsDeviceName;
        public string configuredTorchDevice;
        public bool pythonEnvironmentProbeSucceeded;
        public string pythonEnvironmentProbeError;
        public string condaEnvironmentName;
        public string pythonExecutable;
        public string pythonVersion;
        public string mlAgentsPythonVersion;
        public string pytorchVersion;
        public string cudaRuntimeVersion;
        public bool cudaAvailable;
        public string trainerGpuName;
        public string sourceControlRevision;
        public bool sourceControlDirty;
    }

    internal static class TrainingRunRepository
    {
        const string EnvConfigFileName = "EnvConfig.json";
        const string InitialEnvConfigFileName = "EnvConfig.initial.json";
        const string PartsConfigFileName = "PartsConfig.json";
        const string MlConfigFileName = "TrainingConfig.yaml";
        const string TelemetryConfigFileName = "TelemetryConfig.json";
        const string RunManifestFileName = "RunManifest.json";

        /// <summary>
        /// Returns the project-level ML-Agents results folder. The path is
        /// stable even before the first run creates it, so Unity snapshots and
        /// ML-Agents trainer output cannot split across two different roots.
        /// </summary>
        public static string GetResultsRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
        }

        /// <summary>
        /// Returns the folder path that stores configs and trainer output for one run id.
        /// </summary>
        public static string RunRoot(string runId) => Path.Combine(GetResultsRoot(), runId);

        /// <summary>
        /// Prevents a new trainer from reusing any existing results directory.
        /// The launcher uses --force only for genuinely new ids; continuing an
        /// existing id requires an explicit resume with complete saved configs.
        /// </summary>
        public static bool TryValidateRunDestination(
            string runId,
            bool resume,
            SimEnvironmentConfig requestedEnvironment,
            RocketPartsConfig requestedParts,
            MLAgentsConfig requestedMl,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(runId))
            {
                error = "Run ID cannot be empty.";
                return false;
            }

            bool directoryExists = Directory.Exists(RunRoot(runId));
            if (resume)
            {
                if (directoryExists && HasCompleteRunConfig(runId))
                {
                    if (!TryValidateResumeContract(
                            RunRoot(runId),
                            requestedEnvironment,
                            requestedParts,
                            requestedMl,
                            out error))
                        return false;
                    return true;
                }

                error = $"Run '{runId}' cannot be resumed because its saved configuration is incomplete.";
                return false;
            }

            if (directoryExists)
            {
                error =
                    $"Run '{runId}' already has a results directory. Enable Resume for that run or choose a unique Run ID; " +
                    "new training will not overwrite existing evidence.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prevents a resumed trainer from silently changing its active task,
        /// environment, hardware, trainer settings, or objective under the same
        /// run identity. Any experiment change must use a new run ID.
        /// </summary>
        static bool TryValidateResumeContract(
            string runRoot,
            SimEnvironmentConfig requestedEnvironment,
            RocketPartsConfig requestedParts,
            MLAgentsConfig requestedMl,
            out string error)
        {
            error = null;
            if (requestedEnvironment == null || requestedParts == null || requestedMl == null)
            {
                error = "The requested environment, hardware, or trainer configuration is missing.";
                return false;
            }
            if (!TryLoadRunManifest(runRoot, out TrainingRunManifest manifest) ||
                manifest.schemaVersion != TrainingRunManifest.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(manifest.trainingObjectiveSha256) ||
                string.IsNullOrWhiteSpace(manifest.partsConfigSha256) ||
                string.IsNullOrWhiteSpace(manifest.mlAgentsConfigSha256))
            {
                error =
                    "The run has no complete current-schema training-contract fingerprint and cannot be resumed.";
                return false;
            }

            string requestedScenario = requestedEnvironment.scenario.ToString();
            if (!string.Equals(
                    requestedScenario,
                    manifest.scenario,
                    StringComparison.Ordinal))
            {
                error =
                    $"The requested scenario '{requestedScenario}' differs from the saved run scenario '{manifest.scenario}'. Choose a new Run ID when changing tasks.";
                return false;
            }

            string requestedJson = JsonUtility.ToJson(
                requestedEnvironment.EnsureTrainingObjective());
            string requestedHash = Sha256Hex(requestedJson);
            if (!string.Equals(
                    requestedHash,
                    manifest.trainingObjectiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "The training objective differs from the saved run. Choose a new Run ID for reward, shaping, or termination changes.";
                return false;
            }

            string savedEnvironmentPath = Path.Combine(runRoot, EnvConfigFileName);
            SimEnvironmentConfig savedEnvironment;
            try
            {
                savedEnvironment = JsonUtility.FromJson<SimEnvironmentConfig>(
                    File.ReadAllText(savedEnvironmentPath));
            }
            catch
            {
                savedEnvironment = null;
            }
            if (savedEnvironment == null)
            {
                error =
                    "The saved environment state is missing or malformed and cannot be resumed.";
                return false;
            }

            if (!string.Equals(
                    ResumeEnvironmentSha256(requestedEnvironment),
                    ResumeEnvironmentSha256(savedEnvironment),
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "The environment configuration differs from the current saved run state. Choose a new Run ID for spawn, curriculum, weather, fault, or other environment changes.";
                return false;
            }

            string requestedPartsHash = Sha256Hex(JsonUtility.ToJson(requestedParts));
            if (!string.Equals(
                    requestedPartsHash,
                    manifest.partsConfigSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "The hardware configuration differs from the saved run. Choose a new Run ID for vehicle or actuator changes.";
                return false;
            }

            string requestedMlHash = Sha256Hex(requestedMl.ToYAML());
            if (!string.Equals(
                    requestedMlHash,
                    manifest.mlAgentsConfigSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "The ML-Agents trainer configuration differs from the saved run. Choose a new Run ID for optimizer, network, reward-signal, seed, or training-schedule changes.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fingerprints training-relevant environment state while ignoring the
        /// evaluator-only fields deliberately preserved by the run-loading UI.
        /// Live curriculum counters remain included, so the current EnvConfig
        /// snapshot is the authoritative resume point.
        /// </summary>
        static string ResumeEnvironmentSha256(SimEnvironmentConfig environment)
        {
            SimEnvironmentConfig normalized = JsonUtility.FromJson<SimEnvironmentConfig>(
                JsonUtility.ToJson(environment));
            normalized.behaviorType = BehaviorType.Training;
            normalized.inferencePurpose = InferencePurpose.StandardEvaluation;
            normalized.evaluation = new EvaluationConfig();
            return Sha256Hex(JsonUtility.ToJson(normalized));
        }

        /// <summary>
        /// Writes ML-Agents YAML plus environment and parts JSON snapshots into
        /// the run folder so training and later inference use the same settings.
        /// </summary>
        public static string SaveTrainingConfigs(
            string runId,
            MLAgentsConfig mlConfig,
            SimEnvironmentConfig envConfig,
            RocketPartsConfig partsConfig,
            string mlConfigPath,
            string envConfigPath,
            string partsConfigPath,
            TelemetryConfig telemetryConfig,
            bool resumed,
            string initializedFromRunId,
            TrainingEnvironmentProvenance trainingEnvironment,
            string configuredTorchDevice)
        {
            if (mlConfig == null) throw new ArgumentNullException(nameof(mlConfig));
            if (envConfig == null) throw new ArgumentNullException(nameof(envConfig));
            if (partsConfig == null) throw new ArgumentNullException(nameof(partsConfig));
            envConfig.EnsureTrainingObjective();

            string runRoot = RunRoot(runId);
            Directory.CreateDirectory(runRoot);

            File.WriteAllText(Path.Combine(runRoot, mlConfigPath), mlConfig.ToYAML());
            string serializedEnvironment = JsonUtility.ToJson(envConfig);
            File.WriteAllText(Path.Combine(runRoot, envConfigPath), serializedEnvironment);
            string initialEnvironmentPath = Path.Combine(runRoot, InitialEnvConfigFileName);
            if (!File.Exists(initialEnvironmentPath))
                File.WriteAllText(initialEnvironmentPath, serializedEnvironment);
            File.WriteAllText(Path.Combine(runRoot, partsConfigPath), JsonUtility.ToJson(partsConfig));
            File.WriteAllText(
                Path.Combine(runRoot, TelemetryConfigFileName),
                JsonUtility.ToJson(telemetryConfig ?? new TelemetryConfig(), true));
            SaveRunManifest(
                runRoot,
                runId,
                mlConfig,
                envConfig,
                partsConfig,
                telemetryConfig,
                resumed,
                initializedFromRunId,
                trainingEnvironment,
                configuredTorchDevice);

            return runRoot;
        }

        /// <summary>
        /// Verifies that a prior run has a checkpoint and the exact policy
        /// schema/network settings needed for a scientifically clean transfer.
        /// ML-Agents can partially load incompatible networks, but that would
        /// make a hover-pretraining comparison ambiguous.
        /// </summary>
        public static bool TryValidateInitializationSource(
            string sourceRunId,
            RocketPartsConfig targetParts,
            MLAgentsConfig targetMl,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sourceRunId))
                return true;
            if (!HasCompleteRunConfig(sourceRunId))
            {
                error = $"Initialization run '{sourceRunId}' does not have complete saved configs.";
                return false;
            }

            string sourceRoot = RunRoot(sourceRunId);
            if (Directory.GetFiles(sourceRoot, "*.pt", SearchOption.AllDirectories).Length == 0)
            {
                error = $"Initialization run '{sourceRunId}' has no ML-Agents checkpoint (.pt).";
                return false;
            }

            TrainingRunConfigs source = LoadRunConfigs(sourceRunId);
            if (!TryLoadRunManifest(sourceRoot, out TrainingRunManifest sourceManifest) ||
                sourceManifest.schemaVersion != TrainingRunManifest.CurrentSchemaVersion ||
                sourceManifest.vectorObservationSize <= 0 ||
                sourceManifest.continuousActionSize <= 0)
            {
                error =
                    $"Initialization run '{sourceRunId}' does not use training-objective schema " +
                    $"{TrainingRunManifest.CurrentSchemaVersion}. Train a new source run with the current simulator.";
                return false;
            }

            int sourceObservations = sourceManifest.vectorObservationSize;
            int targetObservations = RocketAgentSchema.ObservationSize(targetParts);
            int sourceActions = sourceManifest.continuousActionSize;
            int targetActions = RocketAgentSchema.ContinuousActionSize(targetParts);
            if (sourceObservations != targetObservations || sourceActions != targetActions)
            {
                error =
                    $"Initialization run '{sourceRunId}' has policy schema " +
                    $"{sourceObservations} observations/{sourceActions} actions, but the target has " +
                    $"{targetObservations}/{targetActions}. Use the same hardware for hover and landing.";
                return false;
            }

            string targetPreset = targetParts.hardwarePreset.ToString();
            if (!string.Equals(sourceManifest.hardwarePreset, targetPreset, StringComparison.Ordinal))
            {
                error =
                    $"Initialization run '{sourceRunId}' uses hardware preset " +
                    $"'{sourceManifest.hardwarePreset}', but the target uses '{targetPreset}'. " +
                    "Use a hover checkpoint trained with the same named preset.";
                return false;
            }

            MLAgentsConfig sourceMl = source.mlConfig;
            bool sameNetwork = sourceMl.trainerType == targetMl.trainerType &&
                               sourceMl.behaviorName == targetMl.behaviorName &&
                               sourceMl.normalize == targetMl.normalize &&
                               sourceMl.hiddenUnits == targetMl.hiddenUnits &&
                               sourceMl.numLayers == targetMl.numLayers;
            if (!sameNetwork)
            {
                error =
                    $"Initialization run '{sourceRunId}' uses different trainer/network settings. " +
                    "Match trainer type, behavior name, normalization, hidden units, and layer count.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads the compact provenance record used for checkpoint compatibility.
        /// Missing or malformed manifests are rejected instead of guessing the
        /// policy shape from today's code and accidentally accepting an old model.
        /// </summary>
        static bool TryLoadRunManifest(string runRoot, out TrainingRunManifest manifest)
        {
            manifest = null;
            string path = Path.Combine(runRoot, RunManifestFileName);
            if (!File.Exists(path)) return false;

            try
            {
                manifest = JsonUtility.FromJson<TrainingRunManifest>(File.ReadAllText(path));
                return manifest != null;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        static void SaveRunManifest(
            string runRoot,
            string runId,
            MLAgentsConfig mlConfig,
            SimEnvironmentConfig envConfig,
            RocketPartsConfig partsConfig,
            TelemetryConfig telemetryConfig,
            bool resumed,
            string initializedFromRunId,
            TrainingEnvironmentProvenance trainingEnvironment,
            string configuredTorchDevice)
        {
            string manifestPath = Path.Combine(runRoot, RunManifestFileName);
            string createdUtc = DateTime.UtcNow.ToString("o");
            if (File.Exists(manifestPath))
            {
                try
                {
                    TrainingRunManifest previous = JsonUtility.FromJson<TrainingRunManifest>(File.ReadAllText(manifestPath));
                    if (previous != null && !string.IsNullOrWhiteSpace(previous.createdUtc))
                        createdUtc = previous.createdUtc;
                }
                catch
                {
                    // A malformed manifest cannot provide a reusable creation timestamp.
                }
            }

            TryReadSourceControlState(out string revision, out bool dirty);
            trainingEnvironment ??= new TrainingEnvironmentProvenance();
            float initialDryMassKg = RocketAssembly.EstimateAdjustedDryMass(partsConfig);
            float initialVehicleMassKg = initialDryMassKg + partsConfig.startFuelMass +
                                         (partsConfig.rcsEnabled ? partsConfig.rcsPropellantMass : 0f);
            ThrustAuthoritySnapshot thrustAuthority = ThrustAuthorityMetrics.Calculate(
                initialVehicleMassKg,
                Physics.gravity.y,
                partsConfig.GetActiveEngineCount(),
                partsConfig.maxThrustPerEngine,
                partsConfig.minThrottle,
                partsConfig.independentEngines);
            string trainingObjectiveJson = JsonUtility.ToJson(envConfig.EnsureTrainingObjective());
            var manifest = new TrainingRunManifest
            {
                runId = runId,
                createdUtc = createdUtc,
                lastLaunchedUtc = DateTime.UtcNow.ToString("o"),
                resumed = resumed,
                initializedFromRunId = resumed ? string.Empty : initializedFromRunId ?? string.Empty,
                scenario = envConfig.scenario.ToString(),
                hardwarePreset = partsConfig.hardwarePreset.ToString(),
                engineLayout = partsConfig.engineLayout.ToString(),
                octawebBurnGroup = partsConfig.octawebBurnGroup.ToString(),
                installedEngineCount = partsConfig.GetEngineCount(),
                activeEngineCount = partsConfig.GetActiveEngineCount(),
                engineControlChannels = partsConfig.GetIndependentEngineCount(),
                independentEngineControl = partsConfig.independentEngines,
                finCount = partsConfig.GetFinCount(),
                finsEnabled = partsConfig.finsEnabled,
                rcsEnabled = partsConfig.rcsEnabled,
                startFuelFraction = partsConfig.startFuelFraction,
                startFuelMassKg = partsConfig.startFuelMass,
                maximumThrustPerEngineN = partsConfig.maxThrustPerEngine,
                minimumThrottle01 = partsConfig.minThrottle,
                engineStartupDelaySeconds = partsConfig.engineStartupDelay,
                engineMinimumRunTimeSeconds = partsConfig.engineMinimumRunTime,
                engineRestartCooldownSeconds = partsConfig.engineRestartCooldown,
                physicalLandingGearEnabled = envConfig.scenario == ScenarioType.LegLanding,
                landingGearFootprintDiameterM = envConfig.scenario == ScenarioType.LegLanding
                    ? LandingLegAssembly.ReferenceFootRadiusM * 2f
                    : 0f,
                landingRequiredStableFeet = envConfig.scenario == ScenarioType.LegLanding
                    ? envConfig.GetTrainingObjective(ScenarioType.LegLanding).terminations.legMinimumStableFeet
                    : 0,
                commonSingleEngineLandingEnvelope = envConfig.scenario == ScenarioType.LegLanding,
                vectorObservationSize = RocketAgentSchema.ObservationSize(partsConfig),
                continuousActionSize = RocketAgentSchema.ContinuousActionSize(partsConfig),
                behaviorName = mlConfig.behaviorName,
                trainerType = mlConfig.trainerType.ToString(),
                trainerSeed = mlConfig.trainerSeed,
                environmentSeed = envConfig.environmentSeed,
                fixedDeltaTimeSeconds = Time.fixedDeltaTime,
                decisionPeriod = RocketAgentSchema.CanonicalDecisionPeriod,
                trainingStepLogInterval = (telemetryConfig ?? new TelemetryConfig()).TrainingStepLogInterval,
                initialVehicleMassKg = initialVehicleMassKg,
                minimumCommandableNonzeroThrustToWeight = thrustAuthority.MinimumCommandableNonzeroTwr,
                allActiveEnginesMinimumThrottleThrustToWeight = thrustAuthority.AllActiveEnginesMinimumThrottleTwr,
                allActiveEnginesMaximumThrustToWeight = thrustAuthority.AllActiveEnginesMaximumTwr,
                trainingObjectiveSha256 = Sha256Hex(trainingObjectiveJson),
                partsConfigSha256 = Sha256Hex(JsonUtility.ToJson(partsConfig)),
                mlAgentsConfigSha256 = Sha256Hex(mlConfig.ToYAML()),
                unityVersion = Application.unityVersion,
                mlAgentsAssemblyVersion = typeof(Unity.MLAgents.Agent).Assembly.GetName().Version?.ToString() ?? "unknown",
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemorySizeMb = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                configuredTorchDevice = string.IsNullOrWhiteSpace(configuredTorchDevice) ? "unknown" : configuredTorchDevice,
                pythonEnvironmentProbeSucceeded = trainingEnvironment.probeSucceeded,
                pythonEnvironmentProbeError = trainingEnvironment.probeError,
                condaEnvironmentName = trainingEnvironment.condaEnvironmentName,
                pythonExecutable = trainingEnvironment.pythonExecutable,
                pythonVersion = trainingEnvironment.pythonVersion,
                mlAgentsPythonVersion = trainingEnvironment.mlAgentsPythonVersion,
                pytorchVersion = trainingEnvironment.pytorchVersion,
                cudaRuntimeVersion = trainingEnvironment.cudaRuntimeVersion,
                cudaAvailable = trainingEnvironment.cudaAvailable,
                trainerGpuName = trainingEnvironment.gpuName,
                sourceControlRevision = revision,
                sourceControlDirty = dirty
            };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        /// <summary>Returns a lowercase SHA-256 fingerprint for a serialized configuration.</summary>
        static string Sha256Hex(string value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            var result = new StringBuilder(digest.Length * 2);
            foreach (byte valueByte in digest)
                result.Append(valueByte.ToString("x2"));
            return result.ToString();
        }

        /// <summary>
        /// Best-effort Git provenance. Editor runs record the exact revision and
        /// whether local changes were present; exported builds safely fall back
        /// to "unknown" when Git or the repository is unavailable.
        /// </summary>
        static void TryReadSourceControlState(out string revision, out bool dirty)
        {
            revision = "unknown";
            dirty = true;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                revision = RunGit(projectRoot, "rev-parse HEAD");
                dirty = !string.IsNullOrWhiteSpace(RunGit(projectRoot, "status --porcelain"));
                if (string.IsNullOrWhiteSpace(revision))
                    revision = "unknown";
            }
            catch
            {
                revision = "unknown";
                dirty = true;
            }
        }

        static string RunGit(string workingDirectory, string arguments)
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(1500) || process.ExitCode != 0)
                return string.Empty;
            return output;
        }

        /// <summary>
        /// Persists the live environment state, including curriculum counters
        /// and difficulty, so a resumed trainer does not silently restart the
        /// curriculum from its launch-time value.
        /// </summary>
        public static void SaveEnvironmentState(string runId, SimEnvironmentConfig envConfig)
        {
            if (string.IsNullOrWhiteSpace(runId) || envConfig == null)
                return;

            string runRoot = RunRoot(runId);
            Directory.CreateDirectory(runRoot);
            string path = Path.Combine(runRoot, EnvConfigFileName);
            string temporaryPath = path + ".tmp";

            File.WriteAllText(temporaryPath, JsonUtility.ToJson(envConfig));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Fall through to the portable copy-and-delete path.
                }
            }

            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }

        /// <summary>
        /// Returns run ids that have saved environment or parts configs in the
        /// results folder.
        /// </summary>
        public static string[] ScanExistingRuns()
        {
            try
            {
                string resultsRoot = GetResultsRoot();
                if (!Directory.Exists(resultsRoot))
                    return Array.Empty<string>();

                var valid = new List<string>();
                foreach (string dir in Directory.GetDirectories(resultsRoot))
                {
                    if (HasCurrentRunLayout(dir))
                        valid.Add(Path.GetFileName(dir));
                }

                return valid.ToArray();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrainingRunRepository] ScanExistingRuns error: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns whether a run has the environment and parts config snapshots required for resume/inference.
        /// </summary>
        public static bool HasCompleteRunConfig(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
                return false;

            return HasCurrentRunLayout(Path.Combine(GetResultsRoot(), runId));
        }

        /// <summary>
        /// Accepts only complete runs written by this source schema. This is a
        /// deliberate clean break: an old configuration must never be interpreted
        /// as a partially populated configurable objective.
        /// </summary>
        static bool HasCurrentRunLayout(string root)
        {
            string environmentPath = Path.Combine(root, EnvConfigFileName);
            string partsPath = Path.Combine(root, PartsConfigFileName);
            string mlPath = Path.Combine(root, MlConfigFileName);
            if (!File.Exists(environmentPath) ||
                !File.Exists(partsPath) ||
                !File.Exists(mlPath))
                return false;

            if (!TryLoadRunManifest(root, out TrainingRunManifest manifest) ||
                manifest.schemaVersion != TrainingRunManifest.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(manifest.trainingObjectiveSha256) ||
                string.IsNullOrWhiteSpace(manifest.partsConfigSha256) ||
                string.IsNullOrWhiteSpace(manifest.mlAgentsConfigSha256))
                return false;

            try
            {
                SimEnvironmentConfig environment = JsonUtility.FromJson<SimEnvironmentConfig>(
                    File.ReadAllText(environmentPath));
                if (environment == null ||
                    environment.trainingObjective == null ||
                    !Enum.IsDefined(typeof(ScenarioType), environment.scenario) ||
                    !string.Equals(
                        environment.scenario.ToString(),
                        manifest.scenario,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        Sha256Hex(JsonUtility.ToJson(environment.trainingObjective)),
                        manifest.trainingObjectiveSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        Sha256Hex(File.ReadAllText(partsPath)),
                        manifest.partsConfigSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        Sha256Hex(File.ReadAllText(mlPath)),
                        manifest.mlAgentsConfigSha256,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Loads lightweight run metadata used by the UI when listing saved runs.
        /// </summary>
        public static RunConfigInfo LoadRunInfo(string runId)
        {
            try
            {
                string path = Path.Combine(GetResultsRoot(), runId, EnvConfigFileName);
                if (!File.Exists(path))
                    return null;

                var cfg = JsonUtility.FromJson<SimEnvironmentConfig>(File.ReadAllText(path));
                return cfg == null ? null : new RunConfigInfo { scenario = cfg.scenario };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads the saved environment, hardware, and optional ML trainer configs for a completed run.
        /// </summary>
        public static TrainingRunConfigs LoadRunConfigs(string runId)
        {
            string root = Path.Combine(GetResultsRoot(), runId);
            string envPath = Path.Combine(root, EnvConfigFileName);
            string partsPath = Path.Combine(root, PartsConfigFileName);
            string mlPath = Path.Combine(root, MlConfigFileName);

            if (!HasCurrentRunLayout(root))
                throw new InvalidDataException(
                    $"Run '{runId}' is incomplete or was created with an unsupported objective schema.");

            return new TrainingRunConfigs
            {
                envConfig = JsonUtility.FromJson<SimEnvironmentConfig>(File.ReadAllText(envPath)),
                partsConfig = JsonUtility.FromJson<RocketPartsConfig>(File.ReadAllText(partsPath)),
                mlConfig = MLAgentsConfig.TryFromYAML(File.ReadAllText(mlPath), out var loadedMl)
                    ? loadedMl
                    : new MLAgentsConfig()
            };
        }
    }
}
