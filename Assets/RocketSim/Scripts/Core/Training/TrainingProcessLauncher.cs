// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingProcessLauncher.cs
// Purpose: Builds the mlagents-learn command, launches it inside the configured
// Conda environment, and probes the local communicator port during startup.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
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
        public static Process LaunchCondaMlAgents(TrainingLaunchRequest request)
        {
            string resumeFlag = request.resumeIfExists ? "--resume" : "--force";
            string torchDevice = string.IsNullOrWhiteSpace(request.torchDevice) ? "cuda" : request.torchDevice;
            string initializeFlag = !request.resumeIfExists && !string.IsNullOrWhiteSpace(request.initializeFromRunId)
                ? $" --initialize-from={request.initializeFromRunId}"
                : string.Empty;
            string args = $"mlagents-learn \"{request.mlConfigFilePath}\" --run-id={request.runId} {resumeFlag} " +
                          $"--torch-device {torchDevice} --seed {Math.Max(0, request.trainerSeed)}{initializeFlag}";
            return LaunchInCondaTerminal(request.condaEnvName, args);
        }

        /// <summary>
        /// Probes localhost to check whether the ML-Agents trainer communicator
        /// port is accepting connections.
        /// </summary>
        public static bool TcpPortIsOpen(int port)
        {
            IAsyncResult result = null;
            try
            {
                using var client = new TcpClient();
                result = client.BeginConnect("127.0.0.1", port, null, null);
                bool completed = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200));
                if (!completed) return false;

                client.EndConnect(result);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                result?.AsyncWaitHandle.Close();
            }
        }

        /// <summary>
        /// Opens a platform-specific terminal command that activates Conda and
        /// runs the generated ML-Agents command.
        /// </summary>
        static Process LaunchInCondaTerminal(string env, string mlAgentsArgs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string activatePath = Path.Combine(userProfile, "Anaconda3", "Scripts", "activate.bat");

            if (!File.Exists(activatePath))
                activatePath = Path.Combine(userProfile, "Miniconda3", "Scripts", "activate.bat");

            string cmd = $"\"{activatePath}\" {env} && {mlAgentsArgs}";

            psi.FileName = "cmd.exe";
            // /c closes the shell when training ends, so TrainingRunController can
            // detect a failed trainer instead of watching an idle /k window.
            psi.Arguments = $"/c \"{cmd}\"";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Normal;

#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            string cmd = $"source ~/anaconda3/bin/activate {env} && {mlAgentsArgs}";
            psi.FileName = "osascript";
            psi.Arguments = $"-e 'tell application \"Terminal\" to do script \"{cmd}\"'";
            psi.UseShellExecute = false;

#else
            string cmd = $"bash -c 'source ~/anaconda3/bin/activate {env} && {mlAgentsArgs}; exec bash'";
            psi.FileName = "gnome-terminal";
            psi.Arguments = $"-- {cmd}";
            psi.UseShellExecute = false;
#endif

            return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");
        }
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
    }
}
