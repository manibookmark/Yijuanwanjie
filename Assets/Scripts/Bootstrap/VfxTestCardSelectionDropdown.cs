using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// F7 实时调参卡牌选择器。本组件唯一拥有 Dropdown 的层级、
    /// 滚动模板、验收筛选和可见行到卡池索引的映射；调参面板只接收
    /// 原卡池索引，不依赖具体 UGUI 模板结构或显示名称。
    /// </summary>
    internal sealed class VfxTestCardSelectionDropdown : MonoBehaviour
    {
        private const float ItemHeight = 36f;
        private const float ListHeight = 324f;

        private readonly List<CardConfig> _cards = new List<CardConfig>();
        private readonly List<int> _visibleIndices = new List<int>();

        private Action<int> _onSelectionChanged;
        private Dropdown _dropdown;
        private Dropdown _filterDropdown;
        private VfxTestCardSelectionFilter _filter = VfxTestCardSelectionFilter.Pending;
        private bool _refreshing;

        public static VfxTestCardSelectionDropdown Create(
            Transform parent,
            float x,
            float y,
            float width,
            float height,
            Action<int> onSelectionChanged)
        {
            var root = new GameObject(
                "CardSelectionRow",
                typeof(RectTransform),
                typeof(VfxTestCardSelectionDropdown));
            root.transform.SetParent(parent, false);

            var selector =
                root.GetComponent<VfxTestCardSelectionDropdown>();
            selector._onSelectionChanged = onSelectionChanged;
            selector.BuildUi(x, y, width, height);
            return selector;
        }

        public int BindCards(
            IReadOnlyList<CardConfig> cards,
            int selectedIndex)
        {
            _cards.Clear();
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i] != null)
                        _cards.Add(cards[i]);
                }
            }

            return RebuildOptions(selectedIndex);
        }

        public void SetSelectedIndex(int selectedIndex)
        {
            if (_dropdown == null || _cards.Count == 0)
                return;

            int visibleIndex = _visibleIndices.IndexOf(selectedIndex);
            // An explicit Inspector selection may be outside the runtime
            // filter. Startup resolves Pending before reaching this adapter.
            if (visibleIndex < 0 && selectedIndex >= 0 && selectedIndex < _cards.Count)
            {
                _filter = VfxTestCardSelectionFilter.All;
                _filterDropdown.SetValueWithoutNotify((int)_filter);
                _filterDropdown.RefreshShownValue();
                RebuildOptions(selectedIndex);
                return;
            }
            if (_visibleIndices.Count == 0) return;
            if (visibleIndex < 0) visibleIndex = 0;
            if (_dropdown.value == visibleIndex)
                return;

            _refreshing = true;
            _dropdown.SetValueWithoutNotify(visibleIndex);
            _dropdown.RefreshShownValue();
            _refreshing = false;
        }

        public int AdvanceToNextPending(int completedIndex)
        {
            // Completion always resumes the pending queue, even when an
            // Inspector selection previously switched the filter to All.
            _filter = VfxTestCardSelectionFilter.Pending;
            _filterDropdown.SetValueWithoutNotify((int)_filter);
            _filterDropdown.RefreshShownValue();

            int nextIndex = -1;
            int start = Mathf.Clamp(completedIndex, -1, _cards.Count - 1);
            for (int offset = 1; offset <= _cards.Count; offset++)
            {
                int candidate = (start + offset) % _cards.Count;
                if (!VfxTestCardSelectionPolicy.ShouldInclude(
                        _cards[candidate].contentId,
                        VfxTestCardSelectionFilter.Pending))
                    continue;

                nextIndex = candidate;
                break;
            }

            // Rebuild without callbacks; the panel commits this selection
            // once, refreshing the profile and staging the next hand card.
            return RebuildOptions(nextIndex);
        }

        public void MoveSelection(int delta)
        {
            if (_dropdown == null || _visibleIndices.Count == 0)
                return;

            int next = (_dropdown.value + delta) % _visibleIndices.Count;
            if (next < 0)
                next += _visibleIndices.Count;
            _dropdown.value = next;
        }

        private int GetSelectedIndex() => _dropdown != null && _dropdown.value >= 0 &&
            _dropdown.value < _visibleIndices.Count ? _visibleIndices[_dropdown.value] : -1;

        private void OnFilterChanged(int value)
        {
            int selectedIndex = GetSelectedIndex();
            _filter = (VfxTestCardSelectionFilter)value;
            _onSelectionChanged?.Invoke(RebuildOptions(selectedIndex));
        }

        private void BuildUi(
            float x,
            float y,
            float width,
            float height)
        {
            RectTransform rootRect = GetComponent<RectTransform>();
            PlaceTopLeft(rootRect, x, y, width, height);

            _filterDropdown = CreateDropdown("CardStatusFilter", "未确认", 0f, 112f, height);
            _filterDropdown.AddOptions(new List<string> { "未确认", "全部", "已确认" });
            _filterDropdown.SetValueWithoutNotify((int)_filter);
            _filterDropdown.RefreshShownValue();
            _filterDropdown.onValueChanged.AddListener(OnFilterChanged);
            _dropdown = CreateDropdown("AllCardSelector", "暂无卡牌", 120f, width - 120f, height);
            _dropdown.onValueChanged.AddListener(OnValueChanged);
        }

        private Dropdown CreateDropdown(string name, string initialLabel, float x, float width, float height)
        {
            var dropdownObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Dropdown));
            dropdownObject.transform.SetParent(transform, false);
            PlaceTopLeft(
                dropdownObject.GetComponent<RectTransform>(),
                x,
                0f,
                width,
                height);

            Image rootImage = dropdownObject.GetComponent<Image>();
            rootImage.color = new Color(0.16f, 0.11f, 0.07f, 0.99f);

            Text caption = UIElementFactory.CreateText(
                "Label",
                dropdownObject.transform,
                initialLabel,
                17,
                new Color(0.98f, 0.93f, 0.8f),
                TextAnchor.MiddleLeft);
            RectTransform captionRect = caption.rectTransform;
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = new Vector2(12f, 1f);
            captionRect.offsetMax = new Vector2(-42f, -1f);
            caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            caption.verticalOverflow = VerticalWrapMode.Truncate;

            Text arrow = UIElementFactory.CreateText(
                "Arrow",
                dropdownObject.transform,
                "▼",
                16,
                new Color(1f, 0.7f, 0.28f),
                TextAnchor.MiddleCenter);
            RectTransform arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.sizeDelta = new Vector2(36f, 0f);
            arrowRect.anchoredPosition = Vector2.zero;

            RectTransform template = CreateTemplate(
                dropdownObject.transform);
            Text itemLabel = template.GetComponentInChildren<Text>(true);

            Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdown.targetGraphic = rootImage;
            dropdown.captionText = caption;
            dropdown.template = template;
            dropdown.itemText = itemLabel;
            dropdown.options = new List<Dropdown.OptionData>();

            ColorBlock colors = dropdown.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.68f, 1f);
            colors.pressedColor = new Color(0.82f, 0.64f, 0.4f, 1f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.55f);
            dropdown.colors = colors;
            Navigation navigation = dropdown.navigation;
            navigation.mode = Navigation.Mode.None;
            dropdown.navigation = navigation;

            template.gameObject.SetActive(false);
            return dropdown;
        }

        private RectTransform CreateTemplate(Transform dropdownRoot)
        {
            var templateObject = new GameObject(
                "Template",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            templateObject.transform.SetParent(dropdownRoot, false);
            RectTransform template =
                templateObject.GetComponent<RectTransform>();
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, ListHeight);
            template.anchoredPosition = new Vector2(0f, -2f);

            Image templateImage = templateObject.GetComponent<Image>();
            templateImage.color = new Color(0.055f, 0.04f, 0.028f, 0.995f);

            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(Mask));
            viewportObject.transform.SetParent(template, false);
            RectTransform viewport =
                viewportObject.GetComponent<RectTransform>();
            Stretch(viewport, 2f, 2f, -2f, -2f);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform));
            contentObject.transform.SetParent(viewport, false);
            RectTransform content =
                contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, ItemHeight);
            content.anchoredPosition = Vector2.zero;

            CreateItemTemplate(content);

            ScrollRect scroll = templateObject.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = ItemHeight * 1.5f;
            return template;
        }

        private static void CreateItemTemplate(RectTransform content)
        {
            var itemObject = new GameObject(
                "Item",
                typeof(RectTransform),
                typeof(Image),
                typeof(Toggle));
            itemObject.transform.SetParent(content, false);
            RectTransform item = itemObject.GetComponent<RectTransform>();
            item.anchorMin = new Vector2(0f, 1f);
            item.anchorMax = new Vector2(1f, 1f);
            item.pivot = new Vector2(0.5f, 1f);
            item.sizeDelta = new Vector2(0f, ItemHeight);
            item.anchoredPosition = Vector2.zero;

            Image background = itemObject.GetComponent<Image>();
            background.color = new Color(0.14f, 0.095f, 0.055f, 0.98f);

            Image checkmark = UIElementFactory.CreateImage(
                "Item Checkmark",
                item);
            checkmark.color = new Color(1f, 0.55f, 0.12f, 1f);
            RectTransform checkRect = checkmark.rectTransform;
            checkRect.anchorMin = new Vector2(0f, 0f);
            checkRect.anchorMax = new Vector2(0f, 1f);
            checkRect.pivot = new Vector2(0f, 0.5f);
            checkRect.sizeDelta = new Vector2(4f, 0f);
            checkRect.anchoredPosition = Vector2.zero;

            Text label = UIElementFactory.CreateText(
                "Item Label",
                item,
                string.Empty,
                16,
                new Color(0.96f, 0.91f, 0.8f),
                TextAnchor.MiddleLeft);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 1f);
            labelRect.offsetMax = new Vector2(-8f, -1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            Toggle toggle = itemObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.83f, 0.55f, 1f);
            colors.pressedColor = new Color(0.8f, 0.6f, 0.36f, 1f);
            colors.selectedColor = new Color(1f, 0.74f, 0.42f, 1f);
            toggle.colors = colors;
            Navigation navigation = toggle.navigation;
            navigation.mode = Navigation.Mode.None;
            toggle.navigation = navigation;
        }

        private int RebuildOptions(int selectedIndex)
        {
            if (_dropdown == null)
                return -1;

            _refreshing = true;
            _visibleIndices.Clear();
            for (int i = 0; i < _cards.Count; i++)
                if (VfxTestCardSelectionPolicy.ShouldInclude(_cards[i].contentId, _filter))
                    _visibleIndices.Add(i);
            _dropdown.ClearOptions();
            int visibleIndex = _visibleIndices.IndexOf(selectedIndex);
            if (visibleIndex < 0) visibleIndex = 0;
            if (_visibleIndices.Count == 0)
            {
                _dropdown.AddOptions(new List<string>
                {
                    "当前筛选没有卡牌"
                });
                _dropdown.SetValueWithoutNotify(0);
                _dropdown.interactable = false;
            }
            else
            {
                var labels = new List<string>(_visibleIndices.Count);
                foreach (int index in _visibleIndices)
                    labels.Add(BuildOptionLabel(_cards[index], index));

                _dropdown.AddOptions(labels);
                _dropdown.SetValueWithoutNotify(visibleIndex);
                _dropdown.interactable = true;
            }

            _dropdown.RefreshShownValue();
            _refreshing = false;
            return GetSelectedIndex();
        }

        private static string BuildOptionLabel(CardConfig card, int index)
        {
            string status = CardTestStatus.IsReady(card.contentId)
                ? "已完成"
                : "待验收";
            return $"{index + 1:000}  [{status}]  {card.cardName}  · {card.volumeId}";
        }

        private void OnValueChanged(int selectedIndex)
        {
            if (_refreshing || selectedIndex < 0 ||
                selectedIndex >= _visibleIndices.Count)
                return;

            _onSelectionChanged?.Invoke(_visibleIndices[selectedIndex]);
        }

        private void OnDestroy()
        {
            if (_dropdown != null)
                _dropdown.onValueChanged.RemoveListener(OnValueChanged);
            if (_filterDropdown != null)
                _filterDropdown.onValueChanged.RemoveListener(OnFilterChanged);
        }

        private static void PlaceTopLeft(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }
}
