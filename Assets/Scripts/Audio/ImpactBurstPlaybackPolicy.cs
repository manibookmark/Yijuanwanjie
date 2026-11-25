using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// Owns dense impact cue admission and headroom. Availability is supplied
    /// by the dedicated player; this policy never estimates ordinary SFX slots.
    /// </summary>
    public sealed class ImpactBurstPlaybackPolicy
    {
        public const int MaximumVoices = 2;
        public const double MinimumCueIntervalSeconds = 0.06;
        public const float MaximumVoiceGain = 0.5f;

        private readonly Dictionary<int, double> _nextCueTimes =
            new Dictionary<int, double>();
        private readonly List<int> _expiredCues = new List<int>();

        public bool TryAcquire(int cueKey, double nowSeconds,
            bool firstVoiceAvailable, bool secondVoiceAvailable, out int voiceIndex)
        {
            voiceIndex = -1;
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) || nowSeconds < 0 ||
                (!firstVoiceAvailable && !secondVoiceAvailable))
                return false;

            _expiredCues.Clear();
            foreach (KeyValuePair<int, double> cue in _nextCueTimes)
                if (cue.Value <= nowSeconds) _expiredCues.Add(cue.Key);
            foreach (int expiredCue in _expiredCues) _nextCueTimes.Remove(expiredCue);

            if (_nextCueTimes.TryGetValue(cueKey, out double nextTime) && nowSeconds < nextTime)
                return false;
            voiceIndex = firstVoiceAvailable ? 0 : 1;
            _nextCueTimes[cueKey] = nowSeconds + MinimumCueIntervalSeconds;
            return true;
        }

        public void Reset()
        {
            _nextCueTimes.Clear();
            _expiredCues.Clear();
        }

        public static float LimitVoiceGain(float mixedGain)
        {
            if (float.IsNaN(mixedGain) || float.IsInfinity(mixedGain)) return 0f;
            return Math.Max(0f, Math.Min(MaximumVoiceGain, mixedGain));
        }
    }

    /// <summary>
    /// Owns the complete dense impact playback lifetime: two exclusive sources,
    /// cue admission, native playback occupancy and current mix. Ordinary SFX,
    /// music and voice sources are never borrowed or stopped by this player.
    /// </summary>
    internal sealed class ImpactBurstAudioPlayer : IDisposable
    {
        private readonly ImpactBurstPlaybackPolicy _policy = new ImpactBurstPlaybackPolicy();
        private readonly AudioSource[] _sources = new AudioSource[ImpactBurstPlaybackPolicy.MaximumVoices];
        private readonly double[] _reservedUntil = new double[ImpactBurstPlaybackPolicy.MaximumVoices];
        private readonly float[] _contentTrims = new float[ImpactBurstPlaybackPolicy.MaximumVoices];
        private GameObject _root;
        private bool _disposed;

        internal ImpactBurstAudioPlayer(Transform parent)
        {
            _root = new GameObject("ImpactBurstAudio");
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < _sources.Length; i++)
            {
                AudioSource source = _root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = false;
                source.priority = 196;
                source.volume = 0f;
                _sources[i] = source;
            }
        }

        internal bool IsPlaying
        {
            get
            {
                foreach (AudioSource source in _sources)
                    if (source != null && source.isPlaying) return true;
                return false;
            }
        }

        internal bool TryPlay(AudioClip clip, AudioConfig config, float playerSfxVolume,
            float pitch, float contentTrim)
        {
            if (_disposed || _root == null || !_root.activeInHierarchy || AudioListener.pause ||
                clip == null || pitch <= 0f || float.IsNaN(pitch) || float.IsInfinity(pitch) ||
                float.IsNaN(contentTrim) || float.IsInfinity(contentTrim) || clip.length <= 0f)
                return false;

            float gain = GetGain(config, playerSfxVolume, contentTrim);
            if (gain <= 0f) return false;
            // Listener pause freezes the DSP clock, so a paused tail keeps its
            // reservation even if native isPlaying lags on the resume frame.
            double now = AudioSettings.dspTime;
            if (!_policy.TryAcquire(clip.GetInstanceID(), now,
                    IsAvailable(0, now), IsAvailable(1, now), out int voiceIndex))
                return false;

            AudioSource source = _sources[voiceIndex];
            float actualPitch = Mathf.Min(3f, pitch);
            _contentTrims[voiceIndex] = Mathf.Max(0f, contentTrim);
            // Native playback can start after this call. Reserve that source
            // immediately, then also honor isPlaying until its tail actually ends.
            _reservedUntil[voiceIndex] = now + clip.length / actualPitch;
            source.clip = clip;
            source.volume = gain;
            source.pitch = actualPitch;
            source.Play();
            return true;
        }

        private bool IsAvailable(int index, double now)
            => _sources[index] != null && !_sources[index].isPlaying && now >= _reservedUntil[index];

        internal void ApplyMix(AudioConfig config, float playerSfxVolume)
        {
            if (_disposed) return;
            for (int i = 0; i < _sources.Length; i++)
                if (_sources[i] != null && _sources[i].clip != null)
                    _sources[i].volume = GetGain(config, playerSfxVolume, _contentTrims[i]);
        }

        private static float GetGain(AudioConfig config, float playerSfxVolume, float contentTrim)
            => ImpactBurstPlaybackPolicy.LimitVoiceGain(AudioMixPolicy.GetSfxGain(
                SfxType.ProjectileHit, config, playerSfxVolume, contentTrim));

        internal void StopAndClear()
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] != null)
                {
                    _sources[i].Stop();
                    _sources[i].clip = null;
                }
                _reservedUntil[i] = 0;
                _contentTrims[i] = 0f;
            }
            _policy.Reset();
        }

        public void Dispose()
        {
            if (_disposed) return;
            StopAndClear();
            _disposed = true;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
