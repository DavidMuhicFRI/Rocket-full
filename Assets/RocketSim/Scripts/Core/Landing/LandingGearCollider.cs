// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LandingGearCollider.cs
// Purpose: Identifies foot and strut colliders inside the generated compound
// landing gear so contact callbacks can distinguish safe support from a strike.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public enum LandingGearColliderKind
    {
        Foot,
        Strut
    }

    /// <summary>Runtime marker for one collider in the four-leg assembly.</summary>
    public sealed class LandingGearCollider : MonoBehaviour
    {
        public LandingGearColliderKind kind;
        public int footIndex = -1;
    }
}
