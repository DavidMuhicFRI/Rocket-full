using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;

namespace RocketSim
{
    [RequireComponent(typeof(UIDocument))]
    public class RightConfigPanel : MonoBehaviour
    {
        [Header("Stylesheet — drag RocketSimStyles.uss here")]
        public StyleSheet rocketStyles;

        public TrainingLauncher launcher;

        const float PanelW = 320f;
        const float AnimSpd = 9f;
        const int ReferenceEngineCount = 9;
        const int ReferenceFinCount = 4;
        const float EngineDryMassKg = 470f;
        const float GridFinDryMassKg = 200f;
        const float RcsDryMassKg = 250f;

        // Images
        [Header("Engine Config Images — drag sprites here")]
        public Texture2D engineImg1;

        public Texture2D engineImg3;
        public Texture2D engineImg9;

        [Header("Fin Config Images — drag sprites here")]
        public Texture2D finImg1;

        public Texture2D finImg2;
        public Texture2D finImg3;

        // ── Set by ConfigBridge ───────────────────────────────────────────────
        [HideInInspector] public RocketPartsConfig partsConfig;
        [HideInInspector] public TelemetryConfig telemetryConfig;
        [HideInInspector] public SimEnvironmentConfig envConfig;
        [HideInInspector] public MLAgentsConfig mlConfig;
        [HideInInspector] public TrainingAreaManager trainingAreaManager;

        private RocketPartsConfig _partsConfig;
        private SimEnvironmentConfig _envConfig;

        // ── Internal state ────────────────────────────────────────────────────
        UIDocument _doc;
        VisualElement _panel;
        Button _tab;
        Button _startBtn;
        Button _testBtn;
        VisualElement _testOptions;
        Label _testStatusLabel;
        bool _expanded;
        bool _testExpanded;
        bool _resumeRun;
        bool _configsLockedToRun;
        float _animCur, _animTgt;

        int _activeTab;
        VisualElement[] _tabContents;
        Button[] _tabBtns;

        private float _fuelPercentage;

        // Per-tab theme class names (button + content share the same class)
        static readonly string[] TabThemeClasses =
        {
            "rs-theme-parts",
            "rs-theme-telemetry",
            "rs-theme-env",
            "rs-theme-scenario",
            "rs-theme-ml",
        };

        static readonly (ScenarioType type, string name, string desc)[] Scenarios =
        {
            (ScenarioType.Landing,       "Landing",     "Descend 60 m, retro-burn, land upright"),
            (ScenarioType.Hover,         "Hover",       "Hold 30 m altitude, resist wind"),
            (ScenarioType.HoverTracking, "Hover+Track", "Follow moving pad at fixed altitude"),
            (ScenarioType.Takeoff,       "Takeoff",     "Lift off, reach 50 m"),
            (ScenarioType.BellyFlop,     "Belly Flop",  "Reorient from horizontal, land"),
        };

        void Awake()
        {
            _doc = GetComponent<UIDocument>();
            launcher = GetComponent<TrainingLauncher>();

            partsConfig    = new RocketPartsConfig();
            telemetryConfig = new TelemetryConfig();
            envConfig      = new SimEnvironmentConfig();
            mlConfig       = new MLAgentsConfig();
        }

        void Start()
        {
            if (rocketStyles != null) _doc.rootVisualElement.styleSheets.Add(rocketStyles);
            _fuelPercentage = partsConfig.baseFuelMass > 0f
                ? partsConfig.startFuelMass / partsConfig.baseFuelMass * 100f
                : 100f;
            BuildPanel();
            _animCur = _animTgt = PanelW;
            Reposition();
        }

        void Update()
        {
            _animCur = Mathf.Lerp(_animCur, _animTgt, Time.deltaTime * AnimSpd);
            Reposition();
            RefreshHardwareTestStatus();
        }

