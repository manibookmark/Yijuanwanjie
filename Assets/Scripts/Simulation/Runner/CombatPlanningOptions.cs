using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// An isolated unstarted encounter and the owner of all its transient
    /// assets. The factory must reconstruct the entry, including independent
    /// Run progression and instance IDs; it must not reuse the actual Run.
    /// </summary>
    public sealed class CombatPlanningBranch : IDisposable
    {
        public CombatPlanningBranch(CombatController combat, IDisposable owner)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }
        private readonly IDisposable _owner;
        public CombatController Combat { get; }
        public void Dispose() => _owner.Dispose();
    }

    /// <summary>Reproducible search budget and evidence for one encounter.</summary>
    public sealed class CombatPlanningOptions
    {
        public CombatPlanningOptions(Func<IRandomSource, CombatPlanningBranch> branchFactory,
            int seed, int maxDepth = 4, int beamWidth = 6, int futureSamples = 3,
            int maxExpansions = 96)
        {
            BranchFactory = branchFactory ?? throw new ArgumentNullException(nameof(branchFactory));
            if (maxDepth < 1 || beamWidth < 1 || futureSamples < 1 || maxExpansions < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDepth), "All search budgets must be positive.");
            Seed = seed;
            MaxDepth = maxDepth;
            BeamWidth = beamWidth;
            FutureSamples = futureSamples;
            MaxExpansions = maxExpansions;
        }
        internal Func<IRandomSource, CombatPlanningBranch> BranchFactory { get; }
        public int Seed { get; }
        public int MaxDepth { get; }
        public int BeamWidth { get; }
        public int FutureSamples { get; }
        public int MaxExpansions { get; }
        public string EntryIdentity { get; set; } = string.Empty;
        public CombatPlanningStatistics Statistics { get; } = new CombatPlanningStatistics();
        public CombatPlanningTrace Trace { get; internal set; }
    }

    [Serializable]
    public sealed class CombatPlanningStatistics
    {
        public int Decisions, ExpandedNodes, BudgetExhaustions, HiddenBoundaryStops, TurnBoundaryStops, EnemyBoundaryStops;
        public long ReplayedCommands, ElapsedMilliseconds;
        public int MaximumDepth, CrawlerTriggers;
    }

    public sealed class CombatPlanningTrace
    {
        internal CombatPlanningTrace(string identity, ReplayTrace actions,
            IReadOnlyList<CombatRandomDecision> random)
        {
            EntryIdentity = identity;
            Actions = actions;
            RandomHistory = random;
        }
        public string EntryIdentity { get; }
        public string Strategy => PlanningCombatAgent.Id;
        public int StrategyRevision => PlanningCombatAgent.Revision;
        public ReplayTrace Actions { get; }
        public IReadOnlyList<CombatRandomDecision> RandomHistory { get; }
    }
}
