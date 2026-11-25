using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Meta;
using WanjieScroll.Core;
using WanjieScroll.UI;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏界面视图：只负责显示和用户意图转发。
    /// 业务逻辑由 DaozangController 处理。
    /// 静态布局由 DaozangRoot.prefab 管控，本组件只做表现注入 + 动态内容构建。
    /// </summary>
    public sealed class DaozangView : MonoBehaviour
    {
        [Header("顶部栏")]
        public Image lingyuIcon;
        public Text lingyuText;
        public Button overviewButton;

        [Header("内容区")]
        public RectTransform departmentContainer;

        [Header("列容器（动态符箓的父节点）")]
        public RectTransform[] deptContainers;

        [Header("底部详情面板")]
        public Button refundButton;
        public GameObject detailPanel;
        public Image detailGlyph;
        public TMP_Text detailName;
        public Text detailEffect;
        public Text detailLevel;
        public Text detailPrice;
        public Image detailPriceIcon;
        public Button studyButton;
        public GameObject studyActionRoot;
        public RectTransform levelDotsContainer;

        [Header("等级圆点")]
        [Range(4f, 30f)] public float dotSize = 14f;
        [Range(2f, 20f)] public float dotSpacing = 8f;

        [Header("符箓 Prefab")]
        public GameObject talismanPrefab;

        [Header("选中发光（Inspector 可调）")]
        public Color glowColor = new(1f, 0.85f, 0.40f, 0.8f);
        public float glowDistanceX = 4f;
        public float glowDistanceY = -4f;

        [Header("等级圆点 Prefab")]
        public GameObject levelDotPrefab;

        [Header("总览")]
        public GameObject overviewPanel;
        public Button overviewCloseButton;
        public Button overviewBackdropButton;
        public RectTransform overviewLeftColumn;
        public RectTransform overviewRightColumn;

        [Header("返璞确认")]
        public GameObject refundConfirmPanel;
        public Button refundConfirmYes;
        public Button refundConfirmNo;

        private DaozangController _controller;
        private DaozangPresentationProfile _profile;
        private readonly Dictionary<string, DaozangTalismanView> _talismanViews = new();
        private readonly Dictionary<string, Text> _overviewLevelTexts = new();
        private readonly List<Image> _levelDots = new();
        private readonly Dictionary<string, int> _deptJingjueIndex = new();
        private CardTooltipView _tooltip;
        private string _selectedId;
        private bool _detailVisible;
        private Coroutine _slideCoroutine;

        // ── 表现注入（Prefab 加载后调用）──────────────────

        /// <summary>注入静态 sprite/color/font，不创建动态内容。</summary>
        public void ApplyPresentation(DaozangPresentationProfile profile)
        {
            _profile = profile;

            // 背景纸纹
            var bg = transform.Find("Background")?.GetComponent<Image>();
            if (bg != null) bg.color = new(0.95f, 0.92f, 0.85f, 1f);

            var overlay = transform.Find("PaperTexture")?.GetComponent<Image>();
            if (overlay != null)
            {
                overlay.sprite = RuntimeArtLoader.LoadSprite("Sprites/UI/Card/Materials/paper-fiber-overlay-alpha-512");
                overlay.color = new Color(1f, 1f, 1f, 0.16f);
            }

            // 灵玉图标
            if (lingyuIcon != null)
            {
                lingyuIcon.sprite = RuntimeArtLoader.LoadSprite("Sprites/UI/Daozang/utility-icons/currency-lingyu");
                lingyuIcon.color = Color.white;
                lingyuIcon.preserveAspect = true;
            }

            // 总览按钮
            if (overviewButton != null)
            {
                var ovImg = overviewButton.GetComponent<Image>();
                ovImg.sprite = RuntimeArtLoader.LoadSprite("Sprites/UI/Daozang/utility-icons/overview");
                ovImg.color = Color.white;
                ovImg.preserveAspect = true;
            }

            // 部标题牌
            for (int i = 0; i < DaozangConfig.Departments.Length && i < 5; i++)
            {
                var dept = DaozangConfig.Departments[i];
                var hdr = transform.Find($"HeaderContainer/Header_{dept.Id}")?.GetComponent<Image>();
                if (hdr != null)
                {
                    hdr.sprite = RuntimeArtLoader.LoadSprite(dept.HeaderSpritePath);
                    hdr.color = hdr.sprite != null ? Color.white : new(0.87f, 0.82f, 0.72f, 1f);
                    hdr.preserveAspect = false;
                }
            }

            ApplyScrollableLayout(profile);

            // 详情面板
            var dp = detailPanel?.GetComponent<Image>();
            if (dp != null)
            {
                // 点击符箓后出现的详情面板需要保留独立底图；
                // 列表下方的遮挡由滚动容器的边距控制，不复用这里的背景。
                dp.sprite = RuntimeArtLoader.LoadSprite(
                    "Sprites/UI/Daozang/detail-panel/detail-panel-bg");
                dp.type = Image.Type.Simple;
                dp.preserveAspect = true;
                dp.color = Color.white;
                dp.raycastTarget = false;
                dp.enabled = dp.sprite != null;
            }

            // 价格图标
            if (detailPriceIcon != null)
            {
                detailPriceIcon.sprite = RuntimeArtLoader.LoadSprite("Sprites/UI/Daozang/utility-icons/currency-lingyu");
            }

            // 返璞按钮
            ApplyArtworkButton(refundButton, "Sprites/UI/Daozang/utility-icons/reset-fanpu");
            // 修习按钮
            ApplyArtworkButton(studyButton, "Sprites/UI/Daozang/utility-icons/study-xiuxi");

            // 按钮悬停反馈（缩放+阴影，不改颜色）
            EnsureButtonFeedback(refundButton);
            EnsureButtonFeedback(studyButton);

            // Canvas pixelPerfect
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                canvas.pixelPerfect = true;

            // 初始隐藏
            if (detailPanel != null) detailPanel.SetActive(false);
            if (studyActionRoot != null) studyActionRoot.SetActive(false);
            if (overviewPanel != null) overviewPanel.SetActive(false);
            if (refundConfirmPanel != null) refundConfirmPanel.SetActive(false);
        }

        private void ApplyArtworkButton(Button btn, string spritePath)
        {
            if (btn == null) return;
            var icon = btn.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = RuntimeArtLoader.LoadSprite(spritePath);
                icon.color = Color.white;
            }
            btn.transition = Selectable.Transition.None;
        }

        private void ApplyScrollableLayout(DaozangPresentationProfile profile)
        {
            RectTransform mainScroll = transform.Find("MainScroll") as RectTransform;
            if (mainScroll != null)
            {
                float marginX = Mathf.Max(0f, profile.contentMarginX);
                float topInset = Mathf.Max(0f,
                    profile.topBarHeight + profile.headerHeight);
                const float BottomRevealInset = 24f;
                mainScroll.offsetMin = new Vector2(marginX, BottomRevealInset);
                mainScroll.offsetMax = new Vector2(-marginX, -topInset);
            }

            if (deptContainers == null)
                return;

            float spacing = Mathf.Max(0f, profile.talismanRowSpacing);
            float talismanHeight = Mathf.Max(1f, profile.talismanHeight);
            int trailingPadding = Mathf.CeilToInt(
                Mathf.Max(0f, profile.bottomBarHeight));
            foreach (RectTransform container in deptContainers)
            {
                if (container == null)
                    continue;

                var layout = container.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.spacing = spacing;
                    layout.padding.bottom = trailingPadding;
                }

                foreach (Transform child in container)
                {
                    if (child is not RectTransform item)
                        continue;
                    Vector2 size = item.sizeDelta;
                    size.y = talismanHeight;
                    item.sizeDelta = size;
                }
            }
        }

        private void EnsureButtonFeedback(Button btn)
        {
            if (btn == null) return;
            var feedback = btn.GetComponent<DaozangIconButtonFeedback>();
            if (feedback == null)
            {
                feedback = btn.gameObject.AddComponent<DaozangIconButtonFeedback>();
                var icon = btn.transform.Find("Icon")?.GetComponent<Graphic>();
                feedback.Bind(icon ?? btn.targetGraphic);
            }
        }

        // ── 动态内容构建 ──────────────────────────────────

        /// <summary>构建符箓卡片 + 总览行（数据驱动，每次实例化后调用）。</summary>
        public void BuildDynamicContent(DaozangController controller)
        {
            _controller = controller;
            EnsureTooltip();

            // 符箓卡片：查找 prefab 中已有的实例，绑定数据
            for (int i = 0; i < DaozangConfig.Departments.Length && i < deptContainers.Length; i++)
            {
                var dept = DaozangConfig.Departments[i];
                var parent = deptContainers[i];
                Sprite bodySprite = RuntimeArtLoader.LoadSprite(dept.BodySpritePath);
                foreach (var jj in dept.Jingjue)
                    BindTalisman(parent, jj, bodySprite);
            }

            // 总览行
            for (int i = 0; i < DaozangConfig.Departments.Length; i++)
            {
                var dept = DaozangConfig.Departments[i];
                var col = i < 3 ? overviewLeftColumn : overviewRightColumn;
                BuildOverviewSection(col, dept);
            }
        }

        private void BindTalisman(Transform parent, DaozangConfig.JingjueInfo info, Sprite bodySprite)
        {
            // 查找 prefab 中已有的实例
            var go = parent.Find($"Talisman_{info.Id}");
            if (go == null)
            {
                Debug.LogError($"[DAOZANG] Talisman_{info.Id} not found in prefab under {parent.name}");
                return;
            }

            var tv = go.GetComponent<DaozangTalismanView>();
            if (tv == null)
            {
                Debug.LogError($"[DAOZANG] DaozangTalismanView not found on Talisman_{info.Id}");
                return;
            }

            // 注入数据（不覆盖 prefab 上的 glowColor/glowDistanceX/Y 等参数）
            Sprite glyphSp = RuntimeArtLoader.LoadSprite(info.GlyphSpritePath);
            if (tv.BodyImage != null)
            {
                tv.BodyImage.sprite = bodySprite;
                // BodyImage.color 由 RefreshVisual 控制，这里不覆盖
            }
            tv.Setup(info, glyphSp, 0, false);
            tv.BindTooltip(_tooltip);
            tv.OnSelected += SelectJingjue;
            RegisterTalisman(info.Id, tv);

            go.gameObject.SetActive(true);
        }

        private void BuildOverviewSection(RectTransform parent, DaozangConfig.DepartmentInfo dept)
        {
            var hdrGo = new GameObject($"OHdr_{dept.Id}", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            hdrGo.transform.SetParent(parent, false);
            hdrGo.GetComponent<LayoutElement>().preferredHeight = 34f;
            var ht = hdrGo.GetComponent<Text>();
            ConfigureText(ht, TextAnchor.MiddleLeft, 21, new(0.73f, 0.16f, 0.09f, 1f));
            ht.text = dept.Name;

            foreach (var info in dept.Jingjue)
            {
                var row = new GameObject($"ORow_{info.Id}", typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(parent, false);
                row.GetComponent<LayoutElement>().preferredHeight = 29f;

                var nt = CreateStretchText(row.transform, "Name", new(0f, 0f), new(0.62f, 1f), TextAnchor.MiddleLeft, 17, new(0.12f, 0.10f, 0.08f, 1f));
                nt.text = info.Name;

                var lt = CreateStretchText(row.transform, "Level", new(0.62f, 0f), new(1f, 1f), TextAnchor.MiddleRight, 16, new(0.29f, 0.25f, 0.20f, 0.78f));
                RegisterOverviewRow(info.Id, lt);
            }
        }

        private Text CreateStretchText(Transform parent, string name, Vector2 aMin, Vector2 aMax, TextAnchor align, int fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new(4f, 0f); rt.offsetMax = new(-4f, 0f);
            var t = go.GetComponent<Text>();
            ConfigureText(t, align, fontSize, color);
            return t;
        }

        private static void ConfigureText(Text t, TextAnchor align, int fontSize, Color color)
        {
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.alignment = align;
            t.fontSize = fontSize;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>初始化：注入 Controller 和 Profile。</summary>
        public void Initialize(DaozangController controller, DaozangPresentationProfile profile)
        {
            _controller = controller;
            _profile = profile;

            foreach (var dept in DaozangConfig.Departments)
                _deptJingjueIndex[dept.Id] = 0;

            BindButtons();
            RefreshAll();
        }

        private void BindButtons()
        {
            if (refundButton != null)
                refundButton.onClick.AddListener(OnRefundClicked);
            if (overviewButton != null)
                overviewButton.onClick.AddListener(ToggleOverviewPanel);
            if (overviewCloseButton != null)
                overviewCloseButton.onClick.AddListener(CloseOverviewPanel);
            if (overviewBackdropButton != null)
                overviewBackdropButton.onClick.AddListener(CloseOverviewPanel);
            if (studyButton != null)
                studyButton.onClick.AddListener(OnStudyClicked);
            if (refundConfirmYes != null)
                refundConfirmYes.onClick.AddListener(OnRefundConfirmed);
            if (refundConfirmNo != null)
                refundConfirmNo.onClick.AddListener(() =>
                {
                    if (refundConfirmPanel != null)
                        refundConfirmPanel.SetActive(false);
                });

            BindTooltip(refundButton,
                "返璞\n重置全部已修习经诀，并返还投入的灵玉。");
        }

        public void RefreshAll()
        {
            RefreshTopBar();
            UpdateVisibleTalismans();
            RefreshAllTalismans();
            SortTalismansByUnlockState();
            RefreshDetailPanel();
            RefreshOverviewPanel();
        }

        private void RefreshTopBar()
        {
            if (lingyuText != null && _controller != null)
                lingyuText.text = _controller.Lingyu.ToString();
        }

        private void UpdateVisibleTalismans()
        {
            // 所有符箓都显示（可滚动浏览）
            foreach (var dept in DaozangConfig.Departments)
            {
                if (!_deptJingjueIndex.TryGetValue(dept.Id, out int idx))
                    idx = 0;
                for (int i = 0; i < dept.Jingjue.Length; i++)
                {
                    string id = dept.Jingjue[i].Id;
                    if (_talismanViews.TryGetValue(id, out var tv))
                        tv.gameObject.SetActive(true);
                }
            }
        }

        private void RefreshAllTalismans()
        {
            foreach (var kvp in _talismanViews)
            {
                if (!FindJingjue(kvp.Key, out var info)) continue;
                int level = _controller.GetLevel(info.Name);
                bool locked = _controller.IsLocked(info.Tier);
                kvp.Value.Refresh(level, locked, _controller.GetUnlockHint(info.Tier));
            }
        }

        private void RefreshDetailPanel()
        {
            if (string.IsNullOrEmpty(_selectedId) ||
                !FindJingjue(_selectedId, out var info))
            {
                HideDetailPanel();
                return;
            }

            ShowDetailPanel();

            int level = _controller.GetLevel(info.Name);
            bool locked = _controller.IsLocked(info.Tier);
            bool maxed = level >= info.MaxLevel;

            if (detailName != null)
                detailName.text = info.Name;
            if (detailEffect != null)
                detailEffect.text = info.EffectText;
            if (detailLevel != null)
                detailLevel.text = $"Lv.{level}/{info.MaxLevel}";

            RebuildLevelDots(level, info.MaxLevel);

            if (detailPrice != null)
            {
                detailPrice.text = locked ? string.Empty
                    : maxed ? "已满级"
                    : _controller.GetNextPrice(info).ToString();
                detailPrice.color = locked || (!maxed && !_controller.CanStudy(info))
                    ? PriceUnavailable : PriceAvailable;
            }

            if (detailPriceIcon != null)
                detailPriceIcon.gameObject.SetActive(!locked && !maxed);

            if (studyButton != null)
            {
                bool canStudy = _controller.CanStudy(info);
                studyButton.interactable = canStudy;
                if (studyActionRoot != null)
                    studyActionRoot.SetActive(canStudy);
            }

            if (detailGlyph != null)
            {
                Sprite glyph = RuntimeArtLoader.LoadSprite(info.GlyphSpritePath);
                if (glyph != null)
                {
                    detailGlyph.sprite = glyph;
                    detailGlyph.color = locked ? LockedGlyphColor
                        : level > 0 ? Color.white : InactiveGlyphColor;
                    detailGlyph.preserveAspect = true;
                }
            }
        }

        private static readonly Color InactiveGlyphColor = new(0.20f, 0.17f, 0.12f, 0.65f);
        private static readonly Color LockedGlyphColor = new(0.35f, 0.33f, 0.30f, 0.34f);
        private static readonly Color PriceAvailable = new(0.83f, 0.67f, 0.29f, 1f);
        private static readonly Color PriceUnavailable = new(0.72f, 0.42f, 0.35f, 1f);
        private static readonly Color DotFilled = new(0.75f, 0.15f, 0.08f, 1f);
        private static readonly Color DotEmpty = new(0.40f, 0.36f, 0.30f, 0.4f);
        private static Sprite _circleSprite; // 缓存的圆形精灵

        private void RebuildLevelDots(int currentLevel, int maxLevel)
        {
            if (levelDotsContainer == null) return;
            foreach (var dot in _levelDots)
            {
                if (dot != null) DestroyRuntimeObject(dot.gameObject);
            }
            _levelDots.Clear();

            var prefab = levelDotPrefab;
            if (prefab == null)
            {
                Debug.LogError("[DAOZANG] levelDotPrefab not assigned on DaozangView");
                return;
            }

            for (int i = 0; i < maxLevel; i++)
            {
                var go = Object.Instantiate(prefab, levelDotsContainer, false);
                go.name = $"Dot{i}";
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new(0f, 0.5f);
                rt.anchorMax = new(0f, 0.5f);
                rt.pivot = new(0.5f, 0.5f);
                rt.sizeDelta = new(dotSize, dotSize);
                rt.anchoredPosition = new(i * (dotSize + dotSpacing) + dotSize / 2f, 0f);
                var img = go.GetComponent<Image>();
                img.color = i < currentLevel ? DotFilled : DotEmpty;
                img.raycastTarget = false;
                _levelDots.Add(img);
            }
        }

        /// <summary>生成一个纯色圆形精灵（用于等级圆点）。</summary>
        private static Sprite CreateCircleSprite(int texSize)
        {
            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            var center = new Vector2(texSize / 2f, texSize / 2f);
            var radius = texSize / 2f - 1f;
            var radiusSq = radius * radius;
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    var dx = x - center.x;
                    var dy = y - center.y;
                    var distSq = dx * dx + dy * dy;
                    var alpha = distSq <= radiusSq
                        ? 1f
                        : Mathf.Clamp01(1f - (Mathf.Sqrt(distSq) - radius));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return Sprite.Create(tex,
                new Rect(0, 0, texSize, texSize),
                new Vector2(0.5f, 0.5f));
        }

        private void ShowDetailPanel()
        {
            if (detailPanel == null) return;
            if (!_detailVisible)
            {
                _detailVisible = true;
                detailPanel.SetActive(true);
                if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
                _slideCoroutine = StartCoroutine(SlideUpPanel());
            }
        }

        private void HideDetailPanel()
        {
            _detailVisible = false;
            if (detailPanel != null) detailPanel.SetActive(false);
            if (studyActionRoot != null) studyActionRoot.SetActive(false);
        }

        private IEnumerator SlideUpPanel()
        {
            if (detailPanel == null) yield break;
            RectTransform rt = detailPanel.transform as RectTransform;
            if (rt == null) yield break;
            Vector2 targetPos = rt.anchoredPosition;
            Vector2 startPos = new(targetPos.x, targetPos.y - 200f);
            rt.anchoredPosition = startPos;
            float duration = 0.25f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = 1f - Mathf.Pow(1f - t, 3f);
                rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                yield return null;
            }
            rt.anchoredPosition = targetPos;
            _slideCoroutine = null;
        }

        public void SelectJingjue(string id)
        {
            // 公开入口也供预览和测试直接定位；用户侧的锁定符箓只显示 Tooltip，
            // 不允许通过 Button 进入修习详情。
            _selectedId = id;
            foreach (var dept in DaozangConfig.Departments)
                for (int i = 0; i < dept.Jingjue.Length; i++)
                    if (dept.Jingjue[i].Id == id)
                        _deptJingjueIndex[dept.Id] = i;
            UpdateVisibleTalismans();
            foreach (var kvp in _talismanViews)
                kvp.Value.SetSelected(kvp.Key == id);
            RefreshDetailPanel();
        }

        private void RefreshOverviewPanel()
        {
            foreach (var pair in _overviewLevelTexts)
            {
                if (!FindJingjue(pair.Key, out var info)) continue;
                int level = _controller.GetLevel(info.Name);
                bool locked = _controller.IsLocked(info.Tier);
                pair.Value.text = locked ? string.Empty
                    : level > 0 ? $"Lv.{level}/{info.MaxLevel}" : "-";
                pair.Value.color = locked || level == 0
                    ? new(0.40f, 0.37f, 0.32f, 0.72f)
                    : PriceAvailable;
            }
        }

        private void SortTalismansByUnlockState()
        {
            if (_controller == null)
                return;

            foreach (var dept in DaozangConfig.Departments)
            {
                int siblingIndex = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool lockedPass = pass == 1;
                    foreach (var info in dept.Jingjue)
                    {
                        if (_controller.IsLocked(info.Tier) != lockedPass ||
                            !_talismanViews.TryGetValue(info.Id, out var view))
                        {
                            continue;
                        }
                        view.transform.SetSiblingIndex(siblingIndex++);
                    }
                }
            }
        }

        private void EnsureTooltip()
        {
            if (_tooltip != null)
                return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return;
            var tooltipObject = new GameObject(
                "DaozangTooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(canvas.transform, false);
            _tooltip = tooltipObject.AddComponent<CardTooltipView>();
        }

        private void BindTooltip(Button button, string text)
        {
            if (button == null || _tooltip == null)
                return;
            var trigger = button.GetComponent<DaozangTooltipTrigger>() ??
                button.gameObject.AddComponent<DaozangTooltipTrigger>();
            trigger.Bind(_tooltip, text);
        }

        /// <summary>为场景装配层创建的图标按钮复用道藏 Tooltip。</summary>
        public void BindUtilityTooltip(Button button, string text)
        {
            BindTooltip(button, text);
        }

        private void ToggleOverviewPanel()
        {
            if (overviewPanel != null)
                overviewPanel.SetActive(!overviewPanel.activeSelf);
        }

        private void CloseOverviewPanel()
        {
            if (overviewPanel != null) overviewPanel.SetActive(false);
        }

        public bool TryCloseTopmostPanel()
        {
            if (refundConfirmPanel != null && refundConfirmPanel.activeSelf)
            {
                refundConfirmPanel.SetActive(false);
                return true;
            }
            if (overviewPanel != null && overviewPanel.activeSelf)
            {
                overviewPanel.SetActive(false);
                return true;
            }
            return false;
        }

        private void SelectFirst()
        {
            var all = DaozangConfig.AllJingjue();
            if (all.Count > 0) SelectJingjue(all[0].Id);
        }

        // ── 用户意图 → Controller ──────────────────────────

        private void OnStudyClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            if (!FindJingjue(_selectedId, out var info)) return;
            if (_controller.TryStudy(info))
                RefreshAll();
        }

        private void OnRefundClicked()
        {
            if (refundConfirmPanel != null)
                refundConfirmPanel.SetActive(true);
        }

        private void OnRefundConfirmed()
        {
            if (refundConfirmPanel != null)
                refundConfirmPanel.SetActive(false);
            _controller.RefundAll();
            RefreshAll();
        }

        // ── 注册方法（Factory 调用）────────────────────────

        public void RegisterTalisman(string id, DaozangTalismanView view)
        {
            _talismanViews[id] = view;
        }

        public void RegisterOverviewRow(string id, Text levelText)
        {
            _overviewLevelTexts[id] = levelText;
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static bool FindJingjue(string id, out DaozangConfig.JingjueInfo info)
        {
            foreach (var dept in DaozangConfig.Departments)
                foreach (var j in dept.Jingjue)
                    if (j.Id == id) { info = j; return true; }
            info = default;
            return false;
        }
    }
}
