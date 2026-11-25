using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>An immutable enemy/source pair. Amount is the latest installed total, never hit count.</summary>
    public readonly struct MugMark
    {
        public int EnemyRuntimeId { get; }
        public int SourceCardInstanceId { get; }
        public int BaseJade { get; }

        internal MugMark(int enemy, int source, int amount)
        {
            EnemyRuntimeId = enemy;
            SourceCardInstanceId = source;
            BaseJade = amount;
        }
    }

    /// <summary>
    /// Owns encounter Mug marks, source deduplication and exactly-once payout. Only
    /// completed real base attacks may submit hits. Periodic/seal/terrain/counter
    /// damage can settle existing marks but never registers new ones. Marks depend
    /// on stable enemy/card identities, not card lifetime, slots, target indices or UI.
    /// Same-source seals add; a later hit replaces that total. Distinct sources are
    /// paid separately in ascending instance-ID order, so Economy rounds per source.
    /// </summary>
    public sealed class MugMarkSystem
    {
        private readonly CombatState _state;
        private readonly EconomySystem _economy;
        private readonly Func<int> _currentGreed;
        private readonly List<MugMark> _marks = new List<MugMark>();

        public IReadOnlyList<MugMark> Active { get; }

        internal MugMarkSystem(CombatState state, EconomySystem economy, Func<int> currentGreed)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _currentGreed = currentGreed ?? throw new ArgumentNullException(nameof(currentGreed));
            Active = _marks.AsReadOnly();
        }

        /// <summary>
        /// Consume authoritative receipts before any defeat is published. Positive
        /// shield damage counts; zero damage, corpses and undeployed targets do not.
        /// Bounce within the host's own pattern is a host hit; outer splash is not.
        /// A lethal host hit registers here too, then the normal defeat settles it.
        /// </summary>
        internal void ObserveHostHits(int sourceCardInstanceId, int baseJade, IReadOnlyList<DamageEvent> hits)
        {
            if (sourceCardInstanceId <= 0 || baseJade <= 0 || hits == null || _state.IsBattleOver) return;
            foreach (DamageEvent hit in hits)
            {
                if (hit.SourceCardInstanceId != sourceCardInstanceId || hit.AppliedDamage <= 0 ||
                    (hit.Kind != DamageKind.Direct && hit.Kind != DamageKind.Bounce) ||
                    hit.TargetIndex < 0 || hit.TargetIndex >= _state.Enemies.Count) continue;
                EnemyStats target = _state.Enemies[hit.TargetIndex];
                if (target == null || target.runtimeId != hit.TargetEnemyRuntimeId ||
                    !target.IsDeployed || target.IsWithdrawn) continue;
                int index = _marks.FindIndex(m => m.EnemyRuntimeId == target.runtimeId &&
                    m.SourceCardInstanceId == sourceCardInstanceId);
                var mark = new MugMark(target.runtimeId, sourceCardInstanceId, baseJade);
                if (index < 0) _marks.Add(mark);
                else _marks[index] = mark;
            }
        }

        /// <summary>
        /// Remove all marks before callbacks/economic grants. Erase/scripted removal
        /// consumes marks without payout; immunity leaves a still-live enemy untouched.
        /// No source card needs to remain in any pile when its reward is settled.
        /// </summary>
        internal void ResolveDefeat(EnemyDefeatResult defeat)
        {
            if (defeat.WasImmune || defeat.TargetIndex < 0 || defeat.TargetIndex >= _state.Enemies.Count) return;
            EnemyStats target = _state.Enemies[defeat.TargetIndex];
            if (target == null || target.runtimeId != defeat.TargetEnemyRuntimeId || !target.IsDead) return;
            var due = _marks.FindAll(m => m.EnemyRuntimeId == defeat.TargetEnemyRuntimeId);
            ForgetEnemy(defeat.TargetEnemyRuntimeId);
            if (defeat.Cause != DefeatCause.Damage || target.IsWithdrawn || _state.IsBattleOver) return;
            due.Sort((a, b) => a.SourceCardInstanceId.CompareTo(b.SourceCardInstanceId));
            int greed = _currentGreed();
            foreach (MugMark mark in due)
                _economy.Grant(mark.BaseJade, greed, JadeSource.Seal, true);
        }

        internal void ForgetEnemy(int enemyRuntimeId)
            => _marks.RemoveAll(m => m.EnemyRuntimeId == enemyRuntimeId);

        internal void Clear() => _marks.Clear();

        public MugMarkMemento CaptureMemento()
        {
            var saved = new MugMarkMemento();
            foreach (MugMark mark in _marks)
                saved.marks.Add(new MugMarkMemento.Entry
                {
                    enemyRuntimeId = mark.EnemyRuntimeId,
                    sourceCardInstanceId = mark.SourceCardInstanceId,
                    baseJade = mark.BaseJade
                });
            return saved;
        }

        /// <summary>
        /// Restore an owning combat checkpoint after the matching roster, silently and
        /// atomically. Does not import marks into a terminal combat, grant jade or retain
        /// mutable DTO references. Consumed marks are absent from subsequent captures.
        /// </summary>
        public void RestoreMemento(MugMarkMemento saved)
        {
            if (saved == null || saved.version != 1 || saved.marks == null)
                throw new ArgumentException("Invalid Mug memento.", nameof(saved));
            var restored = new List<MugMark>();
            var pairs = new HashSet<(int, int)>();
            foreach (MugMarkMemento.Entry entry in saved.marks)
            {
                if (entry == null || entry.enemyRuntimeId <= 0 || entry.sourceCardInstanceId <= 0 ||
                    entry.baseJade <= 0 || !pairs.Add((entry.enemyRuntimeId, entry.sourceCardInstanceId)))
                    throw new ArgumentException("Invalid or duplicate Mug source.", nameof(saved));
                EnemyStats target = null;
                foreach (EnemyStats enemy in _state.Enemies)
                    if (enemy?.runtimeId == entry.enemyRuntimeId) { target = enemy; break; }
                if (target == null || target.IsDead || target.IsWithdrawn || !target.IsDeployed)
                    throw new ArgumentException("Mug memento requires the matching live encounter roster.", nameof(saved));
                restored.Add(new MugMark(entry.enemyRuntimeId, entry.sourceCardInstanceId, entry.baseJade));
            }
            _marks.Clear();
            if (!_state.IsBattleOver && !_state.IsDefeat) _marks.AddRange(restored);
        }
    }

    /// <summary>Detached DTO, not a second live owner or a Run-persistent mark list.</summary>
    [Serializable]
    public sealed class MugMarkMemento
    {
        public int version = 1;
        public List<Entry> marks = new List<Entry>();

        [Serializable]
        public sealed class Entry
        {
            public int enemyRuntimeId;
            public int sourceCardInstanceId;
            public int baseJade;
        }
    }
}
