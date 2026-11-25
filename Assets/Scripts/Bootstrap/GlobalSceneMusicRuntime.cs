using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Audio;
using WanjieScroll.Core;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the application's baseline scene-music policy and startup guarantee.
    /// It installs once, maps every shipping scene to a semantic track, and retries
    /// briefly until playback has started or its asynchronous load was accepted.
    /// Later combat overrides remain free to change music because the baseline
    /// guarantee ends after scene entry succeeds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobalSceneMusicRuntime : MonoBehaviour
    {
        private const int MaximumStartupAttempts = 12;
        private static GlobalSceneMusicRuntime _instance;
        private Coroutine _applyRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if WANJIE_SIMULATION_HOST
            return;
#else
            if (_instance != null)
            {
                _instance.RequestActiveSceneMusic();
                return;
            }

            GlobalSceneMusicRuntime existing =
                FindObjectOfType<GlobalSceneMusicRuntime>();
            if (existing != null)
            {
                _instance = existing;
                existing.RequestActiveSceneMusic();
                return;
            }

            AudioManager audioManager = AudioManager.EnsureInstance();
            GlobalSceneMusicRuntime runtime =
                audioManager.GetComponent<GlobalSceneMusicRuntime>();
            if (runtime == null)
                runtime = audioManager.gameObject.AddComponent<GlobalSceneMusicRuntime>();
            _instance = runtime;
            runtime.RequestActiveSceneMusic();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyFirstLoadedScene()
        {
#if WANJIE_SIMULATION_HOST
            return;
#else
            // BeforeSceneLoad can observe the editor's temporary bootstrap scene
            // when Play starts from an already-open front-end scene. Reapply after
            // that scene is active so its music request cannot be lost.
            if (_instance == null)
            {
                Install();
                return;
            }

            _instance.RequestActiveSceneMusic();
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
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            RequestActiveSceneMusic();
        }

        private void Start()
        {
            // Start runs after the directly opened scene has completed Awake.
            RequestActiveSceneMusic();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            StopPendingApplication();
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            StopPendingApplication();
            if (_instance == this)
                _instance = null;
        }

        private void HandleActiveSceneChanged(Scene previous, Scene current)
        {
            RequestActiveSceneMusic();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Additive preloads must not reapply a baseline over a later override.
            if (SceneManager.GetActiveScene().handle == scene.handle)
                RequestActiveSceneMusic();
        }

        private void RequestActiveSceneMusic()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded &&
                ShouldSuppressMusic(scene.name))
            {
                StopPendingApplication();
                AudioManager.StopMusicSafe();
                return;
            }

            if (!scene.IsValid() || !scene.isLoaded ||
                !TryResolveMusic(scene.name, out MusicScene musicScene))
            {
                StopPendingApplication();
                return;
            }

            StopPendingApplication();
            _applyRoutine = StartCoroutine(
                ApplyWhenSceneReady(scene.handle, scene.name, musicScene));
        }

        private IEnumerator ApplyWhenSceneReady(
            int expectedSceneHandle,
            string sceneName,
            MusicScene musicScene)
        {
            // Give cameras, listeners, scene bootstraps and Resources one frame
            // to become ready when Play starts directly from an arbitrary scene.
            yield return null;

            for (int attempt = 0; attempt < MaximumStartupAttempts; attempt++)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() ||
                    activeScene.handle != expectedSceneHandle)
                {
                    _applyRoutine = null;
                    yield break;
                }

                AudioManager audioManager = AudioManager.EnsureInstance();
                if (audioManager != null && audioManager.EnsureMusic(musicScene))
                {
                    _applyRoutine = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning(
                $"[GlobalSceneMusic] Scene '{sceneName}' could not start " +
                $"baseline music '{musicScene}' after " +
                $"{MaximumStartupAttempts} attempts.",
                this);
            _applyRoutine = null;
        }

        private void StopPendingApplication()
        {
            if (_applyRoutine == null)
                return;
            StopCoroutine(_applyRoutine);
            _applyRoutine = null;
        }

        /// <summary>
        /// Resolves every shipping scene without using display objects or local
        /// bootstrap state. Semantic tracks may fall back to the front-end clip
        /// inside AudioConfig until their dedicated assets are assigned.
        /// </summary>
        public static bool ShouldSuppressMusic(string sceneName)
        {
            return string.Equals(
                sceneName,
                SceneNames.OpeningCG,
                System.StringComparison.Ordinal);
        }

        public static bool TryResolveMusic(
            string sceneName,
            out MusicScene musicScene)
        {
            switch (sceneName)
            {
                case SceneNames.Loading:
                case SceneNames.MainMenu:
                case SceneNames.Hub:
                case SceneNames.LevelSelect:
                case SceneNames.RouteSelect:
                case SceneNames.Daozang:
                    musicScene = MusicScene.Hall;
                    return true;
                case SceneNames.Dungeon:
                case SceneNames.LevelMap:
                    musicScene = MusicScene.Dungeon;
                    return true;
                // 普通战斗沿用地图曲：从地图进战斗时 EnsureMusic 判定同曲不重播；
                // Boss 战由 Boss 入场演出显式切曲，不受此基线约束。
                case SceneNames.Combat:
                    musicScene = MusicScene.Dungeon;
                    return true;
                default:
                    musicScene = default;
                    return false;
            }
        }
    }
}
