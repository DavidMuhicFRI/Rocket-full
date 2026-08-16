// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Runtime/ConfigBridge.cs
// Purpose: Gives the right panel TrainingAreaManager's canonical session draft
// before the panel builds, preventing duplicate configuration state.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    // Gives the panel the manager-owned session draft. Both sides then read the
    // same sections instead of synchronizing four replaceable object references.
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

            panel.BindSession(manager.SessionDraft, manager);
        }

    }
}
