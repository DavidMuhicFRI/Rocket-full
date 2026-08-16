// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.cs
// Purpose: Defines the shared fields and tab metadata for the right-side configuration panel partial class.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    [RequireComponent(typeof(UIDocument))]
    public partial class RightConfigPanel : MonoBehaviour
    {
        [Header("Stylesheet — drag RocketSimStyles.uss here")]
        public StyleSheet rocketStyles;

        public TrainingLauncher launcher;

        const float DefaultPanelWidth = 460f;
        const float MinPanelWidth = 400f;
        const float MaxPanelWidth = 660f;
        const float AnimSpd = 9f;

        // Named indexes make cross-tab refreshes readable and safe when tabs move.
        const int VehicleTab = 0;
        const int TaskTab = 1;
        const int EnvironmentTab = 2;
        const int RunTab = 3;
        const int RewardsTab = 4;
        const int MlTab = 5;
        const int FaultsTab = 6;
        const int TelemetryTab = 7;

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
        private SimulationSessionDraft _sessionDraft;
        [HideInInspector] public TrainingAreaManager trainingAreaManager;

        private SimulationSessionConfig _trainingSessionSnapshot;

        // ── Internal state ────────────────────────────────────────────────────
        private UIDocument _doc;
        private VisualElement _panel;
        private VisualElement _settingsScroll;
        private VisualElement _resizeHandle;
        private VisualElement _hardwareTestDock;
        private Button _startBtn;
        private Button _stopBtn;
        private Button _trainingModeBtn;
        private Button _inferenceModeBtn;
        private Label _notificationLabel;
        private Camera _previewCamera;
        private Texture2D _resizeCursor;
        
        private int _activeTab;
        private VisualElement[] _tabContents;
        private Button[] _tabBtns;
        private Button _tab;
        private bool _expanded;

        private bool _resumeRun;
        private string _initializeFromRunId = "";
        private string _loadedConfigRunId;
        private string _loadedInferenceRunId;
        private bool _showLoadedRunBanner;
        private bool _autoScaleTrainingAreas;
        private bool _runActive;
        private string _selectedUserVehiclePresetName;
        private string _vehiclePresetNameDraft = "";
        // Vehicle edits rebuild this tab frequently. Keep the user's explicit
        // foldout choice instead of resetting it as a side effect of that rebuild.
        private bool _vehiclePresetsExpanded;
        private float _panelWidth = DefaultPanelWidth;
        private float _resizeStartPointerX;
        private float _resizeStartWidth;
        private float _animCur, _animTgt;
    }
}

