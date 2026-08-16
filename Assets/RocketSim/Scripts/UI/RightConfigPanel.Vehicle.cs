// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Vehicle.cs
// Purpose: Builds the Vehicle tab: body, fuel, engines, fins, RCS, presets, validation, and derived readouts.
// Main flow: edit a field -> mark hardware Custom -> update derived readouts ->
// push the new config into the frozen preview. Active runs block this path.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Builds the hardware editor tab for presets, body geometry, engines,
        /// grid fins, RCS, and derived mass/thrust readouts.
        /// </summary>
        void BuildVehicleTab(VisualElement c)
        {
            c.Clear();

            var presets = UIHelper.Foldout("Vehicle Presets", _vehiclePresetsExpanded);
            presets.RegisterValueChangedCallback(evt =>
                _vehiclePresetsExpanded = evt.newValue);
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
        /// Builds immutable built-in presets, the named user-preset library,
        /// and the lightweight configuration validation action.
        /// </summary>
        void BuildPresetSection(VisualElement root)
        {
            root.Add(BuildPresetSelector());
            BuildUserPresetSection(root);
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

            var axialAreaRow = UIHelper.ReadOnly("Axial Area", "0 m^2");
            axialAreaRow.name = "label-axial-area";
            root.Add(axialAreaRow);

            var lateralAreaRow = UIHelper.ReadOnly("Projected Side Area", "0 m^2");
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
            root.Add(UIHelper.Slider("Gimbal Range (deg)", partsConfig.maxGimbalAngle, 1f, 15f, v =>
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
                root.Add(BuildGroupDetail("Asymmetric layout with alternating 60 deg and 120 deg gaps. Verify these angles against the intended booster reference."));
            root.Add(UIHelper.Slider("Radial Length (m)", partsConfig.finWidthX, 0.2f, 4f, v =>
                EditCustomParts(() => partsConfig.finWidthX = v, () => RefreshFinReadouts(root)),
                "Sets how far each grid fin extends outward from the body."));
            root.Add(UIHelper.Slider("Tangential Width (m)", partsConfig.finWidthZ, 0.2f, 4f, v =>
                EditCustomParts(() => partsConfig.finWidthZ = v, () => RefreshFinReadouts(root)),
                "Sets each grid fin's width around the rocket body."));
            root.Add(UIHelper.Slider("Fin Thickness (m)", partsConfig.finThickness, 0.1f, 1f, v =>
                EditCustomParts(() => partsConfig.finThickness = v),
                "Changes the physical thickness used for the grid-fin shape."));
            root.Add(UIHelper.Slider("Max Angle (deg)", partsConfig.maxFinAngle, 5f, 60f, v =>
                EditCustomParts(() => partsConfig.maxFinAngle = v),
                "Sets the largest grid-fin deflection available for steering."));
            root.Add(UIHelper.Slider("Slew Rate (deg/s)", partsConfig.finSlewRate, 10f, 200f, v =>
                EditCustomParts(() => partsConfig.finSlewRate = v),
                "Sets how quickly grid fins can reach a commanded angle."));
            root.Add(UIHelper.Slider("Aerodynamic Effectiveness", partsConfig.liftScale, 0.1f, 3f, v =>
                EditCustomParts(() => partsConfig.liftScale = v),
                "Multiplies the aerodynamic steering force produced by the fins."));

            var finAreaRow = UIHelper.ReadOnly("Fin area", "0 m^2");
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
            root.Add(UIHelper.ReadOnly("Layout", "2 pods x 4 nozzles"));
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
            MarkVehicleCustom();
            mutate();
            Dirty();
            refresh?.Invoke();
        }

        /// <summary>
        /// Marks the live vehicle as edited while retaining the name draft so
        /// the user can explicitly overwrite the preset it was loaded from.
        /// </summary>
        void MarkVehicleCustom()
        {
            partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
            _selectedUserVehiclePresetName = null;
        }

        /// <summary>
        /// Builds the editable user-preset list plus save/overwrite and delete
        /// controls. Only RocketPartsConfig is persisted by these actions.
        /// </summary>
        void BuildUserPresetSection(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Saved Vehicle Presets"));

            IReadOnlyList<UserVehiclePreset> savedPresets =
                VehiclePresetRepository.LoadAll(out string loadError);
            if (!string.IsNullOrEmpty(loadError))
            {
                var error = BuildGroupDetail(loadError);
                error.AddToClassList("rs-user-preset-error");
                root.Add(error);
            }
            else if (savedPresets.Count == 0)
            {
                root.Add(BuildGroupDetail(
                    "No user presets saved yet. Configure a vehicle, enter a name, and save it below."));
            }
            else
            {
                var list = new VisualElement();
                list.AddToClassList("rs-user-preset-list");
                foreach (UserVehiclePreset saved in savedPresets)
                {
                    string capturedName = saved.name;
                    var button = new Button(() => LoadUserVehiclePreset(capturedName));
                    button.AddToClassList("rs-preset-btn");
                    button.EnableInClassList(
                        "rs-preset-btn-active",
                        string.Equals(
                            _selectedUserVehiclePresetName,
                            capturedName,
                            StringComparison.OrdinalIgnoreCase));

                    var title = new Label(saved.name);
                    title.AddToClassList("rs-preset-title");
                    button.Add(title);

                    var description = new Label(UserVehiclePresetDescription(saved.vehicle));
                    description.AddToClassList("rs-preset-desc");
                    button.Add(description);
                    list.Add(button);
                }
                root.Add(list);
            }

            var editor = new VisualElement();
            editor.AddToClassList("rs-user-preset-editor");

            var nameLabel = new Label("Preset Name");
            nameLabel.AddToClassList("rs-user-preset-name-label");
            editor.Add(nameLabel);

            var nameField = new TextField
            {
                value = _vehiclePresetNameDraft,
                maxLength = VehiclePresetRepository.MaximumNameLength
            };
            nameField.AddToClassList("rs-text-field");
            nameField.AddToClassList("rs-user-preset-name-field");
            UIHelper.TrackTextInputFocus(nameField);
            nameField.RegisterValueChangedCallback(evt =>
                _vehiclePresetNameDraft = evt.newValue);
            editor.Add(nameField);
            root.Add(editor);

            var actions = new VisualElement();
            actions.AddToClassList("rs-user-preset-actions");

            var save = UIHelper.ActionButton("Save / Overwrite", SaveUserVehiclePreset);
            save.AddToClassList("rs-user-preset-action");
            save.AddToClassList("rs-user-preset-action-first");
            actions.Add(save);

            var delete = UIHelper.DangerButton("Delete Selected", DeleteSelectedUserVehiclePreset);
            delete.AddToClassList("rs-user-preset-action");
            delete.AddToClassList("rs-user-preset-action-last");
            delete.SetEnabled(!string.IsNullOrWhiteSpace(_selectedUserVehiclePresetName));
            actions.Add(delete);
            root.Add(actions);
            root.Add(BuildGroupDetail(
                "Saving an existing user name overwrites it. Built-in presets cannot be changed or deleted."));
        }

        /// <summary>
        /// Saves the current vehicle under the entered name, replacing only a
        /// user-owned preset when that name already exists.
        /// </summary>
        void SaveUserVehiclePreset()
        {
            if (!VehiclePresetRepository.TrySave(
                    _vehiclePresetNameDraft,
                    partsConfig,
                    out string normalizedName,
                    out bool overwritten,
                    out string error))
            {
                ShowNotification(error, true);
                return;
            }

            partsConfig.hardwarePreset = RocketHardwarePreset.Custom;
            _selectedUserVehiclePresetName = normalizedName;
            _vehiclePresetNameDraft = normalizedName;
            Dirty();
            BuildVehicleTab(_tabContents[VehicleTab]);
            ShowNotification(
                overwritten
                    ? $"Vehicle preset '{normalizedName}' overwritten."
                    : $"Vehicle preset '{normalizedName}' saved.",
                false);
        }

        /// <summary>Loads a named user vehicle without changing any other config.</summary>
        void LoadUserVehiclePreset(string presetName)
        {
            if (!VehiclePresetRepository.TryLoad(presetName, out RocketPartsConfig loaded, out string error))
            {
                ShowNotification(error, true);
                return;
            }

            partsConfig = loaded;
            _selectedUserVehiclePresetName = presetName;
            _vehiclePresetNameDraft = presetName;
            ApplyCurrentScenarioHardwareDefaults();
            SyncManagerConfigs();
            Dirty();
            RebuildUI();
            ShowNotification($"Vehicle preset '{presetName}' loaded.", false);
        }

        /// <summary>Deletes the selected user-owned preset from the catalog.</summary>
        void DeleteSelectedUserVehiclePreset()
        {
            string presetName = _selectedUserVehiclePresetName;
            if (string.IsNullOrWhiteSpace(presetName)) return;
            if (!VehiclePresetRepository.TryDelete(presetName, out string error))
            {
                ShowNotification(error, true);
                return;
            }

            _selectedUserVehiclePresetName = null;
            _vehiclePresetNameDraft = string.Empty;
            BuildVehicleTab(_tabContents[VehicleTab]);
            ShowNotification($"Vehicle preset '{presetName}' deleted.", false);
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
            UpdateLabelText(root, "label-scenario-fuel", $"{fuelSource} - {partsConfig.startFuelFraction * 100f:F0}%");
            UpdateLabelText(root, "label-fuel-remaining", $"{partsConfig.startFuelMass:F0} kg");
            UpdateLabelText(root, "label-axial-area", $"{Mathf.PI * r * r:F2} m^2");
            UpdateLabelText(root, "label-projected-side-area", $"{2f * r * h:F2} m^2");
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
            UpdateLabelText(root, "label-fin-area", $"{area:F2} m^2");
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
                    MarkVehicleCustom();
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
            _selectedUserVehiclePresetName = null;
            _vehiclePresetNameDraft = string.Empty;
            partsConfig.ApplyPreset(preset);
            ApplyCurrentScenarioHardwareDefaults();
            Dirty();
            RebuildUI();
        }

        /// <summary>Builds the compact hardware summary shown under a user preset.</summary>
        static string UserVehiclePresetDescription(RocketPartsConfig vehicle)
        {
            if (vehicle == null) return "Invalid vehicle configuration";

            string engines = vehicle.engineLayout switch
            {
                EngineLayout.Single => "single engine",
                EngineLayout.Triple => "three engines",
                EngineLayout.Octaweb => $"octaweb, {vehicle.GetActiveEngineCount()} active",
                _ => "custom engine layout"
            };
            string fins = vehicle.finsEnabled ? $"{vehicle.GetFinCount()} fins" : "no fins";
            string rcs = vehicle.rcsEnabled ? "RCS" : "no RCS";
            return $"{engines}, {fins}, {rcs}";
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
                    MarkVehicleCustom();
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
                    MarkVehicleCustom();
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
