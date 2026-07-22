// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/ChopstickCatchPlatformFactory.cs
// Purpose: Finds or creates exactly one generated ChopstickCatchPlatform under
// a training area when the chopstick scenario first needs it.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class ChopstickCatchPlatformFactory
    {
        /// <summary>
        /// Finds an existing generated landing platform under the parent or
        /// creates one when the landing scenario first needs it.
        /// </summary>
        public static ChopstickCatchPlatform Ensure(Transform parent)
        {
            if (!parent) return null;

            var existing = parent.GetComponentInChildren<ChopstickCatchPlatform>(true);
            if (existing) return existing;

            var go = new GameObject("Generated_ChopstickCatchPlatform");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ChopstickCatchPlatform>();
        }
    }
}
