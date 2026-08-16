using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RocketSim
{
    /// <summary>Small index file used to find the current immutable run revision.</summary>
    [Serializable]
    public sealed class SimulationRunManifest
    {
        public int schemaVersion = SimulationSessionConfig.CurrentSchemaVersion;
        public string runId;
        public string createdUtc;
        public string lastLaunchedUtc;
        public int currentRevision;
        public string currentSessionSha256;
        public string scenario;
        public int vectorObservationSize;
        public int continuousActionSize;
        public int engineControlChannels;
        public int finCount;
        public int rcsJetCount;
        public string trainerType;
        public string behaviorName;
        public int hiddenUnits;
        public int numLayers;
        public bool normalize;
    }

    /// <summary>Launch provenance stored beside one immutable session snapshot.</summary>
    [Serializable]
    public sealed class SessionRevisionManifest
    {
        public int schemaVersion = SimulationSessionConfig.CurrentSchemaVersion;
        public int revision;
        public string runId;
        public string sessionSha256;
        public string createdUtc;
        public string launchMode;
        public string sourceRunId;
        public TrainingEnvironmentProvenance trainingEnvironment;
        public string configuredTorchDevice;
    }

    /// <summary>
    /// Stores immutable session revisions and mutable runtime progress. All
    /// writes use a temporary file so an interrupted save cannot leave partial JSON.
    /// </summary>
    public static class SimulationSessionStore
    {
        public const string RunManifestFileName = "RunManifest.json";
        public const string SessionFileName = "Session.json";
        public const string RevisionManifestFileName = "LaunchManifest.json";
        public const string RuntimeStateFileName = "RuntimeState.json";
        public const string RevisionsDirectoryName = "revisions";

        public static string ResultsRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));

        public static string RunRoot(string runId) => Path.Combine(ResultsRoot, ValidateRunId(runId));

        public static string RevisionRoot(string runId, int revision) =>
            Path.Combine(RunRoot(runId), RevisionsDirectoryName, revision.ToString("D4"));

        public static int NextRevision(string runId)
        {
            return TryLoadManifest(runId, out SimulationRunManifest manifest)
                ? Math.Max(1, manifest.currentRevision + 1)
                : 1;
        }

        /// <summary>Writes the exact snapshot and updates the run's latest pointer.</summary>
        public static string SaveSnapshot(
            string runId,
            SimulationSessionSnapshot snapshot,
            RunLaunchRequest request,
            TrainingEnvironmentProvenance trainingEnvironment,
            string configuredTorchDevice)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (request == null) throw new ArgumentNullException(nameof(request));
            string canonicalRunId = ValidateRunId(runId);
            if (!string.Equals(canonicalRunId, request.RunId, StringComparison.Ordinal))
                throw new InvalidOperationException("Run request and storage run ID do not match.");

            SimulationSessionConfig config = snapshot.CreatePersistenceCopy();
            string runRoot = RunRoot(canonicalRunId);
            string revisionRoot = RevisionRoot(canonicalRunId, snapshot.Revision);
            Directory.CreateDirectory(revisionRoot);

            AtomicWrite(
                Path.Combine(revisionRoot, SessionFileName),
                JsonUtility.ToJson(config, true));
            AtomicWrite(
                Path.Combine(revisionRoot, "TrainingConfig.yaml"),
                config.learning.ToYAML());

            var revisionManifest = new SessionRevisionManifest
            {
                revision = snapshot.Revision,
                runId = canonicalRunId,
                sessionSha256 = snapshot.Sha256,
                createdUtc = snapshot.CreatedUtc,
                launchMode = request.Mode.ToString(),
                sourceRunId = request.SourceRunId,
                trainingEnvironment = trainingEnvironment ?? new TrainingEnvironmentProvenance(),
                configuredTorchDevice = configuredTorchDevice ?? string.Empty
            };
            AtomicWrite(
                Path.Combine(revisionRoot, RevisionManifestFileName),
                JsonUtility.ToJson(revisionManifest, true));

            string createdUtc = snapshot.CreatedUtc;
            if (TryLoadManifest(canonicalRunId, out SimulationRunManifest previous) &&
                !string.IsNullOrWhiteSpace(previous.createdUtc))
                createdUtc = previous.createdUtc;

            RocketPartsConfig vehicle = config.vehicle;
            var manifest = new SimulationRunManifest
            {
                runId = canonicalRunId,
                createdUtc = createdUtc,
                lastLaunchedUtc = snapshot.CreatedUtc,
                currentRevision = snapshot.Revision,
                currentSessionSha256 = snapshot.Sha256,
                scenario = config.environment.scenario.ToString(),
                vectorObservationSize = RocketAgentSchema.ObservationSize(vehicle),
                continuousActionSize = RocketAgentSchema.ContinuousActionSize(vehicle),
                engineControlChannels = vehicle.GetIndependentEngineCount(),
                finCount = vehicle.GetFinCount(),
                rcsJetCount = vehicle.GetRCSCount(),
                trainerType = config.learning.trainerType.ToString(),
                behaviorName = config.learning.behaviorName,
                hiddenUnits = config.learning.hiddenUnits,
                numLayers = config.learning.numLayers,
                normalize = config.learning.normalize
            };
            AtomicWrite(Path.Combine(runRoot, RunManifestFileName), JsonUtility.ToJson(manifest, true));
            return runRoot;
        }

        public static bool HasCurrentSession(string runId)
        {
            if (!TryLoadManifest(runId, out SimulationRunManifest manifest)) return false;
            if (manifest.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion || manifest.currentRevision <= 0)
                return false;

            string path = Path.Combine(RevisionRoot(runId, manifest.currentRevision), SessionFileName);
            if (!File.Exists(path)) return false;
            try
            {
                SimulationSessionConfig session = JsonUtility.FromJson<SimulationSessionConfig>(File.ReadAllText(path));
                if (session == null || session.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion)
                    return false;
                session.EnsureSections();
                string actualHash = SimulationSessionSnapshotFactory.Sha256Hex(JsonUtility.ToJson(session));
                return string.Equals(actualHash, manifest.currentSessionSha256, StringComparison.OrdinalIgnoreCase) &&
                       SimulationSessionValidator.Validate(session).IsValid;
            }
            catch
            {
                return false;
            }
        }

        public static SimulationSessionConfig LoadLatest(string runId)
        {
            if (!TryLoadManifest(runId, out SimulationRunManifest manifest) ||
                manifest.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion)
                throw new InvalidDataException($"Run '{runId}' has no current simulation-session manifest.");
            return LoadRevision(runId, manifest.currentRevision);
        }

        public static SimulationSessionConfig LoadRevision(string runId, int revision)
        {
            string path = Path.Combine(RevisionRoot(runId, revision), SessionFileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Run '{runId}' revision {revision} has no session snapshot.", path);

            SimulationSessionConfig session = JsonUtility.FromJson<SimulationSessionConfig>(File.ReadAllText(path));
            if (session == null || session.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion)
                throw new InvalidDataException($"Run '{runId}' revision {revision} uses an unsupported session schema.");
            session.EnsureSections();
            SessionValidationResult validation = SimulationSessionValidator.Validate(session);
            if (!validation.IsValid)
                throw new InvalidDataException($"Run '{runId}' revision {revision} contains invalid session data.");
            return session;
        }

        public static bool TryLoadManifest(string runId, out SimulationRunManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(runId)) return false;
            string path;
            try
            {
                path = Path.Combine(RunRoot(runId), RunManifestFileName);
            }
            catch
            {
                return false;
            }
            if (!File.Exists(path)) return false;
            try
            {
                manifest = JsonUtility.FromJson<SimulationRunManifest>(File.ReadAllText(path));
                return manifest != null;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        public static string[] ScanRuns()
        {
            if (!Directory.Exists(ResultsRoot)) return Array.Empty<string>();
            var runs = new List<string>();
            foreach (string directory in Directory.GetDirectories(ResultsRoot))
            {
                string runId = Path.GetFileName(directory);
                if (HasCurrentSession(runId)) runs.Add(runId);
            }
            runs.Sort(StringComparer.OrdinalIgnoreCase);
            return runs.ToArray();
        }

        public static void SaveRuntimeState(string runId, RunRuntimeState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            string root = RunRoot(runId);
            Directory.CreateDirectory(root);
            AtomicWrite(Path.Combine(root, RuntimeStateFileName), JsonUtility.ToJson(state, true));
        }

        public static RunRuntimeState LoadRuntimeState(string runId)
        {
            string path = Path.Combine(RunRoot(runId), RuntimeStateFileName);
            if (!File.Exists(path)) return new RunRuntimeState();
            RunRuntimeState state = JsonUtility.FromJson<RunRuntimeState>(File.ReadAllText(path));
            if (state == null || state.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion)
                throw new InvalidDataException($"Run '{runId}' has an unsupported runtime-state schema.");
            return state;
        }

        static string ValidateRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
            string value = runId.Trim();
            if (value == "." || value == ".." ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.Contains(Path.DirectorySeparatorChar.ToString()) ||
                value.Contains(Path.AltDirectorySeparatorChar.ToString()))
                throw new ArgumentException($"Run ID '{runId}' is not a safe directory name.", nameof(runId));
            return value;
        }

        static void AtomicWrite(string path, string contents)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, contents ?? string.Empty);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Portable fallback below.
                }
            }
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }
    }
}
