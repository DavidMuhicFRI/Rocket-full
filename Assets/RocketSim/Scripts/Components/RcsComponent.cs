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
        public const int FalconPodCount = RcsHardwareLayout.FalconPodCount;
        public const int NozzlesPerPod = RcsHardwareLayout.NozzlesPerPod;
        public const int JetCount = RcsHardwareLayout.JetCount;
        
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
            RcsHardwareLayout.JetIndex(podIndex, nozzle);

        /// <summary>
        /// Returns the generated pod transform for the requested Falcon-style
        /// RCS pod, or null when the layout has not been built.
        /// </summary>
        public Transform GetPod(int podIndex) =>
            podIndex >= 0 && podIndex < FalconPodCount
                ? FindDirectChild(RcsHardwareLayout.PodName(podIndex))
                : null;

        /// <summary>
        /// Returns the world-space direction the selected jet exhausts gas,
        /// derived from the generated pod orientation and nozzle type.
        /// </summary>
        public Vector3 WorldExhaustDirectionForJet(int jetIndex)
        {
            Transform pod = GetPod(jetIndex / NozzlesPerPod);
            if (!pod) return transform.right;

            var nozzle = (RcsNozzle)(jetIndex % NozzlesPerPod);
            return pod.TransformDirection(RcsHardwareLayout.LocalDirectionForNozzle(nozzle)).normalized;
        }

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
        public Vector3 WorldPositionForJet(int jetIndex)
        {
            Transform pod = GetPod(jetIndex / NozzlesPerPod);
            if (!pod) return transform.position;

            var nozzle = (RcsNozzle)(jetIndex % NozzlesPerPod);
            Transform nozzleTransform = pod.Find(RcsHardwareLayout.NozzleName(nozzle));
            return nozzleTransform ? nozzleTransform.position : pod.position;
        }

        /// <summary>
        /// Rebuilds the generated pod layout around the rocket body radius so
        /// RCS geometry stays attached to resized hardware.
        /// </summary>
        public void Reposition(float bodyRadius = 1.83f)
        {
            EnsurePodRoots();

            float podRadius = bodyRadius * 1.04f;
            for (int i = 0; i < FalconPodCount; i++)
            {
                Transform pod = GetPod(i);
                if (!pod) continue;

                float angleRad = RcsHardwareLayout.PodAngleDeg(i) * Mathf.Deg2Rad;
                Vector3 radial = new(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Vector3 tangent = Vector3.Cross(radial, Vector3.up).normalized;

                pod.gameObject.SetActive(true);
                pod.localPosition = radial * podRadius;
                pod.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                pod.localScale = Vector3.one;

                MeshRenderer rootRenderer = pod.GetComponent<MeshRenderer>();
                if (rootRenderer) rootRenderer.enabled = false;
                RcsGeometryPresenter.BuildPodVisuals(pod, bodyRadius);
            }
        }

        /// <summary>
        /// Updates the visual cold-gas plumes to mirror the current RCS command
        /// buffer without changing the physics forces.
        /// </summary>
        public void ShowCommands(float[] commands) =>
            RcsPlumePresenter.ShowCommands(transform, commands);

        /// <summary>
        /// Clears generated RCS plume meshes, lights, and particles.
        /// </summary>
        public void ClearVisuals() =>
            RcsPlumePresenter.ClearVisuals(transform);

        /// <summary>
        /// Generates and positions RCS pods from the serialized body-relative layout.
        /// </summary>
        void Awake() => Reposition();

        Transform FindDirectChild(string childName)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name == childName)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// Creates a missing pod root as a defensive fallback for custom prefabs.
        /// The supplied simulator prefab already contains both roots.
        /// </summary>
        void EnsurePodRoots()
        {
            for (int i = 0; i < FalconPodCount; i++)
            {
                if (GetPod(i)) continue;

                var pod = new GameObject(RcsHardwareLayout.PodName(i));
                pod.transform.SetParent(transform, false);
            }
        }
    }
}
