using System;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Effects;

namespace WanjieScroll.Bootstrap
{
    /// <summary>把 Effects 一次性音频请求转交给 DontDestroyOnLoad 音频池。</summary>
    public sealed class PersistentEffectOneShotAudio : IEffectOneShotAudio
    {
        private readonly AudioManager _audioManager;

        public PersistentEffectOneShotAudio(AudioManager audioManager)
        {
            _audioManager = audioManager ??
                throw new ArgumentNullException(nameof(audioManager));
        }

        public void Play(
            AudioClip clip,
            EffectOneShotKind kind,
            float pitch = 1f,
            float contentTrim = 1f)
        {
            if (kind == EffectOneShotKind.PeriodicAttackHit ||
                kind == EffectOneShotKind.DenseImpactHit)
            {
                _audioManager.PlayDenseImpactHit(clip, pitch, contentTrim);
                return;
            }
            SfxType type = kind switch
            {
                EffectOneShotKind.CardPlay => SfxType.CardPlay,
                EffectOneShotKind.ProjectileHit =>
                    SfxType.ProjectileHit,
                EffectOneShotKind.ExperienceArrival =>
                    SfxType.ExperienceArrival,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null)
            };
            _audioManager.PlayOneShot(
                clip,
                type,
                pitch,
                contentTrim);
        }
    }
}
