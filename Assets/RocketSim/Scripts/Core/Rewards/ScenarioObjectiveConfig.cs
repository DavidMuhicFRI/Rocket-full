// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ScenarioObjectiveConfig.cs
// Purpose: Groups one task's reward, shaping, and termination configuration.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    [Serializable]
    public sealed class ScenarioObjectiveConfig
    {
        public string presetName = RewardPresetCatalog.BalancedPresetName;
        public string basePresetName = RewardPresetCatalog.BalancedPresetName;
        public RewardParameters rewards = new();
        public RewardShapingParameters shaping = new();
        public TerminationParameters terminations = new();

        /// <summary>Builds the complete documented default for one task.</summary>
        public static ScenarioObjectiveConfig CreateDefault(ScenarioType scenario)
        {
            var objective = new ScenarioObjectiveConfig();
            RewardPresetCatalog.Apply(objective, scenario, RewardPresetCatalog.BalancedPresetName);
            RewardShapingDefaults.Apply(objective.shaping, scenario);
            TerminationDefaults.Apply(objective.terminations, scenario);
            return objective;
        }

        /// <summary>Creates missing children without changing intentional values.</summary>
        public void EnsureObjects(ScenarioType scenario)
        {
            rewards ??= new RewardParameters();
            if (shaping == null)
            {
                shaping = new RewardShapingParameters();
                RewardShapingDefaults.Apply(shaping, scenario);
            }

            if (terminations == null)
            {
                terminations = new TerminationParameters();
                TerminationDefaults.Apply(terminations, scenario);
            }

            if (string.IsNullOrWhiteSpace(presetName))
                presetName = "Custom";
            if (string.IsNullOrWhiteSpace(basePresetName))
                basePresetName = RewardPresetCatalog.BalancedPresetName;
        }

        /// <summary>Deep-copies another task objective.</summary>
        public void CopyFrom(ScenarioObjectiveConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            rewards ??= new RewardParameters();
            shaping ??= new RewardShapingParameters();
            terminations ??= new TerminationParameters();
            rewards.CopyFrom(source.rewards);
            shaping.CopyFrom(source.shaping);
            terminations.CopyFrom(source.terminations);
            presetName = source.presetName;
            basePresetName = source.basePresetName;
        }

        /// <summary>Marks the reward vector as no longer matching a preset.</summary>
        public void MarkCustom() => presetName = "Custom";
    }
}
