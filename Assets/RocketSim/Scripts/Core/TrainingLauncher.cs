using System;
using System.Diagnostics;
using System.IO;
using System.Collections;
using System.Net.Sockets;
using Unity.MLAgents;
using UnityEngine;

namespace RocketSim
{
    public class TrainingLauncher : MonoBehaviour
    {
        [Header("Anaconda Settings")]
        public string condaEnvName = "mlagents_gpu";
        public string runId = "FalconRun";
        public string mlConfigPath = "TrainingConfig.yaml";
        public string partsConfigPath = "PartsConfig.json";
        public string envConfigPath = "EnvConfig.json";
        public bool resumeIfExists = false;

        public enum TrainingState { Idle, Launching, Connected, Failed }
        public TrainingState State { get; private set; } = TrainingState.Idle;
        public event Action<TrainingState> OnStateChanged;

        Process _trainerProcess;

        public void Launch(TrainingAreaManager manager)
        {
            if (manager.envConfig.behaviorType == BehaviorType.Training)
            {
                // We are training
                if (State == TrainingState.Launching || State == TrainingState.Connected) return;
                CommunicatorFactory.Enabled = true;

                runId = manager.envConfig.runId;
                // 1. Write YAML
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId));
                if(!Directory.Exists(projectRoot)) Directory.CreateDirectory(projectRoot);
                
                // Save ML config
                string ml = Path.Combine(projectRoot, mlConfigPath);
                File.WriteAllText(ml, manager.mlConfig.ToYAML());

                // Save env config
                string env = Path.Combine(projectRoot, envConfigPath);
                File.WriteAllText(env, JsonUtility.ToJson(manager.envConfig));

                // Save parts config
                string parts = Path.Combine(projectRoot, partsConfigPath);
                File.WriteAllText(parts, JsonUtility.ToJson(manager.partsConfig));

                // 2. Start Python
                string resumeFlag = resumeIfExists ? "--resume" : "--force";
                string args = $"mlagents-learn \"{ml}\" --run-id={runId} {resumeFlag} --torch-device cuda";
            
                try {
                    _trainerProcess = LaunchInCondaTerminal(condaEnvName, args);
                    SetState(TrainingState.Launching);
                    // 3. Wait for port BEFORE spawning agents
                    StartCoroutine(WaitForPythonAndSpawn(manager));
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogError($"Launch Failed: {e.Message}");
                    SetState(TrainingState.Failed);
                }
            }
            else
            {
                CommunicatorFactory.Enabled = false;
                TelemetryLogger.Instance?.DisableLogging();
                runId = manager.envConfig.runId;
                manager.SpawnAreas();
            }
        }

        IEnumerator WaitForPythonAndSpawn(TrainingAreaManager manager)
        {
            float timeout = 45f;
            int port = 5004;

            while (timeout > 0f)
            {
                if (_trainerProcess == null || _trainerProcess.HasExited) {
                    SetState(TrainingState.Failed);
                    yield break;
                }

                if (TcpPortIsOpen(port))
                {
                    UnityEngine.Debug.Log("[TrainingLauncher] Python is ready. Spawning Agents...");
                    manager.SpawnAreas();
                    SetState(TrainingState.Connected);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
                timeout -= 1f;
            }
            SetState(TrainingState.Failed);
        }

        bool TcpPortIsOpen(int port)
        {
            try {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200));
                return success;
            } catch { return false; }
        }

        public void StopTraining()
        {
            if (_trainerProcess != null && !_trainerProcess.HasExited)
            {
                _trainerProcess.Kill();
                _trainerProcess.Dispose();
                _trainerProcess = null;
                SetState(TrainingState.Idle);
            }
        }

        void OnDestroy() => StopTraining();

        static Process LaunchInCondaTerminal(string env, string mlAgentsArgs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // 1. Resolve path to Anaconda or Miniconda
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string activatePath = Path.Combine(userProfile, "Anaconda3", "Scripts", "activate.bat");
    
            if (!File.Exists(activatePath))
                activatePath = Path.Combine(userProfile, "Miniconda3", "Scripts", "activate.bat");

            // 2. Setup CMD to activate conda and run mlagents
            // /k keeps the window open so you can see errors if the trainer fails to start
            string cmd = $"\"{activatePath}\" {env} && {mlAgentsArgs}";
    
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/k \"{cmd}\"";
            psi.UseShellExecute = true; 
            psi.WindowStyle = ProcessWindowStyle.Normal;

#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    string cmd = $"source ~/anaconda3/bin/activate {env} && {mlAgentsArgs}";
    psi.FileName = "osascript";
    psi.Arguments = $"-e 'tell application \"Terminal\" to do script \"{cmd}\"'";
    psi.UseShellExecute = false;

#else
    // Linux (Gnome Terminal)
    string cmd = $"bash -c 'source ~/anaconda3/bin/activate {env} && {mlAgentsArgs}; exec bash'";
    psi.FileName = "gnome-terminal";
    psi.Arguments = $"-- {cmd}";
    psi.UseShellExecute = false;
#endif

            return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");
        }

        void SetState(TrainingState s) { State = s; OnStateChanged?.Invoke(s); }
    }
}
