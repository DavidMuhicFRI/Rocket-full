// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/EpisodeTelemetryAccumulator.cs
// Purpose: Owns one RunningStats bucket per enabled metric for one area and
// episode, producing compact episode mean and standard-deviation data.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace RocketSim
{
    internal sealed class EpisodeTelemetryAccumulator
    {
        public readonly int AreaIndex;
        public readonly int Episode;
        public readonly RunningStats[] Stats;
        public int StepCount { get; private set; }

        /// <summary>
        /// Allocates one running-stat bucket per enabled telemetry metric for a
        /// single area/episode pair.
        /// </summary>
        public EpisodeTelemetryAccumulator(int areaIndex, int episode, int metricCount)
        {
            AreaIndex = areaIndex;
            Episode = episode;
            Stats = new RunningStats[metricCount];
            for (int i = 0; i < Stats.Length; i++) Stats[i] = new RunningStats();
        }

        /// <summary>
        /// Reads the enabled metrics from one telemetry row and updates the
        /// episode-level running mean and standard deviation inputs.
        /// </summary>
        public void Add(TelemetryRow row, IReadOnlyList<TelemetryMetricDescriptor> metrics)
        {
            for (int i = 0; i < metrics.Count && i < Stats.Length; i++)
                Stats[i].Add(metrics[i].Read(row));

            StepCount++;
        }
    }
}
