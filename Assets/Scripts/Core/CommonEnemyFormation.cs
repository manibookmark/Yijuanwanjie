using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum CommonFormationPreset
    {
        Balanced = 1,
        FrontLoaded = 2,
        RearLoaded = 3,
        Flanks = 4
    }

    /// <summary>
    /// Deterministic physical placement for common encounters; owns no Run choice,
    /// random source, combat phase or persistent state. Rectangles are placed by
    /// descending area/depth/width, with source index breaking ties. A unique
    /// Floor/Stage boss occupies a centered row after all ordinary footprints.
    ///
    /// Supports the existing width 1..Columns and depth 1..VisibleRows bounds,
    /// without rotation. The bounded greedy search increases its row budget;
    /// it is not an exhaustive rectangle packer. Unplaceable mixtures fail
    /// explicitly, even if their area alone would fit. Preset priorities remain
    /// active at every density; crowding can consume intentional gaps. Balanced
    /// rows alternate their horizontal bias. Ordinary formations then close
    /// avoidable empty columns in each three-row window by legal horizontal
    /// moves only; sparse windows leave spare columns at their outer edges
    /// rather than between actors, without acquiring actors to fill the board.
    /// Row loads, footprints and authored boss geometry remain unchanged.
    /// </summary>
    public static class CommonEnemyFormation
    {
        public const int AlgorithmVersion = 4;

        /// <summary>
        /// Identifies a unique explicit Floor/Stage boss after validating the
        /// complete source roster: null entries, aliases, stable ContentIds,
        /// footprints, total capacity and consistent/unique BossRank roles.
        /// An isBoss ability tag with BossRank.None is not a level boss. This
        /// query validates input without changing actors or selecting geometry.
        /// </summary>
        public static bool HasLevelBoss(IReadOnlyList<EnemyStats> enemies)
            => ValidateSourceRoster(enemies, out _, out _) >= 0;

        /// <summary>
        /// Creates a read-only plan without changing enemies or their order.
        /// Requires non-null, distinct source objects with stable content IDs;
        /// an empty roster is valid. BossRank.None retains its legacy ability-tag
        /// meaning. A Floor/Stage rank requires isBoss and is unique per roster.
        /// </summary>
        public static EnemyFormationPlan Create(IReadOnlyList<EnemyStats> enemies,
            CommonFormationPreset preset)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (preset < CommonFormationPreset.Balanced || preset > CommonFormationPreset.Flanks)
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown common formation preset.");

            int bossIndex = ValidateSourceRoster(enemies, out List<int> order, out int area);

            order.Sort((a, b) =>
            {
                EnemyStats left = enemies[a];
                EnemyStats right = enemies[b];
                int size = (right.GridWidth * right.GridDepth).CompareTo(left.GridWidth * left.GridDepth);
                if (size == 0) size = right.GridDepth.CompareTo(left.GridDepth);
                if (size == 0) size = right.GridWidth.CompareTo(left.GridWidth);
                return size != 0 ? size : a.CompareTo(b);
            });

            int bossDepth = bossIndex < 0 ? 0 : enemies[bossIndex].GridDepth;
            int rowLimit = EnemyFormationLayout.HiddenRowLimit - bossDepth;
            int ordinaryArea = area - (bossIndex < 0 ? 0 : enemies[bossIndex].GridWidth * bossDepth);
            if (ordinaryArea > rowLimit * EnemyFormationLayout.Columns)
                throw new ArgumentException("Formation has no capacity for the boss's exclusive trailing rows.", nameof(enemies));

            // Prefer one cell of breathing room per row when the ten-row bound
            // permits it. Otherwise a 40-cell roster fills eight rows exactly,
            // erasing all geometric differences between the four presets.
            int preferredRowCapacity = EnemyFormationLayout.Columns - 1;
            int firstBudget = Math.Min(rowLimit, Math.Max(EnemyFormationLayout.VisibleRows,
                (ordinaryArea + preferredRowCapacity - 1) / preferredRowCapacity));
            for (int rowBudget = firstBudget; rowBudget <= rowLimit; rowBudget++)
            {
                if (!TryPlace(enemies, order, preset, rowBudget, out int[] rows, out int[] columns))
                    continue;
                if (bossIndex >= 0)
                {
                    int tail = 0;
                    foreach (int index in order)
                        tail = Math.Max(tail, rows[index] + enemies[index].GridDepth);
                    rows[bossIndex] = tail;
                    columns[bossIndex] = (EnemyFormationLayout.Columns - enemies[bossIndex].GridWidth) / 2;
                }
                else
                    CloseEmptyLanes(enemies, order, rows, columns, rowBudget);
                return new EnemyFormationPlan(enemies, preset, rows, columns);
            }

            throw new ArgumentException(
                $"Common formation {preset} cannot pack these footprints within {rowLimit} ordinary rows " +
                "using bounded large-first placement; no rotation, resizing or exhaustive packing is supported.", nameof(enemies));
        }

        /// <summary>
        /// Places a generated ordinary encounter using the global Balanced
        /// geometry while retaining its frozen logical source coordinates.
        /// Does not reroll species, reorder actors, consume randomness or alter
        /// stats. Authored Floor/Stage boss encounters use their own path.
        /// </summary>
        public static EnemyFormationPlan CreateGenerated(IReadOnlyList<EnemyStats> enemies,
            CommonEncounterFrozenRoster roster)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (enemies.Count != roster.EnemyContentIds.Count)
                throw new ArgumentException("Generated formation count differs from its frozen roster.", nameof(enemies));
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] == null || !string.Equals(enemies[i].contentId,
                    roster.EnemyContentIds[i], StringComparison.Ordinal))
                    throw new ArgumentException($"Generated formation source index {i} differs from its frozen roster.", nameof(enemies));

            EnemyFormationPlan physical = Create(enemies, CommonFormationPreset.Balanced);
            var rows = new int[enemies.Count];
            var columns = new int[enemies.Count];
            var slots = new EnemyFormationSourceSlot[enemies.Count];
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyFormationPlan.Position position = physical.Positions[i];
                if (position.BossRank != BossRank.None)
                    throw new ArgumentException("Generated ordinary formations cannot contain a Floor or Stage boss.", nameof(enemies));
                rows[i] = position.Row;
                columns[i] = position.Column;
                CommonEncounterRosterEntry source = roster.SourceSlots[i];
                slots[i] = new EnemyFormationSourceSlot(source.SecondAxis, source.FirstAxis);
            }
            return new EnemyFormationPlan(enemies, CommonFormationPreset.Balanced,
                rows, columns, slots, roster.FirstAxisLength);
        }

        private static int ValidateSourceRoster(IReadOnlyList<EnemyStats> enemies,
            out List<int> order, out int area)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            order = new List<int>();
            int bossIndex = -1;
            area = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null)
                    throw new ArgumentException($"Formation source index {i} is null.", nameof(enemies));
                if (string.IsNullOrWhiteSpace(enemy.contentId))
                    throw new ArgumentException($"Formation source index {i} needs a stable ContentId.", nameof(enemies));
                for (int j = 0; j < i; j++)
                    if (ReferenceEquals(enemy, enemies[j]))
                        throw new ArgumentException($"Formation source indices {j} and {i} alias the same enemy.", nameof(enemies));
                EnemyFormationLayout.EnsureValidFootprint(enemy.GridWidth, enemy.GridDepth,
                    $"Formation source index {i}");
                if (enemy.bossRank < BossRank.None || enemy.bossRank > BossRank.Stage ||
                    (enemy.bossRank != BossRank.None && !enemy.isBoss))
                    throw new ArgumentException($"Formation source index {i} has inconsistent BossRank/isBoss.", nameof(enemies));

                area += enemy.GridWidth * enemy.GridDepth;
                if (area > EnemyFormationLayout.Columns * EnemyFormationLayout.HiddenRowLimit)
                    throw new ArgumentException("Formation total footprint exceeds the 5-column, 10-row capacity.", nameof(enemies));
                if (enemy.bossRank == BossRank.None)
                    order.Add(i);
                else
                {
                    if (bossIndex >= 0)
                        throw new ArgumentException("Common formation permits only one Floor or Stage boss.", nameof(enemies));
                    bossIndex = i;
                }
            }
            return bossIndex;
        }

        private static bool TryPlace(IReadOnlyList<EnemyStats> enemies, List<int> order,
            CommonFormationPreset preset, int rowBudget, out int[] rows, out int[] columns)
        {
            rows = new int[enemies.Count];
            columns = new int[enemies.Count];
            var occupied = new bool[rowBudget, EnemyFormationLayout.Columns];
            var rowLoads = new int[rowBudget];
            for (int placement = 0; placement < order.Count; placement++)
            {
                int index = order[placement];
                EnemyStats enemy = enemies[index];
                int bestScore = int.MaxValue;
                int bestRow = -1;
                int bestColumn = -1;
                for (int row = 0; row <= rowBudget - enemy.GridDepth; row++)
                {
                    // All presets retain an initial attacker, including RearLoaded.
                    if (placement == 0 && row != 0) continue;
                    for (int column = 0; column <= EnemyFormationLayout.Columns - enemy.GridWidth; column++)
                    {
                        if (!CanPlace(occupied, row, column, enemy.GridWidth, enemy.GridDepth)) continue;
                        int score = Score(preset, rowLoads, rowBudget, row, column, enemy.GridWidth, enemy.GridDepth);
                        if (score >= bestScore) continue;
                        bestScore = score;
                        bestRow = row;
                        bestColumn = column;
                    }
                }
                if (bestRow < 0) return false;
                rows[index] = bestRow;
                columns[index] = bestColumn;
                for (int row = bestRow; row < bestRow + enemy.GridDepth; row++)
                {
                    rowLoads[row] += enemy.GridWidth;
                    for (int column = bestColumn; column < bestColumn + enemy.GridWidth; column++)
                        occupied[row, column] = true;
                }
            }
            return true;
        }

        private static int Score(CommonFormationPreset preset, int[] loads, int rowBudget,
            int row, int column, int width, int depth)
        {
            int edgeDistance = Math.Min(column, EnemyFormationLayout.Columns - width - column);
            int centerDistance = Math.Abs(2 * column + width - EnemyFormationLayout.Columns);
            // Wide actors touch an edge first, preserving a contiguous remainder
            // (in particular a 3+2 row). Singles then use the central gaps.
            int horizontal = (width > 1 ? edgeDistance : centerDistance) * 10 + column;
            switch (preset)
            {
                case CommonFormationPreset.Balanced:
                    int load = 0;
                    for (int r = row; r < row + depth; r++) load += loads[r];
                    // Offset a single-cell actor's preferred center by half a
                    // cell, then mirror ties on alternate rows. Larger actors
                    // still touch an edge first, leaving a contiguous remainder
                    // for a 3+2 pair rather than creating an unfillable gap.
                    int rowCenter = EnemyFormationLayout.Columns + ((row & 1) == 0 ? -1 : 1);
                    int balancedDistance = width > 1 ? edgeDistance : Math.Abs(2 * column + width - rowCenter);
                    int mirroredColumn = (row & 1) == 0 ? column : EnemyFormationLayout.Columns - width - column;
                    int balancedHorizontal = balancedDistance * 10 + mirroredColumn;
                    return load * 10000 + row * 100 + balancedHorizontal;
                case CommonFormationPreset.FrontLoaded:
                    return row * 100 + horizontal;
                case CommonFormationPreset.RearLoaded:
                    return (rowBudget - depth - row) * 100 + horizontal;
                case CommonFormationPreset.Flanks:
                    return edgeDistance * 10000 + row * 100 + column;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        private static bool CanPlace(bool[,] occupied, int row, int column, int width, int depth)
        {
            for (int r = row; r < row + depth; r++)
                for (int c = column; c < column + width; c++)
                    if (occupied[r, c]) return false;
            return true;
        }

        // Presets choose vertical pressure and initial horizontal preferences.
        // Coverage is a shared constraint rather than another preset: no whole
        // visible lane should remain empty merely because Flanks prefers edges.
        // Count each actor's width once per intersecting window, so a lone deep
        // actor is not mistaken for several units that could fill five columns.
        private static void CloseEmptyLanes(IReadOnlyList<EnemyStats> enemies,
            List<int> order, int[] rows, int[] columns, int rowBudget)
        {
            int lastRow = 0;
            foreach (int index in order)
                lastRow = Math.Max(lastRow, rows[index] + enemies[index].GridDepth);
            int windowCount = Math.Max(1, lastRow - EnemyFormationLayout.VisibleRows + 1);
            var occupied = new bool[rowBudget, EnemyFormationLayout.Columns];
            var coverage = new int[windowCount, EnemyFormationLayout.Columns];
            var desired = new int[windowCount];
            foreach (int index in order)
            {
                EnemyStats enemy = enemies[index];
                SetOccupied(occupied, rows[index], columns[index], enemy, true);
                AdjustCoverage(coverage, rows[index], columns[index], enemy, 1);
                for (int window = 0; window < windowCount; window++)
                    if (IntersectsWindow(rows[index], enemy.GridDepth, window))
                        desired[window] = Math.Min(EnemyFormationLayout.Columns,
                            desired[window] + enemy.GridWidth);
            }

            int deficit = CoverageCost(coverage, desired);
            // Every accepted move strictly reduces this nonnegative integer,
            // with column coverage preceding sparse internal-gap cleanup.
            // The five-column, ten-row bound limits the search; no RNG/repacking.
            while (deficit > 0)
            {
                int bestDeficit = deficit, bestIndex = -1, bestColumn = -1;
                int bestDistance = int.MaxValue;
                foreach (int index in order)
                {
                    EnemyStats enemy = enemies[index];
                    int row = rows[index], originalColumn = columns[index];
                    SetOccupied(occupied, row, originalColumn, enemy, false);
                    AdjustCoverage(coverage, row, originalColumn, enemy, -1);
                    for (int column = 0; column <= EnemyFormationLayout.Columns - enemy.GridWidth; column++)
                    {
                        if (column == originalColumn ||
                            !CanPlace(occupied, row, column, enemy.GridWidth, enemy.GridDepth)) continue;
                        AdjustCoverage(coverage, row, column, enemy, 1);
                        int candidateDeficit = CoverageCost(coverage, desired);
                        AdjustCoverage(coverage, row, column, enemy, -1);
                        int distance = Math.Abs(column - originalColumn);
                        if (candidateDeficit >= deficit || candidateDeficit > bestDeficit ||
                            (candidateDeficit == bestDeficit && distance >= bestDistance)) continue;
                        bestDeficit = candidateDeficit;
                        bestIndex = index; bestColumn = column; bestDistance = distance;
                    }
                    AdjustCoverage(coverage, row, originalColumn, enemy, 1);
                    SetOccupied(occupied, row, originalColumn, enemy, true);
                }
                if (bestIndex < 0) break; // Footprints can make a gap unavoidable.
                EnemyStats moving = enemies[bestIndex];
                SetOccupied(occupied, rows[bestIndex], columns[bestIndex], moving, false);
                AdjustCoverage(coverage, rows[bestIndex], columns[bestIndex], moving, -1);
                columns[bestIndex] = bestColumn;
                SetOccupied(occupied, rows[bestIndex], bestColumn, moving, true);
                AdjustCoverage(coverage, rows[bestIndex], bestColumn, moving, 1);
                deficit = bestDeficit;
            }
        }

        private static bool IntersectsWindow(int row, int depth, int window)
            => row < window + EnemyFormationLayout.VisibleRows && row + depth > window;

        private static void AdjustCoverage(int[,] coverage, int row, int column,
            EnemyStats enemy, int delta)
        {
            for (int window = 0; window < coverage.GetLength(0); window++)
                if (IntersectsWindow(row, enemy.GridDepth, window))
                    for (int c = column; c < column + enemy.GridWidth; c++)
                        coverage[window, c] += delta;
        }

        private static int CoverageCost(int[,] coverage, int[] desired)
        {
            int deficit = 0;
            // Losing one attainable column must cost more than every possible
            // sparse internal hole combined. Empty outer margins are free.
            int coverageWeight = EnemyFormationLayout.Columns * desired.Length + 1;
            for (int window = 0; window < desired.Length; window++)
            {
                int columns = 0, first = -1, last = -1;
                for (int column = 0; column < EnemyFormationLayout.Columns; column++)
                    if (coverage[window, column] > 0)
                    {
                        columns++;
                        if (first < 0) first = column;
                        last = column;
                    }
                deficit += (desired[window] - columns) * coverageWeight;
                if (columns > 0 && desired[window] < EnemyFormationLayout.Columns)
                    deficit += last - first + 1 - columns;
            }
            return deficit;
        }

        private static void SetOccupied(bool[,] occupied, int row, int column,
            EnemyStats enemy, bool value)
        {
            for (int r = row; r < row + enemy.GridDepth; r++)
                for (int c = column; c < column + enemy.GridWidth; c++)
                    occupied[r, c] = value;
        }
    }
}
