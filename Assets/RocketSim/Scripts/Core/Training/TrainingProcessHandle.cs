// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingProcessHandle.cs
// Purpose: Owns one Python trainer process and preserves its console output.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RocketSim
{
    /// <summary>
    /// Keeps the process and its log writer together. ML-Agents failures used
    /// to disappear with the external console window; the most recent output
    /// is now available both on disk and to the Unity failure message.
    /// </summary>
    internal sealed class TrainingProcessHandle : IDisposable
    {
        const int RememberedLineCount = 40;

        readonly object _logLock = new();
        readonly Queue<string> _recentLines = new();
        readonly StreamWriter _logWriter;
        readonly Process _process;
        bool _disposed;

        public string LogFilePath { get; }
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;

        public TrainingProcessHandle(ProcessStartInfo startInfo, string logFilePath)
        {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("A trainer log path is required.", nameof(logFilePath));

            LogFilePath = Path.GetFullPath(logFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath) ?? ".");
            _logWriter = new StreamWriter(LogFilePath, append: false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            _process.OutputDataReceived += CaptureLine;
            _process.ErrorDataReceived += CaptureLine;

            WriteLine($"[RocketSim] Trainer started at {DateTime.UtcNow:O}");
            WriteLine($"[RocketSim] Working directory: {startInfo.WorkingDirectory}");
            WriteLine($"[RocketSim] Command: {startInfo.FileName} {startInfo.Arguments}");

            try
            {
                if (!_process.Start())
                    throw new InvalidOperationException("The trainer process did not start.");
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        void CaptureLine(object sender, DataReceivedEventArgs args)
        {
            if (args.Data != null)
                WriteLine(args.Data);
        }

        void WriteLine(string line)
        {
            lock (_logLock)
            {
                if (_disposed) return;

                _logWriter.WriteLine(line);
                _recentLines.Enqueue(line);
                while (_recentLines.Count > RememberedLineCount)
                    _recentLines.Dequeue();
            }
        }

        /// <summary>
        /// Waits for redirected output callbacks after process exit. This is
        /// called only after HasExited is true, so it does not stall gameplay.
        /// </summary>
        public void FinishReadingOutput()
        {
            if (!_process.HasExited) return;
            _process.WaitForExit();
        }

        /// <summary>Returns a compact diagnostic tail for the Unity Console.</summary>
        public string ReadRecentOutput()
        {
            lock (_logLock)
                return string.Join(Environment.NewLine, _recentLines);
        }

        /// <summary>Stops Python and any worker processes it created.</summary>
        public void TerminateProcessTree()
        {
            if (_process.HasExited) return;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                using Process treeKiller = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {_process.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                treeKiller?.WaitForExit(3000);
            }
            catch
            {
                _process.Kill();
            }
#else
            _process.Kill();
#endif
        }

        public void Dispose()
        {
            lock (_logLock)
            {
                if (_disposed) return;
                _disposed = true;
                _process.OutputDataReceived -= CaptureLine;
                _process.ErrorDataReceived -= CaptureLine;
                _logWriter.Dispose();
                _process.Dispose();
            }
        }
    }
}
