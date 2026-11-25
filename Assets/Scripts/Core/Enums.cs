namespace WanjieScroll.Core
{
    /// <summary>
    /// 卡牌颜色体系（五行+阴阳）
    /// </summary>
    public enum CardColor
    {
        Huo,    // 火（赤朱红）- 攻击
        Tu,     // 土（土褐）- 防御
        Mu,     // 木（苍翠绿）- 增益
        Shui,   // 水（深蓝黑）- 法力
        Jin,    // 金（香槟金）- 英灵
        Yang,   // 阳（日光白）- 万能
        Yin     // 阴（暗夜紫）- 诅咒
    }

    /// <summary>
    /// 费用类型
    /// </summary>
    public enum CostType
    {
        Default,      // 标准费用牌
        Mild,         // 定印（结算后清空连击）
        Wild,         // 阳纹（太极牌，免费，无条件续链）
        X,            // 尽灵（X 费，按当前灵力解析）
        ComboBreaker  // 收势（无条件吃倍率后清空连击）
    }

    /// <summary>
    /// 状态效果类型
    /// </summary>
    public enum StatusEffect
    {
        None,
        ZhenTui,    // 震退（Knockback）
        JiaoXie,    // 缴械（Disarm）
        MoSha,      // 抹煞（Erase）
        ZhuoShao,   // 灼烧（Burn）
        BingFeng,   // 冰封（Freeze）
        BaoJi       // 暴击（Crit）
    }

    /// <summary>
    /// 攻击模式（决定投射物飞行/击中视觉）
    /// </summary>
    public enum AttackPattern
    {
        None,           // 非伤害卡
        SingleShot,     // 单发直线（火棒/法杖/十字架）
        MultiShot,      // 多发直线齐射（飞刀5发/白骨3发）
        Arc,            // 弧线抛物（巨斧3发弧线）
        WhipSlash,      // 横扫斩击（长鞭）
        Orbit,          // 环绕爆发（圣经）
        Chain,          // 闪电链弹射（雷环/符文弹）
        Explosion,      // 爆炸范围（樱桃弹/末日轰炸/熔岩）
        Spiral,         // 螺旋发射（死亡螺旋）
        MultiTarget,    // 多目标同时（金箍棒/万剑诀/血泪）
    }

    /// <summary>攻击的逻辑选目标策略；与投射物视觉完全分离。</summary>
    public enum TargetPolicy
    {
        /// <summary>迁移兼容值；首发不接受玩家选怪，实际按从右到左自动选择。</summary>
        PlayedTargetThenSequential = 0,
        /// <summary>近排优先，并在同排从右到左逐目标位选择。</summary>
        SequentialRightToLeft = 1,
        /// <summary>每个攻击物按开始时的全部暴露目标数动态结算。</summary>
        AllAttackable,
        /// <summary>在当前最近的暴露排中随机选择。</summary>
        RandomAttackable,
        FirstAttackable,
        LeftToRight,
        /// <summary>近排内优先选择将攻击玩家的目标，其余从左到右补齐。</summary>
        UpcomingAttackFirst,
        /// <summary>旧多目标迁移值；新配置使用 LeftToRight + directTargetsPerHit。</summary>
        MultiTarget,
        PlayerSelected,
        /// <summary>完整列槽随机无放回，包含空列；跨攻击物保留预取列，袋空立即补齐。</summary>
        RandomColumnBag,
        /// <summary>攻击意图优先，同优先级与兜底均从右到左。</summary>
        UpcomingAttackFirstRightToLeft,
        /// <summary>冻结本次最近可攻击行的身份集合；不追击新暴露或补位目标。</summary>
        FrozenNearestAttackableRow,
        /// <summary>One wave visits columns left to right once, selecting their current front; never circles back.</summary>
        ColumnFrontOnceLeftToRight,
        /// <summary>从左递增并回绕的列前横扫；每物去重，命中和空列／死亡扫描分别计数。</summary>
        SequentialColumnSweep,
        /// <summary>完整随机列袋；每物按实际命中次数计数，同敌可复击，空列不消耗命中预算。</summary>
        RandomColumnBagMultiHit,
        /// <summary>整次攻击冻结起始暴露目标身份；直击与范围共用，不追加新暴露后敌。</summary>
        FrozenAttackableSet
    }

    /// <summary>伤害事件类型。表现层只能依据类型化事件决定样式。</summary>
    public enum DamageKind
    {
        Direct,
        Splash,
        Bounce,
        Burn,
        DoT,
        Chase,
        Child,
        Echo,
        Explosion,
        Special
    }

    public enum ResolutionMode { Preview, Actual }
    public enum RandomResolutionPolicy { IgnoreOptionalRandom, RollNormally }
    public enum AreaTriggerMode { Disabled, PerSubHit, PerDirectTarget }
    public enum AreaDamageBasis { NormalHitDamage, FinalDirectDamage }
    public enum BounceTargetMode { UniqueThenStop, UniqueThenRepeat }
    /// <summary>
    /// 弹跳事件的时序。默认模式保持“每次直击后立即弹跳”的既有合同；
    /// 同步波次模式让本次攻击的全部实例先完成同一轮，再进入下一轮。
    /// </summary>
    public enum BounceSequenceMode
    {
        PerDirectHit = 0,
        SynchronizedWavesByAttackInstanceCount = 1,
        NativePerAttackInstance = 2,
        [System.Obsolete("Use SynchronizedWavesByAttackInstanceCount.")]
        SynchronizedWavesByAttackCount =
            SynchronizedWavesByAttackInstanceCount
    }
    /// <summary>
    /// 连击如何作用于 Damage Pattern。数值 0 保留给既有内容的单 Hit 放大语义，
    /// 避免新增 RepeatBasePattern 时静默改变已序列化卡牌。
    /// </summary>
    public enum ComboPatternMode
    {
        ScaleHitDamage = 0,
        Disabled = 1,
        Custom = 2,
        RepeatBasePattern = 3
    }
    public enum AmountPatternMode
    {
        ExtraAttackInstance,
        [System.Obsolete("Use ExtraAttackInstance.")]
        ExtraHit = ExtraAttackInstance,
        ExtraProjectile,
        ExtraPatternSeed,
        ExtraBounceSeed,
        ExtraTarget,
        Disabled,
        Custom,
        /// <summary>不增加攻击物；Amount 加入每击的伤害倍率，与 Combo 相加。</summary>
        ScaleHitDamage
    }
    public enum DefeatCause { Damage, Erase, Scripted }

    /// <summary>敌人体型；用于位移抗性和连锁推动比较。</summary>
    public enum EnemyBodySize
    {
        Small = 0,
        Medium = 1,
        Large = 2,
        Giant = 3
    }

    /// <summary>
    /// 属性类型（Deck Stats）
    /// </summary>
    public enum StatType
    {
        FengRui,     // 锋锐（Might）- 伤害加成
        Amount,     // 数量（Amount）- 卡牌攻击物数加值
        BengLie,    // 裂空（Area）- 溅射范围
        NingShen,     // 凝神（Duration）- 英灵触发次数
        ShouPai,    // 展卷（Hand）- 每回合开始的抽牌数；不是手牌持有上限
        ChouPai,    // 引卷（Magnet）- 显式抽牌加值；旧内部枚举名保留兼容
        TianYun,     // 天运（Luck）- 掉落/稀有度
        LianYu,    // 敛玉（Greed）- 金币加成
        MingWu,     // 明悟（Growth）- 经验加成
        HunHun,     // 还魂（Revival）- 濒死复活
        FaLi,       // 灵力（Mana）- 每回合法力上限
        HuJia,      // 护甲（Armor）- 减伤
        ZhaoSha,    // 招煞（Curse）- 敌人强度
        None,       // 非 PlayerStats 的元游戏经诀
        Recovery    // 调息（Recovery）- 本遭遇结束时恢复气血
    }
}
