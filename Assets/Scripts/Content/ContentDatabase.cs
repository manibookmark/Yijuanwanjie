using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 首发内容数据库
    /// 三卷 + 通用 = 全部首发内容
    /// </summary>
    public static class ContentDatabase
    {
        // ========== 卷属 ==========
        public static readonly string[] VolumeIds = { "common", "xiyou", "daming", "xiuxian" };
        public static readonly string[] VolumeNames = { "通用", "西游卷", "大明卷", "修仙卷" };

        // ========== 获取卷内容 ==========
        public static VolumeContent GetVolume(string id)
        {
            return id switch
            {
                "common" => GetCommon(),
                "xiyou" => GetXiyou(),
                "daming" => GetDaming(),
                "xiuxian" => GetXiuxian(),
                _ => GetCommon()
            };
        }

        // ========== 通用卷 ==========
        static VolumeContent GetCommon()
        {
            return new VolumeContent
            {
                volumeId = "common",
                displayName = "通用",
                crawlers = new List<CrawlerData>
                {
                    new CrawlerData { contentId="legacy.crawler.common.5a3f3db3", name="孙悟空", volumeId="common", triggerColor="Tu", duration=3,
                        cardMana=1, enterEffect="拔毫成兵·入场数量+2", triggerEffect="土牌触发数量+1（凝神3）",
                        triggerEffects=new List<CardEffectSpec> { new CardEffectSpec {
                            effectType=EffectType.ModifyStat, statType=StatType.Amount,
                            amount=1, target=TargetRule.Self, comboScalable=false } },
                        unlockCondition="教程自动", recruitPrice=0, playstyleTag="数量成长型(初始英雄)",
                        cardContentId="card.common.sunwukong.v1",
                        startingCardContentIds=new List<string> {
                            "card.xiyou.jingubang.v1", "card.xiyou.jingubang.v1",
                            "card.xiyou.jingubang.v1", "legacy.card.common.cab047cb" } },
                },
                cards = GetCommonCards(),
                enemies = GetCommonEnemies(),
                relics = new List<RelicData>
                {
                    new RelicData { contentId="relic.tutorial.jiudie.v1", name="九叠", description="按灵力消耗递增连续出牌，逐层增加叠势；顺序中断时重置。", relicType=0, unlockLocation="花果山试玩关 W2 后" },
                    new RelicData { contentId="relic.tutorial.wuxing-disc.v1", name="五行盘", description="启用五行相生：相邻两张出牌构成五行相生时，触发对应增益。", relicType=16, unlockLocation="花果山试玩关 W5 后" },
                    new RelicData { contentId="relic.xiyou.yuyin.v1", name="玉印", description="永久解锁即时钤印。取得灵印时，可将其钤入有空印位的卡牌；钤入后本次探索内不可拆卸。", relicType=1, unlockLocation="双叉岭·虎穴解厄 F2 星痕印台" },
                    new RelicData { name="观星台", description="解锁天机阁(Arcana)", relicType=2, unlockLocation="关卡2" },
                    new RelicData { name="引路灯", description="小地图显示", relicType=3, unlockLocation="关卡4" },
                    new RelicData { name="符录", description="局内进化图鉴查询", relicType=4, unlockLocation="关卡5" },
                    new RelicData { name="秘法罗盘", description="解锁Arcana系统", relicType=5, unlockLocation="关卡5 Boss" },
                    new RelicData { name="星铁砧", description="解锁鉴宝斋", relicType=6, unlockLocation="断桥" },
                    new RelicData { name="余威", description="解锁余威灵玉(上限500)", relicType=7, unlockLocation="断桥" },
                    new RelicData { name="爆裂符", description="加速刷怪", relicType=8, unlockLocation="关卡7" },
                    new RelicData { name="至极余威", description="余威上限提升至5000", relicType=9, unlockLocation="关卡9" },
                    new RelicData { name="宝镜", description="宝石稀有度定制/封印", relicType=10, unlockLocation="关卡11" },
                    new RelicData { name="烘炉围裙", description="余威扩展至所有遭遇战", relicType=11, unlockLocation="关卡13" },
                },
                evolutions = GetCommonEvolutions(),
                dungeons = GetCommonDungeons(),
            };
        }

        static List<CardData> GetCommonCards()
        {
            return new List<CardData>
            {
                new CardData { contentId="card.common.sunwukong.v1", name="孙悟空", volumeId="common", color="Jin", manaCost=1,
                    description="召唤孙悟空·入场数量+2\n驻场：土牌使数量+1（凝神3）", detailDescription="打出：召唤孙悟空，入场数量+2，受叠势倍率放大。\n驻场：每打出一张土牌，数量+1（固定），并消耗1凝神，共可触发3次。",
                    effects=new List<CardEffectSpec> {
                        new CardEffectSpec { effectType=EffectType.SummonCrawler, target=TargetRule.Self, crawlerContentId="legacy.crawler.common.5a3f3db3" },
                        new CardEffectSpec { effectType=EffectType.ModifyStat, statType=StatType.Amount, amount=2, target=TargetRule.Self, comboScalable=true } } },
                // 武卡 红 0-4费
                new CardData { name="长鞭", volumeId="common", color="Huo", manaCost=0, baseDamage=8, attackPattern="WhipSlash", description="8伤害" },
                new CardData { contentId="card.common.jiujiebian.v1", name="九节鞭", volumeId="common", color="Huo", manaCost=0, baseDamage=8,
                    attackPattern="WhipSlash", projectileCount=1,
                    targetPolicy="AllAttackable", directTargetsPerHit=0,
                    description="8伤害·全体",
                    detailDescription="攻击：横扫全部当前可攻击的敌人（每列最前），各造成8点伤害；每轮横扫开始时锁定目标，击杀后新暴露的敌人由后续横扫命中。数量每+1，横扫+1。",
                    attack=Attack(8, 1, 0, "AllAttackable", "WhipSlash") },
                new CardData { contentId="legacy.card.common.7e46b7e4", name="飞刀", volumeId="common", color="Huo", manaCost=0, baseDamage=8, attackPattern="MultiShot", projectileCount=5, targetPolicy="SequentialRightToLeft", directTargetsPerHit=1, description="40伤害<color=#FF0000>·暴击</color>", attack=Attack(8, 5, 1, "SequentialRightToLeft", "MultiShot", canCrit:true) },
                new CardData { name="白骨", volumeId="common", color="Huo", manaCost=0, baseDamage=3, attackPattern="MultiShot", projectileCount=3, description="3伤害" },
                new CardData { name="法杖", volumeId="common", color="Huo", manaCost=0, baseDamage=30, description="30伤害" },
                new CardData { contentId="card.common.taomujian.v1", name="桃木剑", volumeId="common", color="Huo", manaCost=0, baseDamage=30,
                    targetPolicy="UpcomingAttackFirst", directTargetsPerHit=1,
                    description="30伤害·优先",
                    detailDescription="攻击：基础生成1个攻击物，每个攻击物对1个目标造成30点伤害；优先选择本回合即将攻击的可攻击敌人，同优先级从左到右。数量每+1，攻击物+1；每个攻击物结算前重新选择当前可攻击目标。",
                    attack=Attack(30, 1, 1, "UpcomingAttackFirst", "SingleShot") },
                new CardData { name="樱桃弹", volumeId="common", color="Huo", manaCost=1, baseDamage=18, statusEffect="Burn", statusChance=0.3f, description="18伤害<color=#FF0000>·灼烧</color>" },
                new CardData { name="火棒", volumeId="common", color="Huo", manaCost=1, baseDamage=45, description="45伤害" },
                new CardData { name="蒜锤", volumeId="common", color="Huo", manaCost=1, baseDamage=10, statusEffect="Disarm", statusChance=0.2f, description="10伤害" },
                new CardData { name="圣经", volumeId="common", color="Huo", manaCost=1, baseDamage=40, statusEffect="Knockback", statusChance=0.3f, attackPattern="Orbit", description="40伤害<color=#FF0000>·击退</color>" },
                new CardData { name="符文弹", volumeId="common", color="Huo", manaCost=1, baseDamage=15, description="15伤害" },
                new CardData { name="巨斧", volumeId="common", color="Huo", manaCost=2, baseDamage=15, statusEffect="Knockback", statusChance=0.3f, attackPattern="MultiTarget", projectileCount=3, description="45伤害<color=#FF0000>·击退</color>" },
                new CardData { name="血泪", volumeId="common", color="Huo", manaCost=2, baseDamage=40, statusEffect="Crit", statusChance=0.25f, heal=3, description="40伤害<color=#FF0000>·暴击</color> +3气血" },
                new CardData { name="十字架", volumeId="common", color="Huo", manaCost=2, baseDamage=80, description="80伤害" },
                new CardData { name="圣杖", volumeId="common", color="Huo", manaCost=2, baseDamage=150, description="150伤害" },
                new CardData { name="雷环", volumeId="common", color="Huo", manaCost=2, baseDamage=125, description="125伤害" },
                new CardData { name="死亡螺旋", volumeId="common", color="Huo", manaCost=3, baseDamage=160, description="160伤害" },
                new CardData { name="天剑", volumeId="common", color="Huo", manaCost=3, baseDamage=100, statusEffect="Crit", statusChance=0.25f, description="100伤害<color=#FF0000>·暴击</color>" },
                new CardData { name="地狱火", volumeId="common", color="Huo", manaCost=3, baseDamage=225, statusEffect="Knockback", statusChance=0.3f, description="225伤害<color=#FF0000>·击退</color>" },
                new CardData { name="千刃", volumeId="common", color="Huo", manaCost=3, baseDamage=180, attackPattern="MultiShot", projectileCount=10, description="180伤害" },
                new CardData { contentId="legacy.card.common.5aa5940d", name="神火飞鸦", volumeId="common", color="Huo", manaCost=4, baseDamage=140, targetPolicy="SequentialRightToLeft", description="140伤害", attack=Attack(140) },
                new CardData { name="熔岩", volumeId="common", color="Huo", manaCost=4, baseDamage=150, statusEffect="Burn", statusChance=1f, description="150伤害<color=#FF0000>·灼烧</color>" },

                // 御卡 蓝
                new CardData { contentId="legacy.card.common.2927e58a", name="锁子甲", volumeId="common", color="Tu", manaCost=0, armor=2, description="+2护甲", effects=new List<CardEffectSpec> { CardEffectSpec.GainArmor(2) } },
                new CardData { name="金甲", volumeId="common", color="Tu", manaCost=1, armor=4, description="+4护甲" },
                new CardData { name="回血符", volumeId="common", color="Tu", manaCost=1, heal=2, description="+2气血" },
                new CardData { name="虹甲", volumeId="common", color="Tu", manaCost=2, armor=6, description="+6护甲" },
                new CardData { name="时之枪", volumeId="common", color="Tu", manaCost=3, baseDamage=0, statusEffect="Freeze", statusChance=1f, description="<color=#FF0000>·冰封</color>" },

                // 气卡 黄
                new CardData { name="护腕", volumeId="common", color="Mu", manaCost=0, description="手牌+1" },
                new CardData { name="烛火", volumeId="common", color="Mu", manaCost=0, description="裂空+5%" },
                new CardData { name="骷髅铃", volumeId="common", color="Mu", manaCost=0, description="敌强+10%" },
                new CardData { contentId="legacy.card.common.b4b26600", name="摄卷术", volumeId="common", color="Mu", manaCost=1,
                    description="抽1牌", effects=new List<CardEffectSpec> { CardEffectSpec.DrawCards(1) } },
                new CardData { name="菠菜", volumeId="common", color="Mu", manaCost=1, description="锋锐+10%", effects = new List<CardEffectSpec> { CardEffectSpec.ModifyStat(StatType.FengRui, 10) } },
                new CardData { name="复制器", volumeId="common", color="Mu", manaCost=1, description="数量+1" },
                new CardData { name="空心符", volumeId="common", color="Mu", manaCost=1, description="+3气血 <color=#FF0000>·销毁</color>", destroyOnUse=true },
                new CardData { name="石面", volumeId="common", color="Mu", manaCost=1, description="敛玉+10% <color=#FF0000>·销毁</color>", destroyOnUse=true, effects = new List<CardEffectSpec> { CardEffectSpec.ModifyStat(StatType.LianYu, 10) } },

                // 灵卡 紫
                new CardData { name="空卷", volumeId="common", color="Shui", manaCost=0, manaGain=1, description="+1灵力" },
                new CardData { name="轻卷", volumeId="common", color="Shui", manaCost=1, manaGain=2, description="+2灵力" },
                new CardData { name="重卷", volumeId="common", color="Shui", manaCost=2, manaGain=3, description="+3灵力" },
                new CardData { name="古卷", volumeId="common", color="Shui", manaCost=3, manaGain=4, description="+4灵力" },

                // 通卡 W
                new CardData { name="灵光闪", volumeId="common", color="Yang", manaCost=0, baseDamage=100, isWild=true, description="100伤害" },
                new CardData { name="翼", volumeId="common", color="Yang", manaCost=0, isWild=true, description="下张卡费-1" },
                new CardData { name="钱袋", volumeId="common", color="Yang", manaCost=0, isWild=true, destroyOnUse=true, description="+25灵玉 <color=#FF0000>·销毁</color>", effects = new List<CardEffectSpec> { CardEffectSpec.GainJade(25) } },
                new CardData { name="幸运瓣", volumeId="common", color="Yang", manaCost=0, isWild=true, destroyOnUse=true, description="+5%天运 抽1牌 <color=#FF0000>·销毁</color>", effects = new List<CardEffectSpec> { CardEffectSpec.ModifyStat(StatType.TianYun, 5), CardEffectSpec.DrawCards(1) } },

                // ===== 教程关卡牌（花果山教学用） =====
                new CardData { name="清静经", volumeId="common", color="Huo", manaCost=1, baseDamage=40, statusEffect="Knockback", statusChance=0.3f, attackPattern="Orbit", description="40伤害<color=#FF0000>·击退</color>" },
                new CardData { contentId="legacy.card.common.973d06a5", name="通脉草", volumeId="common", color="Mu", manaCost=1, description="锋锐+10%", effects = new List<CardEffectSpec> { CardEffectSpec.ModifyStat(StatType.FengRui, 10) } },
                new CardData { contentId="legacy.card.common.f22c29d6", name="开山斧", volumeId="common", color="Huo", manaCost=2, baseDamage=15, attackPattern="Arc", projectileCount=3, targetPolicy="LeftToRight", directTargetsPerHit=2, description="45伤害<color=#FF0000>·击退</color>", attack=Attack(15, 3, 2, "LeftToRight", "Arc", onHitEffect:"Knockback") },
                new CardData { name="玉简", volumeId="common", color="Shui", manaCost=1, manaGain=2, description="+2灵力" },
                new CardData
                {
                    contentId="legacy.card.common.56004d59",
                    name="离火咒",
                    volumeId="common",
                    color="Huo",
                    manaCost=3,
                    baseDamage=60,
                    targetPolicy="LeftToRight",
                    description="60伤害<color=#FF0000>·离火</color>",
                    attack=new CardAttackData
                    {
                        damagePerTarget=30,
                        attackInstanceCount=2,
                        targetsPerAttack=1,
                        targetPolicy="LeftToRight",
                        visualPattern="Arc",
                        comboMode="RepeatBasePattern",
                        amountMode="ExtraAttackInstance",
                        terrainEffect=new CardTerrainEffectData
                        {
                            effectId="terrain.lihuo",
                            effectKind="Damage",
                            triggerTiming="EnemyTurnStarted",
                            damageKind="Burn",
                            magnitudeScale=1f,
                            triggerCount=1
                        }
                    }
                },
                // 进化结果占位卡牌（占位·待制作）
                new CardData { name="不圣颂", volumeId="common", color="Huo", manaCost=2, baseDamage=80, statusEffect="Knockback", statusChance=0.5f, description="80伤害<color=#FF0000>·击退</color>（占位·待制作）" },
                new CardData { name="噬魂", volumeId="common", color="Huo", manaCost=2, baseDamage=60, statusEffect="Disarm", statusChance=0.3f, heal=5, description="60伤害<color=#FF0000>·缴械</color> +5气血（占位·待制作）" },
                new CardData { name="无未来", volumeId="common", color="Huo", manaCost=3, baseDamage=50, armor=8, description="50伤害 +8护甲（占位·待制作）" },
                new CardData { name="雷循环", volumeId="common", color="Huo", manaCost=3, baseDamage=200, attackPattern="MultiShot", projectileCount=3, description="200伤害（占位·待制作）" },
                new CardData { name="焚天炮", volumeId="common", color="Huo", manaCost=4, baseDamage=250, statusEffect="Burn", statusChance=1f, description="250伤害<color=#FF0000>·灼烧</color>（占位·待制作）" },
                new CardData { name="骨王", volumeId="common", color="Huo", manaCost=2, baseDamage=40, attackPattern="MultiShot", projectileCount=5, description="40伤害（占位·待制作）" },
            };

            // Note: ~52 common cards (46 + 6 placeholders), each volume adds ~13-14 cards = ~92 total
        }

        static List<EnemyData> GetCommonEnemies()
        {
            return new List<EnemyData>
            {
                new EnemyData { name="妖鼠", volumeId="common", baseHealth=1, xp=1, maxHit=0, armor=0, isBoss=false, difficulty=1 },
                new EnemyData { name="小妖", volumeId="common", baseHealth=24, xp=1, maxHit=1, armor=0, isBoss=false, difficulty=1 },
                new EnemyData { name="蝙蝠", shortDescription="栖身阴暗洞窟的妖蝠，常结群扑向来客。", volumeId="common", baseHealth=12, xp=1, maxHit=1, armor=0, isBoss=false, difficulty=1, behaviorProfileId="behavior.enemy.common.bat.v1" },
                new EnemyData { contentId="enemy.common.snake_elite", name="蛇妖精英", shortDescription="修炼有成的蛇妖，长躯盘踞着整条战线。", volumeId="common", baseHealth=120, xp=12, maxHit=4, armor=0, isBoss=false, difficulty=2, gridWidth=1, gridDepth=2 },
                new EnemyData { name="骷髅兵", volumeId="common", baseHealth=25, xp=2, maxHit=1, armor=5, isBoss=false, difficulty=1 },
                new EnemyData { name="僵尸", volumeId="common", baseHealth=12, xp=5, maxHit=4, armor=18, isBoss=false, difficulty=1 },
                new EnemyData { name="狼人", volumeId="common", baseHealth=250, xp=5, maxHit=3, armor=15, isBoss=false, difficulty=2 },
                new EnemyData { name="蝙蝠精英", volumeId="common", baseHealth=400, xp=30, maxHit=2, armor=0, isBoss=true, difficulty=2 },
                new EnemyData { name="骷髅精英", volumeId="common", baseHealth=600, xp=35, maxHit=4, armor=60, isBoss=true, difficulty=2 },
                new EnemyData { name="巨人木乃伊", volumeId="common", baseHealth=300, xp=6, maxHit=4, armor=50, isBoss=false, difficulty=2 },
                new EnemyData { name="狼人精英", volumeId="common", baseHealth=2200, xp=55, maxHit=13, armor=220, isBoss=true, difficulty=3 },
                new EnemyData { name="墓穴精英", volumeId="common", baseHealth=1200, xp=50, maxHit=12, armor=120, isBoss=true, difficulty=3 },
                new EnemyData { name="劫灰", volumeId="common", baseHealth=1000000, xp=0, maxHit=333, armor=0, isBoss=true, difficulty=9 },
                new EnemyData { name="破戒魔", volumeId="common", baseHealth=30000, xp=0, maxHit=30, armor=0, isBoss=true, difficulty=9 },
            };
        }

        static List<EvolutionData> GetCommonEvolutions()
        {
            return new List<EvolutionData>
            {
                new EvolutionData { primaryCard="长鞭", secondaryCards = new List<string>{"空心符"}, resultCard="血泪" },
                new EvolutionData { primaryCard="飞刀", secondaryCards = new List<string>{"护腕"}, resultCard="千刃" },
                new EvolutionData { primaryCard="法杖", secondaryCards = new List<string>{"空卷","轻卷","重卷","古卷"}, resultCard="圣杖" },
                new EvolutionData { primaryCard="巨斧", secondaryCards = new List<string>{"烛火"}, resultCard="死亡螺旋" },
                new EvolutionData { primaryCard="圣经", secondaryCards = new List<string>{"骷髅铃"}, resultCard="不圣颂" },
                new EvolutionData { primaryCard="火棒", secondaryCards = new List<string>{"菠菜"}, resultCard="地狱火" },
                new EvolutionData { primaryCard="蒜锤", secondaryCards = new List<string>{"回血符"}, resultCard="噬魂" },
                new EvolutionData { primaryCard="符文弹", secondaryCards = new List<string>{"锁子甲","金甲","虹甲"}, resultCard="无未来" },
                new EvolutionData { primaryCard="雷环", secondaryCards = new List<string>{"复制器"}, resultCard="雷循环" },
                new EvolutionData { primaryCard="十字架", secondaryCards = new List<string>{"幸运瓣"}, resultCard="天剑" },
                new EvolutionData { primaryCard="神火飞鸦", secondaryCards = new List<string>{"熔岩"}, resultCard="焚天炮" },
                new EvolutionData { primaryCard="白骨", secondaryCards = new List<string>{"石面"}, resultCard="骨王" },
            };
        }

        static List<DungeonData> GetCommonDungeons()
        {
            return new List<DungeonData>
            {
                new DungeonData { name="花果山(教程)", volumeId="common", difficulty=1, floors=1, unlockCondition="初始", reward="孙悟空+连击开关", hasBoss=false },
                new DungeonData { name="断桥", volumeId="common", difficulty=3, floors=1, unlockCondition="关卡4 Lv15", reward="星铁砧+余威", hasBoss=false },
                new DungeonData { name="险桥", volumeId="common", difficulty=5, floors=1, unlockCondition="关卡7 Lv15", reward="Tier3解锁", hasBoss=false },
                new DungeonData { name="绝桥", volumeId="common", difficulty=7, floors=1, unlockCondition="击杀巨蟹Boss", reward="无", hasBoss=false },
                new DungeonData { name="万界交汇", volumeId="common", difficulty=8, floors=5, unlockCondition="通关绝桥", reward="无", hasBoss=true, bossName="混沌使者" },
                new DungeonData { name="长卷深渊", volumeId="common", difficulty=9, floors=5, unlockCondition="通关万界交汇", reward="烘炉围裙", hasBoss=true, bossName="深渊主宰" },
                new DungeonData { name="???", volumeId="common", difficulty=0, floors=1, unlockCondition="通关主线", reward="末世号角(后续)", hasBoss=false },
            };
        }

        // ========== 西游卷 ==========
        static VolumeContent GetXiyou()
        {
            return new VolumeContent
            {
                volumeId = "xiyou",
                displayName = "西游卷",
                crawlers = new List<CrawlerData>
                {
                    new CrawlerData { contentId="crawler.xiyou.tangsanzang.v1", name="唐三藏", volumeId="xiyou", triggerColor="Mu", duration=5,
                        cardMana=0, enterEffect="开经明义·立即获得18点经验", triggerEffect="木牌触发明悟+1%（凝神5）",
                        triggerEffects=new List<CardEffectSpec> { new CardEffectSpec {
                            effectType=EffectType.ModifyStat, statType=StatType.MingWu,
                            amount=1, target=TargetRule.Self, comboScalable=false } },
                        unlockCondition="击败双叉岭关BOSS寅将军", recruitPrice=0, playstyleTag="经验与明悟成长引擎",
                        cardContentId="card.xiyou.tangsanzang.v1",
                        startingCardContentIds=new List<string> {
                            "card.common.taomujian.v1", "card.common.taomujian.v1",
                            "legacy.card.common.2927e58a", "legacy.card.common.b4b26600" } },
                    new CrawlerData { name="哪吒", volumeId="xiyou", triggerColor="Huo", duration=5,
                        cardMana=0, enterEffect="三头六臂", triggerEffect="锋锐+10%",
                        unlockCondition="击败关卡1 Boss花果妖王", recruitPrice=500, playstyleTag="攻击铺场" },
                    new CrawlerData { name="二郎神", volumeId="xiyou", triggerColor="Huo", duration=3,
                        cardMana=1, enterEffect="天眼洞察", triggerEffect="伤害+90",
                        unlockCondition="关卡2找到封印台", recruitPrice=1500, playstyleTag="高伤输出" },
                    new CrawlerData { name="铁扇公主", volumeId="xiyou", triggerColor="Tu", duration=6,
                        cardMana=0, enterEffect="芭蕉扇风", triggerEffect="每遭遇回1HP",
                        unlockCondition="累计恢复1000HP", recruitPrice=1360, playstyleTag="治疗辅助" },
                    new CrawlerData { name="太上老君", volumeId="xiyou", triggerColor="Shui", duration=5,
                        cardMana=0, enterEffect="炼丹术", triggerEffect="+1法力",
                        unlockCondition="打出火棒100次", recruitPrice=420, playstyleTag="法力引擎" },
                },
                cards = new List<CardData>
                {
                    new CardData { contentId="card.xiyou.tangsanzang.v1", name="唐三藏", volumeId="xiyou", color="Jin", manaCost=0,
                        description="召唤唐三藏·经验+18\n驻场：木牌使明悟+1%（凝神5）",
                        detailDescription="打出：召唤唐三藏，立即获得18点基础经验，受现有明悟倍率影响。\n驻场：从下一张牌起，每打出一张木牌，本次探索明悟+1%（固定），并消耗1凝神，共可触发5次。",
                        effects=new List<CardEffectSpec> {
                            new CardEffectSpec { effectType=EffectType.SummonCrawler, target=TargetRule.Self, crawlerContentId="crawler.xiyou.tangsanzang.v1" },
                            CardEffectSpec.GainExperience(18) } },
                    new CardData { contentId="card.xiyou.jingubang.v1", name="金箍棒", volumeId="xiyou", color="Huo", manaCost=0,
                        attackPattern="MultiShot", projectileCount=3, targetPolicy="SequentialRightToLeft", directTargetsPerHit=1,
                        description="15伤害<color=#FF0000>·弹跳</color>",
                        detailDescription="攻击：从右往左生成N个挥击实例，每个攻击物对1个目标造成5伤；数量每+1，攻击物+1。\n弹跳：至少2个目标时，首轮后追加最多N-1轮，每轮仍由N个攻击物各挥击1次；每轮开始只剩1个目标则停止。",
                        attack=Attack(5, 3, 1, "SequentialRightToLeft", "MultiShot",
                            bounceSequenceMode:"SynchronizedWavesByAttackInstanceCount",
                            areaTriggerMode:"Disabled", areaCoefficient:0f) },
                    new CardData { contentId="legacy.card.common.cab047cb", name="金刚不坏", volumeId="xiyou", color="Tu", manaCost=1, armor=4,
                        description="+4护甲",
                        detailDescription="土牌：打出时若孙悟空在场，触发一次拔毫成兵（数量+1，消耗 1 凝神）。",
                        effects=new List<CardEffectSpec> { CardEffectSpec.GainArmor(4) } },
                    new CardData { name="火尖枪", volumeId="xiyou", color="Huo", manaCost=1, baseDamage=50, statusEffect="Burn", statusChance=0.3f, description="50伤害<color=#FF0000>·灼烧</color>" },
                    new CardData { name="风火轮", volumeId="xiyou", color="Huo", manaCost=2, baseDamage=55, description="55伤害" },
                    new CardData { contentId="legacy.card.xiyou.0578cfde", name="芭蕉扇", volumeId="xiyou", color="Mu", manaCost=1, baseDamage=8, attackPattern="MultiShot", projectileCount=5, targetPolicy="SequentialRightToLeft", directTargetsPerHit=1, description="40伤害<color=#FF0000>·击退</color>", attack=Attack(8, 5, 1, "SequentialRightToLeft", "MultiShot", onHitEffect:"Knockback") },
                    new CardData { name="定海神针", volumeId="xiyou", color="Huo", manaCost=4, baseDamage=200, description="200伤害" },
                    new CardData { name="金刚琢", volumeId="xiyou", color="Tu", manaCost=1, armor=5, description="+5护甲" },
                    new CardData { name="蟠桃", volumeId="xiyou", color="Mu", manaCost=1, heal=15, description="+15气血 <color=#FF0000>·销毁</color>", destroyOnUse=true },
                    new CardData { name="三昧真火", volumeId="xiyou", color="Shui", manaCost=2, manaGain=3, baseDamage=20, statusEffect="Burn", statusChance=1f, description="+3灵力 20伤害<color=#FF0000>·灼烧</color>" },
                    new CardData { contentId="legacy.card.xiyou.315dc4d8", name="筋斗云", volumeId="xiyou", color="Yang", manaCost=0, isWild=true, destroyOnUse=true, description="抽1牌<color=#7B2D8E>·销毁</color>", effects=new List<CardEffectSpec> { CardEffectSpec.DrawCards(1) } },
                    new CardData { name="七十二变", volumeId="xiyou", color="Yang", manaCost=0, isWild=true, description="抽2牌" },
                    new CardData { name="紫金葫芦", volumeId="xiyou", color="Mu", manaCost=2, description="敛玉+20% <color=#FF0000>·销毁</color>", destroyOnUse=true },
                    new CardData { name="照妖镜", volumeId="xiyou", color="Mu", manaCost=1, description="天运+15%" },
                    new CardData { name="锁妖塔", volumeId="xiyou", color="Tu", manaCost=3, armor=10, statusEffect="Freeze", statusChance=0.5f, description="+10护甲 <color=#FF0000>·冰封</color>" },
                    new CardData { name="烽火连天", volumeId="xiyou", color="Huo", manaCost=3, baseDamage=120, attackPattern="MultiShot", projectileCount=3, description="120伤害（占位·待制作）" },
                },
                enemies = new List<EnemyData>
                {
                    new EnemyData { name="花果山猴兵", volumeId="xiyou", baseHealth=15, xp=2, maxHit=1, armor=0, isBoss=false, difficulty=1 },
                    new EnemyData { name="天兵", volumeId="xiyou", baseHealth=80, xp=3, maxHit=2, armor=10, isBoss=false, difficulty=2 },
                    new EnemyData { name="天将", volumeId="xiyou", baseHealth=300, xp=5, maxHit=4, armor=30, isBoss=false, difficulty=3 },
                    new EnemyData { name="妖将", volumeId="xiyou", baseHealth=600, xp=35, maxHit=5, armor=60, isBoss=true, difficulty=3 },
                    new EnemyData { name="花果妖王", volumeId="xiyou", baseHealth=1200, xp=50, maxHit=12, armor=120, isBoss=true, difficulty=3 },
                    new EnemyData { contentId="enemy.xiyou.mixed_world_demon", name="混世魔王", shortDescription="盘踞水脏洞的魔王，仗一口长刀欺压花果山群猴。", volumeId="xiyou", baseHealth=1200, xp=50, maxHit=12, armor=120, isBoss=true, difficulty=3, gridWidth=3, gridDepth=2, bodySize="Giant", knockbackMultiplier=0.25f, canBeDisplaced=false, disarmMultiplier=0.5f },
                    new EnemyData { name="天宫守将", volumeId="xiyou", baseHealth=3000, xp=55, maxHit=14, armor=185, isBoss=true, difficulty=4 },
                    new EnemyData { name="六耳猕猴", volumeId="xiyou", baseHealth=5000, xp=65, maxHit=16, armor=250, isBoss=true, difficulty=5 },
                    new EnemyData { name="牛魔王", volumeId="xiyou", baseHealth=8000, xp=70, maxHit=18, armor=400, isBoss=true, difficulty=6 },
                    new EnemyData { name="大鹏金翅", volumeId="xiyou", baseHealth=12000, xp=75, maxHit=20, armor=500, isBoss=true, difficulty=7 },
                    new EnemyData { name="六圣真君", volumeId="xiyou", baseHealth=20000, xp=80, maxHit=22, armor=600, isBoss=true, difficulty=8 },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_ridge_minion", name="巡岭小妖", shortDescription="奉命巡视双叉岭旧道的妖卒，见到行人便围上来。", volumeId="xiyou", baseHealth=25, xp=2, maxHit=1, armor=0, isBoss=false, difficulty=1, behaviorProfileId="behavior.enemy.xiyou.shuangchaling_ridge-minion.v1" },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_techushi", name="特处士", shortDescription="隐居双叉岭的牛妖，守着被妖墨侵蚀的古印。", volumeId="xiyou", baseHealth=600, xp=35, maxHit=4, armor=0, maxArmor=60, isBoss=true, difficulty=2, gridWidth=2, gridDepth=1, bodySize="Large", knockbackMultiplier=0.25f, canBeDisplaced=false, disarmMultiplier=0.5f, behaviorProfileId="behavior.enemy.xiyou.shuangchaling_techushi.v1" },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_coiled_snake_boss", name="盘蛇妖", shortDescription="盘踞岭道的蛇妖，以长躯封住深入山腹的去路。", volumeId="xiyou", baseHealth=400, xp=30, maxHit=2, armor=0, isBoss=true, difficulty=2, gridWidth=1, gridDepth=2, behaviorProfileId="behavior.enemy.xiyou.shuangchaling_coiled-snake-boss.v1" },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_huchang", name="虎伥", shortDescription="葬身虎口的游魂，迷失幽壑，仍受虎妖驱使。", volumeId="xiyou", baseHealth=75, xp=4, maxHit=2, armor=0, maxArmor=18, isBoss=false, difficulty=2, gridWidth=1, gridDepth=1, bodySize="Medium", behaviorProfileId="behavior.enemy.xiyou.shuangchaling_huchang.v1" },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_xiongshanjun", name="熊山君", shortDescription="盘踞幽壑的熊妖，身躯如山，惯以蛮力压阵。", volumeId="xiyou", baseHealth=750, xp=40, maxHit=7, armor=0, maxArmor=75, isBoss=true, difficulty=3, gridWidth=2, gridDepth=1, bodySize="Large", knockbackMultiplier=0.25f, canBeDisplaced=false, disarmMultiplier=0.5f },
                    new EnemyData { contentId="enemy.xiyou.shuangchaling_chanteng", name="缠藤妖", shortDescription="虎穴深处受妖气催生的藤魅，伏地缠足，伺机收紧。", volumeId="xiyou", baseHealth=60, xp=4, maxHit=1, armor=0, maxArmor=6, isBoss=false, difficulty=2, gridWidth=1, gridDepth=1, bodySize="Medium", behaviorProfileId="behavior.enemy.xiyou.shuangchaling_chanteng.v1" },
                    new EnemyData { contentId="enemy.xiyou.yinjiangjun", name="寅将军", shortDescription="双叉岭虎妖之首，一记裂岭爪可碎山石。", volumeId="xiyou", baseHealth=1800, xp=50, maxHit=3, armor=0, maxArmor=120, isBoss=true, difficulty=3, gridWidth=3, gridDepth=2, bodySize="Giant", knockbackMultiplier=0.25f, canBeDisplaced=false, disarmMultiplier=0.5f, behaviorProfileId="behavior.enemy.xiyou.yinjiangjun.v1" },
                },
                relics = new List<RelicData>
                {
                    new RelicData { name="金刚不坏", description="护甲+50%", relicType=15, unlockLocation="西游卷 Boss" },
                    new RelicData { name="火眼金睛", description="天运+30%", relicType=15, unlockLocation="西游卷隐藏" },
                },
                evolutions = new List<EvolutionData>
                {
                    new EvolutionData { primaryCard="火尖枪", secondaryCards = new List<string>{"菠菜"}, resultCard="三昧真火" },
                    new EvolutionData { primaryCard="风火轮", secondaryCards = new List<string>{"复制器"}, resultCard="烽火连天" },
                },
                dungeons = new List<DungeonData>
                {
                    new DungeonData { name="花果山秘洞", volumeId="xiyou", difficulty=1, floors=4, unlockCondition="初始", reward="玉印", hasBoss=true, bossName="花果妖王" },
                    new DungeonData { name="天宫外苑", volumeId="xiyou", difficulty=2, floors=5, unlockCondition="通关花果山", reward="观星台", hasBoss=true, bossName="天宫守将" },
                    new DungeonData { name="幽冥渡口", volumeId="xiyou", difficulty=3, floors=5, unlockCondition="通关天宫", reward="无", hasBoss=true, bossName="幽冥渡者" },
                },
            };
        }

        // ========== 大明卷 ==========
        static VolumeContent GetDaming()
        {
            return new VolumeContent
            {
                volumeId = "daming",
                displayName = "大明卷",
                crawlers = new List<CrawlerData>
                {
                    new CrawlerData { name="戚继光", volumeId="daming", triggerColor="Huo", duration=5,
                        cardMana=1, enterEffect="鸳鸯阵", triggerEffect="弹射物+2",
                        unlockCondition="关卡4找到封印台", recruitPrice=1000, playstyleTag="攻击铺场" },
                    new CrawlerData { name="李时珍", volumeId="daming", triggerColor="Tu", duration=7,
                        cardMana=1, enterEffect="本草纲目", triggerEffect="每遭遇回1HP",
                        unlockCondition="累计恢复1000HP", recruitPrice=1360, playstyleTag="治疗辅助" },
                    new CrawlerData { name="刘伯温", volumeId="daming", triggerColor="Mu", duration=6,
                        cardMana=0, enterEffect="推背图", triggerEffect="明悟+1%",
                        unlockCondition="首次进入关卡4", recruitPrice=10, playstyleTag="经验引擎" },
                    new CrawlerData { name="王阳明", volumeId="daming", triggerColor="Shui", duration=4,
                        cardMana=1, enterEffect="知行合一", triggerEffect="+1手牌",
                        unlockCondition="关卡5找到封印台", recruitPrice=2200, playstyleTag="扩手牌" },
                    new CrawlerData { name="郑和", volumeId="daming", triggerColor="Yang", duration=8,
                        cardMana=1, enterEffect="七下西洋", triggerEffect="敛玉+25%",
                        unlockCondition="通关关卡7 Boss", recruitPrice=4800, playstyleTag="经济万能" },
                },
                cards = new List<CardData>
                {
                    new CardData { name="戚家枪", volumeId="daming", color="Huo", manaCost=0, baseDamage=15, description="15伤害" },
                    new CardData { name="狼筅", volumeId="daming", color="Huo", manaCost=1, baseDamage=30, statusEffect="Knockback", statusChance=0.4f, description="30伤害<color=#FF0000>·击退</color>" },
                    new CardData { name="神机箭", volumeId="daming", color="Huo", manaCost=2, baseDamage=60, description="60伤害" },
                    new CardData { name="虎蹲炮", volumeId="daming", color="Huo", manaCost=3, baseDamage=130, statusEffect="Burn", statusChance=0.5f, description="130伤害<color=#FF0000>·灼烧</color>" },
                    new CardData { name="红衣大炮", volumeId="daming", color="Huo", manaCost=4, baseDamage=190, description="190伤害" },
                    new CardData { name="鸳鸯甲", volumeId="daming", color="Tu", manaCost=1, armor=5, description="+5护甲" },
                    new CardData { name="锦衣卫令", volumeId="daming", color="Mu", manaCost=1, description="手牌+1 抽1牌" },
                    new CardData { name="本草方", volumeId="daming", color="Mu", manaCost=1, heal=10, destroyOnUse=true, description="+10气血 <color=#FF0000>·销毁</color>" },
                    new CardData { name="天工开物", volumeId="daming", color="Shui", manaCost=2, manaGain=3, description="+3灵力" },
                    new CardData { name="永乐大典", volumeId="daming", color="Shui", manaCost=3, manaGain=4, description="+4灵力 抽1牌" },
                    new CardData { name="宝船图", volumeId="daming", color="Yang", manaCost=0, isWild=true, destroyOnUse=true, description="+50灵玉 <color=#FF0000>·销毁</color>" },
                    new CardData { name="龙纹佩", volumeId="daming", color="Mu", manaCost=2, description="锋锐+20%" },
                    new CardData { name="玄铁盾", volumeId="daming", color="Tu", manaCost=2, armor=8, statusEffect="Freeze", statusChance=0.3f, description="+8护甲 <color=#FF0000>·冰封</color>" },
                },
                enemies = new List<EnemyData>
                {
                    new EnemyData { name="倭寇", volumeId="daming", baseHealth=40, xp=3, maxHit=2, armor=6, isBoss=false, difficulty=2 },
                    new EnemyData { name="山贼", volumeId="daming", baseHealth=60, xp=4, maxHit=3, armor=10, isBoss=false, difficulty=2 },
                    new EnemyData { name="锦衣卫叛徒", volumeId="daming", baseHealth=200, xp=5, maxHit=4, armor=30, isBoss=false, difficulty=3 },
                    new EnemyData { name="倭寇大将", volumeId="daming", baseHealth=800, xp=40, maxHit=6, armor=80, isBoss=true, difficulty=3 },
                    new EnemyData { name="皇城魔将", volumeId="daming", baseHealth=3000, xp=55, maxHit=14, armor=185, isBoss=true, difficulty=5 },
                    new EnemyData { name="土木堡亡魂", volumeId="daming", baseHealth=5000, xp=60, maxHit=16, armor=250, isBoss=true, difficulty=6 },
                    new EnemyData { name="东厂督主", volumeId="daming", baseHealth=9000, xp=65, maxHit=18, armor=350, isBoss=true, difficulty=7 },
                    new EnemyData { name="建文遗灵", volumeId="daming", baseHealth=15000, xp=75, maxHit=20, armor=500, isBoss=true, difficulty=8 },
                },
                relics = new List<RelicData>
                {
                    new RelicData { name="戚家军旗", description="数量+1(全角色)", relicType=15, unlockLocation="大明卷 Boss" },
                    new RelicData { name="永乐通宝", description="敛玉+20%", relicType=15, unlockLocation="大明卷隐藏" },
                },
                evolutions = new List<EvolutionData>
                {
                    new EvolutionData { primaryCard="戚家枪", secondaryCards = new List<string>{"护腕"}, resultCard="神机箭" },
                    new EvolutionData { primaryCard="狼筅", secondaryCards = new List<string>{"菠菜"}, resultCard="虎蹲炮" },
                    new EvolutionData { primaryCard="神机箭", secondaryCards = new List<string>{"复制器"}, resultCard="红衣大炮" },
                },
                dungeons = new List<DungeonData>
                {
                    new DungeonData { name="皇城地宫", volumeId="daming", difficulty=2, floors=5, unlockCondition="关卡1 Lv10", reward="引路灯", hasBoss=true, bossName="皇城魔将" },
                    new DungeonData { name="书院密室", volumeId="daming", difficulty=3, floors=5, unlockCondition="通关皇城", reward="符录", hasBoss=true, bossName="书院守护" },
                    new DungeonData { name="古战场遗迹", volumeId="daming", difficulty=4, floors=5, unlockCondition="通关断桥", reward="爆裂符", hasBoss=true, bossName="土木堡亡魂" },
                },
            };
        }

        // ========== 修仙卷 ==========
        static VolumeContent GetXiuxian()
        {
            return new VolumeContent
            {
                volumeId = "xiuxian",
                displayName = "修仙卷",
                crawlers = new List<CrawlerData>
                {
                    new CrawlerData { name="剑修·凌霄", volumeId="xiuxian", triggerColor="Huo", duration=4,
                        cardMana=1, enterEffect="万剑归宗", triggerEffect="伤害+50%",
                        unlockCondition="以10%招煞通关任一地牢", recruitPrice=1300, playstyleTag="高倍率输出" },
                    new CrawlerData { name="丹修·灵枢", volumeId="xiuxian", triggerColor="Tu", duration=7,
                        cardMana=1, enterEffect="九转金丹", triggerEffect="凝神+2",
                        unlockCondition="累计收集5000灵玉", recruitPrice=1120, playstyleTag="凝神长线" },
                    new CrawlerData { name="法修·元神", volumeId="xiuxian", triggerColor="Mu", duration=3,
                        cardMana=0, enterEffect="天地法相", triggerEffect="抽1张牌",
                        unlockCondition="关卡8找到封印台", recruitPrice=1440, playstyleTag="抽牌循环" },
                    new CrawlerData { name="符修·天书", volumeId="xiuxian", triggerColor="Shui", duration=3,
                        cardMana=0, enterEffect="全手牌降费1", triggerEffect="缴械1敌人",
                        unlockCondition="找到并打出天书(Lv35解锁)", recruitPrice=2800, playstyleTag="降费控制" },
                    new CrawlerData { name="体修·蛮荒", volumeId="xiuxian", triggerColor="Yang", duration=8,
                        cardMana=1, enterEffect="肉身成圣", triggerEffect="敛玉+25%",
                        unlockCondition="击败关卡11 Boss", recruitPrice=4800, playstyleTag="经济万能" },
                },
                cards = new List<CardData>
                {
                    new CardData { name="飞剑术", volumeId="xiuxian", color="Huo", manaCost=0, baseDamage=20, description="20伤害" },
                    new CardData { name="御剑术", volumeId="xiuxian", color="Huo", manaCost=1, baseDamage=55, statusEffect="Crit", statusChance=0.2f, description="55伤害<color=#FF0000>·暴击</color>" },
                    new CardData { name="万剑诀", volumeId="xiuxian", color="Huo", manaCost=2, baseDamage=70, description="70伤害" },
                    new CardData { name="天雷符", volumeId="xiuxian", color="Huo", manaCost=3, baseDamage=140, statusEffect="Knockback", statusChance=0.4f, description="140伤害<color=#FF0000>·击退</color>" },
                    new CardData { name="诛仙剑", volumeId="xiuxian", color="Huo", manaCost=4, baseDamage=200, description="200伤害" },
                    new CardData { name="护体灵光", volumeId="xiuxian", color="Tu", manaCost=1, armor=6, description="+6护甲" },
                    new CardData { name="聚灵阵", volumeId="xiuxian", color="Mu", manaCost=1, description="明悟+15%" },
                    new CardData { name="筑基丹", volumeId="xiuxian", color="Mu", manaCost=1, heal=20, destroyOnUse=true, description="+20气血 <color=#FF0000>·销毁</color>" },
                    new CardData { name="灵泉涌", volumeId="xiuxian", color="Shui", manaCost=2, manaGain=3, description="+3灵力" },
                    new CardData { name="元神出窍", volumeId="xiuxian", color="Shui", manaCost=3, manaGain=4, baseDamage=30, description="+4灵力 30伤害" },
                    new CardData { name="遁术", volumeId="xiuxian", color="Yang", manaCost=0, isWild=true, description="下张卡费-1" },
                    new CardData { name="天劫符", volumeId="xiuxian", color="Yang", manaCost=0, isWild=true, destroyOnUse=true, statusEffect="Freeze", statusChance=1f, description="<color=#FF0000>·冰封 ·销毁</color>" },
                    new CardData { name="炼器谱", volumeId="xiuxian", color="Mu", manaCost=2, description="数量+2" },
                },
                enemies = new List<EnemyData>
                {
                    new EnemyData { name="妖兽", volumeId="xiuxian", baseHealth=50, xp=4, maxHit=3, armor=8, isBoss=false, difficulty=4 },
                    new EnemyData { name="魔修", volumeId="xiuxian", baseHealth=120, xp=5, maxHit=4, armor=20, isBoss=false, difficulty=4 },
                    new EnemyData { name="剑灵", volumeId="xiuxian", baseHealth=300, xp=6, maxHit=5, armor=40, isBoss=false, difficulty=5 },
                    new EnemyData { name="妖兽精英", volumeId="xiuxian", baseHealth=1000, xp=40, maxHit=8, armor=100, isBoss=true, difficulty=5 },
                    new EnemyData { name="剑冢剑灵", volumeId="xiuxian", baseHealth=4000, xp=60, maxHit=14, armor=200, isBoss=true, difficulty=6 },
                    new EnemyData { name="洞天守护", volumeId="xiuxian", baseHealth=7000, xp=65, maxHit=16, armor=300, isBoss=true, difficulty=7 },
                    new EnemyData { name="渡劫失败者", volumeId="xiuxian", baseHealth=12000, xp=70, maxHit=18, armor=400, isBoss=true, difficulty=8 },
                    new EnemyData { name="丹房魔修", volumeId="xiuxian", baseHealth=18000, xp=75, maxHit=20, armor=500, isBoss=true, difficulty=8 },
                    new EnemyData { name="大乘期老祖", volumeId="xiuxian", baseHealth=25000, xp=80, maxHit=22, armor=600, isBoss=true, difficulty=9 },
                },
                relics = new List<RelicData>
                {
                    new RelicData { name="剑心通明", description="锋锐+25%(全角色)", relicType=15, unlockLocation="修仙卷 Boss" },
                    new RelicData { name="灵脉眼", description="天运+20%,宝石稀有度提升", relicType=15, unlockLocation="修仙卷隐藏" },
                },
                evolutions = new List<EvolutionData>
                {
                    new EvolutionData { primaryCard="飞剑术", secondaryCards = new List<string>{"护腕"}, resultCard="御剑术" },
                    new EvolutionData { primaryCard="御剑术", secondaryCards = new List<string>{"菠菜"}, resultCard="万剑诀" },
                    new EvolutionData { primaryCard="万剑诀", secondaryCards = new List<string>{"复制器"}, resultCard="诛仙剑" },
                },
                dungeons = new List<DungeonData>
                {
                    new DungeonData { name="剑冢", volumeId="xiuxian", difficulty=5, floors=5, unlockCondition="通关古战场", reward="至极余威", hasBoss=true, bossName="剑冢剑灵" },
                    new DungeonData { name="洞天福地", volumeId="xiuxian", difficulty=6, floors=5, unlockCondition="通关剑冢", reward="宝镜", hasBoss=true, bossName="洞天守护" },
                    new DungeonData { name="丹房禁地", volumeId="xiuxian", difficulty=7, floors=6, unlockCondition="通关险桥", reward="无", hasBoss=true, bossName="丹房魔修" },
                },
            };
        }

        private static CardAttackData Attack(
            int damagePerTarget,
            int attackInstanceCount = 1,
            int targetsPerAttack = 1,
            string targetPolicy = "LeftToRight",
            string visualPattern = "SingleShot",
            bool canCrit = false,
            string onHitEffect = null,
            float? onHitChanceOverride = null,
            int bounceHits = 0,
            string bounceSequenceMode = null,
            string areaTriggerMode = "PerSubHit",
            float areaCoefficient = 0.2f)
        {
            var attack = new CardAttackData
            {
                damagePerTarget = damagePerTarget,
                attackInstanceCount = attackInstanceCount,
                targetsPerAttack = targetsPerAttack,
                targetPolicy = targetPolicy,
                visualPattern = visualPattern,
                canCrit = canCrit,
                areaTriggerMode = areaTriggerMode,
                areaCoefficient = areaCoefficient
            };
            if (bounceHits > 0 ||
                !string.IsNullOrWhiteSpace(bounceSequenceMode))
            {
                attack.bounce = new CardBounceData
                {
                    enabled = true,
                    sequenceMode = string.IsNullOrWhiteSpace(
                            bounceSequenceMode)
                        ? nameof(BounceSequenceMode.PerDirectHit)
                        : bounceSequenceMode,
                    hitsPerDirectHit = bounceHits
                };
            }
            if (!string.IsNullOrWhiteSpace(onHitEffect))
            {
                attack.onHitEffects.Add(new CardOnHitEffectData
                {
                    effectType = onHitEffect,
                    overrideChance = onHitChanceOverride.HasValue,
                    chance = onHitChanceOverride.GetValueOrDefault()
                });
            }
            return attack;
        }

        // ========== 全局统计 ==========
        public static int TotalCrawlers => 1 + 5 + 5 + 5; // common + 3 volumes
        public static int TotalCards => GetCommonCards().Count + 15 + 13 + 13; // common(+孙悟空金卡) + 3 volumes
        public static int TotalEnemies => GetCommonEnemies().Count + 10 + 8 + 9;
        public static int TotalRelics => 13 + 2 + 2 + 2;
        public static int TotalEvolutions => 12 + 2 + 3 + 3; // 金箍棒配方移除，待卡牌专项重定
        public static int TotalDungeons => 7 + 3 + 3 + 3;
    }
}
