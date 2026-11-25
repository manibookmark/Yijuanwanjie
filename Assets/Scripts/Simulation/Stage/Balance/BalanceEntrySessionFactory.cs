using WanjieScroll.Content;
using System;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Balance
{
    public enum BalanceEntryStateKind
    {
        FreshRun,
        Checkpoint
    }

    /// <summary>
    /// Versioned authority for the state that exists before the first simulated
    /// combat. Higher floors must use a serialized DungeonRun checkpoint so a
    /// scenario's default deck/HP cannot masquerade as production carry-over.
    /// </summary>
    public sealed class BalanceEntryState
    {
        internal BalanceEntryState(
            BalanceEntryStateKind kind,
            int revision,
            string checkpointFile,
            string checkpointSha256,
            string checkpointPayload)
        {
            Kind = kind;
            Revision = revision;
            CheckpointFile = checkpointFile ?? string.Empty;
            CheckpointSha256 = checkpointSha256 ?? string.Empty;
            CheckpointPayload = checkpointPayload;
        }

        public BalanceEntryStateKind Kind { get; }
        public int Revision { get; }
        public string CheckpointFile { get; }
        public string CheckpointSha256 { get; }
        internal string CheckpointPayload { get; }
    }

    internal static class BalanceEntrySessionFactory
    {
        public static DungeonRunSession Create(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            string runId,
            IInstanceIdGenerator instanceIds,
            int? runSeed = null,
            Func<string, LevelRunProfile> resolveFloor = null)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            if (instanceIds == null)
                throw new ArgumentNullException(nameof(instanceIds));

            if (suite.EntryState.Kind == BalanceEntryStateKind.FreshRun)
            {
                return DungeonRunSession.Begin(
                    floor,
                    suite.SnapshotId,
                    runId,
                    instanceIds,
                    runSeed, commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
            }

            if (!DungeonRunSession.TryRestore(
                    suite.EntryState.CheckpointPayload,
                    resolveFloor ?? LevelRunProfileCatalog.Get,
                    instanceIds,
                    runId, // Restore retains the saved exploration seed, not this sample's seed.
                    out DungeonRunSession restored,
                    out string error,
                    commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id)))
            {
                throw new BalanceSuiteException(
                    "Cannot restore entry checkpoint " +
                    suite.EntryState.CheckpointFile + ": " + error);
            }
            if (!string.Equals(
                    restored.CurrentFloorContentId,
                    floor.contentId,
                    StringComparison.Ordinal))
            {
                throw new BalanceSuiteException(
                    "Entry checkpoint current floor " +
                    restored.CurrentFloorContentId + " does not match suite floor " +
                    floor.contentId + ".");
            }
            if (!string.Equals(
                    restored.CurrentFloor.SnapshotId,
                    suite.SnapshotId,
                    StringComparison.Ordinal))
            {
                throw new BalanceSuiteException(
                    "Entry checkpoint snapshot " +
                    restored.CurrentFloor.SnapshotId +
                    " does not match suite snapshotId " +
                    suite.SnapshotId + ".");
            }
            RequireQuiescent(restored, suite.EntryState.CheckpointFile);
            RequireProductionCarryOver(
                restored,
                floor,
                suite.EntryState.CheckpointFile,
                resolveFloor);
            return restored;
        }

        internal static void RequireQuiescent(
            DungeonRunSession session,
            string checkpointFile)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            string state = null;
            LevelRunSession floor = session.CurrentFloor;
            if (floor.ActiveEncounter.HasValue)
                state = "active encounter " +
                    floor.ActiveEncounter.Value.NodeContentId;
            else if (!string.IsNullOrWhiteSpace(floor.ActiveInteractionNodeId))
                state = "active interaction " + floor.ActiveInteractionNodeId;
            else if (floor.TryGetPendingReturn(out var pendingReturn))
                state = "pending return " + pendingReturn.NodeContentId;

            if (state == null) return;
            throw new BalanceSuiteException(
                "Entry checkpoint must be a quiescent map state, but " +
                (checkpointFile ?? string.Empty) + " contains " + state + ".");
        }

        internal static void RequireProductionCarryOver(
            DungeonRunSession session,
            LevelRunProfile floor,
            string checkpointFile,
            Func<string, LevelRunProfile> resolveFloor = null)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (floor == null) throw new ArgumentNullException(nameof(floor));

            string source = checkpointFile ?? string.Empty;
            if (!session.HasPlayerState ||
                session.CurrentHealth <= 0 ||
                session.RunCardCount <= 0)
            {
                throw new BalanceSuiteException(
                    "Entry checkpoint " + source + " has no living, seeded " +
                    "production player/deck carry-over. Scenario defaults are " +
                    "not a valid checkpoint entry.");
            }

            CombatProgressionState progression = session.CombatProgression;
            int requiredExperience = progression.GetRequiredExperience(
                progression.CurrentLevel);
            if (progression.CurrentExperience >= requiredExperience)
            {
                throw new BalanceSuiteException(
                    "Entry checkpoint " + source + " contains unsettled " +
                    "level progression: level=" + progression.CurrentLevel +
                    ", experience=" + progression.CurrentExperience +
                    ", required=" + requiredExperience + ". Regenerate the " +
                    "checkpoint so every earned level-up reward is settled.");
            }

            if (floor.floorIndex <= 1) return;
            string cursor = session.FirstFloorContentId;
            var visited = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            bool advancedFromEarlierFloor = false;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor))
            {
                if (string.Equals(
                        cursor,
                        session.CurrentFloorContentId,
                        StringComparison.Ordinal))
                {
                    if (advancedFromEarlierFloor) return;
                    break;
                }
                LevelRunProfile profile = (resolveFloor ?? LevelRunProfileCatalog.Get)(cursor);
                if (profile == null) break;
                cursor = profile.nextFloorContentId;
                advancedFromEarlierFloor = true;
            }

            throw new BalanceSuiteException(
                "Entry checkpoint " + source + " does not prove a production " +
                "floor progression chain from " + session.FirstFloorContentId +
                " to " + session.CurrentFloorContentId + ".");
        }
    }
}
