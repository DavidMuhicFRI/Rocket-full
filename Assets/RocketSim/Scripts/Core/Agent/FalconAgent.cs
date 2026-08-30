// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.cs
// Purpose: Defines the shared state, references, constants, and public read-only accessors for the FalconAgent partial class.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        [Header("References — wire inside prefab")]
        public Rigidbody rb;

        public RocketAssembly assembly;
        public Transform targetPad;
        [Tooltip("Guidance/capture reference point near the grid-fin hardpoint.")]
        public Transform catchFrame;

        [Header("Configs - assigned by SimulationAreaHost")]
        public SimEnvironmentConfig envConfig = new();
        
        // Sensors
        RocketSensorPackage _sensors = new();
        
        // Telemetry identity
        int _areaIndex;
        int _episode;
        int _step;
        float _stepReward;
        bool _hasEpisodeStarted;
        bool _currentEpisodeCompleted;
        bool _telemetryLoggedThisStep;
        bool _episodeEndedThisStep;
        bool _landingEpisodeEndLogged;
        bool _hoverTrackTargetReachedThisStep;
        bool _landingEpisodeSucceeded;
        bool _objectiveSuccessTerminalReached;
        EpisodeTerminationReason _episodeTerminationReason;
        int _hoverTrackEpisodeCaptures;
        readonly RewardContributionBuffer _rewardContributions = new();
        float _objectiveDifficulty01;
        
        
        [Header("Flame Visual")] public float minFlameWidth = 1.5f;
        public float maxFlameWidth = 4f;
        public float minFlameLength = 2f;
        public float maxFlameLength = 20f;

        // ── Physics config ───────────────────────────
        RocketPhysicsConfig cfg;
        
        // ── Engine ─────────────────────────────────────────────────
        Vector2[] targetGimbal, gimbal;
        float[] commandedThrottle, targetThrottle, throttle;
        float[] engineEnableCommands;
        private float fuel;
        private float rcsPropellant;

        EngineRunState[] engineStates;
        float[] engineStateTimers;
        float[] engineRunTimes;
        float[] engineOffTimes;
        int[] engineIgnitionCounts;
        int _engineRestartsThisStep;
        int _episodeEngineRestartCount;
        bool _legLandingPropulsionLocked;

        // Fins 
        float[] finAngles;
        float[] targetFinAngles;

        // RCS
        float[] rcsValveRequests;
        float[] rcsValveStates;
        float[] rcsPulseTimeRemaining;

        // ── Environment ───────────────────────────────────────────────────────
        readonly WindEnvironmentRuntime _windEnvironment = new();
        Vector3 wind => _windEnvironment.Current;
        DeterministicRandom _episodeRandom;
        int _episodeSeed;

        readonly EpisodeFaultRuntime _episodeFaults = new();
        float _episodeElapsedSeconds;
        bool _hardwareTestMode;
        bool _manualControlActive;
        float[] _manualThrottle;
        Vector2[] _manualGimbal;
        float[] _manualFinAngles;
        float[] _manualRcsValveRequests;

        // ── Cached for observations / debug ───────────────────────────────────
        float q; // dynamic pressure
        float aoaDeg; // angle of attack
        float _hoverTrackStableTime;
        bool _hoverTrackCaptureLatched;
        float _hoverTrackSegmentStartDistance;
        float _hoverTrackSegmentElapsedTime;
        readonly ChopstickLandingRuntime _chopstickLanding = new();
        bool _landingPlatformInsideCapture => _chopstickLanding.InsideCapture;
        bool _landingPlatformStable => _chopstickLanding.Stable;
        bool _landingPlatformBecameStable => _chopstickLanding.BecameStable;
        float _landingPlatformStableTime => _chopstickLanding.StableTime;
        readonly LegLandingRuntime _legLanding = new();
        int _defaultSolverIterations = 6;
        int _defaultSolverVelocityIterations = 1;
        CollisionDetectionMode _defaultCollisionDetectionMode = CollisionDetectionMode.Discrete;
        bool _defaultIsKinematic;
        bool _defaultDetectCollisions = true;
        Vector3 _stagedEpisodeLinearVelocity;
        Vector3 _stagedEpisodeAngularVelocity;
        bool _hasStagedEpisodeMotion;
        LandingCurriculumProfile _landingEpisodeProfile;
        bool _landingEpisodeProfileInitialized;
        bool _landingEpisodeUsesEasierReplay;
        float _landingEpisodeStartAltitude;
        float _landingEpisodeFlyawayAltitude;
        float _episodeInitialPlanarDistance;
        float _episodeInitialYawErrorDeg;
        float _episodeInitialSpeed;
        float _episodeInitialVerticalSpeed;
        float _episodeInitialHorizontalSpeed;
        float _episodeInitialTiltDeg;
        float _episodeInitialAngularRateDegS;
        float _episodeInitialFuelKg;
        float _episodeInitialVehicleMassKg;
        float _episodeMinimumCommandableNonzeroThrustToWeight;
        float _episodeAllEnginesMinimumThrustToWeight;
        float _episodeAllEnginesMaximumThrustToWeight;
        float _previousLegLandingCenteringPotential;
        bool _legLandingCenteringPotentialInitialized;
        int _maximumRewardedLegSupportFeet;

        const float Rho0 = 1.225f; // ISA sea-level density (kg/m³)
        const float HScale = 8500f; // ISA scale height (m)
        const float G0 = 9.80665f;
        const float BaseGroundClearance = 0.5f;
        const float EngineIgnitionThreshold = 0.08f;
        const float EngineShutdownThreshold = 0.03f;
        const float EngineEnableOnThreshold = 0.35f;
        const float EngineEnableOffThreshold = -0.35f;
        const float RcsValveActionThreshold = 0.5f;
        const float VacuumThrustMultiplier = 1.08f;
        const float VacuumIspMultiplier = 1.10f;
        const float EngineCommandEpsilon = 0.01f;

        //public getters for HUD
        /// <summary>
        /// Reads one bounded engine throttle channel after spool smoothing so HUD and hardware tests can show actuator state safely.
        /// </summary>
        public float GetCurrentThrottle(int index) => throttle != null && index >= 0 && index < throttle.Length ? throttle[index] : 0f;
        
        public float GetFuel => fuel;
        
        /// <summary>
        /// Reads one bounded engine gimbal X channel in degrees for HUD and hardware-test displays.
        /// </summary>
        public float GetGimbalX(int index) => gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].x : 0f;
        
        /// <summary>
        /// Reads one bounded engine gimbal Z channel in degrees for HUD and hardware-test displays.
        /// </summary>
        public float GetGimbalZ(int index) => gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].y : 0f;
        public RocketPhysicsConfig CurrentPhysicsConfig => cfg;
        public float DynamicPressure => q;
        public bool HardwareTestMode => _hardwareTestMode;
        public float ObjectiveDifficulty01 => _objectiveDifficulty01;
        public int HoverTrackEpisodeCaptures => _hoverTrackEpisodeCaptures;
        public bool LegLandingPropulsionLocked => _legLandingPropulsionLocked;
        
        void ResetEpisodeRandom()
        {
            int baseSeed = envConfig?.environmentSeed ?? 1;
            int seedEpisodeIndex = envConfig?.RandomSeedEpisodeIndex(_episode) ?? _episode;
            _episodeSeed = DeterministicRandom.EpisodeSeed(baseSeed, _areaIndex, seedEpisodeIndex);
            _episodeRandom = new DeterministicRandom(_episodeSeed);
        }

        float RandomRange(float min, float max) => _episodeRandom.Range(min, max);

        Vector2 RandomInsideUnitCircle() => _episodeRandom.InsideUnitCircle();

        Vector3 RandomUnitVector3() => _episodeRandom.UnitVector3();

        /// <summary>
        /// Returns the landing thresholds frozen at the episode start.
        /// </summary>
        LandingCurriculumProfile ActiveLandingProfile => _landingEpisodeProfileInitialized ? _landingEpisodeProfile : envConfig.GetActiveLandingCurriculumProfile(envConfig.ActiveLandingCurriculumProgress);

        /// <summary>
        /// Freezes one task difficulty for the whole episode and independently samples the controlled easier-task replay condition.
        /// </summary>
        void PrepareLandingEpisodeProfile()
        {
            _landingEpisodeProfileInitialized = false;
            _landingEpisodeUsesEasierReplay = false;
            if (envConfig == null || !envConfig.scenario.IsLanding())
                return;
            
            var curriculumRandom = new DeterministicRandom(DeterministicRandom.EpisodeSeed(envConfig.environmentSeed, _areaIndex, _episode, stream: 1));
            _landingEpisodeUsesEasierReplay =
                envConfig.behaviorType == BehaviorType.Training &&
                envConfig.ActiveLandingCurriculumEnabled &&
                envConfig.ActiveLandingCurriculumProgress > 0f &&
                curriculumRandom.Chance(envConfig.ActiveLandingReplayProbability);
            float difficulty = envConfig.IsStandardEvaluation ? envConfig.StandardEvaluationDifficultyForEpisode(_episode) : envConfig.LandingEpisodeDifficulty(_landingEpisodeUsesEasierReplay);
            _landingEpisodeProfile = envConfig.GetActiveLandingCurriculumProfile(difficulty);
            _landingEpisodeProfileInitialized = true;
        }

        static string PassFail(bool passed) => passed ? "PASS" : "FAIL";
    }
}
