// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LegLandingReferenceEnvelope.cs
// Purpose: Defines one hardware-independent feasibility reference so every
// landing ablation samples the same seeded initial-state distribution.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    public static class LegLandingReferenceEnvelope
    {
        public const int ActiveEngineCount = 1;
        public const float MaxThrustPerEngineN = Falcon9Reference.MerlinSeaLevelThrustN;
        public const float StartupDelayS = Falcon9Reference.MerlinStartupDelayS;
        public const float MaxGimbalDeg = Falcon9Reference.MerlinMaxGimbalDeg;
        public const float ReferenceStartFuelKg =
            Falcon9Reference.FuelCapacityKg * ScenarioCatalog.LegLandingStartFuelFraction;
        public const float ReferenceVehicleMassKg =
            Falcon9Reference.BoosterDryMassKg + ReferenceStartFuelKg + Falcon9Reference.RcsPropellantMassKg;
    }
}