        void BuildPanel()
        {
            var root = _doc.rootVisualElement;
            root.Clear();

            _panel = new VisualElement();
            _panel.name = "rs-right-panel";
            _panel.AddToClassList("rs-right-panel");

            var tabBar = new VisualElement();
            tabBar.AddToClassList("rs-tab-bar");

            string[] names = { "Parts", "Telemetry", "Env", "Scenario", "ML" };
            _tabBtns    = new Button[names.Length];
            _tabContents = new VisualElement[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                _tabBtns[i] = new Button(() => SelectTab(idx)) { text = names[idx] };
                _tabBtns[i].AddToClassList("rs-tab-btn");
                _tabBtns[i].AddToClassList(TabThemeClasses[i]);   // ← per-tab theme
                tabBar.Add(_tabBtns[i]);
            }

            _panel.Add(tabBar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            for (int i = 0; i < names.Length; i++)
            {
                _tabContents[i] = new VisualElement();
                _tabContents[i].AddToClassList("rs-panel-content");
                _tabContents[i].AddToClassList(TabThemeClasses[i]); // ← per-tab theme
                scroll.Add(_tabContents[i]);
            }

            BuildPartsTab(_tabContents[0]);
            BuildTelemetryTab(_tabContents[1]);
            BuildEnvTab(_tabContents[2]);
            BuildScenarioTab(_tabContents[3]);
            BuildMLTab(_tabContents[4]);

            _panel.Add(scroll);

            _startBtn = UIHelper.ActionButton("▶  Start Training", OnStartTraining);
            _startBtn.style.marginBottom = 8;
            _startBtn.style.marginTop    = 8;
            _startBtn.style.marginLeft   = 8;
            _startBtn.style.marginRight  = 8;
            _panel.Add(_startBtn);

            _tab = new Button(ToggleExpand);
            _tab.name = "rs-right-tab";
            _tab.AddToClassList("rs-right-tab");
            _tab.text = "‹";

            root.Add(_panel);
            root.Add(_tab);
            root.Add(BuildTestControls());
            SelectTab(0);
        }

        public void RebuildUI()
        {
            if (_panel == null) return;
            BuildPartsTab(_tabContents[0]);
            BuildTelemetryTab(_tabContents[1]);
            BuildEnvTab(_tabContents[2]);
            BuildScenarioTab(_tabContents[3]);
            BuildMLTab(_tabContents[4]);
            SelectTab(_activeTab);
        }

        // =====================================================================
        //  PARTS TAB
        // =====================================================================

        void BuildPartsTab(VisualElement c)
        {
            c.Clear();

            c.Add(UIHelper.SectionLabel("Rocket Body"));
            c.Add(UIHelper.Slider("Radius (m)", partsConfig.bodyRadius, 0.5f, 6f, v =>
            {
                partsConfig.bodyRadius = v;
                Dirty();
                RefreshCalculatedLabels(c);
            }));
            c.Add(UIHelper.Slider("Height (m)", partsConfig.bodyHeight, 5f, 80f, v =>
            {
                partsConfig.bodyHeight = v;
                Dirty();
                RefreshCalculatedLabels(c);
            }));
            c.Add(UIHelper.Slider("Fuel remaining %", partsConfig.startFuelMass / partsConfig.baseFuelMass * 100f, 0f,
                100f, v =>
                {
                    _fuelPercentage = v;
                    Dirty();
                    RefreshCalculatedLabels(c);
                }));

            var dryMassRow       = UIHelper.ReadOnly("Dry Mass",       "22000 kg");  dryMassRow.name       = "label-dry-mass";       c.Add(dryMassRow);
            var fuelMassRow      = UIHelper.ReadOnly("Max Fuel",        "400000 kg"); fuelMassRow.name      = "label-fuel-mass";      c.Add(fuelMassRow);
            var fuelRemainingRow = UIHelper.ReadOnly("Fuel Remaining",  "40000 kg");  fuelRemainingRow.name = "label-fuel-remaining"; c.Add(fuelRemainingRow);
            var axialAreaRow     = UIHelper.ReadOnly("Axial Area",      "0 m²");      axialAreaRow.name     = "label-axial-area";     c.Add(axialAreaRow);
            var lateralAreaRow   = UIHelper.ReadOnly("Lat. Area",       "0 m²");      lateralAreaRow.name   = "label-lateral-area";   c.Add(lateralAreaRow);

            c.Add(UIHelper.SectionLabel("Main Engine"));
            c.Add(BuildEngineSelector(c));
            c.Add(UIHelper.Toggle("Independent", partsConfig.independentEngines, v =>
            {
                partsConfig.independentEngines = v;
                Dirty();
            }));
            c.Add(UIHelper.Slider("Thrust / Engine (kN)", partsConfig.maxThrustPerEngine / 1000f, 50f, 1500f, v =>
            {
                partsConfig.maxThrustPerEngine = v * 1000f;
                Dirty();
                RefreshCalculatedLabelsThrusters(c);
            }));
            c.Add(UIHelper.Slider("Min Throttle (%)",    partsConfig.minThrottle * 100f, 25f, 80f, v => partsConfig.minThrottle = v / 100f));
            c.Add(UIHelper.Slider("Engine Spacing",      partsConfig.engineSpacing, 0.2f, 1f, v => { partsConfig.engineSpacing = v; Dirty(); }));
            c.Add(UIHelper.Slider("Isp (s)",             partsConfig.specificImpulse, 200f, 450f, v => partsConfig.specificImpulse = v));
            c.Add(UIHelper.Slider("Throttle Spool (/s)", partsConfig.throttleSpoolRate, 0.5f, 20f, v => partsConfig.throttleSpoolRate = v));
            c.Add(UIHelper.Slider("Gimbal Range (°)",    partsConfig.maxGimbalAngle, 1f, 15f, v => partsConfig.maxGimbalAngle = v));
            c.Add(UIHelper.Slider("Gimbal Slew (°/s)",   partsConfig.gimbalSlewRate, 5f, 90f, v => partsConfig.gimbalSlewRate = v));

            var totalThrustRow = UIHelper.ReadOnly("Total Thrust", "0 kN");   totalThrustRow.name = "label-total-thrust"; c.Add(totalThrustRow);
            var burnRateRow    = UIHelper.ReadOnly("Max Burn Rate", "0 kg/s"); burnRateRow.name    = "label-burn-rate";   c.Add(burnRateRow);

            c.Add(UIHelper.SectionLabel("Grid Fins"));
            c.Add(BuildFinSelector(c));
            c.Add(UIHelper.Toggle("Enabled", partsConfig.finsEnabled, v => { partsConfig.finsEnabled = v; Dirty(); }));
            c.Add(UIHelper.Slider("Fin Width X (m)", partsConfig.finWidthX, 0.2f, 4f, v => { partsConfig.finWidthX = v; Dirty(); RefreshCalculatedLabelsFins(c); }));
            c.Add(UIHelper.Slider("Fin Width Z (m)", partsConfig.finWidthZ, 0.2f, 4f, v => { partsConfig.finWidthZ = v; Dirty(); RefreshCalculatedLabelsFins(c); }));
            c.Add(UIHelper.Slider("Fin Height (m)",  partsConfig.finHeight,  0.1f, 1f, v => { partsConfig.finHeight = v; Dirty(); }));
            c.Add(UIHelper.Slider("Max Angle (°)",   partsConfig.maxFinAngle, 5f, 60f, v => partsConfig.maxFinAngle = v));
            c.Add(UIHelper.Slider("Slew Rate (°/s)", partsConfig.finSlewRate, 10f, 200f, v => partsConfig.finSlewRate = v));
            c.Add(UIHelper.Slider("Lift Scale",      partsConfig.liftScale, 0.5f, 3f, v => partsConfig.liftScale = v));

            var finAreaRow = UIHelper.ReadOnly("Fin area", "0 m²"); finAreaRow.name = "label-fin-area"; c.Add(finAreaRow);

            c.Add(UIHelper.SectionLabel("RCS Thrusters"));
            c.Add(UIHelper.Toggle("Enabled", partsConfig.rcsEnabled, v => { partsConfig.rcsEnabled = v; Dirty(); }));
            c.Add(UIHelper.Slider("Thrust / Jet (N)", partsConfig.rcsThrust, 100f, 15000f, v => partsConfig.rcsThrust = v));
            c.Add(UIHelper.ReadOnly("Layout", "4 pods / 12 jets"));

            RefreshCalculatedLabels(c);
            RefreshCalculatedLabelsThrusters(c);
            RefreshCalculatedLabelsFins(c);
        }

        void RefreshCalculatedLabels(VisualElement root)
        {
            float h = partsConfig.bodyHeight;
            float r = partsConfig.bodyRadius;
            float baseR = 1.83f, baseH = 41.2f;

            float surfRatio = (2f * Mathf.PI * r * (h + r)) / (2f * Mathf.PI * baseR * (baseH + baseR));
            float volRatio  = (Mathf.PI * r * r * h)        / (Mathf.PI * baseR * baseR * baseH);

            partsConfig.startFuelMass = GetMaxFuelCapacity() * (_fuelPercentage / 100f);

            UpdateLabelText(root, "label-dry-mass",       $"{AdjustedDryMass(partsConfig.baseDryMass * surfRatio):F0} kg");
            UpdateLabelText(root, "label-fuel-mass",      $"{partsConfig.baseFuelMass * volRatio:F0} kg");
            UpdateLabelText(root, "label-fuel-remaining", $"{partsConfig.startFuelMass:F0} kg");
            UpdateLabelText(root, "label-axial-area",     $"{Mathf.PI * r * r:F2} m²");
            UpdateLabelText(root, "label-lateral-area",   $"{2f * Mathf.PI * r * h:F2} m²");
        }

        void RefreshCalculatedLabelsThrusters(VisualElement root)
        {
            float engineCount = partsConfig.engineLayout switch
            {
                EngineLayout.Single   => 1f,
                EngineLayout.Triple   => 3f,
                EngineLayout.Octaweb  => 9f,
                _ => 1f
            };
            float totalThrust = partsConfig.maxThrustPerEngine * engineCount;
            float burnRate    = totalThrust / (partsConfig.specificImpulse * 9.80665f);
            UpdateLabelText(root, "label-total-thrust", $"{totalThrust / 1000f:F1} kN");
            UpdateLabelText(root, "label-burn-rate",    $"{burnRate:F1} kg/s");
        }

        void RefreshCalculatedLabelsFins(VisualElement root)
        {
            float area = partsConfig.finWidthX * partsConfig.finWidthZ;
            UpdateLabelText(root, "label-fin-area", $"{area:F2} m²");
        }

        float AdjustedDryMass(float bodyDryMass)
        {
            float selectedHardwareMass =
                partsConfig.GetEngineCount() * EngineDryMassKg +
                partsConfig.GetFinCount() * GridFinDryMassKg +
                (partsConfig.rcsEnabled ? RcsDryMassKg : 0f);

            float defaultHardwareMass =
                ReferenceEngineCount * EngineDryMassKg +
                ReferenceFinCount * GridFinDryMassKg +
                RcsDryMassKg;

            return Mathf.Max(bodyDryMass * 0.35f, bodyDryMass + selectedHardwareMass - defaultHardwareMass);
        }

        void Dirty() => trainingAreaManager?.ApplyPartsConfigToAll();

        // =====================================================================
        //  TELEMETRY TAB
        // =====================================================================

        void BuildTelemetryTab(VisualElement c)
        {
            c.Clear();
            var tc = telemetryConfig ?? new TelemetryConfig();

            c.Add(UIHelper.SectionLabel("Log Groups"));
            c.Add(UIHelper.ReadOnly("Identity", "Always on  —  WallTime · Area · Episode · Step"));

            c.Add(UIHelper.Toggle("Goal / Position Metrics", tc.logGoalMetrics, v => { tc.logGoalMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("Goal distance 3D · planar distance · vertical error · altitude"));

            c.Add(UIHelper.Toggle("Attitude / Rotation Metrics", tc.logAttitudeMetrics, v => { tc.logAttitudeMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("Tilt · uprightness · signed roll/pitch · angular rate · tilt rate · angle of attack"));

            c.Add(UIHelper.Toggle("Velocity Metrics", tc.logVelocityMetrics, v => { tc.logVelocityMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("3D speed · planar speed · vertical speed · goal closure rate · air-relative speed"));

            c.Add(UIHelper.Toggle("Control / Fuel Metrics", tc.logControlMetrics, v => { tc.logControlMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("Throttle mean/max · gimbal effort · fin effort · fuel fraction · fuel used"));

            c.Add(UIHelper.Toggle("Aero / Load Metrics", tc.logAeroLoadMetrics, v => { tc.logAeroLoadMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("G force · angular acceleration · dynamic pressure · heat flux · structural stress"));

            c.Add(UIHelper.Toggle("Environment Metrics", tc.logEnvironmentMetrics, v => { tc.logEnvironmentMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("Wind speed · planar wind speed · wind alignment · normalized dynamic pressure"));

            c.Add(UIHelper.Toggle("Reward Metrics", tc.logRewardMetrics, v => { tc.logRewardMetrics = v; RefreshColCount(c, tc); }));
            c.Add(BuildGroupDetail("Scalar reward accumulated this step"));

            c.Add(UIHelper.SectionLabel("Estimated CSV Width"));
            var colCountLabel = UIHelper.ReadOnly("Columns", "");
            colCountLabel.name = "label-col-count";
            c.Add(colCountLabel);
            RefreshColCount(c, tc);

            c.Add(UIHelper.SectionLabel("Output"));
            string outputDir   = Path.Combine(Application.persistentDataPath, "Telemetry");
            string displayPath = outputDir + Path.DirectorySeparatorChar +
                                 "telemetry_<run_id>_episodes.csv / telemetry_<run_id>_steps.csv";

            var pathLabel = new Label(displayPath);
            pathLabel.AddToClassList("rs-section-desc");
            pathLabel.style.whiteSpace  = WhiteSpace.Normal;
            pathLabel.style.marginLeft  = 8;
            pathLabel.style.marginBottom = 6;
            c.Add(pathLabel);

            c.Add(UIHelper.ActionButton("Open Folder", () =>
            {
                Directory.CreateDirectory(outputDir);
                Application.OpenURL($"file://{outputDir}");
            }));
        }

        // =====================================================================
        //  ENV TAB
        // =====================================================================

        void BuildEnvTab(VisualElement c)
        {
            c.Clear();

            c.Add(UIHelper.SectionLabel("Wind"));
            c.Add(UIHelper.Toggle("Wind Enabled", envConfig.windEnabled, v =>
            {
                envConfig.SetWindEnabled(v);
                BuildEnvTab(c);
            }));
            c.Add(UIHelper.Slider("Wind Speed (m/s)",  envConfig.windSpeed,         0f, 30f,  v => { envConfig.SetWindSpeed(v); RefreshCalculatedLabelsEnv(c); }));
            c.Add(UIHelper.Slider("Gust Amplitude",    envConfig.windGustAmplitude,  0f, 15f,  v => { envConfig.SetWindGustAmplitude(v); RefreshCalculatedLabelsEnv(c); }));
            c.Add(UIHelper.Slider("Wind Drift Rate",   envConfig.windChangeRate,     0f, 0.5f, v => { envConfig.SetWindChangeRate(v); RefreshCalculatedLabelsEnv(c); }));

            c.Add(UIHelper.SectionLabel("Weather"));
            var wRow = new VisualElement();
            wRow.AddToClassList("rs-btn-group");

            foreach (WeatherType w in Enum.GetValues(typeof(WeatherType)))
            {
                var wt  = w;
                var btn = new Button(() =>
                {
                    envConfig.weather = wt;
                    RefreshWeatherButtons(wRow);
                    RefreshCalculatedLabelsEnv(c);
                }) { text = w.ToString() };
                btn.AddToClassList("rs-weather-btn");
                btn.userData = w;
                wRow.Add(btn);
            }

            c.Add(wRow);
            RefreshWeatherButtons(wRow);

            c.Add(UIHelper.SectionLabel("Derived"));
            var densityRow = UIHelper.ReadOnly("Air Density ×", envConfig.AirDensityMultiplier.ToString("F3"));
            densityRow.name = "label-air-density";
            c.Add(densityRow);
        }

        void RefreshWeatherButtons(VisualElement row)
        {
            foreach (var child in row.Children())
                if (child is Button { userData: WeatherType wt } b)
                    b.EnableInClassList("rs-weather-btn-active", wt == envConfig.weather);
        }

        void RefreshCalculatedLabelsEnv(VisualElement root)
        {
            UpdateLabelText(root, "label-air-density", envConfig.AirDensityMultiplier.ToString("F3"));
        }

        // =====================================================================
        //  SCENARIO TAB
        // =====================================================================

        void BuildScenarioTab(VisualElement c)
        {
            c.Clear();

            c.Add(UIHelper.SectionLabel("Mode"));

            var modeRow = new VisualElement();
            modeRow.name = "scenario-mode-row";
            modeRow.AddToClassList("rs-mode-row");
            c.Add(modeRow);

            modeRow.Add(CreateModeCard("Training",  "Train a new or existing run", BehaviorType.Training,   modeRow, c));
            modeRow.Add(CreateModeCard("Inference", "Run a saved model live",      BehaviorType.Inference,  modeRow, c));

            var modeContent = new VisualElement { name = "scenario-mode-content" };
            c.Add(modeContent);

            BuildModeContent(modeContent);
            RefreshModeCards(modeRow);
            RefreshStartButton();
        }

        VisualElement CreateModeCard(string title, string desc,
            BehaviorType mode, VisualElement modeRow, VisualElement tabRoot)
        {
            var card = new VisualElement();
            card.AddToClassList("rs-mode-card");
            card.userData = mode;

            var titleLbl = new Label(title); titleLbl.AddToClassList("rs-mode-title");
            var descLbl  = new Label(desc);  descLbl.AddToClassList("rs-mode-desc");
            card.Add(titleLbl);
            card.Add(descLbl);

            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (envConfig.behaviorType == mode) return;

                if (mode == BehaviorType.Inference)
                {
                    _partsConfig = JsonUtility.FromJson<RocketPartsConfig>(JsonUtility.ToJson(partsConfig));
                    _envConfig   = JsonUtility.FromJson<SimEnvironmentConfig>(JsonUtility.ToJson(envConfig));

                    envConfig.behaviorType = BehaviorType.Inference;
                    if (trainingAreaManager != null)
                        trainingAreaManager.envConfig = envConfig;

                    var mc = tabRoot.Q<VisualElement>("scenario-mode-content");
                    if (mc != null) BuildModeContent(mc);
                    RefreshModeCards(modeRow);
                    RefreshStartButton();
                }
                else
                {
                    if (_partsConfig != null && _envConfig != null)
                    {
                        partsConfig = _partsConfig;
                        envConfig   = _envConfig;
                        if (trainingAreaManager != null)
                        {
                            trainingAreaManager.partsConfig = partsConfig;
                            trainingAreaManager.envConfig   = envConfig;
                        }
                    }

                    _configsLockedToRun    = false;
                    _resumeRun             = false;
                    envConfig.behaviorType = BehaviorType.Training;
                    if (trainingAreaManager != null)
                        trainingAreaManager.envConfig = envConfig;

                    Dirty();
                    RebuildUI();
                }
            });

            return card;
        }

        void BuildModeContent(VisualElement container)
        {
            container.Clear();
            if (envConfig.behaviorType == BehaviorType.Training)
                BuildTrainingModeContent(container);
            else
                BuildInferenceModeContent(container);
        }

        void RefreshModeCards(VisualElement modeRow)
        {
            foreach (var child in modeRow.Children())
                if (child.userData is BehaviorType bt)
                    child.EnableInClassList("rs-mode-card-active", bt == envConfig.behaviorType);
        }

        void RefreshScenarioCards(VisualElement grid)
        {
            foreach (var child in grid.Children())
                if (child.userData is ScenarioType st)
                    child.EnableInClassList("rs-scenario-card-active", st == envConfig.scenario);
        }

        void RefreshStartButton()
        {
            if (_startBtn == null) return;
            _startBtn.text = envConfig.behaviorType == BehaviorType.Training
                ? "▶  Start Training"
                : "▶  Start Inference";
        }

        // =====================================================================
        //  TRAINING MODE CONTENT
        // =====================================================================

        void BuildTrainingModeContent(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Run Configuration"));

            string[] existingRuns = ScanExistingRuns();

            // ── Run ID row ────────────────────────────────────────────────────
            var runIdRow = new VisualElement();
            runIdRow.AddToClassList("rs-field-row");
            runIdRow.style.marginBottom = 2;

            var runIdFieldLabel = new Label("Run ID");
            runIdFieldLabel.AddToClassList("rs-field-label");
            runIdRow.Add(runIdFieldLabel);

            var runIdField = new TextField { value = envConfig.runId };
            runIdField.AddToClassList("rs-run-id-field");
            runIdField.style.flexGrow = 1;
            runIdField.focusable      = true;
            runIdField.pickingMode    = PickingMode.Position;

            var innerInput = runIdField.Q<VisualElement>("unity-text-input");
            if (innerInput != null)
            {
                innerInput.focusable   = true;
                innerInput.pickingMode = PickingMode.Position;
            }
            runIdRow.Add(runIdField);
            c.Add(runIdRow);

            // ── Autocomplete dropdown ─────────────────────────────────────────
            var dropdown = new VisualElement();
            dropdown.name = "run-id-dropdown";
            dropdown.AddToClassList("rs-run-dropdown");
            dropdown.style.display   = DisplayStyle.None;
            dropdown.style.maxHeight = 130f;
            dropdown.style.overflow  = Overflow.Hidden;
            c.Add(dropdown);

            // ── Validation hint ───────────────────────────────────────────────
            var validationLabel = new Label("");
            validationLabel.AddToClassList("rs-validation-label");
            c.Add(validationLabel);

            // ── Resume toggle ─────────────────────────────────────────────────
            var resumeRow = new VisualElement();
            resumeRow.AddToClassList("rs-field-row");
            var resumeFieldLabel = new Label("Resume Run");
            resumeFieldLabel.AddToClassList("rs-field-label");
            resumeRow.Add(resumeFieldLabel);

            var resumeToggle = new Toggle { value = _resumeRun };
            resumeToggle.AddToClassList("rs-toggle");
            resumeToggle.SetEnabled(Array.Exists(existingRuns, r => r == envConfig.runId));
            resumeRow.Add(resumeToggle);
            c.Add(resumeRow);

            // ── Loaded-config banner ──────────────────────────────────────────
            var banner = new VisualElement();
            banner.name = "run-loaded-banner";
            banner.AddToClassList("rs-info-banner");
            banner.style.display = _configsLockedToRun ? DisplayStyle.Flex : DisplayStyle.None;

            var bannerLabel = new Label($"✓  Configs loaded from \"{envConfig.runId}\"");
            bannerLabel.AddToClassList("rs-banner-label");
            banner.Add(bannerLabel);

            var unlockBtn = new Button(() =>
            {
                _configsLockedToRun = false;
                _resumeRun          = false;
                resumeToggle.SetValueWithoutNotify(false);
                resumeToggle.SetEnabled(Array.Exists(existingRuns, r => r == envConfig.runId));
                banner.style.display = DisplayStyle.None;
            }) { text = "Unlock" };
            unlockBtn.AddToClassList("rs-banner-btn");
            banner.Add(unlockBtn);
            c.Add(banner);

            // ── Local helpers ─────────────────────────────────────────────────

            void SetValidation(string text, Color col)
            {
                validationLabel.text        = text;
                validationLabel.style.color = new StyleColor(col);
            }

            void UpdateValidation(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    SetValidation("⚠  Run ID cannot be empty",          new Color(1f,    0.45f, 0.25f));
                else if (Array.Exists(existingRuns, r => r == id))
                    SetValidation("✓  Existing run — resume available",  new Color(0.35f, 0.85f, 0.45f));
                else
                    SetValidation("○  New run will be created",          new Color(0.55f, 0.75f, 1f));
            }

            void PopulateDropdown(string filter)
            {
                dropdown.Clear();
                var matches = new System.Collections.Generic.List<string>();
                foreach (var r in existingRuns)
                    if (string.IsNullOrEmpty(filter) ||
                        r.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        matches.Add(r);

                dropdown.style.display = matches.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

                foreach (var run in matches)
                {
                    var info = LoadRunInfo(run);
                    var item = new VisualElement();
                    item.AddToClassList("rs-run-item");
                    item.pickingMode = PickingMode.Position;

                    var nameLbl = new Label(run);
                    nameLbl.AddToClassList("rs-run-item-name");
                    nameLbl.pickingMode = PickingMode.Ignore;
                    item.Add(nameLbl);

                    if (info != null)
                    {
                        var metaLbl = new Label(info.scenario.ToString());
                        metaLbl.AddToClassList("rs-run-item-meta");
                        metaLbl.pickingMode = PickingMode.Ignore;
                        item.Add(metaLbl);
                    }

                    var capturedRun = run;
                    item.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        evt.StopPropagation();
                        runIdField.SetValueWithoutNotify(capturedRun);
                        envConfig.runId        = capturedRun;
                        dropdown.style.display = DisplayStyle.None;
                        resumeToggle.SetEnabled(true);
                        UpdateValidation(capturedRun);
                    });

                    dropdown.Add(item);
                }
            }

            // ── Wire run ID field ─────────────────────────────────────────────

            runIdField.RegisterValueChangedCallback(evt =>
            {
                string raw       = evt.newValue ?? "";
                string sanitised = Regex.Replace(raw, @"[^\w\-]", "_");
                if (sanitised != raw) runIdField.SetValueWithoutNotify(sanitised);

                envConfig.runId = sanitised;
                bool exists = Array.Exists(existingRuns, r => r == sanitised);

                resumeToggle.SetEnabled(exists);
                if (!exists && _resumeRun)
                {
                    resumeToggle.SetValueWithoutNotify(false);
                    _resumeRun          = false;
                    _configsLockedToRun = false;
                    banner.style.display = DisplayStyle.None;
                }

                UpdateValidation(sanitised);
                PopulateDropdown(sanitised);
            });

            runIdField.RegisterCallback<FocusInEvent>(_ => PopulateDropdown(runIdField.value));
            runIdField.RegisterCallback<FocusOutEvent>(_ =>
                runIdField.schedule
                    .Execute(() => dropdown.style.display = DisplayStyle.None)
                    .ExecuteLater(150));

            // ── Wire resume toggle ────────────────────────────────────────────

            resumeToggle.RegisterValueChangedCallback(evt =>
            {
                _resumeRun = evt.newValue;
                if (evt.newValue)
                {
                    try
                    {
                        ApplyRunConfigs(envConfig.runId, preserveBehaviorType: true);
                        _configsLockedToRun  = true;
                        banner.style.display = DisplayStyle.Flex;
                        bannerLabel.text     = $"✓  Configs loaded from \"{envConfig.runId}\"";
                        RebuildUI();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Panel] Failed to load run configs: {ex.Message}");
                        resumeToggle.SetValueWithoutNotify(false);
                        _resumeRun = false;
                    }
                }
                else
                {
                    _configsLockedToRun  = false;
                    banner.style.display = DisplayStyle.None;
                }
            });

            UpdateValidation(envConfig.runId);

            // ── Scenario Cards ────────────────────────────────────────────────
            c.Add(UIHelper.SectionLabel("Training Scenario"));
            var grid = new VisualElement();
            grid.AddToClassList("rs-scenario-grid");

            foreach (var (type, scenarioName, desc) in Scenarios)
            {
                var st   = type;
                var card = new VisualElement();
                card.AddToClassList("rs-scenario-card");
                card.userData = type;

                var titleLbl = new Label(scenarioName); titleLbl.AddToClassList("rs-scenario-title");
                var descLbl  = new Label(desc);  descLbl.AddToClassList("rs-scenario-desc");
                card.Add(titleLbl);
                card.Add(descLbl);
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    envConfig.scenario = st;
                    envConfig.moveTargetEnabled = ScenarioProfile.UsesMovingTarget(st);
                    if (st == ScenarioType.HoverTracking)
                        envConfig.ResetHoverTrackCurriculum();
                    RefreshScenarioCards(grid);
                    RefreshMoveTargetToggle(c);
                });
                grid.Add(card);
            }
            c.Add(grid);
            RefreshScenarioCards(grid);

            // ── Parallel Training ─────────────────────────────────────────────
            c.Add(UIHelper.SectionLabel("Parallel Training"));

            c.Add(UIHelper.Toggle("Auto-Detect (Hardware)", true, v => {
                trainingAreaManager.instanceCount = v ? Mathf.Clamp(SystemInfo.processorCount * 2, 1, 64) : 16;
                UpdateScalingUI(c, v);
                RefreshEnvDisplay(c);
            }));
            
            var sliderContainer = new VisualElement();
            sliderContainer.name = "manual-slider-container";
            sliderContainer.Add(UIHelper.IntSlider("Manual Count", trainingAreaManager.instanceCount, 1, 64, v => {
                trainingAreaManager.instanceCount = v;
                RefreshEnvDisplay(c);
            }));
            c.Add(sliderContainer);

            // Set initial auto mode
            trainingAreaManager.instanceCount = Mathf.Clamp(SystemInfo.processorCount * 2, 1, 64);
            UpdateScalingUI(c, true);
            RefreshEnvDisplay(c);

            c.Add(UIHelper.Slider("Area spacing (m)", trainingAreaManager.spacing, 250f, 2000f, v =>
            {
                trainingAreaManager.spacing = v;
                RefreshEnvDisplay(c);
            }));

            var countRow   = UIHelper.ReadOnly("Active Areas",  $"{Mathf.Clamp(SystemInfo.processorCount * 2, 1, 64)}"); countRow.name   = "label-active-areas"; c.Add(countRow);
            var spacingRow = UIHelper.ReadOnly("Area Spacing",  $"{trainingAreaManager.spacing} m");     spacingRow.name = "label-area-spacing"; c.Add(spacingRow);

            UpdateScalingUI(c, true);
            RefreshEnvDisplay(c);

            // ── Curriculum ────────────────────────────────────────────────────
            c.Add(UIHelper.SectionLabel("Curriculum"));
            c.Add(UIHelper.ReadOnly("Hover Track", "Auto success-based"));
            c.Add(UIHelper.ReadOnly("Pad Radius", $"{SimEnvironmentConfig.HoverTrackStartMoveRadius:F0} -> {SimEnvironmentConfig.HoverTrackEndMoveRadius:F0} m"));
            c.Add(UIHelper.ReadOnly("Hover Radius", $"{SimEnvironmentConfig.HoverTrackStartSettleRadius:F0} -> {SimEnvironmentConfig.HoverTrackEndSettleRadius:F0} m"));
            c.Add(UIHelper.ReadOnly("Hold Time", $"{SimEnvironmentConfig.HoverTrackStartHoldTime:F2} -> {SimEnvironmentConfig.HoverTrackEndHoldTime:F1} s"));
            c.Add(UIHelper.ReadOnly("Max Speed", $"{SimEnvironmentConfig.HoverTrackStartMaxSpeed:F1} -> {SimEnvironmentConfig.HoverTrackEndMaxSpeed:F1} m/s"));
            c.Add(UIHelper.ReadOnly("Max Tilt", $"{SimEnvironmentConfig.HoverTrackStartMaxTiltDeg:F0} -> {SimEnvironmentConfig.HoverTrackEndMaxTiltDeg:F0} deg"));
            RefreshMoveTargetToggle(c);
        }

        VisualElement BuildMoveTargetToggle()
        {
            var row = new VisualElement();
            row.AddToClassList("rs-field-row");

            var label = new Label("Move Target Enabled");
            label.AddToClassList("rs-field-label");
            row.Add(label);

            var toggle = new Toggle { value = envConfig.moveTargetEnabled };
            toggle.name = "move-target-toggle";
            toggle.AddToClassList("rs-toggle");
            toggle.RegisterValueChangedCallback(e =>
            {
                envConfig.moveTargetEnabled = ScenarioProfile.UsesMovingTarget(envConfig.scenario) && e.newValue;
                RefreshMoveTargetToggle(row.parent);
            });
            row.Add(toggle);
            return row;
        }

        void RefreshMoveTargetToggle(VisualElement root)
        {
            var toggle = root?.Q<Toggle>("move-target-toggle");
            if (toggle == null) return;

            bool canMove = ScenarioProfile.UsesMovingTarget(envConfig.scenario);
            if (!canMove) envConfig.moveTargetEnabled = false;
            toggle.SetValueWithoutNotify(canMove && envConfig.moveTargetEnabled);
            toggle.SetEnabled(canMove);
        }

        // =====================================================================
        //  INFERENCE MODE CONTENT
        // =====================================================================

        void BuildInferenceModeContent(VisualElement c)
        {
            c.Add(UIHelper.SectionLabel("Saved Runs"));

            var gridContainer = new VisualElement();
            gridContainer.name = "inference-grid-container";
            c.Add(gridContainer);

            PopulateInferenceGrid(gridContainer);

            c.Add(UIHelper.ActionButton("↻  Refresh Run List", () =>
                PopulateInferenceGrid(gridContainer)));
        }

        void PopulateInferenceGrid(VisualElement gridContainer)
        {
            gridContainer.Clear();

            string[] runs = ScanExistingRuns();

            if (runs.Length == 0)
            {
                var empty = new Label(
                    "No saved runs found.\nComplete a training session first.\n\n" +
                    $"Expected path:\n{GetResultsRoot()}");
                empty.AddToClassList("rs-empty-state");
                gridContainer.Add(empty);
                return;
            }

            var grid = new VisualElement();
            grid.name = "inference-run-grid";
            grid.AddToClassList("rs-inference-grid");

            foreach (var run in runs)
            {
                var info        = LoadRunInfo(run);
                var capturedRun = run;
                var card        = new VisualElement();
                card.AddToClassList("rs-inference-card");
                card.userData    = run;
                card.pickingMode = PickingMode.Position;
                
                bool hasModel = Resources.Load<ModelAsset>($"Models/{run}") != null;

                var nameLbl = new Label(run);
                nameLbl.AddToClassList("rs-inference-card-name");
                nameLbl.pickingMode = PickingMode.Ignore;
                card.Add(nameLbl);

                if (info != null)
                {
                    var scenLbl = new Label(info.scenario.ToString());
                    scenLbl.AddToClassList("rs-inference-card-scenario");
                    scenLbl.pickingMode = PickingMode.Ignore;
                    card.Add(scenLbl);
                }
                
                var modelBadge = new Label(hasModel ? "● Model ready" : "○ No model built");
                modelBadge.style.fontSize   = 9;
                modelBadge.style.marginTop  = 2;
                modelBadge.style.color      = new StyleColor(hasModel
                    ? new Color(0.35f, 0.85f, 0.45f)   // green
                    : new Color(0.6f,  0.6f,  0.6f));  // grey
                modelBadge.pickingMode = PickingMode.Ignore;
                card.Add(modelBadge);

                // Only allow selecting runs that have a model
                if (hasModel)
                {
                    card.RegisterCallback<ClickEvent>(_ =>
                    {
                        envConfig.runId = capturedRun;
                        ApplyRunConfigs(capturedRun, preserveBehaviorType: false);
                        envConfig.behaviorType = BehaviorType.Inference;

                        if (trainingAreaManager != null)
                            trainingAreaManager.envConfig = envConfig;

                        foreach (var child in grid.Children())
                            child.EnableInClassList("rs-inference-card-active",
                                (string)child.userData == capturedRun);

                        BuildPartsTab(_tabContents[0]);
                        BuildEnvTab(_tabContents[2]);
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
                    (string)child.userData == envConfig.runId);

            gridContainer.Add(grid);
        }

        // =====================================================================
        //  Run-scanning helpers
        // =====================================================================

        string GetResultsRoot()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
            if (Directory.Exists(projectRoot)) return projectRoot;
            Debug.Log("[Panel] MLAgents results directory not found, falling back to Assets/results");
            string assetsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "results"));
            return assetsRoot;
        }

        string[] ScanExistingRuns()
        {
            try
            {
                string resultsRoot = GetResultsRoot();
                if (!Directory.Exists(resultsRoot))
                {
                    Debug.Log($"[Panel] Results folder not found at: {resultsRoot}");
                    return new string[0];
                }

                var dirs  = Directory.GetDirectories(resultsRoot);
                var valid = new System.Collections.Generic.List<string>();
                foreach (var dir in dirs)
                {
                    bool hasEnv   = File.Exists(Path.Combine(dir, "EnvConfig.json"));
                    bool hasParts = File.Exists(Path.Combine(dir, "PartsConfig.json"));
                    if (hasEnv || hasParts) valid.Add(Path.GetFileName(dir));
                }

                if (valid.Count == 0)
                    Debug.Log($"[Panel] No valid run folders found in: {resultsRoot}");

                return valid.ToArray();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Panel] ScanExistingRuns error: {ex.Message}");
                return new string[0];
            }
        }

        private class RunInfo { public ScenarioType scenario; }

        RunInfo LoadRunInfo(string runId)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "results", runId, "EnvConfig.json"));
                if (!File.Exists(path)) return null;
                var cfg = JsonUtility.FromJson<SimEnvironmentConfig>(File.ReadAllText(path));
                return new RunInfo { scenario = cfg.scenario };
            }
            catch { return null; }
        }

        void ApplyRunConfigs(string runId, bool preserveBehaviorType)
        {
            string root     = Path.Combine(GetResultsRoot(), runId);
            string envPath  = Path.Combine(root, "EnvConfig.json");
            string partsPath = Path.Combine(root, "PartsConfig.json");

            if (!File.Exists(envPath) || !File.Exists(partsPath))
                throw new FileNotFoundException($"Config files missing in: {root}");

            var loadedEnv   = JsonUtility.FromJson<SimEnvironmentConfig>(File.ReadAllText(envPath));
            var loadedParts = JsonUtility.FromJson<RocketPartsConfig>(File.ReadAllText(partsPath));

            if (preserveBehaviorType) loadedEnv.behaviorType = envConfig.behaviorType;
            loadedEnv.runId = runId;

            envConfig   = loadedEnv;
            partsConfig = loadedParts;

            if (trainingAreaManager != null)
            {
                trainingAreaManager.envConfig   = envConfig;
                trainingAreaManager.partsConfig = partsConfig;
            }
            Dirty();
        }

        void OnStartTraining()
        {
            Dirty();
            if (launcher == null) { Debug.LogError("[Panel] TrainingLauncher not found."); return; }
            launcher.resumeIfExists = _resumeRun;
            if (TelemetryLogger.Instance != null)
            {
                if (trainingAreaManager.envConfig.behaviorType == BehaviorType.Training)
                    TelemetryLogger.Instance.Initialize(trainingAreaManager.telemetryConfig, trainingAreaManager.envConfig.runId);
                else
                    TelemetryLogger.Instance.DisableLogging();
            }
            launcher.Launch(trainingAreaManager);
        }

        VisualElement BuildTestControls()
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("rs-test-wrapper");

            _testBtn = UIHelper.ActionButton("Test", ToggleTestOptions);
            _testBtn.AddToClassList("rs-test-main-btn");
            wrapper.Add(_testBtn);

            _testOptions = new VisualElement();
            _testOptions.AddToClassList("rs-test-options");
            _testOptions.style.display = _testExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            _testOptions.Add(UIHelper.ActionButton("Fin X rotation", () => StartHardwareTest(RocketHardwareTestType.FinAxisX)));
            _testOptions.Add(UIHelper.ActionButton("Fin Y spin", () => StartHardwareTest(RocketHardwareTestType.FinAxisY)));
            _testOptions.Add(UIHelper.ActionButton("Fin Z rotation", () => StartHardwareTest(RocketHardwareTestType.FinAxisZ)));
            _testOptions.Add(UIHelper.ActionButton("Aerodynamics", () => StartHardwareTest(RocketHardwareTestType.Aerodynamics)));
            _testOptions.Add(UIHelper.ActionButton("Takeoff", () => StartHardwareTest(RocketHardwareTestType.Takeoff)));
            _testOptions.Add(UIHelper.ActionButton("Landing", () => StartHardwareTest(RocketHardwareTestType.Landing)));
            _testOptions.Add(UIHelper.ActionButton("RCS vacuum", () => StartHardwareTest(RocketHardwareTestType.RcsVacuum)));
            _testOptions.Add(UIHelper.ActionButton("Stage separation RCS", () => StartHardwareTest(RocketHardwareTestType.RcsSeparation)));
            _testOptions.Add(UIHelper.ActionButton("Engine static", () => StartHardwareTest(RocketHardwareTestType.Thrusters)));
            _testOptions.Add(UIHelper.DangerButton("Stop test", () =>
            {
                trainingAreaManager?.StopHardwareTest();
                RefreshHardwareTestStatus();
            }));

            _testStatusLabel = new Label("No hardware test running.");
            _testStatusLabel.AddToClassList("rs-test-status");
            _testOptions.Add(_testStatusLabel);

            wrapper.Add(_testOptions);
            return wrapper;
        }

        void ToggleTestOptions()
        {
            _testExpanded = !_testExpanded;
            if (_testOptions != null)
                _testOptions.style.display = _testExpanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void StartHardwareTest(RocketHardwareTestType testType)
        {
            Dirty();
            if (trainingAreaManager == null)
            {
                Debug.LogError("[Panel] TrainingAreaManager not assigned.");
                return;
            }

            trainingAreaManager.RunHardwareTest(testType);
            RefreshHardwareTestStatus();
        }

        void RefreshHardwareTestStatus()
        {
            if (_testStatusLabel == null) return;
            _testStatusLabel.text = trainingAreaManager != null
                ? trainingAreaManager.HardwareTestStatus
                : "No hardware test manager assigned.";
        }

        // =====================================================================
        //  ML CONFIG TAB
        // =====================================================================

        void BuildMLTab(VisualElement c)
        {
            c.Clear();
        
            c.Add(UIHelper.SectionLabel("Trainer"));
            var trainerEnum = new EnumField("Type", mlConfig.trainerType);
            trainerEnum.AddToClassList("rs-enum-field");
            trainerEnum.RegisterValueChangedCallback(e => mlConfig.trainerType = (TrainerType)e.newValue);
            c.Add(trainerEnum);
            c.Add(MLDesc("PPO = stable & general. SAC = off-policy, sample-efficient."));
        
            c.Add(UIHelper.SectionLabel("Hyperparameters"));
        
            c.Add(UIHelper.IntSlider("batch_size",    mlConfig.batchSize,    32,    8192,   v => mlConfig.batchSize    = v));
            c.Add(MLDesc("Samples used per gradient update. Larger = smoother but slower."));
        
            c.Add(UIHelper.IntSlider("buffer_size",   mlConfig.bufferSize,   512,  131072,  v => mlConfig.bufferSize   = v));
            c.Add(MLDesc("Experiences collected before each policy update. Must be ≥ batch_size."));
        
            c.Add(UIHelper.ScientificSlider("learning_rate",  mlConfig.learningRate,  1e-5f, 1e-3f,  v => mlConfig.learningRate = v));
            c.Add(MLDesc("Optimizer step size. Too high → unstable; too low → slow convergence."));
        
            c.Add(UIHelper.ScientificSlider("beta (entropy)", mlConfig.beta,          1e-4f, 0.05f,  v => mlConfig.beta         = v));
            c.Add(MLDesc("Entropy bonus weight. Higher = more exploration, less exploitation."));
        
            c.Add(UIHelper.Slider("epsilon (clip)",   mlConfig.epsilon,      0.05f, 0.5f,   v => mlConfig.epsilon      = v));
            c.Add(MLDesc("Max allowed policy update ratio per step (PPO clipping parameter)."));
        
            c.Add(UIHelper.Slider("lambd (GAE)",      mlConfig.lambd,        0.80f, 0.999f, v => mlConfig.lambd        = v));
            c.Add(MLDesc("Bias-variance trade-off for advantage estimation. 0.95 is a safe default."));
        
            c.Add(UIHelper.IntSlider("num_epoch",     mlConfig.numEpoch,     1,     20,     v => mlConfig.numEpoch     = v));
            c.Add(MLDesc("Full passes over the buffer per update. Higher → more data reuse."));
        
            var schedEnum = new EnumField("lr_schedule", mlConfig.lrSchedule);
            schedEnum.AddToClassList("rs-enum-field");
            schedEnum.RegisterValueChangedCallback(e => mlConfig.lrSchedule = (LRSchedule)e.newValue);
            c.Add(schedEnum);
            c.Add(MLDesc("Linear = decays to 0 over training. Constant = fixed throughout."));
        
            c.Add(UIHelper.SectionLabel("Network"));
        
            c.Add(UIHelper.Toggle("normalize",       mlConfig.normalize,    v => mlConfig.normalize   = v));
            c.Add(MLDesc("Normalise observations with running mean/variance. Recommended."));
        
            c.Add(UIHelper.IntSlider("hidden_units", mlConfig.hiddenUnits,  64,  1024, v => mlConfig.hiddenUnits = v));
            c.Add(MLDesc("Neurons per hidden layer. Higher = more capacity, more VRAM."));
        
            c.Add(UIHelper.IntSlider("num_layers",   mlConfig.numLayers,    1,   6,    v => mlConfig.numLayers   = v));
            c.Add(MLDesc("Network depth. 2-3 layers is sufficient for most locomotion tasks."));
        
            c.Add(UIHelper.SectionLabel("Reward — Extrinsic"));
        
            c.Add(UIHelper.Slider("gamma",    mlConfig.extrinsicGamma,    0.80f, 0.9999f, v => mlConfig.extrinsicGamma    = v));
            c.Add(MLDesc("Discount factor. Higher = agent cares more about future rewards."));
        
            c.Add(UIHelper.Slider("strength", mlConfig.extrinsicStrength, 0.10f, 5f,      v => mlConfig.extrinsicStrength = v));
            c.Add(MLDesc("Scales the environment reward signal before combining with curiosity."));
        
            c.Add(UIHelper.SectionLabel("Reward — Curiosity"));
        
            c.Add(UIHelper.Toggle("enabled",            mlConfig.curiosityEnabled,  v => mlConfig.curiosityEnabled = v));
            c.Add(MLDesc("Adds an intrinsic reward for visiting novel states."));
        
            c.Add(UIHelper.Slider("gamma",              mlConfig.curiosityGamma,    0.80f, 0.9999f, v => mlConfig.curiosityGamma    = v));
            c.Add(MLDesc("Discount applied to the curiosity reward stream."));
        
            c.Add(UIHelper.ScientificSlider("strength", mlConfig.curiosityStrength, 0.001f, 0.1f,  v => mlConfig.curiosityStrength = v));
            c.Add(MLDesc("Scale of intrinsic reward relative to extrinsic."));
        
            c.Add(UIHelper.ScientificSlider("lr",       mlConfig.curiosityLR,       1e-5f,  1e-3f, v => mlConfig.curiosityLR      = v));
            c.Add(MLDesc("Learning rate for the curiosity forward/inverse models."));
        
            c.Add(UIHelper.SectionLabel("Run Settings"));
        
            c.Add(UIHelper.IntSlider("max_steps (×1k)",    mlConfig.maxSteps    / 1000, 100, 50000, v => mlConfig.maxSteps    = v * 1000));
            c.Add(MLDesc("Total environment steps before training ends."));
        
            c.Add(UIHelper.IntSlider("time_horizon",       mlConfig.timeHorizon,         64,  2048,  v => mlConfig.timeHorizon = v));
            c.Add(MLDesc("Steps collected per agent before bootstrapping returns."));
        
            c.Add(UIHelper.IntSlider("summary_freq (×1k)", mlConfig.summaryFreq / 1000,   1,  100,   v => mlConfig.summaryFreq = v * 1000));
            c.Add(MLDesc("Steps between TensorBoard stat writes."));
        
            c.Add(UIHelper.Toggle("threaded",              mlConfig.threaded,    v => mlConfig.threaded = v));
            c.Add(MLDesc("Run inference in a background thread. Helps CPU-heavy environments."));
        
            c.Add(UIHelper.ActionButton("Copy YAML to Clipboard", () =>
                GUIUtility.systemCopyBuffer = mlConfig.ToYAML()));
        }
        
        static VisualElement MLDesc(string text)
        {
            var l = new Label(text);
            l.style.fontSize    = 9;
            l.style.color       = new StyleColor(new Color(0.42f, 0.50f, 0.62f));
            l.style.marginLeft  = 122;   // align under the value column, not the label
            l.style.marginBottom = 3;
            l.style.whiteSpace  = WhiteSpace.Normal;
            return l;
        }

        // =====================================================================
        //  Tab navigation
        // =====================================================================

        void SelectTab(int idx)
        {
            _activeTab = idx;
            for (int i = 0; i < _tabContents.Length; i++)
            {
                _tabContents[i].style.display = (i == idx) ? DisplayStyle.Flex : DisplayStyle.None;
                _tabBtns[i].EnableInClassList("rs-tab-btn-active", i == idx);
            }
        }

        void ToggleExpand()
        {
            _expanded = !_expanded;
            _animTgt  = _expanded ? 0f : PanelW;
            _tab.text = _expanded ? "‹" : "›";
        }

        void Reposition()
        {
            if (_panel == null || _tab == null) return;
            _panel.style.right = _animCur - PanelW;
            _tab.style.right   = _animCur;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        VisualElement BuildEngineSelector(VisualElement root)
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.AddToClassList("rs-engine-row");
            wrapper.Add(row);

            var configs = new (EngineLayout layout, string label, Texture2D tex)[]
            {
                (EngineLayout.Single,  "Single",  engineImg1),
                (EngineLayout.Triple,  "Triple",  engineImg3),
                (EngineLayout.Octaweb, "Octaweb", engineImg9),
            };

            foreach (var cfg in configs)
            {
                var card = new VisualElement();
                card.AddToClassList("rs-engine-card");
                card.userData = cfg.layout;

                if (cfg.tex != null)
                {
                    var img = new Image { image = cfg.tex };
                    img.AddToClassList("rs-engine-img");
                    card.Add(img);
                }

                var lbl = new Label(cfg.label);
                lbl.AddToClassList("rs-engine-label");
                card.Add(lbl);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    partsConfig.engineLayout = cfg.layout;
                    RebuildUI();
                    foreach (var child in row.Children())
                        child.EnableInClassList("rs-engine-card-active",
                            (EngineLayout)child.userData == cfg.layout);
                    Dirty();
                    RefreshCalculatedLabelsThrusters(root);
                });

                row.Add(card);
            }

            foreach (var child in row.Children())
                child.EnableInClassList("rs-engine-card-active",
                    (EngineLayout)child.userData == partsConfig.engineLayout);

            return wrapper;
        }

