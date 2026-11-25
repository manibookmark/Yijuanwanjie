using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 从 Resources/Content/Battle/ 按 scenarioId 建立缓存。
    /// 重复 scenarioId 必须报错。
    /// </summary>
    public static class BattleScenarioCatalog
    {
        private const string ResourceDir = "Content/Battle";
        private static readonly Dictionary<string, BattleScenarioProfile> _cache
            = new Dictionary<string, BattleScenarioProfile>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>按 scenarioId 获取战斗配置。首次访问时从 Resources 加载。</summary>
        public static BattleScenarioProfile Get(string scenarioId)
        {
            if (string.IsNullOrWhiteSpace(scenarioId))
                throw new ArgumentException(
                    "scenarioId 不能为空。", nameof(scenarioId));

            if (_cache.TryGetValue(scenarioId, out BattleScenarioProfile cached))
                return cached;

            BattleScenarioProfile[] all = Resources.LoadAll<BattleScenarioProfile>(
                ResourceDir);
            _cache.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                BattleScenarioProfile profile = all[i];
                if (string.IsNullOrWhiteSpace(profile.scenarioId))
                    continue;
                if (_cache.ContainsKey(profile.scenarioId))
                    throw new InvalidOperationException(
                        $"BattleScenarioProfile scenarioId 重复：" +
                        $"{profile.scenarioId}。");
                _cache[profile.scenarioId] = profile;
            }

            if (!_cache.TryGetValue(scenarioId, out cached))
                return null;

            return cached;
        }

        /// <summary>清空缓存（测试用）。</summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
