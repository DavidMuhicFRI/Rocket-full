// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/LandingPadSurface.cs
// Purpose: Identifies the reusable physical pad and exposes its measured top
// surface/bounds to spawning, observations, and landing-contact evaluation.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Marker and geometry adapter for an existing landing-pad collider. The
    /// leg scenario derives its target plane from the collider instead of a
    /// hard-coded world altitude.
    /// </summary>
    public sealed class LandingPadSurface : MonoBehaviour
    {
        Collider _surfaceCollider;

        public Collider SurfaceCollider => _surfaceCollider
            ? _surfaceCollider
            : (_surfaceCollider = GetComponent<Collider>());

        public static LandingPadSurface Ensure(Transform pad)
        {
            if (!pad) return null;
            return pad.GetComponent<LandingPadSurface>() ??
                   pad.gameObject.AddComponent<LandingPadSurface>();
        }

        /// <summary>
        /// Returns the collider's top-centre point in the training-area local
        /// frame used by scenario spawning and observations.
        /// </summary>
        public static bool TryGetLocalTopCenter(Transform pad, out Vector3 localTopCenter)
        {
            localTopCenter = Vector3.zero;
            if (!pad) return false;

            Collider collider = pad.GetComponent<Collider>();
            if (!collider) return false;

            Bounds bounds = collider.bounds;
            Vector3 worldTop = new(bounds.center.x, bounds.max.y, bounds.center.z);
            localTopCenter = pad.parent ? pad.parent.InverseTransformPoint(worldTop) : worldTop;
            return true;
        }

        /// <summary>
        /// Checks that a foot centre remains inside the pad footprint. Margin
        /// reserves room for half the physical footpad instead of accepting a
        /// collider that only clips the pad edge.
        /// </summary>
        public bool ContainsFootCenter(Vector3 worldPoint, float marginM)
        {
            Collider collider = SurfaceCollider;
            if (!collider) return false;

            Bounds bounds = collider.bounds;
            float margin = Mathf.Max(0f, marginM);
            return worldPoint.x >= bounds.min.x + margin &&
                   worldPoint.x <= bounds.max.x - margin &&
                   worldPoint.z >= bounds.min.z + margin &&
                   worldPoint.z <= bounds.max.z - margin;
        }

        public float WorldTopY => SurfaceCollider ? SurfaceCollider.bounds.max.y : transform.position.y;
    }
}
