// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingProcessLauncher.cs
// Purpose: Builds the mlagents-learn command, launches it inside the configured
// Conda environment, captures its output, and inspects the communicator port.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Linq;
using UnityEngine;

namespace RocketSim
{
    internal static class TrainingProcessLauncher
    {
        [Serializable]
        sealed class PythonProbeOutput
        {
            public string pythonVersion;
            public string mlAgentsPythonVersion;
            public string pytorchVersion;
            public string cudaRuntimeVersion;
            public bool cudaAvailable;
            public string gpuName;
        }

        /// <summary>
        /// Reads the exact Python, ML-Agents, PyTorch, CUDA, and GPU versions
        /// from the Conda environment that will launch the trainer. Failure is
        /// returned as provenance data so the run can proceed without inventing
        /// version information.
        /// </summary>
        public static TrainingEnvironmentProvenance ProbeEnvironment(string condaEnvironmentName)
        {
            var result = new TrainingEnvironmentProvenance
            {
                condaEnvironmentName = string.IsNullOrWhiteSpace(condaEnvironmentName) ? "unknown" : condaEnvironmentName
            };

            try
            {
                string pythonExecutable = FindCondaPython(condaEnvironmentName);
                result.pythonExecutable = pythonExecutable ?? "unknown";
                if (string.IsNullOrWhiteSpace(pythonExecutable) || !File.Exists(pythonExecutable))
                {
                    result.probeError = $"Could not find Python for Conda environment '{condaEnvironmentName}'.";
                    return result;
                }

                const string probeCode =
                    "import json,platform,importlib.metadata as m,torch;" +
                    "print(json.dumps(dict(" +
                    "pythonVersion=platform.python_version()," +
                    "mlAgentsPythonVersion=m.version('mlagents')," +
                    "pytorchVersion=str(torch.__version__)," +
                    "cudaRuntimeVersion=str(torch.version.cuda or 'none')," +
                    "cudaAvailable=torch.cuda.is_available()," +
                    "gpuName=torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'none')))";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonExecutable,
                        Arguments = $"-c \"{probeCode}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                if (!process.WaitForExit(30_000))
                {
                    process.Kill();
                    result.probeError = "Python environment probe timed out after 30 seconds.";
                    return result;
                }

                string standardOutput = process.StandardOutput.ReadToEnd().Trim();
                string standardError = process.StandardError.ReadToEnd().Trim();
                if (process.ExitCode != 0)
                {
                    result.probeError = string.IsNullOrWhiteSpace(standardError)
                        ? $"Python environment probe exited with code {process.ExitCode}."
                        : standardError;
                    return result;
                }

                PythonProbeOutput probe = JsonUtility.FromJson<PythonProbeOutput>(standardOutput);
                if (probe == null)
                {
                    result.probeError = "Python environment probe returned malformed JSON.";
                    return result;
                }

                result.probeSucceeded = true;
                result.probeError = string.Empty;
                result.pythonVersion = probe.pythonVersion ?? "unknown";
                result.mlAgentsPythonVersion = probe.mlAgentsPythonVersion ?? "unknown";
                result.pytorchVersion = probe.pytorchVersion ?? "unknown";
                result.cudaRuntimeVersion = probe.cudaRuntimeVersion ?? "unknown";
                result.cudaAvailable = probe.cudaAvailable;
                result.gpuName = probe.gpuName ?? "unknown";
                return result;
            }
            catch (Exception ex)
            {
                result.probeError = ex.Message;
                return result;
            }
        }

        /// <summary>Finds the environment's Python executable without activating a shell.</summary>
        static string FindCondaPython(string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentName)) return null;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string executableName = "python.exe";
            string environmentSuffix = Path.Combine("envs", environmentName, executableName);
#else
            string executableName = "python";
            string environmentSuffix = Path.Combine("envs", environmentName, "bin", executableName);
#endif
            string[] roots =
            {
                Path.Combine(home, "Anaconda3"),
                Path.Combine(home, "Miniconda3"),
                Path.Combine(home, "miniconda3"),
                Path.Combine(home, "anaconda3")
            };
            foreach (string root in roots)
            {
                string candidate = Path.Combine(root, environmentSuffix);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Builds the `mlagents-learn` command for the requested run and starts
        /// it inside the configured Conda environment.
        /// </summary>
        public static TrainingProcessHandle LaunchCondaMlAgents(TrainingLaunchRequest request)
        {
            string resumeFlag = request.resumeIfExists ? "--resume" : "--force";
            string torchDevice = string.IsNullOrWhiteSpace(request.torchDevice) ? "cuda" : request.torchDevice;
            string initializeFlag = !request.resumeIfExists && !string.IsNullOrWhiteSpace(request.initializeFromRunId)
                ? $" --initialize-from={request.initializeFromRunId}"
                : string.Empty;
            string trainerArgs = $"-u -m mlagents.trainers.learn {Quote(request.mlConfigFilePath)} " +
                                 $"--run-id={Quote(request.runId)} {resumeFlag} " +
                                 $"--torch-device {Quote(torchDevice)} --seed {Math.Max(0, request.trainerSeed)}{initializeFlag}";

            string pythonExecutable = FindCondaPython(request.condaEnvName);
            if (string.IsNullOrWhiteSpace(pythonExecutable) || !File.Exists(pythonExecutable))
                throw new FileNotFoundException(
                    $"Could not find Python for Conda environment '{request.condaEnvName}'.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = trainerArgs,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            return new TrainingProcessHandle(startInfo, request.trainerLogFilePath);
        }

        /// <summary>
        /// Probes localhost to check whether the ML-Agents trainer communicator
        /// port is accepting connections.
        /// </summary>
        public static bool TcpPortIsListening(int port)
        {
            try
            {
                // A connection probe can be accepted as the trainer's one
                // Unity client. Reading the OS listener table observes
                // readiness without touching the ML-Agents protocol.
                return IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port);
            }
            catch
            {
                return false;
            }
        }

        static string Quote(string value) =>
            $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";
    }
    
    internal struct TrainingLaunchRequest
    {
        public string condaEnvName;
        public string runId;
        public string mlConfigFilePath;
        public bool resumeIfExists;
        public string initializeFromRunId;
        public string torchDevice;
        public int trainerSeed;
        public string trainerLogFilePath;
    }
}
