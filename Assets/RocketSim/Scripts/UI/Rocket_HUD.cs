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

        [Header("Gimbal Visuals")]
        public RectTransform gimbalDot;
        public float uiMovementScale = 5f;

        public void SetAgent(FalconAgent nextAgent)
        {
            agent = nextAgent;
            gameObject.SetActive(agent != null);
        }

        void Awake()
        {
            if (!agent) gameObject.SetActive(false);
        }

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
        }
    }
}
