// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Rewards.cs
// Purpose: Connects pure reward evaluation to ML-Agents reward and termination.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Follows the complete reward flow for one physics step: measure,
        /// evaluate, integrate rates, apply events, then process termination.
        /// </summary>
        void CalculateRewards()
        {
            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            RewardRuntimeContext context = BuildRewardRuntimeContext();
            ScenarioObjectiveConfig objective = envConfig.GetTrainingObjective(envConfig.scenario);

            RewardDecision decision = RocketRewardModel.Evaluate(
                envConfig.scenario,
                terms,
                context,
                objective,
                _rewardContributions);

            ApplyRewardDecision(decision);

            if (!decision.endEpisode) return;

            _episodeTerminationReason = decision.terminationReason;
            LogLandingEpisodeEnd(decision.terminationReason, terms, context, decision.terminalReward);
            EndEpisode();
        }

        /// <summary>Applies rate, event, and terminal values at their correct cadence.</summary>
        void ApplyRewardDecision(RewardDecision decision)
        {
            // A shaping rate is integrated over simulated time. Event and
            // terminal values are already one-off values and are not scaled.
            AddReward(decision.shapingRate * Time.fixedDeltaTime);
            if (decision.eventReward != 0f)
                AddReward(decision.eventReward);
            if (decision.hasTerminalReward)
                AddReward(decision.terminalReward);

            if (!decision.successTerminal) return;

            _objectiveSuccessTerminalReached = true;
            if (envConfig.scenario.IsLanding())
                _landingEpisodeSucceeded = true;
        }

        /// <summary>
        /// Adds an ML-Agents reward and the identical value to step telemetry.
        /// </summary>
        public new void AddReward(float reward)
        {
            _stepReward += reward;
            base.AddReward(reward);
        }

        /// <summary>Builds the measurement-only context consumed by reward models.</summary>
        RewardRuntimeContext BuildRewardRuntimeContext()
        {
            float terminalAltitude = envConfig.scenario.IsLanding()
                ? ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y
                : ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig);
            return new RewardRuntimeContext(
                altitude: ScenarioReferenceLocalPosition().y,
                terminalAltitude: terminalAltitude,
                episodeStartAltitude: _landingEpisodeStartAltitude,
                gravityMagnitude: Mathf.Abs(Physics.gravity.y),
                episodeElapsedSeconds: _episodeElapsedSeconds,
                curriculumDifficulty01: _objectiveDifficulty01,
                fuelKg: fuel,
                landingPlatformInsideCapture: _landingPlatformInsideCapture,
                landingPlatformStable: _landingPlatformStable,
                landingPlatformBecameStable: _landingPlatformBecameStable,
                landingPlatformStableTime: _landingPlatformStableTime,
                engineRestartsThisStep: _engineRestartsThisStep,
                hoverTrackTargetCapturedThisStep: _hoverTrackTargetReachedThisStep,
                hoverTrackEpisodeCaptures: _hoverTrackEpisodeCaptures,
                legTouchdownStarted: _legLanding.TouchdownStarted,
                legFirstContactThisStep: _legLanding.FirstContactThisStep,
                legFeetOnPad: LegLandingContactEvaluator.CountFeet(_legLanding.FootMask),
                legFootOutsidePad: _legLanding.FootOutsidePad,
                legStructuralStrike: _legLanding.StructuralStrike,
                legStable: _legLanding.Stable,
                legBecameStable: _legLanding.BecameStable,
                legStableTime: _legLanding.StableTime,
                legFirstContactSpeed: _legLanding.FirstContactSpeed,
                legFirstContactVerticalSpeed: _legLanding.FirstContactVerticalSpeed,
                legFirstContactHorizontalSpeed: _legLanding.FirstContactHorizontalSpeed,
                legFirstContactTiltDeg: _legLanding.FirstContactTiltDeg,
                legFirstContactAngularRateDegS: _legLanding.FirstContactAngularRateDegS,
                legExcessiveRebound: _legLanding.ExcessiveRebound);
        }
    }
}
