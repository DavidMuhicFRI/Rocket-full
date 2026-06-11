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
        const float RcsDryMassKg = 250f;         // pods, plumbing, tanks, valves
        const float RcsPropellantMassKg = 150f;  // cold-gas nitrogen proxy
        
        [Header("Components — wire inside prefab")]
        public RocketBody        body;
        public ThrusterComponent thrusters; // Thrusters parent GO
        public FinComponent      fins;     // Fins parent GO
        public RcsComponent      rcs;      // RCS parent GO
        
        public static RocketPhysicsConfig Falcon9StaticFallback => Falcon9Fallback;

        // ── Apply shared config to this instance ──────────────────────────────
        // Called by TrainingAreaManager after spawn and whenever panel changes a value.
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
                thrusters.engineSpacing      = cfg.engineSpacing;
                thrusters.maxThrustPerEngine = cfg.maxThrustPerEngine;
                thrusters.minThrottle        = cfg.minThrottle;
                thrusters.specificImpulse    = cfg.specificImpulse;
                thrusters.throttleSpoolRate  = cfg.throttleSpoolRate;
                thrusters.gimbalSlewRate     = cfg.gimbalSlewRate;
                thrusters.maxGimbalAngle     = cfg.maxGimbalAngle;
                thrusters.transform.localPosition = Vector3.zero;
                thrusters.ApplyLayout(body.radius);
            }

            // Grid fins
            if (fins)
            {
                fins.gameObject.SetActive(cfg.finsEnabled);
                fins.layout      = cfg.finLayout;
                fins.finWidthX   = cfg.finWidthX;
                fins.finWidthZ   = cfg.finWidthZ;
                fins.finHeight   = cfg.finHeight;
                fins.maxFinAngle = cfg.maxFinAngle;
                fins.finSlewRate = cfg.finSlewRate;
                fins.liftScale   = cfg.liftScale;
                fins.transform.localPosition = new Vector3(0f, cfg.bodyHeight - 1.2f, 0f);
                fins.ApplyLayout(body.radius);
            }

            // RCS — same pattern
            if (rcs)
            {
                rcs.gameObject.SetActive(cfg.rcsEnabled);
                rcs.thrustPerThruster = cfg.rcsThrust;
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
            float rcsDryMass = hasRcs ? RcsDryMassKg : 0f;
            float rcsPropellantMass = hasRcs ? RcsPropellantMassKg : 0f;

            float defaultHardwareMass = ReferenceEngineCount * EngineDryMassKg +
                                        ReferenceFinCount * GridFinDryMassKg +
                                        RcsDryMassKg;
            float selectedHardwareMass = engineDryMass + finDryMass + rcsDryMass;
            float adjustedDryMass = Mathf.Max(
                body.DryMass * 0.35f,
                body.DryMass + selectedHardwareMass - defaultHardwareMass);
        
            return new RocketPhysicsConfig {
                radius        = body.radius,
                length        = body.height,
                dryMass       = adjustedDryMass,
                maxFuelMass   = body.MaxFuelCapacity,
                startFuelMass = body.startFuelMass,
                
                A_axial       = body.AxialArea,
                A_lateral     = body.LateralArea,
                cpLocalY      = body.CPLocalY,
        
                independentEngines     = !thrusters || thrusters.independentEngines,
                independentEngineCount = thrusters ? thrusters.IndependentEngineCount : 1,
                
                maxThrust         = thrusters ? thrusters.maxThrustPerEngine : 845000f,
                minThrottle       = thrusters ? thrusters.minThrottle        : 0.39f, 
                burnRate          = thrusters ? thrusters.BurnRate           : 305f, 
                specificImpulse   = thrusters ? thrusters.specificImpulse    : 282f,
                engineCount       = engineCount,
                engineDryMass     = engineDryMass,
        
                throttleSpoolRate = thrusters ? thrusters.throttleSpoolRate  : 5f, 
                gimbalSlewRate    = thrusters ? thrusters.gimbalSlewRate     : 30f, 
                maxGimbal         = thrusters ? thrusters.maxGimbalAngle     : 7f, 
        
                hasFins      = hasFins,
                finCount     = hasFins ? finCount      : 0, 
                finSlewRate  = fins ? fins.finSlewRate : 50f, 
                maxFinAngle  = fins ? fins.maxFinAngle : 35f, 
                A_fin        = fins ? fins.A_fin       : 2.89f, 
                finLiftScale = fins ? fins.liftScale   : 1f,
                finDryMass   = finDryMass,
        
                hasRCS      = hasRcs,
                rcsThrust   = hasRcs ? rcs.thrustPerThruster : 0f,
                rcsJetCount = hasRcs ? RcsComponent.JetCount : 0,
                rcsDryMass  = rcsDryMass,
                rcsPropellantMass = rcsPropellantMass,
            };
        }

        // ── Fallback in case body is not assigned ─────────────────────────────
        static readonly RocketPhysicsConfig Falcon9Fallback = new RocketPhysicsConfig {
            radius = 1.83f, length = 41.2f, dryMass = 22200f, maxFuelMass = 400000f, startFuelMass = 40000f,
            A_axial = 10.53f, A_lateral = 150.8f, cpLocalY = 26.78f,
            maxThrust = 845000f, minThrottle = 0.39f, burnRate = 305f, specificImpulse = 282f,
            engineCount = ReferenceEngineCount, engineDryMass = ReferenceEngineCount * EngineDryMassKg,
            throttleSpoolRate = 5f, gimbalSlewRate = 30f, maxGimbal = 5f,
            hasFins = true, finCount = 4, finSlewRate = 50f, maxFinAngle = 35f,
            A_fin = 2.89f, finLiftScale = 1f, finDryMass = ReferenceFinCount * GridFinDryMassKg,
            hasRCS = true, rcsThrust = 6000f, rcsJetCount = RcsComponent.JetCount,
            rcsDryMass = RcsDryMassKg, rcsPropellantMass = RcsPropellantMassKg
        };
    }
}
