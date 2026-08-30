// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Environment/WindEnvironmentRuntime.cs
// Purpose: Owns the prevailing wind and gust state for one simulation area.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Produces a horizontal wind vector from the frozen environment settings.
    /// Gusts perturb the episode's prevailing direction instead of replacing it.
    /// </summary>
    internal sealed class WindEnvironmentRuntime
    {
        Vector3 _prevailing;
        Vector3 _target;

        public Vector3 Current { get; private set; }

        /// <summary>Clears all wind state before the first episode.</summary>
        public void Clear()
        {
            _prevailing = Vector3.zero;
            _target = Vector3.zero;
            Current = Vector3.zero;
        }

        /// <summary>Selects the episode's deterministic prevailing wind.</summary>
        public void BeginEpisode(SimEnvironmentConfig config, ref DeterministicRandom random)
        {
            if (config == null || !config.windEnabled || config.windSpeed <= 0f)
            {
                Clear();
                return;
            }

            float directionDeg = config.randomizeWindDirectionEachEpisode ? random.Range(0f, 360f) : config.windDirectionDeg;
            float directionRad = directionDeg * Mathf.Deg2Rad;
            _prevailing = new Vector3(Mathf.Sin(directionRad), 0f, Mathf.Cos(directionRad)) * config.windSpeed;
            _target = Current = _prevailing;
        }

        /// <summary>Advances gust selection and smooth wind response by one physics step.</summary>
        public void Step(SimEnvironmentConfig config, ref DeterministicRandom random, float deltaTime)
        {
            if (config is not { windEnabled: true })
            {
                Clear();
                return;
            }

            float windAlpha = 1f - Mathf.Exp(-Mathf.Max(0f, config.windChangeRate) * deltaTime);
            Current = Vector3.Lerp(Current, _target, windAlpha);

            float gustChance = 1f - Mathf.Exp(-Mathf.Max(0f, config.gustFrequencyHz) * deltaTime);
            if (random.Chance(gustChance))
                _target = _prevailing + RandomPlanarVector(ref random, config.EffectiveGustAmp);
        }

        static Vector3 RandomPlanarVector(ref DeterministicRandom random, float maxMagnitude)
        {
            if (maxMagnitude <= 0f)
                return Vector3.zero;

            float angle = random.Range(0f, Mathf.PI * 2f);
            float magnitude = random.Range(0f, maxMagnitude);
            return new Vector3(Mathf.Cos(angle) * magnitude, 0f, Mathf.Sin(angle) * magnitude);
        }
    }
}
