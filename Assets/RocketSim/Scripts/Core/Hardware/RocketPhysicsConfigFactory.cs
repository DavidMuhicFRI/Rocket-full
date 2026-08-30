// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketPhysicsConfigFactory.cs
// Purpose: Derives an immutable runtime physics snapshot from vehicle hardware.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Keeps physical derivation separate from scene construction. Both live
    /// vehicles and pre-run manifests use the same hardware-mass assumptions.
    /// </summary>
    public static class RocketPhysicsConfigFactory
    {
        const int ReferenceEngineCount = 9;
        const int ReferenceFinCount = 4;
        const float EngineDryMassKg = 470f;
        const float GridFinDryMassKg = 200f;

        /// <summary>Fallback used only when a scene vehicle is incomplete.</summary>
        public static RocketPhysicsConfig Falcon9Fallback => Fallback;

        /// <summary>Builds a physics snapshot from configured scene components.</summary>
        public static RocketPhysicsConfig Create(
            BodyComponent body,
            ThrusterComponent thrusters,
            FinComponent fins,
            RcsComponent rcs)
        {
            if (!body) return Fallback;

            int engineCount = thrusters ? thrusters.EngineCount : ReferenceEngineCount;
            bool hasFins = fins && fins.gameObject.activeSelf;
            int finCount = hasFins ? fins.FinCount : 0;
            bool hasRcs = rcs && rcs.gameObject.activeSelf;
            float rcsDryMass = hasRcs ? rcs.dryMass : 0f;
            float rcsPropellantMass = hasRcs ? rcs.propellantMass : 0f;

            return new RocketPhysicsConfig
            {
                radius = body.radius,
                length = body.height,
                dryMass = AdjustDryMassForInstalledHardware(body.DryMass, engineCount, finCount, hasRcs, rcsDryMass),
                maxFuelMass = body.MaxFuelCapacity,
                startFuelMass = body.startFuelMass,

                A_axial = body.AxialArea,
                A_projectedSide = body.ProjectedSideArea,
                cpLocalY = body.CPLocalY,

                independentEngines = !thrusters || thrusters.independentEngines,
                separateEngineEnableActions = thrusters && thrusters.separateEngineEnableActions,
                independentEngineCount = thrusters ? thrusters.IndependentEngineCount : 1,
                engineLayout = thrusters ? thrusters.layout : EngineLayout.Octaweb,
                octawebBurnGroup = thrusters ? thrusters.octawebBurnGroup : OctawebBurnGroup.CenterOnly,
                activeEngineCount = thrusters ? thrusters.ActiveEngineCount : 1,
                maxThrust = thrusters ? thrusters.maxThrustPerEngine : 845000f,
                minThrottle = thrusters ? thrusters.minThrottle : RocketPartsConfig.Falcon9MinThrottle,
                burnRate = thrusters ? thrusters.BurnRate * thrusters.ActiveEngineCount : 305f,
                specificImpulse = thrusters ? thrusters.specificImpulse : 282f,
                engineCount = engineCount,
                engineDryMass = engineCount * EngineDryMassKg,
                throttleSpoolRate = thrusters ? thrusters.throttleSpoolRate : 5f,
                gimbalSlewRate = thrusters ? thrusters.gimbalSlewRate : 30f,
                maxGimbal = thrusters ? thrusters.maxGimbalAngle : RocketPartsConfig.Falcon9MaxGimbalDeg,
                engineStartupDelay = thrusters ? thrusters.engineStartupDelay : RocketPartsConfig.Falcon9EngineStartupDelayS,
                engineShutdownTransient = thrusters ? thrusters.engineShutdownTransient : RocketPartsConfig.Falcon9EngineShutdownTransientS,
                engineMinimumRunTime = thrusters ? thrusters.engineMinimumRunTime : RocketPartsConfig.Falcon9EngineMinimumRunTimeS,
                engineRestartCooldown = thrusters ? thrusters.engineRestartCooldown : RocketPartsConfig.Falcon9EngineRestartCooldownS,

                hasFins = hasFins,
                finCount = finCount,
                finSlewRate = fins ? fins.finSlewRate : 50f,
                maxFinAngle = fins ? fins.maxFinAngle : 35f,
                A_fin = fins ? fins.A_fin : RocketPartsConfig.DefaultFinRadialLengthM * RocketPartsConfig.DefaultFinTangentialWidthM,
                finLiftScale = fins ? fins.liftScale : RocketPartsConfig.DefaultGridFinLiftScale,
                finDryMass = finCount * GridFinDryMassKg,
                finLocalY = fins ? fins.transform.localPosition.y : body.height - 1.2f,

                hasRCS = hasRcs,
                rcsThrust = hasRcs ? rcs.thrustPerThruster : 0f,
                rcsSpecificImpulse = hasRcs ? rcs.specificImpulse : 0f,
                rcsMinimumPulseDuration = hasRcs ? rcs.minimumPulseDuration : 0f,
                rcsJetCount = hasRcs ? RcsComponent.JetCount : 0,
                rcsDryMass = rcsDryMass,
                rcsPropellantMass = rcsPropellantMass,
                rcsLocalY = rcs ? rcs.transform.localPosition.y : body.height - 0.2f
            };
        }

        /// <summary>
        /// Estimates runtime dry mass before a scene vehicle exists so run
        /// manifests and preflight use the same calculation as the assembly.
        /// </summary>
        public static float EstimateAdjustedDryMass(RocketPartsConfig config)
        {
            if (config == null) return 0f;

            float surfaceArea = 2f * Mathf.PI * config.bodyRadius * (config.bodyHeight + config.bodyRadius);
            float referenceArea = 2f * Mathf.PI * RocketPartsConfig.ReferenceBodyRadiusM * (RocketPartsConfig.ReferenceBodyHeightM + RocketPartsConfig.ReferenceBodyRadiusM);
            float scaledBodyDryMass = config.baseDryMass * surfaceArea / Mathf.Max(0.001f, referenceArea);
            return AdjustDryMassForInstalledHardware(scaledBodyDryMass, config.GetEngineCount(), config.GetFinCount(), config.rcsEnabled, config.rcsDryMass);
        }

        static float AdjustDryMassForInstalledHardware(float scaledBodyDryMass, int engineCount, int finCount, bool hasRcs, float rcsDryMass)
        {
            float referenceHardwareMass = ReferenceEngineCount * EngineDryMassKg + ReferenceFinCount * GridFinDryMassKg + RocketPartsConfig.DefaultRcsDryMassKg;
            float installedHardwareMass = Mathf.Max(0, engineCount) * EngineDryMassKg + Mathf.Max(0, finCount) * GridFinDryMassKg + (hasRcs ? Mathf.Max(0f, rcsDryMass) : 0f);
            return Mathf.Max(scaledBodyDryMass * 0.35f, scaledBodyDryMass + installedHardwareMass - referenceHardwareMass);
        }

        static readonly RocketPhysicsConfig Fallback = new()
        {
            radius = RocketPartsConfig.ReferenceBodyRadiusM,
            length = RocketPartsConfig.ReferenceBodyHeightM,
            dryMass = 22200f,
            maxFuelMass = RocketPartsConfig.ReferenceFuelCapacityKg,
            startFuelMass = 32000f,
            A_axial = 10.53f,
            A_projectedSide = 150.8f,
            cpLocalY = 20.6f,
            maxThrust = 845000f,
            minThrottle = RocketPartsConfig.Falcon9MinThrottle,
            burnRate = 305f,
            specificImpulse = 282f,
            engineLayout = EngineLayout.Octaweb,
            octawebBurnGroup = OctawebBurnGroup.CenterOnly,
            activeEngineCount = 1,
            independentEngines = true,
            separateEngineEnableActions = false,
            independentEngineCount = 1,
            engineCount = ReferenceEngineCount,
            engineDryMass = ReferenceEngineCount * EngineDryMassKg,
            throttleSpoolRate = 5f,
            gimbalSlewRate = 30f,
            maxGimbal = RocketPartsConfig.Falcon9MaxGimbalDeg,
            engineStartupDelay = RocketPartsConfig.Falcon9EngineStartupDelayS,
            engineShutdownTransient = RocketPartsConfig.Falcon9EngineShutdownTransientS,
            engineMinimumRunTime = RocketPartsConfig.Falcon9EngineMinimumRunTimeS,
            engineRestartCooldown = RocketPartsConfig.Falcon9EngineRestartCooldownS,
            hasFins = true,
            finCount = ReferenceFinCount,
            finSlewRate = 50f,
            maxFinAngle = 35f,
            A_fin = RocketPartsConfig.DefaultFinRadialLengthM * RocketPartsConfig.DefaultFinTangentialWidthM,
            finLiftScale = RocketPartsConfig.DefaultGridFinLiftScale,
            finDryMass = ReferenceFinCount * GridFinDryMassKg,
            finLocalY = RocketPartsConfig.ReferenceBodyHeightM - 1.2f,
            hasRCS = true,
            rcsThrust = RocketPartsConfig.DefaultRcsThrustN,
            rcsSpecificImpulse = RocketPartsConfig.DefaultRcsSpecificImpulseS,
            rcsMinimumPulseDuration = RocketPartsConfig.DefaultRcsMinimumPulseS,
            rcsJetCount = RcsComponent.JetCount,
            rcsDryMass = RocketPartsConfig.DefaultRcsDryMassKg,
            rcsPropellantMass = RocketPartsConfig.DefaultRcsPropellantMassKg,
            rcsLocalY = RocketPartsConfig.ReferenceBodyHeightM - 0.2f
        };
    }
}
