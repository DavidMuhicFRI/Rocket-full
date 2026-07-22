// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/Panels/HardwarePresetUiCatalog.cs
// Purpose: Keeps vehicle-preset labels/descriptions and octaweb burn-group order
// out of the panel builder so UI metadata has one small source of truth.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    internal readonly struct HardwarePresetUiOption
    {
        public readonly RocketHardwarePreset preset;
        public readonly string label;
        public readonly string description;

        /// <summary>
        /// Stores the preset enum plus the label and description shown in the
        /// hardware preset selector.
        /// </summary>
        public HardwarePresetUiOption(RocketHardwarePreset preset, string label, string description)
        {
            this.preset = preset;
            this.label = label;
            this.description = description;
        }
    }

    internal static class HardwarePresetUiCatalog
    {
        public const string BurnGroupDescription =
            "Octaweb burn group controls which installed engines can produce thrust/actions. Engine dry mass still includes all 9 Merlins.";

        public static readonly HardwarePresetUiOption[] Presets =
        {
            new(RocketHardwarePreset.Falcon9, "Falcon 9", "Octaweb, grid fins, RCS, center-engine + 2 landing burn"),
            new(RocketHardwarePreset.SimpleSingle, "Simple", "Single engine only; no fins/RCS, hover-friendly timing"),
            new(RocketHardwarePreset.Octaweb, "Octaweb Configuration", "F9 layout with hover-friendly throttle, gimbal, and timing"),
            new(RocketHardwarePreset.AblationFull, "Ablation - Full", "Nine available engines, four grid fins, and RCS; scientific baseline"),
            new(RocketHardwarePreset.AblationNoFins, "Ablation - No Grid Fins", "Full baseline with only grid-fin hardware removed"),
            new(RocketHardwarePreset.AblationNoRcs, "Ablation - No RCS", "Full baseline with only reaction-control hardware removed"),
            new(RocketHardwarePreset.AblationTripleEngine, "Ablation - Triple Engine", "Full baseline with a three-engine layout"),
            new(RocketHardwarePreset.AblationSingleEngine, "Ablation - Single Engine", "Full baseline with a single center engine"),
        };

        public static readonly OctawebBurnGroup[] BurnGroups =
        {
            OctawebBurnGroup.CenterOnly,
            OctawebBurnGroup.CenterPlusTwo,
            OctawebBurnGroup.CenterPlusFour,
            OctawebBurnGroup.OuterRing,
            OctawebBurnGroup.AllNine,
        };
    }
}
