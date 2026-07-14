// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/RocketHardwareTypes.cs
// Purpose: Defines selectable hardware layouts and maps an octaweb burn group
// to installed engines and agent control channels.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    public enum EngineLayout
    {
        Single,
        Triple,
        Octaweb
    }

    public enum OctawebBurnGroup
    {
        CenterOnly,
        CenterPlusTwo,
        CenterPlusFour,
        OuterRing,
        AllNine
    }

    public enum RocketHardwarePreset
    {
        Falcon9,
        SimpleSingle,
        Octaweb,
        Custom
    }

    public enum FinLayout
    {
        ThreeFins_120,
        FourFins_Plus,
        FourFins_X
    }

    public static class EngineBurnGroups
    {
        /// <summary>
        /// Returns how many installed engines belong to the selected burn group
        /// for the current engine layout.
        /// </summary>
        public static int ActiveEngineCount(EngineLayout layout, OctawebBurnGroup burnGroup)
        {
            return layout switch
            {
                EngineLayout.Single => 1,
                EngineLayout.Triple => 3,
                EngineLayout.Octaweb => burnGroup switch
                {
                    OctawebBurnGroup.CenterOnly => 1,
                    OctawebBurnGroup.CenterPlusTwo => 3,
                    OctawebBurnGroup.CenterPlusFour => 5,
                    OctawebBurnGroup.OuterRing => 8,
                    OctawebBurnGroup.AllNine => 9,
                    _ => 1
                },
                _ => 1
            };
        }

        /// <summary>
        /// Returns the number of throttle/gimbal channels to expose, collapsing
        /// engines to one shared channel when independent control is disabled.
        /// </summary>
        public static int ControlChannelCount(EngineLayout layout, bool independentEngines, OctawebBurnGroup burnGroup)
        {
            return independentEngines ? ActiveEngineCount(layout, burnGroup) : 1;
        }

        /// <summary>
        /// Returns whether a specific installed engine index participates in
        /// the selected layout and octaweb burn group.
        /// </summary>
        public static bool IncludesEngine(EngineLayout layout, OctawebBurnGroup burnGroup, int installedEngineIndex)
        {
            if (installedEngineIndex < 0) return false;

            return layout switch
            {
                EngineLayout.Single => installedEngineIndex == 0,
                EngineLayout.Triple => installedEngineIndex < 3,
                EngineLayout.Octaweb => burnGroup switch
                {
                    OctawebBurnGroup.CenterOnly => installedEngineIndex == 0,
                    OctawebBurnGroup.CenterPlusTwo => installedEngineIndex == 0 || installedEngineIndex == 1 || installedEngineIndex == 5,
                    OctawebBurnGroup.CenterPlusFour => installedEngineIndex == 0 ||
                                                       installedEngineIndex == 1 ||
                                                       installedEngineIndex == 3 ||
                                                       installedEngineIndex == 5 ||
                                                       installedEngineIndex == 7,
                    OctawebBurnGroup.OuterRing => installedEngineIndex >= 1 && installedEngineIndex <= 8,
                    OctawebBurnGroup.AllNine => installedEngineIndex <= 8,
                    _ => installedEngineIndex == 0
                },
                _ => installedEngineIndex == 0
            };
        }

        /// <summary>
        /// Returns the UI label for an octaweb burn group.
        /// </summary>
        public static string Label(OctawebBurnGroup burnGroup)
        {
            return burnGroup switch
            {
                OctawebBurnGroup.CenterOnly => "Center only",
                OctawebBurnGroup.CenterPlusTwo => "Center + 2",
                OctawebBurnGroup.CenterPlusFour => "Center + 4",
                OctawebBurnGroup.OuterRing => "Outer ring",
                OctawebBurnGroup.AllNine => "All 9",
                _ => burnGroup.ToString()
            };
        }
    }
}
