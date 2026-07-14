// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/RCS/RcsVisualController.cs
// Purpose: Mirrors RCS valve commands with generated cold-gas meshes, lights,
// and particles. It changes presentation only, never physical thrust.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    internal static class RcsVisualController
    {
        /// <summary>
        /// Shows, scales, and animates each generated plume according to the
        /// current RCS command buffer so actuator activity is visible in scene.
        /// </summary>
        public static void ShowCommands(Transform root, float[] commands)
        {
            if (commands == null)
            {
                ClearVisuals(root);
                return;
            }

            for (int i = 0; i < RcsJetLayout.FalconPodCount; i++)
            {
                Transform pod = RcsLayoutBuilder.GetPod(root, i);
                if (!pod) continue;

                SetPlume(pod, RcsComponent.RcsNozzle.Aft, RcsJetLayout.CommandValue(commands, i, RcsComponent.RcsNozzle.Aft));
                SetPlume(pod, RcsComponent.RcsNozzle.Outboard, RcsJetLayout.CommandValue(commands, i, RcsComponent.RcsNozzle.Outboard));
                SetPlume(pod, RcsComponent.RcsNozzle.TangentialPositive, RcsJetLayout.CommandValue(commands, i, RcsComponent.RcsNozzle.TangentialPositive));
                SetPlume(pod, RcsComponent.RcsNozzle.TangentialNegative, RcsJetLayout.CommandValue(commands, i, RcsComponent.RcsNozzle.TangentialNegative));
            }
        }

        /// <summary>
        /// Clears generated RCS plume meshes, lights, and particles.
        /// </summary>
        public static void ClearVisuals(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
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

        /// <summary>
        /// Adds or updates the light used to make an active cold-gas plume visible.
        /// </summary>
        public static void EnsurePlumeLight(Transform plume, float length)
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

        /// <summary>
        /// Adds or updates the particle system used by a generated cold-gas plume.
        /// </summary>
        public static void EnsureColdGasParticles(Transform plume, float length, float diameter, Material particleMaterial)
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
                renderer.material = particleMaterial;
            }
        }

        /// <summary>
        /// Applies one command strength to a plume by enabling its mesh, light,
        /// and particles, or hiding the plume when the command is near zero.
        /// </summary>
        static void SetPlume(Transform pod, RcsComponent.RcsNozzle nozzle, float strength)
        {
            Transform plume = pod.Find(RcsJetLayout.PlumeName(nozzle));
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

            Vector3 baseScale = RcsGeometryBuilder.GetPlumeBaseScale(plume);
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
    }
}
