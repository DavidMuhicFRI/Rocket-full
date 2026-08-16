// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/SimulationRunCoordinator.cs
// Purpose: Freezes a session and routes it to training, inference, or evaluation.
// Main flow: validate draft -> freeze snapshot -> persist training revision ->
// give a runtime copy to the area host -> start the requested runner.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using Unity.MLAgents;
using UnityEngine;

namespace RocketSim
{
    public class SimulationRunCoordinator : MonoBehaviour
    {
        [Header("Anaconda Settings")]
        public string condaEnvName = "mlagents_gpu";

        public enum RunState { Idle, Launching, Connected, Completed, Failed }
        public RunState State { get; private set; } = RunState.Idle;
        public event Action<RunState> OnStateChanged;
        public string LastEvaluationSummaryPath => _evaluation?.LastSummaryPath;

        TrainingRunController _training;
        EvaluationRunController _evaluation;

        /// <summary>
        /// Starts the requested training or inference workflow.
        /// </summary>
        public void Launch(SimulationAreaHost manager, RunLaunchRequest request)
        {
            if (State is RunState.Launching or RunState.Connected) return;
            if (!manager)
            {
                UnityEngine.Debug.LogError("[SimulationRunCoordinator] Cannot launch without a SimulationAreaHost.");
                SetState(RunState.Failed);
                return;
            }

            SimulationSessionConfig requestedSession = manager.SessionDraft.Config.DeepCopy();
            if (request == null ||
                !string.Equals(
                    request.RunId,
                    requestedSession.environment.runId,
                    StringComparison.Ordinal))
            {
                UnityEngine.Debug.LogError("[SimulationRunCoordinator] Launch request and session Run ID do not match.");
                SetState(RunState.Failed);
                return;
            }
            bool trainingMode = requestedSession.environment.behaviorType == BehaviorType.Training;
            bool requestIsTraining = request.Mode is RunLaunchMode.NewTraining or
                RunLaunchMode.ResumeTraining or RunLaunchMode.InitializeTraining;
            if (trainingMode != requestIsTraining)
            {
                UnityEngine.Debug.LogError("[SimulationRunCoordinator] Launch mode does not match the session behavior mode.");
                SetState(RunState.Failed);
                return;
            }
            PrepareDerivedTelemetry(requestedSession);
            if (requestedSession.environment.behaviorType == BehaviorType.Inference)
                requestedSession.environment.PrepareStandardEvaluation();

            if (!TryValidateSession(requestedSession))
            {
                SetState(RunState.Failed);
                return;
            }

            if (requestedSession.environment.behaviorType == BehaviorType.Training)
            {
                if (!SimulationRunService.TryValidateTrainingDestination(
                        requestedSession.environment.runId,
                        request.Mode == RunLaunchMode.ResumeTraining,
                        requestedSession.environment,
                        requestedSession.vehicle,
                        requestedSession.learning,
                        out var destinationError))
                {
                    UnityEngine.Debug.LogError($"[SimulationRunCoordinator] {destinationError}");
                    SetState(RunState.Failed);
                    return;
                }
                string initializationError = null;
                if (request.Mode == RunLaunchMode.InitializeTraining &&
                    (string.IsNullOrWhiteSpace(request.SourceRunId) ||
                     !SimulationRunService.TryValidateInitializationSource(
                         request.SourceRunId,
                         requestedSession.vehicle,
                         requestedSession.learning,
                         out initializationError)))
                {
                    UnityEngine.Debug.LogError(
                        $"[SimulationRunCoordinator] {initializationError ?? "Initialization requires a source run."}");
                    SetState(RunState.Failed);
                    return;
                }

                // We are training
                CommunicatorFactory.Enabled = true;

                string runId = requestedSession.environment.runId;
                const string torchDevice = "cuda";
                TrainingEnvironmentProvenance trainingEnvironment = TrainingProcessLauncher.ProbeEnvironment(condaEnvName);
                if (!trainingEnvironment.probeSucceeded)
                    UnityEngine.Debug.LogWarning(
                        $"[SimulationRunCoordinator] Trainer provenance probe failed: {trainingEnvironment.probeError}");
                SimulationSessionSnapshot snapshot = SimulationRunService.SaveTrainingLaunch(
                    requestedSession,
                    request,
                    trainingEnvironment,
                    torchDevice);

                try {
                    manager.PrepareRuntime(snapshot);
                    TelemetryLogger.Instance?.Initialize(manager.telemetryConfig, runId);
                    EnsureTrainingController();
                    _training.Start(new TrainingLaunchRequest
                    {
                        condaEnvName = condaEnvName,
                        runId = runId,
                        mlConfigFilePath = System.IO.Path.Combine(
                            SimulationRunService.RunRoot(runId),
                            "TrainingConfig.yaml"),
                        resumeIfExists = request.Mode == RunLaunchMode.ResumeTraining,
                        initializeFromRunId = request.Mode == RunLaunchMode.InitializeTraining
                            ? request.SourceRunId
                            : string.Empty,
                        torchDevice = torchDevice,
                        trainerSeed = requestedSession.learning.trainerSeed
                    }, manager);
                    SetState(RunState.Launching);
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogError($"Launch Failed: {e.Message}");
                    manager.CancelPreparedRuntime();
                    TelemetryLogger.Instance?.DisableLogging();
                    SetState(RunState.Failed);
                }
            }
            else
            {
                CommunicatorFactory.Enabled = false;
                string runId = requestedSession.environment.runId;
                if (requestedSession.environment.IsStandardEvaluation && !TelemetryLogger.Instance)
                {
                    // The evaluator advances from completed telemetry outcomes;
                    // starting without its logger would otherwise run forever.
                    UnityEngine.Debug.LogError(
                        "[Evaluator] Cannot start standard evaluation because no TelemetryLogger exists in the scene.");
                    SetState(RunState.Failed);
                    return;
                }
                int revision = SimulationSessionStore.TryLoadManifest(
                    runId, out SimulationRunManifest manifest) ? manifest.currentRevision : 1;
                if (!SimulationSessionSnapshotFactory.TryCreate(
                        requestedSession,
                        Math.Max(1, revision),
                        out SimulationSessionSnapshot snapshot,
                        out SessionValidationResult snapshotValidation))
                {
                    LogSessionValidation(snapshotValidation);
                    SetState(RunState.Failed);
                    return;
                }
                manager.PrepareRuntime(snapshot);
                TelemetryLogger.Instance?.InitializeEvaluation(manager.telemetryConfig, runId);

                if (manager.envConfig.IsStandardEvaluation)
                {
                    EvaluationConfig evaluation = manager.envConfig.EnsureEvaluationConfig();
                    EnsureEvaluationController();
                    _evaluation.Start(
                        manager,
                        runId,
                        evaluation,
                        TelemetryLogger.Instance?.EpisodeFilePath,
                        manager.envConfig.scenario);
                }

                if (!manager.SpawnAreas())
                {
                    _evaluation?.Cancel(writePartialSummary: false);
                    manager.CancelPreparedRuntime();
                    TelemetryLogger.Instance?.DisableLogging();
                    SetState(RunState.Failed);
                    return;
                }

                SetState(RunState.Connected);
            }
        }

