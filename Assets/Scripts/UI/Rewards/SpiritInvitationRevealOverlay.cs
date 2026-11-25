using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Presentations;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>Owns the short, automatic invitation presentation. It consumes
    /// actual actor frames and the granted card, never story/unlock history or
    /// combat residency. It has no reward action, confirmation or persistence.</summary>
    public sealed class SpiritInvitationRevealOverlay : MonoBehaviour
    {
        public GameObject Root { get; private set; }
        public bool IsShown => Root != null;
        private Coroutine _animation;
        private CanvasGroup _group;
        private CanvasGroup _actorGroup;
        private RawImage _actor;
        private RectTransform _actorRect;
        private RectTransform _cardRect;
        private CanvasGroup _cardGroup;
        private Text _title;
        private Image _sheen;

        public void Show(Canvas canvas, CharacterPresentationProfile actor, CardConfig granted, Action onClosed)
        {
            if (canvas == null || actor == null || granted == null)
                throw new ArgumentException("请灵显形需要实际英灵演出与已获得的召唤牌。");
            Close();
            Root = RewardPanelFactory.CreateRoot(canvas, "SpiritInvitationReveal", false, out _group, out _);
            _group.alpha = 1f;
            var panel = RewardPanelFactory.CreatePanel(Root.transform, "InvitationPaper", new Vector2(1100, 860), out _);
            _title = RewardPanelFactory.CreateText(panel, "Title", "英灵应邀", 38,
                TextAnchor.MiddleCenter, RewardPanelFactory.PrimaryText);
            Place(_title.rectTransform, new Vector2(0, 355), new Vector2(800, 70));

            var sheen = new GameObject("PaperSheen", typeof(RectTransform), typeof(Image));
            sheen.transform.SetParent(panel, false);
            _sheen = sheen.GetComponent<Image>();
            _sheen.sprite = CardPresentationProfile.LoadDefault()?.paperFiberSprite;
            _sheen.color = new Color(.72f, .86f, .77f, .04f);
            _sheen.raycastTarget = false;
            Place(_sheen.rectTransform, new Vector2(0, -10), new Vector2(850, 650));

            var actorObject = new GameObject("InvitedSpirit", typeof(RectTransform), typeof(CanvasGroup), typeof(RawImage));
            actorObject.transform.SetParent(panel, false);
            _actor = actorObject.GetComponent<RawImage>();
            _actor.texture = actor.ResolvedFrameAtlas;
            _actor.raycastTarget = false;
            _actorRect = actorObject.GetComponent<RectTransform>();
            Place(_actorRect, new Vector2(0, -10), new Vector2(610 * actor.FrameAspect, 610));
            _actorGroup = actorObject.GetComponent<CanvasGroup>();

            var card = CardViewFactory.Create(panel, CardPresentationProfile.LoadDefault(), addHoverHandler: false);
            card.UpdateCard(CardDisplayData.FromCard(granted, canPlay: true), 0);
            _cardRect = card.transform as RectTransform;
            Place(_cardRect, new Vector2(0, -10), CardPresentationProfile.LoadDefault().referenceSize);
            _cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            _cardGroup.alpha = 0;
            _cardGroup.interactable = false;
            _cardGroup.blocksRaycasts = false;
            _animation = StartCoroutine(Animate(actor, onClosed));
        }

        private IEnumerator Animate(CharacterPresentationProfile profile, Action onClosed)
        {
            float elapsed = 0;
            const float materialize = .38f, display = 1.35f, condense = .40f, hold = .38f, returnTime = .48f;
            float total = materialize + display + condense + hold + returnTime;
            while (Root != null && elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                int frame = Mathf.Min(profile.ResolvedFrameCount - 1,
                    Mathf.FloorToInt(Mathf.Clamp01(elapsed / (materialize + display)) * profile.ResolvedFrameCount));
                _actor.uvRect = profile.GetUvRect(frame);
                float appear = Mathf.SmoothStep(0, 1, elapsed / materialize);
                float gather = Mathf.SmoothStep(0, 1, (elapsed - materialize - display) / condense);
                float returning = Mathf.SmoothStep(0, 1,
                    (elapsed - materialize - display - condense - hold) / returnTime);
                _actorGroup.alpha = appear * (1 - gather);
                _actorRect.localScale = Vector3.one * Mathf.Lerp(.96f, 1, appear) * Mathf.Lerp(1, .55f, gather);
                _sheen.color = new Color(.72f, .86f, .77f, .09f * Mathf.Sin(Mathf.Clamp01(elapsed / 2.2f) * Mathf.PI));
                _cardGroup.alpha = gather * (1 - returning);
                _cardRect.localScale = Vector3.one * Mathf.Lerp(.8f, 1.5f, gather) * Mathf.Lerp(1, .12f, returning);
                _cardRect.anchoredPosition = Vector2.Lerp(new Vector2(0, -10), new Vector2(420, -350), returning);
                if (gather > .9f) _title.text = "已加入本局牌组";
                _group.alpha = 1 - returning;
                yield return null;
            }
            _animation = null;
            Close();
            onClosed?.Invoke();
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        public void Close()
        {
            if (_animation != null) StopCoroutine(_animation);
            _animation = null;
            if (_group != null) { _group.blocksRaycasts = false; _group.interactable = false; }
            if (Root != null) { Root.SetActive(false); Destroy(Root); }
            Root = null;
        }
        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
