using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 升级界面 ViewModel
    /// GDD 13.3: 三选一卡牌/宝石，曾进化过的组合会提示，符录可查询进化图鉴
    /// </summary>
    public class LevelUpViewModel : ViewModelBase
    {
        private LevelUpSystem _levelUp;
        private List<CardConfig> _deck;
        private List<RewardDisplayData> _rewards = new List<RewardDisplayData>();
        private List<LevelUpSystem.Reward> _cachedRewards = new List<LevelUpSystem.Reward>();
        private bool _isPending = false;

        public const string PROP_REWARDS = "Rewards";
        public const string PROP_PENDING = "IsPending";

        /// <summary>当前奖励选项</summary>
        public IReadOnlyList<RewardDisplayData> Rewards => _rewards;

        /// <summary>是否有待选择的升级奖励</summary>
        public bool IsPending => _isPending;

        /// <summary>奖励数量</summary>
        public int RewardCount => _rewards.Count;

        /// <summary>
        /// 绑定升级系统
        /// </summary>
        public void Bind(LevelUpSystem levelUp, List<CardConfig> deck)
        {
            _levelUp = levelUp;
            _deck = deck;
            Log("✓ LevelUpViewModel 已绑定");
        }

        public override void Dispose()
        {
            _levelUp = null;
            _deck = null;
            base.Dispose();
        }

        /// <summary>
        /// 生成奖励选项（升级时调用）
        /// 缓存原始 Reward 对象以便后续选择
        /// </summary>
        public void GenerateRewards(int count = 3)
        {
            if (_levelUp == null)
            {
                Log("⚠ LevelUpSystem 未绑定，无法生成奖励");
                return;
            }

            _cachedRewards = _levelUp.GenerateRewards(count);
            _rewards.Clear();

            foreach (var reward in _cachedRewards)
            {
                _rewards.Add(RewardDisplayData.FromReward(reward));
            }

            _isPending = true;
            Log($"★ 升级奖励已生成: {_rewards.Count} 个选项");
            for (int i = 0; i < _rewards.Count; i++)
            {
                var r = _rewards[i];
                string evolve = r.canEvolve ? $" {r.evolutionHint}" : "";
                Log($"  [{i}] {r.name} - {r.description}{evolve}");
            }

            NotifyCollectionChanged(PROP_REWARDS);
            RaisePropertyChanged(PROP_PENDING);
        }

        /// <summary>
        /// 选择奖励（玩家点击后调用）
        /// </summary>
        public bool SelectReward(int index)
        {
            if (_levelUp == null || _deck == null)
            {
                Log("⚠ 升级系统未绑定");
                return false;
            }

            if (index < 0 || index >= _cachedRewards.Count)
            {
                Log($"⚠ 无效奖励索引: {index}");
                return false;
            }

            var chosen = _cachedRewards[index];
            _levelUp.SelectReward(chosen, _deck);

            Log($"✓ 已选择奖励: {chosen.GetDisplay()}");

            _isPending = false;
            _rewards.Clear();
            _cachedRewards.Clear();

            RaisePropertyChanged(PROP_PENDING);
            NotifyCollectionChanged(PROP_REWARDS);
            return true;
        }

        /// <summary>
        /// 检查卡牌是否可进化（用于升级界面提示）
        /// </summary>
        public static bool CanEvolve(CardConfig card)
        {
            return card?.evolutionTarget != null;
        }

        /// <summary>
        /// 获取进化提示文本
        /// </summary>
        public static string GetEvolutionHint(CardConfig card)
        {
            if (card?.evolutionTarget == null) return null;
            return $"→ 可进化为「{card.evolutionTarget.cardName}」";
        }
    }
}
