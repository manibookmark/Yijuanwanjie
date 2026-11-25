using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 场景名称常量 + 流程管理
    /// Loading → MainMenu → OpeningCG → Dungeon → Combat
    /// </summary>
    public static class SceneNames
    {
        public const string Loading = "Loading";
        public const string MainMenu = "MainMenu";
        public const string Hub = "Hub";
        public const string LevelSelect = "LevelSelect";
        public const string RouteSelect = "RouteSelect";
        public const string Combat = "Combat";
        public const string Dungeon = "Dungeon";
        public const string OpeningCG = "OpeningCG";
        public const string Daozang = "Daozang";
        public const string LevelMap = "LevelMap";
    }

    /// <summary>
    /// 全局游戏状态（跨场景持久化）
    /// </summary>
    public static class GameState
    {
        /// <summary>当前选中的卷（0=神话, 1=历史, 2=修仙）</summary>
        public static int SelectedVolumeId = 0;

        /// <summary>当前选中的地牢索引</summary>
        public static int SelectedDungeonIndex = 0;

        /// <summary>玩家选择的英灵 ID</summary>
        public static string SelectedCrawlerId =
            GameSession.DefaultCrawlerContentId;

        /// <summary>遗留余额入口；与新账号零初始货币保持一致。</summary>
        public static int PlayerLingyu = 0;

        /// <summary>已通关的关卡数</summary>
        public static int ClearedLevels = 0;

        /// <summary>获取当前卷的 ContentDatabase ID</summary>
        public static string GetVolumeId()
        {
            return SelectedVolumeId switch
            {
                0 => "xiyou",
                1 => "daming",
                2 => "xiuxian",
                _ => "xiyou"
            };
        }

        /// <summary>获取当前卷的中文名</summary>
        public static string GetVolumeName()
        {
            return SelectedVolumeId switch
            {
                0 => "神话卷",
                1 => "历史卷",
                2 => "修仙卷",
                _ => "神话卷"
            };
        }
    }

    /// <summary>
    /// 场景切换辅助器
    /// </summary>
    public static class SceneNavigator
    {
        /// <summary>切到 Loading 场景</summary>
        public static void GoToLoading()
        {
            SceneManager.LoadScene(SceneNames.Loading);
        }

        /// <summary>切到主菜单</summary>
        public static void GoToMainMenu()
        {
            SceneManager.LoadScene(SceneNames.MainMenu);
        }

        /// <summary>切到局外神域</summary>
        public static void GoToHub()
        {
            SceneManager.LoadScene(SceneNames.Hub);
        }

        /// <summary>切到关卡选择</summary>
        public static void GoToLevelSelect()
        {
            SceneManager.LoadScene(SceneNames.LevelSelect);
        }

        /// <summary>切到路线选择</summary>
        public static void GoToRouteSelect()
        {
            SceneManager.LoadScene(SceneNames.RouteSelect);
        }

        /// <summary>切到战斗场景</summary>
        public static void GoToCombat()
        {
            SceneManager.LoadScene(SceneNames.Combat);
        }

        /// <summary>切到地牢探索</summary>
        public static void GoToDungeon()
        {
            SceneManager.LoadScene(SceneNames.Dungeon);
        }

        /// <summary>切到道藏（Power-Up 升级界面）</summary>
        public static void GoToDaozang()
        {
            SceneManager.LoadScene(SceneNames.Daozang);
        }

        public static void GoToLevelMap()
        {
            SceneManager.LoadScene(SceneNames.LevelMap);
        }

        /// <summary>切到开场 CG</summary>
        public static void GoToOpeningCG()
        {
            SceneManager.LoadScene(SceneNames.OpeningCG);
        }

        /// <summary>通用场景切换</summary>
        public static void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
