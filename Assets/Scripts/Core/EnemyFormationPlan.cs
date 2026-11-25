using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// An immutable placement snapshot bound to one ordered source roster.
    /// Only identity tokens retain source references; all compared values and
    /// geometry are copied. No mutable enemy data is exposed through the plan.
    /// Applying changes geometry only: deployment belongs to LevelBossEncounter.
    /// </summary>
    public enum EnemyFormationPlacementMode { CommonPreset, SourceMatrix }

    public sealed class EnemyFormationPlan
    {
        public readonly struct Position
        {
            public int SourceIndex { get; }
            public int RuntimeId { get; }
            public string ContentId { get; }
            public int Row { get; }
            public int Column { get; }
            public int Width { get; }
            public int Depth { get; }
            public bool IsBoss { get; }
            public BossRank BossRank { get; }
            public int SourceRow { get; }
            public int SourceColumn { get; }
            public int SourceColumnCount { get; }

            internal Position(int index, EnemyStats enemy, int row, int column,
                EnemyFormationSourceSlot source = null, int sourceColumnCount = 0)
            {
                SourceIndex = index;
                RuntimeId = enemy.runtimeId;
                ContentId = enemy.contentId;
                Row = row;
                Column = column;
                Width = enemy.GridWidth;
                Depth = enemy.GridDepth;
                IsBoss = enemy.isBoss;
                BossRank = enemy.bossRank;
                SourceRow = source?.sourceRow ?? -1;
                SourceColumn = source?.sourceColumn ?? -1;
                SourceColumnCount = sourceColumnCount;
            }
        }

        private readonly EnemyStats[] _sourceIdentities;

        public EnemyFormationPlacementMode Mode { get; }
        public CommonFormationPreset Preset { get; }
        public IReadOnlyList<Position> Positions { get; }

        internal EnemyFormationPlan(IReadOnlyList<EnemyStats> enemies,
            CommonFormationPreset preset, int[] rows, int[] columns)
            : this(enemies, preset, rows, columns, null, 0) { }

        internal EnemyFormationPlan(IReadOnlyList<EnemyStats> enemies,
            CommonFormationPreset preset, int[] rows, int[] columns,
            IReadOnlyList<EnemyFormationSourceSlot> sourceSlots, int sourceColumnCount)
        {
            Mode = sourceSlots == null ? EnemyFormationPlacementMode.CommonPreset : EnemyFormationPlacementMode.SourceMatrix;
            Preset = preset;
            _sourceIdentities = new EnemyStats[enemies.Count];
            var positions = new Position[enemies.Count];
            for (int i = 0; i < enemies.Count; i++)
            {
                _sourceIdentities[i] = enemies[i];
                positions[i] = new Position(i, enemies[i], rows[i], columns[i],
                    sourceSlots == null ? (EnemyFormationSourceSlot)null : sourceSlots[i], sourceColumnCount);
            }
            Positions = Array.AsReadOnly(positions);
        }

        /// <summary>
        /// Validates the entire roster before the first write. Object identity,
        /// source order, runtime/content IDs, footprint and boss role must match;
        /// health and other combat attributes may change and are never restored.
        /// A clone/rebuilt roster requires its own Create call. Call on the combat
        /// owner's thread, without concurrent roster mutation.
        /// </summary>
        public void ApplyTo(IList<EnemyStats> enemies)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (enemies.Count != Positions.Count)
                throw new ArgumentException("Formation plan enemy count does not match the source roster.", nameof(enemies));

            // Capture validated targets before writing, including for a custom IList.
            var targets = new EnemyStats[enemies.Count];
            for (int i = 0; i < targets.Length; i++)
            {
                EnemyStats enemy = enemies[i];
                Position expected = Positions[i];
                if (!ReferenceEquals(enemy, _sourceIdentities[i]) || enemy == null ||
                    enemy.runtimeId != expected.RuntimeId ||
                    !string.Equals(enemy.contentId, expected.ContentId, StringComparison.Ordinal) ||
                    enemy.GridWidth != expected.Width || enemy.GridDepth != expected.Depth ||
                    enemy.isBoss != expected.IsBoss || enemy.bossRank != expected.BossRank)
                    throw new ArgumentException(
                        $"Formation plan source index {i} has changed identity, footprint or BossRank.", nameof(enemies));
                targets[i] = enemy;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                EnemyStats enemy = targets[i];
                enemy.row = Positions[i].Row;
                enemy.column = Positions[i].Column;
                enemy.spawnWaveIndex = 0;
                enemy.sourceFormationRow = Positions[i].SourceRow;
                enemy.sourceFormationColumn = Positions[i].SourceColumn;
                enemy.sourceFormationColumnCount = Positions[i].SourceColumnCount;
            }
        }
    }
}
