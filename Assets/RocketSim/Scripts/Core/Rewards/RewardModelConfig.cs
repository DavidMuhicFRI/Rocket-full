// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardModelConfig.cs
// Purpose: Stores one editable reward-factor set per scenario, handles defaults
// and old saved data, and applies named thesis experiment presets.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace RocketSim
{
    [Serializable]
    public class RewardModelConfig
    {
        // `landing` is the serialized legacy field for the chopstick task.
        public ScenarioRewardFactors landing = new();
        public ScenarioRewardFactors legLanding = new();
        public ScenarioRewardFactors hover = new();
        public ScenarioRewardFactors hoverTracking = new();
        public ScenarioRewardFactors takeoff = new();
        public ScenarioRewardFactors bellyFlop = new();

        /// <summary>
        /// Creates missing scenario reward-factor objects and migrates them to
        /// the current default schema.
        /// </summary>
        public void EnsureDefaults()
        {
            landing ??= new ScenarioRewardFactors();
            legLanding ??= new ScenarioRewardFactors();
            hover ??= new ScenarioRewardFactors();
            hoverTracking ??= new ScenarioRewardFactors();
            takeoff ??= new ScenarioRewardFactors();
            bellyFlop ??= new ScenarioRewardFactors();

            landing.EnsureInitialized();
            legLanding.EnsureInitialized();
            hover.EnsureInitialized();
            hoverTracking.EnsureInitialized();
            takeoff.EnsureInitialized();
            bellyFlop.EnsureInitialized();
        }

        /// <summary>
        /// Returns the reward-factor set used by the selected scenario.
        /// </summary>
        public ScenarioRewardFactors ForScenario(ScenarioType scenario)
        {
            EnsureDefaults();
            return scenario switch
            {
                ScenarioType.ChopstickLanding => landing,
                ScenarioType.LegLanding => legLanding,
                ScenarioType.Hover => hover,
                ScenarioType.HoverTracking => hoverTracking,
                ScenarioType.Takeoff => takeoff,
                ScenarioType.BellyFlop => bellyFlop,
                _ => landing
            };
        }

        /// <summary>
        /// Resets a scenario's reward factors to balanced values, applies the
        /// named preset multipliers, and clamps the result.
        /// </summary>
        public void ApplyPreset(ScenarioType scenario, string presetName)
        {
            var f = ForScenario(scenario);
            f.Reset();

            switch (presetName)
            {
                case "Upright Focus":
                    f.uprightness = 1.7f;
                    f.rotation = 1.25f;
                    f.speed = 1.1f;
                    f.progress = 0.9f;
                    break;
                case "Speed Discipline":
                    f.speed = 1.7f;
                    f.verticalSpeed = 1.6f;
                    f.settle = 1.25f;
                    f.tracking = 0.9f;
                    f.progress = 0.85f;
                    break;
                case "Descent Focus":
                    f.descentDrive = 1.7f;
                    f.targetPrecision = 1.15f;
                    f.uprightness = 1.1f;
                    break;
                case "Target Precision":
                    f.targetPrecision = 1.65f;
                    f.altitude = 1.25f;
                    f.settle = 1.25f;
                    f.speed = 1.1f;
                    break;
                case "Chopstick Catch":
                    f.targetPrecision = 1.7f;
                    f.orientation = 1.65f;
                    f.uprightness = 1.25f;
                    f.rotation = 1.2f;
                    f.descentDrive = 1.1f;
                    break;
                case "Efficient Control":
                    f.controlEffort = 1.8f;
                    f.speed = 1.15f;
                    f.rotation = 1.15f;
                    f.progress = 0.9f;
                    break;
                case "Soft Landing":
                    f.uprightness = 1.3f;
                    f.targetPrecision = 1.2f;
                    f.rotation = 1.2f;
                    f.descentDrive = 1.1f;
                    break;
                case "Stable Hover":
                    f.altitude = 1.45f;
                    f.targetPrecision = 1.25f;
                    f.uprightness = 1.3f;
                    f.speed = 1.35f;
                    f.rotation = 1.3f;
                    break;
                case "Track And Settle":
                    f.tracking = 1.5f;
                    f.settle = 1.45f;
                    f.speed = 1.2f;
                    f.verticalSpeed = 1.15f;
                    break;
                case "Fast Approach":
                    f.tracking = 1.7f;
                    f.settle = 0.9f;
                    f.speed = 0.9f;
                    f.verticalSpeed = 0.9f;
                    break;
                case "Ascent Drive":
                    f.progress = 1.65f;
                    f.verticalSpeed = 1.35f;
                    f.targetPrecision = 0.9f;
                    f.speed = 0.9f;
                    break;
                case "Flip Practice":
                    f.bellyAttitude = 1.65f;
                    f.uprightness = 1.25f;
                    f.rotation = 0.85f;
                    f.speed = 1.1f;
                    break;
            }

            f.presetName = presetName;
            f.Clamp();
        }
    }

    [Serializable]
    public class ScenarioRewardFactors
    {
        const int CurrentSchemaVersion = 4;

        public int schemaVersion = CurrentSchemaVersion;
        public string presetName = "Balanced";
        public float targetPrecision = 1f;
        public float altitude = 1f;
        public float uprightness = 1f;
        public float speed = 1f;
        public float verticalSpeed = 1f;
        public float descentDrive = 1f;
        public float ascentPenalty = 1f;
        public float timePressure = 1f;
        public float rotation = 1f;
        public float orientation = 1f;
        public float controlEffort = 1f;
        public float engineRestart = 1f;
        public float progress = 1f;
        public float tracking = 1f;
        public float settle = 1f;
        public float bellyAttitude = 1f;
        public float terminal = 1f;

        /// <summary>
        /// Restores all reward multipliers to the balanced baseline and updates
        /// the schema marker.
        /// </summary>
        public void Reset()
        {
            targetPrecision = 1f;
            altitude = 1f;
            uprightness = 1f;
            speed = 1f;
            verticalSpeed = 1f;
            descentDrive = 1f;
            ascentPenalty = 1f;
            timePressure = 1f;
            rotation = 1f;
            orientation = 1f;
            controlEffort = 1f;
            engineRestart = 1f;
            progress = 1f;
            tracking = 1f;
            settle = 1f;
            bellyAttitude = 1f;
            terminal = 1f;
            presetName = "Balanced";
            schemaVersion = CurrentSchemaVersion;
        }

        /// <summary>
        /// Clamps reward multipliers to the UI-supported range so corrupted or
        /// hand-edited configs cannot destabilize rewards.
        /// </summary>
        public void Clamp()
        {
            targetPrecision = Mathf.Clamp(targetPrecision, 0f, 2.5f);
            altitude = Mathf.Clamp(altitude, 0f, 2.5f);
            uprightness = Mathf.Clamp(uprightness, 0f, 2.5f);
            speed = Mathf.Clamp(speed, 0f, 2.5f);
            verticalSpeed = Mathf.Clamp(verticalSpeed, 0f, 2.5f);
            descentDrive = Mathf.Clamp(descentDrive, 0f, 2.5f);
            ascentPenalty = Mathf.Clamp(ascentPenalty, 0f, 2.5f);
            timePressure = Mathf.Clamp(timePressure, 0f, 2.5f);
            rotation = Mathf.Clamp(rotation, 0f, 2.5f);
            orientation = Mathf.Clamp(orientation, 0f, 2.5f);
            controlEffort = Mathf.Clamp(controlEffort, 0f, 2.5f);
            engineRestart = Mathf.Clamp(engineRestart, 0f, 2.5f);
            progress = Mathf.Clamp(progress, 0f, 2.5f);
            tracking = Mathf.Clamp(tracking, 0f, 2.5f);
            settle = Mathf.Clamp(settle, 0f, 2.5f);
            bellyAttitude = Mathf.Clamp(bellyAttitude, 0f, 2.5f);
            terminal = Mathf.Clamp(terminal, 0f, 2.5f);
        }

        /// <summary>
        /// Migrates missing or older serialized reward-factor data and then
        /// clamps the values before the reward models read them.
        /// </summary>
        public void EnsureInitialized()
        {
            bool missingValues =
                targetPrecision <= 0f &&
                altitude <= 0f &&
                uprightness <= 0f &&
                speed <= 0f &&
                verticalSpeed <= 0f &&
                descentDrive <= 0f &&
                ascentPenalty <= 0f &&
                timePressure <= 0f &&
                rotation <= 0f &&
                orientation <= 0f &&
                controlEffort <= 0f &&
                engineRestart <= 0f &&
                progress <= 0f &&
                tracking <= 0f &&
                settle <= 0f &&
                bellyAttitude <= 0f &&
                terminal <= 0f;

            if (missingValues)
                Reset();

            if (schemaVersion < CurrentSchemaVersion)
            {
                descentDrive = descentDrive <= 0f ? 1f : descentDrive;
                ascentPenalty = ascentPenalty <= 0f ? 1f : ascentPenalty;
                timePressure = timePressure <= 0f ? 1f : timePressure;
                orientation = orientation <= 0f ? 1f : orientation;
                engineRestart = engineRestart <= 0f ? 1f : engineRestart;
                schemaVersion = CurrentSchemaVersion;
            }

            if (string.IsNullOrEmpty(presetName))
                presetName = "Custom";

            Clamp();
        }
    }}
