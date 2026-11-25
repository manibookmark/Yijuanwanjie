using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>Audio owner for native boss tracks. Each actor has one cancellable
    /// voice. Disable/rebind/destroy stop synchronously; late generations cannot
    /// revive it. UI supplies immutable intent, never owns an AudioSource.</summary>
    public sealed class BossMotionAudioPlayer : MonoBehaviour
    {
        private sealed class Voice
        {
            internal AudioSource Source;
            internal float Trim;
        }

        private readonly Dictionary<long, Voice> _voices = new Dictionary<long, Voice>();
        private readonly AudioOwnerGenerationGate _generations = new AudioOwnerGenerationGate();
        private AudioManager _manager;

        public void Bind(AudioManager manager)
        {
            StopAll();
            _generations.Clear();
            _manager = manager;
        }

        public void Play(long owner, long generation, AudioClip clip, bool loop,
            float volume, float pitch, float startOffsetSeconds = 0f)
        {
            if (!isActiveAndEnabled || !_generations.TryAdmit(owner, generation)) return;
            StopVoice(owner);
            if (clip == null || (_manager != null && _manager.IsHeadless)) return;
            // Malformed or expired intentions retire the previous voice and still
            // consume this generation. They must not restart from zero or allow
            // an older request to resurrect playback.
            if (startOffsetSeconds < 0f || float.IsNaN(startOffsetSeconds) ||
                float.IsInfinity(startOffsetSeconds) || clip.samples <= 0 || clip.frequency <= 0)
                return;
            double samplePosition = (double)startOffsetSeconds * clip.frequency;
            if (!loop && samplePosition >= clip.samples) return;
            int startSample = (int)System.Math.Floor(loop
                ? samplePosition % clip.samples
                : samplePosition);
            if (!_voices.TryGetValue(owner, out Voice voice))
            {
                voice = new Voice { Source = gameObject.AddComponent<AudioSource>() };
                _voices.Add(owner, voice);
            }
            AudioSource source = voice.Source;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = loop;
            source.clip = clip;
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            voice.Trim = Mathf.Max(0f, volume);
            source.volume = Gain(voice.Trim);
            source.timeSamples = startSample;
            source.Play();
        }

        public void Stop(long owner, long generation)
        {
            if (_generations.TryAdmit(owner, generation)) StopVoice(owner);
        }

        private float Gain(float trim)
            => _manager != null
                ? AudioMixPolicy.GetSfxGain(SfxType.EnemyAttack, _manager.config,
                    _manager.PlayerSettings.Sfx, trim)
                : AudioMixPolicy.GetSfxGain(SfxType.EnemyAttack, null, trim);

        private void Update()
        {
            // Settings can change while a native segment is playing.
            foreach (Voice voice in _voices.Values)
                if (voice.Source != null && voice.Source.clip != null)
                    voice.Source.volume = Gain(voice.Trim);
        }

        private void StopVoice(long owner)
        {
            if (!_voices.TryGetValue(owner, out Voice voice) || voice.Source == null) return;
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Source.loop = false;
        }

        public void StopAll()
        {
            foreach (long owner in _voices.Keys) StopVoice(owner);
        }

        private void OnDisable() => StopAll();
        private void OnDestroy()
        {
            StopAll();
            _voices.Clear();
            _generations.Clear();
            _manager = null;
        }
    }
}
