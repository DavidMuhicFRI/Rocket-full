using System;
using System.Globalization;

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
        public float lambd = 0.95f;
        public int numEpoch = 6;
        public LRSchedule lrSchedule = LRSchedule.Linear;

        public bool normalize = true;
        public int hiddenUnits = 512;
        public int numLayers = 3;

        public float extrinsicGamma = 0.99f;
        public float extrinsicStrength = 1.0f;
        public bool curiosityEnabled = true;
        public float curiosityGamma = 0.99f;
        public float curiosityStrength = 0.01f;
        public float curiosityLR = 1e-4f;

        public int maxSteps = 10_000_000;
        public int timeHorizon = 512;
        public int summaryFreq = 20_000;
        public bool threaded = true;

        public string ToYAML()
        {
            var ci = CultureInfo.InvariantCulture;
            return $@"behaviors:
  {behaviorName}:
    trainer_type: {trainerType.ToString().ToLower()}
    hyperparameters:
      batch_size: {batchSize}
      buffer_size: {bufferSize}
      learning_rate: {learningRate.ToString(ci):G3}
      beta: {beta.ToString(ci):G3}
      epsilon: {epsilon.ToString(ci)}
      lambd: {lambd.ToString(ci)}
      num_epoch: {numEpoch}
      learning_rate_schedule: {lrSchedule.ToString().ToLower()}
    network_settings:
      normalize: {normalize.ToString().ToLower()}
      hidden_units: {hiddenUnits}
      num_layers: {numLayers}
      vis_encode_type: simple
    reward_signals:
      extrinsic:
        gamma: {extrinsicGamma.ToString(ci)}
        strength: {extrinsicStrength.ToString(ci)}
      {(curiosityEnabled ?
          $"curiosity:\n        gamma: {curiosityGamma.ToString(ci)}\n        strength: {curiosityStrength.ToString(ci):G3}\n        learning_rate: {curiosityLR.ToString(ci):G3}" : "")}
    max_steps: {maxSteps}
    time_horizon: {timeHorizon}
    summary_freq: {summaryFreq}
    threaded: {threaded.ToString().ToLower()}";
        }
    }
}
