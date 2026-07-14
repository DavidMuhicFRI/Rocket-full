// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.ML.cs
// Purpose: Builds ML-Agents trainer controls for the right-side configuration panel.
// Each control writes directly into MLAgentsConfig; that same object later
// produces the YAML saved with the run and passed to mlagents-learn.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>
        /// Builds ML-Agents trainer, network, reward-signal, and run-setting controls that serialize into YAML.
        /// </summary>
        void BuildMLTab(VisualElement c)
        {
            c.Clear();

            BuildTrainerSection(c);
            BuildHyperparametersSection(c);
            BuildNetworkSection(c);
            BuildExtrinsicRewardSection(c);
            BuildCuriosityRewardSection(c);
            BuildRunSettingsSection(c);
        }

        /// <summary>
        /// Builds the PPO/SAC selector. Changing it rebuilds the tab so only
        /// hyperparameters supported by the selected algorithm remain visible.
        /// </summary>
        void BuildTrainerSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Trainer"));
            var trainerEnum = new EnumField("Type", mlConfig.trainerType);
            trainerEnum.AddToClassList("rs-enum-field");
            trainerEnum.RegisterValueChangedCallback(e =>
            {
                mlConfig.trainerType = (TrainerType)e.newValue;
                BuildMLTab(root);
            });
            root.Add(trainerEnum);
            root.Add(BuildMlDescription("PPO = stable & general. SAC = off-policy, sample-efficient."));
        }

        /// <summary>
        /// Builds shared optimization settings, followed by either SAC-specific
        /// replay/target-network settings or PPO-specific clipping/epoch settings.
        /// </summary>
        void BuildHyperparametersSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Hyperparameters"));

            root.Add(UIHelper.IntSlider("batch_size", mlConfig.batchSize, 32, 8192, v => mlConfig.batchSize = v,
                "Samples used per gradient update. Larger is smoother but slower."));

            root.Add(UIHelper.IntSlider("buffer_size", mlConfig.bufferSize, 512, 131072, v => mlConfig.bufferSize = v,
                "Experiences collected before each policy update. Must be at least batch_size."));

            root.Add(UIHelper.ScientificSlider("learning_rate", mlConfig.learningRate, 1e-5f, 1e-3f, v => mlConfig.learningRate = v,
                "Optimizer step size. Too high can be unstable; too low learns slowly."));

            if (mlConfig.trainerType == TrainerType.SAC)
            {
                root.Add(UIHelper.IntSlider("buffer_init_steps", mlConfig.bufferInitSteps, 1000, 100000, v => mlConfig.bufferInitSteps = v,
                    "Experiences collected before SAC starts updating the policy."));
                root.Add(UIHelper.ScientificSlider("tau", mlConfig.tau, 0.001f, 0.05f, v => mlConfig.tau = v,
                    "Controls how quickly the target network follows the learned network."));
                root.Add(UIHelper.Slider("steps_per_update", mlConfig.stepsPerUpdate, 0.1f, 10f, v => mlConfig.stepsPerUpdate = v,
                    "Sets the average number of SAC updates per environment step.", 1));
                root.Add(UIHelper.ScientificSlider("initial entropy", mlConfig.initialEntropyCoefficient, 0.01f, 10f,
                    v => mlConfig.initialEntropyCoefficient = v,
                    "Sets SAC's initial preference for random exploration."));
                root.Add(UIHelper.Toggle("save replay buffer", mlConfig.saveReplayBuffer, v => mlConfig.saveReplayBuffer = v));
                return;
            }

            root.Add(UIHelper.ScientificSlider("beta (entropy)", mlConfig.beta, 1e-4f, 0.05f, v => mlConfig.beta = v,
                "Entropy bonus weight. Higher means more exploration."));

            root.Add(UIHelper.Slider("epsilon (clip)", mlConfig.epsilon, 0.05f, 0.5f, v => mlConfig.epsilon = v,
                "Limits how much PPO may change the policy in one update."));

            root.Add(UIHelper.Slider("lambd (GAE)", mlConfig.lambd, 0.80f, 0.999f, v => mlConfig.lambd = v,
                "Balances short-term bias against noisy long-term advantage estimates."));

            root.Add(UIHelper.IntSlider("num_epoch", mlConfig.numEpoch, 1, 20, v => mlConfig.numEpoch = v,
                "Full passes over each buffer. Higher values reuse data more."));

            var schedEnum = new EnumField("lr_schedule", mlConfig.lrSchedule);
            schedEnum.AddToClassList("rs-enum-field");
            schedEnum.RegisterValueChangedCallback(e => mlConfig.lrSchedule = (LRSchedule)e.newValue);
            root.Add(schedEnum);
            root.Add(BuildMlDescription("Linear = decays to 0 over training. Constant = fixed throughout."));
        }

        /// <summary>Builds observation normalization and hidden-network size controls.</summary>
        void BuildNetworkSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Network"));

            root.Add(UIHelper.Toggle("normalize", mlConfig.normalize, v => mlConfig.normalize = v));
            root.Add(BuildMlDescription("Normalise observations with running mean/variance. Recommended."));

            root.Add(UIHelper.IntSlider("hidden_units", mlConfig.hiddenUnits, 64, 1024, v => mlConfig.hiddenUnits = v,
                "Neurons per hidden layer. More neurons add capacity and memory cost."));

            root.Add(UIHelper.IntSlider("num_layers", mlConfig.numLayers, 1, 6, v => mlConfig.numLayers = v,
                "Sets network depth; two or three layers are a useful starting point."));
        }

        /// <summary>
        /// Builds ML-Agents' task-reward discount and strength settings. These
        /// scale the reward produced by the simulator's reward models.
        /// </summary>
        void BuildExtrinsicRewardSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Reward - Extrinsic"));

            root.Add(UIHelper.Slider("gamma", mlConfig.extrinsicGamma, 0.80f, 0.9999f, v => mlConfig.extrinsicGamma = v,
                "Discount factor. Higher values make the agent care more about future rewards."));

            root.Add(UIHelper.Slider("strength", mlConfig.extrinsicStrength, 0.10f, 5f, v => mlConfig.extrinsicStrength = v,
                "Scales the task reward before it is combined with curiosity."));
        }

        /// <summary>
        /// Builds the optional intrinsic curiosity signal. Its detailed controls
        /// stay hidden while disabled to keep the normal thesis baseline clear.
        /// </summary>
        void BuildCuriosityRewardSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Reward - Curiosity"));

            root.Add(UIHelper.Toggle("enabled", mlConfig.curiosityEnabled, v => mlConfig.curiosityEnabled = v));
            root.Add(BuildMlDescription("Adds an intrinsic reward for visiting novel states."));

            if (!mlConfig.curiosityEnabled) return;

            root.Add(UIHelper.Slider("gamma", mlConfig.curiosityGamma, 0.80f, 0.9999f, v => mlConfig.curiosityGamma = v,
                "Sets how strongly future curiosity rewards affect the current decision."));

            root.Add(UIHelper.ScientificSlider("strength", mlConfig.curiosityStrength, 0.001f, 0.1f, v => mlConfig.curiosityStrength = v,
                "Scales curiosity reward relative to the task reward."));

            root.Add(UIHelper.ScientificSlider("lr", mlConfig.curiosityLR, 1e-5f, 1e-3f, v => mlConfig.curiosityLR = v,
                "Sets the learning rate of the curiosity prediction models."));
        }

        /// <summary>
        /// Builds training length, rollout, summary, checkpoint, seed, and
        /// threading settings, plus a shortcut for inspecting generated YAML.
        /// </summary>
        void BuildRunSettingsSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Run Settings"));

            root.Add(UIHelper.IntSlider("max_steps (x1k)", mlConfig.maxSteps / 1000, 100, 50000, v => mlConfig.maxSteps = v * 1000,
                "Sets the total environment steps before training ends."));

            root.Add(UIHelper.IntSlider("time_horizon", mlConfig.timeHorizon, 64, 2048, v => mlConfig.timeHorizon = v,
                "Steps collected per agent before estimating the remaining return."));

            root.Add(UIHelper.IntSlider("summary_freq (x1k)", mlConfig.summaryFreq / 1000, 1, 100, v => mlConfig.summaryFreq = v * 1000,
                "Sets how often training statistics are written for TensorBoard."));

            root.Add(UIHelper.IntSlider("checkpoint (x1k)", mlConfig.checkpointInterval / 1000, 10, 5000,
                v => mlConfig.checkpointInterval = v * 1000,
                "Sets the number of steps between saved model checkpoints."));
            root.Add(UIHelper.IntSlider("keep checkpoints", mlConfig.keepCheckpoints, 1, 20, v => mlConfig.keepCheckpoints = v,
                "Sets how many recent checkpoints remain on disk."));
            root.Add(UIHelper.IntSlider("trainer seed", mlConfig.trainerSeed, 0, 100000, v => mlConfig.trainerSeed = v,
                "Sets ML-Agents' random seed for repeatable experiments."));
            root.Add(UIHelper.IntSlider("environment seed", envConfig.environmentSeed, 0, 100000, v => envConfig.environmentSeed = v,
                "Seeds spawn, wind, target, and evaluation samples independently from the trainer."));

            root.Add(UIHelper.Toggle("threaded", mlConfig.threaded, v => mlConfig.threaded = v));
            root.Add(BuildMlDescription("Run inference in a background thread. Helps CPU-heavy environments."));

            root.Add(UIHelper.ActionButton("Copy YAML to Clipboard", () =>
                GUIUtility.systemCopyBuffer = mlConfig.ToYAML()));
        }

        /// <summary>
        /// Creates the small explanatory text shown below ML-Agents controls.
        /// </summary>
        static VisualElement BuildMlDescription(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.color = new StyleColor(new Color(0.42f, 0.50f, 0.62f));
            label.style.marginLeft = 154;
            label.style.marginBottom = 3;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
