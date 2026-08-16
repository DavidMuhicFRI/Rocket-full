// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/TrainingObjectiveConfig.cs
// Purpose: Stores one independent objective for each supported task.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Root objective section serialized in a simulation session. It selects a
    /// task objective; the parameter types live in their own focused files.
    /// </summary>
    [Serializable]
    public sealed class TrainingObjectiveConfig
    {
        public ScenarioObjectiveConfig chopstickLanding =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
        public ScenarioObjectiveConfig legLanding =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
        public ScenarioObjectiveConfig hover =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
        public ScenarioObjectiveConfig hoverTracking =
            ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);

        /// <summary>
        /// Recreates only missing task objects. An all-zero objective is valid
        /// and is never treated as uninitialized data.
        /// </summary>
        public void EnsureDefaults()
        {
            chopstickLanding ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
            legLanding ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            hover ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            hoverTracking ??= ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);

            chopstickLanding.EnsureObjects(ScenarioType.ChopstickLanding);
            legLanding.EnsureObjects(ScenarioType.LegLanding);
            hover.EnsureObjects(ScenarioType.Hover);
            hoverTracking.EnsureObjects(ScenarioType.HoverTracking);
        }

        /// <summary>Returns the objective owned by a supported task.</summary>
        public ScenarioObjectiveConfig ForScenario(ScenarioType scenario)
        {
            EnsureDefaults();
            return scenario switch
            {
                ScenarioType.ChopstickLanding => chopstickLanding,
                ScenarioType.LegLanding => legLanding,
                ScenarioType.Hover => hover,
                ScenarioType.HoverTracking => hoverTracking,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario), scenario, "The scenario has no reward objective.")
            };
        }

        /// <summary>Replaces one task's reward magnitudes with a complete preset.</summary>
        public void ApplyPreset(ScenarioType scenario, string presetName) =>
            RewardPresetCatalog.Apply(ForScenario(scenario), scenario, presetName);

        /// <summary>Restores one task's rewards, shaping, and termination rules.</summary>
        public void ResetScenario(ScenarioType scenario) =>
            ForScenario(scenario).CopyFrom(ScenarioObjectiveConfig.CreateDefault(scenario));
    }
}
