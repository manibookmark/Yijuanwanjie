using System;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Versioned rule inputs for route balance. They describe an entry build,
    /// not a second rules engine; CombatController still owns every behavior.
    /// </summary>
    [Obsolete(
        "Compatibility preset for legacy G1F1 tests. " +
        "Generic balance rules must come from a versioned Suite.")]
    public static class StageSimulationRulePresets
    {
        public const string FirstClearId = "ENTRY_FIRST_CLEAR";
        public const int FirstClearRevision = 1;
        public const string FirstClearHeroContentId =
            "legacy.crawler.common.5a3f3db3";

        /// <summary>
        /// G1F1 first-clear begins after the tutorial permanently unlocks Jiudie
        /// and the Wuxing Disc. The new Run carries neither temporary tutorial
        /// state nor equipped arcana, while Bootstrap enables those two persisted
        /// relic rules for every non-tutorial Run.
        /// </summary>
        public static StageSimulationRuleInput CreateFirstClear()
        {
            return new StageSimulationRuleInput(
                FirstClearId,
                FirstClearRevision,
                FirstClearHeroContentId,
                new CombatRuleSet
                {
                    preservePlayerArmorBetweenTurns = false,
                    shieldBashEnabled = false,
                    comboEnabled = true,
                    wuxingEnabled = true,
                    bossOverkillEnabled = false,
                    normalOverkillEnabled = false,
                    ultraOverkillEnabled = false
                });
        }
    }

    public sealed class StageSimulationRuleInput
    {
        private readonly CombatRuleSet _rules;

        public StageSimulationRuleInput(
            string id,
            int revision,
            string selectedHeroContentId,
            CombatRuleSet rules)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Rule input id cannot be empty.", nameof(id));
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (string.IsNullOrWhiteSpace(selectedHeroContentId))
            {
                throw new ArgumentException(
                    "Selected hero content id cannot be empty.",
                    nameof(selectedHeroContentId));
            }
            Id = id;
            Revision = revision;
            SelectedHeroContentId = selectedHeroContentId;
            _rules = rules?.Clone() ??
                throw new ArgumentNullException(nameof(rules));
        }

        public string Id { get; }
        public int Revision { get; }
        public string SelectedHeroContentId { get; }

        internal CombatRuleSet CreateRules() => _rules.Clone();

        /// <summary>
        /// Formal scenarios may pin a hero. Empty scenario values delegate the
        /// selection to this versioned entry input instead of becoming an error.
        /// </summary>
        public bool MatchesScenarioSelection(string scenarioHeroContentId)
        {
            return string.IsNullOrWhiteSpace(scenarioHeroContentId) ||
                string.Equals(
                    scenarioHeroContentId,
                    SelectedHeroContentId,
                    StringComparison.Ordinal);
        }
    }
}
