using System.Collections.Generic;
using RocketSim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// Attach to the main Camera.
    /// Mouse wheel          → zoom (rocket stays centred)
    /// Ctrl + scroll up     → previous rocket
    /// Ctrl + scroll down   → next rocket
    /// Left mouse drag      → orbit around current rocket
    /// The HUD agent reference is updated automatically on switch.
    [RequireComponent(typeof(Camera))]
    public class RocketCameraController : MonoBehaviour
    {
        // ── Inspector wiring ─────────────────────────────────────────────────
        [Header("References")]
        public TrainingAreaManager trainingAreaManager;
        public RocketHUD           hud;

        [Header("Zoom")]
        [Tooltip("Starting distance from rocket in metres")]
        public float startDistance  = 40f;
        public float minDistance    = 5f;
        public float maxDistance    = 400f;
        [Tooltip("Higher = more distance change per scroll tick")]
        public float zoomSensitivity = 0.12f;
        [Tooltip("Smooth damp speed — higher feels snappier")]
        public float zoomSmoothing  = 12f;

        [Header("Orbit (left-mouse drag)")]
        public float orbitSensitivity = 0.4f;
        [Range(-89f, 0f)]
        public float minElevation   = -10f;
        [Range(0f, 89f)]
        public float maxElevation   = 80f;

        [Header("Follow")]
        [Tooltip("How quickly camera tracks a moving rocket")]
        public float followSmoothing = 8f;
        [Tooltip("Extra smoothing during rocket switch (lerp to new target)")]
        public float switchSmoothing = 5f;

        [Header("Selector Label")]
        [Tooltip("Optional TMP label — e.g. 'ROCKET 03 / 16'  Leave null to skip.")]
        public TextMeshProUGUI selectorLabel;

        [Header("Camera Presets")]
        public float sideViewDistance = 80f;
        public float eagleViewDistance = 120f;
        public float closeViewDistance = 24f;

        // ── Private state ────────────────────────────────────────────────────
        int     _index;            // currently watched agent index
        float   _azimuth   = 225f; // horizontal angle (degrees)
        float   _elevation =  20f; // vertical angle (degrees)
        float   _distance;         // current (smoothed) distance
        float   _targetDist;       // desired distance
        float   _distVelocity;     // used by SmoothDamp

        Vector3 _lookTarget;       // smoothed world position we orbit around
        Vector3 _lookVelocity;     // used by SmoothDamp

        bool    _switching;        // true while blending to a new rocket
        Vector3 _switchOrigin;     // camera look-at point at moment of switch
        string  _viewName = "ORBIT";

        // ── Convenience ──────────────────────────────────────────────────────
        IReadOnlyList<FalconAgent> Agents =>
            trainingAreaManager != null ? trainingAreaManager.Agents : null;

        FalconAgent Current =>
            Agents != null && Agents.Count > 0 && _index < Agents.Count
                ? Agents[_index]
                : null;

        // =====================================================================
        void Start()
        {
            ResolveHud();
            SetHudVisible(Current != null);

            _distance    = startDistance;
            _targetDist  = startDistance;
            _lookTarget  = Current != null ? Current.transform.position : Vector3.zero;

            RefreshHUD();
            RefreshLabel();
        }

        // =====================================================================
        void LateUpdate()
        {
            // Rebuild list each frame in case SpawnAreas was called after Start
            if (Agents == null || Agents.Count == 0) return;
            _index = Mathf.Clamp(_index, 0, Agents.Count - 1);

            HandleInput();
            SmoothFollowTarget();
            PositionCamera();
        }

        // =====================================================================
        //  Input
        // =====================================================================
        void HandleInput()
        {
            HandlePresetKeys();

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll != 0f)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) ||
                            Input.GetKey(KeyCode.RightControl);

                if (ctrl)
                {
                    // ── Switch rocket ────────────────────────────────────────
                    // Scroll UP (positive) = previous rocket (feels natural:
                    // "scrolling up the list"). Scroll DOWN = next.
                    int dir = scroll > 0f ? -1 : 1;
                    SwitchTo((_index + dir + Agents.Count) % Agents.Count);
                }
                else
                {
                    // ── Zoom ─────────────────────────────────────────────────
                    // Proportional zoom: same gesture zooms less when close,
                    // more when far — feels consistent at any distance.
                    _targetDist *= 1f - scroll * zoomSensitivity * 10f;
                    _targetDist  = Mathf.Clamp(_targetDist, minDistance, maxDistance);
                }
            }

            // ── Orbit (left-drag) ────────────────────────────────────────────
            if (Input.GetMouseButton(0))
            {
                _azimuth   += Input.GetAxis("Mouse X") * orbitSensitivity * _distance * 0.05f;
                _elevation -= Input.GetAxis("Mouse Y") * orbitSensitivity * _distance * 0.05f;
                _elevation  = Mathf.Clamp(_elevation, minElevation, maxElevation);
            }
        }

        void HandlePresetKeys()
        {
            if (KeyPressed(KeyCode.Alpha1, KeyCode.Keypad1))
                ApplyViewPreset("CHASE", 225f, 20f, startDistance);

            if (KeyPressed(KeyCode.Alpha2, KeyCode.Keypad2))
                ApplyViewPreset("X SIDE", 90f, 8f, sideViewDistance);

            if (KeyPressed(KeyCode.Alpha3, KeyCode.Keypad3))
                ApplyViewPreset("Z SIDE", 0f, 8f, sideViewDistance);

            if (KeyPressed(KeyCode.Alpha4, KeyCode.Keypad4))
                ApplyViewPreset("EAGLE", 0f, 82f, eagleViewDistance);

            if (KeyPressed(KeyCode.Alpha5, KeyCode.Keypad5))
                ApplyViewPreset("LOW ENGINE", 180f, -6f, closeViewDistance);

            if (KeyPressed(KeyCode.Alpha6, KeyCode.Keypad6))
                ApplyViewPreset("APPROACH", 135f, 32f, sideViewDistance);
        }

        static bool KeyPressed(KeyCode alpha, KeyCode keypad)
        {
            return Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad);
        }

        void ApplyViewPreset(string viewName, float azimuth, float elevation, float distance)
        {
            _viewName = viewName;
            _azimuth = azimuth;
            _elevation = Mathf.Clamp(elevation, minElevation, maxElevation);
            _targetDist = Mathf.Clamp(distance, minDistance, maxDistance);
            RefreshLabel();
        }

        // =====================================================================
        //  Rocket switching
        // =====================================================================
        void SwitchTo(int newIndex)
        {
            if (newIndex == _index) return;

            _switchOrigin = _lookTarget;  // remember where we were looking
            _switching    = true;
            _index        = newIndex;

            RefreshHUD();
            RefreshLabel();
        }

        // =====================================================================
        //  Follow logic
        // =====================================================================
        void SmoothFollowTarget()
        {
            if (Current == null) return;

            Vector3 rocketPos = Current.transform.position;

            if (_switching)
            {
                // Blend from old look target to new rocket quickly
                _lookTarget = Vector3.SmoothDamp(
                    _lookTarget, rocketPos,
                    ref _lookVelocity,
                    1f / switchSmoothing);

                // Stop switching once we're close enough
                if (Vector3.Distance(_lookTarget, rocketPos) < 0.5f)
                    _switching = false;
            }
            else
            {
                // Normal per-frame follow
                _lookTarget = Vector3.SmoothDamp(
                    _lookTarget, rocketPos,
                    ref _lookVelocity,
                    1f / followSmoothing);
            }
        }

        // =====================================================================
        //  Camera positioning
        // =====================================================================
        void PositionCamera()
        {
            // Smooth zoom
            _distance = Mathf.SmoothDamp(
                _distance, _targetDist,
                ref _distVelocity,
                1f / zoomSmoothing);

            // Spherical → Cartesian offset
            float azRad = _azimuth   * Mathf.Deg2Rad;
            float elRad = _elevation * Mathf.Deg2Rad;

            Vector3 offset = new Vector3(
                Mathf.Sin(azRad) * Mathf.Cos(elRad),
                Mathf.Sin(elRad),
                Mathf.Cos(azRad) * Mathf.Cos(elRad)
            ) * _distance;

            transform.position = _lookTarget + offset;
            transform.LookAt(_lookTarget);
        }

        // =====================================================================
        //  HUD + label
        // =====================================================================
        void RefreshHUD()
        {
            ResolveHud();
            if (hud == null) return;

            hud.SetAgent(Current);
        }

        void RefreshLabel()
        {
            if (selectorLabel == null) return;

            int total = Agents?.Count ?? 0;
            // e.g.  "ROCKET 03 / 16  [Ctrl+Scroll to switch]"
            selectorLabel.text = total > 0
                ? $"ROCKET <color=#FFD166>{(_index + 1):D2}</color> / {total:D2}" +
                  $"  <size=60%><color=#888888>{_viewName}  |  1-6 VIEWS  |  CTRL+SCROLL SWITCH</color></size>"
                : "NO AGENTS";
        }
        
        public void OnAgentsReady()
        {
            ResolveHud();
            _index      = 0;
            _lookTarget = Current != null ? Current.transform.position : Vector3.zero;
            _switching  = false;
            SetHudVisible(Current != null);
            RefreshHUD();
            RefreshLabel();
        }

        void ResolveHud()
        {
            if (hud != null) return;

            var huds = Resources.FindObjectsOfTypeAll<RocketHUD>();
            for (int i = 0; i < huds.Length; i++)
            {
                if (!huds[i] || !huds[i].gameObject.scene.IsValid()) continue;
                hud = huds[i];
                break;
            }

            if (hud == null)
                hud = CreateRuntimeHud();
        }

        void SetHudVisible(bool visible)
        {
            if (hud != null && hud.gameObject.activeSelf != visible)
                hud.gameObject.SetActive(visible);
        }

        RocketHUD CreateRuntimeHud()
        {
            var canvasGo = new GameObject("RocketHUD_RuntimeCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("HUD_Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -18f);
            panelRect.sizeDelta = new Vector2(430f, 250f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var runtimeHud = canvasGo.AddComponent<RocketHUD>();
            runtimeHud.altText = CreateHudText(panelRect, "ALT", 16f, -18f);
            runtimeHud.vVelText = CreateHudText(panelRect, "V-VEL", 16f, -48f);
            runtimeHud.hVelText = CreateHudText(panelRect, "H-VEL", 16f, -78f);
            runtimeHud.thrustText = CreateHudText(panelRect, "THRUST", 16f, -108f);
            runtimeHud.currentFuelText = CreateHudText(panelRect, "FUEL", 16f, -138f);
            runtimeHud.gimbalText = CreateHudText(panelRect, "GIMBAL", 16f, -168f);
            selectorLabel ??= CreateHudText(panelRect, "ROCKET_LABEL", 16f, -202f);

            var gimbalFrame = new GameObject("Gimbal_Frame", typeof(RectTransform), typeof(Image));
            gimbalFrame.transform.SetParent(panelRect, false);
            var frameRect = gimbalFrame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(1f, 1f);
            frameRect.anchorMax = new Vector2(1f, 1f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = new Vector2(-72f, -82f);
            frameRect.sizeDelta = new Vector2(110f, 110f);
            gimbalFrame.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var dot = new GameObject("Gimbal_Dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(frameRect, false);
            var dotRect = dot.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(12f, 12f);
            dot.GetComponent<Image>().color = new Color(0.2f, 0.85f, 1f, 1f);
            runtimeHud.gimbalDot = dotRect;
            runtimeHud.uiMovementScale = 6f;

            canvasGo.SetActive(false);
            return runtimeHud;
        }

        static TextMeshProUGUI CreateHudText(RectTransform parent, string name, float x, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(300f, 28f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = name;
            text.fontSize = 20f;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        // =====================================================================
        //  Public helpers (optional — call from UI buttons if you want them)
        // =====================================================================
        public void NextRocket()     => SwitchTo((_index + 1) % Mathf.Max(Agents?.Count ?? 1, 1));
        public void PreviousRocket() => SwitchTo((_index - 1 + Mathf.Max(Agents?.Count ?? 1, 1))
                                                  % Mathf.Max(Agents?.Count ?? 1, 1));
    }
}
