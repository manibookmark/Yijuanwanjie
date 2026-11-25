using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// One Run's actual XP collection ledger, attributed to stable project
    /// floors. The aggregate owns award/receipt deduplication and persistence;
    /// this owner atomically records checked deltas and owns its complete wire
    /// format. Source floors merged into one project floor remain one entry.
    /// Restoration replaces known facts and never infers missing history.
    /// </summary>
    public sealed class DungeonRunExperienceCollections
    {
        public const int CurrentVersion = 1;
        private Dictionary<string, ExperienceCollectionTotals> _floors =
            new Dictionary<string, ExperienceCollectionTotals>(StringComparer.Ordinal);
        public bool HistoryComplete { get; private set; } = true;

        /// <summary>Old saves have no all-source ledger; zero means unrecorded history, not zero past earnings.</summary>
        public static DungeonRunExperienceCollections EmptyLegacy()
            => new DungeonRunExperienceCollections { HistoryComplete = false };

        public ExperienceCollectionTotals RunTotals
        {
            get
            {
                var total = ExperienceCollectionTotals.Zero;
                foreach (ExperienceCollectionTotals value in _floors.Values) total = total.Add(value);
                return total;
            }
        }

        public IReadOnlyList<DungeonRunFloorExperienceCollection> Floors
        {
            get
            {
                var ids = new List<string>(_floors.Keys);
                ids.Sort(StringComparer.Ordinal);
                var copy = new List<DungeonRunFloorExperienceCollection>(ids.Count);
                foreach (string id in ids) copy.Add(new DungeonRunFloorExperienceCollection(id, _floors[id]));
                return new ReadOnlyCollection<DungeonRunFloorExperienceCollection>(copy);
            }
        }

        public ExperienceCollectionTotals GetFloorTotals(string floorContentId)
        {
            RequireId(floorContentId);
            return _floors.TryGetValue(floorContentId, out ExperienceCollectionTotals value)
                ? value : ExperienceCollectionTotals.Zero;
        }

        internal void Record(string floorContentId, ExperienceCollectionTotals delta)
        {
            RequireId(floorContentId);
            ExperienceCollectionTotals next = GetFloorTotals(floorContentId).Add(delta);
            // Check the complete Run before changing even one floor.
            RunTotals.Add(delta);
            if (delta == ExperienceCollectionTotals.Zero) return;
            _floors[floorContentId] = next;
        }

        internal DungeonRunExperienceCollections Clone()
            => Restore(CaptureSaveData());

        internal void Adopt(DungeonRunExperienceCollections other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var copy = Restore(other.CaptureSaveData());
            _floors = copy._floors;
            HistoryComplete = copy.HistoryComplete;
        }

        /// <summary>The caller supplies visited project-floor IDs; no production topology is inferred.</summary>
        internal void RequireVisitedFloors(IReadOnlyList<string> visitedFloorContentIds)
        {
            if (visitedFloorContentIds == null) throw new ArgumentNullException(nameof(visitedFloorContentIds));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in visitedFloorContentIds)
            {
                RequireId(id);
                if (!visited.Add(id)) throw new InvalidOperationException("Visited floor IDs must be unique.");
            }
            foreach (string id in _floors.Keys)
                if (!visited.Contains(id)) throw new InvalidOperationException("Experience belongs to an unvisited floor.");
        }

        public DungeonRunExperienceCollectionSaveData CaptureSaveData()
        {
            ExperienceCollectionTotals totals = RunTotals;
            var data = new DungeonRunExperienceCollectionSaveData
            {
                version = CurrentVersion, historyComplete = HistoryComplete, playerXPGathered = totals.PlayerXPGathered,
                pickupCrystal = totals.PickupCrystal, floors = new List<DungeonRunFloorExperienceCollectionSaveData>()
            };
            foreach (DungeonRunFloorExperienceCollection floor in Floors)
                data.floors.Add(new DungeonRunFloorExperienceCollectionSaveData
                {
                    floorContentId = floor.FloorContentId, playerXPGathered = floor.Totals.PlayerXPGathered,
                    pickupCrystal = floor.Totals.PickupCrystal
                });
            return data;
        }

        public static DungeonRunExperienceCollections Restore(DungeonRunExperienceCollectionSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.version != CurrentVersion || data.floors == null)
                throw new InvalidOperationException("Run experience requires its complete supported checkpoint.");
            var expected = new ExperienceCollectionTotals(data.playerXPGathered, data.pickupCrystal);
            var result = new DungeonRunExperienceCollections { HistoryComplete = data.historyComplete };
            var actual = ExperienceCollectionTotals.Zero;
            foreach (DungeonRunFloorExperienceCollectionSaveData floor in data.floors)
            {
                if (floor == null) throw new InvalidOperationException("Run experience floor cannot be null.");
                RequireId(floor.floorContentId);
                var totals = new ExperienceCollectionTotals(floor.playerXPGathered, floor.pickupCrystal);
                if (totals == ExperienceCollectionTotals.Zero || result._floors.ContainsKey(floor.floorContentId))
                    throw new InvalidOperationException("Run experience floors must be nonzero and unique.");
                actual = actual.Add(totals);
                result._floors.Add(floor.floorContentId, totals);
            }
            if (actual != expected) throw new InvalidOperationException("Run experience totals do not match its floors.");
            return result;
        }

        public string Serialize()
        {
            DungeonRunExperienceCollectionSaveData data = CaptureSaveData();
            var text = new StringBuilder("{\"version\":1,\"historyComplete\":");
            text.Append(data.historyComplete ? "true" : "false").Append(",\"playerXPGathered\":");
            text.Append(data.playerXPGathered.ToString(CultureInfo.InvariantCulture));
            text.Append(",\"pickupCrystal\":").Append(data.pickupCrystal.ToString(CultureInfo.InvariantCulture));
            text.Append(",\"floors\":[");
            for (int index = 0; index < data.floors.Count; index++)
            {
                if (index != 0) text.Append(',');
                DungeonRunFloorExperienceCollectionSaveData floor = data.floors[index];
                text.Append("{\"floorContentId\":\"").Append(floor.floorContentId)
                    .Append("\",\"playerXPGathered\":").Append(floor.playerXPGathered.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"pickupCrystal\":").Append(floor.pickupCrystal.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            return text.Append("]}").ToString();
        }

        /// <summary>Strict modern wire: required zero fields, closed objects and explicit arrays.</summary>
        public static DungeonRunExperienceCollectionSaveData ReadSaveData(string rawJson)
        {
            Dictionary<string, string> fields = ReadFields(rawJson, "version", "historyComplete", "playerXPGathered", "pickupCrystal", "floors");
            long version = ReadCount(fields["version"]);
            if (version != CurrentVersion) throw new InvalidOperationException("Unsupported Run experience version.");
            var data = new DungeonRunExperienceCollectionSaveData
            {
                version = (int)version, historyComplete = ReadBoolean(fields["historyComplete"]),
                playerXPGathered = ReadCount(fields["playerXPGathered"]),
                pickupCrystal = ReadCount(fields["pickupCrystal"]), floors = new List<DungeonRunFloorExperienceCollectionSaveData>()
            };
            if (!JsonObjectShape.TryGetArrayItems(fields["floors"], out IReadOnlyList<string> items))
                throw new InvalidOperationException("Run experience floors must be an explicit array.");
            foreach (string item in items)
            {
                Dictionary<string, string> floor = ReadFields(item, "floorContentId", "playerXPGathered", "pickupCrystal");
                data.floors.Add(new DungeonRunFloorExperienceCollectionSaveData
                {
                    floorContentId = ReadId(floor["floorContentId"]), playerXPGathered = ReadCount(floor["playerXPGathered"]),
                    pickupCrystal = ReadCount(floor["pickupCrystal"])
                });
            }
            Restore(data);
            return data;
        }

        private static string ReadId(string raw)
        {
            if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"')
                throw new InvalidOperationException("Experience floor ID must be a JSON string.");
            string id = raw.Substring(1, raw.Length - 2);
            RequireId(id);
            return id;
        }

        private static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Experience requires a stable floor ID.");
            foreach (char value in id)
                if (char.IsWhiteSpace(value) || char.IsControl(value) || value == '"' || value == '\\')
                    throw new ArgumentException("Experience floor IDs cannot contain whitespace, controls or JSON escapes.");
            try { new UTF8Encoding(false, true).GetByteCount(id); }
            catch (EncoderFallbackException exception) { throw new ArgumentException("Invalid Unicode floor ID.", exception); }
        }

        private static long ReadCount(string raw)
        {
            if (raw.Length == 0 || (raw.Length > 1 && raw[0] == '0'))
                throw new InvalidOperationException("Experience counts require nonnegative integer literals.");
            foreach (char value in raw)
                if (value < '0' || value > '9') throw new InvalidOperationException("Experience counts require nonnegative integer literals.");
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long result))
                throw new InvalidOperationException("Experience count exceeds Int64.");
            return result;
        }

        private static bool ReadBoolean(string raw)
        {
            if (raw == "true") return true;
            if (raw == "false") return false;
            throw new InvalidOperationException("Experience history completeness requires a boolean literal.");
        }

        // Exact schema traversal complements JsonObjectShape; missing/duplicate/escaped
        // names must never collapse to a zero-valued DTO during deserialization.
        private static Dictionary<string, string> ReadFields(string raw, params string[] names)
        {
            if (raw == null) throw new InvalidOperationException("Experience object is missing.");
            string json = raw.Trim(' ', '\t', '\r', '\n');
            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}')
                throw new InvalidOperationException("Experience checkpoint requires an object.");
            var expected = new HashSet<string>(names, StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = 1;
            while (index < json.Length - 1)
            {
                while (index < json.Length - 1 && IsJsonWhiteSpace(json[index])) index++;
                if (json[index] != '"') throw new InvalidOperationException("Experience checkpoint has invalid fields.");
                int start = ++index;
                while (index < json.Length - 1 && json[index] != '"')
                {
                    if (json[index] == '\\' || char.IsControl(json[index])) throw new InvalidOperationException("Escaped experience field names are invalid.");
                    index++;
                }
                if (index >= json.Length - 1) throw new InvalidOperationException("Experience field is unterminated.");
                string name = json.Substring(start, index++ - start);
                while (index < json.Length - 1 && IsJsonWhiteSpace(json[index])) index++;
                if (!expected.Contains(name) || values.ContainsKey(name) || index >= json.Length - 1 || json[index++] != ':' ||
                    !JsonObjectShape.TryGetField(json, name, out string value))
                    throw new InvalidOperationException("Unknown, duplicate or malformed experience field.");
                while (index < json.Length - 1 && IsJsonWhiteSpace(json[index])) index++;
                if (index + value.Length > json.Length - 1 || string.CompareOrdinal(json, index, value, 0, value.Length) != 0)
                    throw new InvalidOperationException("Malformed experience value.");
                index += value.Length;
                values.Add(name, value);
                while (index < json.Length - 1 && IsJsonWhiteSpace(json[index])) index++;
                if (index == json.Length - 1) break;
                if (json[index++] != ',') throw new InvalidOperationException("Experience fields need a comma.");
                int next = index;
                while (next < json.Length - 1 && IsJsonWhiteSpace(json[next])) next++;
                if (next == json.Length - 1) throw new InvalidOperationException("Experience fields cannot have a trailing comma.");
            }
            if (values.Count != expected.Count) throw new InvalidOperationException("Experience checkpoint is incomplete.");
            return values;
        }

        private static bool IsJsonWhiteSpace(char value)
            => value == ' ' || value == '\t' || value == '\r' || value == '\n';
    }

    public sealed class DungeonRunFloorExperienceCollection
    {
        public string FloorContentId { get; }
        public ExperienceCollectionTotals Totals { get; }
        internal DungeonRunFloorExperienceCollection(string floorContentId, ExperienceCollectionTotals totals)
        { FloorContentId = floorContentId; Totals = totals; }
    }

    [Serializable]
    public sealed class DungeonRunExperienceCollectionSaveData
    {
        public int version;
        public bool historyComplete;
        public long playerXPGathered;
        public long pickupCrystal;
        public List<DungeonRunFloorExperienceCollectionSaveData> floors;
    }

    [Serializable]
    public sealed class DungeonRunFloorExperienceCollectionSaveData
    {
        public string floorContentId;
        public long playerXPGathered;
        public long pickupCrystal;
    }
}
