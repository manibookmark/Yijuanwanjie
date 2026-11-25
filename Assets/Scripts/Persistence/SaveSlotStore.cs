using System;
using System.IO;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Persistence
{
    /// <summary>存档槽摘要：只读快照，不写入 GameSession。</summary>
    public sealed class SaveSlotSummary
    {
        public int slot;
        public bool exists;
        public bool corrupt;
        public string savedAtUtc;
        public int clearedLevels;
        public int playerLingyu;
        public int metaLingyu;
        public int relicCount;
        public int arcanasCount;
        public int crawlersCount;
    }

    /// <summary>
    /// 存档槽位唯一所有者：槽位文件命名、活动槽位记忆、旧单档迁移与摘要读取。
    /// GameSaveService.CreateDefault 经此路由，场景侧自动保存无需感知多档。
    /// </summary>
    public sealed class SaveSlotStore
    {
        public const int SlotCount = 3;
        public const string LegacyFileName = "save-v1.json";
#if WANJIE_DEMO
        public const string ActiveSlotPreferenceKey = "save.demo.g1g3.slot.active";
#else
        public const string ActiveSlotPreferenceKey = "save.slot.active";
#endif

        private static SaveSlotStore _current;

        private readonly string _rootDirectory;

        private SaveSlotStore(string rootDirectory) { _rootDirectory = rootDirectory; }

        public static SaveSlotStore Current =>
            _current ??= Create(ResolveDefaultRootDirectory(
                Application.persistentDataPath, DemoReleasePolicy.IsDemoBuild));

        /// <summary>Demo slots and legacy migration never read the full game's directory.</summary>
        public static string ResolveDefaultRootDirectory(string persistentRoot, bool demo)
        {
            if (string.IsNullOrWhiteSpace(persistentRoot))
                throw new ArgumentException("存档根目录不能为空。", nameof(persistentRoot));
            return demo ? Path.Combine(persistentRoot, "Demo-G1G3") : persistentRoot;
        }

        public static SaveSlotStore Create(string rootDirectory)
        {
            var store = new SaveSlotStore(rootDirectory);
            store.MigrateLegacySave();
            return store;
        }

        public string GetSlotPath(int slot)
        {
            ValidateSlot(slot);
            return Path.Combine(_rootDirectory, $"save-slot-{slot}.json");
        }

        public GameSaveService GetSlotService(int slot)
        {
            return new GameSaveService(GetSlotPath(slot));
        }

        public GameSaveService GetActiveService()
        {
            return GetSlotService(ActiveSlot);
        }

        public int ActiveSlot
        {
            get
            {
                int stored = PlayerPrefs.GetInt(ActiveSlotPreferenceKey, 1);
                return stored >= 1 && stored <= SlotCount ? stored : 1;
            }
            set
            {
                ValidateSlot(value);
                PlayerPrefs.SetInt(ActiveSlotPreferenceKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>开始新游戏：绑定槽位为活动槽，后续保存落在该槽。</summary>
        public void BeginNewGame(int slot)
        {
            ActiveSlot = slot;
            // 新槽必须从默认跨场景状态开始，不能继承刚载入的另一槽进度。
            GameSession.Current = new GameSession();
        }

        public bool HasAnySave()
        {
            for (int slot = 1; slot <= SlotCount; slot++)
            {
                if (File.Exists(GetSlotPath(slot)))
                    return true;
            }
            return false;
        }

        /// <summary>最近可继续的槽：活动槽有档优先，否则取最新写入的有档槽；全空返回 0。</summary>
        public int MostRecentFilledSlot()
        {
            if (File.Exists(GetSlotPath(ActiveSlot)))
                return ActiveSlot;

            int bestSlot = 0;
            DateTime bestTime = DateTime.MinValue;
            for (int slot = 1; slot <= SlotCount; slot++)
            {
                string path = GetSlotPath(slot);
                if (!File.Exists(path))
                    continue;
                DateTime written = File.GetLastWriteTimeUtc(path);
                if (written > bestTime)
                {
                    bestTime = written;
                    bestSlot = slot;
                }
            }
            return bestSlot;
        }

        public SaveSlotSummary ReadSummary(int slot)
        {
            string path = GetSlotPath(slot);
            var summary = new SaveSlotSummary { slot = slot };
            if (!File.Exists(path))
                return summary;

            summary.exists = true;
            try
            {
                SaveGameData data = JsonUtility.FromJson<SaveGameData>(
                    File.ReadAllText(path));
                if (data == null || data.meta == null)
                {
                    summary.corrupt = true;
                    return summary;
                }
                summary.savedAtUtc = data.savedAtUtc;
                summary.clearedLevels = data.clearedLevels;
                summary.playerLingyu = data.playerLingyu;
                summary.metaLingyu = data.meta.lingyu;
                summary.relicCount = data.meta.unlockedRelics != null
                    ? data.meta.unlockedRelics.Count : 0;
                summary.arcanasCount = data.meta.unlockedArcanas != null
                    ? data.meta.unlockedArcanas.Count : 0;
                summary.crawlersCount = data.meta.unlockedCrawlers != null
                    ? data.meta.unlockedCrawlers.Count : 0;
            }
            catch (Exception)
            {
                summary.corrupt = true;
            }
            return summary;
        }

        /// <summary>旧单档迁移：仅当槽 1 为空时复制，不覆盖既有槽位，旧文件保留。</summary>
        private void MigrateLegacySave()
        {
            try
            {
                string legacy = Path.Combine(_rootDirectory, LegacyFileName);
                string slotOne = GetSlotPath(1);
                if (!File.Exists(legacy) || File.Exists(slotOne))
                    return;
                Directory.CreateDirectory(_rootDirectory);
                File.Copy(legacy, slotOne);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SaveSlotStore] 旧存档迁移失败，忽略：{exception.Message}");
            }
        }

        private void ValidateSlot(int slot)
        {
            if (slot < 1 || slot > SlotCount)
                throw new ArgumentOutOfRangeException(
                    nameof(slot), $"槽位必须在 1~{SlotCount} 之间，当前 {slot}。");
        }
    }
}
