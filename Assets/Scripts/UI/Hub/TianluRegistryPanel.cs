using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 天箓名册面板：全屏只读列表，按古籍版式呈现——宣纸底、墨字、
    /// 朱砂印痕；页首书名与收录总览、右上返回，左侧竖排五签目录
    /// （默认不选签显示全部记录，点选后按卷筛选），右侧条目为
    /// 「大字条名 + 小字注文」，行间界栏分隔，右侧滚动条快速浏览。
    /// 只负责内容呈现、目录切换与开合生命周期；解锁判定全部在
    /// TianluRegistryCatalog 内完成，本类不读存档。
    /// 后续美术底图就绪后，配色与框线可整体替换为母图叠加。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class TianluRegistryPanel : MonoBehaviour
    {
        // 古籍配色：宣纸 / 墨 / 灰墨 / 朱砂 / 深朱 / 框线墨 / 界栏墨。
        private static readonly Color Paper =
            new Color(0.918f, 0.878f, 0.800f, 0.97f);
        private static readonly Color Ink =
            new Color(0.20f, 0.16f, 0.12f, 1f);
        private static readonly Color FadedInk =
            new Color(0.44f, 0.40f, 0.35f, 1f);
        private static readonly Color Cinnabar =
            new Color(0.58f, 0.19f, 0.14f, 1f);
        private static readonly Color DeepCinnabar =
            new Color(0.44f, 0.12f, 0.09f, 1f);
        private static readonly Color FrameInk =
            new Color(0.29f, 0.25f, 0.19f, 1f);
        private static readonly Color LedgerRule =
            new Color(0.29f, 0.25f, 0.19f, 0.18f);

        // 版式常量（1920×1080 参考画布，中心原点）。弹窗是 UI 图层：
        // 它按当前视口等比适配，不套用战斗背景的 16:9 核心裁切规则。
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float FrameOuterHalf = 485f;
        private const float FrameOuterWidth = 1760f;
        private const float FrameInnerHalf = 457f;
        private const float FrameInnerWidth = 1704f;
        private const float TitleY = 380f;
        private const float OverviewY = 298f;
        private const float DividerY = 252f;
        private const float ColumnX = -740f;
        private const float ColumnTabTop = 172f;
        private const float ColumnTabPitch = 96f;
        private const float ColumnDividerX = -600f;
        private const float EntryCenterX = 110f;
        private const float EntryWidth = 1420f;
        private const float RowPitch = 66f;
        private const float ViewportTop = 240f;
        private const float ViewportBottom = -390f;
        private const float ViewportHeight = 630f;
        private const float ViewportCenterY = -75f;
        private const float ScrollbarX = 835f;

        private static readonly string[] KindLabels =
            { "英灵", "遗珍", "天机", "关卡", "设施" };

        private readonly List<TianluRegistryItem> _items =
            new List<TianluRegistryItem>();
        private readonly List<TianluRegistryItem>[] _kindBuckets =
            new List<TianluRegistryItem>[KindLabels.Length];
        private readonly List<GameObject> _rowObjects = new List<GameObject>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<Text> _tabLabels = new List<Text>();
        private readonly List<Text> _tabCounts = new List<Text>();
        private readonly List<RectTransform> _tabMarkers =
            new List<RectTransform>();

        private CanvasGroup _canvasGroup;
        private RectTransform _contentRoot;
        private CanvasGroup _contentGroup;
        private RectTransform _scrollContent;
        private ScrollRect _scroll;
        private Text _emptyHint;
        private Text _overviewText;
        private Font _titleFont;
        private Font _bodyFont;
        private Coroutine _showRoutine;
        private Coroutine _hideRoutine;
        private int _selectedKind = -1;
        private bool _lockedOnly;
        private Button _lockedOnlyToggle;
        private Text _lockedOnlyLabel;
        private float _layoutScale = 1f;
        private Vector3 _animationScale = Vector3.one;

        public bool IsVisible =>
            _canvasGroup != null && _canvasGroup.alpha > 0.01f;
        public event System.Action CloseRequested;

        /// <summary>右上返回按钮；供场景装配点接统一点击音等跨模块表现。</summary>
        public Button ReturnButton { get; private set; }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            for (int index = 0; index < _kindBuckets.Length; index++)
                _kindBuckets[index] = new List<TianluRegistryItem>();
            BuildView();
            ApplyLayoutScale();
            HideImmediately();
        }

        /// <summary>
        /// Tianlu 的版式以 1920×1080 为参考，但整个内容根按当前弹窗
        /// 视口等比缩放，保证窄高视口不裁掉框线、标题和操作区；宣纸
        /// 背板仍然由全屏根节点铺满。该规则只属于 UI，不改变背景图合同。
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            ApplyLayoutScale();
        }

        private void ApplyLayoutScale()
        {
            if (_contentRoot == null)
                return;

            RectTransform root = transform as RectTransform;
            if (root == null)
                return;

            Rect bounds = root.rect;
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;

            _layoutScale = Mathf.Max(
                0.01f,
                Mathf.Min(
                    bounds.width / ReferenceWidth,
                    bounds.height / ReferenceHeight));
            ApplyContentScale();
        }

        private void SetContentAnimationScale(Vector3 scale)
        {
            _animationScale = scale;
            ApplyContentScale();
        }

        private void ApplyContentScale()
        {
            if (_contentRoot == null)
                return;
            _contentRoot.localScale = new Vector3(
                _animationScale.x * _layoutScale,
                _animationScale.y * _layoutScale,
                _animationScale.z);
        }

        private void Update()
        {
            if (!IsVisible || !_canvasGroup.interactable)
                return;
            if (Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.W))
                SwitchTab(-1);
            else if (Input.GetKeyDown(KeyCode.DownArrow) ||
                     Input.GetKeyDown(KeyCode.S))
                SwitchTab(1);
            else if (Input.GetKeyDown(KeyCode.Escape))
                CloseRequested?.Invoke();
        }

        private void OnDisable()
        {
            StopAnimations();
        }

        /// <summary>替换名册内容；空列表清空全部目录项。</summary>
        public void SetItems(IReadOnlyList<TianluRegistryItem> items)
        {
            _items.Clear();
            if (items != null)
            {
                for (int index = 0; index < items.Count; index++)
                    _items.Add(items[index]);
            }

            foreach (List<TianluRegistryItem> bucket in _kindBuckets)
                bucket.Clear();
            foreach (TianluRegistryItem item in
                     TianluRegistryCatalog.SortForDisplay(_items))
            {
                int kindIndex = (int)item.Kind;
                if (kindIndex >= 0 && kindIndex < _kindBuckets.Length)
                    _kindBuckets[kindIndex].Add(item);
            }

            _selectedKind = -1;
            RefreshTabs();
            RebuildRows();
        }

        public void Show()
        {
            StopAnimations();
            _showRoutine = StartCoroutine(ShowRoutine());
        }

        public void Hide()
        {
            if (_hideRoutine != null)
                return;
            StopAnimations();
            _hideRoutine = StartCoroutine(HideRoutine());
        }

        public void HideImmediately()
        {
            StopAnimations();
            if (_canvasGroup == null)
                return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            if (_contentGroup != null)
            {
                _contentGroup.alpha = 0f;
                _contentRoot.anchoredPosition = new Vector2(0f, 24f);
                SetContentAnimationScale(new Vector3(0.97f, 0.985f, 1f));
            }
        }

        private IEnumerator ShowRoutine()
        {
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 0f;
            _contentGroup.alpha = 0f;
            _contentGroup.interactable = false;
            _contentGroup.blocksRaycasts = false;
            _contentRoot.anchoredPosition = new Vector2(0f, 24f);
            SetContentAnimationScale(new Vector3(0.97f, 0.985f, 1f));

            // 宣纸先铺开，内容随后从纸上浮现，避免整屏瞬间反白。
            const float duration = 0.26f;
            const float contentDelay = 0.12f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = EaseOutCubic(progress);
                float contentProgress = Mathf.Clamp01(
                    (progress - contentDelay / duration) /
                    (1f - contentDelay / duration));
                float contentEased = EaseOutCubic(contentProgress);
                _contentGroup.alpha = contentProgress;
                _contentRoot.anchoredPosition = Vector2.LerpUnclamped(
                    new Vector2(0f, 24f),
                    Vector2.zero,
                    contentEased);
                SetContentAnimationScale(new Vector3(
                    Mathf.Lerp(0.97f, 1f, contentEased),
                    Mathf.Lerp(0.985f, 1f, contentEased),
                    1f));
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            _contentGroup.alpha = 1f;
            _contentRoot.anchoredPosition = Vector2.zero;
            SetContentAnimationScale(Vector3.one);
            _contentGroup.interactable = true;
            _contentGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _showRoutine = null;
        }

        private IEnumerator HideRoutine()
        {
            _canvasGroup.interactable = false;
            _contentGroup.interactable = false;
            const float duration = 0.18f;
            float startAlpha = _contentGroup.alpha;
            float startPaperAlpha = _canvasGroup.alpha;
            Vector2 startPosition = _contentRoot.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = EaseOutCubic(progress);
                _contentGroup.alpha = Mathf.Lerp(
                    startAlpha,
                    0f,
                    progress * progress);
                _canvasGroup.alpha = Mathf.Lerp(startPaperAlpha, 0f, eased);
                _contentRoot.anchoredPosition = Vector2.LerpUnclamped(
                    startPosition,
                    new Vector2(0f, 24f),
                    eased);
                yield return null;
            }

            _hideRoutine = null;
            HideImmediately();
        }

        private void SwitchTab(int direction)
        {
            if (_selectedKind < 0)
            {
                // 从「全部」进入目录：↓ 取第一签，↑ 取最后一签。
                SelectTab(direction > 0 ? 0 : KindLabels.Length - 1);
                return;
            }
            int targetKind = Mathf.Clamp(
                _selectedKind + direction,
                0,
                KindLabels.Length - 1);
            SelectTab(targetKind);
        }

        /// <summary>点选签筛选该卷；再点同一签（或越界方向键）回到全部。</summary>
        private void SelectTab(int kindIndex)
        {
            _selectedKind = kindIndex == _selectedKind ? -1 : kindIndex;
            RefreshTabs();
            RebuildRows();
        }

        private void RefreshTabs()
        {
            int unlockedTotal = 0;
            foreach (TianluRegistryItem item in _items)
            {
                if (item.Unlocked)
                    unlockedTotal++;
            }
            _overviewText.text = $"已收录 {unlockedTotal}/{_items.Count}";

            for (int index = 0; index < _tabLabels.Count; index++)
            {
                List<TianluRegistryItem> bucket = _kindBuckets[index];
                int unlockedInKind = 0;
                foreach (TianluRegistryItem item in bucket)
                {
                    if (item.Unlocked)
                        unlockedInKind++;
                }
                bool selected = index == _selectedKind;
                _tabLabels[index].text = KindLabels[index];
                _tabLabels[index].color = selected ? Cinnabar : FadedInk;
                _tabCounts[index].text = $"{unlockedInKind}/{bucket.Count}";
                _tabCounts[index].color = selected
                    ? Cinnabar
                    : new Color(FadedInk.r, FadedInk.g, FadedInk.b, 0.9f);
                _tabMarkers[index].gameObject.SetActive(selected);
                // 悬停反馈与选中态共用同一份配色，保证两套状态不打架。
                _tabButtons[index].colors = TintBlock(
                    selected ? Cinnabar : FadedInk,
                    selected ? DeepCinnabar : Ink,
                    Ink);
            }
        }

        /// <summary>按当前筛选重建滚动列表并回到顶部。</summary>
        private void RebuildRows()
        {
            foreach (GameObject row in _rowObjects)
                Destroy(row);
            _rowObjects.Clear();

            var visible = new List<TianluRegistryItem>();
            foreach (TianluRegistryItem item in
                     TianluRegistryCatalog.SortForDisplay(_items))
            {
                bool kindMatches =
                    _selectedKind < 0 || (int)item.Kind == _selectedKind;
                bool lockedFilterMatches = !_lockedOnly || !item.Unlocked;
                if (kindMatches && lockedFilterMatches)
                    visible.Add(item);
            }

            _emptyHint.text = _lockedOnly
                ? "此卷条目皆已收录"
                : "此卷尚无条目";
            _emptyHint.gameObject.SetActive(visible.Count == 0);
            _scrollContent.sizeDelta = new Vector2(
                EntryWidth,
                visible.Count * RowPitch);
            for (int index = 0; index < visible.Count; index++)
            {
                BuildEntryRow(visible[index], index);
            }
            if (_scroll != null)
                _scroll.verticalNormalizedPosition = 1f;
        }

        private void BuildView()
        {
            RectTransform root = GetComponent<RectTransform>();
            Stretch(root);

            RectTransform paper = CreateImage("RegistryPaper", transform, Paper);
            Stretch(paper);
            // 宣纸底兼输入屏蔽：挡住对场景设施的点击穿透。
            paper.GetComponent<Image>().raycastTarget = true;

            var contentObject = new GameObject(
                "RegistryContent",
                typeof(RectTransform),
                typeof(CanvasGroup));
            contentObject.transform.SetParent(transform, false);
            _contentRoot = contentObject.GetComponent<RectTransform>();
            Stretch(_contentRoot);
            _contentGroup = contentObject.GetComponent<CanvasGroup>();
            _contentGroup.interactable = false;
            _contentGroup.blocksRaycasts = false;

            // 古籍「文武线」边栏：粗外线 + 细内线，贴合屏幕内沿。
            DrawFrame(_contentRoot, FrameOuterHalf, FrameOuterWidth, 3f);
            DrawFrame(_contentRoot, FrameInnerHalf, FrameInnerWidth, 1.5f);

            // 页首：书名居中，两侧短栏，右上角返回按钮。
            Text title = CreateText(
                _contentRoot,
                "RegistryTitle",
                "天箓",
                72,
                TextAnchor.MiddleCenter,
                Ink,
                true);
            Place(
                title.rectTransform,
                new Vector2(0f, TitleY),
                new Vector2(320f, 104f));
            CreateRule(
                _contentRoot,
                new Vector2(-290f, TitleY),
                new Vector2(170f, 2f));
            CreateRule(
                _contentRoot,
                new Vector2(290f, TitleY),
                new Vector2(170f, 2f));

            Button back = CreateTextButton(
                "ReturnToHub",
                "返回",
                _contentRoot,
                new Vector2(770f, TitleY),
                new Vector2(120f, 48f),
                22,
                Cinnabar);
            back.onClick.AddListener(() => CloseRequested?.Invoke());
            ReturnButton = back;

            // 页首总览：仅显示收录进度。
            _overviewText = CreateText(
                _contentRoot,
                "RegistryOverview",
                string.Empty,
                22,
                TextAnchor.MiddleCenter,
                FadedInk,
                false);
            Place(
                _overviewText.rectTransform,
                new Vector2(0f, OverviewY),
                new Vector2(600f, 36f));

            // 书名下通栏分隔线。
            CreateRule(
                _contentRoot,
                new Vector2(0f, DividerY),
                new Vector2(1640f, 1.5f));

            // 左侧竖排目录（五签）与目录栏界。
            for (int index = 0; index < KindLabels.Length; index++)
            {
                BuildDirectoryTab(index);
            }
            CreateRule(
                _contentRoot,
                new Vector2(
                    ColumnDividerX,
                    (DividerY + ViewportBottom) * 0.5f),
                new Vector2(1.5f, DividerY - ViewportBottom));

            BuildFilterToggle();
            BuildScrollArea();
        }

        private void BuildDirectoryTab(int index)
        {
            // 目录竖排：类名居中，左侧朱砂短栏标记当前卷，可点选。
            float slotY = ColumnTabTop - index * ColumnTabPitch;

            Button zone = CreateInvisibleButton(
                $"DirectoryZone{KindLabels[index]}",
                _contentRoot,
                new Vector2(ColumnX, slotY),
                new Vector2(160f, 84f));
            zone.onClick.AddListener(() => SelectTab(index));

            Text label = CreateText(
                zone.transform,
                "Label",
                string.Empty,
                28,
                TextAnchor.MiddleCenter,
                FadedInk,
                true);
            Place(label.rectTransform, new Vector2(0f, 14f), new Vector2(150f, 40f));
            _tabLabels.Add(label);
            zone.targetGraphic = label;
            zone.transition = Selectable.Transition.ColorTint;

            Text count = CreateText(
                zone.transform,
                "Count",
                string.Empty,
                16,
                TextAnchor.MiddleCenter,
                FadedInk,
                false);
            Place(count.rectTransform, new Vector2(0f, -22f), new Vector2(150f, 26f));
            _tabCounts.Add(count);

            RectTransform marker = CreateRule(
                zone.transform,
                new Vector2(-74f, 0f),
                new Vector2(4f, 52f),
                Cinnabar);
            marker.gameObject.SetActive(false);
            _tabMarkers.Add(marker);
            _tabButtons.Add(zone);
        }

        /// <summary>目录栏底部的「只看未收录」统一开关。</summary>
        private void BuildFilterToggle()
        {
            _lockedOnlyToggle = CreateInvisibleButton(
                "LockedOnlyToggle",
                _contentRoot,
                new Vector2(ColumnX, -310f),
                new Vector2(160f, 56f));
            _lockedOnlyToggle.onClick.AddListener(() =>
            {
                _lockedOnly = !_lockedOnly;
                RefreshFilterToggle();
                RebuildRows();
            });

            _lockedOnlyLabel = CreateText(
                _lockedOnlyToggle.transform,
                "Label",
                string.Empty,
                20,
                TextAnchor.MiddleCenter,
                FadedInk,
                false);
            Stretch(_lockedOnlyLabel.rectTransform);
            _lockedOnlyToggle.targetGraphic = _lockedOnlyLabel;
            _lockedOnlyToggle.transition = Selectable.Transition.ColorTint;
            RefreshFilterToggle();
        }

        private void RefreshFilterToggle()
        {
            if (_lockedOnlyToggle == null || _lockedOnlyLabel == null)
                return;
            _lockedOnlyLabel.text = (_lockedOnly ? "● " : "○ ") + "只看未收录";
            _lockedOnlyLabel.color = _lockedOnly ? Cinnabar : FadedInk;
            _lockedOnlyToggle.colors = TintBlock(
                _lockedOnly ? Cinnabar : FadedInk,
                _lockedOnly ? DeepCinnabar : Ink,
                Ink);
        }

        private void BuildScrollArea()
        {
            // 条目区视口：透明面板承接滚轮与拖拽，RectMask2D 裁掉溢出行。
            var viewportObject = new GameObject(
                "RegistryScrollViewport",
                typeof(RectTransform),
                typeof(RectMask2D),
                typeof(CanvasRenderer),
                typeof(Image));
            viewportObject.transform.SetParent(_contentRoot, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Place(
                viewportRect,
                new Vector2(EntryCenterX, ViewportCenterY),
                new Vector2(EntryWidth, ViewportHeight));
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;

            var contentObject = new GameObject(
                "RegistryScrollContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            _scrollContent = contentObject.GetComponent<RectTransform>();
            _scrollContent.anchorMin = new Vector2(0.5f, 1f);
            _scrollContent.anchorMax = new Vector2(0.5f, 1f);
            _scrollContent.pivot = new Vector2(0.5f, 1f);
            _scrollContent.anchoredPosition = Vector2.zero;
            _scrollContent.sizeDelta = new Vector2(EntryWidth, 0f);

            ScrollRect scrollRect = viewportObject.AddComponent<ScrollRect>();
            scrollRect.content = _scrollContent;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.08f;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.135f;
            scrollRect.scrollSensitivity = 32f;
            _scroll = scrollRect;

            // 右侧竖向滚动条：淡墨轨道 + 墨色滑块，内容不足一屏时自动隐藏。
            var barObject = new GameObject(
                "RegistryScrollbar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Scrollbar));
            barObject.transform.SetParent(_contentRoot, false);
            RectTransform barRect = barObject.GetComponent<RectTransform>();
            Place(
                barRect,
                new Vector2(ScrollbarX, ViewportCenterY),
                new Vector2(14f, ViewportHeight));
            Image barImage = barObject.GetComponent<Image>();
            barImage.color = new Color(FrameInk.r, FrameInk.g, FrameInk.b, 0.14f);
            barImage.raycastTarget = true;

            var slidingObject = new GameObject("SlidingArea", typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform slidingRect = slidingObject.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(0f, 6f);
            slidingRect.offsetMax = new Vector2(0f, -6f);

            var handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            handleObject.transform.SetParent(slidingObject.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(3f, 0f);
            handleRect.offsetMax = new Vector2(-3f, 0f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(Ink.r, Ink.g, Ink.b, 0.32f);
            handleImage.raycastTarget = true;

            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = TintBlock(
                new Color(Ink.r, Ink.g, Ink.b, 0.32f),
                new Color(Ink.r, Ink.g, Ink.b, 0.45f),
                new Color(Ink.r, Ink.g, Ink.b, 0.6f));

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;

            // 空卷提示（覆盖在条目区中央）。
            _emptyHint = CreateText(
                _contentRoot,
                "RegistryEmptyHint",
                "此卷尚无条目",
                26,
                TextAnchor.MiddleCenter,
                FadedInk,
                false);
            Place(
                _emptyHint.rectTransform,
                new Vector2(EntryCenterX, ViewportCenterY),
                new Vector2(600f, 48f));
            _emptyHint.gameObject.SetActive(false);
        }

        private void BuildEntryRow(TianluRegistryItem item, int index)
        {
            var rowObject = new GameObject(
                $"Row{index:00}",
                typeof(RectTransform));
            rowObject.transform.SetParent(_scrollContent, false);
            RectTransform row = rowObject.GetComponent<RectTransform>();
            // 行锚定在滚动内容顶部：内容矩形以顶部为枢轴，行若锚在矩形
            // 中心会随内容高度整体下移，导致列表头部出现大段空白。
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -(index * RowPitch));
            row.sizeDelta = new Vector2(EntryWidth, 60f);

            // 行间界栏：细淡墨线，随行显隐。
            CreateRule(row, new Vector2(0f, -32f), new Vector2(1380f, 1f), LedgerRule);

            // 朱砂实点表示已收录；未收录为描灰空圈。
            Text marker = CreateText(
                row,
                "Marker",
                item.Unlocked ? "●" : "○",
                item.Unlocked ? 26 : 24,
                TextAnchor.MiddleCenter,
                item.Unlocked ? Cinnabar : FadedInk,
                false);
            Place(marker.rectTransform, new Vector2(-560f, 0f), new Vector2(48f, 40f));

            Text name = CreateText(
                row,
                "Name",
                item.DisplayName ?? string.Empty,
                30,
                TextAnchor.MiddleLeft,
                item.Unlocked ? Ink : FadedInk,
                true);
            Place(name.rectTransform, new Vector2(-380f, 0f), new Vector2(280f, 46f));

            Text detail = CreateText(
                row,
                "Detail",
                (item.Unlocked ? item.UnlockedText : item.LockedText) ?? string.Empty,
                26,
                TextAnchor.MiddleLeft,
                item.Unlocked ? Ink : FadedInk,
                false);
            Place(detail.rectTransform, new Vector2(245f, 0f), new Vector2(930f, 50f));

            _rowObjects.Add(rowObject);
        }

        private RectTransform CreateRule(
            Transform parent,
            Vector2 position,
            Vector2 size,
            Color? colour = null)
        {
            RectTransform rule = CreateImage(
                "Rule",
                parent,
                colour ?? FrameInk);
            Place(rule, position, size);
            return rule;
        }

        private void DrawFrame(
            Transform parent,
            float halfHeight,
            float width,
            float thickness)
        {
            CreateRule(parent, new Vector2(0f, halfHeight), new Vector2(width, thickness));
            CreateRule(parent, new Vector2(0f, -halfHeight), new Vector2(width, thickness));
            CreateRule(
                parent,
                new Vector2(-width / 2f, 0f),
                new Vector2(thickness, halfHeight * 2f));
            CreateRule(
                parent,
                new Vector2(width / 2f, 0f),
                new Vector2(thickness, halfHeight * 2f));
        }

        private Button CreateTextButton(
            string objectName,
            string value,
            Transform parent,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color colour)
        {
            Button button = CreateInvisibleButton(objectName, parent, position, size);
            Text label = CreateText(
                button.transform,
                "Label",
                value,
                fontSize,
                TextAnchor.MiddleCenter,
                colour,
                false);
            Stretch(label.rectTransform);
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = TintBlock(
                colour,
                colour == Cinnabar ? DeepCinnabar : Ink,
                Ink);
            return button;
        }

        private Button CreateInvisibleButton(
            string objectName,
            Transform parent,
            Vector2 position,
            Vector2 size)
        {
            RectTransform frame = CreateImage(
                objectName,
                parent,
                new Color(1f, 1f, 1f, 0.001f));
            Place(frame, position, size);
            Image frameImage = frame.GetComponent<Image>();
            frameImage.raycastTarget = true;
            Button button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frameImage;
            button.transition = Selectable.Transition.None;
            return button;
        }

        private static ColorBlock TintBlock(
            Color normal,
            Color highlighted,
            Color pressed)
        {
            return new ColorBlock
            {
                normalColor = normal,
                highlightedColor = highlighted,
                pressedColor = pressed,
                selectedColor = highlighted,
                disabledColor =
                    new Color(FadedInk.r, FadedInk.g, FadedInk.b, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
        }

        private Text CreateText(
            Transform parent,
            string objectName,
            string value,
            int size,
            TextAnchor alignment,
            Color colour,
            bool titleFont)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = titleFont ? TitleFont : BodyFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform CreateImage(
            string objectName,
            Transform parent,
            Color colour)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private void StopAnimations()
        {
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }
        }

        private Font TitleFont => _titleFont != null
            ? _titleFont
            : (_titleFont = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy")
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private Font BodyFont => _bodyFont != null
            ? _bodyFont
            : (_bodyFont = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                ?? TitleFont);

        private static float EaseOutCubic(float progress)
        {
            return 1f - Mathf.Pow(1f - progress, 3f);
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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
