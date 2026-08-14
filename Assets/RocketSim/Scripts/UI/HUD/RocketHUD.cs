// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/HUD/RocketHUD.cs
// Purpose: Displays read-only flight values for the currently watched rocket agent.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using RocketSim;
using TMPro;
using UnityEngine;

namespace UI
{
    public class RocketHUD : MonoBehaviour
    {
        [Header("Agent Reference")]
        public FalconAgent agent;

        [Header("Telemetry Texts")]
        public TextMeshProUGUI altText;
        public TextMeshProUGUI vVelText;
        public TextMeshProUGUI hVelText;
        public TextMeshProUGUI thrustText;
        public TextMeshProUGUI currentFuelText;
        public TextMeshProUGUI gimbalText;
        public TextMeshProUGUI curriculumTitleText;
        public TextMeshProUGUI curriculumProgressText;
        public TextMeshProUGUI curriculumRateText;
        public TextMeshProUGUI curriculumPrimaryText;
        public TextMeshProUGUI curriculumSecondaryText;
        public TextMeshProUGUI curriculumTertiaryText;

        [Header("Gimbal Visuals")]
        public RectTransform gimbalDot;
        public float uiMovementScale = 5f;

        /// <summary>
        /// Points the HUD at a watched agent and hides the HUD when no agent is selected.
        /// </summary>
        public void SetAgent(FalconAgent nextAgent)
        {
            agent = nextAgent;
            gameObject.SetActive(agent != null);
        }

        /// <summary>
        /// Ensures older scene-authored HUDs receive the newer curriculum labels
        /// and hides the HUD until a watched agent is assigned.
        /// </summary>
        void Awake()
        {
            EnsureCurriculumHudLabels();
            if (!agent) gameObject.SetActive(false);
        }

        /// <summary>
        /// Refreshes altitude, velocity, throttle, gimbal, fuel, and curriculum
        /// text from the watched agent once per rendered frame.
        /// </summary>
        void Update()
        {
            if (!agent) return;

            // 1. Altitude & Vertical Velocity
            float altitude = agent.transform.localPosition.y;
            float vSpeed = agent.rb.linearVelocity.y;
        
            if (altText) altText.text = $"ALT: <color=#FFFFFF>{altitude:F1}</color> m";
        
            // Color coding for vertical speed: Red if crashing (> 4m/s)
            string vColor = (vSpeed < -4.0f) ? "#FF4B4B" : "#4BFF4B";
            if (vVelText) vVelText.text = $"V-VEL: <color={vColor}>{vSpeed:F1}</color> m/s";

            // 2. Horizontal Velocity
            float hSpeed = new Vector2(agent.rb.linearVelocity.x, agent.rb.linearVelocity.z).magnitude;
            if (hVelText) hVelText.text = $"H-VEL: {hSpeed:F1} m/s";

            // 3. Thrust Percentage
            float thrustPercent = agent.GetCurrentThrottle(0) * 100f;
            float gimbalX = agent.GetGimbalX(0);
            float gimbalZ = agent.GetGimbalZ(0);
            if (thrustText) thrustText.text = $"THRUST: {thrustPercent:F0}%";

            // 4. Gimbal Dot Position
            // Maps the degrees to UI pixels
            if (gimbalDot) gimbalDot.localPosition = new Vector3(gimbalZ * uiMovementScale, gimbalX * uiMovementScale, 0);
            if (gimbalText) gimbalText.text = $"GIMBAL: X {gimbalX:F1} deg  Z {gimbalZ:F1} deg";
            else if (thrustText) thrustText.text += $"  GIMBAL X:{gimbalX:F1} Z:{gimbalZ:F1}";
        
            // 5. Current Fuel
            float currentFuel = agent.GetFuel;
            if (currentFuelText) currentFuelText.text = $"FUEL: {currentFuel:F1} kg";

            UpdateCurriculumHud();
        }

        /// <summary>
        /// Adds the curriculum HUD labels at runtime for older scene HUDs that
        /// predate the generated curriculum fields.
        /// </summary>
        void EnsureCurriculumHudLabels()
        {
            if (curriculumTitleText || !altText) return;

            var parent = altText.transform.parent as RectTransform;
            if (!parent) return;

            Vector2 size = parent.sizeDelta;
            parent.sizeDelta = new Vector2(Mathf.Max(size.x, 520f), Mathf.Max(size.y, 410f));

            curriculumTitleText = CreateCurriculumText(parent, "CURRICULUM_TITLE", 16f, -238f);
            curriculumProgressText = CreateCurriculumText(parent, "CURRICULUM_PROGRESS", 16f, -266f);
            curriculumRateText = CreateCurriculumText(parent, "CURRICULUM_RATE", 16f, -294f);
            curriculumPrimaryText = CreateCurriculumText(parent, "CURRICULUM_PRIMARY", 16f, -322f);
            curriculumSecondaryText = CreateCurriculumText(parent, "CURRICULUM_SECONDARY", 16f, -350f);
            curriculumTertiaryText = CreateCurriculumText(parent, "CURRICULUM_TERTIARY", 16f, -378f);
        }

