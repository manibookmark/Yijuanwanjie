using System;
using System.Collections.Generic;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Protocol
{
    /// <summary>
    /// Owns the actual command/random history and isolated reconstruction. Only
    /// player-visible observations and trial summaries reach the planner.
    /// Trials finish the known effects of the current card before a reveal
    /// becomes a leaf. EndTurn always uses Core on the same known-actor
    /// hypothesis and then stops; real hidden actors never participate.
    /// </summary>
    public sealed class CombatPlanningSession : IDisposable
    {
        private readonly CombatController _actual;
        private readonly CombatPlanningOptions _options;
        private readonly HeadlessCombatSession _session;
        private readonly CombatRandomHistory _random;
        private readonly List<LegalAction> _actions = new List<LegalAction>();
        private readonly List<int> _randomBoundaries = new List<int>();
        private readonly List<string> _stateBoundaries = new List<string>();
        private int _entryExperience;

        public CombatPlanningSession(CombatController actual, CombatPlanningOptions options)
        {
            _actual = actual ?? throw new ArgumentNullException(nameof(actual));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (actual.State.CurrentTurn != 0)
                throw new ArgumentException("Planning must attach before the first turn.", nameof(actual));
            _random = new CombatRandomHistory(actual.State.Random ??
                throw new ArgumentException("Actual combat requires an explicit game random source."));
            actual.SetRandomSource(_random);
            _session = new HeadlessCombatSession(actual);
        }

        public void Start(int entryExperience = 0)
        {
            if (_stateBoundaries.Count != 0) throw new InvalidOperationException("Already started.");
            _entryExperience = entryExperience;
            _actual.StartTurn();
            if (entryExperience > 0) _actual.GainXP(entryExperience);
            RecordBoundary();
        }

        public BattleObservation Observe() => _session.Observe();
        public IReadOnlyList<LegalAction> GetLegalActions() => _session.GetLegalActions();

        public StepResult Step(LegalAction action, string explanation = null)
        {
            StepResult result = _session.Step(action, explanation);
            if (!result.Success)
                throw new InvalidOperationException($"Planner command rejected: {result.FailureCode} {result.Message}");
            _actions.Add(action);
            RecordBoundary();
            return result;
        }

        public CombatPlanningTrace ExportTrace() => new CombatPlanningTrace(
            _options.EntryIdentity, _session.ExportTrace(), _random.Export());

        public void Dispose() => _session.Dispose();

        /// <summary>
        /// Reconstructs an exported actual past without running a strategy or
        /// sampling any future. The entry factory comes from the saved Run
        /// payload, scenario and instance-ID boundary in the report.
        /// </summary>
        public static ReplayTrace ReplayRecorded(
            Func<IRandomSource, CombatPlanningBranch> entryFactory,
            IReadOnlyList<CombatRandomDecision> randomHistory,
            IReadOnlyList<LegalAction> actions, int entryExperience = 0)
        {
            if (entryFactory == null || actions == null) throw new ArgumentNullException(nameof(entryFactory));
            CombatRandomHistory random = CombatRandomHistory.FromRecorded(randomHistory);
            using CombatPlanningBranch branch = entryFactory(random);
            if (branch.Combat.State.CurrentTurn != 0)
                throw new ArgumentException("Replay entry is already started.");
            branch.Combat.SetRandomSource(random);
            using var session = new HeadlessCombatSession(branch.Combat);
            branch.Combat.StartTurn();
            if (entryExperience > 0) branch.Combat.GainXP(entryExperience);
            foreach (LegalAction action in actions)
                if (!session.Step(action).Success)
                    throw new InvalidOperationException("Exported action cannot be replayed at this entry.");
            if (random.Position != random.Count)
                throw new InvalidOperationException("Replay left unexplained random history.");
            return session.ExportTrace();
        }

        /// <summary>
        /// Evaluates the same requested sequence in one independent hypothetical
        /// future. A reveal is a leaf, never an invitation to inspect the real
        /// hidden roster or choose a different action for each sampled future.
        /// </summary>
        public CombatTrial Evaluate(IReadOnlyList<LegalAction> sequence, int futureSeed)
        {
            BattleObservation origin = Observe();
            string actualBefore = Fingerprint(origin);
            int randomBefore = _random.Count;
            CombatRandomHistory replay = _random.Replay();
            using CombatPlanningBranch branch = _options.BranchFactory(replay);
            CombatController combat = branch.Combat;
            ValidateIsolation(combat);
            combat.SetRandomSource(replay);
            using var session = new HeadlessCombatSession(combat);
            combat.StartTurn();
            if (_entryExperience > 0) combat.GainXP(_entryExperience);
            AssertBoundary(session, replay, 0);
            for (int i = 0; i < _actions.Count; i++)
            {
                StepResult step = session.Step(_actions[i]);
                if (!step.Success) throw new InvalidOperationException("Past command failed during reconstruction.");
                _options.Statistics.ReplayedCommands++;
                AssertBoundary(session, replay, i + 1);
            }
            replay.BeginFuture(futureSeed);
            var known = new HashSet<int>();
            foreach (BattleObservation.EnemyView enemy in origin.VisibleEnemies)
                known.Add(enemy.RuntimeId);
            // Unknown enemies never contribute their real geometry, resistances,
            // death gates or statistics to a candidate. Core owns this explicit
            // empty-unknown-space hypothesis, applied only after exact past replay.
            combat.ApplyKnownEnemyHypothesis(known);
            bool revealed = false, endsTurn = false;
            int overkillDamage = 0;
            void CheckNewEnemies()
            {
                foreach (EnemyStats enemy in combat.Enemies)
                    if (!enemy.IsDead && enemy.IsDeployed && enemy.row >= 0 &&
                        enemy.row < EnemyFormationLayout.VisibleRows && !known.Contains(enemy.runtimeId))
                        revealed = true;
            }
            combat.OnFormationAdvanced += _ => CheckNewEnemies();
            combat.OnLevelBossArrived += _ => revealed = true;
            combat.OnCardsDrawn += e => { if (e.Count > 0) revealed = true; };
            combat.OnLevelUpRewardsGenerated += _ => revealed = true;
            // Core publishes this after all effects, Wuxing, resident crawler
            // triggers and the card's boss timer, before victory resets the
            // encounter-only stats. Any known defeat may expose unknown space,
            // so stop at that complete-card boundary in every hypothesis.
            combat.OnStateChanged += reason =>
            {
                if (reason == CombatStateChangeReason.CardPlayed &&
                    (revealed || AnyKnownDefeated(combat, known)))
                {
                    if (AnyKnownDefeated(combat, known)) _options.Statistics.EnemyBoundaryStops++;
                    throw new CompletedCardBoundary();
                }
            };
            combat.OnDamageEvent += e =>
            {
                if (known.Contains(e.TargetEnemyRuntimeId)) overkillDamage += e.OverkillDamage;
            };
            try
            {
                for (int i = 0; i < sequence.Count; i++)
                {
                    if (!Contains(session.GetLegalActions(), sequence[i]))
                        return CombatTrial.Illegal;
                    endsTurn = sequence[i].Type == LegalActionType.EndTurn;
                    // Every candidate uses the same known-actor hypothesis.
                    // Finish Core's current enemy turn and stop before making
                    // any decision from the newly drawn hand or next intent.
                    if (endsTurn)
                        _options.Statistics.TurnBoundaryStops++;
                    StepResult result = session.Step(sequence[i]);
                    if (!result.Success) throw new InvalidOperationException("Core rejected a legal trial action.");
                    if (revealed || endsTurn || result.Observation.IsCombatOver ||
                        AllKnownDefeated(combat, known)) break;
                }
            }
            catch (CompletedCardBoundary) { }
            finally
            {
                if (randomBefore != _random.Count || actualBefore != Fingerprint(Observe()))
                    throw new InvalidOperationException("Discarded planning branch changed the actual encounter.");
            }
            if (revealed) _options.Statistics.HiddenBoundaryStops++;
            BattleObservation after = session.Observe();
            var remaining = new List<CombatTrial.EnemyRemainder>();
            foreach (EnemyStats enemy in combat.Enemies)
                if (known.Contains(enemy.runtimeId))
                    remaining.Add(new CombatTrial.EnemyRemainder(enemy.runtimeId,
                        Math.Max(0, enemy.currentHealth), Math.Max(0, enemy.armor),
                        enemy.IsDead || enemy.IsWithdrawn, enemy.isDisarmed,
                        enemy.IsDeployed && enemy.row >= 0 && enemy.row < EnemyFormationLayout.VisibleRows));
            bool stopped = revealed || endsTurn || after.IsCombatOver || AnyKnownDefeated(combat, known);
            // Do not export newly revealed cards, enemies, intents or reward
            // values: even an interrupted Core command is only a numeric leaf.
            return new CombatTrial(after.Player, remaining.AsReadOnly(),
                after.Hand.Count, after.ActiveCrawlers.Count, overkillDamage, stopped,
                endsTurn, stopped ? Array.Empty<LegalAction>() : session.GetLegalActions());
        }

        private static bool AllKnownDefeated(CombatController combat, HashSet<int> known)
        {
            foreach (EnemyStats enemy in combat.Enemies)
                if (known.Contains(enemy.runtimeId) && !enemy.IsDead && !enemy.IsWithdrawn) return false;
            return true;
        }

        private static bool AnyKnownDefeated(CombatController combat, HashSet<int> known)
        {
            foreach (EnemyStats enemy in combat.Enemies)
                if (known.Contains(enemy.runtimeId) && (enemy.IsDead || enemy.IsWithdrawn)) return true;
            return false;
        }

        private void ValidateIsolation(CombatController branch)
        {
            if (branch.State.CurrentTurn != 0 || ReferenceEquals(_actual, branch) ||
                ReferenceEquals(_actual.Player, branch.Player) ||
                ReferenceEquals(_actual.RunProgression, branch.RunProgression) ||
                ReferenceEquals(_actual.Economy, branch.Economy) ||
                ReferenceEquals(_actual.State.Deck, branch.State.Deck))
                throw new InvalidOperationException("Branch factory reused actual mutable state or returned a started combat.");
            foreach (EnemyStats enemy in branch.Enemies)
                foreach (EnemyStats actualEnemy in _actual.Enemies)
                    if (ReferenceEquals(enemy, actualEnemy))
                        throw new InvalidOperationException("Branch factory reused an actual enemy.");
        }

        private void RecordBoundary()
        {
            _randomBoundaries.Add(_random.Count);
            _stateBoundaries.Add(Fingerprint(Observe()));
        }

        private void AssertBoundary(HeadlessCombatSession session, CombatRandomHistory replay, int index)
        {
            if (_randomBoundaries[index] != replay.Position ||
                _stateBoundaries[index] != Fingerprint(session.Observe()))
                throw new InvalidOperationException($"Reconstruction differs at past action {index}.");
        }

        internal static bool Contains(IReadOnlyList<LegalAction> legal, LegalAction action)
        {
            foreach (LegalAction item in legal) if (item.Equals(action)) return true;
            return false;
        }

        private static string Fingerprint(BattleObservation o)
        {
            var b = new StringBuilder();
            var p = o.Player;
            b.Append(o.Turn).Append('|').Append(o.Phase).Append('|')
                .Append(o.AwaitingRewardSelection).Append(',').Append(o.SealAssignmentPending).Append('|')
                .Append(p.CurrentHealth).Append(',').Append(p.MaxHealth).Append(',').Append(p.Armor)
                .Append(',').Append(p.CurrentMana).Append(',').Append(p.TemporaryMana).Append(',').Append(p.BaseMana)
                .Append(',').Append(p.ComboCount).Append(',').Append(p.ComboLength)
                .Append(',').Append(p.Level).Append(',').Append(p.Experience)
                .Append(',').Append(p.Might).Append(',').Append(p.Amount).Append(',').Append(p.Area)
                .Append(',').Append(p.Duration).Append(',').Append(p.HandLimit).Append(',').Append(p.DrawPerTurn)
                .Append(',').Append(p.Luck).Append(',').Append(p.Greed).Append(',').Append(p.Growth)
                .Append(',').Append(p.Revival).Append(',').Append(p.Curse)
                .Append(',').Append(p.CardPlayGrowth.Enabled).Append(',').Append(p.CardPlayGrowth.TotalRealPlays)
                .Append(',').Append(p.CardPlayGrowth.SettledCycles).Append(',').Append(p.CardPlayGrowth.BaseManaBonus)
                .Append('|').Append(o.DrawPileCount).Append(',').Append(o.DiscardPileCount);
            foreach (var c in o.Hand) b.Append("|h:").Append(c.RuntimeInstanceId).Append(':').Append(c.ContentId)
                .Append(':').Append(c.PaymentCost).Append(':').Append(c.DisplayedDamage);
            foreach (var c in o.DiscardPile) b.Append("|d:").Append(c.RuntimeInstanceId).Append(':').Append(c.ContentId);
            foreach (var e in o.VisibleEnemies)
            {
                b.Append("|e:").Append(e.RuntimeId).Append(':').Append(e.CurrentHealth).Append(':').Append(e.Armor)
                    .Append(':').Append(e.Row).Append(':').Append(e.Column).Append(':').Append(e.IsDisarmed)
                    .Append(':').Append(e.IsOverkillTarget).Append(':').Append(e.Intent.Type).Append(':').Append(e.Intent.Amount);
                foreach (var s in e.Statuses) b.Append(':').Append(s.Effect).Append(':').Append(s.Stacks).Append(':').Append(s.RemainingTurns);
            }
            foreach (var s in o.PlayerStatuses) b.Append("|s:").Append(s.Effect).Append(':').Append(s.Stacks).Append(':').Append(s.RemainingTurns);
            foreach (var c in o.ActiveCrawlers) b.Append("|c:").Append(c.RuntimeId).Append(':').Append(c.ContentId).Append(':').Append(c.RemainingTriggers);
            foreach (var t in o.TerrainEffects) b.Append("|t:").Append(t.RuntimeId).Append(':').Append(t.Stacks).Append(':').Append(t.RemainingTriggers);
            foreach (var r in o.RewardOptions) b.Append("|r:").Append(r.RewardIndex).Append(':').Append(r.CardContentId).Append(':').Append(r.StatType).Append(':').Append(r.StatAmount);
            if (o.BossTimer != null) b.Append("|b:").Append(o.BossTimer.OpenEyes).Append(':').Append(o.BossTimer.IsTriggered);
            return b.ToString();
        }

        private sealed class CompletedCardBoundary : Exception { }
    }

    /// <summary>A trial exports only public consequences, with no hidden future objects.</summary>
    public sealed class CombatTrial
    {
        internal static readonly CombatTrial Illegal = new CombatTrial();
        private CombatTrial() { IsLegal = false; }
        internal CombatTrial(BattleObservation.PlayerView player, IReadOnlyList<EnemyRemainder> enemies,
            int handCount, int crawlers, int overkillDamage, bool stopped, bool endsTurn, IReadOnlyList<LegalAction> legal)
        {
            IsLegal = true; Player = player; Enemies = enemies; HandCount = handCount;
            Crawlers = crawlers; OverkillDamage = overkillDamage; Stopped = stopped; EndsTurn = endsTurn; LegalActions = legal;
        }
        public bool IsLegal { get; }
        public BattleObservation.PlayerView Player { get; }
        public IReadOnlyList<EnemyRemainder> Enemies { get; }
        public int HandCount { get; }
        public int Crawlers { get; }
        public int OverkillDamage { get; }
        public bool Stopped { get; }
        /// <summary>The known-actor enemy turn ran and the candidate stops at that boundary.</summary>
        public bool EndsTurn { get; }
        public IReadOnlyList<LegalAction> LegalActions { get; }
        public sealed class EnemyRemainder
        {
            internal EnemyRemainder(int id, int health, int armor, bool defeated, bool disarmed, bool visible)
            { RuntimeId = id; Health = health; Armor = armor; Defeated = defeated; Disarmed = disarmed; Visible = visible; }
            public int RuntimeId { get; }
            public int Health { get; }
            public int Armor { get; }
            public bool Defeated { get; }
            public bool Disarmed { get; }
            public bool Visible { get; }
        }
    }
}
