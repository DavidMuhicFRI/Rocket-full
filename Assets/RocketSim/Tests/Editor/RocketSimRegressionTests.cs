// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Tests/Editor/RocketSimRegressionTests.cs
// Purpose: Locks the thesis experiment's trainer, landing reward, curriculum,
// feasibility, and evaluator contracts against accidental configuration drift.
// -----------------------------------------------------------------------------

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RocketSim.Tests
{
    public class RocketSimRegressionTests
    {
        const float Epsilon = 0.0001f;

        [Test]
        public void PpoDefaultsMatchDocumentedExperimentContract()
        {
            var config = new MLAgentsConfig();

            Assert.That(config.trainerType, Is.EqualTo(TrainerType.PPO));
            Assert.That(config.extrinsicGamma, Is.EqualTo(0.995f).Within(Epsilon));
            Assert.That(config.lambd, Is.EqualTo(0.98f).Within(Epsilon));
            Assert.That(config.timeHorizon, Is.EqualTo(1024));
            Assert.That(config.curiosityEnabled, Is.False);
            Assert.That(config.keepCheckpoints, Is.EqualTo(20));
            Assert.That(RocketAgentSchema.CanonicalDecisionPeriod, Is.EqualTo(3));
        }

        [Test]
        public void PpoYamlRoundTripPreservesTemporalSettings()
        {
            var source = new MLAgentsConfig();
            string yaml = source.ToYAML();

            Assert.That(MLAgentsConfig.TryFromYAML(yaml, out MLAgentsConfig restored), Is.True);
            Assert.That(restored.extrinsicGamma, Is.EqualTo(0.995f).Within(Epsilon));
            Assert.That(restored.lambd, Is.EqualTo(0.98f).Within(Epsilon));
            Assert.That(restored.timeHorizon, Is.EqualTo(1024));
            Assert.That(restored.curiosityEnabled, Is.False);
            StringAssert.DoesNotContain("curiosity:", yaml);
        }

        [Test]
        public void RootTrainerYamlMatchesCurrentPpoBaseline()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TrainingConfig.yaml"));
            Assert.That(File.Exists(path), Is.True, "The manual CLI reference YAML must exist at the project root.");
            Assert.That(MLAgentsConfig.TryFromYAML(File.ReadAllText(path), out MLAgentsConfig config), Is.True);
            Assert.That(config.trainerType, Is.EqualTo(TrainerType.PPO));
            Assert.That(config.extrinsicGamma, Is.EqualTo(0.995f).Within(Epsilon));
            Assert.That(config.lambd, Is.EqualTo(0.98f).Within(Epsilon));
            Assert.That(config.timeHorizon, Is.EqualTo(1024));
            Assert.That(config.curiosityEnabled, Is.False);
            Assert.That(config.checkpointInterval, Is.EqualTo(500_000));
            Assert.That(config.keepCheckpoints, Is.EqualTo(20));
        }

        [Test]
        public void ConfigSceneIsFirstEnabledBuildScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThan(0));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[0].path, Is.EqualTo("Assets/Scenes/ConfigScene.unity"));
        }

        [Test]
        public void HoverEquilibriumThrottleMatchesWeightAndAvailableThrust()
        {
            Assert.That(ScenarioCatalog.HoverStartAltitude, Is.EqualTo(80f).Within(Epsilon));
            Assert.That(ScenarioCatalog.HoverGoalAltitude, Is.EqualTo(30f).Within(Epsilon));

            float throttle = HoverThrustInitialization.EquilibriumThrottle(
                62350f,
                9.80665f,
                845000f,
                1,
                0.40f);

            Assert.That(throttle, Is.EqualTo(0.7235f).Within(0.001f));
        }

        [Test]
        public void IndependentEngineThrustAuthoritySeparatesOneAndAllEngineMinimums()
        {
            ThrustAuthoritySnapshot independent = ThrustAuthorityMetrics.Calculate(
                vehicleMassKg: 54000f,
                gravityMps2: 9.80665f,
                activeEngineCount: 9,
                maximumThrustPerEngineN: 845000f,
                minimumThrottle01: 0.40f,
                independentEngineControl: true);
            ThrustAuthoritySnapshot grouped = ThrustAuthorityMetrics.Calculate(
                vehicleMassKg: 54000f,
                gravityMps2: 9.80665f,
                activeEngineCount: 9,
                maximumThrustPerEngineN: 845000f,
                minimumThrottle01: 0.40f,
                independentEngineControl: false);

            Assert.That(
                independent.AllActiveEnginesMinimumThrottleTwr,
                Is.EqualTo(independent.MinimumCommandableNonzeroTwr * 9f).Within(Epsilon));
            Assert.That(
                independent.AllActiveEnginesMaximumTwr,
                Is.EqualTo(independent.AllActiveEnginesMinimumThrottleTwr / 0.40f).Within(Epsilon));
            Assert.That(
                grouped.MinimumCommandableNonzeroTwr,
                Is.EqualTo(grouped.AllActiveEnginesMinimumThrottleTwr).Within(Epsilon));
        }

        [Test]
        public void HoverRestartPenaltyIsOneOffAndSmallerThanFailure()
        {
            var terms = new RewardTerms
            {
                verticalError = 0f,
                planarDistance = 0f,
                speed = 0f,
                verticalSpeed = 0f,
                upDot = 1f,
                upright01 = 1f,
                angularRateDegS = 0f,
                controlEffort = 0f
            };
            var context = new RewardRuntimeContext(
                30f, 0.5f, 9.80665f, 1f, 120f, 40000f, 2f, 180f,
                2f, 2.5f, 2f, 1f, 5f, 25f, 10f,
                false, false, false, false, 0f, 0.45f, 3f,
                engineRestartsThisStep: 1);

            RewardDecision restart = RocketRewardModel.Evaluate(
                ScenarioType.Hover, terms, context, new ScenarioRewardFactors());

            Assert.That(restart.endEpisode, Is.False);
            Assert.That(restart.eventReward, Is.EqualTo(-RocketRewardModel.HoverEngineRestartPenalty).Within(Epsilon));
            Assert.That(Mathf.Abs(restart.eventReward), Is.LessThan(10f));
        }

        [Test]
        public void AgentSchemaIsCanonicalAcrossHardwareAblations()
        {
            var full = new RocketPartsConfig();
            full.ApplyPreset(RocketHardwarePreset.AblationFull);
            var single = new RocketPartsConfig();
            single.ApplyPreset(RocketHardwarePreset.AblationSingleEngine);

            Assert.That(RocketAgentSchema.ObservationSize(full), Is.EqualTo(109));
            Assert.That(RocketAgentSchema.ObservationSize(single), Is.EqualTo(109));
            Assert.That(RocketAgentSchema.ContinuousActionSize(full), Is.EqualTo(39));
            Assert.That(RocketAgentSchema.ContinuousActionSize(single), Is.EqualTo(39));
        }

        [Test]
        public void LandingRewardPrefersUsefulDescentOverHoverOrFlyaway()
        {
            RewardDecision usefulDescent = EvaluateLanding(
                altitude: 260f,
                verticalSpeed: -18f,
                closureRate: 18f);
            RewardDecision hover = EvaluateLanding(
                altitude: 260f,
                verticalSpeed: 0f,
                closureRate: 0f);
            RewardDecision flyaway = EvaluateLanding(
                altitude: 260f,
                verticalSpeed: 10f,
                closureRate: -10f);

            Assert.That(usefulDescent.shapingReward, Is.GreaterThan(hover.shapingReward));
            Assert.That(hover.shapingReward, Is.GreaterThan(flyaway.shapingReward));
            Assert.That(hover.shapingReward, Is.LessThan(0f), "Stationary hovering must not farm positive landing reward.");
        }

        [Test]
        public void LandingRewardUsesOneOffCaptureEvent()
        {
            RewardDecision ordinary = EvaluateLanding(
                altitude: 62f,
                verticalSpeed: -1f,
                closureRate: 1f,
                platformStable: true,
                platformBecameStable: false);
            RewardDecision transition = EvaluateLanding(
                altitude: 62f,
                verticalSpeed: -1f,
                closureRate: 1f,
                platformStable: true,
                platformBecameStable: true);

            Assert.That(ordinary.eventReward, Is.Zero.Within(Epsilon));
            Assert.That(transition.eventReward, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(transition.shapingReward, Is.EqualTo(ordinary.shapingReward).Within(Epsilon));
        }

        [Test]
        public void LandingTerminalOutcomesHaveFixedSignals()
        {
            RewardDecision success = EvaluateLanding(
                altitude: 60f,
                verticalSpeed: 0f,
                closureRate: 0f,
                platformStable: true);
            RewardDecision failedTouchdown = EvaluateLanding(
                altitude: 60f,
                verticalSpeed: -8f,
                closureRate: 8f,
                platformStable: false);
            RewardDecision timeout = EvaluateLanding(
                altitude: 100f,
                verticalSpeed: 0f,
                closureRate: 0f,
                elapsedSeconds: 120f);

            AssertTerminal(success, 10f, true, EpisodeTerminationReason.ChopstickSuccessfulCapture);
            AssertTerminal(failedTouchdown, -5f, false, EpisodeTerminationReason.ChopstickFailedCapture);
            AssertTerminal(timeout, -5f, false, EpisodeTerminationReason.ChopstickTimeLimit);
        }

        [Test]
        public void LandingProfileInterpolatesContinuouslyAndClampsDifficulty()
        {
            var environment = new SimEnvironmentConfig();
            LandingCurriculumProfile easy = environment.GetLandingCurriculumProfile(0f);
            LandingCurriculumProfile middle = environment.GetLandingCurriculumProfile(0.5f);
            LandingCurriculumProfile full = environment.GetLandingCurriculumProfile(1f);
            LandingCurriculumProfile clamped = environment.GetLandingCurriculumProfile(2f);

            Assert.That(middle.spawnAltitudeMax, Is.EqualTo((easy.spawnAltitudeMax + full.spawnAltitudeMax) * 0.5f).Within(Epsilon));
            Assert.That(middle.successRadius, Is.EqualTo((easy.successRadius + full.successRadius) * 0.5f).Within(Epsilon));
            Assert.That(clamped.spawnAltitudeMax, Is.EqualTo(full.spawnAltitudeMax).Within(Epsilon));
            Assert.That(clamped.successRadius, Is.EqualTo(full.successRadius).Within(Epsilon));
        }

        [Test]
        public void StandardEvaluationForcesCommonFullDifficultySuite()
        {
            var environment = new SimEnvironmentConfig
            {
                behaviorType = BehaviorType.Inference,
                inferencePurpose = InferencePurpose.StandardEvaluation,
                scenario = ScenarioType.ChopstickLanding,
                weather = WeatherType.Stormy,
                windEnabled = true,
                airDensityMultiplier = 1.5f,
                landingCurriculumMode = LandingCurriculumMode.Adaptive,
                landingCurriculumLinearProgress = 0.25f,
                evaluation = new EvaluationConfig { episodeCount = 200, seed = 20257 }
            };
            environment.faults.allowDuringTraining = true;
            environment.faults.evaluationFaultEnabled = true;

            environment.PrepareStandardEvaluation();

            Assert.That(environment.environmentSeed, Is.EqualTo(20257));
            Assert.That(environment.weather, Is.EqualTo(WeatherType.Clear));
            Assert.That(environment.windEnabled, Is.False);
            Assert.That(environment.AirDensityMultiplier, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(environment.faults.allowDuringTraining, Is.False);
            Assert.That(environment.faults.evaluationFaultEnabled, Is.False);
            Assert.That(environment.landingCurriculumMode, Is.EqualTo(LandingCurriculumMode.FixedFullDifficulty));
            Assert.That(environment.landingCurriculumProgress, Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void StandardHoverEvaluationRestoresCanonicalSpawnProfile()
        {
            var environment = new SimEnvironmentConfig
            {
                behaviorType = BehaviorType.Inference,
                inferencePurpose = InferencePurpose.StandardEvaluation,
                scenario = ScenarioType.Hover
            };
            InferenceSpawnProfile profile = environment.GetInferenceSpawnProfile(ScenarioType.Hover);
            profile.altitudeMin = 500f;
            profile.altitudeMax = 600f;

            environment.PrepareStandardEvaluation();

            Assert.That(profile.altitudeMin, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(profile.altitudeMax, Is.EqualTo(35f).Within(Epsilon));
            Assert.That(environment.weather, Is.EqualTo(WeatherType.Clear));
            Assert.That(environment.faults.evaluationFaultEnabled, Is.False);
        }

        [Test]
        public void StandardEvaluatorScopeIncludesFixedHoverAndBothLandingsOnly()
        {
            Assert.That(ScenarioType.Hover.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.ChopstickLanding.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.LegLanding.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.HoverTracking.SupportsStandardEvaluation(), Is.False);
            Assert.That(ScenarioType.Takeoff.SupportsStandardEvaluation(), Is.False);
            Assert.That(ScenarioType.BellyFlop.SupportsStandardEvaluation(), Is.False);
        }

        [Test]
        public void StoppingDistanceAndRecoverableSpeedAreSelfConsistent()
        {
            const float acceleration = 10f;
            const float delay = 0.5f;
            const float gravity = 9.80665f;
            const float altitude = 200f;

            float distanceAtTen = LandingFeasibility.RequiredStoppingDistance(10f, acceleration, delay, gravity);
            float distanceAtTwenty = LandingFeasibility.RequiredStoppingDistance(20f, acceleration, delay, gravity);
            float recoverableSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(altitude, acceleration, delay, gravity);
            float reservedDistance = (altitude - LandingFeasibility.MinimumAltitudeMarginM) *
                                     LandingFeasibility.DefaultDistanceReserveFraction;

            Assert.That(distanceAtTwenty, Is.GreaterThan(distanceAtTen));
            Assert.That(
                LandingFeasibility.RequiredStoppingDistance(recoverableSpeed, acceleration, delay, gravity),
                Is.LessThanOrEqualTo(reservedDistance + 0.01f));
        }

        [Test]
        public void EvaluatorStopsAtTargetAndBuildsAnalysisSummary()
        {
            var session = new EvaluationSession("model_a", 20257, 2, "C:/temp/model_a_episodes.csv");
            var success = new TelemetryEpisodeOutcome
            {
                completed = true,
                success = true,
                terminationReason = EpisodeTerminationReason.ChopstickSuccessfulCapture,
                durationSeconds = 10f,
                fuelUsedKg = 50f,
                finalPlanarDistanceM = 1f,
                finalYawErrorDeg = 2f,
                finalSpeedMps = 2f,
                finalVerticalSpeedMps = -1.5f,
                finalHorizontalSpeedMps = 0.5f,
                finalTiltDeg = 3f,
                finalAngularRateDegS = 4f,
                scenario = ScenarioType.ChopstickLanding,
                rcsPropellantUsedKg = 2f,
                engineRestartCount = 1,
                fixedDeltaTimeSeconds = 0.01f,
                decisionPeriod = 3
            };
            var failure = new TelemetryEpisodeOutcome
            {
                completed = true,
                terminationReason = EpisodeTerminationReason.ChopstickFuelDepleted,
                durationSeconds = 20f,
                fuelUsedKg = 100f,
                scenario = ScenarioType.ChopstickLanding,
                rcsPropellantUsedKg = 4f,
                engineRestartCount = 3,
                fixedDeltaTimeSeconds = 0.01f,
                decisionPeriod = 3
            };

            Assert.That(session.Record(success), Is.False);
            Assert.That(session.Record(failure), Is.True);
            Assert.That(session.Record(success), Is.True, "Outcomes after the configured target must be ignored.");

            EvaluationSummary summary = session.BuildSummary(aborted: false);
            Assert.That(summary.completedEpisodes, Is.EqualTo(2));
            Assert.That(summary.successMetricDefined, Is.True);
            Assert.That(summary.successfulEpisodes, Is.EqualTo(1));
            Assert.That(summary.successRate, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(summary.successWilsonLower95, Is.LessThan(0.5f));
            Assert.That(summary.successWilsonUpper95, Is.GreaterThan(0.5f));
            Assert.That(summary.meanDurationSeconds, Is.EqualTo(15f).Within(Epsilon));
            Assert.That(summary.meanFuelUsedKg, Is.EqualTo(75f).Within(Epsilon));
            Assert.That(summary.meanRcsPropellantUsedKg, Is.EqualTo(3f).Within(Epsilon));
            Assert.That(summary.meanEngineRestartCount, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(summary.meanFinalPlanarDistanceM, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(summary.meanSuccessfulPlanarDistanceM, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(summary.decisionPeriod, Is.EqualTo(3));
        }

        [Test]
        public void FixedHoverEvaluationMarksSuccessAsUndefined()
        {
            var session = new EvaluationSession(
                "hover_model", 20257, 1, "C:/temp/hover_episodes.csv", ScenarioType.Hover);
            session.Record(new TelemetryEpisodeOutcome
            {
                completed = true,
                scenario = ScenarioType.Hover,
                durationSeconds = 30f,
                terminationReason = EpisodeTerminationReason.HoverFuelDepleted
            });

            EvaluationSummary summary = session.BuildSummary(aborted: false);
            Assert.That(summary.successMetricDefined, Is.False);
            Assert.That(summary.successRate, Is.Zero.Within(Epsilon));
            Assert.That(summary.successWilsonLower95, Is.Zero.Within(Epsilon));
            Assert.That(summary.successWilsonUpper95, Is.Zero.Within(Epsilon));
        }

        [Test]
        public void HoverFuelEndpointHasAnExplicitTerminationReason()
        {
            var terms = new RewardTerms
            {
                verticalError = 0f,
                planarDistance = 0f,
                speed = 0f,
                verticalSpeed = 0f,
                upDot = 1f,
                upright01 = 1f,
                angularRateDegS = 0f
            };
            var context = new RewardRuntimeContext(
                30f, 0.5f, 9.80665f, 20f, 120f, 0f, 2f, 180f,
                2f, 2.5f, 2f, 1f, 5f, 25f, 10f,
                false, false, false, false, 0f, 0.45f, 3f);

            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.Hover, terms, context, new ScenarioRewardFactors());

            AssertTerminal(decision, -10f, false, EpisodeTerminationReason.HoverFuelDepleted);
        }

        [Test]
        public void ScenarioValuesPreserveLegacyLandingAsChopstick()
        {
            Assert.That((int)ScenarioType.ChopstickLanding, Is.EqualTo(0));
            Assert.That((int)ScenarioType.Hover, Is.EqualTo(1));
            Assert.That((int)ScenarioType.LegLanding, Is.EqualTo(5));
            Assert.That(ScenarioType.ChopstickLanding.IsLanding(), Is.True);
            Assert.That(ScenarioType.LegLanding.IsLanding(), Is.True);
            Assert.That(ScenarioCatalog.Get(ScenarioType.ChopstickLanding).DisplayName,
                Is.EqualTo("Chopstick Catch Landing"));
            Assert.That(ScenarioCatalog.Get(ScenarioType.LegLanding).DisplayName,
                Is.EqualTo("Falcon 9 Leg Landing"));
        }

        [Test]
        public void AblationPresetsChangeExactlyOneHardwareFamily()
        {
            RocketPartsConfig full = Preset(RocketHardwarePreset.AblationFull);
            RocketPartsConfig noFins = Preset(RocketHardwarePreset.AblationNoFins);
            RocketPartsConfig noRcs = Preset(RocketHardwarePreset.AblationNoRcs);
            RocketPartsConfig triple = Preset(RocketHardwarePreset.AblationTripleEngine);
            RocketPartsConfig single = Preset(RocketHardwarePreset.AblationSingleEngine);

            Assert.That(full.GetEngineCount(), Is.EqualTo(9));
            Assert.That(full.GetActiveEngineCount(), Is.EqualTo(9));
            Assert.That(full.finsEnabled, Is.True);
            Assert.That(full.rcsEnabled, Is.True);

            Assert.That(noFins.GetActiveEngineCount(), Is.EqualTo(9));
            Assert.That(noFins.finsEnabled, Is.False);
            Assert.That(noFins.rcsEnabled, Is.True);

            Assert.That(noRcs.GetActiveEngineCount(), Is.EqualTo(9));
            Assert.That(noRcs.finsEnabled, Is.True);
            Assert.That(noRcs.rcsEnabled, Is.False);

            Assert.That(triple.GetEngineCount(), Is.EqualTo(3));
            Assert.That(triple.finsEnabled, Is.True);
            Assert.That(triple.rcsEnabled, Is.True);
            Assert.That(single.GetEngineCount(), Is.EqualTo(1));
            Assert.That(single.finsEnabled, Is.True);
            Assert.That(single.rcsEnabled, Is.True);

            Assert.That(noFins.minThrottle, Is.EqualTo(full.minThrottle).Within(Epsilon));
            Assert.That(noRcs.engineStartupDelay, Is.EqualTo(full.engineStartupDelay).Within(Epsilon));
            Assert.That(triple.maxThrustPerEngine, Is.EqualTo(full.maxThrustPerEngine).Within(Epsilon));
            Assert.That(single.maxGimbalAngle, Is.EqualTo(full.maxGimbalAngle).Within(Epsilon));
        }

        [Test]
        public void ManifestDryMassEstimateMatchesAblationHardwareRemoval()
        {
            const float massToleranceKg = 0.01f;
            float full = RocketAssembly.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.AblationFull));
            float noFins = RocketAssembly.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.AblationNoFins));
            float noRcs = RocketAssembly.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.AblationNoRcs));
            float triple = RocketAssembly.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.AblationTripleEngine));
            float single = RocketAssembly.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.AblationSingleEngine));

            Assert.That(full, Is.EqualTo(22200f).Within(massToleranceKg));
            Assert.That(noFins, Is.EqualTo(21400f).Within(massToleranceKg));
            Assert.That(noRcs, Is.EqualTo(21950f).Within(massToleranceKg));
            Assert.That(triple, Is.EqualTo(19380f).Within(massToleranceKg));
            Assert.That(single, Is.EqualTo(18440f).Within(massToleranceKg));
        }

        [Test]
        public void LegContactRequiresThreeFeetAndNoStrike()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile profile = environment.GetLegLandingCurriculumProfile(1f);
            var terms = new RewardTerms
            {
                planarDistance = 0.5f,
                speed = 0.5f,
                planarSpeed = 0.2f,
                verticalSpeed = -0.2f,
                angularRateDegS = 1f
            };

            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0011, false, false, terms, 1f, profile), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, false, terms, 1f, profile), Is.True);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, false, true, terms, 1f, profile), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, true, false, terms, 1f, profile), Is.False);
        }

        [Test]
        public void LegGeometryFitsCenteredPadAndHardContactUsesProfileLimits()
        {
            Assert.That(
                LandingLegAssembly.ReferenceFootRadiusM + LandingLegAssembly.ReferenceFootEdgeMarginM,
                Is.LessThan(SimEnvironmentConfig.LegLandingPadHalfSizeM),
                "A centered rocket must fit all four physical feet on the standard pad at any yaw.");

            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile profile = environment.GetLegLandingCurriculumProfile(1f);
            Assert.That(LegLandingContactEvaluator.IsFirstContactSafe(
                1f, -1f, 0.5f, 2f, 10f, profile), Is.True);
            Assert.That(LegLandingContactEvaluator.IsFirstContactSafe(
                profile.successMaxSpeed + 0.1f, -1f, 0.5f, 2f, 10f, profile), Is.False);
        }

        [Test]
        public void LegLandingRewardUsesContactDrivenTerminalOutcomes()
        {
            RewardTerms terms = SafeLegTerms();
            RewardDecision airborneAtPadPlane = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f),
                new ScenarioRewardFactors());
            RewardDecision stable = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f, touchdown: true, stable: true, becameStable: true),
                new ScenarioRewardFactors());
            RewardDecision strike = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f, touchdown: true, structuralStrike: true),
                new ScenarioRewardFactors());

            Assert.That(airborneAtPadPlane.endEpisode, Is.False,
                "Crossing the pad plane must not replace physical foot contact.");
            AssertTerminal(stable, 10f, true, EpisodeTerminationReason.LegLandingSuccessfulTouchdown);
            AssertTerminal(strike, -5f, false, EpisodeTerminationReason.LegLandingStructuralStrike);
        }

        [Test]
        public void LegLandingCurriculumAndEvaluationStateAreIndependent()
        {
            var environment = new SimEnvironmentConfig
            {
                scenario = ScenarioType.LegLanding,
                behaviorType = BehaviorType.Inference,
                inferencePurpose = InferencePurpose.StandardEvaluation,
                legLandingCurriculumLinearProgress = 0.2f,
                landingCurriculumLinearProgress = 0.6f
            };

            environment.PrepareStandardEvaluation();

            Assert.That(environment.legLandingCurriculumMode,
                Is.EqualTo(LandingCurriculumMode.FixedFullDifficulty));
            Assert.That(environment.legLandingCurriculumProgress, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(environment.landingCurriculumLinearProgress, Is.EqualTo(0.6f).Within(Epsilon));
            Assert.That(environment.GetLegLandingCurriculumProfile(1f).platformStableHoldTime,
                Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void LegReferenceEnvelopeIsSingleEngineAndHardwareIndependent()
        {
            Assert.That(LegLandingReferenceEnvelope.ActiveEngineCount, Is.EqualTo(1));
            Assert.That(LegLandingReferenceEnvelope.ReferenceVehicleMassKg, Is.GreaterThan(50000f));
            Assert.That(LegLandingReferenceEnvelope.MaxThrustPerEngineN,
                Is.EqualTo(Falcon9Reference.MerlinSeaLevelThrustN).Within(Epsilon));
        }

        static RocketPartsConfig Preset(RocketHardwarePreset preset)
        {
            var config = new RocketPartsConfig();
            config.ApplyPreset(preset);
            return config;
        }

        static RewardTerms SafeLegTerms() => new()
        {
            planarDistance = 0.5f,
            distance3D = 0.5f,
            speed = 0.2f,
            planarSpeed = 0.1f,
            verticalSpeed = -0.1f,
            goalClosureRate = 0.1f,
            upDot = 1f,
            upright01 = 1f,
            angularRateDegS = 1f,
            controlEffort = 0f
        };

        static RewardRuntimeContext LegContext(
            float altitude,
            bool touchdown = false,
            bool stable = false,
            bool becameStable = false,
            bool structuralStrike = false)
        {
            return new RewardRuntimeContext(
                altitude, 0f, 9.80665f, 1f, 120f, 1000f, 1f, 1000f,
                2f, 2.5f, 2f, 1f, 5f, 25f, 180f,
                false, false, false, false, 0f, 1f, 10f,
                legTouchdownStarted: touchdown,
                legFirstContactThisStep: touchdown,
                legFeetOnPad: stable ? 4 : 1,
                legStructuralStrike: structuralStrike,
                legStable: stable,
                legBecameStable: becameStable,
                legStableTime: stable ? 1f : 0f,
                legFirstContactSpeed: 0.2f,
                legFirstContactVerticalSpeed: -0.1f,
                legFirstContactHorizontalSpeed: 0.1f,
                legFirstContactTiltDeg: 1f,
                legFirstContactAngularRateDegS: 1f);
        }

        static RewardDecision EvaluateLanding(
            float altitude,
            float verticalSpeed,
            float closureRate,
            bool platformStable = false,
            bool platformBecameStable = false,
            float elapsedSeconds = 0f)
        {
            var terms = new RewardTerms
            {
                planarDistance = 0f,
                speed = Mathf.Abs(verticalSpeed),
                planarSpeed = 0f,
                verticalSpeed = verticalSpeed,
                goalClosureRate = closureRate,
                upDot = 1f,
                upright01 = 1f,
                angularRateDegS = 0f,
                yawErrorDeg = 0f,
                controlEffort = 0f
            };
            var context = new RewardRuntimeContext(
                altitude,
                60f,
                9.80665f,
                elapsedSeconds,
                120f,
                1000f,
                1f,
                1000f,
                2f,
                2.5f,
                2f,
                1f,
                5f,
                25f,
                10f,
                true,
                platformStable,
                platformStable,
                platformBecameStable,
                platformStable ? 0.45f : 0f,
                0.45f,
                3f);

            return RocketRewardModel.Evaluate(
                ScenarioType.ChopstickLanding,
                terms,
                context,
                new ScenarioRewardFactors());
        }

        static void AssertTerminal(
            RewardDecision decision,
            float expectedReward,
            bool expectedSuccess,
            EpisodeTerminationReason expectedReason)
        {
            Assert.That(decision.endEpisode, Is.True);
            Assert.That(decision.hasTerminalReward, Is.True);
            Assert.That(decision.terminalReward, Is.EqualTo(expectedReward).Within(Epsilon));
            Assert.That(decision.successTerminal, Is.EqualTo(expectedSuccess));
            Assert.That(decision.terminationReason, Is.EqualTo(expectedReason));
        }
    }
}
