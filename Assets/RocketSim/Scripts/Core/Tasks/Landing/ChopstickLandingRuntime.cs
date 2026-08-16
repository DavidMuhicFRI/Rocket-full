// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/ChopstickLandingRuntime.cs
// Purpose: Owns chopstick-platform geometry and per-episode capture state.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Keeps platform lifecycle and stable-hold bookkeeping out of FalconAgent.
    /// The agent supplies rocket kinematics through one readiness callback.
    /// </summary>
    internal sealed class ChopstickLandingRuntime
    {
        public ChopstickCatchPlatform Platform { get; private set; }
        public bool InsideCapture { get; private set; }
        public bool Stable { get; private set; }
        public bool BecameStable { get; private set; }
        public float StableTime { get; private set; }

        /// <summary>Creates or updates the kinematic catch platform.</summary>
        public void ConfigurePlatform(
            bool enabled,
            Transform parent,
            Vector3 localCenter,
            float yawDeg,
            float halfSize)
        {
            if (!enabled)
            {
                Platform?.DisablePlatform();
                return;
            }

            Platform ??= ChopstickCatchPlatform.Ensure(parent);
            Platform?.Configure(localCenter, yawDeg, halfSize);
        }

        /// <summary>Clears capture state at the start of an episode.</summary>
        public void Reset()
        {
            InsideCapture = false;
            Stable = false;
            BecameStable = false;
            StableTime = 0f;
        }

        /// <summary>Advances capture membership and the stable-hold timer.</summary>
        public void Step(
            bool enabled,
            Vector3 referenceWorldPosition,
            Func<bool> kinematicsReady,
            float requiredHoldSeconds,
            float deltaTime)
        {
            BecameStable = false;
            if (!enabled || !Platform)
            {
                InsideCapture = false;
                Stable = false;
                StableTime = 0f;
                return;
            }

            bool wasStable = Stable;
            InsideCapture = Platform.ContainsWorldPoint(referenceWorldPosition);
            bool ready = InsideCapture && kinematicsReady != null && kinematicsReady();
            StableTime = ready ? StableTime + Mathf.Max(0f, deltaTime) : 0f;
            Stable = ready && StableTime >= Mathf.Max(0f, requiredHoldSeconds);
            BecameStable = !wasStable && Stable;
        }
    }
}
