using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 玩家音频偏好快照。不包含场景音乐、cue 或素材校准，
    /// 恢复玩家默认时不得修改这些产品调音数据。
    /// </summary>
    public readonly struct AudioPlayerSettings
    {
        public AudioPlayerSettings(
            float master,
            float music,
            float sfx,
            float voice,
            bool muted)
        {
            Master = master;
            Music = music;
            Sfx = sfx;
            Voice = voice;
            Muted = muted;
        }

        public static AudioPlayerSettings Recommended =>
            new AudioPlayerSettings(1f, 0.8f, 1f, 1f, false);

        public float Master { get; }
        public float Music { get; }
        public float Sfx { get; }
        public float Voice { get; }
        public bool Muted { get; }

        public AudioPlayerSettings Normalize()
        {
            AudioPlayerSettings fallback = Recommended;
            return new AudioPlayerSettings(
                NormalizeVolume(Master, fallback.Master),
                NormalizeVolume(Music, fallback.Music),
                NormalizeVolume(Sfx, fallback.Sfx),
                NormalizeVolume(Voice, fallback.Voice),
                Muted);
        }

        private static float NormalizeVolume(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    /// <summary>
    /// UI 可调用的最小音频设置端口。实现留在 Audio 模块，Core 不掌握混音细节。
    /// </summary>
    public interface IAudioSettingsRuntime
    {
        AudioPlayerSettings PlayerSettings { get; }
        void ApplyPlayerSettings(AudioPlayerSettings settings);
    }

    /// <summary>
    /// 常驻音频实现的运行时注册点。AudioManager 在自身生命周期内注册，
    /// 首页只依赖该窄端口，不反向引用 Audio 程序集。
    /// </summary>
    public static class AudioSettingsRuntime
    {
        public static IAudioSettingsRuntime Current { get; private set; }

        public static void Register(IAudioSettingsRuntime runtime)
        {
            Current = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public static void Unregister(IAudioSettingsRuntime runtime)
        {
            if (ReferenceEquals(Current, runtime))
                Current = null;
        }
    }
}
