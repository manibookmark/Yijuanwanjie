namespace WanjieScroll.Audio
{
    /// <summary>
    /// 音乐场景类型（GDD 13.1）
    /// </summary>
    public enum MusicScene
    {
        Hall,           // 首页/仙域（局外主场景）：hubMusic，回退 hallMusic
        Dungeon,        // 地牢探索：民乐 + 低频电子，氛围感
        Combat,         // 遭遇战：民乐 + 电子鼓点，节奏感
        Boss,           // Boss 战：民乐 + 电子高潮，大鼓 + 琵琶
        ComboClimax     // 连击高潮：鼓点加密，随连击数递增
    }

    /// <summary>
    /// 音效类型（GDD 13.2）
    /// </summary>
    public enum SfxType
    {
        CardPlay,       // 出牌：翻纸声 + 笔触
        ComboIncrement, // 连击递增：墨迹扩散音 + 音调递增
        CardShatter,   // 卡牌碎裂：干漆与宣纸结构破裂
        Tianjie,        // 天劫倒计时：钟声/雷鸣
        LevelUp,        // 升级：灵气汇聚与卷轴舒展
        TreasureOpen,  // 宝箱开启：木箱 + 玉石
        LingdanPickup,  // 灵丹/治疗：温玉与药气融入
        BossAppear,     // Boss 出现：低沉号角
        CardDraw,       // 抽牌
        ButtonClick,    // 按钮点击
        RoomEnter,      // 进入房间
        Merchant,       // 游方商交互
        ProjectileHit,  // 投射物命中
        EnemyAttack,    // 怪物近身攻击命中
        EnemyDeath,     // 怪物死亡：纸屑碎裂+墨气消散
        CardHover,      // 卡牌悬停：仅让位于出牌、命中与升级音效
        XpRelease,      // 经验释放：金色光点从敌人身上释放
        ExperienceArrival, // 经验群落入经验条
        RewardReveal,   // 奖励正式揭示
        FormationActivate, // 阵法启动：低沉嗡鸣
        SealStamp       // 钤印：玉印压落与灵气微粒散开
    }
}
