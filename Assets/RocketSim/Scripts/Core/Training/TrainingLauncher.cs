// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingLauncher.cs
// Purpose: Starts training or inference flow and coordinates with the ML-Agents trainer process.
// Main flow: save configs -> launch Python -> wait for port 5004 -> spawn agents.
// Inference skips Python and directly spawns one agent with a saved model.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Collections;
using Unity.MLAgents;
using UnityEngine;

namespace RocketSim
{
    public class TrainingLauncher : MonoBehaviour
    {
        [Header("Anaconda Settings")]
        public string condaEnvName = "mlagents_gpu";
        public string runId = "FalconRun";
        public string mlConfigPath = "TrainingConfig.yaml";
        public string partsConfigPath = "PartsConfig.json";
        public string envConfigPath = "EnvConfig.json";
        public bool resumeIfExists = false;
        [Tooltip("Optional prior run used to initialize a new trainer. This is different from resuming the same run.")]
        public string initializeFromRunId = "";

        public enum TrainingState { Idle, Launching, Connected, Completed, Failed }
        public TrainingState State { get; private set; } = TrainingState.Idle;
        public event Action<TrainingState> OnStateChanged;
        public string LastEvaluationSummaryPath { get; private set; }

        Process _trainerProcess;
        EvaluationSession _evaluationSession;
        TrainingAreaManager _evaluationManager;
        bool _evaluationCompleting;

        /// <summary>
        /// Starts the requested training or inference workflow.
        /// </summary>
        public void Launch(TrainingAreaManager manager)
        {
            if (State is TrainingState.Launching or TrainingState.Connected) return;
            if (!manager)
            {
                UnityEngine.Debug.LogError("[TrainingLauncher] Cannot launch without a TrainingAreaManager.");
                SetState(TrainingState.Failed);
                return;
            }

            if (!ValidateObjective(manager.envConfig))
            {
                SetState(TrainingState.Failed);
                return;
            }

            // The logger freezes its columns during Initialize, so scenario and
            // hardware-dependent telemetry must be resolved before that call.
            manager.PrepareTelemetrySchema();

            if (manager.envConfig.behaviorType == BehaviorType.Training)
            {
                if (!TrainingRunRepository.TryValidateRunDestination(
                        manager.envConfig.runId,
                        resumeIfExists,
                        manager.envConfig,
                        manager.partsConfig,
                        manager.mlConfig,
                        out var destinationError))
                {
                    UnityEngine.Debug.LogError($"[TrainingLauncher] {destinationError}");
                    SetState(TrainingState.Failed);
                    return;
                }

                // We are training
                CommunicatorFactory.Enabled = true;

                runId = manager.envConfig.runId;
                const string torchDevice = "cuda";
                TrainingEnvironmentProvenance trainingEnvironment = TrainingProcessLauncher.ProbeEnvironment(condaEnvName);
                if (!trainingEnvironment.probeSucceeded)
                    UnityEngine.Debug.LogWarning(
                        $"[TrainingLauncher] Trainer provenance probe failed: {trainingEnvironment.probeError}");
                TelemetryLogger.Instance?.Initialize(manager.telemetryConfig, runId);
                string runRoot = TrainingRunRepository.SaveTrainingConfigs(
                    runId,
                    manager.mlConfig,
                    manager.envConfig,
                    manager.partsConfig,
                    mlConfigPath,
                    envConfigPath,
                    partsConfigPath,
                    manager.telemetryConfig,
                    resumeIfExists,
                    initializeFromRunId,
                    trainingEnvironment,
                    torchDevice);
            
                try {
                    _trainerProcess = TrainingProcessLauncher.LaunchCondaMlAgents(new TrainingLaunchRequest
                    {
                        condaEnvName = condaEnvName,
                        runId = runId,
                        mlConfigFilePath = System.IO.Path.Combine(runRoot, mlConfigPath),
                        resumeIfExists = resumeIfExists,
                        initializeFromRunId = initializeFromRunId,
                        torchDevice = torchDevice,
                        trainerSeed = manager.mlConfig.trainerSeed
                    });
                    SetState(TrainingState.Launching);
                    StartCoroutine(WaitForPythonAndSpawn(manager));
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogError($"Launch Failed: {e.Message}");
                    TelemetryLogger.Instance?.DisableLogging();
                    SetState(TrainingState.Failed);
                }
            }
            else
            {
                CommunicatorFactory.Enabled = false;
                runId = manager.envConfig.runId;
                manager.envConfig.PrepareStandardEvaluation();
                if (manager.envConfig.IsStandardEvaluation && !TelemetryLogger.Instance)
                {
                    // The evaluator advances from completed telemetry outcomes;
                    // starting without its logger would otherwise run forever.
                    UnityEngine.Debug.LogError(
                        "[Evaluator] Cannot start standard evaluation because no TelemetryLogger exists in the scene.");
                    SetState(TrainingState.Failed);
                    return;
                }
                TelemetryLogger.Instance?.InitializeEvaluation(manager.telemetryConfig, runId);

                if (manager.envConfig.IsStandardEvaluation)
                {
                    EvaluationConfig evaluation = manager.envConfig.EnsureEvaluationConfig();
                    _evaluationManager = manager;
                    _evaluationSession = new EvaluationSession(
                        runId,
                        evaluation.seed,
                        evaluation.episodeCount,
                        TelemetryLogger.Instance?.EpisodeFilePath,
                        manager.envConfig.scenario);
                    LastEvaluationSummaryPath = null;
                    if (TelemetryLogger.Instance != null)
                        TelemetryLogger.Instance.EpisodeCompleted += OnEvaluationEpisodeCompleted;
                }

                if (!manager.SpawnAreas())
                {
                    CancelEvaluationSession(writePartialSummary: false);
                    TelemetryLogger.Instance?.DisableLogging();
                    SetState(TrainingState.Failed);
                    return;
                }

                SetState(TrainingState.Connected);
            }
        }

