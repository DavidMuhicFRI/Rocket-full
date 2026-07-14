// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/TelemetryLogger.cs
// Purpose: Coordinates telemetry logging, open episode accumulation, and CSV writer helpers.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    public class TelemetryLogger : MonoBehaviour
    {
        public static TelemetryLogger Instance { get; private set; }

        [Header("Output")]
        [Tooltip("Relative to Application.persistentDataPath")]
        public string outputFolder  = "Telemetry";
        public int    flushInterval = 200;

        [Header("Step Logging")]
        [Tooltip("Only this training area writes per-step telemetry. All areas still write episode summaries.")]
        public int stepLoggingAreaIndex = 0;

        TelemetryConfig _cfg;
        TelemetryCsvWriter _stepWriter;
        TelemetryCsvWriter _episodeWriter;
        TelemetryRowFormatter _formatter;
        List<TelemetryMetricDescriptor> _metrics = new();
        readonly Dictionary<int, EpisodeTelemetryAccumulator> _openEpisodes = new();

        string _stepFilePath;
        string _episodeFilePath;
        bool _loggingEnabled;

        /// <summary>
        /// Establishes the process-wide logger singleton. The object is detached
        /// from any scene parent before DontDestroyOnLoad because Unity requires a root object.
        /// </summary>
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // DontDestroyOnLoad only accepts root objects. The logger may be
            // placed under a scene setup object, so detach it first.
            if (transform.parent != null)
                transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Cleans up runtime resources when the application exits.
        /// </summary>
        void OnApplicationQuit() => CloseFiles();
        /// <summary>
        /// Flushes and closes telemetry if the logger is destroyed before application quit.
        /// </summary>
        void OnDestroy()         => CloseFiles();

        /// <summary>
        /// Starts telemetry logging with the current config and writes files
        /// under the default unnamed run id.
        /// </summary>
        public void Initialize(TelemetryConfig cfg) => Initialize(cfg, null);

        /// <summary>
        /// Rebuilds the enabled metric list, opens the step and episode CSV
        /// writers for the run id, and resets any previous logging state.
        /// </summary>
        public void Initialize(TelemetryConfig cfg, string runId)
        {
            InitializeSession(cfg, runId, null);
        }

        /// <summary>
        /// Starts an evaluation session in files that are deliberately separate
        /// from training telemetry for the same model/run id.
        /// </summary>
        public void InitializeEvaluation(TelemetryConfig cfg, string runId)
        {
            InitializeSession(cfg, runId, $"evaluation_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
        }

        void InitializeSession(TelemetryConfig cfg, string runId, string sessionSuffix)
        {
            CloseFiles();

            _loggingEnabled = true;
            _cfg = cfg ?? new TelemetryConfig();
            _metrics = TelemetryMetricCatalog.Build(_cfg);
            _formatter = new TelemetryRowFormatter(_metrics);

            var context = TelemetryRunContext.Create(
                Application.persistentDataPath,
                outputFolder,
                runId,
                sessionSuffix);
            _episodeFilePath = context.EpisodeFilePath;
            _stepFilePath = context.StepFilePath;

            _episodeWriter = TelemetryCsvWriter.Open(_episodeFilePath, _formatter.BuildEpisodeHeader(), flushInterval);
            _stepWriter = TelemetryCsvWriter.Open(_stepFilePath, _formatter.BuildStepHeader(), flushInterval);
            _episodeFilePath = _episodeWriter.FilePath;
            _stepFilePath = _stepWriter.FilePath;

            Debug.Log($"[TelemetryLogger] Episode telemetry: {_episodeFilePath}");
            Debug.Log($"[TelemetryLogger] Step telemetry: {_stepFilePath}");
        }

        /// <summary>
        /// Stops telemetry output for the current run and closes any open CSV
        /// files after flushing accumulated episode summaries.
        /// </summary>
        public void DisableLogging()
        {
            _loggingEnabled = false;
            CloseFiles();
            _cfg = null;
        }

        /// <summary>
        /// Records one simulation step into the active episode accumulator and,
        /// for the configured area only, appends the full per-step CSV row.
        /// </summary>
        public void Log(TelemetryRow row)
        {
            if (!_loggingEnabled) return;
            if (_cfg == null || _stepWriter == null || _episodeWriter == null || _formatter == null) return;

            AccumulateEpisode(row);

            if (row.areaIndex == stepLoggingAreaIndex)
                _stepWriter.WriteLine(_formatter.FormatStepRow(row));
        }

        /// <summary>
        /// Writes and removes the accumulated summary for one completed area
        /// episode. Mismatched or empty accumulators are ignored safely.
        /// </summary>
        public void CompleteEpisode(int areaIndex, int episode, TelemetryEpisodeOutcome outcome)
        {
            if (!_loggingEnabled) return;
            if (_cfg == null || _episodeWriter == null || _formatter == null) return;
            if (!_openEpisodes.TryGetValue(areaIndex, out var acc)) return;
            if (acc.Episode != episode || acc.StepCount == 0) return;

            acc.Outcome = outcome;
            AppendEpisodeRow(acc);
            _openEpisodes.Remove(areaIndex);
        }

        public string FilePath        => _stepFilePath;
        public string StepFilePath    => _stepFilePath;
        public string EpisodeFilePath => _episodeFilePath;

        /// <summary>
        /// Writes partial episode summaries, then disposes both CSV writers.
        /// Calling this repeatedly is safe because disposed references are cleared.
        /// </summary>
        void CloseFiles()
        {
            FlushOpenEpisodes();

            _stepWriter?.Dispose();
            _episodeWriter?.Dispose();
            _stepWriter = null;
            _episodeWriter = null;
        }

        /// <summary>
        /// Writes summaries for any in-progress episodes before closing or
        /// reinitializing the logger so partial runs are not lost.
        /// </summary>
        void FlushOpenEpisodes()
        {
            if (_episodeWriter != null && _formatter != null)
            {
                foreach (var acc in _openEpisodes.Values)
                    if (acc.StepCount > 0)
                        AppendEpisodeRow(acc);

                _episodeWriter.Flush();
            }

            _openEpisodes.Clear();
        }

        /// <summary>
        /// Adds a row to the accumulator for its training area, rolling over to
        /// a new accumulator when that area starts a new episode.
        /// </summary>
        void AccumulateEpisode(TelemetryRow row)
        {
            if (!_openEpisodes.TryGetValue(row.areaIndex, out var acc) || acc.Episode != row.episode)
            {
                if (acc != null && acc.StepCount > 0)
                    AppendEpisodeRow(acc);

                acc = new EpisodeTelemetryAccumulator(row.areaIndex, row.episode, _metrics.Count);
                _openEpisodes[row.areaIndex] = acc;
            }

            acc.Add(row, _metrics);
        }

        /// <summary>
        /// Formats and appends the mean/stddev summary row for one completed episode.
        /// </summary>
        void AppendEpisodeRow(EpisodeTelemetryAccumulator acc)
        {
            _episodeWriter?.WriteLine(_formatter.FormatEpisodeRow(acc));
        }
    }

    /// <summary>
    /// Evaluation-oriented metadata attached when an episode closes. Partial
    /// episodes flushed during shutdown retain the default Completed=false value.
    /// </summary>
    public struct TelemetryEpisodeOutcome
    {
        public bool completed;
        public bool success;
        public EpisodeTerminationReason terminationReason;
        public int environmentSeed;
        public int episodeSeed;
        public float curriculumGlobalDifficulty01;
        public float curriculumDifficulty01;
        public bool curriculumReplay;
        public float landingStartAltitude;
        public float landingFlyawayAltitude;
        public float durationSeconds;
        public float fixedDeltaTimeSeconds;
        public int decisionPeriod;
    }

    /// <summary>
    /// Snapshot of one simulation step before column filtering. The metric
    /// catalog selects fields from this structure, so physics code does not need
    /// to know which telemetry groups the user enabled.
    /// </summary>
    public struct TelemetryRow
    {
        public int   areaIndex, episode, step;

        public Vector3  obs_relPos, obs_vel, obs_angVel, obs_up, obs_windLocal;
        public float    obs_fuelFrac, obs_altNorm, obs_aoaDeg, obs_dynPressNorm;

        public float[]   act_throttle;
        public Vector2[] act_gimbal;
        public float[]   act_fins;

        public RocketSensorPackage sen;

        public float phys_speed, phys_aoaDeg, phys_dynPressure, phys_altitude, phys_fuelKg;
        public float goal_distance3D, goal_planarDistance, goal_verticalError;
        public float track_phaseHover01, track_hoverReady01, track_stableTime, track_targetReached01, track_settleRadius;
        public float track_curriculumProgress, track_segmentStartDistance, track_segmentElapsedTime;
        public float track_travelProgress01, track_travelProgressRate, track_directionEfficiency01, track_settleQuality01;
        public float landing_platformRequired01, landing_platformInsideCapture01;
        public float landing_platformStable01, landing_platformStableTime, landing_platformHalfSize;
        public float nav_targetBearingDeg, nav_velocityBearingDeg, nav_velocityTargetErrorDeg;
        public float nav_goalAlignment, nav_gimbalBearingDeg, nav_gimbalTargetErrorDeg;
        public float state_altitude;
        public float att_tiltDeg, att_uprightness, att_rollDeg, att_pitchDeg;
        public float att_angularRateDegS, att_tiltRateDegS, att_pitchRateDegS, att_yawRateDegS, att_rollRateDegS;
        public float vel_speed3D, vel_planarSpeed, vel_verticalSpeed, vel_goalClosureRate, vel_horizontalClosureRate;
        public float ctrl_throttleMean, ctrl_throttleMax, ctrl_gimbalMeanAbsDeg, ctrl_finMeanAbsDeg, ctrl_rcsActiveFraction;
        public float rcs_propellantKg, rcs_propellantFraction;
        public float fuel_fraction, fuel_usedKg;
        public float load_gForce, load_angularAccelDegS2;
        public float env_windSpeed, env_windPlanarSpeed, env_windAlignment;
        public float stepReward;
    }
}
