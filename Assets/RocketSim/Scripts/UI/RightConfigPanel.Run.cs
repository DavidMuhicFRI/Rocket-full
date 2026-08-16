// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Run.cs
// Purpose: Builds the Run tab: run naming/resume, saved inference runs, parallel areas, curriculum, and saved-config loading.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        // Live curriculum values are refreshed four times per second. This is
        // responsive enough for the HUD without rebuilding UI every frame.
        const float CurriculumRefreshInterval = 0.25f;
        float _nextCurriculumRefreshTime;

        /// <summary>
        /// Builds run-management controls. Scenario and rewards have their own
        /// tabs so this tab stays focused on how an experiment is executed.
        /// </summary>
        void BuildRunTab(VisualElement c)
        {
            c.Clear();
            if (envConfig.behaviorType == BehaviorType.Inference)
            {
                BuildInferenceModeContent(c);
                return;
            }

            if (!BuildRunConfigurationSection(c)) return;
            BuildParallelTrainingSection(c);
            var curriculumRoot = new VisualElement { name = "curriculum-section" };
            c.Add(curriculumRoot);
            BuildCurriculumSection(curriculumRoot);
        }

        /// <summary>
        /// Builds run-id validation, autocomplete, resume loading, and the loaded-config banner.
        /// </summary>
        bool BuildRunConfigurationSection(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Run Configuration"));

            if (areaHost == null)
            {
                c.Add(BuildGroupDetail("SimulationAreaHost is not wired yet, so scenario and launch controls cannot be built."));
                return false;
            }

            string[] existingRuns = _sessionLoader.ListRunIds();

            var runIdRow = new VisualElement();
            runIdRow.AddToClassList("rs-field-row");
            runIdRow.style.marginBottom = 2;

            var runIdFieldLabel = new Label("Run ID");
            runIdFieldLabel.AddToClassList("rs-field-label");
            runIdRow.Add(runIdFieldLabel);

            var runIdField = new TextField { value = envConfig.runId };
            runIdField.AddToClassList("rs-run-id-field");
            runIdField.style.flexGrow = 1;
            runIdField.focusable = true;
            runIdField.pickingMode = PickingMode.Position;
            UIHelper.TrackTextInputFocus(runIdField);

            var innerInput = runIdField.Q<VisualElement>("unity-text-input");
            if (innerInput != null)
            {
                innerInput.focusable = true;
                innerInput.pickingMode = PickingMode.Position;
            }
            runIdRow.Add(runIdField);
            c.Add(runIdRow);

            var dropdown = new VisualElement();
            dropdown.name = "run-id-dropdown";
            dropdown.AddToClassList("rs-run-dropdown");
            dropdown.style.display = DisplayStyle.None;
            dropdown.style.maxHeight = 130f;
            dropdown.style.overflow = Overflow.Hidden;
            c.Add(dropdown);

            var validationLabel = new Label("");
            validationLabel.AddToClassList("rs-validation-label");
            c.Add(validationLabel);

            var resumeRow = new VisualElement();
            resumeRow.AddToClassList("rs-field-row");
            var resumeFieldLabel = new Label("Resume Run");
            resumeFieldLabel.AddToClassList("rs-field-label");
            resumeRow.Add(resumeFieldLabel);

            var resumeToggle = new Toggle { value = _resumeRun };
            resumeToggle.AddToClassList("rs-toggle");
            resumeToggle.SetEnabled(RunExists(envConfig.runId));
            resumeRow.Add(resumeToggle);
            c.Add(resumeRow);

            DropdownField initializeFromField = null;
            if (envConfig.behaviorType == BehaviorType.Training)
            {
                var initializationChoices = new List<string> { "None" };
                foreach (string existingRun in existingRuns)
                    if (!RunIdsEqual(existingRun, envConfig.runId))
                        initializationChoices.Add(existingRun);

                int selectedInitialization = 0;
                for (int i = 1; i < initializationChoices.Count; i++)
                    if (RunIdsEqual(initializationChoices[i], _initializeFromRunId))
                        selectedInitialization = i;

                initializeFromField = new DropdownField(
                    "Initialize From",
                    initializationChoices,
                    selectedInitialization);
                initializeFromField.AddToClassList("rs-enum-field");
                initializeFromField.AddToClassList("rs-initialize-from-field");
                initializeFromField.tooltip =
                    "Starts a new run from a prior checkpoint and previews its complete saved session. Resume continues the same run instead.";
                initializeFromField.SetEnabled(!_resumeRun);
                initializeFromField.RegisterValueChangedCallback(evt =>
                {
                    _initializeFromRunId = evt.newValue == "None" ? string.Empty : evt.newValue;
                    if (!string.IsNullOrWhiteSpace(_initializeFromRunId))
                    {
                        try
                        {
                            string targetRunId = envConfig.runId;
                            ApplyRunConfigs(
                                _initializeFromRunId,
                                preserveBehaviorType: true,
                                includeRuntimeState: false);
                            envConfig.runId = targetRunId;
                            RebuildUI();
                            ShowNotification(
                                $"Applied session from '{_initializeFromRunId}'.",
                                false);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[Panel] Failed to load run rewards: {ex.Message}");
                            _initializeFromRunId = string.Empty;
                            initializeFromField.SetValueWithoutNotify("None");
                            ShowNotification("Could not load the selected run's rewards.", true);
                        }
                    }
                    else if (RestoreSessionBeforeRunImport())
                    {
                        RebuildUI();
                        ShowNotification("Restored the setup from before checkpoint initialization.", false);
                    }
                    Dirty();
                    RefreshStartButton();
                });
                c.Add(initializeFromField);
                c.Add(BuildGroupDetail(
                    "Initialization previews the source session. You may edit compatible values; engine, fin, RCS policy channels, and network shape must still match the checkpoint."));
            }

            var banner = new VisualElement();
            banner.name = "run-loaded-banner";
            banner.AddToClassList("rs-info-banner");
            banner.style.display = _showLoadedRunBanner ? DisplayStyle.Flex : DisplayStyle.None;

            var bannerLabel = new Label(LoadedConfigBannerText());
            bannerLabel.AddToClassList("rs-banner-label");
            banner.Add(bannerLabel);

            c.Add(banner);

            // The local helpers below share this section's controls and run list.
            // Keeping them local prevents Run ID UI details leaking into the panel class.

            // Updates the text and color directly beneath the Run ID field.
            void SetValidation(string text, Color col)
            {
                validationLabel.text = text;
                validationLabel.style.color = new StyleColor(col);
            }

            // Explains why changing a restored training configuration is risky.
            string LoadedConfigBannerText() =>
                $"Applied run configuration and rewards from \"{envConfig.runId}\"\nReward edits are saved as a new revision when this run resumes.";

            // Finds the canonical saved spelling of an id, ignoring letter case.
            string FindExistingRun(string id)
            {
                if (string.IsNullOrWhiteSpace(id)) return null;
                foreach (var run in existingRuns)
                    if (RunIdsEqual(run, id))
                        return run;
                return null;
            }

            // A run is resumable only when its complete config set is present.
            bool RunExists(string id)
            {
                string existingRun = FindExistingRun(id);
                return existingRun != null && _sessionLoader.HasRun(existingRun);
            }

            // Clears state that became invalid after the Run ID changed.
            void ClearResumeState()
            {
                bool restored = RestoreSessionBeforeRunImport();
                _resumeRun = false;
                _loadedConfigRunId = null;
                _showLoadedRunBanner = false;
                resumeToggle.SetValueWithoutNotify(false);
                initializeFromField?.SetEnabled(true);
                banner.style.display = DisplayStyle.None;
                if (restored)
                    RebuildUI();
            }

            // Sanitizes input, updates resume availability, and refreshes suggestions.
            void SetRunId(string raw, bool updateField, bool showDropdown)
            {
                string sanitised = Regex.Replace(raw ?? "", @"[^\w\-]", "_");
                string canonicalRun = FindExistingRun(sanitised);
                string nextRunId = canonicalRun ?? sanitised;
                bool canResume = canonicalRun != null && _sessionLoader.HasRun(canonicalRun);
                bool changed = !RunIdsEqual(envConfig.runId, nextRunId);

                if (updateField && runIdField.value != nextRunId)
                    runIdField.SetValueWithoutNotify(nextRunId);

                envConfig.runId = nextRunId;
                resumeToggle.SetEnabled(canResume);

                if (changed)
                    ClearResumeState();

                UpdateValidation(nextRunId);
                RefreshStartButton();

                if (showDropdown)
                    PopulateDropdown(nextRunId);
            }

            // Chooses the status for empty, new, resumable, or incomplete ids.
            void UpdateValidation(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    SetValidation("Run ID cannot be empty", new Color(1f, 0.45f, 0.25f));
                else if (RunExists(id))
                    SetValidation(
                        _resumeRun ? "Existing run - Resume enabled" : "Existing run - enable Resume to continue",
                        _resumeRun ? new Color(0.35f, 0.85f, 0.45f) : new Color(1f, 0.65f, 0.25f));
                else if (FindExistingRun(id) != null)
                    SetValidation("Existing run is missing configs", new Color(1f, 0.65f, 0.25f));
                else
                    SetValidation("New run will be created", new Color(0.55f, 0.75f, 1f));
            }

            // Rebuilds autocomplete entries from saved runs matching the filter.
            void PopulateDropdown(string filter)
            {
                dropdown.Clear();
                var matches = new List<string>();
                foreach (var run in existingRuns)
                    if (string.IsNullOrEmpty(filter) || run.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        matches.Add(run);

                dropdown.style.display = matches.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

                foreach (var run in matches)
                {
                    var info = _sessionLoader.GetSummary(run);
                    var item = new VisualElement();
                    item.AddToClassList("rs-run-item");
                    item.pickingMode = PickingMode.Position;

                    var nameLabel = new Label(run);
                    nameLabel.AddToClassList("rs-run-item-name");
                    nameLabel.pickingMode = PickingMode.Ignore;
                    item.Add(nameLabel);

                    if (info != null)
                    {
                        var metaLabel = new Label(info.scenario.ToString());
                        metaLabel.AddToClassList("rs-run-item-meta");
                        metaLabel.pickingMode = PickingMode.Ignore;
                        item.Add(metaLabel);
                    }

                    var capturedRun = run;
                    item.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        evt.StopPropagation();
                        SetRunId(capturedRun, updateField: true, showDropdown: false);
                        dropdown.style.display = DisplayStyle.None;
                    });

                    dropdown.Add(item);
                }
            }

            runIdField.RegisterValueChangedCallback(evt =>
            {
                SetRunId(evt.newValue, updateField: true, showDropdown: true);
            });

            runIdField.RegisterCallback<FocusInEvent>(_ => PopulateDropdown(runIdField.value));
            runIdField.RegisterCallback<FocusOutEvent>(_ =>
                runIdField.schedule
                    .Execute(() => dropdown.style.display = DisplayStyle.None)
                    .ExecuteLater(150));

            resumeToggle.RegisterValueChangedCallback(evt =>
            {
                _resumeRun = evt.newValue;
                UpdateValidation(envConfig.runId);
                initializeFromField?.SetEnabled(!evt.newValue);
                if (evt.newValue)
                {
                    _initializeFromRunId = string.Empty;
                    initializeFromField?.SetValueWithoutNotify("None");
                    try
                    {
                        if (!_sessionLoader.HasRun(envConfig.runId))
                            throw new InvalidOperationException($"Config files missing in run '{envConfig.runId}'.");

                        ApplyRunConfigs(envConfig.runId, preserveBehaviorType: true);
                        _loadedConfigRunId = envConfig.runId;
                        _showLoadedRunBanner = true;
                        banner.style.display = DisplayStyle.Flex;
                        bannerLabel.text = LoadedConfigBannerText();
                        RebuildUI();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Panel] Failed to load run configs: {ex.Message}");
                        resumeToggle.SetValueWithoutNotify(false);
                        _resumeRun = false;
                        _loadedConfigRunId = null;
                    }
                }
                else
                {
                    bool restored = RestoreSessionBeforeRunImport();
                    _loadedConfigRunId = null;
                    _showLoadedRunBanner = false;
                    banner.style.display = DisplayStyle.None;
                    if (restored)
                    {
                        RebuildUI();
                        ShowNotification("Restored the setup from before Resume was enabled.", false);
                    }
                }
            });

            UpdateValidation(envConfig.runId);
            return true;
        }

        /// <summary>
        /// Builds auto/manual parallel-area controls and derived spacing readouts.
        /// </summary>
        void BuildParallelTrainingSection(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Parallel Training"));

            c.Add(UIHelper.Toggle("Auto-Detect (Hardware)", _autoScaleTrainingAreas, v =>
            {
                _autoScaleTrainingAreas = v;
                if (v)
                    areaHost.instanceCount = AutoDetectedTrainingAreaCount();
                UpdateScalingUI(c, v);
                RefreshEnvDisplay(c);
            }));

            var sliderContainer = new VisualElement { name = "manual-slider-container" };
            sliderContainer.Add(UIHelper.IntSlider("Manual Count", areaHost.instanceCount, 1, 64, v =>
            {
                areaHost.instanceCount = v;
                RefreshEnvDisplay(c);
            }, "Sets how many simulation areas collect training experience in parallel."));
            c.Add(sliderContainer);

            UpdateScalingUI(c, _autoScaleTrainingAreas);

            c.Add(UIHelper.Slider("Area spacing (m)", areaHost.spacing, 250f, 2000f, v =>
            {
                areaHost.spacing = v;
                RefreshEnvDisplay(c);
            }, "Sets the distance between parallel areas so their rockets cannot interact."));

            var countRow = UIHelper.ReadOnly("Active Areas", $"{areaHost.instanceCount}");
            countRow.name = "label-active-areas";
            c.Add(countRow);

            var spacingRow = UIHelper.ReadOnly("Area Spacing", $"{areaHost.spacing} m");
            spacingRow.name = "label-area-spacing";
            c.Add(spacingRow);

            RefreshEnvDisplay(c);
        }

        /// <summary>
        /// Builds the curriculum panel for scenarios that use automatic
        /// curriculum. Runtime stats are intentionally shown in the HUD instead.
        /// </summary>
        void BuildCurriculumSection(VisualElement root)
        {
            if (root == null) return;

            root.Clear();
            if (!envConfig.scenario.IsLanding() &&
                envConfig.scenario != ScenarioType.HoverTracking)
            {
                root.style.display = DisplayStyle.None;
                return;
            }

            root.style.display = DisplayStyle.Flex;
            ApplyActiveCurriculumFromUI();

            root.Add(UIHelper.SectionLabel("Curriculum"));
            if (envConfig.scenario.IsLanding())
            {
                var modeField = new EnumField("Landing Progression", envConfig.ActiveLandingCurriculumMode);
                modeField.AddToClassList("rs-enum-field");
                modeField.RegisterValueChangedCallback(evt =>
                {
                    envConfig.ActiveLandingCurriculumMode = (LandingCurriculumMode)evt.newValue;
                    envConfig.ResetActiveLandingCurriculum();
                    Dirty();
                    BuildCurriculumSection(root);
                });
                root.Add(modeField);
                root.Add(BuildGroupDetail(
                    "Adaptive can advance and retreat, Monotonic only advances, and Fixed Full Difficulty is the no-curriculum baseline."));
            }

            root.Add(UIHelper.Slider(
                "Difficulty Increase Speed",
                envConfig.curriculumDifficultyIncreaseSpeed,
                SimEnvironmentConfig.MinCurriculumDifficultyIncreaseSpeed,
                SimEnvironmentConfig.MaxCurriculumDifficultyIncreaseSpeed,
                v =>
                {
                    envConfig.curriculumDifficultyIncreaseSpeed = Mathf.Clamp(
                        v,
                        SimEnvironmentConfig.MinCurriculumDifficultyIncreaseSpeed,
                        SimEnvironmentConfig.MaxCurriculumDifficultyIncreaseSpeed);
                    ApplyActiveCurriculumFromUI();
                    Dirty();
                }, "Scales how quickly difficulty rises after the recent success rate is high enough."));

            if (envConfig.scenario.IsLanding() &&
                envConfig.ActiveLandingCurriculumMode == LandingCurriculumMode.Adaptive)
            {
                root.Add(UIHelper.ReadOnly(
                    "Adaptive Rule",
                    $"> {envConfig.landingCurriculumPromotionSuccessRate:P0} advance, " +
                    $"< {envConfig.landingCurriculumRetreatSuccessRate:P0} retreat"));
                root.Add(UIHelper.ReadOnly(
                    "Easier Replay",
                    $"{envConfig.landingCurriculumEasierReplayProbability:P0} of episodes, " +
                    $"-{envConfig.landingCurriculumEasierReplayOffset:P0} difficulty"));
            }
        }

        /// <summary>
        /// Applies live curriculum values on a throttled cadence without rebuilding the panel.
        /// </summary>
        void ApplyLiveCurriculumOnInterval()
        {
            if (Time.unscaledTime < _nextCurriculumRefreshTime || _panel == null || envConfig == null)
                return;

            _nextCurriculumRefreshTime = Time.unscaledTime + CurriculumRefreshInterval;
            VisualElement root = _panel.Q<VisualElement>("curriculum-section");
            if (root == null) return;

            if (envConfig.scenario.IsLanding())
                envConfig.ApplyActiveLandingCurriculum();
            else if (envConfig.scenario == ScenarioType.HoverTracking)
                envConfig.ApplyHoverTrackCurriculum();
        }

        /// <summary>
        /// Recalculates the active curriculum using the current active area count.
        /// </summary>
        void ApplyActiveCurriculumFromUI()
        {
            if (envConfig.scenario == ScenarioType.HoverTracking)
            {
                envConfig.ApplyHoverTrackCurriculum();
                return;
            }

            if (envConfig.scenario.IsLanding())
                envConfig.ApplyActiveLandingCurriculum();
        }

        /// <summary>
        /// Calculates the automatic parallel-area count from available CPU threads.
        /// </summary>
        static int AutoDetectedTrainingAreaCount()
        {
            return Mathf.Clamp(SystemInfo.processorCount * 2, 1, 64);
        }

        /// <summary>
        /// Shows or hides manual area-count controls depending on auto-scaling mode.
        /// </summary>
        void UpdateScalingUI(VisualElement root, bool auto)
        {
            var container = root.Q<VisualElement>("manual-slider-container");
            if (container != null)
                container.style.display = auto ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Refreshes derived area-count and spacing labels in the training panel.
        /// </summary>
        void RefreshEnvDisplay(VisualElement root)
        {
            if (areaHost == null) return;
            UpdateLabelText(root, "label-active-areas", $"{areaHost.instanceCount}");
            UpdateLabelText(root, "label-area-spacing", $"{areaHost.spacing:F0} m");
        }

        /// <summary>
        /// Builds saved-run selection. Task and spawn controls live in the Task tab.
        /// </summary>
        void BuildInferenceModeContent(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel(
                envConfig.inferencePurpose == InferencePurpose.StandardEvaluation
                    ? "Models To Evaluate"
                    : "Saved Runs"));

            var gridContainer = new VisualElement();
            gridContainer.name = "inference-grid-container";
            c.Add(gridContainer);

            PopulateInferenceGrid(gridContainer);

            c.Add(UIHelper.ActionButton("Refresh Run List", () =>
                PopulateInferenceGrid(gridContainer)));
        }

        /// <summary>
        /// Scans saved runs, builds selectable model cards, and disables cards
        /// that do not have a loadable ONNX model.
        /// </summary>
        void PopulateInferenceGrid(VisualElement gridContainer)
        {
            gridContainer.Clear();

            string[] runs = _sessionLoader.ListRunIds();

            if (runs.Length == 0)
            {
                var empty = new Label(
                    "No saved runs found.\nComplete a training session first.\n\n" +
                    $"Expected path:\n{SimulationRunService.GetResultsRoot()}");
                empty.AddToClassList("rs-empty-state");
                gridContainer.Add(empty);
                return;
            }

            var grid = new VisualElement();
            grid.name = "inference-run-grid";
            grid.AddToClassList("rs-inference-grid");

            foreach (var run in runs)
            {
                var info = _sessionLoader.GetSummary(run);
                var capturedRun = run;
                var card = new VisualElement();
                card.AddToClassList("rs-inference-card");
                card.userData = run;
                card.pickingMode = PickingMode.Position;

                bool hasModel = ModelRepository.HasModel(run);
                bool hasCompleteConfig = _sessionLoader.HasRun(run);
                bool canLoadRun = hasModel && hasCompleteConfig;

                var nameLabel = new Label(run);
                nameLabel.AddToClassList("rs-inference-card-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                card.Add(nameLabel);

                if (info != null)
                {
                    var scenarioLabel = new Label(info.scenario.ToString());
                    scenarioLabel.AddToClassList("rs-inference-card-scenario");
                    scenarioLabel.pickingMode = PickingMode.Ignore;
                    card.Add(scenarioLabel);
                }

                var modelBadge = new Label(hasModel
                    ? hasCompleteConfig ? "Model ready" : "Configs missing"
                    : "No model built");
                modelBadge.style.fontSize = 9;
                modelBadge.style.marginTop = 2;
                modelBadge.style.color = new StyleColor(canLoadRun
                    ? new Color(0.35f, 0.85f, 0.45f)
                    : new Color(0.6f, 0.6f, 0.6f));
                modelBadge.pickingMode = PickingMode.Ignore;
                card.Add(modelBadge);

                if (canLoadRun)
                {
                    card.RegisterCallback<ClickEvent>(_ =>
                    {
                        try
                        {
                            ApplyRunConfigs(capturedRun, preserveBehaviorType: true);
                            _loadedInferenceRunId = envConfig.runId;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[Panel] Failed to load inference configs: {ex.Message}");
                            _loadedInferenceRunId = null;
                            RefreshStartButton();
                            return;
                        }

                        foreach (var child in grid.Children())
                            child.EnableInClassList("rs-inference-card-active",
                                RunIdsEqual((string)child.userData, _loadedInferenceRunId));

                        BuildVehicleTab(_tabContents[VehicleTab]);
                        BuildEnvironmentTab(_tabContents[EnvironmentTab]);
                        BuildScenarioTab(_tabContents[TaskTab]);
                        BuildRewardsTab(_tabContents[RewardsTab]);
                        RefreshStartButton();
                    });
                }
                else
                {
                    card.style.opacity = 0.45f;
                }

                grid.Add(card);
            }

            foreach (var child in grid.Children())
                child.EnableInClassList("rs-inference-card-active",
                    RunIdsEqual((string)child.userData, _loadedInferenceRunId));

            gridContainer.Add(grid);
        }

        /// <summary>
        /// Loads saved environment, hardware, and trainer configs for a run and
        /// wires them back into the panel and manager.
        /// </summary>
        void ApplyRunConfigs(
            string runId,
            bool preserveBehaviorType,
            bool includeRuntimeState = true)
        {
            SimulationSessionConfig imported = _sessionLoader.Load(runId, includeRuntimeState);

            if (preserveBehaviorType)
            {
                // Evaluation-suite settings belong to the evaluator, not to a
                // trained model. Preserve them while model-specific training,
                // vehicle, reward, and environment configs are restored.
                InferencePurpose preservedPurpose = envConfig.inferencePurpose;
                EvaluationConfig preservedEvaluation = JsonUtility.FromJson<EvaluationConfig>(
                    JsonUtility.ToJson(envConfig.EnsureEvaluationConfig()));
                imported.environment.behaviorType = envConfig.behaviorType;
                imported.environment.inferencePurpose = preservedPurpose;
                imported.environment.evaluation = preservedEvaluation;
            }
            imported.environment.runId = runId;
            imported.EnsureSections();
            RequireSession().ApplyImportedSession(imported);
            ApplyCurrentScenarioHardwareDefaults();
            ApplyObjectiveDerivedState(envConfig.scenario);

            NotifySessionChanged(SessionChangeKind.All);
            Dirty();
        }

    }
}
