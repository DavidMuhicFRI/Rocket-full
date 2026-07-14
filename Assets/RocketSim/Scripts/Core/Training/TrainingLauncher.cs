// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/TrainingLauncher.cs
// Purpose: Starts training or inference flow and coordinates with the ML-Agents trainer process.
// Main flow: save configs -> launch Python -> wait for port 5004 -> spawn agents.
// Inference skips Python and directly spawns one agent with a saved model.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Collections;
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

        /// <summary>
        /// Starts the requested training or inference workflow.
        /// </summary>
        public void Launch(TrainingAreaManager manager)
        {
            if (manager.envConfig.behaviorType == BehaviorType.Training)
            {
                // We are training
                if (State == TrainingState.Launching || State == TrainingState.Connected) return;
                CommunicatorFactory.Enabled = true;

                runId = manager.envConfig.runId;
                string runRoot = TrainingRunRepository.SaveTrainingConfigs(
                    runId,
                    manager.mlConfig,
                    manager.envConfig,
                    manager.partsConfig,
                    mlConfigPath,
                    envConfigPath,
                    partsConfigPath);
            
                try {
                    _trainerProcess = TrainingProcessLauncher.LaunchCondaMlAgents(new TrainingLaunchRequest
                    {
                        condaEnvName = condaEnvName,
                        runId = runId,
                        mlConfigFilePath = System.IO.Path.Combine(runRoot, mlConfigPath),
                        resumeIfExists = resumeIfExists,
                        torchDevice = "cuda",
                        trainerSeed = manager.mlConfig.trainerSeed
                    });
                    SetState(TrainingState.Launching);
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

        /// <summary>
        /// Waits for the ML-Agents Python trainer to open its communicator port
        /// before spawning training agents that will connect to it.
        /// </summary>
        IEnumerator WaitForPythonAndSpawn(TrainingAreaManager manager)
        {
            float timeout = 45f;
            int port = 5004;

            while (timeout > 0f)
            {
                if (_trainerProcess == null || _trainerProcess.HasExited) {
                    _trainerProcess?.Dispose();
                    _trainerProcess = null;
                    SetState(TrainingState.Failed);
                    yield break;
                }

                if (TrainingProcessLauncher.TcpPortIsOpen(port))
                {
                    UnityEngine.Debug.Log("[TrainingLauncher] Python is ready. Spawning Agents...");
                    manager.SpawnAreas();
                    SetState(TrainingState.Connected);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
                timeout -= 1f;
            }
            StopTraining();
            SetState(TrainingState.Failed);
        }

        /// <summary>
        /// Stops the external trainer process if it is still running.
        /// </summary>
        public void StopTraining()
        {
            if (_trainerProcess != null)
            {
                if (!_trainerProcess.HasExited)
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    // Stop cmd.exe and the Python trainer it started. Killing
                    // only cmd.exe can leave mlagents-learn running in the background.
                    try
                    {
                        using Process treeKiller = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill.exe",
                            Arguments = $"/PID {_trainerProcess.Id} /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        treeKiller?.WaitForExit(3000);
                    }
                    catch
                    {
                        _trainerProcess.Kill();
                    }
#else
                    _trainerProcess.Kill();
#endif
                }
                _trainerProcess.Dispose();
                _trainerProcess = null;
            }
            StopAllCoroutines();
            SetState(TrainingState.Idle);
        }

        /// <summary>
        /// Stops the external trainer process when this launcher or scene is destroyed.
        /// </summary>
        void OnDestroy() => StopTraining();

        /// <summary>
        /// Updates launcher state and notifies the panel so buttons, locking,
        /// and validation messages immediately reflect the new run state.
        /// </summary>
        void SetState(TrainingState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
