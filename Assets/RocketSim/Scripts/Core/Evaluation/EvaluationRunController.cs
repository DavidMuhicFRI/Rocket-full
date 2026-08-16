using System;
using System.Collections;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Owns one standard-evaluation suite from telemetry subscription through
    /// final summary. The launch coordinator only starts or cancels it.
    /// </summary>
    public sealed class EvaluationRunController
    {
        readonly MonoBehaviour _coroutineOwner;

        EvaluationSession _session;
        SimulationAreaHost _areaHost;
        bool _completing;

        public string LastSummaryPath { get; private set; }
        public bool IsActive => _session != null;
        public event Action<string> Completed;

        public EvaluationRunController(MonoBehaviour coroutineOwner)
        {
            _coroutineOwner = coroutineOwner
                ? coroutineOwner
                : throw new ArgumentNullException(nameof(coroutineOwner));
        }

        /// <summary>Starts collecting outcomes for a fixed evaluation suite.</summary>
        public void Start(
            SimulationAreaHost areaHost,
            string runId,
            EvaluationConfig evaluation,
            string episodeFilePath,
            ScenarioType scenario)
        {
            Cancel(writePartialSummary: false);
            _areaHost = areaHost ?? throw new ArgumentNullException(nameof(areaHost));
            evaluation ??= new EvaluationConfig();
            _session = new EvaluationSession(
                runId,
                evaluation.seed,
                evaluation.episodeCount,
                episodeFilePath,
                scenario);
            LastSummaryPath = null;
            if (TelemetryLogger.Instance != null)
                TelemetryLogger.Instance.EpisodeCompleted += OnEpisodeCompleted;
        }

        /// <summary>Writes an optional partial summary and releases callbacks.</summary>
        public void Cancel(bool writePartialSummary)
        {
            if (TelemetryLogger.Instance != null)
                TelemetryLogger.Instance.EpisodeCompleted -= OnEpisodeCompleted;

            if (_session != null && writePartialSummary)
            {
                try
                {
                    LastSummaryPath = _session.Finish(aborted: true);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Evaluator] Could not write partial summary: {exception.Message}");
                }
            }

            _session = null;
            _areaHost = null;
            _completing = false;
        }

        void OnEpisodeCompleted(TelemetryEpisodeOutcome outcome)
        {
            if (_session == null || _completing) return;

            bool complete = _session.Record(outcome);
            string successes = _session.SuccessMetricDefined
                ? _session.SuccessfulEpisodes.ToString()
                : "n/a (trajectory endpoint task)";
            Debug.Log(
                $"[Evaluator] {_session.CompletedEpisodes}/{_session.TargetEpisodes} episodes, " +
                $"successes={successes}.");
            if (!complete) return;

            _completing = true;
            try
            {
                LastSummaryPath = _session.Finish(aborted: false);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Evaluator] Could not write final summary: {exception.Message}");
                LastSummaryPath = null;
            }

            if (TelemetryLogger.Instance != null)
            {
                TelemetryLogger.Instance.EpisodeCompleted -= OnEpisodeCompleted;
                TelemetryLogger.Instance.DisableLogging();
            }
            _coroutineOwner.StartCoroutine(FinishAtEndOfFrame());
        }

        IEnumerator FinishAtEndOfFrame()
        {
            // Do not destroy an agent from inside its EndEpisode call stack.
            yield return null;
            _areaHost?.StopActiveRunAndShowPreview();
            _areaHost = null;
            _session = null;
            _completing = false;
            Debug.Log($"[Evaluator] Complete. Summary: {LastSummaryPath}");
            Completed?.Invoke(LastSummaryPath);
        }
    }
}
