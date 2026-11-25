using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 运行时美术资源加载器
    /// 从 Resources/Sprites/ 加载已预裁切的透明PNG
    /// </summary>
    public static class RuntimeArtLoader
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private const string Root = "Sprites/";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => ClearCache();

        // ===== 背景图 =====
        public static Sprite GetMainMenuBackground() => LoadSprite(Root + "Backgrounds/main-menu-bg");
        public static Sprite GetLevelSelectBackground() => LoadSprite(Root + "Backgrounds/level-select-bg");
        public static Sprite GetScrollSelectBackground() => LoadSprite(Root + "Backgrounds/scroll-select-bg");
        public static Sprite GetBattleBackground() =>
            LoadSprite(Root + "Backgrounds/battle-huaguo-papercraft-v1");

        // ===== UI 元素 =====
        public static Sprite GetLogo() => LoadSprite(Root + "Logo/logo");
        public static Sprite GetStartButton() => LoadSprite(Root + "UI/btn-start-game");
        public static Sprite GetScrollPanel() => LoadSprite(Root + "UI/scroll-panel");

        // ===== 角色立绘 =====
        public static Sprite GetWukongPortrait() => LoadSprite(Root + "Heroes/wukong-hero-portrait");

        // ===== 战斗精灵图（已预裁切为独立透明PNG） =====
        public static Sprite GetWukongBattleFrame(int frameIndex)
            => LoadSprite(Root + $"Heroes/WukongFrames/wukong_{frameIndex:00}");

        public static Sprite[] GetWukongBattleFrames()
        {
            var frames = new Sprite[12];
            for (int i = 0; i < 12; i++)
                frames[i] = LoadSprite(Root + $"Heroes/WukongFrames/wukong_{i:00}");
            return frames;
        }

        // ===== 敌人（已预裁切为独立透明PNG） =====
        public static Sprite GetMonsterFrame(int frameIndex)
            => LoadSprite(Root + $"Enemies/MonsterFrames/monster_{frameIndex:00}");

        public static Sprite[] GetMonsterFrames()
        {
            var frames = new Sprite[2];
            frames[0] = LoadSprite(Root + "Enemies/MonsterFrames/monster_idle");
            frames[1] = LoadSprite(Root + "Enemies/MonsterFrames/monster_attack");
            return frames;
        }

        public static Sprite GetPapercraftBat() =>
            LoadSprite(Root + "Enemies/Papercraft/bat");

        public static Sprite GetPapercraftSnake() =>
            LoadSprite(Root + "Enemies/Papercraft/snake");

        public static Sprite GetPapercraftTopFog() =>
            LoadSprite(Root + "Enemies/Papercraft/top-fog");

        // ===== 传送门按钮 =====
        public enum PortalVolume { Myth, History, Xiuxian }
        public enum PortalState { Normal, Active }

        public static Sprite GetPortalButton(PortalVolume volume, PortalState state)
        {
            string volName = volume.ToString().ToLower();
            string stateName = state.ToString().ToLower();
            return LoadSprite(Root + $"UI/portal-{volName}-{stateName}");
        }

        // ===== VFX 素材 =====
        public static Sprite GetVFXSprite(string name) => LoadSprite(Root + $"VFX/{name}");
        public static Sprite GetKnifeProjectile() => LoadSprite(Root + "VFX/knife_projectile");
        public static Sprite GetSparkParticle() => LoadSprite(Root + "VFX/spark_particle");
        public static Sprite GetBloodParticle() => LoadSprite(Root + "VFX/blood_particle");

        // ===== 增益图标 =====
        public static Sprite GetBuffIcon(string glyphId)
            => LoadSprite(Root + $"UI/Battle/BuffIcons/{glyphId}");

        // ===== 天箓悬停动画帧（Hub 设施，12fps） =====
        public const int TianluHoverFrameCount = 49;

        public static Sprite GetTianluHoverFrame(int frameIndex)
            => LoadSprite(Root + $"Hub/TianluHover/tianlu_hover_{frameIndex:00}");

        public static Sprite[] GetTianluHoverFrames()
        {
            var frames = new Sprite[TianluHoverFrameCount];
            for (int i = 0; i < TianluHoverFrameCount; i++)
                frames[i] = LoadSprite(Root + $"Hub/TianluHover/tianlu_hover_{i:00}");
            return frames;
        }

        // ===== 卡牌素材 =====
        public static Sprite GetCardFrame() => LoadSprite(Root + "Cards/Frames/card_frame");
        public static Sprite GetCardIllustration(string cardName) => LoadSprite(Root + $"Cards/Illustrations/{cardName}");

        /// <summary>
        /// 加载完整卡面图（含框架+插图+文字，即 *-final.png）
        /// </summary>
        public static Sprite GetFinalCard(string cardName) => LoadSprite(Root + $"Cards/FinalCards/{cardName}");

        // ===== 核心加载 =====
        public static Sprite LoadSprite(string resourcePath)
        {
            if (_cache.TryGetValue(resourcePath, out Sprite cached) && cached != null)
                return cached;

            // 优先按 Sprite 加载（TextureImporter 已设为 Sprite 类型时直接返回）
            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null)
            {
                _cache[resourcePath] = sprite;
                return sprite;
            }

            // 回退：Texture2D → Sprite
            Texture2D tex = Resources.Load<Texture2D>(resourcePath);
            if (tex != null)
            {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _cache[resourcePath] = sprite;
                return sprite;
            }

            Debug.LogWarning($"[RuntimeArtLoader] 未找到资源: {resourcePath}");
            return null;
        }

        public static void ClearCache() => _cache.Clear();
    }
}
