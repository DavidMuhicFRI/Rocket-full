// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.cs
// Purpose: Defines the shared state, references, constants, and public read-only accessors for the FalconAgent partial class.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using UnityEngine.Serialization;

namespace RocketSim
{
    // ============================================================================
    //  FalconAgent — Falcon 9 landing simulation (ML-Agents)
    //
    //  Physics constants come from RocketAssembly.GetPhysicsConfig()
    //
    //  envConfig is a SHARED reference set by TrainingAreaManager at spawn time.
    //  The right-side panel writes to the same object — all agents see changes.
    //
    // ============================================================================
    public partial class FalconAgent : Agent
    {
        [Header("References — wire inside prefab")]
        public Rigidbody rb;

        public RocketAssembly assembly;
        public Transform targetPad;
        [FormerlySerializedAs("topControlPoint")]
        [Tooltip("Guidance/capture reference point near the grid-fin hardpoint.")]
        public Transform catchFrame;

        [Header("Configs — assigned by TrainingAreaManager via ConfigBridge")]
        public SimEnvironmentConfig envConfig = new();
        
        // Sensors
        RocketSensorPackage _sensors = new();
        
        // Telemetry identity
        int _areaIndex;   // set by TrainingAreaManager after spawn
        int _episode;
        int _step;
        float _stepReward;   // accumulator so we can log reward per step
        bool _hasEpisodeStarted;
        bool _currentEpisodeCompleted;
        bool _telemetryLoggedThisStep;
        bool _episodeEndedThisStep;
        bool _landingEpisodeEndLogged;
        bool _hoverTrackTargetReachedThisStep;
        bool _landingEpisodeSucceeded;
        EpisodeTerminationReason _episodeTerminationReason;
        int _hoverTrackEpisodeCaptures;
        


        [Header("Flame Visual")] public float minFlameWidth = 1.5f;
        public float maxFlameWidth = 4f;
        public float minFlameLength = 2f;
        public float maxFlameLength = 20f;

        // ── Physics config ───────────────────────────
        RocketPhysicsConfig cfg;
        
        // ── Engine ─────────────────────────────────────────────────
        Vector2[] targetGimbal, gimbal;
        float[] commandedThrottle, targetThrottle, throttle;
        private float fuel;
        private float rcsPropellant;

        EngineRunState[] engineStates;
        float[] engineStateTimers;
        float[] engineRunTimes;
        float[] engineOffTimes;

        // Fins 
        float[] finAngles;
        float[] targetFinAngles;

        // RCS
        float[] rcsValveRequests;
        float[] rcsValveStates;
        float[] rcsPulseTimeRemaining;

        // ── Wind ──────────────────────────────────────────────────────────────
        Vector3 wind, targetWind;
        Vector3 _episodePrevailingWind;
        DeterministicRandom _episodeRandom;
        int _episodeSeed;

        // Up to three simple actuator faults can be active in one episode.
        // Fixed arrays avoid allocations inside the physics loop.
        readonly RocketFaultType[] _episodeFaultTypes = new RocketFaultType[3];
        readonly int[] _episodeFaultTargets = new int[3];
        readonly float[] _episodeFaultSeverities = new float[3];
        int _episodeFaultCount;
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
        float _hoverTrackSegmentStartDistance;
        float _hoverTrackSegmentElapsedTime;
        LandingPlatformComponent _landingPlatform;
        bool _landingPlatformInsideCapture;
        bool _landingPlatformStable;
        float _landingPlatformStableTime;
        LandingCurriculumProfile _landingEpisodeProfile;
        bool _landingEpisodeProfileInitialized;
        bool _landingEpisodeUsesEasierReplay;
        float _landingEpisodeStartAltitude;
        float _landingEpisodeFlyawayAltitude;

        const float Rho0 = 1.225f; // ISA sea-level density (kg/m³)
        const float HScale = 8500f; // ISA scale height (m)
        const float G0 = 9.80665f;
        const float BaseGroundClearance = 0.5f;
        const float HoverTrackCycleCompleteReward = 4.0f;
        const float LandingFlyawayAltitudeMargin = 100f;
        const float EngineIgnitionThreshold = 0.08f;
        const float EngineShutdownThreshold = 0.03f;
        const float RcsValveActionThreshold = 0.5f;
        const float VacuumThrustMultiplier = 1.08f;
        const float VacuumIspMultiplier = 1.10f;
        const float EngineCommandEpsilon = 0.01f;

