using System;
using System.Collections;
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
        TrainingProcessHandle _trainerProcess;
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
        internal void Start(TrainingLaunchRequest request, SimulationAreaHost areaHost)
        {
            Stop();
            _areaHost = areaHost ?? throw new ArgumentNullException(nameof(areaHost));
            _trainerProcess = TrainingProcessLauncher.LaunchCondaMlAgents(request);
            UnityEngine.Debug.Log(
                $"[TrainingRunController] Python started. Trainer output: {_trainerProcess.LogFilePath}");
            _waitForTrainer = _coroutineOwner.StartCoroutine(WaitForTrainer());
        }

        IEnumerator WaitForTrainer()
        {
            const int port = 5004;
            float timeoutSeconds = 45f;

            while (timeoutSeconds > 0f)
            {
                if (_trainerProcess == null)
                {
                    Fail("The ML-Agents trainer process is unavailable.");
                    yield break;
                }
                if (_trainerProcess.HasExited)
                {
                    Fail(DescribeTrainerExit(
                        "The ML-Agents trainer stopped before opening its communicator port."));
                    yield break;
                }

                if (TrainingProcessLauncher.TcpPortIsListening(port))
                {
                    UnityEngine.Debug.Log("[TrainingRunController] Python is ready. Spawning agents.");
                    if (!_areaHost.SpawnAreas())
                    {
                        Fail("The simulation areas could not be spawned.");
                        yield break;
                    }
                    Connected?.Invoke();
                    _waitForTrainer = _coroutineOwner.StartCoroutine(MonitorTrainer());
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
                timeoutSeconds -= 1f;
            }

            Fail("Timed out waiting for the ML-Agents trainer on port 5004.");
        }

        /// <summary>
        /// Keeps watching Python after the initial handshake. A trainer error
        /// is now reported with its exit code, log path, and recent output.
        /// </summary>
        IEnumerator MonitorTrainer()
        {
            while (_trainerProcess != null && !_trainerProcess.HasExited)
                yield return new WaitForSecondsRealtime(1f);

            if (_trainerProcess == null)
                yield break;

            Fail(DescribeTrainerExit("The ML-Agents trainer exited unexpectedly."));
        }

        string DescribeTrainerExit(string summary)
        {
            _trainerProcess.FinishReadingOutput();
            string recentOutput = _trainerProcess.ReadRecentOutput();
            string details = string.IsNullOrWhiteSpace(recentOutput)
                ? string.Empty
                : $"\nRecent trainer output:\n{recentOutput}";
            return $"{summary} Exit code: {_trainerProcess.ExitCode}. " +
                   $"Full log: {_trainerProcess.LogFilePath}{details}";
        }

        void Fail(string message)
        {
            SimulationAreaHost failedHost = _areaHost;
            Stop();
            // The same path handles startup failures and a trainer that dies
            // after areas were spawned. It always clears the frozen runtime
            // and restores the editable preview.
            failedHost?.StopActiveRunAndShowPreview();
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
                    _trainerProcess.TerminateProcessTree();
                else
                    _trainerProcess.FinishReadingOutput();
                _trainerProcess.Dispose();
                _trainerProcess = null;
            }
            _areaHost = null;
        }

    }
}
