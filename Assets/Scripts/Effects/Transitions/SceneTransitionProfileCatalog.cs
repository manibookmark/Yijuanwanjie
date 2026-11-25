using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Transitions
{
    /// <summary>
    /// 从 Resources/SceneTransitions 按稳定 contentId 建立缓存。
    /// 重复 contentId 报错。测试结束可清缓存。
    /// </summary>
    public static class SceneTransitionProfileCatalog
    {
        private const string ResourceDir = "SceneTransitions";
        private static readonly Dictionary<string, SceneTransitionProfile> _cache
            = new Dictionary<string, SceneTransitionProfile>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>按 contentId 获取转场配置。</summary>
        public static SceneTransitionProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "contentId 不能为空。", nameof(contentId));

            if (_cache.TryGetValue(contentId, out SceneTransitionProfile cached))
                return cached;

            SceneTransitionProfile[] all = Resources.LoadAll<SceneTransitionProfile>(
                ResourceDir);
            _cache.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                SceneTransitionProfile profile = all[i];
                if (string.IsNullOrWhiteSpace(profile.contentId))
                    continue;
                if (_cache.ContainsKey(profile.contentId))
                    throw new InvalidOperationException(
                        $"SceneTransitionProfile contentId 重复：" +
                        $"{profile.contentId}。");
                _cache[profile.contentId] = profile;
            }

            if (!_cache.TryGetValue(contentId, out cached))
                throw new InvalidOperationException(
                    $"找不到 contentId 为 {contentId} 的 SceneTransitionProfile。");

            return cached;
        }

        /// <summary>清空缓存（测试用）。</summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
