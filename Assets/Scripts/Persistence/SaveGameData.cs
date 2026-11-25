using System;
using System.Collections.Generic;
using System.Globalization;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.Persistence
{
    [Serializable]
    public sealed class SaveGameData
    {
        public int schemaVersion = GameSaveService.CurrentSchemaVersion;
        public string applicationVersion;
        public string savedAtUtc;
        public int selectedVolumeId;
        public int selectedDungeonIndex;
        public string selectedCrawlerId;
        public int playerLingyu;
        public int clearedLevels;
        [UnityEngine.TextArea]
        public string activeDungeonRunPayload = string.Empty;
        public MetaProgressSaveData meta = new MetaProgressSaveData();
    }

    [Serializable]
    public sealed class MetaProgressSaveData
    {
        public int lingyu;
        public int totalLingyuEarned;
        public List<StringIntEntry> powerUpLevels = new List<StringIntEntry>();
        public List<string> unlockedCrawlers = new List<string>();
        public List<string> unlockedRelics = new List<string>();
        public List<RelicType> unlockedRelicTypes = new List<RelicType>();
        public List<string> unlockedArcanas = new List<string>();
        public List<string> unlockedCards = new List<string>();
        public EventUnlockProgressSaveData eventUnlockProgress = EventUnlockProgressSaveData.Capture(default);
        // Account schema 6 requires an explicit ledger. Null in older DTOs means
        // unavailable history, never a reconstruction from levels or statistics.
        public ExperienceCollectionProgressSaveData experienceCollectionProgress;
        public CardUnlockProgressSaveData cardUnlockProgress =
            CardUnlockProgressSaveData.Capture(new CardUnlockProgressSnapshot(0));
        public List<string> unlockedSeals =
            LingyinCatalog.CreateDefaultUnlockedSealContentIds();
        public List<string> clearedLevelContentIds = new List<string>();
        public List<string> completedPresentations = new List<string>();
        public string equippedArcana;
        public bool hasEquippedArcanaEffect;
        public ArcanaEffect equippedArcanaEffect = ArcanaEffect.Custom;
        public List<string> equippedCrawlers = new List<string>();
        public bool shopUnlocked;
        public bool tier2Unlocked;
        public bool tier3Unlocked;
        public bool blacksmithUnlocked;
        public bool arcanasUnlocked;
        public bool rewardRefreshUnlocked;
        public bool rewardAbandonUnlocked;
        public bool rewardExcludeUnlocked;
    }

    /// <summary>
    /// Schema 4 adds Hire's accepted actual-reaction proof; schema 3 retains its
    /// independent Duplicate and Evo permanent proofs. Legacy
    /// versions retain only their already recorded gates; accumulated counts
    /// cannot manufacture a later achievement. Validate presence before Unity
    /// supplies missing values. The outer account schema is unchanged.
    /// </summary>
    [Serializable]
    public sealed class EventUnlockProgressSaveData
    {
        public const int CurrentSchemaVersion = 4;
        public int schemaVersion;
        public bool chickenShopUnlocked;
        public bool manaOfferingUnlocked;
        public string manaOfferingQualifyingRunId = string.Empty;
        public bool duplicateOfferingUnlocked;
        public string duplicateOfferingQualifyingRunId = string.Empty;
        public bool evoOfferingUnlocked;
        public string evoOfferingQualifyingRunId = string.Empty;
        public string evoOfferingDungeonContentId = string.Empty;
        public List<string> evoOfferingCompletedFloorContentIds = new List<string>();
        public bool gunForHireUnlocked;
        public string gunForHireQualifyingRunId = string.Empty;

        public static bool SupportsSchemaVersion(int version) => version >= 1 && version <= CurrentSchemaVersion;

        public static EventUnlockProgressSaveData Capture(EventUnlockProgressSnapshot state)
            => new EventUnlockProgressSaveData { schemaVersion = CurrentSchemaVersion,
                chickenShopUnlocked = state.ChickenShopUnlocked,
                manaOfferingUnlocked = state.ManaOfferingUnlocked,
                manaOfferingQualifyingRunId = state.ManaOfferingQualifyingRunId ?? string.Empty,
                duplicateOfferingUnlocked = state.DuplicateOfferingUnlocked,
                duplicateOfferingQualifyingRunId = state.DuplicateOfferingQualifyingRunId ?? string.Empty,
                evoOfferingUnlocked = state.EvoOfferingUnlocked,
                evoOfferingQualifyingRunId = state.EvoOfferingProof?.RunId ?? string.Empty,
                evoOfferingDungeonContentId = state.EvoOfferingProof?.DungeonContentId ?? string.Empty,
                evoOfferingCompletedFloorContentIds = state.EvoOfferingProof == null ? new List<string>()
                    : new List<string>(state.EvoOfferingProof.CompletedFloorContentIds),
                gunForHireUnlocked = state.GunForHireUnlocked,
                gunForHireQualifyingRunId = state.GunForHireQualifyingRunId ?? string.Empty };

        public EventUnlockProgressSnapshot Read(CardUnlockProgressSnapshot permanentProgress)
        {
            if (!SupportsSchemaVersion(schemaVersion))
                throw new InvalidOperationException("Unsupported event-unlock progress version.");
            if (chickenShopUnlocked && EventUnlockProgress.CountFood(permanentProgress.LifetimeFacts) < EventUnlockProgress.ChickenShopRequiredFood)
                throw new InvalidOperationException("ChickenShop unlock lacks its permanent food evidence.");
            if (schemaVersion < 3 && HasLaterProof())
                throw new InvalidOperationException("Legacy event progress cannot carry Duplicate or Evo proof.");
            if (schemaVersion < 4 && HasHireProof())
                throw new InvalidOperationException("Legacy event progress cannot carry GunForHire proof.");
            if (schemaVersion == 1)
            {
                if (manaOfferingUnlocked || !string.IsNullOrEmpty(manaOfferingQualifyingRunId))
                    throw new InvalidOperationException("Legacy event progress cannot carry ManaOffering proof.");
                return new EventUnlockProgressSnapshot(chickenShopUnlocked);
            }
            var qualification = new ManaOfferingEligibility(manaOfferingUnlocked, manaOfferingQualifyingRunId);
            if (qualification.IsUnlocked)
            {
                CardUnlockRunCheckpoint matchingRun = FindRun(permanentProgress, qualification.QualifyingRunId);
                if (matchingRun == null || EventUnlockProgress.CountManaOfferingPlays(matchingRun.Facts) < EventUnlockProgress.ManaOfferingRequiredPlays)
                    throw new InvalidOperationException("ManaOffering unlock lacks its qualifying accepted-Run evidence.");
            }
            if (schemaVersion == 2)
                return new EventUnlockProgressSnapshot(chickenShopUnlocked, qualification.IsUnlocked, qualification.QualifyingRunId);
            var duplicate = new DuplicateOfferingEligibility(duplicateOfferingUnlocked, duplicateOfferingQualifyingRunId);
            if (duplicate.IsUnlocked)
            {
                CardUnlockRunCheckpoint matching = FindRun(permanentProgress, duplicate.QualifyingRunId);
                if (matching == null || EventUnlockProgress.CountDuplicateOfferingPlays(matching.Facts) < EventUnlockProgress.DuplicateOfferingRequiredPlays)
                    throw new InvalidOperationException("Duplicate unlock lacks its same accepted-Run play evidence.");
            }
            EvoOfferingRunProof evo = ReadEvoProof();
            if (evo != null && FindRun(permanentProgress, evo.RunId) == null)
                throw new InvalidOperationException("Evo unlock lacks its matching accepted-Run checkpoint.");
            var hire = new GunForHireEligibility(gunForHireUnlocked, gunForHireQualifyingRunId);
            if (hire.IsUnlocked)
            {
                CardUnlockRunCheckpoint contributing = FindRun(permanentProgress, hire.QualifyingRunId);
                if (contributing == null || EventUnlockProgress.CountCrawlerActions(contributing.Facts) <= 0 ||
                    EventUnlockProgress.CountAcceptedCrawlerActions(permanentProgress) < EventUnlockProgress.GunForHireRequiredTriggers)
                    throw new InvalidOperationException("GunForHire unlock lacks 200 accepted actual crawler reactions and its contributing Run.");
            }
            return new EventUnlockProgressSnapshot(chickenShopUnlocked, qualification.IsUnlocked, qualification.QualifyingRunId,
                duplicate.IsUnlocked, duplicate.QualifyingRunId, evoOfferingUnlocked, evo,
                hire.IsUnlocked, hire.QualifyingRunId);
        }

        /// <summary>Compatibility for Chicken-only callers; lifetime facts cannot validate Mana's one-Run proof.</summary>
        public EventUnlockProgressSnapshot Read(CardUnlockFactsSnapshot permanentFacts)
        {
            if (permanentFacts == null) throw new ArgumentNullException(nameof(permanentFacts));
            if (manaOfferingUnlocked || !string.IsNullOrEmpty(manaOfferingQualifyingRunId) || HasLaterProof() || HasHireProof())
                throw new InvalidOperationException("Offering permissions require the complete accepted-Run checkpoint.");
            return Read(new CardUnlockProgressSnapshot(0, permanentFacts.Counts));
        }

        public static void ValidateSerializedProgress(string raw)
        {
            if (!JsonObjectShape.TryGetField(raw, "schemaVersion", out string version) ||
                (version != "1" && version != "2" && version != "3" && version != "4") ||
                !JsonObjectShape.TryGetField(raw, "chickenShopUnlocked", out string chicken) ||
                (chicken != "true" && chicken != "false"))
                throw new InvalidOperationException("Event-unlock progress needs explicit version and boolean gate.");
            if (version != "4") RequireLegacyClosed(raw, "gunForHireUnlocked", "gunForHireQualifyingRunId");
            if (version == "1")
            {
                // Old serializers had neither field. If present, they must still
                // describe the locked migration, never fabricated current proof.
                RequireLegacyClosed(raw, "manaOfferingUnlocked", "manaOfferingQualifyingRunId");
                RequireLaterClosed(raw);
                var legacy = UnityEngine.JsonUtility.FromJson<EventUnlockProgressSaveData>(raw);
                if (legacy.manaOfferingUnlocked || !string.IsNullOrEmpty(legacy.manaOfferingQualifyingRunId) || legacy.HasLaterProof() || legacy.HasHireProof())
                    throw new InvalidOperationException("Legacy event progress cannot carry ManaOffering proof.");
                return;
            }
            if (version == "2") RequireLaterClosed(raw);
            if (!JsonObjectShape.TryGetField(raw, "manaOfferingUnlocked", out string mana) ||
                (mana != "true" && mana != "false") ||
                !JsonObjectShape.TryGetField(raw, "manaOfferingQualifyingRunId", out string proof) ||
                proof.Length < 2 || proof[0] != '"' || proof[proof.Length - 1] != '"')
                throw new InvalidOperationException("ManaOffering progress needs an explicit boolean and string proof.");
            var current = UnityEngine.JsonUtility.FromJson<EventUnlockProgressSaveData>(raw);
            if (current.schemaVersion.ToString(CultureInfo.InvariantCulture) != version ||
                current.chickenShopUnlocked != (chicken == "true") || current.manaOfferingUnlocked != (mana == "true"))
                throw new InvalidOperationException("Decoded event gates disagree with their explicit wire values.");
            // Type and presence are checked above; the decoded stable ID must
            // also agree with its gate before any account/session is changed.
            _ = new ManaOfferingEligibility(current.manaOfferingUnlocked, current.manaOfferingQualifyingRunId);
            if (version == "2")
            {
                if (current.HasLaterProof()) throw new InvalidOperationException("Legacy event progress cannot carry later proof.");
                return;
            }
            bool duplicate = RequiredBoolean(raw, "duplicateOfferingUnlocked");
            bool evo = RequiredBoolean(raw, "evoOfferingUnlocked");
            RequireString(raw, "duplicateOfferingQualifyingRunId");
            RequireString(raw, "evoOfferingQualifyingRunId");
            RequireString(raw, "evoOfferingDungeonContentId");
            if (!JsonObjectShape.TryGetField(raw, "evoOfferingCompletedFloorContentIds", out string floors) ||
                !JsonObjectShape.TryGetArrayItems(floors, out IReadOnlyList<string> floorItems))
                throw new InvalidOperationException("Evo proof requires an explicit completed-floor array.");
            foreach (string floor in floorItems)
                if (floor.Length < 2 || floor[0] != '"' || floor[floor.Length - 1] != '"')
                    throw new InvalidOperationException("Evo completed-floor proof entries must be stable ID strings.");
            if (current.duplicateOfferingUnlocked != duplicate || current.evoOfferingUnlocked != evo ||
                current.evoOfferingCompletedFloorContentIds == null || current.evoOfferingCompletedFloorContentIds.Count != floorItems.Count)
                throw new InvalidOperationException("Decoded later event permissions disagree with explicit wire values.");
            _ = new DuplicateOfferingEligibility(duplicate, current.duplicateOfferingQualifyingRunId);
            _ = current.ReadEvoProof();
            if (version == "4")
            {
                bool hire = RequiredBoolean(raw, "gunForHireUnlocked");
                RequireString(raw, "gunForHireQualifyingRunId");
                if (current.gunForHireUnlocked != hire)
                    throw new InvalidOperationException("Decoded Hire permission disagrees with its explicit wire value.");
                _ = new GunForHireEligibility(hire, current.gunForHireQualifyingRunId);
            }
        }

        private EvoOfferingRunProof ReadEvoProof()
        {
            if (evoOfferingCompletedFloorContentIds == null)
                throw new InvalidOperationException("Evo proof requires a completed-floor array.");
            if (!evoOfferingUnlocked)
            {
                if (!string.IsNullOrEmpty(evoOfferingQualifyingRunId) || !string.IsNullOrEmpty(evoOfferingDungeonContentId) ||
                    evoOfferingCompletedFloorContentIds.Count != 0)
                    throw new InvalidOperationException("A locked Evo permission cannot carry a floor proof.");
                return null;
            }
            return new EvoOfferingRunProof(evoOfferingQualifyingRunId, evoOfferingDungeonContentId, evoOfferingCompletedFloorContentIds);
        }

        private bool HasLaterProof() => duplicateOfferingUnlocked || !string.IsNullOrEmpty(duplicateOfferingQualifyingRunId) ||
            evoOfferingUnlocked || !string.IsNullOrEmpty(evoOfferingQualifyingRunId) || !string.IsNullOrEmpty(evoOfferingDungeonContentId) ||
            (evoOfferingCompletedFloorContentIds != null && evoOfferingCompletedFloorContentIds.Count != 0);

        private bool HasHireProof() => gunForHireUnlocked || !string.IsNullOrEmpty(gunForHireQualifyingRunId);

        private static CardUnlockRunCheckpoint FindRun(CardUnlockProgressSnapshot progress, string id)
        {
            foreach (CardUnlockRunCheckpoint accepted in progress.AcceptedRuns)
                if (string.Equals(accepted.RunId, id, StringComparison.Ordinal)) return accepted;
            return null;
        }

        private static void RequireLaterClosed(string raw)
        {
            RequireLegacyClosed(raw, "duplicateOfferingUnlocked", "duplicateOfferingQualifyingRunId");
            RequireLegacyClosed(raw, "evoOfferingUnlocked", "evoOfferingQualifyingRunId");
            RequireLegacyEmptyString(raw, "evoOfferingDungeonContentId");
            if (JsonObjectShape.TryGetField(raw, "evoOfferingCompletedFloorContentIds", out string floors) &&
                (!JsonObjectShape.TryGetArrayItems(floors, out IReadOnlyList<string> entries) || entries.Count != 0))
                throw new InvalidOperationException("Legacy event progress cannot carry later floor proof.");
        }

        private static void RequireLegacyClosed(string raw, string gate, string proof)
        {
            if (JsonObjectShape.TryGetField(raw, gate, out string flag) && flag != "false")
                throw new InvalidOperationException("Legacy event progress cannot carry later permission.");
            RequireLegacyEmptyString(raw, proof);
        }

        private static void RequireLegacyEmptyString(string raw, string field)
        {
            if (JsonObjectShape.TryGetField(raw, field, out string value) && value != "\"\"")
                throw new InvalidOperationException("Legacy event progress cannot carry later Run proof.");
        }

        private static bool RequiredBoolean(string raw, string field)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) || (value != "true" && value != "false"))
                throw new InvalidOperationException("Event progress requires explicit boolean " + field + ".");
            return value == "true";
        }

        private static void RequireString(string raw, string field)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) || value.Length < 2 ||
                value[0] != '"' || value[value.Length - 1] != '"')
                throw new InvalidOperationException("Event progress requires explicit string " + field + ".");
        }
    }

    /// <summary>Versioned permanent facts, separate from the legacy eligibility list.</summary>
    [Serializable]
    public sealed class CardUnlockProgressSaveData
    {
        public const int CurrentSchemaVersion = 3;
        public int schemaVersion;
        // Zero means no historical level evidence, including migrated accounts.
        public int highestPlayerLevel;
        public List<CardUnlockCountSaveData> lifetimeCounts;
        public List<CardUnlockRunCheckpointSaveData> acceptedRuns;
        public List<CardUnlockCrawlerLevelSaveData> crawlerHighestLevels;

        public static CardUnlockProgressSaveData Capture(CardUnlockProgressSnapshot snapshot)
        {
            // Validate the whole checkpoint before projecting any DTO fields.
            snapshot = new CardUnlockProgressSnapshot(snapshot.HighestPlayerLevel,
                snapshot.LifetimeFacts.Counts, snapshot.AcceptedRuns, snapshot.CrawlerHighestLevels);
            var data = new CardUnlockProgressSaveData
            {
                schemaVersion = CurrentSchemaVersion,
                highestPlayerLevel = snapshot.HighestPlayerLevel,
                lifetimeCounts = CardUnlockCountSaveData.Capture(snapshot.LifetimeFacts),
                acceptedRuns = new List<CardUnlockRunCheckpointSaveData>(),
                crawlerHighestLevels = new List<CardUnlockCrawlerLevelSaveData>()
            };
            foreach (CardUnlockRunCheckpoint run in snapshot.AcceptedRuns)
                data.acceptedRuns.Add(new CardUnlockRunCheckpointSaveData
                { runId = run.RunId, counts = CardUnlockCountSaveData.Capture(run.Facts) });
            foreach (CardUnlockCrawlerLevel level in snapshot.CrawlerHighestLevels)
                data.crawlerHighestLevels.Add(new CardUnlockCrawlerLevelSaveData
                { crawlerContentId = level.CrawlerContentId, highestPlayerLevel = level.HighestPlayerLevel });
            return data;
        }

        public CardUnlockProgressSnapshot Read()
        {
            if (schemaVersion == 1)
                return new CardUnlockProgressSnapshot(highestPlayerLevel);
            if (schemaVersion != 2 && schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported permanent card-unlock progress version.");
            if (lifetimeCounts == null || acceptedRuns == null)
                throw new InvalidOperationException("Permanent card-unlock counts and Run watermarks are required.");
            var runs = new List<CardUnlockRunCheckpoint>();
            foreach (CardUnlockRunCheckpointSaveData run in acceptedRuns)
            {
                if (run == null || run.counts == null)
                    throw new InvalidOperationException("An accepted Run requires its complete counts.");
                runs.Add(new CardUnlockRunCheckpoint(run.runId, CardUnlockCountSaveData.Read(run.counts)));
            }
            var levels = new List<CardUnlockCrawlerLevel>();
            if (schemaVersion >= 3)
            {
                if (crawlerHighestLevels == null)
                    throw new InvalidOperationException("Permanent card-unlock crawler level maxima are required.");
                foreach (CardUnlockCrawlerLevelSaveData level in crawlerHighestLevels)
                {
                    if (level == null) throw new InvalidOperationException("Crawler level maxima cannot contain null entries.");
                    levels.Add(new CardUnlockCrawlerLevel(level.crawlerContentId, level.highestPlayerLevel));
                }
            }
            return new CardUnlockProgressSnapshot(highestPlayerLevel,
                CardUnlockCountSaveData.Read(lifetimeCounts).Counts, runs, levels);
        }

        /// <summary>Check wire presence before Unity supplies empty collections.
        /// Schema 1 intentionally carries only the historical level.</summary>
        public static void ValidateSerializedProgress(string rawProgressJson)
        {
            if (!JsonObjectShape.TryGetField(rawProgressJson, "schemaVersion", out string rawVersion) ||
                !int.TryParse(rawVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                throw new InvalidOperationException("Permanent card-unlock progress version is missing.");
            if (version == 1) return;
            if (version != 2 && version != CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported permanent card-unlock progress version.");
            ValidateSerializedCounts(RequiredArray(rawProgressJson, "lifetimeCounts"));
            foreach (string run in RequiredArray(rawProgressJson, "acceptedRuns"))
                ValidateSerializedCounts(RequiredArray(run, "counts"));
            if (version >= 3)
                foreach (string level in RequiredArray(rawProgressJson, "crawlerHighestLevels"))
                    if (!JsonObjectShape.TryGetField(level, "crawlerContentId", out string crawler) ||
                        string.IsNullOrWhiteSpace(crawler) || crawler[0] != '"' ||
                        !JsonObjectShape.TryGetField(level, "highestPlayerLevel", out string rawLevel) ||
                        !int.TryParse(rawLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        throw new InvalidOperationException("Crawler level maxima need explicit stable IDs and levels.");
        }

        private static IReadOnlyList<string> RequiredArray(string objectJson, string name)
        {
            if (!JsonObjectShape.TryGetField(objectJson, name, out string rawArray) ||
                !JsonObjectShape.TryGetArrayItems(rawArray, out IReadOnlyList<string> items))
                throw new InvalidOperationException("Permanent card-unlock progress requires array " + name + ".");
            return items;
        }

        private static void ValidateSerializedCounts(IReadOnlyList<string> counts)
        {
            foreach (string count in counts)
                if (!JsonObjectShape.TryGetField(count, "metric", out string metric) ||
                    !int.TryParse(metric, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                    !JsonObjectShape.TryGetField(count, "count", out string value) ||
                    !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new InvalidOperationException("Permanent card-unlock counters need explicit metric and count.");
        }

    }

    [Serializable]
    public sealed class CardUnlockCrawlerLevelSaveData
    {
        public string crawlerContentId;
        public int highestPlayerLevel;
    }

    [Serializable]
    public sealed class CardUnlockCountSaveData
    {
        public CardUnlockMetric metric;
        public string sourceContentId;
        public int count;

        internal static List<CardUnlockCountSaveData> Capture(CardUnlockFactsSnapshot facts)
        {
            var result = new List<CardUnlockCountSaveData>();
            foreach (CardUnlockCount value in facts.Counts)
                result.Add(new CardUnlockCountSaveData
                { metric = value.Metric, sourceContentId = value.SourceContentId, count = value.Count });
            return result;
        }

        internal static CardUnlockFactsSnapshot Read(IEnumerable<CardUnlockCountSaveData> counts)
        {
            var values = new List<CardUnlockCount>();
            foreach (CardUnlockCountSaveData value in counts ?? Array.Empty<CardUnlockCountSaveData>())
            {
                if (value == null) throw new InvalidOperationException("Null permanent card-unlock count.");
                values.Add(new CardUnlockCount(value.metric, value.sourceContentId, value.count));
            }
            return new CardUnlockFactsSnapshot(values);
        }
    }

    [Serializable]
    public sealed class CardUnlockRunCheckpointSaveData
    {
        public string runId;
        public List<CardUnlockCountSaveData> counts;
    }

    [Serializable]
    public sealed class StringIntEntry
    {
        public string key;
        public int value;
    }
}
