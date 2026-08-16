// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketHardwarePresetFactory.cs
// Purpose: Writes the two built-in vehicle starting points into
// RocketPartsConfig. Custom and user-saved vehicles are never overwritten.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    internal static class RocketHardwarePresetFactory
    {
        /// <summary>
        /// Reapplies the selected preset unless the user has marked the hardware
        /// as custom, keeping serialized configs aligned with preset definitions.
        /// </summary>
        public static void NormalizeSelectedPreset(RocketPartsConfig cfg)
        {
            if (cfg.hardwarePreset == RocketHardwarePreset.Custom) return;

            ApplyPreset(cfg, cfg.hardwarePreset);
        }

        /// <summary>
        /// Writes the requested hardware preset into the mutable parts config,
        /// starting from Falcon 9 defaults and overriding sandbox-specific fields.
        /// </summary>
        public static void ApplyPreset(RocketPartsConfig cfg, RocketHardwarePreset preset)
        {
            if (preset == RocketHardwarePreset.Custom)
            {
                cfg.hardwarePreset = RocketHardwarePreset.Custom;
                return;
            }

            cfg.hardwarePreset = preset;
            ApplyFalcon9Base(cfg);

            switch (preset)
            {
                case RocketHardwarePreset.Falcon9:
                    break;

                case RocketHardwarePreset.SimpleSingle:
                    cfg.engineLayout = EngineLayout.Single;
                    cfg.independentEngines = false;
                    cfg.minThrottle = RocketPartsConfig.SandboxMinThrottle;
                    cfg.maxGimbalAngle = RocketPartsConfig.SandboxMaxGimbalDeg;
                    ApplySandboxEngineTiming(cfg);
                    cfg.finsEnabled = false;
                    cfg.rcsEnabled = false;
                    break;

            }

            cfg.ClampPhysicalRanges();
        }

        /// <summary>
        /// Restores the full Falcon 9 baseline dimensions, engines, fins, and
        /// RCS values before a preset applies its narrower overrides.
        /// </summary>
        static void ApplyFalcon9Base(RocketPartsConfig cfg)
        {
            cfg.bodyRadius = RocketPartsConfig.ReferenceBodyRadiusM;
            cfg.bodyHeight = RocketPartsConfig.ReferenceBodyHeightM;
            cfg.baseDryMass = Falcon9Reference.BoosterDryMassKg;
            cfg.baseFuelMass = RocketPartsConfig.ReferenceFuelCapacityKg;
            cfg.useScenarioRecommendedFuel = true;
            cfg.startFuelFraction = ScenarioProfile.StartFuelFraction(ScenarioType.ChopstickLanding);
            cfg.startFuelMass = cfg.startFuelFraction * RocketPartsConfig.ReferenceFuelCapacityKg;

            cfg.engineLayout = EngineLayout.Octaweb;
            cfg.independentEngines = true;
            cfg.octawebBurnGroup = OctawebBurnGroup.CenterPlusTwo;
            cfg.maxThrustPerEngine = Falcon9Reference.MerlinSeaLevelThrustN;
            cfg.minThrottle = RocketPartsConfig.Falcon9MinThrottle;
            cfg.engineSpacing = 0.8f;
            cfg.specificImpulse = Falcon9Reference.MerlinSeaLevelSpecificImpulseS;
            cfg.throttleSpoolRate = 5f;
            cfg.gimbalSlewRate = 30f;
            cfg.maxGimbalAngle = RocketPartsConfig.Falcon9MaxGimbalDeg;
            cfg.engineStartupDelay = RocketPartsConfig.Falcon9EngineStartupDelayS;
            cfg.engineShutdownTransient = RocketPartsConfig.Falcon9EngineShutdownTransientS;
            cfg.engineMinimumRunTime = RocketPartsConfig.Falcon9EngineMinimumRunTimeS;
            cfg.engineRestartCooldown = RocketPartsConfig.Falcon9EngineRestartCooldownS;

            cfg.finsEnabled = true;
            cfg.finLayout = FinLayout.FourFins_Plus;
            cfg.finWidthX = RocketPartsConfig.DefaultFinRadialLengthM;
            cfg.finWidthZ = RocketPartsConfig.DefaultFinTangentialWidthM;
            cfg.finThickness = RocketPartsConfig.DefaultFinThicknessM;
            cfg.maxFinAngle = 35f;
            cfg.finSlewRate = 50f;
            cfg.liftScale = RocketPartsConfig.DefaultGridFinLiftScale;

            cfg.rcsEnabled = true;
            cfg.rcsThrust = RocketPartsConfig.DefaultRcsThrustN;
            cfg.rcsSpecificImpulse = RocketPartsConfig.DefaultRcsSpecificImpulseS;
            cfg.rcsMinimumPulseDuration = RocketPartsConfig.DefaultRcsMinimumPulseS;
            cfg.rcsPropellantMass = RocketPartsConfig.DefaultRcsPropellantMassKg;
            cfg.rcsDryMass = RocketPartsConfig.DefaultRcsDryMassKg;
        }

        /// <summary>
        /// Uses faster, less restrictive engine timing for simplified sandbox
        /// presets so training experiments are easier to control.
        /// </summary>
        static void ApplySandboxEngineTiming(RocketPartsConfig cfg)
        {
            cfg.engineStartupDelay = RocketPartsConfig.SandboxEngineStartupDelayS;
            cfg.engineShutdownTransient = RocketPartsConfig.SandboxEngineShutdownTransientS;
            cfg.engineMinimumRunTime = RocketPartsConfig.SandboxEngineMinimumRunTimeS;
            cfg.engineRestartCooldown = RocketPartsConfig.SandboxEngineRestartCooldownS;
        }
    }
}
