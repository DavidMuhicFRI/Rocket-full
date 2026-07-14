// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RCS/RcsJetLayout.cs
// Purpose: Defines the shared two-pod/eight-jet indexing, names, angles, and
// local nozzle directions used by RCS geometry, visuals, physics, and tests.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class RcsJetLayout
    {
        public const int FalconPodCount = 2;
        public const int NozzlesPerPod = 4;
        public const int JetCount = FalconPodCount * NozzlesPerPod;

        static readonly string[] PodNames = { "RCS0", "RCS1" };
        static readonly float[] PodAnglesDeg = { 0f, 180f };

        /// <summary>
        /// Converts a pod index and nozzle enum into the flat command-array
        /// index shared by the RCS controller and telemetry.
        /// </summary>
        public static int JetIndex(int podIndex, RcsComponent.RcsNozzle nozzle) =>
            podIndex * NozzlesPerPod + (int)nozzle;

        /// <summary>
        /// Returns the generated child name for a Falcon-style RCS pod index.
        /// </summary>
        public static string PodName(int podIndex) => PodNames[podIndex];

        /// <summary>
        /// Returns the azimuth angle used to place a generated RCS pod around the body.
        /// </summary>
        public static float PodAngleDeg(int podIndex) => PodAnglesDeg[podIndex];

        /// <summary>
        /// Reads and clamps one pod/nozzle command from the flat command array,
        /// returning zero when the array is missing or too short.
        /// </summary>
        public static float CommandValue(float[] commands, int podIndex, RcsComponent.RcsNozzle nozzle)
        {
            int index = JetIndex(podIndex, nozzle);
            return commands != null && index >= 0 && index < commands.Length ? Mathf.Clamp01(commands[index]) : 0f;
        }

        /// <summary>
        /// Returns the nozzle exhaust direction in pod-local space so geometry,
        /// visual plumes, and force mapping stay aligned.
        /// </summary>
        public static Vector3 LocalDirectionForNozzle(RcsComponent.RcsNozzle nozzle)
        {
            return nozzle switch
            {
                RcsComponent.RcsNozzle.Aft => Vector3.down,
                RcsComponent.RcsNozzle.Outboard => Vector3.right,
                RcsComponent.RcsNozzle.TangentialPositive => Vector3.forward,
                RcsComponent.RcsNozzle.TangentialNegative => Vector3.back,
                _ => Vector3.right
            };
        }

        /// <summary>
        /// Returns the generated child name for a nozzle transform in an RCS pod.
        /// </summary>
        public static string NozzleName(RcsComponent.RcsNozzle nozzle)
        {
            return nozzle switch
            {
                RcsComponent.RcsNozzle.Aft => "Nozzle_Aft",
                RcsComponent.RcsNozzle.Outboard => "Nozzle_Outboard",
                RcsComponent.RcsNozzle.TangentialPositive => "Nozzle_Tangent_Pos",
                RcsComponent.RcsNozzle.TangentialNegative => "Nozzle_Tangent_Neg",
                _ => "Nozzle_Outboard"
            };
        }

        /// <summary>
        /// Returns the generated child name for the visual plume paired with an RCS nozzle.
        /// </summary>
        public static string PlumeName(RcsComponent.RcsNozzle nozzle)
        {
            return nozzle switch
            {
                RcsComponent.RcsNozzle.Aft => "Plume_Aft",
                RcsComponent.RcsNozzle.Outboard => "Plume_Outboard",
                RcsComponent.RcsNozzle.TangentialPositive => "Plume_Tangent_Pos",
                RcsComponent.RcsNozzle.TangentialNegative => "Plume_Tangent_Neg",
                _ => "Plume_Outboard"
            };
        }
    }
}
