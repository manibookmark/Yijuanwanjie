using System;

namespace WanjieScroll.Bootstrap
{
    public enum FloorCombatScenePhase
    {
        Empty,
        LoadingShell,
        ShellReady,
        PreparingEncounter,
        EncounterReady,
        Activated,
        CleaningEncounter,
        Failed,
        Releasing
    }

    /// <summary>
    /// Owns the generation and admission rules of one resident floor scene.
    /// Asset, scene and Run references remain exclusively with its runtime owner;
    /// late load/build/return completions cannot revive a released generation.
    /// No gameplay state, encounter result or presentation object is stored here.
    /// </summary>
    public sealed class FloorCombatSceneState
    {
        public int Generation { get; private set; }
        public FloorCombatScenePhase Phase { get; private set; }
        public string RunId { get; private set; }
        public string FloorContentId { get; private set; }
        public string NodeContentId { get; private set; }
        public string ScenarioId { get; private set; }
        public string Error { get; private set; }

        public bool IsBusy => Phase == FloorCombatScenePhase.LoadingShell ||
            Phase == FloorCombatScenePhase.PreparingEncounter ||
            Phase == FloorCombatScenePhase.CleaningEncounter ||
            Phase == FloorCombatScenePhase.Releasing;

        public int Begin(string runId, string floorContentId)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("A resident floor needs a Run ID.", nameof(runId));
            if (string.IsNullOrWhiteSpace(floorContentId))
                throw new ArgumentException("A resident floor needs a content ID.", nameof(floorContentId));
            if (Phase != FloorCombatScenePhase.Empty)
                throw new InvalidOperationException("Release the previous floor before preparing another shell.");
            Generation++;
            RunId = runId;
            FloorContentId = floorContentId;
            NodeContentId = ScenarioId = Error = null;
            Phase = FloorCombatScenePhase.LoadingShell;
            return Generation;
        }

        public bool IsCurrent(int generation) => generation == Generation &&
            Phase != FloorCombatScenePhase.Empty && Phase != FloorCombatScenePhase.Releasing;

        public bool MatchesScope(string runId, string floorContentId) =>
            Phase != FloorCombatScenePhase.Empty && Phase != FloorCombatScenePhase.Releasing &&
            string.Equals(RunId, runId, StringComparison.Ordinal) &&
            string.Equals(FloorContentId, floorContentId, StringComparison.Ordinal);

        public bool MarkShellReady(int generation)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.LoadingShell) return false;
            Phase = FloorCombatScenePhase.ShellReady;
            return true;
        }

        public bool BeginEncounter(int generation, string nodeContentId, string scenarioId)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.ShellReady ||
                string.IsNullOrWhiteSpace(nodeContentId) || string.IsNullOrWhiteSpace(scenarioId))
                return false;
            NodeContentId = nodeContentId;
            ScenarioId = scenarioId;
            Phase = FloorCombatScenePhase.PreparingEncounter;
            return true;
        }

        public bool MatchesEncounter(string nodeContentId, string scenarioId) =>
            string.Equals(NodeContentId, nodeContentId, StringComparison.Ordinal) &&
            string.Equals(ScenarioId, scenarioId, StringComparison.Ordinal);

        public bool MarkEncounterReady(int generation)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.PreparingEncounter) return false;
            Phase = FloorCombatScenePhase.EncounterReady;
            return true;
        }

        public bool Activate(int generation, string nodeContentId, string scenarioId)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.EncounterReady ||
                !MatchesEncounter(nodeContentId, scenarioId)) return false;
            Phase = FloorCombatScenePhase.Activated;
            return true;
        }

        public bool BeginReturn(int generation)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.Activated) return false;
            NodeContentId = ScenarioId = null;
            Phase = FloorCombatScenePhase.CleaningEncounter;
            return true;
        }

        public bool CompleteReturn(int generation)
        {
            if (!IsCurrent(generation) || Phase != FloorCombatScenePhase.CleaningEncounter) return false;
            Error = null;
            Phase = FloorCombatScenePhase.ShellReady;
            return true;
        }

        public bool DiscardEncounter(int generation)
        {
            if (!IsCurrent(generation) || string.IsNullOrWhiteSpace(NodeContentId) ||
                (Phase != FloorCombatScenePhase.PreparingEncounter &&
                 Phase != FloorCombatScenePhase.EncounterReady &&
                 Phase != FloorCombatScenePhase.Failed)) return false;
            NodeContentId = ScenarioId = null;
            Phase = FloorCombatScenePhase.CleaningEncounter;
            return true;
        }

        public bool Fail(int generation, string error)
        {
            if (!IsCurrent(generation)) return false;
            Error = string.IsNullOrWhiteSpace(error) ? "Resident battle preparation failed." : error;
            Phase = FloorCombatScenePhase.Failed;
            return true;
        }

        public int Release()
        {
            if (Phase == FloorCombatScenePhase.Empty || Phase == FloorCombatScenePhase.Releasing)
                return Generation;
            Generation++;
            NodeContentId = ScenarioId = null;
            Phase = FloorCombatScenePhase.Releasing;
            return Generation;
        }

        public bool CompleteRelease(int generation)
        {
            if (generation != Generation || Phase != FloorCombatScenePhase.Releasing) return false;
            RunId = FloorContentId = NodeContentId = ScenarioId = Error = null;
            Phase = FloorCombatScenePhase.Empty;
            return true;
        }
    }
}
