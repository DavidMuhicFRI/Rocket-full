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
        public float A_lateral;
        public float cpLocalY;

        public int independentEngineCount;
        public bool independentEngines;
        public float maxThrust;
        public float minThrottle;
        public float burnRate;
        public float specificImpulse;
        public float throttleSpoolRate;
        public float gimbalSlewRate;
        public float maxGimbal;
        public int engineCount;
        public float engineDryMass;

        public bool hasFins;
        public int finCount;
        public float finSlewRate;
        public float maxFinAngle;
        public float A_fin;
        public float finLiftScale;
        public float finDryMass;

        public bool hasRCS;
        public float rcsThrust;
        public int rcsJetCount;
        public float rcsDryMass;
        public float rcsPropellantMass;
    }
}
