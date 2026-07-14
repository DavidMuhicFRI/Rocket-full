// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Landing/LandingPlatformComponent.cs
// Purpose: Builds and updates the runtime chopstick target, including its visible
// arms, support geometry, capture trigger, and curriculum-controlled colliders.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Generated chopstick/catch target for the landing scenario.
    /// The object is built at runtime so every cloned training area gets a
    /// deterministic platform without hand-wiring a Unity prefab.
    /// </summary>
    public sealed class LandingPlatformComponent : MonoBehaviour
    {
        public const float CaptureHalfHeight = 3f;

        const float ArmThickness = 0.35f;
        const float IndicatorThickness = 0.05f;

        readonly List<BoxCollider> _physicalColliders = new();

        Transform _captureIndicator;
        Transform _leftArm;
        Transform _rightArm;
        Transform _backBeam;
        Transform _leftPost;
        Transform _rightPost;
        BoxCollider _captureTrigger;

        Material _captureMaterial;
        Material _armMaterial;
        Material _supportMaterial;

        float _halfSize = 3f;

        public bool TriggerActive { get; private set; }
        public bool PhysicalActive { get; private set; }
        public float HalfSize => _halfSize;

        /// <summary>
        /// Positions, scales, and activates the generated catch envelope and
        /// physical chopstick geometry for the current landing curriculum state.
        /// </summary>
        public void Configure(
            Vector3 localCenter,
            float yawDeg,
            float halfSize,
            bool triggerActive,
            bool physicalActive)
        {
            EnsureBuilt();

            _halfSize = Mathf.Max(0.5f, halfSize);
            TriggerActive = triggerActive;
            PhysicalActive = physicalActive;

            gameObject.SetActive(true);
            transform.localPosition = localCenter;
            transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            float armLength = Mathf.Max(3f, _halfSize * 2.4f);
            float armOffset = Mathf.Max(1f, _halfSize);
            float postHeight = Mathf.Max(3f, _halfSize * 0.55f);
            float postZ = -armOffset - 0.6f;

            SetLocalBox(_captureIndicator, Vector3.zero,
                new Vector3(_halfSize * 2f, IndicatorThickness, _halfSize * 2f));
            SetLocalBox(_leftArm, new Vector3(0f, 0f, armOffset),
                new Vector3(armLength, ArmThickness, ArmThickness));
            SetLocalBox(_rightArm, new Vector3(0f, 0f, -armOffset),
                new Vector3(armLength, ArmThickness, ArmThickness));
            SetLocalBox(_backBeam, new Vector3(0f, 0f, postZ),
                new Vector3(armLength, ArmThickness, ArmThickness));
            SetLocalBox(_leftPost, new Vector3(-_halfSize, -postHeight * 0.5f, postZ),
                new Vector3(ArmThickness, postHeight, ArmThickness));
            SetLocalBox(_rightPost, new Vector3(_halfSize, -postHeight * 0.5f, postZ),
                new Vector3(ArmThickness, postHeight, ArmThickness));

            _captureTrigger.enabled = triggerActive;
            _captureTrigger.center = Vector3.zero;
            _captureTrigger.size = new Vector3(_halfSize * 2f, CaptureHalfHeight * 2f, _halfSize * 2f);

            for (int i = 0; i < _physicalColliders.Count; i++)
                if (_physicalColliders[i])
                    _physicalColliders[i].enabled = physicalActive;
        }

        /// <summary>
        /// Hides the generated platform and disables both capture and physical surfaces.
        /// </summary>
        public void DisablePlatform()
        {
            TriggerActive = false;
            PhysicalActive = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Returns whether a world position lies inside the active capture box.
        /// </summary>
        public bool ContainsWorldPoint(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled || !TriggerActive)
                return false;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= _halfSize &&
                   Mathf.Abs(local.z) <= _halfSize &&
                   Mathf.Abs(local.y) <= CaptureHalfHeight;
        }

        /// <summary>
        /// Builds the platform child objects, markers, trigger, and colliders
        /// the first time the platform is configured.
        /// </summary>
        void EnsureBuilt()
        {
            if (_captureTrigger)
                return;

            _captureMaterial ??= CreateMaterial(new Color(0.1f, 0.8f, 1.0f, 0.35f));
            _armMaterial ??= CreateMaterial(new Color(1.0f, 0.82f, 0.15f, 1f));
            _supportMaterial ??= CreateMaterial(new Color(0.45f, 0.48f, 0.52f, 1f));

            _captureIndicator = CreateVisualBox("CatchEnvelope_Visual", _captureMaterial);
            _leftArm = CreatePhysicalBox("ChopstickArm_Left", _armMaterial);
            _rightArm = CreatePhysicalBox("ChopstickArm_Right", _armMaterial);
            _backBeam = CreatePhysicalBox("ChopstickBackBeam", _supportMaterial);
            _leftPost = CreateVisualBox("ChopstickPost_Left", _supportMaterial);
            _rightPost = CreateVisualBox("ChopstickPost_Right", _supportMaterial);

            var triggerGo = new GameObject("CatchEnvelope_Trigger");
            triggerGo.transform.SetParent(transform, false);
            var marker = triggerGo.AddComponent<LandingPlatformPart>();
            marker.platform = this;
            marker.captureTrigger = true;
            _captureTrigger = triggerGo.AddComponent<BoxCollider>();
            _captureTrigger.isTrigger = true;
        }

        /// <summary>
        /// Creates a non-colliding cube child used for platform visuals and metadata.
        /// </summary>
        Transform CreateVisualBox(string childName, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = childName;
            go.transform.SetParent(transform, false);

            if (go.TryGetComponent(out Collider collider))
            {
                collider.enabled = false;
                Destroy(collider);
            }

            if (go.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = material;

            var marker = go.AddComponent<LandingPlatformPart>();
            marker.platform = this;

            return go.transform;
        }

        /// <summary>
        /// Creates a platform cube child with a disabled BoxCollider that can be enabled later as a physical surface.
        /// </summary>
        Transform CreatePhysicalBox(string childName, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = childName;
            go.transform.SetParent(transform, false);

            if (go.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = material;

            var marker = go.AddComponent<LandingPlatformPart>();
            marker.platform = this;
            marker.physicalSurface = true;

            if (go.TryGetComponent(out BoxCollider collider))
            {
                collider.enabled = false;
                _physicalColliders.Add(collider);
            }

            return go.transform;
        }

        /// <summary>
        /// Applies local position, identity rotation, and scale to a generated box.
        /// </summary>
        static void SetLocalBox(Transform box, Vector3 localPosition, Vector3 localScale)
        {
            if (!box) return;

            box.localPosition = localPosition;
            box.localRotation = Quaternion.identity;
            box.localScale = localScale;
        }

        /// <summary>
        /// Creates a simple material for generated landing platform geometry.
        /// </summary>
        static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                shader = Shader.Find("Standard");

            var material = new Material(shader)
            {
                color = color
            };
            return material;
        }
    }

    public sealed class LandingPlatformPart : MonoBehaviour
    {
        public LandingPlatformComponent platform;
        public bool captureTrigger;
        public bool physicalSurface;
    }
}
