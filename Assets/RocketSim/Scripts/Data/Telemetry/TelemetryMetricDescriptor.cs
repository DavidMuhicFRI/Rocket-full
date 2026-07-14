// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/TelemetryMetricDescriptor.cs
// Purpose: Binds one CSV column name to its enable condition and the function
// that reads that numeric value from a TelemetryRow.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    internal readonly struct TelemetryMetricDescriptor
    {
        public readonly string Name;
        readonly Func<TelemetryConfig, bool> _enabled;
        readonly Func<TelemetryRow, float> _read;

        /// <summary>
        /// Binds a CSV column name to the config toggle that enables it and
        /// the reader that extracts its value from a telemetry row.
        /// </summary>
        public TelemetryMetricDescriptor(string name, Func<TelemetryConfig, bool> enabled, Func<TelemetryRow, float> read)
        {
            Name = name;
            _enabled = enabled;
            _read = read;
        }

        /// <summary>
        /// Returns whether this metric should be included for the current
        /// telemetry configuration.
        /// </summary>
        public bool IsEnabled(TelemetryConfig cfg) => _enabled(cfg);
        /// <summary>
        /// Extracts this metric's numeric value from one telemetry row.
        /// </summary>
        public float Read(TelemetryRow row) => _read(row);
    }
}
