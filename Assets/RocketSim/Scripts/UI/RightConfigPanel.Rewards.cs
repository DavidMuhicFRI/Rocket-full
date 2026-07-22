// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Rewards.cs
// Purpose: Builds the Rewards tab, named reward presets, and task-specific reward-factor sliders.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>Builds the scenario-specific reward controls in one dedicated tab.</summary>
        void BuildRewardsTab(VisualElement c)
        {
            c.Clear();
            BuildRewardModelSection(c);
            c.Add(BuildGroupDetail(
                "Reward signals are the numeric feedback given to the agent. " +
                "A value above 1 emphasizes that goal; below 1 reduces its influence."));
        }

        enum RewardFactor
        {
            TargetPrecision,
            Altitude,
            Uprightness,
            Speed,
            VerticalSpeed,
            DescentDrive,
            Rotation,
            Orientation,
            ControlEffort,
            EngineRestart,
            Progress,
            Tracking,
            Settle,
            BellyAttitude,
            Terminal
        }

        static readonly string[] ChopstickLandingRewardPresets =
        {
            "Balanced", "Chopstick Catch", "Descent Focus", "Soft Landing", "Upright Focus", "Target Precision", "Efficient Control"
        };

        static readonly string[] LegLandingRewardPresets =
        {
            "Balanced", "Soft Landing", "Descent Focus", "Upright Focus", "Target Precision", "Efficient Control"
        };

        static readonly string[] HoverRewardPresets =
        {
            "Balanced", "Stable Hover", "Speed Discipline", "Target Precision", "Efficient Control"
        };

        static readonly string[] HoverTrackingRewardPresets =
        {
            "Balanced", "Track And Settle", "Fast Approach", "Stable Hover", "Speed Discipline", "Efficient Control"
        };

        static readonly string[] TakeoffRewardPresets =
        {
            "Balanced", "Ascent Drive", "Upright Focus", "Target Precision", "Efficient Control"
        };

        static readonly string[] BellyFlopRewardPresets =
        {
            "Balanced", "Flip Practice", "Soft Landing", "Upright Focus", "Speed Discipline"
        };

        static readonly string[] BalancedRewardPreset = { "Balanced" };

        // These catalogs are the UI-to-config map for each task. Keeping label,
        // meaning, and config key together prevents a slider from editing the
        // wrong reward factor when task-specific controls change.
        static readonly (RewardFactor factor, string label, string hint)[] ChopstickLandingRewardSliders =
        {
            (RewardFactor.TargetPrecision, "Target Precision", "Scales pad-centering reward and horizontal drift penalty."),
            (RewardFactor.Orientation, "Chopstick Alignment", "Scales near-ground reward for matching the configured target yaw."),
            (RewardFactor.DescentDrive, "Vertical Profile", "Scales the altitude-based target vertical speed. Upward motion is bad because it is far from this target."),
            (RewardFactor.Uprightness, "Uprightness", "Scales upright posture shaping during descent."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate reward and angular-rate penalty."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful throttle, gimbal, and fin use."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales success and failure rewards at episode end."),
        };

        static readonly (RewardFactor factor, string label, string hint)[] LegLandingRewardSliders =
        {
            (RewardFactor.TargetPrecision, "Pad Centering", "Scales horizontal centering toward the physical landing pad."),
            (RewardFactor.DescentDrive, "Vertical Profile", "Scales the altitude-based target vertical speed without prescribing throttle."),
            (RewardFactor.Uprightness, "Uprightness", "Scales upright posture shaping during descent."),
            (RewardFactor.Speed, "Touchdown Motion", "Scales near-pad horizontal speed discipline."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate penalties near touchdown."),
            (RewardFactor.Settle, "Contact And Settle", "Scales one-off first-contact and stable-landing events."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful throttle, gimbal, fin, and RCS use."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales success and failure rewards at episode end."),
        };

        static readonly (RewardFactor factor, string label, string hint)[] HoverRewardSliders =
        {
            (RewardFactor.Altitude, "Altitude Hold", "Scales the fixed-height hover reward."),
            (RewardFactor.TargetPrecision, "Target Precision", "Scales horizontal centering around the pad."),
            (RewardFactor.Uprightness, "Uprightness", "Scales upright posture shaping."),
            (RewardFactor.Speed, "Speed Discipline", "Scales reward for calm, low-speed hover motion."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate reward and angular-rate penalty."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful control effort."),
            (RewardFactor.EngineRestart, "Engine Restarts", "Scales the one-off penalty for reigniting an engine after it has shut down. The pre-running hover engine is not penalized."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales terminal failure reward."),
        };

        static readonly (RewardFactor factor, string label, string hint)[] HoverTrackingRewardSliders =
        {
            (RewardFactor.Altitude, "Altitude Hold", "Scales the shared hover altitude reward."),
            (RewardFactor.TargetPrecision, "Target Precision", "Scales horizontal centering in the shared hover baseline."),
            (RewardFactor.Uprightness, "Uprightness", "Scales upright posture shaping."),
            (RewardFactor.Speed, "Speed Discipline", "Scales calm speed reward and overspeed penalties."),
            (RewardFactor.VerticalSpeed, "Vertical Calm", "Scales vertical-speed reward inside the settle phase."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate reward and angular-rate penalty."),
            (RewardFactor.Tracking, "Approach Drive", "Scales reward for moving toward the shifted pad."),
            (RewardFactor.Settle, "Settle Precision", "Scales reward for tight centering once close to the pad."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful control effort."),
            (RewardFactor.EngineRestart, "Engine Restarts", "Scales the one-off penalty for reigniting an engine after it has shut down. Necessary low-mass pulse control remains allowed."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales terminal failure reward."),
        };

        static readonly (RewardFactor factor, string label, string hint)[] TakeoffRewardSliders =
        {
            (RewardFactor.Progress, "Ascent Drive", "Scales altitude progress reward."),
            (RewardFactor.VerticalSpeed, "Climb Speed", "Scales upward-velocity reward."),
            (RewardFactor.Uprightness, "Uprightness", "Scales upright posture shaping during ascent."),
            (RewardFactor.TargetPrecision, "Drift Control", "Scales horizontal drift reward and penalty."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate reward and angular-rate penalty."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful control effort."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales success and failure rewards at episode end."),
        };

        static readonly (RewardFactor factor, string label, string hint)[] BellyFlopRewardSliders =
        {
            (RewardFactor.BellyAttitude, "Belly Attitude", "Scales high-altitude broadside descent shaping."),
            (RewardFactor.Uprightness, "Landing Upright", "Scales low-altitude upright posture shaping."),
            (RewardFactor.TargetPrecision, "Target Precision", "Scales horizontal centering toward the landing pad."),
            (RewardFactor.Speed, "Landing Speed", "Scales low-speed landing reward and speed penalty."),
            (RewardFactor.Rotation, "Attitude Calm", "Scales angular-rate reward and angular-rate penalty."),
            (RewardFactor.ControlEffort, "Control Efficiency", "Scales the penalty for wasteful control effort."),
            (RewardFactor.Terminal, "Terminal Signal", "Scales touchdown success and failure rewards."),
        };

        /// <summary>
        /// Builds reward preset buttons and scenario-specific reward-factor sliders.
        /// </summary>
        void BuildRewardModelSection(VisualElement root)
        {
            if (root == null) return;

            root.Clear();
            root.Add(UIHelper.SectionLabel("Reward Model"));

            var factors = envConfig.GetRewardFactors(envConfig.scenario);
            var presetRow = new VisualElement();
            presetRow.AddToClassList("rs-reward-preset-row");

            foreach (var presetName in RewardPresetsForScenario(envConfig.scenario))
            {
                string capturedPreset = presetName;
                var btn = new Button(() =>
                {
                    envConfig.ApplyRewardPreset(envConfig.scenario, capturedPreset);
                    BuildRewardModelSection(root);
                }) { text = capturedPreset };
                btn.AddToClassList("rs-reward-preset-btn");
                btn.userData = capturedPreset;
                presetRow.Add(btn);
            }

            root.Add(presetRow);
            RefreshRewardPresetButtons(presetRow, factors.presetName);

            foreach (var slider in RewardSlidersForScenario(envConfig.scenario))
            {
                var capturedSlider = slider;
                root.Add(UIHelper.Slider(capturedSlider.label, GetRewardFactor(factors, capturedSlider.factor), 0f, 2.5f, v =>
                {
                    SetRewardFactor(factors, capturedSlider.factor, v);
                    factors.presetName = "Custom";
                    factors.Clamp();
                    RefreshRewardPresetButtons(presetRow, factors.presetName);
                }, capturedSlider.hint));
            }
        }

        /// <summary>
        /// Updates reward preset button styling after a preset or custom slider edit.
        /// </summary>
        void RefreshRewardPresetButtons(VisualElement row, string activePreset)
        {
            foreach (var child in row.Children())
                if (child is Button { userData: string presetName } b)
                    b.EnableInClassList("rs-reward-preset-btn-active", presetName == activePreset);
        }

        /// <summary>Returns only the named reward presets supported by a task.</summary>
        static string[] RewardPresetsForScenario(ScenarioType scenario)
        {
            return scenario switch
            {
                ScenarioType.ChopstickLanding => ChopstickLandingRewardPresets,
                ScenarioType.LegLanding => LegLandingRewardPresets,
                ScenarioType.Hover => HoverRewardPresets,
                ScenarioType.HoverTracking => HoverTrackingRewardPresets,
                ScenarioType.Takeoff => TakeoffRewardPresets,
                ScenarioType.BellyFlop => BellyFlopRewardPresets,
                _ => BalancedRewardPreset
            };
        }

        static (RewardFactor factor, string label, string hint)[] RewardSlidersForScenario(ScenarioType scenario)
        {
            return scenario switch
            {
                ScenarioType.ChopstickLanding => ChopstickLandingRewardSliders,
                ScenarioType.LegLanding => LegLandingRewardSliders,
                ScenarioType.Hover => HoverRewardSliders,
                ScenarioType.HoverTracking => HoverTrackingRewardSliders,
                ScenarioType.Takeoff => TakeoffRewardSliders,
                ScenarioType.BellyFlop => BellyFlopRewardSliders,
                _ => Array.Empty<(RewardFactor factor, string label, string hint)>()
            };
        }

        /// <summary>
        /// Reads the field represented by the internal RewardFactor key. This
        /// keeps the UI catalog compact without reflection or string field names.
        /// </summary>
        static float GetRewardFactor(ScenarioRewardFactors factors, RewardFactor factor)
        {
            return factor switch
            {
                RewardFactor.TargetPrecision => factors.targetPrecision,
                RewardFactor.Altitude => factors.altitude,
                RewardFactor.Uprightness => factors.uprightness,
                RewardFactor.Speed => factors.speed,
                RewardFactor.VerticalSpeed => factors.verticalSpeed,
                RewardFactor.DescentDrive => factors.descentDrive,
                RewardFactor.Rotation => factors.rotation,
                RewardFactor.Orientation => factors.orientation,
                RewardFactor.ControlEffort => factors.controlEffort,
                RewardFactor.EngineRestart => factors.engineRestart,
                RewardFactor.Progress => factors.progress,
                RewardFactor.Tracking => factors.tracking,
                RewardFactor.Settle => factors.settle,
                RewardFactor.BellyAttitude => factors.bellyAttitude,
                RewardFactor.Terminal => factors.terminal,
                _ => 1f
            };
        }

        /// <summary>
        /// Writes the field represented by the internal RewardFactor key. It is
        /// the inverse of GetRewardFactor and is used by every reward slider.
        /// </summary>
        static void SetRewardFactor(ScenarioRewardFactors factors, RewardFactor factor, float value)
        {
            switch (factor)
            {
                case RewardFactor.TargetPrecision: factors.targetPrecision = value; break;
                case RewardFactor.Altitude: factors.altitude = value; break;
                case RewardFactor.Uprightness: factors.uprightness = value; break;
                case RewardFactor.Speed: factors.speed = value; break;
                case RewardFactor.VerticalSpeed: factors.verticalSpeed = value; break;
                case RewardFactor.DescentDrive: factors.descentDrive = value; break;
                case RewardFactor.Rotation: factors.rotation = value; break;
                case RewardFactor.Orientation: factors.orientation = value; break;
                case RewardFactor.ControlEffort: factors.controlEffort = value; break;
                case RewardFactor.EngineRestart: factors.engineRestart = value; break;
                case RewardFactor.Progress: factors.progress = value; break;
                case RewardFactor.Tracking: factors.tracking = value; break;
                case RewardFactor.Settle: factors.settle = value; break;
                case RewardFactor.BellyAttitude: factors.bellyAttitude = value; break;
                case RewardFactor.Terminal: factors.terminal = value; break;
            }
        }
    }
}
