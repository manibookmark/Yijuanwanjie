#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Editor-only representative entry authoring/admission. Owns the explicit
    /// recipe, prior persistent grants and deterministic current-global-pool
    /// choices, never account writes, map history, UI or the active Run pointer.
    /// </summary>
    public static class EditorQuickPlayRepresentativeEntries
    {
        public const string ConfigurationFile = "tools/simulation/quickplay-representative-entries.json";

        [Serializable]
        public sealed class Definition
        {
            public string contentId, floorContentId, runEntryContentId;
            public int seed, level, cardCount, jade, luck;
            public int baseManaBonus, baseArmorBonus, handBonus, rerolls;
            public string[] removedStartingCardContentIds;
            public DungeonRunQuickPlayRepresentativeState ToState() => new DungeonRunQuickPlayRepresentativeState
            {
                contentId = contentId, seed = seed, level = level, cardCount = cardCount,
                jade = jade, luck = luck, baseManaBonus = baseManaBonus,
                baseArmorBonus = baseArmorBonus, handBonus = handBonus, rerolls = rerolls
            };
        }

        [Serializable] private sealed class BlockedDefinition { public string floorContentId, reason; }
        [Serializable] private sealed class Document
        {
            public int schemaVersion;
            public Definition[] entries;
            public BlockedDefinition[] blockedEntries;
        }

        /// <summary>Read/validate without touching saves or a global Run. Used by authoring preflight.</summary>
        public static bool TryResolve(string json, string floorContentId, out Definition definition, out string error)
        {
            definition = null;
            error = null;
            try
            {
                var document = JsonUtility.FromJson<Document>(json);
                if (document?.schemaVersion != 1 || document.entries == null || document.blockedEntries == null)
                    throw new InvalidOperationException("F5代表入口配置版本或列表缺失。");
                var floorIds = new HashSet<string>(StringComparer.Ordinal);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (Definition item in document.entries)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.floorContentId) ||
                        string.IsNullOrWhiteSpace(item.runEntryContentId) || !floorIds.Add(item.floorContentId) ||
                        !ids.Add(item.contentId) || item.ToState().Validate() != null)
                        throw new InvalidOperationException("F5代表入口含缺项、负值或重复的稳定ID。");
                    if (item.floorContentId == floorContentId) definition = item;
                }
                foreach (BlockedDefinition item in document.blockedEntries)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.floorContentId) ||
                        string.IsNullOrWhiteSpace(item.reason) || !floorIds.Add(item.floorContentId))
                        throw new InvalidOperationException("F5未开放入口缺少原因或与已登记入口重复。");
                    if (item.floorContentId == floorContentId) error = item.reason;
                }
                if (definition != null) return true;
                error = error ?? "该层尚未登记人工验收代表入口：" + floorContentId;
                return false;
            }
            catch (Exception exception)
            {
                definition = null;
                error = "读取F5代表入口失败：" + exception.Message;
                return false;
            }
        }

        public static bool TryPrepare(LevelRunProfile target, string snapshotId, MetaProgression account,
            string crawlerContentId, out DungeonRunSession prepared, out string source, out string error)
        {
            prepared = null;
            source = null;
            error = null;
            List<CardConfig> pool = null;
            List<CardConfig> starting = null;
            try
            {
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (target == null || !TryResolve(File.ReadAllText(Path.Combine(project, ConfigurationFile)),
                        target.contentId, out Definition definition, out error)) return false;
                RunEntryLoadoutProfile entry = RunEntryLoadoutProfileCatalog.Get(definition.runEntryContentId);
                LevelRunProfile first = LevelRunProfileCatalog.Get(entry.firstFloorContentId);
                if (target.floorIndex <= 1 || first.dungeonContentId != target.dungeonContentId)
                    throw new InvalidOperationException("代表入口与正式首层关区不一致。");
                pool = ContentRuntimeFactory.CreateCardPool(entry.volumeId);
                if (!new RunStartService(entry, first, pool, DaozangProgressionRules.ApplyToPlayerStats)
                        .TryPrepare(account, crawlerContentId, out DungeonRunSession baseline, out error,
                            runId: "quickplay.baseline." + definition.contentId,
                            instanceIds: new SeededInstanceIdGenerator(1), runSeed: definition.seed)) return false;
                if (!baseline.TryGetCurrentCards(pool, out starting, out error)) return false;
                var seedIds = new List<string>();
                foreach (CardConfig card in starting) seedIds.Add(card.contentId);
                foreach (string removed in definition.removedStartingCardContentIds ?? Array.Empty<string>())
                {
                    if (!seedIds.Remove(removed))
                        throw new InvalidOperationException("代表归元支付牌不在起始牌包：" + removed);
                }
                var deck = StableCardPoolResolver.ResolveRequired(seedIds, pool,
                    CardTestStatus.CanUseInFormalPlay, "F5代表起始牌组");
                AppendPriorPersistentGrants(first, target, pool, deck);
                FillRepresentativeRewards(definition, entry.volumeId, account, baseline, pool, deck);
                if (!DungeonRunQuickPlayRepresentativeEntry.TryPrepare(baseline, target, snapshotId,
                        deck, pool, definition.ToState(), LevelRunProfileCatalog.Get, out prepared, out error)) return false;
                source = "representative-entry:" + definition.contentId;
                return true;
            }
            catch (Exception exception)
            {
                prepared = null;
                error = "F5代表入口准备失败：" + exception.Message;
                return false;
            }
            finally
            {
                Release(starting);
                Release(pool);
            }
        }

        private static void AppendPriorPersistentGrants(LevelRunProfile first, LevelRunProfile target,
            IReadOnlyList<CardConfig> pool, List<CardConfig> deck)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            LevelRunProfile cursor = first;
            while (cursor != null && cursor.contentId != target.contentId && seen.Add(cursor.contentId))
            {
                if (cursor.dungeonContentId != target.dungeonContentId || cursor.floorIndex >= target.floorIndex)
                    throw new InvalidOperationException("代表入口无法证明同关前层配置链。");
                foreach (LevelNode node in cursor.nodes)
                    foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                    {
                        if (effect.kind != LevelInteractionEffectKind.GrantCard) continue;
                        List<CardConfig> grant = StableCardPoolResolver.ResolveRequired(new[] { effect.targetContentId },
                            pool, CardTestStatus.CanUseInFormalPlay, "前层固定奖励");
                        CardConfig card = grant[0];
                        if (card.destroyOnUse || card.IsEncounterTemporary) continue;
                        for (int i = 0; i < effect.amount; i++) deck.Add(card);
                    }
                cursor = string.IsNullOrWhiteSpace(cursor.nextFloorContentId)
                    ? null : LevelRunProfileCatalog.Get(cursor.nextFloorContentId);
            }
            if (cursor?.contentId != target.contentId)
                throw new InvalidOperationException("代表入口缺少通往目标层的作者配置链。");
        }

        private static void FillRepresentativeRewards(Definition definition, string volume,
            MetaProgression account, DungeonRunSession baseline, IReadOnlyList<CardConfig> pool, List<CardConfig> deck)
        {
            if (deck.Count > definition.cardCount)
                throw new InvalidOperationException("当前起始牌及固定奖励超过代表规模，请更新测试入口配置。");
            var resolution = NaturalLevelUpRewardCatalog.ResolveGlobal(pool, volume, account,
                CardTestStatus.CanUseInFormalPlay, runContext: new CardUnlockRunContext(baseline.RunId,
                    () => baseline.RunProgression.CardUnlockFacts.Capture(), baseline.LeaderCrawlerContentId));
            if (resolution.NaturalPool == null)
                throw new InvalidOperationException("F5代表升级选择需要现行全局自然奖励规则。");
            int remaining = definition.cardCount - deck.Count;
            var random = new SeededRandomSource(definition.seed);
            for (int i = 0; i < remaining; i++)
            {
                int reachedLevel = 2 + i * Math.Max(0, definition.level - 2) / Math.Max(1, remaining - 1);
                List<LevelUpSystem.Reward> choices = resolution.NaturalPool.Roll(resolution.Config,
                    definition.luck, random, 3, _ => false, new LevelUpRewardRequest(reachedLevel));
                CardConfig selected = null;
                int best = int.MinValue;
                foreach (LevelUpSystem.Reward choice in choices)
                {
                    CardConfig card = choice.card;
                    if (card == null || !CardTestStatus.CanUseInFormalPlay(card.contentId))
                        throw new InvalidOperationException("全局代表候选没有可发布卡牌。");
                    int copies = deck.FindAll(existing => existing.contentId == card.contentId).Count;
                    int score = (card.destroyOnUse ? 0 : 100) + (copies == 0 ? 40 : -25 * copies) +
                                (card.baseDamage > 0 ? 12 : 0) + (card.armor > 0 ? 10 : 0) +
                                (card.manaCost <= 2 ? 5 : 0);
                    if (selected == null || score > best) { selected = card; best = score; }
                }
                if (selected == null) throw new InvalidOperationException("全局代表奖励没有合法选择。");
                deck.Add(selected);
            }
        }

        private static void Release(IEnumerable<CardConfig> cards)
        {
            foreach (CardConfig card in cards ?? Array.Empty<CardConfig>())
                if (card != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(card);
                    else UnityEngine.Object.DestroyImmediate(card);
                }
        }
    }
}
#endif
