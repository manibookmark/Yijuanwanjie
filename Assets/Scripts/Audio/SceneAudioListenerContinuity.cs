using UnityEngine;
using UnityEngine.SceneManagement;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// Owns the sole persistent fallback listener between scene cameras. Scene
    /// events and explicit dynamic-camera handoffs reconcile ownership; there is
    /// no per-frame search and no change to external listeners' enabled state.
    /// The AudioManager owns this component's persistent lifetime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneAudioListenerContinuity : MonoBehaviour
    {
        private static SceneAudioListenerContinuity _instance;
        [SerializeField, HideInInspector] private AudioListener _fallback;

        public bool IsUsingFallback => _fallback != null && _fallback.isActiveAndEnabled;

        /// <summary>
        /// Idempotently installs on the persistent AudioManager. Call Reconcile
        /// after creating/enabling a dynamic scene listener, before yielding.
        /// </summary>
        public static SceneAudioListenerContinuity Ensure(AudioManager manager = null)
        {
            if (_instance != null)
            {
                if (!_instance.enabled) _instance.enabled = true;
                return _instance;
            }
            manager = manager != null ? manager : AudioManager.EnsureInstance();
            // Ensuring the AudioManager may have installed us from its Awake.
            if (_instance != null) return _instance;
            var owner = manager.GetComponent<SceneAudioListenerContinuity>();
            if (owner == null) owner = manager.gameObject.AddComponent<SceneAudioListenerContinuity>();
            if (!owner.enabled) owner.enabled = true;
            return owner;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false;
                if (Application.isPlaying) Destroy(this);
                else DestroyImmediate(this);
                return;
            }
            _instance = this;
            EnsureFallback();
        }

        private void OnEnable()
        {
            // A script/domain reload may clear the static reference while this
            // persistent component and its serialized listener still exist.
            if (_instance == null) _instance = this;
            if (_instance != this) return;
            Unsubscribe();
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Reconcile();
        }

        /// <summary>
        /// Keeps fallback only while every external listener is absent, disabled
        /// or hidden. Call after dynamic scene-audio infrastructure has changed.
        /// </summary>
        public void Reconcile()
        {
            if (_instance != this || !isActiveAndEnabled) return;
            EnsureFallback();
            bool hasSceneListener = false;
            foreach (AudioListener listener in FindObjectsOfType<AudioListener>())
            {
                if (listener != _fallback && listener.isActiveAndEnabled)
                {
                    hasSceneListener = true;
                    break;
                }
            }
            _fallback.enabled = !hasSceneListener;
        }

        private void EnsureFallback()
        {
            if (_fallback != null) return;
            // Create disabled, even when a scene camera already owns audio.
            var root = new GameObject("SceneAudioFallbackListener");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            _fallback = root.AddComponent<AudioListener>();
            _fallback.enabled = false;
            root.SetActive(true);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Reconcile();
        private void OnSceneUnloaded(Scene scene) => Reconcile();
        private void OnActiveSceneChanged(Scene previous, Scene current) => Reconcile();

        private void OnDisable()
        {
            Unsubscribe();
            if (_fallback != null) _fallback.enabled = false;
        }

        private void Unsubscribe()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_fallback != null)
            {
                _fallback.enabled = false;
                if (Application.isPlaying) Destroy(_fallback.gameObject);
                else DestroyImmediate(_fallback.gameObject);
            }
            if (_instance == this) _instance = null;
        }
    }
}
