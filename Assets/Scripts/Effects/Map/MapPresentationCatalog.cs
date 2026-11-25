using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// 从 Resources/MapPreviewProfiles 按稳定 contentId 建立缓存。
    /// 重复 contentId 必须报错。测试结束可清缓存。
    /// </summary>
    public static class MapPresentationCatalog
    {
        private const string ResourceDir = "MapPreviewProfiles";
        private static readonly Dictionary<string, MapPreviewProfile> _cache
            = new Dictionary<string, MapPreviewProfile>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers an explicitly preloaded profile without scanning unrelated
        /// stages. Re-registering the same asset is safe; identity collisions fail.
        /// </summary>
        public static void Register(MapPreviewProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.contentId))
                throw new ArgumentException("地图预热配置缺少稳定 contentId。");
            if (_cache.TryGetValue(profile.contentId, out MapPreviewProfile existing) &&
                existing != null && existing != profile)
                throw new InvalidOperationException("MapPreviewProfile contentId 重复：" + profile.contentId + "。");
            _cache[profile.contentId] = profile;
        }

        /// <summary>按 contentId 获取展示配置。首次访问时从 Resources 加载。</summary>
        public static MapPreviewProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "contentId 不能为空。", nameof(contentId));

            if (_cache.TryGetValue(contentId, out MapPreviewProfile cached))
                return cached;

            MapPreviewProfile[] all = Resources.LoadAll<MapPreviewProfile>(
                ResourceDir);
            var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Length; i++)
            {
                MapPreviewProfile profile = all[i];
                if (profile == null || string.IsNullOrWhiteSpace(profile.contentId))
                    continue;
                if (!foundIds.Add(profile.contentId))
                    throw new InvalidOperationException(
                        $"MapPreviewProfile contentId 重复：" +
                        $"{profile.contentId}。");
                Register(profile);
            }

            if (!_cache.TryGetValue(contentId, out cached))
                throw new InvalidOperationException(
                    $"找不到 contentId 为 {contentId} 的 MapPreviewProfile。");

            return cached;
        }

        /// <summary>清空缓存（测试用）。</summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
