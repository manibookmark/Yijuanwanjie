using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// VfxProfile 静态注册表。
    /// 从 Resources/VfxProfiles/ 加载所有 VfxProfile 资产，按 contentId 索引。
    /// </summary>
    public static class VfxProfileRegistry
    {
        private static Dictionary<string, VfxProfile> _profiles;
        private static bool _initialized;

        /// <summary>Formal scene consumers reuse the current resource snapshot.
        /// Tools that intentionally reload edited assets still call Initialize.</summary>
        public static void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        /// <summary>
        /// 初始化：从 Resources 加载所有 VfxProfile 资产。
        /// 可重复调用，每次重新加载。
        /// </summary>
        public static void Initialize()
        {
            var profiles = new Dictionary<string, VfxProfile>();
            var allProfiles = Resources.LoadAll<VfxProfile>("VfxProfiles");
            foreach (var profile in allProfiles)
            {
                if (profile != null && !string.IsNullOrEmpty(profile.contentId))
                {
                    if (profile.hitParticleCount < 0 ||
                        profile.secondaryParticleCount < 0)
                    {
                        Debug.LogError(
                            $"[VfxProfileRegistry] {profile.name} " +
                            "未显式配置命中粒子数量；-1 不能进入运行时。");
                    }
                    profiles[profile.contentId] = profile;
                }
            }
            _profiles = profiles;
            _initialized = true;
            Debug.Log($"[VfxProfileRegistry] 共加载 {allProfiles.Length} 个 VfxProfile");
        }

        /// <summary>
        /// 按 contentId 查找 VfxProfile。
        /// 找不到时尝试 fallbackContentId 链（最多 3 层防循环）。
        /// </summary>
        public static VfxProfile Get(string contentId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(contentId)) return null;

            if (_profiles.TryGetValue(contentId, out var profile))
                return profile;

            // Fallback 链
            return null;
        }

        /// <summary>
        /// 便捷重载：直接从 CardConfig 查找。
        /// </summary>
        public static VfxProfile Get(WanjieScroll.Core.CardConfig card)
        {
            if (card == null) return null;
            return Get(card.contentId);
        }

        /// <summary>
        /// 清除资源快照；下一次查找会重新加载。普通场景切换不需要调用。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            _profiles = null;
            _initialized = false;
        }
    }
}
