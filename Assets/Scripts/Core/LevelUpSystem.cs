using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Run-owned consumable budget used by reward selection. Core knows only
    /// the count and atomic consume operation; persistence and map grants stay
    /// outside the combat module.
    /// </summary>
    public interface IRewardRerollBank
    {
        int RewardRerollsRemaining { get; }
        bool TryConsumeRewardReroll();
    }

    /// <summary>
    /// 升级选择系统
    /// 固定奖励、旧卡牌／属性规则，以及显式自然卡牌／灵印池共用提交入口。
    /// </summary>
    public class LevelUpSystem
    {
        public enum RewardType
        {
            NewCard,    // 新卡牌
            Gem,        // 灵印；由调用方通过宿主选择事务应用
            StatBoost   // 属性提升
        }

        public class Reward
        {
            public RewardType type;
            public string name;
            public string description;
            public CardConfig card;        // type=NewCard 时有效
            /// <summary>
            /// type=Gem 时有效。这里保存的是可持久化的规则规格，不暴露
            /// LingyinConfig 或任何 UI 作者资源给 Core。
            /// </summary>
            public CardSealSpec seal;
            public StatType statType;      // type=StatBoost 时有效
            public int statAmount;         // type=StatBoost 时有效

            public Reward Clone()
            {
                return new Reward
                {
                    type = type,
                    name = name,
                    description = description,
                    card = card,
                    seal = seal?.Clone(),
                    statType = statType,
                    statAmount = statAmount
                };
            }

            public string GetDisplay()
            {
                return type switch
                {
                    RewardType.NewCard => $"新卡牌: {name}\n{description}",
                    RewardType.Gem => $"灵印: {name}\n{description}",
                    RewardType.StatBoost => $"属性提升: {name} +{statAmount}\n{description}",
                    _ => name
                };
            }
        }

        private PlayerStats _player;
        private List<CardConfig> _availableCards;
        private readonly RewardConfig _rewardConfig;
        private readonly Func<int> _getLuck;
        private readonly Action<StatType, int> _applyStatBoost;
        private IRandomSource _random;
        private List<CardConfig> _fixedFirstRewardCards;
        private readonly ILevelUpRewardOfferResolver _fixedOfferResolver;
        private readonly NaturalLevelUpRewardPool _naturalRewardPool;
        private readonly Func<CardSealSpec, bool> _hasLegalSealHost;

        /// <summary>
        /// Metadata for the most recently generated group.  CombatController
        /// owns its pending state, but must preserve the author's explicit
        /// cash-out boundary while that group is awaiting selection.
        /// </summary>
        public bool CurrentOfferAllowsCashOut { get; private set; } = true;
        public string CurrentOfferContentId { get; private set; }
        public bool CurrentOfferIsFixedFirstReward { get; private set; }
        public LevelUpRewardRequest CurrentRewardRequest { get; private set; }

        public event Action<Reward> OnRewardSelected;

        public LevelUpSystem(PlayerStats player, List<CardConfig> availableCards,
            RewardConfig rewardConfig = null, Func<int> getLuck = null,
            IRandomSource random = null,
            IReadOnlyList<CardConfig> fixedFirstRewardCards = null,
            Action<StatType, int> applyStatBoost = null,
            ILevelUpRewardOfferResolver fixedOfferResolver = null,
            NaturalLevelUpRewardPool naturalRewardPool = null,
            Func<CardSealSpec, bool> hasLegalSealHost = null)
        {
            _player = player;
            _availableCards = availableCards;
            _rewardConfig = rewardConfig ?? new RewardConfig();
            _getLuck = getLuck ?? (() => Math.Max(0, player?.tianYun ?? 0));
            _applyStatBoost = applyStatBoost;
            _random = random ?? DefaultRandomSource.Instance;
            _fixedOfferResolver = fixedOfferResolver;
            _naturalRewardPool = naturalRewardPool;
            _hasLegalSealHost = hasLegalSealHost;
            if (_rewardConfig.useNaturalCardSealPool &&
                (_naturalRewardPool == null || _hasLegalSealHost == null))
                throw new InvalidOperationException("自然升级规则缺少卡印池或真实宿主资格检查。");
            if (_naturalRewardPool?.LevelCostPolicy != null && _hasLegalSealHost == null)
            {
                if (_naturalRewardPool.SealCount > 0)
                    throw new InvalidOperationException("等级费用奖励缺少真实灵印宿主资格检查。");
                _hasLegalSealHost = _ => false;
            }
            if (fixedFirstRewardCards != null &&
                fixedFirstRewardCards.Count > 0)
            {
                _fixedFirstRewardCards =
                    new List<CardConfig>(fixedFirstRewardCards.Count);
                for (int i = 0; i < fixedFirstRewardCards.Count; i++)
                {
                    if (fixedFirstRewardCards[i] != null)
                        _fixedFirstRewardCards.Add(fixedFirstRewardCards[i]);
                }
            }
        }

        /// <summary>
        /// Replaces the reward random stream without rebuilding progression state.
        /// CombatController uses this together with its deck and combat-state injection
        /// so one seeded source owns every gameplay decision in a headless run.
        /// </summary>
        public void SetRandomSource(IRandomSource random)
        {
            _random = random ?? DefaultRandomSource.Instance;
        }

        /// <summary>
        /// 生成三个随机奖励选项
        /// </summary>
        public List<Reward> GenerateRewards(int count = 0)
            => GenerateRewardsForLevel(0, count);

        /// <summary>
        /// Replaces an already pending authored or random offer with a fresh
        /// roll from the current legal pool.  The caller owns charge
        /// validation and consumption; this method only advances the same
        /// seeded reward stream and never mutates the selected deck.
        /// </summary>
        public List<Reward> RerollRewards(int count)
        {
            LevelUpRewardRequest request = CurrentRewardRequest.AsReroll();
            List<Reward> result = GenerateRandomRewards(count, request);
            CurrentOfferAllowsCashOut = true;
            CurrentOfferContentId = null;
            CurrentOfferIsFixedFirstReward = false;
            CurrentRewardRequest = request;
            return result;
        }

        /// <summary>
        /// Generates the reward group for an already reached level.  A fixed
        /// authored offer wins over legacy first-level and random paths; this
        /// is intentionally keyed by the committed level rather than kill
        /// order, UI timing, or a random stream.
        /// </summary>
        public List<Reward> GenerateRewardsForLevel(
            int reachedLevel,
            int count = 0)
            => GenerateRewardsForRequest(new LevelUpRewardRequest(reachedLevel), count);

        public List<Reward> GenerateRewardsForRequest(LevelUpRewardRequest request, int count = 0)
        {
            if (_naturalRewardPool?.LevelCostPolicy != null)
                request = _naturalRewardPool.LevelCostPolicy.BindRequest(request);
            int reachedLevel = request.ReachedLevel;
            CurrentOfferAllowsCashOut = true;
            CurrentOfferContentId = null;
            CurrentOfferIsFixedFirstReward = false;
            CurrentRewardRequest = request;
            if (reachedLevel >= 2 && _fixedOfferResolver != null)
            {
                if (_fixedOfferResolver.TryResolve(
                        reachedLevel,
                        out LevelUpRewardOffer offer,
                        out string error))
                {
                    if (offer == null)
                    {
                        throw new InvalidOperationException(
                            $"等级 {reachedLevel} 的固定升级 Offer 为空。" );
                    }
                    CurrentOfferAllowsCashOut = offer.AllowsCashOut;
                    CurrentOfferContentId = offer.ContentId;
                    return offer.CreateRewardCopies();
                }
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException(
                        $"等级 {reachedLevel} 的固定升级 Offer 无法解析：" +
                        error);
                }
            }

            if (_fixedFirstRewardCards != null &&
                _fixedFirstRewardCards.Count > 0)
            {
                // The configured first-level rewards form one frozen reward
                // group.  Returning only the first entry made a multi-card
                // authored reward silently lose the remaining cards because
                // the list was consumed one item at a time across levels.
                var firstRewards = new List<Reward>(_fixedFirstRewardCards.Count);
                for (int i = 0; i < _fixedFirstRewardCards.Count; i++)
                {
                    CardConfig card = _fixedFirstRewardCards[i];
                    if (card == null)
                        continue;
                    firstRewards.Add(new Reward
                    {
                        type = RewardType.NewCard,
                        name = card.cardName,
                        description = card.description,
                        card = card
                    });
                }
                _fixedFirstRewardCards.Clear();
                return firstRewards;
            }

            return GenerateRandomRewards(count, request);
        }

        private List<Reward> GenerateRandomRewards(int count, LevelUpRewardRequest request)
        {
            if (_rewardConfig.useNaturalCardSealPool || _naturalRewardPool?.LevelCostPolicy != null)
                return _naturalRewardPool.Roll(_rewardConfig, _getLuck(),
                    _random, count, _hasLegalSealHost, request);
            if (count <= 0)
                count = _rewardConfig.RollChoiceCount(_getLuck(), _random);
            if (_rewardConfig.uniqueCardChoices &&
                _rewardConfig.cardRewardChance >= 1f &&
                count > _availableCards.Count)
            {
                throw new InvalidOperationException(
                    $"卡牌专用升级奖励需要 {count} 个不同候选，" +
                    $"但当前合法卡池只有 {_availableCards.Count} 张。");
            }

            var rewards = new List<Reward>();
            List<CardConfig> remainingCards = _rewardConfig.uniqueCardChoices
                ? new List<CardConfig>(_availableCards)
                : _availableCards;

            for (int i = 0; i < count; i++)
            {
                float roll = _random.Value01();
                if (roll < Math.Max(0f, Math.Min(1f,
                        _rewardConfig.cardRewardChance)) &&
                    remainingCards.Count > 0)
                {
                    int cardIndex = _random.Range(0, remainingCards.Count);
                    CardConfig card = remainingCards[cardIndex];
                    rewards.Add(new Reward
                    {
                        type = RewardType.NewCard,
                        name = card.cardName,
                        description = card.description,
                        card = card
                    });
                    if (_rewardConfig.uniqueCardChoices)
                        remainingCards.RemoveAt(cardIndex);
                }
                else
                {
                    // 40% 概率出属性提升
                    var stat = GetRandomStat();
                    int amount = GetStatAmount(stat);
                    rewards.Add(new Reward
                    {
                        type = RewardType.StatBoost,
                        name = GetStatName(stat),
                        statType = stat,
                        statAmount = amount,
                        description = GetStatDescription(stat, amount)
                    });
                }
            }

            return rewards;
        }

        /// <summary>
        /// 按合法稀有度池与当前整数百分比天运抽取宝石品阶。
        /// 这是后续宝石奖励管线使用的基础算法；当前 GenerateRewards 不调用它。
        /// </summary>
        public RewardRarity RollGemRarity(IReadOnlyList<bool> legalRarities)
            => _rewardConfig.RollRarity(_getLuck(), legalRarities, _random);

        /// <summary>
        /// 选择奖励并应用（属性提升部分，卡牌添加由调用方处理）
        /// </summary>
        public void SelectReward(Reward reward)
        {
            if (reward.type == RewardType.StatBoost)
                ApplyStatBoost(reward.statType, reward.statAmount);
            OnRewardSelected?.Invoke(reward);
        }

        /// <summary>选择奖励并应用（含卡牌添加到牌库）</summary>
        public void SelectReward(Reward reward, List<CardConfig> deck)
        {
            if (reward.type == RewardType.NewCard)
                deck.Add(reward.card);
            else if (reward.type == RewardType.StatBoost)
                ApplyStatBoost(reward.statType, reward.statAmount);
            OnRewardSelected?.Invoke(reward);
        }

        private void ApplyStatBoost(StatType stat, int amount)
        {
            if (_applyStatBoost != null)
            {
                _applyStatBoost(stat, amount);
                return;
            }

            switch (stat)
            {
                case StatType.FengRui: _player.fengRui += amount; break;
                case StatType.Amount: _player.amount += amount; break;
                case StatType.BengLie: _player.bengLie += amount; break;
                case StatType.NingShen: _player.ningShen += amount; break;
                case StatType.ShouPai: _player.shouPai += amount; break;
                case StatType.ChouPai: _player.yinJuan += amount; break;
                case StatType.FaLi: _player.faLi += amount; break;
                case StatType.HuJia: _player.armor += amount; break;
                case StatType.MingWu: _player.mingWu += amount; break;
                case StatType.LianYu: _player.lianYu += amount; break;
                case StatType.TianYun: _player.tianYun += amount; break;
            }
        }

        private StatType GetRandomStat()
        {
            var stats = new[]
            {
                StatType.FengRui, StatType.Amount, StatType.BengLie,
                StatType.ShouPai, StatType.ChouPai, StatType.FaLi,
                StatType.HuJia, StatType.MingWu
            };
            return stats[_random.Range(0, stats.Length)];
        }

        private int GetStatAmount(StatType stat)
        {
            return stat switch
            {
                StatType.FengRui => 10,       // +10% 伤害
                StatType.Amount => 1,        // 卡牌攻击物数 +1
                StatType.BengLie => 10,      // +10% 溅射
                StatType.ShouPai => 1,       // +1 手牌
                StatType.ChouPai => 1,       // 引卷：显式抽牌 +1
                StatType.FaLi => 1,          // +1 法力
                StatType.HuJia => 2,         // +2 护甲
                StatType.MingWu => 10,       // +10% 经验
                _ => 1
            };
        }

        private string GetStatName(StatType stat)
        {
            return stat switch
            {
                StatType.FengRui => "锋锐",
                StatType.Amount => "数量",
                StatType.BengLie => "裂空",
                StatType.ShouPai => "展卷",
                StatType.ChouPai => "引卷",
                StatType.FaLi => "灵力",
                StatType.HuJia => "护甲",
                StatType.MingWu => "明悟",
                _ => stat.ToString()
            };
        }

        private string GetStatDescription(StatType stat, int amount)
        {
            return stat switch
            {
                StatType.FengRui => $"伤害 +{amount}%",
                StatType.Amount => $"数量 +{amount}",
                StatType.BengLie => $"裂空 +{amount}%",
                StatType.ShouPai => $"回合抽牌数 +{amount}",
                StatType.ChouPai => $"显式抽牌 +{amount}",
                StatType.FaLi => $"每回合灵力 +{amount}",
                StatType.HuJia => $"护甲 +{amount}",
                StatType.MingWu => $"经验获取 +{amount}%",
                _ => $"{stat} +{amount}"
            };
        }
    }
}
