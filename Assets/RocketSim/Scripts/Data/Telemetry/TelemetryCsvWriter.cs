// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Data/Telemetry/TelemetryCsvWriter.cs
// Purpose: Owns one CSV stream, preserves an existing run file, writes its
// header once, and periodically flushes buffered rows to disk.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RocketSim
{
    internal sealed class TelemetryCsvWriter : IDisposable
    {
        readonly StreamWriter _writer;
        readonly int _flushInterval;
        int _rowCount;

        TelemetryCsvWriter(StreamWriter writer, int flushInterval)
        {
            _writer = writer;
            _flushInterval = flushInterval;
        }

        /// <summary>
        /// Opens a CSV file for append, writes the header for new files, and
        /// warns if an existing run file has a different header.
        /// </summary>
        public static TelemetryCsvWriter Open(string path, string header, int flushInterval)
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
            return new TelemetryCsvWriter(writer, flushInterval);
        }

        /// <summary>
        /// Appends one already-formatted CSV row and flushes at the configured
        /// interval so long runs do not keep too much unwritten data in memory.
        /// </summary>
        public void WriteLine(string line)
        {
            _writer.WriteLine(line);
            _rowCount++;
            if (_flushInterval > 0 && _rowCount % _flushInterval == 0)
                _writer.Flush();
        }

        /// <summary>
        /// Forces buffered CSV rows to disk without closing the writer.
        /// </summary>
        public void Flush() => _writer.Flush();

        /// <summary>
        /// Flushes any buffered CSV rows and closes the underlying stream.
        /// </summary>
        public void Dispose()
        {
            _writer.Flush();
            _writer.Close();
        }
    }
}
