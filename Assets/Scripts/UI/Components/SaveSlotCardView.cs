using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Persistence;

namespace WanjieScroll.UI.Components
{
    /// <summary>单个命册槽位的纯表现组件；不读取、不写入存档。</summary>
    public sealed class SaveSlotCardView : MonoBehaviour
    {
        private static readonly Color CardIdle = new Color(0.014f, 0.047f, 0.058f, 0.98f);
        private static readonly Color CardSelected = new Color(0.043f, 0.12f, 0.12f, 0.99f);
        private static readonly Color CardCorrupt = new Color(0.11f, 0.035f, 0.035f, 0.98f);

        private Image _background;
        private Outline _outline;
        private Image _selectionRail;
        private Image _indexBadge;
        private Text _indexNumber;
        private Text _slotName;
        private Text _status;
        private Text _headline;
        private Text _primaryMetric;
        private Text _secondaryMetric;
        private Text _collection;
        private Text _savedAt;
        private Button _button;

        public int Slot { get; private set; }
        public SaveSlotSummary Summary { get; private set; }
        public Button Button => _button;

        public void Initialize(int slot, Vector2 position, Action<int> onSelected)
        {
            Slot = slot;
            RectTransform rect = GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(336f, 396f);
            rect.anchoredPosition = position;

            _background = gameObject.AddComponent<Image>();
            _background.color = CardIdle;
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.38f);
            _outline.effectDistance = new Vector2(1f, -1f);

            _button = gameObject.AddComponent<Button>();
            _button.targetGraphic = _background;
            _button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = _button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.1f, 1f);
            colors.pressedColor = new Color(0.82f, 0.86f, 0.84f, 1f);
            colors.selectedColor = new Color(1.08f, 1.08f, 1.06f, 1f);
            colors.fadeDuration = 0.08f;
            _button.colors = colors;
            _button.onClick.AddListener(() => onSelected?.Invoke(Slot));

            var hover = gameObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.018f;

