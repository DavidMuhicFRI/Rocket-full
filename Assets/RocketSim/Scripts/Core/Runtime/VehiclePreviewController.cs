using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Creates and freezes the non-running vehicle shown while users edit the
    /// session. It has no training, policy, telemetry, or run responsibility.
    /// </summary>
    public sealed class VehiclePreviewController
    {
        readonly Transform _parent;
        readonly GameObject _previewPrefab;
        readonly List<RocketAssembly> _assemblies = new();
        readonly Dictionary<Rigidbody, RigidbodyPose> _poses = new();

        public VehiclePreviewController(Transform parent, GameObject previewPrefab)
        {
            _parent = parent;
            _previewPrefab = previewPrefab;
        }

        /// <summary>Creates one frozen preview from the current vehicle.</summary>
        public void Spawn(RocketPartsConfig vehicle)
        {
            if (!_previewPrefab) return;
            GameObject root = Object.Instantiate(
                _previewPrefab,
                Vector3.zero,
                Quaternion.identity,
                _parent);
            root.name = "VehiclePreview";
            RocketAssembly assembly = root.GetComponentInChildren<RocketAssembly>();
            if (assembly)
            {
                _assemblies.Add(assembly);
                assembly.ApplyPartsConfig(vehicle);
            }
            Freeze(root, capturePose: true);
        }

        /// <summary>Refreshes preview hardware while retaining its exact pose.</summary>
        public void Apply(RocketPartsConfig vehicle)
        {
            foreach (RocketAssembly assembly in _assemblies)
            {
                if (!assembly) continue;
                assembly.ApplyPartsConfig(vehicle);
                Freeze(assembly.gameObject, capturePose: false);
            }
        }

        /// <summary>Forgets objects destroyed by SimulationAreaHost.</summary>
        public void ClearReferences()
        {
            _assemblies.Clear();
            _poses.Clear();
        }

        void Freeze(GameObject root, bool capturePose)
        {
            if (!root) return;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (capturePose || !_poses.ContainsKey(body))
                {
                    _poses[body] = new RigidbodyPose
                    {
                        localPosition = body.transform.localPosition,
                        localRotation = body.transform.localRotation
                    };
                }

                RigidbodyPose pose = _poses[body];
                body.transform.SetLocalPositionAndRotation(pose.localPosition, pose.localRotation);
                // Unity rejects velocity writes while a body is kinematic.
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.Sleep();
            }
            Physics.SyncTransforms();
        }

        struct RigidbodyPose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
    }
}
