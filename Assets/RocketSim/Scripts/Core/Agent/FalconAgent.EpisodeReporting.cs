// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.EpisodeReporting.cs
// Purpose: Builds immutable episode summaries and writes end-of-landing diagnostics.
// -----------------------------------------------------------------------------

using Unity.MLAgents;
using UnityEngine;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Copies final episode values before ML-Agents immediately resets the
        /// agent. Telemetry receives this value object instead of reading mutable
        /// agent state after the episode has ended.
        /// </summary>
        TelemetryEpisodeOutcome BuildTelemetryEpisodeOutcome()
        {
            DecisionRequester decisionRequester = GetComponent<DecisionRequester>();
            RewardTerms finalTerms = envConfig != null
                ? MeasureRewardTerms(ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig))
                : default;
            float finalTiltDeg = Mathf.Acos(Mathf.Clamp(finalTerms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            bool success = envConfig != null && envConfig.scenario switch
            {
                ScenarioType.HoverTracking => WasHoverTrackingEpisodeSuccessful(),
                ScenarioType.ChopstickLanding => _landingEpisodeSucceeded,
                ScenarioType.LegLanding => _landingEpisodeSucceeded,
                _ => false
            };
            float globalDifficulty = envConfig == null ? 0f : envConfig.scenario switch
            {
                ScenarioType.HoverTracking => envConfig.hoverTrackCurriculumProgress,
                ScenarioType.ChopstickLanding => envConfig.landingCurriculumProgress,
                ScenarioType.LegLanding => envConfig.legLandingCurriculumProgress,
                _ => 0f
            };
            float episodeDifficulty = envConfig != null && envConfig.scenario.IsLanding()
                ? ActiveLandingProfile.difficulty01
                : globalDifficulty;

            return new TelemetryEpisodeOutcome
            {
                completed = true,
                success = success,
                terminationReason = _episodeTerminationReason,
                environmentSeed = envConfig != null ? envConfig.environmentSeed : 0,
                episodeSeed = _episodeSeed,
                curriculumGlobalDifficulty01 = globalDifficulty,
                curriculumDifficulty01 = episodeDifficulty,
                curriculumReplay = envConfig != null &&
                                   envConfig.scenario.IsLanding() &&
                                   _landingEpisodeUsesEasierReplay,
                scenario = envConfig != null ? envConfig.scenario : ScenarioType.ChopstickLanding,
                landingStartAltitude = envConfig != null && envConfig.scenario.IsLanding()
                    ? _landingEpisodeStartAltitude
                    : 0f,
                landingFlyawayAltitude = envConfig != null && envConfig.scenario.IsLanding()
                    ? _landingEpisodeFlyawayAltitude
                    : 0f,
                initialPlanarDistanceM = _episodeInitialPlanarDistance,
                initialYawErrorDeg = _episodeInitialYawErrorDeg,
                initialSpeedMps = _episodeInitialSpeed,
                initialVerticalSpeedMps = _episodeInitialVerticalSpeed,
                initialHorizontalSpeedMps = _episodeInitialHorizontalSpeed,
                initialTiltDeg = _episodeInitialTiltDeg,
                initialAngularRateDegS = _episodeInitialAngularRateDegS,
                initialFuelKg = _episodeInitialFuelKg,
                initialVehicleMassKg = _episodeInitialVehicleMassKg,
                minimumCommandableNonzeroThrustToWeight = _episodeMinimumCommandableNonzeroThrustToWeight,
                allEnginesMinimumThrustToWeight = _episodeAllEnginesMinimumThrustToWeight,
                allEnginesMaximumThrustToWeight = _episodeAllEnginesMaximumThrustToWeight,
                durationSeconds = _episodeElapsedSeconds,
                fixedDeltaTimeSeconds = Time.fixedDeltaTime,
                decisionPeriod = decisionRequester ? decisionRequester.DecisionPeriod : 1,
                finalPlanarDistanceM = finalTerms.planarDistance,
                finalYawErrorDeg = finalTerms.yawErrorDeg,
                finalSpeedMps = finalTerms.speed,
                finalVerticalSpeedMps = finalTerms.verticalSpeed,
                finalHorizontalSpeedMps = finalTerms.planarSpeed,
                finalTiltDeg = finalTiltDeg,
                finalAngularRateDegS = finalTerms.angularRateDegS,
                fuelUsedKg = Mathf.Max(0f, cfg.startFuelMass - fuel),
                rcsPropellantUsedKg = Mathf.Max(0f, cfg.rcsPropellantMass - rcsPropellant),
                engineRestartCount = _episodeEngineRestartCount,
                legTouchdownOccurred = _legLanding.TouchdownStarted,
                legFeetOnPad = LegLandingContactEvaluator.CountFeet(_legLanding.FootMask),
                legFootOutsidePad = _legLanding.FootOutsidePad,
                legStructuralStrike = _legLanding.StructuralStrike,
                legTouchdownTimeSeconds = _legLanding.TouchdownTime,
                legFirstContactSpeedMps = _legLanding.FirstContactSpeed,
                legFirstContactVerticalSpeedMps = _legLanding.FirstContactVerticalSpeed,
                legFirstContactHorizontalSpeedMps = _legLanding.FirstContactHorizontalSpeed,
                legFirstContactTiltDeg = _legLanding.FirstContactTiltDeg,
                legFirstContactAngularRateDegS = _legLanding.FirstContactAngularRateDegS,
                legStableHoldSeconds = _legLanding.StableTime,
                legMaximumContactImpulseNs = _legLanding.MaximumContactImpulseNs,
                legMaximumReboundHeightM = _legLanding.MaximumReboundHeightM
            };
        }

        /// <summary>
        /// Logs a landing episode end using freshly measured terms when an
        /// external reset or max-step event ends the episode.
        /// </summary>
        void LogLandingEpisodeEnd(EpisodeTerminationReason reason)
        {
            if (_landingEpisodeEndLogged || envConfig == null || !envConfig.scenario.IsLanding())
                return;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            LogLandingEpisodeEnd(reason, terms, BuildRewardRuntimeContext(), float.NaN);
        }

        /// <summary>
        /// Emits a detailed landing diagnostic line once per episode, including
        /// terminal reason, success checks, curriculum progress, and platform state.
        /// </summary>
        void LogLandingEpisodeEnd(
            EpisodeTerminationReason reason,
            RewardTerms terms,
            RewardRuntimeContext context,
            float terminalReward)
        {
            if (_landingEpisodeEndLogged || envConfig == null || !envConfig.scenario.IsLanding())
                return;

            _landingEpisodeEndLogged = true;

            TerminationParameters termination =
                envConfig.GetTrainingObjective(envConfig.scenario).terminations;
            float difficulty = context.curriculumDifficulty01;
            float successRadius = termination.landingSuccessRadiusM.At(difficulty);
            float successMaxTotalSpeed = termination.landingSuccessMaxTotalSpeedMps.At(difficulty);
            float successMaxVerticalSpeed = termination.landingSuccessMaxVerticalSpeedMps.At(difficulty);
            float successMaxHorizontalSpeed = termination.landingSuccessMaxHorizontalSpeedMps.At(difficulty);
            float successMaxTilt = termination.landingSuccessMaxTiltDeg.At(difficulty);
            float successMaxAngularRate = termination.landingSuccessMaxAngularRateDegS.At(difficulty);
            float successMaxYawError = termination.landingSuccessMaxYawErrorDeg.At(difficulty);
            float stableHoldSeconds = termination.landingStableHoldSeconds.At(difficulty);
            float tiltDeg = Mathf.Acos(Mathf.Clamp(terms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            bool isChopstick = envConfig.scenario == ScenarioType.ChopstickLanding;
            bool reachedTargetAltitude = context.altitude <= context.terminalAltitude;
            bool successfulTouchdown = reason == EpisodeTerminationReason.ChopstickSuccessfulCapture ||
                                       reason == EpisodeTerminationReason.LegLandingSuccessfulTouchdown;
            bool uprightPass = tiltDeg <= successMaxTilt;
            bool targetPass = terms.planarDistance <= successRadius;
            bool totalSpeedPass = terms.speed <= successMaxTotalSpeed;
            bool verticalSpeedPass = Mathf.Abs(terms.verticalSpeed) <= successMaxVerticalSpeed;
            bool horizontalSpeedPass = terms.planarSpeed <= successMaxHorizontalSpeed;
            bool angularRatePass = terms.angularRateDegS <= successMaxAngularRate;
            bool headingPass = Mathf.Abs(terms.yawErrorDeg) <= successMaxYawError;
            bool platformPass = !termination.chopstickRequireStablePlatform ||
                                (context.landingPlatformInsideCapture &&
                                 context.landingPlatformStable &&
                                 context.landingPlatformStableTime >= stableHoldSeconds);

            string terminalRewardText = float.IsNaN(terminalReward) ? "n/a" : terminalReward.ToString("F2");
            string touchdownChecks = string.Empty;
            if (reachedTargetAltitude || context.legTouchdownStarted)
            {
                touchdownChecks = isChopstick
                    ? $" checks=[upright:{PassFail(uprightPass)}, target:{PassFail(targetPass)}, " +
                      $"totalSpeed:{PassFail(totalSpeedPass)}, verticalSpeed:{PassFail(verticalSpeedPass)}, " +
                      $"horizontalSpeed:{PassFail(horizontalSpeedPass)}, angularRate:{PassFail(angularRatePass)}, " +
                      $"heading:{PassFail(headingPass)}, platform:{PassFail(platformPass)}]"
                    : $" checks=[upright:{PassFail(uprightPass)}, target:{PassFail(targetPass)}, " +
                      $"totalSpeed:{PassFail(totalSpeedPass)}, verticalSpeed:{PassFail(verticalSpeedPass)}, " +
                      $"horizontalSpeed:{PassFail(horizontalSpeedPass)}, angularRate:{PassFail(angularRatePass)}, " +
                      $"feet:{PassFail(context.legFeetOnPad >= termination.legMinimumStableFeet)}, " +
                      $"insidePad:{PassFail(!context.legFootOutsidePad)}, " +
                      $"noStrike:{PassFail(!context.legStructuralStrike)}, stable:{PassFail(context.legStable)}]";
            }
            string platformStatus = isChopstick
                ? $" chopstick=[enabled:{envConfig.landingPlatformEnabled}, " +
                  $"required:{termination.chopstickRequireStablePlatform}, simulated:true, " +
                  $"inside:{context.landingPlatformInsideCapture}, stable:{context.landingPlatformStable}, " +
                  $"stableTime:{context.landingPlatformStableTime:F2}/{stableHoldSeconds:F2}s, " +
                  $"halfSize:{ActiveLandingProfile.platformHalfSize:F2}m]"
                : $" feet=[onPad:{context.legFeetOnPad}/4, outside:{context.legFootOutsidePad}, " +
                  $"structuralStrike:{context.legStructuralStrike}, stable:{context.legStable}, " +
                  $"stableTime:{context.legStableTime:F2}/{stableHoldSeconds:F2}s]";

            Debug.Log(
                $"[LandingEpisodeEnd] area={_areaIndex} episode={_episode} step={_step} " +
                $"duration={_step * Time.fixedDeltaTime:F2}s reason={reason} success={successfulTouchdown} " +
                $"targetAltitudeReached={reachedTargetAltitude} altitude={context.altitude:F2}m " +
                $"terminalAltitude={context.terminalAltitude:F2}m startAltitude={_landingEpisodeStartAltitude:F2}m " +
                $"flyawayAltitude={_landingEpisodeFlyawayAltitude:F2}m " +
                $"distance3D={terms.distance3D:F2}m planarDistance={terms.planarDistance:F2}m " +
                $"uprightness={terms.upDot:F3} tilt={tiltDeg:F2}deg totalSpeed={terms.speed:F2}m/s " +
                $"verticalSpeed={terms.verticalSpeed:F2}m/s horizontalSpeed={terms.planarSpeed:F2}m/s " +
                $"angularRate={terms.angularRateDegS:F2}deg/s yawError={terms.yawErrorDeg:F2}deg " +
                $"fuel={context.fuelKg:F2}kg " +
                $"terminalReward={terminalRewardText} curriculumEnabled={envConfig.ActiveLandingCurriculumEnabled} " +
                $"curriculumMode={envConfig.ActiveLandingCurriculumMode} " +
                $"globalCurriculum={envConfig.ActiveLandingCurriculumProgress * 100f:F4}% " +
                $"episodeDifficulty={ActiveLandingProfile.difficulty01 * 100f:F4}% " +
                $"easierReplay={_landingEpisodeUsesEasierReplay} " +
                $"curriculumEpisodes={envConfig.ActiveLandingCurriculumEpisodeCount} " +
                $"curriculumSuccesses={envConfig.ActiveLandingCurriculumSuccessfulEpisodes} " +
                $"recentSuccessRate={envConfig.ActiveLandingCurriculumSuccessRate * 100f:F1}% " +
                $"touchdownLimits=[planar<={successRadius:F2}m, " +
                $"totalSpeed<={successMaxTotalSpeed:F2}m/s, " +
                $"absVerticalSpeed<={successMaxVerticalSpeed:F2}m/s, " +
                $"horizontalSpeed<={successMaxHorizontalSpeed:F2}m/s, " +
                $"tilt<={successMaxTilt:F2}deg, " +
                $"angularRate<={successMaxAngularRate:F2}deg/s" +
                (isChopstick ? $", absYawError<={successMaxYawError:F2}deg]" : "]") +
                platformStatus +
                touchdownChecks);
        }

        /// <summary>
        /// Converts an external reset/end into the active task's terminal
        /// category. This keeps telemetry labels consistent for resets and
        /// explicit end requests that do not come from reward evaluation.
        /// </summary>
        EpisodeTerminationReason DefaultExternalTerminationReason(bool maxStepOrReset)
        {
            if (envConfig != null && envConfig.scenario == ScenarioType.LegLanding)
                return maxStepOrReset
                    ? EpisodeTerminationReason.LegLandingMaxStepOrExternalReset
                    : EpisodeTerminationReason.LegLandingExternalEndRequest;
            if (envConfig != null && envConfig.scenario == ScenarioType.ChopstickLanding)
                return maxStepOrReset
                    ? EpisodeTerminationReason.ChopstickMaxStepOrExternalReset
                    : EpisodeTerminationReason.ChopstickExternalEndRequest;
            if (envConfig != null &&
                (envConfig.scenario == ScenarioType.Hover || envConfig.scenario == ScenarioType.HoverTracking))
                return maxStepOrReset
                    ? EpisodeTerminationReason.HoverMaxStepOrExternalReset
                    : EpisodeTerminationReason.HoverExternalEndRequest;
            return EpisodeTerminationReason.None;
        }
    }
}
