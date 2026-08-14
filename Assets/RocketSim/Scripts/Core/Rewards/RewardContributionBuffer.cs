// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardContributionBuffer.cs
// Purpose: Records the signed contribution made by every configured reward
// parameter without allocating while the physics loop is running.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Reusable diagnostics storage for one reward evaluation. Callers create
    /// one buffer per agent and pass it to <see cref="RocketRewardModel.Evaluate"/>.
    /// Rate entries are expressed in reward per simulated second; event and
    /// terminal entries are one-off reward values. The parameter catalog tells
    /// consumers which cadence applies to each entry.
    /// </summary>
    public sealed class RewardContributionBuffer
    {
        readonly float[] _rawFeatures = new float[RewardParameterCatalog.ParameterCount];
        readonly float[] _signedCoefficients = new float[RewardParameterCatalog.ParameterCount];
        readonly float[] _signedContributions = new float[RewardParameterCatalog.ParameterCount];

        /// <summary>Total signed shaping rate recorded during this evaluation.</summary>
        public float shapingRate { get; private set; }

        /// <summary>Total signed one-off event reward recorded during this evaluation.</summary>
        public float eventReward { get; private set; }

        /// <summary>Total signed terminal reward recorded during this evaluation.</summary>
        public float terminalReward { get; private set; }

        /// <summary>Returns the signed contribution for a stable parameter ID.</summary>
        public float this[RewardParameterId id] => GetSignedContribution(id);

        /// <summary>
        /// Returns the unweighted feature used by the equation. For events this
        /// is the event count; for a terminal outcome it is one.
        /// </summary>
        public float GetRawFeature(RewardParameterId id) => _rawFeatures[(int)id];

        /// <summary>
        /// Returns the semantic coefficient: positive for rewards and negative
        /// for costs, even though serialized cost magnitudes are nonnegative.
        /// </summary>
        public float GetSignedCoefficient(RewardParameterId id) =>
            _signedCoefficients[(int)id];

        /// <summary>Returns raw feature multiplied by the signed coefficient.</summary>
        public float GetSignedContribution(RewardParameterId id) =>
            _signedContributions[(int)id];

        /// <summary>Clears the buffer before a new physics-step evaluation.</summary>
        public void Clear()
        {
            Array.Clear(_rawFeatures, 0, _rawFeatures.Length);
            Array.Clear(_signedCoefficients, 0, _signedCoefficients.Length);
            Array.Clear(_signedContributions, 0, _signedContributions.Length);
            shapingRate = 0f;
            eventReward = 0f;
            terminalReward = 0f;
        }

        /// <summary>
        /// Clears step values and snapshots every applicable semantic
        /// coefficient. A simple indexed catalog loop avoids iterator
        /// allocations and ensures dormant events/terminals remain inspectable.
        /// </summary>
        internal void BeginEvaluation(ScenarioType scenario, RewardParameters parameters)
        {
            Clear();
            var descriptors = RewardParameterCatalog.All;
            for (int i = 0; i < descriptors.Count; i++)
            {
                RewardParameterDescriptor descriptor = descriptors[i];
                if (!descriptor.AppliesTo(scenario))
                    continue;

                float magnitude = descriptor.GetValue(parameters);
                _signedCoefficients[(int)descriptor.id] =
                    descriptor.sign == RewardParameterSign.Cost ? -magnitude : magnitude;
            }
        }

        internal void AddRate(
            RewardParameterId id,
            float rawFeature,
            float signedCoefficient,
            float signedContribution)
        {
            Record(id, rawFeature, signedCoefficient, signedContribution);
            shapingRate += signedContribution;
        }

        internal void AddEvent(
            RewardParameterId id,
            float rawFeature,
            float signedCoefficient,
            float signedContribution)
        {
            Record(id, rawFeature, signedCoefficient, signedContribution);
            eventReward += signedContribution;
        }

        internal void AddTerminal(
            RewardParameterId id,
            float rawFeature,
            float signedCoefficient,
            float signedContribution)
        {
            Record(id, rawFeature, signedCoefficient, signedContribution);
            terminalReward += signedContribution;
        }

        void Record(
            RewardParameterId id,
            float rawFeature,
            float signedCoefficient,
            float signedContribution)
        {
            int index = (int)id;
            _rawFeatures[index] += rawFeature;
            _signedCoefficients[index] = signedCoefficient;
            _signedContributions[index] += signedContribution;
        }
    }
}
