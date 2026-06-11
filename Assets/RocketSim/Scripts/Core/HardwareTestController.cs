using UnityEngine;

namespace RocketSim
{
    public enum RocketHardwareTestType
    {
        FinAxisX,
        FinAxisY,
        FinAxisZ,
        Aerodynamics,
        Takeoff,
        Landing,
        RcsVacuum,
        RcsSeparation,
        TopStabilizers,
        Thrusters
    }

    public enum RocketHardwareTestOutcome
    {
        Idle,
        Running,
        Passed,
        Failed,
        Limited
    }

    public class HardwareTestController : MonoBehaviour
    {
        public TrainingAreaManager manager;

        public FalconAgent Agent { get; private set; }
        public RocketHardwareTestType ActiveTest { get; private set; }
        public RocketHardwareTestOutcome Outcome { get; private set; } = RocketHardwareTestOutcome.Idle;
        public string Status { get; private set; } = "No hardware test running.";

        float _elapsed;
        float _fuelStart;
        float _initialAltitude;
        float _maxAltitude;
        float _maxVerticalSpeed;
        float _maxAngularRateDegS;
        float _maxTargetAxisRateDegS;
        float _maxCrossAxisRateDegS;
        float _maxLocalRateXDegS;
        float _maxLocalRateYDegS;
        float _maxLocalRateZDegS;
        float _maxDynamicPressure;
        float _initialVerticalSpeed;
        float _initialLateralSpeed;
        float _minLateralSpeed;
        float _initialHorizontalDistance;
        float _minHorizontalDistance;
        float _startTiltDeg;
        float _minTiltDeg;
        float _twr;
        float _nextStatusUpdateTime;
        Vector3 _targetLocalAxis;
        Vector3 _separationTargetUp;
        float _initialAngularRateDegS;
        float _separationInitialPointingErrorDeg;
        float _minSeparationPointingErrorDeg;
        float _finalSeparationPointingErrorDeg;
        float _finalSeparationAngularRateDegS;

        float[] _throttle;
        Vector2[] _gimbal;
        float[] _fins;
        float[] _rcs;
        const float BaseGroundClearance = 0.5f;

        public bool IsRunning => Outcome == RocketHardwareTestOutcome.Running;

        public void Prepare(RocketHardwareTestType testType)
        {
            Agent = null;
            ActiveTest = testType;
            Outcome = RocketHardwareTestOutcome.Idle;
            _nextStatusUpdateTime = 0f;
            Status = $"Preparing {DisplayName(testType)}...";
        }

        public void FailToStart(RocketHardwareTestType testType, string reason)
        {
            Agent = null;
            ActiveTest = testType;
            Outcome = RocketHardwareTestOutcome.Failed;
            Status = reason;
        }

        public void Begin(RocketHardwareTestType testType, FalconAgent agent)
        {
            Agent = agent;
            ActiveTest = testType;
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
                case RocketHardwareTestType.Takeoff:
                    position = new Vector3(0f, BaseGroundClearance, 0f);
                    rotation = Quaternion.identity;
                    velocity = Vector3.zero;
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

            Status = $"{DisplayName(testType)} running...";
        }

        public void Stop()
        {
            Agent?.ClearManualControl();
            ClearAgentRcsVisuals();
            Outcome = RocketHardwareTestOutcome.Idle;
            Status = "Hardware test stopped.";
        }

        void FixedUpdate()
        {
            if (Outcome != RocketHardwareTestOutcome.Running || !Agent) return;

            _elapsed += Time.fixedDeltaTime;
            UpdateMetrics();

            switch (ActiveTest)
            {
                case RocketHardwareTestType.FinAxisX:
                case RocketHardwareTestType.FinAxisY:
                case RocketHardwareTestType.FinAxisZ:
                    DriveFinAxisTest();
                    break;
                case RocketHardwareTestType.Aerodynamics:
                    DriveAerodynamicsTest();
                    break;
                case RocketHardwareTestType.Takeoff:
                    DriveTakeoffTest();
                    break;
                case RocketHardwareTestType.Landing:
                    DriveLandingTest();
                    break;
                case RocketHardwareTestType.RcsVacuum:
                    DriveRcsVacuumTest();
                    break;
                case RocketHardwareTestType.RcsSeparation:
                    DriveRcsSeparationTest();
                    break;
                case RocketHardwareTestType.TopStabilizers:
                    DriveTopStabilizerTest();
                    break;
                case RocketHardwareTestType.Thrusters:
                    DriveThrusterTest();
                    break;
            }

            if (Outcome == RocketHardwareTestOutcome.Running && _elapsed >= _nextStatusUpdateTime)
            {
                UpdateRunningStatus();
                _nextStatusUpdateTime = _elapsed + 0.2f;
            }
        }

