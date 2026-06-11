using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

        // ── Runtime state ─────────────────────────────────────────────────────
        TelemetryConfig _cfg;
        StreamWriter    _stepWriter;
        StreamWriter    _episodeWriter;
        StringBuilder   _buf = new();
        int             _stepRowCount;
        int             _episodeRowCount;
        string          _stepFilePath;
        string          _episodeFilePath;
        bool            _loggingEnabled;
        List<string>    _metricNames = new();
        List<TelemetryMetricDescriptor> _metrics = new();

        readonly Dictionary<int, EpisodeAccumulator> _openEpisodes = new();

        static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnApplicationQuit() => CloseFiles();
        void OnDestroy()         => CloseFiles();

        public void Initialize(TelemetryConfig cfg) => Initialize(cfg, null);

        public void Initialize(TelemetryConfig cfg, string runId)
        {
            CloseFiles();

            _loggingEnabled = true;
            _cfg             = cfg ?? new TelemetryConfig();
            _metrics         = BuildMetricDescriptors(_cfg);
            _metricNames     = new List<string>(_metrics.Count);
            foreach (var metric in _metrics) _metricNames.Add(metric.Name);
            _stepRowCount    = 0;
            _episodeRowCount = 0;

            OpenFiles(SanitizeRunId(runId));
        }

        public void DisableLogging()
        {
            _loggingEnabled = false;
            CloseFiles();
            _cfg = null;
        }

        public void Log(TelemetryRow row)
        {
            if (!_loggingEnabled) return;
            if (_cfg == null || _stepWriter == null || _episodeWriter == null) return;

            AccumulateEpisode(row);

            if (row.areaIndex == stepLoggingAreaIndex)
            {
                AppendStepRow(row);
                if (flushInterval > 0 && _stepRowCount % flushInterval == 0) _stepWriter.Flush();
            }
        }

        public void CompleteEpisode(int areaIndex, int episode)
        {
            if (!_loggingEnabled) return;
            if (_cfg == null || _episodeWriter == null) return;
            if (!_openEpisodes.TryGetValue(areaIndex, out var acc)) return;
            if (acc.Episode != episode || acc.StepCount == 0) return;

            AppendEpisodeRow(acc);
            _openEpisodes.Remove(areaIndex);
        }

        public string FilePath        => _stepFilePath;
        public string StepFilePath    => _stepFilePath;
        public string EpisodeFilePath => _episodeFilePath;

        void OpenFiles(string runId)
        {
            string dir = Path.Combine(Application.persistentDataPath, outputFolder);
            Directory.CreateDirectory(dir);

            _episodeFilePath = Path.Combine(dir, $"telemetry_{runId}_episodes.csv");
            _stepFilePath    = Path.Combine(dir, $"telemetry_{runId}_steps.csv");

            _episodeWriter = OpenCsv(_episodeFilePath, BuildEpisodeHeader());
            _stepWriter    = OpenCsv(_stepFilePath, BuildStepHeader());

            Debug.Log($"[TelemetryLogger] Episode telemetry: {_episodeFilePath}");
            Debug.Log($"[TelemetryLogger] Step telemetry: {_stepFilePath}");
        }

        StreamWriter OpenCsv(string path, string header)
        {
            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;

            if (!writeHeader)
            {
                using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string existingHeader = reader.ReadLine();
                if (existingHeader != header)
                {
                    Debug.LogWarning(
                        $"[TelemetryLogger] Existing CSV header differs from current telemetry config: {path}. " +
                        "Appending anyway because this run_id already owns the file.");
                }
            }

            var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (writeHeader) writer.WriteLine(header);
            return writer;
        }

        void CloseFiles()
        {
            FlushOpenEpisodes();

            CloseWriter(ref _stepWriter);
            CloseWriter(ref _episodeWriter);
        }

        void CloseWriter(ref StreamWriter writer)
        {
            if (writer == null) return;
            writer.Flush();
            writer.Close();
            writer = null;
        }

        void FlushOpenEpisodes()
        {
            if (_episodeWriter != null)
            {
                foreach (var acc in _openEpisodes.Values)
                    if (acc.StepCount > 0)
                        AppendEpisodeRow(acc);

                _episodeWriter.Flush();
            }

            _openEpisodes.Clear();
        }

        string BuildStepHeader()
        {
            var cols = new List<string> { "WallTime", "AreaIndex", "Episode", "Step" };
            cols.AddRange(_metricNames);
            return string.Join(",", cols);
        }

        string BuildEpisodeHeader()
        {
            var cols = new List<string> { "WallTime", "AreaIndex", "Episode", "StepCount" };
            foreach (string metric in _metricNames)
            {
                cols.Add($"{metric}_Mean");
                cols.Add($"{metric}_StdDev");
            }

            return string.Join(",", cols);
        }

        static List<TelemetryMetricDescriptor> BuildMetricDescriptors(TelemetryConfig cfg)
        {
            var metrics = new List<TelemetryMetricDescriptor>();
            foreach (var metric in MetricCatalog)
                if (metric.IsEnabled(cfg))
                    metrics.Add(metric);

            return metrics;
        }

        void AccumulateEpisode(TelemetryRow row)
        {
            if (!_openEpisodes.TryGetValue(row.areaIndex, out var acc) || acc.Episode != row.episode)
            {
                if (acc != null && acc.StepCount > 0)
                    AppendEpisodeRow(acc);

                acc = new EpisodeAccumulator(row.areaIndex, row.episode, _metrics.Count);
                _openEpisodes[row.areaIndex] = acc;
            }

            acc.Add(row, _metrics);
        }

        void AppendStepRow(TelemetryRow r)
        {
            _buf.Clear();

            Append(DateTime.UtcNow.ToString("o", CsvCulture));
            Append(r.areaIndex);
            Append(r.episode);
            Append(r.step);
            AppendMetricValues(_metrics, r, Append);

            TrimTrailingComma();
            _stepWriter.WriteLine(_buf);
            _stepRowCount++;
        }

        void AppendEpisodeRow(EpisodeAccumulator acc)
        {
            _buf.Clear();

            Append(DateTime.UtcNow.ToString("o", CsvCulture));
            Append(acc.AreaIndex);
            Append(acc.Episode);
            Append(acc.StepCount);

            foreach (var stat in acc.Stats)
            {
                Append(stat.Mean);
                Append(stat.StdDev);
            }

            TrimTrailingComma();
            _episodeWriter.WriteLine(_buf);
            _episodeRowCount++;
            if (flushInterval > 0 && _episodeRowCount % flushInterval == 0) _episodeWriter.Flush();
        }

        static void AppendMetricValues(IReadOnlyList<TelemetryMetricDescriptor> metrics, TelemetryRow r, Action<float> append)
        {
            foreach (var metric in metrics)
                append(metric.Read(r));
        }

        static void AppendV3(Vector3 v, Action<float> append)
        {
            append(v.x);
            append(v.y);
            append(v.z);
        }

        void Append(string v) { _buf.Append(v); _buf.Append(','); }
        void Append(int    v) { _buf.Append(v); _buf.Append(','); }
        void Append(float  v) { _buf.Append(v.ToString("F6", CsvCulture)); _buf.Append(','); }
        void Append(double v) { _buf.Append(v.ToString("F6", CsvCulture)); _buf.Append(','); }

        void TrimTrailingComma()
        {
            if (_buf.Length > 0 && _buf[^1] == ',') _buf.Length--;
        }

        static string SanitizeRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return "unnamed_run";

            var sb = new StringBuilder(runId.Length);
            foreach (char c in runId.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

            return sb.Length > 0 ? sb.ToString() : "unnamed_run";
        }

        // Metric catalog is the single source of truth for CSV column order and
        // row extraction. Add new telemetry here instead of editing headers and
        // row appending separately.
        static readonly TelemetryMetricDescriptor[] MetricCatalog =
        {
            new("Goal_DistanceToGoal3D_m", cfg => cfg.logGoalMetrics, r => r.goal_distance3D),
            new("Goal_HorizontalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => r.goal_planarDistance),
            new("Goal_VerticalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => r.goal_verticalError),
            new("Goal_AbsoluteVerticalDistanceToGoal_m", cfg => cfg.logGoalMetrics, r => Mathf.Abs(r.goal_verticalError)),
            new("Track_IsHoverPhase01", cfg => cfg.logGoalMetrics, r => r.track_phaseHover01),
            new("Track_IsHoverReady01", cfg => cfg.logGoalMetrics, r => r.track_hoverReady01),
            new("Track_StableHoverTime_s", cfg => cfg.logGoalMetrics, r => r.track_stableTime),
            new("Track_TargetCaptured01", cfg => cfg.logGoalMetrics, r => r.track_targetReached01),
            new("Track_HoverRadius_m", cfg => cfg.logGoalMetrics, r => r.track_settleRadius),
            new("Track_CurriculumDifficulty01", cfg => cfg.logGoalMetrics, r => r.track_curriculumProgress),
            new("Track_TargetJumpDistance_m", cfg => cfg.logGoalMetrics, r => r.track_segmentStartDistance),
            new("Track_TargetElapsedTime_s", cfg => cfg.logGoalMetrics, r => r.track_segmentElapsedTime),
            new("Track_TravelProgress01", cfg => cfg.logGoalMetrics, r => r.track_travelProgress01),
            new("Track_NormalizedTravelRate_1ps", cfg => cfg.logGoalMetrics, r => r.track_travelProgressRate),
            new("Track_DirectionAccuracy01", cfg => cfg.logGoalMetrics, r => r.track_directionEfficiency01),
            new("Track_StabilizationQuality01", cfg => cfg.logGoalMetrics, r => r.track_settleQuality01),
            new("State_RocketAltitude_m", cfg => cfg.logGoalMetrics, r => r.state_altitude),
            new("Nav_BearingToGoalDeg", cfg => cfg.logGoalMetrics, r => r.nav_targetBearingDeg),
            new("Nav_HorizontalVelocityBearingDeg", cfg => cfg.logVelocityMetrics, r => r.nav_velocityBearingDeg),
            new("Nav_VelocityDirectionErrorToGoalDeg", cfg => cfg.logVelocityMetrics, r => r.nav_velocityTargetErrorDeg),
            new("Nav_VelocityAlignmentToGoal", cfg => cfg.logVelocityMetrics, r => r.nav_goalAlignment),
            new("Nav_GimbalBearingDeg", cfg => cfg.logControlMetrics, r => r.nav_gimbalBearingDeg),
            new("Nav_GimbalDirectionErrorToGoalDeg", cfg => cfg.logControlMetrics, r => r.nav_gimbalTargetErrorDeg),

            new("Att_TiltDeg", cfg => cfg.logAttitudeMetrics, r => r.att_tiltDeg),
            new("Att_Uprightness", cfg => cfg.logAttitudeMetrics, r => r.att_uprightness),
            new("Att_RollDeg", cfg => cfg.logAttitudeMetrics, r => r.att_rollDeg),
            new("Att_PitchDeg", cfg => cfg.logAttitudeMetrics, r => r.att_pitchDeg),
            new("Att_AngularRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_angularRateDegS),
            new("Att_TiltRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_tiltRateDegS),
            new("Att_PitchRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_pitchRateDegS),
            new("Att_YawRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_yawRateDegS),
            new("Att_RollRateDegS", cfg => cfg.logAttitudeMetrics, r => r.att_rollRateDegS),
            new("Aero_AoaDeg", cfg => cfg.logAttitudeMetrics, r => r.phys_aoaDeg),

            new("Vel_Speed3D_mps", cfg => cfg.logVelocityMetrics, r => r.vel_speed3D),
            new("Vel_HorizontalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.vel_planarSpeed),
            new("Vel_VerticalSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.vel_verticalSpeed),
            new("Vel_ClosureSpeedToGoal_mps", cfg => cfg.logVelocityMetrics, r => r.vel_goalClosureRate),
            new("Vel_HorizontalClosureSpeedToGoal_mps", cfg => cfg.logVelocityMetrics, r => r.vel_horizontalClosureRate),
            new("Vel_AirRelativeSpeed_mps", cfg => cfg.logVelocityMetrics, r => r.phys_speed),

            new("Ctrl_MeanThrottle01", cfg => cfg.logControlMetrics, r => r.ctrl_throttleMean),
            new("Ctrl_MaxThrottle01", cfg => cfg.logControlMetrics, r => r.ctrl_throttleMax),
            new("Ctrl_MeanGimbalDeflectionDeg", cfg => cfg.logControlMetrics, r => r.ctrl_gimbalMeanAbsDeg),
            new("Ctrl_MeanFinDeflectionDeg", cfg => cfg.logControlMetrics, r => r.ctrl_finMeanAbsDeg),
            new("Fuel_RemainingFraction01", cfg => cfg.logControlMetrics, r => r.fuel_fraction),
            new("Fuel_UsedKg", cfg => cfg.logControlMetrics, r => r.fuel_usedKg),

            new("Load_LoadFactorG", cfg => cfg.logAeroLoadMetrics, r => r.load_gForce),
            new("Load_AngularAccelerationDegS2", cfg => cfg.logAeroLoadMetrics, r => r.load_angularAccelDegS2),
            new("Load_DynamicPressurePa", cfg => cfg.logAeroLoadMetrics, r => r.phys_dynPressure),
            new("Thermal_HeatFluxWm2", cfg => cfg.logAeroLoadMetrics, r => r.sen.HeatFlux),
            new("Thermal_PeakHeatFluxWm2", cfg => cfg.logAeroLoadMetrics, r => r.sen.PeakHeatFlux),
            new("Stress_BendingPa", cfg => cfg.logAeroLoadMetrics, r => r.sen.BendingStress),
            new("Stress_AxialPa", cfg => cfg.logAeroLoadMetrics, r => r.sen.AxialStress),

            new("Env_WindSpeed_mps", cfg => cfg.logEnvironmentMetrics, r => r.env_windSpeed),
            new("Env_HorizontalWindSpeed_mps", cfg => cfg.logEnvironmentMetrics, r => r.env_windPlanarSpeed),
            new("Env_WindVelocityAlignment", cfg => cfg.logEnvironmentMetrics, r => r.env_windAlignment),
            new("Env_NormalizedDynamicPressure01", cfg => cfg.logEnvironmentMetrics, r => r.obs_dynPressNorm),

            new("Reward_StepReward", cfg => cfg.logRewardMetrics, r => r.stepReward),
        };

        readonly struct TelemetryMetricDescriptor
        {
            public readonly string Name;
            readonly Func<TelemetryConfig, bool> _enabled;
            readonly Func<TelemetryRow, float> _read;

            public TelemetryMetricDescriptor(string name, Func<TelemetryConfig, bool> enabled, Func<TelemetryRow, float> read)
            {
                Name = name;
                _enabled = enabled;
                _read = read;
            }

            public bool IsEnabled(TelemetryConfig cfg) => _enabled(cfg);
            public float Read(TelemetryRow row) => _read(row);
        }

        class EpisodeAccumulator
        {
            public readonly int AreaIndex;
            public readonly int Episode;
            public readonly RunningStats[] Stats;
            public int StepCount { get; private set; }

            public EpisodeAccumulator(int areaIndex, int episode, int metricCount)
            {
                AreaIndex = areaIndex;
                Episode   = episode;
                Stats     = new RunningStats[metricCount];
                for (int i = 0; i < Stats.Length; i++) Stats[i] = new RunningStats();
            }

            public void Add(TelemetryRow row, IReadOnlyList<TelemetryMetricDescriptor> metrics)
            {
                int i = 0;
                AppendMetricValues(metrics, row, value =>
                {
                    if (i < Stats.Length) Stats[i++].Add(value);
                });

                StepCount++;
            }
        }

        class RunningStats
        {
            int    _count;
            double _mean;
            double _m2;

            public double Mean => _count > 0 ? _mean : 0.0;
            public double StdDev => _count > 1 ? Math.Sqrt(_m2 / (_count - 1)) : 0.0;

            // Welford's online algorithm: stable mean/stddev without storing every step.
            public void Add(double value)
            {
                _count++;
                double delta = value - _mean;
                _mean += delta / _count;
                double delta2 = value - _mean;
                _m2 += delta * delta2;
            }
        }
    }

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
        public float nav_targetBearingDeg, nav_velocityBearingDeg, nav_velocityTargetErrorDeg;
        public float nav_goalAlignment, nav_gimbalBearingDeg, nav_gimbalTargetErrorDeg;
        public float state_altitude;
        public float att_tiltDeg, att_uprightness, att_rollDeg, att_pitchDeg;
        public float att_angularRateDegS, att_tiltRateDegS, att_pitchRateDegS, att_yawRateDegS, att_rollRateDegS;
        public float vel_speed3D, vel_planarSpeed, vel_verticalSpeed, vel_goalClosureRate, vel_horizontalClosureRate;
        public float ctrl_throttleMean, ctrl_throttleMax, ctrl_gimbalMeanAbsDeg, ctrl_finMeanAbsDeg;
        public float fuel_fraction, fuel_usedKg;
        public float load_gForce, load_angularAccelDegS2;
        public float env_windSpeed, env_windPlanarSpeed, env_windAlignment;
        public float stepReward;
    }
}
