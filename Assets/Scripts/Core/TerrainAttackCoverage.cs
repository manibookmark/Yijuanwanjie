using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns column coverage for one attack execution or a shared preview hypothesis.
    /// Copies only effect/column identity from live terrain, then observes planned
    /// placements immediately; neither live layers nor their lifetime are mutated.
    /// Coverage remains per effect so temporary fire cannot stand in for persistent fire.
    /// </summary>
    internal sealed class TerrainAttackCoverage
    {
        private readonly Dictionary<string, HashSet<int>> _columnsByEffect =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        internal TerrainAttackCoverage(IReadOnlyList<TerrainEffectInstance> activeTerrainEffects)
        {
            if (activeTerrainEffects == null) return;
            for (int i = 0; i < activeTerrainEffects.Count; i++)
            {
                TerrainEffectInstance instance = activeTerrainEffects[i];
                if (instance == null || instance.TargetMode != TerrainTargetMode.ColumnDynamicFront ||
                    instance.EffectKind != TerrainEffectKind.Damage || instance.Stacks <= 0 ||
                    instance.MagnitudePerStack <= 0 || instance.RemainingTriggers <= 0)
                    continue;
                AddColumn(instance.EffectId, instance.Column);
            }
        }

        internal bool IsCovered(string effectId, int column)
            => !string.IsNullOrWhiteSpace(effectId) &&
                _columnsByEffect.TryGetValue(effectId, out HashSet<int> columns) &&
                columns.Contains(column);

        /// <summary>
        /// A lethal initial hit still leaves its layer. Empty projectiles never
        /// produce a placement, so they cannot create coverage here.
        /// </summary>
        internal void ObservePlacement(TerrainEffectPlacement placement)
        {
            if (placement.TargetMode != TerrainTargetMode.ColumnDynamicFront ||
                placement.EffectKind != TerrainEffectKind.Damage ||
                placement.MagnitudePerStack <= 0 || placement.TriggerCount <= 0)
                return;
            AddColumn(placement.EffectId, placement.Column);
        }

        private void AddColumn(string effectId, int column)
        {
            if (string.IsNullOrWhiteSpace(effectId) || column < 0 ||
                column >= EnemyFormationLayout.Columns)
                return;
            if (!_columnsByEffect.TryGetValue(effectId, out HashSet<int> columns))
            {
                columns = new HashSet<int>();
                _columnsByEffect.Add(effectId, columns);
            }
            columns.Add(column);
        }
    }
}