        void DriveFinAxisTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            if (Mathf.Abs(_targetLocalAxis.y) < 0.9f)
            {
                DriveFinTiltAxisTest(cfg);
                return;
            }

            float amp = Mathf.Max(5f, cfg.maxFinAngle * 0.9f);

            if (_elapsed >= 1.5f && _elapsed < 6.5f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 6.5f && _elapsed < 11.5f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp);
            }
            else if (_elapsed >= 11.5f && _elapsed < 18.5f)
            {
                float sweep = Mathf.Sin((_elapsed - 11.5f) * Mathf.PI) * amp;
                ApplyFinAxisCommand(_targetLocalAxis, sweep);
            }
            else if (_elapsed >= 20f)
            {
                bool hadAirflow = _maxDynamicPressure > 1000f;
                bool responded = _maxTargetAxisRateDegS > 3f;
                Complete(
                    hadAirflow && responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    hadAirflow && responded
                        ? $"{DisplayName(ActiveTest)} passed: q max {_maxDynamicPressure:F0} Pa, target-axis response {_maxTargetAxisRateDegS:F1} deg/s."
                        : $"{DisplayName(ActiveTest)} weak: q max {_maxDynamicPressure:F0} Pa, target-axis response {_maxTargetAxisRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveFinTiltAxisTest(RocketPhysicsConfig cfg)
        {
            float amp = Mathf.Clamp(cfg.maxFinAngle * 0.28f, 2f, 10f);

            if (_elapsed >= 1f && _elapsed < 2.4f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 2.4f && _elapsed < 4.8f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 4.8f && _elapsed < 6.2f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp);
            }
            else if (_elapsed >= 6.2f && _elapsed < 8.6f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 8.6f && _elapsed < 10f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, amp * 0.6f);
            }
            else if (_elapsed >= 10f && _elapsed < 12f)
            {
                ApplyFinAxisDamping(_targetLocalAxis, amp);
            }
            else if (_elapsed >= 12f && _elapsed < 13.4f)
            {
                ApplyFinAxisCommand(_targetLocalAxis, -amp * 0.6f);
            }
            else if (_elapsed >= 15f)
            {
                bool hadAirflow = _maxDynamicPressure > 1000f;
                bool responded = _maxTargetAxisRateDegS > 1f;
                Complete(
                    hadAirflow && responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    hadAirflow && responded
                        ? $"{DisplayName(ActiveTest)} passed: q max {_maxDynamicPressure:F0} Pa, moderated target-axis response {_maxTargetAxisRateDegS:F1} deg/s."
                        : $"{DisplayName(ActiveTest)} weak: q max {_maxDynamicPressure:F0} Pa, moderated target-axis response {_maxTargetAxisRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void ApplyFinAxisDamping(Vector3 localAxis, float limitDeg)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;
            float signedRate = Vector3.Dot(localRate, localAxis.normalized);
            float command = Mathf.Clamp(-signedRate * 0.18f, -limitDeg, limitDeg);
            ApplyFinAxisCommand(localAxis, command);
        }

        void ApplyFinAxisCommand(Vector3 localAxis, float magnitudeDeg)
        {
            if (_fins == null || _fins.Length == 0) return;

            if (Mathf.Abs(localAxis.y) > 0.9f)
            {
                for (int i = 0; i < _fins.Length; i++)
                    _fins[i] = magnitudeDeg;
                return;
            }

            Transform finRoot = Agent?.assembly?.fins ? Agent.assembly.fins.transform : null;
            if (finRoot == null)
            {
                ApplyFallbackOpposedFinPattern(localAxis, magnitudeDeg);
                return;
            }

            int activeFinIndex = 0;
            Vector3 axis = new Vector3(localAxis.x, 0f, localAxis.z).normalized;
            for (int i = 0; i < finRoot.childCount && activeFinIndex < _fins.Length; i++)
            {
                Transform fin = finRoot.GetChild(i);
                if (!fin.gameObject.activeSelf) continue;

                Vector3 localRadial = Agent.transform.InverseTransformPoint(fin.position);
                localRadial.y = 0f;
                float side = localRadial.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(localRadial.normalized, axis)
                    : 0f;

                _fins[activeFinIndex] = side * magnitudeDeg;
                activeFinIndex++;
            }
        }

        void ApplyFallbackOpposedFinPattern(Vector3 localAxis, float magnitudeDeg)
        {
            for (int i = 0; i < _fins.Length; i++)
                _fins[i] = 0f;

            if (_fins.Length < 2) return;

            if (Mathf.Abs(localAxis.x) >= Mathf.Abs(localAxis.z))
            {
                _fins[0] = magnitudeDeg;
                _fins[Mathf.Min(2, _fins.Length - 1)] = -magnitudeDeg;
                return;
            }

            int positive = Mathf.Min(1, _fins.Length - 1);
            int negative = Mathf.Min(3, _fins.Length - 1);
            _fins[positive] = magnitudeDeg;
            _fins[negative] = -magnitudeDeg;
        }

        void DriveAerodynamicsTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 12f || Agent.transform.localPosition.y < 250f)
            {
                float lateralDrop = _initialLateralSpeed - _minLateralSpeed;
                bool hadAirflow = _maxDynamicPressure > 3000f;
                bool qSane = _maxDynamicPressure < 250000f;
                bool lateralDamped = _initialLateralSpeed > 5f &&
                                      lateralDrop > Mathf.Max(8f, _initialLateralSpeed * 0.2f);
                bool attitudeResponded = _maxAngularRateDegS > 0.2f;
                bool stayedFinite = IsFinite(Agent.rb.linearVelocity) &&
                                    Agent.rb.linearVelocity.magnitude < 900f;

                bool passed = hadAirflow && qSane && lateralDamped && attitudeResponded && stayedFinite;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"Aerodynamics passed: q max {_maxDynamicPressure:F0} Pa, lateral speed damped {lateralDrop:F1} m/s, attitude response {_maxAngularRateDegS:F1} deg/s."
                        : $"Aerodynamics suspicious: q max {_maxDynamicPressure:F0} Pa, lateral damping {lateralDrop:F1} m/s, attitude response {_maxAngularRateDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveTakeoffTest()
        {
            ClearCommandBuffers();

            float throttleCommand = 0f;
            if (_elapsed >= 1.2f && _elapsed < 3f)
                throttleCommand = Mathf.InverseLerp(1.2f, 3f, _elapsed);
            else if (_elapsed >= 3f && _elapsed < 13f)
                throttleCommand = 1f;

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            if (_elapsed >= 13f)
            {
                float altitudeGain = _maxAltitude - _initialAltitude;
                float fuelUsed = Mathf.Max(0f, _fuelStart - Agent.GetFuel);
                float currentTilt = Vector3.Angle(Agent.transform.up, Vector3.up);

                if (_fuelStart <= 1f || fuelUsed <= 0.1f)
                {
                    Complete(RocketHardwareTestOutcome.Failed,
                        "Takeoff failed: no usable propellant was available in the current rocket config.");
                }
                else if (_twr < 1.05f)
                {
                    Complete(RocketHardwareTestOutcome.Limited,
                        $"Takeoff limited: configured liftoff TWR is {_twr:F2}, so the rocket cannot leave the pad cleanly.");
                }
                else
                {
                    bool lifted = altitudeGain > 80f && _maxVerticalSpeed > 12f && currentTilt < 20f;
                    Complete(
                        lifted ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                        lifted
                            ? $"Takeoff passed: climb {altitudeGain:F1} m, max VY {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}."
                            : $"Takeoff weak: climb {altitudeGain:F1} m, max VY {_maxVerticalSpeed:F1} m/s, tilt {currentTilt:F1} deg, TWR {_twr:F2}.");
                }
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveLandingTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            if (_elapsed > 0.5f && _twr < 1.05f)
            {
                Complete(RocketHardwareTestOutcome.Limited,
                    $"Landing limited: configured landing-burn TWR is {_twr:F2}, so thrust cannot overcome gravity.");
                return;
            }

            if (_fuelStart <= 1f)
            {
                Complete(RocketHardwareTestOutcome.Failed,
                    "Landing failed: no usable propellant was available in the current rocket config.");
                return;
            }

            float targetAltitude = LandingTargetAltitude(cfg);
            Vector3 position = Agent.transform.localPosition;
            float altitude = Mathf.Max(0f, position.y - targetAltitude);
            float mass = Mathf.Max(1f, cfg.dryMass + Agent.GetFuel);
            float maxThrustAccel = cfg.maxThrust * ActiveEngineCount() / mass;
            float maxNetUpAccel = maxThrustAccel - 9.80665f;
            float brakeAccel = Mathf.Max(1f, maxNetUpAccel * 0.65f);
            float targetVy = -Mathf.Clamp(
                Mathf.Sqrt(2f * brakeAccel * Mathf.Max(altitude, 1f)),
                2f,
                78f);

            float vy = Agent.rb.linearVelocity.y;
            float desiredNetUpAccel = (targetVy - vy) * 0.45f;
            float desiredThrustAccel = Mathf.Clamp(9.80665f + desiredNetUpAccel, 0f, maxThrustAccel);
            float throttleCommand = maxThrustAccel > 0.1f ? desiredThrustAccel / maxThrustAccel : 0f;

            if (altitude < 80f && vy < -18f)
                throttleCommand = Mathf.Max(throttleCommand, 0.85f);

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            float tilt = Vector3.Angle(Agent.transform.up, Vector3.up);

            if (altitude <= 2f && _elapsed > 4f)
            {
                bool landed = Mathf.Abs(vy) < 6f && tilt < 8f;
                Complete(
                    landed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    landed
                        ? $"Landing passed: straight-engine touchdown VY {vy:F1} m/s, tilt {tilt:F1} deg."
                        : $"Landing failed at touchdown: straight-engine VY {vy:F1} m/s, tilt {tilt:F1} deg.");
                return;
            }

            if (position.y < targetAltitude - 8f || _elapsed >= 36f)
            {
                Complete(RocketHardwareTestOutcome.Failed,
                    $"Landing failed: altitude {altitude:F1} m, straight-engine VY {vy:F1} m/s, TWR {_twr:F2}.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveRcsVacuumTest()
        {
            ClearCommandBuffers();
            float rollAmp = 1.0f;
            float tiltAmp = 0.9f;

            if (_elapsed >= 0.75f && _elapsed < 2.75f)
                ApplyRcsAxisCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 2.75f && _elapsed < 3.75f)
                ApplyRcsDampingCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 3.75f && _elapsed < 5.75f)
                ApplyRcsAxisCommand(Vector3.up, -rollAmp);
            else if (_elapsed >= 5.75f && _elapsed < 6.75f)
                ApplyRcsDampingCommand(Vector3.up, rollAmp);
            else if (_elapsed >= 6.75f && _elapsed < 8.5f)
                ApplyRcsAxisCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 8.5f && _elapsed < 9.5f)
                ApplyRcsDampingCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 9.5f && _elapsed < 11.25f)
                ApplyRcsAxisCommand(Vector3.right, -tiltAmp);
            else if (_elapsed >= 11.25f && _elapsed < 12.25f)
                ApplyRcsDampingCommand(Vector3.right, tiltAmp);
            else if (_elapsed >= 12.25f && _elapsed < 14f)
                ApplyRcsAxisCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 14f && _elapsed < 15f)
                ApplyRcsDampingCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 15f && _elapsed < 16.75f)
                ApplyRcsAxisCommand(Vector3.forward, -tiltAmp);
            else if (_elapsed >= 16.75f && _elapsed < 18f)
                ApplyRcsDampingCommand(Vector3.forward, tiltAmp);
            else if (_elapsed >= 19f)
            {
                bool vacuum = _maxDynamicPressure < 1f;
                bool roll = _maxLocalRateYDegS > 1.0f;
                bool pitch = _maxLocalRateXDegS > 0.5f;
                bool yaw = _maxLocalRateZDegS > 0.5f;

                bool passed = vacuum && pitch && yaw && roll;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"RCS vacuum passed: pitch {_maxLocalRateXDegS:F1}, yaw {_maxLocalRateZDegS:F1}, roll {_maxLocalRateYDegS:F1} deg/s."
                        : $"RCS vacuum weak: q max {_maxDynamicPressure:F2} Pa, pitch {_maxLocalRateXDegS:F1}, yaw {_maxLocalRateZDegS:F1}, roll {_maxLocalRateYDegS:F1} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveRcsSeparationTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 0.75f && _elapsed < 4f)
            {
                ApplyRcsRateDampingCommand(0.95f, 0.08f);
            }
            else if (_elapsed >= 4f && _elapsed < 25f)
            {
                ApplyRcsPointingCommand(_separationTargetUp, 2.4f, 0.035f, 1f, 0.65f);
            }
            else if (_elapsed >= 25f && _elapsed < 35f)
            {
                ApplyRcsPointingCommand(_separationTargetUp, 0.85f, 0.085f, 0.65f, 0.55f);
            }
            else if (_elapsed >= 36f)
            {
                bool thinAir = _maxDynamicPressure < 250f;
                bool initialDisturbance = _initialAngularRateDegS > 0.5f;
                bool reoriented = _separationInitialPointingErrorDeg - _minSeparationPointingErrorDeg > 45f &&
                                  _finalSeparationPointingErrorDeg < 30f;
                bool settled = _finalSeparationAngularRateDegS < 10f;

                bool passed = thinAir && initialDisturbance && reoriented && settled;
                Complete(
                    passed ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    passed
                        ? $"Stage separation RCS passed: target error {_finalSeparationPointingErrorDeg:F1} deg, rate {_finalSeparationAngularRateDegS:F1} deg/s, q max {_maxDynamicPressure:F1} Pa."
                        : $"Stage separation RCS weak: target error {_finalSeparationPointingErrorDeg:F1} deg, best {_minSeparationPointingErrorDeg:F1} deg, rate {_finalSeparationAngularRateDegS:F1} deg/s, q max {_maxDynamicPressure:F1} Pa.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void ApplyRcsPointingCommand(
            Vector3 desiredWorldUp,
            float pointingGain,
            float rateDampingGain,
            float tiltLimit,
            float rollLimit)
        {
            if (desiredWorldUp.sqrMagnitude < 0.1f) return;

            Vector3 currentUp = Agent.transform.up;
            Vector3 targetUp = desiredWorldUp.normalized;
            Vector3 cross = Vector3.Cross(currentUp, targetUp);
            float errorAngleRad = Vector3.Angle(currentUp, targetUp) * Mathf.Deg2Rad;
            Vector3 errorWorld = cross.sqrMagnitude > 0.000001f
                ? cross.normalized * errorAngleRad
                : Vector3.zero;
            Vector3 localError = Agent.transform.InverseTransformDirection(errorWorld);
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;

            float pitch = Mathf.Clamp(localError.x * pointingGain - localRate.x * rateDampingGain, -tiltLimit, tiltLimit);
            float yaw = Mathf.Clamp(localError.z * pointingGain - localRate.z * rateDampingGain, -tiltLimit, tiltLimit);
            float roll = Mathf.Clamp(-localRate.y * rateDampingGain, -rollLimit, rollLimit);

            ApplyRcsAxisCommand(Vector3.right, pitch);
            ApplyRcsAxisCommand(Vector3.forward, yaw);
            ApplyRcsAxisCommand(Vector3.up, roll);
        }

        void ApplyRcsRateDampingCommand(float limit, float rateDampingGain)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;

            ApplyRcsAxisCommand(Vector3.right, Mathf.Clamp(-localRate.x * rateDampingGain, -limit, limit));
            ApplyRcsAxisCommand(Vector3.up, Mathf.Clamp(-localRate.y * rateDampingGain, -limit, limit));
            ApplyRcsAxisCommand(Vector3.forward, Mathf.Clamp(-localRate.z * rateDampingGain, -limit, limit));
        }

        void ApplyRcsDampingCommand(Vector3 localAxis, float limit)
        {
            Vector3 localRate = Agent.transform.InverseTransformDirection(Agent.rb.angularVelocity) * Mathf.Rad2Deg;
            float signedRate = Vector3.Dot(localRate, localAxis.normalized);
            float command = Mathf.Clamp(-signedRate * 0.01f, -limit, limit);
            ApplyRcsAxisCommand(localAxis, command);
        }

        void ApplyRcsAxisCommand(Vector3 localAxis, float command)
        {
            if (Mathf.Abs(localAxis.y) > 0.9f)
            {
                ApplyRcsRollCommand(command);
                return;
            }

            if (Mathf.Abs(localAxis.x) > 0.9f)
            {
                ApplyRcsPitchCommand(command);
                return;
            }

            ApplyRcsYawCommand(command);
        }

        void ApplyRcsPitchCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            // Plumes point outboard; the rocket force is opposite the plume.
            // RCS3 sits on -Z and pitches +X when firing outboard.
            // RCS2 sits on +Z and pitches -X when firing outboard.
            SetRcsJet(command >= 0f ? 3 : 2, RcsComponent.RcsNozzle.Outboard, Mathf.Abs(command));
        }

        void ApplyRcsYawCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            // RCS0 sits on +X and yaws +Z when firing outboard.
            // RCS1 sits on -X and yaws -Z when firing outboard.
            SetRcsJet(command >= 0f ? 0 : 1, RcsComponent.RcsNozzle.Outboard, Mathf.Abs(command));
        }

