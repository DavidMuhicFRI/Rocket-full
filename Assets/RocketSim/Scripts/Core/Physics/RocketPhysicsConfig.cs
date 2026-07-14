// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Physics/RocketPhysicsConfig.cs
// Purpose: Stores runtime physics constants derived from the selected rocket hardware.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;

namespace RocketSim
{
    /// <summary>
    /// Immutable per-episode physics snapshot consumed by FalconAgent.
    /// RocketAssembly builds this from RocketPartsConfig after applying the
    /// current UI-selected hardware layout.
    /// </summary>
    [Serializable]
    public struct RocketPhysicsConfig
    {
        public float radius;
        public float length;
        public float dryMass;
        public float maxFuelMass;
        public float startFuelMass;
        public float A_axial;
        public float A_projectedSide;
        public float cpLocalY;

        public int independentEngineCount;
        public bool independentEngines;
        public EngineLayout engineLayout;
        public OctawebBurnGroup octawebBurnGroup;
        public int activeEngineCount;
        public float maxThrust;
        public float minThrottle;
        public float burnRate;
        public float specificImpulse;
        public float throttleSpoolRate;
        public float gimbalSlewRate;
        public float maxGimbal;
        public float engineStartupDelay;
        public float engineShutdownTransient;
        public float engineMinimumRunTime;
        public float engineRestartCooldown;
        public int engineCount;
        public float engineDryMass;

        public bool hasFins;
        public int finCount;
        public float finSlewRate;
        public float maxFinAngle;
        public float A_fin;
        public float finLiftScale;
        public float finDryMass;
        public float finLocalY;

        public bool hasRCS;
        public float rcsThrust;
        public float rcsSpecificImpulse;
        public float rcsMinimumPulseDuration;
        public int rcsJetCount;
        public float rcsDryMass;
        public float rcsPropellantMass;
        public float rcsLocalY;
    }
}
