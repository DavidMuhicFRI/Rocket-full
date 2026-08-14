// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RocketCameraController.cs
// Purpose: Follows a selected rocket, switches between overview/hardware views,
// handles orbit and zoom input, and keeps the HUD tied to the watched agent.
// Main flow: choose rocket/view -> build an ideal camera pose -> smooth toward it
// in LateUpdate so physics has already moved the rocket for the current frame.
// -----------------------------------------------------------------------------

using RocketSim;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UI
{
    /// <summary>
    /// Camera positions available while watching a rocket.  The numeric order is
    /// also the order used by the number keys and by <see cref="CycleView"/>.
    /// </summary>
    public enum RocketCameraView
    {
        Orbit = 0,
        Side = 1,
        Top = 2,
        Thrusters = 3,
        Rcs = 4,
        Fins = 5,
        Nose = 6,
        Chase = 7,
        Wide = 8
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RocketCameraController : MonoBehaviour
    {
        [Header("Scene references")]
        public TrainingAreaManager trainingAreaManager;
        public RocketHUD hud;
        public TextMeshProUGUI selectorLabel;

        [Header("Orbit and zoom")]
        [Min(0.1f)] public float startDistance = 70f;
        [Min(0.1f)] public float minDistance = 5f;
        [Min(0.1f)] public float maxDistance = 400f;
        [Min(0f)] public float zoomSensitivity = 0.12f;
        [Min(0f)] public float zoomSmoothing = 12f;
        [Min(0f)] public float orbitSensitivity = 0.4f;
        [Range(-89f, 89f)] public float minElevation = -10f;
        [Range(-89f, 89f)] public float maxElevation = 80f;

        [Header("Follow")]
        [Min(0f)] public float followSmoothing = 8f;
        [Min(0f)] public float switchSmoothing = 5f;

        [Header("View distances")]
        [Min(0.1f)] public float sideViewDistance = 80f;
        [Min(0.1f)] public float eagleViewDistance = 120f;
        [Min(0.1f)] public float closeViewDistance = 24f;

        [Header("Initial view")]
        [SerializeField] RocketCameraView initialView = RocketCameraView.Orbit;
        [SerializeField] float initialOrbitYaw = 25f;
        [SerializeField] float initialOrbitElevation = 15f;

        FalconAgent _selectedAgent;
        RocketAssembly _selectedAssembly;
        RocketCameraView _currentView;
        Vector3 _smoothedFocus;
        float _orbitYaw;
        float _orbitElevation;
        float _distance;
        float _targetDistance;
        bool _hasCameraPose;
        bool _isOrbitDragging;

        public FalconAgent SelectedAgent => _selectedAgent;
        public RocketAssembly SelectedAssembly => _selectedAssembly;
        public RocketCameraView CurrentView => _currentView;

        /// <summary>Initializes view, orbit angles, and zoom from Inspector settings.</summary>
        void Awake()
        {
            _currentView = initialView;
            _orbitYaw = initialOrbitYaw;
            _orbitElevation = Mathf.Clamp(initialOrbitElevation, minElevation, maxElevation);
            _distance = _targetDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
        }

        /// <summary>Finds the area manager, creates/fetches the HUD, and selects a rocket.</summary>
        void Start()
        {
            if (!trainingAreaManager)
                trainingAreaManager = FindAnyObjectByType<TrainingAreaManager>();

            EnsureHud();
            OnAgentsReady();
        }

        /// <summary>Ends an orbit drag if the component or scene becomes inactive.</summary>
        void OnDisable()
        {
            _isOrbitDragging = false;
        }

        /// <summary>
        /// Repairs Inspector values so distance and elevation ranges remain valid
        /// before Play Mode begins.
        /// </summary>
        void OnValidate()
        {
            minDistance = Mathf.Max(0.1f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            startDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
            maxElevation = Mathf.Max(minElevation, maxElevation);
            sideViewDistance = Mathf.Max(0.1f, sideViewDistance);
            eagleViewDistance = Mathf.Max(0.1f, eagleViewDistance);
            closeViewDistance = Mathf.Max(0.1f, closeViewDistance);
        }

        /// <summary>
        /// Refreshes the watched rocket after the manager has replaced or spawned
        /// training areas. Called by <see cref="TrainingAreaManager"/>.
        /// </summary>
        public void OnAgentsReady()
        {
            if (!trainingAreaManager)
                trainingAreaManager = FindAnyObjectByType<TrainingAreaManager>();

            FalconAgent nextAgent = null;
            if (trainingAreaManager != null)
            {
                var agents = trainingAreaManager.Agents;
                for (int i = 0; i < agents.Count; i++)
                {
                    if (!agents[i]) continue;
                    if (agents[i] == _selectedAgent)
                    {
                        nextAgent = _selectedAgent;
                        break;
                    }

                    if (!nextAgent) nextAgent = agents[i];
                }
            }

            if (nextAgent)
                SelectAgent(nextAgent, snap: true);
            else
                SelectAssembly(FindFallbackAssembly(), snap: true);
        }

        /// <summary>Selects an agent and points both camera and HUD at it.</summary>
        public void SelectAgent(FalconAgent agent, bool snap = false)
        {
            _selectedAgent = agent;
            _selectedAssembly = agent ? agent.assembly : null;
            if (!_selectedAssembly && agent)
                _selectedAssembly = agent.GetComponentInChildren<RocketAssembly>(true);

            EnsureHud();
            if (hud) hud.SetAgent(agent);
            if (snap) SnapToCurrentView();
            RefreshLabel();
        }

        /// <summary>Selects the next spawned rocket, wrapping at the end.</summary>
        public void NextAgent() => SelectRelativeAgent(1);

        /// <summary>Selects the previous spawned rocket, wrapping at the start.</summary>
        public void PreviousAgent() => SelectRelativeAgent(-1);

        /// <summary>Changes to a specific view. This method is suitable for UI buttons.</summary>
        public void SetView(RocketCameraView view)
        {
            if (!System.Enum.IsDefined(typeof(RocketCameraView), view)) return;
            _currentView = view;
            _isOrbitDragging = false;
            RefreshLabel();
        }

        /// <summary>Changes view using an enum integer, for Unity inspector events.</summary>
        public void SetView(int view) => SetView((RocketCameraView)view);

        /// <summary>Moves to the next camera view and wraps back to orbit.</summary>
        public void CycleView()
        {
            int count = System.Enum.GetValues(typeof(RocketCameraView)).Length;
            SetView((RocketCameraView)(((int)_currentView + 1) % count));
        }

        /// <summary>Immediately places the camera at the current view's desired pose.</summary>
        public void SnapToCurrentView()
        {
            if (!_selectedAssembly) return;

            _distance = _targetDistance;
            CameraPose pose = BuildCameraPose();
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            _smoothedFocus = pose.focus;
            _hasCameraPose = true;
        }

        /// <summary>
        /// Reads input, resolves a valid rocket, and smoothly approaches the ideal
        /// pose after the current physics/render movement has completed.
        /// </summary>
        void LateUpdate()
        {
            ReadInput();

            if (!_selectedAssembly)
            {
                if (_selectedAgent) _selectedAssembly = _selectedAgent.assembly;
                if (!_selectedAssembly) SelectAssembly(FindFallbackAssembly(), snap: false);
                if (!_selectedAssembly) return;
            }

            float dt = Time.unscaledDeltaTime;
            float zoomT = DampFactor(zoomSmoothing, dt);
            _distance = Mathf.Lerp(_distance, _targetDistance, zoomT);

            CameraPose pose = BuildCameraPose();
            if (!_hasCameraPose)
            {
                transform.SetPositionAndRotation(pose.position, pose.rotation);
                _smoothedFocus = pose.focus;
                _hasCameraPose = true;
                return;
            }

            float smoothing = _currentView == RocketCameraView.Orbit ? followSmoothing : switchSmoothing;
            float t = DampFactor(smoothing, dt);
            _smoothedFocus = Vector3.Lerp(_smoothedFocus, pose.focus, t);

            // Rebuild rotation toward the smoothed point so a fast rocket never
            // drifts away from the centre of the shot during interpolation.
            Vector3 position = Vector3.Lerp(transform.position, pose.position, t);
            Quaternion lookRotation = SafeLookRotation(_smoothedFocus - position, pose.up, pose.rotation);
            Quaternion rotation = Quaternion.Slerp(transform.rotation, lookRotation, t);
            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Handles view keys, rocket selection, reset, right-drag orbit, and wheel
        /// zoom. Mouse camera input is ignored while the pointer is over UI.
        /// </summary>
        void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            // A text editor owns digit, Tab, bracket, V, and F keystrokes while it
            // is focused. Do not let those same keys change the watched camera.
            if (keyboard != null && !UIHelper.IsTextInputFocused)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) SetView(RocketCameraView.Orbit);
                if (keyboard.digit2Key.wasPressedThisFrame) SetView(RocketCameraView.Side);
                if (keyboard.digit3Key.wasPressedThisFrame) SetView(RocketCameraView.Top);
                if (keyboard.digit4Key.wasPressedThisFrame) SetView(RocketCameraView.Thrusters);
                if (keyboard.digit5Key.wasPressedThisFrame) SetView(RocketCameraView.Rcs);
                if (keyboard.digit6Key.wasPressedThisFrame) SetView(RocketCameraView.Fins);
                if (keyboard.digit7Key.wasPressedThisFrame) SetView(RocketCameraView.Nose);
                if (keyboard.digit8Key.wasPressedThisFrame) SetView(RocketCameraView.Chase);
                if (keyboard.digit9Key.wasPressedThisFrame) SetView(RocketCameraView.Wide);
                if (keyboard.vKey.wasPressedThisFrame) CycleView();
                if (keyboard.rightBracketKey.wasPressedThisFrame ||
                    (keyboard.tabKey.wasPressedThisFrame && !keyboard.shiftKey.isPressed))
                    NextAgent();
                if (keyboard.leftBracketKey.wasPressedThisFrame ||
                    (keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed))
                    PreviousAgent();
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    _targetDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
                    SnapToCurrentView();
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (mouse.rightButton.wasPressedThisFrame && !pointerOverUi)
                _isOrbitDragging = true;
            if (mouse.rightButton.wasReleasedThisFrame)
                _isOrbitDragging = false;

            if (_isOrbitDragging && mouse.rightButton.isPressed)
            {
                if (_currentView != RocketCameraView.Orbit)
                {
                    SetView(RocketCameraView.Orbit);
                    _isOrbitDragging = true;
                }
                Vector2 delta = mouse.delta.ReadValue();
                _orbitYaw += delta.x * orbitSensitivity;
                _orbitElevation = Mathf.Clamp(
                    _orbitElevation - delta.y * orbitSensitivity,
                    minElevation,
                    maxElevation);
            }

            if (!pointerOverUi)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // Input System wheel steps are normally +/-120. Exponential
                    // scaling feels consistent at both close and wide distances.
                    _targetDistance *= Mathf.Exp(-scroll * zoomSensitivity * 0.01f);
                    _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
                }
            }
        }

        /// <summary>
        /// Calculates the unsmoothed position, focus, and up direction for the
        /// active view. Close hardware views focus on their component; overview
        /// views use the body center. Zoom scales every view consistently.
        /// </summary>
        CameraPose BuildCameraPose()
        {
            Transform frame = _selectedAssembly.transform;
            float height = BodyHeight;
            float radius = BodyRadius;
            float viewScale = _distance / Mathf.Max(0.1f, startDistance);
            Vector3 bodyCentre = frame.TransformPoint(Vector3.up * (height * 0.5f));

            switch (_currentView)
            {
                case RocketCameraView.Side:
                    return LookAtPose(
                        bodyCentre + frame.right * sideViewDistance * viewScale,
                        bodyCentre,
                        frame.up);

                case RocketCameraView.Top:
                    return LookAtPose(
                        bodyCentre + frame.up * eagleViewDistance * viewScale,
                        bodyCentre,
                        frame.forward);

                case RocketCameraView.Thrusters:
                {
                    Vector3 focus = ComponentPosition(_selectedAssembly.thrusters, frame.position);
                    Vector3 position = focus - frame.up * Mathf.Max(radius * 3f, closeViewDistance * 0.45f * viewScale)
                                             - frame.forward * Mathf.Max(radius * 2f, closeViewDistance * 0.25f * viewScale);
                    return LookAtPose(position, focus + frame.up * radius, frame.up);
                }

                case RocketCameraView.Rcs:
                {
                    Vector3 focus = ComponentPosition(
                        _selectedAssembly.rcs,
                        frame.TransformPoint(Vector3.up * (height - 0.2f)));
                    Vector3 position = focus + frame.right * closeViewDistance * 0.75f * viewScale
                                             - frame.forward * closeViewDistance * 0.35f * viewScale
                                             + frame.up * radius;
                    return LookAtPose(position, focus, frame.up);
                }

                case RocketCameraView.Fins:
                {
                    Vector3 focus = ComponentPosition(
                        _selectedAssembly.fins,
                        frame.TransformPoint(Vector3.up * (height - 1.2f)));
                    Vector3 position = focus + frame.right * closeViewDistance * 0.75f * viewScale
                                             - frame.forward * closeViewDistance * 0.35f * viewScale
                                             + frame.up * radius;
                    return LookAtPose(position, focus, frame.up);
                }

                case RocketCameraView.Nose:
                {
                    Vector3 focus = frame.TransformPoint(Vector3.up * height);
                    Vector3 position = focus + frame.forward * closeViewDistance * 0.7f * viewScale
                                             + frame.up * closeViewDistance * 0.35f * viewScale;
                    return LookAtPose(position, focus - frame.up * radius, frame.up);
                }

                case RocketCameraView.Chase:
                {
                    Vector3 focus = frame.TransformPoint(Vector3.up * (height * 0.55f));
                    Vector3 position = frame.position - frame.forward * closeViewDistance * viewScale
                                                     + frame.up * Mathf.Max(height * 0.22f, radius * 3f);
                    return LookAtPose(position, focus, frame.up);
                }

                case RocketCameraView.Wide:
                {
                    float distance = Mathf.Max(sideViewDistance * 1.75f, height * 3f) * viewScale;
                    Vector3 direction = (frame.right - frame.forward * 0.65f + frame.up * 0.25f).normalized;
                    return LookAtPose(bodyCentre + direction * distance, bodyCentre, Vector3.up);
                }

                case RocketCameraView.Orbit:
                default:
                {
                    Quaternion orbit = Quaternion.Euler(_orbitElevation, _orbitYaw, 0f);
                    Vector3 position = bodyCentre + orbit * Vector3.back * _distance;
                    return LookAtPose(position, bodyCentre, Vector3.up);
                }
            }
        }

        /// <summary>
        /// Selects the next existing agent in either direction and wraps around
        /// the list, skipping destroyed entries.
        /// </summary>
        void SelectRelativeAgent(int direction)
        {
            if (!trainingAreaManager || trainingAreaManager.Agents.Count == 0) return;

            var agents = trainingAreaManager.Agents;
            int current = direction > 0 ? -1 : 0;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] == _selectedAgent) current = i;

            for (int step = 1; step <= agents.Count; step++)
            {
                int index = Mod(current + direction * step, agents.Count);
                if (!agents[index]) continue;
                SelectAgent(agents[index]);
                return;
            }
        }

        /// <summary>
        /// Selects an assembly directly, which also supports the preview rocket
        /// because it has no active FalconAgent.
        /// </summary>
        void SelectAssembly(RocketAssembly assembly, bool snap)
        {
            _selectedAgent = assembly ? assembly.GetComponentInParent<FalconAgent>() : null;
            _selectedAssembly = assembly;
            EnsureHud();
            if (hud) hud.SetAgent(_selectedAgent);
            if (snap) SnapToCurrentView();
            RefreshLabel();
        }

        /// <summary>
        /// Returns the first manager-owned assembly, or any scene assembly as a
        /// final fallback while only the preview exists.
        /// </summary>
        RocketAssembly FindFallbackAssembly()
        {
            if (trainingAreaManager != null)
            {
                var assemblies = trainingAreaManager.Assemblies;
                for (int i = 0; i < assemblies.Count; i++)
                    if (assemblies[i]) return assemblies[i];
            }

            return FindAnyObjectByType<RocketAssembly>();
        }

        /// <summary>
        /// Reuses an assigned HUD or creates the lightweight runtime HUD. It also
        /// locates the selector label when only the HUD reference was assigned.
        /// </summary>
        void EnsureHud()
        {
            if (hud)
            {
                if (!selectorLabel)
                {
                    var labels = hud.GetComponentsInChildren<TextMeshProUGUI>(true);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        if (labels[i].name != "ROCKET_LABEL") continue;
                        selectorLabel = labels[i];
                        break;
                    }
                }
                return;
            }
            hud = RocketHudFactory.CreateRuntimeHud(out TextMeshProUGUI generatedLabel);
            if (!selectorLabel) selectorLabel = generatedLabel;
        }

        /// <summary>Updates the watched rocket number, view name, and control hint.</summary>
        void RefreshLabel()
        {
            if (!selectorLabel) return;

            int index = -1;
            int count = 0;
            if (trainingAreaManager != null)
            {
                var agents = trainingAreaManager.Agents;
                count = agents.Count;
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] == _selectedAgent) index = i;
            }

            string rocket = index >= 0 ? $"ROCKET {index + 1}/{count}" : "ROCKET PREVIEW";
            selectorLabel.text = $"{rocket}  |  {ViewDisplayName(_currentView)}  |  1-9 views, V cycle, [ ] rocket";
        }

        float BodyHeight => _selectedAssembly && _selectedAssembly.body
            ? Mathf.Max(0.1f, _selectedAssembly.body.height)
            : RocketPartsConfig.ReferenceBodyHeightM;

        float BodyRadius => _selectedAssembly && _selectedAssembly.body
            ? Mathf.Max(0.1f, _selectedAssembly.body.radius)
            : RocketPartsConfig.ReferenceBodyRadiusM;

        /// <summary>Returns a component position, or a safe fallback when hardware is absent.</summary>
        static Vector3 ComponentPosition(Component component, Vector3 fallback) =>
            component ? component.transform.position : fallback;

        /// <summary>Creates a complete camera pose looking from position toward focus.</summary>
        static CameraPose LookAtPose(Vector3 position, Vector3 focus, Vector3 up)
        {
            Quaternion rotation = SafeLookRotation(focus - position, up, Quaternion.identity);
            return new CameraPose(position, focus, up, rotation);
        }

        /// <summary>
        /// Builds a look rotation while handling zero-length directions and the
        /// case where forward and up are almost parallel.
        /// </summary>
        static Quaternion SafeLookRotation(Vector3 forward, Vector3 up, Quaternion fallback)
        {
            if (forward.sqrMagnitude < 0.000001f) return fallback;
            forward.Normalize();
            if (up.sqrMagnitude < 0.000001f) up = Vector3.up;
            up.Normalize();
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.999f)
                up = Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) < 0.999f
                    ? Vector3.forward
                    : Vector3.right;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>Converts smoothing strength into a frame-rate-independent interpolation factor.</summary>
        static float DampFactor(float smoothing, float deltaTime) =>
            smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * deltaTime);

        /// <summary>Positive modulo used when cycling backward past the first rocket.</summary>
        static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

        /// <summary>Returns the short uppercase view name shown in the HUD.</summary>
        static string ViewDisplayName(RocketCameraView view) => view switch
        {
            RocketCameraView.Orbit => "ORBIT",
            RocketCameraView.Side => "SIDE",
            RocketCameraView.Top => "TOP",
            RocketCameraView.Thrusters => "THRUSTERS",
            RocketCameraView.Rcs => "RCS",
            RocketCameraView.Fins => "FINS",
            RocketCameraView.Nose => "NOSE",
            RocketCameraView.Chase => "CHASE",
            RocketCameraView.Wide => "WIDE",
            _ => view.ToString().ToUpperInvariant()
        };

        /// <summary>
        /// Immutable desired camera state. Keeping focus/up beside position and
        /// rotation lets LateUpdate smooth the shot without recalculating intent.
        /// </summary>
        readonly struct CameraPose
        {
            public readonly Vector3 position;
            public readonly Vector3 focus;
            public readonly Vector3 up;
            public readonly Quaternion rotation;

            /// <summary>Stores one complete desired camera state.</summary>
            public CameraPose(Vector3 position, Vector3 focus, Vector3 up, Quaternion rotation)
            {
                this.position = position;
                this.focus = focus;
                this.up = up;
                this.rotation = rotation;
            }
        }
    }
}
