using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Presentations
{
    /// <summary>
    /// 透明角色演出的唯一运行时状态所有者。配置决定循环方式、台词节拍、面板样式、
    /// 自动退场、强制确认或确认后驻守；调用方无需接触帧、UV、点击门和渐隐顺序。
    /// </summary>
    public sealed class CharacterPresentationOverlay : MonoBehaviour
    {
        private enum Phase
        {
            Entering,
            Presenting,
            AwaitingAcknowledgement,
            FadingPanel,
            Exiting,
            Persisting,
            Closed
        }

        private CharacterPresentationView _view;
        private CharacterPresentationProfile _profile;
        private CharacterPresentationVariant _variant;
        private IReadOnlyList<CharacterPresentationLine> _lines;
        private Action _onClosed;
        private Action<CharacterPresentationExitSnapshot> _onExitStarted;
        private Action<string> _onAudioCueRequested;
        private Func<string, bool> _isAudioCueActive;
        private string _activeCueId;
        private Phase _phase = Phase.Closed;
        private float _phaseElapsed;
        private float _sequenceElapsed;
        private float _lineElapsed;
        private float _audioWaitElapsed;
        private bool _voiceWaitTimedOut;
        private int _frameIndex;
        private int _lineIndex;
        private bool _dialogueComplete;
        private bool _closing;
        private bool _entranceEffectPending;
        private int _entranceEffectStartFrame;

        public GameObject Root => _view?.Root;
        public CharacterPresentationProfile Profile => _profile;
        public string VariantId => _variant?.variantId;
        public int CurrentFrameIndex => _frameIndex;
        public bool IsReadyToDismiss =>
            _phase == Phase.AwaitingAcknowledgement;
        public bool IsPersisting => _phase == Phase.Persisting;

        /// <summary>
        /// 将当前 Profile 的布局参数立即应用到正在播放的视图。
        /// 供编辑器 Inspector 在 Play 模式下做所见即所得调参。
        /// </summary>
        public bool RefreshTuningPreview()
        {
            if (_view == null || _profile == null || _phase == Phase.Closed)
                return false;

            _view.RefreshTunableVisuals();
            return true;
        }

        /// <summary>
        /// 不重置台词或语音，仅按当前参数重播角色淡入与显化特效。
        /// </summary>
        public bool ReplayEntranceTuningPreview()
        {
            if (!RefreshTuningPreview())
                return false;

            _entranceEffectPending = false;
            _closing = false;
            _phaseElapsed = 0f;
            bool hasEntrance = _profile.ResolvedEnterDuration > 0f;
            _view.SetEntrance(
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0.965f : 1f);
            _phase = hasEntrance ? Phase.Entering : Phase.Presenting;
            _view.ReplayEnterEffect(_profile.ResolvedEnterDuration);
            return true;
        }

        /// <summary>从首帧、首句和入场特效开始重播当前演出。</summary>
        public bool ReplayFullTuningPreview()
        {
            if (!RefreshTuningPreview())
                return false;

            _entranceEffectPending = false;
            _phaseElapsed = 0f;
            _sequenceElapsed = 0f;
            _lineElapsed = 0f;
            _audioWaitElapsed = 0f;
            _voiceWaitTimedOut = false;
            _frameIndex = 0;
            _lineIndex = 0;
            _dialogueComplete = false;
            _closing = false;
            _view.PrepareFullReplay();
            ApplyFrame(0);
            RequestAudio(_profile.openingAudioCueId);
            ApplyLine(0);

            bool hasEntrance = _profile.ResolvedEnterDuration > 0f;
            _view.SetEntrance(
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0.965f : 1f);
            _phase = hasEntrance ? Phase.Entering : Phase.Presenting;
            _view.ReplayEnterEffect(_profile.ResolvedEnterDuration);
            return true;
        }

        public void Show(
            Canvas canvas,
            CharacterPresentationProfile profile,
            CharacterPresentationVariant variant,
            Action onClosed,
            Action<string> onAudioCueRequested = null,
            Func<string, bool> isAudioCueActive = null,
            Action<CharacterPresentationExitSnapshot> onExitStarted = null)
        {
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (!profile.TryValidate(out string error))
                throw new InvalidOperationException(error);
            if (profile.HasVariants && variant == null)
            {
                throw new InvalidOperationException(
                    $"{profile.contentId} 必须明确选择内容变体。");
            }
            if (!profile.HasVariants && variant != null)
            {
                throw new InvalidOperationException(
                    $"{profile.contentId} 不接受内容变体 {variant.variantId}。");
            }
            if (variant != null)
            {
                if (!profile.TryGetVariant(
                        variant.variantId,
                        out CharacterPresentationVariant configuredVariant))
                {
                    throw new InvalidOperationException(
                        $"{profile.contentId} 不包含内容变体 {variant.variantId}。");
                }
                variant = configuredVariant;
            }

            CloseSilently();
            _profile = profile;
            _variant = variant;
            _lines = variant != null ? variant.lines : profile.lines;
            _onClosed = onClosed;
            _onExitStarted = onExitStarted;
            _onAudioCueRequested = onAudioCueRequested;
            _isAudioCueActive = isAudioCueActive;
            _activeCueId = null;
            _phaseElapsed = 0f;
            _sequenceElapsed = 0f;
            _lineElapsed = 0f;
            _audioWaitElapsed = 0f;
            _voiceWaitTimedOut = false;
            _frameIndex = 0;
            _lineIndex = 0;
            _dialogueComplete = false;
            _closing = false;
            _entranceEffectPending =
                profile.ResolvedEffectPreset ==
                CharacterPresentationEffectPreset.ScrollSpiritMaterialize;
            _entranceEffectStartFrame = Time.frameCount + 1;

            _view = new CharacterPresentationView(
                canvas, profile, variant, OnDismissClicked);
            _view.SetInputBlocking(profile.BlocksUnderlyingInteraction);
            ApplyFrame(0);
            RequestAudio(profile.openingAudioCueId);
            ApplyLine(0);

            if (profile.hintTiming ==
                CharacterPresentationHintTiming.WithAnimation)
            {
                // 动画开始即显示提示；生效仍由 phase 与语音门控兜底。
                _view.SetAcknowledgementReady(true);
            }

            bool hasEntrance = profile.ResolvedEnterDuration > 0f;
            _view.SetEntrance(
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0f : 1f,
                hasEntrance ? 0.965f : 1f);
            _phase = hasEntrance ? Phase.Entering : Phase.Presenting;
        }

        private void Update()
        {
            if (_view == null || _profile == null || _phase == Phase.Closed)
                return;

            // 直接 F5 进入地图时，Canvas 与演出同帧创建。等待完整一帧后
            // 再按最终 RectTransform 建粒子，保证首次入场与 Inspector/F9
            // 重播使用相同的有效布局。
            if (_entranceEffectPending)
            {
                if (Time.frameCount < _entranceEffectStartFrame)
                    return;
                _entranceEffectPending = false;
                _view.RefreshTunableVisuals();
                _view.PlayEnterEffect(_profile.ResolvedEnterDuration);
            }

            float deltaTime = Time.unscaledDeltaTime;
            _sequenceElapsed += deltaTime;
            UpdateFrame();

            switch (_phase)
            {
                case Phase.Entering:
                    UpdateEntrance(deltaTime);
                    break;
                case Phase.Presenting:
                    UpdatePresentation(deltaTime);
                    break;
                case Phase.FadingPanel:
                    UpdatePanelFade(deltaTime);
                    break;
                case Phase.Exiting:
                    UpdateExit(deltaTime);
                    break;
            }
        }

        private void UpdateEntrance(float deltaTime)
        {
            _phaseElapsed += deltaTime;
            float normalized = Mathf.Clamp01(
                _phaseElapsed /
                Mathf.Max(0.01f, _profile.ResolvedEnterDuration));
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float panelAlpha = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.22f, 1f, normalized));
            _view.SetEntrance(
                eased,
                panelAlpha,
                Mathf.Lerp(0.965f, 1f, eased));

            if (normalized < 1f)
                return;

            _view.SetEntrance(1f, 1f, 1f);
            _phaseElapsed = 0f;
            _phase = Phase.Presenting;
        }

        private void UpdatePresentation(float deltaTime)
        {
            if (!_dialogueComplete)
                AdvanceDialogue(deltaTime);
            if (!_dialogueComplete)
                return;

            // 台词节拍走完后仍要等最后一句语音播完，才允许
            // 自动退场或显示确认按钮，避免"继续"出现过早。
            if (IsAudioStillPlaying())
            {
                _audioWaitElapsed += deltaTime;
                float timeout = Mathf.Max(
                    0f, _profile.maxVoiceWaitAfterDialogue);
                if (_audioWaitElapsed < timeout)
                    return;

                // 同一句超时后，时间线与确认点击必须共用已放行状态。
                // 原生音频停滞时 isPlaying 可能一直为 true，不能再次锁住确认。
                _voiceWaitTimedOut = true;
                Debug.LogWarning(
                    $"[CharacterPresentation] 语音等待超过 {timeout:F1}s，" +
                    $"按字幕节拍继续收尾：{_activeCueId}");
            }

            if (_profile.completionMode ==
                CharacterPresentationCompletionMode.AutoDismiss)
            {
                BeginClosing();
                return;
            }

            if (!_profile.PersistsAfterAcknowledgement &&
                _sequenceElapsed < _profile.SequenceDuration)
                return;

            _phase = Phase.AwaitingAcknowledgement;
            _view.SetAcknowledgementReady(true);
        }

        private void AdvanceDialogue(float deltaTime)
        {
            _lineElapsed += deltaTime;
            CharacterPresentationLine line = _lines[_lineIndex];
            float visibleSeconds = Mathf.Max(0.2f, line.visibleSeconds);
            if (_lineElapsed < visibleSeconds)
                return;

            _lineElapsed -= visibleSeconds;
            if (_lineIndex + 1 < _lines.Count)
            {
                ApplyLine(_lineIndex + 1);
                return;
            }

            _dialogueComplete = true;
        }

        private void UpdatePanelFade(float deltaTime)
        {
            _phaseElapsed += deltaTime;
            float normalized = Mathf.Clamp01(
                _phaseElapsed /
                Mathf.Max(0.01f, _profile.ResolvedPanelFadeDuration));
            _view.SetPanelAlpha(1f - normalized);
            if (normalized < 1f)
                return;

            _view.HidePanel();
            _phaseElapsed = 0f;
            if (_profile.PersistsAfterAcknowledgement)
            {
                BeginPersisting();
                return;
            }

            _phase = Phase.Exiting;
            Action<CharacterPresentationExitSnapshot> exitStarted =
                _onExitStarted;
            _onExitStarted = null;
            exitStarted?.Invoke(
                _view.CaptureExitSnapshot());
            if (_profile.ShouldPlayExitMaterialization(_variant))
            {
                _view.PlayExitEffect(_profile.ResolvedExitDuration);
            }
        }

        private void BeginPersisting()
        {
            _closing = false;
            _phase = Phase.Persisting;
            _view.SetInputBlocking(false);

            Action callback = _onClosed;
            _onClosed = null;
            _onExitStarted = null;
            _onAudioCueRequested = null;
            _isAudioCueActive = null;
            _activeCueId = null;
            callback?.Invoke();
        }

        private void UpdateExit(float deltaTime)
        {
            _phaseElapsed += deltaTime;
            float normalized = Mathf.Clamp01(
                _phaseElapsed /
                Mathf.Max(0.01f, ResolveExitVisualDuration()));
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            _view.SetActor(
                1f - eased,
                Mathf.Lerp(1f, 0.975f, eased));
            if (normalized >= 1f)
                Finish();
        }

        private float ResolveExitVisualDuration()
        {
            float duration = _profile.ResolvedExitDuration;
            if (!_profile.ShouldPlayExitMaterialization(_variant))
                return duration;

            float speed = Mathf.Max(
                0.1f,
                _profile.ResolvedMaterializationSpeedScale);
            return Mathf.Max(0.05f, duration / speed);
        }

        private void UpdateFrame()
        {
            int rawFrame = Mathf.FloorToInt(
                _sequenceElapsed * _profile.ResolvedFramesPerSecond);
            int nextFrame = _profile.ResolvedPlaybackMode ==
                CharacterPresentationPlaybackMode.Loop
                    ? rawFrame % _profile.ResolvedFrameCount
                    : Mathf.Min(_profile.ResolvedFrameCount - 1, rawFrame);
            if (nextFrame != _frameIndex)
                ApplyFrame(nextFrame);
        }

        private void ApplyFrame(int frameIndex)
        {
            _frameIndex = Mathf.Clamp(
                frameIndex, 0, _profile.ResolvedFrameCount - 1);
            _view?.ApplyFrame(_profile.GetUvRect(_frameIndex));
        }

        private void ApplyLine(int lineIndex)
        {
            _lineIndex = Mathf.Clamp(lineIndex, 0, _lines.Count - 1);
            _lineElapsed = 0f;
            _audioWaitElapsed = 0f;
            _voiceWaitTimedOut = false;
            CharacterPresentationLine line = _lines[_lineIndex];
            _view?.ApplyLine(line);
            RequestAudio(line.audioCueId);
        }

        private void OnDismissClicked()
        {
            if (_phase != Phase.AwaitingAcknowledgement || _closing)
                return;
            if (IsAudioStillPlaying())
                return;
            BeginClosing();
        }

        /// <summary>
        /// 最近一次请求的语音未播完时，确认点击（消失／返回）暂不生效；
        /// 无语音配置、素材缺失、未接查询或本句已超时放行时视为不阻塞。
        /// </summary>
        private bool IsAudioStillPlaying()
        {
            return !_voiceWaitTimedOut &&
                   _isAudioCueActive != null &&
                   !string.IsNullOrWhiteSpace(_activeCueId) &&
                   _isAudioCueActive(_activeCueId);
        }

        private void BeginClosing()
        {
            if (_closing || _phase == Phase.FadingPanel ||
                _phase == Phase.Exiting || _phase == Phase.Closed)
                return;

            _closing = true;
            _view.SetAcknowledgementReady(false);
            _phaseElapsed = 0f;
            _phase = Phase.FadingPanel;
        }

        private void Finish()
        {
            Action callback = _onClosed;
            CloseSilently();
            callback?.Invoke();
        }

        private void RequestAudio(string cueId)
        {
            if (string.IsNullOrWhiteSpace(cueId))
                return;
            _activeCueId = cueId;
            _onAudioCueRequested?.Invoke(cueId);
        }

        public void CloseSilently()
        {
            _closing = true;
            _phase = Phase.Closed;
            _view?.Destroy();
            _view = null;
            _profile = null;
            _variant = null;
            _lines = null;
            _onClosed = null;
            _onAudioCueRequested = null;
            _isAudioCueActive = null;
            _activeCueId = null;
            _dialogueComplete = false;
            _audioWaitElapsed = 0f;
            _voiceWaitTimedOut = false;
            _entranceEffectPending = false;
            _entranceEffectStartFrame = 0;
        }

        private void OnDestroy()
        {
            CloseSilently();
        }
    }
}
