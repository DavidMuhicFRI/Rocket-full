// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Vehicle.cs
// Purpose: Builds the Vehicle tab: body, fuel, engines, fins, RCS, presets, validation, and derived readouts.
// Main flow: edit a field -> mark hardware Custom -> update derived readouts ->
// push the new config into the frozen preview. Active runs block this path.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        const int ReferenceEngineCount = 9;
        const int ReferenceFinCount = 4;
        const float EngineDryMassKg = 470f;
        const float GridFinDryMassKg = 200f;
        const string CustomBuildPlayerPrefsKey = "RocketSim.CustomVehicleBuild";

        /// <summary>
        /// Builds the hardware editor tab for presets, body geometry, engines,
        /// grid fins, RCS, and derived mass/thrust readouts.
        /// </summary>
        void BuildVehicleTab(VisualElement c)
        {
            c.Clear();

            var presets = UIHelper.Foldout("Vehicle Presets");
            BuildPresetSection(presets);
            c.Add(presets);

            var body = UIHelper.Foldout("Body and Fuel", true);
            BuildRocketBodySection(body);
            c.Add(body);

            var engines = UIHelper.Foldout("Engines", true);
            BuildMainEngineSection(engines);
            BuildEngineReadoutRows(engines);
            c.Add(engines);

            var actuatorDynamics = UIHelper.Foldout("Advanced Actuator Dynamics");
            BuildEngineTimingSection(actuatorDynamics);
            actuatorDynamics.Add(UIHelper.Slider("Throttle Response", partsConfig.throttleSpoolRate, 0.5f, 20f, v =>
                EditCustomParts(() => partsConfig.throttleSpoolRate = v),
                "How quickly actual throttle catches up with the commanded throttle.", 2, "/s"));
            actuatorDynamics.Add(UIHelper.Slider("Gimbal Slew", partsConfig.gimbalSlewRate, 5f, 90f, v =>
                EditCustomParts(() => partsConfig.gimbalSlewRate = v),
                "How quickly an engine can rotate toward a new gimbal command.", 0, " deg/s"));
            c.Add(actuatorDynamics);

            var fins = UIHelper.Foldout("Grid Fins");
            fins.EnableInClassList("rs-foldout-hardware-disabled", !partsConfig.finsEnabled);
            UIHelper.SetFoldoutMuted(fins, !partsConfig.finsEnabled);
            BuildGridFinsSection(fins);
            c.Add(fins);

            var rcs = UIHelper.Foldout("Reaction Control System");
            rcs.EnableInClassList("rs-foldout-hardware-disabled", !partsConfig.rcsEnabled);
            UIHelper.SetFoldoutMuted(rcs, !partsConfig.rcsEnabled);
            BuildRcsSection(rcs);
            c.Add(rcs);

            RefreshBodyReadouts(c);
            RefreshEngineReadouts(c);
            RefreshFinReadouts(c);
        }

        /// <summary>
        /// Builds preset selection, one local custom-build save slot, and the
        /// lightweight configuration validation action.
        /// </summary>
        void BuildPresetSection(VisualElement root)
        {
            root.Add(BuildPresetSelector());
            var customActions = new VisualElement();
            customActions.AddToClassList("rs-btn-group");
            customActions.Add(UIHelper.ActionButton("Save Custom Build", SaveCustomBuild));
            var load = UIHelper.ActionButton("Load Custom Build", LoadCustomBuild);
            load.SetEnabled(PlayerPrefs.HasKey(CustomBuildPlayerPrefsKey));
            customActions.Add(load);
            root.Add(customActions);
            root.Add(UIHelper.ActionButton("Check Configuration", () =>
            {
                bool valid = ValidateConfiguration(out string error, out string warning);
                ShowNotification(valid
                    ? string.IsNullOrEmpty(warning) ? "Configuration check passed." : warning
                    : error, !valid);
            }));
        }

        /// <summary>
        /// Builds body geometry and starting-fuel controls plus derived mass,
        /// capacity, and aerodynamic-area readouts.
        /// </summary>
        void BuildRocketBodySection(VisualElement root)
        {
            root.Add(UIHelper.Slider("Radius (m)", partsConfig.bodyRadius, 0.5f, 6f, v =>
                EditCustomParts(() => partsConfig.bodyRadius = v, () => RefreshBodyReadouts(root)),
                "Changes rocket width, frontal area, side area, and automatically scaled mass."));
            root.Add(UIHelper.Slider("Height (m)", partsConfig.bodyHeight, 5f, 80f, v =>
                EditCustomParts(() => partsConfig.bodyHeight = v, () => RefreshBodyReadouts(root)),
                "Changes rocket length, side area, and automatically scaled mass."));

            root.Add(UIHelper.Toggle("Use Task Recommended Fuel", partsConfig.useScenarioRecommendedFuel, value =>
            {
                EditCustomParts(() => partsConfig.useScenarioRecommendedFuel = value);
                ApplyCurrentScenarioHardwareDefaults();
                BuildVehicleTab(_tabContents[VehicleTab]);
            }));
            if (!partsConfig.useScenarioRecommendedFuel)
            {
                root.Add(UIHelper.Slider("Starting Fuel", partsConfig.startFuelFraction, 0.01f, 1f, value =>
                {
                    EditCustomParts(() => partsConfig.startFuelFraction = value);
                    partsConfig.ApplyScenarioHardwareDefaults(CurrentScenario());
                    RefreshBodyReadouts(root);
                }, "Sets the fuel loaded at the start of each episode.", 0, "%", valueScale: 100f));
            }

            var dryMassRow = UIHelper.ReadOnly("Dry Mass", "22000 kg");
            dryMassRow.name = "label-dry-mass";
            root.Add(dryMassRow);

            var fuelMassRow = UIHelper.ReadOnly("Max Fuel", "400000 kg");
            fuelMassRow.name = "label-fuel-mass";
            root.Add(fuelMassRow);

            var scenarioFuelRow = UIHelper.ReadOnly("Scenario Fuel", "10 %");
            scenarioFuelRow.name = "label-scenario-fuel";
            root.Add(scenarioFuelRow);

            var fuelRemainingRow = UIHelper.ReadOnly("Start Fuel", "32000 kg");
            fuelRemainingRow.name = "label-fuel-remaining";
            root.Add(fuelRemainingRow);

            var axialAreaRow = UIHelper.ReadOnly("Axial Area", "0 mÂ²");
            axialAreaRow.name = "label-axial-area";
            root.Add(axialAreaRow);

            var lateralAreaRow = UIHelper.ReadOnly("Projected Side Area", "0 mÂ²");
            lateralAreaRow.name = "label-projected-side-area";
            root.Add(lateralAreaRow);
        }

        /// <summary>
        /// Builds engine layout/burn-group selection and the main thrust,
        /// efficiency, spacing, throttle, and gimbal limits.
        /// </summary>
        void BuildMainEngineSection(VisualElement root)
        {
            root.Add(BuildEngineSelector(root));
            if (partsConfig.engineLayout == EngineLayout.Octaweb)
                root.Add(BuildBurnGroupSelector(root));

            root.Add(UIHelper.Toggle("Independent Engine Control", partsConfig.independentEngines, v =>
                EditCustomParts(() => partsConfig.independentEngines = v)));
            root.Add(UIHelper.Slider("Thrust / Engine (kN)", partsConfig.maxThrustPerEngine / 1000f, 50f, 1500f, v =>
                EditCustomParts(() => partsConfig.maxThrustPerEngine = v * 1000f, () => RefreshEngineReadouts(root)),
                "Sets the maximum force produced by each installed main engine."));
            root.Add(UIHelper.Slider("Min Throttle (%)", partsConfig.minThrottle * 100f, 25f, 80f, v =>
                EditCustomParts(() => partsConfig.minThrottle = v / 100f),
                "Sets the lowest non-zero thrust an active engine can hold."));
            root.Add(UIHelper.Slider("Layout Spacing", partsConfig.engineSpacing, 0.2f, 1f, v =>
                EditCustomParts(() => partsConfig.engineSpacing = v),
                "Changes how far the engines sit from the rocket centre."));
            root.Add(UIHelper.Slider("Specific Impulse (s)", partsConfig.specificImpulse, 200f, 450f, v =>
                EditCustomParts(() => partsConfig.specificImpulse = v, () => RefreshEngineReadouts(root)),
                "Sets main-engine fuel efficiency; higher values burn less fuel for the same thrust."));
            root.Add(UIHelper.Slider("Gimbal Range (Â°)", partsConfig.maxGimbalAngle, 1f, 15f, v =>
                EditCustomParts(() => partsConfig.maxGimbalAngle = v),
                "Sets the maximum engine tilt used to steer the rocket."));
        }

        /// <summary>
        /// Builds the engine state-machine timing controls. These delay state
        /// transitions; they do not model detailed turbopump or combustion physics.
        /// </summary>
        void BuildEngineTimingSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Engine Timing"));
            root.Add(UIHelper.Slider("Startup Delay (s)", partsConfig.engineStartupDelay, 0f, 3f, v =>
                EditCustomParts(() => partsConfig.engineStartupDelay = v),
                "Delay between an ignition command and the engine entering its running state."));
            root.Add(UIHelper.Slider("Min Run Time (s)", partsConfig.engineMinimumRunTime, 0f, 5f, v =>
                EditCustomParts(() => partsConfig.engineMinimumRunTime = v),
                "Minimum time an ignited engine must remain running before shutdown."));
            root.Add(UIHelper.Slider("Restart Cooldown (s)", partsConfig.engineRestartCooldown, 0f, 5f, v =>
                EditCustomParts(() => partsConfig.engineRestartCooldown = v),
                "Minimum wait after shutdown before the engine can ignite again."));
            root.Add(UIHelper.Slider("Shutdown State Delay (s)", partsConfig.engineShutdownTransient, 0f, 1f, v =>
                EditCustomParts(() => partsConfig.engineShutdownTransient = v),
                "Delays the Off state after throttle reaches zero; it does not add a thrust-decay curve."));
        }

        /// <summary>Adds derived total-thrust and maximum burn-rate rows.</summary>
        void BuildEngineReadoutRows(VisualElement root)
        {
            var totalThrustRow = UIHelper.ReadOnly("Total Thrust", "0 kN");
            totalThrustRow.name = "label-total-thrust";
            root.Add(totalThrustRow);

            var burnRateRow = UIHelper.ReadOnly("Max Burn Rate", "0 kg/s");
            burnRateRow.name = "label-burn-rate";
            root.Add(burnRateRow);
        }

        /// <summary>
        /// Builds the fin enable/layout controls and, only when enabled, exposes
        /// dimensions, actuator limits, effectiveness, and derived fin area.
        /// </summary>
        void BuildGridFinsSection(VisualElement root)
        {
            root.Add(UIHelper.Toggle("Enabled", partsConfig.finsEnabled, v =>
            {
                EditCustomParts(() => partsConfig.finsEnabled = v);
                BuildVehicleTab(_tabContents[VehicleTab]);
            }));
            if (!partsConfig.finsEnabled) return;
            root.Add(BuildFinSelector(root));
            if (partsConfig.finLayout == FinLayout.FourFins_X)
                root.Add(BuildGroupDetail("Asymmetric layout with alternating 60Â° and 120Â° gaps. Verify these angles against the intended booster reference."));
            root.Add(UIHelper.Slider("Radial Length (m)", partsConfig.finWidthX, 0.2f, 4f, v =>
                EditCustomParts(() => partsConfig.finWidthX = v, () => RefreshFinReadouts(root)),
                "Sets how far each grid fin extends outward from the body."));
            root.Add(UIHelper.Slider("Tangential Width (m)", partsConfig.finWidthZ, 0.2f, 4f, v =>
                EditCustomParts(() => partsConfig.finWidthZ = v, () => RefreshFinReadouts(root)),
                "Sets each grid fin's width around the rocket body."));
            root.Add(UIHelper.Slider("Fin Thickness (m)", partsConfig.finThickness, 0.1f, 1f, v =>
                EditCustomParts(() => partsConfig.finThickness = v),
                "Changes the physical thickness used for the grid-fin shape."));
            root.Add(UIHelper.Slider("Max Angle (Â°)", partsConfig.maxFinAngle, 5f, 60f, v =>
                EditCustomParts(() => partsConfig.maxFinAngle = v),
                "Sets the largest grid-fin deflection available for steering."));
            root.Add(UIHelper.Slider("Slew Rate (Â°/s)", partsConfig.finSlewRate, 10f, 200f, v =>
                EditCustomParts(() => partsConfig.finSlewRate = v),
                "Sets how quickly grid fins can reach a commanded angle."));
            root.Add(UIHelper.Slider("Aerodynamic Effectiveness", partsConfig.liftScale, 0.1f, 3f, v =>
                EditCustomParts(() => partsConfig.liftScale = v),
                "Multiplies the aerodynamic steering force produced by the fins."));

            var finAreaRow = UIHelper.ReadOnly("Fin area", "0 mÂ²");
            finAreaRow.name = "label-fin-area";
            root.Add(finAreaRow);
        }

        /// <summary>
        /// Builds the RCS enable control and, only when enabled, its per-jet
        /// thrust, efficiency, pulse timing, propellant, and dry mass.
        /// </summary>
        void BuildRcsSection(VisualElement root)
        {
            root.Add(UIHelper.Toggle("Enabled", partsConfig.rcsEnabled, v =>
            {
                EditCustomParts(() => partsConfig.rcsEnabled = v);
                BuildVehicleTab(_tabContents[VehicleTab]);
            }));
            if (!partsConfig.rcsEnabled) return;
            root.Add(UIHelper.ReadOnly("Layout", "2 pods Ã— 4 nozzles"));
            root.Add(UIHelper.Slider("Thrust / Jet (N)", partsConfig.rcsThrust, 100f, 2000f, v =>
                EditCustomParts(() => partsConfig.rcsThrust = v),
                "Sets the force produced by each reaction-control jet."));
            root.Add(UIHelper.Slider("Specific Impulse (s)", partsConfig.rcsSpecificImpulse, 30f, 100f, v =>
                EditCustomParts(() => partsConfig.rcsSpecificImpulse = v),
                "Sets RCS propellant efficiency; higher values use less nitrogen."));
            root.Add(UIHelper.Slider("Minimum Pulse (s)", partsConfig.rcsMinimumPulseDuration, 0.02f, 0.5f, v =>
                EditCustomParts(() => partsConfig.rcsMinimumPulseDuration = v),
                "Sets the shortest RCS firing, limiting how finely it can control rotation."));
            root.Add(UIHelper.Slider("Nitrogen Mass (kg)", partsConfig.rcsPropellantMass, 0f, 1000f, v =>
                EditCustomParts(() => partsConfig.rcsPropellantMass = v),
                "Sets the RCS propellant available at the start of an episode."));
            root.Add(UIHelper.Slider("RCS Dry Mass (kg)", partsConfig.rcsDryMass, 0f, 1000f, v =>
                EditCustomParts(() => partsConfig.rcsDryMass = v),
                "Adds the fixed mass of RCS tanks, pods, and plumbing."));
        }

        /// <summary>
        /// Central mutation path for vehicle edits. It marks the build Custom,
        /// applies the requested change, refreshes dependent UI, and updates preview.
        /// </summary>
        void EditCustomParts(Action mutate, Action refresh = null)
        {
            partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
            mutate();
            Dirty();
            refresh?.Invoke();
        }

        /// <summary>
        /// Serializes the current vehicle into the single local PlayerPrefs save
        /// slot used by the panel's simple custom-build workflow.
        /// </summary>
        void SaveCustomBuild()
        {
            partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
            PlayerPrefs.SetString(CustomBuildPlayerPrefsKey, JsonUtility.ToJson(partsConfig));
            PlayerPrefs.Save();
            ShowNotification("Custom vehicle build saved on this computer.", false);
            BuildVehicleTab(_tabContents[VehicleTab]);
        }

        /// <summary>
        /// Restores the local custom build, clamps physical ranges, reconnects
        /// shared config references, and rebuilds every affected tab/readout.
        /// </summary>
        void LoadCustomBuild()
        {
            if (!PlayerPrefs.HasKey(CustomBuildPlayerPrefsKey)) return;
            partsConfig = JsonUtility.FromJson<RocketPartsConfig>(PlayerPrefs.GetString(CustomBuildPlayerPrefsKey));
            partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
            partsConfig.ClampPhysicalRanges();
            SyncManagerConfigs();
            Dirty();
            RebuildUI();
            ShowNotification("Custom vehicle build loaded.", false);
        }

        /// <summary>
        /// Refreshes derived body, fuel, area, and scenario-fuel labels after
        /// geometry or scenario changes.
        /// </summary>
        void RefreshBodyReadouts(VisualElement root)
        {
            float h = partsConfig.bodyHeight;
            float r = partsConfig.bodyRadius;
            float baseR = RocketPartsConfig.ReferenceBodyRadiusM;
            float baseH = RocketPartsConfig.ReferenceBodyHeightM;

            float surfRatio = (2f * Mathf.PI * r * (h + r)) / (2f * Mathf.PI * baseR * (baseH + baseR));
            float volRatio  = (Mathf.PI * r * r * h)        / (Mathf.PI * baseR * baseR * baseH);

            UpdateLabelText(root, "label-dry-mass", $"{AdjustedDryMass(partsConfig.baseDryMass * surfRatio):F0} kg");
            UpdateLabelText(root, "label-fuel-mass", $"{partsConfig.baseFuelMass * volRatio:F0} kg");
            string fuelSource = partsConfig.useScenarioRecommendedFuel ? ScenarioLabel(CurrentScenario()) : "Custom start fuel";
            UpdateLabelText(root, "label-scenario-fuel", $"{fuelSource} Â· {partsConfig.startFuelFraction * 100f:F0}%");
            UpdateLabelText(root, "label-fuel-remaining", $"{partsConfig.startFuelMass:F0} kg");
            UpdateLabelText(root, "label-axial-area", $"{Mathf.PI * r * r:F2} mÂ²");
            UpdateLabelText(root, "label-projected-side-area", $"{2f * r * h:F2} mÂ²");
        }

        /// <summary>
        /// Refreshes total thrust and maximum fuel burn-rate labels from engine settings.
        /// </summary>
        void RefreshEngineReadouts(VisualElement root)
        {
            float engineCount = partsConfig.GetActiveEngineCount();
            float totalThrust = partsConfig.maxThrustPerEngine * engineCount;
            float burnRate = totalThrust / (partsConfig.specificImpulse * 9.80665f);
            UpdateLabelText(root, "label-total-thrust", $"{totalThrust / 1000f:F1} kN");
            UpdateLabelText(root, "label-burn-rate", $"{burnRate:F1} kg/s");
        }

        /// <summary>
        /// Refreshes the per-fin aerodynamic area label from the fin dimensions.
        /// </summary>
        void RefreshFinReadouts(VisualElement root)
        {
            float area = partsConfig.finWidthX * partsConfig.finWidthZ;
            UpdateLabelText(root, "label-fin-area", $"{area:F2} mÂ²");
        }

        /// <summary>
        /// Estimates dry mass after hardware selection by adjusting body mass
        /// for selected engines, fins, and RCS dry mass.
        /// </summary>
        float AdjustedDryMass(float bodyDryMass)
        {
            float selectedHardwareMass =
                partsConfig.GetEngineCount() * EngineDryMassKg +
                partsConfig.GetFinCount() * GridFinDryMassKg +
                (partsConfig.rcsEnabled ? partsConfig.rcsDryMass : 0f);

            float defaultHardwareMass =
                ReferenceEngineCount * EngineDryMassKg +
                ReferenceFinCount * GridFinDryMassKg +
                RocketPartsConfig.DefaultRcsDryMassKg;

            return Mathf.Max(bodyDryMass * 0.35f, bodyDryMass + selectedHardwareMass - defaultHardwareMass);
        }

        /// <summary>
        /// Returns the scenario fuel percentage shown in the parts tab.
        /// </summary>
        float CurrentScenarioFuelPercent()
        {
            return ScenarioProfile.StartFuelFraction(CurrentScenario()) * 100f;
        }

        /// <summary>
        /// Returns the text label used beside the scenario fuel percentage.
        /// </summary>
        static string ScenarioLabel(ScenarioType scenario)
        {
            return scenario switch
            {
                ScenarioType.ChopstickLanding => "Chopstick landing reserve",
                ScenarioType.LegLanding => "Leg-landing reserve",
                ScenarioType.Hover => "Hover reserve",
                ScenarioType.HoverTracking => "Hover-track reserve",
                ScenarioType.Takeoff => "Takeoff load",
                ScenarioType.BellyFlop => "Belly-flop reserve",
                _ => scenario.ToString()
            };
        }

        /// <summary>
        /// Builds the hardware preset button list and marks the active preset.
        /// </summary>
        VisualElement BuildPresetSelector()
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("rs-preset-list");

            foreach (var option in HardwarePresetUiCatalog.Presets)
            {
                var row = new Button(() => ApplyHardwarePreset(option.preset));
                row.AddToClassList("rs-preset-btn");
                row.EnableInClassList("rs-preset-btn-active", partsConfig.hardwarePreset == option.preset);

                var title = new Label(option.label);
                title.AddToClassList("rs-preset-title");
                row.Add(title);

                var desc = new Label(option.description);
                desc.AddToClassList("rs-preset-desc");
                row.Add(desc);

                wrapper.Add(row);
            }

            return wrapper;
        }

        /// <summary>
        /// Builds octaweb burn-group controls plus derived engine/channel counts.
        /// </summary>
        VisualElement BuildBurnGroupSelector(VisualElement root)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("rs-burn-group-list");
            wrapper.Add(BuildGroupDetail(HardwarePresetUiCatalog.BurnGroupDescription));

            var row = new VisualElement();
            row.AddToClassList("rs-burn-group-row");
            wrapper.Add(row);

            foreach (var group in HardwarePresetUiCatalog.BurnGroups)
            {
                var captured = group;
                var button = new Button(() =>
                {
                    partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
                    partsConfig.octawebBurnGroup = captured;
                    RebuildUI();
                    Dirty();
                })
                {
                    text = EngineBurnGroups.Label(group)
                };
                button.AddToClassList("rs-burn-group-btn");
                button.EnableInClassList("rs-burn-group-btn-active", partsConfig.octawebBurnGroup == group);
                row.Add(button);
            }

            var activeCount = partsConfig.GetActiveEngineCount();
            wrapper.Add(UIHelper.ReadOnly("Burn Engines", $"{activeCount} / {partsConfig.GetEngineCount()}"));
            return wrapper;
        }

        /// <summary>
        /// Applies a hardware preset, syncs scenario fuel, pushes the change to
        /// spawned rockets, and rebuilds visible controls.
        /// </summary>
        void ApplyHardwarePreset(RocketHardwarePreset preset)
        {
            partsConfig.ApplyPreset(preset);
            ApplyCurrentScenarioHardwareDefaults();
            Dirty();
            RebuildUI();
        }

        /// <summary>
        /// Builds selectable engine-layout cards and updates active styling after selection.
        /// </summary>
        VisualElement BuildEngineSelector(VisualElement root)
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.AddToClassList("rs-engine-row");
            wrapper.Add(row);

            var configs = new (EngineLayout layout, string label, Texture2D tex)[]
            {
                (EngineLayout.Single,  "Single",  engineImg1),
                (EngineLayout.Triple,  "Triple",  engineImg3),
                (EngineLayout.Octaweb, "Octaweb", engineImg9),
            };

            foreach (var cfg in configs)
            {
                var card = new VisualElement();
                card.AddToClassList("rs-engine-card");
                card.userData = cfg.layout;

                if (cfg.tex != null)
                {
                    var img = new Image { image = cfg.tex };
                    img.AddToClassList("rs-engine-img");
                    card.Add(img);
                }

                var lbl = new Label(cfg.label);
                lbl.AddToClassList("rs-engine-label");
                card.Add(lbl);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
                    partsConfig.engineLayout = cfg.layout;
                    RebuildUI();
                    Dirty();
                });

                row.Add(card);
            }

            foreach (var child in row.Children())
                child.EnableInClassList("rs-engine-card-active",
                    (EngineLayout)child.userData == partsConfig.engineLayout);

            return wrapper;
        }

        /// <summary>
        /// Builds selectable fin-layout cards and updates active styling after selection.
        /// </summary>
        VisualElement BuildFinSelector(VisualElement root)
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.AddToClassList("rs-fin-row");
            wrapper.Add(row);

            var configs = new (FinLayout layout, string label, Texture2D tex)[]
            {
                (FinLayout.ThreeFins_120, "3x",     finImg1),
                (FinLayout.FourFins_Plus, "4x (+)", finImg2),
                (FinLayout.FourFins_X,    "4x asymmetric", finImg3),
            };

            foreach (var cfg in configs)
            {
                var card = new VisualElement();
                card.AddToClassList("rs-fin-card");
                card.userData = cfg.layout;

                if (cfg.tex != null)
                {
                    var img = new Image { image = cfg.tex };
                    img.AddToClassList("rs-fin-img");
                    card.Add(img);
                }

                var lbl = new Label(cfg.label);
                lbl.AddToClassList("rs-fin-label");
                card.Add(lbl);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
                    partsConfig.finLayout = cfg.layout;
                    foreach (var child in row.Children())
                        child.EnableInClassList("rs-fin-card-active",
                            (FinLayout)child.userData == cfg.layout);
                    Dirty();
                    RefreshFinReadouts(root);
                });

                row.Add(card);
            }

            foreach (var child in row.Children())
                child.EnableInClassList("rs-fin-card-active",
                    (FinLayout)child.userData == partsConfig.finLayout);

            return wrapper;
        }
    }
}
