using System.Collections.Generic;
using UnityEngine;

namespace RocketSim
{
    // Falcon-style cold-gas attitude control: four pods near the interstage,
    // with small nozzle bells on each pod for roll and lateral attitude control.
    public class RcsComponent : MonoBehaviour
    {
        public const int FalconPodCount = 4;
        public const int NozzlesPerPod = 3;
        public const int JetCount = FalconPodCount * NozzlesPerPod;

        public enum RcsNozzle
        {
            Outboard = 0,
            RollPositive = 1,
            RollNegative = 2
        }

        static readonly string[] PodNames = { "RCS0", "RCS1", "RCS2", "RCS3" };
        static readonly float[] PodAnglesDeg = { 0f, 180f, 90f, 270f };

        static Material _podMaterial;
        static Material _nozzleMaterial;
        static Material _plumeMaterial;
        static Material _particleMaterial;
        static readonly Dictionary<Transform, Vector3> PlumeBaseScales = new();

        [Header("RCS Specs - written by RocketAssembly.ApplyPartsConfig")]
        [Range(100f, 15000f)] public float thrustPerThruster = 6000f;

        public int PodCount => FalconPodCount;
        public int VisualNozzleCount => JetCount;

        public static int JetIndex(int podIndex, RcsNozzle nozzle)
        {
            return podIndex * NozzlesPerPod + (int)nozzle;
        }

        public Transform GetPod(int podIndex)
        {
            return podIndex >= 0 && podIndex < FalconPodCount ? FindDirectChild(PodNames[podIndex]) : null;
        }

        public Vector3 WorldDirectionForJet(int jetIndex)
        {
            return WorldExhaustDirectionForJet(jetIndex);
        }

        public Vector3 WorldExhaustDirectionForJet(int jetIndex)
        {
            Transform pod = GetPod(jetIndex / NozzlesPerPod);
            if (!pod) return transform.right;

            return pod.TransformDirection(LocalDirectionForNozzle((RcsNozzle)(jetIndex % NozzlesPerPod))).normalized;
        }

        public Vector3 WorldForceDirectionForJet(int jetIndex)
        {
            return -WorldExhaustDirectionForJet(jetIndex);
        }

        public Vector3 WorldPositionForJet(int jetIndex)
        {
            Transform pod = GetPod(jetIndex / NozzlesPerPod);
            if (!pod) return transform.position;

            Transform nozzle = pod.Find(NozzleName((RcsNozzle)(jetIndex % NozzlesPerPod)));
            return nozzle ? nozzle.position : pod.position;
        }

        public void Reposition(float bodyRadius = 1.83f)
        {
            EnsurePodRoots();

            float podRadius = bodyRadius * 1.04f;
            for (int i = 0; i < FalconPodCount; i++)
            {
                Transform pod = FindDirectChild(PodNames[i]);
                if (!pod) continue;

                float angleDeg = PodAnglesDeg[i];
                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)).normalized;
                Vector3 tangent = Vector3.Cross(radial, Vector3.up).normalized;

