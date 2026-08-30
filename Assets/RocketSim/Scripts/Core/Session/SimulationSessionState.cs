using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>Identifies which session section changed in the editor.</summary>
    public enum SessionChangeKind
    {
        All,
        Vehicle,
        Task,
        Curriculum,
        Environment,
        Faults,
        Objective,
        Learning,
        Telemetry
    }

    /// <summary>
    /// Owns the mutable configuration shown in the right configuration panel.
    /// Consumers receive copies so the draft cannot leak into an active run.
    /// </summary>
    public sealed class SimulationSessionDraft
    {
        SimulationSessionConfig _config;
        SimulationSessionConfig _beforeImportedSession;

        public SimulationSessionConfig Config => _config;
        public bool CanRestoreBeforeImport => _beforeImportedSession != null;
        public event Action<SessionChangeKind> Changed;

        public SimulationSessionDraft(SimulationSessionConfig initial)
        {
            _config = (initial ?? new SimulationSessionConfig()).DeepCopy();
        }

        /// <summary>Notifies preview and UI subscribers after an in-place edit.</summary>
        public void NotifyChanged(SessionChangeKind kind)
        {
            _config.EnsureSections();
            Changed?.Invoke(kind);
        }

        /// <summary>
        /// Replaces the editable value without treating it as a run import.
        /// This is used for deliberate mode changes and local reset actions.
        /// </summary>
        public void Replace(SimulationSessionConfig replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            _config = replacement.DeepCopy();
            Changed?.Invoke(SessionChangeKind.All);
        }

        /// <summary>
        /// Applies a loaded run while retaining the local draft that existed
        /// before the first import. Loading another run does not erase it.
        /// </summary>
        public void ApplyImportedSession(SimulationSessionConfig imported)
        {
            if (imported == null) throw new ArgumentNullException(nameof(imported));
            _beforeImportedSession ??= _config.DeepCopy();
            _config = imported.DeepCopy();
            Changed?.Invoke(SessionChangeKind.All);
        }

        /// <summary>Restores the draft saved before a run was applied.</summary>
        public bool RestoreBeforeImport()
        {
            if (_beforeImportedSession == null) return false;
            _config = _beforeImportedSession;
            _beforeImportedSession = null;
            Changed?.Invoke(SessionChangeKind.All);
            return true;
        }

        /// <summary>Accepts the current draft as the new local baseline.</summary>
        public void CommitImport()
        {
            _beforeImportedSession = null;
        }
    }

    /// <summary>
    /// Immutable launch input. Its configuration is a private deep copy and a
    /// second copy is returned to each runtime consumer.
    /// </summary>
    public sealed class SimulationSessionSnapshot
    {
        readonly SimulationSessionConfig _config;

        public int Revision { get; }
        public string Sha256 { get; }
        public string CreatedUtc { get; }

        internal SimulationSessionSnapshot(
            SimulationSessionConfig config,
            int revision,
            string sha256,
            string createdUtc)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Revision = revision;
            Sha256 = sha256;
            CreatedUtc = createdUtc;
        }

        public SimulationSessionConfig CreateRuntimeConfig() => _config.DeepCopy();
        internal SimulationSessionConfig CreatePersistenceCopy() => _config.DeepCopy();
    }

    /// <summary>Mutable progress saved separately from the launch configuration.</summary>
    [Serializable]
    public sealed class RunRuntimeState
    {
        public int schemaVersion = SimulationSessionConfig.CurrentSchemaVersion;
        public int completedEpisodes;
        public float landingCurriculumProgress;
        public float landingCurriculumPeakProgress;
        public int landingCurriculumAttempts;
        public int landingCurriculumSuccesses;
        public float legLandingCurriculumProgress;
        public float legLandingCurriculumPeakProgress;
        public int legLandingCurriculumAttempts;
        public int legLandingCurriculumSuccesses;
        public bool legLandingCurriculumDetailsCaptured;
        public float legLandingCurriculumLinearProgress;
        public float legLandingCurriculumRecentSuccessRate;
        public int legLandingCurriculumEpisodeCount;
        public int legLandingCurriculumSuccessfulEpisodes;
        public float hoverTrackingCurriculumProgress;
        public bool hoverTrackingCurriculumDetailsCaptured;
        // False for runtime-state files written while hover progression used
        // terminal episodes rather than bounded target attempts.
        public bool hoverTrackingCurriculumUsesTargetAttempts;
        public float hoverTrackingCurriculumLinearProgress;
        public float hoverTrackingCurriculumRecentSuccessRate;
        public int hoverTrackingCurriculumAttempts;
        public int hoverTrackingCurriculumSuccessfulEpisodes;
        public int hoverTrackingCurriculumSuccesses;

        public static RunRuntimeState Capture(SimEnvironmentConfig environment, int completedEpisodes = 0)
        {
            if (environment == null) return new RunRuntimeState();
            return new RunRuntimeState
            {
                completedEpisodes = Math.Max(0, completedEpisodes),
                landingCurriculumProgress = environment.landingCurriculumProgress,
                landingCurriculumPeakProgress = environment.landingCurriculumPeakLinearProgress,
                landingCurriculumAttempts = environment.landingCurriculumBatchEpisodeCount,
                landingCurriculumSuccesses = environment.landingCurriculumSuccesses,
                legLandingCurriculumProgress = environment.legLandingCurriculumProgress,
                legLandingCurriculumPeakProgress = environment.legLandingCurriculumPeakLinearProgress,
                legLandingCurriculumAttempts = environment.legLandingCurriculumBatchEpisodeCount,
                legLandingCurriculumSuccesses = environment.legLandingCurriculumSuccesses,
                legLandingCurriculumDetailsCaptured = true,
                legLandingCurriculumLinearProgress = environment.legLandingCurriculumLinearProgress,
                legLandingCurriculumRecentSuccessRate = environment.legLandingCurriculumRecentSuccessRate,
                legLandingCurriculumEpisodeCount = environment.legLandingCurriculumEpisodeCount,
                legLandingCurriculumSuccessfulEpisodes = environment.legLandingCurriculumSuccessfulEpisodes,
                hoverTrackingCurriculumProgress = environment.hoverTrackCurriculumProgress,
                hoverTrackingCurriculumDetailsCaptured = true,
                hoverTrackingCurriculumUsesTargetAttempts = true,
                hoverTrackingCurriculumLinearProgress = environment.hoverTrackCurriculumLinearProgress,
                hoverTrackingCurriculumRecentSuccessRate = environment.hoverTrackCurriculumRecentSuccessRate,
                hoverTrackingCurriculumAttempts = environment.hoverTrackCurriculumEpisodeCount,
                hoverTrackingCurriculumSuccessfulEpisodes = environment.hoverTrackCurriculumSuccessfulEpisodes,
                hoverTrackingCurriculumSuccesses = environment.hoverTrackCurriculumSuccesses
            };
        }

        public void ApplyTo(SimEnvironmentConfig environment)
        {
            if (environment == null) return;
            environment.landingCurriculumProgress = landingCurriculumProgress;
            environment.landingCurriculumPeakLinearProgress = landingCurriculumPeakProgress;
            environment.landingCurriculumBatchEpisodeCount = landingCurriculumAttempts;
            environment.landingCurriculumSuccesses = landingCurriculumSuccesses;
            environment.legLandingCurriculumProgress = legLandingCurriculumProgress;
            environment.legLandingCurriculumPeakLinearProgress = legLandingCurriculumPeakProgress;
            environment.legLandingCurriculumBatchEpisodeCount = legLandingCurriculumAttempts;
            environment.legLandingCurriculumSuccesses = legLandingCurriculumSuccesses;
            if (legLandingCurriculumDetailsCaptured)
            {
                environment.legLandingCurriculumLinearProgress =
                    Mathf.Clamp01(legLandingCurriculumLinearProgress);
                environment.legLandingCurriculumRecentSuccessRate =
                    Mathf.Clamp01(legLandingCurriculumRecentSuccessRate);
                environment.legLandingCurriculumEpisodeCount =
                    Math.Max(0, legLandingCurriculumEpisodeCount);
                environment.legLandingCurriculumSuccessfulEpisodes = Mathf.Clamp(
                    legLandingCurriculumSuccessfulEpisodes,
                    0,
                    environment.legLandingCurriculumEpisodeCount);
            }
            else
            {
                // Older runtime files stored only the derived SmoothStep value.
                // Preserve achieved difficulty, then rebuild a fresh EMA window.
                environment.legLandingCurriculumLinearProgress =
                    InverseSmoothStep01(legLandingCurriculumProgress);
                environment.legLandingCurriculumRecentSuccessRate = 0f;
                environment.legLandingCurriculumEpisodeCount = 0;
                environment.legLandingCurriculumSuccessfulEpisodes = 0;
            }
            environment.hoverTrackCurriculumProgress = hoverTrackingCurriculumProgress;
            environment.hoverTrackCurriculumSuccesses = Math.Max(0, hoverTrackingCurriculumSuccesses);
            // Preserve achieved difficulty, but do not mix legacy episode
            // outcomes into the new target-attempt success window.
            environment.hoverTrackCurriculumEpisodeCount = hoverTrackingCurriculumUsesTargetAttempts
                ? Math.Max(0, hoverTrackingCurriculumAttempts)
                : 0;

            if (hoverTrackingCurriculumDetailsCaptured)
            {
                environment.hoverTrackCurriculumLinearProgress =
                    Mathf.Clamp01(hoverTrackingCurriculumLinearProgress);
                environment.hoverTrackCurriculumRecentSuccessRate = hoverTrackingCurriculumUsesTargetAttempts
                    ? Mathf.Clamp01(hoverTrackingCurriculumRecentSuccessRate)
                    : 0f;
                environment.hoverTrackCurriculumSuccessfulEpisodes = hoverTrackingCurriculumUsesTargetAttempts
                    ? Mathf.Clamp(
                        hoverTrackingCurriculumSuccessfulEpisodes,
                        0,
                        environment.hoverTrackCurriculumEpisodeCount)
                    : 0;
                return;
            }

            // RuntimeState files written before the detailed hover fields can
            // still resume without collapsing their saved derived progress.
            environment.hoverTrackCurriculumLinearProgress =
                InverseSmoothStep01(hoverTrackingCurriculumProgress);
            environment.hoverTrackCurriculumSuccessfulEpisodes = 0;
            environment.hoverTrackCurriculumRecentSuccessRate = 0f;
        }

        static float InverseSmoothStep01(float value)
        {
            float target = Mathf.Clamp01(value);
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 16; i++)
            {
                float midpoint = (low + high) * 0.5f;
                if (Mathf.SmoothStep(0f, 1f, midpoint) < target)
                    low = midpoint;
                else
                    high = midpoint;
            }
            return (low + high) * 0.5f;
        }
    }

    public enum RunLaunchMode
    {
        NewTraining,
        ResumeTraining,
        InitializeTraining,
        Evaluation,
        ManualInference
    }

    /// <summary>Transient command describing how a frozen session is launched.</summary>
    public sealed class RunLaunchRequest
    {
        public string RunId { get; }
        public RunLaunchMode Mode { get; }
        public string SourceRunId { get; }

        public RunLaunchRequest(string runId, RunLaunchMode mode, string sourceRunId = null)
        {
            RunId = runId?.Trim();
            Mode = mode;
            SourceRunId = sourceRunId?.Trim() ?? string.Empty;
        }
    }
}
