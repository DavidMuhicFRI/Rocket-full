using UI;
using UnityEngine;

namespace RocketSim
{
    // Wires the four shared config objects from TrainingAreaManager into
    // RightConfigPanel, then triggers a full UI rebuild so the panel
    // displays the current values.
    //
    // Runs in Start() so TrainingAreaManager.Awake() has already run.
    public class ConfigBridge : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        public RightConfigPanel    panel;
        public TrainingAreaManager manager;

        void Start()
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

            // Rebuild UI so sliders show the values already on the configs
            panel.RebuildUI();
        }
    }
}