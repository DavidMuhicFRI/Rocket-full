// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketPartsConfig.cs
// Purpose: Stores user-editable rocket hardware settings and applies hardware defaults used by the simulator.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// User-tunable rocket hardware parameters. One shared instance lives on
    /// TrainingAreaManager; the UI edits it and RocketAssembly applies it to
    /// every active training/inference area.
    /// </summary>
    [Serializable]
    public class RocketPartsConfig
    {
        // Falcon 9 titanium grid-fin envelope, estimated from public imagery.
        // The lift scale corrects the ideal 2*pi thin-plate lift slope used by
        // FalconAgent for lattice porosity and cell-flow interference.
        public const float DefaultFinRadialLengthM = Falcon9Reference.GridFinRadialLengthM;
        public const float DefaultFinTangentialWidthM = Falcon9Reference.GridFinTangentialWidthM;
        public const float DefaultFinThicknessM = Falcon9Reference.GridFinThicknessM;
        public const float DefaultGridFinLiftScale = Falcon9Reference.GridFinLiftScale;
        public const float DefaultRcsThrustN = Falcon9Reference.RcsThrustN;
        public const float DefaultRcsSpecificImpulseS = Falcon9Reference.RcsSpecificImpulseS;
        public const float DefaultRcsMinimumPulseS = Falcon9Reference.RcsMinimumPulseS;
        public const float DefaultRcsPropellantMassKg = Falcon9Reference.RcsPropellantMassKg;
        public const float DefaultRcsDryMassKg = Falcon9Reference.RcsDryMassKg;
        public const float Falcon9MinThrottle = Falcon9Reference.MerlinMinThrottle;
        public const float Falcon9MaxGimbalDeg = Falcon9Reference.MerlinMaxGimbalDeg;
        public const float SandboxMinThrottle = Falcon9Reference.SandboxMinThrottle;
        public const float SandboxMaxGimbalDeg = Falcon9Reference.SandboxMaxGimbalDeg;
        public const float Falcon9EngineStartupDelayS = Falcon9Reference.MerlinStartupDelayS;
        public const float Falcon9EngineShutdownTransientS = Falcon9Reference.MerlinShutdownTransientS;
        public const float Falcon9EngineMinimumRunTimeS = Falcon9Reference.MerlinMinimumRunTimeS;
        public const float Falcon9EngineRestartCooldownS = Falcon9Reference.MerlinRestartCooldownS;
        public const float SandboxEngineStartupDelayS = Falcon9Reference.SandboxEngineStartupDelayS;
        public const float SandboxEngineShutdownTransientS = Falcon9Reference.SandboxEngineShutdownTransientS;
        public const float SandboxEngineMinimumRunTimeS = Falcon9Reference.SandboxEngineMinimumRunTimeS;
        public const float SandboxEngineRestartCooldownS = Falcon9Reference.SandboxEngineRestartCooldownS;
        public const float ReferenceBodyRadiusM = Falcon9Reference.BodyRadiusM;
        public const float ReferenceBodyHeightM = Falcon9Reference.BodyHeightM;
        public const float ReferenceFuelCapacityKg = Falcon9Reference.FuelCapacityKg;

        [Header("Preset")]
        public RocketHardwarePreset hardwarePreset = RocketHardwarePreset.Falcon9;

        [Header("Body")]
        [Range(0.5f, 6f)] public float bodyRadius = Falcon9Reference.BodyRadiusM;
        [Range(5f, 80f)] public float bodyHeight = Falcon9Reference.BodyHeightM;
        public float baseDryMass = Falcon9Reference.BoosterDryMassKg;
        public float baseFuelMass = Falcon9Reference.FuelCapacityKg;
        public bool useScenarioRecommendedFuel = true;
        [Range(0f, 1f)] public float startFuelFraction = ScenarioCatalog.ChopstickLandingStartFuelFraction;
        public float startFuelMass = 32000f;

        [Header("Thruster")]
        public EngineLayout engineLayout = EngineLayout.Octaweb;
        public bool independentEngines = true;
        public OctawebBurnGroup octawebBurnGroup = OctawebBurnGroup.CenterOnly;
        [Range(50000f, 1200000f)] public float maxThrustPerEngine = Falcon9Reference.MerlinSeaLevelThrustN;
        [Range(0.15f, 0.80f)] public float minThrottle = Falcon9MinThrottle;
        [Range(0.2f, 1f)] public float engineSpacing = 0.8f;
        [Range(200f, 450f)] public float specificImpulse = Falcon9Reference.MerlinSeaLevelSpecificImpulseS;
        [Range(0.5f, 20f)] public float throttleSpoolRate = 5f;
        [Range(5f, 90f)] public float gimbalSlewRate = 30f;
        [Range(1f, 15f)] public float maxGimbalAngle = Falcon9MaxGimbalDeg;
        [Range(0f, 3f)] public float engineStartupDelay = Falcon9EngineStartupDelayS;
        [Range(0f, 1f)] public float engineShutdownTransient = Falcon9EngineShutdownTransientS;
        [Range(0f, 5f)] public float engineMinimumRunTime = Falcon9EngineMinimumRunTimeS;
        [Range(0f, 5f)] public float engineRestartCooldown = Falcon9EngineRestartCooldownS;

        [Header("Grid Fins")]
        public bool finsEnabled = true;
        public FinLayout finLayout = FinLayout.FourFins_Plus;
        [Range(0.2f, 4f)] public float finWidthX = DefaultFinRadialLengthM;
        [Range(0.2f, 4f)] public float finWidthZ = DefaultFinTangentialWidthM;
        [Range(0.1f, 1f)] public float finThickness = DefaultFinThicknessM;
        [Range(5f, 60f)] public float maxFinAngle = 35f;
        [Range(10f, 200f)] public float finSlewRate = 50f;
        [Range(0.1f, 3f)] public float liftScale = DefaultGridFinLiftScale;

        [Header("RCS")]
        public bool rcsEnabled = true;
        [Range(100f, 2000f)] public float rcsThrust = DefaultRcsThrustN;
        [Range(30f, 100f)] public float rcsSpecificImpulse = DefaultRcsSpecificImpulseS;
        [Range(0.02f, 0.5f)] public float rcsMinimumPulseDuration = DefaultRcsMinimumPulseS;
        [Range(0f, 1000f)] public float rcsPropellantMass = DefaultRcsPropellantMassKg;
        [Range(0f, 1000f)] public float rcsDryMass = DefaultRcsDryMassKg;

        /// <summary>
        /// Returns the number of engine objects installed by the selected
        /// layout, regardless of which burn group is active.
        /// </summary>
        public int GetEngineCount()
        {
            return engineLayout switch
            {
                EngineLayout.Single => 1,
                EngineLayout.Triple => 3,
                EngineLayout.Octaweb => 9,
                _ => 1
            };
        }

        /// <summary>
        /// Returns how many installed engines are allowed to ignite for the
        /// selected layout and octaweb burn group.
        /// </summary>
        public int GetActiveEngineCount()
        {
            return EngineBurnGroups.ActiveEngineCount(engineLayout, octawebBurnGroup);
        }

        /// <summary>
        /// Returns the number of engine command channels the agent needs after
        /// accounting for grouped versus independent control.
        /// </summary>
        public int GetIndependentEngineCount()
        {
            return EngineBurnGroups.ControlChannelCount(engineLayout, independentEngines, octawebBurnGroup);
        }

        /// <summary>
        /// Returns the number of enabled grid fins for the selected fin layout,
        /// or zero when fins are disabled.
        /// </summary>
        public int GetFinCount()
        {
            if (!finsEnabled) return 0;
            return finLayout switch
            {
                FinLayout.ThreeFins_120 => 3,
                FinLayout.FourFins_Plus => 4,
                FinLayout.FourFins_X => 4,
                _ => 4
            };
        }

        /// <summary>
        /// Returns the number of RCS jets exposed to the agent, or zero when
        /// the RCS hardware is disabled.
        /// </summary>
        public int GetRCSCount()
        {
            return rcsEnabled ? RcsComponent.JetCount : 0;
        }

        /// <summary>
        /// Estimates tank capacity by scaling the reference Falcon 9 fuel mass
        /// by the configured cylindrical body volume.
        /// </summary>
        public float EstimatedMaxFuelCapacity()
        {
            return baseFuelMass * (Mathf.PI * bodyRadius * bodyRadius * bodyHeight)
                                / (Mathf.PI * ReferenceBodyRadiusM * ReferenceBodyRadiusM * ReferenceBodyHeightM);
        }

        /// <summary>
        /// Clamps JSON-loaded or UI-edited hardware values to the same physical
        /// ranges used by the Unity inspectors before configs reach the agent.
        /// </summary>
        public void ClampPhysicalRanges()
        {
            // Matches the existing inspector ranges. This primarily protects JSON-loaded configs.
            bodyRadius = Mathf.Clamp(bodyRadius, 0.5f, 6f);
            bodyHeight = Mathf.Clamp(bodyHeight, 5f, 80f);
            float fuelCapacity = EstimatedMaxFuelCapacity();
            if (startFuelFraction <= 0f && startFuelMass > 0f)
                startFuelFraction = startFuelMass / Mathf.Max(fuelCapacity, 1f);
            startFuelFraction = Mathf.Clamp01(startFuelFraction);
            startFuelMass = startFuelFraction * fuelCapacity;

            maxThrustPerEngine = Mathf.Clamp(maxThrustPerEngine, 50000f, 1200000f);
            minThrottle = Mathf.Clamp(minThrottle, 0.15f, 0.80f);
            engineSpacing = Mathf.Clamp(engineSpacing, 0.2f, 1f);
            specificImpulse = Mathf.Clamp(specificImpulse, 200f, 450f);
            throttleSpoolRate = Mathf.Clamp(throttleSpoolRate, 0.5f, 20f);
            gimbalSlewRate = Mathf.Clamp(gimbalSlewRate, 5f, 90f);
            maxGimbalAngle = Mathf.Clamp(maxGimbalAngle, 1f, 15f);

            engineStartupDelay = Mathf.Clamp(engineStartupDelay, 0f, 3f);
            engineShutdownTransient = Mathf.Clamp(engineShutdownTransient, 0f, 1f);
            engineMinimumRunTime = Mathf.Clamp(engineMinimumRunTime, 0f, 5f);
            engineRestartCooldown = Mathf.Clamp(engineRestartCooldown, 0f, 5f);

            finWidthX = Mathf.Clamp(finWidthX, 0.2f, 4f);
            finWidthZ = Mathf.Clamp(finWidthZ, 0.2f, 4f);
            finThickness = Mathf.Clamp(finThickness, 0.1f, 1f);
            maxFinAngle = Mathf.Clamp(maxFinAngle, 5f, 60f);
            finSlewRate = Mathf.Clamp(finSlewRate, 10f, 200f);
            liftScale = Mathf.Clamp(liftScale, 0.1f, 3f);

            rcsThrust = Mathf.Clamp(rcsThrust, 100f, 2000f);
            rcsSpecificImpulse = Mathf.Clamp(rcsSpecificImpulse, 30f, 100f);
            rcsMinimumPulseDuration = Mathf.Clamp(rcsMinimumPulseDuration, 0.02f, 0.5f);
            rcsPropellantMass = Mathf.Clamp(rcsPropellantMass, 0f, 1000f);
            rcsDryMass = Mathf.Clamp(rcsDryMass, 0f, 1000f);
        }

        /// <summary>
        /// Applies the optional scenario-recommended starting fuel. Engine
        /// layout, burn group, and every other hardware choice remain exactly
        /// as selected by the user or vehicle preset.
        /// </summary>
        public void ApplyScenarioHardwareDefaults(ScenarioType scenario)
        {
            float capacity = EstimatedMaxFuelCapacity();
            if (useScenarioRecommendedFuel)
                startFuelFraction = ScenarioProfile.StartFuelFraction(scenario);
            else if (startFuelFraction <= 0f && startFuelMass > 0f)
                startFuelFraction = startFuelMass / Mathf.Max(capacity, 1f);

            startFuelFraction = Mathf.Clamp01(startFuelFraction);
            startFuelMass = startFuelFraction * capacity;
        }

        /// <summary>
        /// Replaces the mutable hardware fields with the selected preset values.
        /// </summary>
        public void ApplyPreset(RocketHardwarePreset preset) =>
            RocketHardwarePresetFactory.ApplyPreset(this, preset);

        /// <summary>
        /// Reapplies the selected preset unless this config has been marked as
        /// custom, keeping serialized preset-backed configs deterministic.
        /// </summary>
        public void NormalizeSelectedPreset() => RocketHardwarePresetFactory.NormalizeSelectedPreset(this);
    }
}
