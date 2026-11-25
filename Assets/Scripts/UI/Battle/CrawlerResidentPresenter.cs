using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 驻场英灵透明序列的唯一运行时所有者：监听权威英灵列表，管理最多三层
    /// 纵深槽位、循环帧与可见性。规则层不等待动画完成，也不读取本组件状态。
    ///
    /// 模块审查：本类达到 500 行关注阈值，但仍只维护“权威在场列表映射为三个
    /// 驻灵槽生命周期”这一知识边界；角色帧、凝神数字、入退场都是同一槽状态。
    /// 首帧碎片算法已由 CrawlerResidentMaterializationCanvasEffect 完整隐藏。
    /// 触发预览与短促光效由 CrawlerResidentFeedbackView 独立拥有；本类仅按
    /// 运行时英灵 ID 把展示意图路由到正确槽位。
    /// 2026-10-13 审查：本次仅调整槽位为后灵气/人物/前灵气结构，位置、
    /// 缩放和入退场仍归槽位唯一管理；指针命中与说明生命周期交给独立 HoverView。
    /// 触发条件变化只刷新驻场绑定，条件由 HoverView 按需显示；不再创建常驻文字标记。
    /// 法器显形沿共享表现流程，不保存回执历史，也不复制触发规则。
    /// 下次新增另一类独立驻场动画或规则缓存前重新审查，不能继续扩展本类职责。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrawlerResidentPresenter : MonoBehaviour
    {
        private const int MaxVisibleSlots = 3;
        private const float DurationPulseDuration = 0.24f;
        private static readonly Color DurationBaseColor =
            new Color(1f, 0.88f, 0.48f, 1f);
        private static readonly Color DurationIncreaseColor =
            new Color(0.62f, 1f, 0.58f, 1f);
        private static readonly Color DurationDecreaseColor =
            new Color(1f, 0.42f, 0.24f, 1f);

        private sealed class ResidentSlot
        {
            public RectTransform Rect;
            public RawImage Actor;
            public Text DurationText;
            public CrawlerDurationGlyphTransfer DurationTransfer;
            public CrawlerResidentFeedbackView Feedback;
            public CrawlerResidentHoverView Hover;
            public CrawlerResidentMaterializationCanvasEffect Materialization;
            public CrawlerSandPresenceGraphic SandPresence;
            public ColorTriggerSystem.ActiveCrawler Crawler;
            public CrawlerResidentPresentationProfile Profile;
            public Vector2 BasePosition;
            public float Elapsed;
            public int FrameIndex = -1;
            public float EntryElapsed;
            public float EntryDuration;
            public bool Entering;
            public float ExitElapsed;
            public bool Exiting;
            public bool PreviewExit;
            public bool PreviewHidden;
            public int DisplayedDuration = -1;
            public float DurationPulseElapsed = DurationPulseDuration;
            public int DurationPulseDirection;
            public int? DurationPreview;
            public bool DurationGainPending;
        }

        private readonly List<ResidentSlot> _slots =
            new List<ResidentSlot>(MaxVisibleSlots);
        private CombatController _combat;
        private CrawlerResidentLayoutProfile _layout;
        private Canvas _tooltipCanvas;

        public int VisibleCount { get; private set; }

        /// <summary>
        /// 新权威驻场实例开始显化时请求一次召唤语音。调用方只获得 cue，
        /// 音频模块接线由 Bootstrap 负责；初始绑定与 F7 重播不会发布。
        /// </summary>
        public event System.Action<string> SummonVoiceRequested;

        public static CrawlerResidentPresenter Create(
            Transform parent,
            CrawlerResidentLayoutProfile layout = null)
        {
            var root = new GameObject(
                "CrawlerResidentPresenter",
                typeof(RectTransform),
                typeof(CrawlerResidentPresenter));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            root.transform.SetAsFirstSibling();
            CrawlerResidentPresenter presenter =
                root.GetComponent<CrawlerResidentPresenter>();
            presenter._layout = layout != null
                ? layout
                : CrawlerResidentLayoutProfile.LoadDefault();
            // The caller supplies a stable battle HUD parent; never search unrelated Canvases.
            presenter._tooltipCanvas = parent.GetComponentInParent<Canvas>();
            if (!presenter._layout.TryValidate(out string error))
                throw new System.InvalidOperationException(error);
            return presenter;
        }

        /// <summary>
        /// 提前加载本场可能用到的英灵表现资源，但不创建在场实例，也不改变可见性。
        /// 真正显示仍只服从 CombatController 的 ActiveCrawlers。
        /// </summary>
        public void Prepare(IEnumerable<string> crawlerContentIds)
        {
            if (crawlerContentIds == null)
                return;

            var prepared = new HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (string contentId in crawlerContentIds)
            {
                if (string.IsNullOrWhiteSpace(contentId) ||
                    !prepared.Add(contentId))
                {
                    continue;
                }

                if (!CrawlerResidentPresentationCatalog.TryGet(
                        contentId,
                        out _))
                {
                    Debug.LogWarning(
                        $"[CrawlerResident] 未配置英灵驻场表现：{contentId}。");
                }
            }
        }

        public void Bind(CombatController combat)
        {
            if (_combat == combat)
            {
                RefreshResidents(false);
                return;
            }

            Unbind();
            _combat = combat;
            if (_combat != null)
                _combat.OnStateChanged += OnCombatStateChanged;
            RefreshResidents(false);
        }

        /// <summary>
        /// Clears every slot, including an in-progress exit, without publishing
        /// summon or duration receipts into the next battle using this view.
        /// </summary>
        public void ReleaseEncounter()
        {
            Unbind();
            for (int i = 0; i < _slots.Count; i++)
            {
                // Cancel may complete an old UI transfer synchronously. Clear
                // identity first so its runtime-id guard cannot touch the slot.
                _slots[i].Crawler = null;
                HideSlot(_slots[i]);
            }
            VisibleCount = 0;
        }

        /// <summary>
        /// 把共享布局中的凝神与 Hover 灵气形状，以及逐英灵的灵气偏移，
        /// 立即应用到已创建槽位。供 F7 Play 模式调参使用；不改变英灵
        /// 规则状态或显隐生命周期。
        /// </summary>
        public void RefreshTunableLayout()
        {
            if (_layout == null)
                return;

            for (int i = 0; i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (slot.DurationText != null)
                {
                    slot.DurationText.fontSize = _layout.durationFontSize;
                    slot.DurationText.rectTransform.anchoredPosition =
                        _layout.durationOffset;
                }
                slot.Feedback?.ApplyAuraSettings(
                    _layout.GetHoverAuraSettings(
                        slot.Profile != null
                            ? slot.Profile.hoverAuraOffset
                            : Vector2.zero,
                        slot.Profile != null ? slot.Profile.residentBodyBounds : (Rect?)null));
                if (slot.Crawler != null && slot.Profile != null)
                    slot.Hover?.Bind(slot.Crawler, slot.Profile.residentBodyBounds, _tooltipCanvas);
            }
        }

        /// <summary>按内容 ID 开关 F7 常亮灵气预览，不修改规则或卡牌 Hover 状态。</summary>
        public bool SetTuningAuraPreview(
            string crawlerContentId,
            bool visible,
            string effectText = "数量 +1")
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return false;

            bool changed = false;
            for (int i = 0; i < VisibleCount && i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (slot.Crawler == null ||
                    !string.Equals(
                        slot.Crawler.config?.contentId,
                        crawlerContentId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                slot.Feedback?.SetTuningPreview(visible, effectText);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// 只重播指定在场英灵的显化表现，不重复召唤、不增加属性、
        /// 不修改凝神。供 F7 实时调参使用。
        /// </summary>
        public bool TryReplayEntry(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return false;

            for (int i = 0; i < VisibleCount && i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (slot.Crawler == null ||
                    !string.Equals(
                        slot.Crawler.config?.contentId,
                        crawlerContentId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BeginEntry(slot);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 只预览指定在场英灵的退场表现。完成后保留权威英灵实例与凝神，
        /// 画面保持隐藏，直到 F7 重播入场或规则状态发生实际退场。
        /// </summary>
        public bool TryReplayExit(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return false;

            for (int i = 0; i < VisibleCount && i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (slot.Crawler == null || slot.PreviewHidden ||
                    !string.Equals(
                        slot.Crawler.config?.contentId,
                        crawlerContentId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BeginExit(slot, previewOnly: true);
                return true;
            }
            return false;
        }

        /// <summary>清除所有卡牌 Hover 触发预览，不中断已开始的实际触发脉冲。</summary>
        public void ClearTriggerPreviews()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Feedback?.HidePreview();
                _slots[i].DurationPreview = null;
                RefreshDurationLabel(_slots[i]);
                ApplyDurationStyle(_slots[i], _slots[i].Actor.color.a);
            }
        }

        /// <summary>Preview only the matching resident's number. The global
        /// Duration icon never includes resident base or remaining counts.</summary>
        public bool ShowDurationPreview(int crawlerRuntimeId, int remaining)
        {
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            if (slot == null || slot.Exiting || slot.DurationGainPending) return false;
            slot.DurationPreview = Mathf.Clamp(remaining, 0, ColorTriggerSystem.MaxRemainingTriggers);
            RefreshDurationLabel(slot);
            ApplyDurationStyle(slot, slot.Actor.color.a);
            return true;
        }

        /// <summary>Prepare authoritative per-resident receipts before state
        /// refresh. Each flight commits that slot's latest actual number.</summary>
        public System.Action<Vector2> PrepareDurationGains(IReadOnlyList<CrawlerDurationChange> changes)
        {
            var arrivals = new List<System.Action<Vector2>>();
            if (changes != null)
                foreach (CrawlerDurationChange change in changes)
                {
                    if (change.Amount <= 0) continue;
                    ResidentSlot slot = FindVisibleSlot(change.CrawlerRuntimeId);
                    if (slot == null || slot.Exiting) continue;
                    int runtimeId = change.CrawlerRuntimeId;
                    System.Action<Vector2> arrival = slot.DurationTransfer.Prepare(() =>
                    {
                        if (this == null || slot.Crawler?.RuntimeId != runtimeId) return;
                        slot.DurationGainPending = false;
                        RefreshDuration(slot, animate: true);
                        PulseDurationGain(runtimeId);
                    });
                    slot.DurationPreview = null;
                    slot.DurationGainPending = true;
                    RefreshDurationLabel(slot);
                    arrivals.Add(arrival);
                }
            return screen =>
            {
                foreach (System.Action<Vector2> arrival in arrivals)
                {
                    try { arrival(screen); }
                    catch (System.Exception exception) { Debug.LogException(exception, this); }
                }
            };
        }

        /// <summary>按运行时英灵 ID 显示本张牌会提供的驻场效果。</summary>
        public bool ShowTriggerPreview(
            int crawlerRuntimeId,
            string effectText)
        {
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            if (slot == null)
                return false;
            slot.Feedback?.ShowPreview(effectText);
            return true;
        }

        /// <summary>
        /// 播放权威触发反馈，并返回增益图标飞行起点和该英灵配置的语音 cue。
        /// </summary>
        public bool TryPlayTriggerFeedback(
            int crawlerRuntimeId,
            string effectText,
            out RectTransform effectSource,
            out string voiceCue)
        {
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            effectSource = slot?.Feedback?.EffectSource;
            voiceCue = slot?.Profile?.triggerVoiceCue;
            if (slot == null)
                return false;
            slot.Feedback?.PlayTrigger(effectText);
            return true;
        }

        /// <summary>按权威实例查询可见英灵胸口位置，只返回不可变屏幕坐标。</summary>
        public bool TryGetResidentScreenPosition(int crawlerRuntimeId, out Vector2 position)
        {
            position = default;
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            if (slot?.Actor == null || !slot.Rect.gameObject.activeInHierarchy || slot.Exiting)
                return false;
            RectTransform rect = slot.Actor.rectTransform;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            Vector2 local = rect.rect.center + Vector2.up * rect.rect.height * 0.08f;
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local));
            return true;
        }

        /// <summary>Immutable position of the actual Duration label. A source
        /// that just left may still have its exit slot, but is not a recipient.</summary>
        public bool TryGetDurationScreenPosition(int crawlerRuntimeId, out Vector2 position)
        {
            position = default;
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            if (slot?.DurationText == null || slot.Exiting || !slot.Rect.gameObject.activeInHierarchy) return false;
            RectTransform rect = slot.DurationText.rectTransform;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            return true;
        }

        public void PulseDurationGain(int crawlerRuntimeId)
        {
            ResidentSlot slot = FindVisibleSlot(crawlerRuntimeId);
            if (slot == null || slot.Exiting) return;
            // A consume-then-gain can leave the same final number. Receipt
            // feedback still lights it without inventing a numeric increment.
            slot.DurationPulseDirection = 1;
            slot.DurationPulseElapsed = 0f;
            ApplyDurationStyle(slot, slot.Actor.color.a);
        }

        private ResidentSlot FindVisibleSlot(int crawlerRuntimeId)
        {
            if (crawlerRuntimeId <= 0)
                return null;
            for (int i = 0; i < VisibleCount && i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (slot.Crawler?.RuntimeId == crawlerRuntimeId &&
                    !slot.PreviewHidden)
                {
                    return slot;
                }
            }
            return null;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (!slot.Rect.gameObject.activeSelf)
                    continue;
                if (slot.PreviewHidden)
                    continue;
                UpdateDurationPulse(slot, Time.unscaledDeltaTime);
                if (slot.Exiting)
                {
                    UpdateExit(slot, Time.unscaledDeltaTime);
                    continue;
                }
                if (i >= VisibleCount)
                    continue;

                CrawlerResidentPresentationProfile profile = slot.Profile;
                if (profile == null || profile.frameCount <= 0)
                    continue;

                if (slot.Entering)
                {
                    slot.Elapsed += deltaTime;
                    ApplyFrame(slot, profile.GetLoopFrame(slot.Elapsed));
                    UpdateEntry(slot, Time.unscaledDeltaTime);
                    continue;
                }

                slot.Elapsed += deltaTime;
                int frameIndex = profile.GetLoopFrame(slot.Elapsed);
                ApplyFrame(slot, frameIndex);
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (_combat != null)
                _combat.OnStateChanged -= OnCombatStateChanged;
            _combat = null;
        }

        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
            ClearTriggerPreviews();
            if (reason == CombatStateChangeReason.CrawlerChanged ||
                reason == CombatStateChangeReason.CombatEnded)
            {
                RefreshResidents(true);
            }
        }

        private void RefreshResidents(bool playNewEntries)
        {
            int visible = 0;
            var entering = new List<ResidentSlot>(MaxVisibleSlots);
            IReadOnlyList<ColorTriggerSystem.ActiveCrawler> active =
                _combat?.ColorTrigger?.ActiveCrawlers;
            if (active != null)
            {
                for (int i = 0; i < active.Count && visible < MaxVisibleSlots; i++)
                {
                    ColorTriggerSystem.ActiveCrawler crawler = active[i];
                    string contentId = crawler?.config?.contentId;
                    if (!CrawlerResidentPresentationCatalog.TryGet(
                            contentId,
                            out CrawlerResidentPresentationProfile profile))
                    {
                        continue;
                    }

                    ResidentSlot slot = TakeSlotForCrawler(visible, crawler);
                    bool changed = ConfigureSlot(
                        slot,
                        crawler,
                        profile,
                        _layout.GetSlot(visible),
                        _layout.GetHoverAuraSettings(
                            profile.hoverAuraOffset, profile.residentBodyBounds));
                    slot.Hover.Bind(crawler, profile.residentBodyBounds, _tooltipCanvas);
                    if (changed && playNewEntries)
                        entering.Add(slot);
                    else if (changed)
                        FinishEntry(slot);
                    visible++;
                }
            }

            VisibleCount = visible;
            for (int i = visible; i < _slots.Count; i++)
            {
                ResidentSlot slot = _slots[i];
                if (!slot.Rect.gameObject.activeSelf || slot.Exiting)
                    continue;
                RefreshDuration(slot, animate: true);
                if (slot.PreviewHidden)
                    HideSlot(slot);
                else if (playNewEntries && slot.Crawler != null)
                    BeginExit(slot);
                else
                    HideSlot(slot);
            }

            // Canvas 后绘制的同级元素位于上方：远槽先排，前槽最后排。
            for (int i = visible - 1; i >= 0; i--)
                _slots[i].Rect.SetAsLastSibling();

            for (int i = 0; i < entering.Count; i++)
            {
                ResidentSlot slot = entering[i];
                BeginEntry(slot);
                string voiceCue = slot.Profile?.summonVoiceCue;
                if (!string.IsNullOrWhiteSpace(voiceCue))
                    SummonVoiceRequested?.Invoke(voiceCue);
            }
        }

        private ResidentSlot TakeSlotForCrawler(
            int index,
            ColorTriggerSystem.ActiveCrawler crawler)
        {
            for (int i = index; i < _slots.Count; i++)
            {
                if (!ReferenceEquals(_slots[i].Crawler, crawler))
                    continue;

                if (i != index)
                {
                    ResidentSlot existing = _slots[index];
                    _slots[index] = _slots[i];
                    _slots[i] = existing;
                }
                return _slots[index];
            }

            return EnsureSlot(index);
        }

        private ResidentSlot EnsureSlot(int index)
        {
            while (_slots.Count <= index)
            {
                var slotObject = new GameObject(
                    $"ResidentSlot{_slots.Count + 1}",
                    typeof(RectTransform),
                    typeof(CrawlerResidentMaterializationCanvasEffect),
                    typeof(CrawlerResidentFeedbackView));
                slotObject.transform.SetParent(transform, false);
                var actorObject = new GameObject("Actor", typeof(RectTransform),
                    typeof(RawImage), typeof(CrawlerResidentHoverView));
                actorObject.transform.SetParent(slotObject.transform, false);
                RawImage actor = actorObject.GetComponent<RawImage>();
                actor.color = Color.white;
                actor.raycastTarget = true;
                actor.rectTransform.anchorMin = Vector2.zero;
                actor.rectTransform.anchorMax = Vector2.one;
                actor.rectTransform.offsetMin = actor.rectTransform.offsetMax = Vector2.zero;
                RectTransform rect = slotObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                Text durationText = UIElementFactory.CreateText(
                    "Duration",
                    slotObject.transform,
                    "0",
                    _layout.durationFontSize,
                    DurationBaseColor,
                    TextAnchor.LowerRight);
                durationText.horizontalOverflow =
                    HorizontalWrapMode.Overflow;
                durationText.verticalOverflow =
                    VerticalWrapMode.Overflow;
                RectTransform durationRect = durationText.rectTransform;
                durationRect.anchorMin = new Vector2(1f, 0f);
                durationRect.anchorMax = durationRect.anchorMin;
                durationRect.pivot = new Vector2(1f, 0f);
                durationRect.sizeDelta = new Vector2(76f, 52f);
                durationRect.anchoredPosition = _layout.durationOffset;
                Outline outline =
                    durationText.gameObject.AddComponent<Outline>();
                outline.effectColor =
                    new Color(0.08f, 0.045f, 0.02f, 0.94f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                outline.useGraphicAlpha = false;
                CrawlerResidentFeedbackView feedback =
                    slotObject.GetComponent<CrawlerResidentFeedbackView>();
                feedback.Initialize(actor);
                feedback.ApplyAuraSettings(
                    _layout.GetHoverAuraSettings(Vector2.zero));
                CrawlerDurationGlyphTransfer transfer = durationText.gameObject.AddComponent<CrawlerDurationGlyphTransfer>();
                transfer.Bind(durationRect);
                _slots.Add(new ResidentSlot
                {
                    Rect = rect,
                    Actor = actor,
                    DurationText = durationText,
                    DurationTransfer = transfer,
                    Feedback = feedback,
                    Hover = actorObject.GetComponent<CrawlerResidentHoverView>(),
                    Materialization = slotObject.GetComponent<
                        CrawlerResidentMaterializationCanvasEffect>()
                });
            }

            return _slots[index];
        }

        private static bool ConfigureSlot(
            ResidentSlot slot,
            ColorTriggerSystem.ActiveCrawler crawler,
            CrawlerResidentPresentationProfile profile,
            CrawlerResidentSlotLayout layout,
            CrawlerResidentAuraSettings auraSettings)
        {
            bool wasExiting = slot.Exiting;
            bool crawlerChanged = slot.Profile != profile ||
                !ReferenceEquals(slot.Crawler, crawler);
            bool changed = crawlerChanged ||
                wasExiting;
            if (crawlerChanged)
            {
                slot.DurationTransfer?.Cancel();
                slot.DurationGainPending = false;
                slot.DurationPreview = null;
            }
            slot.Crawler = crawler;
            slot.Profile = profile;
            slot.Feedback?.ApplyAuraSettings(auraSettings);
            if (wasExiting)
            {
                slot.Exiting = false;
                slot.Materialization.Clear();
                slot.Rect.localScale = Vector3.one;
                SetVisualAlpha(slot, 1f);
            }
            slot.Actor.texture = profile.frameAtlas;
            if (profile.sandPresence && slot.SandPresence == null)
            {
                var sandObject = new GameObject("ResidentFineSand", typeof(RectTransform), typeof(CrawlerSandPresenceGraphic));
                sandObject.transform.SetParent(slot.Rect, false);
                RectTransform sandRect = (RectTransform)sandObject.transform;
                sandRect.anchorMin = Vector2.zero; sandRect.anchorMax = Vector2.one;
                sandRect.offsetMin = sandRect.offsetMax = Vector2.zero;
                slot.SandPresence = sandObject.GetComponent<CrawlerSandPresenceGraphic>();
            }
            if (slot.SandPresence != null)
                slot.SandPresence.SetPresence(profile.sandPresence
                    ? slot.Actor.color.a : 0f);

            float height = layout.displayHeight;
            slot.Rect.sizeDelta = new Vector2(
                height * profile.FrameAspect,
                height);
            slot.BasePosition = layout.anchoredPosition;
            if (!slot.Entering)
                slot.Rect.anchoredPosition = slot.BasePosition;
            slot.Rect.gameObject.SetActive(true);
            if (crawlerChanged)
            {
                slot.DisplayedDuration = -1;
                slot.Feedback?.ResetFeedback();
            }
            RefreshDuration(slot, animate: !crawlerChanged);

            if (!changed)
                return false;

            slot.Elapsed = 0f;
            slot.FrameIndex = -1;
            ApplyFrame(slot, 0);
            return true;
        }

        private void BeginEntry(ResidentSlot slot)
        {
            slot.Hover?.SetAvailable(false);
            slot.Exiting = false;
            slot.PreviewExit = false;
            slot.PreviewHidden = false;
            slot.EntryElapsed = 0f;
            slot.EntryDuration = _layout.entryDuration;
            slot.Entering = true;
            slot.Elapsed = 0f;
            slot.FrameIndex = -1;
            ApplyFrame(slot, 0);
            slot.Rect.localScale = Vector3.one;
            slot.Rect.anchoredPosition = slot.BasePosition;

            slot.Materialization.Build(
                (RectTransform)transform,
                slot.Rect,
                slot.Profile.frameAtlas,
                slot.Profile.GetUvRect(0),
                _layout.GetMaterializationSettings());
            slot.Materialization.PlayEnter(_layout.entryDuration);
            ApplyEntryPose(slot, 0f);
        }

        private void BeginExit(
            ResidentSlot slot,
            bool previewOnly = false)
        {
            slot.Hover?.SetAvailable(false);
            slot.DurationTransfer?.Cancel();
            slot.DurationPreview = null;
            slot.Entering = false;
            slot.Exiting = true;
            slot.PreviewExit = previewOnly;
            slot.PreviewHidden = false;
            slot.ExitElapsed = 0f;
            slot.Elapsed = 0f;
            slot.FrameIndex = -1;
            ApplyFrame(slot, 0);
            slot.Rect.localScale = Vector3.one;
            slot.Rect.anchoredPosition = slot.BasePosition;
            SetVisualAlpha(slot, 1f);

            slot.Materialization.Build(
                (RectTransform)transform,
                slot.Rect,
                slot.Profile.frameAtlas,
                slot.Profile.GetUvRect(0),
                _layout.GetMaterializationSettings());
            slot.Materialization.PlayExit(_layout.exitDuration);
        }

        private void UpdateEntry(ResidentSlot slot, float deltaTime)
        {
            slot.EntryElapsed += deltaTime;
            float normalized = Mathf.Clamp01(
                slot.EntryElapsed / slot.EntryDuration);
            ApplyEntryPose(slot, normalized);
            if (normalized >= 1f)
                FinishEntry(slot);
        }

        private void ApplyEntryPose(ResidentSlot slot, float normalized)
        {
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            float scale = Mathf.Lerp(
                _layout.entryStartScale,
                1f,
                eased);
            scale += Mathf.Sin(normalized * Mathf.PI) * 0.035f;
            slot.Rect.localScale = Vector3.one * scale;
            slot.Rect.anchoredPosition = Vector2.LerpUnclamped(
                slot.BasePosition + _layout.entryStartOffset,
                slot.BasePosition,
                eased);

            float alpha = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.58f, 0.94f, normalized));
            SetVisualAlpha(slot, alpha);
        }

        private void UpdateExit(ResidentSlot slot, float deltaTime)
        {
            slot.ExitElapsed += deltaTime;
            float normalized = Mathf.Clamp01(
                slot.ExitElapsed / _layout.exitDuration);
            float alpha = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.08f, 0.48f, normalized));
            SetVisualAlpha(slot, alpha);
            slot.Rect.localScale = Vector3.one * Mathf.Lerp(
                1f,
                0.96f,
                normalized);
            if (normalized >= 1f)
            {
                if (slot.PreviewExit)
                    FinishPreviewExit(slot);
                else
                    HideSlot(slot);
            }
        }

        private static void FinishEntry(ResidentSlot slot)
        {
            slot.Entering = false;
            slot.Exiting = false;
            slot.PreviewExit = false;
            slot.PreviewHidden = false;
            slot.Rect.localScale = Vector3.one;
            slot.Rect.anchoredPosition = slot.BasePosition;
            SetVisualAlpha(slot, 1f);
            slot.Hover?.SetAvailable(true);
        }

        private static void FinishPreviewExit(ResidentSlot slot)
        {
            slot.Hover?.SetAvailable(false);
            slot.Entering = false;
            slot.Exiting = false;
            slot.PreviewExit = false;
            slot.PreviewHidden = true;
            slot.Materialization.Clear();
            slot.Rect.localScale = Vector3.one;
            slot.Rect.anchoredPosition = slot.BasePosition;
            SetVisualAlpha(slot, 0f);
        }

        private static void HideSlot(ResidentSlot slot)
        {
            slot.Hover?.Clear();
            slot.DurationTransfer?.Cancel();
            slot.DurationGainPending = false;
            slot.DurationPreview = null;
            slot.Entering = false;
            slot.Exiting = false;
            slot.PreviewExit = false;
            slot.PreviewHidden = false;
            slot.Materialization.Clear();
            slot.Crawler = null;
            slot.Profile = null;
            slot.DisplayedDuration = -1;
            slot.DurationPulseElapsed = DurationPulseDuration;
            slot.DurationPulseDirection = 0;
            slot.Feedback?.ResetFeedback();
            slot.DurationText.rectTransform.localScale = Vector3.one;
            slot.Rect.localScale = Vector3.one;
            SetVisualAlpha(slot, 1f);
            slot.Rect.gameObject.SetActive(false);
        }

        private static void RefreshDuration(
            ResidentSlot slot,
            bool animate)
        {
            if (slot.DurationGainPending && slot.Crawler != null && !slot.Crawler.IsExpired) return;
            int next = Mathf.Clamp(
                slot.Crawler?.remainingTriggers ?? 0,
                0,
                ColorTriggerSystem.MaxRemainingTriggers);
            if (slot.DisplayedDuration == next)
                return;

            int previous = slot.DisplayedDuration;
            slot.DisplayedDuration = next;
            RefreshDurationLabel(slot);
            if (animate && previous >= 0)
            {
                slot.DurationPulseDirection = next.CompareTo(previous);
                slot.DurationPulseElapsed = 0f;
            }
            else
            {
                slot.DurationPulseDirection = 0;
                slot.DurationPulseElapsed = DurationPulseDuration;
                slot.DurationText.rectTransform.localScale = Vector3.one;
            }
            ApplyDurationStyle(slot, slot.Actor.color.a);
        }

        private static void RefreshDurationLabel(ResidentSlot slot)
        {
            int current = Mathf.Max(0, slot.DisplayedDuration);
            slot.DurationText.text = slot.DurationPreview.HasValue && slot.DurationPreview.Value != current
                ? $"{current}→{slot.DurationPreview.Value}" : current.ToString();
        }

        private static void UpdateDurationPulse(
            ResidentSlot slot,
            float deltaTime)
        {
            if (slot.DurationPulseElapsed < DurationPulseDuration)
            {
                slot.DurationPulseElapsed = Mathf.Min(
                    DurationPulseDuration,
                    slot.DurationPulseElapsed + deltaTime);
            }

            float normalized = Mathf.Clamp01(
                slot.DurationPulseElapsed / DurationPulseDuration);
            float bump = Mathf.Sin(normalized * Mathf.PI) * 0.22f;
            slot.DurationText.rectTransform.localScale =
                Vector3.one * (1f + bump);
            ApplyDurationStyle(slot, slot.Actor.color.a);
        }

        private static void SetVisualAlpha(
            ResidentSlot slot,
            float alpha)
        {
            Color actorColor = slot.Actor.color;
            actorColor.a = Mathf.Clamp01(alpha);
            slot.Actor.color = actorColor;
            if (slot.SandPresence != null)
                slot.SandPresence.SetPresence(slot.Profile != null && slot.Profile.sandPresence
                    ? Mathf.Max(actorColor.a, slot.Entering ? 0.9f * Mathf.Sin(
                        Mathf.Clamp01(slot.EntryElapsed / slot.EntryDuration) * Mathf.PI) : 0f) : 0f,
                    slot.Entering ? slot.EntryElapsed / slot.EntryDuration : 1f);
            ApplyDurationStyle(slot, actorColor.a);
        }

        /// <summary>Refresh bindings after an effective condition change. Hover reads
        /// the current condition; no persistent condition label is displayed.</summary>
        public void ShowTriggerColorChanges(IReadOnlyList<CrawlerTriggerColorChange> changes)
        {
            if (!isActiveAndEnabled || _combat == null) return;
            RefreshResidents(false);
        }

        private static void ApplyDurationStyle(
            ResidentSlot slot,
            float alpha)
        {
            float normalized = Mathf.Clamp01(
                slot.DurationPulseElapsed / DurationPulseDuration);
            int direction = slot.DurationPreview.HasValue
                ? slot.DurationPreview.Value.CompareTo(slot.DisplayedDuration) : slot.DurationPulseDirection;
            Color pulseColor = direction > 0
                ? DurationIncreaseColor
                : direction < 0
                    ? DurationDecreaseColor
                    : DurationBaseColor;
            Color color = Color.Lerp(
                pulseColor,
                DurationBaseColor,
                slot.DurationPreview.HasValue ? 0f : normalized);
            color.a = Mathf.Clamp01(alpha);
            slot.DurationText.color = color;
        }

        private static void ApplyFrame(ResidentSlot slot, int frameIndex)
        {
            if (slot.Profile == null || slot.FrameIndex == frameIndex)
                return;
            slot.FrameIndex = frameIndex;
            slot.Actor.uvRect = slot.Profile.GetUvRect(frameIndex);
        }
    }
}
