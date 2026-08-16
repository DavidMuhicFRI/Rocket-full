// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/SimulatorPreflightValidator.cs
// Purpose: Reports invalid configurations and conservative landing capability.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    internal static class SimulatorPreflightValidator
    {
        const float G0 = 9.80665f;

        /// <summary>
        /// Checks configuration consistency. Fatal findings are errors; values
        /// that the coupled sampler can safely clip are warnings.
        /// </summary>
        public static bool ValidateAndLog(RocketPhysicsConfig cfg, SimEnvironmentConfig env)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            ValidateObjective(env, errors, warnings);

            float startFuel = Mathf.Clamp(cfg.startFuelMass, 0f, cfg.maxFuelMass);
            float startMass = cfg.dryMass + startFuel + Mathf.Max(0f, cfg.rcsPropellantMass);
            int engines = Mathf.Max(0, cfg.activeEngineCount);
            float totalThrust = Mathf.Max(0f, cfg.maxThrust) * engines;
            float maxTwr = startMass > 0f ? totalThrust / (startMass * G0) : 0f;
            float minThrottleTwr = maxTwr * Mathf.Clamp01(cfg.minThrottle);

            ValidateVehicle(cfg, startFuel, engines, errors, warnings);
            ValidateFeasibilityMath(errors);

            if (env != null && env.scenario.IsLanding())
                ValidateLandingCapability(cfg, env, startMass, engines, maxTwr, minThrottleTwr, errors, warnings);

            foreach (string warning in warnings)
                Debug.LogWarning($"[SimulatorPreflight] {warning}.");
            foreach (string error in errors)
                Debug.LogError($"[SimulatorPreflight] {error}.");

            return errors.Count == 0;
        }

        static void ValidateObjective(
            SimEnvironmentConfig env,
            ICollection<string> errors,
            ICollection<string> warnings)
        {
            if (env == null) return;

            ObjectiveValidationResult result = ObjectiveValidator.Validate(
                env.scenario,
                env.GetTrainingObjective(env.scenario));
            foreach (ObjectiveValidationIssue issue in result.issues)
            {
                if (issue.severity == ObjectiveValidationSeverity.Error)
                    errors.Add($"objective: {issue.message}");
                else
                    warnings.Add($"objective: {issue.message}");
            }
        }

        static void ValidateVehicle(
            RocketPhysicsConfig cfg,
            float startFuel,
            int engines,
            ICollection<string> errors,
            ICollection<string> warnings)
        {
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
        }

        static void ValidateFeasibilityMath(ICollection<string> errors)
        {
            const float acceleration = 10f;
            float stopAtTen = LandingFeasibility.RequiredStoppingDistance(10f, acceleration, 0.5f, G0);
            float stopAtTwenty = LandingFeasibility.RequiredStoppingDistance(20f, acceleration, 0.5f, G0);
            if (!(stopAtTen > 0f && stopAtTwenty > stopAtTen))
                errors.Add("landing stopping-distance model failed its monotonicity self-check");

            const float altitude = 200f;
            float speed = LandingFeasibility.MaxRecoverableDownwardSpeed(altitude, acceleration, 0.5f, G0);
            float usableDistance = (altitude - LandingFeasibility.MinimumAltitudeMarginM) *
                                   LandingFeasibility.DefaultDistanceReserveFraction;
            if (LandingFeasibility.RequiredStoppingDistance(speed, acceleration, 0.5f, G0) >
                usableDistance + 0.1f)
                errors.Add("landing recoverable-speed solver exceeded its reserved distance");
        }

        static void ValidateLandingCapability(
            RocketPhysicsConfig cfg,
            SimEnvironmentConfig env,
            float startMass,
            int engines,
            float maxTwr,
            float minThrottleTwr,
            ICollection<string> errors,
            ICollection<string> warnings)
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

            LandingCurriculumProfile initial = env.GetActiveLandingCurriculumProfile(0f);
            LandingCurriculumProfile full = env.GetActiveLandingCurriculumProfile(1f);
            float initialSpeed = RecoverableSpeedAtMinimumAltitude(initial, terminalAltitude, netAcceleration, cfg);
            float fullSpeed = RecoverableSpeedAtMinimumAltitude(full, terminalAltitude, netAcceleration, cfg);

            if (initial.verticalSpeedMax > initialSpeed + 0.1f)
                warnings.Add(
                    $"initial downward-speed range reaches {initial.verticalSpeedMax:F1} m/s; " +
                    $"the coupled sampler will cap it near {initialSpeed:F1} m/s at minimum altitude");
            if (full.verticalSpeedMax > fullSpeed + 0.1f)
                warnings.Add(
                    $"full downward-speed range reaches {full.verticalSpeedMax:F1} m/s; " +
                    $"the coupled sampler will cap it near {fullSpeed:F1} m/s at minimum altitude");

            float deltaV = cfg.specificImpulse > 0f && startMass > cfg.dryMass
                ? cfg.specificImpulse * G0 * Mathf.Log(startMass / Mathf.Max(1f, cfg.dryMass + cfg.rcsPropellantMass))
                : 0f;
            string guidanceFrame = env.scenario == ScenarioType.LegLanding ? "feetFrame" : "catchFrame";
            float guidanceFrameLocalY = env.scenario == ScenarioType.LegLanding
                ? LandingLegComponent.ReferenceFootPlaneLocalY *
                  cfg.radius / LandingLegComponent.ReferenceBodyRadiusM
                : cfg.finLocalY;
            Debug.Log(
                $"[SimulatorPreflight] landing capability: startMass={startMass:F0} kg, " +
                $"maxTWR={maxTwr:F2}, minThrottleTWR={minThrottleTwr:F2}, " +
                $"actualNetDeceleration={netAcceleration:F2} m/s^2, idealDeltaV={deltaV:F0} m/s, " +
                $"recoverableDownwardSpeed=[initialMinAltitude:{initialSpeed:F1}, " +
                $"fullMinAltitude:{fullSpeed:F1}] m/s, " +
                $"{guidanceFrame}Y={guidanceFrameLocalY:F1} m, targetAltitude={terminalAltitude:F1} m, " +
                $"fixedDeltaTime={Time.fixedDeltaTime:F4} s.");
        }

        static float RecoverableSpeedAtMinimumAltitude(
            LandingCurriculumProfile profile,
            float terminalAltitude,
            float netAcceleration,
            RocketPhysicsConfig cfg) =>
            LandingFeasibility.MaxRecoverableDownwardSpeed(
                Mathf.Max(0f, profile.spawnAltitudeMin - terminalAltitude),
                netAcceleration,
                cfg.engineStartupDelay,
                G0);
    }
}
