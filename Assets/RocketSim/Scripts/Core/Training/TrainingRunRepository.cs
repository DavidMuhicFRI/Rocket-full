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

    internal static class TrainingRunRepository
    {
        const string EnvConfigFileName = "EnvConfig.json";
        const string InitialEnvConfigFileName = "EnvConfig.initial.json";
        const string PartsConfigFileName = "PartsConfig.json";
        const string MlConfigFileName = "TrainingConfig.yaml";

        /// <summary>
        /// Returns the project-level ML-Agents results folder, falling back to
        /// Assets/results when the normal root does not exist.
        /// </summary>
        public static string GetResultsRoot()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
            if (Directory.Exists(projectRoot)) return projectRoot;

            Debug.Log("[TrainingRunRepository] MLAgents results directory not found, falling back to Assets/results");
            return Path.GetFullPath(Path.Combine(Application.dataPath, "results"));
        }

        /// <summary>
        /// Returns the folder path that stores configs and trainer output for one run id.
        /// </summary>
        public static string RunRoot(string runId) => Path.Combine(GetResultsRoot(), runId);

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
            string partsConfigPath)
        {
            string runRoot = RunRoot(runId);
            Directory.CreateDirectory(runRoot);

            File.WriteAllText(Path.Combine(runRoot, mlConfigPath), mlConfig.ToYAML());
            string serializedEnvironment = JsonUtility.ToJson(envConfig);
            File.WriteAllText(Path.Combine(runRoot, envConfigPath), serializedEnvironment);
            string initialEnvironmentPath = Path.Combine(runRoot, InitialEnvConfigFileName);
            if (!File.Exists(initialEnvironmentPath))
                File.WriteAllText(initialEnvironmentPath, serializedEnvironment);
            File.WriteAllText(Path.Combine(runRoot, partsConfigPath), JsonUtility.ToJson(partsConfig));

            return runRoot;
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
                    bool hasEnv = File.Exists(Path.Combine(dir, EnvConfigFileName));
                    bool hasParts = File.Exists(Path.Combine(dir, PartsConfigFileName));
                    bool hasMl = File.Exists(Path.Combine(dir, MlConfigFileName));
                    if (hasEnv && hasParts && hasMl)
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

            string root = Path.Combine(GetResultsRoot(), runId);
            return File.Exists(Path.Combine(root, EnvConfigFileName)) &&
                   File.Exists(Path.Combine(root, PartsConfigFileName)) &&
                   File.Exists(Path.Combine(root, MlConfigFileName));
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

            if (!File.Exists(envPath) || !File.Exists(partsPath) || !File.Exists(mlPath))
                throw new FileNotFoundException($"Config files missing in: {root}");

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
