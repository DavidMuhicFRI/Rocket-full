// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/MLAgentsConfig.cs
// Purpose: Stores ML-Agents trainer settings and converts them to and from the YAML saved with each run.
// Main flow: panel edits this object -> ToYAML writes a run configuration ->
// mlagents-learn consumes it -> TryFromYAML restores it for later inspection.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;

namespace RocketSim
{
    public enum TrainerType
    {
        PPO,
        SAC
    }

    public enum LRSchedule
    {
        Linear,
        Constant
    }

    /// <summary>
    /// ML-Agents trainer configuration that maps directly to the generated YAML
    /// used by TrainingLauncher.
    /// </summary>
    [Serializable]
    public class MLAgentsConfig
    {
        public string behaviorName = "Rocket";
        public TrainerType trainerType = TrainerType.PPO;

        public int batchSize = 4096;
        public int bufferSize = 40960;
        public float learningRate = 2e-4f;
        public float beta = 1e-2f;
        public float epsilon = 0.20f;
        // At the canonical 33.3 Hz decision rate, 0.98 carries advantage
        // information farther through a landing burn without the variance of
        // values extremely close to one.
        public float lambd = 0.98f;
        public int numEpoch = 6;
        public LRSchedule lrSchedule = LRSchedule.Linear;

        // SAC-only settings. They are written only when SAC is selected.
        public int bufferInitSteps = 5000;
        public float tau = 0.005f;
        public float stepsPerUpdate = 1f;
        public float initialEntropyCoefficient = 1f;
        public bool saveReplayBuffer;

        public bool normalize = true;
        public int hiddenUnits = 512;
        public int numLayers = 3;

        // Gamma is applied once per ML-Agents decision, not once per second.
        // 0.995 retains about 19% of a terminal signal across ten simulated
        // seconds at DecisionPeriod=3 and a 0.01 s physics timestep.
        public float extrinsicGamma = 0.995f;
        public float extrinsicStrength = 1.0f;
        // Disabled for the curriculum baseline: novelty rewards depend on the
        // state distribution and would interact with the curriculum treatment.
        public bool curiosityEnabled = false;
        public float curiosityGamma = 0.99f;
        public float curiosityStrength = 0.01f;
        public float curiosityLR = 1e-4f;

        public int maxSteps = 10_000_000;
        // 1024 decisions cover 30.72 simulated seconds at the canonical control
        // rate. Reaching this value bootstraps from the critic; it does not end
        // the Unity episode.
        public int timeHorizon = 1024;
        public int summaryFreq = 20_000;
        public bool threaded = true;
        public int checkpointInterval = 500_000;
        public int keepCheckpoints = 20;
        public int trainerSeed = 1;

