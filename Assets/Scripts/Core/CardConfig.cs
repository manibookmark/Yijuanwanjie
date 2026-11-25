using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 卡牌配置（ScriptableObject 数据驱动）
    /// 对应 VC 的 CardConfig
    /// </summary>
    [CreateAssetMenu(fileName = "NewCard", menuName = "万界长卷/卡牌")]
    public class CardConfig : ScriptableObject
    {
        [System.NonSerialized]
        private CardInstance _runtimeInstance;

        /// <summary>
        /// 迁移期兼容门面：牌区仍传递 CardConfig，但实例身份实际由
        /// <see cref="CardInstance"/> 唯一拥有。赋予新 ID 会创建全新的实例状态。
        /// </summary>
        public int runtimeInstanceId =>
            _runtimeInstance?.InstanceId ?? 0;

        /// <summary>此实体牌的突破层数；作者内容资产始终返回 0。</summary>
        public int LimitBreakCount =>
            _runtimeInstance?.LimitBreakCount ?? 0;

        /// <summary>Read-only turn provenance; zero-offset Confuse is still an affected card.</summary>
        public bool IsCostConfusedThisTurn =>
            _runtimeInstance?.IsCostConfusedThisTurn ?? false;

        /// <summary>
        /// Whether Confuse currently changes this card's numeric cost. A zero
        /// roll retains its provenance but has no changed-cost state. Compare
        /// the card number, not payment: free-play may still pay zero.
        /// </summary>
        public bool HasConfusedCostChange => IsCostConfusedThisTurn &&
            costType == CostType.Default && GetCost() != manaCost;

        /// <summary>
        /// Instance lifetime at the encounter/Run boundary.  Author content
        /// assets have no runtime instance and therefore are always persistent
        /// definitions; only a cloned card can be encounter-temporary.
        /// </summary>
        public CardLifetime Lifetime =>
            _runtimeInstance?.Lifetime ?? CardLifetime.Persistent;

        /// <summary>Read-only physical provenance; unbound authors make no historical claim.</summary>
        public CardAuthorProvenance AuthorProvenance =>
            _runtimeInstance?.AuthorProvenance ?? CardAuthorProvenance.LegacyUnknown;
        public bool HasKnownAuthorConfigurations => _runtimeInstance?.HasKnownAuthorConfigurations ?? false;

        public bool IsEncounterTemporary =>
            Lifetime == CardLifetime.EncounterTemporary;

        /// <summary>Intrinsic Copy destruction cannot be undone by modifying an author-field facade.</summary>
        public bool MustDestroyOnUse => destroyOnUse || (_runtimeInstance?.IsSealCopy ?? false);

        /// <summary>为此实体牌增加突破层数。</summary>
        public int ApplyLimitBreak(int levels = 1)
        {
            if (_runtimeInstance == null)
                throw new System.InvalidOperationException(
                    "只有已分配 runtimeInstanceId 的实体牌可以突破。");
            return _runtimeInstance.ApplyLimitBreak(levels);
        }

        /// <summary>版本化 Run 存档恢复精确的实例突破进度。</summary>
        internal void RestoreLimitBreakCount(int count)
        {
            if (_runtimeInstance == null)
                throw new System.InvalidOperationException(
                    "恢复突破进度前必须先恢复 runtimeInstanceId。");
            _runtimeInstance.RestoreLimitBreakCount(count);
        }

        /// <summary>
        /// 仅由 CardInstanceFactory 在创建独立运行副本时绑定身份。对外没有
        /// 身份 setter，牌区只读视图中的元素因而不能被调用方改造成重复 ID。
        /// </summary>
        internal void BindRuntimeInstance(int instanceId, CardBaseTemplate baseTemplate = null,
            CardBaseTemplate currentTemplate = null,
            CardAuthorProvenance provenance = CardAuthorProvenance.FrozenKnown)
        {
            if (provenance == CardAuthorProvenance.FrozenKnown)
                baseTemplate ??= CardBaseTemplate.Capture(this);
            currentTemplate ??= CardBaseTemplate.CaptureCurrent(this);
            var instance = new CardInstance(instanceId, contentId);
            instance.BindAuthorConfigurations(baseTemplate, currentTemplate, provenance);
            _runtimeInstance = instance;
        }

        internal void MarkSealCopy()
        {
            _runtimeInstance.MarkSealCopy();
            // Keep legacy consumers truthful while the intrinsic policy remains
            // instance-owned and survives restore/evolution of this same entity.
            destroyOnUse = true;
        }

        internal void MarkEncounterTemporary()
        {
            if (_runtimeInstance == null)
            {
                throw new System.InvalidOperationException(
                    "只有已分配 runtimeInstanceId 的实体牌可以标记为遭遇临时牌。");
            }
            _runtimeInstance.MarkEncounterTemporary();
        }

        [SerializeField] private CardOfferingAdmission _offeringAdmission;
        public CardOfferingAdmission OfferingAdmission => _offeringAdmission ?? CardOfferingAdmission.Unknown;

        /// <summary>Content construction only: an existing physical card cannot rewrite source evidence.</summary>
        public void SetOfferingAdmission(CardOfferingAdmission admission)
        {
            if (_runtimeInstance != null)
                throw new System.InvalidOperationException("Offering evidence is immutable after instance binding.");
            admission = admission ?? CardOfferingAdmission.Unknown;
            _offeringAdmission = new CardOfferingAdmission(admission.State,
                admission.SourceConfigId, admission.SourceClass, admission.RawAllowInOfferingTable);
        }

        internal CardInstance RuntimeInstance => _runtimeInstance;

        [Tooltip("稳定内容标识；存档和跨表引用必须使用该字段")]
        public string contentId;

        [Header("基本信息")]
        [Tooltip("卡牌名称")]
        public string cardName;

        [Tooltip("卡牌描述")]
        [TextArea(2, 4)]
        public string description;

        [Tooltip("卡牌详细描述（Tooltip/图鉴用）")]
        [TextArea(2, 6)]
        public string detailDescription;

        [Tooltip("卷属标识（xiyou/daming/xiuxian/common）")]
        public string volumeId = "common";

        [Header("美术资源")]
        [Tooltip("卡面主图（512×768）")]
        public Sprite cardArt;

        [Tooltip("卡牌边框图（null=使用默认程序化边框）")]
        public Sprite borderArt;

        [Tooltip("卡牌图标（128×128，用于牌库视图缩略图）")]
        public Sprite iconArt;

        [Header("颜色与费用")]
        public CardColor color = CardColor.Huo;
        public CostType costType = CostType.Default;
        [Min(0)]
        public int manaCost = 0;
        [Header("普通奖励资格（作者源）")]
        public bool ordinaryRewardEnabled;
        public bool defaultRewardUnlocked;
        [Tooltip("普通奖励独立费用档；-1表示未配置，不能回退为支付费用。")]
        public int rewardPoolCost = -1;
        // Ordinary reward membership starts at the current Run level, independently
        // of permanent account unlocks and the card's actual payment cost.
        public int rewardMinPlayerLevel = 1;
        [Min(0), Tooltip("首批永久等级里程碑；0表示没有已接通的等级条件。")]
        public int rewardUnlockPlayerLevel;
        // Empty means any Run leader; otherwise the level milestone belongs to this stable crawler ID.
        public string rewardUnlockCrawlerContentId;
        public CardUnlockCondition rewardUnlockCondition;
        [Tooltip("免耗：只免除灵力支付，保留原连击值")]
        public bool isFreeToPlay = false;
        [Tooltip("逆行印：打出后下一张数字卡可降1连击")]
        public bool hasReverse = false;

        [Header("战斗属性")]
        [Tooltip("每个 Hit 的基础伤害（0=无伤害）；卡面名义总伤由 AttackPlan 按 Hit 序列计算")]
        public int baseDamage = 0;

        [Tooltip("攻击模式（决定投射物视觉）")]
        public AttackPattern attackPattern = AttackPattern.SingleShot;

        [Tooltip("基础攻击物数（如飞刀=5、桃木剑=1）")]
        public int projectileCount = 1;

        [Tooltip("逻辑目标策略；与 attackPattern 的视觉语义分离")]
        public TargetPolicy targetPolicy = TargetPolicy.LeftToRight;

        [Tooltip("每个 Hit 的直接目标数；0 表示命中全部当前可攻击目标")]
        public int directTargetsPerHit = 1;

        [Tooltip("是否允许暴击；暴击按每个 Direct Hit 独立判定")]
        public bool canCrit = false;

        [Range(0f, 1f), Tooltip("启用暴击时的逐 SubHit 概率")]
        public float critChance = DamageSystem.DefaultCritChance;

        [Min(1f), Tooltip("暴击倍率")]
        public float critMultiplier = DamageSystem.DefaultCritMultiplier;

        [Tooltip("Area 的默认触发粒度")]
        public AreaTriggerMode areaTriggerMode = AreaTriggerMode.PerDirectTarget;

        [Min(0f), Tooltip("Area 系数")]
        public float areaCoefficient = 0.2f;

        [Tooltip("护甲值（仅御卡）")]
        public int armor = 0;

        [Tooltip("治疗量（0=无治疗）")]
        public int heal = 0;

        [Tooltip("法力回复（仅灵卡，0=无回复）")]
        public int manaGain = 0;

        [Header("状态效果")]
        public StatusEffect statusEffect = StatusEffect.None;
        [Range(0f, 1f)]
        [Tooltip("状态效果触发几率")]
        public float statusChance = 0f;

        [Header("特殊标记")]
        [Tooltip("使用后销毁")]
        public bool destroyOnUse = false;

        [Tooltip("抽到时由卡牌效果自动打出；不支付灵力或推进普通出牌链")]
        public bool autoPlayOnDraw;

        [Tooltip("宝石槽数量")]
        [Range(0, 3)]
        public int gemSlots = 1;

        [Tooltip("镶嵌在此卡实例上的灵印；每次实际 CardPlayed 触发一次")]
        public List<CardSealSpec> seals;

        [Tooltip("进化目标卡（null=不可进化）")]
        public CardConfig evolutionTarget = null;

        [Header("类型化效果（Phase 3）")]
        [Tooltip("非空时由 CardEffectSpec 驱动结算；为空时回退到上面的散落字段")]
        public List<CardEffectSpec> effects;

        /// <summary>是否有类型化效果</summary>
        public bool HasSpecEffects => effects != null && effects.Count > 0;

        /// <summary>
        /// 获取实际费用（Wild=0，X=属性值，其余=manaCost）
        /// </summary>
        public virtual int GetCost(int statValue = 0)
        {
            int value = costType switch
            {
                CostType.Wild => 0,
                CostType.X => Mathf.FloorToInt(statValue),
                _ => manaCost
            };
            if (_runtimeInstance != null &&
                costType != CostType.Wild &&
                costType != CostType.X)
            {
                value = _runtimeInstance.ApplyTemporaryCostTo(value);
            }
            return value;
        }

        /// <summary>
        /// 解析三值费用模型。详见 docs/rules/连击系统规范.md §1。
        /// </summary>
        public virtual ResolvedCardCost ResolveCost(int statValue = 0)
        {
            int baseValue = manaCost;
            if (_runtimeInstance != null &&
                costType != CostType.Wild &&
                costType != CostType.X)
            {
                baseValue = _runtimeInstance.ApplyTemporaryCostTo(baseValue);
            }
            bool isWild = costType == CostType.Wild;
            bool isMild = costType == CostType.Mild;
            bool isX = costType == CostType.X;
            bool isBreaker = costType == CostType.ComboBreaker;

            int comboValue;
            int paymentCost;

            if (isWild)
            {
                comboValue = 0;
                paymentCost = 0;
            }
            else if (isX)
            {
                comboValue = Mathf.FloorToInt(statValue);
                paymentCost = comboValue;
            }
            else
            {
                comboValue = baseValue;
                paymentCost = baseValue;
            }

            if (isFreeToPlay)
                paymentCost = 0;

            return new ResolvedCardCost(
                baseManaValue: manaCost,
                comboValue: comboValue,
                paymentCost: paymentCost,
                isWild: isWild,
                isFreeToPlay: isFreeToPlay,
                isXCost: isX,
                isComboBreaker: isBreaker,
                isMild: isMild,
                hasReverse: hasReverse || CardSealRules.HasEffect(seals, CardSealEffect.ReverseCombo)
            );
        }

        /// <summary>
        /// Sets an instance-owned temporary numeric cost for the current turn.
        /// Authoring assets without a runtime instance cannot be mutated.
        /// </summary>
        internal void SetTemporaryCostModifier(int amount)
        {
            if (_runtimeInstance == null)
                throw new System.InvalidOperationException(
                    "只有实体卡牌可以设置临时费用。");
            _runtimeInstance.SetTemporaryCostModifier(amount);
        }

        /// <summary>Deck cost assignment owns both the resolved offset and its Confuse source.</summary>
        internal void SetConfusedCostModifier(int amount)
        {
            if (_runtimeInstance == null)
                throw new System.InvalidOperationException(
                    "只有实体卡牌可以设置扰灵费用。");
            _runtimeInstance.SetConfusedCostModifier(amount);
        }

        /// <summary>
        /// 是否参与连击
        /// </summary>
        public virtual bool DoesCardCombo()
        {
            return costType switch
            {
                CostType.Mild => false,
                CostType.Wild => true,
                _ => true
            };
        }
    }

    /// <summary>
    /// 解析后的卡牌费用三值模型。详见 docs/rules/连击系统规范.md §1。
    /// </summary>
    public readonly struct ResolvedCardCost
    {
        public readonly int BaseManaValue;
        public readonly int ComboValue;
        public readonly int PaymentCost;
        public readonly bool IsWild;
        public readonly bool IsFreeToPlay;
        public readonly bool IsXCost;
        public readonly bool IsComboBreaker;
        public readonly bool IsMild;
        public readonly bool HasReverse;

        public ResolvedCardCost(
            int baseManaValue, int comboValue, int paymentCost,
            bool isWild, bool isFreeToPlay, bool isXCost,
            bool isComboBreaker, bool isMild, bool hasReverse)
        {
            BaseManaValue = baseManaValue;
            ComboValue = comboValue;
            PaymentCost = paymentCost;
            IsWild = isWild;
            IsFreeToPlay = isFreeToPlay;
            IsXCost = isXCost;
            IsComboBreaker = isComboBreaker;
            IsMild = isMild;
            HasReverse = hasReverse;
        }
    }
}
