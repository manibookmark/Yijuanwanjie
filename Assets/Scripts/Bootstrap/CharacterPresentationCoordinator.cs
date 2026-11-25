using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Audio;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 角色演出的场景级入口。集中隐藏配置解析、专用画布、宿主输入阻断、
    /// Run 级一次性历史、存档级一次性历史（OncePerSave）、驻守后的输入恢复和语音通道
    /// 接线（台词 cue -> 播放，演出结束 -> 停止）；具体视觉时间线仍由 UI 播放器拥有。
    /// </summary>
    public sealed class CharacterPresentationCoordinator : MonoBehaviour
    {
        private LevelRunSession _session;
        private Action<bool> _setUnderlyingInteractionEnabled;
        private MetaProgression _metaProgression;
        private GameSaveService _saveService;
        private Canvas _canvas;
        private CharacterPresentationOverlay _overlay;
        private CharacterPresentationProfile _activeProfile;
        private string _activePresentationId;
        private string _activeRepeatKey;
        private Action _onCompleted;
        private bool _ignoreRepeatRulesForSession;
        private bool _activeIgnoresRepeatRules;
        private bool _isBound;

        public bool IsPlaying => !string.IsNullOrWhiteSpace(_activePresentationId);
        public bool ActiveBlocksUnderlyingInteraction =>
            _activeProfile?.BlocksUnderlyingInteraction ?? false;

        public void Bind(
            LevelRunSession session,
            MapPresentation mapPresentation,
            MetaProgression metaProgression = null,
            GameSaveService saveService = null,
            bool ignoreRepeatRulesForSession = false)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (mapPresentation == null)
                throw new ArgumentNullException(nameof(mapPresentation));

            BindCore(
                session,
                mapPresentation.SetInteractionEnabled,
                metaProgression,
                saveService,
                ignoreRepeatRulesForSession);
        }

        /// <summary>
        /// 绑定没有 LevelRunSession/MapPresentation 的场景宿主（例如 Hub）。
        /// OncePerRun 仍要求关卡会话；Always 与 OncePerSave 可直接复用同一播放器。
        /// </summary>
        public void BindSceneHost(
            Action<bool> setUnderlyingInteractionEnabled,
            MetaProgression metaProgression = null,
            GameSaveService saveService = null,
            bool ignoreRepeatRulesForSession = false)
        {
            BindCore(
                null,
                setUnderlyingInteractionEnabled ??
                    throw new ArgumentNullException(
                        nameof(setUnderlyingInteractionEnabled)),
                metaProgression,
                saveService,
                ignoreRepeatRulesForSession);
        }

        private void BindCore(
            LevelRunSession session,
            Action<bool> setUnderlyingInteractionEnabled,
            MetaProgression metaProgression,
            GameSaveService saveService,
            bool ignoreRepeatRulesForSession)
        {
            _session = session;
            _setUnderlyingInteractionEnabled =
                setUnderlyingInteractionEnabled;
            _metaProgression = metaProgression;
            _saveService = saveService;
            _ignoreRepeatRulesForSession = ignoreRepeatRulesForSession;
            _isBound = true;

            EnsureEventSystem();
            EnsureCanvas();
        }

        public bool TryPlay(
            string presentationContentId,
            Action onCompleted = null,
            bool ignoreRepeatRules = false)
        {
            return TryPlayCore(
                presentationContentId,
                null,
                onCompleted,
                ignoreRepeatRules);
        }

        public bool TryPlayVariant(
            string presentationContentId,
            string variantId,
            Action onCompleted = null,
            bool ignoreRepeatRules = false,
            Action<CharacterPresentationExitSnapshot> onExitStarted = null)
        {
            return TryPlayCore(
                presentationContentId,
                variantId,
                onCompleted,
                ignoreRepeatRules,
                onExitStarted);
        }

        private bool TryPlayCore(
            string presentationContentId,
            string variantId,
            Action onCompleted,
            bool ignoreRepeatRules,
            Action<CharacterPresentationExitSnapshot> onExitStarted = null)
        {
            if (IsPlaying || !_isBound)
                return false;

            CharacterPresentationProfile profile;
            try
            {
                if (!CharacterPresentationCatalog.TryGet(
                        presentationContentId, out profile))
                {
                    Debug.LogError(
                        $"[CharacterPresentation] 找不到角色演出：{presentationContentId}。");
                    return false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[CharacterPresentation] 配置无效：{exception.Message}");
                return false;
            }

            CharacterPresentationVariant variant = null;
            if (profile.HasVariants)
            {
                if (!profile.TryGetVariant(variantId, out variant))
                {
                    Debug.LogError(
                        $"[CharacterPresentation] 演出 {profile.contentId} " +
                        $"缺少内容变体：{variantId ?? "<空>"}。");
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(variantId))
            {
                Debug.LogError(
                    $"[CharacterPresentation] 演出 {profile.contentId} " +
                    $"不接受内容变体：{variantId}。");
                return false;
            }

            string repeatKey = profile.ResolveRepeatKey(variant);
            bool ignoresRepeatRules =
                ignoreRepeatRules || _ignoreRepeatRulesForSession;

            if (!ignoresRepeatRules &&
                profile.repeatMode == CharacterPresentationRepeatMode.OncePerRun)
            {
                if (_session == null)
                {
                    Debug.LogError(
                        $"[CharacterPresentation] 演出 {profile.contentId} 使用 " +
                        "OncePerRun，但当前场景宿主没有 LevelRunSession。");
                    return false;
                }

                if (!_session.Presentations.TryBegin(
                        repeatKey, out string historyError))
                {
                    Debug.Log(
                        $"[CharacterPresentation] 跳过一次性演出：{historyError}");
                    return false;
                }
            }

            if (!ignoresRepeatRules &&
                profile.repeatMode == CharacterPresentationRepeatMode.OncePerSave &&
                _metaProgression != null &&
                _metaProgression.IsPresentationCompleted(repeatKey))
            {
                Debug.Log(
                    $"[CharacterPresentation] 跳过存档级一次性演出：" +
                    $"{repeatKey}（该存档已完成）");
                return false;
            }

            _activeProfile = profile;
            _activePresentationId = profile.contentId;
            _activeRepeatKey = repeatKey;
            _onCompleted = onCompleted;
            _activeIgnoresRepeatRules = ignoresRepeatRules;
            if (profile.BlocksUnderlyingInteraction)
                _setUnderlyingInteractionEnabled?.Invoke(false);
            _canvas.gameObject.SetActive(true);

            if (_overlay == null)
                _overlay = gameObject.AddComponent<CharacterPresentationOverlay>();
            try
            {
                _overlay.Show(
                    _canvas,
                    profile,
                    variant,
                    CompleteActivePresentation,
                    cueId => AudioManager.PlayVoiceCueSafe(cueId),
                    cueId => AudioManager.IsVoiceCuePlayingSafe(cueId),
                    onExitStarted);
            }
            catch (Exception exception)
            {
                if (profile.repeatMode ==
                    CharacterPresentationRepeatMode.OncePerRun)
                {
                    _session.Presentations.Cancel(repeatKey);
                }
                AudioManager.StopVoiceCueSafe();
                _activePresentationId = null;
                _activeRepeatKey = null;
                _activeProfile = null;
                _onCompleted = null;
                _activeIgnoresRepeatRules = false;
                _canvas.gameObject.SetActive(false);
                if (profile.BlocksUnderlyingInteraction)
                    _setUnderlyingInteractionEnabled?.Invoke(true);
                Debug.LogError(
                    $"[CharacterPresentation] 演出启动失败：{exception.Message}");
                return false;
            }
            return true;
        }

        private void CompleteActivePresentation()
        {
            string repeatKey = _activeRepeatKey;
            CharacterPresentationProfile profile = _activeProfile;
            Action callback = _onCompleted;
            bool ignoredRepeatRules = _activeIgnoresRepeatRules;
            bool keepsActorVisible =
                profile != null && profile.PersistsAfterAcknowledgement;

            // 台词已结束（或被确认跳过），语音随演出收尾，不得残留。
            AudioManager.StopVoiceCueSafe();
            _activePresentationId = null;
            _activeRepeatKey = null;
            _activeProfile = null;
            _onCompleted = null;
            _activeIgnoresRepeatRules = false;
            if (profile != null &&
                !ignoredRepeatRules &&
                profile.repeatMode == CharacterPresentationRepeatMode.OncePerRun)
            {
                _session?.Presentations.Complete(repeatKey);
            }

            if (profile != null &&
                !ignoredRepeatRules &&
                profile.repeatMode == CharacterPresentationRepeatMode.OncePerSave &&
                _metaProgression != null)
            {
                try
                {
                    if (_metaProgression.TryCompletePresentation(repeatKey))
                        _saveService?.Save(_metaProgression);
                }
                catch (Exception exception)
                {
                    // 演出已完成，持久化失败只影响下次是否重播，不阻断收尾。
                    Debug.LogWarning(
                        $"[CharacterPresentation] 存档级演出记录写入失败：" +
                        $"{exception.Message}");
                }
            }

            if (_canvas != null && !keepsActorVisible)
                _canvas.gameObject.SetActive(false);
            if (profile?.BlocksUnderlyingInteraction ?? true)
                _setUnderlyingInteractionEnabled?.Invoke(true);
            callback?.Invoke();
        }

        public void CloseSilently()
        {
            bool restoreMapInteraction =
                _activeProfile?.BlocksUnderlyingInteraction ?? false;
            if (_activeProfile != null &&
                !_activeIgnoresRepeatRules &&
                _activeProfile.repeatMode == CharacterPresentationRepeatMode.OncePerRun)
            {
                _session?.Presentations.Cancel(_activeRepeatKey);
            }

            AudioManager.StopVoiceCueSafe();
            _overlay?.CloseSilently();
            _activePresentationId = null;
            _activeRepeatKey = null;
            _activeProfile = null;
            _onCompleted = null;
            _activeIgnoresRepeatRules = false;
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
            if (restoreMapInteraction)
                _setUnderlyingInteractionEnabled?.Invoke(true);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;

            var canvasObject = new GameObject(
                "CharacterPresentationCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32500;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasObject.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;

            var eventSystemObject = new GameObject(
                "CharacterPresentationEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        private void OnDestroy()
        {
            CloseSilently();
        }
    }
}
