using System;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Effects;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI.Hub;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the complete first-restoration arrival state of the Hub: dormant
    /// facilities, shared Scroll Spirit dialogue, materialization, and the
    /// single point at which Hub interaction becomes available again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubArrivalPresentationCoordinator : MonoBehaviour
    {
        private HubBaguaPortalGraphic _portal;
        private RectTransform _portalRect;
        private CanvasGroup _portalGroup;
        private HubTianluHoverAnim _tianluAnimation;
        private RectTransform _tianluRect;
        private CanvasGroup _tianluGroup;
        private Vector3 _portalRestScale;
        private Vector3 _tianluRestScale;
        private HubArrivalPresentationProfile _profile;
        private MetaProgression _meta;
        private GameSaveService _saveService;
        private CharacterPresentationCoordinator _characterPresenter;
        private HubRealmRestorationEffect _restorationEffect;
        private Action<bool> _setHubInteractionEnabled;
        private bool _sequenceAllowsInteraction = true;
        private bool _characterAllowsInteraction = true;
        private bool _recordCompletionAfterReveal;
        private bool _portalAwakeningStarted;
        private bool _portalSettled;
        private bool _restorationStarted;
        private bool _restorationCompleted;
        private bool _characterPresentationCompleted;
        private string _resolvedRepeatKey;

        public bool IsPlaying =>
            (_restorationEffect != null && _restorationEffect.IsPlaying) ||
            (_characterPresenter != null && _characterPresenter.IsPlaying);

        public void Bind(
            HubBaguaPortalGraphic portal,
            RectTransform portalRect,
            Image tianluImage,
            RectTransform tianluRect,
            HubTianluHoverAnim tianluAnimation,
            HubRealmRestorationEffect restorationEffect,
            HubArrivalPresentationProfile profile,
            MetaProgression meta,
            GameSaveService saveService,
            Action<bool> setHubInteractionEnabled)
        {
            _portal = portal ?? throw new ArgumentNullException(nameof(portal));
            _portalRect = portalRect ??
                throw new ArgumentNullException(nameof(portalRect));
            if (tianluImage == null)
                throw new ArgumentNullException(nameof(tianluImage));
            _tianluRect = tianluRect ??
                throw new ArgumentNullException(nameof(tianluRect));
            _tianluAnimation = tianluAnimation ??
                throw new ArgumentNullException(nameof(tianluAnimation));
            _restorationEffect = restorationEffect ??
                throw new ArgumentNullException(nameof(restorationEffect));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _saveService = saveService ??
                throw new ArgumentNullException(nameof(saveService));
            _setHubInteractionEnabled = setHubInteractionEnabled ??
                throw new ArgumentNullException(
                    nameof(setHubInteractionEnabled));

            _portalGroup = GetOrAddCanvasGroup(_portal.gameObject);
            _tianluGroup = GetOrAddCanvasGroup(tianluImage.gameObject);
            _portalRestScale = _portalRect.localScale;
            _tianluRestScale = _tianluRect.localScale;
            if (_profile.RestorationVfx != null)
                _restorationEffect.Configure(_profile.RestorationVfx);

            var presenterObject = new GameObject(
                "HubCharacterPresentationCoordinator");
            presenterObject.transform.SetParent(transform, false);
            _characterPresenter =
                presenterObject.AddComponent<CharacterPresentationCoordinator>();
            _characterPresenter.BindSceneHost(
                SetCharacterInteractionEnabled,
                _meta,
                _saveService);
        }

        public void BeginForCurrentProgression()
        {
            if (!TryResolvePendingPresentation(out string failureReason))
            {
                ShowFacilitiesImmediately();
                if (!string.IsNullOrWhiteSpace(failureReason))
                {
                    Debug.LogWarning(
                        $"[HubArrival] 跳过首次复苏演出：{failureReason}",
                        this);
                }
                return;
            }

            PlayConfiguredSequence(true);
        }

        /// <summary>
        /// Inspector/F5 调试入口：忽略存档级一次限制，重播完整的
        /// “空神域 → 卷灵 → 设施显化”时间线，且不写回一次性历史。
        /// </summary>
        public bool ReplayConfiguredSequence()
        {
            return PlayConfiguredSequence(false);
        }

        /// <summary>
        /// Inspector tuning entry: skips dialogue and replays only the realm
        /// awakening without writing presentation history.
        /// </summary>
        public bool ReplayRestorationEffectOnly()
        {
            if (!_profile.TryValidate(out string validationError))
            {
                Debug.LogWarning(
                    $"[HubArrival] 神域特效配置无效：{validationError}",
                    this);
                return false;
            }
            StopActiveSequence();
            PrepareDormantFacilities();
            _recordCompletionAfterReveal = false;
            _characterPresentationCompleted = true;
            BeginFacilityReveal();
            return true;
        }

        private bool PlayConfiguredSequence(bool recordCompletionAfterReveal)
        {
            if (!_profile.TryValidate(out string validationError))
            {
                Debug.LogWarning(
                    $"[HubArrival] 首次复苏配置无效：{validationError}",
                    this);
                ShowFacilitiesImmediately();
                return false;
            }
            StopActiveSequence();
            PrepareDormantFacilities();
            _recordCompletionAfterReveal = recordCompletionAfterReveal;

            bool started = _characterPresenter.TryPlayVariant(
                _profile.CharacterPresentationId,
                _profile.CharacterPresentationVariantId,
                HandleCharacterPresentationCompleted,
                // The whole Hub sequence owns completion. The shared character
                // renderer must not persist its repeat key before facilities
                // have finished materializing.
                true,
                BeginSpiritHandoff);
            if (started)
                return true;

            Debug.LogWarning(
                "[HubArrival] 卷灵演出未能启动；设施将直接恢复，避免 Hub 锁死。",
                this);
            ShowFacilitiesImmediately();
            return false;
        }

        private bool TryResolvePendingPresentation(out string failureReason)
        {
            failureReason = null;
            if (!_profile.TryValidate(out string profileError))
            {
                failureReason = profileError;
                return false;
            }
            bool prerequisiteCompleted = string.Equals(
                    _profile.RequiredClearedLevelContentId,
                    TutorialProgressionGate.LevelContentId,
                    StringComparison.OrdinalIgnoreCase)
                ? TutorialProgressionGate.IsCompleted(_meta)
                : _meta.IsLevelCleared(
                    _profile.RequiredClearedLevelContentId);
            if (!prerequisiteCompleted)
            {
                return false;
            }

            try
            {
                if (!CharacterPresentationCatalog.TryGet(
                        _profile.CharacterPresentationId,
                        out CharacterPresentationProfile presentation))
                {
                    failureReason =
                        $"找不到角色演出 {_profile.CharacterPresentationId}。";
                    return false;
                }
                if (!presentation.TryGetVariant(
                        _profile.CharacterPresentationVariantId,
                        out CharacterPresentationVariant variant))
                {
                    failureReason =
                        $"角色演出缺少变体 {_profile.CharacterPresentationVariantId}。";
                    return false;
                }
                if (presentation.repeatMode !=
                    CharacterPresentationRepeatMode.OncePerSave)
                {
                    failureReason =
                        $"{presentation.contentId} 必须使用 OncePerSave。";
                    return false;
                }

                string repeatKey = presentation.ResolveRepeatKey(variant);
                _resolvedRepeatKey = repeatKey;
                return !_meta.IsPresentationCompleted(repeatKey);
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                return false;
            }
        }

        private void PrepareDormantFacilities()
        {
            _sequenceAllowsInteraction = false;
            _characterAllowsInteraction = true;
            _portalAwakeningStarted = false;
            _portalSettled = false;
            _restorationStarted = false;
            _restorationCompleted = false;
            _characterPresentationCompleted = false;
            _portal.SetState(
                HubBaguaPortalGraphic.PortalState.Dormant,
                true);
            _tianluAnimation.BeginAwakening();
            HubRealmRestorationProfile vfx = _profile.RestorationVfx;
            SetGroupVisual(
                _portalGroup,
                _portalRect,
                _portalRestScale,
                0f,
                vfx.HiddenScale);
            SetGroupVisual(
                _tianluGroup,
                _tianluRect,
                _tianluRestScale,
                0f,
                vfx.HiddenScale);
            _restorationEffect.Configure(vfx);
            _restorationEffect.PrepareDormant(
                _portalRect.anchoredPosition);
            ApplyInteractionState();
        }

        private void BeginFacilityReveal()
        {
            if (_restorationStarted)
                return;

            _restorationStarted = true;
            _restorationEffect.Play(
                ApplyRestorationFrame,
                CompleteFacilityReveal);
        }

        private void BeginSpiritHandoff(
            CharacterPresentationExitSnapshot snapshot)
        {
            if (_restorationStarted)
                return;

            _restorationStarted = true;
            _restorationEffect.PlayFromScreenSubject(
                snapshot.SubjectScreenRect,
                ApplyRestorationFrame,
                CompleteFacilityReveal);
        }

        private void HandleCharacterPresentationCompleted()
        {
            _characterPresentationCompleted = true;
            // A normal close starts restoration at visual-exit start. This is
            // only a fail-safe for interrupted/custom players that never emit
            // the typed handoff callback.
            if (!_restorationStarted)
                BeginFacilityReveal();
            TryCompleteSequence();
        }

        private void ApplyRestorationFrame(
            HubRealmRestorationEffect.FacilityFrame frame)
        {
            if (!_portalAwakeningStarted && frame.PortalProgress > 0f)
            {
                _portalAwakeningStarted = true;
                _portal.SetState(
                    HubBaguaPortalGraphic.PortalState.Awakening);
            }

            HubRealmRestorationProfile vfx = _profile.RestorationVfx;
            ApplyRevealVisual(
                _portalGroup,
                _portalRect,
                _portalRestScale,
                frame.PortalProgress,
                vfx);
            ApplyRevealVisual(
                _tianluGroup,
                _tianluRect,
                _tianluRestScale,
                frame.TianluProgress,
                vfx);
            _tianluAnimation.SetAwakeningProgress(frame.TianluGlow);

            if (!_portalSettled && frame.PortalProgress >= 0.72f)
            {
                _portalSettled = true;
                _portal.SetState(HubBaguaPortalGraphic.PortalState.Idle);
            }
        }

        private void CompleteFacilityReveal()
        {
            _portal.SetState(
                HubBaguaPortalGraphic.PortalState.Idle,
                true);
            _tianluAnimation.EndAwakening();
            SetGroupVisual(
                _portalGroup,
                _portalRect,
                _portalRestScale,
                1f,
                1f);
            SetGroupVisual(
                _tianluGroup,
                _tianluRect,
                _tianluRestScale,
                1f,
                1f);

            _restorationCompleted = true;
            TryCompleteSequence();
        }

        private void TryCompleteSequence()
        {
            if (!_restorationCompleted ||
                !_characterPresentationCompleted)
                return;

            CompleteSequenceHistory();
            _sequenceAllowsInteraction = true;
            ApplyInteractionState();
        }

        private void ApplyRevealVisual(
            CanvasGroup group,
            RectTransform rect,
            Vector3 restScale,
            float progress,
            HubRealmRestorationProfile vfx)
        {
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float scale = Mathf.Lerp(
                vfx.HiddenScale,
                1f,
                eased);
            scale += Mathf.Sin(progress * Mathf.PI) *
                     vfx.ScaleOvershoot;
            SetGroupVisual(
                group,
                rect,
                restScale,
                Mathf.SmoothStep(0f, 1f, progress),
                scale);
        }

        private void ShowFacilitiesImmediately()
        {
            _restorationEffect?.ShowCompleted();
            _portal.SetState(
                HubBaguaPortalGraphic.PortalState.Idle,
                true);
            _tianluAnimation.EndAwakening();
            SetGroupVisual(
                _portalGroup,
                _portalRect,
                _portalRestScale,
                1f,
                1f);
            SetGroupVisual(
                _tianluGroup,
                _tianluRect,
                _tianluRestScale,
                1f,
                1f);
            _sequenceAllowsInteraction = true;
            _characterAllowsInteraction = true;
            _recordCompletionAfterReveal = false;
            _restorationStarted = false;
            _restorationCompleted = false;
            _characterPresentationCompleted = false;
            ApplyInteractionState();
        }

        private void StopActiveSequence()
        {
            _restorationEffect?.ShowCompleted();
            _recordCompletionAfterReveal = false;
            if (_characterPresenter != null && _characterPresenter.IsPlaying)
                _characterPresenter.CloseSilently();
        }

        private void CompleteSequenceHistory()
        {
            if (!_recordCompletionAfterReveal ||
                string.IsNullOrWhiteSpace(_resolvedRepeatKey))
            {
                _recordCompletionAfterReveal = false;
                return;
            }

            try
            {
                if (_meta.TryCompletePresentation(_resolvedRepeatKey))
                    _saveService.Save(_meta);
            }
            catch (Exception exception)
            {
                // Visual completion must still unlock Hub interaction. A save
                // failure only causes the complete sequence to retry next load.
                Debug.LogWarning(
                    $"[HubArrival] 首次复苏记录保存失败：{exception.Message}",
                    this);
            }
            finally
            {
                _recordCompletionAfterReveal = false;
            }
        }

        private void SetCharacterInteractionEnabled(bool enabled)
        {
            _characterAllowsInteraction = enabled;
            ApplyInteractionState();
        }

        private void ApplyInteractionState()
        {
            bool enabled =
                _sequenceAllowsInteraction && _characterAllowsInteraction;
            _portalGroup.interactable = enabled;
            _portalGroup.blocksRaycasts = enabled;
            _tianluGroup.interactable = enabled;
            _tianluGroup.blocksRaycasts = enabled;
            _setHubInteractionEnabled?.Invoke(enabled);
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.AddComponent<CanvasGroup>();
        }

        private static void SetGroupVisual(
            CanvasGroup group,
            RectTransform rect,
            Vector3 restScale,
            float alpha,
            float scale)
        {
            group.alpha = Mathf.Clamp01(alpha);
            rect.localScale = restScale * Mathf.Max(0.01f, scale);
        }

    }
}
