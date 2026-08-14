// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Task.cs
// Purpose: Builds the Task tab for training and inference, including scenario cards, spawn ranges, and task-specific targets.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>
        /// Scenario cards shown in training and inference, pairing each enum
        /// with display text.
        /// </summary>
        static readonly IReadOnlyList<ScenarioDefinition> Scenarios = ScenarioCatalog.All;

        /// <summary>
        /// Returns the currently selected task, falling back to Chopstick Landing while
        /// the environment config is not yet connected.
        /// </summary>
        ScenarioType CurrentScenario()
        {
            return envConfig?.scenario ?? ScenarioType.ChopstickLanding;
        }

        /// <summary>
        /// Applies scenario-specific fuel and engine defaults to the panel's
        /// editable parts config.
        /// </summary>
        void ApplyCurrentScenarioHardwareDefaults()
        {
            if (partsConfig == null) return;

            ScenarioType scenario = CurrentScenario();
            partsConfig.ApplyScenarioHardwareDefaults(scenario);
        }

        /// <summary>
        /// Builds scenario and initial-condition controls for the selected mode.
        /// The mode switch itself stays in the fixed footer so it is always visible.
        /// </summary>
        void BuildScenarioTab(VisualElement c)
        {
            c.Clear();
            if (envConfig.behaviorType == BehaviorType.Training)
                BuildTrainingTaskSection(c);
            else
                BuildInferenceScenarioSection(c);
            RefreshStartButton();
        }

        /// <summary>
        /// Creates one scenario card with consistent training/inference styling.
        /// </summary>
        VisualElement CreateScenarioCard(ScenarioType type, string title, string desc, Action<ScenarioType> onClick)
        {
            var card = new VisualElement { userData = type };
            card.AddToClassList("rs-scenario-card");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("rs-scenario-title");
            card.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("rs-scenario-desc");
            card.Add(descLabel);

            card.RegisterCallback<ClickEvent>(_ => onClick(type));
            return card;
        }

        /// <summary>
        /// Updates scenario card styling to show the active scenario.
        /// </summary>
        void RefreshScenarioCards(VisualElement grid)
        {
            foreach (var child in grid.Children())
                if (child.userData is ScenarioType st)
                    child.EnableInClassList("rs-scenario-card-active", st == envConfig.scenario);
        }

        /// <summary>Builds task selection without mixing in run or reward controls.</summary>
        void BuildTrainingTaskSection(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Training Task"));
            var grid = new VisualElement();
            grid.AddToClassList("rs-scenario-grid");

            foreach (var (type, scenarioName, desc) in Scenarios)
            {
                var card = CreateScenarioCard(type, scenarioName, desc, selectedScenario =>
                {
                    envConfig.scenario = selectedScenario;
                    envConfig.moveTargetEnabled = ScenarioProfile.UsesMovingTarget(selectedScenario);
                    if (selectedScenario == ScenarioType.HoverTracking) envConfig.ResetHoverTrackCurriculum();
                    if (selectedScenario.IsLanding()) envConfig.ResetActiveLandingCurriculum();
                    ApplyCurrentScenarioHardwareDefaults();
                    Dirty();
                    BuildVehicleTab(_tabContents[VehicleTab]);
                    BuildRewardsTab(_tabContents[RewardsTab]);
                    BuildRunTab(_tabContents[RunTab]);
                    RefreshScenarioCards(grid);
                });
                grid.Add(card);
            }

            c.Add(grid);
            RefreshScenarioCards(grid);
            c.Add(BuildGroupDetail(
                "Task changes are applied to the preview now and to agents when the next run starts."));
        }

        /// <summary>
        /// Builds scenario cards and spawn controls for the selected inference run.
        /// </summary>
        void BuildInferenceScenarioSection(VisualElement root)
        {
            root.Clear();
            root.Add(UIHelper.SectionLabel("Inference Purpose"));

            var purposeField = new EnumField("Purpose", envConfig.inferencePurpose);
            purposeField.AddToClassList("rs-enum-field");
            purposeField.RegisterValueChangedCallback(evt =>
            {
                envConfig.inferencePurpose = (InferencePurpose)evt.newValue;
                Dirty();
                BuildInferenceScenarioSection(root);
                BuildRunTab(_tabContents[RunTab]);
                RefreshStartButton();
            });
            root.Add(purposeField);

            if (envConfig.inferencePurpose == InferencePurpose.StandardEvaluation)
            {
                BuildStandardEvaluationControls(root);
                return;
            }

            root.Add(BuildGroupDetail(
                "Manual inference keeps editable spawn ranges for visual inspection. Its episodes are not the standardized thesis benchmark."));
            root.Add(UIHelper.SectionLabel("Simulation Scenario"));

            var grid = new VisualElement();
            grid.AddToClassList("rs-scenario-grid");
            foreach (var (type, scenarioName, desc) in Scenarios)
            {
                var card = CreateScenarioCard(type, scenarioName, desc, selectedScenario =>
                {
                    envConfig.scenario = selectedScenario;
                    envConfig.moveTargetEnabled = ScenarioProfile.UsesMovingTarget(selectedScenario);
                    ApplyCurrentScenarioHardwareDefaults();
                    Dirty();
                    BuildVehicleTab(_tabContents[VehicleTab]);
                    BuildInferenceScenarioSection(root);
                });
                grid.Add(card);
            }

            root.Add(grid);
            RefreshScenarioCards(grid);

            BuildInferenceSpawnControls(root, envConfig.GetInferenceSpawnProfile(envConfig.scenario));
        }

        /// <summary>
        /// Builds the immutable benchmark controls. Only episode count and the
        /// shared suite seed are editable. Landing uses the canonical d=1
        /// profile; fixed hover uses its canonical near-target spawn profile.
        /// </summary>
        void BuildStandardEvaluationControls(VisualElement root)
        {
            EvaluationConfig evaluation = envConfig.EnsureEvaluationConfig();
            string evaluatorDescription = envConfig.scenario.IsLanding()
                ? "Standard evaluation uses deterministic policy actions, clear weather, no faults, no curriculum replay, and the full d=1 landing distribution."
                : "Standard hover evaluation uses deterministic policy actions, clear weather, no faults, and a fixed seeded near-target spawn distribution. Episodes end through fuel depletion or an existing failure terminal.";
            root.Add(BuildGroupDetail(evaluatorDescription));

            root.Add(UIHelper.SectionLabel("Evaluation Suite"));
            root.Add(UIHelper.ReadOnly("Scenario", envConfig.scenario.ToString()));
            root.Add(UIHelper.IntSlider("Episodes", evaluation.episodeCount, 10, 1000, value =>
            {
                evaluation.episodeCount = value;
                evaluation.Clamp();
            }, "Runs exactly this many completed episodes before writing the aggregate summary and stopping automatically."));
            root.Add(UIHelper.IntSlider("Evaluation Seed", evaluation.seed, 0, 100000, value =>
            {
                evaluation.seed = value;
                evaluation.Clamp();
            }, "Use the same seed for every trained model so episode indices map to identical initial states."));

            if (envConfig.scenario == ScenarioType.Hover)
            {
                InferenceSpawnProfile hover = envConfig.GetInferenceSpawnProfile(ScenarioType.Hover);
                root.Add(UIHelper.SectionLabel("Fixed Benchmark"));
                root.Add(UIHelper.ReadOnly("Spawn Altitude", $"{hover.altitudeMin:F0}..{hover.altitudeMax:F0} m"));
                root.Add(UIHelper.ReadOnly("Spawn Offset", $"up to {hover.horizontalOffsetMax:F0} m"));
                root.Add(UIHelper.ReadOnly("Vertical Speed", $"{hover.verticalSpeedMin:F1}..{hover.verticalSpeedMax:F1} m/s"));
                root.Add(UIHelper.ReadOnly("Horizontal Speed", $"up to {hover.horizontalSpeedMax:F1} m/s"));
                root.Add(UIHelper.ReadOnly("Endpoint", "Fuel depletion or an existing failure terminal"));
                root.Add(BuildGroupDetail(
                    "Hover has no artificial success terminal. Compare duration, time in the declared hover envelope, RMS position/motion error, fuel use, restart count, and terminal state."));
                return;
            }

            if (!envConfig.scenario.IsLanding())
            {
                root.Add(BuildGroupDetail(
                    "This scenario does not yet have a standardized evaluator contract. Use Manual Inference."));
                return;
            }

            LandingCurriculumProfile full = envConfig.GetActiveLandingCurriculumProfile(1f);
            root.Add(UIHelper.SectionLabel("Fixed Benchmark"));
            root.Add(UIHelper.ReadOnly("Difficulty", "d = 1.000 (full)"));
            root.Add(UIHelper.ReadOnly("Spawn Altitude", $"{full.spawnAltitudeMin:F0}..{full.spawnAltitudeMax:F0} m"));
            root.Add(UIHelper.ReadOnly("Spawn Offset", $"up to {full.spawnRadius:F0} m"));
            root.Add(UIHelper.ReadOnly("Downward Speed", $"{full.verticalSpeedMin:F0}..{full.verticalSpeedMax:F0} m/s before feasibility clipping"));
            TerminationParameters termination =
                envConfig.GetTrainingObjective(envConfig.scenario).terminations;
            string limits = envConfig.scenario == ScenarioType.ChopstickLanding
                ? $"{full.successRadius:F1} m radius, {full.successMaxVerticalSpeed:F1} m/s vertical, {full.successMaxYawErrorDeg:F0} deg yaw"
                : $"{termination.legMinimumStableFeet}/4 feet, {full.successMaxVerticalSpeed:F1} m/s vertical, " +
                  $"{full.successMaxTiltDeg:F0} deg tilt, {full.platformStableHoldTime:F2} s hold";
            root.Add(UIHelper.ReadOnly("Success Limits", limits));
            root.Add(UIHelper.ReadOnly(
                "Emergency Limit",
                termination.timeLimitEnabled
                    ? $"{termination.maximumEpisodeSeconds:F0} simulated seconds (edit in Reward tab)"
                    : "disabled (edit in Reward tab)"));
        }

        /// <summary>
        /// Builds editable initial-position, velocity, rotation, and scenario
        /// target controls for inference episodes.
        /// </summary>
        void BuildInferenceSpawnControls(VisualElement root, InferenceSpawnProfile profile)
        {
            BuildEpisodeStartControls(root, profile);
            BuildInitialMotionControls(root, profile);
            BuildInitialRotationControls(root, profile);
            BuildScenarioTargetControls(root);
        }

        /// <summary>
        /// Builds deterministic/random start selection plus altitude and horizontal
        /// offset ranges for the selected scenario profile.
        /// </summary>
        void BuildEpisodeStartControls(VisualElement root, InferenceSpawnProfile profile)
        {
            root.Add(UIHelper.SectionLabel("Episode Start"));
            root.Add(UIHelper.Toggle("Randomize Each Episode", profile.randomizeEachEpisode, value =>
            {
                profile.randomizeEachEpisode = value;
                BuildInferenceScenarioSection(root);
            }));
            root.Add(BuildGroupDetail(profile.randomizeEachEpisode
                ? "Each episode samples independently from the configured ranges."
                : "Range midpoints are used; offset and horizontal motion use the +X direction."));

            const float altitudeMax = 1500f;
            AddProfileSlider(root, profile, "Altitude Min (m)", profile.altitudeMin, 0f, altitudeMax,
                value => profile.altitudeMin = value,
                "Lowest altitude from which an inference episode can start.");
            AddProfileSlider(root, profile, "Altitude Max (m)", profile.altitudeMax, 0f, altitudeMax,
                value => profile.altitudeMax = value,
                "Highest altitude from which an inference episode can start.");
            AddProfileSlider(root, profile, "Offset Radius (m)", profile.horizontalOffsetMax, 0f, 200f,
                value => profile.horizontalOffsetMax = value,
                "Maximum horizontal distance between the rocket and target at spawn.");
        }

        /// <summary>Builds starting vertical and horizontal velocity ranges.</summary>
        void BuildInitialMotionControls(VisualElement root, InferenceSpawnProfile profile)
        {
            root.Add(UIHelper.SectionLabel("Initial Motion"));
            AddProfileSlider(root, profile, "Vertical Speed Min", profile.verticalSpeedMin, -150f, 150f,
                value => profile.verticalSpeedMin = value,
                "Lowest initial vertical velocity; negative values mean downward motion.");
            AddProfileSlider(root, profile, "Vertical Speed Max", profile.verticalSpeedMax, -150f, 150f,
                value => profile.verticalSpeedMax = value,
                "Highest initial vertical velocity; positive values mean upward motion.");
            AddProfileSlider(root, profile, "Horizontal Speed Min", profile.horizontalSpeedMin, 0f, 50f,
                value => profile.horizontalSpeedMin = value,
                "Lowest horizontal speed used when the episode begins.");
            AddProfileSlider(root, profile, "Horizontal Speed Max", profile.horizontalSpeedMax, 0f, 50f,
                value => profile.horizontalSpeedMax = value,
                "Highest horizontal speed used when the episode begins.");
        }

        /// <summary>Builds starting tilt, yaw, and angular-speed ranges.</summary>
        void BuildInitialRotationControls(VisualElement root, InferenceSpawnProfile profile)
        {
            root.Add(UIHelper.SectionLabel("Initial Rotation"));
            AddProfileSlider(root, profile, "Base Tilt (deg)", profile.baseTiltDeg, 0f, 180f,
                value => profile.baseTiltDeg = value,
                "Sets the rocket's main starting tilt away from upright.");
            AddProfileSlider(root, profile, "Tilt Variation (deg)", profile.tiltVariationDeg, 0f, 45f,
                value => profile.tiltVariationDeg = value,
                "Adds a random amount above or below the base starting tilt.");
            AddProfileSlider(root, profile, "Yaw Min (deg)", profile.yawMinDeg, -180f, 180f,
                value => profile.yawMinDeg = value,
                "Lowest initial heading angle when starts are randomized.");
            AddProfileSlider(root, profile, "Yaw Max (deg)", profile.yawMaxDeg, -180f, 180f,
                value => profile.yawMaxDeg = value,
                "Highest initial heading angle when starts are randomized.");
            AddProfileSlider(root, profile, "Angular Speed Max", profile.angularSpeedMaxDegS, 0f, 90f,
                value => profile.angularSpeedMaxDegS = value,
                "Limits the random rotation speed applied at episode start.");
        }

        /// <summary>
        /// Adds only the target controls relevant to the current task: chopstick
        /// settings for Landing or moving-pad settings for Hover Tracking.
        /// </summary>
        void BuildScenarioTargetControls(VisualElement root)
        {
            if (envConfig.scenario == ScenarioType.ChopstickLanding)
                BuildLandingTargetControls(root);
            else if (envConfig.scenario == ScenarioType.LegLanding)
                BuildLegLandingTargetControls(root);
            else if (envConfig.scenario == ScenarioType.HoverTracking)
                BuildMovingTargetControls(root);
        }

        /// <summary>Builds catch height/yaw controls and objective-derived capture readouts.</summary>
        void BuildLandingTargetControls(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Tower Capture Target"));
            root.Add(UIHelper.Slider("Catch-Frame Height (m)", envConfig.landingCatchAltitude, 20f, 150f,
                value => envConfig.landingCatchAltitude = Mathf.Max(20f, value),
                "Sets the tower-arm height matched by the booster's upper catch frame near the grid fins."));
            root.Add(UIHelper.Slider("Target Yaw (deg)", envConfig.landingTargetYawDeg, -180f, 180f,
                value => envConfig.landingTargetYawDeg = value,
                "Sets the heading the rocket should match when reaching the catch point."));
            root.Add(UIHelper.ReadOnly(
                "Catch Limits",
                $"catch @ {envConfig.landingCatchAltitude:F1} m, {envConfig.CurrentLandingSuccessRadius:F1} m, " +
                $"{envConfig.CurrentLandingSuccessMaxYawErrorDeg:F0} deg yaw (edit in Reward tab)"));
            root.Add(UIHelper.ReadOnly(
                "Catch Platform",
                envConfig.landingPlatformEnabled
                    ? $"{envConfig.CurrentLandingPlatformHalfSize:F1} m half-size, hold " +
                      $"{envConfig.CurrentLandingPlatformStableHoldTime:F2} s (edit hold in Reward tab)"
                    : "disabled"));
        }

        /// <summary>Shows the fixed physical-pad and deployed-foot contact contract.</summary>
        void BuildLegLandingTargetControls(VisualElement root)
        {
            LandingCurriculumProfile profile = envConfig.GetActiveLandingCurriculumProfile(
                envConfig.ActiveLandingCurriculumProgress);
            TerminationParameters termination =
                envConfig.GetTrainingObjective(ScenarioType.LegLanding).terminations;
            root.Add(UIHelper.SectionLabel("Physical Landing Target"));
            root.Add(UIHelper.ReadOnly("Pad", "Existing 20 x 20 m Landing_Pad collider"));
            root.Add(UIHelper.ReadOnly(
                "Stable Contact",
                $"at least {termination.legMinimumStableFeet} of 4 feet for " +
                $"{profile.platformStableHoldTime:F2} s (edit in Reward tab)"));
            root.Add(UIHelper.ReadOnly("Touchdown Limits",
                $"vertical < {profile.successMaxVerticalSpeed:F1} m/s, horizontal < " +
                $"{profile.successMaxHorizontalSpeed:F1} m/s, tilt < {profile.successMaxTiltDeg:F0} deg " +
                "(edit in Reward tab)"));
        }

        /// <summary>
        /// Builds hover-target movement controls. Capture criteria are read-only
        /// here because the Reward tab is their single editable source of truth.
        /// </summary>
        void BuildMovingTargetControls(VisualElement root)
        {
            TerminationParameters termination =
                envConfig.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
            float difficulty = envConfig.hoverTrackCurriculumProgress;
            root.Add(UIHelper.SectionLabel("Moving Target"));
            root.Add(UIHelper.ReadOnly("Move Interval", $"{envConfig.targetMoveInterval:F1} s (curriculum)"));
            root.Add(UIHelper.Slider("Move Radius (m)", envConfig.targetMoveRadius, 2f, 80f,
                value => envConfig.targetMoveRadius = value,
                "Sets how far the hover target can move from its previous position."));
            root.Add(UIHelper.ReadOnly(
                "Capture Criteria",
                $"{termination.trackingCaptureRadiusM.At(difficulty):F1} m radius, " +
                $"{termination.trackingCaptureMaxHorizontalSpeedMps.At(difficulty):F1} m/s, " +
                $"{termination.trackingCaptureMaxTiltDeg.At(difficulty):F0} deg, " +
                $"hold {termination.trackingCaptureHoldSeconds.At(difficulty):F2} s " +
                "(edit in Reward tab)"));
        }

        /// <summary>
        /// Adds one described spawn-profile slider and clamps the complete profile
        /// after editing so paired minimum/maximum ranges remain safe to sample.
        /// </summary>
        static void AddProfileSlider(
            VisualElement root,
            InferenceSpawnProfile profile,
            string label,
            float value,
            float min,
            float max,
            Action<float> apply,
            string description)
        {
            root.Add(UIHelper.Slider(label, value, min, max, nextValue =>
            {
                apply(nextValue);
                profile.Clamp();
            }, description));
        }
    }
}
