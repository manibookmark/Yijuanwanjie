using System.Collections;
using Unity.Profiling;
using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// Owns the persistent BOSS horn/music preparation state. Its caller supplies
    /// already configured clips when available; the module alone knows the
    /// fallback resource path, background request, decode timeout and readiness.
    /// Playback and mixer policy remain inside AudioManager.
    /// </summary>
    internal sealed class BossPresentationAudioWarmup
    {
        private const float DecodeTimeoutSeconds = 8f;
        private static readonly ProfilerMarker RequestMarker =
            new ProfilerMarker("Audio.BossWarmup.Request");
        private static readonly ProfilerMarker CompleteMarker =
            new ProfilerMarker("Audio.BossWarmup.Complete");

        private AudioClip _horn;
        private AudioClip _music;
        private bool _inFlight;

        public bool IsReady => IsLoaded(_horn) && IsLoaded(_music);

        public bool Begin(
            MonoBehaviour coroutineOwner,
            AudioClip configuredHorn,
            AudioClip configuredMusic)
        {
            if (IsReady || _inFlight)
                return true;
            if (coroutineOwner == null ||
                !coroutineOwner.isActiveAndEnabled)
            {
                return false;
            }

            _inFlight = true;
            coroutineOwner.StartCoroutine(PrepareAsync(
                configuredHorn,
                configuredMusic,
                coroutineOwner));
            return true;
        }

        public bool TryGetHorn(out AudioClip clip)
        {
            clip = IsLoaded(_horn) ? _horn : null;
            return clip != null;
        }

        public bool TryGetMusic(out AudioClip clip)
        {
            clip = IsLoaded(_music) ? _music : null;
            return clip != null;
        }

        private IEnumerator PrepareAsync(
            AudioClip configuredHorn,
            AudioClip configuredMusic,
            Object logContext)
        {
            AudioClip horn = configuredHorn;
            AudioClip music = configuredMusic;

            // Begin fallback I/O before decoding the short horn so both waits
            // overlap without decoding two clips in the same frame.
            ResourceRequest musicRequest = null;
            if (music == null)
            {
                using (RequestMarker.Auto())
                {
                    musicRequest = Resources.LoadAsync<AudioClip>(
                        AudioConfig.CombatFallbackResourcePath);
                    musicRequest.priority = -1;
                }
            }

            BeginDecode(horn);
            yield return WaitForAudioData(horn);
            if (IsLoaded(horn))
                _horn = horn;

            if (musicRequest != null)
            {
                yield return musicRequest;
                music = musicRequest.asset as AudioClip;
            }

            BeginDecode(music);
            yield return WaitForAudioData(music);
            if (IsLoaded(music))
                _music = music;

            using (CompleteMarker.Auto())
                _inFlight = false;

            if (!IsReady)
            {
                Debug.LogWarning(
                    "[AudioManager] Boss presentation audio warmup " +
                    "completed with one or more unavailable clips.",
                    logContext);
            }
        }

        private static void BeginDecode(AudioClip clip)
        {
            if (clip != null &&
                clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }
        }

        private static IEnumerator WaitForAudioData(AudioClip clip)
        {
            if (clip == null)
                yield break;
            float deadline = Time.realtimeSinceStartup + DecodeTimeoutSeconds;
            while (!IsLoaded(clip) &&
                   clip.loadState != AudioDataLoadState.Failed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static bool IsLoaded(AudioClip clip)
        {
            return clip != null &&
                   clip.loadState == AudioDataLoadState.Loaded;
        }
    }
}
