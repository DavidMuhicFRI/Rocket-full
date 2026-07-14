// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/RunningStats.cs
// Purpose: Computes a stable running mean and sample standard deviation without
// retaining every telemetry value from an episode in memory.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    internal sealed class RunningStats
    {
        int _count;
        double _mean;
        double _m2;

        public double Mean => _count > 0 ? _mean : 0.0;
        /// <summary>
        /// Returns the sample standard deviation computed from Welford's
        /// accumulated second moment, or zero until at least two samples exist.
        /// </summary>
        public double StdDev => _count > 1 ? Math.Sqrt(_m2 / (_count - 1)) : 0.0;

        /// <summary>
        /// Adds one sample using Welford's online algorithm, avoiding the
        /// precision loss of subtracting two large accumulated sums.
        /// </summary>
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
