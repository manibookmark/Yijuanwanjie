using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>One positive identity domain for every native boss-audio producer.
    /// Generation remains local to the producer; identity never restarts per type.</summary>
    public static class BossAudioOwnerIdentity
    {
        private static long _next;
        public static long Allocate()
            => System.Threading.Interlocked.Increment(ref _next);
    }

    public enum BossAudioRequestKind { Play, Stop }

    /// <summary>
    /// Playback intent only. Audio owns the actual source, SFX mixer and player volume.
    /// OwnerKey is unique to an animator lifetime; Generation orders requests for that owner.
    /// </summary>
    public readonly struct BossAudioRequest
    {
        public BossAudioRequest(long ownerKey, long generation, BossAudioRequestKind kind,
            AudioClip clip = null, bool loop = false, float volume = 1f, float pitch = 1f,
            float startOffsetSeconds = 0f)
        {
            OwnerKey = ownerKey;
            Generation = generation;
            Kind = kind;
            Clip = clip;
            Loop = loop;
            Volume = volume;
            Pitch = pitch;
            StartOffsetSeconds = startOffsetSeconds;
        }

        public long OwnerKey { get; }
        public long Generation { get; }
        public BossAudioRequestKind Kind { get; }
        public AudioClip Clip { get; }
        public bool Loop { get; }
        public float Volume { get; }
        public float Pitch { get; }
        /// <summary>Position in the source clip, before pitch is applied.</summary>
        public float StartOffsetSeconds { get; }
    }
}
