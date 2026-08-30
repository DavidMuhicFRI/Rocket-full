// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Evaluation/EvaluationSession.cs
// Purpose: Accumulates a fixed standardized evaluation suite and writes one
// aggregate, analysis-ready JSON summary beside the detailed telemetry CSVs.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RocketSim
{
    /// <summary>Serializable count for one terminal outcome category.</summary>
    [Serializable]
    public class EvaluationTerminationCount
    {
        public string reason;
        public int count;
    }

    /// <summary>Analysis-ready result for one fixed curriculum difficulty band.</summary>
    [Serializable]
    public class EvaluationDifficultyBandSummary
    {
        public float difficulty01;
        public int targetEpisodes;
        public int completedEpisodes;
        public bool successMetricDefined;
        public int successfulEpisodes;
        public float successRate;
        public float successWilsonLower95;
        public float successWilsonUpper95;
        public float meanDurationSeconds;
        public float meanFuelUsedKg;
        public float meanFinalPlanarDistanceM;
        public float meanFinalSpeedMps;
        public float meanFinalTiltDeg;
        public float meanHoverTrackCaptures;
        public float meanHoverTrackRelocatedCaptures;
        public List<EvaluationTerminationCount> terminationCounts = new();
    }

    /// <summary>
    /// Final evaluator artifact. It reports terminal state across every episode,
    /// successful-only landing quality, and physical touchdown diagnostics when
    /// the active task is leg landing.
    /// </summary>
    [Serializable]
    public class EvaluationSummary
    {
        public string modelRunId;
        public string scenario;
        public string completedUtc;
        public bool aborted;
        public int evaluationSeed;
        public int targetEpisodes;
        public int completedEpisodes;
        public bool successMetricDefined;
        public int successfulEpisodes;
        public float successRate;
        public float successWilsonLower95;
        public float successWilsonUpper95;
        public int successfulMetricEpisodes;
        public float meanDurationSeconds;
        public float meanFuelUsedKg;
        public float meanRcsPropellantUsedKg;
        public float meanEngineRestartCount;
        public float meanEngineFirstIgnitionCount;
        public float meanFinalPlanarDistanceM;
        public float meanFinalYawErrorDeg;
        public float meanFinalSpeedMps;
        public float meanFinalVerticalSpeedMps;
        public float meanFinalHorizontalSpeedMps;
        public float meanFinalTiltDeg;
        public float meanFinalAngularRateDegS;
        public float meanSuccessfulPlanarDistanceM;
        public float meanSuccessfulYawErrorDeg;
        public float meanSuccessfulSpeedMps;
        public float meanSuccessfulVerticalSpeedMps;
        public float meanSuccessfulHorizontalSpeedMps;
        public float meanSuccessfulTiltDeg;
        public float meanSuccessfulAngularRateDegS;
        public int legTouchdownMetricEpisodes;
        public float meanLegFirstContactSpeedMps;
        public float meanLegFirstContactVerticalSpeedMps;
        public float meanLegFirstContactHorizontalSpeedMps;
        public float meanLegFirstContactTiltDeg;
        public float meanLegFirstContactAngularRateDegS;
        public float meanLegMaximumContactImpulseNs;
        public float meanLegMaximumReboundHeightM;
        public float meanLegStableHoldSeconds;
        public float legStructuralStrikeRate;
        public float legFootOutsidePadRate;
        public float fixedDeltaTimeSeconds;
        public int decisionPeriod;
        public float evaluationTimeScale = 1f;
        public bool usesCurriculumBands;
        public int episodesPerCurriculumBand;
        public float evaluationDifficulty01;
        public float meanHoverTrackCaptures;
        public float meanHoverTrackRelocatedCaptures;
        public List<EvaluationDifficultyBandSummary> curriculumBands = new();
        public List<EvaluationTerminationCount> terminationCounts = new();
    }

    /// <summary>
    /// Pure result accumulator used by EvaluationRunController. It has no dependency
    /// on Agent lifecycle, which keeps confidence-interval and summary behavior
    /// independently testable.
    /// </summary>
    public sealed class EvaluationSession
    {
        const double WilsonZ95 = 1.959963984540054;

        readonly string _modelRunId;
        readonly int _evaluationSeed;
        readonly int _targetEpisodes;
        readonly string _summaryPath;
        readonly int[] _terminationCounts;
        readonly bool _usesCurriculumBands;
        readonly float _evaluationTimeScale;
        readonly DifficultyBandAccumulator[] _difficultyBands;

        int _completed;
        int _successes;
        double _durationSum;
        double _fuelUsedSum;
        double _rcsPropellantUsedSum;
        double _engineRestartCountSum;
        double _engineFirstIgnitionCountSum;
        double _hoverTrackCaptureSum;
        double _hoverTrackRelocatedCaptureSum;
        double _allPlanarDistanceSum;
        double _allYawErrorSum;
        double _allSpeedSum;
        double _allVerticalSpeedSum;
        double _allHorizontalSpeedSum;
        double _allTiltSum;
        double _allAngularRateSum;
        double _successPlanarDistanceSum;
        double _successYawErrorSum;
        double _successSpeedSum;
        double _successVerticalSpeedSum;
        double _successHorizontalSpeedSum;
        double _successTiltSum;
        double _successAngularRateSum;
        int _legTouchdownMetricEpisodes;
        int _legStructuralStrikes;
        int _legFootOutsidePadEpisodes;
        double _legFirstContactSpeedSum;
        double _legFirstContactVerticalSpeedSum;
        double _legFirstContactHorizontalSpeedSum;
        double _legFirstContactTiltSum;
        double _legFirstContactAngularRateSum;
        double _legMaximumContactImpulseSum;
        double _legMaximumReboundHeightSum;
        double _legStableHoldSum;
        float _fixedDeltaTime;
        int _decisionPeriod;
        ScenarioType _scenario;
        bool _finished;

        public EvaluationSession(
            string modelRunId,
            int evaluationSeed,
            int targetEpisodes,
            string episodeTelemetryPath,
            ScenarioType scenario = ScenarioType.ChopstickLanding,
            float evaluationTimeScale = EvaluationConfig.DefaultTimeScale)
        {
            _modelRunId = string.IsNullOrWhiteSpace(modelRunId) ? "unnamed_run" : modelRunId;
            _evaluationSeed = Mathf.Max(0, evaluationSeed);
            _targetEpisodes = Mathf.Max(1, targetEpisodes);
            _summaryPath = BuildSummaryPath(episodeTelemetryPath);
            _terminationCounts = new int[Enum.GetValues(typeof(EpisodeTerminationReason)).Length];
            _scenario = scenario;
            _usesCurriculumBands = scenario.UsesCurriculumEvaluationBands();
            _evaluationTimeScale = Mathf.Max(1f, evaluationTimeScale);
            if (_usesCurriculumBands)
            {
                _difficultyBands = new DifficultyBandAccumulator[EvaluationConfig.CurriculumBandCount];
                for (int band = 0; band < _difficultyBands.Length; band++)
                    _difficultyBands[band] = new DifficultyBandAccumulator(
                        band / (float)(EvaluationConfig.CurriculumBandCount - 1));
            }
        }

        public int CompletedEpisodes => _completed;
        public int SuccessfulEpisodes => _successes;
        public int TargetEpisodes => _targetEpisodes;
        public bool TargetReached => _completed >= _targetEpisodes;
        public bool SuccessMetricDefined => ScenarioDefinesSuccess(_scenario);
        public string SummaryPath => _summaryPath;

        /// <summary>
        /// Adds one completed episode. Returns true exactly when the configured
        /// suite is complete; further records are ignored.
        /// </summary>
        public bool Record(TelemetryEpisodeOutcome outcome)
        {
            if (_finished || TargetReached || !outcome.completed)
                return TargetReached;

            _completed++;
            _durationSum += outcome.durationSeconds;
            _fuelUsedSum += outcome.fuelUsedKg;
            _rcsPropellantUsedSum += outcome.rcsPropellantUsedKg;
            _engineRestartCountSum += outcome.engineRestartCount;
            _engineFirstIgnitionCountSum += outcome.engineFirstIgnitionCount;
            _hoverTrackCaptureSum += outcome.hoverTrackCaptures;
            _hoverTrackRelocatedCaptureSum += outcome.hoverTrackRelocatedCaptures;
            _allPlanarDistanceSum += outcome.finalPlanarDistanceM;
            _allYawErrorSum += outcome.finalYawErrorDeg;
            _allSpeedSum += outcome.finalSpeedMps;
            _allVerticalSpeedSum += outcome.finalVerticalSpeedMps;
            _allHorizontalSpeedSum += outcome.finalHorizontalSpeedMps;
            _allTiltSum += outcome.finalTiltDeg;
            _allAngularRateSum += outcome.finalAngularRateDegS;
            _fixedDeltaTime = outcome.fixedDeltaTimeSeconds;
            _decisionPeriod = outcome.decisionPeriod;
            _scenario = outcome.scenario;

            if (outcome.legStructuralStrike) _legStructuralStrikes++;
            if (outcome.legFootOutsidePad) _legFootOutsidePadEpisodes++;
            if (outcome.legTouchdownOccurred)
            {
                _legTouchdownMetricEpisodes++;
                _legFirstContactSpeedSum += outcome.legFirstContactSpeedMps;
                _legFirstContactVerticalSpeedSum += outcome.legFirstContactVerticalSpeedMps;
                _legFirstContactHorizontalSpeedSum += outcome.legFirstContactHorizontalSpeedMps;
                _legFirstContactTiltSum += outcome.legFirstContactTiltDeg;
                _legFirstContactAngularRateSum += outcome.legFirstContactAngularRateDegS;
                _legMaximumContactImpulseSum += outcome.legMaximumContactImpulseNs;
                _legMaximumReboundHeightSum += outcome.legMaximumReboundHeightM;
                _legStableHoldSum += outcome.legStableHoldSeconds;
            }

            int reason = (int)outcome.terminationReason;
            if (reason >= 0 && reason < _terminationCounts.Length)
                _terminationCounts[reason]++;

            if (outcome.success)
            {
                _successes++;
                _successPlanarDistanceSum += outcome.finalPlanarDistanceM;
                _successYawErrorSum += outcome.finalYawErrorDeg;
                _successSpeedSum += outcome.finalSpeedMps;
                _successVerticalSpeedSum += outcome.finalVerticalSpeedMps;
                _successHorizontalSpeedSum += outcome.finalHorizontalSpeedMps;
                _successTiltSum += outcome.finalTiltDeg;
                _successAngularRateSum += outcome.finalAngularRateDegS;
            }

            if (_usesCurriculumBands)
                _difficultyBands[EvaluationConfig.BandIndex(outcome.curriculumDifficulty01)]
                    .Record(outcome);

            return TargetReached;
        }

        /// <summary>
        /// Writes the summary once. An early user stop sets aborted=true while
        /// retaining every completed episode collected so far.
        /// </summary>
        public string Finish(bool aborted)
        {
            if (_finished) return _summaryPath;
            _finished = true;

            EvaluationSummary summary = BuildSummary(aborted);
            string directory = Path.GetDirectoryName(_summaryPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_summaryPath, JsonUtility.ToJson(summary, true));
            return _summaryPath;
        }

        /// <summary>Builds an in-memory summary for tests and UI diagnostics.</summary>
        public EvaluationSummary BuildSummary(bool aborted)
        {
            bool successMetricDefined = SuccessMetricDefined;
            float lower;
            float upper;
            if (successMetricDefined)
                WilsonInterval95(_successes, _completed, out lower, out upper);
            else
            {
                lower = 0f;
                upper = 0f;
            }
            double completedDivisor = Math.Max(1, _completed);
            double successDivisor = Math.Max(1, _successes);
            double legTouchdownDivisor = Math.Max(1, _legTouchdownMetricEpisodes);

            var summary = new EvaluationSummary
            {
                modelRunId = _modelRunId,
                scenario = _scenario.ToString(),
                completedUtc = DateTime.UtcNow.ToString("o"),
                aborted = aborted,
                evaluationSeed = _evaluationSeed,
                targetEpisodes = _targetEpisodes,
                completedEpisodes = _completed,
                successMetricDefined = successMetricDefined,
                successfulEpisodes = _successes,
                successRate = successMetricDefined && _completed > 0 ? (float)_successes / _completed : 0f,
                successWilsonLower95 = lower,
                successWilsonUpper95 = upper,
                successfulMetricEpisodes = _successes,
                meanDurationSeconds = (float)(_durationSum / completedDivisor),
                meanFuelUsedKg = (float)(_fuelUsedSum / completedDivisor),
                meanRcsPropellantUsedKg = (float)(_rcsPropellantUsedSum / completedDivisor),
                meanEngineRestartCount = (float)(_engineRestartCountSum / completedDivisor),
                meanEngineFirstIgnitionCount = (float)(_engineFirstIgnitionCountSum / completedDivisor),
                meanHoverTrackCaptures = (float)(_hoverTrackCaptureSum / completedDivisor),
                meanHoverTrackRelocatedCaptures = (float)(_hoverTrackRelocatedCaptureSum / completedDivisor),
                meanFinalPlanarDistanceM = (float)(_allPlanarDistanceSum / completedDivisor),
                meanFinalYawErrorDeg = (float)(_allYawErrorSum / completedDivisor),
                meanFinalSpeedMps = (float)(_allSpeedSum / completedDivisor),
                meanFinalVerticalSpeedMps = (float)(_allVerticalSpeedSum / completedDivisor),
                meanFinalHorizontalSpeedMps = (float)(_allHorizontalSpeedSum / completedDivisor),
                meanFinalTiltDeg = (float)(_allTiltSum / completedDivisor),
                meanFinalAngularRateDegS = (float)(_allAngularRateSum / completedDivisor),
                meanSuccessfulPlanarDistanceM = (float)(_successPlanarDistanceSum / successDivisor),
                meanSuccessfulYawErrorDeg = (float)(_successYawErrorSum / successDivisor),
                meanSuccessfulSpeedMps = (float)(_successSpeedSum / successDivisor),
                meanSuccessfulVerticalSpeedMps = (float)(_successVerticalSpeedSum / successDivisor),
                meanSuccessfulHorizontalSpeedMps = (float)(_successHorizontalSpeedSum / successDivisor),
                meanSuccessfulTiltDeg = (float)(_successTiltSum / successDivisor),
                meanSuccessfulAngularRateDegS = (float)(_successAngularRateSum / successDivisor),
                legTouchdownMetricEpisodes = _legTouchdownMetricEpisodes,
                meanLegFirstContactSpeedMps = (float)(_legFirstContactSpeedSum / legTouchdownDivisor),
                meanLegFirstContactVerticalSpeedMps = (float)(_legFirstContactVerticalSpeedSum / legTouchdownDivisor),
                meanLegFirstContactHorizontalSpeedMps = (float)(_legFirstContactHorizontalSpeedSum / legTouchdownDivisor),
                meanLegFirstContactTiltDeg = (float)(_legFirstContactTiltSum / legTouchdownDivisor),
                meanLegFirstContactAngularRateDegS = (float)(_legFirstContactAngularRateSum / legTouchdownDivisor),
                meanLegMaximumContactImpulseNs = (float)(_legMaximumContactImpulseSum / legTouchdownDivisor),
                meanLegMaximumReboundHeightM = (float)(_legMaximumReboundHeightSum / legTouchdownDivisor),
                meanLegStableHoldSeconds = (float)(_legStableHoldSum / legTouchdownDivisor),
                legStructuralStrikeRate = _completed > 0 ? (float)_legStructuralStrikes / _completed : 0f,
                legFootOutsidePadRate = _completed > 0 ? (float)_legFootOutsidePadEpisodes / _completed : 0f,
                fixedDeltaTimeSeconds = _fixedDeltaTime,
                decisionPeriod = _decisionPeriod,
                evaluationTimeScale = _evaluationTimeScale,
                usesCurriculumBands = _usesCurriculumBands,
                episodesPerCurriculumBand = _usesCurriculumBands ? EvaluationConfig.EpisodesPerCurriculumBand : 0,
                evaluationDifficulty01 = _usesCurriculumBands ? -1f : 0f
            };

            if (_usesCurriculumBands)
                for (int band = 0; band < _difficultyBands.Length; band++)
                    summary.curriculumBands.Add(
                        _difficultyBands[band].Build(successMetricDefined));

            Array reasons = Enum.GetValues(typeof(EpisodeTerminationReason));
            foreach (EpisodeTerminationReason reason in reasons)
            {
                int count = _terminationCounts[(int)reason];
                if (count <= 0) continue;
                summary.terminationCounts.Add(new EvaluationTerminationCount
                {
                    reason = reason.ToString(),
                    count = count
                });
            }

            return summary;
        }

        /// <summary>
        /// Identifies scenarios with an explicit terminal/cycle success event.
        /// Fixed hover intentionally has none: its evaluator measures trajectory
        /// quality through its fixed horizon or an earlier safety failure.
        /// </summary>
        static bool ScenarioDefinesSuccess(ScenarioType scenario) =>
            scenario == ScenarioType.ChopstickLanding ||
            scenario == ScenarioType.LegLanding ||
            scenario == ScenarioType.HoverTracking;

        /// <summary>Computes a two-sided 95% Wilson interval for a proportion.</summary>
        public static void WilsonInterval95(int successes, int trials, out float lower, out float upper)
        {
            if (trials <= 0)
            {
                lower = 0f;
                upper = 0f;
                return;
            }

            double n = trials;
            double p = Math.Max(0d, Math.Min(1d, (double)successes / trials));
            double z2 = WilsonZ95 * WilsonZ95;
            double denominator = 1d + z2 / n;
            double center = (p + z2 / (2d * n)) / denominator;
            double margin = WilsonZ95 * Math.Sqrt(p * (1d - p) / n + z2 / (4d * n * n)) / denominator;
            lower = (float)Math.Max(0d, center - margin);
            upper = (float)Math.Min(1d, center + margin);
        }

        /// <summary>Places the JSON summary beside the evaluation episode CSV.</summary>
        public static string BuildSummaryPath(string episodeTelemetryPath)
        {
            if (string.IsNullOrWhiteSpace(episodeTelemetryPath))
                return Path.Combine(Application.persistentDataPath, "Telemetry", "evaluation_summary.json");

            string directory = Path.GetDirectoryName(episodeTelemetryPath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(episodeTelemetryPath);
            if (stem.EndsWith("_episodes", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^"_episodes".Length];
            return Path.Combine(directory, $"{stem}_summary.json");
        }

        /// <summary>Small independent accumulator for a single difficulty stratum.</summary>
        sealed class DifficultyBandAccumulator
        {
            readonly float _difficulty01;
            readonly int[] _terminationCounts =
                new int[Enum.GetValues(typeof(EpisodeTerminationReason)).Length];
            int _completed;
            int _successes;
            double _durationSum;
            double _fuelSum;
            double _finalPlanarDistanceSum;
            double _finalSpeedSum;
            double _finalTiltSum;
            double _hoverTrackCaptureSum;
            double _hoverTrackRelocatedCaptureSum;

            public DifficultyBandAccumulator(float difficulty01)
            {
                _difficulty01 = Mathf.Clamp01(difficulty01);
            }

            public void Record(TelemetryEpisodeOutcome outcome)
            {
                _completed++;
                if (outcome.success) _successes++;
                _durationSum += outcome.durationSeconds;
                _fuelSum += outcome.fuelUsedKg;
                _finalPlanarDistanceSum += outcome.finalPlanarDistanceM;
                _finalSpeedSum += outcome.finalSpeedMps;
                _finalTiltSum += outcome.finalTiltDeg;
                _hoverTrackCaptureSum += outcome.hoverTrackCaptures;
                _hoverTrackRelocatedCaptureSum += outcome.hoverTrackRelocatedCaptures;

                int reason = (int)outcome.terminationReason;
                if (reason >= 0 && reason < _terminationCounts.Length)
                    _terminationCounts[reason]++;
            }

            public EvaluationDifficultyBandSummary Build(bool successMetricDefined)
            {
                double divisor = Math.Max(1, _completed);
                float lower = 0f;
                float upper = 0f;
                if (successMetricDefined)
                    WilsonInterval95(_successes, _completed, out lower, out upper);

                var result = new EvaluationDifficultyBandSummary
                {
                    difficulty01 = _difficulty01,
                    targetEpisodes = EvaluationConfig.EpisodesPerCurriculumBand,
                    completedEpisodes = _completed,
                    successMetricDefined = successMetricDefined,
                    successfulEpisodes = _successes,
                    successRate = successMetricDefined && _completed > 0
                        ? (float)_successes / _completed
                        : 0f,
                    successWilsonLower95 = lower,
                    successWilsonUpper95 = upper,
                    meanDurationSeconds = (float)(_durationSum / divisor),
                    meanFuelUsedKg = (float)(_fuelSum / divisor),
                    meanFinalPlanarDistanceM = (float)(_finalPlanarDistanceSum / divisor),
                    meanFinalSpeedMps = (float)(_finalSpeedSum / divisor),
                    meanFinalTiltDeg = (float)(_finalTiltSum / divisor),
                    meanHoverTrackCaptures = (float)(_hoverTrackCaptureSum / divisor),
                    meanHoverTrackRelocatedCaptures =
                        (float)(_hoverTrackRelocatedCaptureSum / divisor)
                };

                foreach (EpisodeTerminationReason reason in Enum.GetValues(
                             typeof(EpisodeTerminationReason)))
                {
                    int count = _terminationCounts[(int)reason];
                    if (count <= 0) continue;
                    result.terminationCounts.Add(new EvaluationTerminationCount
                    {
                        reason = reason.ToString(),
                        count = count
                    });
                }
                return result;
            }
        }
    }
}
