using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 将 Core 的钤印结果投影为独立 Run 存档快照；隐藏宿主恢复、容量覆盖与临时
    /// ScriptableObject 生命周期。此模块不持有或写入 Run 状态，三个奖励事务共用它。
    /// </summary>
    internal static class DungeonRunSealAssignmentRules
    {
        public static bool TryPrepare(
            DungeonRunCardSaveData saved, CardConfig source, CardSealSpec seal,
            Func<string, bool> isEvolutionResultEligible,
            out DungeonRunCardSaveData replacement, out int slotIndex, out string error,
            ICardAuthorReferenceResolver references = null)
        {
            replacement = null;
            slotIndex = saved?.seals?.Count ?? 0;
            error = null;
            if (saved == null || source == null)
            {
                error = "钤印目标缺少卡牌实例或作者配置。";
                return false;
            }

            CardConfig candidate = null;
            CardConfig assigned = null;
            try
            {
                references = references ?? DungeonRunCardCodec.CreateResolver(new[] { source });
                candidate = DungeonRunCardCodec.Restore(saved, references);
                assigned = CardSealRules.BuildAssignedCard(candidate, seal,
                    isEvolutionResultEligible: isEvolutionResultEligible);
                if (assigned == null)
                {
                    error = $"卡牌实例 {saved.runtimeInstanceId} 不是该灵印的合法宿主。";
                    return false;
                }

                replacement = DungeonRunCardCodec.Capture(assigned, references);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally
            {
                DestroyClone(assigned);
                DestroyClone(candidate);
            }
        }

        public static CardConfig FindSource(IReadOnlyList<CardConfig> cardPool, string contentId)
        {
            if (cardPool == null) return null;
            for (int i = 0; i < cardPool.Count; i++)
                if (cardPool[i] != null && string.Equals(cardPool[i].contentId, contentId,
                    StringComparison.OrdinalIgnoreCase)) return cardPool[i];
            return null;
        }

        private static void DestroyClone(UnityEngine.Object clone)
        {
            if (clone == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(clone);
            else UnityEngine.Object.DestroyImmediate(clone);
        }
    }
}