        /// <summary>
        /// Serializes the UI-editable trainer settings into the ML-Agents YAML
        /// format consumed by the external training process.
        /// </summary>
        public string ToYAML()
        {
            var ci = CultureInfo.InvariantCulture;
            var yaml = new StringBuilder();
            yaml.AppendLine($"# rocket_sim_trainer_seed: {trainerSeed}");
            yaml.AppendLine("behaviors:");
            yaml.AppendLine($"  {behaviorName}:");
            yaml.AppendLine($"    trainer_type: {trainerType.ToString().ToLower()}");
            yaml.AppendLine("    hyperparameters:");
            yaml.AppendLine($"      batch_size: {batchSize}");
            yaml.AppendLine($"      buffer_size: {bufferSize}");
            yaml.AppendLine($"      learning_rate: {learningRate.ToString("G3", ci)}");
            yaml.AppendLine($"      learning_rate_schedule: {lrSchedule.ToString().ToLower()}");

            if (trainerType == TrainerType.PPO)
            {
                yaml.AppendLine($"      beta: {beta.ToString("G3", ci)}");
                yaml.AppendLine($"      epsilon: {epsilon.ToString(ci)}");
                yaml.AppendLine($"      lambd: {lambd.ToString(ci)}");
                yaml.AppendLine($"      num_epoch: {numEpoch}");
            }
            else
            {
                yaml.AppendLine($"      buffer_init_steps: {bufferInitSteps}");
                yaml.AppendLine($"      tau: {tau.ToString("G3", ci)}");
                yaml.AppendLine($"      steps_per_update: {stepsPerUpdate.ToString("G3", ci)}");
                yaml.AppendLine($"      save_replay_buffer: {saveReplayBuffer.ToString().ToLower()}");
                yaml.AppendLine($"      init_entcoef: {initialEntropyCoefficient.ToString("G3", ci)}");
            }

            yaml.AppendLine("    network_settings:");
            yaml.AppendLine($"      normalize: {normalize.ToString().ToLower()}");
            yaml.AppendLine($"      hidden_units: {hiddenUnits}");
            yaml.AppendLine($"      num_layers: {numLayers}");
            yaml.AppendLine("      vis_encode_type: simple");
            yaml.AppendLine("    reward_signals:");
            yaml.AppendLine("      extrinsic:");
            yaml.AppendLine($"        gamma: {extrinsicGamma.ToString(ci)}");
            yaml.AppendLine($"        strength: {extrinsicStrength.ToString(ci)}");
            if (curiosityEnabled)
            {
                yaml.AppendLine("      curiosity:");
                yaml.AppendLine($"        gamma: {curiosityGamma.ToString(ci)}");
                yaml.AppendLine($"        strength: {curiosityStrength.ToString("G3", ci)}");
                yaml.AppendLine($"        learning_rate: {curiosityLR.ToString("G3", ci)}");
            }

            yaml.AppendLine($"    max_steps: {maxSteps}");
            yaml.AppendLine($"    time_horizon: {timeHorizon}");
            yaml.AppendLine($"    summary_freq: {summaryFreq}");
            yaml.AppendLine($"    checkpoint_interval: {checkpointInterval}");
            yaml.AppendLine($"    keep_checkpoints: {keepCheckpoints}");
            yaml.Append($"    threaded: {threaded.ToString().ToLower()}");
            return yaml.ToString();
        }

