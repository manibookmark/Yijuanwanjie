using UnityEngine;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// 音频配置（ScriptableObject）
    /// 管理所有音乐和音效剪辑引用
    /// </summary>
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "万界长卷/音频配置")]
    public class AudioConfig : ScriptableObject
    {
        public const string DefaultResourcePath = "Audio/AudioConfig";
        public const string CombatFallbackResourcePath =
            "Audio/Music/普通怪战斗背景2";

        [Header("音乐（按场景）")]
        [Tooltip("首页/仙域（局外主场景）背景音乐")]
        public AudioClip hubMusic;

        [Tooltip("长卷殿音乐（旧字段，未绑 hubMusic 时回退）")]
        public AudioClip hallMusic;

        [Tooltip("地牢探索音乐")]
        public AudioClip dungeonMusic;

        [Tooltip("遭遇战音乐")]
        public AudioClip combatMusic;

        [Tooltip("Boss 战音乐")]
        public AudioClip bossMusic;

        [Tooltip("连击高潮音乐（叠加在战斗音乐上）")]
        public AudioClip comboClimaxMusic;

        [Header("音乐场景校准（乘在 musicVolume 上）")]
        [Tooltip("首页、仙域与前端场景音乐校准")]
        [Range(0.05f, 1f)]
        public float hallMusicVolume = 0.4f;

        [Tooltip("地图/地牢基线音乐相对音量（乘在 musicVolume 上），用于单独压低偏响的地图曲")]
        [Range(0.05f, 1f)]
        public float mapMusicVolume = 0.4f;

        [Tooltip("独立遭遇战音乐校准；正式试玩当前仍沿用 Dungeon 基线")]
        [Range(0.05f, 1f)]
        public float combatMusicVolume = 0.9f;

        [Tooltip("Boss 战音乐校准")]
        [Range(0.05f, 1f)]
        public float bossMusicVolume = 1f;

        [Tooltip("叠势高潮音乐校准")]
        [Range(0.05f, 1f)]
        public float comboClimaxMusicVolume = 0.35f;

        [Header("音效")]
        [Tooltip("出牌：翻纸声 + 笔触")]
        public AudioClip sfxCardPlay;

        [Tooltip("连击递增：墨迹扩散音")]
        public AudioClip sfxComboIncrement;

        [Tooltip("卡牌碎裂：干漆与宣纸结构破裂")]
        public AudioClip sfxCardShatter;

        [Tooltip("天劫倒计时：钟声")]
        public AudioClip sfxTianjie;

        [Tooltip("升级：灵气汇聚与卷轴舒展")]
        public AudioClip sfxLevelUp;

        [Tooltip("宝箱开启：木箱 + 玉石")]
        public AudioClip sfxTreasureOpen;

        [Tooltip("灵丹或治疗：温玉与药气融入")]
        public AudioClip sfxLingdanPickup;

        [Tooltip("Boss 出现：低沉号角")]
        public AudioClip sfxBossAppear;

        [Tooltip("抽牌")]
        public AudioClip sfxCardDraw;

        [Tooltip("卡牌悬停：极轻纸面掠过声；仅让位于出牌、命中与升级音效")]
        public AudioClip sfxCardHover;

        [Tooltip("按钮点击")]
        public AudioClip sfxButtonClick;

        [Tooltip("进入房间")]
        public AudioClip sfxRoomEnter;

        [Tooltip("游方商交互")]
        public AudioClip sfxMerchant;

        [Tooltip("投射物命中")]
        public AudioClip sfxProjectileHit;

        [Tooltip("怪物近身攻击命中：短促扑击 + 肉身/纸偶冲击")]
        public AudioClip sfxEnemyAttack;

        [Tooltip("怪物死亡：纸屑碎裂 + 墨气消散")]
        public AudioClip sfxEnemyDeath;

        [Tooltip("经验释放：金色光点从敌人身上释放")]
        public AudioClip sfxXpRelease;

        [Tooltip("经验群落入经验条；未绑定时回退到 xp-bar-arrive-01")]
        public AudioClip sfxExperienceArrival;

        [Tooltip("奖励正式揭示；未绑定时回退到 reward-relic-01")]
        public AudioClip sfxRewardReveal;

        [Tooltip("阵法启动：低沉嗡鸣；未绑定时回退到 formation-activate")]
        public AudioClip sfxFormationActivate;

        [Tooltip("钤印：完整两秒玉印压落音轨；未绑定时回退到 yuyin-stamp-2s")]
        public AudioClip sfxSealStamp;

        [Header("音量")]
        [Tooltip("全局输出总音量；音乐、SFX 与语音都会受它影响")]
        [Range(0f, 1f)]
        public float masterVolume = 1.0f;
        [Tooltip("音乐总线；最终还会乘对应音乐场景校准")]
        [Range(0f, 1f)]
        public float musicVolume = 0.8f;
        [Tooltip("音效总线；最终还会乘事件类型增益与可选内容素材校准")]
        [Range(0f, 1f)]
        public float sfxVolume = 1.0f;
        [Tooltip("角色演出语音音量（独立于 SFX 与 BGM，设置界面“语音音量”）")]
        [Range(0f, 1f)]
        public float voiceVolume = 1.0f;

        [Header("SFX 事件校准")]
        [Tooltip("各语义事件在 SFX 总线内的相对增益。卡牌、命中、奖励可分别调节。")]
        public AudioCueGainProfile sfxCueGains = new AudioCueGainProfile();

        [Header("连击鼓点")]
        [Tooltip("连击鼓点基础间隔（秒），随连击数递减")]
        public float comboDrumBaseInterval = 0.5f;
        [Tooltip("连击鼓点最小间隔（秒）")]
        public float comboDrumMinInterval = 0.1f;
        [Tooltip("连击鼓点音效")]
        public AudioClip comboDrumSound;

        private static AudioClip _cachedDungeonFallback;
        private static AudioClip _cachedCombatFallback;
        private static AudioClip _cachedExperienceArrivalFallback;
        private static AudioClip _cachedRewardRevealFallback;
        private static AudioClip _cachedFormationActivateFallback;
        private static AudioClip _cachedSealStampFallback;

        private static AudioClip DungeonFallback =>
            _cachedDungeonFallback ??= Resources.Load<AudioClip>(
                "Audio/Music/西游地图背景音");

        private static AudioClip CombatFallback =>
            _cachedCombatFallback ??= Resources.Load<AudioClip>(
                CombatFallbackResourcePath);

        private static AudioClip ExperienceArrivalFallback =>
            _cachedExperienceArrivalFallback ??= Resources.Load<AudioClip>(
                "Audio/SFX/xp-bar-arrive-01");

        private static AudioClip RewardRevealFallback =>
            _cachedRewardRevealFallback ??= Resources.Load<AudioClip>(
                "Audio/SFX/reward-relic-01");

        private static AudioClip FormationActivateFallback =>
            _cachedFormationActivateFallback ??= Resources.Load<AudioClip>(
                "Audio/SFX/formation-activate");

        private static AudioClip SealStampFallback =>
            _cachedSealStampFallback ??= Resources.Load<AudioClip>(
                "Audio/SFX/yuyin-stamp-2s");

        public float GetMusicSceneGain(MusicScene scene)
        {
            return Mathf.Clamp01(scene switch
            {
                MusicScene.Hall => hallMusicVolume,
                MusicScene.Dungeon => mapMusicVolume,
                MusicScene.Combat => combatMusicVolume,
                MusicScene.Boss => bossMusicVolume,
                MusicScene.ComboClimax => comboClimaxMusicVolume,
                _ => hallMusicVolume
            });
        }

        public float GetSfxCueGain(SfxType type)
        {
            return sfxCueGains != null
                ? sfxCueGains.GetCueGain(type)
                : AudioMixPolicy.GetRecommendedSfxCueGain(type);
        }

        /// <summary>仅重置混音数值，不改动任何音乐或音效素材引用。</summary>
        public void ResetMixToRecommended()
        {
            masterVolume = 1f;
            musicVolume = 0.8f;
            sfxVolume = 1f;
            voiceVolume = 1f;
            hallMusicVolume = 0.4f;
            mapMusicVolume = 0.4f;
            combatMusicVolume = 0.9f;
            bossMusicVolume = 1f;
            comboClimaxMusicVolume = 0.35f;
            sfxCueGains ??= new AudioCueGainProfile();
            sfxCueGains.ResetToRecommended();
        }

        /// <summary>
        /// 按场景获取音乐剪辑
        /// </summary>
        public AudioClip GetMusicClip(MusicScene scene)
        {
            AudioClip frontEndFallback = hubMusic != null
                ? hubMusic
                : hallMusic;
            return scene switch
            {
                MusicScene.Hall => frontEndFallback,
                MusicScene.Dungeon => dungeonMusic != null
                    ? dungeonMusic
                    : (DungeonFallback ?? frontEndFallback),
                MusicScene.Combat => combatMusic != null
                    ? combatMusic
                    : (CombatFallback ?? frontEndFallback),
                MusicScene.Boss => bossMusic != null
                    ? bossMusic
                    : (combatMusic != null
                        ? combatMusic
                        : (CombatFallback ?? frontEndFallback)),
                MusicScene.ComboClimax => comboClimaxMusic,
                _ => null
            };
        }

        /// <summary>
        /// 按类型获取音效剪辑
        /// </summary>
        public AudioClip GetSfxClip(SfxType type)
        {
            return type switch
            {
                SfxType.CardPlay => sfxCardPlay,
                SfxType.ComboIncrement => sfxComboIncrement,
                SfxType.CardShatter => sfxCardShatter,
                SfxType.Tianjie => sfxTianjie,
                SfxType.LevelUp => sfxLevelUp,
                SfxType.TreasureOpen => sfxTreasureOpen,
                SfxType.LingdanPickup => sfxLingdanPickup,
                SfxType.BossAppear => sfxBossAppear,
                SfxType.CardDraw => sfxCardDraw,
                SfxType.CardHover => sfxCardHover,
                SfxType.ButtonClick => sfxButtonClick,
                SfxType.RoomEnter => sfxRoomEnter,
                SfxType.Merchant => sfxMerchant,
                SfxType.ProjectileHit => sfxProjectileHit,
                SfxType.EnemyAttack => sfxEnemyAttack,
                SfxType.EnemyDeath => sfxEnemyDeath,
                SfxType.XpRelease => sfxXpRelease,
                SfxType.ExperienceArrival => sfxExperienceArrival != null
                    ? sfxExperienceArrival
                    : ExperienceArrivalFallback,
                SfxType.RewardReveal => sfxRewardReveal != null
                    ? sfxRewardReveal
                    : RewardRevealFallback,
                SfxType.FormationActivate => sfxFormationActivate != null
                    ? sfxFormationActivate
                    : FormationActivateFallback,
                SfxType.SealStamp => sfxSealStamp != null
                    ? sfxSealStamp
                    : SealStampFallback,
                _ => null
            };
        }
    }
}
