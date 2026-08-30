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
        /// Follows the complete reward flow for one physics step:
        /// measure, evaluate, integrate rates, apply events, then process termination.
        /// </summary>
        void CalculateRewards()
        {
            RewardTerms terms = MeasureRewardTerms(ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            ScenarioObjectiveConfig objective = envConfig.GetTrainingObjective(envConfig.scenario);
            float centeringProgressRate = MeasureLegLandingCenteringProgressRate(terms, objective.shaping, Time.fixedDeltaTime);
            float footSupportProgress01 = MeasureLegFootSupportProgress01();
            RewardRuntimeContext context = BuildRewardRuntimeContext(centeringProgressRate, footSupportProgress01);

            RewardDecision decision = RocketRewardModel.Evaluate(envConfig.scenario, terms, context, objective, _rewardContributions);

            ApplyRewardDecision(decision);

            if (!decision.endEpisode) return;

            _episodeTerminationReason = decision.terminationReason;
            LogLandingEpisodeEnd(decision.terminationReason, terms, context, decision.terminalReward);
            EndEpisode();
        }

        /// <summary>Applies rate, event, and terminal values at their correct cadence.</summary>
        void ApplyRewardDecision(RewardDecision decision)
        {
            // A shaping rate is integrated over simulated time. Event and terminal values are already one-off values and are not scaled.
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
        RewardRuntimeContext BuildRewardRuntimeContext(
            float legLandingCenteringProgressRate = 0f,
            float legFootSupportProgress01 = 0f)
        {
            float terminalAltitude = envConfig.scenario.IsLanding() ? ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y : ScenarioProfile.TerminalAltitude(envConfig.scenario, envConfig);
            return new RewardRuntimeContext(
                altitude: ScenarioReferenceLocalPosition().y,
                terminalAltitude: terminalAltitude,
                episodeStartAltitude: _landingEpisodeStartAltitude,
                gravityMagnitude: Mathf.Abs(Physics.gravity.y),
                episodeElapsedSeconds: _episodeElapsedSeconds,
                curriculumDifficulty01: _objectiveDifficulty01,
                fuelKg: fuel,
                landingCriteriaDifficulty01: envConfig.scenario.IsLanding() ? ActiveLandingProfile.criteriaDifficulty01 : _objectiveDifficulty01,
                fuelFraction01: fuel / Mathf.Max(cfg.startFuelMass, 1f),
                episodeEngineRestartCount: _episodeEngineRestartCount,
                episodeEngineFirstIgnitionCount: EpisodeEngineFirstIgnitionCount(),
                legLandingCenteringProgressRate: legLandingCenteringProgressRate,
                legFootSupportProgress01: legFootSupportProgress01,
                landingPlatformInsideCapture: _landingPlatformInsideCapture,
                landingPlatformStable: _landingPlatformStable,
                landingPlatformBecameStable: _landingPlatformBecameStable,
                landingPlatformStableTime: _landingPlatformStableTime,
                engineRestartsThisStep: _engineRestartsThisStep,
                hoverTrackTargetCapturedThisStep: _hoverTrackTargetReachedThisStep,
                hoverTrackEpisodeCaptures: _hoverTrackEpisodeCaptures,
                legTouchdownStarted: _legLanding.TouchdownStarted,
                legImpactStarted: _legLanding.ImpactStarted,
                legOffPadImpact: _legLanding.OffPadImpact,
                legFirstContactThisStep: _legLanding.FirstContactThisStep,
                legFeetOnPad: LegLandingContactEvaluator.CountFeet(_legLanding.FootMask),
                legFootOutsidePad: _legLanding.FootOutsidePad,
                legStructuralStrike: _legLanding.StructuralStrike,
                legPropulsionOff: IsLegLandingPropulsionOff(),
                legSupportSettled: _legLanding.SupportSettled,
                legSupportSettledTime: _legLanding.SupportSettledTime,
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

        /// <summary>
        /// Pays only the first improvement to each support level. Tracking the episode maximum prevents contact chatter or bouncing from farming the otherwise useful one-, two-, and three-foot learning signal.
        /// </summary>
        float MeasureLegFootSupportProgress01()
        {
            if (envConfig.scenario != ScenarioType.LegLanding)
                return 0f;

            int currentFeet = LegLandingContactEvaluator.CountFeet(_legLanding.FootMask);
            if (currentFeet <= _maximumRewardedLegSupportFeet)
                return 0f;

            float previousQuality = RocketRewardModel.LegFootSupportQuality01(_maximumRewardedLegSupportFeet);
            _maximumRewardedLegSupportFeet = currentFeet;
            return Mathf.Max(0f, RocketRewardModel.LegFootSupportQuality01(currentFeet) - previousQuality);
        }

        float MeasureLegLandingCenteringProgressRate(
            RewardTerms terms,
            RewardShapingParameters shaping,
            float deltaTime)
        {
            if (envConfig.scenario != ScenarioType.LegLanding)
                return 0f;

            float current = RocketRewardModel.LegLandingCenteringPotential(terms, shaping);
            float progressRate = _legLandingCenteringPotentialInitialized && deltaTime > 0f
                ? (current - _previousLegLandingCenteringPotential) / deltaTime
                : 0f;
            _previousLegLandingCenteringPotential = current;
            _legLandingCenteringPotentialInitialized = true;
            return progressRate;
        }
    }
}
