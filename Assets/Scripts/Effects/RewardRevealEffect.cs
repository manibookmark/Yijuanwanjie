using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Reward-grade celebration facade. Every reward now uses one deterministic
    /// window-exterior Infinity bloom; the five legacy grade prefabs and their
    /// pale full-screen droplet fallback are intentionally unreachable here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardRevealEffect : MonoBehaviour
    {
        private static bool _warnedUnavailableBloom;

        private RewardWindowBloomEffect _windowBloom;
        private RewardRarity _resolvedRarity = RewardRarity.Common;
        private bool _initialized;

        public RewardRarity ResolvedRarity => _resolvedRarity;
        public bool UsesWindowBloom => _windowBloom != null;
        public RewardWindowBloomEffect ActiveWindowBloom => _windowBloom;

        public void Initialize(Color accent, RewardRarity rarity)
        {
            if (_initialized)
            {
                Debug.LogWarning(
                    "[RewardRevealVfx] RewardRevealEffect 不能重复初始化。",
                    this);
                return;
            }

            _initialized = true;
            _ = accent;
            _resolvedRarity = NormalizeRarity(rarity);
            _windowBloom = gameObject.AddComponent<RewardWindowBloomEffect>();
            if (_windowBloom.Initialize(_resolvedRarity))
                return;

            Destroy(_windowBloom);
            _windowBloom = null;
            if (_warnedUnavailableBloom)
                return;
            _warnedUnavailableBloom = true;
            Debug.LogWarning(
                "[RewardRevealVfx] 窗外灵华 Infinity 通道不可用；" +
                "为避免灰白旧粒子，本次保持无品阶背景。",
                this);
        }

        public void PlayHoverPulse()
        {
            _windowBloom?.PlayHoverPulse();
        }

        private static RewardRarity NormalizeRarity(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Uncommon => rarity,
                RewardRarity.Rare => rarity,
                RewardRarity.VeryRare => rarity,
                RewardRarity.UltraRare => rarity,
                _ => RewardRarity.Common
            };
        }
    }
}