        /// <summary>
        /// Enforces the same objective validation for UI, scripted, and
        /// headless launches. Warnings remain advisory so deliberate zero-value
        /// experiments are possible; errors stop before files or processes are
        /// created.
        /// </summary>
        static bool TryValidateSession(SimulationSessionConfig session)
        {
            SessionValidationResult validation = SimulationSessionValidator.Validate(session);
            LogSessionValidation(validation);
            return validation.IsValid;
        }

        static void LogSessionValidation(SessionValidationResult validation)
        {
            if (validation == null) return;
            for (int i = 0; i < validation.Issues.Count; i++)
            {
                SessionValidationIssue issue = validation.Issues[i];
                string message = $"[SimulationRunCoordinator] {issue.Section}/{issue.Code}: {issue.Message}";
                if (issue.Severity == SessionValidationSeverity.Error)
                    UnityEngine.Debug.LogError(message);
                else
                    UnityEngine.Debug.LogWarning(message);
            }
        }

        /// <summary>Freezes telemetry columns that depend on selected hardware.</summary>
        static void PrepareDerivedTelemetry(SimulationSessionConfig session)
        {
            session.telemetry.activeEngineCount = session.vehicle.GetActiveEngineCount();
            session.telemetry.independentEngines = session.vehicle.independentEngines;
            session.telemetry.activeFinCount = session.vehicle.GetFinCount();
            session.telemetry.scenario = session.environment.scenario;
        }

        /// <summary>
        /// Stops the external trainer process if it is still running.
        /// </summary>
        public void StopRun()
        {
            _evaluation?.Cancel(writePartialSummary: true);

            _training?.Stop();
            SetState(RunState.Idle);
        }

        void EnsureTrainingController()
        {
            if (_training != null) return;
            _training = new TrainingRunController(this);
            _training.Connected += OnTrainingConnected;
            _training.Failed += OnTrainingFailed;
        }

        void OnTrainingConnected() => SetState(RunState.Connected);

        void OnTrainingFailed(string message)
        {
            UnityEngine.Debug.LogError($"[SimulationRunCoordinator] {message}");
            TelemetryLogger.Instance?.DisableLogging();
            SetState(RunState.Failed);
        }

        void EnsureEvaluationController()
        {
            if (_evaluation != null) return;
            _evaluation = new EvaluationRunController(this);
            _evaluation.Completed += OnEvaluationCompleted;
        }

        void OnEvaluationCompleted(string _) => SetState(RunState.Completed);

        /// <summary>
        /// Stops the external trainer process when this launcher or scene is destroyed.
        /// </summary>
        void OnDestroy()
        {
            StopRun();
            if (_training != null)
            {
                _training.Connected -= OnTrainingConnected;
                _training.Failed -= OnTrainingFailed;
            }
            if (_evaluation != null)
                _evaluation.Completed -= OnEvaluationCompleted;
        }

        /// <summary>
        /// Updates launcher state and notifies the panel so buttons, locking,
        /// and validation messages immediately reflect the new run state.
        /// </summary>
        void SetState(RunState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
