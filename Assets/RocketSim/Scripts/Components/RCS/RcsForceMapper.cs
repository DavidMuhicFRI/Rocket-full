// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RCS/RcsForceMapper.cs
// Purpose: Converts a flat RCS jet index into the matching generated nozzle's
// world position and exhaust direction for force-at-position physics.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class RcsForceMapper
    {
        /// <summary>
        /// Resolves a flat jet index to its pod/nozzle pair and returns the
        /// nozzle exhaust direction in world space.
        /// </summary>
        public static Vector3 WorldExhaustDirectionForJet(Transform root, int jetIndex)
        {
            Transform pod = RcsLayoutBuilder.GetPod(root, jetIndex / RcsJetLayout.NozzlesPerPod);
            if (!pod) return root.right;

            var nozzle = (RcsComponent.RcsNozzle)(jetIndex % RcsJetLayout.NozzlesPerPod);
            return pod.TransformDirection(RcsJetLayout.LocalDirectionForNozzle(nozzle)).normalized;
        }

        /// <summary>
        /// Resolves a flat jet index to the generated nozzle transform and
        /// returns the world-space force application point.
        /// </summary>
        public static Vector3 WorldPositionForJet(Transform root, int jetIndex)
        {
            Transform pod = RcsLayoutBuilder.GetPod(root, jetIndex / RcsJetLayout.NozzlesPerPod);
            if (!pod) return root.position;

            var nozzleKind = (RcsComponent.RcsNozzle)(jetIndex % RcsJetLayout.NozzlesPerPod);
            Transform nozzle = pod.Find(RcsJetLayout.NozzleName(nozzleKind));
            return nozzle ? nozzle.position : pod.position;
        }
    }
}
