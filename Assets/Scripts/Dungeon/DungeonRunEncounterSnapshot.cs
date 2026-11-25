using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    public enum DungeonRunEncounterMode { LegacyAuthored = 0, CommonPreset = 1, Generated = 2 }

    /// <summary>
    /// Read-only launch data from the Run's sole formation owner. Generated
    /// actors must be constructed from GeneratedRoster before resolving their
    /// physical plan. Legacy modes retain the existing authored Scenario roster;
    /// they never infer historical generated species from new configuration.
    /// </summary>
    public sealed class DungeonRunEncounterSnapshot
    {
        public string NodeContentId { get; }
        public string ScenarioId { get; }
        public DungeonRunEncounterMode Mode { get; }
        public CommonFormationPreset Preset { get; }
        public CommonEncounterFrozenRoster GeneratedRoster { get; }

        internal DungeonRunEncounterSnapshot(string nodeContentId, string scenarioId,
            DungeonRunEncounterMode mode, CommonFormationPreset preset,
            CommonEncounterFrozenRoster generatedRoster = null)
        {
            if (string.IsNullOrWhiteSpace(nodeContentId) || string.IsNullOrWhiteSpace(scenarioId) ||
                !Enum.IsDefined(typeof(DungeonRunEncounterMode), mode) ||
                (mode == DungeonRunEncounterMode.Generated) != (generatedRoster != null) ||
                (mode == DungeonRunEncounterMode.CommonPreset && !Enum.IsDefined(typeof(CommonFormationPreset), preset)))
                throw new ArgumentException("Invalid frozen encounter snapshot.");
            NodeContentId = nodeContentId; ScenarioId = scenarioId; Mode = mode;
            Preset = preset; GeneratedRoster = generatedRoster;
        }
    }
}
