// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Runtime/ConfigBridge.cs
// Purpose: Points the right panel at TrainingAreaManager's canonical shared
// configuration objects before the panel builds, preventing duplicate config state.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    // Wires the four shared config objects from TrainingAreaManager into
    // RightConfigPanel, then triggers a full UI rebuild so the panel
    // displays the current values.
    //
    // Runs before RightConfigPanel.Start() so the panel builds from the
    // manager-owned config objects instead of temporary local defaults.
    [DefaultExecutionOrder(-5)]
    public class ConfigBridge : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        public RightConfigPanel    panel;
        public TrainingAreaManager manager;

        /// <summary>
        /// Wires shared config objects before the panel builds its tabs.
        /// </summary>
        void Awake()
        {
            if (panel == null || manager == null)
            {
                Debug.LogError("[ConfigBridge] panel or manager not assigned.", this);
                return;
            }

            // Point panel at the manager's canonical config objects
            panel.partsConfig         = manager.partsConfig;
            panel.telemetryConfig     = manager.telemetryConfig;
            panel.envConfig           = manager.envConfig;
            panel.mlConfig            = manager.mlConfig;
            panel.trainingAreaManager = manager;
        }

    }
}
