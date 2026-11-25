using UnityEngine;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌对象池：管理 CardView 实例的创建、复用、隐藏和回调绑定。
    /// </summary>
    public class CardViewPool
    {
        private readonly RectTransform _cardContainer;
        private readonly CardView _cardPrefab;
        private readonly CardPresentationProfile _presentation;

        private System.Action<int> _clickCallback;
        private System.Action<int> _hoverEnterCallback;
        private System.Action _hoverExitCallback;

        private CardView[] _cardViews;

        public CardViewPool(RectTransform cardContainer, CardView cardPrefab,
            CardPresentationProfile presentation)
        {
            _cardContainer = cardContainer;
            _cardPrefab = cardPrefab;
            _presentation = presentation ?? CardPresentationProfile.LoadDefault();
        }

        public int Count => _cardViews?.Length ?? 0;

        public CardView this[int index] =>
            _cardViews != null && index >= 0 && index < _cardViews.Length
                ? _cardViews[index] : null;

        public CardView GetCardView(int index) => this[index];

        public void SetCallbacks(System.Action<int> clickCallback,
            System.Action<int> hoverEnter, System.Action hoverExit)
        {
            _clickCallback = clickCallback;
            _hoverEnterCallback = hoverEnter;
            _hoverExitCallback = hoverExit;
            if (_cardViews == null) return;
            foreach (var cv in _cardViews) WireCallbacks(cv);
        }

        public void EnsureCardViews(int count)
        {
            if (_cardViews != null && _cardViews.Length >= count)
            {
                for (int i = 0; i < count; i++)
                    if (_cardViews[i] == null) _cardViews[i] = CreateCardView();
                return;
            }

            int newSize = Mathf.Max(count, 5);
            CardView[] previous = _cardViews;
            _cardViews = new CardView[newSize];
            if (previous != null)
                System.Array.Copy(previous, _cardViews,
                    Mathf.Min(previous.Length, newSize));

            for (int i = 0; i < newSize; i++)
                if (_cardViews[i] == null) _cardViews[i] = CreateCardView();
        }

        public void HideAll()
        {
            if (_cardViews == null) return;
            for (int i = 0; i < _cardViews.Length; i++)
            {
                if (_cardViews[i] != null)
                    _cardViews[i].gameObject.SetActive(false);
            }
        }

        public void HideExtras(int fromIndex)
        {
            if (_cardViews == null) return;
            for (int i = fromIndex; i < _cardViews.Length; i++)
            {
                if (_cardViews[i] != null)
                    _cardViews[i].gameObject.SetActive(false);
            }
        }

        public void RemoveCardView(CardView cardView)
        {
            if (cardView == null || _cardViews == null) return;
            for (int i = 0; i < _cardViews.Length; i++)
            {
                if (_cardViews[i] == cardView)
                {
                    _cardViews[i] = null;
                    return;
                }
            }
        }

        public void SetCardView(int index, CardView cardView)
        {
            if (_cardViews != null && index >= 0 && index < _cardViews.Length)
                _cardViews[index] = cardView;
        }

        private CardView CreateCardView()
        {
            CardView view = _cardPrefab != null
                ? Object.Instantiate(_cardPrefab, _cardContainer)
                : CardViewFactory.Create(_cardContainer, _presentation);
            view.SetPresentationProfile(_presentation);
            WireCallbacks(view);
            return view;
        }

        private void WireCallbacks(CardView cardView)
        {
            if (cardView == null) return;
            if (cardView.cardButton != null)
            {
                cardView.cardButton.onClick.RemoveAllListeners();
                if (_clickCallback != null)
                {
                    CardView captured = cardView;
                    cardView.cardButton.onClick.AddListener(() =>
                    {
                        var hover = captured.GetComponent<CardHoverHandler>();
                        if (hover != null && hover.IsHovering)
                            hover.ExitHover();
                        _clickCallback(captured.SortedIndex);
                    });
                }
            }
            var hoverComp = cardView.GetComponent<CardHoverHandler>();
            if (hoverComp != null)
            {
                hoverComp.OnHoverEnter = _hoverEnterCallback;
                hoverComp.OnHoverExit = _hoverExitCallback;
            }
        }
    }
}
