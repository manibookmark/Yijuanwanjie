using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.Persistence;
using WanjieScroll.UI;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 跨场景设置装配点。从 Persistence 的唯一快照读取偏好，
    /// 再交给显示、Audio 和输入模块；不保存第二份设置状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobalGameSettingsRuntime : MonoBehaviour
    {
        private static GlobalGameSettingsRuntime _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if WANJIE_SIMULATION_HOST
            return;
#else
            if (_instance != null)
            {
                _instance.Apply(GameSettingsStore.Shared.Current);
                return;
            }

            AudioManager audio = AudioManager.EnsureInstance();
            GlobalGameSettingsRuntime runtime =
                audio.GetComponent<GlobalGameSettingsRuntime>();
            if (runtime == null)
                runtime = audio.gameObject.AddComponent<GlobalGameSettingsRuntime>();
            _instance = runtime;
            runtime.Apply(GameSettingsStore.Shared.Current);
#endif
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            GameSettingsStore.Shared.Changed -= Apply;
            GameSettingsStore.Shared.Changed += Apply;
            Apply(GameSettingsStore.Shared.Current);
        }

        private void OnDisable()
        {
            GameSettingsStore.Shared.Changed -= Apply;
        }

        private void OnApplicationQuit()
        {
            GameSettingsStore.Shared.Flush();
        }

        private void OnDestroy()
        {
            GameSettingsStore.Shared.Changed -= Apply;
            if (_instance == this)
                _instance = null;
        }

        private void Apply(GameSettingsData settings)
        {
            Screen.fullScreen = settings.Fullscreen;
            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;
            ShortcutBindingRuntime.Apply(settings.EndTurnKey);

            IAudioSettingsRuntime audio = AudioSettingsRuntime.Current;
            if (audio == null)
            {
                AudioManager.EnsureInstance();
                audio = AudioSettingsRuntime.Current;
            }
            audio?.ApplyPlayerSettings(settings.Audio);
        }
    }
}
