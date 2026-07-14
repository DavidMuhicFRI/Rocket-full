// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Faults.cs
// Purpose: Builds the Faults tab for stochastic training faults and repeatable fixed evaluation faults.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine.UIElements;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        /// <summary>
        /// Builds the intentionally small first fault UI. Training faults are
        /// stochastic; inference faults are deterministic for repeatable comparisons.
        /// </summary>
        void BuildFaultsTab(VisualElement root)
        {
            root.Clear();
            var faults = envConfig.faults ??= new RocketFaultConfig();

            root.Add(UIHelper.SectionLabel("Training Faults"));
            root.Add(UIHelper.Toggle("Allow During Training", faults.allowDuringTraining, value =>
            {
                faults.allowDuringTraining = value;
                BuildFaultsTab(root);
            }));

            if (faults.allowDuringTraining)
            {
                root.Add(UIHelper.Slider("Faulty Episodes", faults.faultyEpisodeProbability, 0f, 1f,
                    value => { faults.faultyEpisodeProbability = value; faults.Clamp(); },
                    "Sets the chance that a training episode contains at least one fault.", 0, "%", valueScale: 100f));
                root.Add(UIHelper.Slider("Minimum Severity", faults.trainingSeverityMin, 0f, 1f,
                    value => { faults.trainingSeverityMin = value; faults.Clamp(); },
                    "Sets the weakest fault that may be sampled during training.", 0, "%", valueScale: 100f));
                root.Add(UIHelper.Slider("Maximum Severity", faults.trainingSeverityMax, 0f, 1f,
                    value => { faults.trainingSeverityMax = value; faults.Clamp(); },
                    "Sets the strongest fault that may be sampled during training.", 0, "%", valueScale: 100f));
                root.Add(UIHelper.IntSlider("Maximum At Once", faults.maxSimultaneousFaults, 1, 3,
                    value => faults.maxSimultaneousFaults = value,
                    "Limits how many different faults can be active in one episode."));

                var allowed = UIHelper.Foldout("Allowed Fault Types", true);
                allowed.Add(UIHelper.Toggle("Engine Thrust Loss", faults.allowEngineThrustLoss, v => faults.allowEngineThrustLoss = v));
                allowed.Add(UIHelper.Toggle("Gimbal Jam", faults.allowGimbalJam, v => faults.allowGimbalJam = v));
                allowed.Add(UIHelper.Toggle("Fin Effectiveness Loss", faults.allowFinEffectivenessLoss, v => faults.allowFinEffectivenessLoss = v));
                allowed.Add(UIHelper.Toggle("RCS Stuck Closed", faults.allowRcsStuckClosed, v => faults.allowRcsStuckClosed = v));
                allowed.Add(UIHelper.Toggle("RCS Stuck Open", faults.allowRcsStuckOpen, v => faults.allowRcsStuckOpen = v));
                root.Add(allowed);
            }

            root.Add(UIHelper.SectionLabel("Evaluation Fault"));
            root.Add(UIHelper.Toggle("Enable Fixed Fault", faults.evaluationFaultEnabled, value =>
            {
                faults.evaluationFaultEnabled = value;
                BuildFaultsTab(root);
            }));

            if (!faults.evaluationFaultEnabled) return;
            var type = new EnumField("Fault Type", faults.evaluationFaultType);
            type.AddToClassList("rs-enum-field");
            type.RegisterValueChangedCallback(e => faults.evaluationFaultType = (RocketFaultType)e.newValue);
            root.Add(type);
            root.Add(UIHelper.IntSlider("Target Index", faults.evaluationTargetIndex, 0, 8,
                value => faults.evaluationTargetIndex = value,
                "Selects the affected engine, fin, or RCS unit by its zero-based index."));
            root.Add(UIHelper.Slider("Starts After", faults.evaluationOnsetSeconds, 0f, 60f,
                value => faults.evaluationOnsetSeconds = value,
                "Sets how many seconds after episode start the fixed fault begins.", 1, " s"));
            root.Add(UIHelper.Slider("Duration (0 = rest)", faults.evaluationDurationSeconds, 0f, 60f,
                value => faults.evaluationDurationSeconds = value,
                "Sets how long the fault lasts; zero keeps it active until the episode ends.", 1, " s"));
            root.Add(UIHelper.Slider("Severity", faults.evaluationSeverity, 0f, 1f,
                value => faults.evaluationSeverity = value,
                "Sets the strength of the fixed evaluation fault.", 0, "%", valueScale: 100f));
            root.Add(BuildGroupDetail("Evaluation settings repeat exactly, which makes model comparisons fair."));
        }
    }
}
