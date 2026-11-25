using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// A lossless cell from an authored encounter source matrix.  The list
    /// index remains the source-order identity of an enemy instance; repeated
    /// content IDs are therefore unambiguous.  These coordinates deliberately
    /// do not encode a gameplay inference such as "rear row".
    /// </summary>
    [Serializable]
    public sealed class EnemyFormationSourceSlot
    {
        [Min(0)] public int sourceRow;
        [Min(0)] public int sourceColumn;

        public EnemyFormationSourceSlot()
        {
        }

        public EnemyFormationSourceSlot(int sourceRow, int sourceColumn)
        {
            this.sourceRow = sourceRow;
            this.sourceColumn = sourceColumn;
        }
    }

    /// <summary>
    /// 敌人阵法的确定性占格器。row 表示怪物最靠近玩家的一排，
    /// gridDepth 从该排向后延伸；死亡与前排推进不会改变列关系。
    /// 普通敌人始终稳定保序并先于显式关卡 BOSS 落位，保证 BOSS 身后
    /// 不会遗留无法攻击玩家的小怪。显式源矩阵是例外：它保留外部证据中的
    /// 源顺序与格位，不能套用旧的 BOSS 尾置规则。
    ///
    /// 规模审查：本文件超过 1200 行，但仍是无可变状态的单一阵型几何模块；
    /// 自动、分波、逐行、图案、补位与震退均共享同一套占格、碰撞和可见区
    /// 不变量。本次完整入场只扩展占格资格与按体型计算的震退返回边界，
    /// 复用矩形碰撞和单步推进；空场恢复时机由 EnemyBackfillCoordinator
    /// 独占，不在此增加调度状态或职责。暂保留该几何模块；
    /// 若新增另一套棋盘拓扑、独立可变生命周期或新推进策略，必须
    /// 在不泄漏 occupied/占格细节的前提下重新划分深模块。
    /// </summary>
    public static class EnemyFormationLayout
    {
        private const string SourceMatrixPatternPrefix = "source-v1:";

        /// <summary>战斗阵法的固定横向格数。玩法与表现均从这里读取。</summary>
        public const int Columns = 5;

        /// <summary>玩家当前能看见的纵向排数；更后的排仍可参与等待队列。</summary>
        public const int VisibleRows = 3;

        /// <summary>隐藏区允许的最大排数；击退可推入隐藏区但不能超出此上限。</summary>
        public const int HiddenRowLimit = 10;

        /// <summary>
        /// 为敌人确定性分配矩形占格。调用方只提供宽和深，不需要了解碰撞细节。
        /// 普通敌人稳定保序并先落位；显式关卡 BOSS 最后落位且横向居中。
        /// </summary>
        public static void Assign(IList<EnemyStats> enemies)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            ClearSourceFormationMetadata(enemies);

            var occupied = new HashSet<long>();
            List<int> deploymentOrder = BuildLevelBossLastDeploymentOrder(enemies);
            int bossStartRow = 0;
            for (int orderIndex = 0;
                 orderIndex < deploymentOrder.Count;
                 orderIndex++)
            {
                EnemyStats enemy = enemies[deploymentOrder[orderIndex]];
                if (IsLevelBoss(enemy))
                {
                    PlaceAtCenterColumn(
                        enemy,
                        occupied,
                        bossStartRow);
                    bossStartRow = Math.Max(
                        bossStartRow,
                        enemy.row + enemy.GridDepth);
                    continue;
                }

                PlaceAtFirstAvailableCell(enemy, occupied, startRow: 0);
                bossStartRow = Math.Max(
                    bossStartRow,
                    enemy.row + enemy.GridDepth);
            }

            CenterRows(enemies);
        }

        /// <summary>
        /// 分波布阵。第 n 波从 row n 开始紧凑落位；普通敌人跨波稳定保序，
        /// 显式关卡 BOSS 统一占据最后部署槽并落在所有其他敌人之后。波次大小总和
        /// 必须等于敌人数量。空列表时退化为 <see cref="Assign"/>。
        /// </summary>
        public static void AssignWaves(
            IList<EnemyStats> enemies,
            IReadOnlyList<int> waveSizes)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            if (waveSizes == null || waveSizes.Count == 0)
            {
                Assign(enemies);
                for (int i = 0; i < enemies.Count; i++)
                    enemies[i].spawnWaveIndex = 0;
                return;
            }

            int total = 0;
            for (int i = 0; i < waveSizes.Count; i++)
            {
                if (waveSizes[i] <= 0)
                    throw new ArgumentException(
                        $"波次 {i} 的大小必须为正数，当前为 {waveSizes[i]}。",
                        nameof(waveSizes));
                total += waveSizes[i];
            }
            if (total != enemies.Count)
                throw new ArgumentException(
                    $"波次大小总和 ({total}) 不等于敌人数量 ({enemies.Count})。",
                    nameof(waveSizes));

            ClearSourceFormationMetadata(enemies);
            var occupied = new HashSet<long>();
            List<int> deploymentOrder = BuildLevelBossLastDeploymentOrder(enemies);
            int deploymentIndex = 0;
            int bossStartRow = 0;
            for (int wave = 0; wave < waveSizes.Count; wave++)
            {
                int waveSize = waveSizes[wave];
                // 波次逐排紧凑堆叠：wave n 从第 n 排起放（0=最前排）。
                // 前三排（VisibleRows 内）全部可见；只有超过第三排的批次隐藏。
                int startRow = wave;

                for (int i = 0; i < waveSize; i++)
                {
                    int index = deploymentOrder[deploymentIndex++];
                    EnemyStats enemy = enemies[index];
                    enemy.spawnWaveIndex = wave;
                    if (IsLevelBoss(enemy))
                    {
                        PlaceAtCenterColumn(
                            enemy,
                            occupied,
                            Math.Max(startRow, bossStartRow));
                        bossStartRow = Math.Max(
                            bossStartRow,
                            enemy.row + enemy.GridDepth);
                        continue;
                    }

                    PlaceAtFirstAvailableCell(enemy, occupied, startRow);
                    bossStartRow = Math.Max(
                        bossStartRow,
                        enemy.row + enemy.GridDepth);
                }
            }

            CenterRows(enemies);
        }

        /// <summary>
        /// 按行布阵。rowCounts[i] 为第 i 排（0=最前排）的怪物数量，0 表示该排整体留空。
        /// 普通敌人稳定保序、关卡 BOSS 尾置后，按行从左到右放置并整体居中。
        /// 总和必须等于敌人数量。
        /// 本布阵不分波：全部敌人 spawnWaveIndex=0，供非满编教学布阵（如 [3,3]、[5,0,3]）使用。
        /// 含 BOSS 时必须为其预留在所有普通敌人完整占格之后的独立后排。
        /// </summary>
        public static void AssignByRows(
            IList<EnemyStats> enemies,
            IReadOnlyList<int> rowCounts)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (rowCounts == null || rowCounts.Count == 0)
                throw new ArgumentException(
                    "行数量列表不能为空。", nameof(rowCounts));

            int total = 0;
            for (int i = 0; i < rowCounts.Count; i++)
            {
                if (rowCounts[i] < 0)
                    throw new ArgumentException(
                        $"第 {i} 排的数量不能为负数，当前为 {rowCounts[i]}。",
                        nameof(rowCounts));
                total += rowCounts[i];
            }
            if (total != enemies.Count)
                throw new ArgumentException(
                    $"行数量总和 ({total}) 不等于敌人数量 ({enemies.Count})。",
                    nameof(rowCounts));

            ClearSourceFormationMetadata(enemies);
            var occupied = new HashSet<long>();
            List<int> deploymentOrder = BuildLevelBossLastDeploymentOrder(enemies);
            int deploymentIndex = 0;
            int bossStartRow = 0;
            for (int row = 0; row < rowCounts.Count; row++)
            {
                for (int i = 0; i < rowCounts[row]; i++)
                {
                    int enemyIndex = deploymentOrder[deploymentIndex++];
                    EnemyStats enemy = enemies[enemyIndex];
                    if (IsLevelBoss(enemy) && row < bossStartRow)
                    {
                        throw new ArgumentException(
                            $"BOSS {enemy.contentId ?? enemy.name} 必须位于" +
                            $"所有普通敌人之后：配置行为 {row}，" +
                            $"最早允许行为 {bossStartRow}。",
                            nameof(rowCounts));
                    }
                    enemy.spawnWaveIndex = 0;

                    int width = enemy.GridWidth;
                    int depth = enemy.GridDepth;
                    EnsureValidFootprint(width, depth,
                        $"敌人 {enemy.contentId ?? enemy.name}");

                    bool placed = false;
                    for (int column = 0; column <= Columns - width; column++)
                    {
                        if (CanOccupy(occupied, row, column, width, depth))
                        {
                            enemy.row = row;
                            enemy.column = column;
                            enemy.gridWidth = width;
                            enemy.gridDepth = depth;
                            MarkOccupied(occupied, row, column, width, depth);
                            placed = true;
                            break;
                        }
                    }
                    if (!placed)
                        throw new ArgumentException(
                            $"第 {row} 排容纳不下第 {enemyIndex + 1} 只敌人" +
                            $"（{enemy.contentId ?? enemy.name}）。",
                            nameof(rowCounts));
                    bossStartRow = Math.Max(
                        bossStartRow,
                        enemy.row + enemy.GridDepth);
                }
            }

            CenterRows(enemies);
        }

        /// <summary>
        /// 按棋盘图案布阵。pattern 行从前到后以 “/” 分隔（如
        /// “10101/01010/11111”），1=该格有怪，0=留空；行内从左到右对应列。
        /// 图案中的 1 总数必须等于敌人数量；普通敌人稳定保序后
        /// 先按行优先顺序放入，BOSS 占用最后的图案格。
        /// 本布阵不分波：全部敌人 spawnWaveIndex=0，供需要显式列位置的
        /// 教学布阵（如 W5 棋盘阵）使用。
        /// 含 BOSS 时，其配置格必须位于所有普通敌人完整占格之后。
        /// </summary>
        public static void AssignByPattern(
            IList<EnemyStats> enemies,
            string pattern)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            if (TryParseSourceMatrixPattern(
                    pattern,
                    enemies.Count,
                    out int sourceColumnCount,
                    out List<EnemyFormationSourceSlot> sourceSlots,
                    out string sourceError))
            {
                AssignByExplicitSourceSlots(
                    enemies,
                    sourceColumnCount,
                    sourceSlots);
                return;
            }
            if (IsSourceMatrixPattern(pattern))
                throw new ArgumentException(sourceError, nameof(pattern));

            if (!TryParsePatternCells(
                    pattern,
                    enemies.Count,
                    out List<Vector2Int> cells,
                    out string error))
            {
                throw new ArgumentException(error, nameof(pattern));
            }

            ClearSourceFormationMetadata(enemies);
            var occupied = new HashSet<long>();
            List<int> deploymentOrder = BuildLevelBossLastDeploymentOrder(enemies);
            int bossStartRow = 0;
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                int enemyIndex = deploymentOrder[cellIndex];
                EnemyStats enemy = enemies[enemyIndex];

                int width = enemy.GridWidth;
                int depth = enemy.GridDepth;
                EnsureValidFootprint(width, depth,
                    $"敌人 {enemy.contentId ?? enemy.name}");
                int row = cells[cellIndex].y;
                int column = cells[cellIndex].x;
                if (IsLevelBoss(enemy) && row < bossStartRow)
                {
                    throw new ArgumentException(
                        $"BOSS {enemy.contentId ?? enemy.name} 必须位于" +
                        $"所有普通敌人之后：配置行为 {row}，" +
                        $"最早允许行为 {bossStartRow}。",
                        nameof(pattern));
                }
                if (column + width > Columns)
                    throw new ArgumentException(
                        $"第 {row} 排第 {column} 列容纳不下敌人" +
                        $"（{enemy.contentId ?? enemy.name}）的横向占格。",
                        nameof(pattern));
                if (!CanOccupy(occupied, row, column, width, depth))
                    throw new ArgumentException(
                        $"第 {row} 排第 {column} 列与已有占格重叠" +
                        $"（{enemy.contentId ?? enemy.name}）。",
                        nameof(pattern));

                enemy.spawnWaveIndex = 0;
                enemy.row = row;
                enemy.column = column;
                enemy.gridWidth = width;
                enemy.gridDepth = depth;
                MarkOccupied(occupied, row, column, width, depth);
                bossStartRow = Math.Max(
                    bossStartRow,
                    enemy.row + enemy.GridDepth);
            }
        }

        /// <summary>
        /// Assigns an authored source matrix without reordering level bosses.
        /// Unlike legacy automatic/row/pattern layouts, the caller's list index
        /// maps directly to the corresponding source slot. This is the sole
        /// exception to the historical "level boss goes last" policy: a source
        /// matrix is evidence about each actor's original position.
        ///
        /// The source grid is logical evidence, not yet proof that either axis
        /// maps 1:1 to this project's playable grid. Its cells also describe
        /// units uniformly, while this project permits wider/deeper enemies.
        /// Therefore the raw indices are retained on each enemy and one stable
        /// adapter lays physical footprints in source-list order. A later
        /// visual confirmation changes that adapter in one place, never the
        /// authored source slots.
        /// </summary>
        public const int SourcePlanAdapterVersion = 1;

        public static void AssignByExplicitSourceSlots(
            IList<EnemyStats> enemies, int sourceColumnCount,
            IReadOnlyList<EnemyFormationSourceSlot> sourceSlots)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            var identities = new List<EnemyStats>(enemies);
            CreateSourcePlan(identities, sourceColumnCount, sourceSlots).ApplyTo(enemies);
        }

        /// <summary>
        /// Creates the physical plan without changing live actors. The source
        /// first axis is column, second is row; both remain logical evidence.
        /// Footprint packing/centering uses the existing single adapter below.
        /// </summary>
        public static EnemyFormationPlan CreateFromFrozenRoster(
            IReadOnlyList<EnemyStats> enemies, CommonEncounterFrozenRoster roster)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (enemies.Count != roster.EnemyContentIds.Count)
                throw new ArgumentException("Frozen roster and actor count differ.", nameof(enemies));
            var slots = new EnemyFormationSourceSlot[enemies.Count];
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null || !string.Equals(enemies[i].contentId,
                    roster.EnemyContentIds[i], StringComparison.Ordinal))
                    throw new ArgumentException("Frozen actor order or identity differs.", nameof(enemies));
                CommonEncounterRosterEntry slot = roster.SourceSlots[i];
                slots[i] = new EnemyFormationSourceSlot(slot.SecondAxis, slot.FirstAxis);
            }
            return CreateSourcePlan(enemies, roster.FirstAxisLength, slots);
        }

        private static EnemyFormationPlan CreateSourcePlan(
            IReadOnlyList<EnemyStats> enemies, int sourceColumnCount,
            IReadOnlyList<EnemyFormationSourceSlot> sourceSlots)
        {
            var identities = new List<EnemyStats>(enemies);
            var unique = new HashSet<EnemyStats>();
            foreach (EnemyStats actor in identities)
                if (actor == null || !unique.Add(actor))
                    throw new ArgumentException("Source formation requires distinct non-null actor instances.", nameof(enemies));
            ValidateSourceSlots(identities, sourceColumnCount, sourceSlots, out string validationError);
            if (validationError != null) throw new ArgumentException(validationError, nameof(sourceSlots));
            // Geometry-only scratch objects have no runtime identity, behavior,
            // art or card resources. They never become actors and allocate no IDs.
            var scratch = new EnemyStats[enemies.Count];
            for (int i = 0; i < scratch.Length; i++)
                scratch[i] = new EnemyStats(null, 1, contentId: enemies[i].contentId,
                    gridWidth: enemies[i].GridWidth, gridDepth: enemies[i].GridDepth);
            PackSourceFootprints(scratch);
            var rows = new int[scratch.Length];
            var columns = new int[scratch.Length];
            for (int i = 0; i < scratch.Length; i++)
            { rows[i] = scratch[i].row; columns[i] = scratch[i].column; }
            return new EnemyFormationPlan(enemies, default, rows, columns, sourceSlots, sourceColumnCount);
        }

        private static void PackSourceFootprints(IList<EnemyStats> enemies)
        {
            var occupied = new HashSet<long>();
            for (int index = 0; index < enemies.Count; index++)
                PlaceAtFirstAvailableCell(enemies[index], occupied, startRow: 0);
            CenterRows(enemies);
        }

        /// <summary>
        /// Encodes an explicit source matrix for legacy call sites that only
        /// accept <c>enemyFormationPattern</c>.  The serialized author fields
        /// remain column count plus slots; this string is an internal transport
        /// format, not a second source of truth.
        /// </summary>
        public static string EncodeSourceMatrixPattern(
            int sourceColumnCount,
            IReadOnlyList<EnemyFormationSourceSlot> sourceSlots)
        {
            ValidateSourceSlots(
                null,
                sourceColumnCount,
                sourceSlots,
                out string validationError);
            if (validationError != null)
                throw new ArgumentException(validationError, nameof(sourceSlots));

            var encodedSlots = new string[sourceSlots.Count];
            for (int i = 0; i < sourceSlots.Count; i++)
            {
                EnemyFormationSourceSlot slot = sourceSlots[i];
                encodedSlots[i] = string.Concat(
                    slot.sourceRow.ToString(CultureInfo.InvariantCulture),
                    ",",
                    slot.sourceColumn.ToString(CultureInfo.InvariantCulture));
            }
            return string.Concat(
                SourceMatrixPatternPrefix,
                sourceColumnCount.ToString(CultureInfo.InvariantCulture),
                ":",
                string.Join(";", encodedSlots));
        }

        /// <summary>
        /// 返回稳定部署顺序：其他敌人的相对顺序不变，随后才是显式层/关 BOSS；
        /// 列表本身不重排，避免改变运行时身份。
        /// </summary>
        private static List<int> BuildLevelBossLastDeploymentOrder(
            IList<EnemyStats> enemies)
        {
            var order = new List<int>(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null)
                    throw new ArgumentException(
                        $"敌人列表第 {i} 项为空。",
                        nameof(enemies));
                if (!IsLevelBoss(enemy)) order.Add(i);
            }
            for (int i = 0; i < enemies.Count; i++)
            {
                if (IsLevelBoss(enemies[i])) order.Add(i);
            }
            return order;
        }

        private static void ClearSourceFormationMetadata(
            IList<EnemyStats> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null) continue;
                enemy.sourceFormationRow = -1;
                enemy.sourceFormationColumn = -1;
                enemy.sourceFormationColumnCount = 0;
            }
        }

        private static bool IsLevelBoss(EnemyStats enemy)
            => enemy != null && enemy.isBoss &&
                enemy.bossRank != BossRank.None;

        /// <summary>从指定排开始，按行列顺序把普通敌人放入首个空位。</summary>
        private static void PlaceAtFirstAvailableCell(
            EnemyStats enemy,
            ISet<long> occupied,
            int startRow)
        {
            int width = enemy.GridWidth;
            int depth = enemy.GridDepth;
            EnsureValidFootprint(width, depth,
                $"敌人 {enemy.contentId ?? enemy.name}");

            for (int row = startRow; ; row++)
            {
                for (int column = 0;
                     column <= Columns - width;
                     column++)
                {
                    if (!CanOccupy(
                            occupied,
                            row,
                            column,
                            width,
                            depth))
                    {
                        continue;
                    }

                    enemy.row = row;
                    enemy.column = column;
                    enemy.gridWidth = width;
                    enemy.gridDepth = depth;
                    MarkOccupied(
                        occupied,
                        row,
                        column,
                        width,
                        depth);
                    return;
                }
            }
        }

        /// <summary>解析棋盘图案为敌人占格坐标（x=列，y=排，0=最前排）。</summary>
        public static bool TryParsePatternCells(
            string pattern,
            int enemyCount,
            out List<Vector2Int> cells,
            out string error)
        {
            cells = null;
            if (TryParseSourceMatrixPattern(
                    pattern,
                    enemyCount,
                    out _,
                    out List<EnemyFormationSourceSlot> sourceSlots,
                    out string sourceError))
            {
                cells = new List<Vector2Int>(sourceSlots.Count);
                for (int i = 0; i < sourceSlots.Count; i++)
                {
                    EnemyFormationSourceSlot slot = sourceSlots[i];
                    cells.Add(new Vector2Int(
                        slot.sourceColumn,
                        slot.sourceRow));
                }
                error = null;
                return true;
            }
            if (IsSourceMatrixPattern(pattern))
            {
                error = sourceError;
                return false;
            }
            if (string.IsNullOrWhiteSpace(pattern))
            {
                error = "棋盘布阵图案不能为空。";
                return false;
            }

            string[] rows = pattern.Split('/');
            var parsed = new List<Vector2Int>();
            for (int row = 0; row < rows.Length; row++)
            {
                if (rows[row].Length > Columns)
                {
                    error = $"棋盘布阵第 {row} 排长度 ({rows[row].Length}) " +
                            $"超过列数 ({Columns})。";
                    return false;
                }
                for (int column = 0; column < rows[row].Length; column++)
                {
                    char c = rows[row][column];
                    if (c == '0') continue;
                    if (c != '1')
                    {
                        error = $"棋盘布阵只允许 0/1，第 {row} 排第 {column} " +
                                $"位为 “{c}”。";
                        return false;
                    }
                    parsed.Add(new Vector2Int(column, row));
                }
            }
            if (parsed.Count != enemyCount)
            {
                error = $"棋盘布阵的怪数 ({parsed.Count}) 不等于敌人数量 " +
                        $"({enemyCount})。";
                return false;
            }
            if (rows.Length > HiddenRowLimit)
            {
                error = $"棋盘布阵排数 ({rows.Length}) 超过上限 " +
                        $"{HiddenRowLimit}。";
                return false;
            }
            cells = parsed;
            error = null;
            return true;
        }

        private static bool IsSourceMatrixPattern(string pattern)
            => !string.IsNullOrWhiteSpace(pattern) &&
               pattern.TrimStart().StartsWith(
                   SourceMatrixPatternPrefix,
                   StringComparison.Ordinal);

        private static bool TryParseSourceMatrixPattern(
            string pattern,
            int enemyCount,
            out int sourceColumnCount,
            out List<EnemyFormationSourceSlot> sourceSlots,
            out string error)
        {
            sourceColumnCount = 0;
            sourceSlots = null;
            error = null;
            if (!IsSourceMatrixPattern(pattern))
                return false;

            string body = pattern.Trim().Substring(
                SourceMatrixPatternPrefix.Length);
            string[] parts = body.Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out sourceColumnCount))
            {
                error = "源矩阵格式必须为 source-v1:<列数>:<行,列;...>。";
                return false;
            }

            string[] encodedSlots = string.IsNullOrWhiteSpace(parts[1])
                ? Array.Empty<string>()
                : parts[1].Split(';');
            var parsed = new List<EnemyFormationSourceSlot>(
                encodedSlots.Length);
            for (int index = 0; index < encodedSlots.Length; index++)
            {
                string[] coordinates = encodedSlots[index].Split(',');
                if (coordinates.Length != 2 ||
                    !int.TryParse(
                        coordinates[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int row) ||
                    !int.TryParse(
                        coordinates[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int column))
                {
                    error = $"源矩阵第 {index} 个格位格式无效。";
                    return false;
                }
                parsed.Add(new EnemyFormationSourceSlot(row, column));
            }

            if (!ValidateSourceSlots(
                    null,
                    sourceColumnCount,
                    parsed,
                    out error))
            {
                return false;
            }
            if (parsed.Count != enemyCount)
            {
                error = $"源矩阵的怪数 ({parsed.Count}) 不等于敌人数量 " +
                    $"({enemyCount})。";
                return false;
            }

            sourceSlots = parsed;
            error = null;
            return true;
        }

        private static bool ValidateSourceSlots(
            IList<EnemyStats> enemies,
            int sourceColumnCount,
            IReadOnlyList<EnemyFormationSourceSlot> sourceSlots,
            out string error)
        {
            if (sourceColumnCount <= 0)
            {
                error = $"源矩阵列数必须大于 0，当前为 {sourceColumnCount}。";
                return false;
            }
            if (sourceSlots == null || sourceSlots.Count == 0)
            {
                error = "源矩阵格位不能为空。";
                return false;
            }
            if (enemies != null && sourceSlots.Count != enemies.Count)
            {
                error = $"源矩阵格位数量 ({sourceSlots.Count}) 不等于敌人数量 " +
                    $"({enemies.Count})。";
                return false;
            }

            var sourceCells = new HashSet<long>();
            for (int i = 0; i < sourceSlots.Count; i++)
            {
                EnemyFormationSourceSlot slot = sourceSlots[i];
                if (slot == null)
                {
                    error = $"源矩阵格位 {i} 不能为空。";
                    return false;
                }
                if (slot.sourceRow < 0 ||
                    slot.sourceColumn < 0 ||
                    slot.sourceColumn >= sourceColumnCount)
                {
                    error = $"源矩阵格位 {i} 越界：" +
                        $"({slot.sourceRow},{slot.sourceColumn})。";
                    return false;
                }
                long key = ((long)slot.sourceRow << 32) |
                    (uint)slot.sourceColumn;
                if (!sourceCells.Add(key))
                {
                    error = $"源矩阵格位重复：" +
                        $"({slot.sourceRow},{slot.sourceColumn})。";
                    return false;
                }
                if (enemies != null && enemies[i] == null)
                {
                    error = $"敌人列表第 {i} 项为空。";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 将指定敌人从 startRow 起按“中心列优先”的顺序放入首个可行位置，
        /// 保证 BOSS 横向居中出现；中心被占时向两侧扩散寻找。
        /// </summary>
        private static void PlaceAtCenterColumn(
            EnemyStats enemy,
            ISet<long> occupied,
            int startRow)
        {
            int width = enemy.GridWidth;
            int depth = enemy.GridDepth;
            EnsureValidFootprint(width, depth,
                $"敌人 {enemy.contentId ?? enemy.name}");

            for (int row = startRow; ; row++)
            {
                foreach (int column in CenterOrderedColumns(width))
                {
                    if (!CanOccupy(occupied, row, column, width, depth))
                        continue;
                    enemy.row = row;
                    enemy.column = column;
                    enemy.gridWidth = width;
                    enemy.gridDepth = depth;
                    MarkOccupied(occupied, row, column, width, depth);
                    return;
                }
            }
        }

        /// <summary>宽度 width 的候选列，按离中心由近到远排序。</summary>
        private static IEnumerable<int> CenterOrderedColumns(int width)
        {
            int center = (Columns - width) / 2;
            yield return center;
            for (int offset = 1; ; offset++)
            {
                bool any = false;
                if (center - offset >= 0)
                {
                    yield return center - offset;
                    any = true;
                }
                if (center + offset + width <= Columns)
                {
                    yield return center + offset;
                    any = true;
                }
                if (!any) yield break;
            }
        }

        /// <summary>
        /// 将每排的敌人横向居中。当一排敌人总占格不足 Columns 时，
        /// 整体右移使排列居中。仅在初始化布阵后调用，不影响死亡补位逻辑。
        /// 自动布阵的 BOSS 落位时已经居中；显式按行布阵的独立 BOSS 排
        /// 也在这里按整排统一居中。
        /// 平移必须保证不与任何其他存活敌人（含跨行占格）重叠，否则收缩。
        /// </summary>
        private static void CenterRows(IList<EnemyStats> enemies)
        {
            var byRow = new Dictionary<int, List<int>>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats e = enemies[i];
                if (e == null) continue;
                if (!byRow.TryGetValue(e.row, out var list))
                {
                    list = new List<int>();
                    byRow[e.row] = list;
                }
                list.Add(i);
            }

            foreach (var kvp in byRow)
            {
                var indices = kvp.Value;
                if (indices.Count == 0) continue;
                int row = kvp.Key;

                int leftmost = int.MaxValue;
                int rightmost = int.MinValue;
                foreach (int idx in indices)
                {
                    EnemyStats e = enemies[idx];
                    leftmost = Math.Min(leftmost, e.column);
                    rightmost = Math.Max(rightmost, e.column + e.GridWidth);
                }

                int span = rightmost - leftmost;
                if (span >= Columns) continue;

                int preferred = (Columns - span) / 2 - leftmost;
                if (preferred == 0) continue;

                // 优先期望位移；若与跨行占格冲突，则向原位收缩，
                // 直到找到安全位移；全部冲突则保持原位。
                int shift = preferred;
                while (shift != 0 &&
                       !IsSafeRowShift(enemies, indices, row, shift))
                {
                    shift -= Math.Sign(preferred);
                }
                if (shift == 0) continue;

                foreach (int idx in indices)
                    enemies[idx].column += shift;
            }
        }

        /// <summary>
        /// 检查把 row 行的本排敌人整体平移 shift 列后，是否会越界或与
        /// 跨行敌人（row 落在其 GridDepth 范围内）的占格重叠。
        /// </summary>
        /// <summary>
        /// 检查把本排敌人整体平移 shift 列后，是否会越界或与任何非本排
        /// 敌人的完整矩形占格重叠（含跨行敌人延伸到其他排的部分）。
        /// </summary>
        private static bool IsSafeRowShift(
            IList<EnemyStats> enemies,
            IReadOnlyList<int> indices,
            int row,
            int shift)
        {
            var selfIndices = new HashSet<int>(indices);
            foreach (int idx in indices)
            {
                EnemyStats e = enemies[idx];
                int shiftedColumn = e.column + shift;
                if (shiftedColumn < 0 ||
                    shiftedColumn + e.GridWidth > Columns)
                {
                    return false;
                }

                for (int j = 0; j < enemies.Count; j++)
                {
                    if (selfIndices.Contains(j)) continue;
                    EnemyStats other = enemies[j];
                    if (other == null) continue;
                    if (shiftedColumn < other.column + other.GridWidth &&
                        shiftedColumn + e.GridWidth > other.column &&
                        e.row < other.row + other.GridDepth &&
                        e.row + e.GridDepth > other.row)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 校验矩形占格是否能放入五列三排的可见阵法。后排队列可以无限延伸，
        /// 但单只怪物的纵深不能超过可见排数，否则永远无法完整进入战场。
        /// </summary>
        public static bool TryValidateFootprint(
            int width,
            int depth,
            out string error)
        {
            if (width < 1 || width > Columns)
            {
                error = $"横向占格必须在 1 到 {Columns} 之间，当前为 {width}。";
                return false;
            }

            if (depth < 1 || depth > VisibleRows)
            {
                error = $"纵向占格必须在 1 到 {VisibleRows} 之间，当前为 {depth}。";
                return false;
            }

            error = null;
            return true;
        }

        public static void EnsureValidFootprint(
            int width,
            int depth,
            string context = null)
        {
            if (TryValidateFootprint(width, depth, out string error))
                return;

            string prefix = string.IsNullOrWhiteSpace(context)
                ? string.Empty
                : context + "：";
            string parameterName = width < 1 || width > Columns
                ? nameof(width)
                : nameof(depth);
            throw new ArgumentOutOfRangeException(
                parameterName,
                prefix + error);
        }

        /// <summary>
        /// 返回怪物占格与前三排相交的纵向格数，仅作几何查询。
        /// 相交不代表已经入场；表现、选敌和行动应使用完整入场资格。
        /// </summary>
        public static int GetVisibleDepth(EnemyStats enemy)
        {
            if (enemy == null || !enemy.IsDeployed) return 0;

            int firstRow = Math.Max(0, enemy.row);
            int lastRow = Math.Min(
                VisibleRows,
                enemy.row + enemy.GridDepth);
            return Math.Max(0, lastRow - firstRow);
        }

        public static bool IntersectsVisibleRows(EnemyStats enemy)
        {
            return GetVisibleDepth(enemy) > 0;
        }

        /// <summary>
        /// 已部署敌人的完整纵向占格必须落入前三排，才可显示、被选中或行动。
        /// 不包含生死判断，调用方仍应按自身用途排除死亡实体。
        /// </summary>
        public static bool IsFullyInsideVisibleRows(EnemyStats enemy)
            => enemy != null && enemy.IsDeployed && enemy.row >= 0 &&
                enemy.row <= VisibleRows - enemy.GridDepth;

        /// <summary>
        /// 判断敌人是否为其占用列中最靠前的存活者（“每列最前”）。
        /// 玩家攻击资格以本判定为基础；棋盘布阵中某列最前可能在第二排，
        /// 但只有第一排敌人才具备攻击玩家的距离资格。
        /// </summary>
        public static bool IsColumnFront(List<EnemyStats> enemies, int index)
            => IsColumnFrontCore(enemies.Count, i => enemies[i], index);

        public static bool IsColumnFront(IList<EnemyStats> enemies, int index)
            => IsColumnFrontCore(enemies.Count, i => enemies[i], index);

        public static bool IsColumnFront(
            IReadOnlyList<EnemyStats> enemies,
            int index)
            => IsColumnFrontCore(enemies.Count, i => enemies[i], index);

        private static bool IsColumnFrontCore(
            int enemyCount, Func<int, EnemyStats> getEnemy, int index)
        {
            if (index < 0 || index >= enemyCount)
                return false;
            EnemyStats enemy = getEnemy(index);
            if (enemy == null || enemy.IsDead ||
                !IsFullyInsideVisibleRows(enemy))
            {
                return false;
            }

            for (int j = 0; j < enemyCount; j++)
            {
                if (j == index) continue;
                EnemyStats other = getEnemy(j);
                if (other == null || other.IsDead ||
                    !IsFullyInsideVisibleRows(other))
                {
                    continue;
                }
                if (other.row >= enemy.row) continue;
                if (other.column >= enemy.column + enemy.GridWidth ||
                    other.column + other.GridWidth <= enemy.column)
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>只有占据第一排的可见存活敌人才可以攻击玩家。</summary>
        public static bool CanAttackPlayer(EnemyStats enemy)
            => enemy != null && !enemy.IsDead && enemy.row == 0 &&
                IsFullyInsideVisibleRows(enemy);

        /// <summary>
        /// 玩家自动选敌使用的威胁排。刚从后排补到第一排的敌人仍按原排
        /// 处理到本回合结束，避免它越过尚未结算的原前排目标。
        /// </summary>
        public static int GetPlayerTargetPriorityRow(EnemyStats enemy)
        {
            if (enemy == null)
                return int.MaxValue;
            return enemy.row + (enemy.justFilledFrontRow ? 1 : 0);
        }

        /// <summary>
        /// 将已满足资格的关 BOSS 部署到前排中央。资格与时机由
        /// <see cref="LevelBossEncounter"/> 独占；本方法只封装占格几何。
        /// </summary>
        public static void DeployBossAtFrontCenter(
            IList<EnemyStats> enemies,
            int bossIndex)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (bossIndex < 0 || bossIndex >= enemies.Count)
                throw new ArgumentOutOfRangeException(nameof(bossIndex));

            EnemyStats boss = enemies[bossIndex];
            if (boss == null || !boss.isBoss ||
                boss.bossRank != BossRank.Stage || boss.IsDead)
                throw new InvalidOperationException(
                    "只能降临部署存活的关 BOSS。");

            int targetColumn = (Columns - boss.GridWidth) / 2;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (i == bossIndex) continue;
                EnemyStats blocker = enemies[i];
                if (blocker == null || blocker.IsDead ||
                    !blocker.IsDeployed)
                {
                    continue;
                }
                if (RectanglesOverlap(
                        targetColumn,
                        0,
                        boss.GridWidth,
                        boss.GridDepth,
                        blocker.column,
                        blocker.row,
                        blocker.GridWidth,
                        blocker.GridDepth))
                {
                    throw new InvalidOperationException(
                        "关 BOSS 入场时前排中央仍被存活敌人占用。");
                }
            }

            boss.row = 0;
            boss.column = targetColumn;
        }

        /// <summary>
        /// 返回每一列当前最靠前的可见存活敌人。结果按列扫描，属于几何查询；
        /// 玩家自动选择的威胁排优先顺序由 AttackResolver 统一提供。
        /// </summary>
        public static List<int> GetColumnFrontEnemyIndices(
            IReadOnlyList<EnemyStats> enemies)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            var result = new List<int>();
            var seenIndices = new HashSet<int>();
            for (int column = 0; column < Columns; column++)
            {
                int best = -1;
                int bestRow = int.MaxValue;
                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemyStats enemy = enemies[i];
                    if (enemy == null || enemy.IsDead ||
                        !IsFullyInsideVisibleRows(enemy))
                    {
                        continue;
                    }
                    int enemyColumn = enemy.column >= 0
                        ? enemy.column
                        : i % Columns;
                    if (enemyColumn > column ||
                        enemyColumn + enemy.GridWidth <= column)
                    {
                        continue;
                    }
                    if (enemy.row < bestRow)
                    {
                        best = i;
                        bestRow = enemy.row;
                    }
                }
                if (best >= 0 && seenIndices.Add(best)) result.Add(best);
            }
            return result;
        }

        /// <summary>是否还有存活敌人占据最靠近玩家的第一排。</summary>
        public static bool HasLivingFrontRow(IList<EnemyStats> enemies)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy != null && enemy.IsDeployed &&
                    !enemy.IsDead && enemy.row <= 0)
                    return true;
            }
            return false;
        }

        /// <summary>是否有指定波次之前的存活敌人。</summary>
        private static bool HasEarlierWaveSurvivor(
            IList<EnemyStats> enemies,
            int waveIndex)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats e = enemies[i];
                if (e != null && e.IsDeployed && !e.IsDead &&
                    e.spawnWaveIndex < waveIndex)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 按指定空位列进行局部补位。该兼容原语供撤离等明确按列恢复的
        /// 边界使用；死亡结算由 <see cref="BackfillBehindDefeats"/>
        /// 进一步限定死亡者后方及入场资格。前排先动，同一次调用内允许链式跟进一排，
        /// 每只怪最多移动一次。
        /// </summary>
        public static List<int> BackfillAfterDeaths(
            IList<EnemyStats> enemies,
            int waveIndex,
            IReadOnlyCollection<int> deathColumns,
            ISet<int> stationaryEnemyIndices = null)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (deathColumns == null)
                throw new ArgumentNullException(nameof(deathColumns));

            var moved = new List<int>();
            if (deathColumns.Count == 0) return moved;

            var columnSet = deathColumns as ISet<int> ??
                new HashSet<int>(deathColumns);
            var order = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    (stationaryEnemyIndices == null || !stationaryEnemyIndices.Contains(i)) &&
                    IntersectsColumns(enemy, columnSet))
                {
                    order.Add(i);
                }
            }
            return AdvanceOrderForwardOnce(enemies, order, moved);
        }

        /// <summary>
        /// 死亡仅释放死者原占列中的后方队列。完整可见的后继可跨波次跟进；
        /// 隐藏后继只响应本波新死亡，其他列、死者前方及未部署单位不动。
        /// 一次结算内每只后继最多前进一排，即使同时死去多个同列单位也不重复推进。
        /// 多格单位仍使用完整目标矩形进行碰撞检查，不把移动扩散到相邻列。
        /// </summary>
        internal static List<int> BackfillBehindDefeats(
            IList<EnemyStats> enemies,
            IReadOnlyDictionary<int, List<FormationFootprint>> defeatsByWave,
            ISet<int> stationaryEnemyIndices = null)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (defeatsByWave == null)
                throw new ArgumentNullException(nameof(defeatsByWave));

            var order = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || !enemy.IsDeployed || enemy.IsDead ||
                    (stationaryEnemyIndices != null && stationaryEnemyIndices.Contains(i)))
                    continue;

                // Freeze eligibility before any row changes. In particular a
                // hidden later wave cannot join because another actor moved.
                bool visible = IsFullyInsideVisibleRows(enemy);
                bool followsDefeat = false;
                foreach (var wave in defeatsByWave)
                {
                    if (!visible && enemy.spawnWaveIndex != wave.Key) continue;
                    foreach (FormationFootprint vacancy in wave.Value)
                    {
                        if (enemy.row >= vacancy.EndRowExclusive &&
                            enemy.column < vacancy.EndColumnExclusive &&
                            enemy.column + enemy.GridWidth > vacancy.Column)
                        {
                            followsDefeat = true;
                            break;
                        }
                    }
                    if (followsDefeat) break;
                }
                if (followsDefeat) order.Add(i);
            }
            return AdvanceOrderForwardOnce(enemies, order, new List<int>());
        }

        /// <summary>
        /// 显式整波推进：指定波次的所有已部署存活实体都尝试保持列不变
        /// 向前一排。完整目标矩形被占用时保持原位；前排先动，同一次调用
        /// 内允许后方实体链式跟进，但每只实体最多移动一次。
        /// </summary>
        public static List<int> AdvanceWaveForwardOnce(
            IList<EnemyStats> enemies,
            int waveIndex)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            var order = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    enemy.spawnWaveIndex == waveIndex)
                {
                    order.Add(i);
                }
            }
            return AdvanceOrderForwardOnce(enemies, order, new List<int>());
        }

        /// <summary>
        /// 回合推进边界（结束回合）：所有不在前排的存活敌人整体向前收拢
        /// 一排，与死亡列无关；目标格被其他存活怪物完整矩形阻挡时保持
        /// 原位。前排先动，同一次推进内允许链式跟进一排。
        /// </summary>
        public static List<int> AdvanceAllForwardOnce(
            IList<EnemyStats> enemies,
            ISet<int> stationaryEnemyIndices = null)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            var order = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    (stationaryEnemyIndices == null || !stationaryEnemyIndices.Contains(i)))
                {
                    order.Add(i);
                }
            }
            return AdvanceOrderForwardOnce(enemies, order, new List<int>());
        }

        /// <summary>
        /// 按排升序逐个前移一排；movedOnce 保证每只怪一次调用内最多移动
        /// 一次，前排腾出的排位允许后方在同一次调用内链式跟进。
        /// </summary>
        private static List<int> AdvanceOrderForwardOnce(
            IList<EnemyStats> enemies,
            List<int> order,
            List<int> moved)
        {
            if (order.Count == 0) return moved;

            var movedOnce = new HashSet<int>();
            // 前排先动：前排腾出的排位允许同一次结算内后排跟进一排。
            order.Sort((a, b) =>
            {
                int rowComparison = enemies[a].row.CompareTo(enemies[b].row);
                return rowComparison != 0 ? rowComparison : a.CompareTo(b);
            });

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int i in order)
                {
                    if (movedOnce.Contains(i) ||
                        !CanAdvanceForward(enemies, i))
                    {
                        continue;
                    }

                    EnemyStats enemy = enemies[i];
                    enemy.row--;
                    enemy.justFilledFrontRow = true;
                    movedOnce.Add(i);
                    moved.Add(i);
                    changed = true;
                }
            }
            return moved;
        }

        private static bool IntersectsColumns(
            EnemyStats enemy,
            ISet<int> columns)
        {
            for (int x = 0; x < enemy.GridWidth; x++)
            {
                if (columns.Contains(enemy.column + x))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 查询按列局部补位是否仍有下一步合法推进（仅列对齐的后排参与）。
        /// </summary>
        public static bool CanBackfillAfterDeaths(
            IList<EnemyStats> enemies,
            int waveIndex,
            IReadOnlyCollection<int> deathColumns)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (deathColumns == null)
                throw new ArgumentNullException(nameof(deathColumns));
            if (deathColumns.Count == 0) return false;

            var columnSet = deathColumns as ISet<int> ??
                new HashSet<int>(deathColumns);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    IntersectsColumns(enemy, columnSet) &&
                    CanAdvanceForward(enemies, i))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 将已解锁的新波次按其内部相对阵型整体部署到前方。该操作不读取上一波
        /// 死亡列，因此波次切换不会继承旧补位信号；前序波仍有存活者时不移动。
        /// </summary>
        public static List<int> DeployWaveToFront(
            IList<EnemyStats> enemies,
            int waveIndex)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            var moved = new List<int>();
            if (HasEarlierWaveSurvivor(enemies, waveIndex)) return moved;

            int frontRow = int.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    enemy.spawnWaveIndex != waveIndex)
                {
                    continue;
                }
                frontRow = Math.Min(frontRow, enemy.row);
            }
            if (frontRow == int.MaxValue || frontRow <= 0) return moved;

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    enemy.spawnWaveIndex != waveIndex)
                {
                    continue;
                }
                enemy.row -= frontRow;
                enemy.justFilledFrontRow = true;
                moved.Add(i);
            }
            return moved;
        }

        /// <summary>
        /// 将指定的隐藏存活敌人拉回完整入场边界（前三排减去自身纵深）。调用方只传入
        /// 已经越界的活动敌人；本方法只拥有占格校验与确定性移动规则，不扫描
        /// 或解锁原本就在隐藏区等待的后备波次。
        /// </summary>
        public static List<int> MoveToVisibleBoundary(
            IList<EnemyStats> enemies,
            IReadOnlyCollection<int> enemyIndices)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (enemyIndices == null)
                throw new ArgumentNullException(nameof(enemyIndices));

            var order = new List<int>();
            var seen = new HashSet<int>();
            foreach (int enemyIndex in enemyIndices)
            {
                if (enemyIndex < 0 || enemyIndex >= enemies.Count ||
                    !seen.Add(enemyIndex))
                {
                    continue;
                }
                EnemyStats enemy = enemies[enemyIndex];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    !IsFullyInsideVisibleRows(enemy))
                {
                    order.Add(enemyIndex);
                }
            }
            order.Sort((a, b) =>
            {
                int rowComparison = enemies[a].row.CompareTo(enemies[b].row);
                return rowComparison != 0 ? rowComparison : a.CompareTo(b);
            });

            var moved = new List<int>();
            for (int i = 0; i < order.Count; i++)
            {
                int enemyIndex = order[i];
                int targetRow = VisibleRows - enemies[enemyIndex].GridDepth;
                bool pathClear = true;
                for (int row = enemies[enemyIndex].row - 1; row >= targetRow; row--)
                {
                    if (CanMoveEnemyToRow(enemies, enemyIndex, row)) continue;
                    pathClear = false;
                    break;
                }
                if (!pathClear)
                    continue;

                enemies[enemyIndex].row = targetRow;
                moved.Add(enemyIndex);
            }
            return moved;
        }

        /// <summary>该怪是否可向前收拢一排：存活、不在前排、且前方格无重叠。</summary>
        private static bool CanAdvanceForward(
            IList<EnemyStats> enemies,
            int enemyIndex)
        {
            EnemyStats enemy = enemies[enemyIndex];
            if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                enemy.row <= 0)
            {
                return false;
            }

            int targetRow = enemy.row - 1;
            return CanMoveEnemyToRow(enemies, enemyIndex, targetRow);
        }

        private static bool CanMoveEnemyToRow(
            IList<EnemyStats> enemies,
            int enemyIndex,
            int targetRow)
        {
            EnemyStats enemy = enemies[enemyIndex];
            if (enemy == null || !enemy.IsDeployed) return false;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (i == enemyIndex) continue;
                EnemyStats blocker = enemies[i];
                if (blocker == null || blocker.IsDead ||
                    !blocker.IsDeployed)
                {
                    continue;
                }
                if (RectanglesOverlap(
                        enemy.column,
                        targetRow,
                        enemy.GridWidth,
                        enemy.GridDepth,
                        blocker.column,
                        blocker.row,
                        blocker.GridWidth,
                        blocker.GridDepth))
                {
                    return false;
                }
            }
            return true;
        }

        public static IEnumerable<Vector2Int> GetOccupiedCells(
            EnemyStats enemy)
        {
            if (enemy == null)
                yield break;

            for (int rowOffset = 0;
                 rowOffset < enemy.GridDepth;
                 rowOffset++)
            {
                for (int columnOffset = 0;
                     columnOffset < enemy.GridWidth;
                     columnOffset++)
                {
                    yield return new Vector2Int(
                        enemy.column + columnOffset,
                        enemy.row + rowOffset);
                }
            }
        }

        private static bool CanOccupy(
            ISet<long> occupied,
            int row,
            int column,
            int width,
            int depth)
        {
            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (occupied.Contains(Key(row + y, column + x)))
                        return false;
                }
            }
            return true;
        }

        private static void MarkOccupied(
            ISet<long> occupied,
            int row,
            int column,
            int width,
            int depth)
        {
            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                    occupied.Add(Key(row + y, column + x));
            }
        }

        private static long Key(int row, int column)
        {
            return ((long)row << 32) | (uint)column;
        }

        private static bool RectanglesOverlap(int ax, int ay, int aw, int ah,
            int bx, int by, int bw, int bh)
            => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
    }
}
