using UnityEngine;

namespace WanjieScroll.Effects
{
    public enum PeriodicAttackerVisualKind { FireCrow = 0, ClockworkBee = 1 }

    public enum ProceduralBladeShape
    {
        [InspectorName("短刃／飞刀")]
        Knife = 0,
        [InspectorName("带护手长剑")]
        Sword = 1
    }

    /// <summary>
    /// 死亡特效类型
    /// </summary>
    public enum DeathEffectType
    {
        None,
        FadeOut,
        PaperCut,
        InkDisperse,
        BossDisperse,
        SealErase,
        /// <summary>伏魔珠整排抹除：封印环收缩成虚空核心后向外炸裂。</summary>
        RowAnnihilation
    }

    /// <summary>
    /// 卡牌在施法位置播放的程序化法器或自作用特效；不承担伤害结算。
    /// None 不额外创建施法主体；增益由 HUD 或英灵的真实反馈表现。
    /// </summary>
    public enum CardSelfEffectStyle
    {
        None,
        ArmorWard,
        VitalityPill,
        ManaScript,
        JadeSpiral,
        FortuneBloom,
        TianjiMirror,
        [InspectorName("分影玉镯")]
        QuantityMechanism,
        WindOrb,
        LightningRing = 9,
        ThunderWoodSword = 10,
        ManaBook = 11,
        DealScript = 12,
        SandSpirit = 13,
        MuddleSigil = 14
    }

