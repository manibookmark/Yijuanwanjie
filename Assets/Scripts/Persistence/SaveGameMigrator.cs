using System;
using System.Collections.Generic;
using WanjieScroll.Meta;

namespace WanjieScroll.Persistence
{
    public static class SaveGameMigrator
    {
        public static SaveGameData Migrate(SaveGameData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion < 6 && data.meta?.experienceCollectionProgress != null)
                throw new InvalidOperationException("Legacy account saves cannot contain future experience evidence.");
            if (data.schemaVersion > GameSaveService.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"存档版本 {data.schemaVersion} 高于当前支持版本 {GameSaveService.CurrentSchemaVersion}。");
            }

            while (data.schemaVersion < GameSaveService.CurrentSchemaVersion)
            {
                switch (data.schemaVersion)
                {
                    case 0:
                        MigrateV0ToV1(data);
                        break;
                    case 1:
                        MigrateV1ToV2(data);
                        break;
                    case 2:
                        MigrateV2ToV3(data);
                        break;
                    case 3:
                        MigrateV3ToV4(data);
                        break;
                    case 4:
                        MigrateV4ToV5(data);
                        break;
                    case 5:
                        MigrateV5ToV6(data);
                        break;
                    default:
                        throw new InvalidOperationException($"没有存档版本 {data.schemaVersion} 的迁移器。");
                }
            }

            // A current DTO must not silently gain a missing ledger. Only the
            // explicit version transition above is allowed to create empty history.
            var experience = new ExperienceCollectionProgress();
            experience.RestoreSaveData(data.meta?.experienceCollectionProgress ??
                throw new InvalidOperationException("Current account saves require experience evidence."));
            Normalize(data);
            return data;
        }

        private static void MigrateV0ToV1(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            data.schemaVersion = 1;
        }

        private static void MigrateV1ToV2(SaveGameData data)
        {
            data.activeDungeonRunPayload ??= string.Empty;
            data.schemaVersion = 2;
        }

        private static void MigrateV2ToV3(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            data.meta.unlockedSeals =
                LingyinCatalog.CreateDefaultUnlockedSealContentIds();
            data.schemaVersion = 3;
        }

        private static void MigrateV3ToV4(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            MetaProgressSaveData meta = data.meta;
            // Version-transition compensation for prior G2 whitelist access.
            // This is not evidence that a VC condition or a historical level was reached.
            bool hadG1Access = meta.shopUnlocked ||
                (meta.clearedLevelContentIds?.Exists(id => string.Equals(id,
                    MetaProgression.G1FacilityUnlockLevelContentId,
                    StringComparison.OrdinalIgnoreCase)) ?? false);
            meta.unlockedCards ??= new List<string>();
            if (hadG1Access)
            {
                AddTransitionCard(meta.unlockedCards, "legacy.card.common.13a386c5");
                AddTransitionCard(meta.unlockedCards, "legacy.card.common.c0191ee0");
            }
            meta.cardUnlockProgress = CardUnlockProgressSaveData.Capture(new CardUnlockProgressSnapshot(0));
            data.schemaVersion = 4;
        }

        private static void MigrateV4ToV5(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            // The old metric watermark proves a count, not a completed whole Run.
            // Preserve that ledger; do not invent a historical event achievement.
            data.meta.eventUnlockProgress = EventUnlockProgressSaveData.Capture(default);
            data.schemaVersion = 5;
        }

        private static void MigrateV5ToV6(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            // Old saves recorded no all-source XP collection ledger. Explicit
            // zero starts future accounting; it says nothing about past earnings.
            data.meta.experienceCollectionProgress = new ExperienceCollectionProgress().CaptureSaveData();
            data.schemaVersion = 6;
        }

        private static void AddTransitionCard(List<string> cards, string id)
        {
            if (!cards.Exists(value => string.Equals(value?.Trim(), id,
                    StringComparison.OrdinalIgnoreCase))) cards.Add(id);
        }

        private static void Normalize(SaveGameData data)
        {
            data.meta ??= new MetaProgressSaveData();
            data.meta.powerUpLevels ??= new List<StringIntEntry>();
            data.meta.unlockedCrawlers ??= new List<string>();
            data.meta.unlockedRelics ??= new List<string>();
            data.meta.unlockedArcanas ??= new List<string>();
            data.meta.unlockedCards ??= new List<string>();
            data.meta.cardUnlockProgress ??= CardUnlockProgressSaveData.Capture(new CardUnlockProgressSnapshot(0));
            data.meta.unlockedSeals ??=
                LingyinCatalog.CreateDefaultUnlockedSealContentIds();
            data.meta.clearedLevelContentIds ??= new List<string>();
            data.meta.completedPresentations ??= new List<string>();
            data.meta.equippedCrawlers ??= new List<string>();
            data.activeDungeonRunPayload ??= string.Empty;
        }
    }
}
