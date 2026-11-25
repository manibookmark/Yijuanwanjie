using System;
using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// 全局 SFX 语义增益表。这里保存“某类事件应该多响”的产品知识；
    /// 素材引用和 AudioSource 生命周期不属于本类。
    /// </summary>
    [Serializable]
    public sealed class AudioCueGainProfile
    {
        [Header("高频轻反馈")]
        [Tooltip("卡牌悬停。应当只是轻微触感，不抢出牌和命中。")]
        [Range(0f, 1f)] public float cardHover = 0.18f;
        [Tooltip("普通按钮点击。")]
        [Range(0f, 1f)] public float buttonClick = 0.30f;

        [Header("卡牌操作")]
        [Tooltip("抽牌纸声。")]
        [Range(0f, 1f)] public float cardDraw = 0.40f;
        [Tooltip("卡牌释放或专属出牌音。")]
        [Range(0f, 1f)] public float cardPlay = 0.66f;
        [Tooltip("叠势递增反馈。")]
        [Range(0f, 1f)] public float comboIncrement = 0.42f;

        [Header("战斗结果")]
        [Tooltip("投射物或武器实际命中。")]
        [Range(0f, 1f)] public float projectileHit = 0.56f;
        [Tooltip("怪物攻击落点。当前素材能量偏低，初值已用满。")]
        [Range(0f, 1f)] public float enemyAttack = 1.00f;
        [Tooltip("怪物死亡。")]
        [Range(0f, 1f)] public float enemyDeath = 0.48f;
        [Tooltip("卡牌碎裂。")]
        [Range(0f, 1f)] public float cardShatter = 0.48f;

        [Header("资源与成长")]
        [Tooltip("普通经验释放。")]
        [Range(0f, 1f)] public float experienceRelease = 0.38f;
        [Tooltip("经验粒子落入经验条；素材瞬态较强，保持克制。")]
        [Range(0f, 1f)] public float experienceArrival = 0.25f;
        [Tooltip("灵丹、灵玉等轻量拾取。")]
        [Range(0f, 1f)] public float pickup = 0.42f;
        [Tooltip("升级阶段反馈。")]
        [Range(0f, 1f)] public float levelUp = 0.60f;
        [Tooltip("奖励正式揭示；强度来自层次与尾韵，不靠顶峰值。")]
        [Range(0f, 1f)] public float rewardReveal = 0.66f;

        [Header("场景与里程碑")]
        [Range(0f, 1f)] public float treasureOpen = 0.60f;
        [Range(0f, 1f)] public float roomEnter = 0.56f;
        [Range(0f, 1f)] public float merchant = 0.56f;
        [Range(0f, 1f)] public float bossAppear = 0.68f;
        [Range(0f, 1f)] public float tianjie = 0.68f;

        [Header("阵法")]
        [Tooltip("阵法启动嗡鸣；低频厚重但短促，保持中等增益。")]
        [Range(0f, 1f)] public float formationActivate = 0.60f;
        [Tooltip("钤印完整音轨；保留压印重量，同时避免盖过其它场景音。")]
        [Range(0f, 1f)] public float sealStamp = 0.72f;

        /// <summary>返回事件在 SFX 总线内的相对增益。</summary>
        public float GetCueGain(SfxType type)
        {
            return Mathf.Clamp01(type switch
            {
                SfxType.CardHover => cardHover,
                SfxType.ButtonClick => buttonClick,
                SfxType.CardDraw => cardDraw,
                SfxType.CardPlay => cardPlay,
                SfxType.ComboIncrement => comboIncrement,
                SfxType.ProjectileHit => projectileHit,
                SfxType.EnemyAttack => enemyAttack,
                SfxType.EnemyDeath => enemyDeath,
                SfxType.CardShatter => cardShatter,
                SfxType.XpRelease => experienceRelease,
                SfxType.ExperienceArrival => experienceArrival,
                SfxType.LingdanPickup => pickup,
                SfxType.LevelUp => levelUp,
                SfxType.RewardReveal => rewardReveal,
                SfxType.TreasureOpen => treasureOpen,
                SfxType.RoomEnter => roomEnter,
                SfxType.Merchant => merchant,
                SfxType.BossAppear => bossAppear,
                SfxType.Tianjie => tianjie,
                SfxType.FormationActivate => formationActivate,
                SfxType.SealStamp => sealStamp,
                _ => 0.48f
            });
        }

        public void ResetToRecommended()
        {
            cardHover = 0.18f;
            buttonClick = 0.30f;
            cardDraw = 0.40f;
            cardPlay = 0.66f;
            comboIncrement = 0.42f;
            projectileHit = 0.56f;
            enemyAttack = 1.00f;
            enemyDeath = 0.48f;
            cardShatter = 0.48f;
            experienceRelease = 0.38f;
            experienceArrival = 0.25f;
            pickup = 0.42f;
            levelUp = 0.60f;
            rewardReveal = 0.66f;
            treasureOpen = 0.60f;
            roomEnter = 0.56f;
            merchant = 0.56f;
            bossAppear = 0.68f;
            tianjie = 0.68f;
            formationActivate = 0.60f;
            sealStamp = 0.72f;
        }
    }

    /// <summary>
    /// 全局混音计算的唯一入口。最终增益始终由“用户总线 × 场景/事件增益 ×
    /// 可选内容校准”组成，调用方无需知道线性音量如何组合。
    /// </summary>
    public static class AudioMixPolicy
    {
        private static readonly AudioCueGainProfile RecommendedSfxCueGains =
            new AudioCueGainProfile();

        public static float GetMusicGain(MusicScene scene, AudioConfig config)
        {
            float bus = config != null ? config.musicVolume : 0.8f;
            return GetMusicGain(scene, config, bus);
        }

        public static float GetMusicGain(
            MusicScene scene,
            AudioConfig config,
            float playerMusicVolume)
        {
            float sceneGain = config != null
                ? config.GetMusicSceneGain(scene)
                : GetRecommendedMusicSceneGain(scene);
            return Mathf.Clamp01(playerMusicVolume * sceneGain);
        }

        public static float GetSfxGain(
            SfxType type,
            AudioConfig config,
            float contentTrim = 1f)
        {
            float bus = config != null ? config.sfxVolume : 1f;
            return GetSfxGain(type, config, bus, contentTrim);
        }

        public static float GetSfxGain(
            SfxType type,
            AudioConfig config,
            float playerSfxVolume,
            float contentTrim)
        {
            float cue = config != null
                ? config.GetSfxCueGain(type)
                : GetRecommendedSfxCueGain(type);
            return Mathf.Clamp01(
                playerSfxVolume * cue * Mathf.Max(0f, contentTrim));
        }

        public static float GetRecommendedMusicSceneGain(MusicScene scene)
        {
            return scene switch
            {
                MusicScene.Hall => 0.40f,
                MusicScene.Dungeon => 0.40f,
                MusicScene.Combat => 0.90f,
                MusicScene.Boss => 1.00f,
                MusicScene.ComboClimax => 0.35f,
                _ => 0.40f
            };
        }

        public static float GetRecommendedSfxCueGain(SfxType type)
        {
            return RecommendedSfxCueGains.GetCueGain(type);
        }

        public static float LinearToDecibels(float gain)
        {
            return gain <= 0.0001f
                ? -80f
                : 20f * Mathf.Log10(gain);
        }
    }
}