        /// <summary>
        /// Enforces the same objective validation for UI, scripted, and
        /// headless launches. Warnings remain advisory so deliberate zero-value
        /// experiments are possible; errors stop before files or processes are
        /// created.
        /// </summary>
        static bool ValidateObjective(SimEnvironmentConfig envConfig)
        {
            if (envConfig == null)
            {
                UnityEngine.Debug.LogError("[TrainingLauncher] Environment configuration is missing.");
                return false;
            }
            if (!Enum.IsDefined(typeof(ScenarioType), envConfig.scenario))
            {
                UnityEngine.Debug.LogError(
                    $"[TrainingLauncher] Scenario value {(int)envConfig.scenario} is not part of the current schema.");
                return false;
            }

            ObjectiveValidationResult validation = ObjectiveValidator.Validate(envConfig.scenario, envConfig.GetTrainingObjective(envConfig.scenario));
            for (int i = 0; i < validation.issues.Count; i++)
            {
                ObjectiveValidationIssue issue = validation.issues[i];
                string message = $"[TrainingLauncher] Objective {issue.code}: {issue.message}";
                if (issue.severity == ObjectiveValidationSeverity.Error)
                    UnityEngine.Debug.LogError(message);
                else
                    UnityEngine.Debug.LogWarning(message);
            }

            return validation.IsValid;
        }

        /// <summary>
        /// Waits for the ML-Agents Python trainer to open its communicator port
        /// before spawning training agents that will connect to it.
        /// </summary>
        IEnumerator WaitForPythonAndSpawn(TrainingAreaManager manager)
        {
            float timeout = 45f;
            int port = 5004;

            while (timeout > 0f)
            {
                if (_trainerProcess == null || _trainerProcess.HasExited) {
                    _trainerProcess?.Dispose();
                    _trainerProcess = null;
                    TelemetryLogger.Instance?.DisableLogging();
                    SetState(TrainingState.Failed);
                    yield break;
                }

                if (TrainingProcessLauncher.TcpPortIsOpen(port))
                {
                    UnityEngine.Debug.Log("[TrainingLauncher] Python is ready. Spawning Agents...");
                    if (!manager.SpawnAreas())
                    {
                        StopTraining();
                        TelemetryLogger.Instance?.DisableLogging();
                        SetState(TrainingState.Failed);
                        yield break;
                    }
                    SetState(TrainingState.Connected);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
                timeout -= 1f;
            }
            StopTraining();
            TelemetryLogger.Instance?.DisableLogging();
            SetState(TrainingState.Failed);
        }

        /// <summary>
        /// Stops the external trainer process if it is still running.
        /// </summary>
        public void StopTraining()
        {
            CancelEvaluationSession(writePartialSummary: true);

            if (_trainerProcess != null)
            {
                if (!_trainerProcess.HasExited)
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    // Stop cmd.exe and the Python trainer it started. Killing
                    // only cmd.exe can leave mlagents-learn running in the background.
                    try
                    {
                        using Process treeKiller = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill.exe",
                            Arguments = $"/PID {_trainerProcess.Id} /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        treeKiller?.WaitForExit(3000);
                    }
                    catch
                    {
                        _trainerProcess.Kill();
                    }
#else
                    _trainerProcess.Kill();
#endif
                }
                _trainerProcess.Dispose();
                _trainerProcess = null;
            }
            StopAllCoroutines();
            SetState(TrainingState.Idle);
        }

