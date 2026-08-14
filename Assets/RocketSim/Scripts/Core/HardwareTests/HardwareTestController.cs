// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/HardwareTests/HardwareTestController.cs
// Purpose: Defines hardware test status, outcome enums, and shared controller state.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public enum RocketHardwareTestType
    {
        FinAxisX,
        FinAxisY,
        FinAxisZ,
        Aerodynamics,
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

    public partial class HardwareTestController : MonoBehaviour
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
        IRocketHardwareTest _activeTestCase;

        float[] _throttle;
        Vector2[] _gimbal;
        float[] _fins;
        float[] _rcs;
        const float BaseGroundClearance = 0.5f;

        public bool IsRunning => Outcome == RocketHardwareTestOutcome.Running;

        /// <summary>
        /// Returns whether all vector components are finite simulation values.
        /// </summary>
        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        /// <summary>
        /// Returns whether a scalar is neither NaN nor infinite.
        /// </summary>
        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
