// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/TelemetryRowFormatter.cs
// Purpose: Builds stable CSV headers and formats step or episode-summary rows
// in exactly the metric order selected at run start.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RocketSim
{
    internal sealed class TelemetryRowFormatter
    {
        static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;

        readonly IReadOnlyList<TelemetryMetricDescriptor> _metrics;
        readonly List<string> _metricNames;
        readonly StringBuilder _buf = new();

        /// <summary>
        /// Captures the enabled metric order so step rows and episode summary
        /// rows are formatted with stable CSV columns for the run.
        /// </summary>
        public TelemetryRowFormatter(IReadOnlyList<TelemetryMetricDescriptor> metrics)
        {
            _metrics = metrics;
            _metricNames = new List<string>(metrics.Count);
            foreach (var metric in metrics)
                _metricNames.Add(metric.Name);
        }

        /// <summary>
        /// Builds the per-step CSV header with wall time, area/episode/step,
        /// and one column per enabled telemetry metric.
        /// </summary>
        public string BuildStepHeader()
        {
            var cols = new List<string> { "WallTime", "AreaIndex", "Episode", "Step" };
            cols.AddRange(_metricNames);
            return string.Join(",", cols);
        }

        /// <summary>
        /// Builds the per-episode CSV header with mean and standard deviation
        /// columns for each enabled telemetry metric.
        /// </summary>
        public string BuildEpisodeHeader()
        {
            var cols = new List<string>
            {
                "WallTime", "AreaIndex", "Episode", "StepCount", "Completed",
                "Success", "TerminationReason", "EnvironmentSeed", "EpisodeSeed",
                "CurriculumGlobalDifficulty01", "CurriculumEpisodeDifficulty01",
                "CurriculumReplay01", "LandingStartAltitude_m", "LandingFlyawayAltitude_m",
                "Scenario", "InitialPlanarDistance_m", "InitialYawError_deg",
                "InitialSpeed_mps", "InitialVerticalSpeed_mps", "InitialHorizontalSpeed_mps",
                "InitialTilt_deg", "InitialAngularRate_deg_s", "InitialFuel_kg",
                "InitialVehicleMass_kg", "MinimumCommandableNonzeroThrustToWeight",
                "AllActiveEnginesMinimumThrottleThrustToWeight", "AllActiveEnginesMaximumThrustToWeight",
                "LoggedRewardSum", "DurationSeconds",
                "FixedDeltaTimeSeconds", "DecisionPeriod",
                "FinalPlanarDistance_m", "FinalYawError_deg", "FinalSpeed_mps",
                "FinalVerticalSpeed_mps", "FinalHorizontalSpeed_mps",
                "FinalTilt_deg", "FinalAngularRate_deg_s", "FuelUsed_kg",
                "RcsPropellantUsed_kg", "EngineRestartCount", "EngineFirstIgnitionCount",
                "HoverTrackCaptures", "HoverTrackRelocatedCaptures",
                "LegTouchdownOccurred01", "LegFeetOnPad", "LegFootOutsidePad01",
                "LegStructuralStrike01", "LegTouchdownTime_s", "LegFirstContactSpeed_mps",
                "LegFirstContactVerticalSpeed_mps", "LegFirstContactHorizontalSpeed_mps",
                "LegFirstContactTilt_deg", "LegFirstContactAngularRate_deg_s",
                "LegStableHold_s", "LegMaximumContactImpulse_Ns", "LegMaximumReboundHeight_m"
            };
            foreach (string metric in _metricNames)
            {
                cols.Add($"{metric}_Mean");
                cols.Add($"{metric}_StdDev");
            }

            return string.Join(",", cols);
        }

        /// <summary>
        /// Formats one per-step telemetry CSV row in the enabled metric order.
        /// </summary>
        public string FormatStepRow(TelemetryRow r)
        {
            _buf.Clear();

            Append(DateTime.UtcNow.ToString("o", CsvCulture));
            Append(r.areaIndex);
            Append(r.episode);
            Append(r.step);

            foreach (var metric in _metrics)
                Append(metric.Read(r));

            TrimTrailingComma();
            return _buf.ToString();
        }

        /// <summary>
        /// Formats one per-episode telemetry CSV row with mean and standard deviation values.
        /// </summary>
        public string FormatEpisodeRow(EpisodeTelemetryAccumulator acc)
        {
            _buf.Clear();

            Append(DateTime.UtcNow.ToString("o", CsvCulture));
            Append(acc.AreaIndex);
            Append(acc.Episode);
            Append(acc.StepCount);
            Append(acc.Outcome.completed ? 1 : 0);
            Append(acc.Outcome.success ? 1 : 0);
            Append(acc.Outcome.terminationReason.ToString());
            Append(acc.Outcome.environmentSeed);
            Append(acc.Outcome.episodeSeed);
            Append(acc.Outcome.curriculumGlobalDifficulty01);
            Append(acc.Outcome.curriculumDifficulty01);
            Append(acc.Outcome.curriculumReplay ? 1 : 0);
            Append(acc.Outcome.landingStartAltitude);
            Append(acc.Outcome.landingFlyawayAltitude);
            Append(acc.Outcome.scenario.ToString());
            Append(acc.Outcome.initialPlanarDistanceM);
            Append(acc.Outcome.initialYawErrorDeg);
            Append(acc.Outcome.initialSpeedMps);
            Append(acc.Outcome.initialVerticalSpeedMps);
            Append(acc.Outcome.initialHorizontalSpeedMps);
            Append(acc.Outcome.initialTiltDeg);
            Append(acc.Outcome.initialAngularRateDegS);
            Append(acc.Outcome.initialFuelKg);
            Append(acc.Outcome.initialVehicleMassKg);
            Append(acc.Outcome.minimumCommandableNonzeroThrustToWeight);
            Append(acc.Outcome.allEnginesMinimumThrustToWeight);
            Append(acc.Outcome.allEnginesMaximumThrustToWeight);
            Append(acc.LoggedRewardSum);
            Append(acc.Outcome.durationSeconds);
            Append(acc.Outcome.fixedDeltaTimeSeconds);
            Append(acc.Outcome.decisionPeriod);
            Append(acc.Outcome.finalPlanarDistanceM);
            Append(acc.Outcome.finalYawErrorDeg);
            Append(acc.Outcome.finalSpeedMps);
            Append(acc.Outcome.finalVerticalSpeedMps);
            Append(acc.Outcome.finalHorizontalSpeedMps);
            Append(acc.Outcome.finalTiltDeg);
            Append(acc.Outcome.finalAngularRateDegS);
            Append(acc.Outcome.fuelUsedKg);
            Append(acc.Outcome.rcsPropellantUsedKg);
            Append(acc.Outcome.engineRestartCount);
            Append(acc.Outcome.engineFirstIgnitionCount);
            Append(acc.Outcome.hoverTrackCaptures);
            Append(acc.Outcome.hoverTrackRelocatedCaptures);
            Append(acc.Outcome.legTouchdownOccurred ? 1 : 0);
            Append(acc.Outcome.legFeetOnPad);
            Append(acc.Outcome.legFootOutsidePad ? 1 : 0);
            Append(acc.Outcome.legStructuralStrike ? 1 : 0);
            Append(acc.Outcome.legTouchdownTimeSeconds);
            Append(acc.Outcome.legFirstContactSpeedMps);
            Append(acc.Outcome.legFirstContactVerticalSpeedMps);
            Append(acc.Outcome.legFirstContactHorizontalSpeedMps);
            Append(acc.Outcome.legFirstContactTiltDeg);
            Append(acc.Outcome.legFirstContactAngularRateDegS);
            Append(acc.Outcome.legStableHoldSeconds);
            Append(acc.Outcome.legMaximumContactImpulseNs);
            Append(acc.Outcome.legMaximumReboundHeightM);

            foreach (var stat in acc.Stats)
            {
                Append(stat.Mean);
                Append(stat.StdDev);
            }

            TrimTrailingComma();
            return _buf.ToString();
        }

        // All append helpers use one shared buffer. Numeric values use invariant
        // culture so decimal points remain valid regardless of computer locale.
        /// <summary>Appends a text field followed by the CSV separator.</summary>
        void Append(string v) { _buf.Append(v); _buf.Append(','); }
        /// <summary>Appends an integer field followed by the CSV separator.</summary>
        void Append(int    v) { _buf.Append(v); _buf.Append(','); }
        /// <summary>Appends a fixed-precision float using invariant culture.</summary>
        void Append(float  v) { _buf.Append(v.ToString("F6", CsvCulture)); _buf.Append(','); }
        /// <summary>Appends a fixed-precision double using invariant culture.</summary>
        void Append(double v) { _buf.Append(v.ToString("F6", CsvCulture)); _buf.Append(','); }

        /// <summary>
        /// Removes the final comma left by the append helpers before returning
        /// the completed CSV line.
        /// </summary>
        void TrimTrailingComma()
        {
            if (_buf.Length > 0 && _buf[^1] == ',') _buf.Length--;
        }
    }
}
