using System;
using System.IO;
using UnityEngine;

namespace RocketSim
{
    public sealed class RunConfigInfo
    {
        public ScenarioType scenario;
        public int revision;
        public string sessionSha256;
    }

    public sealed class TrainingRunConfigs
    {
        public SimEnvironmentConfig envConfig;
        public RocketPartsConfig partsConfig;
        public MLAgentsConfig mlConfig;
        public TelemetryConfig telemetryConfig;
        public int revision;
        public string sessionSha256;
    }

    /// <summary>
    /// Best-effort record of the Python environment used to start ML-Agents.
    /// Probe failures are recorded rather than blocking a valid launch.
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
    /// Compatibility facade used by the current UI and launcher while session
    /// ownership is moved into application services. Persistence itself belongs
    /// exclusively to SimulationSessionStore.
    /// </summary>
    internal static class TrainingRunRepository
    {
        public static string GetResultsRoot() => SimulationSessionStore.ResultsRoot;
        public static string RunRoot(string runId) => SimulationSessionStore.RunRoot(runId);

        /// <summary>
        /// New training requires an unused run ID. Resume accepts a revisioned
        /// configuration change only when the saved policy interface and network
        /// remain compatible with the checkpoint.
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

            bool directoryExists;
            try
            {
                directoryExists = Directory.Exists(RunRoot(runId));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (!resume)
            {
                if (!directoryExists) return true;
                error = $"Run '{runId}' already exists. Resume it or choose a new Run ID.";
                return false;
            }

            if (!SimulationSessionStore.HasCurrentSession(runId))
            {
                error = $"Run '{runId}' has no complete current session snapshot.";
                return false;
            }
            if (requestedEnvironment == null || requestedParts == null || requestedMl == null)
            {
                error = "The requested task, vehicle, or learning configuration is missing.";
                return false;
            }

            SimulationSessionConfig saved = SimulationSessionStore.LoadLatest(runId);
            if (!PolicySchemaMatches(saved.vehicle, requestedParts))
            {
                error = "The modified vehicle/task exposes a different policy interface. Start a new compatible run instead of resuming this checkpoint.";
                return false;
            }
            if (!NetworkMatches(saved.learning, requestedMl))
            {
                error = "Trainer type, behavior name, normalization, hidden units, and layer count must match when resuming.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Freezes and stores the complete session before the Python process is
        /// started. Every launch gets an immutable numbered revision.
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
            SimulationSessionConfig session = SimulationSessionConfig.Capture(
                partsConfig,
                envConfig,
                mlConfig,
                telemetryConfig);
            int revision = SimulationSessionStore.NextRevision(runId);
            if (!SimulationSessionSnapshotFactory.TryCreate(
                    session,
                    revision,
                    out SimulationSessionSnapshot snapshot,
                    out SessionValidationResult validation))
                throw new InvalidDataException(FirstValidationError(validation));

            RunLaunchMode mode = resumed
                ? RunLaunchMode.ResumeTraining
                : string.IsNullOrWhiteSpace(initializedFromRunId)
                    ? RunLaunchMode.NewTraining
                    : RunLaunchMode.InitializeTraining;
            var request = new RunLaunchRequest(runId, mode, initializedFromRunId);
            string runRoot = SimulationSessionStore.SaveSnapshot(
                runId,
                snapshot,
                request,
                trainingEnvironment,
                configuredTorchDevice);

            // ML-Agents receives a stable root-level path. The authoritative
            // copy remains beside the immutable session revision.
            File.WriteAllText(Path.Combine(runRoot, mlConfigPath), session.learning.ToYAML());
            SimulationSessionStore.SaveRuntimeState(runId, RunRuntimeState.Capture(envConfig));
            return runRoot;
        }

        public static bool TryValidateInitializationSource(
            string sourceRunId,
            RocketPartsConfig targetParts,
            MLAgentsConfig targetMl,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sourceRunId)) return true;
            if (!SimulationSessionStore.HasCurrentSession(sourceRunId))
            {
                error = $"Initialization run '{sourceRunId}' has no current session snapshot.";
                return false;
            }
            string sourceRoot = RunRoot(sourceRunId);
            if (Directory.GetFiles(sourceRoot, "*.pt", SearchOption.AllDirectories).Length == 0)
            {
                error = $"Initialization run '{sourceRunId}' has no ML-Agents checkpoint (.pt).";
                return false;
            }

            SimulationSessionConfig source = SimulationSessionStore.LoadLatest(sourceRunId);
            if (!PolicySchemaMatches(source.vehicle, targetParts))
            {
                error = $"Initialization run '{sourceRunId}' has a different observation/action interface.";
                return false;
            }
            if (!NetworkMatches(source.learning, targetMl))
            {
                error = $"Initialization run '{sourceRunId}' uses incompatible trainer/network settings.";
                return false;
            }
            return true;
        }