        //public getters for HUD
        public int GetEngineCount => throttle?.Length ?? 0;
        /// <summary>
        /// Reads one bounded engine throttle channel after spool smoothing so HUD and hardware tests can show actuator state safely.
        /// </summary>
        public float GetCurrentThrottle(int index) =>
            throttle != null && index >= 0 && index < throttle.Length ? throttle[index] : 0f;
        public float GetFuel => fuel;
        /// <summary>
        /// Reads one bounded engine gimbal X channel in degrees for HUD and hardware-test displays.
        /// </summary>
        public float GetGimbalX(int index) =>
            gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].x : 0f;
        /// <summary>
        /// Reads one bounded engine gimbal Z channel in degrees for HUD and hardware-test displays.
        /// </summary>
        public float GetGimbalZ(int index) =>
            gimbal != null && index >= 0 && index < gimbal.Length ? gimbal[index].y : 0f;
        public RocketPhysicsConfig CurrentPhysicsConfig => cfg;
        public float DynamicPressure => q;
        public float AngleOfAttackDeg => aoaDeg;
        public bool HardwareTestMode => _hardwareTestMode;
        
        void ResetEpisodeRandom()
        {
            int baseSeed = envConfig != null ? envConfig.environmentSeed : 1;
            _episodeSeed = DeterministicRandom.EpisodeSeed(baseSeed, _areaIndex, _episode);
            _episodeRandom = new DeterministicRandom(_episodeSeed);
        }

        float RandomRange(float min, float max) => _episodeRandom.Range(min, max);

        Vector2 RandomInsideUnitCircle() => _episodeRandom.InsideUnitCircle();

        Vector3 RandomUnitVector3() => _episodeRandom.UnitVector3();

        /// <summary>
        /// Returns the landing thresholds frozen at episode start. Editor
        /// previews fall back to the shared current profile before an episode exists.
        /// </summary>
        LandingCurriculumProfile ActiveLandingProfile =>
            _landingEpisodeProfileInitialized
                ? _landingEpisodeProfile
                : envConfig.GetLandingCurriculumProfile(envConfig.landingCurriculumProgress);

        /// <summary>
        /// Freezes one task difficulty for the whole episode and independently
        /// samples the controlled easier-task replay condition.
        /// </summary>
        void PrepareLandingEpisodeProfile()
        {
            _landingEpisodeProfileInitialized = false;
            _landingEpisodeUsesEasierReplay = false;
            if (envConfig == null || envConfig.scenario != ScenarioType.Landing)
                return;

            // Use a separate deterministic stream so enabling replay does not
            // shift spawn, wind, or fault samples for the same experiment seed.
            var curriculumRandom = new DeterministicRandom(DeterministicRandom.EpisodeSeed(
                envConfig.environmentSeed,
                _areaIndex,
                _episode,
                stream: 1));
            _landingEpisodeUsesEasierReplay =
                envConfig.behaviorType == BehaviorType.Training &&
                envConfig.landingCurriculumEnabled &&
                envConfig.landingCurriculumProgress > 0f &&
                curriculumRandom.Chance(envConfig.landingCurriculumEasierReplayProbability);
            float difficulty = envConfig.LandingEpisodeDifficulty(_landingEpisodeUsesEasierReplay);
            _landingEpisodeProfile = envConfig.GetLandingCurriculumProfile(difficulty);
            _landingEpisodeProfileInitialized = true;
        }

        Vector3 RandomPlanarVector(float maxMagnitude)
        {
            if (maxMagnitude <= 0f) return Vector3.zero;

            float angle = RandomRange(0f, Mathf.PI * 2f);
            float magnitude = RandomRange(0f, maxMagnitude);
            return new Vector3(Mathf.Cos(angle) * magnitude, 0f, Mathf.Sin(angle) * magnitude);
        }
        static string PassFail(bool passed) => passed ? "PASS" : "FAIL";
    }
}
