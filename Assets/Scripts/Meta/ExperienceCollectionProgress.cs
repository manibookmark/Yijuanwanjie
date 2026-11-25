using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// Sole owner of account XP collections and accepted cumulative Run
    /// watermarks. Ordinary strict saves credit real new facts; a conclusion
    /// uses the same ledger and cannot credit them again. These account saves
    /// are a project persistence boundary, not a claim of VC per-hit disk writes.
    /// A callback holds this owner against mutation and failures restore both
    /// the totals and every watermark before another attempt can begin.
    /// </summary>
    public sealed class ExperienceCollectionProgress
    {
        public const int CurrentVersion = 1;
        private ExperienceCollectionTotals _lifetime;
        private Dictionary<string, ExperienceCollectionTotals> _acceptedRuns =
            new Dictionary<string, ExperienceCollectionTotals>(StringComparer.Ordinal);
        public ExperienceCollectionTotals LifetimeTotals => _lifetime;
        public bool IsCommitting { get; private set; }

        public ExperienceCollectionTotals GetAcceptedRunTotals(string runId)
        {
            RequireId(runId);
            return _acceptedRuns.TryGetValue(runId, out ExperienceCollectionTotals totals)
                ? totals : ExperienceCollectionTotals.Zero;
        }

        /// <summary>Reads the potential new account total without accepting or persisting anything.</summary>
        public ExperienceCollectionTotals ProjectRun(string runId, ExperienceCollectionTotals cumulativeTotals)
            => _lifetime.Add(cumulativeTotals.Subtract(GetAcceptedRunTotals(runId)));

        public ExperienceCollectionProgressSnapshot Capture()
        {
            var runs = new List<ExperienceCollectionRunCheckpoint>();
            foreach (var item in _acceptedRuns)
                runs.Add(new ExperienceCollectionRunCheckpoint(item.Key, item.Value));
            return new ExperienceCollectionProgressSnapshot(_lifetime, runs);
        }

        public void Restore(ExperienceCollectionProgressSnapshot snapshot)
        {
            if (IsCommitting) throw new InvalidOperationException("Experience collections are already committing.");
            Adopt(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
        }

        public bool TryCommitRunFacts(string runId, ExperienceCollectionTotals cumulativeTotals,
            Action persist, out string error)
            => TryCommit(runId, cumulativeTotals, persist, out error);

        public bool TryCommitRunConclusion(string runId, ExperienceCollectionTotals cumulativeTotals,
            EventUnlockRunConclusion conclusion, Action persist, out string error)
        {
            if (conclusion != EventUnlockRunConclusion.Victory && conclusion != EventUnlockRunConclusion.Defeat)
            { error = "Experience conclusion requires a real whole-Run victory or defeat."; return false; }
            return TryCommit(runId, cumulativeTotals, persist, out error);
        }

        private bool TryCommit(string runId, ExperienceCollectionTotals cumulativeTotals, Action persist, out string error)
        {
            error = null;
            if (IsCommitting) { error = "Experience collections are already committing."; return false; }
            if (persist == null) { error = "Experience collections require strict persistence."; return false; }
            ExperienceCollectionProgressSnapshot before = Capture();
            IsCommitting = true;
            try
            {
                ExperienceCollectionTotals projected = ProjectRun(runId, cumulativeTotals);
                var runs = new Dictionary<string, ExperienceCollectionTotals>(_acceptedRuns, StringComparer.Ordinal)
                { [runId] = cumulativeTotals };
                _lifetime = projected;
                _acceptedRuns = runs;
                // Repeat acceptance still persists the same account checkpoint;
                // it never silently turns a strict save into an in-memory replay.
                persist();
                return true;
            }
            catch (Exception exception)
            {
                Adopt(before);
                error = "Experience collection persistence failed: " + exception.Message;
                return false;
            }
            finally { IsCommitting = false; }
        }

        private void Adopt(ExperienceCollectionProgressSnapshot snapshot)
        {
            var validated = new ExperienceCollectionProgressSnapshot(snapshot.LifetimeTotals, snapshot.AcceptedRuns);
            var runs = new Dictionary<string, ExperienceCollectionTotals>(StringComparer.Ordinal);
            foreach (ExperienceCollectionRunCheckpoint item in validated.AcceptedRuns) runs.Add(item.RunId, item.Totals);
            _lifetime = validated.LifetimeTotals;
            _acceptedRuns = runs;
        }

        public ExperienceCollectionProgressSaveData CaptureSaveData()
        {
            ExperienceCollectionProgressSnapshot snapshot = Capture();
            var data = new ExperienceCollectionProgressSaveData
            {
                version = CurrentVersion, playerXPGathered = snapshot.LifetimeTotals.PlayerXPGathered,
                pickupCrystal = snapshot.LifetimeTotals.PickupCrystal,
                acceptedRuns = new List<ExperienceCollectionRunSaveData>()
            };
            foreach (ExperienceCollectionRunCheckpoint run in snapshot.AcceptedRuns)
                data.acceptedRuns.Add(new ExperienceCollectionRunSaveData
                {
                    runId = run.RunId, playerXPGathered = run.Totals.PlayerXPGathered,
                    pickupCrystal = run.Totals.PickupCrystal
                });
            return data;
        }

        /// <summary>Detached DTO restoration is strict; a missing legacy ledger is handled by the enclosing schema.</summary>
        public void RestoreSaveData(ExperienceCollectionProgressSaveData data)
            => Restore(ToSnapshot(data));

        private static ExperienceCollectionProgressSnapshot ToSnapshot(ExperienceCollectionProgressSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.version != CurrentVersion || data.acceptedRuns == null)
                throw new InvalidOperationException("Account experience requires its complete supported checkpoint.");
            var runs = new List<ExperienceCollectionRunCheckpoint>();
            foreach (ExperienceCollectionRunSaveData run in data.acceptedRuns)
            {
                if (run == null) throw new InvalidOperationException("Accepted experience Run cannot be null.");
                runs.Add(new ExperienceCollectionRunCheckpoint(run.runId,
                    new ExperienceCollectionTotals(run.playerXPGathered, run.pickupCrystal)));
            }
            return new ExperienceCollectionProgressSnapshot(
                new ExperienceCollectionTotals(data.playerXPGathered, data.pickupCrystal), runs);
        }

        public string Serialize()
        {
            ExperienceCollectionProgressSaveData data = CaptureSaveData();
            var text = new StringBuilder("{\"version\":1,\"playerXPGathered\":");
            text.Append(data.playerXPGathered.ToString(CultureInfo.InvariantCulture));
            text.Append(",\"pickupCrystal\":").Append(data.pickupCrystal.ToString(CultureInfo.InvariantCulture));
            text.Append(",\"acceptedRuns\":[");
            for (int index = 0; index < data.acceptedRuns.Count; index++)
            {
                if (index != 0) text.Append(',');
                ExperienceCollectionRunSaveData run = data.acceptedRuns[index];
                text.Append("{\"runId\":\"").Append(run.runId)
                    .Append("\",\"playerXPGathered\":").Append(run.playerXPGathered.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"pickupCrystal\":").Append(run.pickupCrystal.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            return text.Append("]}").ToString();
        }

        public static ExperienceCollectionProgressSaveData ReadSaveData(string rawJson)
        {
            Dictionary<string, string> fields = ReadFields(rawJson, "version", "playerXPGathered", "pickupCrystal", "acceptedRuns");
            long version = ReadCount(fields["version"]);
            if (version != CurrentVersion) throw new InvalidOperationException("Unsupported account experience version.");
            var data = new ExperienceCollectionProgressSaveData
            {
                version = (int)version, playerXPGathered = ReadCount(fields["playerXPGathered"]),
                pickupCrystal = ReadCount(fields["pickupCrystal"]), acceptedRuns = new List<ExperienceCollectionRunSaveData>()
            };
            if (!JsonObjectShape.TryGetArrayItems(fields["acceptedRuns"], out IReadOnlyList<string> items))
                throw new InvalidOperationException("Accepted experience Runs must be an explicit array.");
            foreach (string item in items)
            {
                Dictionary<string, string> run = ReadFields(item, "runId", "playerXPGathered", "pickupCrystal");
                data.acceptedRuns.Add(new ExperienceCollectionRunSaveData
                {
                    runId = ReadId(run["runId"]), playerXPGathered = ReadCount(run["playerXPGathered"]),
                    pickupCrystal = ReadCount(run["pickupCrystal"])
                });
            }
            ToSnapshot(data);
            return data;
        }

        internal static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Experience acceptance requires a stable Run ID.");
            foreach (char value in id)
                if (char.IsWhiteSpace(value) || char.IsControl(value) || value == '"' || value == '\\')
                    throw new ArgumentException("Experience Run IDs cannot contain whitespace, controls or JSON escapes.");
            try { new UTF8Encoding(false, true).GetByteCount(id); }
            catch (EncoderFallbackException exception) { throw new ArgumentException("Invalid Unicode Run ID.", exception); }
        }

        private static string ReadId(string raw)
        {
            if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"')
                throw new InvalidOperationException("Accepted experience Run ID must be a JSON string.");
            string id = raw.Substring(1, raw.Length - 2);
            RequireId(id);
            return id;
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

    public sealed class ExperienceCollectionRunCheckpoint
    {
        public string RunId { get; }
        public ExperienceCollectionTotals Totals { get; }
        public ExperienceCollectionRunCheckpoint(string runId, ExperienceCollectionTotals totals)
        { ExperienceCollectionProgress.RequireId(runId); RunId = runId; Totals = totals; }
    }

    /// <summary>Immutable account rollback boundary; all watermarks are detached and ordered by stable Run ID.</summary>
    public sealed class ExperienceCollectionProgressSnapshot
    {
        public ExperienceCollectionTotals LifetimeTotals { get; }
        public IReadOnlyList<ExperienceCollectionRunCheckpoint> AcceptedRuns { get; }
        public ExperienceCollectionProgressSnapshot(ExperienceCollectionTotals lifetimeTotals,
            IEnumerable<ExperienceCollectionRunCheckpoint> acceptedRuns = null)
        {
            var runs = new List<ExperienceCollectionRunCheckpoint>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var total = ExperienceCollectionTotals.Zero;
            foreach (ExperienceCollectionRunCheckpoint source in acceptedRuns ?? Array.Empty<ExperienceCollectionRunCheckpoint>())
            {
                if (source == null || !ids.Add(source.RunId))
                    throw new InvalidOperationException("Accepted experience Runs must be non-null and unique.");
                var run = new ExperienceCollectionRunCheckpoint(source.RunId, source.Totals);
                total = total.Add(run.Totals);
                runs.Add(run);
            }
            if (total != lifetimeTotals)
                throw new InvalidOperationException("Account experience must equal its accepted Run watermarks.");
            runs.Sort((left, right) => StringComparer.Ordinal.Compare(left.RunId, right.RunId));
            LifetimeTotals = lifetimeTotals;
            AcceptedRuns = new ReadOnlyCollection<ExperienceCollectionRunCheckpoint>(runs);
        }
    }

    [Serializable]
    public sealed class ExperienceCollectionProgressSaveData
    {
        public int version;
        public long playerXPGathered;
        public long pickupCrystal;
        public List<ExperienceCollectionRunSaveData> acceptedRuns;
    }

    [Serializable]
    public sealed class ExperienceCollectionRunSaveData
    {
        public string runId;
        public long playerXPGathered;
        public long pickupCrystal;
    }
}
