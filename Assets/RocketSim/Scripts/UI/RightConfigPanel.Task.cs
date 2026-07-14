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
        /// Returns the currently selected task, falling back to Landing while
        /// the environment config is not yet connected.
        /// </summary>
        ScenarioType CurrentScenario()
        {
            return envConfig?.scenario ?? ScenarioType.Landing;
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
                    if (selectedScenario == ScenarioType.Landing) envConfig.ResetLandingCurriculum();
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
        /// Builds training scenario cards plus the scenario-specific reward section.
        /// </summary>
        VisualElement BuildTrainingScenarioSection(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Training Scenario"));

            var grid = new VisualElement();
            grid.AddToClassList("rs-scenario-grid");
            var rewardModelRoot = new VisualElement { name = "reward-model-section" };
            var curriculumRoot = new VisualElement { name = "curriculum-section" };

            foreach (var (type, scenarioName, desc) in Scenarios)
            {
                var card = CreateScenarioCard(type, scenarioName, desc, selectedScenario =>
                {
                    envConfig.scenario = selectedScenario;
                    envConfig.moveTargetEnabled = ScenarioProfile.UsesMovingTarget(selectedScenario);
                    ApplyCurrentScenarioHardwareDefaults();
                    if (selectedScenario == ScenarioType.HoverTracking)
                        envConfig.ResetHoverTrackCurriculum();
                    if (selectedScenario == ScenarioType.Landing)
                        envConfig.ResetLandingCurriculum();
                    Dirty();
                    BuildVehicleTab(_tabContents[VehicleTab]);
                    RefreshScenarioCards(grid);
                    BuildRewardModelSection(rewardModelRoot);
                    BuildCurriculumSection(curriculumRoot);
                });
                grid.Add(card);
            }

            c.Add(grid);
            RefreshScenarioCards(grid);

            c.Add(rewardModelRoot);
            BuildRewardModelSection(rewardModelRoot);

            return curriculumRoot;
        }

        /// <summary>
        /// Builds scenario cards and spawn controls for the selected inference run.
        /// </summary>
        void BuildInferenceScenarioSection(VisualElement root)
        {
            root.Clear();
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

            float altitudeMax = envConfig.scenario == ScenarioType.Takeoff ? 200f : 500f;
            AddProfileSlider(root, profile, "Altitude Min (m)", profile.altitudeMin, 0f, altitudeMax,
                value => profile.altitudeMin = value,
                "Lowest altitude from which an inference episode can start.");
            AddProfileSlider(root, profile, "Altitude Max (m)", profile.altitudeMax, 0f, altitudeMax,
                value => profile.altitudeMax = value,
                "Highest altitude from which an inference episode can start.");
            AddProfileSlider(root, profile, "Offset Radius (m)", profile.horizontalOffsetMax, 0f, 100f,
                value => profile.horizontalOffsetMax = value,
                "Maximum horizontal distance between the rocket and target at spawn.");
        }

        /// <summary>Builds starting vertical and horizontal velocity ranges.</summary>
        void BuildInitialMotionControls(VisualElement root, InferenceSpawnProfile profile)
        {
            root.Add(UIHelper.SectionLabel("Initial Motion"));
            AddProfileSlider(root, profile, "Vertical Speed Min", profile.verticalSpeedMin, -100f, 100f,
                value => profile.verticalSpeedMin = value,
                "Lowest initial vertical velocity; negative values mean downward motion.");
            AddProfileSlider(root, profile, "Vertical Speed Max", profile.verticalSpeedMax, -100f, 100f,
                value => profile.verticalSpeedMax = value,
                "Highest initial vertical velocity; positive values mean upward motion.");
            AddProfileSlider(root, profile, "Horizontal Speed Min", profile.horizontalSpeedMin, 0f, 30f,
                value => profile.horizontalSpeedMin = value,
                "Lowest horizontal speed used when the episode begins.");
            AddProfileSlider(root, profile, "Horizontal Speed Max", profile.horizontalSpeedMax, 0f, 30f,
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
            if (envConfig.scenario == ScenarioType.Landing)
                BuildLandingTargetControls(root);
            else if (envConfig.scenario == ScenarioType.HoverTracking)
                BuildMovingTargetControls(root);
        }

        /// <summary>Builds inference catch height/yaw controls and capture-limit readouts.</summary>
        void BuildLandingTargetControls(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Chopstick Target"));
            root.Add(UIHelper.Slider("Catch Height - Base (m)", envConfig.landingCatchAltitude, 0.5f, 80f,
                value => envConfig.landingCatchAltitude = Mathf.Max(0.5f, value),
                "Sets the target catch height above the base of the launch structure."));
            root.Add(UIHelper.Slider("Target Yaw (deg)", envConfig.landingTargetYawDeg, -180f, 180f,
                value => envConfig.landingTargetYawDeg = value,
                "Sets the heading the rocket should match when reaching the catch point."));
            root.Add(UIHelper.ReadOnly(
                "Catch Limits",
                $"catch @ {envConfig.landingCatchAltitude:F1} m, {envConfig.CurrentLandingSuccessRadius:F1} m, {envConfig.CurrentLandingSuccessMaxYawErrorDeg:F0} deg yaw"));
            root.Add(UIHelper.ReadOnly(
                "Catch Platform",
                envConfig.landingPlatformEnabled
                    ? $"{envConfig.CurrentLandingPlatformHalfSize:F1} m half-size, hold {envConfig.CurrentLandingPlatformStableHoldTime:F2} s"
                    : "disabled"));
        }

        /// <summary>Builds hover-target movement and settling controls.</summary>
        void BuildMovingTargetControls(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Moving Target"));
            root.Add(UIHelper.ReadOnly("Move Interval", $"{envConfig.targetMoveInterval:F1} s (curriculum)"));
            root.Add(UIHelper.Slider("Move Radius (m)", envConfig.targetMoveRadius, 2f, 80f,
                value => envConfig.targetMoveRadius = value,
                "Sets how far the hover target can move from its previous position."));
            root.Add(UIHelper.Slider("Settle Radius (m)", envConfig.hoverTrackSettleRadius, 1f, 20f,
                value => envConfig.hoverTrackSettleRadius = value,
                "Sets how close the rocket must be before precise settling rewards begin."));
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
