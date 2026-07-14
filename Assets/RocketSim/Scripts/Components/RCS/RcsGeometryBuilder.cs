// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RCS/RcsGeometryBuilder.cs
// Purpose: Generates simple pod, nozzle, plume, light, particle, and material
// objects for the Falcon-style RCS without requiring a separate prefab asset.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    internal static class RcsGeometryBuilder
    {
        static Material _podMaterial;
        static Material _nozzleMaterial;
        static Material _plumeMaterial;
        static Material _particleMaterial;
        static readonly Dictionary<Transform, Vector3> PlumeBaseScales = new();

        /// <summary>
        /// Builds or refreshes the visible pod body, nozzles, and cold-gas
        /// plume primitives for one RCS pod using dimensions scaled from body radius.
        /// </summary>
        public static void BuildPodVisuals(Transform pod, float bodyRadius)
        {
            float podDepth = Mathf.Clamp(bodyRadius * 0.16f, 0.22f, 0.48f);
            float podHeight = Mathf.Clamp(bodyRadius * 0.11f, 0.16f, 0.34f);
            float podWidth = Mathf.Clamp(bodyRadius * 0.22f, 0.32f, 0.72f);
            float nozzleLength = Mathf.Clamp(bodyRadius * 0.12f, 0.18f, 0.38f);
            float nozzleDiameter = Mathf.Clamp(bodyRadius * 0.055f, 0.08f, 0.18f);
            float plumeLength = Mathf.Clamp(bodyRadius * 1.55f, 2.4f, 4.8f);
            float plumeDiameter = Mathf.Clamp(nozzleDiameter * 4.6f, 0.38f, 0.9f);

            Transform body = EnsurePrimitive(pod, "Pod_Body", PrimitiveType.Cube, PodMaterial()).transform;
            body.localPosition = new Vector3(podDepth * 0.45f, 0f, 0f);
            body.localRotation = Quaternion.identity;
            body.localScale = new Vector3(podDepth, podHeight, podWidth);

            PlaceNozzle(
                EnsurePrimitive(pod, RcsJetLayout.NozzleName(RcsComponent.RcsNozzle.Aft), PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.down,
                new Vector3(podDepth * 0.35f, -podHeight * 0.5f - nozzleLength * 0.4f, 0f),
                nozzleLength,
                nozzleDiameter);

            PlaceNozzle(
                EnsurePrimitive(pod, RcsJetLayout.NozzleName(RcsComponent.RcsNozzle.Outboard), PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.right,
                new Vector3(podDepth + nozzleLength * 0.45f, 0f, 0f),
                nozzleLength,
                nozzleDiameter);

            PlaceNozzle(
                EnsurePrimitive(pod, RcsJetLayout.NozzleName(RcsComponent.RcsNozzle.TangentialPositive), PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.forward,
                new Vector3(podDepth * 0.35f, 0f, podWidth * 0.5f + nozzleLength * 0.4f),
                nozzleLength,
                nozzleDiameter);

            PlaceNozzle(
                EnsurePrimitive(pod, RcsJetLayout.NozzleName(RcsComponent.RcsNozzle.TangentialNegative), PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.back,
                new Vector3(podDepth * 0.35f, 0f, -podWidth * 0.5f - nozzleLength * 0.4f),
                nozzleLength,
                nozzleDiameter);

            PlacePlume(
                EnsurePrimitive(pod, RcsJetLayout.PlumeName(RcsComponent.RcsNozzle.Aft), PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.down,
                new Vector3(podDepth * 0.35f, -podHeight * 0.5f - nozzleLength - plumeLength * 0.45f, 0f),
                plumeLength,
                plumeDiameter);

            PlacePlume(
                EnsurePrimitive(pod, RcsJetLayout.PlumeName(RcsComponent.RcsNozzle.Outboard), PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.right,
                new Vector3(podDepth + nozzleLength + plumeLength * 0.45f, 0f, 0f),
                plumeLength,
                plumeDiameter);

            PlacePlume(
                EnsurePrimitive(pod, RcsJetLayout.PlumeName(RcsComponent.RcsNozzle.TangentialPositive), PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.forward,
                new Vector3(podDepth * 0.35f, 0f, podWidth * 0.5f + nozzleLength + plumeLength * 0.45f),
                plumeLength,
                plumeDiameter);

            PlacePlume(
                EnsurePrimitive(pod, RcsJetLayout.PlumeName(RcsComponent.RcsNozzle.TangentialNegative), PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.back,
                new Vector3(podDepth * 0.35f, 0f, -podWidth * 0.5f - nozzleLength - plumeLength * 0.45f),
                plumeLength,
                plumeDiameter);
        }

        /// <summary>
        /// Returns the cached neutral plume scale so command visuals can pulse
        /// relative to the geometry created during layout.
        /// </summary>
        public static Vector3 GetPlumeBaseScale(Transform plume)
        {
            return PlumeBaseScales.TryGetValue(plume, out var cachedScale) && cachedScale.sqrMagnitude > 0.0001f
                ? cachedScale
                : plume.localScale;
        }

        /// <summary>
        /// Finds or creates a named primitive child, removes its collider, and
        /// assigns the material used by generated RCS geometry.
        /// </summary>
        static GameObject EnsurePrimitive(Transform parent, string childName, PrimitiveType type, Material material)
        {
            Transform existing = parent.Find(childName);
            GameObject go;

            if (existing)
            {
                go = existing.gameObject;
            }
            else
            {
                go = GameObject.CreatePrimitive(type);
                go.name = childName;
                go.transform.SetParent(parent, false);
            }

            var collider = go.GetComponent<Collider>();
            if (collider)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = material;
            }

            return go;
        }

        /// <summary>
        /// Places and sizes a cylinder primitive so its local up axis points
        /// along the requested nozzle direction.
        /// </summary>
        static void PlaceNozzle(Transform nozzle, Vector3 localDirection, Vector3 localPosition, float length, float diameter)
        {
            nozzle.localPosition = localPosition;
            nozzle.localRotation = Quaternion.FromToRotation(Vector3.up, localDirection.normalized);
            nozzle.localScale = new Vector3(diameter, length * 0.5f, diameter);
            nozzle.gameObject.SetActive(true);
        }

        /// <summary>
        /// Places a visual plume on the same axis as a nozzle, caches its base
        /// scale, and attaches the light and particle effects used by command previews.
        /// </summary>
        static void PlacePlume(Transform plume, Vector3 localDirection, Vector3 localPosition, float length, float diameter)
        {
            PlaceNozzle(plume, localDirection, localPosition, length, diameter);
            PlumeBaseScales[plume] = plume.localScale;
            RcsVisualController.EnsurePlumeLight(plume, length);
            RcsVisualController.EnsureColdGasParticles(plume, length, diameter, ParticleMaterial());
            plume.gameObject.SetActive(false);
        }

        /// <summary>
        /// Lazily creates the shared dark pod material used by generated RCS pod bodies.
        /// </summary>
        static Material PodMaterial()
        {
            if (_podMaterial) return _podMaterial;

            _podMaterial = new Material(FindUsableShader())
            {
                name = "Generated_RCS_Pod",
                color = new Color(0.08f, 0.09f, 0.11f, 1f)
            };
            return _podMaterial;
        }

        /// <summary>
        /// Lazily creates the shared metallic material used by generated RCS nozzles.
        /// </summary>
        static Material NozzleMaterial()
        {
            if (_nozzleMaterial) return _nozzleMaterial;

            _nozzleMaterial = new Material(FindUsableShader())
            {
                name = "Generated_RCS_Nozzle",
                color = new Color(0.52f, 0.54f, 0.58f, 1f)
            };
            return _nozzleMaterial;
        }

        /// <summary>
        /// Lazily creates the translucent blue material used by visual cold-gas plumes.
        /// </summary>
        static Material PlumeMaterial()
        {
            if (_plumeMaterial) return _plumeMaterial;

            _plumeMaterial = new Material(FindUsableShader())
            {
                name = "Generated_RCS_ColdGasPlume",
                color = new Color(0.72f, 0.9f, 1f, 0.55f)
            };
            return _plumeMaterial;
        }

        /// <summary>
        /// Lazily creates a particle-compatible cold-gas material, preferring URP
        /// particle shaders and falling back to simple built-in shaders.
        /// </summary>
        static Material ParticleMaterial()
        {
            if (_particleMaterial) return _particleMaterial;

            _particleMaterial = new Material(
                Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Sprites/Default") ??
                FindUsableShader())
            {
                name = "Generated_RCS_ColdGasParticles",
                color = new Color(0.65f, 0.92f, 1f, 0.55f)
            };
            return _particleMaterial;
        }

        /// <summary>
        /// Chooses the first common lit shader available in the active render
        /// pipeline, allowing generated geometry to work in URP and fallback projects.
        /// </summary>
        static Shader FindUsableShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Standard") ??
                   Shader.Find("Diffuse");
        }
    }
}
