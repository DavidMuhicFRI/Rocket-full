using System;

namespace RocketSim
{
    /// <summary>
    /// Session-facing part of the right configuration panel. The visual tab
    /// files edit these properties, but the manager owns the actual draft.
    /// </summary>
    public partial class RightConfigPanel
    {
        RocketPartsConfig partsConfig
        {
            get => RequireSession().Config.vehicle;
            set => RequireSession().Config.vehicle = value ?? new RocketPartsConfig();
        }

        SimEnvironmentConfig envConfig
        {
            get => RequireSession().Config.environment;
            set
            {
                SimulationSessionConfig session = RequireSession().Config;
                session.environment = value ?? new SimEnvironmentConfig();
                session.environment.trainingObjective = session.objective;
            }
        }

        MLAgentsConfig mlConfig
        {
            get => RequireSession().Config.learning;
            set => RequireSession().Config.learning = value ?? new MLAgentsConfig();
        }

        TelemetryConfig telemetryConfig
        {
            get => RequireSession().Config.telemetry;
            set => RequireSession().Config.telemetry = value ?? new TelemetryConfig();
        }

        /// <summary>Called once by ConfigBridge before the panel builds.</summary>
        public void BindSession(
            SimulationSessionDraft sessionDraft,
            TrainingAreaManager areaManager)
        {
            _sessionDraft = sessionDraft ?? throw new ArgumentNullException(nameof(sessionDraft));
            trainingAreaManager = areaManager ?? throw new ArgumentNullException(nameof(areaManager));
        }

        SimulationSessionDraft RequireSession()
        {
            _sessionDraft ??= new SimulationSessionDraft(new SimulationSessionConfig());
            return _sessionDraft;
        }

        /// <summary>Signals that an in-place UI edit changed the draft.</summary>
        void NotifySessionChanged(SessionChangeKind kind = SessionChangeKind.All)
        {
            RequireSession().NotifyChanged(kind);
        }

        /// <summary>
        /// Restores the local setup retained before a run was previewed. The
        /// caller rebuilds whichever tabs are visible after this succeeds.
        /// </summary>
        bool RestoreSessionBeforeRunImport()
        {
            return RequireSession().RestoreBeforeImport();
        }
    }
}
