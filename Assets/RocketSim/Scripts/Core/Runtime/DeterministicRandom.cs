// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Runtime/DeterministicRandom.cs
// Purpose: Provides a small cross-platform deterministic random stream for
// episode initialization, target placement, wind, and fault selection.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Xorshift32 generator with explicitly defined arithmetic. Unlike Unity's
    /// global Random state, each area/episode owns an independent stream, so
    /// asynchronous parallel agents cannot change one another's samples.
    /// </summary>
    internal struct DeterministicRandom
    {
        uint _state;

        public DeterministicRandom(int seed)
        {
            _state = unchecked((uint)seed);
            if (_state == 0u)
                _state = 0x6D2B79F5u;
        }

        public static int EpisodeSeed(int baseSeed, int areaIndex, int episode, int stream = 0)
        {
            unchecked
            {
                uint value = (uint)baseSeed;
                value ^= (uint)(areaIndex + 1) * 0x9E3779B9u;
                value ^= (uint)(episode + 1) * 0x85EBCA6Bu;
                value ^= (uint)(stream + 1) * 0xC2B2AE35u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (int)(value == 0u ? 0x6D2B79F5u : value);
            }
        }

        uint NextUInt()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public bool Chance(float probability) => Next01() < Mathf.Clamp01(probability);

        public float Range(float min, float max)
        {
            if (max < min)
                (min, max) = (max, min);
            return Mathf.Lerp(min, max, Next01());
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public Vector2 InsideUnitCircle()
        {
            float angle = Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Next01());
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        public Vector3 UnitVector3()
        {
            float z = Range(-1f, 1f);
            float angle = Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(radius * Mathf.Cos(angle), z, radius * Mathf.Sin(angle));
        }
    }
}
