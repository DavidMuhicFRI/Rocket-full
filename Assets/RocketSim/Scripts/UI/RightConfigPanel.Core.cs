// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Core.cs
// Purpose: Coordinates the right-side panel lifecycle, shell, launch state, and shared UI helpers.
// Main flow: build tabs from config objects -> validate before launch -> lock
// experiment settings during a run -> unlock and restore preview after Stop/failure.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        // Tab labels and theme classes stay together so their ordering cannot drift.
        private static readonly (string name, string themeClass)[] Tabs =
        {
            ("Vehicle",   "rs-theme-parts"),
            ("Task",      "rs-theme-scenario"),
            ("Env",       "rs-theme-env"),
            ("Run",       "rs-theme-run"),
            ("Reward",    "rs-theme-rewards"),
            ("ML",        "rs-theme-ml"),
            ("Faults",    "rs-theme-faults"),
            ("Data",      "rs-theme-telemetry"),
        };

        /// <summary>
        /// Initializes the panel after ConfigBridge has supplied the one shared
        /// session draft.
        /// </summary>
        void Awake()
        {
            _doc = GetComponent<UIDocument>();
            runCoordinator ??= GetComponent<SimulationRunCoordinator>();

            if (runCoordinator != null)
                runCoordinator.OnStateChanged += OnRunStateChanged;

            RequireSession().Config.EnsureSections();
        }

        /// <summary>
        /// Builds the panel before the first frame and opens it on the Parts tab.
        /// </summary>
        void Start()
        {
            if (rocketStyles != null) _doc.rootVisualElement.styleSheets.Add(rocketStyles);
            _expanded = true;
            ApplyCurrentScenarioHardwareDefaults();
            BuildPanel();
            _animCur = _animTgt = _panelWidth;
            Reposition();
        }

        /// <summary>
        /// Runs panel animation and live status refreshes once per rendered frame.
        /// </summary>
        void Update()
        {
            _animCur = Mathf.Lerp(_animCur, _animTgt, Time.deltaTime * AnimSpd);
            Reposition();
            RefreshHardwareTestStatus();
            ApplyLiveCurriculumOnInterval();
        }

        /// <summary>
        /// Builds the right-side drawer, tabs, launch button, and hardware-test controls from the current config objects.
        /// </summary>
        void BuildPanel()
        {
            var root = _doc.rootVisualElement;
            root.Clear();

            _panel = new VisualElement();
            _panel.name = "rs-right-panel";
            _panel.AddToClassList("rs-right-panel");
            _panel.style.width = _panelWidth;

            _resizeHandle = new VisualElement { name = "rs-panel-resize-handle" };
            _resizeHandle.AddToClassList("rs-panel-resize-handle");
            _resizeHandle.RegisterCallback<PointerDownEvent>(BeginResize);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(ContinueResize);
            _resizeHandle.RegisterCallback<PointerUpEvent>(EndResize);
            _resizeHandle.RegisterCallback<PointerEnterEvent>(_ => ShowResizeCursor());
            _resizeHandle.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!_resizeHandle.HasPointerCapture(PointerId.mousePointerId))
                    RestoreDefaultCursor();
            });
            _panel.Add(_resizeHandle);

            var tabBar = new VisualElement();
            tabBar.AddToClassList("rs-tab-bar");

            _tabBtns = new Button[Tabs.Length];
            _tabContents = new VisualElement[Tabs.Length];

            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                _tabBtns[i] = new Button(() => SelectTab(idx)) { text = Tabs[idx].name };
                _tabBtns[i].AddToClassList("rs-tab-btn");
                _tabBtns[i].AddToClassList(Tabs[i].themeClass);
                tabBar.Add(_tabBtns[i]);
            }

            _panel.Add(tabBar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "rs-settings-scroll";
            scroll.style.flexGrow = 1;
            _settingsScroll = scroll;

            for (int i = 0; i < Tabs.Length; i++)
            {
                _tabContents[i] = new VisualElement();
                _tabContents[i].AddToClassList("rs-panel-content");
                _tabContents[i].AddToClassList(Tabs[i].themeClass);
                scroll.Add(_tabContents[i]);
            }

            BuildAllTabs();

            _panel.Add(scroll);

            _panel.Add(BuildFixedFooter());

            _tab = new Button(ToggleExpand);
            _tab.name = "rs-right-tab";
            _tab.AddToClassList("rs-right-tab");
            _tab.text = _expanded ? ">" : "<";

            root.Add(_panel);
            root.Add(_tab);
            _hardwareTestDock = BuildHardwareTestDock();
            root.Add(_hardwareTestDock);
            SelectTab(0);
            RefreshStartButton();
            SetConfigurationLocked(false);
        }

        /// <summary>
        /// Builds the always-visible footer containing notifications, mode
        /// selection, and Start/Stop actions. Keeping it outside the ScrollView
        /// makes the essential run controls reachable from every tab position.
        /// </summary>
        VisualElement BuildFixedFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("rs-panel-footer");

            _notificationLabel = new Label();
            _notificationLabel.AddToClassList("rs-panel-notification");
            _notificationLabel.style.display = DisplayStyle.None;
            footer.Add(_notificationLabel);

            var actions = new VisualElement();
            actions.AddToClassList("rs-footer-actions");

            // Keeping mode and launch in one footer row leaves the top of the
            // panel for navigation and gives the scroll area more room.
            _trainingModeBtn = new Button(() => SetBehaviorMode(BehaviorType.Training)) { text = "Training" };
            _inferenceModeBtn = new Button(() => SetBehaviorMode(BehaviorType.Inference)) { text = "Inference" };
            _trainingModeBtn.AddToClassList("rs-footer-mode-btn");
            _inferenceModeBtn.AddToClassList("rs-footer-mode-btn");
            _startBtn = UIHelper.ActionButton("Start Training", OnLaunchClicked);
            _stopBtn = UIHelper.DangerButton("Stop", OnStopClicked);
            actions.Add(_trainingModeBtn);
            actions.Add(_inferenceModeBtn);
            actions.Add(_startBtn);
            actions.Add(_stopBtn);
            footer.Add(actions);
            RefreshModeButtons();
            return footer;
        }

        /// <summary>
        /// Rebuilds each tab from the current session draft. Individual
        /// builders own their tab only, keeping cross-tab refreshes predictable.
        /// </summary>
        void BuildAllTabs()
        {
            BuildVehicleTab(_tabContents[VehicleTab]);
            BuildScenarioTab(_tabContents[TaskTab]);
            BuildEnvironmentTab(_tabContents[EnvironmentTab]);
            BuildRunTab(_tabContents[RunTab]);
            BuildRewardsTab(_tabContents[RewardsTab]);
            BuildMLTab(_tabContents[MlTab]);
            BuildFaultsTab(_tabContents[FaultsTab]);
            BuildTelemetryTab(_tabContents[TelemetryTab]);
        }

        /// <summary>
        /// Rebuilds every tab from the current config objects while preserving the selected tab.
        /// </summary>
        public void RebuildUI()
        {
            if (_panel == null) return;
            ApplyCurrentScenarioHardwareDefaults();
            BuildAllTabs();
            SelectTab(_activeTab);
            RefreshStartButton();
            SetConfigurationLocked(_runActive);
        }

        /// <summary>
        /// Applies scenario defaults and refreshes the editable vehicle preview.
        /// Active runs are locked and continue using their frozen session copy.
        /// </summary>
        void Dirty()
        {
            if (_runActive) return;
            ApplyCurrentScenarioHardwareDefaults();
            NotifySessionChanged();
            if (envConfig.behaviorType == BehaviorType.Inference &&
                RunIdsEqual(_loadedInferenceRunId, envConfig.runId) &&
                !SimulationRunService.TryValidatePolicySchema(
                    envConfig.runId,
                    partsConfig,
                    envConfig.scenario,
                    out string compatibilityError))
            {
                ShowNotification(compatibilityError, true);
            }
            RefreshStartButton();
        }

        /// <summary>
        /// Pushes pending UI changes, freezes the draft, and asks the coordinator
        /// to start either training or inference.
        /// </summary>
        void OnLaunchClicked()
        {
            if (_runActive) return;
            Dirty();
            if (!ValidateConfiguration(out string error, out string warning))
            {
                ShowNotification(error, true);
                return;
            }
            if (areaHost.envConfig.behaviorType == BehaviorType.Inference && !CanStartLoadedInferenceRun())
            {
                Debug.LogError($"[Panel] Cannot start inference because run '{areaHost.envConfig.runId}' has not been loaded with complete configs and a model.");
                return;
            }

            bool canResumeLoadedRun = _resumeRun && RunIdsEqual(_loadedConfigRunId, envConfig.runId);
            if (_resumeRun && !canResumeLoadedRun)
            {
                Debug.LogWarning("[Panel] Resume was disabled because the selected run no longer matches the loaded run config.");
                _resumeRun = false;
            }

            // Launching accepts the imported preview as the current draft.
            RequireSession().CommitImport();
            _runActive = true;
            SetConfigurationLocked(true);
            ShowNotification(string.IsNullOrEmpty(warning) ? "Settings are locked until the run stops." : warning, false);
            try
            {
                // Mark the UI active before Launch so a synchronous Failed event
                // can reliably unlock it again.
                RunLaunchMode launchMode = envConfig.behaviorType == BehaviorType.Inference
                    ? envConfig.inferencePurpose == InferencePurpose.StandardEvaluation
                        ? RunLaunchMode.Evaluation
                        : RunLaunchMode.ManualInference
                    : canResumeLoadedRun
                        ? RunLaunchMode.ResumeTraining
                        : string.IsNullOrWhiteSpace(_initializeFromRunId)
                            ? RunLaunchMode.NewTraining
                            : RunLaunchMode.InitializeTraining;
                runCoordinator.Launch(
                    areaHost,
                    new RunLaunchRequest(envConfig.runId, launchMode, _initializeFromRunId));
            }
            catch (Exception ex)
            {
                _runActive = false;
                SetConfigurationLocked(false);
                ShowNotification($"Launch failed: {ex.Message}", true);
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Stops Python training if present, removes active areas, restores the
        /// frozen preview rocket, and makes all configuration controls editable.
        /// </summary>
        void OnStopClicked()
        {
            runCoordinator?.StopRun();
            areaHost?.StopActiveRunAndShowPreview();
            _runActive = false;
            SetConfigurationLocked(false);
            ShowNotification("Run stopped. Settings can be edited again.", false);
        }

        /// <summary>
        /// Handles asynchronous launcher failure. A failed Python process must
        /// unlock the panel because no later Stop event is guaranteed to arrive.
        /// </summary>
        void OnRunStateChanged(SimulationRunCoordinator.RunState state)
        {
            if (state == SimulationRunCoordinator.RunState.Failed)
            {
                _runActive = false;
                SetConfigurationLocked(false);
                ShowNotification("Training could not start. Check the Unity console and trainer environment.", true);
            }
            else if (state == SimulationRunCoordinator.RunState.Completed)
            {
                _runActive = false;
                SetConfigurationLocked(false);
                string summary = runCoordinator != null ? runCoordinator.LastEvaluationSummaryPath : null;
                ShowNotification(
                    string.IsNullOrWhiteSpace(summary)
                        ? "Evaluation completed."
                        : $"Evaluation completed. Summary: {summary}",
                    false);
            }
        }

        /// <summary>
        /// Changes the launch button text and enabled state to match the active mode.
        /// </summary>
        void RefreshStartButton()
        {
            if (_startBtn == null) return;
            _startBtn.text = envConfig.behaviorType == BehaviorType.Training
                ? "Start Training"
                : envConfig.inferencePurpose == InferencePurpose.StandardEvaluation
                    ? "Start Evaluation"
                    : "Start Manual Inference";

            bool canStart = runCoordinator != null && areaHost != null && HasValidRunId(envConfig.runId);

            if (canStart && envConfig.behaviorType == BehaviorType.Inference)
                canStart = CanStartLoadedInferenceRun();

            _startBtn.SetEnabled(canStart);
            if (_stopBtn != null)
                _stopBtn.style.display = _runActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Runs inexpensive checks that prevent clearly invalid launches. Hard
        /// errors stop the run, while recoverable configuration concerns are
        /// returned as warnings so users can inspect them before launch.
        /// </summary>
        bool ValidateConfiguration(out string error, out string warning)
        {
            error = "";
            warning = "";
            if (runCoordinator == null) { error = "SimulationRunCoordinator is not assigned."; return false; }
            if (areaHost == null) { error = "SimulationAreaHost is not assigned."; return false; }
            if (!HasValidRunId(envConfig.runId)) { error = "Run ID cannot be empty."; return false; }
            if (partsConfig.GetActiveEngineCount() <= 0) { error = "The vehicle needs at least one active engine."; return false; }
            if (partsConfig.EstimatedMaxFuelCapacity() <= 0f || partsConfig.startFuelMass <= 0f) { error = "Starting fuel must be greater than zero."; return false; }
            if (mlConfig.batchSize <= 0 || mlConfig.bufferSize < mlConfig.batchSize)
            {
                error = "ML buffer size must be at least as large as batch size.";
                return false;
            }
            if (mlConfig.checkpointInterval <= 0 || mlConfig.keepCheckpoints <= 0)
            {
                error = "Checkpoint interval and retained checkpoint count must be positive.";
                return false;
            }
            if (!Enum.IsDefined(typeof(ScenarioType), envConfig.scenario))
            {
                error = $"Scenario value {(int)envConfig.scenario} is not part of the current schema.";
                return false;
            }

            ObjectiveValidationResult objectiveValidation = ObjectiveValidator.Validate(
                envConfig.scenario,
                envConfig.GetTrainingObjective(envConfig.scenario));
            for (int i = 0; i < objectiveValidation.issues.Count; i++)
            {
                ObjectiveValidationIssue issue = objectiveValidation.issues[i];
                if (issue.severity == ObjectiveValidationSeverity.Error)
                {
                    error = $"Objective: {issue.message}";
                    return false;
                }

                string objectiveWarning = $"Objective: {issue.message}";
                warning = string.IsNullOrEmpty(warning)
                    ? objectiveWarning
                    : $"{warning}\n{objectiveWarning}";
            }
            if (envConfig.behaviorType == BehaviorType.Training &&
                !SimulationRunService.TryValidateTrainingDestination(
                    envConfig.runId,
                    _resumeRun,
                    envConfig,
                    partsConfig,
                    mlConfig,
                    out string destinationError))
            {
                error = destinationError;
                return false;
            }
            if (envConfig.behaviorType == BehaviorType.Training &&
                !_resumeRun &&
                !SimulationRunService.TryValidateInitializationSource(
                    _initializeFromRunId,
                    partsConfig,
                    envConfig.scenario,
                    mlConfig,
                    out string initializationError))
            {
                error = initializationError;
                return false;
            }
            if (envConfig.behaviorType == BehaviorType.Inference && !CanStartLoadedInferenceRun())
            {
                error = "Select a saved run that has both configs and an ONNX model.";
                return false;
            }
            if (envConfig.IsStandardEvaluation && !envConfig.scenario.SupportsStandardEvaluation())
            {
                error = "Standard evaluation supports all four flight tasks.";
                return false;
            }

            if (envConfig.scenario == ScenarioType.ChopstickLanding)
            {
                float catchFrameY = partsConfig.bodyHeight - 1.2f;
                float enginePlaneAltitudeAtCapture = envConfig.landingCatchAltitude - catchFrameY;
                if (enginePlaneAltitudeAtCapture < 1f)
                {
                    error =
                        $"Catch-frame height is too low for this vehicle: the engine plane would be " +
                        $"{enginePlaneAltitudeAtCapture:F1} m above ground at capture.";
                    return false;
                }
            }

            float mass = Mathf.Max(1f, partsConfig.baseDryMass + partsConfig.startFuelMass);
            float thrustToWeight = partsConfig.GetActiveEngineCount() * partsConfig.maxThrustPerEngine / (mass * 9.80665f);
            if (envConfig.scenario.IsLanding() && thrustToWeight <= 1f)
            {
                error = $"Landing-burn thrust-to-weight ratio is only {thrustToWeight:F2}; the vehicle cannot decelerate upward.";
                return false;
            }
            int generatedCheckpoints = Mathf.CeilToInt(
                mlConfig.maxSteps / (float)Mathf.Max(1, mlConfig.checkpointInterval));
            if (mlConfig.keepCheckpoints < generatedCheckpoints)
            {
                string checkpointWarning =
                    $"Warning: this run can create {generatedCheckpoints} checkpoints, but only " +
                    $"{mlConfig.keepCheckpoints} will be retained; early learning-curve models will be deleted.";
                warning = string.IsNullOrEmpty(warning)
                    ? checkpointWarning
                    : $"{warning}\n{checkpointWarning}";
            }
            return true;
        }

        /// <summary>
        /// Enables or disables experiment controls as one operation. Telemetry
        /// remains readable during a run, while settings that would change the
        /// experiment are disabled until Stop.
        /// </summary>
        void SetConfigurationLocked(bool locked)
        {
            if (_tabContents == null) return;
            for (int i = 0; i < _tabContents.Length; i++)
                _tabContents[i].SetEnabled(!locked || i == TelemetryTab);
            _trainingModeBtn?.SetEnabled(!locked);
            _inferenceModeBtn?.SetEnabled(!locked);
            _hardwareTestDock?.SetEnabled(!locked);
            if (_startBtn != null)
                _startBtn.style.display = locked ? DisplayStyle.None : DisplayStyle.Flex;
            if (_stopBtn != null)
                _stopBtn.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Shows one concise message in the fixed footer and applies error styling
        /// without creating transient popup objects.
        /// </summary>
        void ShowNotification(string message, bool isError)
        {
            if (_notificationLabel == null) return;
            _notificationLabel.text = message;
            _notificationLabel.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
            _notificationLabel.EnableInClassList("rs-panel-notification-error", isError);
        }

        /// <summary>
        /// Returns true only when the selected inference id is exactly the run
        /// whose complete saved configs and ONNX model were loaded into the panel.
        /// </summary>
        bool CanStartLoadedInferenceRun()
        {
            return HasValidRunId(envConfig.runId) &&
                   RunIdsEqual(_loadedInferenceRunId, envConfig.runId) &&
                   SimulationRunService.HasRun(envConfig.runId) &&
                   SimulationRunService.TryValidatePolicySchema(
                       envConfig.runId,
                       partsConfig,
                       envConfig.scenario,
                       out _) &&
                   ModelRepository.HasModel(envConfig.runId);
        }

        /// <summary>
        /// Shows the selected tab content and updates active tab button styling.
        /// </summary>
        void SelectTab(int idx)
        {
            _activeTab = idx;
            for (int i = 0; i < _tabContents.Length; i++)
            {
                _tabContents[i].style.display = (i == idx) ? DisplayStyle.Flex : DisplayStyle.None;
                _tabBtns[i].EnableInClassList("rs-tab-btn-active", i == idx);
            }
        }

        /// <summary>
        /// Opens or closes the right-side drawer by changing the animation target.
        /// </summary>
        void ToggleExpand()
        {
            _expanded = !_expanded;
            _animTgt = _expanded ? _panelWidth : 0f;
            _tab.text = _expanded ? ">" : "<";
        }

        /// <summary>
        /// Positions the panel and its handle from the current drawer animation value.
        /// </summary>
        void Reposition()
        {
            if (_panel == null || _tab == null) return;
            _panel.style.width = _panelWidth;
            _panel.style.right = _animCur - _panelWidth;
            _tab.style.right = _animCur;
            UpdatePreviewCameraViewport();
        }

        /// <summary>
        /// Uses only the unobstructed part of the screen for world rendering.
        /// The camera then keeps the preview rocket centered beside the panel
        /// instead of centering it underneath the panel.
        /// </summary>
        void UpdatePreviewCameraViewport()
        {
            float rootWidth = _doc?.rootVisualElement?.resolvedStyle.width ?? 0f;
            if (rootWidth <= 1f || float.IsNaN(rootWidth)) return;

            if (!_previewCamera)
            {
                var controller = FindAnyObjectByType<UI.RocketCameraController>();
                _previewCamera = controller ? controller.GetComponent<Camera>() : Camera.main;
            }
            if (!_previewCamera) return;

            float visibleWidth = Mathf.Clamp01(1f - _animCur / rootWidth);
            _previewCamera.rect = new Rect(0f, 0f, Mathf.Max(0.35f, visibleWidth), 1f);
        }

        /// <summary>Captures the left-edge drag and remembers its starting width.</summary>
        void BeginResize(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _resizeStartPointerX = evt.position.x;
            _resizeStartWidth = _panelWidth;
            _resizeHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        /// <summary>Converts pointer movement into a clamped panel width.</summary>
        void ContinueResize(PointerMoveEvent evt)
        {
            if (!_resizeHandle.HasPointerCapture(evt.pointerId)) return;
            _panelWidth = Mathf.Clamp(_resizeStartWidth + (_resizeStartPointerX - evt.position.x), MinPanelWidth, MaxPanelWidth);
            if (_expanded) _animCur = _animTgt = _panelWidth;
            Reposition();
        }

        /// <summary>Releases the resize drag and restores the normal pointer.</summary>
        void EndResize(PointerUpEvent evt)
        {
            if (_resizeHandle.HasPointerCapture(evt.pointerId))
                _resizeHandle.ReleasePointer(evt.pointerId);
            RestoreDefaultCursor();
        }

        /// <summary>Shows the generated horizontal-resize cursor over the panel edge.</summary>
        void ShowResizeCursor()
        {
            _resizeCursor ??= CreateResizeCursor();
            UnityEngine.Cursor.SetCursor(_resizeCursor, new Vector2(16f, 16f), CursorMode.Auto);
        }

        /// <summary>Returns cursor ownership to Unity after resize interaction ends.</summary>
        void RestoreDefaultCursor()
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>Creates a tiny horizontal double-arrow cursor without adding an image asset.</summary>
        static Texture2D CreateResizeCursor()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RocketSimResizeCursor",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var dark = new Color32(15, 22, 35, 255);
            var light = new Color32(225, 238, 255, 255);
            // Local helper clips every pixel write, keeping the arrow drawing
            // loops readable and preventing an accidental texture overrun.
            void Pixel(int x, int y, Color32 color)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                    pixels[y * size + x] = color;
            }

            for (int x = 5; x <= 26; x++)
            {
                Pixel(x, 14, dark); Pixel(x, 15, light); Pixel(x, 16, light); Pixel(x, 17, dark);
            }
            for (int offset = 0; offset <= 6; offset++)
            {
                Pixel(4 + offset, 15 + offset, light);
                Pixel(4 + offset, 16 - offset, light);
                Pixel(27 - offset, 15 + offset, light);
                Pixel(27 - offset, 16 - offset, light);
            }

            texture.SetPixels32(pixels);
            // Custom runtime cursors must remain CPU-readable.
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// Removes event subscriptions and restores camera/cursor state so
        /// leaving the scene cannot affect the next scene or Play Mode session.
        /// </summary>
        void OnDestroy()
        {
            if (runCoordinator != null)
                runCoordinator.OnStateChanged -= OnRunStateChanged;
            if (_previewCamera != null)
                _previewCamera.rect = new Rect(0f, 0f, 1f, 1f);
            RestoreDefaultCursor();
            if (_resizeCursor != null)
                Destroy(_resizeCursor);
        }

        /// <summary>Run ids may contain any text but cannot be empty or whitespace.</summary>
        static bool HasValidRunId(string runId)
        {
            return !string.IsNullOrWhiteSpace(runId);
        }

        /// <summary>Compares run ids without treating letter case as meaningful.</summary>
        static bool RunIdsEqual(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Changes between training and inference while the simulation is stopped.
        /// Training values are restored after temporarily inspecting a saved model.
        /// </summary>
        void SetBehaviorMode(BehaviorType mode)
        {
            if (_runActive || envConfig.behaviorType == mode) return;

            if (mode == BehaviorType.Inference)
            {
                _trainingSessionSnapshot = RequireSession().Config.DeepCopy();
                _loadedInferenceRunId = null;
                envConfig.behaviorType = BehaviorType.Inference;
                NotifySessionChanged(SessionChangeKind.All);
                RebuildUI();
            }
            else
            {
                RestoreTrainingMode();
            }

            RefreshModeButtons();
        }

        /// <summary>
        /// Updates Training/Inference button highlighting without rebuilding tabs.
        /// </summary>
        void RefreshModeButtons()
        {
            if (envConfig == null) return;
            _trainingModeBtn?.EnableInClassList("rs-mode-btn-active", envConfig.behaviorType == BehaviorType.Training);
            _inferenceModeBtn?.EnableInClassList("rs-mode-btn-active", envConfig.behaviorType == BehaviorType.Inference);
        }

        /// <summary>
        /// Restores the configs that were active before entering inference mode.
        /// </summary>
        void RestoreTrainingMode()
        {
            if (_trainingSessionSnapshot != null)
                RequireSession().Replace(_trainingSessionSnapshot);
            RequireSession().CommitImport();

            _showLoadedRunBanner  = false;
            _resumeRun            = false;
            _loadedConfigRunId    = null;
            _loadedInferenceRunId = null;
            envConfig.behaviorType = BehaviorType.Training;
            NotifySessionChanged(SessionChangeKind.All);

            Dirty();
            RebuildUI();
            RefreshModeButtons();
        }

        /// <summary>
        /// Notifies the manager that the panel replaced one or more session
        /// sections. Kept as a small named operation at cross-tab call sites.
        /// </summary>
        void SyncManagerConfigs()
        {
            NotifySessionChanged(SessionChangeKind.All);
        }

        /// <summary>
        /// Updates the value label inside a named read-only row.
        /// </summary>
        void UpdateLabelText(VisualElement root, string rowName, string newText)
        {
            var row = root.Q<VisualElement>(rowName);
            if (row == null) return;
            var label = row.Query<Label>().Last();
            if (label != null) label.text = newText;
        }

        /// <summary>
        /// Creates compact descriptive text used under grouped telemetry, reward, and curriculum controls.
        /// </summary>
        VisualElement BuildGroupDetail(string text)
        {
            var label = new Label(text);
            label.AddToClassList("rs-group-detail");
            return label;
        }
    }
}
