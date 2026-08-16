// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/LandingGearCollider.cs
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

        FalconAgent _owner;

        /// <summary>Returns the compound rigidbody's agent without repeated hierarchy searches.</summary>
        public FalconAgent Owner => _owner ? _owner : (_owner = GetComponentInParent<FalconAgent>());

        void OnCollisionEnter(Collision collision) => RelayCollision(collision);
        void OnCollisionStay(Collision collision) => RelayCollision(collision);

        /// <summary>
        /// Child colliders do not rely on the Rigidbody root receiving a callback.
        /// The agent still owns all filtering and episode-scoped contact state.
        /// </summary>
        void RelayCollision(Collision collision)
        {
            FalconAgent owner = Owner;
            if (owner)
                owner.RecordLandingGearCollision(collision, this);
        }
    }
}
