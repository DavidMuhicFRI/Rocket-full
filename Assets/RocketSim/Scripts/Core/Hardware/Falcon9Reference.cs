// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/Falcon9Reference.cs
// Purpose: Collects the reference dimensions, masses, propulsion values, fin
// sizes, and RCS values used as the Falcon 9 baseline throughout the simulator.
// One source prevents different systems from quietly using different baselines.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    public static class Falcon9Reference
    {
        public const float BodyRadiusM = 1.83f;
        public const float BodyHeightM = 41.2f;
        public const float BoosterDryMassKg = 22200f;
        public const float FuelCapacityKg = 400000f;

        public const float MerlinSeaLevelThrustN = 845000f;
        public const float MerlinSeaLevelSpecificImpulseS = 282f;
        public const float MerlinMinThrottle = 0.57f;
        public const float MerlinMaxGimbalDeg = 5f;
        public const float MerlinStartupDelayS = 0.85f;
        public const float MerlinShutdownTransientS = 0.25f;
        public const float MerlinMinimumRunTimeS = 2.00f;
        public const float MerlinRestartCooldownS = 2.00f;

        public const float SandboxMinThrottle = 0.39f;
        public const float SandboxMaxGimbalDeg = 7f;
        public const float SandboxEngineStartupDelayS = 0.15f;
        public const float SandboxEngineShutdownTransientS = 0.05f;
        public const float SandboxEngineMinimumRunTimeS = 0.20f;
        public const float SandboxEngineRestartCooldownS = 0.20f;

        public const float GridFinRadialLengthM = 1.59f;
        public const float GridFinTangentialWidthM = 1.23f;
        public const float GridFinThicknessM = 0.30f;
        public const float GridFinLiftScale = 0.50f;

        public const float RcsThrustN = 750f;
        public const float RcsSpecificImpulseS = 70f;
        public const float RcsMinimumPulseS = 0.10f;
        public const float RcsPropellantMassKg = 150f;
        public const float RcsDryMassKg = 250f;
    }
}
