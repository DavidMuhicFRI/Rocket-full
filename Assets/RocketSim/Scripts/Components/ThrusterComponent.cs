// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/ThrusterComponent.cs
// Purpose: Stores main-engine limits, arranges engine scene objects for each
// layout, maps control channels to engines, and updates gimbal/flame visuals.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public class ThrusterComponent : MonoBehaviour
    {
        [Header("Engine Specs")]
        public EngineLayout layout = EngineLayout.Single;
        public bool independentEngines = true;
        public OctawebBurnGroup octawebBurnGroup = OctawebBurnGroup.CenterOnly;
        [Range(50000f, 1200000f)]   public float maxThrustPerEngine = 845000f;
        [Range(0.15f, 0.80f)]       public float minThrottle        = 0.57f;
        [Range(0.2f, 1f)]           public float engineSpacing      = 0.8f;
        [Range(200f,  450f)]        public float specificImpulse    = 282f;

        [Header("Actuator Limits")]
        [Range(0.5f, 20f)] public float throttleSpoolRate = 5f;
        [Range(5f,   90f)] public float gimbalSlewRate    = 30f;
        [Range(1f,   15f)] public float maxGimbalAngle    = 5f;
        [Range(0f,   3f)]  public float engineStartupDelay = RocketPartsConfig.Falcon9EngineStartupDelayS;
        [Range(0f,   1f)]  public float engineShutdownTransient = RocketPartsConfig.Falcon9EngineShutdownTransientS;
        [Range(0f,   5f)]  public float engineMinimumRunTime = RocketPartsConfig.Falcon9EngineMinimumRunTimeS;
        [Range(0f,   5f)]  public float engineRestartCooldown = RocketPartsConfig.Falcon9EngineRestartCooldownS;

        // ── Computed totals ───────────────────────────────────────────────────
        /// <summary>Number of engine objects installed by the selected layout.</summary>
        public int EngineCount => layout switch
        {
            EngineLayout.Single => 1,
            EngineLayout.Triple => 3,
            EngineLayout.Octaweb => 9,
            _ => 1
        };
        /// <summary>
        /// Returns how many installed engines can currently produce thrust for
        /// the selected engine layout and octaweb burn group.
        /// </summary>
        public int ActiveEngineCount => EngineBurnGroups.ActiveEngineCount(layout, octawebBurnGroup);
        /// <summary>
        /// Returns how many independent throttle/gimbal command channels the
        /// agent should expose for the current engine layout.
        /// </summary>
        public int IndependentEngineCount => EngineBurnGroups.ControlChannelCount(layout, independentEngines, octawebBurnGroup);
        /// <summary>Maximum propellant flow of one engine at full throttle.</summary>
        public float BurnRate => maxThrustPerEngine / (specificImpulse * 9.80665f);

        /// <summary>
        /// Applies the current throttle/gimbal state to each active engine
        /// transform and updates the child flame visuals.
        /// </summary>
        public void ApplyActuatorVisuals(
            float[] throttle,
            Vector2[] gimbal,
            float minFlameWidth,
            float maxFlameWidth,
            float minFlameLength,
            float maxFlameLength,
            float time)
        {
            var installedEngineIndex = 0;
            var burnEngineIndex = 0;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (!engine.gameObject.activeSelf) continue;

                bool burns = EngineBurnGroups.IncludesEngine(layout, octawebBurnGroup, installedEngineIndex);
                var index = independentEngines ? burnEngineIndex : 0;
                float currentThrottle = burns && throttle != null && index < throttle.Length ? throttle[index] : 0f;
                Vector2 currentGimbal = burns && gimbal != null && index < gimbal.Length ? gimbal[index] : Vector2.zero;

                engine.localRotation = Quaternion.Euler(currentGimbal.x, 0f, currentGimbal.y);
                UpdateFlameVisual(engine, installedEngineIndex, currentThrottle, minFlameWidth, maxFlameWidth, minFlameLength, maxFlameLength, time);

                if (burns) burnEngineIndex++;
                installedEngineIndex++;
            }
        }
        
        /// <summary>
        /// Positions and enables the child engine objects for the configured
        /// engine layout so physics and visuals use the same hardware geometry.
        /// </summary>
        public void ApplyLayout(float bodyRadius)
        {
            float spacing = bodyRadius * engineSpacing;
            switch (layout)
            {
                case EngineLayout.Single: ApplyLayoutSingle(); break;
                case EngineLayout.Triple: ApplyLayoutTriple(spacing); break;
                case EngineLayout.Octaweb: ApplyLayoutOctaweb(spacing); break;
            }
        }

        /// <summary>
        /// Enables only the center engine and parks it on the rocket axis for
        /// single-engine scenarios.
        /// </summary>
        void ApplyLayoutSingle()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i == 0) {
                    engine.transform.localPosition = Vector3.zero;
                    engine.gameObject.SetActive(true);
                } else {
                    engine.gameObject.SetActive(false);
                }
            }
        }
        
        /// <summary>
        /// Enables the first three engine objects and spaces them evenly around
        /// the body so a triple-engine layout has symmetric thrust points.
        /// </summary>
        void ApplyLayoutTriple(float spacing)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i < 3) {
                    float angle = i * 120f * Mathf.Deg2Rad;
                    engine.transform.localPosition = new Vector3(Mathf.Cos(angle) * spacing, 0, Mathf.Sin(angle) * spacing);
                    engine.gameObject.SetActive(true);
                } else {
                    engine.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Enables the center engine plus eight outer engines in a Falcon-style
        /// octaweb ring and disables any extra child engine objects.
        /// </summary>
        void ApplyLayoutOctaweb(float spacing)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i == 0)
                {
                    engine.transform.localPosition = Vector3.zero;
                    engine.gameObject.SetActive(true);
                }
                else if (i < 9)
                {
                    float angle = (i - 1) * 45f * Mathf.Deg2Rad;
                    engine.transform.localPosition = new Vector3(Mathf.Cos(angle) * spacing, 0, Mathf.Sin(angle) * spacing);
                    engine.gameObject.SetActive(true);
                }
                else
                {
                    engine.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Updates the optional child flame object for a single engine.
        /// </summary>
        static void UpdateFlameVisual(
            Transform engine,
            int installedEngineIndex,
            float currentThrottle,
            float minFlameWidth,
            float maxFlameWidth,
            float minFlameLength,
            float maxFlameLength,
            float time)
        {
            Transform flame = engine.Find("Flame_Effect");
            if (!flame) return;

            flame.gameObject.SetActive(currentThrottle > 0.01f);
            float pulse = 1f + Mathf.Sin(time * 45f + installedEngineIndex * 1.7f) * 0.08f;
            float width = Mathf.Lerp(minFlameWidth, maxFlameWidth, currentThrottle) * pulse;
            float length = Mathf.Lerp(minFlameLength, maxFlameLength, currentThrottle) * pulse;
            flame.localScale = new Vector3(width, length, width);
        }
    }
}
