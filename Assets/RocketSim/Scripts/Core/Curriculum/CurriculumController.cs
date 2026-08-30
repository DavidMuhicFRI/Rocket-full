// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Curriculum/CurriculumController.cs
// Purpose: Gives the runtime one entry point for applying, resetting, and
// advancing the curriculum selected by the simulation session.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>
    /// Routes runtime events to the selected task curriculum. Task-specific interpolation and progression remain in the matching Tasks file.
    /// </summary>
    public sealed class CurriculumController
    {
        /// <summary>Applies saved progress to derived task values before areas spawn.</summary>
        public void Apply(SimEnvironmentConfig environment)
        {
            if (environment == null) return;

            switch (environment.scenario)
            {
                case ScenarioType.HoverTracking:
                    environment.ApplyHoverTrackCurriculum();
                    break;
                case ScenarioType.ChopstickLanding:
                case ScenarioType.LegLanding:
                    environment.ApplyActiveLandingCurriculum();
                    break;
            }
        }

        /// <summary>Records one terminal episode for a landing curriculum.</summary>
        public void RecordEpisode(SimEnvironmentConfig environment, bool successfulEpisode, bool includeInEstimate, int activeAreaCount)
        {
            if (environment == null) return;

            if (environment.scenario.IsLanding() && includeInEstimate)
                environment.RecordLandingCurriculumEpisode(successfulEpisode, activeAreaCount);
        }

        /// <summary>Records an intermediate hover-target capture.</summary>
        public void RecordTargetCapture(SimEnvironmentConfig environment)
        {
            if (environment?.scenario == ScenarioType.HoverTracking)
                environment.hoverTrackCurriculumSuccesses++;
        }

        /// <summary>Records one completed hover-target attempt window.</summary>
        public void RecordHoverTrackAttempt(SimEnvironmentConfig environment, bool successfulAttempt, int activeAreaCount)
        {
            if (environment?.scenario == ScenarioType.HoverTracking)
                environment.RecordHoverTrackCurriculumAttempt(successfulAttempt, activeAreaCount);
        }
    }
}