                pod.gameObject.SetActive(true);
                pod.localPosition = radial * podRadius;
                pod.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                DisableLegacyRootRenderer(pod);
                BuildPodVisuals(pod, bodyRadius);
            }
        }

        public void ShowCommands(float[] commands)
        {
            if (commands == null)
            {
                ClearVisuals();
                return;
            }

            for (int i = 0; i < FalconPodCount; i++)
            {
                Transform pod = FindDirectChild(PodNames[i]);
                if (!pod) continue;

                SetPlume(pod, "Plume_Outboard", CommandValue(commands, i, RcsNozzle.Outboard));
                SetPlume(pod, "Plume_Roll_Pos", CommandValue(commands, i, RcsNozzle.RollPositive));
                SetPlume(pod, "Plume_Roll_Neg", CommandValue(commands, i, RcsNozzle.RollNegative));
            }
        }

        public void ShowCommand(Vector3 command)
        {
            float[] commands = new float[JetCount];
            float roll = Mathf.Clamp(command.z, -1f, 1f);
            float pitch = Mathf.Clamp(command.x, -1f, 1f);
            float yaw = Mathf.Clamp(command.y, -1f, 1f);

            AddVirtualAxisCommand(commands, Vector3.up, roll);
            AddVirtualAxisCommand(commands, Vector3.right, pitch);
            AddVirtualAxisCommand(commands, Vector3.forward, yaw);
            ShowCommands(commands);
        }

        public void ClearVisuals()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("Plume_")) continue;

                var light = child.GetComponent<Light>();
                if (light)
                    light.enabled = false;

                var particles = child.GetComponent<ParticleSystem>();
                if (particles)
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                child.gameObject.SetActive(false);
            }
        }

        void EnsurePodRoots()
        {
            for (int i = 0; i < FalconPodCount; i++)
            {
                if (FindDirectChild(PodNames[i])) continue;

                var pod = new GameObject(PodNames[i]);
                pod.transform.SetParent(transform, false);
            }
        }

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

        void DisableLegacyRootRenderer(Transform pod)
        {
            var renderer = pod.GetComponent<MeshRenderer>();
            if (renderer)
                renderer.enabled = false;
        }

        void BuildPodVisuals(Transform pod, float bodyRadius)
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
                EnsurePrimitive(pod, "Nozzle_Outboard", PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.right,
                new Vector3(podDepth + nozzleLength * 0.45f, 0f, 0f),
                nozzleLength,
                nozzleDiameter);

            PlaceNozzle(
                EnsurePrimitive(pod, "Nozzle_Roll_Pos", PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.back,
                new Vector3(podDepth * 0.35f, 0f, -podWidth * 0.5f - nozzleLength * 0.4f),
                nozzleLength,
                nozzleDiameter);

            PlaceNozzle(
                EnsurePrimitive(pod, "Nozzle_Roll_Neg", PrimitiveType.Cylinder, NozzleMaterial()).transform,
                Vector3.forward,
                new Vector3(podDepth * 0.35f, 0f, podWidth * 0.5f + nozzleLength * 0.4f),
                nozzleLength,
                nozzleDiameter);

            PlacePlume(
                EnsurePrimitive(pod, "Plume_Outboard", PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.right,
                new Vector3(podDepth + nozzleLength + plumeLength * 0.45f, 0f, 0f),
                plumeLength,
                plumeDiameter);

            PlacePlume(
                EnsurePrimitive(pod, "Plume_Roll_Pos", PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.back,
                new Vector3(podDepth * 0.35f, 0f, -podWidth * 0.5f - nozzleLength - plumeLength * 0.45f),
                plumeLength,
                plumeDiameter);

            PlacePlume(
                EnsurePrimitive(pod, "Plume_Roll_Neg", PrimitiveType.Cylinder, PlumeMaterial()).transform,
                Vector3.forward,
                new Vector3(podDepth * 0.35f, 0f, podWidth * 0.5f + nozzleLength + plumeLength * 0.45f),
                plumeLength,
                plumeDiameter);
        }

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
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = material;
            }

            return go;
        }

        static void PlaceNozzle(Transform nozzle, Vector3 localDirection, Vector3 localPosition, float length, float diameter)
        {
            nozzle.localPosition = localPosition;
            nozzle.localRotation = Quaternion.FromToRotation(Vector3.up, localDirection.normalized);
            nozzle.localScale = new Vector3(diameter, length * 0.5f, diameter);
            nozzle.gameObject.SetActive(true);
        }

        static void PlacePlume(Transform plume, Vector3 localDirection, Vector3 localPosition, float length, float diameter)
        {
            PlaceNozzle(plume, localDirection, localPosition, length, diameter);
            PlumeBaseScales[plume] = plume.localScale;
            EnsurePlumeLight(plume, length);
            EnsureColdGasParticles(plume, length, diameter);
            plume.gameObject.SetActive(false);
        }

        static void SetPlume(Transform pod, string plumeName, float strength)
        {
            Transform plume = pod.Find(plumeName);
            if (!plume) return;

            bool active = strength > 0.03f;
            if (!active)
            {
                var lightOff = plume.GetComponent<Light>();
                if (lightOff)
                    lightOff.enabled = false;

                var particlesOff = plume.GetComponent<ParticleSystem>();
                if (particlesOff)
                    particlesOff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                plume.gameObject.SetActive(false);
                return;
            }

            plume.gameObject.SetActive(true);

            Vector3 baseScale = PlumeBaseScales.TryGetValue(plume, out var cachedScale) && cachedScale.sqrMagnitude > 0.0001f
                ? cachedScale
                : plume.localScale;
            float pulse = 1f + Mathf.Sin(Time.time * 42f + plume.GetSiblingIndex() * 1.7f) * 0.16f;
            float lengthScale = Mathf.Lerp(0.95f, 1.9f, Mathf.Clamp01(strength)) * pulse;
            float widthScale = Mathf.Lerp(1.1f, 1.6f, Mathf.Clamp01(strength));
            plume.localScale = new Vector3(baseScale.x * widthScale, baseScale.y * lengthScale, baseScale.z * widthScale);

            var light = plume.GetComponent<Light>();
            if (light)
            {
                light.enabled = true;
                light.intensity = Mathf.Lerp(1.6f, 6.5f, Mathf.Clamp01(strength)) * pulse;
            }

            var particles = plume.GetComponent<ParticleSystem>();
            if (particles && !particles.isPlaying)
                particles.Play();
        }

        static void EnsurePlumeLight(Transform plume, float length)
        {
            var light = plume.GetComponent<Light>();
            if (!light)
                light = plume.gameObject.AddComponent<Light>();

            light.type = LightType.Point;
            light.color = new Color(0.55f, 0.9f, 1f, 1f);
            light.range = Mathf.Max(4f, length * 3.5f);
            light.intensity = 2.4f;
            light.enabled = false;
        }

        static void EnsureColdGasParticles(Transform plume, float length, float diameter)
        {
            var particles = plume.GetComponent<ParticleSystem>();
            if (!particles)
                particles = plume.gameObject.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(diameter * 0.42f, diameter * 1.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.76f, 0.95f, 1f, 0.72f),
                new Color(0.36f, 0.78f, 1f, 0.18f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 220;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 260f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = diameter * 0.45f;
            shape.length = length * 1.2f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.material = ParticleMaterial();
            }
        }

        static float CommandValue(float[] commands, int podIndex, RcsNozzle nozzle)
        {
            int index = JetIndex(podIndex, nozzle);
            return commands != null && index >= 0 && index < commands.Length ? Mathf.Clamp01(commands[index]) : 0f;
        }

        static Vector3 LocalDirectionForNozzle(RcsNozzle nozzle)
        {
            return nozzle switch
            {
                RcsNozzle.Outboard => Vector3.right,
                RcsNozzle.RollPositive => Vector3.back,
                RcsNozzle.RollNegative => Vector3.forward,
                _ => Vector3.right
            };
        }

        static string NozzleName(RcsNozzle nozzle)
        {
            return nozzle switch
            {
                RcsNozzle.Outboard => "Nozzle_Outboard",
                RcsNozzle.RollPositive => "Nozzle_Roll_Pos",
                RcsNozzle.RollNegative => "Nozzle_Roll_Neg",
                _ => "Nozzle_Outboard"
            };
        }

        static void AddVirtualAxisCommand(float[] commands, Vector3 localAxis, float command)
        {
            if (commands == null || Mathf.Abs(command) < 0.001f) return;

            float magnitude = Mathf.Abs(command);
            if (Mathf.Abs(localAxis.y) > 0.9f)
            {
                RcsNozzle nozzle = command >= 0f ? RcsNozzle.RollNegative : RcsNozzle.RollPositive;
                for (int podIndex = 0; podIndex < FalconPodCount; podIndex++)
                    commands[JetIndex(podIndex, nozzle)] = Mathf.Max(commands[JetIndex(podIndex, nozzle)], magnitude);
                return;
            }

            int selectedPod;
            if (Mathf.Abs(localAxis.x) > 0.9f)
                selectedPod = command >= 0f ? 3 : 2;
            else
                selectedPod = command >= 0f ? 0 : 1;

            int jetIndex = JetIndex(selectedPod, RcsNozzle.Outboard);
            commands[jetIndex] = Mathf.Max(commands[jetIndex], magnitude);
        }

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

        static Shader FindUsableShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Standard") ??
                   Shader.Find("Diffuse");
        }

        void Awake() => Reposition();
    }
}
