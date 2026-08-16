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
            Assert.That(leg.rewards.landingYawSpinCostRate, Is.EqualTo(0.040f).Within(Epsilon));
            Assert.That(leg.rewards.firstFootContactReward, Is.EqualTo(0.250f).Within(Epsilon));
            Assert.That(leg.rewards.stableTouchdownReward, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(leg.rewards.legSuccessfulTouchdownReward, Is.EqualTo(10f).Within(Epsilon));

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
            Assert.That(RocketAgentSchema.ObservationSize(falcon, ScenarioType.LegLanding), Is.EqualTo(61));
            Assert.That(RocketAgentSchema.ContinuousActionSize(falcon), Is.EqualTo(21));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.Hover), Is.EqualTo(105));
            Assert.That(RocketAgentSchema.ObservationSize(allNine, ScenarioType.LegLanding), Is.EqualTo(109));
            Assert.That(RocketAgentSchema.ContinuousActionSize(allNine), Is.EqualTo(39));
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
        public void LegLandingStartsFastCalmAndKeepsEnginesOffByContract()
        {
            var environment = new SimEnvironmentConfig { scenario = ScenarioType.LegLanding };
            LandingCurriculumProfile easy = environment.GetLegLandingCurriculumProfile(0f);
            LandingCurriculumProfile full = environment.GetLegLandingCurriculumProfile(1f);

            Assert.That(easy.spawnAltitudeMin,
                Is.EqualTo(SimEnvironmentConfig.LegLandingInitialSpawnAltitudeMin).Within(Epsilon));
            Assert.That(easy.verticalSpeedMin, Is.EqualTo(20f).Within(Epsilon));
            Assert.That(easy.verticalSpeedMax, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(easy.spawnTiltRangeDeg, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(easy.angularSpeedMaxDegS, Is.Zero.Within(Epsilon));
            Assert.That(full.angularSpeedMaxDegS, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(full.verticalSpeedMax, Is.EqualTo(70f).Within(Epsilon));
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
            Assert.That(falcon.finsEnabled, Is.True);
            Assert.That(falcon.rcsEnabled, Is.True);
            Assert.That(simple.GetEngineCount(), Is.EqualTo(1));
            Assert.That(simple.GetActiveEngineCount(), Is.EqualTo(1));
            Assert.That(simple.independentEngines, Is.False);
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
        public void LegContactRequiresThreeFeetAndNoStrike()
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
                0b0011, false, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.True);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, false, true, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, true, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.False);

            termination.legMinimumStableFeet = 4;
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b0111, false, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.False,
                "The contact evaluator must consume the user-configured minimum-foot count.");
            Assert.That(LegLandingContactEvaluator.IsStableCandidate(
                0b1111, false, false, terms, 1f, profile, termination.legMinimumStableFeet), Is.True);
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
                footOutsidePad: false,
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
            AssertTerminal(stable, 10f, true, EpisodeTerminationReason.LegLandingSuccessfulTouchdown);
            AssertTerminal(strike, -5f, false, EpisodeTerminationReason.LegLandingStructuralStrike);
            AssertTerminal(rebound, -5f, false, EpisodeTerminationReason.LegLandingExcessiveRebound);
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
                Is.EqualTo(0.04f).Within(Epsilon));
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
            bool structuralStrike = false,
            bool excessiveRebound = false)
        {
            return new RewardRuntimeContext(
                altitude: altitude,
                terminalAltitude: 0f,
                episodeStartAltitude: 300f,
                gravityMagnitude: 9.80665f,
                episodeElapsedSeconds: 1f,
                curriculumDifficulty01: 1f,
                fuelKg: 1000f,
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
                legFirstContactAngularRateDegS: 1f,
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
