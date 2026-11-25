using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// Owns at most one fade per music source. Replacing a fade revokes its
    /// lease before stopping its iterator, so an old tail cannot stop or clear
    /// a source that now plays another clip. Mixing decisions remain outside.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioSourceFadeOwner : MonoBehaviour
    {
        private sealed class Fade
        {
            public AudioSource Source;
            public AudioClip Clip;
            public Coroutine Routine;
        }

        private readonly Dictionary<AudioSource, Fade> _fades =
            new Dictionary<AudioSource, Fade>();

        public int ActiveFadeCount => _fades.Count;

        public void FadeTo(AudioSource source, float targetVolume, float duration,
            bool stopWhenSilent = false, bool clearClipWhenSilent = false)
        {
            if (source == null) return;
            Cancel(source);
            if (!isActiveAndEnabled) return;
            var fade = new Fade { Source = source, Clip = source.clip };
            _fades.Add(source, fade);
            fade.Routine = StartCoroutine(Play(fade, Mathf.Clamp01(targetVolume),
                Mathf.Max(0f, duration), stopWhenSilent, clearClipWhenSilent));
        }

        public void Cancel(AudioSource source)
        {
            // A destroyed Unity wrapper can still be a dictionary key.
            if (ReferenceEquals(source, null) || !_fades.TryGetValue(source, out Fade fade)) return;
            _fades.Remove(source);
            if (fade.Routine != null) StopCoroutine(fade.Routine);
        }

        public void CancelAll()
        {
            var old = new List<Fade>(_fades.Values);
            _fades.Clear();
            foreach (Fade fade in old)
                if (fade.Routine != null) StopCoroutine(fade.Routine);
        }

        private IEnumerator Play(Fade fade, float target, float duration,
            bool stopWhenSilent, bool clearClipWhenSilent)
        {
            try
            {
                float start = fade.Source.volume;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (!IsCurrent(fade)) yield break;
                    elapsed += Time.unscaledDeltaTime;
                    fade.Source.volume = Mathf.Lerp(start, target,
                        duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
                if (!IsCurrent(fade)) yield break;
                fade.Source.volume = target;
                if (stopWhenSilent && target <= 0.0001f)
                {
                    fade.Source.Stop();
                    if (clearClipWhenSilent) fade.Source.clip = null;
                }
            }
            finally
            {
                if (_fades.TryGetValue(fade.Source, out Fade current) &&
                    ReferenceEquals(current, fade))
                    _fades.Remove(fade.Source);
            }
        }

        private bool IsCurrent(Fade fade) => isActiveAndEnabled &&
            fade.Source != null && fade.Source.clip == fade.Clip &&
            _fades.TryGetValue(fade.Source, out Fade current) && ReferenceEquals(current, fade);

        private void OnDisable() => CancelAll();
        private void OnDestroy() => CancelAll();
    }
}
