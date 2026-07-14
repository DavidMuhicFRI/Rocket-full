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
            ("Reward",    "rs-theme-rewards"),
            ("Run",       "rs-theme-run"),
            ("ML",        "rs-theme-ml"),
            ("Faults",    "rs-theme-faults"),
            ("Data",      "rs-theme-telemetry"),
        };

        /// <summary>
        /// Initializes the panel component and keeps any config references that
        /// ConfigBridge already wired before this Awake call.
        /// </summary>
        void Awake()
        {
            _doc = GetComponent<UIDocument>();
            launcher ??= GetComponent<TrainingLauncher>();

            if (launcher != null)
                launcher.OnStateChanged += OnLauncherStateChanged;

            partsConfig ??= new RocketPartsConfig();
            telemetryConfig ??= new TelemetryConfig();
            envConfig ??= new SimEnvironmentConfig();
            mlConfig ??= new MLAgentsConfig();
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
        /// Rebuilds each tab from the current shared config objects. Individual
        /// builders own their tab only, keeping cross-tab refreshes predictable.
        /// </summary>
        void BuildAllTabs()
        {
            BuildVehicleTab(_tabContents[VehicleTab]);
            BuildScenarioTab(_tabContents[TaskTab]);
            BuildEnvironmentTab(_tabContents[EnvironmentTab]);
            BuildRewardsTab(_tabContents[RewardsTab]);
            BuildRunTab(_tabContents[RunTab]);
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
        /// Applies scenario defaults and pushes config changes into spawned training, inference, dummy, or hardware-test areas.
        /// </summary>
        void Dirty()
        {
            if (_runActive) return;
            ApplyCurrentScenarioHardwareDefaults();
            trainingAreaManager?.ApplyPartsConfigToAll();
            RefreshStartButton();
        }

        /// <summary>
        /// Pushes pending UI changes, prepares telemetry, and asks the launcher
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
            if (trainingAreaManager.envConfig.behaviorType == BehaviorType.Inference && !CanStartLoadedInferenceRun())
            {
                Debug.LogError($"[Panel] Cannot start inference because run '{trainingAreaManager.envConfig.runId}' has not been loaded with complete configs and a model.");
                return;
            }

            bool canResumeLoadedRun = _resumeRun && RunIdsEqual(_loadedConfigRunId, envConfig.runId);
            if (_resumeRun && !canResumeLoadedRun)
            {
                Debug.LogWarning("[Panel] Resume was disabled because the selected run no longer matches the loaded run config.");
                _resumeRun = false;
            }

            launcher.resumeIfExists = canResumeLoadedRun;
            if (TelemetryLogger.Instance != null)
            {
                if (trainingAreaManager.envConfig.behaviorType == BehaviorType.Training)
                    TelemetryLogger.Instance.Initialize(trainingAreaManager.telemetryConfig, trainingAreaManager.envConfig.runId);
                else
                    TelemetryLogger.Instance.DisableLogging();
            }
            _runActive = true;
            SetConfigurationLocked(true);
            ShowNotification(string.IsNullOrEmpty(warning) ? "Settings are locked until the run stops." : warning, false);
            try
            {
                // Mark the UI active before Launch so a synchronous Failed event
                // can reliably unlock it again.
                launcher.Launch(trainingAreaManager);
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
            launcher?.StopTraining();
            trainingAreaManager?.StopActiveRunAndShowPreview();
            _runActive = false;
            SetConfigurationLocked(false);
            ShowNotification("Run stopped. Settings can be edited again.", false);
        }

        /// <summary>
        /// Handles asynchronous launcher failure. A failed Python process must
        /// unlock the panel because no later Stop event is guaranteed to arrive.
        /// </summary>
        void OnLauncherStateChanged(TrainingLauncher.TrainingState state)
        {
            if (state == TrainingLauncher.TrainingState.Failed)
            {
                _runActive = false;
                SetConfigurationLocked(false);
                ShowNotification("Training could not start. Check the Unity console and trainer environment.", true);
            }
        }

        /// <summary>
        /// Changes the launch button text and enabled state to match the active mode.
        /// </summary>
        void RefreshStartButton()
        {
            if (_startBtn == null) return;
            _startBtn.text = envConfig.behaviorType == BehaviorType.Training ? "Start Training" : "Start Inference";

            bool canStart = launcher != null && trainingAreaManager != null && HasValidRunId(envConfig.runId);

            if (canStart && envConfig.behaviorType == BehaviorType.Inference)
                canStart = CanStartLoadedInferenceRun();

            _startBtn.SetEnabled(canStart);
            if (_stopBtn != null)
                _stopBtn.style.display = _runActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Runs inexpensive checks that prevent clearly invalid launches. Hard
        /// errors stop the run; a weak takeoff thrust-to-weight ratio is reported
        /// as a warning because it can still be a deliberate experiment.
        /// </summary>
        bool ValidateConfiguration(out string error, out string warning)
        {
            error = "";
            warning = "";
            if (launcher == null) { error = "TrainingLauncher is not assigned."; return false; }
            if (trainingAreaManager == null) { error = "TrainingAreaManager is not assigned."; return false; }
            if (!HasValidRunId(envConfig.runId)) { error = "Run ID cannot be empty."; return false; }
            if (partsConfig.GetActiveEngineCount() <= 0) { error = "The vehicle needs at least one active engine."; return false; }
            if (partsConfig.EstimatedMaxFuelCapacity() <= 0f || partsConfig.startFuelMass <= 0f) { error = "Starting fuel must be greater than zero."; return false; }
            if (mlConfig.batchSize <= 0 || mlConfig.bufferSize < mlConfig.batchSize)
            {
                error = "ML buffer size must be at least as large as batch size.";
                return false;
            }
            if (envConfig.behaviorType == BehaviorType.Inference && !CanStartLoadedInferenceRun())
            {
                error = "Select a saved run that has both configs and an ONNX model.";
                return false;
            }

            float mass = Mathf.Max(1f, partsConfig.baseDryMass + partsConfig.startFuelMass);
            float thrustToWeight = partsConfig.GetActiveEngineCount() * partsConfig.maxThrustPerEngine / (mass * 9.80665f);
            if (envConfig.scenario == ScenarioType.Takeoff && thrustToWeight <= 1f)
                warning = $"Warning: takeoff thrust-to-weight ratio is only {thrustToWeight:F2}.";
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
                   TrainingRunRepository.HasCompleteRunConfig(envConfig.runId) &&
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
            if (launcher != null)
                launcher.OnStateChanged -= OnLauncherStateChanged;
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
                _trainingPartsSnapshot = CloneConfig(partsConfig);
                _trainingEnvSnapshot = CloneConfig(envConfig);
                _trainingMlSnapshot = CloneConfig(mlConfig);
                _loadedInferenceRunId = null;
                envConfig.behaviorType = BehaviorType.Inference;
                SyncManagerConfigs();
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
            if (_trainingPartsSnapshot != null && _trainingEnvSnapshot != null)
            {
                partsConfig = _trainingPartsSnapshot;
                envConfig   = _trainingEnvSnapshot;
                if (_trainingMlSnapshot != null)
                    mlConfig = _trainingMlSnapshot;
            }

            _showLoadedRunBanner  = false;
            _resumeRun            = false;
            _loadedConfigRunId    = null;
            _loadedInferenceRunId = null;
            envConfig.behaviorType = BehaviorType.Training;
            SyncManagerConfigs();

            Dirty();
            RebuildUI();
            RefreshModeButtons();
        }

        /// <summary>
        /// Pushes the panel's current config object references into the training manager.
        /// </summary>
        void SyncManagerConfigs()
        {
            if (trainingAreaManager == null) return;

            trainingAreaManager.partsConfig = partsConfig;
            trainingAreaManager.envConfig   = envConfig;
            trainingAreaManager.mlConfig    = mlConfig;
        }

        /// <summary>
        /// Creates an independent JSON copy of a serializable config before
        /// inference temporarily replaces the training configuration.
        /// </summary>
        static T CloneConfig<T>(T config)
        {
            return JsonUtility.FromJson<T>(JsonUtility.ToJson(config));
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
            label.style.fontSize = 10;
            label.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 20;
            label.style.marginBottom = 4;
            return label;
        }
    }
}
