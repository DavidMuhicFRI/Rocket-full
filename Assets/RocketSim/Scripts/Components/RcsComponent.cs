// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RcsComponent.cs
// Purpose: Exposes the rocket's two cold-gas RCS pods as a simple flat list of
// jets for the agent, physics, hardware tests, and plume-visual code.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    // Falcon 9-style cold-gas attitude control: two opposing pods near the
    // interstage, each with aft, outboard, and two tangential nozzles.
    public class RcsComponent : MonoBehaviour
    {
        public const int FalconPodCount = RcsJetLayout.FalconPodCount;
        public const int NozzlesPerPod = RcsJetLayout.NozzlesPerPod;
        public const int JetCount = RcsJetLayout.JetCount;
        
        public enum RcsNozzle
        {
            Aft = 0,
            Outboard = 1,
            TangentialPositive = 2,
            TangentialNegative = 3
        }

        [Header("RCS Specs - written by RocketAssembly.ApplyPartsConfig")]
        [Range(100f, 2000f)] public float thrustPerThruster = RocketPartsConfig.DefaultRcsThrustN;
        [Range(30f, 100f)] public float specificImpulse = RocketPartsConfig.DefaultRcsSpecificImpulseS;
        [Range(0.02f, 0.5f)] public float minimumPulseDuration = RocketPartsConfig.DefaultRcsMinimumPulseS;
        [Range(0f, 1000f)] public float propellantMass = RocketPartsConfig.DefaultRcsPropellantMassKg;
        [Range(0f, 1000f)] public float dryMass = RocketPartsConfig.DefaultRcsDryMassKg;

        /// <summary>
        /// Converts a pod/nozzle pair into the flat RCS command-buffer index
        /// used by the agent and valve bank.
        /// </summary>
        public static int JetIndex(int podIndex, RcsNozzle nozzle) =>
            RcsJetLayout.JetIndex(podIndex, nozzle);

        /// <summary>
        /// Returns the generated pod transform for the requested Falcon-style
        /// RCS pod, or null when the layout has not been built.
        /// </summary>
        public Transform GetPod(int podIndex) =>
            RcsLayoutBuilder.GetPod(transform, podIndex);

        /// <summary>
        /// Returns the world-space direction the selected jet exhausts gas,
        /// derived from the generated pod orientation and nozzle type.
        /// </summary>
        public Vector3 WorldExhaustDirectionForJet(int jetIndex) =>
            RcsForceMapper.WorldExhaustDirectionForJet(transform, jetIndex);

        /// <summary>
        /// Returns the world-space force direction applied to the rocket by the
        /// selected jet, which is opposite the exhaust direction.
        /// </summary>
        public Vector3 WorldForceDirectionForJet(int jetIndex) =>
            -WorldExhaustDirectionForJet(jetIndex);

        /// <summary>
        /// Returns the world-space nozzle position used as the RCS force
        /// application point for torque calculations.
        /// </summary>
        public Vector3 WorldPositionForJet(int jetIndex) =>
            RcsForceMapper.WorldPositionForJet(transform, jetIndex);

        /// <summary>
        /// Rebuilds the generated pod layout around the rocket body radius so
        /// RCS geometry stays attached to resized hardware.
        /// </summary>
        public void Reposition(float bodyRadius = 1.83f) =>
            RcsLayoutBuilder.Reposition(transform, bodyRadius);

        /// <summary>
        /// Updates the visual cold-gas plumes to mirror the current RCS command
        /// buffer without changing the physics forces.
        /// </summary>
        public void ShowCommands(float[] commands) =>
            RcsVisualController.ShowCommands(transform, commands);

        /// <summary>
        /// Clears generated RCS plume meshes, lights, and particles.
        /// </summary>
        public void ClearVisuals() =>
            RcsVisualController.ClearVisuals(transform);

        /// <summary>
        /// Generates and positions RCS pods from the serialized body-relative layout.
        /// </summary>
        void Awake() => Reposition();
    }
}