            BuildSelectionRail();
            BuildTopLine();
            BuildIndexBadge();
            BuildTextHierarchy();
        }

        public void Bind(SaveSlotSummary summary, bool activeSlot)
        {
            Summary = summary ?? new SaveSlotSummary { slot = Slot };
            _slotName.text = $"命册 · {ToChineseNumber(Slot)}";
            _indexNumber.text = ToChineseNumber(Slot);

            bool exists = Summary.exists;
            bool corrupt = exists && Summary.corrupt;
            _status.text = corrupt ? "卷宗受损" : activeSlot && exists ? "续行之卷" : exists ? "已有行迹" : "空卷";
            _status.color = corrupt
                ? JadePanelView.Danger
                : activeSlot && exists ? JadePanelView.Gold : JadePanelView.JadeMuted;

            _background.color = corrupt ? CardCorrupt : CardIdle;
            _indexBadge.color = corrupt
                ? new Color(0.34f, 0.085f, 0.072f, 0.96f)
                : exists
                    ? new Color(0.08f, 0.24f, 0.20f, 0.98f)
                    : new Color(0.045f, 0.10f, 0.105f, 0.96f);
            _indexNumber.color = corrupt
                ? new Color(1f, 0.72f, 0.66f, 1f)
                : exists ? JadePanelView.Gold : JadePanelView.JadeMuted;

            if (!exists)
            {
                SetEmptyState();
                return;
            }

            if (corrupt)
            {
                SetCorruptState();
                return;
            }

            _headline.text = $"当前关卡 · 第 {Summary.clearedLevels + 1} 关";
            _headline.color = JadePanelView.TextPrimary;
            _primaryMetric.text = $"{Summary.clearedLevels}";
            _secondaryMetric.text = $"{Summary.metaLingyu}";
            _collection.text =
                $"遗物  {Summary.relicCount}    秘宝  {Summary.arcanasCount}    英灵  {Summary.crawlersCount}";
            _savedAt.text = FormatSaveTime(Summary);
        }

        public void SetSelected(bool selected)
        {
            bool corrupt = Summary != null && Summary.exists && Summary.corrupt;
            _background.color = corrupt
                ? (selected ? new Color(0.18f, 0.055f, 0.048f, 0.99f) : CardCorrupt)
                : selected ? CardSelected : CardIdle;
            _outline.effectColor = selected
                ? JadePanelView.Gold
                : corrupt
                    ? new Color(JadePanelView.Danger.r, JadePanelView.Danger.g,
                        JadePanelView.Danger.b, 0.52f)
                    : new Color(JadePanelView.Jade.r, JadePanelView.Jade.g,
                        JadePanelView.Jade.b, 0.38f);
            _outline.effectDistance = selected ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            _selectionRail.gameObject.SetActive(selected);
        }

        private void BuildSelectionRail()
        {
            var rail = new GameObject("SelectionRail", typeof(Image));
            rail.transform.SetParent(transform, false);
            RectTransform rect = rail.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(4f, -24f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            _selectionRail = rail.GetComponent<Image>();
            _selectionRail.color = JadePanelView.Gold;
            _selectionRail.raycastTarget = false;
            rail.SetActive(false);
        }

        private void BuildTopLine()
        {
            var line = new GameObject("TopAccent", typeof(Image));
            line.transform.SetParent(transform, false);
            RectTransform rect = line.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-28f, 2f);
            rect.anchoredPosition = new Vector2(0f, -12f);
            Image image = line.GetComponent<Image>();
            image.color = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.46f);
            image.raycastTarget = false;
        }

        private void BuildIndexBadge()
        {
            var badge = new GameObject("SlotIndexBadge", typeof(Image), typeof(Outline));
            badge.transform.SetParent(transform, false);
            RectTransform rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(58f, 58f);
            rect.anchoredPosition = new Vector2(24f, -28f);
            _indexBadge = badge.GetComponent<Image>();
            _indexBadge.color = new Color(0.045f, 0.10f, 0.105f, 0.96f);
            _indexBadge.raycastTarget = false;
            Outline badgeOutline = badge.GetComponent<Outline>();
            badgeOutline.effectColor = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.54f);
            badgeOutline.effectDistance = new Vector2(1f, -1f);

            _indexNumber = JadePanelView.CreateText(
                "Number", badge.transform, ToChineseNumber(Slot), 24,
                JadePanelView.JadeMuted, TextAnchor.MiddleCenter, true);
            JadePanelView.Stretch(_indexNumber.rectTransform);
        }

        private void BuildTextHierarchy()
        {
            _slotName = MakeText("SlotName", "", 19, JadePanelView.TextPrimary,
                TextAnchor.MiddleLeft, new Vector2(96f, -30f), new Vector2(132f, 30f), true);
            _status = MakeText("Status", "", 15, JadePanelView.JadeMuted,
                TextAnchor.MiddleRight, new Vector2(-22f, -30f), new Vector2(104f, 30f), false, true);

            _headline = MakeText("Headline", "", 26, JadePanelView.TextPrimary,
                TextAnchor.MiddleLeft, new Vector2(24f, -116f), new Vector2(288f, 42f), true);

            CreateMetricBlock("ProgressMetric", "已通关", new Vector2(24f, -174f), out _primaryMetric);
            CreateMetricBlock("JadeMetric", "灵玉", new Vector2(174f, -174f), out _secondaryMetric);

            _collection = MakeText("Collection", "", 16, JadePanelView.TextSecondary,
                TextAnchor.MiddleLeft, new Vector2(24f, -270f), new Vector2(288f, 28f));

            var divider = new GameObject("FooterDivider", typeof(Image));
            divider.transform.SetParent(transform, false);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.sizeDelta = new Vector2(-48f, 1f);
            dividerRect.anchoredPosition = new Vector2(0f, -310f);
            divider.GetComponent<Image>().color = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.24f);
            divider.GetComponent<Image>().raycastTarget = false;

            _savedAt = MakeText("SavedAt", "", 15, JadePanelView.TextSecondary,
                TextAnchor.MiddleLeft, new Vector2(24f, -330f), new Vector2(288f, 28f));
        }

        private void CreateMetricBlock(
            string name,
            string label,
            Vector2 position,
            out Text valueText)
        {
            var metric = new GameObject(name, typeof(Image));
            metric.transform.SetParent(transform, false);
            RectTransform rect = metric.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(138f, 76f);
            rect.anchoredPosition = position;
            Image background = metric.GetComponent<Image>();
            background.color = new Color(0.055f, 0.12f, 0.125f, 0.58f);
            background.raycastTarget = false;

            Text labelText = JadePanelView.CreateText(
                "Label", metric.transform, label, 15,
                JadePanelView.TextSecondary, TextAnchor.UpperLeft);
            JadePanelView.Stretch(labelText.rectTransform, new Vector2(16f, 8f), new Vector2(-12f, -8f));

            valueText = JadePanelView.CreateText(
                "Value", metric.transform, "—", 28,
                JadePanelView.Gold, TextAnchor.LowerRight, true);
            JadePanelView.Stretch(valueText.rectTransform, new Vector2(12f, 8f), new Vector2(-16f, -8f));
        }

        private Text MakeText(
            string name,
            string value,
            int fontSize,
            Color color,
            TextAnchor alignment,
            Vector2 position,
            Vector2 size,
            bool heavy = false,
            bool rightAnchored = false)
        {
            Text text = JadePanelView.CreateText(
                name, transform, value, fontSize, color, alignment, heavy);
            RectTransform rect = text.rectTransform;
            float anchorX = rightAnchored ? 1f : 0f;
            rect.anchorMin = new Vector2(anchorX, 1f);
            rect.anchorMax = new Vector2(anchorX, 1f);
            rect.pivot = new Vector2(anchorX, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return text;
        }

        private void SetEmptyState()
        {
            _headline.text = "此卷尚未落笔";
            _headline.color = JadePanelView.TextSecondary;
            _primaryMetric.text = "—";
            _secondaryMetric.text = "—";
            _collection.text = string.Empty;
            _savedAt.text = "未建立存档";
        }

        private void SetCorruptState()
        {
            _headline.text = "卷宗数据无法识读";
            _headline.color = JadePanelView.Danger;
            _primaryMetric.text = "!";
            _secondaryMetric.text = "—";
            _collection.text = string.Empty;
            _savedAt.text = "读取失败";
        }

        private static string ToChineseNumber(int slot)
        {
            switch (slot)
            {
                case 1: return "壹";
                case 2: return "贰";
                case 3: return "叁";
                default: return slot.ToString();
            }
        }

        private static string FormatSaveTime(SaveSlotSummary summary)
        {
            if (DateTime.TryParse(summary.savedAtUtc, null,
                    DateTimeStyles.RoundtripKind, out DateTime parsed))
                return parsed.ToLocalTime().ToString("yyyy.MM.dd  HH:mm");
            return "保存时间未知";
        }
    }
}
