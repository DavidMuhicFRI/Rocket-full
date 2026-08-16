// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.HardwareTests.cs
// Purpose: Builds UI controls for launching and monitoring hardware diagnostic tests.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        private Button _stopHardwareTestButton;
        private VisualElement _hardwareTestOptions;
        private Label _hardwareTestStatusLabel;
        private bool _hardwareTestExpanded;

        /// <summary>
        /// Builds the floating hardware-test menu, launch buttons, stop button,
        /// and live status label.
        /// </summary>
        VisualElement BuildHardwareTestDock()
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("rs-test-wrapper");

            var hardwareTestButton = UIHelper.ActionButton("Hardware Tests", ToggleHardwareTestOptions);
            hardwareTestButton.AddToClassList("rs-test-main-btn");
            wrapper.Add(hardwareTestButton);

            _hardwareTestOptions = new VisualElement();
            _hardwareTestOptions.AddToClassList("rs-test-options");
            _hardwareTestOptions.style.display = _hardwareTestExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            var propulsion = UIHelper.Foldout("Propulsion and RCS", true);
            AddHardwareTestButton(propulsion, RocketHardwareTestType.Thrusters, "Engine static test");
            AddHardwareTestButton(propulsion, RocketHardwareTestType.RcsVacuum, "RCS vacuum test");
            AddHardwareTestButton(propulsion, RocketHardwareTestType.RcsSeparation, "Stage-separation RCS");
            _hardwareTestOptions.Add(propulsion);

            var aero = UIHelper.Foldout("Aerodynamics and Fins");
            AddHardwareTestButton(aero, RocketHardwareTestType.Aerodynamics, "Aerodynamics test");
            AddHardwareTestButton(aero, RocketHardwareTestType.FinAxisX, "Fin X rotation");
            AddHardwareTestButton(aero, RocketHardwareTestType.FinAxisY, "Fin Y rotation");
            AddHardwareTestButton(aero, RocketHardwareTestType.FinAxisZ, "Fin Z rotation");
            AddHardwareTestButton(aero, RocketHardwareTestType.TopStabilizers, "Top stabilizers");
            _hardwareTestOptions.Add(aero);

            var flight = UIHelper.Foldout("Flight Smoke Tests");
            AddHardwareTestButton(flight, RocketHardwareTestType.Landing, "Landing test");
            _hardwareTestOptions.Add(flight);

            _stopHardwareTestButton = UIHelper.DangerButton("Stop test", () =>
            {
                areaHost?.StopHardwareTest();
                RefreshHardwareTestStatus();
            });
            _hardwareTestOptions.Add(_stopHardwareTestButton);

            _hardwareTestStatusLabel = new Label("No hardware test running.");
            _hardwareTestStatusLabel.AddToClassList("rs-test-status");
            _hardwareTestOptions.Add(_hardwareTestStatusLabel);

            wrapper.Add(_hardwareTestOptions);
            RefreshHardwareTestStatus();
            return wrapper;
        }

        /// <summary>Adds a consistently styled button that launches one scripted test type.</summary>
        void AddHardwareTestButton(VisualElement root, RocketHardwareTestType type, string label)
        {
            root.Add(UIHelper.ActionButton(label, () => StartHardwareTest(type)));
        }
        /// <summary>
        /// Opens or closes the floating hardware-test option list.
        /// </summary>
        void ToggleHardwareTestOptions()
        {
            _hardwareTestExpanded = !_hardwareTestExpanded;
            if (_hardwareTestOptions != null)
                _hardwareTestOptions.style.display = _hardwareTestExpanded ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <summary>
        /// Applies current UI config changes and asks the manager to run the
        /// selected scripted hardware diagnostic.
        /// </summary>
        void StartHardwareTest(RocketHardwareTestType testType)
        {
            if (_runActive) return;
            Dirty();
            if (areaHost == null)
            {
                Debug.LogError("[Panel] SimulationAreaHost not assigned.");
                return;
            }

            areaHost.RunHardwareTest(testType);
            RefreshHardwareTestStatus();
        }
        /// <summary>
        /// Copies the manager's latest hardware-test status into the panel label.
        /// </summary>
        void RefreshHardwareTestStatus()
        {
            bool isRunning = areaHost != null &&
                             areaHost.HardwareTests != null &&
                             areaHost.HardwareTests.IsRunning;

            if (_stopHardwareTestButton != null)
                _stopHardwareTestButton.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;

            if (_hardwareTestStatusLabel == null) return;
            _hardwareTestStatusLabel.text = areaHost ? areaHost.HardwareTestStatus : "No simulation area host assigned.";
        }
    }
}
