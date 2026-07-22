// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Telemetry.cs
// Purpose: Builds telemetry logging controls for the right-side configuration panel.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>
        /// Builds telemetry group toggles, column-count estimates, and the persistent output-folder action.
        /// </summary>
        void BuildTelemetryTab(VisualElement c)
        {
            c.Clear();
            var tc = telemetryConfig ?? new TelemetryConfig();

            BuildTelemetryLogGroups(c, tc);
            BuildTelemetrySamplingSection(c, tc);
            BuildTelemetryWidthSection(c, tc);
            BuildTelemetryOutputSection(c);
        }

        /// <summary>
        /// Controls only detailed training trajectories. Episode statistics are
        /// always accumulated at full physics rate and evaluation trajectories
        /// deliberately ignore this interval and log every step.
        /// </summary>
        void BuildTelemetrySamplingSection(VisualElement root, TelemetryConfig tc)
        {
            root.Add(UIHelper.SectionLabel("Trajectory Sampling"));
            root.Add(UIHelper.IntSlider(
                "Training Step Interval",
                tc.TrainingStepLogInterval,
                1,
                100,
                value =>
                {
                    tc.trainingStepLogInterval = value;
                    RefreshTelemetryColumnCount(root, tc);
                },
                "1 writes every physics step; 10 writes at 10 Hz with the current 0.01 s timestep. Episode summaries and evaluation stay full-rate."));
        }

        /// <summary>
        /// Builds one toggle per metric family and briefly lists the measurements
        /// included by that family. Identity columns cannot be disabled.
        /// </summary>
        void BuildTelemetryLogGroups(VisualElement root, TelemetryConfig tc)
        {
            root.Add(UIHelper.SectionLabel("Log Groups"));
            root.Add(UIHelper.ReadOnly("Identity", "Always on - WallTime - Area - Episode - Step"));

            root.Add(UIHelper.Toggle("Goal / Position Metrics", tc.logGoalMetrics, v => { tc.logGoalMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("Goal distance 3D - planar distance - vertical error - altitude"));

            root.Add(UIHelper.Toggle("Attitude / Rotation Metrics", tc.logAttitudeMetrics, v => { tc.logAttitudeMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("Tilt - uprightness - signed roll/pitch - angular rate - tilt rate - angle of attack"));

            root.Add(UIHelper.Toggle("Velocity Metrics", tc.logVelocityMetrics, v => { tc.logVelocityMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("3D speed - planar speed - vertical speed - goal closure rate - air-relative speed"));

            root.Add(UIHelper.Toggle("Control / Fuel Metrics", tc.logControlMetrics, v => { tc.logControlMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("Throttle mean/max - gimbal effort - fin effort - fuel fraction - fuel used"));

            root.Add(UIHelper.Toggle("Aero / Load Metrics", tc.logAeroLoadMetrics, v => { tc.logAeroLoadMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("G force - angular acceleration - dynamic pressure - heat flux - structural stress"));

            root.Add(UIHelper.Toggle("Environment Metrics", tc.logEnvironmentMetrics, v => { tc.logEnvironmentMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("Wind speed - planar wind speed - wind alignment - normalized dynamic pressure"));

            root.Add(UIHelper.Toggle("Reward Metrics", tc.logRewardMetrics, v => { tc.logRewardMetrics = v; RefreshTelemetryColumnCount(root, tc); }));
            root.Add(BuildGroupDetail("Scalar reward accumulated this step"));
        }

        /// <summary>
        /// Adds live column-count and approximate storage readouts so a user can
        /// judge logging cost before starting a long experiment.
        /// </summary>
        void BuildTelemetryWidthSection(VisualElement root, TelemetryConfig tc)
        {
            root.Add(UIHelper.SectionLabel("Estimated CSV Width"));
            var colCountLabel = UIHelper.ReadOnly("Columns", "");
            colCountLabel.name = "label-col-count";
            root.Add(colCountLabel);
            var sizeLabel = UIHelper.ReadOnly("Approx. / 1M rows", "");
            sizeLabel.name = "label-csv-size";
            root.Add(sizeLabel);
            RefreshTelemetryColumnCount(root, tc);
        }

        /// <summary>
        /// Shows the two output filename patterns and provides shortcuts to the
        /// telemetry and ML-Agents results directories.
        /// </summary>
        void BuildTelemetryOutputSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Output"));
            string outputDir = Path.Combine(Application.persistentDataPath, "Telemetry");
            string displayPath = outputDir + Path.DirectorySeparatorChar +
                                 "telemetry_<run_id>_episodes.csv / telemetry_<run_id>_steps.csv";

            var pathLabel = new Label(displayPath);
            pathLabel.AddToClassList("rs-section-desc");
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            pathLabel.style.marginLeft = 8;
            pathLabel.style.marginBottom = 6;
            root.Add(pathLabel);

            root.Add(UIHelper.ActionButton("Open Folder", () =>
            {
                Directory.CreateDirectory(outputDir);
                Application.OpenURL(new Uri(outputDir).AbsoluteUri);
            }));
            root.Add(UIHelper.ActionButton("Open Training Results", () =>
            {
                string results = TrainingRunRepository.GetResultsRoot();
                Directory.CreateDirectory(results);
                Application.OpenURL(new Uri(results).AbsoluteUri);
            }));
        }

        /// <summary>
        /// Recalculates step and episode CSV column counts from enabled telemetry groups.
        /// </summary>
        void RefreshTelemetryColumnCount(VisualElement root, TelemetryConfig tc)
        {
            int metricCols = TelemetryMetricCatalog.CountEnabled(tc);
            int stepCols = 4 + metricCols;
            const int episodeIdentityAndOutcomeCols = 40;
            int episodeCols = episodeIdentityAndOutcomeCols + metricCols * 2;
            UpdateLabelText(root, "label-col-count",
                $"Steps: {stepCols} cols - Episodes: {episodeCols} cols");
            // A typical CSV number plus comma is roughly 12 bytes. This is an
            // estimate, not a promise, but is useful before starting a long run.
            float estimatedMegabytes = stepCols * 12f * 1_000_000f /
                                       tc.TrainingStepLogInterval /
                                       (1024f * 1024f);
            UpdateLabelText(
                root,
                "label-csv-size",
                $"~{estimatedMegabytes:F0} MB per 1M simulated physics steps at 1/{tc.TrainingStepLogInterval} sampling");
        }
    }
}
