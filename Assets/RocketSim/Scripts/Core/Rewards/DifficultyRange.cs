// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/DifficultyRange.cs
// Purpose: Represents a value interpolated by continuous curriculum difficulty.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    [Serializable]
    public struct DifficultyRange
    {
        public float initial;
        public float full;

        public DifficultyRange(float initial, float full)
        {
            this.initial = initial;
            this.full = full;
        }

        /// <summary>Linearly samples this range at a clamped 0..1 difficulty.</summary>
        public readonly float At(float difficulty01) => Mathf.Lerp(initial, full, Mathf.Clamp01(difficulty01));
    }
}