        VisualElement BuildFinSelector(VisualElement root)
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.AddToClassList("rs-fin-row");
            wrapper.Add(row);

            var configs = new (FinLayout layout, string label, Texture2D tex)[]
            {
                (FinLayout.ThreeFins_120, "3×",     finImg1),
                (FinLayout.FourFins_Plus, "4× (+)", finImg2),
                (FinLayout.FourFins_X,    "4× (X)", finImg3),
            };

            foreach (var cfg in configs)
            {
                var card = new VisualElement();
                card.AddToClassList("rs-fin-card");
                card.userData = cfg.layout;

                if (cfg.tex != null)
                {
                    var img = new Image { image = cfg.tex };
                    img.AddToClassList("rs-fin-img");
                    card.Add(img);
                }

                var lbl = new Label(cfg.label);
                lbl.AddToClassList("rs-fin-label");
                card.Add(lbl);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    partsConfig.finLayout = cfg.layout;
                    foreach (var child in row.Children())
                        child.EnableInClassList("rs-fin-card-active",
                            (FinLayout)child.userData == cfg.layout);
                    Dirty();
                    RefreshCalculatedLabelsFins(root);
                });

                row.Add(card);
            }

            foreach (var child in row.Children())
                child.EnableInClassList("rs-fin-card-active",
                    (FinLayout)child.userData == partsConfig.finLayout);

            return wrapper;
        }

        float GetMaxFuelCapacity()
        {
            float h = partsConfig.bodyHeight, r = partsConfig.bodyRadius;
            float baseR = 1.83f, baseH = 41.2f;
            float volRatio = (Mathf.PI * r * r * h) / (Mathf.PI * baseR * baseR * baseH);
            return partsConfig.baseFuelMass * volRatio;
        }

        void UpdateLabelText(VisualElement root, string rowName, string newText)
        {
            var row = root.Q<VisualElement>(rowName);
            if (row == null) return;
            var label = row.Query<Label>().Last();
            if (label != null) label.text = newText;
        }

        VisualElement BuildGroupDetail(string text)
        {
            var label = new Label(text);
            label.style.fontSize    = 10;
            label.style.color       = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
            label.style.whiteSpace  = WhiteSpace.Normal;
            label.style.marginLeft  = 20;
            label.style.marginBottom = 4;
            return label;
        }

        void RefreshColCount(VisualElement root, TelemetryConfig tc)
        {
            int cols = 4;
            if (tc.logGoalMetrics)        cols += 18;
            if (tc.logAttitudeMetrics)    cols += 10;
            if (tc.logVelocityMetrics)    cols += 9;
            if (tc.logControlMetrics)     cols += 8;
            if (tc.logAeroLoadMetrics)    cols += 7;
            if (tc.logEnvironmentMetrics) cols += 4;
            if (tc.logRewardMetrics)      cols += 1;
            int metricCols  = cols - 4;
            int episodeCols = 4 + metricCols * 2;
            UpdateLabelText(root, "label-col-count",
                $"Steps: {cols} cols · Episodes: {episodeCols} cols");
        }

        void UpdateScalingUI(VisualElement root, bool auto)
        {
            var container = root.Q<VisualElement>("manual-slider-container");
            if (container != null)
                container.style.display = auto ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RefreshEnvDisplay(VisualElement root)
        {
            if (trainingAreaManager == null) return;
            UpdateLabelText(root, "label-active-areas", $"{trainingAreaManager.instanceCount}");
            UpdateLabelText(root, "label-area-spacing", $"{trainingAreaManager.spacing:F0} m");
        }
    }
}

