using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Dungeon
{
    /// <summary>从 Resources/Levels 按稳定 contentId 获取关卡运行配置。</summary>
    public static class LevelRunProfileCatalog
    {
        private const string ResourceDirectory = "Levels";
        private static readonly Dictionary<string, LevelRunProfile> Cache =
            new Dictionary<string, LevelRunProfile>(
                StringComparer.OrdinalIgnoreCase);

        public static LevelRunProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "关卡 contentId 不能为空。", nameof(contentId));

            if (Cache.TryGetValue(contentId, out LevelRunProfile cached))
                return cached;

            LevelRunProfile[] profiles =
                Resources.LoadAll<LevelRunProfile>(ResourceDirectory);
            Cache.Clear();
            for (int i = 0; i < profiles.Length; i++)
            {
                LevelRunProfile profile = profiles[i];
                if (profile == null ||
                    string.IsNullOrWhiteSpace(profile.contentId))
                    continue;
                if (Cache.ContainsKey(profile.contentId))
                {
                    throw new InvalidOperationException(
                        $"LevelRunProfile contentId 重复：{profile.contentId}。");
                }
                Cache.Add(profile.contentId, profile);
            }

            if (!Cache.TryGetValue(contentId, out cached))
            {
                throw new InvalidOperationException(
                    $"找不到关卡运行配置：{contentId}。");
            }
            return cached;
        }

        public static void ClearCache()
        {
            Cache.Clear();
        }
    }
}
