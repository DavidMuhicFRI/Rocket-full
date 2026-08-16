// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Physics/RocketPhysicsConfig.cs
// Purpose: Stores runtime physics constants derived from the selected rocket
// hardware and defines unambiguous thrust-authority calculations for telemetry.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

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

    /// <summary>
    /// Three distinct TWR quantities needed to describe engine authority.
    /// The minimum nonzero command differs from the all-engine minimum whenever
    /// engines have independent command channels.
    /// </summary>
    public readonly struct ThrustAuthoritySnapshot
    {
        public ThrustAuthoritySnapshot(
            float minimumCommandableNonzeroTwr,
            float allActiveEnginesMinimumThrottleTwr,
            float allActiveEnginesMaximumTwr)
        {
            MinimumCommandableNonzeroTwr = minimumCommandableNonzeroTwr;
            AllActiveEnginesMinimumThrottleTwr = allActiveEnginesMinimumThrottleTwr;
            AllActiveEnginesMaximumTwr = allActiveEnginesMaximumTwr;
        }

        public float MinimumCommandableNonzeroTwr { get; }
        public float AllActiveEnginesMinimumThrottleTwr { get; }
        public float AllActiveEnginesMaximumTwr { get; }
    }

    /// <summary>
    /// Computes thrust authority without implying a target throttle. Values use
    /// the supplied instantaneous mass and local gravity magnitude.
    /// </summary>
    public static class ThrustAuthorityMetrics
    {
        public static ThrustAuthoritySnapshot Calculate(
            float vehicleMassKg,
            float gravityMps2,
            int activeEngineCount,
            float maximumThrustPerEngineN,
            float minimumThrottle01,
            bool independentEngineControl)
        {
            float weightN = Mathf.Max(1f, vehicleMassKg) * Mathf.Max(0.001f, Mathf.Abs(gravityMps2));
            int engines = Mathf.Max(0, activeEngineCount);
            float perEngineMaximumN = Mathf.Max(0f, maximumThrustPerEngineN);
            float throttle = Mathf.Clamp01(minimumThrottle01);
            float allEngineMaximumN = engines * perEngineMaximumN;
            int smallestCommandedEngineCount = independentEngineControl ? Mathf.Min(1, engines) : engines;

            return new ThrustAuthoritySnapshot(
                smallestCommandedEngineCount * perEngineMaximumN * throttle / weightN,
                allEngineMaximumN * throttle / weightN,
                allEngineMaximumN / weightN);
        }
    }
}
