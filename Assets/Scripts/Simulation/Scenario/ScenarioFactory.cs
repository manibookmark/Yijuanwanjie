using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 从 BattleScenarioProfile 构建 EncounterScenarioSpec。
    /// 复用 ContentRuntimeFactory，不引入第二套内容管线。
    /// </summary>
    public static class ScenarioFactory
    {
        /// <summary>从 Resources 加载的 BattleScenarioProfile 构建模拟场景。</summary>
        public static EncounterScenarioSpec FromProfile(
            BattleScenarioProfile profile,
            CombatRuleSet rules = null)
            => FromProfile(profile, rules, null);

        /// <summary>
        /// Uses an already frozen source roster; no generation, random access or
        /// Dungeon dependency belongs to this content materialization boundary.
        /// Legacy callers continue to use the authored Scenario and its layout.
        /// </summary>
        public static EncounterScenarioSpec FromProfile(
            BattleScenarioProfile profile, CombatRuleSet rules,
            CommonEncounterFrozenRoster frozenRoster)
            => Compile(profile, rules, frozenRoster, materializeActors: true);

        /// <summary>
        /// Inspects Scenario player, deck and reward inputs for a generated node.
        /// No authored fallback actors are constructed and the empty actor list is
        /// inspection-only; it is not a generated encounter or a playable result.
        /// </summary>
        public static EncounterScenarioSpec FromProfileForSourceInspection(
            BattleScenarioProfile profile, CombatRuleSet rules = null)
            => Compile(profile, rules, null, materializeActors: false);

        private static EncounterScenarioSpec Compile(BattleScenarioProfile profile,
            CombatRuleSet rules, CommonEncounterFrozenRoster frozenRoster,
            bool materializeActors)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            List<string> errors = profile.ValidateProfile();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Simulation scenario {profile.scenarioId} is invalid:" +
                    Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            string volumeId = profile.volumeId;
            IReadOnlyList<CardConfig> cardPool =
                ContentRuntimeFactory.CreateCardPool(volumeId);
            List<EnemyStats> enemies = null;
            try
            {
                Func<string, bool> isEligible = profile.testOnlyUnconfirmed
                    ? new Func<string, bool>(CardTestStatus.CanUseInTestBattle)
                    : CardTestStatus.CanUseInFormalPlay;
                RunReferenceLoadoutProfile reference =
                    profile.ResolveReferenceLoadout();
                PlayerStats player = profile.CreatePlayer();
                List<CardConfig> deck = reference != null
                    ? reference.ResolveDeck(cardPool, isEligible)
                    : StableCardPoolResolver.ResolveRequired(
                        profile.deckContentIds,
                        cardPool,
                        isEligible,
                        "Simulation battle deck ");
                var upgradePool = new List<CardConfig>();
                for (int i = 0; i < cardPool.Count; i++)
                {
                    CardConfig card = cardPool[i];
                    if (card != null && isEligible(card.contentId))
                        upgradePool.Add(card);
                }
    
                enemies = !materializeActors ? new List<EnemyStats>() : frozenRoster == null
                    ? ContentRuntimeFactory.CreateEnemyLineup(volumeId, profile.enemyContentIds,
                        profile.enemyHealthOverride, profile.enemyExperienceOverride,
                        profile.levelBossContentId, profile.levelBossRank)
                    : ContentRuntimeFactory.CreateRequiredEnemyLineup(volumeId, frozenRoster.EnemyContentIds,
                        profile.enemyHealthOverride, profile.enemyExperienceOverride,
                        profile.levelBossContentId, profile.levelBossRank);
    
                return new EncounterScenarioSpec
                {
                    ScenarioId = profile.scenarioId,
                    Player = player,
                    Deck = deck,
                    UpgradePool = upgradePool,
                    OwnedCardPool = cardPool,
                    Enemies = enemies,
                    EnemyWaveSizes = materializeActors && frozenRoster == null ? profile.enemyWaveSizes : null,
                    EnemyFormationRows = materializeActors && frozenRoster == null ? profile.enemyFormationRows : null,
                    EnemyFormationPattern = materializeActors && frozenRoster == null ? profile.enemyFormationPattern : null,
                    EntryExperienceGrant = profile.entryExperienceGrant,
                    Rules = rules ?? new CombatRuleSet(),
                };
            }
            catch
            {
                foreach (CardConfig card in cardPool)
                    if (card != null) UnityEngine.Object.DestroyImmediate(card, allowDestroyingAssets: false);
                // Enemy materialization owns cleanup on failure. No fallible work
                // occurs after it returns and before the complete spec is transferred.
                throw;
            }
        }
    }
}
