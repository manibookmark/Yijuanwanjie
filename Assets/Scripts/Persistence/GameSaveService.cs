using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.Persistence
{
    public sealed class GameSaveService
    {
        public const int CurrentSchemaVersion = 6;
        public const string DefaultFileName = "save-v1.json";

        public GameSaveService(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("存档路径不能为空。", nameof(savePath));
            SavePath = Path.GetFullPath(savePath);
        }

        public string SavePath { get; }
        public string BackupPath => SavePath + ".bak";

        public static GameSaveService CreateDefault()
        {
            return SaveSlotStore.Current.GetActiveService();
        }

        public void Save(MetaProgression metaProgression)
        {
            SaveInternal(
                metaProgression,
                TryReadExistingActiveRunPayload());
        }

        /// <summary>
        /// 保存元进度并显式替换活动 Run 载荷。传空字符串表示清除活动 Run；
        /// 普通 <see cref="Save(MetaProgression)"/> 不会误删已有载荷。
        /// </summary>
        public void Save(
            MetaProgression metaProgression,
            string activeDungeonRunPayload)
        {
            SaveInternal(
                metaProgression,
                activeDungeonRunPayload ?? string.Empty);
        }

        /// <summary>
        /// For gameplay transactions whose caller rolls back memory on failure.
        /// An unsupported/failed atomic replace must leave the prior save intact;
        /// never enter the legacy delete-then-move fallback in this contract.
        /// Initial creation uses a same-directory rename with no overwrite.
        /// </summary>
        public void SaveTransactional(MetaProgression metaProgression, string activeDungeonRunPayload)
            => SaveInternal(metaProgression, activeDungeonRunPayload ?? string.Empty,
                requireAtomicReplace: true);

        private void SaveInternal(
            MetaProgression metaProgression,
            string activeDungeonRunPayload,
            bool requireAtomicReplace = false)
        {
            SaveGameData data = Capture(metaProgression);
            data.activeDungeonRunPayload = activeDungeonRunPayload ??
                string.Empty;
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = SavePath + ".tmp";
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));

            if (!File.Exists(SavePath))
            {
                File.Move(temporaryPath, SavePath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, SavePath, BackupPath);
            }
            catch (PlatformNotSupportedException) when (!requireAtomicReplace)
            {
                ReplaceWithBackup(temporaryPath);
            }
            catch (IOException) when (!requireAtomicReplace)
            {
                ReplaceWithBackup(temporaryPath);
            }
        }

        public bool TryLoad(MetaProgression metaProgression, out string error)
        {
            return TryLoad(metaProgression, out _, out error);
        }

        public bool TryLoad(
            MetaProgression metaProgression,
            out SaveGameData loadedData,
            out string error)
        {
            loadedData = null;
            error = null;
            if (!File.Exists(SavePath))
                return false;

            try
            {
                string json = File.ReadAllText(SavePath, Encoding.UTF8);
                SaveGameData data = ReadCompatibleData(json);
                Apply(data, metaProgression);
                loadedData = data;
                return true;
            }
            catch (Exception exception)
            {
                error = $"读取存档失败：{exception.Message}";
                return false;
            }
        }

        public void Delete()
        {
            foreach (string path in new[] { SavePath, BackupPath, SavePath + ".tmp" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        public static SaveGameData Capture(MetaProgression metaProgression)
        {
            var data = new SaveGameData
            {
                schemaVersion = CurrentSchemaVersion,
                applicationVersion = Application.version,
                savedAtUtc = DateTime.UtcNow.ToString("O"),
                selectedVolumeId = GameSession.Current.SelectedVolumeId,
                selectedDungeonIndex = GameSession.Current.SelectedDungeonIndex,
                selectedCrawlerId = GameSession.Current.SelectedCrawlerId,
                playerLingyu = GameSession.Current.PlayerLingyu,
                clearedLevels = GameSession.Current.ClearedLevels,
                meta = CaptureMeta(metaProgression)
            };
            return data;
        }

        public static void Apply(SaveGameData data, MetaProgression metaProgression)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            MetaProgressSaveData meta = data.meta ?? new MetaProgressSaveData();
            CardUnlockProgressSnapshot cardUnlockProgress = ReadCardUnlockProgress(meta.cardUnlockProgress);
            EventUnlockProgressSnapshot eventUnlockProgress = (meta.eventUnlockProgress ??
                throw new InvalidOperationException("Missing event-unlock progress.")).Read(cardUnlockProgress);
            // Validate all account evidence before touching the shared session or
            // any live owner. Restoration is forbidden inside either transaction.
            var validatedExperience = new ExperienceCollectionProgress();
            validatedExperience.RestoreSaveData(meta.experienceCollectionProgress ??
                throw new InvalidOperationException("Missing experience collection progress."));
            ExperienceCollectionProgressSnapshot experience = validatedExperience.Capture();
            if (metaProgression != null && (metaProgression.EventUnlocks.IsCommitting ||
                metaProgression.ExperienceCollections.IsCommitting))
                throw new InvalidOperationException("Account restoration cannot reenter a strict commit.");

            GameSession.Current.SelectedVolumeId = data.selectedVolumeId;
            GameSession.Current.SelectedDungeonIndex = data.selectedDungeonIndex;
            GameSession.Current.SelectedCrawlerId = string.IsNullOrWhiteSpace(data.selectedCrawlerId)
                ? GameSession.DefaultCrawlerContentId
                : data.selectedCrawlerId;
            GameSession.Current.PlayerLingyu = data.playerLingyu;
            GameSession.Current.ClearedLevels = data.clearedLevels;

            if (metaProgression == null)
                return;

            metaProgression.lingyu = meta.lingyu;
            metaProgression.totalLingyuEarned = meta.totalLingyuEarned;
            metaProgression.powerUpLevels = ToDictionary(meta.powerUpLevels);
            metaProgression.unlockedCrawlers = Copy(meta.unlockedCrawlers);
            metaProgression.unlockedRelics = Copy(meta.unlockedRelics);
            metaProgression.unlockedRelicTypes = meta.unlockedRelicTypes != null
                ? new List<RelicType>(meta.unlockedRelicTypes) : new List<RelicType>();
            metaProgression.unlockedArcanas = Copy(meta.unlockedArcanas);
            metaProgression.unlockedCards = Copy(meta.unlockedCards);
            metaProgression.CardUnlocks.Restore(cardUnlockProgress);
            metaProgression.EventUnlocks.Restore(eventUnlockProgress);
            metaProgression.ExperienceCollections.Restore(experience);
            metaProgression.unlockedSeals = Copy(meta.unlockedSeals);
            metaProgression.clearedLevelContentIds = Copy(
                meta.clearedLevelContentIds);
            metaProgression.completedPresentations = Copy(
                meta.completedPresentations);
            metaProgression.equippedArcana = meta.equippedArcana;
            metaProgression.hasEquippedArcanaEffect = meta.hasEquippedArcanaEffect;
            metaProgression.equippedArcanaEffect = meta.equippedArcanaEffect;
            metaProgression.equippedCrawlers = Copy(meta.equippedCrawlers);
            metaProgression.shopUnlocked = meta.shopUnlocked;
            metaProgression.tier2Unlocked = meta.tier2Unlocked;
            metaProgression.tier3Unlocked = meta.tier3Unlocked;
            metaProgression.blacksmithUnlocked = meta.blacksmithUnlocked;
            metaProgression.arcanasUnlocked = meta.arcanasUnlocked;
            metaProgression.rewardRefreshUnlocked =
                meta.rewardRefreshUnlocked;
            metaProgression.rewardAbandonUnlocked =
                meta.rewardAbandonUnlocked;
            metaProgression.rewardExcludeUnlocked =
                meta.rewardExcludeUnlocked;
        }

        private static MetaProgressSaveData CaptureMeta(MetaProgression source)
        {
            if (source == null)
                return new MetaProgressSaveData {
                    experienceCollectionProgress = new ExperienceCollectionProgress().CaptureSaveData() };

            var data = new MetaProgressSaveData
            {
                lingyu = source.lingyu,
                totalLingyuEarned = source.totalLingyuEarned,
                unlockedCrawlers = Copy(source.unlockedCrawlers),
                unlockedRelics = Copy(source.unlockedRelics),
                unlockedRelicTypes = source.unlockedRelicTypes != null
                    ? new List<RelicType>(source.unlockedRelicTypes) : new List<RelicType>(),
                unlockedArcanas = Copy(source.unlockedArcanas),
                unlockedCards = Copy(source.unlockedCards),
                cardUnlockProgress = CardUnlockProgressSaveData.Capture(source.CardUnlocks.Capture()),
                eventUnlockProgress = EventUnlockProgressSaveData.Capture(source.EventUnlocks.Capture()),
                experienceCollectionProgress = source.ExperienceCollections.CaptureSaveData(),
                unlockedSeals = Copy(source.unlockedSeals),
                clearedLevelContentIds = Copy(source.clearedLevelContentIds),
                completedPresentations = Copy(source.completedPresentations),
                equippedArcana = source.equippedArcana,
                hasEquippedArcanaEffect = source.hasEquippedArcanaEffect,
                equippedArcanaEffect = source.equippedArcanaEffect,
                equippedCrawlers = Copy(source.equippedCrawlers),
                shopUnlocked = source.shopUnlocked,
                tier2Unlocked = source.tier2Unlocked,
                tier3Unlocked = source.tier3Unlocked,
                blacksmithUnlocked = source.blacksmithUnlocked,
                arcanasUnlocked = source.arcanasUnlocked,
                rewardRefreshUnlocked = source.rewardRefreshUnlocked,
                rewardAbandonUnlocked = source.rewardAbandonUnlocked,
                rewardExcludeUnlocked = source.rewardExcludeUnlocked
            };

            foreach (KeyValuePair<string, int> item in source.powerUpLevels)
                data.powerUpLevels.Add(new StringIntEntry { key = item.Key, value = item.Value });
            data.powerUpLevels.Sort((left, right) => string.CompareOrdinal(left.key, right.key));
            // Capture is also a publication boundary: a gate with no matching
            // accepted CardUnlock evidence must not reach a durable save.
            data.eventUnlockProgress.Read(data.cardUnlockProgress.Read());
            return data;
        }

        private static CardUnlockProgressSnapshot ReadCardUnlockProgress(CardUnlockProgressSaveData saved)
        {
            if (saved == null) return default;
            return saved.Read();
        }

        private void ReplaceWithBackup(string temporaryPath)
        {
            File.Copy(SavePath, BackupPath, true);
            File.Delete(SavePath);
            File.Move(temporaryPath, SavePath);
        }

        private string TryReadExistingActiveRunPayload()
        {
            if (!File.Exists(SavePath))
                return string.Empty;
            try
            {
                string json = File.ReadAllText(SavePath, Encoding.UTF8);
                SaveGameData data = ReadCompatibleData(json);
                return data.activeDungeonRunPayload ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static SaveGameData ReadCompatibleData(string json)
        {
            bool hasVersion = FindAccountField(json, "schemaVersion", out string rawVersion);
            int version = 0;
            if (hasVersion && (!int.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out version) ||
                version.ToString(CultureInfo.InvariantCulture) != rawVersion))
                throw new InvalidOperationException("Account schema must be an explicit nonnegative integer.");
            bool hasMeta = FindAccountField(json, "meta", out string accountMeta);
            bool hasExperience = hasMeta && accountMeta != "null" &&
                FindAccountField(accountMeta, "experienceCollectionProgress", out _);
            if (version < 6 && hasExperience)
                throw new InvalidOperationException("Legacy account saves cannot contain future experience evidence.");
            ExperienceCollectionProgressSaveData experience = null;
            if (version >= 6)
            {
                if (!hasExperience || !FindAccountField(accountMeta, "experienceCollectionProgress", out string rawExperience))
                    throw new InvalidOperationException("Current saves require explicit experience collection progress.");
                experience = ExperienceCollectionProgress.ReadSaveData(rawExperience);
            }
            SaveGameData data = JsonUtility.FromJson<SaveGameData>(json)
                ?? throw new InvalidOperationException("Save data could not be decoded.");
            data.schemaVersion = version;
            if (data.meta != null) data.meta.experienceCollectionProgress = experience;
            if (data.schemaVersion >= 5)
            {
                if (!JsonObjectShape.TryGetField(json, "meta", out string eventMeta) ||
                    !JsonObjectShape.TryGetField(eventMeta, "eventUnlockProgress", out string eventProgress) ||
                    eventProgress == "null")
                    throw new InvalidOperationException("Current saves require explicit event-unlock progress.");
                EventUnlockProgressSaveData.ValidateSerializedProgress(eventProgress);
            }
            string rawProgress = null;
            bool hasProgress = JsonObjectShape.TryGetField(json, "meta", out string rawMeta) &&
                JsonObjectShape.TryGetField(rawMeta, "cardUnlockProgress", out rawProgress);
            if (hasProgress)
            {
                if (rawProgress != "null") CardUnlockProgressSaveData.ValidateSerializedProgress(rawProgress);
                else if (data.meta != null)
                    data.meta.cardUnlockProgress = CardUnlockProgressSaveData.Capture(default);
            }
            else if (data.meta != null)
            {
                // Legacy saves have no behavior evidence. Normalize the absent
                // wire field explicitly instead of trusting Unity's nested default.
                data.meta.cardUnlockProgress = CardUnlockProgressSaveData.Capture(default);
            }
            return SaveGameMigrator.Migrate(data);
        }

        // JsonObjectShape intentionally merges missing and duplicate results.
        // Account envelope fields need a presence decision before that lookup,
        // especially when an old schema must reject even a null future field.
        private static bool FindAccountField(string json, string field, out string raw)
        {
            raw = null;
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Missing account object.");
            string text = json.Trim();
            if (text.Length < 2 || text[0] != '{' || text[text.Length - 1] != '}')
                throw new InvalidOperationException("Account checkpoint must be an object.");
            int depth = 0, count = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (value == '{' || value == '[') { depth++; continue; }
                if (value == '}' || value == ']') { depth--; continue; }
                if (value != '"') continue;
                int start = index;
                var name = new StringBuilder();
                bool escaped = false, ended = false;
                while (++index < text.Length)
                {
                    value = text[index];
                    if (value == '"') { ended = true; break; }
                    if (value == '\\')
                    {
                        escaped = true;
                        if (++index >= text.Length) break;
                        value = text[index];
                        if (value == 'u')
                        {
                            if (index + 4 >= text.Length || !ushort.TryParse(text.Substring(index + 1, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                                throw new InvalidOperationException("Malformed account JSON escape.");
                            name.Append((char)code); index += 4;
                        }
                        else
                        {
                            switch (value)
                            {
                                case '"': case '\\': case '/': name.Append(value); break;
                                case 'b': name.Append('\b'); break;
                                case 'f': name.Append('\f'); break;
                                case 'n': name.Append('\n'); break;
                                case 'r': name.Append('\r'); break;
                                case 't': name.Append('\t'); break;
                                default: throw new InvalidOperationException("Malformed account JSON escape.");
                            }
                        }
                    }
                    else name.Append(value);
                }
                if (!ended) throw new InvalidOperationException("Unterminated account string at " + start + ".");
                int next = index + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                if (depth != 1 || next >= text.Length || text[next] != ':' || name.ToString() != field) continue;
                if (escaped || ++count > 1)
                    throw new InvalidOperationException("Account evidence fields cannot be escaped or duplicated.");
            }
            if (count == 0) return false;
            if (!JsonObjectShape.TryGetField(text, field, out raw))
                throw new InvalidOperationException("Malformed account evidence field.");
            return true;
        }

        private static Dictionary<string, int> ToDictionary(IEnumerable<StringIntEntry> entries)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (entries == null)
                return result;
            foreach (StringIntEntry item in entries)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.key))
                    continue;
                result[item.key] = item.value;
            }
            return result;
        }

        private static List<string> Copy(IEnumerable<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }
    }
}