    /// <summary>
    /// 以 contentId 为主键的特效配置。
    /// 每张卡牌可创建独立 VfxProfile 资产，配置投射物、命中粒子、反馈和死亡特效。
    /// </summary>
    [CreateAssetMenu(fileName = "NewVfxProfile", menuName = "万界长卷/特效配置")]
    public class VfxProfile : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("对应卡牌的 contentId")]
        public string contentId;

        [Header("投射物")]
        [Tooltip("旧内容 Sprite 模式兼容字段；新卡攻击特效使用程序化粒子，本字段必须为空。")]
        public Sprite projectileSprite;
        [Tooltip("攻击外观类型：Sprite=旧图片投射物；ProceduralKnife/ProceduralFireTalisman/ProceduralWaterDragon等=程序化投射物；ProceduralWhipSweep=程序化横扫；ProceduralLightningStrike=目标上空直接落雷。")]
        public string visualType = "Sprite";
        public float projectileScale = 0.3f;
        public float flightDuration = 0.18f;
        public bool flightArc = false;
        public float flightArcHeight = 0f;
        public float multiProjectileDelay = 0.045f;
        [Tooltip("按权威 DamageEvent 的实际 Direct/Bounce 序列播放投射物；用于数量会变化或带弹跳的攻击。")]
        public bool useAuthoritativeHitSequence = false;
        [Tooltip("Bounce 段相对 Direct 段的投射物缩放；未配置或不大于 0 时回退 0.72。")]
        [Range(0.1f, 1.5f)] public float bounceScaleMultiplier = 0.72f;

        [Header("回旋剑阵参数（SwordArray 模式专用）")]
        [Tooltip("四象内阵的世界空间半径；数量额外剑使用更大的外圈。")]
        [Range(0.65f, 1.5f)] public float swordArrayRadius = 0.95f;
        [Tooltip("阵纹与实体剑从出牌点结阵的时长。")]
        [Range(0.12f, 0.5f)] public float swordArrayAssembleDuration = 0.26f;
        [Tooltip("剑完成最后一次权威接触后回到阵位的时长；回程不造成伤害。")]
        [Range(0.08f, 0.35f)] public float swordArrayReturnDuration = 0.18f;
        [Tooltip("全部攻击物完成后，剑与阵纹向中心收束的时长。")]
        [Range(0.1f, 0.4f)] public float swordArrayCollapseDuration = 0.18f;
        [Tooltip("锁阵的镏金阵纹色。")]
        public Color swordArrayRuneColor = new Color(1f, 0.73f, 0.22f, 0.92f);
        [Tooltip("内阵与剑轨的银青灵光色。")]
        public Color swordArrayEnergyColor = new Color(0.54f, 0.92f, 1f, 0.88f);

        [Header("持续空中攻击物")]
        [Tooltip("只订阅权威周期攻击源的外观；不决定回合、目标或伤害。")]
        public bool periodicFireCrow;
        [Tooltip("持续空中攻击物外观；periodicFireCrow为既有族开关，保持资产兼容。")]
        public PeriodicAttackerVisualKind periodicAttackerVisualKind;
        public Color beeBodyColor = new Color(0.84f, 0.55f, 0.18f, 1f);
        public Color beeWingColor = new Color(1f, 0.91f, 0.66f, 0.75f);
        public Color fireCrowBodyColor = new Color(0.09f, 0.025f, 0.035f, 1f);
        public Color fireCrowFlameColor = new Color(1f, 0.24f, 0.035f, 0.95f);
        [Range(0.2f, 1.2f)] public float fireCrowScale = 0.55f;
        [Range(0.2f, 2f)] public float fireCrowHoverHeight = 0.8f;
        [Tooltip("无棋盘边界时的备用巡飞半径参数；正式与 F7 场景按实际棋盘宽度巡飞。")]
        [Range(0.2f, 2f)] public float fireCrowOrbitWidth = 0.9f;
        [Range(0.05f, 0.5f)] public float fireCrowOrbitSpeed = 0.16f;

        [Header("程序化刀刃参数（ProceduralBlade 模式专用）")]
        public Color proceduralBladeColor = new Color(0.025f, 0.03f, 0.04f, 1f);
        public Color proceduralBladeEdgeColor = new Color(0.55f, 0.62f, 0.7f, 0.8f);
        [Tooltip("短刃用于飞刀；带护手长剑用于桃木剑等剑类攻击物。")]
        public ProceduralBladeShape proceduralBladeShape =
            ProceduralBladeShape.Knife;
        [Tooltip("只改变刀剑主体长度，不改变整体尺寸与飞行距离。")]
        [Range(0.5f, 2.5f)] public float proceduralBladeLengthScale = 1f;
        [Range(0.08f, 0.5f)] public float proceduralBladeWidthRatio = 0.24f;
        [Range(1f, 1.3f)] public float proceduralBladeEdgeScale = 1.1f;

        [Header("程序化战斧参数（ProceduralAxe 模式专用）")]
        public Color proceduralAxeSteelColor = new Color(0.22f, 0.25f, 0.28f, 1f);
        public Color proceduralAxeEdgeColor = new Color(0.92f, 0.78f, 0.34f, 1f);
        public Color proceduralAxeHandleColor = new Color(0.22f, 0.08f, 0.035f, 1f);
        public Color proceduralAxeAccentColor = new Color(0.78f, 0.22f, 0.08f, 1f);
        [Range(360f, 1800f)] public float proceduralAxeSpinSpeed = 1080f;
        [Range(0.75f, 1.35f)] public float proceduralAxeHeadScale = 1f;

        [Header("程序化离火瓶参数（ProceduralFireVial 模式专用）")]
        public Color proceduralVialGlassColor = new Color(0.2f, 0.08f, 0.035f, 0.9f);
        public Color proceduralVialFireColor = new Color(1f, 0.28f, 0.035f, 1f);
        public Color proceduralVialCoreColor = new Color(1f, 0.86f, 0.25f, 1f);
        public Color proceduralVialCorkColor = new Color(0.24f, 0.095f, 0.035f, 1f);
        [Range(180f, 1080f)] public float proceduralVialSpinSpeed = 620f;
        [Range(0.25f, 0.8f)] public float proceduralVialFillRatio = 0.56f;

        [Header("程序化中央火符参数（ProceduralFireTalisman 模式专用）")]
        [Tooltip("只加宽完整符纸；不改变攻击目标、数量或飞行轨迹。")]
        [Range(0.75f, 1.6f)] public float fireTalismanPaperWidth = 1f;
        [Tooltip("朱砂符胆内的焰心；进化火符使用更亮的金白色。")]
        public Color fireTalismanGlyphCoreColor = new Color(1f, 0.48f, 0.10f, 1f);
        public Color fireTalismanFlameColor = new Color(1f, 0.24f, 0.025f, 0.82f);
        public Color fireTalismanFlameCoreColor = new Color(1f, 0.72f, 0.22f, 0.95f);
        [Tooltip("火炁的宽度与环绕范围；低强度为符纸后方短焰，高强度为绕纸盘旋。")]
        [Range(0f, 1.5f)] public float fireTalismanFlameIntensity = 0.4f;
        [Range(0, 6)] public int fireTalismanFlameRibbonCount = 2;
        [Tooltip("中央符箓周围的细小火星数，不代表攻击次数；两卡命中火焰共用。")]
        [Range(0, 24)] public int fireTalismanSparkCount = 4;
        [Range(0.008f, 0.045f)] public float fireTalismanSparkSize = 0.014f;

        [Header("如意金箍棒粒子参数（RuyiStaffParticles 模式专用）")]
        [Tooltip("棍身可见长度倍率；用于区分棍身与普通光点。")]
        [Range(0.4f, 2.5f)] public float ruyiStaffLengthScale = 1f;
        [Tooltip("棍身、描边和金箍的粗细倍率。")]
        [Range(0.4f, 2.5f)] public float ruyiStaffThicknessScale = 1f;
        [Tooltip("飞行尾迹粒子的尺寸倍率。")]
        [Range(0.2f, 3f)] public float ruyiStaffWakeScale = 1f;

        [Header("程序化法杖灵弹参数（ProceduralWandBolt 模式专用）")]
        public Color wandBoltCoreColor = new Color(1f, 0.86f, 0.28f, 1f);
        public Color wandBoltAuraColor = new Color(1f, 0.28f, 0.04f, 0.72f);
        public Color wandBoltRuneColor = new Color(1f, 0.92f, 0.58f, 0.9f);
        [Range(0.5f, 2.5f)] public float wandBoltLengthScale = 1f;
        [Range(0.5f, 2.5f)] public float wandBoltWidthScale = 1f;
        [Range(0f, 1080f)] public float wandBoltSpinSpeed = 360f;
        [Range(0f, 12f)] public float wandBoltPulseSpeed = 6f;
        [Tooltip("开启后在灵弹外绘制双层法环；天师杖等高阶法术使用。")]
        public bool wandBoltRuneRing;

        [Header("程序化天雷参数（ProceduralLightningStrike 模式专用）")]
        public Color lightningCoreColor = new Color(1f, 0.98f, 0.82f, 1f);
        public Color lightningAuraColor = new Color(0.26f, 0.72f, 1f, 0.82f);
        public Color lightningGroundColor = new Color(0.66f, 0.36f, 1f, 0.9f);
        [Range(2f, 8f)] public float lightningSkyHeight = 4.8f;
        [Range(7, 24)] public int lightningSegments = 13;
        [Range(0.12f, 0.6f)] public float lightningDuration = 0.24f;
        [Range(0.01f, 0.12f)] public float lightningContactDelay = 0.045f;
        [Range(0.1f, 1.5f)] public float lightningJitter = 0.58f;
        [Range(0, 3)] public int lightningBranchCount = 2;
        [Range(0.5f, 2f)] public float lightningIntensity = 1f;

        [Header("九节鞭柔性鞭影参数（ProceduralWhipSweep 模式专用）")]
        [Tooltip("连续柔性鞭身的主体颜色；不表示分离的鞭节。")]
        public Color whipSegmentColor = new Color(0.48f, 0.22f, 0.07f, 1f);
        [Tooltip("鞭痕末端的渐变强调色，不表示独立关节粒子。")]
        public Color whipJointColor = new Color(0.95f, 0.58f, 0.14f, 1f);
        [Tooltip("连续鞭身下方较宽、低透明的柔边阴影。")]
        public Color whipInkColor = new Color(0.24f, 0.055f, 0.025f, 0.82f);
        [Tooltip("一次完整的甩出、从左向右拉扫并收回的总时长。")]
        [Range(0.3f, 1f)] public float whipSweepDuration = 0.64f;
        [Tooltip("数量产生多个攻击物时，相邻横扫的启动间隔；横扫会重叠播放，不等待上一条结束。")]
        [Range(0f, 0.3f)] public float whipInterSweepDelay = 0.1f;
        [Tooltip("鞭梢越过敌方右侧攻击线继续延伸的世界单位；左侧落点固定为最左目标中心。")]
        [Range(0.2f, 1.2f)] public float whipSpanPadding = 0.62f;
        [Tooltip("弯曲波从鞭根传到鞭梢时的上下摆幅。")]
        [Range(0.08f, 0.8f)] public float whipArcHeight = 0.38f;
        [Tooltip("真实绳模拟的柔软度：提高会增加松弛量、惯性保留与弯曲连续性，并降低鞭梢硬跟随感。")]
        [Range(0f, 1f)] public float whipFlexibility = 0.78f;
        [Tooltip("连续柔性鞭身根部的粗细；不会切成可见分节。")]
        [Range(0.05f, 0.28f)] public float whipSegmentThickness = 0.12f;
        [Tooltip("鞭梢首次命中最左目标中心时的空气环强度；0 表示关闭。")]
        [Range(0f, 2f)] public float whipAirRingIntensity = 1f;
        [Tooltip("鞭梢从左向右横拉时的空气扰动强度；0 表示关闭。")]
        [Range(0f, 3f)] public float whipAirWakeIntensity = 1f;
        [Tooltip("鞭梢空气扰动的长度倍率。")]
        [Range(0.25f, 3f)] public float whipAirWakeLength = 1f;

        [Header("九齿钉耙横扫武器参数（ProceduralRakeSweep 模式专用）")]
        [Tooltip("钉耙柄部主色；普通钉耙使用土金色，九齿噬魂使用赤焰色。")]
        public Color rakeWindPrimaryColor = new Color(0.92f, 0.66f, 0.20f, 0.95f);
        [Tooltip("钉耙耙头与齿部高光颜色。")]
        public Color rakeWindSecondaryColor = new Color(1f, 0.94f, 0.62f, 0.72f);
        [Tooltip("保留的旧版兼容色值；当前普通钉耙不生成罡风尘雾。")]
        public Color rakeWindMistColor = new Color(0.28f, 0.16f, 0.06f, 0.34f);
        [Tooltip("钉耙轮廓整体尺寸强度，不改变伤害或命中时序。")]
        [Range(0.5f, 2f)] public float rakeWindIntensity = 1f;
        [Tooltip("保留的旧版兼容粒子数量，不代表命中次数。")]
        [Range(32, 128)] public int rakeWindParticleCount = 72;

        [Header("九齿噬魂强化魂焰参数（ProceduralRakeSweep 模式专用）")]
        [Tooltip("强化魂焰层强度；0 表示普通九齿钉耙表现。")]
        [Range(0f, 3f)] public float rakeSoulAccentIntensity = 0f;
        [Tooltip("强化魂焰层的环绕粒子数量；0 表示关闭。")]
        [Range(0, 96)] public int rakeSoulAccentParticleCount = 0;
        [Tooltip("魂焰环绕粒子的主色。")]
        public Color rakeSoulAccentPrimaryColor = new Color(0.58f, 0.025f, 0.02f, 1f);
        [Tooltip("魂焰环绕粒子的次色。")]
        public Color rakeSoulAccentSecondaryColor = new Color(1f, 0.28f, 0.04f, 0.95f);

        [Header("非伤害卡牌自作用特效")]
        public CardSelfEffectStyle selfEffectStyle = CardSelfEffectStyle.None;
        [Tooltip("由参考图离线烘焙的粒子位置、简化配色与粒径数据；显现使用公共粒子材质，不绑定整图。")]
        public TextAsset selfEffectParticleShape;
        public Color selfEffectPrimaryColor = new Color(0.95f, 0.82f, 0.35f, 1f);
        public Color selfEffectSecondaryColor = new Color(1f, 0.96f, 0.72f, 0.88f);
        [Range(0.25f, 2f)] public float selfEffectDuration = 0.8f;
        [Range(0.4f, 2.5f)] public float selfEffectScale = 1f;
        [Tooltip("法器主体相对出牌位置的世界偏移；默认零保持已有法器位置。")]
        public Vector3 selfEffectOffset = Vector3.zero;
        [Tooltip("木剑下沿避让出牌上沿的参考像素距离（1920×1080）；只用于表现。")]
        [Range(0f, 280f)] public float castingArtifactCardClearancePixels = 160f;
        [Range(0, 64)] public int selfEffectParticleCount = 12;
        [Tooltip("灵符纸的美术档位，按卡牌形制配置，不随实际支付费用变化。")]
        public TalismanPaperTier talismanPaperTier = TalismanPaperTier.Plain;
        [Tooltip("天书的纸页层数，仅影响厚卷造型。")]
        [Range(1, 5)] public int bookPageLayers = 1;
        [Tooltip("天书展开后显露的灵纹数量，仅影响表现。")]
        [Range(0, 18)] public int bookRuneCount = 6;
        [Tooltip("分影玉镯展开的影环层数，仅影响表现，不代表实际数量增益；保留原字段名兼容已有资产。")]
        [Range(1, 2)] public int quantityMechanismCount = 1;
        [Tooltip("雷环的环体层数，仅影响造型。")]
        [Range(1, 2)] public int lightningRingLayerCount = 1;

        [Header("拖尾")]
        public bool enableTrail = true;
        public Color trailColor = new Color(0.92f, 0.95f, 1f, 0.6f);
        public float trailWidth = 0.06f;
        public float trailTime = 0.10f;

        [Header("命中粒子 - 主层")]
        public Sprite hitParticleTexture;
        public Color hitParticleColor = new Color(0.95f, 0.97f, 1f, 1f);
        [Tooltip("每次命中发射的主层粒子数。-1 表示尚未配置（内容错误），0 表示明确不发射。")]
        [Range(-1, 40)] public int hitParticleCount = -1;
        public float hitParticleSize = 0.05f;
        public float hitParticleSpeed = 2.5f;
        public float hitParticleLifetime = 0.18f;

        [Header("命中粒子 - 次层")]
        public Sprite secondaryParticleTexture;
        public Color secondaryParticleColor = new Color(0.15f, 0.12f, 0.10f, 0.9f);
        [Tooltip("每次命中发射的次层粒子数。-1 表示尚未配置（内容错误），0 表示明确不发射。")]
        [Range(-1, 20)] public int secondaryParticleCount = -1;
        public float secondaryParticleSize = 0.04f;
        public float secondaryParticleSpeed = 1.5f;
        public float secondaryParticleLifetime = 0.20f;

        [Header("命中反馈")]
        public float punchScaleIntensity = 0.12f;
        public float critPunchScaleIntensity = 0.25f;
        public float hitStopDuration = 0.02f;
        public float critHitStopDuration = 0.04f;
        public float hitStopTimeScale = 0.05f;

        [Header("死亡特效")]
        public DeathEffectType deathEffect = DeathEffectType.PaperCut;
        [Header("伏魔珠整排湮灭")]
        [Tooltip("整排虚空罡带的宽度；只影响 RowAnnihilation 的表现，不影响伤害或目标选择。")]
        [Range(4f, 18f)] public float rowAnnihilationWidth = 10.8f;
        [Tooltip("虚空罡带的纵向厚度。")]
        [Range(0.22f, 1.8f)] public float rowAnnihilationBandHeight = 0.72f;
        [Tooltip("湮灭粒子与爆裂核心的强度倍率。")]
        [Range(0.5f, 3f)] public float rowAnnihilationIntensity = 1.35f;

        [Header("出牌音效")]
        [Tooltip("卡牌成功打出时立即播放的专属音效。新卡优先直接绑定 AudioClip。")]
        public AudioClip playSfxClip;
        [Tooltip("旧卡兼容：Resources 相对路径（不含扩展名）。直接绑定 playSfxClip 后无需填写；两者都留空表示有意不配置专属出牌音。")]
        public string playSfxPath = "";
        [Tooltip("专属出牌素材校准。只修正该素材自身响度，不替代全局 CardPlay 增益。")]
        [Range(0f, 2f)] public float playSfxGain = 1f;

        [Header("命中音效")]
        [Tooltip("Resources 相对路径（不含扩展名），如 Audio/SFX/feidao-hit。留空则不播音效。")]
        public string hitSfxPath = "";
        [Tooltip("专属命中素材校准。只修正该素材自身响度，不替代全局 ProjectileHit 增益。")]
        [Range(0f, 2f)] public float hitSfxGain = 1f;
        [Tooltip("密集命中音使用独立双音源限频。仅控制命中与暴击声音，不改变攻击数量、视觉或持续攻击机制。")]
        public bool denseImpactAudio;

        [Header("真实暴击强调")]
        [Tooltip("仅在权威暴击回执出现时播放的碎光和短冲击。")]
        public bool knifeCriticalAccent;
        public string critSfxPath = "";
        [Range(0f, 2f)] public float critSfxGain = 1f;

        [Header("回退")]
        public string fallbackContentId;
    }
}