        /// <summary>
        /// Creates one generated curriculum label inside the existing HUD panel.
        /// </summary>
        static TextMeshProUGUI CreateCurriculumText(RectTransform parent, string name, float x, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(486f, 26f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Updates the scenario-specific curriculum HUD lines.
        /// </summary>
        void UpdateCurriculumHud()
        {
            SimEnvironmentConfig env = agent != null ? agent.envConfig : null;
            if (env == null)
            {
                SetCurriculumHudVisible(false);
                return;
            }

            if (env.scenario == ScenarioType.HoverTracking)
            {
                TerminationParameters termination =
                    env.GetTrainingObjective(ScenarioType.HoverTracking).terminations;
                float difficulty = env.hoverTrackCurriculumProgress;
                float captureRadius = termination.trackingCaptureRadiusM.At(difficulty);
                float holdSeconds = termination.trackingCaptureHoldSeconds.At(difficulty);
                float horizontalSpeed = termination.trackingCaptureMaxHorizontalSpeedMps.At(difficulty);
                float verticalSpeed = termination.trackingCaptureMaxVerticalSpeedMps.At(difficulty);
                float tilt = termination.trackingCaptureMaxTiltDeg.At(difficulty);
                SetCurriculumHudVisible(true);
                SetText(curriculumTitleText, "CURRICULUM: HOVER TRACKING");
                SetText(curriculumProgressText, $"PROGRESS: {FormatPercent(env.hoverTrackCurriculumProgress)}");
                SetText(curriculumRateText,
                    $"SUCCESS RATE: {FormatPercent(env.HoverTrackCurriculumSuccessRate)}  ({env.hoverTrackCurriculumSuccessfulEpisodes}/{env.hoverTrackCurriculumEpisodeCount} episodes)");
                SetText(curriculumPrimaryText,
                    $"TARGET: {env.targetMoveRadius:F0} m move radius, {captureRadius:F1} m capture");
                SetText(curriculumSecondaryText,
                    $"CAPTURE: {holdSeconds:F1} s hold, <= {horizontalSpeed:F1} m/s planar, <= {verticalSpeed:F1} m/s vertical");
                SetText(curriculumTertiaryText,
                    $"ATTITUDE: <= {tilt:F0} deg tilt, {env.hoverTrackCurriculumSuccesses} target captures");
                return;
            }

            if (env.scenario.IsLanding())
            {
                SetCurriculumHudVisible(true);
                string taskName = env.scenario == ScenarioType.LegLanding ? "LEG LANDING" : "CHOPSTICK LANDING";
                SetText(curriculumTitleText, $"CURRICULUM: {taskName}");
                SetText(curriculumProgressText, $"PROGRESS: {FormatPercent(env.ActiveLandingCurriculumProgress)}");
                SetText(curriculumRateText,
                    $"SUCCESS RATE: {FormatPercent(env.ActiveLandingCurriculumSuccessRate)}  ({env.ActiveLandingCurriculumSuccessfulEpisodes}/{env.ActiveLandingCurriculumEpisodeCount} episodes)");
                SetText(curriculumPrimaryText,
                    $"SPAWN: {env.CurrentLandingSpawnAltitudeMin:F0}-{env.CurrentLandingSpawnAltitudeMax:F0} m, <= {env.CurrentLandingSpawnRadius:F0} m offset");
                SetText(curriculumSecondaryText,
                    $"SPEED: {env.CurrentLandingVerticalSpeedMin:F0}-{env.CurrentLandingVerticalSpeedMax:F0} m/s down, <= {env.CurrentLandingHorizontalSpeedMax:F1} m/s lateral");
                SetText(curriculumTertiaryText,
                    $"LIMITS: <= {env.CurrentLandingSuccessRadius:F1} m, <= {env.CurrentLandingSuccessMaxTiltDeg:F0} deg, <= {env.CurrentLandingAngularSpeedMaxDegS:F0} deg/s start spin");
                return;
            }

            SetCurriculumHudVisible(false);
        }

        /// <summary>
        /// Shows or hides all generated curriculum labels.
        /// </summary>
        void SetCurriculumHudVisible(bool visible)
        {
            SetTextVisible(curriculumTitleText, visible);
            SetTextVisible(curriculumProgressText, visible);
            SetTextVisible(curriculumRateText, visible);
            SetTextVisible(curriculumPrimaryText, visible);
            SetTextVisible(curriculumSecondaryText, visible);
            SetTextVisible(curriculumTertiaryText, visible);
        }

        /// <summary>Safely updates an optional generated or scene-authored label.</summary>
        static void SetText(TextMeshProUGUI label, string text)
        {
            if (label) label.text = text;
        }

        /// <summary>Safely shows or hides an optional HUD label.</summary>
        static void SetTextVisible(TextMeshProUGUI label, bool visible)
        {
            if (label) label.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Formats a normalized value as a compact percentage, preserving one
        /// decimal place only for small non-zero progress.
        /// </summary>
        static string FormatPercent(float value)
        {
            float percent = Mathf.Clamp01(value) * 100f;
            if (percent <= 0f) return "0%";
            if (percent < 1f) return $"{percent:F1}%";
            return $"{percent:F0}%";
        }
    }
}
