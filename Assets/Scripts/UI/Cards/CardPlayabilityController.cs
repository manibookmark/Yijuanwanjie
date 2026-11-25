using UnityEngine;
using WanjieScroll.UI.Cards;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌可出牌状态管理器：统一控制手牌交互与费用可支付状态。
    /// 灵力不足只弱化费用文字，不压暗卡面；全局输入锁定不改变费用语义。
    /// </summary>
    public class CardPlayabilityController
    {
        private System.Func<int, bool> _lastChecker;
        private bool _interactionEnabled = true;
        private readonly System.Func<int, bool> _isPresentationReady;

        public CardPlayabilityController(System.Func<int, bool> isPresentationReady = null)
        {
            _isPresentationReady = isPresentationReady;
        }

        public bool InteractionEnabled => _interactionEnabled;

        public void UpdatePlayability(CardViewPool pool,
            System.Func<int, bool> canPlayChecker)
        {
            _lastChecker = canPlayChecker;
            RefreshLastState(pool);
        }

        /// <summary>
        /// 全局可交互状态与可支付状态分离：全局状态不改变“是否可支付”的语义表达。
        /// </summary>
        public void SetInteractionEnabled(CardViewPool pool, bool enabled)
        {
            _interactionEnabled = enabled;
            RefreshLastState(pool);
        }

        public void RefreshLastState(CardViewPool pool)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Count; i++)
            {
                var cv = pool[i];
                if (cv == null) continue;

                bool canPlay = _lastChecker != null
                    ? _lastChecker(cv.SortedIndex)
                    : cv.CardData.canPlay;
                bool ready = _isPresentationReady?.Invoke(cv.CardData.runtimeInstanceId) ?? true;
                CanvasGroup group = cv.GetComponent<CanvasGroup>();
                bool interactive = _interactionEnabled && ready &&
                    (group == null || group.alpha >= 0.999f);
                cv.GetComponent<CardHoverHandler>()?.SetInteractionEnabled(interactive);
                if (cv.cardButton != null)
                    cv.cardButton.interactable = interactive && canPlay;

                if (group != null)
                {
                    group.interactable = interactive;
                    group.blocksRaycasts = interactive;
                }

                cv.SetAffordability(canPlay);
            }
        }
    }
}
