// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/LandingLegComponent.cs
// Purpose: Builds a documented, configurable four-leg Falcon 9-like landing
// footprint with compound colliders and no aerodynamic contribution.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Runtime-generated deployed gear. Dimensions are intentionally exposed as
    /// documented approximations; this is a contact/stability model, not a leg
    /// deployment or structural finite-element model. Falcon9Reference dry mass
    /// is treated as already including landing gear, so this collider assembly
    /// does not add a second leg mass or an aerodynamic term.
    /// </summary>
    public sealed class LandingLegComponent : MonoBehaviour
    {
        public const int LegCount = 4;
        public const float ReferenceBodyRadiusM = Falcon9Reference.BodyRadiusM;
        public const float ReferenceBodyHeightM = Falcon9Reference.BodyHeightM;
        public const float ReferenceFootRadiusM = 9.0f;
        public const float ReferenceFootPlaneLocalY = -1.5f;
        public const float ReferenceHingeLocalY = 5.5f;
        public const float ReferenceFootSizeM = 1.2f;
        public const float ReferenceFootThicknessM = 0.25f;
        public const float ReferenceFootEdgeMarginM = ReferenceFootSizeM * 0.70710678f;

        readonly Transform[] _feet = new Transform[LegCount];
        readonly Transform[] _struts = new Transform[LegCount];
        Transform _gearRoot;
        Transform _feetFrame;
        PhysicsMaterial _contactMaterial;
        Material _legVisualMaterial;
        Material _footVisualMaterial;

        public Transform FeetFrame => _feetFrame;
        public float FootPlaneLocalY { get; private set; } = ReferenceFootPlaneLocalY;
        public float FootEdgeMarginM { get; private set; } = ReferenceFootEdgeMarginM;

        public static LandingLegComponent Ensure(Transform rocketRoot)
        {
            if (!rocketRoot) return null;
            LandingLegComponent existing = rocketRoot.GetComponentInChildren<LandingLegComponent>(true);
            if (existing) return existing;

            var go = new GameObject("LandingLegs");
            go.transform.SetParent(rocketRoot, false);
            return go.AddComponent<LandingLegComponent>();
        }

        /// <summary>
        /// Builds/resizes the four deployed legs and toggles only their child
        /// hierarchy. This component lives on the rocket root, so disabling its
        /// own GameObject would also disable FalconAgent during initialization.
        /// </summary>
        public void Configure(bool enabled, float bodyRadius, float bodyHeight)
        {
            EnsureBuilt();
            _gearRoot.gameObject.SetActive(enabled);
            if (!enabled) return;

            float radialScale = Mathf.Max(0.25f, bodyRadius / ReferenceBodyRadiusM);
            float heightScale = Mathf.Max(0.25f, bodyHeight / ReferenceBodyHeightM);
            float footRadius = ReferenceFootRadiusM * radialScale;
            FootPlaneLocalY = ReferenceFootPlaneLocalY * radialScale;
            float hingeY = ReferenceHingeLocalY * heightScale;
            float hingeRadius = bodyRadius * 0.95f;
            float footSize = ReferenceFootSizeM * radialScale;
            float footThickness = ReferenceFootThicknessM * radialScale;
            // Half the square foot's diagonal is a yaw-independent pad-edge
            // reserve, unlike half its width, which can allow a rotated corner
            // to overhang the physical platform.
            FootEdgeMarginM = footSize * 0.70710678f;

            _feetFrame.localPosition = new Vector3(0f, FootPlaneLocalY, 0f);
            for (int i = 0; i < LegCount; i++)
            {
                // Forty-five degrees keeps feet clear of the +X/+Z fin axes and
                // gives a symmetric square support polygon.
                float angle = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 footPosition = radial * footRadius + Vector3.up * (FootPlaneLocalY + footThickness * 0.5f);
                Vector3 hingePosition = radial * hingeRadius + Vector3.up * hingeY;
                float strutRadius = 0.18f * radialScale;
                Vector3 strutFootPosition = footPosition + Vector3.up * (footThickness * 0.5f + strutRadius);

                Transform foot = _feet[i];
                foot.localPosition = footPosition;
                foot.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                foot.localScale = new Vector3(footSize, footThickness, footSize);

                Transform strut = _struts[i];
                Vector3 delta = strutFootPosition - hingePosition;
                strut.localPosition = (strutFootPosition + hingePosition) * 0.5f;
                strut.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
                strut.localScale = new Vector3(strutRadius * 2f, delta.magnitude * 0.5f, strutRadius * 2f);
            }
        }

        public Transform FootTransform(int index) => index >= 0 && index < _feet.Length ? _feet[index] : null;

        void EnsureBuilt()
        {
            if (_feetFrame) return;

            _contactMaterial = new PhysicsMaterial("LandingFootContact")
            {
                staticFriction = 0.8f,
                dynamicFriction = 0.6f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            _legVisualMaterial = CreateVisualMaterial(new Color(0.18f, 0.20f, 0.23f, 1f));
            _footVisualMaterial = CreateVisualMaterial(new Color(0.08f, 0.09f, 0.10f, 1f));
            
            var gearObject = new GameObject("LandingGear");
            _gearRoot = gearObject.transform;
            _gearRoot.SetParent(transform, false);

            var frameObject = new GameObject("FeetFrame");
            _feetFrame = frameObject.transform;
            _feetFrame.SetParent(_gearRoot, false);

            for (int i = 0; i < LegCount; i++)
            {
                GameObject strut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                strut.name = $"LandingLeg_{i + 1}_Strut";
                strut.transform.SetParent(_gearRoot, false);
                strut.GetComponent<MeshRenderer>().sharedMaterial = _legVisualMaterial;
                strut.GetComponent<Collider>().sharedMaterial = _contactMaterial;
                var strutMarker = strut.AddComponent<LandingGearCollider>();
                strutMarker.kind = LandingGearColliderKind.Strut;
                strutMarker.footIndex = i;
                _struts[i] = strut.transform;

                GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                foot.name = $"LandingFoot_{i + 1}";
                foot.transform.SetParent(_gearRoot, false);
                foot.GetComponent<MeshRenderer>().sharedMaterial = _footVisualMaterial;
                foot.GetComponent<Collider>().sharedMaterial = _contactMaterial;
                var footMarker = foot.AddComponent<LandingGearCollider>();
                footMarker.kind = LandingGearColliderKind.Foot;
                footMarker.footIndex = i;
                _feet[i] = foot.transform;
            }
        }

        static Material CreateVisualMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = color };
        }
    }
}
