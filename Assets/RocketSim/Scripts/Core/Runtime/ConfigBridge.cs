// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Runtime/ConfigBridge.cs
// Purpose: Gives the right panel SimulationAreaHost's canonical session draft
// before the panel builds, preventing duplicate configuration state.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    [DefaultExecutionOrder(-5)]
    public class ConfigBridge : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        public RightConfigPanel    panel;
        public SimulationAreaHost manager;

        /// <summary>
        /// Wires the one editable session draft before the panel builds its tabs.
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
