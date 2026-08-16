// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketAssembly.cs
// Purpose: Converts the selected rocket parts configuration into scene hardware objects and runtime physics constants.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    // Central per-rocket manager inside the TrainingArea prefab.
    // Two responsibilities:
    //   1. ApplyPartsConfig  — push shared config into component fields and reposition transforms.
    //   2. GetPhysicsConfig  — snapshot for FalconAgent at episode begin.
    public class RocketAssembly : MonoBehaviour
    {
        const int ReferenceEngineCount = 9;
        const int ReferenceFinCount = 4;
        const float EngineDryMassKg = 470f;      // Merlin-class engine dry mass proxy
        const float GridFinDryMassKg = 200f;     // grid fin + actuator proxy
        
        [Header("Components — wire inside prefab")]
        public BodyComponent     body;
        public ThrusterComponent thrusters; // Thrusters parent GO
        public FinComponent      fins;     // Fins parent GO
        public RcsComponent      rcs;      // RCS parent GO
        public LandingLegComponent landingLegs;

        /// <summary>The serialized landing-gear component owned by this vehicle.</summary>
        public LandingLegComponent LandingLegs
        {
            get
            {
                if (!landingLegs)
                    landingLegs = GetComponentInChildren<LandingLegComponent>(true);
                return landingLegs;
            }
        }
        
        public static RocketPhysicsConfig Falcon9StaticFallback => Falcon9Fallback;

        // ── Apply shared config to this instance ──────────────────────────────
        // Called by SimulationAreaHost after spawn and whenever panel changes a value.
        public void ApplyPartsConfig(RocketPartsConfig cfg)
        {
            // Body
            if (body)
            {
                body.radius       = cfg.bodyRadius;
                body.height       = cfg.bodyHeight;
                body.baseDryMass  = cfg.baseDryMass;
                body.baseFuelMass = cfg.baseFuelMass;
                body.startFuelMass = cfg.startFuelMass;
                body.ApplyDimensions();
            }

            // Thrusters
            if (thrusters)
            {
                thrusters.layout             = cfg.engineLayout;
                thrusters.independentEngines = cfg.independentEngines;
                thrusters.octawebBurnGroup   = cfg.octawebBurnGroup;
                thrusters.engineSpacing      = cfg.engineSpacing;
                thrusters.maxThrustPerEngine = cfg.maxThrustPerEngine;
                thrusters.minThrottle        = cfg.minThrottle;
                thrusters.specificImpulse    = cfg.specificImpulse;
                thrusters.throttleSpoolRate  = cfg.throttleSpoolRate;
                thrusters.gimbalSlewRate     = cfg.gimbalSlewRate;
                thrusters.maxGimbalAngle     = cfg.maxGimbalAngle;
                thrusters.engineStartupDelay = cfg.engineStartupDelay;
                thrusters.engineShutdownTransient = cfg.engineShutdownTransient;
                thrusters.engineMinimumRunTime = cfg.engineMinimumRunTime;
                thrusters.engineRestartCooldown = cfg.engineRestartCooldown;
                thrusters.transform.localPosition = Vector3.zero;
                thrusters.ApplyLayout(body.radius);
            }

            // Grid fins
            if (fins)
            {
                fins.gameObject.SetActive(cfg.finsEnabled);
                fins.layout       = cfg.finLayout;
                fins.finWidthX    = cfg.finWidthX;
                fins.finWidthZ    = cfg.finWidthZ;
                fins.finThickness = cfg.finThickness;
                fins.maxFinAngle  = cfg.maxFinAngle;
                fins.finSlewRate  = cfg.finSlewRate;
                fins.liftScale    = cfg.liftScale;
                fins.transform.localPosition = new Vector3(0f, cfg.bodyHeight - 1.2f, 0f);
                fins.ApplyLayout(body.radius);
            }

            // RCS — same pattern
            if (rcs)
            {
                rcs.gameObject.SetActive(cfg.rcsEnabled);
                rcs.thrustPerThruster = cfg.rcsThrust;
                rcs.specificImpulse = cfg.rcsSpecificImpulse;
                rcs.minimumPulseDuration = cfg.rcsMinimumPulseDuration;
                rcs.propellantMass = cfg.rcsPropellantMass;
                rcs.dryMass = cfg.rcsDryMass;
                rcs.transform.localPosition = new Vector3(0f, cfg.bodyHeight - 0.2f, 0f);
                rcs.Reposition(body.radius);
            }
        }

        // ── Build physics snapshot for FalconAgent ────────────────────────────
        public RocketPhysicsConfig GetPhysicsConfig()
        {
            if (!body) return Falcon9Fallback;

            int engineCount = thrusters ? thrusters.EngineCount : ReferenceEngineCount;
            bool hasFins = fins != null && fins.gameObject.activeSelf;
            int finCount = hasFins ? fins.FinCount : 0;
            bool hasRcs = rcs != null && rcs.gameObject.activeSelf;

            float engineDryMass = engineCount * EngineDryMassKg;
            float finDryMass = finCount * GridFinDryMassKg;
            float rcsDryMass = hasRcs ? rcs.dryMass : 0f;
            float rcsPropellantMass = hasRcs ? rcs.propellantMass : 0f;

            float adjustedDryMass = AdjustDryMassForInstalledHardware(
                body.DryMass,
                engineCount,
                finCount,
                hasRcs,
                rcsDryMass);
        
            return new RocketPhysicsConfig {
                radius        = body.radius,
                length        = body.height,
                dryMass       = adjustedDryMass,
                maxFuelMass   = body.MaxFuelCapacity,
                startFuelMass = body.startFuelMass,
                
                A_axial           = body.AxialArea,
                A_projectedSide   = body.ProjectedSideArea,
                cpLocalY          = body.CPLocalY,
        
                independentEngines     = !thrusters || thrusters.independentEngines,
                independentEngineCount = thrusters ? thrusters.IndependentEngineCount : 1,
                engineLayout       = thrusters ? thrusters.layout : EngineLayout.Octaweb,
                octawebBurnGroup   = thrusters ? thrusters.octawebBurnGroup : OctawebBurnGroup.CenterOnly,
                activeEngineCount  = thrusters ? thrusters.ActiveEngineCount : 1,
                
                maxThrust         = thrusters ? thrusters.maxThrustPerEngine : 845000f,
                minThrottle       = thrusters ? thrusters.minThrottle        : RocketPartsConfig.Falcon9MinThrottle,
                burnRate          = thrusters ? thrusters.BurnRate * thrusters.ActiveEngineCount : 305f,
                specificImpulse   = thrusters ? thrusters.specificImpulse    : 282f,
                engineCount       = engineCount,
                engineDryMass     = engineDryMass,
        
                throttleSpoolRate = thrusters ? thrusters.throttleSpoolRate  : 5f, 
                gimbalSlewRate    = thrusters ? thrusters.gimbalSlewRate     : 30f, 
                maxGimbal         = thrusters ? thrusters.maxGimbalAngle     : RocketPartsConfig.Falcon9MaxGimbalDeg,
                engineStartupDelay = thrusters ? thrusters.engineStartupDelay : RocketPartsConfig.Falcon9EngineStartupDelayS,
                engineShutdownTransient = thrusters ? thrusters.engineShutdownTransient : RocketPartsConfig.Falcon9EngineShutdownTransientS,
                engineMinimumRunTime = thrusters ? thrusters.engineMinimumRunTime : RocketPartsConfig.Falcon9EngineMinimumRunTimeS,
                engineRestartCooldown = thrusters ? thrusters.engineRestartCooldown : RocketPartsConfig.Falcon9EngineRestartCooldownS,
        
                hasFins      = hasFins,
                finCount     = hasFins ? finCount      : 0, 
                finSlewRate  = fins ? fins.finSlewRate : 50f, 
                maxFinAngle  = fins ? fins.maxFinAngle : 35f, 
                A_fin        = fins ? fins.A_fin       : RocketPartsConfig.DefaultFinRadialLengthM * RocketPartsConfig.DefaultFinTangentialWidthM,
                finLiftScale = fins ? fins.liftScale   : RocketPartsConfig.DefaultGridFinLiftScale,
                finDryMass   = finDryMass,
                finLocalY    = fins ? fins.transform.localPosition.y : body.height - 1.2f,
        
                hasRCS      = hasRcs,
                rcsThrust   = hasRcs ? rcs.thrustPerThruster : 0f,
                rcsSpecificImpulse = hasRcs ? rcs.specificImpulse : 0f,
                rcsMinimumPulseDuration = hasRcs ? rcs.minimumPulseDuration : 0f,
                rcsJetCount = hasRcs ? RcsComponent.JetCount : 0,
                rcsDryMass  = rcsDryMass,
                rcsPropellantMass = rcsPropellantMass,
                rcsLocalY   = rcs ? rcs.transform.localPosition.y : body.height - 0.2f,
            };
        }

        /// <summary>
        /// Estimates the runtime dry mass directly from a parts configuration.
        /// Run manifests use this before a scene rocket exists so their initial
        /// mass and TWR values match the assembly calculation.
        /// </summary>
        public static float EstimateAdjustedDryMass(RocketPartsConfig cfg)
        {
            if (cfg == null) return 0f;

            float currentSurfaceArea = 2f * Mathf.PI * cfg.bodyRadius * (cfg.bodyHeight + cfg.bodyRadius);
            float referenceSurfaceArea = 2f * Mathf.PI *
                                         RocketPartsConfig.ReferenceBodyRadiusM *
                                         (RocketPartsConfig.ReferenceBodyHeightM + RocketPartsConfig.ReferenceBodyRadiusM);
            float scaledBodyDryMass = cfg.baseDryMass * currentSurfaceArea / Mathf.Max(0.001f, referenceSurfaceArea);
            return AdjustDryMassForInstalledHardware(
                scaledBodyDryMass,
                cfg.GetEngineCount(),
                cfg.GetFinCount(),
                cfg.rcsEnabled,
                cfg.rcsDryMass);
        }

        /// <summary>
        /// Replaces the reference hardware contribution in the configured body
        /// mass with the hardware that is actually installed on this vehicle.
        /// </summary>
        static float AdjustDryMassForInstalledHardware(
            float scaledBodyDryMass,
            int engineCount,
            int finCount,
            bool hasRcs,
            float rcsDryMass)
        {
            float defaultHardwareMass = ReferenceEngineCount * EngineDryMassKg +
                                        ReferenceFinCount * GridFinDryMassKg +
                                        RocketPartsConfig.DefaultRcsDryMassKg;
            float selectedHardwareMass = Mathf.Max(0, engineCount) * EngineDryMassKg +
                                         Mathf.Max(0, finCount) * GridFinDryMassKg +
                                         (hasRcs ? Mathf.Max(0f, rcsDryMass) : 0f);
            return Mathf.Max(
                scaledBodyDryMass * 0.35f,
                scaledBodyDryMass + selectedHardwareMass - defaultHardwareMass);
        }

        // ── Fallback in case body is not assigned ─────────────────────────────
        static readonly RocketPhysicsConfig Falcon9Fallback = new RocketPhysicsConfig {
            radius = RocketPartsConfig.ReferenceBodyRadiusM,
            length = RocketPartsConfig.ReferenceBodyHeightM,
            dryMass = 22200f,
            maxFuelMass = RocketPartsConfig.ReferenceFuelCapacityKg,
            startFuelMass = 32000f,
            A_axial = 10.53f, A_projectedSide = 150.8f, cpLocalY = 20.6f,
            maxThrust = 845000f, minThrottle = RocketPartsConfig.Falcon9MinThrottle, burnRate = 305f, specificImpulse = 282f,
            engineLayout = EngineLayout.Octaweb, octawebBurnGroup = OctawebBurnGroup.CenterOnly,
            activeEngineCount = 1, independentEngines = true, independentEngineCount = 1,
            engineCount = ReferenceEngineCount, engineDryMass = ReferenceEngineCount * EngineDryMassKg,
            throttleSpoolRate = 5f, gimbalSlewRate = 30f, maxGimbal = RocketPartsConfig.Falcon9MaxGimbalDeg,
            engineStartupDelay = RocketPartsConfig.Falcon9EngineStartupDelayS,
            engineShutdownTransient = RocketPartsConfig.Falcon9EngineShutdownTransientS,
            engineMinimumRunTime = RocketPartsConfig.Falcon9EngineMinimumRunTimeS,
            engineRestartCooldown = RocketPartsConfig.Falcon9EngineRestartCooldownS,
            hasFins = true, finCount = 4, finSlewRate = 50f, maxFinAngle = 35f,
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
