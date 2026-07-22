// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LandingFeasibility.cs
// Purpose: Provides conservative, testable reachability estimates used to keep
// randomized terminal-guidance starts inside the booster's control envelope.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    public static class LandingFeasibility
    {
        public const float DefaultUsableThrustFraction = 0.82f;
        public const float DefaultDistanceReserveFraction = 0.80f;
        public const float MinimumAltitudeMarginM = 8f;

        /// <summary>Returns usable upward acceleration after gravity.</summary>
        public static float NetUpwardAcceleration(
            float thrustPerEngineN,
            int activeEngineCount,
            float vehicleMassKg,
            float gravity,
            float usableThrustFraction = DefaultUsableThrustFraction)
        {
            float mass = Mathf.Max(vehicleMassKg, 1f);
            float thrust = Mathf.Max(0f, thrustPerEngineN) * Mathf.Max(0, activeEngineCount);
            float grossAcceleration = thrust * Mathf.Clamp01(usableThrustFraction) / mass;
            return Mathf.Max(0f, grossAcceleration - Mathf.Max(0f, gravity));
        }

        /// <summary>
        /// Estimates vertical distance consumed by ignition delay followed by a
        /// constant maximum-deceleration burn from an initial downward speed.
        /// </summary>
        public static float RequiredStoppingDistance(
            float downwardSpeed,
            float netUpwardAcceleration,
            float ignitionDelay,
            float gravity)
        {
            float speed = Mathf.Max(0f, downwardSpeed);
            float delay = Mathf.Max(0f, ignitionDelay);
            float g = Mathf.Max(0f, gravity);
            float acceleration = Mathf.Max(0.001f, netUpwardAcceleration);

            float delayDistance = speed * delay + 0.5f * g * delay * delay;
            float speedAfterDelay = speed + g * delay;
            float brakingDistance = speedAfterDelay * speedAfterDelay / (2f * acceleration);
            return delayDistance + brakingDistance;
        }

        /// <summary>
        /// Finds the largest initial downward speed whose stopping distance fits
        /// inside a reserved fraction of the available altitude.
        /// </summary>
        public static float MaxRecoverableDownwardSpeed(
            float availableAltitude,
            float netUpwardAcceleration,
            float ignitionDelay,
            float gravity,
            float distanceReserveFraction = DefaultDistanceReserveFraction)
        {
            float usableDistance = Mathf.Max(0f, availableAltitude - MinimumAltitudeMarginM) *
                                   Mathf.Clamp01(distanceReserveFraction);
            if (usableDistance <= 0f || netUpwardAcceleration <= 0f)
                return 0f;

            float low = 0f;
            float high = 300f;
            for (int i = 0; i < 28; i++)
            {
                float mid = (low + high) * 0.5f;
                if (RequiredStoppingDistance(mid, netUpwardAcceleration, ignitionDelay, gravity) <= usableDistance)
                    low = mid;
                else
                    high = mid;
            }

            return low;
        }

        /// <summary>
        /// Conservative lateral correction estimate based on gimbal authority
        /// and approximate time remaining before the catch altitude.
        /// </summary>
        public static void HorizontalEnvelope(
            float availableAltitude,
            float downwardSpeed,
            float thrustPerEngineN,
            int activeEngineCount,
            float vehicleMassKg,
            float maxGimbalDeg,
            float ignitionDelay,
            float gravity,
            out float maxCorrectableSpeed,
            out float maxCorrectableOffset)
        {
            float mass = Mathf.Max(vehicleMassKg, 1f);
            float thrustAcceleration = Mathf.Max(0f, thrustPerEngineN) * Mathf.Max(0, activeEngineCount) /
                                       mass * DefaultUsableThrustFraction;
            float lateralAcceleration = thrustAcceleration * Mathf.Sin(Mathf.Max(0f, maxGimbalDeg) * Mathf.Deg2Rad);
            float g = Mathf.Max(0.001f, gravity);
            float speed = Mathf.Max(0f, downwardSpeed);
            float altitude = Mathf.Max(0f, availableAltitude);
            // Time to the capture plane under ballistic descent is a safer
            // control-horizon estimate than altitude/current-speed, which grows
            // unrealistically large when the sampled downward speed is small.
            float flightTime = (Mathf.Sqrt(speed * speed + 2f * g * altitude) - speed) / g;
            float controlTime = Mathf.Max(0f, flightTime - Mathf.Max(0f, ignitionDelay));

            // Leave half of the theoretical lateral authority for simultaneous
            // attitude, vertical-speed, and position control.
            maxCorrectableSpeed = lateralAcceleration * controlTime * 0.5f;
            maxCorrectableOffset = 0.5f * lateralAcceleration * controlTime * controlTime * 0.5f;
        }
    }

    internal static class SimulatorPreflightValidator
    {
        const float G0 = 9.80665f;

        /// <summary>
        /// Checks actuator/config consistency and prints a landing capability
        /// summary. Fatal findings are logged as errors; conservative envelope
        /// clipping is reported as a warning so it is visible before training.
        /// </summary>
        public static bool ValidateAndLog(RocketPhysicsConfig cfg, SimEnvironmentConfig env)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            float startFuel = Mathf.Clamp(cfg.startFuelMass, 0f, cfg.maxFuelMass);
            float startMass = cfg.dryMass + startFuel + Mathf.Max(0f, cfg.rcsPropellantMass);
            int engines = Mathf.Max(0, cfg.activeEngineCount);
            float totalThrust = Mathf.Max(0f, cfg.maxThrust) * engines;
            float maxTwr = startMass > 0f ? totalThrust / (startMass * G0) : 0f;
            float minThrottleTwr = maxTwr * Mathf.Clamp01(cfg.minThrottle);

            if (cfg.length <= 0f || cfg.radius <= 0f || cfg.dryMass <= 0f)
                errors.Add("vehicle dimensions and dry mass must be positive");
            if (engines <= 0 || cfg.maxThrust <= 0f)
                errors.Add("at least one active engine with positive thrust is required");
            if (startFuel <= 0f)
                errors.Add("landing starts with no main-engine propellant");
            if (cfg.minThrottle < 0f || cfg.minThrottle > 1f)
                errors.Add("minimum throttle must be in the 0..1 range");
            if (cfg.engineStartupDelay < 0f || cfg.throttleSpoolRate <= 0f)
                errors.Add("engine startup delay and throttle spool rate are invalid");
            if (cfg.finLocalY < 0f || cfg.finLocalY > cfg.length)
                errors.Add("catch-frame/fin station lies outside the vehicle body");
            if (cfg.independentEngines && cfg.independentEngineCount != engines)
                warnings.Add($"{cfg.independentEngineCount} engine action channels control {engines} active engines");
            if (!cfg.independentEngines && cfg.independentEngineCount != 1)
                warnings.Add("shared engine control should expose exactly one action channel");
            if (Time.fixedDeltaTime <= 0f)
                errors.Add("Unity fixed timestep must be positive");

            // Executable regression checks for the reachability math itself.
            float checkAcceleration = 10f;
            float stopAtTen = LandingFeasibility.RequiredStoppingDistance(10f, checkAcceleration, 0.5f, G0);
            float stopAtTwenty = LandingFeasibility.RequiredStoppingDistance(20f, checkAcceleration, 0.5f, G0);
            if (!(stopAtTen > 0f && stopAtTwenty > stopAtTen))
                errors.Add("landing stopping-distance model failed its monotonicity self-check");

            float checkAltitude = 200f;
            float checkSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(
                checkAltitude,
                checkAcceleration,
                0.5f,
                G0);
            float checkUsableDistance = (checkAltitude - LandingFeasibility.MinimumAltitudeMarginM) *
                                        LandingFeasibility.DefaultDistanceReserveFraction;
            if (LandingFeasibility.RequiredStoppingDistance(checkSpeed, checkAcceleration, 0.5f, G0) >
                checkUsableDistance + 0.1f)
                errors.Add("landing recoverable-speed solver exceeded its reserved distance");

            if (env != null && env.scenario.IsLanding())
            {
                float terminalAltitude = env.scenario == ScenarioType.ChopstickLanding
                    ? env.landingCatchAltitude
                    : 0f;
                if (env.scenario == ScenarioType.ChopstickLanding)
                {
                    float captureRootAltitude = env.landingCatchAltitude - cfg.finLocalY;
                    if (captureRootAltitude < 1f)
                        errors.Add(
                            $"catch altitude {env.landingCatchAltitude:F1} m places the engine plane " +
                            $"at {captureRootAltitude:F1} m during capture");
                }

                float netAcceleration = LandingFeasibility.NetUpwardAcceleration(
                    cfg.maxThrust,
                    engines,
                    startMass,
                    G0);
                if (netAcceleration <= 0f)
                    errors.Add($"maximum landing-burn TWR is {maxTwr:F2}, so the vehicle cannot decelerate upward");

                float initialAvailableAltitude = Mathf.Max(
                    0f,
                    SimEnvironmentConfig.LandingInitialSpawnAltitudeMin - terminalAltitude);
                float fullAvailableAltitude = Mathf.Max(
                    0f,
                    SimEnvironmentConfig.LandingFullSpawnAltitudeMin - terminalAltitude);
                float initialRecoverableSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(
                    initialAvailableAltitude,
                    netAcceleration,
                    cfg.engineStartupDelay,
                    G0);
                float fullRecoverableSpeed = LandingFeasibility.MaxRecoverableDownwardSpeed(
                    fullAvailableAltitude,
                    netAcceleration,
                    cfg.engineStartupDelay,
                    G0);

                if (SimEnvironmentConfig.LandingInitialVerticalSpeedMax > initialRecoverableSpeed + 0.1f)
                    warnings.Add(
                        $"initial downward-speed range reaches {SimEnvironmentConfig.LandingInitialVerticalSpeedMax:F1} m/s; " +
                        $"the coupled sampler will cap it near {initialRecoverableSpeed:F1} m/s at minimum altitude");
                if (SimEnvironmentConfig.LandingFullVerticalSpeedMax > fullRecoverableSpeed + 0.1f)
                    warnings.Add(
                        $"full downward-speed range reaches {SimEnvironmentConfig.LandingFullVerticalSpeedMax:F1} m/s; " +
                        $"the coupled sampler will cap it near {fullRecoverableSpeed:F1} m/s at minimum altitude");

                float deltaV = cfg.specificImpulse > 0f && startMass > cfg.dryMass
                    ? cfg.specificImpulse * G0 * Mathf.Log(startMass / Mathf.Max(1f, cfg.dryMass + cfg.rcsPropellantMass))
                    : 0f;
                string guidanceFrame = env.scenario == ScenarioType.LegLanding ? "feetFrame" : "catchFrame";
                float guidanceFrameLocalY = env.scenario == ScenarioType.LegLanding
                    ? LandingLegAssembly.ReferenceFootPlaneLocalY *
                      cfg.radius / LandingLegAssembly.ReferenceBodyRadiusM
                    : cfg.finLocalY;
                Debug.Log(
                    $"[SimulatorPreflight] landing capability: startMass={startMass:F0} kg, " +
                    $"maxTWR={maxTwr:F2}, minThrottleTWR={minThrottleTwr:F2}, " +
                    $"netDeceleration={netAcceleration:F2} m/s^2, idealDeltaV={deltaV:F0} m/s, " +
                    $"recoverableDownwardSpeed=[initialMinAltitude:{initialRecoverableSpeed:F1}, " +
                    $"fullMinAltitude:{fullRecoverableSpeed:F1}] m/s, " +
                    $"{guidanceFrame}Y={guidanceFrameLocalY:F1} m, targetAltitude={terminalAltitude:F1} m, " +
                    $"fixedDeltaTime={Time.fixedDeltaTime:F4} s.");
            }

            foreach (string warning in warnings)
                Debug.LogWarning($"[SimulatorPreflight] {warning}.");
            foreach (string error in errors)
                Debug.LogError($"[SimulatorPreflight] {error}.");

            return errors.Count == 0;
        }
    }
}
