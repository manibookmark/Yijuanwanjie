using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WanjieScroll.UI.Presentations
{
    /// <summary>
    /// Immutable screen-space handoff captured when a character begins leaving.
    /// Scene orchestration can align a follow-up effect without receiving the
    /// presentation's mutable RectTransform or depending on its hierarchy.
    /// </summary>
    public readonly struct CharacterPresentationExitSnapshot
    {
        public CharacterPresentationExitSnapshot(Rect subjectScreenRect)
        {
            SubjectScreenRect = subjectScreenRect;
        }

        public Rect SubjectScreenRect { get; }
    }

    /// <summary>
    /// 角色演出视图构建与局部视觉操作。它隐藏具体层级、字体、面板尺寸和粒子组件，
    /// 不持有演出计时、台词推进或完成策略。
    /// </summary>
    internal sealed class CharacterPresentationView
    {
        private readonly CharacterPresentationProfile _profile;
        private readonly CharacterPresentationVariant _variant;
        private readonly Canvas _canvas;
        private readonly UnityAction _onDismissClicked;
        private readonly RectTransform _rootRect;
        private readonly CanvasGroup _rootGroup;
        private readonly Image _backdrop;
        private readonly RawImage _actor;
        private readonly RectTransform _actorRect;
        private readonly CanvasGroup _actorGroup;
        private readonly RectTransform _panelRect;
        private readonly CanvasGroup _panelGroup;
        private readonly Text _speaker;
        private readonly Text _dialogue;
        private readonly Text _dismissHint;
        private readonly Button _dismissButton;
        private CharacterMaterializationCanvasEffect _materialization;

        public GameObject Root { get; }

        public CharacterPresentationView(
            Canvas canvas,
            CharacterPresentationProfile profile,
            CharacterPresentationVariant variant,
            UnityAction onDismissClicked)
        {
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            _canvas = canvas;
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _variant = variant;
            _onDismissClicked = onDismissClicked;

            Root = new GameObject(
                "CharacterPresentationOverlay",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(CanvasGroup));
            Root.transform.SetParent(canvas.transform, false);
            _rootRect = Root.GetComponent<RectTransform>();
            Stretch(_rootRect);

            _rootGroup = Root.GetComponent<CanvasGroup>();
            _rootGroup.alpha = 1f;
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = false;

            _backdrop = Root.GetComponent<Image>();
            _backdrop.color = profile.ResolvedBackdropColor;
            _backdrop.raycastTarget = true;

            _dismissButton = Root.GetComponent<Button>();
            _dismissButton.transition = Selectable.Transition.None;
            _dismissButton.interactable = false;
            if (_onDismissClicked != null)
                _dismissButton.onClick.AddListener(_onDismissClicked);

            (_actor, _actorRect, _actorGroup, _materialization) =
                BuildActor();
            (_panelRect, _panelGroup, _speaker, _dialogue, _dismissHint) =
                BuildDialoguePanel();
        }

        /// <summary>
        /// 将 Profile 中可调的视觉参数重新应用到现有运行时视图。
        /// 编辑器调参只表达“刷新配置”，不直接接触内部 RectTransform。
        /// </summary>
        public void RefreshTunableVisuals()
        {
            if (_backdrop != null)
                _backdrop.color = _profile.ResolvedBackdropColor;
            if (_actor != null)
                _actor.texture = _profile.ResolvedFrameAtlas;
            if (_speaker != null)
                _speaker.fontSize = _profile.ResolvedSpeakerFontSize;
            if (_dialogue != null)
                _dialogue.fontSize = _profile.ResolvedDialogueFontSize;
            if (_dismissHint != null)
                _dismissHint.fontSize = _profile.ResolvedDismissHintFontSize;
            ApplyActorLayout(_actorRect);
            ApplyPanelLayout(_panelRect);
        }

        /// <summary>按当前 Profile 重建并重播卷灵显化特效。</summary>
        public void ReplayEnterEffect(float duration)
        {
            EnsureMaterialization(rebuild: true);
            _materialization?.PlayEnter(ApplyParticleSpeed(duration));
        }

        public void PrepareFullReplay()
        {
            if (_panelGroup != null)
            {
                _panelGroup.gameObject.SetActive(true);
                _panelGroup.alpha = 0f;
            }
            SetAcknowledgementReady(false);
        }

        public void ApplyFrame(Rect uvRect)
        {
            if (_actor != null)
                _actor.uvRect = uvRect;
        }

        public void ApplyLine(CharacterPresentationLine line)
        {
            if (line == null)
                return;
            bool showSpeaker = false;
            if (_speaker != null)
            {
                string speakerText = string.IsNullOrWhiteSpace(line.speakerName)
                    ? _profile.displayTitle ?? string.Empty
                    : line.speakerName;
                showSpeaker = !string.IsNullOrEmpty(speakerText);
                _speaker.text = speakerText;
                _speaker.gameObject.SetActive(showSpeaker);
            }
            if (_dialogue != null)
            {
                _dialogue.text = line.text ?? string.Empty;
                // 英灵面板没有动态 speaker（用静态 Identity 标题），
                // 只有卷灵伴随面板才按是否显示说话人切换对齐。
                if (_profile.ResolvedPanelStyle ==
                    CharacterPresentationPanelStyle.HeroArrival)
                {
                    _dialogue.alignment = TextAnchor.MiddleCenter;
                    SetStretch(_dialogue.rectTransform, 34f, 52f, 34f, 58f);
                }
                else if (showSpeaker)
                {
                    _dialogue.alignment = TextAnchor.MiddleCenter;
                    SetStretch(_dialogue.rectTransform, 30f, 40f, 30f, 50f);
                }
                else
                {
                    _dialogue.alignment = TextAnchor.MiddleCenter;
                    SetStretch(_dialogue.rectTransform, 30f, 62f, 30f, 62f);
                }
            }
        }

        public void SetEntrance(float actorAlpha, float panelAlpha, float scale)
        {
            _actorGroup.alpha = actorAlpha;
            _panelGroup.alpha = panelAlpha;
            _actorRect.localScale = Vector3.one * scale;
        }

        public void SetPanelAlpha(float alpha)
        {
            _panelGroup.alpha = alpha;
        }

        public void HidePanel()
        {
            _panelGroup.alpha = 0f;
            _panelGroup.gameObject.SetActive(false);
        }

        public void SetActor(float alpha, float scale)
        {
            _actorGroup.alpha = alpha;
            _actorRect.localScale = Vector3.one * scale;
        }

        public CharacterPresentationExitSnapshot CaptureExitSnapshot()
        {
            var worldCorners = new Vector3[4];
            _actorRect.GetWorldCorners(worldCorners);
            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
            Vector2 minimum = RectTransformUtility.WorldToScreenPoint(
                camera, worldCorners[0]);
            Vector2 maximum = minimum;
            for (int index = 1; index < worldCorners.Length; index++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(
                    camera, worldCorners[index]);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }

            return new CharacterPresentationExitSnapshot(
                Rect.MinMaxRect(
                    minimum.x,
                    minimum.y,
                    maximum.x,
                    maximum.y));
        }

        public void SetAcknowledgementReady(bool ready)
        {
            _dismissButton.interactable = ready;
            if (_dismissHint != null)
                _dismissHint.gameObject.SetActive(ready);
            // PassiveOverlay 期间背景不挡射线（奖励窗仍可点）；确认就绪后
            // 恢复射线接收，让“点击继续”在全屏任意位置生效。Modal 本就
            // 一直接收，不受影响。关闭过程不再收回，退场动画很短。
            if (ready && _profile.BlocksUnderlyingInteraction &&
                _backdrop != null)
                _backdrop.raycastTarget = true;
        }

        public void SetInputBlocking(bool blocking)
        {
            // 被动遗物讲解必须整层穿透，而不是只依赖每个子 Graphic
            // 各自关闭 raycastTarget；否则后续新增文字或特效很容易再次
            // 抢走奖励本体的 Hover 与点击。
            if (_rootGroup != null)
            {
                _rootGroup.blocksRaycasts = blocking;
                _rootGroup.interactable = blocking;
            }
            if (_backdrop != null)
                _backdrop.raycastTarget = blocking;
            if (!blocking && _dismissButton != null)
                _dismissButton.interactable = false;
        }

        public void PlayEnterEffect(float duration)
        {
            // 首次播放也按 Canvas 完成布局后的 Actor Rect 重建，避免 F5
            // 同帧创建地图与演出时沿用尚未就绪的坐标。
            EnsureMaterialization(rebuild: true);
            _materialization?.PlayEnter(ApplyParticleSpeed(duration));
        }

        public void PlayExitEffect(float duration)
        {
            _materialization?.PlayExit(ApplyParticleSpeed(duration));
        }

        private float ApplyParticleSpeed(float duration)
        {
            float speed = Mathf.Max(
                0.1f, _profile.ResolvedMaterializationSpeedScale);
            return duration / speed;
        }

        public void Destroy()
        {
            if (_dismissButton != null && _onDismissClicked != null)
                _dismissButton.onClick.RemoveListener(_onDismissClicked);
            if (Root != null)
                UnityEngine.Object.Destroy(Root);
        }

        private (RawImage actor, RectTransform rect, CanvasGroup group,
            CharacterMaterializationCanvasEffect materialization)
            BuildActor()
        {
            var actorObject = new GameObject(
                "ActorSequence",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(CanvasGroup));
            actorObject.transform.SetParent(Root.transform, false);
            RawImage actor = actorObject.GetComponent<RawImage>();
            actor.texture = _profile.ResolvedFrameAtlas;
            actor.color = Color.white;
            actor.raycastTarget = false;

            CanvasGroup group = actorObject.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform rect = actorObject.GetComponent<RectTransform>();
            ApplyActorLayout(rect);

            CharacterMaterializationCanvasEffect materialization = null;
            if (_profile.ResolvedEffectPreset ==
                CharacterPresentationEffectPreset.ScrollSpiritMaterialize)
            {
                materialization = actorObject.AddComponent<
                    CharacterMaterializationCanvasEffect>();
            }

            return (actor, rect, group, materialization);
        }

        private (RectTransform rect, CanvasGroup group, Text speaker,
            Text dialogue, Text hint)
            BuildDialoguePanel()
        {
            bool compact = _profile.ResolvedPanelStyle ==
                CharacterPresentationPanelStyle.CompactCompanion;
            var panelObject = new GameObject(
                compact ? "CompanionDialoguePanel" : "HeroArrivalPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(Root.transform, false);

            CanvasGroup group = panelObject.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            ApplyPanelLayout(panelRect);

            Image panel = panelObject.GetComponent<Image>();
            panel.color = compact
                ? new Color(0.045f, 0.055f, 0.045f, 0.91f)
                : new Color(0.045f, 0.031f, 0.02f, 0.94f);
            panel.raycastTarget = false;
            Outline outline = panelObject.GetComponent<Outline>();
            outline.effectColor = compact
                ? new Color(0.55f, 0.82f, 0.64f, 0.32f)
                : new Color(0.91f, 0.66f, 0.25f, 0.42f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            BuildAccent(panelObject.transform, compact);
            (CanvasGroup panelGroup, Text speaker, Text dialogue, Text hint) =
                compact
                    ? BuildCompanionText(panelObject.transform, group)
                    : BuildHeroText(panelObject.transform, group);
            return (panelRect, panelGroup, speaker, dialogue, hint);
        }

        private void ApplyActorLayout(RectTransform rect)
        {
            if (rect == null)
                return;

            float anchorX = ResolveActorAnchorX(_profile.ResolvedPlacement);
            rect.anchorMin = new Vector2(anchorX, 0.54f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(
                _profile.ResolvedDisplayHeight * _profile.FrameAspect,
                _profile.ResolvedDisplayHeight);
            rect.anchoredPosition = _profile.ResolvedAnchoredOffset;
        }

        private void ApplyPanelLayout(RectTransform panelRect)
        {
            if (panelRect == null)
                return;

            if (_profile.ResolvedPanelStyle ==
                CharacterPresentationPanelStyle.CompactCompanion)
            {
                ApplyCompanionPanelLayout(panelRect);
            }
            else
            {
                ApplyHeroPanelLayout(panelRect);
            }
        }

        private void ApplyCompanionPanelLayout(RectTransform panelRect)
        {
            float anchorX = _profile.ResolvedPlacement switch
            {
                CharacterPresentationPlacement.Right => 0.47f,
                CharacterPresentationPlacement.Center => 0.72f,
                _ => 0.46f
            };
            float anchorY = _profile.ResolvedPlacement switch
            {
                CharacterPresentationPlacement.Right => 0.51f,
                CharacterPresentationPlacement.Center => 0.51f,
                _ => 0.66f
            };
            panelRect.anchorMin = new Vector2(anchorX, anchorY);
            panelRect.anchorMax = panelRect.anchorMin;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = ResolveDialoguePanelSize();
            panelRect.anchoredPosition =
                _profile.ResolvedDialoguePanelOffset;
        }

        private void ApplyHeroPanelLayout(RectTransform panelRect)
        {
            panelRect.anchorMin = new Vector2(0.5f, 0.16f);
            panelRect.anchorMax = panelRect.anchorMin;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = ResolveDialoguePanelSize();
            panelRect.anchoredPosition =
                _profile.ResolvedDialoguePanelOffset;
        }

        private Vector2 ResolveDialoguePanelSize()
        {
            return _variant != null
                ? _variant.ResolveDialoguePanelSize(
                    _profile.ResolvedDialoguePanelSize)
                : _profile.ResolvedDialoguePanelSize;
        }

        private void EnsureMaterialization(bool rebuild)
        {
            if (_profile.ResolvedEffectPreset !=
                CharacterPresentationEffectPreset.ScrollSpiritMaterialize)
            {
                if (_materialization != null)
                {
                    _materialization.Clear();
                    UnityEngine.Object.Destroy(_materialization);
                    _materialization = null;
                }
                return;
            }

            if (_materialization == null)
            {
                _materialization = _actor.gameObject.AddComponent<
                    CharacterMaterializationCanvasEffect>();
                rebuild = true;
            }

            if (rebuild)
            {
                _materialization.Build(
                    _rootRect,
                    _actorRect,
                    _profile.ResolvedMaterializationParticleCount,
                    _profile.ResolvedMaterializationSizeScale);
            }
        }

        private static void BuildAccent(Transform parent, bool compact)
        {
            var accentObject = new GameObject(
                "Accent", typeof(RectTransform), typeof(Image));
            accentObject.transform.SetParent(parent, false);
            RectTransform rect = accentObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(4f, 0f);
            rect.anchoredPosition = Vector2.zero;
            Image image = accentObject.GetComponent<Image>();
            image.color = compact
                ? new Color(0.55f, 0.88f, 0.66f, 0.82f)
                : new Color(1f, 0.72f, 0.27f, 0.9f);
            image.raycastTarget = false;
        }

        private (CanvasGroup, Text, Text, Text) BuildCompanionText(
            Transform panel,
            CanvasGroup group)
        {
            Text speaker = CreateText(
                panel, "Speaker", string.Empty,
                _profile.ResolvedSpeakerFontSize,
                new Color(0.68f, 0.91f, 0.73f, 1f),
                TextAnchor.MiddleLeft);
            SetStretch(speaker.rectTransform, 30f, 98f, 30f, 14f);

            Text dialogue = CreateText(
                panel, "Dialogue", string.Empty,
                _profile.ResolvedDialogueFontSize,
                new Color(0.96f, 0.94f, 0.86f, 1f),
                TextAnchor.MiddleCenter);
            SetStretch(dialogue.rectTransform, 30f, 40f, 30f, 50f);

            Text hint = CreateText(
                panel, "DismissHint", _profile.dismissHint,
                _profile.ResolvedDismissHintFontSize,
                new Color(0.72f, 0.78f, 0.68f, 0.9f),
                TextAnchor.MiddleCenter);
            SetStretch(hint.rectTransform, 30f, 8f, 30f, 116f);
            hint.gameObject.SetActive(false);
            return (group, speaker, dialogue, hint);
        }

        private (CanvasGroup, Text, Text, Text) BuildHeroText(
            Transform panel,
            CanvasGroup group)
        {
            Text title = CreateText(
                panel, "Identity", _profile.displayTitle, 34,
                new Color(1f, 0.88f, 0.55f, 1f),
                TextAnchor.MiddleCenter);
            SetStretch(title.rectTransform, 34f, 126f, 34f, 14f);

            Text dialogue = CreateText(
                panel, "Dialogue", string.Empty,
                _profile.ResolvedDialogueFontSize,
                new Color(0.97f, 0.94f, 0.86f, 1f),
                TextAnchor.MiddleCenter);
            SetStretch(dialogue.rectTransform, 34f, 52f, 34f, 58f);

            Text hint = CreateText(
                panel, "DismissHint", _profile.dismissHint,
                _profile.ResolvedDismissHintFontSize,
                new Color(0.79f, 0.73f, 0.62f, 0.92f),
                TextAnchor.MiddleCenter);
            SetStretch(hint.rectTransform, 34f, 10f, 34f, 146f);
            hint.gameObject.SetActive(false);
            return (group, null, dialogue, hint);
        }

        private static float ResolveActorAnchorX(
            CharacterPresentationPlacement placement)
        {
            return placement switch
            {
                CharacterPresentationPlacement.Left => 0.28f,
                CharacterPresentationPlacement.Right => 0.72f,
                _ => 0.5f
            };
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            var textObject = new GameObject(
                name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy") ??
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = value ?? string.Empty;
            return text;
        }

        private static void SetStretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
