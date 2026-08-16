using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Complete user-defined configuration for one simulator launch.
    /// Run identity and live curriculum progress are kept outside this object.
    /// </summary>
    [Serializable]
    public sealed class SimulationSessionConfig
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public RocketPartsConfig vehicle = new();
        public SimEnvironmentConfig environment = new();
        public TrainingObjectiveConfig objective = new();
        public MLAgentsConfig learning = new();
        public TelemetryConfig telemetry = new();

        /// <summary>
        /// Creates missing sections for a new in-memory draft. Loading persisted
        /// sessions does not call this method until the schema has been checked.
        /// </summary>
        public void EnsureSections()
        {
            vehicle ??= new RocketPartsConfig();
            environment ??= new SimEnvironmentConfig();
            objective ??= new TrainingObjectiveConfig();
            learning ??= new MLAgentsConfig();
            telemetry ??= new TelemetryConfig();

            objective.EnsureDefaults();
            // Environment methods still consume the objective while the task
            // and reward runtimes are extracted. This is a runtime reference;
            // the objective is serialized only once at the session root.
            environment.trainingObjective = objective;
        }

        /// <summary>Returns an independent copy safe for editing or runtime use.</summary>
        public SimulationSessionConfig DeepCopy()
        {
            EnsureSections();
            SimulationSessionConfig copy = JsonUtility.FromJson<SimulationSessionConfig>(
                JsonUtility.ToJson(this));
            if (copy == null)
                throw new InvalidOperationException("The simulation session could not be copied.");
            copy.EnsureSections();
            return copy;
        }

        /// <summary>
        /// Builds a session from the simulator's existing configuration objects.
        /// The returned session owns copies, so later UI edits cannot mutate a
        /// frozen launch through a shared reference.
        /// </summary>
        public static SimulationSessionConfig Capture(
            RocketPartsConfig vehicle,
            SimEnvironmentConfig environment,
            MLAgentsConfig learning,
            TelemetryConfig telemetry)
        {
            var source = new SimulationSessionConfig
            {
                vehicle = vehicle ?? new RocketPartsConfig(),
                environment = environment ?? new SimEnvironmentConfig(),
                objective = environment?.EnsureTrainingObjective() ?? new TrainingObjectiveConfig(),
                learning = learning ?? new MLAgentsConfig(),
                telemetry = telemetry ?? new TelemetryConfig()
            };
            return source.DeepCopy();
        }
    }
}
