using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Audio;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Installs one persistent bridge between accepted Unity UI Button actions
    /// and the shared ButtonClick sound. Bootstrap automatically binds scene and
    /// runtime-created buttons while keeping UI assemblies free of Audio references.
    /// Pointer and submit polling remain as fallbacks, with same-frame deduplication.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobalButtonAudioRuntime : MonoBehaviour
    {
        private const float ButtonBindingRefreshSeconds = 0.5f;

        private static GlobalButtonAudioRuntime _instance;

        private readonly List<RaycastResult> _raycastResults =
            new List<RaycastResult>(16);
        private readonly HashSet<Button> _boundButtons = new HashSet<Button>();
        private Button _pressedMouseButton;
        private Button _pressedTouchButton;
        private int _trackedFingerId = -1;
        private int _lastPlayedFrame = -1;
        private float _nextButtonBindingRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if WANJIE_SIMULATION_HOST
            return;
#else
            if (_instance != null)
                return;

            AudioManager audioManager = AudioManager.Instance;
            GameObject runtimeRoot;
            if (audioManager == null)
            {
                runtimeRoot = new GameObject("[GlobalAudioRuntime]");
                audioManager = runtimeRoot.AddComponent<AudioManager>();
            }
            else
            {
                runtimeRoot = audioManager.gameObject;
            }

            if (audioManager.config == null)
            {
                audioManager.config = Resources.Load<AudioConfig>(
                    AudioConfig.DefaultResourcePath);
            }

            GlobalButtonAudioRuntime runtime =
                runtimeRoot.GetComponent<GlobalButtonAudioRuntime>();
            if (runtime == null)
                runtime = runtimeRoot.AddComponent<GlobalButtonAudioRuntime>();
            _instance = runtime;
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

        private void OnDisable()
        {
            ResetPointerState();
        }

        private void OnDestroy()
        {
            UnbindButtons();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            bool clickPlayed = Input.touchCount > 0
                ? ProcessTouches()
                : ProcessMouse();
            if (!clickPlayed && SubmitPressed())
                PlaySelectedButtonClick();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextButtonBindingRefresh)
                return;

            _nextButtonBindingRefresh =
                Time.unscaledTime + ButtonBindingRefreshSeconds;
            RefreshButtonBindings();
        }

        /// <summary>
        /// Button.onClick 是唯一能在“回调立即隐藏或销毁界面”的情况下仍稳定
        /// 表达一次已接受点击的时机。周期刷新同时覆盖运行时新建按钮，并修复
        /// 个别旧视图调用 RemoveAllListeners 后移除全局监听的情况。
        /// </summary>
        private void RefreshButtonBindings()
        {
            _boundButtons.RemoveWhere(button => button == null);
            BindButtons(FindObjectsOfType<Button>(true));
        }

        private void BindButtons(IEnumerable<Button> buttons)
        {
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;
                if (!IsGlobalButtonCandidate(button))
                {
                    button.onClick.RemoveListener(PlayClick);
                    _boundButtons.Remove(button);
                    continue;
                }

                button.onClick.RemoveListener(PlayClick);
                button.onClick.AddListener(PlayClick);
                _boundButtons.Add(button);
            }
        }

        /// <summary>
        /// Dynamic overlays call this at their Bootstrap assembly point so
        /// their first accepted click uses the global sound immediately rather
        /// than waiting for the periodic scene scan.
        /// </summary>
        public static void BindHierarchyNow(GameObject root)
        {
            if (root == null)
                return;
            if (_instance == null)
                Install();
            if (_instance == null)
                return;

            _instance._boundButtons.RemoveWhere(button => button == null);
            _instance.BindButtons(
                root.GetComponentsInChildren<Button>(true));
        }

        private void UnbindButtons()
        {
            foreach (Button button in _boundButtons)
            {
                if (button != null)
                    button.onClick.RemoveListener(PlayClick);
            }
            _boundButtons.Clear();
        }

        private bool ProcessMouse()
        {
            if (Input.GetMouseButtonDown(0))
                _pressedMouseButton = ResolveButton(Input.mousePosition);

            if (!Input.GetMouseButtonUp(0))
                return false;

            Button releasedButton = ResolveButton(Input.mousePosition);
            bool accepted = _pressedMouseButton != null &&
                            _pressedMouseButton == releasedButton;
            _pressedMouseButton = null;
            if (accepted)
                PlayClick();
            return accepted;
        }

        private bool ProcessTouches()
        {
            bool accepted = false;
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began && _trackedFingerId < 0)
                {
                    _trackedFingerId = touch.fingerId;
                    _pressedTouchButton = ResolveButton(touch.position);
                    continue;
                }

                if (touch.fingerId != _trackedFingerId)
                    continue;

                if (touch.phase == TouchPhase.Canceled)
                {
                    _trackedFingerId = -1;
                    _pressedTouchButton = null;
                    continue;
                }

                if (touch.phase != TouchPhase.Ended)
                    continue;

                Button releasedButton = ResolveButton(touch.position);
                accepted = _pressedTouchButton != null &&
                           _pressedTouchButton == releasedButton;
                _trackedFingerId = -1;
                _pressedTouchButton = null;
                if (accepted)
                    PlayClick();
            }
            return accepted;
        }

        private void PlaySelectedButtonClick()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return;

            Button button = eventSystem.currentSelectedGameObject
                .GetComponentInParent<Button>();
            if (IsUsable(button))
                PlayClick();
        }

        private Button ResolveButton(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return null;

            _raycastResults.Clear();
            var pointer = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };
            eventSystem.RaycastAll(pointer, _raycastResults);
            if (_raycastResults.Count == 0)
                return null;

            Button button = _raycastResults[0].gameObject
                .GetComponentInParent<Button>();
            return IsUsable(button) ? button : null;
        }

        /// <summary>
        /// 供场景装配点为特定按钮显式补发统一点击音（如天箓名册的返回
        /// 按钮）。与射线检测路径共用同帧去重，双路径同时命中也不会
        /// 重复发声。
        /// </summary>
        public static void PlayClickSound()
        {
            if (_instance != null)
                _instance.PlayClick();
        }

        private void PlayClick()
        {
            if (_lastPlayedFrame == Time.frameCount)
                return;

            _lastPlayedFrame = Time.frameCount;
            AudioManager.PlaySfxSafe(SfxType.ButtonClick);
        }

        private void ResetPointerState()
        {
            _pressedMouseButton = null;
            _pressedTouchButton = null;
            _trackedFingerId = -1;
        }

        private static bool IsUsable(Button button)
        {
            return IsGlobalButtonCandidate(button) &&
                   button.isActiveAndEnabled &&
                   button.IsInteractable();
        }

        private static bool IsGlobalButtonCandidate(Button button)
        {
            return button != null &&
                   button.GetComponentInParent<CardView>() == null;
        }

        private static bool SubmitPressed()
        {
            return Input.GetKeyDown(KeyCode.Return) ||
                   Input.GetKeyDown(KeyCode.KeypadEnter) ||
                   Input.GetKeyDown(KeyCode.Space) ||
                   Input.GetKeyDown(KeyCode.JoystickButton0);
        }
    }
}
