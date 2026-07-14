// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RCS/RcsLayoutBuilder.cs
// Purpose: Creates and positions the two RCS pod roots around a resized rocket,
// then asks RcsGeometryBuilder to refresh their visible child geometry.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class RcsLayoutBuilder
    {
        /// <summary>
        /// Finds the generated RCS pod root for the requested pod index.
        /// </summary>
        public static Transform GetPod(Transform root, int podIndex)
        {
            return podIndex >= 0 && podIndex < RcsJetLayout.FalconPodCount
                ? FindDirectChild(root, RcsJetLayout.PodName(podIndex))
                : null;
        }

        /// <summary>
        /// Ensures both Falcon-style pod roots exist, places them around the
        /// rocket body, and rebuilds their generated nozzle/plume visuals.
        /// </summary>
        public static void Reposition(Transform root, float bodyRadius)
        {
            EnsurePodRoots(root);

            float podRadius = bodyRadius * 1.04f;
            for (int i = 0; i < RcsJetLayout.FalconPodCount; i++)
            {
                Transform pod = GetPod(root, i);
                if (!pod) continue;

                float angleRad = RcsJetLayout.PodAngleDeg(i) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)).normalized;
                Vector3 tangent = Vector3.Cross(radial, Vector3.up).normalized;

                pod.gameObject.SetActive(true);
                pod.localPosition = radial * podRadius;
                pod.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                pod.localScale = Vector3.one;
                DisableRootRenderer(pod);
                RcsGeometryBuilder.BuildPodVisuals(pod, bodyRadius);
            }
        }

        /// <summary>
        /// Searches only direct children by name so a nozzle with a similar name
        /// deeper inside a pod cannot be mistaken for the pod root.
        /// </summary>
        public static Transform FindDirectChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// Creates the expected RCS pod root transforms when they are missing
        /// from the rocket hierarchy.
        /// </summary>
        static void EnsurePodRoots(Transform root)
        {
            for (int i = 0; i < RcsJetLayout.FalconPodCount; i++)
            {
                if (GetPod(root, i)) continue;

                var pod = new GameObject(RcsJetLayout.PodName(i));
                pod.transform.SetParent(root, false);
            }
        }

        /// <summary>
        /// Hides any accidental renderer on the pod root because visible
        /// geometry is generated on named child primitives.
        /// </summary>
        static void DisableRootRenderer(Transform pod)
        {
            var renderer = pod.GetComponent<MeshRenderer>();
            if (renderer)
                renderer.enabled = false;
        }
    }
}
