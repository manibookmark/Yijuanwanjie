using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 手牌 ViewModel
    /// 管理手牌排序、牌堆计数，纯 C# 可 headless 测试
    /// GDD 13.3: 牌库视图（手牌/抽牌堆/弃牌堆同屏）+ 卡牌排序（按颜色/按费用）
    /// </summary>
    public class HandViewModel : ViewModelBase
    {
        /// <summary>排序模式</summary>
        public enum SortMode
        {
            DrawOrder,  // 抽牌顺序（默认）
            ByCost,     // 按费用升序
            ByColor     // 按颜色分组
        }

        private CombatController _combat;
        private List<CardDisplayData> _sortedHand = new List<CardDisplayData>();
        private SortMode _currentSort = SortMode.DrawOrder;

        // 属性名常量
        public const string PROP_SORT_MODE = "SortMode";
        public const string PROP_HAND = "Hand";
        public const string PROP_PILE_COUNTS = "PileCounts";

        /// <summary>Actual effect-triggered plays, independent of a card's author flags.</summary>
        public event Action<int, bool> CardAutomaticallyPlayed;

        /// <summary>当前排序模式</summary>
        public SortMode CurrentSortMode => _currentSort;

        /// <summary>排序后的手牌列表</summary>
        public IReadOnlyList<CardDisplayData> SortedHand => _sortedHand;

        /// <summary>手牌数</summary>
        public int HandCount => _combat?.HandCount ?? 0;

        /// <summary>抽牌堆数</summary>
        public int DeckCount => _combat?.DeckCount ?? 0;

        /// <summary>弃牌堆数</summary>
        public int DiscardCount => _combat?.DiscardCount ?? 0;

        /// <summary>历史属性名；表示回合抽牌数，手牌持有无限制。</summary>
        public int HandLimit => _combat?.Player.shouPai ?? 5;

        /// <summary>
        /// 绑定战斗控制器
        /// </summary>
        public void Bind(CombatController combat)
        {
            if (_combat != null)
            {
                _combat.OnStateChanged -= OnCombatStateChanged;
                _combat.OnCardPlayResolved -= OnCardPlayResolved;
                _combat.OnDamagePreviewsReady -= Refresh;
            }
            _combat = combat;
            if (_combat != null)
            {
                _combat.OnStateChanged += OnCombatStateChanged;
                _combat.OnCardPlayResolved += OnCardPlayResolved;
                _combat.OnDamagePreviewsReady += Refresh;
            }
            Refresh();
            Log("✓ HandViewModel 已绑定");
        }

        /// <summary>
        /// 解绑
        /// </summary>
        public override void Dispose()
        {
            if (_combat != null)
            {
                _combat.OnStateChanged -= OnCombatStateChanged;
                _combat.OnCardPlayResolved -= OnCardPlayResolved;
                _combat.OnDamagePreviewsReady -= Refresh;
                _combat = null;
            }
            base.Dispose();
        }

        /// <summary>
        /// 监听明确的战斗状态变更
        /// </summary>
        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
            // This is the single hand snapshot owner. Stat/reward changes also
            // affect card previews, so retain the scene's former full coverage
            // without its duplicate RefreshAll broadcast.
            // CardPlayed may still be resolving. The separate preview-ready
            // event refreshes again after Core freezes the next legal plays.
            Refresh();
        }

        private void OnCardPlayResolved(CombatCardResolvedEvent result)
        {
            if (result?.Card == null || result.PlayOrigin != CardPlayOrigin.CardEffect) return;
            CardAutomaticallyPlayed?.Invoke(result.Card.runtimeInstanceId, result.DestroyedOnUse);
        }

        /// <summary>
        /// 刷新手牌数据
        /// </summary>
        public void Refresh()
        {
            if (_combat == null) return;

            _sortedHand.Clear();
            int might = _combat.Player != null ? _combat.Player.fengRui : 0;
            int amount = _combat.Player != null ? _combat.Player.amount : 0;
            long manaBeforePayment = (long)_combat.Mana.CurrentMana +
                _combat.Mana.TemporaryMana;
            foreach (var card in _combat.Hand)
            {
                CardDisplayData display = CardDisplayData.FromCard(
                    card, might: might, amount: amount,
                    manaBeforePayment: manaBeforePayment);
                display.attackDisplay = _combat.GetCardAttackDisplay(card);
                display.immediateDamageDisplay = _combat.GetCardImmediateDamageDisplay(card);
                display.displayCombo = _combat.GetComboPreview(card).ResultingCombo;
                display.usesCurrentPlayDisplay = true;
                _sortedHand.Add(display);
            }

            ApplySort();
            NotifyCollectionChanged(PROP_HAND);
            NotifyCollectionChanged(PROP_PILE_COUNTS);
        }

        /// <summary>
        /// 切换排序模式
        /// </summary>
        public void SetSortMode(SortMode mode)
        {
            if (_currentSort == mode) return;
            _currentSort = mode;
            Log($"📌 手牌排序 → {mode}");
            ApplySort();
            Set(ref _currentSort, mode, PROP_SORT_MODE);
            NotifyCollectionChanged(PROP_HAND);
        }

        /// <summary>
        /// 按费用排序
        /// </summary>
        public void SortByCost()
        {
            SetSortMode(SortMode.ByCost);
        }

        /// <summary>
        /// 按颜色排序
        /// </summary>
        public void SortByColor()
        {
            SetSortMode(SortMode.ByColor);
        }

        /// <summary>
        /// 恢复抽牌顺序
        /// </summary>
        public void SortByDrawOrder()
        {
            SetSortMode(SortMode.DrawOrder);
        }

        /// <summary>
        /// 应用当前排序
        /// </summary>
        private void ApplySort()
        {
            switch (_currentSort)
            {
                case SortMode.ByCost:
                    _sortedHand.Sort((a, b) => a.cost.CompareTo(b.cost));
                    break;

                case SortMode.ByColor:
                    _sortedHand.Sort((a, b) =>
                    {
                        int colorCmp = a.color.CompareTo(b.color);
                        return colorCmp != 0 ? colorCmp : a.cost.CompareTo(b.cost);
                    });
                    break;

                case SortMode.DrawOrder:
                default:
                    // 保持原顺序
                    break;
            }
        }

        /// <summary>
        /// 获取指定索引的卡牌（用于点击出牌）
        /// </summary>
        public CardConfig GetCardAt(int sortedIndex)
        {
            if (_combat == null || sortedIndex < 0 || sortedIndex >= _sortedHand.Count)
                return null;

            var displayData = _sortedHand[sortedIndex];
            // Runtime instance ID is the authoritative hand association.
            // Content ID remains a migration fallback for legacy test data.
            foreach (var card in _combat.Hand)
            {
                if (displayData.runtimeInstanceId != 0)
                {
                    if (card.runtimeInstanceId == displayData.runtimeInstanceId)
                        return card;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(displayData.contentId)
                    && string.Equals(
                        card.contentId,
                        displayData.contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return card;
                }

                if (string.IsNullOrWhiteSpace(displayData.contentId)
                    && card.cardName == displayData.name)
                    return card;
            }
            return null;
        }

        public override void RefreshAll()
        {
            Refresh();
            RaisePropertyChanged(PROP_SORT_MODE);
        }
    }
}
