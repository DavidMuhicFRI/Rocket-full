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

namespace RocketSim
{
    internal static class TrainingProcessLauncher
    {
        /// <summary>
        /// Builds the `mlagents-learn` command for the requested run and starts
        /// it inside the configured Conda environment.
        /// </summary>
        public static Process LaunchCondaMlAgents(TrainingLaunchRequest request)
        {
            string resumeFlag = request.resumeIfExists ? "--resume" : "--force";
            string torchDevice = string.IsNullOrWhiteSpace(request.torchDevice) ? "cuda" : request.torchDevice;
            string args = $"mlagents-learn \"{request.mlConfigFilePath}\" --run-id={request.runId} {resumeFlag} " +
                          $"--torch-device {torchDevice} --seed {Math.Max(0, request.trainerSeed)}";
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
            // /c closes the shell when training ends, so TrainingLauncher can
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
        public string torchDevice;
        public int trainerSeed;
    }
}
