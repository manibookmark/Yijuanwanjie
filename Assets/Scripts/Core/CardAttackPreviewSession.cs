using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the complete hand's frozen attack choices for one decision state.
    /// Candidates are prepared in stable instance order, independently of hand
    /// layout, card hover or display reads. Each matching entry preserves its
    /// recorded critical/target decisions until an authoritative input changes.
    /// Actual play reuses the same tape; composite/unknown effects remain estimates.
    /// CombatController supplies the frozen attack stats, not cache internals.
    /// </summary>
    internal sealed class CardAttackPreviewSession
    {
        private readonly CombatState _state;
        private readonly CombatRuleSet _rules;
        private readonly Func<CardConfig, CombatStatSnapshot> _captureStats;
        private readonly Dictionary<CardConfig, Entry> _entries = new Dictionary<CardConfig, Entry>();
        private int _suspensions;
        public bool HasIncompleteBattlefield { get; private set; }

        public CardAttackPreviewSession(CombatState state, CombatRuleSet rules,
            Func<CardConfig, CombatStatSnapshot> captureStats)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _rules = rules;
            _captureStats = captureStats ?? throw new ArgumentNullException(nameof(captureStats));
        }

        public void Clear() => _entries.Clear();

        // Only an isolated planning branch may remove unknown actors. A roll can
        // be exact for that branch while still not promise the live battle result.
        public void MarkIncompleteBattlefield() => HasIncompleteBattlefield = true;

        // Draw batches can publish identities before auto-play-on-draw settles.
        // A read during that transaction must never consume planning randomness.
        public IDisposable Suspend()
        {
            _suspensions++;
            return new Suspension(this);
        }

        public bool Prepare()
        {
            if (_suspensions != 0 || !_state.CanPlayCards) return false;
            var cards = new List<CardConfig>(_state.Hand);
            cards.Sort((left, right) => (left?.runtimeInstanceId ?? 0)
                .CompareTo(right?.runtimeInstanceId ?? 0));
            var stale = new List<CardConfig>();
            foreach (CardConfig cached in _entries.Keys)
                if (!cards.Contains(cached)) stale.Add(cached);
            foreach (CardConfig cached in stale) _entries.Remove(cached);

            foreach (CardConfig card in cards)
            {
                if (card == null) continue;
                if (CardPlayDamagePreview.NeedsEstimate(card, _rules))
                {
                    _entries.Remove(card);
                    continue;
                }
                CombatStatSnapshot stats = _captureStats(card);
                if (_entries.TryGetValue(card, out Entry entry) && entry.Matches(card, stats, _state))
                    continue;
                if (!AttackPlanBuilder.TryBuildFromCard(card, _rules, stats, out AttackPlan plan))
                {
                    _entries.Remove(card);
                    continue;
                }
                AttackProjection projection = AttackResolver.Project(plan, _state.EnemyRoster,
                    _state.Random ?? DefaultRandomSource.Instance,
                    _state.EnemyAi?.GetPlannedIntents(), _state.BossOverkill,
                    isEstimate: HasIncompleteBattlefield ||
                        CardPlayDamagePreview.HasUnresolvedConditions(card),
                    activeTerrainEffects: _state.TerrainEffects?.Active);
                _entries[card] = new Entry(card, stats, _state, projection);
            }
            return true;
        }

        public AttackProjection Get(CardConfig card)
        {
            if (card == null || _suspensions != 0 || !_state.CanPlayCards) return null;
            Prepare();
            return GetPrepared(card);
        }

        // Observation/decision protocols may inspect a prepared promise but never
        // roll a new one. Missing, changed or suspended states remain estimates.
        public AttackProjection GetPrepared(CardConfig card)
        {
            if (card == null || _suspensions != 0 || !_state.CanPlayCards ||
                !_state.Deck.HasInHand(card)) return null;
            return _entries.TryGetValue(card, out Entry entry) &&
                entry.Matches(card, _captureStats(card), _state) ? entry.Projection : null;
        }

        private sealed class Suspension : IDisposable
        {
            private CardAttackPreviewSession _owner;
            public Suspension(CardAttackPreviewSession owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._suspensions--;
                _owner = null;
            }
        }

        private readonly struct EnemyProjectionStamp
        {
            private readonly int _runtimeId;
            private readonly int _health;
            private readonly int _armor;
            private readonly int _row;
            private readonly int _column;
            private readonly int _gridWidth;
            private readonly int _gridDepth;
            private readonly bool _deployed;
            private readonly bool _withdrawn;
            private readonly bool _disarmed;
            private readonly bool _overkillTarget;
            private readonly bool _justFilledFrontRow;
            private readonly float _knockbackMultiplier;
            private readonly float _disarmMultiplier;

            public EnemyProjectionStamp(EnemyStats enemy)
            {
                _runtimeId = enemy?.runtimeId ?? 0;
                _health = enemy?.currentHealth ?? 0;
                _armor = enemy?.armor ?? 0;
                _row = enemy?.row ?? 0;
                _column = enemy?.column ?? -1;
                _gridWidth = enemy?.GridWidth ?? 0;
                _gridDepth = enemy?.GridDepth ?? 0;
                _deployed = enemy?.IsDeployed == true;
                _withdrawn = enemy?.IsWithdrawn == true;
                _disarmed = enemy?.isDisarmed == true;
                _overkillTarget = enemy?.isOverkillTarget == true;
                _justFilledFrontRow = enemy?.justFilledFrontRow == true;
                _knockbackMultiplier =
                    enemy?.EffectiveKnockbackMultiplier ?? 0f;
                _disarmMultiplier = enemy?.EffectiveDisarmMultiplier ?? 0f;
            }

            public bool Matches(EnemyStats enemy)
            {
                return enemy != null &&
                    _runtimeId == enemy.runtimeId &&
                    _health == enemy.currentHealth &&
                    _armor == enemy.armor &&
                    _row == enemy.row &&
                    _column == enemy.column &&
                    _gridWidth == enemy.GridWidth &&
                    _gridDepth == enemy.GridDepth &&
                    _deployed == enemy.IsDeployed &&
                    _withdrawn == enemy.IsWithdrawn &&
                    _disarmed == enemy.isDisarmed &&
                    _overkillTarget == enemy.isOverkillTarget &&
                    _justFilledFrontRow == enemy.justFilledFrontRow &&
                    Mathf.Approximately(
                        _knockbackMultiplier,
                        enemy.EffectiveKnockbackMultiplier) &&
                    Mathf.Approximately(
                        _disarmMultiplier,
                        enemy.EffectiveDisarmMultiplier);
            }
        }

        private sealed class Entry
        {
            private readonly CardConfig _card;
            private readonly IRandomSource _random;
            private readonly int _cardRuntimeId;
            private readonly int _limitBreakCount;
            private readonly int _baseManaCost;
            private readonly int _sealSignature;
            private readonly int _resultingCombo;
            private readonly int _turn;
            private readonly BattlePhase _phase;
            private readonly int _might;
            private readonly int _area;
            private readonly int _amount;
            private readonly int _luck;
            private readonly long _manaBeforePayment;
            private readonly float _heldAttackMultiplier;
            private readonly TerrainEffectSystem _terrainEffects;
            private readonly long _terrainRevision;
            private readonly EnemyProjectionStamp[] _enemies;

            public AttackProjection Projection { get; }

            public Entry(
                CardConfig card,
                CombatStatSnapshot stats,
                CombatState state,
                AttackProjection projection)
            {
                _card = card ?? throw new ArgumentNullException(nameof(card));
                _random = state.Random;
                _terrainEffects = state.TerrainEffects;
                _terrainRevision = state.TerrainEffects?.Revision ?? 0;
                _cardRuntimeId = card.runtimeInstanceId;
                _limitBreakCount = card.LimitBreakCount;
                _baseManaCost = card.manaCost;
                _sealSignature = GetSealSignature(card);
                _resultingCombo = stats.ComboValue;
                _turn = state.CurrentTurn;
                _phase = state.CurrentPhase;
                _might = state.Player?.fengRui ?? 0;
                _area = state.Player?.bengLie ?? 0;
                _amount = state.Player?.amount ?? 0;
                _luck = stats.Luck;
                _manaBeforePayment = (long)state.Mana.CurrentMana + state.Mana.TemporaryMana;
                _heldAttackMultiplier = CardSealRules.GetHeldAttackMultiplier(state.Deck.Hand, card);
                _enemies = new EnemyProjectionStamp[state.Enemies.Count];
                for (int i = 0; i < _enemies.Length; i++)
                    _enemies[i] = new EnemyProjectionStamp(state.Enemies[i]);
                Projection = projection ??
                    throw new ArgumentNullException(nameof(projection));
            }

            public bool Matches(
                CardConfig card,
                CombatStatSnapshot stats,
                CombatState state)
            {
                if (!ReferenceEquals(_card, card) ||
                    !ReferenceEquals(_random, state.Random) ||
                    card == null ||
                    _cardRuntimeId != card.runtimeInstanceId ||
                    _limitBreakCount != card.LimitBreakCount ||
                    _baseManaCost != card.manaCost ||
                    _sealSignature != GetSealSignature(card) ||
                    _resultingCombo != stats.ComboValue ||
                    _turn != state.CurrentTurn ||
                    _phase != state.CurrentPhase ||
                    _might != (state.Player?.fengRui ?? 0) ||
                    _area != (state.Player?.bengLie ?? 0) ||
                    _amount != (state.Player?.amount ?? 0) ||
                    _luck != stats.Luck ||
                    _manaBeforePayment != (long)state.Mana.CurrentMana + state.Mana.TemporaryMana ||
                    _heldAttackMultiplier != CardSealRules.GetHeldAttackMultiplier(state.Deck.Hand, card) ||
                    !ReferenceEquals(_terrainEffects, state.TerrainEffects) ||
                    _terrainRevision != (state.TerrainEffects?.Revision ?? 0) ||
                    _enemies.Length != state.Enemies.Count)
                {
                    return false;
                }

                for (int i = 0; i < _enemies.Length; i++)
                {
                    if (!_enemies[i].Matches(state.Enemies[i]))
                        return false;
                }
                return true;
            }

            private static int GetSealSignature(CardConfig card)
            {
                unchecked
                {
                    int signature = 17;
                    if (card?.seals == null) return signature;
                    for (int i = 0; i < card.seals.Count; i++)
                    {
                        CardSealSpec seal = card.seals[i];
                        signature = signature * 31 + (int)(seal?.effect ?? 0);
                        signature = signature * 31 + (seal?.value ?? 0);
                    }
                    return signature;
                }
            }
        }

    }
}
