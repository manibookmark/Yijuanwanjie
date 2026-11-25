using System;
using System.Collections.Generic;
using System.Diagnostics;
using WanjieScroll.Core;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// Bounded beam search over authoritative trial consequences. This policy
    /// cannot access CombatController or game randomness. Each candidate uses
    /// the same action sequence across hypothetical futures; only its first
    /// action is committed, and every real reveal causes a fresh decision.
    /// </summary>
    public sealed class PlanningCombatAgent
    {
        public const string Id = "PlanningCombatAgent";
        public const int Revision = 2;
        private readonly CombatPlanningOptions _options;
        public PlanningCombatAgent(CombatPlanningOptions options)
            => _options = options ?? throw new ArgumentNullException(nameof(options));

        public LegalAction ChooseAction(CombatPlanningSession session, out string explanation)
        {
            BattleObservation observation = session.Observe();
            IReadOnlyList<LegalAction> legal = session.GetLegalActions();
            if (legal.Count == 0) throw new InvalidOperationException("No legal combat action.");
            if (observation.AwaitingRewardSelection)
                throw new InvalidOperationException("Build policy must resolve rewards before combat planning.");
            if (_options.MaxExpansions < legal.Count)
                throw new InvalidOperationException("Search budget cannot evaluate every current legal action.");
            var timer = Stopwatch.StartNew();
            int decision = _options.Statistics.Decisions++;
            int expanded = 0, reachedDepth = 0;
            var beam = new List<Candidate> { new Candidate(new List<LegalAction>(), legal) };
            Candidate best = null;
            for (int depth = 1; depth <= _options.MaxDepth && beam.Count > 0; depth++)
            {
                var next = new List<Candidate>();
                foreach (Candidate parent in beam)
                {
                    foreach (LegalAction action in parent.Legal)
                    {
                        if (expanded >= _options.MaxExpansions) break;
                        var sequence = new List<LegalAction>(parent.Sequence) { action };
                        Candidate candidate = Evaluate(session, observation, sequence, decision);
                        expanded++;
                        reachedDepth = depth;
                        if (candidate == null) continue;
                        if (best == null || Compare(candidate, best) < 0) best = candidate;
                        if (candidate.Legal.Count > 0) next.Add(candidate);
                    }
                    if (expanded >= _options.MaxExpansions) break;
                }
                if (expanded >= _options.MaxExpansions)
                {
                    _options.Statistics.BudgetExhaustions++;
                    break;
                }
                next.Sort(Compare);
                if (next.Count > _options.BeamWidth)
                    next.RemoveRange(_options.BeamWidth, next.Count - _options.BeamWidth);
                beam = next;
            }
            timer.Stop();
            _options.Statistics.ExpandedNodes += expanded;
            _options.Statistics.MaximumDepth = Math.Max(_options.Statistics.MaximumDepth, reachedDepth);
            _options.Statistics.ElapsedMilliseconds += timer.ElapsedMilliseconds;
            if (best == null) throw new InvalidOperationException("No current action survived isolated evaluation.");
            explanation = $"{Id}@{Revision}; depth={reachedDepth}; candidates={expanded}; " +
                $"samples={_options.FutureSamples}; value={best.Score:F3}; sequence=" +
                string.Join(",", best.Sequence);
            return best.Sequence[0];
        }

        private Candidate Evaluate(CombatPlanningSession session, BattleObservation origin,
            List<LegalAction> sequence, int decision)
        {
            double score = 0;
            int playedCards = 0;
            double committedDamagePotential = 0;
            foreach (LegalAction action in sequence)
                if (action.Type == LegalActionType.PlayCard)
                {
                    playedCards++;
                    foreach (BattleObservation.CardView card in origin.Hand)
                        if (card.RuntimeInstanceId == action.CardRuntimeInstanceId)
                            committedDamagePotential += Math.Max(0, card.DisplayedDamage);
                }
            List<LegalAction> common = null;
            for (int sample = 0; sample < _options.FutureSamples; sample++)
            {
                // Common hypothetical seeds reduce noise between candidate
                // comparisons; this seed never comes from the actual game RNG.
                int seed = unchecked(_options.Seed * 397 ^ decision * 7919 ^ sample * 104729);
                CombatTrial trial = session.Evaluate(sequence, seed);
                if (!trial.IsLegal) return null;
                score += Score(origin, trial, sequence.Count, playedCards, committedDamagePotential);
                if (common == null) common = new List<LegalAction>(trial.LegalActions);
                else common.RemoveAll(action => !CombatPlanningSession.Contains(trial.LegalActions, action));
            }
            return new Candidate(sequence, common ?? new List<LegalAction>())
                { Score = score / _options.FutureSamples };
        }

        private static double Score(BattleObservation before, CombatTrial after, int actions,
            int playedCards, double committedDamagePotential)
        {
            var p = before.Player;
            var q = after.Player;
            if (q.CurrentHealth <= 0) return -1000000;
            double damage = 0, incoming = 0, removedThreat = 0;
            int defeated = 0;
            foreach (BattleObservation.EnemyView enemy in before.VisibleEnemies)
            {
                CombatTrial.EnemyRemainder remaining = null;
                foreach (var candidate in after.Enemies)
                    if (candidate.RuntimeId == enemy.RuntimeId) { remaining = candidate; break; }
                if (remaining == null) throw new InvalidOperationException("Trial lost a known enemy identity.");
                damage += Math.Max(0, enemy.CurrentHealth + enemy.Armor - remaining.Health - remaining.Armor);
                double threat = enemy.Intent.Type == EnemyIntentType.Attack ? Math.Max(0, enemy.Intent.Amount) : 0;
                if (remaining.Defeated) defeated++;
                if (remaining.Defeated || remaining.Disarmed || !remaining.Visible) removedThreat += threat;
                else incoming += threat;
            }
            double futureLoss = after.EndsTurn ? 0 : Math.Max(0, incoming - q.Armor);
            double score = (q.CurrentHealth - p.CurrentHealth) * 24.0 - futureLoss * 24.0;
            if (futureLoss >= q.CurrentHealth) score -= 100000;
            score += damage + defeated * 10 + removedThreat * 1.5 + after.OverkillDamage * 0.2;
            // At a potential reveal, retain a stronger known card when a
            // smaller hit achieves the same kill. This values unused damage
            // potential without searching into a newly revealed enemy group.
            score -= Math.Max(0, committedDamagePotential - damage - after.OverkillDamage) * 0.1;
            score += Math.Min(q.Armor, incoming) * 0.4 + Math.Max(0, q.Armor - incoming) * 0.08;
            score += (q.MaxHealth - p.MaxHealth) * 20;
            score += (q.Might - p.Might) * 0.9 + (q.Amount - p.Amount) * 20;
            score += (q.BaseMana - p.BaseMana) * 16 + (q.HandLimit - p.HandLimit) * 12;
            score += (q.DrawPerTurn - p.DrawPerTurn) * 12 + (q.Duration - p.Duration) * 2;
            score += (q.Level - p.Level) * 14 + (q.Experience - p.Experience) * 0.25;
            score += (after.Crawlers - before.ActiveCrawlers.Count) * 9;
            if (!after.EndsTurn)
            {
                score += (q.AvailableMana - p.AvailableMana) * 1.5;
                // Draws are valuable without inspecting the identity of a
                // newly drawn card in the same search tree.
                score += Math.Max(0, after.HandCount - before.Hand.Count + playedCards) * 5;
                score += Math.Min(8, q.ComboCount) * 0.3;
            }
            return score - actions * 0.02;
        }

        private static int Compare(Candidate left, Candidate right)
        {
            int value = right.Score.CompareTo(left.Score);
            if (value != 0) return value;
            int length = Math.Min(left.Sequence.Count, right.Sequence.Count);
            for (int i = 0; i < length; i++)
            {
                int type = left.Sequence[i].Type.CompareTo(right.Sequence[i].Type);
                if (type != 0) return type;
                int id = left.Sequence[i].CardRuntimeInstanceId.CompareTo(right.Sequence[i].CardRuntimeInstanceId);
                if (id != 0) return id;
            }
            return left.Sequence.Count.CompareTo(right.Sequence.Count);
        }

        private sealed class Candidate
        {
            internal Candidate(List<LegalAction> sequence, IReadOnlyList<LegalAction> legal)
            { Sequence = sequence; Legal = legal; }
            internal readonly List<LegalAction> Sequence;
            internal readonly IReadOnlyList<LegalAction> Legal;
            internal double Score;
        }
    }
}
