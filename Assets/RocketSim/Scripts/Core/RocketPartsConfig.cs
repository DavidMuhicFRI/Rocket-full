using System;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("bodyRadiusZ")]
        [Header("Body")]
        [Range(0.5f, 6f)] public float bodyRadius = 1.83f;
        [Range(5f, 80f)] public float bodyHeight = 41.2f;
        public float baseDryMass = 22200f;
        public float baseFuelMass = 400000f;
        [FormerlySerializedAs("remainingFuelMass")]
        public float startFuelMass = 40000f;

        [Header("Thruster")]
        public EngineLayout engineLayout = EngineLayout.Octaweb;
        public bool independentEngines = true;
        [Range(50000f, 1200000f)] public float maxThrustPerEngine = 845000f;
        [Range(0.15f, 0.80f)] public float minThrottle = 0.39f;
        [Range(0.2f, 1f)] public float engineSpacing = 0.8f;
        [Range(200f, 450f)] public float specificImpulse = 282f;
        [Range(0.5f, 20f)] public float throttleSpoolRate = 5f;
        [Range(5f, 90f)] public float gimbalSlewRate = 30f;
        [Range(1f, 15f)] public float maxGimbalAngle = 7f;

        [Header("Grid Fins")]
        public bool finsEnabled = true;
        public FinLayout finLayout = FinLayout.FourFins_Plus;
        [Range(0.2f, 4f)] public float finWidthX = 1.73f;
        [Range(0.2f, 4f)] public float finWidthZ = 1.73f;
        [Range(0.1f, 1f)] public float finHeight = 0.3f;
        [Range(5f, 60f)] public float maxFinAngle = 35f;
        [Range(10f, 200f)] public float finSlewRate = 50f;
        [Range(0.5f, 3f)] public float liftScale = 1.0f;

        [Header("RCS")]
        public bool rcsEnabled = true;
        [Range(100f, 15000f)] public float rcsThrust = 6000f;

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

        public int GetIndependentEngineCount()
        {
            return independentEngines ? GetEngineCount() : 1;
        }

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

        public int GetRCSCount()
        {
            return rcsEnabled ? RcsComponent.JetCount : 0;
        }
    }
}