        /// <summary>
        /// Parses the ML-Agents YAML produced by ToYAML so saved runs can restore
        /// the same trainer settings in the panel.
        /// </summary>
        public static bool TryFromYAML(string yaml, out MLAgentsConfig config)
        {
            config = new MLAgentsConfig();
            if (string.IsNullOrWhiteSpace(yaml)) return false;
            // Parsing must reflect the saved file, so absence of a curiosity
            // reward signal explicitly restores the disabled state.
            config.curiosityEnabled = false;

            string section = "";
            string rewardSignal = "";
            bool foundBehavior = false;

            // This is intentionally a small reader for YAML created by ToYAML,
            // not a general YAML parser. Indentation tells us which ML-Agents
            // section a key belongs to, especially the two reward signals.
            foreach (string rawLine in yaml.Replace("\r\n", "\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;

                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("# rocket_sim_trainer_seed:", StringComparison.Ordinal))
                {
                    string seedText = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    if (int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                        config.trainerSeed = seed;
                    continue;
                }

                int indent = rawLine.Length - rawLine.TrimStart(' ').Length;
                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.EndsWith(":", StringComparison.Ordinal) && !line.Contains(": "))
                {
                    string name = line[..^1];
                    if (indent == 2 && name != "hyperparameters" && name != "network_settings" && name != "reward_signals")
                    {
                        config.behaviorName = name;
                        foundBehavior = true;
                    }
                    else if (name == "hyperparameters" || name == "network_settings" || name == "reward_signals")
                    {
                        section = name;
                        rewardSignal = "";
                    }
                    else if (section == "reward_signals")
                    {
                        rewardSignal = name;
                    }
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                ApplyYamlValue(config, section, rewardSignal, key, value);
            }

            return foundBehavior;
        }

        /// <summary>
        /// Applies one parsed YAML key to the matching strongly typed field.
        /// Section and reward-signal names disambiguate repeated keys such as
        /// learning_rate, gamma, and strength.
        /// </summary>
        static void ApplyYamlValue(MLAgentsConfig config, string section, string rewardSignal, string key, string value)
        {
            var ci = CultureInfo.InvariantCulture;
            switch (key)
            {
                case "trainer_type":
                    if (Enum.TryParse(value, true, out TrainerType trainerType))
                        config.trainerType = trainerType;
                    break;
                case "batch_size":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int batchSize))
                        config.batchSize = batchSize;
                    break;
                case "buffer_size":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int bufferSize))
                        config.bufferSize = bufferSize;
                    break;
                case "learning_rate":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float learningRate))
                    {
                        if (section == "reward_signals" && rewardSignal == "curiosity")
                            config.curiosityLR = learningRate;
                        else
                            config.learningRate = learningRate;
                    }
                    break;
                case "beta":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float beta))
                        config.beta = beta;
                    break;
                case "epsilon":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float epsilon))
                        config.epsilon = epsilon;
                    break;
                case "lambd":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float lambd))
                        config.lambd = lambd;
                    break;
                case "num_epoch":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int numEpoch))
                        config.numEpoch = numEpoch;
                    break;
                case "buffer_init_steps":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int bufferInitSteps))
                        config.bufferInitSteps = bufferInitSteps;
                    break;
                case "tau":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float tau))
                        config.tau = tau;
                    break;
                case "steps_per_update":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float stepsPerUpdate))
                        config.stepsPerUpdate = stepsPerUpdate;
                    break;
                case "save_replay_buffer":
                    if (bool.TryParse(value, out bool saveReplayBuffer))
                        config.saveReplayBuffer = saveReplayBuffer;
                    break;
                case "init_entcoef":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float initialEntropyCoefficient))
                        config.initialEntropyCoefficient = initialEntropyCoefficient;
                    break;
                case "learning_rate_schedule":
                    if (Enum.TryParse(value, true, out LRSchedule lrSchedule))
                        config.lrSchedule = lrSchedule;
                    break;
                case "normalize":
                    if (bool.TryParse(value, out bool normalize))
                        config.normalize = normalize;
                    break;
                case "hidden_units":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int hiddenUnits))
                        config.hiddenUnits = hiddenUnits;
                    break;
                case "num_layers":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int numLayers))
                        config.numLayers = numLayers;
                    break;
                case "gamma":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float gamma))
                    {
                        if (rewardSignal == "curiosity")
                            config.curiosityGamma = gamma;
                        else
                            config.extrinsicGamma = gamma;
                    }
                    break;
                case "strength":
                    if (float.TryParse(value, NumberStyles.Float, ci, out float strength))
                    {
                        if (rewardSignal == "curiosity")
                        {
                            config.curiosityEnabled = true;
                            config.curiosityStrength = strength;
                        }
                        else
                        {
                            config.extrinsicStrength = strength;
                        }
                    }
                    break;
                case "max_steps":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int maxSteps))
                        config.maxSteps = maxSteps;
                    break;
                case "time_horizon":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int timeHorizon))
                        config.timeHorizon = timeHorizon;
                    break;
                case "summary_freq":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int summaryFreq))
                        config.summaryFreq = summaryFreq;
                    break;
                case "checkpoint_interval":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int checkpointInterval))
                        config.checkpointInterval = checkpointInterval;
                    break;
                case "keep_checkpoints":
                    if (int.TryParse(value, NumberStyles.Integer, ci, out int keepCheckpoints))
                        config.keepCheckpoints = keepCheckpoints;
                    break;
                case "threaded":
                    if (bool.TryParse(value, out bool threaded))
                        config.threaded = threaded;
                    break;
            }
        }
    }
}
