// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/TelemetryRunContext.cs
// Purpose: Creates one run's telemetry directory and safe, predictable file
// paths for per-step and per-episode CSV output.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.IO;
using System.Text;

namespace RocketSim
{
    internal readonly struct TelemetryRunContext
    {
        public readonly string DirectoryPath;
        public readonly string RunId;
        public readonly string StepFilePath;
        public readonly string EpisodeFilePath;

        TelemetryRunContext(string directoryPath, string runId)
        {
            DirectoryPath = directoryPath;
            RunId = runId;
            EpisodeFilePath = Path.Combine(directoryPath, $"telemetry_{runId}_episodes.csv");
            StepFilePath = Path.Combine(directoryPath, $"telemetry_{runId}_steps.csv");
        }

        /// <summary>
        /// Creates the output directory and returns the sanitized run id plus
        /// concrete step and episode CSV file paths for that run.
        /// </summary>
        public static TelemetryRunContext Create(string persistentDataPath, string outputFolder, string runId)
        {
            string directoryPath = Path.Combine(persistentDataPath, outputFolder);
            Directory.CreateDirectory(directoryPath);
            return new TelemetryRunContext(directoryPath, SanitizeRunId(runId));
        }

        /// <summary>
        /// Normalizes a user/run supplied id into a filename-safe token while
        /// preserving letters, digits, underscores, and hyphens.
        /// </summary>
        static string SanitizeRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return "unnamed_run";

            var sb = new StringBuilder(runId.Length);
            foreach (char c in runId.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

            return sb.Length > 0 ? sb.ToString() : "unnamed_run";
        }
    }
}
