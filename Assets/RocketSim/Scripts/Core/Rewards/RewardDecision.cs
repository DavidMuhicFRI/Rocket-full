// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardDecision.cs
// Purpose: Carries one reward-model result back to FalconAgent: step shaping,
// optional terminal reward, whether to end, success, and termination reason.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>
    /// Immutable result of evaluating one scenario for one physics step. Factory
    /// methods make terminal and non-terminal outcomes explicit at call sites.
    /// </summary>
    public readonly struct RewardDecision
    {
        public readonly float shapingReward;
        public readonly bool endEpisode;
        public readonly bool hasTerminalReward;
        public readonly float terminalReward;
        public readonly bool successTerminal;
        public readonly EpisodeTerminationReason terminationReason;

        RewardDecision(
            float shapingReward,
            bool endEpisode,
            bool hasTerminalReward,
            float terminalReward,
            bool successTerminal,
            EpisodeTerminationReason terminationReason)
        {
            this.shapingReward = shapingReward;
            this.endEpisode = endEpisode;
            this.hasTerminalReward = hasTerminalReward;
            this.terminalReward = terminalReward;
            this.successTerminal = successTerminal;
            this.terminationReason = terminationReason;
        }

        /// <summary>
        /// Represents no shaping reward and no terminal action for scenarios
        /// that do not produce a decision this step.
        /// </summary>
        public static RewardDecision None => new(0f, false, false, 0f, false, EpisodeTerminationReason.None);
        /// <summary>
        /// Returns a non-terminal shaping reward for the current step.
        /// </summary>
        public static RewardDecision Continue(float shapingReward) =>
            new(shapingReward, false, false, 0f, false, EpisodeTerminationReason.None);
        /// <summary>
        /// Returns a decision that ends the episode with both step shaping and
        /// terminal reward metadata.
        /// </summary>
        public static RewardDecision Terminate(
            float shapingReward,
            float terminalReward,
            bool successTerminal = false,
            EpisodeTerminationReason terminationReason = EpisodeTerminationReason.None) =>
            new(shapingReward, true, true, terminalReward, successTerminal, terminationReason);
    }

    public enum EpisodeTerminationReason
    {
        None,
        LandingUnsafeAttitude,
        LandingTooFarFromTarget,
        LandingFuelDepleted,
        LandingAboveAltitudeLimit,
        LandingSuccessfulTouchdown,
        LandingFailedTouchdown,
        LandingExternalEndRequest,
        LandingMaxStepOrExternalReset
    }

}
