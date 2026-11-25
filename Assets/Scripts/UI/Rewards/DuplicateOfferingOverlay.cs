using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>Owns only the rubbing card wall, readonly result preview and
    /// committed return animation. Domain candidates and grants are injected;
    /// hovering never submits, allocates inventory or changes the source card.</summary>
    public sealed class DuplicateOfferingOverlay : MonoBehaviour
    {
        public GameObject Root { get; private set; }
        public bool IsShown => Root != null;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<RewardCardHoverMotion> _hoverStates = new List<RewardCardHoverMotion>();
        private IReadOnlyList<CardConfig> _originals, _results;
        private int _hoveredIndex = -1;
        private CanvasGroup _group;
        private RectTransform _panel;
        private CardView _preview;
        private Text _hint;
        private Button _back;
        private Coroutine _animation;
        private CardTooltipView _tooltip;
        private bool _pending;

        public void Show(Canvas canvas, IReadOnlyList<CardConfig> originals,
            IReadOnlyList<CardConfig> results, Action<int> onSelected, Action onBack)
        {
            CloseSilently();
            if (canvas == null || originals == null || results == null || originals.Count != results.Count)
                throw new ArgumentException("拓印预览必须与真实候选逐一对应。");
            Build(canvas, "拓印处 · 选择一张牌");
            _originals = originals; _results = results;
            var viewport = Rect(_panel, "CandidateViewport", RewardPanelFactory.CandidateAreaMin, RewardPanelFactory.CandidateAreaMax);
            var image = viewport.gameObject.AddComponent<Image>(); image.color = new Color(0, 0, 0, .001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 42;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = Rect(viewport, "CandidateContent", new Vector2(0, 1), Vector2.one);
            content.pivot = new Vector2(.5f, 1);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(22, 22, 16, 24); grid.cellSize = new Vector2(276, 382);
            grid.spacing = new Vector2(18, 24); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperLeft;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var rail = RewardPanelFactory.CreateResultRail(_panel, "DuplicateResultRail", "拓印所得");
            _preview = CardViewFactory.Create(rail, addHoverHandler: false);
            var previewRect = (RectTransform)_preview.transform;
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(.5f, .62f);
            previewRect.anchoredPosition = Vector2.zero; previewRect.localScale = Vector3.one * .8f;
            _preview.cardButton.interactable = false;
            _hint = RewardPanelFactory.CreateText(rail, "DuplicateRules", "保留原牌\n复制当前形态、灵印和印位\n副本突破归零", 20, TextAnchor.MiddleCenter, RewardPanelFactory.SecondaryText);
            Place(_hint.rectTransform, new Vector2(.04f, .2f), new Vector2(.96f, .38f));
            _back = RewardPanelFactory.CreateButton(rail, "BackButton", "暂别", RewardPanelFactory.NeutralAction);
            Place((RectTransform)_back.transform, new Vector2(.1f, .04f), new Vector2(.9f, .13f));
            _back.onClick.AddListener(() => { if (!_pending) onBack?.Invoke(); });
            var tooltipObject = new GameObject("DuplicateResultTooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(Root.transform, false);
            _tooltip = tooltipObject.AddComponent<CardTooltipView>();
            for (int index = 0; index < originals.Count; index++)
            {
                int captured = index;
                var slot = Rect(content, "Candidate_" + index, Vector2.zero, Vector2.one);
                var card = CardViewFactory.Create(slot, addHoverHandler: false);
                var cardRect = (RectTransform)card.transform;
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
                cardRect.anchoredPosition = Vector2.zero;
                card.UpdateCard(CardDisplayData.FromCard(originals[index]), index);
                var scale = slot.gameObject.AddComponent<UI.Components.HoverScaleEffect>();
                scale.NormalScale = 1; scale.HoverScale = 1.045f;
                // Shared reward hover handlers leave scroll/drag bubbling to the
                // ScrollRect; EventTrigger would swallow those events on cards.
                var motion = card.gameObject.AddComponent<RewardCardHoverMotion>(); motion.Bind(card);
                _hoverStates.Add(motion);
                card.gameObject.AddComponent<RewardCardTooltipPresenter>().Bind(results[index], _tooltip, null);
                card.cardButton.onClick.AddListener(() => {
                    if (_pending) return; _pending = true; SetInteractable(false);
                    _tooltip.Hide(); ShowPreview(originals[captured], results[captured], captured);
                    onSelected?.Invoke(originals[captured].runtimeInstanceId);
                });
                _buttons.Add(card.cardButton);
            }
            if (results.Count > 0) ShowPreview(originals[0], results[0], 0);
            else
            {
                _preview.gameObject.SetActive(false);
                var empty = RewardPanelFactory.CreateText(viewport, "EmptyPool", "当前没有可拓印的卡牌", 30, TextAnchor.MiddleCenter, RewardPanelFactory.PrimaryText);
                RewardPanelFactory.Stretch(empty.rectTransform);
            }
        }

        private void ShowPreview(CardConfig original, CardConfig result, int index)
        {
            _preview.UpdateCard(CardDisplayData.FromCard(result), index);
            _hint.text = "保留原牌 · 新增右侧副本\n形态、灵印、印位保留\n突破：" +
                original.LimitBreakCount + " → " + result.LimitBreakCount;
        }

        private void LateUpdate()
        {
            if (_pending || !IsShown) return;
            for (int index = 0; index < _hoverStates.Count; index++)
                if (_hoverStates[index].IsHovered)
                {
                    if (_hoveredIndex != index) ShowPreview(_originals[index], _results[index], index);
                    _hoveredIndex = index; return;
                }
            _hoveredIndex = -1;
        }

        public void PlayCommitted(Canvas canvas, CardConfig result, Action onClosed)
        {
            CloseSilently(); Build(canvas, "拓印完成"); _pending = true;
            var card = CardViewFactory.Create(_panel, addHoverHandler: false);
            card.UpdateCard(CardDisplayData.FromCard(result), 0); card.cardButton.interactable = false;
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
            var glow = new GameObject("InkReveal", typeof(RectTransform), typeof(Image));
            glow.transform.SetParent(rect, false); glow.transform.SetAsFirstSibling();
            RewardPanelFactory.Stretch((RectTransform)glow.transform, -6);
            var wash = glow.GetComponent<Image>(); wash.color = new Color(.58f, .77f, .71f, .24f); wash.raycastTarget = false;
            _animation = StartCoroutine(ReturnCopy(rect, wash, onClosed));
        }

        private IEnumerator ReturnCopy(RectTransform card, Image ink, Action onClosed)
        {
            // A short ink impression reveals the actual saved copy; automatic
            // return shares the map reward's unobtrusive lower-left direction.
            for (float t = 0; t < .65f; t += Time.unscaledDeltaTime)
            {
                float progress = Mathf.Clamp01(t / .65f);
                card.localScale = Vector3.one * Mathf.Lerp(.94f, 1, progress);
                ink.color = new Color(.58f, .77f, .71f, .26f * (1 - progress));
                yield return null;
            }
            for (float t = 0; t < .7f; t += Time.unscaledDeltaTime) yield return null;
            Vector2 start = card.anchoredPosition;
            Vector2 end = new Vector2(-_panel.rect.width * .43f, -_panel.rect.height * .47f);
            for (float t = 0; t < .42f; t += Time.unscaledDeltaTime)
            {
                float progress = Mathf.Clamp01(t / .42f);
                card.anchoredPosition = Vector2.Lerp(start, end, progress * progress);
                card.localScale = Vector3.one * Mathf.Lerp(1, .08f, progress);
                _group.alpha = 1 - progress;
                yield return null;
            }
            _animation = null; CloseSilently(); onClosed?.Invoke();
        }

        private void Build(Canvas canvas, string title)
        {
            Root = RewardPanelFactory.CreateRoot(canvas, "DuplicateOfferingOverlay", false, out _group, out _);
            _group.alpha = 1;
            _panel = RewardPanelFactory.CreatePanel(Root.transform, "DuplicateOfferingPanel", new Vector2(1660, 860), out _);
            _panel.localScale = Vector3.one;
            RewardPanelFactory.CreateHeader(_panel, title);
        }

        private void SetInteractable(bool value)
        { foreach (var button in _buttons) button.interactable = value; if (_back != null) _back.interactable = value; }

        public void CloseSilently()
        {
            if (_animation != null) StopCoroutine(_animation); _animation = null;
            _tooltip?.Hide(); _tooltip = null;
            if (_group != null) { _group.blocksRaycasts = false; _group.interactable = false; }
            if (Root != null) { Root.SetActive(false); Destroy(Root); }
            Root = null; _group = null; _panel = null; _preview = null; _hint = null; _back = null;
            _buttons.Clear(); _pending = false;
            _hoverStates.Clear(); _originals = _results = null; _hoveredIndex = -1;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); Place(rect, min, max); return rect; }
        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