        void ApplyRcsRollCommand(float command)
        {
            if (Mathf.Abs(command) < 0.001f) return;

            var nozzle = command >= 0f
                ? RcsComponent.RcsNozzle.RollNegative
                : RcsComponent.RcsNozzle.RollPositive;

            for (int podIndex = 0; podIndex < RcsComponent.FalconPodCount; podIndex++)
                SetRcsJet(podIndex, nozzle, Mathf.Abs(command));
        }

        void SetRcsJet(int podIndex, RcsComponent.RcsNozzle nozzle, float command)
        {
            if (_rcs == null) return;

            int jetIndex = RcsComponent.JetIndex(podIndex, nozzle);
            if (jetIndex < 0 || jetIndex >= _rcs.Length) return;

            _rcs[jetIndex] = Mathf.Max(_rcs[jetIndex], Mathf.Clamp01(command));
        }

        void DriveTopStabilizerTest()
        {
            ClearCommandBuffers();

            if (_elapsed >= 1f && _elapsed < 2.5f)
                ApplyRcsPitchCommand(1f);
            else if (_elapsed >= 2.5f && _elapsed < 4f)
                ApplyRcsPitchCommand(-1f);
            else if (_elapsed >= 4f && _elapsed < 5.5f)
                ApplyRcsYawCommand(1f);
            else if (_elapsed >= 5.5f && _elapsed < 7f)
                ApplyRcsRollCommand(1f);
            else if (_elapsed >= 8f)
            {
                bool responded = _maxAngularRateDegS > 0.25f;
                Complete(
                    responded ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                    responded
                        ? $"Top stabilizer test passed: RCS angular response {_maxAngularRateDegS:F2} deg/s."
                        : $"Top stabilizer response weak: RCS angular response {_maxAngularRateDegS:F2} deg/s.");
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

        void DriveThrusterTest()
        {
            ClearCommandBuffers();
            RocketPhysicsConfig cfg = Agent.CurrentPhysicsConfig;

            float throttleCommand = 0f;
            if (_elapsed >= 0.75f && _elapsed < 2.5f)
                throttleCommand = Mathf.InverseLerp(0.75f, 2.5f, _elapsed);
            else if (_elapsed >= 2.5f && _elapsed < 7.5f)
                throttleCommand = 1f;

            for (int i = 0; i < _throttle.Length; i++)
                _throttle[i] = throttleCommand;

            if (_elapsed >= 8.5f)
            {
                float altitudeGain = _maxAltitude - _initialAltitude;
                float fuelUsed = Mathf.Max(0f, _fuelStart - Agent.GetFuel);

                if (_fuelStart <= 1f || fuelUsed <= 0.1f)
                {
                    Complete(RocketHardwareTestOutcome.Failed,
                        "Thruster test failed: no usable propellant was available in the current rocket config.");
                }
                else if (_twr < 1.05f)
                {
                    Complete(RocketHardwareTestOutcome.Limited,
                        $"Thrusters fired, but lift-off is not expected at TWR {_twr:F2}. Fuel used {fuelUsed:F1} kg.");
                }
                else
                {
                    bool lifted = altitudeGain > 5f && _maxVerticalSpeed > 1f;
                    Complete(
                        lifted ? RocketHardwareTestOutcome.Passed : RocketHardwareTestOutcome.Failed,
                        lifted
                            ? $"Thruster test passed: altitude gain {altitudeGain:F1} m, max V {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}."
                            : $"Thruster response weak: altitude gain {altitudeGain:F1} m, max V {_maxVerticalSpeed:F1} m/s, TWR {_twr:F2}.");
                }
                return;
            }

            Agent.SetManualControl(_throttle, _gimbal, _fins, _rcs);
        }

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

        float LandingTargetAltitude(RocketPhysicsConfig cfg)
        {
            return BaseGroundClearance;
        }

        float HorizontalSpeed()
        {
            if (!Agent || !Agent.rb) return 0f;
            Vector3 velocity = Agent.rb.linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        float HorizontalDistanceToPad()
        {
            if (!Agent) return 0f;
            Vector3 position = Agent.transform.localPosition;
            position.y = 0f;
            return position.magnitude;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        void Complete(RocketHardwareTestOutcome outcome, string status)
        {
            Agent?.ClearManualControl();
            ClearAgentRcsVisuals();
            Outcome = outcome;
            Status = status;
        }

        void ClearAgentRcsVisuals()
        {
            if (Agent && Agent.assembly && Agent.assembly.rcs)
                Agent.assembly.rcs.ClearVisuals();
        }

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

        float ComputeTwr(RocketPhysicsConfig cfg)
        {
            float mass = Mathf.Max(1f, cfg.dryMass + Agent.GetFuel);
            float totalThrust = cfg.maxThrust * ActiveEngineCount();
            return totalThrust / (mass * 9.80665f);
        }

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

        static string DisplayName(RocketHardwareTestType testType)
        {
            return testType switch
            {
                RocketHardwareTestType.FinAxisX => "Fin X-axis rotation",
                RocketHardwareTestType.FinAxisY => "Fin Y-axis spin",
                RocketHardwareTestType.FinAxisZ => "Fin Z-axis rotation",
                RocketHardwareTestType.Aerodynamics => "Aerodynamics",
                RocketHardwareTestType.Takeoff => "Takeoff",
                RocketHardwareTestType.Landing => "Landing",
                RocketHardwareTestType.RcsVacuum => "RCS vacuum",
                RocketHardwareTestType.RcsSeparation => "Stage separation RCS",
                RocketHardwareTestType.TopStabilizers => "Top stabilizers",
                RocketHardwareTestType.Thrusters => "Engine static",
                _ => "Hardware test"
            };
        }

        static bool IsFinTest(RocketHardwareTestType testType)
        {
            return testType == RocketHardwareTestType.FinAxisX ||
                   testType == RocketHardwareTestType.FinAxisY ||
                   testType == RocketHardwareTestType.FinAxisZ;
        }

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

        static Vector3 ComputeSeparationTargetUp(Vector3 separationVelocity)
        {
            Vector3 retrograde = -separationVelocity;
            if (retrograde.sqrMagnitude < 1f)
                retrograde = new Vector3(-1f, -0.1f, 0f);

            return retrograde.normalized;
        }
    }
}