        /// <summary>
        /// Consumes one evaluation outcome, logs progress, and schedules a safe
        /// end-of-frame teardown after the configured suite reaches its target.
        /// </summary>
        void OnEvaluationEpisodeCompleted(TelemetryEpisodeOutcome outcome)
        {
            if (_evaluationSession == null || _evaluationCompleting) return;

            bool complete = _evaluationSession.Record(outcome);
            string successProgress = _evaluationSession.SuccessMetricDefined
                ? _evaluationSession.SuccessfulEpisodes.ToString()
                : "n/a (trajectory endpoint task)";
            UnityEngine.Debug.Log(
                $"[Evaluator] {_evaluationSession.CompletedEpisodes}/{_evaluationSession.TargetEpisodes} episodes, " +
                $"successes={successProgress}.");
            if (!complete) return;

            _evaluationCompleting = true;
            try
            {
                LastEvaluationSummaryPath = _evaluationSession.Finish(aborted: false);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[Evaluator] Could not write final summary: {exception.Message}");
                LastEvaluationSummaryPath = null;
            }
            if (TelemetryLogger.Instance != null)
            {
                TelemetryLogger.Instance.EpisodeCompleted -= OnEvaluationEpisodeCompleted;
                // The target episode has already been written. Disabling now
                // prevents the immediate ML-Agents reset from opening episode N+1.
                TelemetryLogger.Instance.DisableLogging();
            }
            StartCoroutine(FinishEvaluationAtEndOfFrame());
        }

        /// <summary>
        /// Removes the evaluated agent outside its EndEpisode call stack, then
        /// reports completion so the panel can unlock itself automatically.
        /// </summary>
        IEnumerator FinishEvaluationAtEndOfFrame()
        {
            yield return null;
            _evaluationManager?.StopActiveRunAndShowPreview();
            _evaluationManager = null;
            _evaluationSession = null;
            _evaluationCompleting = false;
            UnityEngine.Debug.Log($"[Evaluator] Complete. Summary: {LastEvaluationSummaryPath}");
            SetState(TrainingState.Completed);
        }

        /// <summary>
        /// Unsubscribes evaluator callbacks and optionally preserves an aborted
        /// partial summary when the user stops before the target episode count.
        /// </summary>
        void CancelEvaluationSession(bool writePartialSummary)
        {
            if (TelemetryLogger.Instance != null)
                TelemetryLogger.Instance.EpisodeCompleted -= OnEvaluationEpisodeCompleted;

            if (_evaluationSession != null && writePartialSummary)
            {
                try
                {
                    LastEvaluationSummaryPath = _evaluationSession.Finish(aborted: true);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError($"[Evaluator] Could not write partial summary: {exception.Message}");
                }
            }

            _evaluationSession = null;
            _evaluationManager = null;
            _evaluationCompleting = false;
        }

        /// <summary>
        /// Stops the external trainer process when this launcher or scene is destroyed.
        /// </summary>
        void OnDestroy() => StopTraining();

        /// <summary>
        /// Updates launcher state and notifies the panel so buttons, locking,
        /// and validation messages immediately reflect the new run state.
        /// </summary>
        void SetState(TrainingState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
