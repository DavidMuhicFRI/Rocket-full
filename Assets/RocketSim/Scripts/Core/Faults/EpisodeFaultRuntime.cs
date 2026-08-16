// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Faults/EpisodeFaultRuntime.cs
// Purpose: Selects deterministic per-episode faults and reports their active
// severity to actuator and physics code.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Owns all mutable fault state for one agent. The agent asks for a severity;
    /// it does not decide which faults exist or when an evaluation fault is active.
    /// </summary>
    internal sealed class EpisodeFaultRuntime
    {
        const int Capacity = 3;

        readonly RocketFaultType[] _types = new RocketFaultType[Capacity];
        readonly int[] _targets = new int[Capacity];
        readonly float[] _severities = new float[Capacity];

        RocketFaultConfig _config;
        RocketPhysicsConfig _vehicle;
        bool _evaluationMode;
        int _count;

        /// <summary>Selects the faults used for the next episode.</summary>
        public void BeginEpisode(
            RocketFaultConfig config,
            BehaviorType behavior,
            RocketPhysicsConfig vehicle,
            int areaIndex,
            int episode)
        {
            _config = config;
            _vehicle = vehicle;
            _evaluationMode = behavior == BehaviorType.Inference;
            _count = 0;

            if (_config == null)
                return;

            _config.Clamp();
            if (_evaluationMode)
            {
                if (_config.evaluationFaultEnabled && _config.evaluationFaultType != RocketFaultType.None)
                    Add(_config.evaluationFaultType, _config.evaluationTargetIndex, _config.evaluationSeverity, areaIndex, episode);
                return;
            }

            if (!_config.allowDuringTraining)
                return;

            var random = new DeterministicRandom(
                DeterministicRandom.EpisodeSeed(_config.seed, areaIndex, episode, stream: 17));
            if (!random.Chance(_config.faultyEpisodeProbability))
                return;

            var candidates = new RocketFaultType[5];
            int candidateCount = PopulateCandidates(candidates);
            if (candidateCount == 0)
                return;

            // Fisher-Yates makes fault selection repeatable for the configured seed.
            for (int i = candidateCount - 1; i > 0; i--)
            {
                int swapIndex = random.Range(0, i + 1);
                (candidates[i], candidates[swapIndex]) = (candidates[swapIndex], candidates[i]);
            }

            int maximumCount = Mathf.Min(_config.maxSimultaneousFaults, candidateCount);
            int selectedCount = random.Range(1, maximumCount + 1);
            for (int i = 0; i < selectedCount; i++)
            {
                RocketFaultType type = candidates[i];
                int targetCount = TargetCount(type);
                int target = targetCount > 0 ? random.Range(0, targetCount) : 0;
                float severity = random.Range(_config.trainingSeverityMin, _config.trainingSeverityMax);
                Add(type, target, severity, areaIndex, episode);
            }
        }

        /// <summary>Returns the strongest active fault matching one actuator.</summary>
        public float Severity(RocketFaultType type, int target, float elapsedSeconds)
        {
            float severity = 0f;
            for (int i = 0; i < _count; i++)
            {
                if (_types[i] != type || _targets[i] != target)
                    continue;

                if (_evaluationMode && !EvaluationWindowContains(elapsedSeconds))
                    continue;

                severity = Mathf.Max(severity, _severities[i]);
            }
            return severity;
        }

        int PopulateCandidates(RocketFaultType[] candidates)
        {
            int count = 0;
            if (_config.allowEngineThrustLoss) candidates[count++] = RocketFaultType.EngineThrustLoss;
            if (_config.allowGimbalJam) candidates[count++] = RocketFaultType.GimbalJam;
            if (_config.allowFinEffectivenessLoss && _vehicle.hasFins) candidates[count++] = RocketFaultType.FinEffectivenessLoss;
            if (_config.allowRcsStuckClosed && _vehicle.hasRCS) candidates[count++] = RocketFaultType.RcsStuckClosed;
            if (_config.allowRcsStuckOpen && _vehicle.hasRCS) candidates[count++] = RocketFaultType.RcsStuckOpen;
            return count;
        }

        void Add(RocketFaultType type, int target, float severity, int areaIndex, int episode)
        {
            if (_count >= Capacity)
                return;

            int targetCount = Mathf.Max(1, TargetCount(type));
            int safeTarget = Mathf.Clamp(target, 0, targetCount - 1);
            float safeSeverity = Mathf.Clamp01(severity);
            _types[_count] = type;
            _targets[_count] = safeTarget;
            _severities[_count] = safeSeverity;
            _count++;

            Debug.Log($"[Fault] area={areaIndex} episode={episode} type={type} target={safeTarget} severity={safeSeverity:F2}");
        }

        int TargetCount(RocketFaultType type) => type switch
        {
            RocketFaultType.EngineThrustLoss => _vehicle.independentEngineCount,
            RocketFaultType.GimbalJam => _vehicle.independentEngineCount,
            RocketFaultType.FinEffectivenessLoss => _vehicle.finCount,
            RocketFaultType.RcsStuckClosed => _vehicle.rcsJetCount,
            RocketFaultType.RcsStuckOpen => _vehicle.rcsJetCount,
            _ => 0
        };

        bool EvaluationWindowContains(float elapsedSeconds)
        {
            if (_config == null || elapsedSeconds < _config.evaluationOnsetSeconds)
                return false;
            return _config.evaluationDurationSeconds <= 0f ||
                   elapsedSeconds <= _config.evaluationOnsetSeconds + _config.evaluationDurationSeconds;
        }
    }
}
