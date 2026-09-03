// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Tests/Editor/RocketSimRegressionTests.cs
// Purpose: Locks trainer, configurable objectives, scenario-specific episode
// endings, curriculum, feasibility, and evaluator contracts against drift.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
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
            Assert.That(config.extrinsicGamma, Is.EqualTo(0.9995f).Within(Epsilon));
            Assert.That(Mathf.Pow(config.extrinsicGamma, 10f / 0.03f), Is.GreaterThan(0.80f),
                "A landing terminal ten seconds away must retain useful learning weight.");
            float oneDecisionTimeCost = ScenarioObjectiveConfig
                .CreateDefault(ScenarioType.ChopstickLanding)
                .rewards.timeCostRate * 0.03f;
            float oneDecisionFailureDelayBenefit = 5f * (1f - config.extrinsicGamma);
            Assert.That(oneDecisionTimeCost, Is.GreaterThan(oneDecisionFailureDelayBenefit),
                "Delaying a -5 landing failure by one decision must reduce, not improve, return.");
            ScenarioObjectiveConfig leg =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            Assert.That(leg.rewards.timeCostRate, Is.Zero.Within(Epsilon),
                "Leg landing must not reward a failed policy for terminating early.");
            float maximumLegContactCost =
                leg.rewards.legHardTouchdownCost + leg.rewards.legImpactSeverityCost;
            Assert.That(leg.rewards.legTooFarFromTargetCost, Is.GreaterThan(maximumLegContactCost));
            Assert.That(leg.rewards.legTimeLimitCost, Is.GreaterThan(maximumLegContactCost),
                "Waiting out the episode must not be cheaper than attempting contact.");
            Assert.That(config.lambd, Is.EqualTo(0.98f).Within(Epsilon));
            Assert.That(config.timeHorizon, Is.EqualTo(1024));
            Assert.That(config.curiosityEnabled, Is.False);
            Assert.That(config.maxSteps, Is.EqualTo(30_000_000));
            Assert.That(config.keepCheckpoints, Is.EqualTo(60));
            Assert.That(RocketAgentSchema.CanonicalDecisionPeriod, Is.EqualTo(3));
        }

        [Test]
        public void MlRunControlsSupportLongAdaptiveCurriculumContinuations()
        {
            Assert.That(MLAgentsConfig.MaximumSupportedMaxSteps, Is.GreaterThanOrEqualTo(80_000_000),
                "A resumed landing curriculum needs enough total-step headroom to progress beyond the old 50M UI ceiling.");
            Assert.That(MLAgentsConfig.MaximumSupportedKeepCheckpoints, Is.GreaterThanOrEqualTo(160),
                "At a 500k interval, an 80M experiment needs capacity for 160 checkpoints when the full curve is retained.");
        }

        [Test]
        public void PpoYamlRoundTripPreservesTemporalSettings()
        {
            var source = new MLAgentsConfig();
            string yaml = source.ToYAML();

            Assert.That(MLAgentsConfig.TryFromYAML(yaml, out MLAgentsConfig restored), Is.True);
            Assert.That(restored.extrinsicGamma, Is.EqualTo(0.9995f).Within(Epsilon));
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
            Assert.That(config.extrinsicGamma, Is.EqualTo(0.9995f).Within(Epsilon));
            Assert.That(config.lambd, Is.EqualTo(0.98f).Within(Epsilon));
            Assert.That(config.timeHorizon, Is.EqualTo(1024));
            Assert.That(config.curiosityEnabled, Is.False);
            Assert.That(config.checkpointInterval, Is.EqualTo(500_000));
            Assert.That(config.maxSteps, Is.EqualTo(30_000_000));
            Assert.That(config.keepCheckpoints, Is.EqualTo(60));
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
        public void InferenceUsesEvaluationAreaPrefabWhileTrainingUsesTrainingAreaPrefab()
        {
            var trainingPrefab = new GameObject("TrainingPrefab");
            var evaluationPrefab = new GameObject("EvaluationPrefab");
            try
            {
                var environment = new SimEnvironmentConfig
                {
                    behaviorType = BehaviorType.Inference,
                    inferencePurpose = InferencePurpose.ManualInference
                };

                Assert.That(
                    SimulationAreaHost.SelectRuntimeAreaPrefab(
                        environment,
                        trainingPrefab,
                        evaluationPrefab),
                    Is.SameAs(evaluationPrefab));

                environment.inferencePurpose = InferencePurpose.StandardEvaluation;
                Assert.That(
                    SimulationAreaHost.SelectRuntimeAreaPrefab(
                        environment,
                        trainingPrefab,
                        evaluationPrefab),
                    Is.SameAs(evaluationPrefab));

                environment.behaviorType = BehaviorType.Training;
                Assert.That(
                    SimulationAreaHost.SelectRuntimeAreaPrefab(
                        environment,
                        trainingPrefab,
                        evaluationPrefab),
                    Is.SameAs(trainingPrefab));
            }
            finally
            {
                Object.DestroyImmediate(trainingPrefab);
                Object.DestroyImmediate(evaluationPrefab);
            }
        }

        [Test]
        public void TrainerReadinessCheckDoesNotConsumeTheListeningConnection()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                bool detected = false;
                for (int attempt = 0; attempt < 10 && !detected; attempt++)
                {
                    detected = TrainingProcessLauncher.TcpPortIsListening(port);
                    if (!detected) Thread.Sleep(10);
                }

                Assert.That(detected, Is.True, "The OS listener check should detect a ready trainer port.");
                Assert.That(listener.Pending(), Is.False,
                    "A readiness check must not occupy ML-Agents' Unity connection.");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void HoverEquilibriumThrottleMatchesWeightAndAvailableThrust()
        {
            Assert.That(ScenarioCatalog.HoverStartAltitude, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(ScenarioCatalog.HoverTrackingStartAltitude, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(ScenarioCatalog.HoverGoalAltitude, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(ScenarioCatalog.HoverTrackingGoalAltitude, Is.EqualTo(30f).Within(Epsilon));

            float throttle = HoverThrustInitialization.EquilibriumThrottle(
                62350f,
                9.80665f,
                845000f,
                1,
                0.40f);

            Assert.That(throttle, Is.EqualTo(0.7235f).Within(0.001f));
        }

        [Test]
        public void TouchdownInterlockForceOffBypassesEngineRunTimers()
        {
            float[] commanded = { 0.7f, 0.6f };
            float[] target = { 0.7f, 0.6f };
            float[] actual = { 0.7f, 0.6f };
            float[] enabled = { 1f, 1f };
            EngineRunState[] states = { EngineRunState.Running, EngineRunState.Starting };
            float[] stateTimers = { 1f, 2f };
            float[] runTimes = { 0.01f, 0f };
            float[] offTimes = { 0f, 0f };

            EngineActuatorStateMachine.ForceOff(
                2,
                commanded,
                target,
                actual,
                enabled,
                states,
                stateTimers,
                runTimes,
                offTimes);

            CollectionAssert.AreEqual(new[] { 0f, 0f }, commanded);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, target);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, actual);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, enabled);
            CollectionAssert.AreEqual(
                new[] { EngineRunState.Off, EngineRunState.Off },
                states);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, stateTimers);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, runTimes);
            CollectionAssert.AreEqual(new[] { 0f, 0f }, offTimes);
        }

        [Test]
        public void HoverTrackingFirstTargetRequiresPlanarAcquisition()
        {
            var area = new GameObject("HoverTrackingInitialTargetTest");
            area.SetActive(false);
            var rocket = new GameObject("Rocket");
            var pad = new GameObject("TargetPad");
            try
            {
                rocket.transform.SetParent(area.transform, false);
                pad.transform.SetParent(area.transform, false);
                rocket.transform.localPosition = new Vector3(4f, 50f, -3f);
                pad.transform.localPosition = new Vector3(-12f, 0f, 9f);

                FalconAgent agent = rocket.AddComponent<FalconAgent>();
                agent.envConfig = new SimEnvironmentConfig
                {
                    scenario = ScenarioType.HoverTracking,
                    targetMoveRadius = 16f,
                    environmentSeed = 123
                };
                agent.targetPad = pad.transform;

                typeof(FalconAgent)
                    .GetMethod("ResetEpisodeRandom", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(agent, null);
                typeof(FalconAgent)
                    .GetMethod("InitializeTargetForEpisode", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(agent, null);

                float planarDistance = Vector2.Distance(
                    new Vector2(pad.transform.localPosition.x, pad.transform.localPosition.z),
                    new Vector2(rocket.transform.localPosition.x, rocket.transform.localPosition.z));
                Assert.That(planarDistance, Is.GreaterThanOrEqualTo(12f - Epsilon));
                Assert.That(Mathf.Abs(pad.transform.localPosition.x), Is.LessThanOrEqualTo(16f));
                Assert.That(Mathf.Abs(pad.transform.localPosition.z), Is.LessThanOrEqualTo(16f));
                Assert.That(new Vector2(
                        pad.transform.localPosition.x,
                        pad.transform.localPosition.z).magnitude,
                    Is.LessThanOrEqualTo(16f + Epsilon));
                Assert.That(pad.transform.localPosition.y, Is.EqualTo(0f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(area);
            }
        }

        [Test]
        public void BothHoverTasksRewardTheExactCommandedAltitudeMost()
        {
            foreach (ScenarioType scenario in new[] { ScenarioType.Hover, ScenarioType.HoverTracking })
            {
                ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(scenario);
                objective.rewards.Clear();
                objective.terminations.Clear();
                objective.rewards.hoverAltitudeProximityRewardRate = 0.2f;

                RewardTerms atGoal = SafeHoverTerms();
                RewardTerms aboveGoal = atGoal;
                RewardTerms belowGoal = atGoal;
                aboveGoal.verticalError = 20f;
                belowGoal.verticalError = -20f;

                RewardDecision exact = RocketRewardModel.Evaluate(
                    scenario, atGoal, HoverContext(), objective);
                RewardDecision above = RocketRewardModel.Evaluate(
                    scenario, aboveGoal, HoverContext(altitude: 50f), objective);
                RewardDecision below = RocketRewardModel.Evaluate(
                    scenario, belowGoal, HoverContext(altitude: 10f), objective);

                Assert.That(exact.shapingRate, Is.EqualTo(0.2f).Within(Epsilon));
                Assert.That(exact.shapingRate, Is.GreaterThan(above.shapingRate));
                Assert.That(above.shapingRate, Is.EqualTo(below.shapingRate).Within(Epsilon));
            }
        }

        [Test]
        public void HoverCannotFarmStabilityRewardsAwayFromTheCompleteTarget()
        {
            foreach (ScenarioType scenario in new[] { ScenarioType.Hover, ScenarioType.HoverTracking })
            {
                ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(scenario);
                objective.rewards.Clear();
                objective.terminations.Clear();
                objective.rewards.hoverPlanarProximityRewardRate = 1f;
                objective.rewards.hoverUprightRewardRate = 1f;
                objective.rewards.hoverSpeedCalmRewardRate = 1f;
                objective.rewards.hoverRotationCalmRewardRate = 1f;

                RewardTerms exactTerms = SafeHoverTerms();
                RewardTerms altitudeFlyawayTerms = exactTerms;
                altitudeFlyawayTerms.verticalError = 60f;
                RewardTerms planarFlyawayTerms = exactTerms;
                planarFlyawayTerms.planarDistance = 100f;

                RewardDecision exact = RocketRewardModel.Evaluate(
                    scenario, exactTerms, HoverContext(), objective);
                RewardDecision altitudeFlyaway = RocketRewardModel.Evaluate(
                    scenario, altitudeFlyawayTerms, HoverContext(altitude: 90f), objective);
                RewardDecision planarFlyaway = RocketRewardModel.Evaluate(
                    scenario, planarFlyawayTerms, HoverContext(), objective);

                Assert.That(exact.shapingRate, Is.EqualTo(4f).Within(Epsilon));
                Assert.That(altitudeFlyaway.shapingRate, Is.LessThan(0.02f),
                    "A calm rocket far from the commanded altitude must not earn a survivable reward floor.");
                Assert.That(planarFlyaway.shapingRate, Is.LessThan(0.02f),
                    "Satisfying altitude alone must not pay the full fixed-hover stability reward.");
            }
        }

        [Test]
        public void HoverTrackingEpisodeSuccessRequiresAcquiringARelocatedTarget()
        {
            Assert.That(SimEnvironmentConfig.IsHoverTrackCurriculumEpisodeSuccessful(
                captureGoalEnabled: false,
                objectiveSuccessTerminalReached: false,
                episodeCaptures: 1), Is.False);
            Assert.That(SimEnvironmentConfig.IsHoverTrackCurriculumEpisodeSuccessful(
                captureGoalEnabled: false,
                objectiveSuccessTerminalReached: false,
                episodeCaptures: 2), Is.True);
            Assert.That(SimEnvironmentConfig.IsHoverTrackCurriculumEpisodeSuccessful(
                captureGoalEnabled: true,
                objectiveSuccessTerminalReached: false,
                episodeCaptures: 10), Is.False);
            Assert.That(SimEnvironmentConfig.IsHoverTrackCurriculumEpisodeSuccessful(
                captureGoalEnabled: true,
                objectiveSuccessTerminalReached: true,
                episodeCaptures: 2), Is.True);
        }

        [Test]
        public void HoverTrackingCurriculumAdvancesAfterSuccessfulTargetAttempts()
        {
            var environment = new SimEnvironmentConfig
            {
                scenario = ScenarioType.HoverTracking
            };
            environment.ResetHoverTrackCurriculum();

            for (int episode = 0; episode < 32; episode++)
                environment.RecordHoverTrackCurriculumAttempt(true, activeAreaCount: 16);

            Assert.That(environment.hoverTrackCurriculumLinearProgress, Is.GreaterThan(0f));
            Assert.That(environment.hoverTrackCurriculumProgress, Is.GreaterThan(0f));
            Assert.That(environment.targetMoveRadius,
                Is.GreaterThan(environment.hoverTrackStartMoveRadius));
        }

        [Test]
        public void HoverTrackingCaptureCountersAndCurriculumAttemptsAreIndependent()
        {
            var environment = new SimEnvironmentConfig
            {
                scenario = ScenarioType.HoverTracking
            };
            environment.ResetHoverTrackCurriculum();
            var curriculum = new CurriculumController();

            curriculum.RecordTargetCapture(environment);

            Assert.That(environment.hoverTrackCurriculumSuccesses, Is.EqualTo(1));
            Assert.That(environment.hoverTrackCurriculumEpisodeCount, Is.Zero,
                "The initial acquisition capture must not promote curriculum by itself.");

            curriculum.RecordHoverTrackAttempt(
                environment,
                successfulAttempt: true,
                activeAreaCount: 1);

            Assert.That(environment.hoverTrackCurriculumEpisodeCount, Is.EqualTo(1));
            Assert.That(environment.hoverTrackCurriculumSuccessfulEpisodes, Is.EqualTo(1));
            Assert.That(environment.hoverTrackCurriculumLinearProgress, Is.GreaterThan(0f));

            curriculum.RecordHoverTrackAttempt(
                environment,
                successfulAttempt: false,
                activeAreaCount: 1);

            Assert.That(environment.hoverTrackCurriculumEpisodeCount, Is.EqualTo(2));
            Assert.That(environment.hoverTrackCurriculumSuccessfulEpisodes, Is.EqualTo(1));
            Assert.That(environment.HoverTrackCurriculumSuccessRate,
                Is.EqualTo(0.5f).Within(Epsilon));
        }

        [Test]
        public void HoverTrackingRuntimeStateRoundTripsAllProgressInputs()
        {
            var source = new SimEnvironmentConfig
            {
                scenario = ScenarioType.HoverTracking,
                hoverTrackCurriculumLinearProgress = 0.4f,
                hoverTrackCurriculumEpisodeCount = 50,
                hoverTrackCurriculumSuccessfulEpisodes = 35,
                hoverTrackCurriculumRecentSuccessRate = 0.7f,
                hoverTrackCurriculumSuccesses = 42
            };
            source.ApplyHoverTrackCurriculum();

            RunRuntimeState state = RunRuntimeState.Capture(source, completedEpisodes: 123);
            var restored = new SimEnvironmentConfig { scenario = ScenarioType.HoverTracking };
            state.ApplyTo(restored);
            restored.ApplyHoverTrackCurriculum();

            Assert.That(state.completedEpisodes, Is.EqualTo(123));
            Assert.That(state.hoverTrackingCurriculumUsesTargetAttempts, Is.True);
            Assert.That(restored.hoverTrackCurriculumLinearProgress, Is.EqualTo(0.4f).Within(Epsilon));
            Assert.That(restored.hoverTrackCurriculumProgress,
                Is.EqualTo(source.hoverTrackCurriculumProgress).Within(Epsilon));
            Assert.That(restored.hoverTrackCurriculumEpisodeCount, Is.EqualTo(50));
            Assert.That(restored.hoverTrackCurriculumSuccessfulEpisodes, Is.EqualTo(35));
            Assert.That(restored.hoverTrackCurriculumRecentSuccessRate, Is.EqualTo(0.7f).Within(Epsilon));
            Assert.That(restored.hoverTrackCurriculumSuccesses, Is.EqualTo(42));
        }

        [Test]
        public void LegLandingRuntimeStateRoundTripsAllProgressInputs()
        {
            var source = new SimEnvironmentConfig
            {
                scenario = ScenarioType.LegLanding,
                legLandingCurriculumLinearProgress = 0.4f,
                legLandingCurriculumPeakLinearProgress = 0.55f,
                legLandingCurriculumBatchEpisodeCount = 7,
                legLandingCurriculumEpisodeCount = 50,
                legLandingCurriculumSuccessfulEpisodes = 35,
                legLandingCurriculumRecentSuccessRate = 0.7f,
                legLandingCurriculumSuccesses = 42
            };
            source.ApplyActiveLandingCurriculum();

            RunRuntimeState state = RunRuntimeState.Capture(source, completedEpisodes: 123);
            var restored = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            state.ApplyTo(restored);
            restored.ApplyActiveLandingCurriculum();

            Assert.That(state.completedEpisodes, Is.EqualTo(123));
            Assert.That(state.legLandingCurriculumDetailsCaptured, Is.True);
            Assert.That(restored.legLandingCurriculumLinearProgress, Is.EqualTo(0.4f).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumProgress,
                Is.EqualTo(source.legLandingCurriculumProgress).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumPeakLinearProgress, Is.EqualTo(0.55f).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumBatchEpisodeCount, Is.EqualTo(7));
            Assert.That(restored.legLandingCurriculumEpisodeCount, Is.EqualTo(50));
            Assert.That(restored.legLandingCurriculumSuccessfulEpisodes, Is.EqualTo(35));
            Assert.That(restored.legLandingCurriculumRecentSuccessRate, Is.EqualTo(0.7f).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumSuccesses, Is.EqualTo(42));
        }

        [Test]
        public void LegacyLegLandingRuntimeStateKeepsProgressAndResetsSuccessWindow()
        {
            var legacy = new RunRuntimeState
            {
                legLandingCurriculumProgress = 0.5f,
                legLandingCurriculumPeakProgress = 0.6f,
                legLandingCurriculumAttempts = 7,
                legLandingCurriculumSuccesses = 42
            };
            var restored = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };

            legacy.ApplyTo(restored);
            restored.ApplyActiveLandingCurriculum();

            Assert.That(restored.legLandingCurriculumProgress, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumPeakLinearProgress, Is.EqualTo(0.6f).Within(Epsilon));
            Assert.That(restored.legLandingCurriculumBatchEpisodeCount, Is.EqualTo(7));
            Assert.That(restored.legLandingCurriculumSuccesses, Is.EqualTo(42));
            Assert.That(restored.legLandingCurriculumEpisodeCount, Is.Zero);
            Assert.That(restored.legLandingCurriculumSuccessfulEpisodes, Is.Zero);
            Assert.That(restored.legLandingCurriculumRecentSuccessRate, Is.Zero.Within(Epsilon));
        }

        [Test]
        public void LegacyHoverTrackingRuntimeStateKeepsProgressAndResetsAttemptWindow()
        {
            var legacy = new RunRuntimeState
            {
                hoverTrackingCurriculumProgress = 0.5f,
                hoverTrackingCurriculumAttempts = 10,
                hoverTrackingCurriculumSuccesses = 6
            };
            var restored = new SimEnvironmentConfig { scenario = ScenarioType.HoverTracking };

            legacy.ApplyTo(restored);
            restored.ApplyHoverTrackCurriculum();

            Assert.That(restored.hoverTrackCurriculumProgress, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(restored.hoverTrackCurriculumSuccesses, Is.EqualTo(6));
            Assert.That(restored.hoverTrackCurriculumEpisodeCount, Is.Zero);
            Assert.That(restored.hoverTrackCurriculumSuccessfulEpisodes, Is.Zero);
            Assert.That(restored.hoverTrackCurriculumRecentSuccessRate, Is.Zero.Within(Epsilon));
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
                altitude: 30f,
                terminalAltitude: 0.5f,
                episodeStartAltitude: 30f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: 1f,
                curriculumDifficulty01: 1f,
                fuelKg: 40000f,
                engineRestartsThisStep: 1);

            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            RewardDecision restart = RocketRewardModel.Evaluate(
                ScenarioType.Hover, terms, context, objective);

            Assert.That(restart.endEpisode, Is.False);
            Assert.That(restart.eventReward, Is.EqualTo(-objective.rewards.engineRestartCost).Within(Epsilon));
            Assert.That(Mathf.Abs(restart.eventReward), Is.LessThan(10f));
        }

        [Test]
        public void BalancedObjectivesContainActualRewardMagnitudes()
        {
            ScenarioObjectiveConfig chopstick =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
            ScenarioObjectiveConfig leg =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            ScenarioObjectiveConfig hover =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            ScenarioObjectiveConfig tracking =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);

            Assert.That(chopstick.presetName, Is.EqualTo("Balanced"));
            Assert.That(chopstick.rewards.landingGoalClosureRewardRate, Is.EqualTo(0.060f).Within(Epsilon));
            Assert.That(chopstick.rewards.landingDescentProfileErrorCostRate, Is.EqualTo(0.040f).Within(Epsilon));
            Assert.That(chopstick.rewards.landingYawErrorCostRate, Is.EqualTo(0.015f).Within(Epsilon));
            Assert.That(chopstick.rewards.timeCostRate, Is.EqualTo(0.100f).Within(Epsilon));
            Assert.That(chopstick.rewards.stableCaptureReward, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(chopstick.rewards.chopstickSuccessfulCaptureReward, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(chopstick.rewards.chopstickFailedCaptureCost, Is.EqualTo(5f).Within(Epsilon));

            Assert.That(leg.rewards.landingYawErrorCostRate, Is.Zero.Within(Epsilon));
            Assert.That(leg.rewards.landingGoalClosureRewardRate, Is.EqualTo(0.500f).Within(Epsilon));
            Assert.That(leg.rewards.landingPlanarDistanceCostRate, Is.EqualTo(0.040f).Within(Epsilon));
            Assert.That(leg.rewards.landingUprightErrorCostRate, Is.EqualTo(0.150f).Within(Epsilon));
            Assert.That(leg.rewards.landingPlanarSpeedCostRate, Is.Zero.Within(Epsilon));
            Assert.That(leg.rewards.landingAngularRateCostRate, Is.EqualTo(0.010f).Within(Epsilon));
            Assert.That(leg.rewards.landingNearTargetAngularRateCostRate, Is.EqualTo(0.100f).Within(Epsilon));
            Assert.That(leg.rewards.landingReadinessProgressRewardRate, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(leg.rewards.landingYawSpinCostRate, Is.EqualTo(0.025f).Within(Epsilon));
            Assert.That(leg.rewards.landingDescentProfileErrorCostRate, Is.EqualTo(0.150f).Within(Epsilon));
            Assert.That(leg.rewards.landingNearTargetVerticalSpeedCostRate, Is.EqualTo(0.120f).Within(Epsilon));
            Assert.That(leg.rewards.landingUpwardVelocityCostRate, Is.EqualTo(0.300f).Within(Epsilon));
            Assert.That(leg.rewards.controlEffortCostRate, Is.EqualTo(0.003f).Within(Epsilon));
            Assert.That(leg.rewards.timeCostRate, Is.Zero.Within(Epsilon));
            Assert.That(leg.rewards.firstFootContactReward, Is.Zero.Within(Epsilon));
            Assert.That(leg.rewards.stableTouchdownReward, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(leg.rewards.legSuccessfulTouchdownReward, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(leg.rewards.legSuccessfulFuelEfficiencyReward, Is.EqualTo(6f).Within(Epsilon));
            Assert.That(leg.rewards.legHardTouchdownCost, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(leg.rewards.legImpactSeverityCost, Is.EqualTo(20f).Within(Epsilon));
            Assert.That(leg.rewards.legStructuralStrikeCost, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(leg.rewards.legTooFarFromTargetCost, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(leg.rewards.legFuelDepletedCost, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(leg.rewards.legAboveAltitudeLimitCost, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(leg.rewards.legMissedPadCost, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(leg.rewards.legTimeLimitCost, Is.EqualTo(50f).Within(Epsilon));
            Assert.That(leg.shaping.landingClosureMinimumScaleMps, Is.EqualTo(5f).Within(Epsilon));
            Assert.That(leg.shaping.landingPlanarSpeedScaleMps, Is.EqualTo(5f).Within(Epsilon));
            Assert.That(leg.shaping.landingVerticalSpeedExcessScaleMps,
                Is.EqualTo(5f).Within(Epsilon));
            Assert.That(leg.shaping.legFuelEfficiencyBudgetFraction,
                Is.EqualTo(0.20f).Within(Epsilon));
            Assert.That(leg.shaping.legMissionEfficiencyBudgetFullFraction,
                Is.EqualTo(0.28f).Within(Epsilon));
            Assert.That(leg.shaping.legRestartEquivalentFuelFraction,
                Is.EqualTo(0.006f).Within(Epsilon));
            Assert.That(leg.shaping.legAdditionalEngineIgnitionEquivalentFuelFraction,
                Is.EqualTo(0.0005f).Within(Epsilon));
            Assert.That(leg.shaping.legFuelEfficiencyStartDifficulty, Is.Zero.Within(Epsilon));
            Assert.That(leg.shaping.legFuelEfficiencyFullDifficulty, Is.Zero.Within(Epsilon));
            Assert.That(leg.shaping.legTouchdownQualityRewardFraction,
                Is.EqualTo(0.85f).Within(Epsilon));
            Assert.That(leg.shaping.landingUpwardVelocityToleranceMps,
                Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(leg.shaping.landingUpwardVelocityScaleMps,
                Is.EqualTo(3f).Within(Epsilon));
            Assert.That(leg.terminations.unsafeAttitudeEnabled, Is.False);
            Assert.That(leg.terminations.legFootOutsidePadEnabled, Is.False);
            Assert.That(leg.terminations.legInitialMinimumStableFeet, Is.EqualTo(4));
            Assert.That(leg.terminations.landingSuccessMaxTotalSpeedMps.initial,
                Is.EqualTo(7f).Within(Epsilon));
            Assert.That(leg.terminations.landingSuccessMaxVerticalSpeedMps.initial,
                Is.EqualTo(6f).Within(Epsilon));
            Assert.That(leg.terminations.legMinimumStableFeet, Is.EqualTo(4));
            Assert.That(leg.terminations.maximumAltitudeAboveStartM, Is.EqualTo(100f).Within(Epsilon));
            Assert.That(leg.terminations.maximumEpisodeSeconds, Is.EqualTo(60f).Within(Epsilon));

            Assert.That(hover.rewards.hoverAltitudeProximityRewardRate, Is.EqualTo(0.120f).Within(Epsilon));
            Assert.That(hover.rewards.hoverLinearSpeedCostRate, Is.EqualTo(0.0015f).Within(Epsilon));
            Assert.That(hover.rewards.engineRestartCost, Is.EqualTo(0.250f).Within(Epsilon));
            Assert.That(hover.rewards.hoverGroundImpactCost, Is.EqualTo(10f).Within(Epsilon));

            Assert.That(tracking.rewards.trackingSettleCenterRewardRate, Is.EqualTo(0.045f).Within(Epsilon));
            Assert.That(tracking.rewards.trackingApproachClosureRewardRate, Is.EqualTo(0.085f).Within(Epsilon));
            Assert.That(tracking.rewards.trackingMovingAwayCostRate, Is.EqualTo(0.045f).Within(Epsilon));
        }

        [Test]
        public void ApplyingPresetReplacesTheWholeRewardVectorAndEditsBecomeCustom()
        {
            var config = new TrainingObjectiveConfig();
            ScenarioObjectiveConfig objective = config.ForScenario(ScenarioType.ChopstickLanding);
            objective.rewards.landingGoalClosureRewardRate = 99f;
            objective.rewards.trackingTargetCaptureReward = 99f;
            objective.MarkCustom();

            config.ApplyPreset(ScenarioType.ChopstickLanding, "Balanced");

            Assert.That(objective.presetName, Is.EqualTo("Balanced"));
            Assert.That(objective.rewards.landingGoalClosureRewardRate, Is.EqualTo(0.060f).Within(Epsilon));
            Assert.That(objective.rewards.trackingTargetCaptureReward, Is.Zero.Within(Epsilon),
                "A complete preset must clear even inapplicable stale values before writing its vector.");

            objective.rewards.landingGoalClosureRewardRate = 0.08f;
            objective.MarkCustom();
            Assert.That(objective.presetName, Is.EqualTo("Custom"));
        }

        [Test]
        public void AllZeroRewardObjectiveRemainsIntentionalData()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            objective.rewards.Clear();
            objective.MarkCustom();

            objective.EnsureObjects(ScenarioType.Hover);

            Assert.That(objective.presetName, Is.EqualTo("Custom"));
            Assert.That(
                RewardParameterCatalog.All.All(descriptor =>
                    descriptor.GetValue(objective.rewards) == 0f),
                Is.True,
                "Initialization must never reinterpret a deliberate all-zero objective as missing data.");

            ObjectiveValidationResult validation =
                ObjectiveValidator.Validate(ScenarioType.Hover, objective);
            Assert.That(validation.HasErrors, Is.False,
                "A zero reward vector is allowed for deliberate custom experiments.");
            Assert.That(validation.issues.Any(issue => issue.code == "reward.all_zero"), Is.True,
                "The validator should still explain why an all-zero objective is usually unhelpful.");
        }

        [Test]
        public void LegacyBalancedLegObjectiveUpgradesButCustomObjectiveIsPreserved()
        {
            ScenarioObjectiveConfig legacy =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            legacy.schemaVersion = 0;
            legacy.shaping.landingClosureMinimumScaleMps = 5f;
            legacy.shaping.landingNearTargetAltitudeFalloffM = 50f;
            legacy.shaping.landingPlanarSpeedScaleMps = 5f;
            legacy.terminations.unsafeAttitudeEnabled = true;
            legacy.terminations.maximumAltitudeAboveStartM = 100f;
            legacy.terminations.maximumEpisodeSeconds = 120f;
            legacy.terminations.landingSuccessRadiusM = new DifficultyRange(8f, 2f);
            legacy.terminations.landingSuccessMaxTotalSpeedMps = new DifficultyRange(7f, 2.5f);
            legacy.terminations.landingSuccessMaxVerticalSpeedMps = new DifficultyRange(5f, 2f);
            legacy.terminations.landingSuccessMaxHorizontalSpeedMps = new DifficultyRange(5f, 1f);
            legacy.terminations.landingStableHoldSeconds = new DifficultyRange(0.25f, 1f);

            legacy.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(legacy.schemaVersion, Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(legacy.rewards.timeCostRate, Is.Zero.Within(Epsilon));
            Assert.That(legacy.rewards.legImpactSeverityCost, Is.EqualTo(20f).Within(Epsilon));
            Assert.That(legacy.shaping.landingClosureMinimumScaleMps, Is.EqualTo(5f).Within(Epsilon));
            Assert.That(legacy.terminations.unsafeAttitudeEnabled, Is.False);
            Assert.That(legacy.terminations.maximumEpisodeSeconds, Is.EqualTo(60f).Within(Epsilon));

            ScenarioObjectiveConfig custom =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            custom.schemaVersion = 0;
            custom.MarkCustom();
            custom.rewards.timeCostRate = 0.123f;
            custom.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(custom.schemaVersion, Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(custom.rewards.timeCostRate, Is.EqualTo(0.123f).Within(Epsilon));
            Assert.That(custom.rewards.legImpactSeverityCost, Is.EqualTo(20f).Within(Epsilon));
        }

        [Test]
        public void VersionTenBalancedLegObjectiveUpgradesToResolvedImpactBaseline()
        {
            ScenarioObjectiveConfig objective =
                VersionTwelveBalancedLegObjective();
            objective.schemaVersion = 10;
            objective.rewards.landingGoalClosureRewardRate = 0.120f;
            objective.rewards.landingReadinessProgressRewardRate = 0f;
            objective.rewards.timeCostRate = 0.30f;
            objective.rewards.legImpactSeverityCost = 4f;
            objective.rewards.legTimeLimitCost = 16f;
            objective.terminations.landingSuccessMaxTotalSpeedMps =
                new DifficultyRange(5f, 2f);
            objective.terminations.landingSuccessMaxVerticalSpeedMps =
                new DifficultyRange(4f, 1.5f);
            objective.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.timeCostRate, Is.Zero.Within(Epsilon));
            Assert.That(objective.rewards.legImpactSeverityCost,
                Is.EqualTo(20f).Within(Epsilon));
            Assert.That(objective.rewards.legTimeLimitCost,
                Is.EqualTo(50f).Within(Epsilon));
            Assert.That(objective.terminations.landingSuccessMaxTotalSpeedMps.initial,
                Is.EqualTo(7f).Within(Epsilon));
            Assert.That(objective.terminations.landingSuccessMaxVerticalSpeedMps.initial,
                Is.EqualTo(6f).Within(Epsilon));
        }

        [Test]
        public void VersionElevenBalancedLegObjectiveUpgradesButEditsArePreserved()
        {
            ScenarioObjectiveConfig objective =
                VersionTwelveBalancedLegObjective();
            objective.schemaVersion = 11;
            objective.rewards.landingGoalClosureRewardRate = 0.120f;
            objective.rewards.landingReadinessProgressRewardRate = 0f;
            objective.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.500f).Within(Epsilon));
            Assert.That(objective.rewards.landingReadinessProgressRewardRate,
                Is.EqualTo(4f).Within(Epsilon));
            Assert.That(objective.terminations.landingStableHoldSeconds.initial,
                Is.EqualTo(0.40f).Within(Epsilon));
            Assert.That(objective.terminations.landingStableHoldSeconds.full,
                Is.EqualTo(1f).Within(Epsilon));

            ScenarioObjectiveConfig edited =
                VersionTwelveBalancedLegObjective();
            edited.schemaVersion = 11;
            edited.rewards.landingGoalClosureRewardRate = 0.121f;
            edited.rewards.landingReadinessProgressRewardRate = 0f;
            edited.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.121f).Within(Epsilon),
                "A deliberately edited v11 objective must not be replaced.");
            Assert.That(edited.rewards.landingReadinessProgressRewardRate,
                Is.Zero.Within(Epsilon));
            Assert.That(edited.terminations.landingStableHoldSeconds.initial,
                Is.EqualTo(0.75f).Within(Epsilon));
        }

        [Test]
        public void VersionTwelveBalancedLegObjectiveUpgradesButEditsArePreserved()
        {
            ScenarioObjectiveConfig objective = VersionTwelveBalancedLegObjective();

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.500f).Within(Epsilon));
            Assert.That(objective.rewards.landingUprightErrorCostRate,
                Is.EqualTo(0.150f).Within(Epsilon));
            Assert.That(objective.rewards.landingNearTargetAngularRateCostRate,
                Is.EqualTo(0.100f).Within(Epsilon));
            Assert.That(objective.rewards.legHardTouchdownCost,
                Is.EqualTo(25f).Within(Epsilon));
            Assert.That(objective.rewards.legImpactSeverityCost,
                Is.EqualTo(20f).Within(Epsilon));

            ScenarioObjectiveConfig edited = VersionTwelveBalancedLegObjective();
            edited.rewards.landingUprightErrorCostRate = 0.041f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(edited.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.250f).Within(Epsilon));
            Assert.That(edited.rewards.landingUprightErrorCostRate,
                Is.EqualTo(0.041f).Within(Epsilon),
                "A deliberately edited v12 objective must not be replaced.");
            Assert.That(edited.rewards.legHardTouchdownCost,
                Is.EqualTo(10f).Within(Epsilon));
        }

        [Test]
        public void VersionThirteenBalancedLegObjectiveUpgradesButEditsArePreserved()
        {
            ScenarioObjectiveConfig objective = VersionThirteenBalancedLegObjective();

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingDescentProfileErrorCostRate,
                Is.EqualTo(0.150f).Within(Epsilon));
            Assert.That(objective.rewards.landingReadinessProgressRewardRate,
                Is.EqualTo(4f).Within(Epsilon));
            Assert.That(objective.shaping.legFuelEfficiencyStartDifficulty,
                Is.Zero.Within(Epsilon));
            Assert.That(objective.shaping.legFuelEfficiencyFullDifficulty,
                Is.Zero.Within(Epsilon));

            ScenarioObjectiveConfig edited = VersionThirteenBalancedLegObjective();
            edited.rewards.landingReadinessProgressRewardRate = 2.1f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(edited.rewards.landingDescentProfileErrorCostRate,
                Is.EqualTo(0.050f).Within(Epsilon));
            Assert.That(edited.rewards.landingReadinessProgressRewardRate,
                Is.EqualTo(2.1f).Within(Epsilon),
                "A deliberately edited v13 objective must not be replaced.");
            Assert.That(edited.shaping.legFuelEfficiencyStartDifficulty,
                Is.EqualTo(0.6f).Within(Epsilon));
        }

        [Test]
        public void VersionFourteenBalancedLegObjectiveUpgradesMissionEfficiencyButEditsArePreserved()
        {
            ScenarioObjectiveConfig objective = VersionFourteenBalancedLegObjective();

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.legSuccessfulFuelEfficiencyReward,
                Is.EqualTo(6f).Within(Epsilon));
            Assert.That(objective.shaping.legFuelEfficiencyBudgetFraction,
                Is.EqualTo(0.20f).Within(Epsilon));
            Assert.That(objective.shaping.legMissionEfficiencyBudgetFullFraction,
                Is.EqualTo(0.28f).Within(Epsilon));
            Assert.That(objective.shaping.legRestartEquivalentFuelFraction,
                Is.EqualTo(0.006f).Within(Epsilon));

            ScenarioObjectiveConfig edited = VersionFourteenBalancedLegObjective();
            edited.rewards.landingReadinessProgressRewardRate = 3.9f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.rewards.landingReadinessProgressRewardRate,
                Is.EqualTo(3.9f).Within(Epsilon),
                "A deliberately edited v14 objective must not be replaced.");
            Assert.That(edited.rewards.legSuccessfulFuelEfficiencyReward,
                Is.EqualTo(3f).Within(Epsilon));
            Assert.That(edited.shaping.legMissionEfficiencyBudgetFullFraction,
                Is.EqualTo(0.08f).Within(Epsilon),
                "A legacy custom objective should retain its old scale across difficulty.");
            Assert.That(edited.shaping.legRestartEquivalentFuelFraction,
                Is.Zero.Within(Epsilon),
                "A legacy custom objective must not silently acquire a new switching preference.");
        }

        [Test]
        public void VersionFifteenBalancedLegObjectiveUpgradesL11GuidanceButEditsArePreserved()
        {
            ScenarioObjectiveConfig objective = VersionFifteenBalancedLegObjective();

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingDescentProfileErrorCostRate,
                Is.EqualTo(0.150f).Within(Epsilon));
            Assert.That(objective.rewards.landingNearTargetVerticalSpeedCostRate,
                Is.EqualTo(0.120f).Within(Epsilon));
            Assert.That(objective.rewards.legSuccessfulFuelEfficiencyReward,
                Is.EqualTo(6f).Within(Epsilon));
            Assert.That(objective.shaping.legRestartEquivalentFuelFraction,
                Is.EqualTo(0.006f).Within(Epsilon));
            Assert.That(objective.shaping.legTouchdownQualityRewardFraction,
                Is.EqualTo(0.85f).Within(Epsilon));

            ScenarioObjectiveConfig edited = VersionFifteenBalancedLegObjective();
            edited.rewards.landingDescentProfileErrorCostRate = 0.121f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(edited.rewards.landingDescentProfileErrorCostRate,
                Is.EqualTo(0.121f).Within(Epsilon),
                "A deliberately edited v15 objective must not be replaced.");
            Assert.That(edited.rewards.legSuccessfulFuelEfficiencyReward,
                Is.EqualTo(4f).Within(Epsilon));
            Assert.That(edited.shaping.legRestartEquivalentFuelFraction,
                Is.EqualTo(0.003f).Within(Epsilon));
        }

        [Test]
        public void VersionSixBalancedLegObjectiveRestoresPreRedesignBaseline()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.schemaVersion = 6;
            objective.rewards.landingGoalClosureRewardRate = 0.25f;
            objective.rewards.landingDescentProfileErrorCostRate = 0f;
            objective.rewards.landingPlanarDistanceCostRate = 0.12f;
            objective.rewards.landingUprightErrorCostRate = 0.22f;
            objective.rewards.landingPlanarSpeedCostRate = 0.015f;
            objective.rewards.landingAngularRateCostRate = 0.12f;
            objective.rewards.landingUpwardVelocityCostRate = 1.5f;
            objective.rewards.landingReadinessProgressRewardRate = 8f;
            objective.rewards.controlEffortCostRate = 0.003f;
            objective.rewards.timeCostRate = 0.60f;
            objective.rewards.legSuccessfulTouchdownReward = 30f;
            objective.rewards.legSuccessfulFuelEfficiencyReward = 8f;
            objective.rewards.legAboveAltitudeLimitCost = 50f;
            objective.shaping.landingClosureMinimumScaleMps = 4f;
            objective.shaping.landingPlanarDistanceFalloffM = 20f;
            objective.shaping.landingNearTargetAltitudeFalloffM = 75f;
            objective.shaping.landingUpwardVelocityToleranceMps = 0.5f;
            objective.shaping.landingUpwardVelocityScaleMps = 3f;
            objective.shaping.legFuelEfficiencyBudgetFraction = 0.06f;
            objective.shaping.legTouchdownQualityRewardFraction = 0.7f;
            objective.terminations.unsafeAttitudeEnabled = false;
            objective.terminations.maximumAltitudeAboveStartM = 5f;
            objective.terminations.maximumEpisodeSeconds = 60f;
            objective.terminations.landingSuccessRadiusM = new DifficultyRange(4f, 2f);
            objective.terminations.landingSuccessMaxTotalSpeedMps = new DifficultyRange(8f, 2.5f);
            objective.terminations.landingSuccessMaxVerticalSpeedMps = new DifficultyRange(7f, 2f);
            objective.terminations.landingSuccessMaxHorizontalSpeedMps = new DifficultyRange(3f, 1f);
            objective.terminations.legInitialMinimumStableFeet = 2;
            objective.terminations.legMinimumStableFeet = 3;

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.50f).Within(Epsilon));
            Assert.That(objective.rewards.landingDescentProfileErrorCostRate,
                Is.EqualTo(0.12f).Within(Epsilon));
            Assert.That(objective.rewards.legSuccessfulTouchdownReward,
                Is.EqualTo(30f).Within(Epsilon));
            Assert.That(objective.rewards.legAboveAltitudeLimitCost,
                Is.EqualTo(50f).Within(Epsilon));
            Assert.That(objective.shaping.landingBallisticDescentFraction,
                Is.EqualTo(0.30f).Within(Epsilon));
            Assert.That(objective.terminations.maximumAltitudeAboveStartM,
                Is.EqualTo(100f).Within(Epsilon));
        }

        [Test]
        public void VersionSevenBalancedLegObjectiveRepairsOnlyUntouchedInvalidScales()
        {
            ScenarioObjectiveConfig objective = VersionEightBalancedLegObjective();
            objective.schemaVersion = 7;
            objective.shaping.landingVerticalSpeedExcessScaleMps = 0f;
            objective.shaping.landingUpwardVelocityToleranceMps = 0f;
            objective.shaping.landingUpwardVelocityScaleMps = 0f;
            objective.shaping.legFuelEfficiencyStartDifficulty = 0f;
            objective.shaping.legFuelEfficiencyFullDifficulty = 0f;
            objective.shaping.legFuelEfficiencyBudgetFraction = 0f;
            objective.shaping.legTouchdownQualityRewardFraction = 0f;

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.shaping.landingVerticalSpeedExcessScaleMps,
                Is.EqualTo(5f).Within(Epsilon));
            Assert.That(objective.shaping.landingUpwardVelocityScaleMps,
                Is.EqualTo(3f).Within(Epsilon));
            Assert.That(objective.shaping.legFuelEfficiencyBudgetFraction,
                Is.EqualTo(0.08f).Within(Epsilon));
            Assert.That(objective.terminations.legMinimumStableFeet, Is.EqualTo(4));
            Assert.That(
                ObjectiveValidator.Validate(ScenarioType.LegLanding, objective).HasErrors,
                Is.False);

            ScenarioObjectiveConfig edited = VersionEightBalancedLegObjective();
            edited.schemaVersion = 7;
            edited.shaping.landingVerticalSpeedExcessScaleMps = 0f;
            edited.shaping.landingUpwardVelocityToleranceMps = 0f;
            edited.shaping.landingUpwardVelocityScaleMps = 0f;
            edited.shaping.legFuelEfficiencyStartDifficulty = 0f;
            edited.shaping.legFuelEfficiencyFullDifficulty = 0f;
            edited.shaping.legFuelEfficiencyBudgetFraction = 0f;
            edited.shaping.legTouchdownQualityRewardFraction = 0f;
            edited.terminations.maximumEpisodeSeconds = 99f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(edited.terminations.maximumEpisodeSeconds,
                Is.EqualTo(99f).Within(Epsilon));
            Assert.That(edited.shaping.landingVerticalSpeedExcessScaleMps,
                Is.Zero.Within(Epsilon),
                "A v7 objective with custom termination settings must not be replaced by the migration.");
        }

        [Test]
        public void VersionEightBalancedLegObjectiveUpgradesButEditedObjectiveIsPreserved()
        {
            ScenarioObjectiveConfig objective = VersionEightBalancedLegObjective();

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.50f).Within(Epsilon));
            Assert.That(objective.shaping.legTouchdownQualityRewardFraction,
                Is.EqualTo(0.85f).Within(Epsilon));
            Assert.That(objective.terminations.legMinimumStableFeet, Is.EqualTo(4));

            ScenarioObjectiveConfig edited = VersionEightBalancedLegObjective();
            edited.terminations.maximumEpisodeSeconds = 99f;

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(edited.terminations.maximumEpisodeSeconds,
                Is.EqualTo(99f).Within(Epsilon));
            Assert.That(edited.rewards.landingGoalClosureRewardRate,
                Is.EqualTo(0.06f).Within(Epsilon),
                "A v8 objective with custom termination settings must not be replaced by the migration.");
        }

        [Test]
        public void VersionNineBalancedLegObjectiveClosesEarlyEscapeShortcutButPreservesEdits()
        {
            ScenarioObjectiveConfig objective =
                VersionTwelveBalancedLegObjective();
            objective.schemaVersion = 9;
            objective.rewards.landingGoalClosureRewardRate = 0.120f;
            objective.rewards.landingReadinessProgressRewardRate = 0f;
            objective.rewards.timeCostRate = 0.30f;
            objective.rewards.legImpactSeverityCost = 4f;
            objective.rewards.legTimeLimitCost = 16f;
            objective.rewards.landingUpwardVelocityCostRate = 0f;
            objective.rewards.legTooFarFromTargetCost = 16f;
            objective.rewards.legFuelDepletedCost = 16f;
            objective.rewards.legAboveAltitudeLimitCost = 16f;
            objective.rewards.legMissedPadCost = 16f;
            objective.terminations.landingSuccessMaxTotalSpeedMps =
                new DifficultyRange(5f, 2f);
            objective.terminations.landingSuccessMaxVerticalSpeedMps =
                new DifficultyRange(4f, 1.5f);
            objective.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion,
                Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingUpwardVelocityCostRate,
                Is.EqualTo(0.30f).Within(Epsilon));
            Assert.That(objective.rewards.legAboveAltitudeLimitCost,
                Is.EqualTo(50f).Within(Epsilon));
            Assert.That(objective.rewards.timeCostRate, Is.Zero.Within(Epsilon));
            Assert.That(objective.rewards.legImpactSeverityCost,
                Is.EqualTo(20f).Within(Epsilon));

            ScenarioObjectiveConfig edited =
                VersionTwelveBalancedLegObjective();
            edited.schemaVersion = 9;
            edited.rewards.landingGoalClosureRewardRate = 0.120f;
            edited.rewards.landingReadinessProgressRewardRate = 0f;
            edited.rewards.timeCostRate = 0.30f;
            edited.rewards.legImpactSeverityCost = 4f;
            edited.rewards.legTimeLimitCost = 16f;
            edited.rewards.landingUpwardVelocityCostRate = 0f;
            edited.rewards.legTooFarFromTargetCost = 15f;
            edited.rewards.legFuelDepletedCost = 16f;
            edited.rewards.legAboveAltitudeLimitCost = 16f;
            edited.rewards.legMissedPadCost = 16f;
            edited.terminations.landingSuccessMaxTotalSpeedMps =
                new DifficultyRange(5f, 2f);
            edited.terminations.landingSuccessMaxVerticalSpeedMps =
                new DifficultyRange(4f, 1.5f);
            edited.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);

            edited.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(edited.rewards.landingUpwardVelocityCostRate,
                Is.Zero.Within(Epsilon));
            Assert.That(edited.rewards.legTooFarFromTargetCost,
                Is.EqualTo(15f).Within(Epsilon),
                "A deliberately edited v9 objective must not be replaced by the migration.");
        }

        [Test]
        public void VersionTwoCustomLegObjectiveDoesNotReceiveRejectedRedesignDefaults()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.schemaVersion = 2;
            objective.MarkCustom();
            objective.rewards.landingReadinessProgressRewardRate = 0f;
            objective.rewards.legImpactSeverityCost = 0f;
            objective.rewards.timeCostRate = 0.45f;
            objective.rewards.legTooFarFromTargetCost = 25f;
            objective.rewards.legFuelDepletedCost = 25f;
            objective.rewards.legAboveAltitudeLimitCost = 25f;
            objective.rewards.legMissedPadCost = 15f;
            objective.rewards.legTimeLimitCost = 25f;
            objective.terminations.legInitialMinimumStableFeet = 0;

            objective.EnsureObjects(ScenarioType.LegLanding);

            Assert.That(objective.schemaVersion, Is.EqualTo(ScenarioObjectiveConfig.CurrentSchemaVersion));
            Assert.That(objective.rewards.landingReadinessProgressRewardRate, Is.Zero.Within(Epsilon));
            Assert.That(objective.rewards.legImpactSeverityCost, Is.Zero.Within(Epsilon));
            Assert.That(objective.rewards.timeCostRate, Is.EqualTo(0.45f).Within(Epsilon));
            Assert.That(objective.rewards.legTooFarFromTargetCost, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(objective.rewards.legMissedPadCost, Is.EqualTo(15f).Within(Epsilon));
            Assert.That(objective.terminations.legInitialMinimumStableFeet, Is.EqualTo(3));
        }

        [Test]
        public void RewardCatalogHasUniqueStableIdsAndScenarioApplicability()
        {
            Assert.That(
                RewardParameterCatalog.All.Select(descriptor => descriptor.id).Distinct().Count(),
                Is.EqualTo(RewardParameterCatalog.All.Count));
            Assert.That(
                RewardParameterCatalog.All.Select(descriptor => descriptor.key).Distinct().Count(),
                Is.EqualTo(RewardParameterCatalog.All.Count));
            Assert.That(
                RewardParameterCatalog.All.All(descriptor => descriptor.id != RewardParameterId.None),
                Is.True);

            int declaredParameterCount = System.Enum.GetValues(typeof(RewardParameterId)).Length - 1;
            Assert.That(RewardParameterCatalog.All.Count, Is.EqualTo(declaredParameterCount),
                "Every declared reward ID must have exactly one descriptor.");

            RewardParameterDescriptor legStrike =
                RewardParameterCatalog.Find(RewardParameterId.LegStructuralStrikeCost);
            Assert.That(legStrike, Is.Not.Null);
            Assert.That(legStrike.AppliesTo(ScenarioType.LegLanding), Is.True);
            Assert.That(legStrike.AppliesTo(ScenarioType.Hover), Is.False,
                "Leg contact outcomes must not leak into a legless hover objective.");
            Assert.That(
                RewardParameterCatalog.ForScenario(ScenarioType.Hover)
                    .Any(descriptor => descriptor.key.StartsWith("leg.")),
                Is.False,
                "The scenario-filtered reward breakdown must not create leg-term columns for hover.");

            foreach (ScenarioDefinition scenario in ScenarioCatalog.All)
                Assert.That(
                    RewardParameterCatalog.ForScenario(scenario.Type).All(item => item.AppliesTo(scenario.Type)),
                    Is.True);
        }

        [Test]
        public void RewardCatalogBindsEveryEditableRewardMagnitudeExactlyOnce()
        {
            FieldInfo[] rewardFields = typeof(RewardParameters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float))
                .ToArray();
            var boundFields = new HashSet<string>();

            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
            {
                var values = new RewardParameters();
                values.Clear();
                descriptor.SetValue(values, 1f);

                FieldInfo[] changed = rewardFields
                    .Where(field => (float)field.GetValue(values) != 0f)
                    .ToArray();
                Assert.That(changed.Length, Is.EqualTo(1),
                    $"Reward descriptor '{descriptor.key}' must bind exactly one serialized magnitude.");
                Assert.That(boundFields.Add(changed[0].Name), Is.True,
                    $"Reward field '{changed[0].Name}' is bound by more than one descriptor.");
            }

            Assert.That(boundFields.Count, Is.EqualTo(rewardFields.Length),
                "The catalog-driven Reward panel must expose every editable RewardParameters field.");
        }

        [Test]
        public void ShapingCatalogBindsEveryEditableScaleExactlyOnce()
        {
            FieldInfo[] shapingFields = typeof(RewardShapingParameters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float))
                .ToArray();
            var boundFields = new HashSet<string>();

            Assert.That(
                ShapingParameterCatalog.All.Select(descriptor => descriptor.id).Distinct().Count(),
                Is.EqualTo(ShapingParameterCatalog.All.Count));
            Assert.That(
                ShapingParameterCatalog.All.Select(descriptor => descriptor.key).Distinct().Count(),
                Is.EqualTo(ShapingParameterCatalog.All.Count));
            Assert.That(
                ShapingParameterCatalog.All.Count,
                Is.EqualTo(System.Enum.GetValues(typeof(ShapingParameterId)).Length));

            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
            {
                var values = new RewardShapingParameters();
                values.Clear();
                descriptor.SetValue(values, 1f);

                FieldInfo[] changed = shapingFields
                    .Where(field => (float)field.GetValue(values) != 0f)
                    .ToArray();
                Assert.That(changed.Length, Is.EqualTo(1),
                    $"Shaping descriptor '{descriptor.key}' must bind exactly one serialized scale.");
                Assert.That(boundFields.Add(changed[0].Name), Is.True,
                    $"Shaping field '{changed[0].Name}' is bound by more than one descriptor.");
            }

            Assert.That(boundFields.Count, Is.EqualTo(shapingFields.Length),
                "The catalog-driven Reward panel must expose every editable shaping field.");
        }

        [Test]
        public void RewardSliderCurveKeepsNormalAndSmallValuesReachable()
        {
            Assert.That(
                RewardParameterCatalog.Find(RewardParameterId.HoverAltitudeProximityRewardRate).scaleMode,
                Is.EqualTo(NumericScaleMode.QuadraticWithZero));

            float normalPosition = Mathf.Sqrt(0.08f / 0.25f);
            float smallPosition = Mathf.Sqrt(0.001f / 0.25f);
            Assert.That(normalPosition, Is.InRange(0.50f, 0.65f),
                "A normal 0.08 reward should not render against the right edge of a 0..0.25 slider.");
            Assert.That(smallPosition, Is.InRange(0.05f, 0.08f),
                "Small nonzero rewards must remain reachable by dragging instead of collapsing into zero.");
        }

        [Test]
        public void TerminationCatalogIsUniqueAndHidesLegRulesFromHover()
        {
            Assert.That(
                TerminationRuleCatalog.All.Select(rule => rule.id).Distinct().Count(),
                Is.EqualTo(TerminationRuleCatalog.All.Count));
            Assert.That(
                TerminationRuleCatalog.All.Select(rule => rule.key).Distinct().Count(),
                Is.EqualTo(TerminationRuleCatalog.All.Count));
            Assert.That(
                TerminationRuleCatalog.All.Count,
                Is.EqualTo(System.Enum.GetValues(typeof(TerminationRuleId)).Length));

            TerminationRuleDescriptor legRebound =
                TerminationRuleCatalog.Find(TerminationRuleId.LegExcessiveRebound);
            Assert.That(legRebound, Is.Not.Null);
            Assert.That(legRebound.AppliesTo(ScenarioType.LegLanding), Is.True);
            Assert.That(legRebound.AppliesTo(ScenarioType.Hover), Is.False);
            Assert.That(
                TerminationRuleCatalog.ForScenario(ScenarioType.Hover)
                    .Any(rule => rule.key.StartsWith("leg.")),
                Is.False,
                "A hover task without landing legs must not expose leg-contact termination controls.");

            foreach (ScenarioDefinition scenario in ScenarioCatalog.All)
                Assert.That(
                    TerminationRuleCatalog.ForScenario(scenario.Type)
                        .All(rule => rule.AppliesTo(scenario.Type)),
                    Is.True);
        }

        [Test]
        public void FreshHoverTimeLimitsStayDisabledButAreImmediatelyValidWhenEnabled()
        {
            TerminationRuleDescriptor timeLimitRule =
                TerminationRuleCatalog.Find(TerminationRuleId.HoverTimeLimit);
            Assert.That(timeLimitRule, Is.Not.Null);
            TerminationThresholdDescriptor timeLimitThreshold = timeLimitRule.thresholds.Single(
                threshold => threshold.key == "common.maximum_episode_seconds");

            foreach (ScenarioType scenario in new[] { ScenarioType.Hover, ScenarioType.HoverTracking })
            {
                ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(scenario);

                Assert.That(objective.terminations.timeLimitEnabled, Is.False,
                    $"A fresh {scenario} objective must not terminate an otherwise healthy hover by time.");
                Assert.That(
                    objective.terminations.maximumEpisodeSeconds,
                    Is.InRange(timeLimitThreshold.hardMinimum, timeLimitThreshold.hardMaximum),
                    "Disabled controls must retain an editable value accepted by their descriptor.");

                timeLimitRule.SetEnabled(objective.terminations, true);
                ObjectiveValidationResult enabled = ObjectiveValidator.Validate(scenario, objective);
                Assert.That(enabled.HasErrors, Is.False,
                    $"Enabling the untouched {scenario} time limit must produce a runnable objective.");
                Assert.That(
                    enabled.issues.Any(issue =>
                        issue.code == "termination.out_of_range" &&
                        issue.parameterKey == timeLimitThreshold.key),
                    Is.False);
            }
        }

        [Test]
        public void SemanticSignsAndContributionDiagnosticsMatchTheAppliedReward()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            objective.rewards.Clear();
            objective.terminations.Clear();
            objective.rewards.hoverAltitudeProximityRewardRate = 0.20f;
            objective.rewards.hoverLinearSpeedCostRate = 0.03f;
            var terms = SafeHoverTerms();
            terms.speed = 2f;
            var contributions = new RewardContributionBuffer();

            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.Hover,
                terms,
                HoverContext(),
                objective,
                contributions);

            Assert.That(decision.shapingRate, Is.EqualTo(0.14f).Within(Epsilon));
            Assert.That(contributions.shapingRate, Is.EqualTo(decision.shapingRate).Within(Epsilon));
            Assert.That(contributions.eventReward, Is.Zero.Within(Epsilon));
            Assert.That(contributions.terminalReward, Is.Zero.Within(Epsilon));

            Assert.That(
                contributions.GetRawFeature(RewardParameterId.HoverAltitudeProximityRewardRate),
                Is.EqualTo(1f).Within(Epsilon));
            Assert.That(
                contributions.GetSignedCoefficient(RewardParameterId.HoverAltitudeProximityRewardRate),
                Is.EqualTo(0.20f).Within(Epsilon));
            Assert.That(
                contributions.GetSignedContribution(RewardParameterId.HoverAltitudeProximityRewardRate),
                Is.EqualTo(0.20f).Within(Epsilon));

            Assert.That(
                contributions.GetRawFeature(RewardParameterId.HoverLinearSpeedCostRate),
                Is.EqualTo(2f).Within(Epsilon));
            Assert.That(
                contributions.GetSignedCoefficient(RewardParameterId.HoverLinearSpeedCostRate),
                Is.EqualTo(-0.03f).Within(Epsilon));
            Assert.That(
                contributions.GetSignedContribution(RewardParameterId.HoverLinearSpeedCostRate),
                Is.EqualTo(-0.06f).Within(Epsilon));

            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
            {
                if (descriptor.AppliesTo(ScenarioType.Hover)) continue;
                Assert.That(contributions.GetRawFeature(descriptor.id), Is.Zero.Within(Epsilon));
                Assert.That(contributions.GetSignedCoefficient(descriptor.id), Is.Zero.Within(Epsilon));
                Assert.That(contributions.GetSignedContribution(descriptor.id), Is.Zero.Within(Epsilon));
            }

            Assert.That(
                RewardParameterCatalog.Find(RewardParameterId.HoverAltitudeProximityRewardRate).sign,
                Is.EqualTo(RewardParameterSign.Reward));
            Assert.That(
                RewardParameterCatalog.Find(RewardParameterId.HoverLinearSpeedCostRate).sign,
                Is.EqualTo(RewardParameterSign.Cost));
        }

        [Test]
        public void HoverTerminationRulesRespectEnableSwitchesAndThresholds()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            objective.terminations.hoverGroundClearanceM = 2f;

            RewardDecision enabled = RocketRewardModel.Evaluate(
                ScenarioType.Hover,
                SafeHoverTerms(),
                HoverContext(altitude: 1.9f),
                objective);
            AssertTerminal(enabled, -10f, false, EpisodeTerminationReason.HoverGroundImpact);

            objective.terminations.hoverGroundImpactEnabled = false;
            RewardDecision disabled = RocketRewardModel.Evaluate(
                ScenarioType.Hover,
                SafeHoverTerms(),
                HoverContext(altitude: 1.9f),
                objective);
            Assert.That(disabled.endEpisode, Is.False,
                "Disabling a configurable termination rule must change runtime behavior.");

            objective.terminations.planarFlyawayEnabled = true;
            objective.terminations.maximumPlanarDistanceM = 5f;
            RewardTerms far = SafeHoverTerms();
            far.planarDistance = 6f;
            RewardDecision tightThreshold = RocketRewardModel.Evaluate(
                ScenarioType.Hover, far, HoverContext(), objective);
            AssertTerminal(tightThreshold, -10f, false, EpisodeTerminationReason.HoverTooFarFromTarget);

            objective.terminations.maximumPlanarDistanceM = 10f;
            RewardDecision relaxedThreshold = RocketRewardModel.Evaluate(
                ScenarioType.Hover, far, HoverContext(), objective);
            Assert.That(relaxedThreshold.endEpisode, Is.False);
        }

        [Test]
        public void HoverTrackingCaptureGoalIsOptionalAndUsesConfiguredSignals()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);
            objective.terminations.trackingCaptureGoalEnabled = true;
            objective.terminations.trackingRequiredCaptures = 2;
            objective.rewards.trackingTargetCaptureReward = 1.25f;
            objective.rewards.trackingCaptureGoalReward = 6f;
            var contributions = new RewardContributionBuffer();

            RewardDecision firstCapture = RocketRewardModel.Evaluate(
                ScenarioType.HoverTracking,
                SafeHoverTerms(),
                HoverContext(targetCaptured: true, captureCount: 1),
                objective,
                contributions);
            Assert.That(firstCapture.endEpisode, Is.False);
            Assert.That(firstCapture.eventReward, Is.EqualTo(1.25f).Within(Epsilon));

            RewardDecision goal = RocketRewardModel.Evaluate(
                ScenarioType.HoverTracking,
                SafeHoverTerms(),
                HoverContext(targetCaptured: true, captureCount: 2),
                objective,
                contributions);
            AssertTerminal(goal, 6f, true, EpisodeTerminationReason.HoverTrackingCaptureGoal);
            Assert.That(goal.eventReward, Is.EqualTo(1.25f).Within(Epsilon));
            Assert.That(contributions.eventReward, Is.EqualTo(goal.eventReward).Within(Epsilon));
            Assert.That(contributions.shapingRate, Is.EqualTo(goal.shapingRate).Within(Epsilon));
            Assert.That(
                contributions.GetRawFeature(RewardParameterId.TrackingTargetCaptureReward),
                Is.EqualTo(1f).Within(Epsilon));
            Assert.That(
                contributions.GetSignedCoefficient(RewardParameterId.TrackingCaptureGoalReward),
                Is.EqualTo(6f).Within(Epsilon));
            Assert.That(contributions.terminalReward, Is.EqualTo(6f).Within(Epsilon));

            objective.terminations.trackingCaptureGoalEnabled = false;
            RewardDecision optional = RocketRewardModel.Evaluate(
                ScenarioType.HoverTracking,
                SafeHoverTerms(),
                HoverContext(targetCaptured: true, captureCount: 2),
                objective);
            Assert.That(optional.endEpisode, Is.False);
            Assert.That(optional.eventReward, Is.EqualTo(1.25f).Within(Epsilon));
        }

        [Test]
        public void ObjectiveValidationAcceptsDefaultsAndRejectsInvalidValues()
        {
            foreach (ScenarioDefinition scenario in ScenarioCatalog.All)
            {
                ObjectiveValidationResult defaults = ObjectiveValidator.Validate(
                    scenario.Type,
                    ScenarioObjectiveConfig.CreateDefault(scenario.Type));
                Assert.That(defaults.HasErrors, Is.False,
                    $"The fresh {scenario.DisplayName} objective must be runnable.");
            }

            ObjectiveValidationResult balancedLeg = ObjectiveValidator.Validate(
                ScenarioType.LegLanding,
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            Assert.That(
                balancedLeg.issues.Any(issue =>
                    issue.code == "termination.zero_outcome" &&
                    issue.parameterKey == "leg.terminal.impact_severity_cost"),
                Is.False,
                "The optional impact-severity add-on may be disabled while fixed contact costs remain active.");
            Assert.That(
                balancedLeg.issues.Any(issue => issue.code == "leg.outcome_hierarchy"),
                Is.False,
                "Balanced escape and timeout costs must remain worse than the maximum contact cost.");

            ScenarioObjectiveConfig cheapEscape =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            cheapEscape.rewards.legAboveAltitudeLimitCost = 16f;
            ObjectiveValidationResult cheapEscapeValidation =
                ObjectiveValidator.Validate(ScenarioType.LegLanding, cheapEscape);
            Assert.That(
                cheapEscapeValidation.issues.Any(issue => issue.code == "leg.outcome_hierarchy"),
                Is.True,
                "Validation must catch the exact early-reset incentive observed in L2.");

            ScenarioObjectiveConfig hover =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover);
            hover.rewards.hoverLinearSpeedCostRate = -0.01f;
            ObjectiveValidationResult negativeReward =
                ObjectiveValidator.Validate(ScenarioType.Hover, hover);
            Assert.That(negativeReward.HasErrors, Is.True);
            Assert.That(negativeReward.issues.Any(issue => issue.code == "reward.out_of_range"), Is.True);

            ScenarioObjectiveConfig tracking =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.HoverTracking);
            tracking.terminations.trackingCaptureGoalEnabled = true;
            tracking.terminations.trackingRequiredCaptures = 0;
            ObjectiveValidationResult invalidCaptureGoal =
                ObjectiveValidator.Validate(ScenarioType.HoverTracking, tracking);
            Assert.That(invalidCaptureGoal.HasErrors, Is.True);
            Assert.That(
                invalidCaptureGoal.issues.Any(issue => issue.code == "termination.capture_count"),
                Is.True);

            ScenarioObjectiveConfig leg =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            leg.shaping.legFuelEfficiencyStartDifficulty = 0.8f;
            leg.shaping.legFuelEfficiencyFullDifficulty = 0.4f;
            ObjectiveValidationResult reversedFuelRamp =
                ObjectiveValidator.Validate(ScenarioType.LegLanding, leg);
            Assert.That(reversedFuelRamp.issues.Any(issue =>
                issue.code == "shaping.fuel_efficiency_curriculum_order"), Is.True);

            ScenarioObjectiveConfig reversedMissionScale =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            reversedMissionScale.shaping.legMissionEfficiencyBudgetFullFraction = 0.10f;
            ObjectiveValidationResult reversedMissionScaleValidation =
                ObjectiveValidator.Validate(ScenarioType.LegLanding, reversedMissionScale);
            Assert.That(reversedMissionScaleValidation.issues.Any(issue =>
                issue.code == "shaping.mission_efficiency_scale_order"), Is.True);

            ScenarioObjectiveConfig chopstick =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding);
            chopstick.terminations.chopstickCapturePlaneEnabled = false;
            DifficultyRange invalidEventRadius = chopstick.terminations.landingSuccessRadiusM;
            invalidEventRadius.initial = float.NaN;
            chopstick.terminations.landingSuccessRadiusM = invalidEventRadius;
            ObjectiveValidationResult invalidStableCaptureEvent =
                ObjectiveValidator.Validate(ScenarioType.ChopstickLanding, chopstick);
            Assert.That(invalidStableCaptureEvent.HasErrors, Is.True,
                "Stable-capture event criteria remain active when terminal capture is disabled.");
        }

        [Test]
        public void ObjectiveValidationRejectsObjectivesWithoutAnyEnabledTerminationRule()
        {
            foreach (ScenarioDefinition scenario in ScenarioCatalog.All)
            {
                ScenarioObjectiveConfig objective =
                    ScenarioObjectiveConfig.CreateDefault(scenario.Type);
                foreach (TerminationRuleDescriptor rule in
                         TerminationRuleCatalog.ForScenario(scenario.Type))
                    rule.SetEnabled(objective.terminations, false);

                ObjectiveValidationResult validation =
                    ObjectiveValidator.Validate(scenario.Type, objective);

                Assert.That(validation.HasErrors, Is.True,
                    $"A {scenario.DisplayName} episode with MaxStep=0 needs at least one enabled termination path.");
                Assert.That(
                    validation.issues.Any(issue =>
                        issue.code == "termination.none_enabled" &&
                        issue.severity == ObjectiveValidationSeverity.Error),
                    Is.True,
                    "The missing termination path must be a launch-blocking validation error.");
            }
        }

        [Test]
        public void ResumeAcceptsTheExactCurrentTrainingContract()
        {
            using var fixture = new ResumeContractFixture();

            bool accepted = fixture.TryValidate(
                fixture.CloneEnvironment(),
                fixture.CloneParts(),
                fixture.CloneMl(),
                out string error);

            Assert.That(accepted, Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void SessionDraftRestoresTheConfigurationFromBeforeRunPreview()
        {
            var local = new SimulationSessionConfig();
            local.environment.runId = "local-run";
            local.vehicle.bodyRadius = 2.25f;
            var imported = local.DeepCopy();
            imported.environment.runId = "imported-run";
            imported.vehicle.bodyRadius = 4.5f;

            var draft = new SimulationSessionDraft(local);
            draft.ApplyImportedSession(imported);
            Assert.That(draft.Config.environment.runId, Is.EqualTo("imported-run"));
            Assert.That(draft.RestoreBeforeImport(), Is.True);
            Assert.That(draft.Config.environment.runId, Is.EqualTo("local-run"));
            Assert.That(draft.Config.vehicle.bodyRadius, Is.EqualTo(2.25f).Within(Epsilon));
            Assert.That(draft.RestoreBeforeImport(), Is.False);
        }

        [Test]
        public void FrozenSessionSnapshotCannotBeChangedThroughTheDraft()
        {
            var draft = new SimulationSessionDraft(new SimulationSessionConfig());
            draft.Config.vehicle.bodyRadius = 2f;
            Assert.That(SimulationSessionSnapshotFactory.TryCreate(
                draft.Config,
                revision: 1,
                out SimulationSessionSnapshot snapshot,
                out SessionValidationResult validation), Is.True,
                string.Join("; ", validation.Issues.Select(issue => issue.Message)));

            draft.Config.vehicle.bodyRadius = 8f;
            SimulationSessionConfig runtime = snapshot.CreateRuntimeConfig();
            Assert.That(runtime.vehicle.bodyRadius, Is.EqualTo(2f).Within(Epsilon));
        }

        [Test]
        public void RunDiscoveryVerifiesPersistedHashBeforeNestedSchemaMigration()
        {
            using var fixture = new ResumeContractFixture();
            Assert.That(SimulationSessionStore.TryLoadManifest(
                fixture.RunId, out SimulationRunManifest manifest), Is.True);

            string sessionPath = Path.Combine(
                fixture.RunRoot,
                "revisions",
                manifest.currentRevision.ToString("D4"),
                "Session.json");
            SimulationSessionConfig legacySession =
                JsonUtility.FromJson<SimulationSessionConfig>(File.ReadAllText(sessionPath));
            legacySession.objective.hover.schemaVersion = 1;

            string compactLegacyJson = JsonUtility.ToJson(legacySession);
            File.WriteAllText(sessionPath, JsonUtility.ToJson(legacySession, true));
            manifest.currentSessionSha256 =
                SimulationSessionSnapshotFactory.Sha256Hex(compactLegacyJson);
            File.WriteAllText(
                Path.Combine(fixture.RunRoot, "RunManifest.json"),
                JsonUtility.ToJson(manifest, true));

            Assert.That(SimulationSessionStore.HasCurrentSession(fixture.RunId), Is.True,
                "A valid legacy nested objective must be migrated only after its persisted snapshot hash is verified.");
            Assert.That(SimulationSessionStore.ScanRuns(), Does.Contain(fixture.RunId));
        }

        [Test]
        public void ResumeAllowsARewardRevisionUnderTheSameRunId()
        {
            using var fixture = new ResumeContractFixture();
            SimEnvironmentConfig requestedEnvironment = fixture.CloneEnvironment();
            requestedEnvironment
                .GetTrainingObjective(ScenarioType.Hover)
                .rewards
                .hoverAltitudeProximityRewardRate += 0.25f;

            bool accepted = fixture.TryValidate(
                requestedEnvironment,
                fixture.CloneParts(),
                fixture.CloneMl(),
                out string error);

            Assert.That(accepted, Is.True,
                "Reward changes are revisioned at launch rather than rejected by Resume.");
            Assert.That(error, Is.Null);
        }

        [Test]
        public void TrainingLaunchCreatesImmutableSessionRevisions()
        {
            using var fixture = new ResumeContractFixture();
            SimulationSessionConfig firstRevision = fixture.LoadSessionRevision(1);
            SimEnvironmentConfig revisedEnvironment = fixture.CloneEnvironment();
            revisedEnvironment
                .GetTrainingObjective(ScenarioType.Hover)
                .rewards
                .hoverAltitudeProximityRewardRate += 0.5f;

            fixture.SaveTrainingLaunch(revisedEnvironment, resumed: true);

            TrainingObjectiveConfig loaded = fixture.LoadTrainingObjective();
            Assert.That(
                JsonUtility.ToJson(loaded),
                Is.EqualTo(JsonUtility.ToJson(revisedEnvironment.EnsureTrainingObjective())),
                "Loading the run must return the objective from its latest session revision.");

            Assert.That(SimulationSessionStore.TryLoadManifest(
                fixture.RunId, out SimulationRunManifest manifest), Is.True);
            Assert.That(manifest.currentRevision, Is.EqualTo(2));

            SimulationSessionConfig secondRevision = fixture.LoadSessionRevision(2);
            Assert.That(
                JsonUtility.ToJson(firstRevision.objective),
                Is.EqualTo(JsonUtility.ToJson(fixture.SavedEnvironment.EnsureTrainingObjective())),
                "Starting a later launch must not mutate its earlier session snapshot.");
            Assert.That(
                JsonUtility.ToJson(secondRevision.objective),
                Is.EqualTo(JsonUtility.ToJson(revisedEnvironment.EnsureTrainingObjective())));
            Assert.That(File.Exists(Path.Combine(
                fixture.RunRoot, "revisions", "0001", "Session.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(
                fixture.RunRoot, "revisions", "0002", "Session.json")), Is.True);
        }

        [Test]
        public void ResumeTrainingPreservesCompletedEpisodeCount()
        {
            using var fixture = new ResumeContractFixture();
            SimulationSessionStore.SaveRuntimeState(
                fixture.RunId,
                RunRuntimeState.Capture(fixture.SavedEnvironment, completedEpisodes: 77));

            fixture.SaveTrainingLaunch(fixture.CloneEnvironment(), resumed: true);

            Assert.That(
                SimulationSessionStore.LoadRuntimeState(fixture.RunId).completedEpisodes,
                Is.EqualTo(77));
        }

        [Test]
        public void ResumeIgnoresOnlyThePreservedEvaluatorFields()
        {
            using var fixture = new ResumeContractFixture();
            SimEnvironmentConfig requestedEnvironment = fixture.CloneEnvironment();
            requestedEnvironment.behaviorType = BehaviorType.Inference;
            requestedEnvironment.inferencePurpose = InferencePurpose.ManualInference;
            requestedEnvironment.evaluation = new EvaluationConfig
            {
                episodeCount = 17,
                seed = 7341
            };

            bool accepted = fixture.TryValidate(
                requestedEnvironment,
                fixture.CloneParts(),
                fixture.CloneMl(),
                out string error);

            Assert.That(accepted, Is.True,
                "Run loading deliberately preserves evaluator mode, purpose, seed, and episode count.");
            Assert.That(error, Is.Null);
        }

        [Test]
        public void ResumeAllowsChangingTheActiveScenarioWhenThePolicyInterfaceMatches()
        {
            using var fixture = new ResumeContractFixture();
            SimEnvironmentConfig requestedEnvironment = fixture.CloneEnvironment();
            requestedEnvironment.scenario = ScenarioType.HoverTracking;
            Assert.That(
                JsonUtility.ToJson(requestedEnvironment.EnsureTrainingObjective()),
                Is.EqualTo(JsonUtility.ToJson(fixture.SavedEnvironment.EnsureTrainingObjective())),
                "This regression must vary only the active scenario, not the objective payload.");

            bool accepted = fixture.TryValidate(
                requestedEnvironment,
                fixture.CloneParts(),
                fixture.CloneMl(),
                out string error);

            Assert.That(accepted, Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void ResumeAllowsTrainingEnvironmentChanges()
        {
            using var fixture = new ResumeContractFixture();
            SimEnvironmentConfig requestedEnvironment = fixture.CloneEnvironment();
            requestedEnvironment.windEnabled = true;
            requestedEnvironment.windSpeed = 12f;

            Assert.That(fixture.TryValidate(
                requestedEnvironment,
                fixture.CloneParts(),
                fixture.CloneMl(),
                out string error), Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void ResumeAllowsPhysicalVehicleChangesThatKeepTheSameChannels()
        {
            using var fixture = new ResumeContractFixture();
            RocketPartsConfig requestedParts = fixture.CloneParts();
            requestedParts.maxThrustPerEngine += 1000f;

            Assert.That(fixture.TryValidate(
                fixture.CloneEnvironment(),
                requestedParts,
                fixture.CloneMl(),
                out string error), Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void ResumeAllowsOptimizerChangesThatKeepTheNetworkCompatible()
        {
            using var fixture = new ResumeContractFixture();
            MLAgentsConfig requestedMl = fixture.CloneMl();
            requestedMl.learningRate *= 0.5f;

            Assert.That(fixture.TryValidate(
                fixture.CloneEnvironment(),
                fixture.CloneParts(),
                requestedMl,
                out string error), Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void ResumeRejectsVehicleChangesThatAlterThePolicyInterface()
        {
            using var fixture = new ResumeContractFixture();
            RocketPartsConfig requestedParts = fixture.CloneParts();
            requestedParts.octawebBurnGroup = OctawebBurnGroup.AllNine;

            AssertResumeRejected(
                fixture,
                fixture.CloneEnvironment(),
                requestedParts,
                fixture.CloneMl(),
                "different policy interface");
        }

        [Test]
        public void ResumeRejectsNetworkArchitectureChanges()
        {
            using var fixture = new ResumeContractFixture();
            MLAgentsConfig requestedMl = fixture.CloneMl();
            requestedMl.hiddenUnits += 128;

            AssertResumeRejected(
                fixture,
                fixture.CloneEnvironment(),
                fixture.CloneParts(),
                requestedMl,
                "must match when resuming");
        }

        [Test]
        public void ObjectiveDifficultyRangesInterpolateAndClampAtCurriculumEndpoints()
        {
            var range = new DifficultyRange(initial: 8f, full: 2f);

            Assert.That(range.At(-1f), Is.EqualTo(8f).Within(Epsilon));
            Assert.That(range.At(0f), Is.EqualTo(8f).Within(Epsilon));
            Assert.That(range.At(0.5f), Is.EqualTo(5f).Within(Epsilon));
            Assert.That(range.At(1f), Is.EqualTo(2f).Within(Epsilon));
            Assert.That(range.At(2f), Is.EqualTo(2f).Within(Epsilon));
        }

        [Test]
        public void AgentSchemaContainsOnlyConfiguredHardwareChannels()
        {
            RocketPartsConfig simple = Preset(RocketHardwarePreset.SimpleSingle);
            RocketPartsConfig falcon = Preset(RocketHardwarePreset.Falcon9);
            RocketPartsConfig allNine = Preset(RocketHardwarePreset.Falcon9);
            allNine.octawebBurnGroup = OctawebBurnGroup.AllNine;

            Assert.That(RocketAgentSchema.ObservationSize(simple, ScenarioType.Hover), Is.EqualTo(29));
            Assert.That(RocketAgentSchema.ObservationSize(simple, ScenarioType.LegLanding), Is.EqualTo(33));
            Assert.That(RocketAgentSchema.ContinuousActionSize(simple), Is.EqualTo(3));
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.Hover), Is.EqualTo(57));
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.HoverTracking),
                Is.EqualTo(57),
                "Evaluation must use the exact Hover Track policy interface used for training.");
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.LegLanding), Is.EqualTo(61));
            Assert.That(RocketAgentSchema.ContinuousActionSize(falcon), Is.EqualTo(21));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.Hover), Is.EqualTo(105));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.LegLanding), Is.EqualTo(109));
            Assert.That(RocketAgentSchema.ContinuousActionSize(allNine), Is.EqualTo(39));

            simple.separateEngineEnableActions = true;
            falcon.separateEngineEnableActions = true;
            allNine.separateEngineEnableActions = true;
            Assert.That(RocketAgentSchema.ObservationSize(simple, ScenarioType.Hover), Is.EqualTo(30));
            Assert.That(RocketAgentSchema.ObservationSize(simple, ScenarioType.LegLanding), Is.EqualTo(34));
            Assert.That(RocketAgentSchema.ContinuousActionSize(simple), Is.EqualTo(4));
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.Hover), Is.EqualTo(60));
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.HoverTracking),
                Is.EqualTo(60));
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.LegLanding), Is.EqualTo(64));
            Assert.That(RocketAgentSchema.ContinuousActionSize(falcon), Is.EqualTo(24));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.Hover), Is.EqualTo(114));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.LegLanding), Is.EqualTo(118));
            Assert.That(RocketAgentSchema.ContinuousActionSize(allNine), Is.EqualTo(48));
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

            Assert.That(usefulDescent.shapingRate, Is.GreaterThan(hover.shapingRate));
            Assert.That(hover.shapingRate, Is.GreaterThan(flyaway.shapingRate));
            Assert.That(hover.shapingRate, Is.LessThan(0f), "Stationary hovering must not farm positive landing reward.");
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
            Assert.That(transition.shapingRate, Is.EqualTo(ordinary.shapingRate).Within(Epsilon));
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
        public void LegLandingCurriculumTeachesNavigationBeforeFullDescentDifficulty()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile easy = environment.GetLegLandingCurriculumProfile(0f);
            LandingCurriculumProfile full = environment.GetLegLandingCurriculumProfile(1f);

            Assert.That(easy.spawnAltitudeMin,
                Is.EqualTo(SimEnvironmentConfig.LegLandingInitialSpawnAltitudeMin).Within(Epsilon));
            Assert.That(easy.spawnAltitudeMax, Is.EqualTo(180f).Within(Epsilon));
            Assert.That(easy.verticalSpeedMin, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(easy.verticalSpeedMax, Is.EqualTo(18f).Within(Epsilon));
            Assert.That(easy.spawnRadius, Is.EqualTo(8f).Within(Epsilon));
            Assert.That(easy.spawnTiltRangeDeg, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(easy.angularSpeedMaxDegS, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(easy.horizontalSpeedMax, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(full.spawnRadius, Is.EqualTo(60f).Within(Epsilon));
            Assert.That(full.angularSpeedMaxDegS, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(full.verticalSpeedMax, Is.EqualTo(70f).Within(Epsilon));
            Assert.That(full.horizontalSpeedMax, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(easy.successRadius, Is.EqualTo(6f).Within(Epsilon));
            Assert.That(full.successRadius, Is.EqualTo(1.5f).Within(Epsilon));
            Assert.That(easy.successMaxSpeed, Is.EqualTo(7f).Within(Epsilon));
            Assert.That(easy.successMaxVerticalSpeed, Is.EqualTo(6f).Within(Epsilon));
            Assert.That(easy.successMaxHorizontalSpeed, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(easy.successMaxTiltDeg, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(easy.successMaxAngularRateDegS, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(easy.platformStableHoldTime, Is.EqualTo(0.40f).Within(Epsilon));
            Assert.That(full.platformStableHoldTime, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(easy.platformHalfSize, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(full.platformHalfSize, Is.EqualTo(25f).Within(Epsilon));
            Assert.That(easy.minimumStableFeet, Is.EqualTo(4));
            Assert.That(full.minimumStableFeet, Is.EqualTo(4));
        }

        [Test]
        public void LegLandingCurriculumInterpolatesAllRangesAtTheSameDifficulty()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile staged = environment.GetLegLandingCurriculumProfile(0.25f);

            Assert.That(staged.spawnAltitudeMin, Is.EqualTo(190f).Within(Epsilon));
            Assert.That(staged.spawnRadius, Is.EqualTo(21f).Within(Epsilon));
            Assert.That(staged.criteriaDifficulty01, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(staged.successRadius, Is.EqualTo(4.875f).Within(Epsilon));
        }

        [Test]
        public void StandardEvaluationForcesFiveCurriculumBands()
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
            Assert.That(environment.evaluation.episodeCount,
                Is.EqualTo(EvaluationConfig.DefaultEpisodeCount));
            Assert.That(environment.evaluation.timeScale,
                Is.EqualTo(EvaluationConfig.DefaultTimeScale).Within(Epsilon));
            Assert.That(environment.landingCurriculumMode, Is.EqualTo(LandingCurriculumMode.Adaptive));
            Assert.That(environment.StandardEvaluationDifficultyForEpisode(0), Is.Zero.Within(Epsilon));
            Assert.That(environment.StandardEvaluationDifficultyForEpisode(50), Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(environment.StandardEvaluationDifficultyForEpisode(249), Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void StandardHoverEvaluationUsesABoundedTrainingAlignedContract()
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

            Assert.That(profile.altitudeMin, Is.EqualTo(500f).Within(Epsilon),
                "Manual-inference ranges are ignored rather than rewritten by evaluation.");
            Assert.That(profile.altitudeMax, Is.EqualTo(600f).Within(Epsilon));
            Assert.That(environment.weather, Is.EqualTo(WeatherType.Clear));
            Assert.That(environment.faults.evaluationFaultEnabled, Is.False);
            TerminationParameters termination =
                environment.GetTrainingObjective(ScenarioType.Hover).terminations;
            Assert.That(termination.timeLimitEnabled, Is.False,
                "The neutral evaluator horizon must not apply a training timeout penalty.");
        }

        [Test]
        public void StandardHoverEvaluationIgnoresManualSpawnAndUsesTrainingSpawn()
        {
            var rocket = new GameObject("StandardHoverSpawnTest");
            rocket.SetActive(false);
            try
            {
                FalconAgent agent = rocket.AddComponent<FalconAgent>();
                agent.rb = rocket.AddComponent<Rigidbody>();
                agent.envConfig = new SimEnvironmentConfig
                {
                    behaviorType = BehaviorType.Inference,
                    inferencePurpose = InferencePurpose.StandardEvaluation,
                    scenario = ScenarioType.Hover,
                    environmentSeed = 456
                };
                InferenceSpawnProfile manual =
                    agent.envConfig.GetInferenceSpawnProfile(ScenarioType.Hover);
                manual.altitudeMin = 500f;
                manual.altitudeMax = 600f;
                agent.envConfig.PrepareStandardEvaluation();

                typeof(FalconAgent)
                    .GetMethod("ResetEpisodeRandom", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(agent, null);
                agent.rb.isKinematic = true;
                typeof(FalconAgent)
                    .GetMethod("SpawnForScenario", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(agent, null);
                typeof(FalconAgent)
                    .GetMethod("SetEpisodePhysicsActive", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(agent, new object[] { true });

                Assert.That(rocket.transform.localPosition.y,
                    Is.EqualTo(ScenarioCatalog.HoverStartAltitude).Within(Epsilon));
                Assert.That(Mathf.Abs(rocket.transform.localPosition.x), Is.LessThanOrEqualTo(5f));
                Assert.That(Mathf.Abs(rocket.transform.localPosition.z), Is.LessThanOrEqualTo(5f));
                Assert.That(agent.rb.linearVelocity.y, Is.Zero.Within(Epsilon));
                Assert.That(Mathf.Abs(agent.rb.linearVelocity.x), Is.LessThanOrEqualTo(2f));
                Assert.That(Mathf.Abs(agent.rb.linearVelocity.z), Is.LessThanOrEqualTo(2f));
                Assert.That(new Vector2(agent.rb.linearVelocity.x, agent.rb.linearVelocity.z).magnitude,
                    Is.GreaterThan(0.01f),
                    "The sampled start motion must survive the kinematic reset boundary.");
            }
            finally
            {
                Object.DestroyImmediate(rocket);
            }
        }

        [Test]
        public void StandardEvaluatorScopeIncludesEveryFlightTask()
        {
            Assert.That(ScenarioType.Hover.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.ChopstickLanding.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.LegLanding.SupportsStandardEvaluation(), Is.True);
            Assert.That(ScenarioType.HoverTracking.SupportsStandardEvaluation(), Is.True);
        }

        [Test]
        public void EvaluationBandsContainFiftyPairedReplicatesEach()
        {
            int[] counts = new int[EvaluationConfig.CurriculumBandCount];
            for (int episode = 0; episode < EvaluationConfig.DefaultEpisodeCount; episode++)
            {
                float difficulty = EvaluationConfig.DifficultyForEpisode(episode);
                counts[EvaluationConfig.BandIndex(difficulty)]++;
            }

            Assert.That(counts, Is.All.EqualTo(EvaluationConfig.EpisodesPerCurriculumBand));
            for (int replicate = 0; replicate < EvaluationConfig.EpisodesPerCurriculumBand; replicate++)
                for (int band = 0; band < EvaluationConfig.CurriculumBandCount; band++)
                    Assert.That(EvaluationConfig.ReplicateIndexForEpisode(
                            band * EvaluationConfig.EpisodesPerCurriculumBand + replicate),
                        Is.EqualTo(replicate));
        }

        [Test]
        public void StandardHoverTrackingEvaluationHasCaptureAndTargetTimeoutEndpoints()
        {
            var environment = new SimEnvironmentConfig
            {
                behaviorType = BehaviorType.Inference,
                inferencePurpose = InferencePurpose.StandardEvaluation,
                scenario = ScenarioType.HoverTracking
            };

            environment.PrepareStandardEvaluation();

            TerminationParameters termination =
                environment.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            Assert.That(termination.trackingCaptureGoalEnabled, Is.True);
            Assert.That(termination.trackingRequiredCaptures,
                Is.EqualTo(EvaluationConfig.HoverTrackRequiredCaptures));
            Assert.That(termination.timeLimitEnabled, Is.False);
            Assert.That(environment.HoverTrackAttemptWindowSeconds,
                Is.EqualTo(EvaluationConfig.HoverTrackTargetTimeoutSeconds).Within(Epsilon));
            Assert.That(environment.HoverTrackMoveRadiusAt(0f),
                Is.EqualTo(SimEnvironmentConfig.HoverTrackStartMoveRadius).Within(Epsilon));
            Assert.That(environment.HoverTrackMoveRadiusAt(1f),
                Is.EqualTo(SimEnvironmentConfig.HoverTrackEndMoveRadius).Within(Epsilon));
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
                engineFirstIgnitionCount = 1,
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
                engineFirstIgnitionCount = 3,
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
            Assert.That(summary.meanEngineFirstIgnitionCount, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(summary.meanFinalPlanarDistanceM, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(summary.meanSuccessfulPlanarDistanceM, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(summary.decisionPeriod, Is.EqualTo(3));
        }

        [Test]
        public void EvaluatorReportsEachCurriculumBandSeparately()
        {
            var session = new EvaluationSession(
                "track_model",
                20257,
                EvaluationConfig.CurriculumBandCount,
                "C:/temp/track_episodes.csv",
                ScenarioType.HoverTracking);

            for (int band = 0; band < EvaluationConfig.CurriculumBandCount; band++)
            {
                session.Record(new TelemetryEpisodeOutcome
                {
                    completed = true,
                    success = band < EvaluationConfig.CurriculumBandCount - 1,
                    scenario = ScenarioType.HoverTracking,
                    curriculumDifficulty01 = band / (float)(EvaluationConfig.CurriculumBandCount - 1),
                    terminationReason = band < EvaluationConfig.CurriculumBandCount - 1
                        ? EpisodeTerminationReason.HoverTrackingCaptureGoal
                        : EpisodeTerminationReason.HoverTrackingTargetTimeout,
                    durationSeconds = 10f + band,
                    hoverTrackCaptures = band < EvaluationConfig.CurriculumBandCount - 1 ? 3 : 1,
                    hoverTrackRelocatedCaptures = band < EvaluationConfig.CurriculumBandCount - 1 ? 2 : 0
                });
            }

            EvaluationSummary summary = session.BuildSummary(aborted: false);
            Assert.That(summary.usesCurriculumBands, Is.True);
            Assert.That(summary.curriculumBands.Count,
                Is.EqualTo(EvaluationConfig.CurriculumBandCount));
            foreach (EvaluationDifficultyBandSummary band in summary.curriculumBands)
                Assert.That(band.completedEpisodes, Is.EqualTo(1));
            Assert.That(summary.curriculumBands[0].successRate, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(summary.curriculumBands[^1].successRate, Is.Zero.Within(Epsilon));
            Assert.That(summary.meanHoverTrackCaptures, Is.EqualTo(2.6f).Within(Epsilon));
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
                altitude: 30f,
                terminalAltitude: 0.5f,
                episodeStartAltitude: 30f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: 20f,
                curriculumDifficulty01: 1f,
                fuelKg: 0f);

            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.Hover,
                terms,
                context,
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover));

            AssertTerminal(decision, -10f, false, EpisodeTerminationReason.HoverFuelDepleted);
        }

        [Test]
        public void ScenarioCatalogContainsOnlySupportedTasks()
        {
            ScenarioType[] scenarios = ScenarioCatalog.All.Select(profile => profile.Type).ToArray();
            ScenarioType[] declaredScenarios =
                (ScenarioType[])System.Enum.GetValues(typeof(ScenarioType));
            var expectedScenarios = new[]
            {
                ScenarioType.ChopstickLanding,
                ScenarioType.LegLanding,
                ScenarioType.Hover,
                ScenarioType.HoverTracking
            };

            CollectionAssert.AreEquivalent(expectedScenarios, scenarios);
            CollectionAssert.AreEquivalent(expectedScenarios, declaredScenarios,
                "Removed tasks must not remain as hidden enum values.");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 },
                declaredScenarios.Select(value => (int)value).OrderBy(value => value).ToArray(),
                "The fresh scenario schema should not retain obsolete numeric-ID gaps.");
            Assert.That(ScenarioType.ChopstickLanding.IsLanding(), Is.True);
            Assert.That(ScenarioType.LegLanding.IsLanding(), Is.True);
            Assert.That(ScenarioCatalog.Get(ScenarioType.ChopstickLanding).DisplayName,
                Is.EqualTo("Chopstick Catch Landing"));
            Assert.That(ScenarioCatalog.Get(ScenarioType.LegLanding).DisplayName,
                Is.EqualTo("Falcon 9 Leg Landing"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ScenarioCatalog.Get((ScenarioType)99),
                "Unknown serialized values must not be migrated into a supported task.");
            ObjectiveValidationResult invalidScenario = ObjectiveValidator.Validate(
                (ScenarioType)99,
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.Hover));
            Assert.That(invalidScenario.HasErrors, Is.True);
            Assert.That(invalidScenario.issues.Any(issue => issue.code == "scenario.unsupported"), Is.True);
        }

        [Test]
        public void BuiltInVehiclePresetsCoverFullAndSimpleStartingPoints()
        {
            RocketPartsConfig falcon = Preset(RocketHardwarePreset.Falcon9);
            RocketPartsConfig simple = Preset(RocketHardwarePreset.SimpleSingle);
            RocketHardwarePreset[] presets =
                (RocketHardwarePreset[])System.Enum.GetValues(typeof(RocketHardwarePreset));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    RocketHardwarePreset.Falcon9,
                    RocketHardwarePreset.SimpleSingle,
                    RocketHardwarePreset.Custom
                },
                presets);
            Assert.That(falcon.GetEngineCount(), Is.EqualTo(9));
            Assert.That(falcon.separateEngineEnableActions, Is.False);
            Assert.That(falcon.finsEnabled, Is.True);
            Assert.That(falcon.rcsEnabled, Is.True);
            Assert.That(simple.GetEngineCount(), Is.EqualTo(1));
            Assert.That(simple.GetActiveEngineCount(), Is.EqualTo(1));
            Assert.That(simple.independentEngines, Is.False);
            Assert.That(simple.separateEngineEnableActions, Is.False);
            Assert.That(simple.finsEnabled, Is.False);
            Assert.That(simple.rcsEnabled, Is.False);
        }

        [Test]
        public void DryMassEstimateMatchesBuiltInVehicleHardware()
        {
            const float massToleranceKg = 0.01f;
            float falcon = RocketPhysicsConfigFactory.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.Falcon9));
            float simple = RocketPhysicsConfigFactory.EstimateAdjustedDryMass(Preset(RocketHardwarePreset.SimpleSingle));

            Assert.That(falcon, Is.EqualTo(22200f).Within(massToleranceKg));
            Assert.That(simple, Is.EqualTo(17390f).Within(massToleranceKg));
        }

        [Test]
        public void LegContactDefaultsToFourFeetAndNoStrikeRegardlessOfPadEdgeFlag()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile profile = environment.GetLegLandingCurriculumProfile(1f);
            TerminationParameters termination =
                environment.GetTrainingObjective(ScenarioType.LegLanding).terminations;
            var terms = new RewardTerms
            {
                planarDistance = 0.5f,
                speed = 0.5f,
                planarSpeed = 0.2f,
                verticalSpeed = -0.2f,
                angularRateDegS = 1f
            };

            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0011, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, true, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.True,
                "Stable support depends on centre error and contacts, not the pad-edge diagnostic.");

            RewardTerms offCenter = terms;
            offCenter.planarDistance = profile.successRadius + 0.1f;
            Assert.That(LegLandingContactEvaluator.IsSettledSupportCandidate(
                0b1111, false, offCenter, 1f, profile, termination.legMinimumStableFeet), Is.True,
                "A calm four-foot contact is physically settled even when it missed the target radius.");
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, false, offCenter, 1f, profile, termination.legMinimumStableFeet), Is.False,
                "Center error must still prevent touchdown success.");

            termination.legMinimumStableFeet = 3;
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.True,
                "The contact evaluator must still consume an explicit custom minimum-foot count.");

            termination.legInitialMinimumStableFeet = 3;
            termination.legMinimumStableFeet = 4;
            LandingCurriculumProfile easy = environment.GetLegLandingCurriculumProfile(0f);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0001, false, terms, 1f, easy, easy.minimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0011, false, terms, 1f, easy, easy.minimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, terms, 1f, easy, easy.minimumStableFeet), Is.True,
                "The immutable episode profile must use the configured initial foot count.");
        }

        [Test]
        public void LegGeometryFitsCenteredPadAndHardContactUsesProfileLimits()
        {
            Assert.That(
                LandingLegComponent.ReferenceFootRadiusM + LandingLegComponent.ReferenceFootEdgeMarginM,
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
        public void LegPadCurriculumResizesFromOriginalGeometryWithoutAccumulatingScale()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                pad.transform.localScale = new Vector3(20f, 1f, 20f);
                LandingPadSurface surface = LandingPadSurface.Ensure(pad.transform);

                surface.SetFootprintHalfSize(25f);
                Assert.That(surface.SurfaceCollider.bounds.extents.x, Is.EqualTo(25f).Within(0.001f));
                Assert.That(surface.SurfaceCollider.bounds.extents.z, Is.EqualTo(25f).Within(0.001f));

                surface.SetFootprintHalfSize(25f);
                surface.SetFootprintHalfSize(25f);
                Assert.That(surface.SurfaceCollider.bounds.extents.x, Is.EqualTo(25f).Within(0.001f));
                Assert.That(surface.SurfaceCollider.bounds.extents.z, Is.EqualTo(25f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(pad);
            }
        }

        [Test]
        public void LegLandingContactMaximumThresholdsAreInclusiveAtEquality()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile profile = environment.GetLegLandingCurriculumProfile(1f);
            TerminationParameters termination =
                environment.GetTrainingObjective(ScenarioType.LegLanding).terminations;
            var boundaryTerms = new RewardTerms
            {
                planarDistance = profile.successRadius,
                speed = profile.successMaxSpeed,
                planarSpeed = profile.successMaxHorizontalSpeed,
                verticalSpeed = -profile.successMaxVerticalSpeed,
                angularRateDegS = profile.successMaxAngularRateDegS
            };

            Assert.That(LegLandingContactEvaluator.IsFirstContactSafe(
                totalSpeed: profile.successMaxSpeed,
                verticalSpeed: -profile.successMaxVerticalSpeed,
                horizontalSpeed: profile.successMaxHorizontalSpeed,
                tiltDeg: profile.successMaxTiltDeg,
                angularRateDegS: profile.successMaxAngularRateDegS,
                profile: profile), Is.True,
                "Every first-contact value equal to its labeled maximum must remain safe.");
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                footMask: 0b1111,
                structuralStrike: false,
                terms: boundaryTerms,
                tiltDeg: profile.successMaxTiltDeg,
                profile: profile,
                minimumStableFeet: termination.legMinimumStableFeet), Is.True,
                "Target radius and every stable-touchdown maximum must include equality.");
            Assert.That(LegLandingContactEvaluator.IsExcessiveRebound(
                reboundRiseM: termination.legMaximumReboundRiseM,
                allFeetContactLossSeconds: termination.legMaximumAllFeetContactLossSeconds,
                maximumReboundRiseM: termination.legMaximumReboundRiseM,
                maximumAllFeetContactLossSeconds: termination.legMaximumAllFeetContactLossSeconds),
                Is.False,
                "Rebound and contact-loss values equal to their maximums must remain accepted.");
        }

        [Test]
        public void GeneratedLandingGearCollidersResolveTheirOwningAgent()
        {
            var root = new GameObject("GeneratedLandingGearRelayTest");
            try
            {
                FalconAgent owner = root.AddComponent<FalconAgent>();
                LandingLegComponent legs = LandingLegComponent.Ensure(root.transform);
                legs.Configure(true, Falcon9Reference.BodyRadiusM, Falcon9Reference.BodyHeightM);
                LandingGearCollider[] markers = root.GetComponentsInChildren<LandingGearCollider>();

                Assert.That(markers.Length, Is.EqualTo(LandingLegComponent.LegCount * 2));
                foreach (LandingGearCollider marker in markers)
                    Assert.That(marker.Owner, Is.SameAs(owner));

                legs.Configure(false, Falcon9Reference.BodyRadiusM, Falcon9Reference.BodyHeightM);

                Assert.That(root.activeSelf, Is.True,
                    "Disabling task-specific legs must never disable the rocket root.");
                Assert.That(owner.isActiveAndEnabled, Is.True,
                    "The ML-Agents lifecycle must remain active while leg geometry is hidden.");
                foreach (LandingGearCollider marker in markers)
                    Assert.That(marker.gameObject.activeInHierarchy, Is.False,
                        "Disabled leg geometry must not produce contacts in another task.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LegReboundRequiresEitherMeaningfulRiseOrSustainedContactLoss()
        {
            TerminationParameters termination = ScenarioObjectiveConfig
                .CreateDefault(ScenarioType.LegLanding)
                .terminations;
            Assert.That(LegLandingContactEvaluator.IsExcessiveRebound(
                termination.legMaximumReboundRiseM,
                termination.legMaximumAllFeetContactLossSeconds,
                termination.legMaximumReboundRiseM,
                termination.legMaximumAllFeetContactLossSeconds), Is.False);
            Assert.That(LegLandingContactEvaluator.IsExcessiveRebound(
                termination.legMaximumReboundRiseM + 0.01f,
                0f,
                termination.legMaximumReboundRiseM,
                termination.legMaximumAllFeetContactLossSeconds), Is.True);
            Assert.That(LegLandingContactEvaluator.IsExcessiveRebound(
                0f,
                termination.legMaximumAllFeetContactLossSeconds + 0.01f,
                termination.legMaximumReboundRiseM,
                termination.legMaximumAllFeetContactLossSeconds), Is.True);

            Assert.That(LegLandingContactEvaluator.IsExcessiveRebound(
                reboundRiseM: 0.75f,
                allFeetContactLossSeconds: 0.40f,
                maximumReboundRiseM: 1f,
                maximumAllFeetContactLossSeconds: 0.5f), Is.False,
                "Raising both configured limits must change the contact verdict without code changes.");
        }

        [Test]
        public void LegLandingRewardUsesContactDrivenTerminalOutcomes()
        {
            RewardTerms terms = SafeLegTerms();
            RewardDecision airborneAtPadPlane = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            RewardDecision stable = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f, touchdown: true, stable: true, becameStable: true),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            RewardDecision strike = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0f, touchdown: true, structuralStrike: true),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            RewardDecision rebound = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 0.6f, touchdown: true, excessiveRebound: true),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));

            Assert.That(airborneAtPadPlane.endEpisode, Is.False,
                "Crossing the pad plane must not replace physical foot contact.");
            Assert.That(stable.endEpisode, Is.True);
            Assert.That(stable.successTerminal, Is.True);
            Assert.That(stable.terminationReason,
                Is.EqualTo(EpisodeTerminationReason.LegLandingSuccessfulTouchdown));
            Assert.That(stable.terminalReward, Is.InRange(7.5f, 34f));
            Assert.That(stable.eventReward, Is.EqualTo(2f).Within(Epsilon));
            AssertTerminal(strike, -25f, false, EpisodeTerminationReason.LegLandingStructuralStrike);
            AssertTerminal(rebound, -25f, false, EpisodeTerminationReason.LegLandingExcessiveRebound);
        }

        [Test]
        public void OffPadImpactEndsImmediatelyAsMissedPadBeforeFlyaway()
        {
            RewardTerms terms = SafeLegTerms();
            terms.planarDistance = 151f;
            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(altitude: 100f, offPadImpact: true),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));

            AssertTerminal(
                decision,
                -50f,
                false,
                EpisodeTerminationReason.LegLandingMissedPad);
            Assert.That(decision.eventReward, Is.Zero.Within(Epsilon),
                "Terrain contact must not earn the pad first-contact event.");
        }

        [Test]
        public void SettledOffCenterLandingEndsAsMissedPadInsteadOfTimingOut()
        {
            RewardTerms terms = SafeLegTerms();
            terms.planarDistance = 7f;
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);

            RewardDecision stillSettling = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, firstContactThisStep: false),
                objective);
            RewardDecision settled = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, firstContactThisStep: false,
                    supportSettled: true),
                objective);

            Assert.That(stillSettling.endEpisode, Is.False,
                "A contact still moving toward four-foot support needs its short settling window.");
            AssertTerminal(
                settled,
                -50f,
                false,
                EpisodeTerminationReason.LegLandingMissedPad);
        }

        [Test]
        public void FirstContactAloneDoesNotEarnACompletionMilestone()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardTerms terms = SafeLegTerms();
            RewardDecision slowUpright = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true,
                    curriculumDifficulty01: 0f,
                    firstContactSpeed: 1f,
                    firstContactVerticalSpeed: -1f,
                    firstContactTiltDeg: 1f),
                objective);
            RewardDecision fastUpright = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true,
                    curriculumDifficulty01: 0f,
                    firstContactSpeed: 4.9f,
                    firstContactVerticalSpeed: -3.9f,
                    firstContactTiltDeg: 1f),
                objective);
            RewardDecision slowTilted = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true,
                    curriculumDifficulty01: 0f,
                    firstContactSpeed: 1f,
                    firstContactVerticalSpeed: -1f,
                    firstContactTiltDeg: 11.9f),
                objective);

            Assert.That(slowUpright.endEpisode, Is.False);
            Assert.That(fastUpright.endEpisode, Is.False);
            Assert.That(slowTilted.endEpisode, Is.False);
            Assert.That(slowUpright.eventReward, Is.Zero.Within(Epsilon));
            Assert.That(fastUpright.eventReward, Is.Zero.Within(Epsilon));
            Assert.That(slowTilted.eventReward, Is.Zero.Within(Epsilon));
        }

        [Test]
        public void SuccessfulTouchdownGradesSmoothnessAndCenterAccuracy()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardTerms centered = SafeLegTerms();
            centered.planarDistance = 0f;
            RewardDecision slowUpright = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                centered,
                LegContext(0f, touchdown: true, stable: true,
                    curriculumDifficulty01: 0f,
                    firstContactSpeed: 0.3f,
                    firstContactVerticalSpeed: -0.2f,
                    firstContactTiltDeg: 0.5f),
                objective);
            RewardTerms edge = centered;
            edge.planarDistance = 5.9f;
            RewardDecision marginal = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                edge,
                LegContext(0f, touchdown: true, stable: true,
                    curriculumDifficulty01: 0f,
                    firstContactSpeed: 4.9f,
                    firstContactVerticalSpeed: -3.9f,
                    firstContactTiltDeg: 11.9f),
                objective);

            Assert.That(slowUpright.terminationReason,
                Is.EqualTo(EpisodeTerminationReason.LegLandingSuccessfulTouchdown));
            Assert.That(marginal.terminationReason,
                Is.EqualTo(EpisodeTerminationReason.LegLandingSuccessfulTouchdown),
                "A marginal contact inside the curriculum limits should remain a success.");
            Assert.That(slowUpright.terminalReward, Is.GreaterThan(marginal.terminalReward));
            Assert.That(slowUpright.terminalReward, Is.LessThanOrEqualTo(34f));
            Assert.That(marginal.terminalReward, Is.GreaterThanOrEqualTo(7.5f));
        }

        [Test]
        public void LegSuccessRequiresFourFeetPropulsionOffAndTheCompleteHold()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardTerms terms = SafeLegTerms();

            RewardDecision threeFeet = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true, feetOnPad: 3),
                objective);
            RewardDecision propulsionRunning = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true, propulsionOff: false),
                objective);
            RewardDecision shortHold = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true, stableTimeSeconds: 0.99f),
                objective);
            RewardDecision complete = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true),
                objective);

            Assert.That(threeFeet.endEpisode, Is.False);
            Assert.That(propulsionRunning.endEpisode, Is.False);
            Assert.That(shortHold.endEpisode, Is.False);
            Assert.That(complete.successTerminal, Is.True);
            Assert.That(complete.terminationReason,
                Is.EqualTo(EpisodeTerminationReason.LegLandingSuccessfulTouchdown));
        }

        [Test]
        public void LegFootSupportQualityFavorsTripodAndCapsAtFourFeet()
        {
            Assert.That(RocketRewardModel.LegFootSupportQuality01(0), Is.Zero.Within(Epsilon));
            Assert.That(RocketRewardModel.LegFootSupportQuality01(1), Is.EqualTo(0.10f).Within(Epsilon));
            Assert.That(RocketRewardModel.LegFootSupportQuality01(2), Is.EqualTo(0.35f).Within(Epsilon));
            Assert.That(RocketRewardModel.LegFootSupportQuality01(3), Is.EqualTo(0.90f).Within(Epsilon));
            Assert.That(RocketRewardModel.LegFootSupportQuality01(4), Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void MissionEfficiencyBonusGradesOnlySuccessfulLandings()
        {
            RewardTerms terms = SafeLegTerms();
            terms.planarDistance = 0f;
            ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardDecision efficientInitialSuccess = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true, curriculumDifficulty01: 0f, fuelFraction01: 1f),
                objective);
            RewardDecision fullSuccess = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true, curriculumDifficulty01: 1f, fuelFraction01: 0.96f),
                objective);
            RewardDecision flyaway = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(401f, curriculumDifficulty01: 1f, fuelFraction01: 1f),
                objective);
            RewardDecision inefficientFlyaway = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(401f, curriculumDifficulty01: 1f, fuelFraction01: 0.1f,
                    episodeEngineRestartCount: 50, engineFirstIgnitionCount: 3),
                objective);

            AssertTerminal(efficientInitialSuccess, 34f, true, EpisodeTerminationReason.LegLandingSuccessfulTouchdown);
            float expectedFullReward = 30f + 4f * Mathf.Exp(-0.04f / 0.28f);
            AssertTerminal(fullSuccess, expectedFullReward, true, EpisodeTerminationReason.LegLandingSuccessfulTouchdown);
            AssertTerminal(flyaway, -50f, false, EpisodeTerminationReason.LegLandingAboveAltitudeLimit);
            AssertTerminal(inefficientFlyaway, -50f, false, EpisodeTerminationReason.LegLandingAboveAltitudeLimit);
        }

        [Test]
        public void MissionEfficiencyPrefersFuelAndFewRelightsWithoutPrescribingEngineCount()
        {
            RewardTerms terms = SafeLegTerms();
            terms.planarDistance = 0f;
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);

            RewardDecision oneEngine = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true,
                    curriculumDifficulty01: 1f, fuelFraction01: 0.82f,
                    engineFirstIgnitionCount: 1),
                objective);
            RewardDecision threeEnginesOnce = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true,
                    curriculumDifficulty01: 1f, fuelFraction01: 0.82f,
                    engineFirstIgnitionCount: 3),
                objective);
            RewardDecision repeatedRelights = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, stable: true,
                    curriculumDifficulty01: 1f, fuelFraction01: 0.82f,
                    episodeEngineRestartCount: 10,
                    engineFirstIgnitionCount: 3),
                objective);

            Assert.That(oneEngine.terminalReward,
                Is.GreaterThan(threeEnginesOnce.terminalReward));
            Assert.That(threeEnginesOnce.terminalReward,
                Is.GreaterThan(repeatedRelights.terminalReward));
            Assert.That(oneEngine.terminalReward - threeEnginesOnce.terminalReward,
                Is.LessThan(threeEnginesOnce.terminalReward - repeatedRelights.terminalReward),
                "Lighting both braking engines once should be much cheaper than repeated PWM relights.");
        }

        [Test]
        public void MissionEfficiencyRemainsSmoothAboveItsNominalCostScale()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardRuntimeContext lowerCost = LegContext(
                0f, curriculumDifficulty01: 0f, fuelFraction01: 0.70f,
                episodeEngineRestartCount: 2, engineFirstIgnitionCount: 3);
            RewardRuntimeContext higherCost = LegContext(
                0f, curriculumDifficulty01: 0f, fuelFraction01: 0.60f,
                episodeEngineRestartCount: 2, engineFirstIgnitionCount: 3);

            float lowerCostQuality = RocketRewardModel.LegMissionEfficiency01(
                lowerCost, objective.shaping);
            float higherCostQuality = RocketRewardModel.LegMissionEfficiency01(
                higherCost, objective.shaping);

            Assert.That(lowerCostQuality, Is.GreaterThan(higherCostQuality));
            Assert.That(higherCostQuality, Is.GreaterThan(0f),
                "Efficiency must not become a flat zero after crossing a hard fuel budget.");
        }

        [Test]
        public void RestoredLegGuidanceUsesTheBallisticDescentProfile()
        {
            ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingDescentProfileErrorCostRate = 0.04f;
            RewardTerms onProfile = SafeLegTerms();
            onProfile.verticalSpeed = -Mathf.Sqrt(2f * 9.80665f * 250f) * 0.30f;
            RewardTerms climbing = onProfile;
            climbing.verticalSpeed = 3.5f;

            RewardDecision onProfileDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, onProfile, LegContext(250f), objective);
            RewardDecision climbingDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, climbing, LegContext(250f), objective);

            Assert.That(onProfileDecision.shapingRate - climbingDecision.shapingRate,
                Is.EqualTo(0.04f).Within(Epsilon));
        }

        [Test]
        public void RestoredLegGuidancePenalizesOffProfileDescentSpeed()
        {
            ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingDescentProfileErrorCostRate = 1f;
            RewardTerms terms = SafeLegTerms();
            terms.verticalSpeed = 0f;

            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, terms, LegContext(250f), objective);

            Assert.That(decision.shapingRate, Is.EqualTo(-1f).Within(Epsilon));
        }

        [Test]
        public void LegClosureRewardUsesHorizontalNavigationNotPassiveVerticalFall()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingGoalClosureRewardRate = 1f;

            RewardTerms verticalFall = SafeLegTerms();
            verticalFall.goalClosureRate = 30f;
            verticalFall.horizontalClosureRate = 0f;
            RewardTerms navigating = verticalFall;
            navigating.horizontalClosureRate =
                objective.shaping.landingClosureMinimumScaleMps;

            RewardDecision fallDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, verticalFall, LegContext(250f), objective);
            RewardDecision navigationDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, navigating, LegContext(250f), objective);

            Assert.That(fallDecision.shapingRate, Is.Zero.Within(Epsilon));
            Assert.That(navigationDecision.shapingRate, Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void LegLandingPenalizesBodyAxisSpinAtAltitudeWithoutRequiringAHeading()
        {
            RewardTerms calmTerms = SafeLegTerms();
            calmTerms.yawRateDegS = 0f;
            calmTerms.yawErrorDeg = 150f;
            RewardTerms spinningTerms = calmTerms;
            spinningTerms.yawRateDegS = 20f;

            RewardDecision calm = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                calmTerms,
                LegContext(altitude: 250f),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            RewardDecision spinning = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                spinningTerms,
                LegContext(altitude: 250f),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));

            Assert.That(calm.endEpisode, Is.False);
            Assert.That(spinning.endEpisode, Is.False);
            Assert.That(calm.shapingRate - spinning.shapingRate,
                Is.EqualTo(0.025f).Within(Epsilon));
        }

        [Test]
        public void AirborneTiltIsShapedWithoutProvidingAnEasyReset()
        {
            RewardTerms tilted = SafeLegTerms();
            tilted.upDot = Mathf.Cos(80f * Mathf.Deg2Rad);
            tilted.upright01 = Mathf.Clamp01(tilted.upDot);

            RewardTerms upright = tilted;
            upright.upDot = 1f;
            upright.upright01 = 1f;

            RewardDecision tiltedDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                tilted,
                LegContext(altitude: 250f),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));
            RewardDecision uprightDecision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                upright,
                LegContext(altitude: 250f),
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding));

            Assert.That(tiltedDecision.endEpisode, Is.False);
            Assert.That(uprightDecision.shapingRate, Is.GreaterThan(tiltedDecision.shapingRate));
        }

        [Test]
        public void UnsafeImpactAndEscapeHaveDistinctCostsWithoutContactReward()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            RewardTerms hardContactTerms = SafeLegTerms();
            RewardRuntimeContext hardContactContext =
                LegContext(0f, touchdown: true, curriculumDifficulty01: 0f,
                    firstContactSpeed: 84f);
            RewardDecision hardContact = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, hardContactTerms, hardContactContext, objective);

            RewardTerms flyawayTerms = SafeLegTerms();
            flyawayTerms.planarDistance = 151f;
            RewardDecision flyaway = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, flyawayTerms, LegContext(250f), objective);

            AssertTerminal(hardContact, -45f, false, EpisodeTerminationReason.LegLandingHardTouchdown);
            Assert.That(hardContact.eventReward, Is.Zero.Within(Epsilon));
            AssertTerminal(flyaway, -50f, false, EpisodeTerminationReason.LegLandingTooFarFromTarget);
        }

        [Test]
        public void LegImpactSeveritySeparatesControlledNearMissFromFastCrash()
        {
            ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.legHardTouchdownCost = 12f;
            objective.rewards.legImpactSeverityCost = 18f;
            RewardTerms terms = SafeLegTerms();
            var nearContributions = new RewardContributionBuffer();
            var moderateContributions = new RewardContributionBuffer();
            var fastContributions = new RewardContributionBuffer();
            RewardDecision near = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, curriculumDifficulty01: 0f, firstContactSpeed: 7.01f),
                objective,
                nearContributions);
            RewardDecision moderate = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, curriculumDifficulty01: 0f, firstContactSpeed: 14f),
                objective,
                moderateContributions);
            RewardDecision fast = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, curriculumDifficulty01: 0f, firstContactSpeed: 84f),
                objective,
                fastContributions);

            Assert.That(near.terminationReason, Is.EqualTo(EpisodeTerminationReason.LegLandingHardTouchdown));
            Assert.That(near.terminalReward, Is.LessThan(-12f).And.GreaterThan(-12.1f));
            Assert.That(moderate.terminalReward,
                Is.LessThan(near.terminalReward).And.GreaterThan(fast.terminalReward));
            AssertTerminal(fast, -30f, false, EpisodeTerminationReason.LegLandingHardTouchdown);
            Assert.That(nearContributions[RewardParameterId.LegImpactSeverityCost],
                Is.GreaterThan(-0.1f));
            Assert.That(moderateContributions[RewardParameterId.LegImpactSeverityCost],
                Is.EqualTo(-9f).Within(Epsilon));
            Assert.That(fastContributions[RewardParameterId.LegImpactSeverityCost],
                Is.EqualTo(-18f).Within(Epsilon));
        }

        [Test]
        public void LegImpactSeverityAppliesToEveryPhysicalContactFailureRoute()
        {
            ScenarioObjectiveConfig objective = ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.legStructuralStrikeCost = 15f;
            objective.rewards.legFootOutsidePadCost = 12f;
            objective.rewards.legExcessiveReboundCost = 12f;
            objective.rewards.legImpactSeverityCost = 18f;
            objective.terminations.legFootOutsidePadEnabled = true;
            RewardTerms terms = SafeLegTerms();
            RewardDecision structural = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, structuralStrike: true, curriculumDifficulty01: 0f, firstContactSpeed: 84f),
                objective);
            RewardDecision outside = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, footOutsidePad: true,
                    curriculumDifficulty01: 0f, firstContactSpeed: 84f),
                objective);
            RewardDecision rebound = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, excessiveRebound: true,
                    curriculumDifficulty01: 0f, firstContactSpeed: 84f,
                    firstContactThisStep: false),
                objective);

            AssertTerminal(structural, -33f, false, EpisodeTerminationReason.LegLandingStructuralStrike);
            AssertTerminal(outside, -30f, false, EpisodeTerminationReason.LegLandingFootOutsidePad);
            AssertTerminal(rebound, -30f, false, EpisodeTerminationReason.LegLandingExcessiveRebound);
        }

        [Test]
        public void LandingCenteringPotentialDependsOnlyOnPlanarDistance()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            ScenarioObjectiveConfig objective = environment.GetTrainingObjective(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingReadinessProgressRewardRate = 1f;
            RewardTerms controlled = SafeLegTerms();
            controlled.planarDistance = 8f;
            RewardTerms fallingAndTilted = controlled;
            fallingAndTilted.speed = 35f;
            fallingAndTilted.verticalSpeed = -35f;
            fallingAndTilted.planarSpeed = 8f;
            fallingAndTilted.upDot = Mathf.Cos(12f * Mathf.Deg2Rad);
            fallingAndTilted.angularRateDegS = 40f;

            float controlledPotential = RocketRewardModel.LegLandingCenteringPotential(
                controlled, objective.shaping);
            float fallingPotential = RocketRewardModel.LegLandingCenteringPotential(
                fallingAndTilted, objective.shaping);
            Assert.That(fallingPotential, Is.EqualTo(controlledPotential).Within(Epsilon),
                "Descent and approach quality must not manufacture or erase centering progress.");

            RewardTerms closer = fallingAndTilted;
            closer.planarDistance = 1f;
            RewardTerms farther = fallingAndTilted;
            farther.planarDistance = 20f;
            Assert.That(
                RocketRewardModel.LegLandingCenteringPotential(closer, objective.shaping),
                Is.GreaterThan(RocketRewardModel.LegLandingCenteringPotential(
                    farther, objective.shaping)));

            RewardRuntimeContext progress = LegContext(10f);
            progress = new RewardRuntimeContext(
                altitude: progress.altitude,
                terminalAltitude: progress.terminalAltitude,
                episodeStartAltitude: progress.episodeStartAltitude,
                gravityMagnitude: progress.gravityMagnitude,
                episodeElapsedSeconds: progress.episodeElapsedSeconds,
                curriculumDifficulty01: 0f,
                fuelKg: progress.fuelKg,
                legLandingCenteringProgressRate: 0.5f);
            RewardDecision decision = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, controlled, progress, objective);
            Assert.That(decision.shapingRate, Is.GreaterThan(0f));
        }

        [Test]
        public void ReadinessProgressCannotRewardThePostImpactVelocityDrop()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingReadinessProgressRewardRate = 2f;
            RewardTerms terms = SafeLegTerms();

            RewardDecision airborne = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(10f, readinessProgressRate: 0.5f),
                objective);
            RewardDecision impact = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding,
                terms,
                LegContext(0f, touchdown: true, firstContactThisStep: false,
                    readinessProgressRate: 0.5f),
                objective);

            Assert.That(airborne.shapingRate, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(impact.shapingRate, Is.Zero.Within(Epsilon),
                "Physics stopping the body on impact must not look like policy-made readiness progress.");
        }

        [Test]
        public void UprightShapingStrengthensNearThePadWithoutForbiddingSteeringAloft()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.rewards.Clear();
            objective.rewards.landingUprightErrorCostRate = 1f;
            RewardTerms tilted = SafeLegTerms();
            tilted.upDot = Mathf.Cos(6f * Mathf.Deg2Rad);
            tilted.upright01 = tilted.upDot;

            RewardDecision aloft = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, tilted, LegContext(250f), objective);
            RewardDecision nearPad = RocketRewardModel.Evaluate(
                ScenarioType.LegLanding, tilted, LegContext(1f), objective);

            Assert.That(aloft.shapingRate, Is.LessThan(0f));
            Assert.That(nearPad.shapingRate, Is.LessThan(aloft.shapingRate));
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
                Is.EqualTo(LandingCurriculumMode.Adaptive));
            Assert.That(environment.legLandingCurriculumProgress, Is.Zero.Within(Epsilon));
            Assert.That(environment.landingCurriculumLinearProgress, Is.EqualTo(0.6f).Within(Epsilon));
            Assert.That(environment.StandardEvaluationDifficultyForEpisode(200),
                Is.EqualTo(1f).Within(Epsilon));
            Assert.That(environment.GetLegLandingCurriculumProfile(1f).platformStableHoldTime,
                Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void ScenarioFuelUpdateDoesNotChangeSelectedEngineConfiguration()
        {
            RocketPartsConfig vehicle = Preset(RocketHardwarePreset.Falcon9);
            vehicle.octawebBurnGroup = OctawebBurnGroup.AllNine;
            vehicle.independentEngines = false;

            vehicle.ApplyScenarioHardwareDefaults(ScenarioType.LegLanding);

            Assert.That(vehicle.octawebBurnGroup, Is.EqualTo(OctawebBurnGroup.AllNine));
            Assert.That(vehicle.independentEngines, Is.False);
            Assert.That(vehicle.startFuelFraction,
                Is.EqualTo(ScenarioProfile.StartFuelFraction(ScenarioType.LegLanding)).Within(Epsilon));
        }

        [Test]
        public void VehiclePresetNamesProtectBuiltInsAndRejectBlankNames()
        {
            Assert.That(VehiclePresetRepository.IsBuiltInName("falcon 9"), Is.True);
            Assert.That(VehiclePresetRepository.IsBuiltInName(" SIMPLE "), Is.True);
            Assert.That(VehiclePresetRepository.TryNormalizeName(
                "  My landing vehicle  ", out string normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("My landing vehicle"));
            Assert.That(VehiclePresetRepository.TryNormalizeName(
                "   ", out _, out string error), Is.False);
            Assert.That(error, Does.Contain("Enter a name"));
        }

        static void AssertResumeRejected(
            ResumeContractFixture fixture,
            SimEnvironmentConfig environment,
            RocketPartsConfig parts,
            MLAgentsConfig ml,
            string expectedCategory)
        {
            bool accepted = fixture.TryValidate(environment, parts, ml, out string error);

            Assert.That(accepted, Is.False);
            Assert.That(error, Does.Contain(expectedCategory));
        }

        sealed class ResumeContractFixture : System.IDisposable
        {
            readonly bool _safeToDelete;

            public readonly string RunId;
            public readonly string RunRoot;
            public readonly SimEnvironmentConfig SavedEnvironment;
            public readonly RocketPartsConfig SavedParts;
            public readonly MLAgentsConfig SavedMl;

            public ResumeContractFixture()
            {
                string resultsRoot = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "results"));
                RunId = "__rocketsim_resume_contract_test_" + System.Guid.NewGuid().ToString("N");
                RunRoot = Path.GetFullPath(Path.Combine(resultsRoot, RunId));
                string safePrefix = resultsRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                _safeToDelete = RunRoot.StartsWith(
                    safePrefix,
                    System.StringComparison.OrdinalIgnoreCase);
                Assert.That(_safeToDelete, Is.True,
                    "The test run must stay inside the project results directory.");

                SavedEnvironment = new SimEnvironmentConfig
                {
                    scenario = ScenarioType.Hover,
                    behaviorType = BehaviorType.Training,
                    runId = RunId,
                    trainingObjective = new TrainingObjectiveConfig()
                };
                SavedEnvironment.EnsureTrainingObjective();
                SavedParts = new RocketPartsConfig();
                SavedMl = new MLAgentsConfig();

                try
                {
                    SaveTrainingLaunch(SavedEnvironment, resumed: false);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public SimEnvironmentConfig CloneEnvironment()
            {
                SimEnvironmentConfig clone =
                    JsonUtility.FromJson<SimEnvironmentConfig>(JsonUtility.ToJson(SavedEnvironment));
                clone.trainingObjective = JsonUtility.FromJson<TrainingObjectiveConfig>(
                    JsonUtility.ToJson(SavedEnvironment.EnsureTrainingObjective()));
                return clone;
            }

            public RocketPartsConfig CloneParts() =>
                JsonUtility.FromJson<RocketPartsConfig>(JsonUtility.ToJson(SavedParts));

            public MLAgentsConfig CloneMl() =>
                JsonUtility.FromJson<MLAgentsConfig>(JsonUtility.ToJson(SavedMl));

            public bool TryValidate(
                SimEnvironmentConfig environment,
                RocketPartsConfig parts,
                MLAgentsConfig ml,
                out string error)
            {
                return SimulationRunService.TryValidateTrainingDestination(
                    RunId,
                    true,
                    environment,
                    parts,
                    ml,
                    out error);
            }

            public void SaveTrainingLaunch(
                SimEnvironmentConfig environment,
                bool resumed)
            {
                SimulationRunService.SaveTrainingLaunch(
                    SimulationSessionConfig.Capture(
                        CloneParts(),
                        environment,
                        CloneMl(),
                        new TelemetryConfig()),
                    new RunLaunchRequest(
                        RunId,
                        resumed ? RunLaunchMode.ResumeTraining : RunLaunchMode.NewTraining),
                    new TrainingEnvironmentProvenance(),
                    "cpu");
            }

            public TrainingObjectiveConfig LoadTrainingObjective()
            {
                return SimulationRunService.LoadTrainingObjective(RunId);
            }

            public SimulationSessionConfig LoadSessionRevision(int revision) =>
                SimulationSessionStore.LoadRevision(RunId, revision);

            public void Dispose()
            {
                if (_safeToDelete && Directory.Exists(RunRoot))
                    Directory.Delete(RunRoot, true);
            }
        }

        static RocketPartsConfig Preset(RocketHardwarePreset preset)
        {
            var config = new RocketPartsConfig();
            config.ApplyPreset(preset);
            return config;
        }

        static RewardTerms SafeHoverTerms() => new()
        {
            verticalError = 0f,
            planarDistance = 0f,
            speed = 0f,
            planarSpeed = 0f,
            verticalSpeed = 0f,
            horizontalClosureRate = 0f,
            upDot = 1f,
            upright01 = 1f,
            angularRateDegS = 0f,
            controlEffort = 0f
        };

        static RewardRuntimeContext HoverContext(
            float altitude = 30f,
            bool targetCaptured = false,
            int captureCount = 0,
            float fuelKg = 1000f)
        {
            return new RewardRuntimeContext(
                altitude: altitude,
                terminalAltitude: 0.5f,
                episodeStartAltitude: 30f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: 1f,
                curriculumDifficulty01: 1f,
                fuelKg: fuelKg,
                hoverTrackTargetCapturedThisStep: targetCaptured,
                hoverTrackEpisodeCaptures: captureCount);
        }

        static ScenarioObjectiveConfig VersionEightBalancedLegObjective()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.schemaVersion = 8;

            objective.rewards.Clear();
            objective.rewards.landingGoalClosureRewardRate = 0.060f;
            objective.rewards.landingDescentProfileErrorCostRate = 0.040f;
            objective.rewards.landingPlanarDistanceCostRate = 0.025f;
            objective.rewards.landingUprightErrorCostRate = 0.020f;
            objective.rewards.landingNearTargetPlanarSpeedCostRate = 0.025f;
            objective.rewards.landingNearTargetAngularRateCostRate = 0.015f;
            objective.rewards.landingYawSpinCostRate = 0.040f;
            objective.rewards.controlEffortCostRate = 0.005f;
            objective.rewards.timeCostRate = 0.100f;
            objective.rewards.firstFootContactReward = 0.25f;
            objective.rewards.stableTouchdownReward = 1f;
            objective.rewards.legSuccessfulTouchdownReward = 10f;
            objective.rewards.legHardTouchdownCost = 5f;
            objective.rewards.legStructuralStrikeCost = 5f;
            objective.rewards.legFootOutsidePadCost = 5f;
            objective.rewards.legExcessiveReboundCost = 5f;
            objective.rewards.legUnsafeAttitudeCost = 5f;
            objective.rewards.legTooFarFromTargetCost = 5f;
            objective.rewards.legFuelDepletedCost = 5f;
            objective.rewards.legAboveAltitudeLimitCost = 5f;
            objective.rewards.legMissedPadCost = 5f;
            objective.rewards.legTimeLimitCost = 5f;

            objective.shaping.Clear();
            objective.shaping.landingBallisticDescentFraction = 0.30f;
            objective.shaping.landingDescentErrorMinimumScaleMps = 5f;
            objective.shaping.landingClosureMinimumScaleMps = 5f;
            objective.shaping.landingPlanarDistanceFalloffM = 25f;
            objective.shaping.landingNearTargetAltitudeFalloffM = 50f;
            objective.shaping.landingPlanarSpeedScaleMps = 5f;
            objective.shaping.landingVerticalSpeedExcessScaleMps = 5f;
            objective.shaping.landingAngularRateScaleDegS = 60f;
            objective.shaping.landingUpwardVelocityToleranceMps = 0.5f;
            objective.shaping.landingUpwardVelocityScaleMps = 3f;
            objective.shaping.legFuelEfficiencyStartDifficulty = 0.5f;
            objective.shaping.legFuelEfficiencyFullDifficulty = 1f;
            objective.shaping.legFuelEfficiencyBudgetFraction = 0.06f;
            objective.shaping.legTouchdownQualityRewardFraction = 0f;
            objective.shaping.landingYawSpinScaleDegS = 20f;

            objective.terminations.unsafeAttitudeEnabled = true;
            objective.terminations.maximumEpisodeSeconds = 120f;
            objective.terminations.landingSuccessRadiusM = new DifficultyRange(8f, 2f);
            objective.terminations.landingSuccessMaxTotalSpeedMps = new DifficultyRange(7f, 2.5f);
            objective.terminations.landingSuccessMaxVerticalSpeedMps = new DifficultyRange(5f, 2f);
            objective.terminations.landingSuccessMaxHorizontalSpeedMps = new DifficultyRange(5f, 1f);
            objective.terminations.landingSuccessMaxTiltDeg = new DifficultyRange(20f, 5f);
            objective.terminations.landingSuccessMaxAngularRateDegS = new DifficultyRange(50f, 25f);
            objective.terminations.landingStableHoldSeconds = new DifficultyRange(0.25f, 1f);
            objective.terminations.legInitialMinimumStableFeet = 3;
            objective.terminations.legMinimumStableFeet = 3;
            return objective;
        }

        static ScenarioObjectiveConfig VersionTwelveBalancedLegObjective()
        {
            ScenarioObjectiveConfig objective = VersionThirteenBalancedLegObjective();
            objective.schemaVersion = 12;
            objective.rewards.landingGoalClosureRewardRate = 0.250f;
            objective.rewards.landingUprightErrorCostRate = 0.040f;
            objective.rewards.landingNearTargetAngularRateCostRate = 0.030f;
            objective.rewards.legHardTouchdownCost = 10f;
            objective.rewards.legImpactSeverityCost = 30f;
            objective.rewards.legStructuralStrikeCost = 10f;
            objective.rewards.legFootOutsidePadCost = 10f;
            objective.rewards.legExcessiveReboundCost = 10f;
            return objective;
        }

        static ScenarioObjectiveConfig VersionThirteenBalancedLegObjective()
        {
            ScenarioObjectiveConfig objective = VersionFourteenBalancedLegObjective();
            objective.schemaVersion = 13;
            objective.rewards.landingDescentProfileErrorCostRate = 0.050f;
            objective.rewards.landingReadinessProgressRewardRate = 2f;
            objective.shaping.legFuelEfficiencyStartDifficulty = 0.6f;
            objective.shaping.legFuelEfficiencyFullDifficulty = 1f;
            return objective;
        }

        static ScenarioObjectiveConfig VersionFourteenBalancedLegObjective()
        {
            ScenarioObjectiveConfig objective = VersionFifteenBalancedLegObjective();
            objective.schemaVersion = 14;
            objective.rewards.legSuccessfulFuelEfficiencyReward = 3f;
            objective.shaping.legFuelEfficiencyBudgetFraction = 0.08f;
            objective.shaping.legMissionEfficiencyBudgetFullFraction = 0f;
            objective.shaping.legRestartEquivalentFuelFraction = 0f;
            objective.shaping.legAdditionalEngineIgnitionEquivalentFuelFraction = 0f;
            return objective;
        }

        static ScenarioObjectiveConfig VersionFifteenBalancedLegObjective()
        {
            ScenarioObjectiveConfig objective =
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.LegLanding);
            objective.schemaVersion = 15;
            objective.rewards.landingDescentProfileErrorCostRate = 0.120f;
            objective.rewards.landingNearTargetVerticalSpeedCostRate = 0.080f;
            objective.rewards.legSuccessfulFuelEfficiencyReward = 4f;
            objective.shaping.legRestartEquivalentFuelFraction = 0.003f;
            objective.shaping.legTouchdownQualityRewardFraction = 0.75f;
            return objective;
        }

        static RewardTerms SafeLegTerms() => new()
        {
            planarDistance = 0.5f,
            distance3D = 0.5f,
            speed = 0.2f,
            planarSpeed = 0.1f,
            verticalSpeed = -0.1f,
            goalClosureRate = 0.1f,
            horizontalClosureRate = 0.1f,
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
            bool structuralStrike = false,
            bool offPadImpact = false,
            bool footOutsidePad = false,
            bool excessiveRebound = false,
            float curriculumDifficulty01 = 1f,
            float fuelFraction01 = 0f,
            float firstContactSpeed = 0f,
            bool firstContactThisStep = true,
            float firstContactVerticalSpeed = 0f,
            float firstContactHorizontalSpeed = 0f,
            float firstContactTiltDeg = 0f,
            float firstContactAngularRateDegS = 0f,
            float footSupportProgress01 = -1f,
            bool propulsionOff = true,
            bool supportSettled = false,
            float supportSettledTimeSeconds = 2f,
            float readinessProgressRate = 0f,
            float stableTimeSeconds = 2f,
            int feetOnPad = -1,
            int episodeEngineRestartCount = 0,
            int engineFirstIgnitionCount = 0)
        {
            return new RewardRuntimeContext(
                altitude: altitude,
                terminalAltitude: 0f,
                episodeStartAltitude: 300f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: 1f,
                curriculumDifficulty01: curriculumDifficulty01,
                fuelKg: 1000f,
                fuelFraction01: fuelFraction01,
                episodeEngineRestartCount: episodeEngineRestartCount,
                episodeEngineFirstIgnitionCount: engineFirstIgnitionCount,
                legLandingCenteringProgressRate: readinessProgressRate,
                legFootSupportProgress01: footSupportProgress01 >= 0f
                    ? footSupportProgress01
                    : stable ? 1f : touchdown ? 0.10f : 0f,
                legTouchdownStarted: touchdown,
                legImpactStarted: touchdown || structuralStrike || offPadImpact || footOutsidePad,
                legOffPadImpact: offPadImpact,
                legFirstContactThisStep: touchdown && firstContactThisStep,
                legFeetOnPad: feetOnPad >= 0 ? feetOnPad : stable ? 4 : 1,
                legFootOutsidePad: footOutsidePad,
                legStructuralStrike: structuralStrike,
                legPropulsionOff: propulsionOff,
                legSupportSettled: supportSettled || stable,
                legSupportSettledTime: supportSettled || stable
                    ? supportSettledTimeSeconds
                    : 0f,
                legStable: stable,
                legBecameStable: becameStable,
                legStableTime: stable ? stableTimeSeconds : 0f,
                legFirstContactSpeed: firstContactSpeed,
                legFirstContactVerticalSpeed: firstContactVerticalSpeed,
                legFirstContactHorizontalSpeed: firstContactHorizontalSpeed,
                legFirstContactTiltDeg: firstContactTiltDeg,
                legFirstContactAngularRateDegS: firstContactAngularRateDegS,
                legExcessiveRebound: excessiveRebound);
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
                altitude: altitude,
                terminalAltitude: 60f,
                episodeStartAltitude: 300f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: elapsedSeconds,
                curriculumDifficulty01: 1f,
                fuelKg: 1000f,
                landingPlatformInsideCapture: platformStable,
                landingPlatformStable: platformStable,
                landingPlatformBecameStable: platformBecameStable,
                landingPlatformStableTime: platformStable ? 0.45f : 0f);

            return RocketRewardModel.Evaluate(
                ScenarioType.ChopstickLanding,
                terms,
                context,
                ScenarioObjectiveConfig.CreateDefault(ScenarioType.ChopstickLanding));
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
