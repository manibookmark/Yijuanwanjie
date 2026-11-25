using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Persistence
{
    /// <summary>键盘快捷键的稳定默认与持久化校验规则。</summary>
    public static class KeyboardShortcutSettings
    {
        public const KeyCode RecommendedEndTurnKey = KeyCode.E;

        public static bool TryValidate(KeyCode candidate, out string error)
        {
            if (candidate == KeyCode.None)
            {
                error = "请选择按键";
                return false;
            }

            if (candidate == KeyCode.Escape ||
                candidate == KeyCode.Return ||
                candidate == KeyCode.KeypadEnter ||
                candidate == KeyCode.Space)
            {
                error = "该键已被占用，请换一个";
                return false;
            }

            string name = candidate.ToString();
            if (name.StartsWith("Mouse", StringComparison.Ordinal) ||
                name.StartsWith("Joystick", StringComparison.Ordinal))
            {
                error = "请使用键盘按键";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    /// <summary>全局玩家设置的不可变快照；独立于任何进度存档槽。</summary>
    public readonly struct GameSettingsData
    {
        public GameSettingsData(
            bool fullscreen,
            bool vSync,
            float masterVolume,
            float musicVolume,
            float sfxVolume,
            float voiceVolume,
            bool muted,
            KeyCode endTurnKey)
        {
            Fullscreen = fullscreen;
            VSync = vSync;
            MasterVolume = masterVolume;
            MusicVolume = musicVolume;
            SfxVolume = sfxVolume;
            VoiceVolume = voiceVolume;
            Muted = muted;
            EndTurnKey = endTurnKey;
        }

        public static GameSettingsData Recommended => new GameSettingsData(
            true, true, 1f, 0.8f, 1f, 1f, false,
            KeyboardShortcutSettings.RecommendedEndTurnKey);

        public bool Fullscreen { get; }
        public bool VSync { get; }
        public float MasterVolume { get; }
        public float MusicVolume { get; }
        public float SfxVolume { get; }
        public float VoiceVolume { get; }
        public bool Muted { get; }
        public KeyCode EndTurnKey { get; }

        public AudioPlayerSettings Audio => new AudioPlayerSettings(
            MasterVolume, MusicVolume, SfxVolume, VoiceVolume, Muted);

        public GameSettingsData WithDisplay(bool fullscreen, bool vSync)
        {
            return new GameSettingsData(
                fullscreen, vSync,
                MasterVolume, MusicVolume, SfxVolume, VoiceVolume, Muted,
                EndTurnKey);
        }

        public GameSettingsData WithAudio(
            float master,
            float music,
            float sfx,
            float voice,
            bool muted)
        {
            return new GameSettingsData(
                Fullscreen, VSync,
                master, music, sfx, voice, muted,
                EndTurnKey);
        }

        public GameSettingsData WithEndTurnKey(KeyCode key)
        {
            return new GameSettingsData(
                Fullscreen, VSync,
                MasterVolume, MusicVolume, SfxVolume, VoiceVolume, Muted,
                key);
        }

        public GameSettingsData Normalize()
        {
            AudioPlayerSettings audio = Audio.Normalize();
            KeyCode endTurn = KeyboardShortcutSettings.TryValidate(
                EndTurnKey, out _)
                ? EndTurnKey
                : KeyboardShortcutSettings.RecommendedEndTurnKey;
            return new GameSettingsData(
                Fullscreen, VSync,
                audio.Master, audio.Music, audio.Sfx, audio.Voice, audio.Muted,
                endTurn);
        }
    }

    /// <summary>
    /// 本机设置的唯一持久化边界：封装默认值、稳定 key、
    /// 归一化与落盘。Update 立即发布新快照，Flush 才强制写盘。
    /// </summary>
    public sealed class GameSettingsStore
    {
        public const int CurrentSchemaVersion = 1;
        public const string SchemaVersionKey = "schema.version";
        public const string FullscreenKey = "display.fullscreen";
        public const string VSyncKey = "display.vsync";
        public const string MasterVolumeKey = "audio.master";
        public const string MusicVolumeKey = "audio.music";
        public const string SfxVolumeKey = "audio.sfx";
        public const string VoiceVolumeKey = "audio.voice";
        public const string MutedKey = "audio.mute";
        public const string EndTurnKeyKey = "input.combat.end_turn";

        private const string ProductionPrefix = "settings.";
        private static GameSettingsStore _current;

        private readonly string _prefix;

        private GameSettingsStore(string prefix)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            Current = Read();
        }

        public static IReadOnlyList<string> PreferenceKeySuffixes { get; } =
            new[]
            {
                SchemaVersionKey,
                FullscreenKey,
                VSyncKey,
                MasterVolumeKey,
                MusicVolumeKey,
                SfxVolumeKey,
                VoiceVolumeKey,
                MutedKey,
                EndTurnKeyKey
            };

        public static GameSettingsStore Shared =>
            _current ??= Create(ProductionPrefix);

        public static GameSettingsStore Create(string keyPrefix)
        {
            return new GameSettingsStore(keyPrefix);
        }

        public GameSettingsData Current { get; private set; }

        public event Action<GameSettingsData> Changed;

        public void Update(GameSettingsData settings, bool flush = false)
        {
            Current = settings.Normalize();
            Write(Current);
            Changed?.Invoke(Current);
            if (flush)
                Flush();
        }

        public void ResetToRecommended(bool flush = false)
        {
            Update(GameSettingsData.Recommended, flush);
        }

        public void Flush()
        {
            PlayerPrefs.Save();
        }

        private GameSettingsData Read()
        {
            GameSettingsData fallback = GameSettingsData.Recommended;
            var loaded = new GameSettingsData(
                ReadBool(FullscreenKey, fallback.Fullscreen),
                ReadBool(VSyncKey, fallback.VSync),
                PlayerPrefs.GetFloat(Key(MasterVolumeKey), fallback.MasterVolume),
                PlayerPrefs.GetFloat(Key(MusicVolumeKey), fallback.MusicVolume),
                PlayerPrefs.GetFloat(Key(SfxVolumeKey), fallback.SfxVolume),
                PlayerPrefs.GetFloat(Key(VoiceVolumeKey), fallback.VoiceVolume),
                ReadBool(MutedKey, fallback.Muted),
                (KeyCode)PlayerPrefs.GetInt(
                    Key(EndTurnKeyKey), (int)fallback.EndTurnKey));
            return loaded.Normalize();
        }

        private void Write(GameSettingsData settings)
        {
            PlayerPrefs.SetInt(Key(SchemaVersionKey), CurrentSchemaVersion);
            PlayerPrefs.SetInt(Key(FullscreenKey), settings.Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Key(VSyncKey), settings.VSync ? 1 : 0);
            PlayerPrefs.SetFloat(Key(MasterVolumeKey), settings.MasterVolume);
            PlayerPrefs.SetFloat(Key(MusicVolumeKey), settings.MusicVolume);
            PlayerPrefs.SetFloat(Key(SfxVolumeKey), settings.SfxVolume);
            PlayerPrefs.SetFloat(Key(VoiceVolumeKey), settings.VoiceVolume);
            PlayerPrefs.SetInt(Key(MutedKey), settings.Muted ? 1 : 0);
            PlayerPrefs.SetInt(Key(EndTurnKeyKey), (int)settings.EndTurnKey);
        }

        private bool ReadBool(string suffix, bool fallback)
        {
            return PlayerPrefs.GetInt(Key(suffix), fallback ? 1 : 0) != 0;
        }

        private string Key(string suffix) => _prefix + suffix;
    }
}
