using System;
using System.Collections.Generic;

namespace RocketSim
{
    public enum SessionValidationSeverity
    {
        Warning,
        Error
    }

    /// <summary>One actionable problem found before a session is launched.</summary>
    public sealed class SessionValidationIssue
    {
        public string Code { get; }
        public string Section { get; }
        public string Message { get; }
        public SessionValidationSeverity Severity { get; }

        public SessionValidationIssue(
            string code,
            string section,
            string message,
            SessionValidationSeverity severity)
        {
            Code = code;
            Section = section;
            Message = message;
            Severity = severity;
        }
    }

    /// <summary>Structured validation output shared by UI and launch workflows.</summary>
    public sealed class SessionValidationResult
    {
        readonly List<SessionValidationIssue> _issues = new();

        public IReadOnlyList<SessionValidationIssue> Issues => _issues;
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < _issues.Count; i++)
                    if (_issues[i].Severity == SessionValidationSeverity.Error)
                        return false;
                return true;
            }
        }

        public void AddError(string code, string section, string message) =>
            _issues.Add(new SessionValidationIssue(code, section, message, SessionValidationSeverity.Error));

        public void AddWarning(string code, string section, string message) =>
            _issues.Add(new SessionValidationIssue(code, section, message, SessionValidationSeverity.Warning));
    }

    /// <summary>
    /// Validates session data without repairing or mutating it. New drafts are
    /// initialized by their factories; malformed saved sessions are rejected.
    /// </summary>
    public static class SimulationSessionValidator
    {
        public static SessionValidationResult Validate(SimulationSessionConfig session)
        {
            var result = new SessionValidationResult();
            if (session == null)
            {
                result.AddError("session.missing", "Session", "The simulation session is missing.");
                return result;
            }

            if (session.schemaVersion != SimulationSessionConfig.CurrentSchemaVersion)
                result.AddError(
                    "session.schema",
                    "Session",
                    $"Session schema {session.schemaVersion} is unsupported. Expected {SimulationSessionConfig.CurrentSchemaVersion}.");

            if (session.vehicle == null) result.AddError("vehicle.missing", "Vehicle", "Vehicle configuration is missing.");
            if (session.environment == null) result.AddError("environment.missing", "Environment", "Environment configuration is missing.");
            if (session.objective == null) result.AddError("objective.missing", "Rewards", "Task objective is missing.");
            if (session.learning == null) result.AddError("learning.missing", "ML", "Learning configuration is missing.");
            if (session.telemetry == null) result.AddError("telemetry.missing", "Telemetry", "Telemetry configuration is missing.");
            if (!result.IsValid) return result;

            ValidateVehicle(session.vehicle, session.environment, result);
            ValidateLearning(session.learning, result);
            ValidateEnvironment(session.environment, result);
            ValidateObjective(session.environment.scenario, session.objective, result);
            ValidateTelemetry(session.telemetry, result);
            return result;
        }

        static void ValidateVehicle(
            RocketPartsConfig vehicle,
            SimEnvironmentConfig environment,
            SessionValidationResult result)
        {
            if (vehicle.bodyRadius <= 0f || vehicle.bodyHeight <= 0f || vehicle.baseDryMass <= 0f)
                result.AddError("vehicle.dimensions", "Vehicle", "Body dimensions and dry mass must be positive.");
            if (vehicle.GetActiveEngineCount() <= 0 || vehicle.maxThrustPerEngine <= 0f)
                result.AddError("vehicle.engines", "Vehicle", "At least one active engine with positive thrust is required.");
            if (vehicle.startFuelMass <= 0f || vehicle.startFuelMass > vehicle.EstimatedMaxFuelCapacity() + 0.01f)
                result.AddError("vehicle.fuel", "Vehicle", "Starting fuel must be positive and fit in the configured tank.");
            if (vehicle.minThrottle < 0f || vehicle.minThrottle > 1f)
                result.AddError("vehicle.throttle", "Vehicle", "Minimum throttle must be between zero and one.");
            if (vehicle.rcsEnabled && vehicle.rcsPropellantMass <= 0f)
                result.AddWarning("vehicle.rcs-propellant", "Vehicle", "RCS is enabled with no RCS propellant.");

            float mass = Math.Max(1f, vehicle.baseDryMass + vehicle.startFuelMass +
                                      (vehicle.rcsEnabled ? vehicle.rcsPropellantMass : 0f));
            float twr = vehicle.GetActiveEngineCount() * vehicle.maxThrustPerEngine / (mass * 9.80665f);
            if (environment.scenario.IsLanding() && twr <= 1f)
                result.AddError("vehicle.landing-twr", "Vehicle", $"Landing maximum thrust-to-weight ratio is {twr:F2}, not above one.");
        }

        static void ValidateLearning(MLAgentsConfig learning, SessionValidationResult result)
        {
            if (learning.batchSize <= 0 || learning.bufferSize < learning.batchSize)
                result.AddError("learning.buffer", "ML", "Buffer size must be at least as large as batch size.");
            if (learning.hiddenUnits <= 0 || learning.numLayers <= 0)
                result.AddError("learning.network", "ML", "Hidden units and network layers must be positive.");
            if (learning.maxSteps <= 0 || learning.timeHorizon <= 0)
                result.AddError("learning.schedule", "ML", "Maximum steps and time horizon must be positive.");
            if (learning.checkpointInterval <= 0 || learning.keepCheckpoints <= 0)
                result.AddError("learning.checkpoints", "ML", "Checkpoint interval and retained checkpoint count must be positive.");
        }

        static void ValidateEnvironment(SimEnvironmentConfig environment, SessionValidationResult result)
        {
            if (!Enum.IsDefined(typeof(ScenarioType), environment.scenario))
                result.AddError("task.unknown", "Task", $"Task value {(int)environment.scenario} is not supported.");
            if (environment.airDensityMultiplier < 0.5f || environment.airDensityMultiplier > 1.5f)
                result.AddError("environment.density", "Environment", "Air-density multiplier must be between 0.5 and 1.5.");
            if (environment.windSpeed < 0f || environment.windGustAmplitude < 0f || environment.gustFrequencyHz < 0f)
                result.AddError("environment.wind", "Environment", "Wind speed, gust amplitude, and gust frequency cannot be negative.");
            if (environment.faults == null)
                result.AddError("faults.missing", "Faults", "Fault configuration is missing.");
        }

        static void ValidateObjective(
            ScenarioType scenario,
            TrainingObjectiveConfig objective,
            SessionValidationResult result)
        {
            ScenarioObjectiveConfig selected;
            try
            {
                objective.EnsureDefaults();
                selected = objective.ForScenario(scenario);
            }
            catch (Exception ex)
            {
                result.AddError("objective.task", "Rewards", ex.Message);
                return;
            }

            ObjectiveValidationResult objectiveResult = ObjectiveValidator.Validate(scenario, selected);
            for (int i = 0; i < objectiveResult.issues.Count; i++)
            {
                ObjectiveValidationIssue issue = objectiveResult.issues[i];
                if (issue.severity == ObjectiveValidationSeverity.Error)
                    result.AddError($"objective.{issue.code}", "Rewards", issue.message);
                else
                    result.AddWarning($"objective.{issue.code}", "Rewards", issue.message);
            }
        }

        static void ValidateTelemetry(TelemetryConfig telemetry, SessionValidationResult result)
        {
            if (telemetry.trainingStepLogInterval < 1 || telemetry.trainingStepLogInterval > 100)
                result.AddError("telemetry.interval", "Telemetry", "Training step interval must be between 1 and 100.");
        }
    }
}