        public static bool TryValidateInferencePolicySchema(
            string runId,
            RocketPartsConfig currentParts,
            out string error)
        {
            error = string.Empty;
            if (currentParts == null)
            {
                error = "The current vehicle configuration is missing.";
                return false;
            }
            if (!SimulationSessionStore.HasCurrentSession(runId) ||
                !SimulationSessionStore.TryLoadManifest(runId, out SimulationRunManifest manifest))
            {
                error = $"Run '{runId}' has no complete current model configuration.";
                return false;
            }

            bool compatible = manifest.vectorObservationSize == RocketAgentSchema.ObservationSize(currentParts) &&
                              manifest.continuousActionSize == RocketAgentSchema.ContinuousActionSize(currentParts) &&
                              manifest.engineControlChannels == currentParts.GetIndependentEngineCount() &&
                              manifest.finCount == currentParts.GetFinCount() &&
                              manifest.rcsJetCount == currentParts.GetRCSCount();
            if (!compatible)
            {
                error = $"Run '{runId}' was trained with a different policy interface. Restore its saved vehicle or use a compatible model.";
                return false;
            }
            return true;
        }

        /// <summary>Persists live curriculum progress without changing the frozen session.</summary>
        public static void SaveEnvironmentState(string runId, SimEnvironmentConfig envConfig)
        {
            if (string.IsNullOrWhiteSpace(runId) || envConfig == null ||
                !SimulationSessionStore.HasCurrentSession(runId))
                return;
            SimulationSessionStore.SaveRuntimeState(runId, RunRuntimeState.Capture(envConfig));
        }

        public static string[] ScanExistingRuns()
        {
            try
            {
                return SimulationSessionStore.ScanRuns();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrainingRunRepository] Could not scan runs: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public static bool HasCompleteRunConfig(string runId) =>
            SimulationSessionStore.HasCurrentSession(runId);

        public static RunConfigInfo LoadRunInfo(string runId)
        {
            try
            {
                SimulationSessionConfig session = SimulationSessionStore.LoadLatest(runId);
                SimulationSessionStore.TryLoadManifest(runId, out SimulationRunManifest manifest);
                return new RunConfigInfo
                {
                    scenario = session.environment.scenario,
                    revision = manifest?.currentRevision ?? 0,
                    sessionSha256 = manifest?.currentSessionSha256
                };
            }
            catch
            {
                return null;
            }
        }

        public static TrainingRunConfigs LoadRunConfigs(
            string runId,
            bool includeRuntimeState = true)
        {
            SimulationSessionConfig session = SimulationSessionStore.LoadLatest(runId);
            if (includeRuntimeState)
            {
                RunRuntimeState runtimeState = SimulationSessionStore.LoadRuntimeState(runId);
                runtimeState.ApplyTo(session.environment);
            }
            session.environment.runId = runId;
            SimulationSessionStore.TryLoadManifest(runId, out SimulationRunManifest manifest);
            return new TrainingRunConfigs
            {
                envConfig = session.environment,
                partsConfig = session.vehicle,
                mlConfig = session.learning,
                telemetryConfig = session.telemetry,
                revision = manifest?.currentRevision ?? 0,
                sessionSha256 = manifest?.currentSessionSha256
            };
        }

        public static TrainingObjectiveConfig LoadTrainingObjective(string runId)
        {
            SimulationSessionConfig session = SimulationSessionStore.LoadLatest(runId);
            return JsonUtility.FromJson<TrainingObjectiveConfig>(JsonUtility.ToJson(session.objective));
        }

        static bool PolicySchemaMatches(RocketPartsConfig left, RocketPartsConfig right)
        {
            if (left == null || right == null) return false;
            return RocketAgentSchema.ObservationSize(left) == RocketAgentSchema.ObservationSize(right) &&
                   RocketAgentSchema.ContinuousActionSize(left) == RocketAgentSchema.ContinuousActionSize(right) &&
                   left.GetIndependentEngineCount() == right.GetIndependentEngineCount() &&
                   left.GetFinCount() == right.GetFinCount() &&
                   left.GetRCSCount() == right.GetRCSCount();
        }

        static bool NetworkMatches(MLAgentsConfig left, MLAgentsConfig right)
        {
            if (left == null || right == null) return false;
            return left.trainerType == right.trainerType &&
                   string.Equals(left.behaviorName, right.behaviorName, StringComparison.Ordinal) &&
                   left.normalize == right.normalize &&
                   left.hiddenUnits == right.hiddenUnits &&
                   left.numLayers == right.numLayers;
        }

        static string FirstValidationError(SessionValidationResult validation)
        {
            if (validation != null)
                for (int i = 0; i < validation.Issues.Count; i++)
                    if (validation.Issues[i].Severity == SessionValidationSeverity.Error)
                        return validation.Issues[i].Message;
            return "The simulation session is invalid.";
        }
    }
}
