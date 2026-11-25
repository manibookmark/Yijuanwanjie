using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Effects 可以请求的语义化一次性音效。具体音量与音频分类由宿主决定，
    /// 避免视觉模块依赖 Audio 的配置与类型系统。
    /// </summary>
    public enum EffectOneShotKind
    {
        CardPlay,
        ProjectileHit,
        ExperienceArrival,
        PeriodicAttackHit,
        /// <summary>Dense ordinary projectile contacts share bounded audio
        /// voices without changing their attack or resident behavior.</summary>
        DenseImpactHit
    }

    /// <summary>Effects 使用的一次性音频窄端口；具体宿主由 Bootstrap 注入。</summary>
    public interface IEffectOneShotAudio
    {
        void Play(
            AudioClip clip,
            EffectOneShotKind kind,
            float pitch = 1f,
            float contentTrim = 1f);
    }
}
