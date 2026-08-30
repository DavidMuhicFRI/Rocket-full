// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/ScenarioObjectiveConfig.cs
// Purpose: Groups one task's reward, shaping, and termination configuration.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    [Serializable]
    public sealed class ScenarioObjectiveConfig
    {
        public const int CurrentSchemaVersion = 15;

        // Zero identifies objectives saved before objective-level versioning.
        // CreateDefault writes the current version explicitly so intentional
        // custom objectives are never mistaken for missing data.
        public int schemaVersion;
        public string presetName = RewardPresetCatalog.BalancedPresetName;
        public string basePresetName = RewardPresetCatalog.BalancedPresetName;
        public RewardParameters rewards = new();
        public RewardShapingParameters shaping = new();
        public TerminationParameters terminations = new();

        /// <summary>Builds the complete documented default for one task.</summary>
        public static ScenarioObjectiveConfig CreateDefault(ScenarioType scenario)
        {
            var objective = new ScenarioObjectiveConfig();
            RewardPresetCatalog.Apply(objective, scenario, RewardPresetCatalog.BalancedPresetName);
            RewardShapingDefaults.Apply(objective.shaping, scenario);
            TerminationDefaults.Apply(objective.terminations, scenario);
            objective.schemaVersion = CurrentSchemaVersion;
            return objective;
        }

        /// <summary>Creates missing children without changing intentional values.</summary>
        public void EnsureObjects(ScenarioType scenario)
        {
            rewards ??= new RewardParameters();
            if (shaping == null)
            {
                shaping = new RewardShapingParameters();
                RewardShapingDefaults.Apply(shaping, scenario);
            }

            if (terminations == null)
            {
                terminations = new TerminationParameters();
                TerminationDefaults.Apply(terminations, scenario);
            }

            if (string.IsNullOrWhiteSpace(presetName))
                presetName = "Custom";
            if (string.IsNullOrWhiteSpace(basePresetName))
                basePresetName = RewardPresetCatalog.BalancedPresetName;

            // The first leg-landing objective revision changed rewards without
            // replacing a saved draft's older shaping and termination values.
            // Upgrade only recognized exact built-in Balanced baselines;
            // Custom and even slightly edited objectives are preserved
            // byte-for-byte apart from their version marker.
            if (schemaVersion < CurrentSchemaVersion &&
                IsRecognizedLegacyLegLandingBaseline(scenario))
            {
                CopyFrom(CreateDefault(ScenarioType.LegLanding));
                return;
            }

            if (schemaVersion < 3 && scenario == ScenarioType.LegLanding)
                UpgradeLegLandingV3();
            if (schemaVersion < 4 && scenario == ScenarioType.LegLanding)
                UpgradeLegLandingV4();
            if (schemaVersion < 15 && scenario == ScenarioType.LegLanding)
                UpgradeLegLandingV15();

            schemaVersion = CurrentSchemaVersion;
        }

        /// <summary>Deep-copies another task objective.</summary>
        public void CopyFrom(ScenarioObjectiveConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            rewards ??= new RewardParameters();
            shaping ??= new RewardShapingParameters();
            terminations ??= new TerminationParameters();
            rewards.CopyFrom(source.rewards);
            shaping.CopyFrom(source.shaping);
            terminations.CopyFrom(source.terminations);
            presetName = source.presetName;
            basePresetName = source.basePresetName;
            schemaVersion = source.schemaVersion;
        }

        /// <summary>Marks the reward vector as no longer matching a preset.</summary>
        public void MarkCustom() => presetName = "Custom";

        void UpgradeLegLandingV3()
        {
            // The redesigned v3 reward fields are optional experiments again.
            // Do not inject their rejected nonzero defaults into older custom
            // objectives. Only initialize the physical support requirement.
            if (terminations.legInitialMinimumStableFeet <= 0)
                terminations.legInitialMinimumStableFeet = 3;
        }

        void UpgradeLegLandingV4()
        {
            // Fuel-efficiency and touchdown-quality shaping are retained for
            // custom experiments, but the restored baseline leaves them off.
        }

        void UpgradeLegLandingV15()
        {
            // Older custom objectives had only one fuel-efficiency scale. Keep
            // that scale constant across difficulty and leave the newly added
            // switching equivalents disabled. Exact untouched Balanced v14
            // objectives are recognized below and replaced with the complete
            // mission-efficiency defaults instead.
            if (shaping.legMissionEfficiencyBudgetFullFraction <= 0f)
                shaping.legMissionEfficiencyBudgetFullFraction =
                    shaping.legFuelEfficiencyBudgetFraction;
        }

        bool IsRecognizedLegacyLegLandingBaseline(ScenarioType scenario)
        {
            if (scenario != ScenarioType.LegLanding ||
                !string.Equals(presetName, RewardPresetCatalog.BalancedPresetName, StringComparison.Ordinal) ||
                !string.Equals(basePresetName, RewardPresetCatalog.BalancedPresetName, StringComparison.Ordinal))
                return false;

            // Version 7 restores the pre-redesign Balanced leg objective after
            // the stronger v6 guidance collapsed into repeated ascent. Upgrade
            // only exact built-in baselines; custom experiments keep their
            // saved values.
            bool versionSixBaseline =
                schemaVersion == 6 &&
                Approximately(rewards.landingGoalClosureRewardRate, 0.25f) &&
                Approximately(rewards.landingDescentProfileErrorCostRate, 0f) &&
                Approximately(rewards.landingPlanarDistanceCostRate, 0.12f) &&
                Approximately(rewards.landingUprightErrorCostRate, 0.22f) &&
                Approximately(rewards.landingPlanarSpeedCostRate, 0.015f) &&
                Approximately(rewards.landingAngularRateCostRate, 0.12f) &&
                Approximately(rewards.landingUpwardVelocityCostRate, 1.5f) &&
                Approximately(rewards.landingReadinessProgressRewardRate, 8f) &&
                Approximately(rewards.controlEffortCostRate, 0.003f) &&
                Approximately(rewards.timeCostRate, 0.60f) &&
                Approximately(rewards.legSuccessfulTouchdownReward, 30f) &&
                Approximately(rewards.legSuccessfulFuelEfficiencyReward, 8f) &&
                Approximately(rewards.legAboveAltitudeLimitCost, 50f) &&
                Approximately(shaping.landingClosureMinimumScaleMps, 4f) &&
                Approximately(shaping.landingPlanarDistanceFalloffM, 20f) &&
                Approximately(shaping.landingNearTargetAltitudeFalloffM, 75f) &&
                Approximately(shaping.landingUpwardVelocityToleranceMps, 0.5f) &&
                Approximately(shaping.landingUpwardVelocityScaleMps, 3f) &&
                Approximately(shaping.legFuelEfficiencyBudgetFraction, 0.06f) &&
                Approximately(shaping.legTouchdownQualityRewardFraction, 0.7f) &&
                !terminations.unsafeAttitudeEnabled &&
                Approximately(terminations.maximumAltitudeAboveStartM, 5f) &&
                Approximately(terminations.maximumEpisodeSeconds, 60f) &&
                Approximately(terminations.landingSuccessRadiusM.initial, 4f) &&
                Approximately(terminations.landingSuccessMaxTotalSpeedMps.initial, 8f) &&
                Approximately(terminations.landingSuccessMaxVerticalSpeedMps.initial, 7f) &&
                Approximately(terminations.landingSuccessMaxHorizontalSpeedMps.initial, 3f) &&
                !terminations.legFootOutsidePadEnabled &&
                terminations.legInitialMinimumStableFeet == 2 &&
                terminations.legMinimumStableFeet == 3;
            if (versionSixBaseline)
                return true;

            // Version 7 restored the pre-redesign reward vector but
            // accidentally left newly retained, opt-in shaping denominators
            // at zero. Migrate only that exact untouched baseline so custom
            // shaping experiments are not overwritten.
            bool versionSevenBaseline =
                schemaVersion == 7 &&
                MatchesVersionEightLegRewards() &&
                MatchesVersionSevenLegShaping() &&
                MatchesVersionEightLegTerminations();
            if (versionSevenBaseline)
                return true;

            // Version 8 repaired editor scale validity while retaining the
            // old fall-dominated reward and three-foot success contract.
            // Replace only the exact untouched Balanced objective.
            bool versionEightBaseline =
                schemaVersion == 8 &&
                MatchesVersionEightLegRewards() &&
                MatchesVersionEightLegShaping() &&
                MatchesVersionEightLegTerminations();
            if (versionEightBaseline)
                return true;

            // Version 9 is the exact L2 baseline. It priced an altitude escape
            // at 16 while charging up to 18 points of time cost, making a fast
            // upward reset cheaper than any attempt that stayed airborne.
            bool versionNineBaseline =
                schemaVersion == 9 &&
                MatchesVersionNineLegBaseline();
            if (versionNineBaseline)
                return true;

            // Version 10 removed upward-reset incentives, but L3 showed that
            // its flat high-speed impact cost and per-second time cost made a
            // fast pad crash the best readily discoverable failure outcome.
            bool versionTenBaseline =
                schemaVersion == 10 && MatchesVersionTenLegBaseline();
            if (versionTenBaseline)
                return true;

            // Version 11 is the L5 baseline. It learned reliable contact but
            // mostly waited for favorable centered spawns, approached the
            // limits of the touchdown envelope, and could discount a calm
            // off-center landing all the way to the global timeout.
            bool versionElevenBaseline =
                schemaVersion == 11 && MatchesLegObjective(VersionElevenLegBaseline());
            if (versionElevenBaseline)
                return true;

            // Version 12 is the L6 baseline. It made contact reliable, but its
            // multiplicative readiness signal was almost always exactly zero
            // and a typical hard arrival cost only about -12 versus -50 for
            // escape. Upgrade only the exact untouched Balanced objective.
            bool versionTwelveBaseline =
                schemaVersion == 12 && MatchesLegObjective(VersionTwelveLegBaseline());
            if (versionTwelveBaseline)
                return true;

            // Version 13 is the L7 baseline. Its mixed readiness potential paid
            // for the altitude gate increasing during descent, even when the
            // vehicle drifted away from the pad center.
            bool versionThirteenBaseline =
                schemaVersion == 13 && MatchesLegObjective(VersionThirteenLegBaseline());
            if (versionThirteenBaseline)
                return true;

            // Version 14 is the L9 baseline. It scored fuel alone with a
            // clipped budget, so equally successful policies received no
            // gradient once they crossed that threshold and PWM switching was
            // invisible to the terminal objective.
            bool versionFourteenBaseline =
                schemaVersion == 14 && MatchesLegObjective(VersionFourteenLegBaseline());
            if (versionFourteenBaseline)
                return true;

            if (schemaVersion >= 7)
                return false;

            // Version 5 introduced horizontal navigation but made its first
            // curriculum stage too compound and left ascent too inexpensive.
            bool versionFiveBaseline =
                schemaVersion == 5 &&
                Approximately(rewards.landingGoalClosureRewardRate, 0.25f) &&
                Approximately(rewards.landingPlanarDistanceCostRate, 0.12f) &&
                Approximately(rewards.landingUprightErrorCostRate, 0.22f) &&
                Approximately(rewards.landingPlanarSpeedCostRate, 0.015f) &&
                Approximately(rewards.landingAngularRateCostRate, 0.12f) &&
                Approximately(rewards.landingUpwardVelocityCostRate, 0.30f) &&
                Approximately(rewards.legSuccessfulTouchdownReward, 25f) &&
                Approximately(rewards.legSuccessfulFuelEfficiencyReward, 8f) &&
                Approximately(rewards.legAboveAltitudeLimitCost, 35f) &&
                Approximately(shaping.landingClosureMinimumScaleMps, 4f) &&
                Approximately(shaping.landingUpwardVelocityToleranceMps, 1.5f) &&
                Approximately(shaping.landingUpwardVelocityScaleMps, 15f) &&
                Approximately(terminations.maximumAltitudeAboveStartM, 40f) &&
                Approximately(terminations.landingSuccessRadiusM.initial, 4f) &&
                !terminations.legFootOutsidePadEnabled &&
                terminations.legInitialMinimumStableFeet == 2 &&
                terminations.legMinimumStableFeet == 3;
            if (versionFiveBaseline)
                return true;

            // Version 4 is the older built-in Balanced leg
            // objective. Its 3D closure reward favored passive vertical fall;
            // upgrade the exact preset to the new horizontal-navigation design.
            bool versionFourBaseline =
                schemaVersion == 4 &&
                Approximately(rewards.landingGoalClosureRewardRate, 0.25f) &&
                Approximately(rewards.landingPlanarDistanceCostRate, 0.08f) &&
                Approximately(rewards.landingUprightErrorCostRate, 0.12f) &&
                Approximately(rewards.landingPlanarSpeedCostRate, 0.10f) &&
                Approximately(rewards.landingAngularRateCostRate, 0.05f) &&
                Approximately(rewards.landingReadinessProgressRewardRate, 6f) &&
                Approximately(rewards.timeCostRate, 0.60f) &&
                Approximately(rewards.legSuccessfulTouchdownReward, 20f) &&
                Approximately(rewards.legSuccessfulFuelEfficiencyReward, 3f) &&
                Approximately(shaping.landingClosureMinimumScaleMps, 30f) &&
                Approximately(shaping.landingPlanarSpeedScaleMps, 15f) &&
                Approximately(terminations.landingSuccessRadiusM.initial, 10f) &&
                Approximately(terminations.landingSuccessRadiusM.full, 2f) &&
                terminations.legFootOutsidePadEnabled &&
                terminations.legInitialMinimumStableFeet == 1 &&
                terminations.legMinimumStableFeet == 3;
            if (versionFourBaseline)
                return true;

            bool commonShaping = Approximately(shaping.landingPlanarDistanceFalloffM, 25f) &&
                                 Approximately(shaping.landingNearTargetAltitudeFalloffM, 50f) &&
                                 Approximately(shaping.landingPlanarSpeedScaleMps, 5f) &&
                                 Approximately(shaping.landingAngularRateScaleDegS, 60f);
            bool commonTermination = terminations.unsafeAttitudeEnabled &&
                                     terminations.planarFlyawayEnabled &&
                                     terminations.altitudeCeilingEnabled &&
                                     terminations.fuelDepletionEnabled &&
                                     terminations.timeLimitEnabled &&
                                     Approximately(terminations.maximumEpisodeSeconds, 120f) &&
                                     Approximately(terminations.landingSuccessRadiusM.full, 2f) &&
                                     Approximately(terminations.landingSuccessMaxTotalSpeedMps.full, 2.5f) &&
                                     Approximately(terminations.landingSuccessMaxVerticalSpeedMps.full, 2f) &&
                                     Approximately(terminations.landingSuccessMaxHorizontalSpeedMps.full, 1f) &&
                                     Approximately(terminations.landingStableHoldSeconds.full, 1f);
            if (!commonShaping || !commonTermination) return false;

            bool originalBaseline =
                Approximately(shaping.landingClosureMinimumScaleMps, 5f) &&
                Approximately(terminations.maximumAltitudeAboveStartM, 100f) &&
                Approximately(terminations.landingSuccessRadiusM.initial, 8f) &&
                Approximately(terminations.landingSuccessMaxTotalSpeedMps.initial, 7f) &&
                Approximately(terminations.landingSuccessMaxVerticalSpeedMps.initial, 5f) &&
                Approximately(terminations.landingSuccessMaxHorizontalSpeedMps.initial, 5f) &&
                Approximately(terminations.landingStableHoldSeconds.initial, 0.25f);
            bool firstRevisionBaseline =
                Approximately(shaping.landingClosureMinimumScaleMps, 30f) &&
                Approximately(terminations.maximumAltitudeAboveStartM, 40f) &&
                Approximately(terminations.landingSuccessRadiusM.initial, 10f) &&
                Approximately(terminations.landingSuccessMaxTotalSpeedMps.initial, 8f) &&
                Approximately(terminations.landingSuccessMaxVerticalSpeedMps.initial, 6f) &&
                Approximately(terminations.landingSuccessMaxHorizontalSpeedMps.initial, 3f) &&
                Approximately(terminations.landingStableHoldSeconds.initial, 0.15f);
            return originalBaseline || firstRevisionBaseline;
        }

        static bool Approximately(float left, float right)
        {
            float scale = Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= scale * 0.000001f;
        }

        bool MatchesVersionEightLegRewards()
        {
            var expected = new RewardParameters();
            expected.Clear();
            expected.landingGoalClosureRewardRate = 0.060f;
            expected.landingDescentProfileErrorCostRate = 0.040f;
            expected.landingPlanarDistanceCostRate = 0.025f;
            expected.landingUprightErrorCostRate = 0.020f;
            expected.landingNearTargetPlanarSpeedCostRate = 0.025f;
            expected.landingNearTargetAngularRateCostRate = 0.015f;
            expected.landingYawSpinCostRate = 0.040f;
            expected.controlEffortCostRate = 0.005f;
            expected.timeCostRate = 0.100f;
            expected.firstFootContactReward = 0.25f;
            expected.stableTouchdownReward = 1f;
            expected.legSuccessfulTouchdownReward = 10f;
            expected.legHardTouchdownCost = 5f;
            expected.legStructuralStrikeCost = 5f;
            expected.legFootOutsidePadCost = 5f;
            expected.legExcessiveReboundCost = 5f;
            expected.legUnsafeAttitudeCost = 5f;
            expected.legTooFarFromTargetCost = 5f;
            expected.legFuelDepletedCost = 5f;
            expected.legAboveAltitudeLimitCost = 5f;
            expected.legMissedPadCost = 5f;
            expected.legTimeLimitCost = 5f;
            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                if (!Approximately(
                        descriptor.GetValue(rewards),
                        descriptor.GetValue(expected)))
                    return false;
            return true;
        }

        bool MatchesVersionSevenLegShaping()
        {
            RewardShapingParameters expected = VersionEightLegShaping();
            expected.landingVerticalSpeedExcessScaleMps = 0f;
            expected.landingUpwardVelocityToleranceMps = 0f;
            expected.landingUpwardVelocityScaleMps = 0f;
            expected.legFuelEfficiencyStartDifficulty = 0f;
            expected.legFuelEfficiencyFullDifficulty = 0f;
            expected.legFuelEfficiencyBudgetFraction = 0f;
            expected.legTouchdownQualityRewardFraction = 0f;
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                if (!Approximately(
                        descriptor.GetValue(shaping),
                        descriptor.GetValue(expected)))
                    return false;
            return true;
        }

        bool MatchesVersionEightLegShaping()
        {
            RewardShapingParameters expected = VersionEightLegShaping();
            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                if (!Approximately(
                        descriptor.GetValue(shaping),
                        descriptor.GetValue(expected)))
                    return false;
            return true;
        }

        static RewardShapingParameters VersionEightLegShaping()
        {
            var expected = new RewardShapingParameters();
            expected.Clear();
            expected.landingBallisticDescentFraction = 0.30f;
            expected.landingDescentErrorMinimumScaleMps = 5f;
            expected.landingClosureMinimumScaleMps = 5f;
            expected.landingPlanarDistanceFalloffM = 25f;
            expected.landingNearTargetAltitudeFalloffM = 50f;
            expected.landingPlanarSpeedScaleMps = 5f;
            expected.landingVerticalSpeedExcessScaleMps = 5f;
            expected.landingAngularRateScaleDegS = 60f;
            expected.landingUpwardVelocityToleranceMps = 0.5f;
            expected.landingUpwardVelocityScaleMps = 3f;
            expected.legFuelEfficiencyStartDifficulty = 0.5f;
            expected.legFuelEfficiencyFullDifficulty = 1f;
            expected.legFuelEfficiencyBudgetFraction = 0.06f;
            expected.legTouchdownQualityRewardFraction = 0f;
            expected.landingYawSpinScaleDegS = 20f;
            return expected;
        }

        bool MatchesVersionEightLegTerminations()
        {
            var expected = new TerminationParameters();
            TerminationDefaults.Apply(expected, ScenarioType.LegLanding);
            expected.unsafeAttitudeEnabled = true;
            expected.maximumEpisodeSeconds = 120f;
            expected.landingSuccessRadiusM = new DifficultyRange(8f, 2f);
            expected.landingSuccessMaxTotalSpeedMps = new DifficultyRange(7f, 2.5f);
            expected.landingSuccessMaxVerticalSpeedMps = new DifficultyRange(5f, 2f);
            expected.landingSuccessMaxHorizontalSpeedMps = new DifficultyRange(5f, 1f);
            expected.landingSuccessMaxTiltDeg = new DifficultyRange(20f, 5f);
            expected.landingSuccessMaxAngularRateDegS = new DifficultyRange(50f, 25f);
            expected.landingStableHoldSeconds = new DifficultyRange(0.25f, 1f);
            expected.legInitialMinimumStableFeet = 3;
            expected.legMinimumStableFeet = 3;
            foreach (TerminationRuleDescriptor rule in
                     TerminationRuleCatalog.ForScenario(ScenarioType.LegLanding))
            {
                if (rule.GetEnabled(terminations) != rule.GetEnabled(expected))
                    return false;
                foreach (TerminationThresholdDescriptor threshold in rule.thresholds)
                {
                    if (!Approximately(
                            threshold.GetInitialValue(terminations),
                            threshold.GetInitialValue(expected)) ||
                        !Approximately(
                            threshold.GetFullValue(terminations),
                            threshold.GetFullValue(expected)))
                        return false;
                }
            }
            return true;
        }

        bool MatchesVersionNineLegBaseline()
        {
            ScenarioObjectiveConfig expected = VersionTenLegBaseline();
            expected.rewards.landingUpwardVelocityCostRate = 0f;
            expected.rewards.legTooFarFromTargetCost = 16f;
            expected.rewards.legFuelDepletedCost = 16f;
            expected.rewards.legAboveAltitudeLimitCost = 16f;
            expected.rewards.legMissedPadCost = 16f;

            return MatchesLegObjective(expected);
        }

        bool MatchesVersionTenLegBaseline() =>
            MatchesLegObjective(VersionTenLegBaseline());

        static ScenarioObjectiveConfig VersionTenLegBaseline()
        {
            ScenarioObjectiveConfig expected = VersionElevenLegBaseline();
            expected.rewards.timeCostRate = 0.30f;
            expected.rewards.legImpactSeverityCost = 4f;
            expected.rewards.legTimeLimitCost = 16f;
            expected.terminations.landingSuccessMaxTotalSpeedMps =
                new DifficultyRange(5f, 2f);
            expected.terminations.landingSuccessMaxVerticalSpeedMps =
                new DifficultyRange(4f, 1.5f);
            expected.schemaVersion = 10;
            return expected;
        }

        static ScenarioObjectiveConfig VersionElevenLegBaseline()
        {
            ScenarioObjectiveConfig expected = VersionTwelveLegBaseline();
            expected.rewards.landingGoalClosureRewardRate = 0.120f;
            expected.rewards.landingReadinessProgressRewardRate = 0f;
            expected.terminations.landingStableHoldSeconds =
                new DifficultyRange(0.75f, 2f);
            expected.schemaVersion = 11;
            return expected;
        }

        static ScenarioObjectiveConfig VersionTwelveLegBaseline()
        {
            ScenarioObjectiveConfig expected = VersionThirteenLegBaseline();
            expected.rewards.landingGoalClosureRewardRate = 0.250f;
            expected.rewards.landingUprightErrorCostRate = 0.040f;
            expected.rewards.landingNearTargetAngularRateCostRate = 0.030f;
            expected.rewards.legHardTouchdownCost = 10f;
            expected.rewards.legImpactSeverityCost = 30f;
            expected.rewards.legStructuralStrikeCost = 10f;
            expected.rewards.legFootOutsidePadCost = 10f;
            expected.rewards.legExcessiveReboundCost = 10f;
            expected.schemaVersion = 12;
            return expected;
        }

        static ScenarioObjectiveConfig VersionThirteenLegBaseline()
        {
            ScenarioObjectiveConfig expected = VersionFourteenLegBaseline();
            expected.rewards.landingDescentProfileErrorCostRate = 0.050f;
            expected.rewards.landingReadinessProgressRewardRate = 2f;
            expected.shaping.legFuelEfficiencyStartDifficulty = 0.6f;
            expected.shaping.legFuelEfficiencyFullDifficulty = 1f;
            expected.schemaVersion = 13;
            return expected;
        }

        static ScenarioObjectiveConfig VersionFourteenLegBaseline()
        {
            ScenarioObjectiveConfig expected = CreateDefault(ScenarioType.LegLanding);
            expected.rewards.legSuccessfulFuelEfficiencyReward = 3f;
            expected.shaping.legFuelEfficiencyBudgetFraction = 0.08f;
            expected.shaping.legMissionEfficiencyBudgetFullFraction = 0f;
            expected.shaping.legRestartEquivalentFuelFraction = 0f;
            expected.shaping.legAdditionalEngineIgnitionEquivalentFuelFraction = 0f;
            expected.schemaVersion = 14;
            return expected;
        }

        bool MatchesLegObjective(ScenarioObjectiveConfig expected)
        {
            if (expected == null) return false;

            foreach (RewardParameterDescriptor descriptor in RewardParameterCatalog.All)
                if (!Approximately(
                        descriptor.GetValue(rewards),
                        descriptor.GetValue(expected.rewards)))
                    return false;

            foreach (ShapingParameterDescriptor descriptor in ShapingParameterCatalog.All)
                if (!Approximately(
                        descriptor.GetValue(shaping),
                        descriptor.GetValue(expected.shaping)))
                    return false;

            foreach (TerminationRuleDescriptor rule in
                     TerminationRuleCatalog.ForScenario(ScenarioType.LegLanding))
            {
                if (rule.GetEnabled(terminations) != rule.GetEnabled(expected.terminations))
                    return false;
                foreach (TerminationThresholdDescriptor threshold in rule.thresholds)
                {
                    if (!Approximately(
                            threshold.GetInitialValue(terminations),
                            threshold.GetInitialValue(expected.terminations)) ||
                        !Approximately(
                            threshold.GetFullValue(terminations),
                            threshold.GetFullValue(expected.terminations)))
                        return false;
                }
            }

            return true;
        }
    }
}
