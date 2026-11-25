using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Stage
{
    public enum StageSimulationStepKind
    {
        Combat,
        Interaction,
        Exit
    }

    public enum StageSimulationSealDisposition
    {
        None,
        Assign,
        CashOut
    }

    /// <summary>
    /// Explicit player decision for a ConvertCard interaction. The production
    /// effect owns conversion rules and jade value; the route owns only which
    /// supported disposition was selected.
    /// </summary>
    public enum StageSimulationCardConversionDisposition
    {
        None,
        Convert,
        CashOut
    }

    /// <summary>
    /// Immutable intent for one production floor node. Stable card occurrence
    /// is used only by headless callers; Dungeon remains the owner of runtime
    /// card identity and all committed Run state.
    /// </summary>
    public sealed class StageSimulationStep
    {
        public StageSimulationStep(
            StageSimulationStepKind kind,
            string nodeContentId,
            string targetCardContentId = null,
            int targetCardOccurrence = 0,
            StageSimulationSealDisposition victorySealDisposition =
                StageSimulationSealDisposition.None,
            StageSimulationCardConversionDisposition
                cardConversionDisposition =
                    StageSimulationCardConversionDisposition.None,
            string victorySealContentId = null)
        {
            if (!Enum.IsDefined(typeof(StageSimulationStepKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(nodeContentId))
            {
                throw new ArgumentException(
                    "nodeContentId cannot be empty.",
                    nameof(nodeContentId));
            }
            if (targetCardOccurrence < 0)
                throw new ArgumentOutOfRangeException(nameof(targetCardOccurrence));
            if (string.IsNullOrWhiteSpace(targetCardContentId) &&
                targetCardOccurrence != 0)
            {
                throw new ArgumentException(
                    "A card occurrence requires targetCardContentId.",
                    nameof(targetCardOccurrence));
            }
            if (!Enum.IsDefined(
                    typeof(StageSimulationSealDisposition),
                    victorySealDisposition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(victorySealDisposition));
            }
            if (!Enum.IsDefined(
                    typeof(StageSimulationCardConversionDisposition),
                    cardConversionDisposition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cardConversionDisposition));
            }
            if (victorySealDisposition ==
                    StageSimulationSealDisposition.Assign &&
                string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "Assign requires targetCardContentId.",
                    nameof(targetCardContentId));
            }
            if (victorySealDisposition ==
                    StageSimulationSealDisposition.CashOut &&
                !string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "CashOut cannot also declare a card target.",
                    nameof(targetCardContentId));
            }
            if (kind != StageSimulationStepKind.Combat &&
                victorySealDisposition !=
                    StageSimulationSealDisposition.None)
            {
                throw new ArgumentException(
                    "Victory seal disposition is valid only for Combat steps.",
                    nameof(victorySealDisposition));
            }
            if (!string.IsNullOrWhiteSpace(victorySealContentId) &&
                (kind != StageSimulationStepKind.Combat ||
                 victorySealDisposition == StageSimulationSealDisposition.None))
            {
                throw new ArgumentException(
                    "A victory seal identity requires a Combat Assign or CashOut decision.",
                    nameof(victorySealContentId));
            }
            if (kind == StageSimulationStepKind.Combat &&
                victorySealDisposition !=
                    StageSimulationSealDisposition.Assign &&
                !string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "A Combat card target is valid only for Assign.",
                    nameof(targetCardContentId));
            }
            if (kind == StageSimulationStepKind.Exit &&
                !string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "Exit cannot declare a card target.",
                    nameof(targetCardContentId));
            }
            if (kind != StageSimulationStepKind.Interaction &&
                cardConversionDisposition !=
                    StageSimulationCardConversionDisposition.None)
            {
                throw new ArgumentException(
                    "Card conversion disposition is valid only for " +
                    "Interaction steps.",
                    nameof(cardConversionDisposition));
            }
            if (cardConversionDisposition ==
                    StageSimulationCardConversionDisposition.Convert &&
                string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "Convert requires targetCardContentId.",
                    nameof(targetCardContentId));
            }
            if (cardConversionDisposition ==
                    StageSimulationCardConversionDisposition.CashOut &&
                !string.IsNullOrWhiteSpace(targetCardContentId))
            {
                throw new ArgumentException(
                    "Card conversion CashOut cannot also declare a card target.",
                    nameof(targetCardContentId));
            }

            Kind = kind;
            NodeContentId = nodeContentId;
            TargetCardContentId = targetCardContentId ?? string.Empty;
            TargetCardOccurrence = targetCardOccurrence;
            VictorySealDisposition = victorySealDisposition;
            VictorySealContentId = victorySealContentId ?? string.Empty;
            CardConversionDisposition = cardConversionDisposition;
        }

        public StageSimulationStepKind Kind { get; }
        public string NodeContentId { get; }
        public string TargetCardContentId { get; }
        public int TargetCardOccurrence { get; }
        public StageSimulationSealDisposition VictorySealDisposition { get; }
        /// <summary>Explicit offered seal for a multi-seal reward; legacy single-seal steps may omit it.</summary>
        public string VictorySealContentId { get; }
        public StageSimulationCardConversionDisposition
            CardConversionDisposition { get; }
    }

    /// <summary>
    /// A versioned route through one production floor. The legacy constructor
    /// preserves encounter-only callers by compiling every node into a Combat
    /// step; typed callers can also model interactions and an explicit Exit.
    /// </summary>
    public sealed class StageSimulationRoute
    {
        private readonly IReadOnlyList<StageSimulationStep> _steps;
        private readonly IReadOnlyList<string> _encounterNodeContentIds;

        public StageSimulationRoute(
            string routeId,
            int revision,
            IReadOnlyList<string> encounterNodeContentIds)
            : this(
                routeId,
                revision,
                CompileLegacySteps(encounterNodeContentIds))
        {
        }

        public StageSimulationRoute(
            string routeId,
            int revision,
            IReadOnlyList<StageSimulationStep> steps)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("routeId cannot be empty.", nameof(routeId));
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (steps == null || steps.Count == 0)
            {
                throw new ArgumentException(
                    "A stage route must contain at least one step.",
                    nameof(steps));
            }

            RouteId = routeId;
            Revision = revision;
            var copiedSteps = new StageSimulationStep[steps.Count];
            var encounterNodeIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < steps.Count; i++)
            {
                StageSimulationStep step = steps[i] ??
                    throw new ArgumentException($"Route step {i} is null.");
                string nodeId = step.NodeContentId;
                if (!seen.Add(nodeId))
                    throw new ArgumentException($"Route repeats node {nodeId}.");
                copiedSteps[i] = step;
                if (step.Kind == StageSimulationStepKind.Combat)
                    encounterNodeIds.Add(nodeId);
            }
            if (encounterNodeIds.Count == 0)
            {
                throw new ArgumentException(
                    "A stage route must contain at least one Combat step.",
                    nameof(steps));
            }
            _steps = Array.AsReadOnly(copiedSteps);
            _encounterNodeContentIds = encounterNodeIds.AsReadOnly();
        }

        public string RouteId { get; }
        public int Revision { get; }
        public IReadOnlyList<StageSimulationStep> Steps => _steps;
        public IReadOnlyList<string> EncounterNodeContentIds =>
            _encounterNodeContentIds;

        private static IReadOnlyList<StageSimulationStep> CompileLegacySteps(
            IReadOnlyList<string> encounterNodeContentIds)
        {
            if (encounterNodeContentIds == null)
                return null;
            var steps = new StageSimulationStep[encounterNodeContentIds.Count];
            for (int i = 0; i < encounterNodeContentIds.Count; i++)
            {
                steps[i] = new StageSimulationStep(
                    StageSimulationStepKind.Combat,
                    encounterNodeContentIds[i]);
            }
            return Array.AsReadOnly(steps);
        }
    }

    public enum StageSimulationTermination
    {
        RouteCompleted,
        CombatDefeat,
        SimulationLimit
    }

    public readonly struct StageCardSnapshot
    {
        public StageCardSnapshot(int runtimeInstanceId, string contentId)
        {
            RuntimeInstanceId = runtimeInstanceId;
            ContentId = contentId ?? string.Empty;
        }

        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
    }

    /// <summary>Immutable per-encounter evidence copied out of mutable combat state.</summary>
    public sealed class StageEncounterResult
    {
        public StageEncounterResult(
            string nodeContentId,
            string scenarioId,
            int enteringHealth,
            int enteringLevel,
            int enteringExperience,
            IReadOnlyList<StageCardSnapshot> enteringDeck,
            EncounterMetrics metrics,
            int endingLevel,
            int endingExperience,
            IReadOnlyList<StageCardSnapshot> endingDeck)
        {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));
            NodeContentId = nodeContentId;
            ScenarioId = scenarioId;
            EnteringHealth = enteringHealth;
            EnteringLevel = enteringLevel;
            EnteringExperience = enteringExperience;
            EnteringDeck = Freeze(enteringDeck);
            Win = metrics.Win;
            Termination = metrics.Termination;
            Turns = metrics.Turns;
            EndingHealth = metrics.EndingHP;
            HpLost = metrics.HpLost;
            DamageTaken = metrics.DamageTaken;
            ArmorGenerated = metrics.ArmorGenerated;
            CardsPlayed = metrics.CardsPlayed;
            CardsDrawn = metrics.CardsDrawn;
            EnemyActions = metrics.EnemyActions;
            ManaSpent = metrics.ManaSpent;
            MaxCombo = metrics.MaxCombo;
            TotalDamage = metrics.TotalDamage;
            TelemetryRevision = metrics.TelemetryRevision;
            AppliedDamage = metrics.AppliedDamage;
            CritDamage = metrics.CritDamage;
            DoTDamage = metrics.DoTDamage;
            OverkillDamage = metrics.OverkillDamage;
            ComboBreakCount = metrics.ComboBreakCount;
            KillsByDirect = metrics.KillsByDirect;
            KillsByArea = metrics.KillsByArea;
            KillsByDoT = metrics.KillsByDoT;
            ArmorAbsorbed = metrics.ArmorAbsorbed;
            HealingDone = metrics.HealingDone;
            KilledEnemies = metrics.KilledEnemies;
            EscapedEnemies = metrics.EscapedEnemies;
            EnemyExperienceEarned = metrics.EnemyExperienceEarned;
            RewardSelections = Freeze(metrics.RewardSelections);
            CardUsage = Freeze(metrics.CardUsage);
            EnemyUsage = Freeze(metrics.EnemyUsage);
            EndingLevel = endingLevel;
            EndingExperience = endingExperience;
            EndingDeck = Freeze(endingDeck);
        }

        public string NodeContentId { get; }
        public string ScenarioId { get; }
        public int EnteringHealth { get; }
        public int EnteringLevel { get; }
        public int EnteringExperience { get; }
        public IReadOnlyList<StageCardSnapshot> EnteringDeck { get; }
        public bool Win { get; }
        public SimulationTerminationReason Termination { get; }
        public int Turns { get; }
        public int EndingHealth { get; }
        public int HpLost { get; }
        public int DamageTaken { get; }
        public int ArmorGenerated { get; }
        public int CardsPlayed { get; }
        public int CardsDrawn { get; }
        public int EnemyActions { get; }
        public int ManaSpent { get; }
        public int MaxCombo { get; }
        public int TotalDamage { get; }
        public int TelemetryRevision { get; }
        public int AppliedDamage { get; }
        public int CritDamage { get; }
        public int DoTDamage { get; }
        public int OverkillDamage { get; }
        public int ComboBreakCount { get; }
        public int KillsByDirect { get; }
        public int KillsByArea { get; }
        public int KillsByDoT { get; }
        public int ArmorAbsorbed { get; }
        public int HealingDone { get; }
        public int KilledEnemies { get; }
        public int EscapedEnemies { get; }
        public int EnemyExperienceEarned { get; }
        public IReadOnlyList<RewardSelectionMetric> RewardSelections { get; }
        public IReadOnlyList<CardUsageMetric> CardUsage { get; }
        public IReadOnlyList<EnemyActionUsageMetric> EnemyUsage { get; }
        public int EndingLevel { get; }
        public int EndingExperience { get; }
        public IReadOnlyList<StageCardSnapshot> EndingDeck { get; }

        private static IReadOnlyList<StageCardSnapshot> Freeze(
            IReadOnlyList<StageCardSnapshot> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<StageCardSnapshot>();
            return new List<StageCardSnapshot>(source).AsReadOnly();
        }

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            return new List<T>(source).AsReadOnly();
        }
    }

    /// <summary>
    /// Immutable result of one floor route. It deliberately contains no
    /// CombatController, ScriptableObject, or mutable Dungeon session reference.
    /// </summary>
    public sealed class StageSimulationReport
    {
        internal StageSimulationReport(
            string floorContentId,
            string snapshotId,
            string routeId,
            int routeRevision,
            int masterSeed,
            string agentName,
            int agentRevision,
            string rewardPolicyId,
            int rewardPolicyRevision,
            string ruleInputId,
            int ruleInputRevision,
            string selectedHeroContentId,
            int startingHealth,
            IReadOnlyList<StageEncounterResult> encounters,
            StageSimulationTermination termination,
            string failedNodeContentId,
            int endingHealth,
            int endingLevel,
            int endingExperience,
            IReadOnlyList<StageCardSnapshot> finalDeck)
        {
            FloorContentId = floorContentId;
            SnapshotId = snapshotId;
            RouteId = routeId;
            RouteRevision = routeRevision;
            MasterSeed = masterSeed;
            AgentName = agentName;
            AgentRevision = agentRevision;
            RewardPolicyId = rewardPolicyId;
            RewardPolicyRevision = rewardPolicyRevision;
            RuleInputId = ruleInputId;
            RuleInputRevision = ruleInputRevision;
            SelectedHeroContentId = selectedHeroContentId;
            StartingHealth = startingHealth;
            Encounters = Freeze(encounters);
            Termination = termination;
            FailedNodeContentId = failedNodeContentId;
            EndingHealth = endingHealth;
            EndingLevel = endingLevel;
            EndingExperience = endingExperience;
            FinalDeck = Freeze(finalDeck);
        }

        public const int SchemaVersion = 2;
        public string FloorContentId { get; }
        public string SnapshotId { get; }
        public string RouteId { get; }
        public int RouteRevision { get; }
        public int MasterSeed { get; }
        public string AgentName { get; }
        public int AgentRevision { get; }
        public string RewardPolicyId { get; }
        public int RewardPolicyRevision { get; }
        public string RuleInputId { get; }
        public int RuleInputRevision { get; }
        public string SelectedHeroContentId { get; }
        public int StartingHealth { get; }
        public IReadOnlyList<StageEncounterResult> Encounters { get; }
        public StageSimulationTermination Termination { get; }
        public string FailedNodeContentId { get; }
        public int EndingHealth { get; }
        public int EndingLevel { get; }
        public int EndingExperience { get; }
        public IReadOnlyList<StageCardSnapshot> FinalDeck { get; }
        public bool RouteCompleted =>
            Termination == StageSimulationTermination.RouteCompleted;

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            return new List<T>(source).AsReadOnly();
        }
    }
}
