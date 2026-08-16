using System;

namespace RocketSim
{
    /// <summary>
    /// Read-only entry point for saved sessions. It turns storage failures into
    /// clear load errors and returns fully bound session values to callers.
    /// </summary>
    public sealed class SimulationSessionLoader
    {
        public string[] ListRunIds() => SimulationRunService.ListRunIds();
        public bool HasRun(string runId) => SimulationRunService.HasRun(runId);
        public SavedRunSummary GetSummary(string runId) => SimulationRunService.GetSummary(runId);

        public SimulationSessionConfig Load(string runId, bool includeRuntimeState)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("Run ID cannot be empty.", nameof(runId));

            LoadedSimulationRun loaded = SimulationRunService.Load(runId, includeRuntimeState);
            var session = new SimulationSessionConfig
            {
                vehicle = loaded.partsConfig,
                environment = loaded.envConfig,
                objective = loaded.envConfig.EnsureTrainingObjective(),
                learning = loaded.mlConfig,
                telemetry = loaded.telemetryConfig
            };
            session.EnsureSections();
            SessionValidationResult validation = SimulationSessionValidator.Validate(session);
            if (!validation.IsValid)
                throw new InvalidOperationException($"Run '{runId}' contains an invalid simulation session.");
            return session;
        }
    }
}
