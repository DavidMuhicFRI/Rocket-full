using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Owns the external ML-Agents process and the trainer-port handshake. It
    /// knows nothing about UI, persistence, or how sessions are edited.
    /// </summary>
    internal sealed class TrainingRunController
    {
        readonly MonoBehaviour _coroutineOwner;
        Process _trainerProcess;
        SimulationAreaHost _areaHost;
        Coroutine _waitForTrainer;

        public event Action Connected;
        public event Action<string> Failed;

        public TrainingRunController(MonoBehaviour coroutineOwner)
        {
            _coroutineOwner = coroutineOwner
                ? coroutineOwner
                : throw new ArgumentNullException(nameof(coroutineOwner));
        }

        /// <summary>Starts Python and waits until agents may connect safely.</summary>
        public void Start(TrainingLaunchRequest request, SimulationAreaHost areaHost)
        {
            Stop();
            _areaHost = areaHost ?? throw new ArgumentNullException(nameof(areaHost));
            _trainerProcess = TrainingProcessLauncher.LaunchCondaMlAgents(request);
            _waitForTrainer = _coroutineOwner.StartCoroutine(WaitForTrainer());
        }

        IEnumerator WaitForTrainer()
        {
            const int port = 5004;
            float timeoutSeconds = 45f;

            while (timeoutSeconds > 0f)
            {
                if (_trainerProcess == null || _trainerProcess.HasExited)
                {
                    Fail("The ML-Agents trainer stopped before opening its communicator port.");
                    yield break;
                }

                if (TrainingProcessLauncher.TcpPortIsOpen(port))
                {
                    UnityEngine.Debug.Log("[TrainingRunController] Python is ready. Spawning agents.");
                    if (!_areaHost.SpawnAreas())
                    {
                        Fail("The simulation areas could not be spawned.");
                        yield break;
                    }
                    Connected?.Invoke();
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
                timeoutSeconds -= 1f;
            }

            Fail("Timed out waiting for the ML-Agents trainer on port 5004.");
        }

        void Fail(string message)
        {
            SimulationAreaHost failedHost = _areaHost;
            Stop();
            failedHost?.CancelPreparedRuntime();
            Failed?.Invoke(message);
        }

        /// <summary>Stops the trainer process tree and all handshake work.</summary>
        public void Stop()
        {
            if (_waitForTrainer != null)
            {
                _coroutineOwner.StopCoroutine(_waitForTrainer);
                _waitForTrainer = null;
            }
            if (_trainerProcess != null)
            {
                if (!_trainerProcess.HasExited)
                    KillProcessTree(_trainerProcess);
                _trainerProcess.Dispose();
                _trainerProcess = null;
            }
            _areaHost = null;
        }

        static void KillProcessTree(Process process)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                using Process treeKiller = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {process.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                treeKiller?.WaitForExit(3000);
            }
            catch
            {
                process.Kill();
            }
#else
            process.Kill();
#endif
        }
    }
}
