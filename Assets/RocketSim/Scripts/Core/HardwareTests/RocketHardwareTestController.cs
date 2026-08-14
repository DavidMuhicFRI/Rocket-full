// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/RocketHardwareTestController.cs
// Purpose: Orchestrates active hardware tests and provides shared base logic for test cases.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public partial class HardwareTestController
    {
        /// <summary>
        /// Selects a hardware test and resets controller status before an agent
        /// has been spawned for the test run.
        /// </summary>
        public void Prepare(RocketHardwareTestType testType)
        {
            Agent = null;
            ActiveTest = testType;
            _activeTestCase = CreateTestCase(testType);
            Outcome = RocketHardwareTestOutcome.Idle;
            _nextStatusUpdateTime = 0f;
            Status = $"Preparing {DisplayName(testType)}...";
        }
        /// <summary>
        /// Marks the selected test as failed before it starts when setup cannot
        /// provide a valid agent or configuration.
        /// </summary>
        public void FailToStart(RocketHardwareTestType testType, string reason)
        {
            Agent = null;
            ActiveTest = testType;
            _activeTestCase = null;
            Outcome = RocketHardwareTestOutcome.Failed;
            Status = reason;
        }
        /// <summary>
        /// Starts the selected hardware test by placing the active agent in a
        /// purpose-built initial state and resetting all test metrics.
        /// </summary>
        public void Begin(RocketHardwareTestType testType, FalconAgent agent)
        {
            Agent = agent;
            ActiveTest = testType;
            _activeTestCase = CreateTestCase(testType);
            Outcome = RocketHardwareTestOutcome.Running;
            _elapsed = 0f;
            _nextStatusUpdateTime = 0f;

            if (!Agent || !Agent.rb)
            {
                Complete(RocketHardwareTestOutcome.Failed, "Hardware test failed: no active rocket agent was spawned.");
                return;
            }

            RocketPhysicsConfig cfg = Agent.assembly
                ? Agent.assembly.GetPhysicsConfig()
                : RocketAssembly.Falcon9StaticFallback;

            Vector3 position;
            Quaternion rotation;
            Vector3 velocity;
            Vector3 angularVelocity = Vector3.zero;

            switch (testType)
            {
                case RocketHardwareTestType.FinAxisX:
                case RocketHardwareTestType.FinAxisY:
                case RocketHardwareTestType.FinAxisZ:
                    position = new Vector3(0f, Mathf.Max(5000f, cfg.length * 100f), 0f);
                    rotation = Quaternion.identity;
                    velocity = Vector3.down * 260f;
                    break;
                case RocketHardwareTestType.Aerodynamics:
                    position = new Vector3(0f, Mathf.Max(3500f, cfg.length * 85f), 0f);
                    rotation = Quaternion.Euler(8f, 0f, -6f);
                    velocity = new Vector3(90f, -180f, 35f);
                    break;
                case RocketHardwareTestType.Landing:
                    position = new Vector3(0f, Mathf.Max(1200f, cfg.length * 25f), 0f);
                    rotation = Quaternion.identity;
                    velocity = Vector3.down * 85f;
                    break;
                case RocketHardwareTestType.RcsVacuum:
                    position = new Vector3(0f, Mathf.Max(120000f, cfg.length * 2500f), 0f);
                    rotation = Quaternion.Euler(10f, 20f, -8f);
                    velocity = Vector3.zero;
                    break;
                case RocketHardwareTestType.RcsSeparation:
                    position = new Vector3(0f, Mathf.Max(85000f, cfg.length * 2100f), 0f);
                    rotation = Quaternion.identity;
                    velocity = new Vector3(1450f, 180f, 180f);
                    angularVelocity = new Vector3(0.04f, -0.03f, 0.05f);
                    break;
                case RocketHardwareTestType.TopStabilizers:
                    position = new Vector3(0f, Mathf.Max(140f, cfg.length * 3f), 0f);
                    rotation = Quaternion.Euler(6f, 0f, -6f);
                    velocity = new Vector3(0f, -8f, 0f);
                    break;
                default:
                    position = new Vector3(0f, BaseGroundClearance, 0f);
                    rotation = Quaternion.identity;
                    velocity = Vector3.zero;
                    break;
            }

            Agent.ResetForHardwareTest(position, rotation, velocity, angularVelocity);
            ResizeCommandBuffers();
            ClearCommandBuffers();

            cfg = Agent.CurrentPhysicsConfig;
            _fuelStart = Agent.GetFuel;
            _initialAltitude = Agent.transform.localPosition.y;
            _maxAltitude = _initialAltitude;
            _maxVerticalSpeed = Agent.rb.linearVelocity.y;
            _maxAngularRateDegS = 0f;
            _maxTargetAxisRateDegS = 0f;
            _maxCrossAxisRateDegS = 0f;
            _maxLocalRateXDegS = 0f;
            _maxLocalRateYDegS = 0f;
            _maxLocalRateZDegS = 0f;
            _maxDynamicPressure = 0f;
            _initialVerticalSpeed = Agent.rb.linearVelocity.y;
            _initialLateralSpeed = HorizontalSpeed();
            _minLateralSpeed = _initialLateralSpeed;
            _initialHorizontalDistance = HorizontalDistanceToPad();
            _minHorizontalDistance = _initialHorizontalDistance;
            _startTiltDeg = Vector3.Angle(Agent.transform.up, Vector3.up);
            _minTiltDeg = _startTiltDeg;
            _twr = ComputeTwr(cfg);
            _targetLocalAxis = TargetAxisFor(testType);
            _initialAngularRateDegS = Agent.rb.angularVelocity.magnitude * Mathf.Rad2Deg;
            _separationTargetUp = ComputeSeparationTargetUp(Agent.rb.linearVelocity);
            _separationInitialPointingErrorDeg = Vector3.Angle(Agent.transform.up, _separationTargetUp);
            _minSeparationPointingErrorDeg = _separationInitialPointingErrorDeg;
            _finalSeparationPointingErrorDeg = _separationInitialPointingErrorDeg;
            _finalSeparationAngularRateDegS = _initialAngularRateDegS;

            if (IsFinTest(testType) && !cfg.hasFins)
            {
                Complete(RocketHardwareTestOutcome.Failed, $"{DisplayName(testType)} skipped: grid fins are disabled in the current rocket config.");
                return;
            }

            if ((testType == RocketHardwareTestType.TopStabilizers ||
                 testType == RocketHardwareTestType.RcsVacuum ||
                 testType == RocketHardwareTestType.RcsSeparation) && !cfg.hasRCS)
            {
                Complete(RocketHardwareTestOutcome.Failed, $"{DisplayName(testType)} skipped: RCS thrusters are disabled in the current rocket config.");
                return;
            }

            _activeTestCase?.Prepare(agent);
            Status = $"{DisplayName(testType)} running...";
        }
        /// <summary>
        /// Stops the active hardware test, clears manual controls and RCS visuals,
        /// and returns the controller to idle state.
        /// </summary>
        public void Stop()
        {
            _activeTestCase?.Stop();
            Agent?.ClearManualControl();
            ClearAgentRcsVisuals();
            _activeTestCase = null;
            Outcome = RocketHardwareTestOutcome.Idle;
            Status = "Hardware test stopped.";
        }
        /// <summary>
        /// Runs physics-step simulation logic at Unity fixed timestep intervals.
        /// </summary>
        void FixedUpdate()
        {
            if (Outcome != RocketHardwareTestOutcome.Running || !Agent) return;

            _elapsed += Time.fixedDeltaTime;
            UpdateMetrics();

            if (_activeTestCase == null)
            {
                Complete(RocketHardwareTestOutcome.Failed, $"Hardware test failed: no test case exists for {DisplayName(ActiveTest)}.");
                return;
            }

            _activeTestCase.Step(Time.fixedDeltaTime);

            if (Outcome == RocketHardwareTestOutcome.Running && _elapsed >= _nextStatusUpdateTime)
            {
                UpdateRunningStatus();
                _nextStatusUpdateTime = _elapsed + 0.2f;
            }
        }
        /// <summary>
        /// Updates hardware-test peak and minimum metrics from the active agent state.
        /// </summary>
        void UpdateMetrics()
        {
            if (!Agent || !Agent.rb) return;

            _maxAltitude = Mathf.Max(_maxAltitude, Agent.transform.localPosition.y);
            _maxVerticalSpeed = Mathf.Max(_maxVerticalSpeed, Agent.rb.linearVelocity.y);
            _maxDynamicPressure = Mathf.Max(_maxDynamicPressure, Agent.DynamicPressure);

            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;
            _maxAngularRateDegS = Mathf.Max(_maxAngularRateDegS, localRate.magnitude);
            _maxLocalRateXDegS = Mathf.Max(_maxLocalRateXDegS, Mathf.Abs(localRate.x));
            _maxLocalRateYDegS = Mathf.Max(_maxLocalRateYDegS, Mathf.Abs(localRate.y));
            _maxLocalRateZDegS = Mathf.Max(_maxLocalRateZDegS, Mathf.Abs(localRate.z));
            if (_targetLocalAxis.sqrMagnitude > 0.1f)
            {
                Vector3 axis = _targetLocalAxis.normalized;
                float targetRate = Mathf.Abs(Vector3.Dot(localRate, axis));
                float crossRate = (localRate - axis * Vector3.Dot(localRate, axis)).magnitude;
                _maxTargetAxisRateDegS = Mathf.Max(_maxTargetAxisRateDegS, targetRate);
                _maxCrossAxisRateDegS = Mathf.Max(_maxCrossAxisRateDegS, crossRate);
            }
            if (ActiveTest == RocketHardwareTestType.RcsSeparation && _separationTargetUp.sqrMagnitude > 0.1f)
            {
                _finalSeparationPointingErrorDeg = Vector3.Angle(Agent.transform.up, _separationTargetUp);
                _minSeparationPointingErrorDeg = Mathf.Min(
                    _minSeparationPointingErrorDeg,
                    _finalSeparationPointingErrorDeg);
                _finalSeparationAngularRateDegS = localRate.magnitude;
            }
            _minTiltDeg = Mathf.Min(_minTiltDeg, Vector3.Angle(Agent.transform.up, Vector3.up));
            _minLateralSpeed = Mathf.Min(_minLateralSpeed, HorizontalSpeed());
            _minHorizontalDistance = Mathf.Min(_minHorizontalDistance, HorizontalDistanceToPad());
        }
        /// <summary>
        /// Rebuilds the live status string shown while a hardware test is running.
        /// </summary>
        void UpdateRunningStatus()
        {
            if (ActiveTest == RocketHardwareTestType.RcsSeparation)
            {
                Status = $"{DisplayName(ActiveTest)} { _elapsed:F1}s | " +
                         $"target {_finalSeparationPointingErrorDeg:F1} deg | " +
                         $"rate {_finalSeparationAngularRateDegS:F1} deg/s | " +
                         $"q {Agent.DynamicPressure:F1} Pa";
                return;
            }

            Status = $"{DisplayName(ActiveTest)} { _elapsed:F1}s | " +
                     $"alt {Agent.transform.localPosition.y:F1} m | " +
                     $"vY {Agent.rb.linearVelocity.y:F1} m/s | " +
                     $"q {Agent.DynamicPressure:F0} Pa | " +
                     $"axis {_maxTargetAxisRateDegS:F1} deg/s";
        }
        /// <summary>
        /// Returns the active agent's horizontal speed in the local test frame.
        /// </summary>
        float HorizontalSpeed()
        {
            if (!Agent || !Agent.rb) return 0f;
            Vector3 velocity = Agent.rb.linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }
        /// <summary>
        /// Returns the active agent's horizontal distance from the test pad origin.
        /// </summary>
        float HorizontalDistanceToPad()
        {
            if (!Agent) return 0f;
            Vector3 position = Agent.transform.localPosition;
            position.y = 0f;
            return position.magnitude;
        }
        /// <summary>
        /// Finishes the active test with an outcome and visible status message,
        /// then clears manual actuator commands.
        /// </summary>
        void Complete(RocketHardwareTestOutcome outcome, string status)
        {
            Agent?.ClearManualControl();
            ClearAgentRcsVisuals();
            Outcome = outcome;
            Status = status;
        }
        /// <summary>
        /// Clears any visible RCS plumes left by scripted hardware-test commands.
        /// </summary>
        void ClearAgentRcsVisuals()
        {
            if (Agent && Agent.assembly && Agent.assembly.rcs)
                Agent.assembly.rcs.ClearVisuals();
        }
        /// <summary>
        /// Resizes scripted command buffers to match the current agent hardware.
        /// </summary>
        void ResizeCommandBuffers()
        {
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;
            int engineCount = Mathf.Max(1, cfg.independentEngineCount);
            int finCount = Mathf.Max(0, cfg.finCount);
            int rcsCount = cfg.hasRCS ? Mathf.Max(0, cfg.rcsJetCount) : 0;

            if (_throttle == null || _throttle.Length != engineCount) _throttle = new float[engineCount];
            if (_gimbal == null || _gimbal.Length != engineCount) _gimbal = new Vector2[engineCount];
            if (_fins == null || _fins.Length != finCount) _fins = new float[finCount];
            if (_rcs == null || _rcs.Length != rcsCount) _rcs = new float[rcsCount];
        }
        /// <summary>
        /// Resets scripted throttle, gimbal, fin, and RCS command buffers to zero.
        /// </summary>
        void ClearCommandBuffers()
        {
            if (_throttle != null)
                for (int i = 0; i < _throttle.Length; i++) _throttle[i] = 0f;

            if (_gimbal != null)
                for (int i = 0; i < _gimbal.Length; i++) _gimbal[i] = Vector2.zero;

            if (_fins != null)
                for (int i = 0; i < _fins.Length; i++) _fins[i] = 0f;

            if (_rcs != null)
                for (int i = 0; i < _rcs.Length; i++) _rcs[i] = 0f;
        }
        /// <summary>
        /// Computes thrust-to-weight ratio from current mass, fuel, thrust, and active engine count.
        /// </summary>
        float ComputeTwr(RocketPhysicsConfig cfg)
        {
            float mass = Mathf.Max(1f, cfg.dryMass + Agent.GetFuel);
            float totalThrust = cfg.maxThrust * ActiveEngineCount();
            return totalThrust / (mass * 9.80665f);
        }
        /// <summary>
        /// Counts active engine transforms for TWR checks, falling back to the
        /// physics config when the scene hierarchy is unavailable.
        /// </summary>
        int ActiveEngineCount()
        {
            if (!Agent || !Agent.assembly || !Agent.assembly.thrusters)
                return Mathf.Max(1, Agent.CurrentPhysicsConfig.independentEngineCount);

            int count = 0;
            Transform parent = Agent.assembly.thrusters.transform;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).gameObject.activeSelf)
                    count++;
            }

            return Mathf.Max(1, count);
        }
        /// <summary>
        /// Returns the human-readable label shown in hardware-test status text.
        /// </summary>
        static string DisplayName(RocketHardwareTestType testType)
        {
            return testType switch
            {
                RocketHardwareTestType.FinAxisX => "Fin X-axis rotation",
                RocketHardwareTestType.FinAxisY => "Fin Y-axis spin",
                RocketHardwareTestType.FinAxisZ => "Fin Z-axis rotation",
                RocketHardwareTestType.Aerodynamics => "Aerodynamics",
                RocketHardwareTestType.Landing => "Landing",
                RocketHardwareTestType.RcsVacuum => "RCS vacuum",
                RocketHardwareTestType.RcsSeparation => "Stage separation RCS",
                RocketHardwareTestType.TopStabilizers => "Top stabilizers",
                RocketHardwareTestType.Thrusters => "Engine static",
                _ => "Hardware test"
            };
        }
        /// <summary>
        /// Returns whether a hardware test targets grid-fin axis response.
        /// </summary>
        static bool IsFinTest(RocketHardwareTestType testType)
        {
            return testType == RocketHardwareTestType.FinAxisX ||
                   testType == RocketHardwareTestType.FinAxisY ||
                   testType == RocketHardwareTestType.FinAxisZ;
        }
        /// <summary>
        /// Returns the local rotation axis measured by fin-axis hardware tests.
        /// </summary>
        static Vector3 TargetAxisFor(RocketHardwareTestType testType)
        {
            return testType switch
            {
                RocketHardwareTestType.FinAxisX => Vector3.right,
                RocketHardwareTestType.FinAxisY => Vector3.up,
                RocketHardwareTestType.FinAxisZ => Vector3.forward,
                _ => Vector3.zero
            };
        }

        /// <summary>
        /// Creates the small adapter that connects a test type to its scripted
        /// drive method while sharing metrics and command buffers in this controller.
        /// </summary>
        IRocketHardwareTest CreateTestCase(RocketHardwareTestType testType)
        {
            return testType switch
            {
                RocketHardwareTestType.FinAxisX or
                RocketHardwareTestType.FinAxisY or
                RocketHardwareTestType.FinAxisZ => new FinAxisHardwareTestCase(this),
                RocketHardwareTestType.Aerodynamics => new AerodynamicsHardwareTestCase(this),
                RocketHardwareTestType.Landing => new LandingHardwareTestCase(this),
                RocketHardwareTestType.RcsVacuum => new RcsVacuumHardwareTestCase(this),
                RocketHardwareTestType.RcsSeparation => new RcsSeparationHardwareTestCase(this),
                RocketHardwareTestType.TopStabilizers => new TopStabilizerHardwareTestCase(this),
                RocketHardwareTestType.Thrusters => new ThrusterHardwareTestCase(this),
                _ => null
            };
        }

        abstract class ControllerBackedHardwareTest : IRocketHardwareTest
        {
            protected readonly HardwareTestController Controller;

            /// <summary>
            /// Stores the shared controller used by small test-case adapters to
            /// call their scripted drive method.
            /// </summary>
            protected ControllerBackedHardwareTest(HardwareTestController controller)
            {
                Controller = controller;
            }

            public abstract RocketHardwareTestType TestType { get; }
            public RocketHardwareTestOutcome Outcome => Controller.Outcome;
            public string Status => Controller.Status;

            /// <summary>Default adapters need no additional setup.</summary>
            public virtual void Prepare(FalconAgent agent) { }
            /// <summary>Implemented by each adapter to call its scripted drive method.</summary>
            public abstract void Step(float dt);
            /// <summary>Default adapters own no extra cleanup resources.</summary>
            public virtual void Stop() { }
        }
    }
}
