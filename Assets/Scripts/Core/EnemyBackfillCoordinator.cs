using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Opaque marker for one authoritative damage/effect resolution.
    /// </summary>
    public readonly struct EnemyBackfillCheckpoint
    {
        internal EnemyBackfillCheckpoint(int defeatVersion)
        {
            DefeatVersion = defeatVersion;
        }

        internal int DefeatVersion { get; }
    }

    /// <summary>
    /// Owns formation-recovery mutations for one combat. It captures typed
    /// before/after batches at five distinct boundaries: affected-column backfill
    /// after defeats, immediate column backfill after withdrawal, whole-wave
    /// deployment, enemy-turn preparation, and turn-end return of knockback
    /// boundary crossers. Empty-field recovery also belongs to this owner:
    /// it reuses collision-checked one-row steps without spending a turn.
    /// A visible survivor preserves pending knockback returns. If every enemy
    /// leaves the active field, recovery takes precedence so the next decision
    /// always has a legal opponent, without skipping an earlier wave.
    /// </summary>
    public sealed class EnemyBackfillCoordinator
    {
        private readonly IList<EnemyStats> _enemies;
        private readonly Dictionary<int, List<FormationFootprint>> _pendingDefeats =
            new Dictionary<int, List<FormationFootprint>>();
        private readonly HashSet<int> _registeredDefeatIndices =
            new HashSet<int>();
        private readonly HashSet<int> _registeredWithdrawalIndices =
            new HashSet<int>();
        private readonly HashSet<int> _knockbackBoundaryCrossers =
            new HashSet<int>();
        private int _defeatVersion;

        public EnemyBackfillCoordinator(IList<EnemyStats> enemies)
        {
            _enemies = enemies ??
                throw new ArgumentNullException(nameof(enemies));
        }

        /// <summary>Marks the start of one complete card, status, or terrain resolution.</summary>
        public EnemyBackfillCheckpoint BeginResolution()
            => new EnemyBackfillCheckpoint(_defeatVersion);

        /// <summary>
        /// Records a newly defeated enemy exactly once. Historical corpses and
        /// repeated defeat notifications cannot recreate a movement signal.
        /// </summary>
        public bool RegisterDefeat(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _enemies.Count)
                return false;

            EnemyStats enemy = _enemies[targetIndex];
            if (enemy == null || !enemy.IsDead ||
                !_registeredDefeatIndices.Add(targetIndex))
            {
                return false;
            }

            if (!_pendingDefeats.TryGetValue(enemy.spawnWaveIndex, out var footprints))
            {
                footprints = new List<FormationFootprint>();
                _pendingDefeats.Add(enemy.spawnWaveIndex, footprints);
            }
            // Capture the vacancy now; later movement must not widen a defeat
            // into a wave-wide signal or move enemies ahead of the dead enemy.
            footprints.Add(FootprintOf(enemy));

            unchecked
            {
                _defeatVersion++;
            }
            return true;
        }

        /// <summary>
        /// 撤离行动完成后立即回填该敌人原占列。撤离与死亡分别去重、分别发布
        /// 类型化推进原因，不会制造击败信号或消费待处理的死亡波次。
        /// </summary>
        internal IReadOnlyList<FormationAdvanceBatch>
            AdvanceAfterWithdrawal(int withdrawnIndex)
        {
            if (withdrawnIndex < 0 || withdrawnIndex >= _enemies.Count)
                return Array.Empty<FormationAdvanceBatch>();

            EnemyStats withdrawn = _enemies[withdrawnIndex];
            if (withdrawn == null ||
                !withdrawn.IsWithdrawn ||
                withdrawn.IsDeployed ||
                !_registeredWithdrawalIndices.Add(withdrawnIndex))
            {
                return Array.Empty<FormationAdvanceBatch>();
            }

            var columns = new HashSet<int>();
            for (int x = 0; x < withdrawn.GridWidth; x++)
                columns.Add(withdrawn.column + x);

            List<FormationAdvanceBatch> batches = AdvanceWithdrawalVacancyOnce(
                withdrawn.spawnWaveIndex,
                columns);
            AppendEmptyFieldRecovery(batches);
            return batches;
        }

        /// <summary>
        /// Death backfill advances once only for a newly registered defeat;
        /// unrelated cards cannot reuse an old death signal. If the active
        /// field is entirely empty, recovery may additionally take legal steps
        /// until the first full footprint enters, including after knockback.
        /// </summary>
        internal IReadOnlyList<FormationAdvanceBatch> AdvanceAfterResolution(
            EnemyBackfillCheckpoint checkpoint)
        {
            List<FormationAdvanceBatch> batches =
                checkpoint.DefeatVersion == _defeatVersion
                    ? new List<FormationAdvanceBatch>()
                    : AdvancePendingOnce();
            AppendEmptyFieldRecovery(batches);
            return batches;
        }

        /// <summary>
        /// 回合推进边界（结束回合后的敌方行动准备）：所有不在前排的存活
        /// 敌人整体向前收拢一排，与死亡波次无关。尚未结算的死亡波次信号
        /// 随之消解——整体前压已覆盖其效果。
        /// </summary>
        internal FormationAdvanceBatch
            AdvanceAtEnemyActionPreparation()
        {
            Dictionary<int, FormationFootprint> before =
                CaptureFormationFootprints();
            _pendingDefeats.Clear();
            _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);
            List<int> moved = EnemyFormationLayout.AdvanceAllForwardOnce(
                _enemies, _knockbackBoundaryCrossers);
            foreach (int index in AdvanceEmptyFieldUntilEntry())
            {
                if (!moved.Contains(index)) moved.Add(index);
            }
            return BuildBatch(
                FormationAdvanceCause.EnemyTurnPreparation,
                moved,
                before);
        }

        /// <summary>
        /// Only an empty active field may fast-forward its earliest living wave.
        /// Each step preserves columns, collision checks and queue order. This
        /// does not advance the combat turn, execute actions or unlock a boss.
        /// </summary>
        internal FormationAdvanceBatch RecoverEmptyField()
        {
            Dictionary<int, FormationFootprint> before = CaptureFormationFootprints();
            return BuildBatch(
                FormationAdvanceCause.EmptyFieldRecovery,
                AdvanceEmptyFieldUntilEntry(),
                before);
        }

        private void AppendEmptyFieldRecovery(List<FormationAdvanceBatch> batches)
        {
            FormationAdvanceBatch recovery = RecoverEmptyField();
            if (recovery == null) return;

            // Publish only each actor's original-to-final footprint. Consumers
            // observe the final roster, so intermediate batches could otherwise
            // release a visual before the final destination has been vacated.
            batches.Add(recovery);
            var origins = new Dictionary<int, FormationFootprint>();
            var moved = new List<int>();
            foreach (FormationAdvanceBatch batch in batches)
            {
                foreach (FormationAdvanceMove move in batch.Moves)
                {
                    if (origins.ContainsKey(move.FormationIndex)) continue;
                    origins.Add(move.FormationIndex, move.FromFootprint);
                    moved.Add(move.FormationIndex);
                }
            }
            batches.Clear();
            FormationAdvanceBatch combined = BuildBatch(
                FormationAdvanceCause.EmptyFieldRecovery, moved, origins);
            if (combined != null) batches.Add(combined);
        }

        private List<int> AdvanceEmptyFieldUntilEntry()
        {
            var moved = new List<int>();
            var seen = new HashSet<int>();
            _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);
            while (true)
            {
                int firstWave = int.MaxValue;
                for (int i = 0; i < _enemies.Count; i++)
                {
                    EnemyStats enemy = _enemies[i];
                    if (enemy == null || enemy.IsDead || !enemy.IsDeployed) continue;
                    if (EnemyFormationLayout.IsFullyInsideVisibleRows(enemy)) return moved;
                    firstWave = Math.Min(firstWave, enemy.spawnWaveIndex);
                }
                if (firstWave == int.MaxValue) return moved;

                List<int> step = EnemyFormationLayout.AdvanceWaveForwardOnce(
                    _enemies, firstWave);
                // Every successful step strictly decreases an enemy's row.
                // A blocked layout therefore terminates without spinning or
                // teleporting through another entity's footprint.
                if (step.Count == 0) return moved;
                for (int i = 0; i < step.Count; i++)
                    if (seen.Add(step[i])) moved.Add(step[i]);
                // Empty-field recovery is the only early-return exception for
                // knockback: leave other hidden crossers pending while at least
                // one full footprint can participate in the next decision.
                _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);
            }
        }

        /// <summary>
        /// 记录本回合因震退从完整入场状态越过到隐藏区的敌人。原本就在隐藏区的
        /// 后备波次不会进入该集合，因此回合末归位不会提前解锁波次。
        /// </summary>
        public void RegisterKnockbackBoundaryCrossings(
            IReadOnlyList<int> enemyIndices)
        {
            if (enemyIndices == null) return;

            for (int i = 0; i < enemyIndices.Count; i++)
            {
                int enemyIndex = enemyIndices[i];
                if (enemyIndex < 0 || enemyIndex >= _enemies.Count)
                    continue;

                EnemyStats enemy = _enemies[enemyIndex];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                {
                    _knockbackBoundaryCrossers.Add(enemyIndex);
                }
            }
        }

        /// <summary>
        /// 回合结束时把震退越界者拉回按体型计算的完整入场边界。若目标格暂时
        /// 被占用，索引会保留到下个回合结束重试，避免制造重叠占格。
        /// </summary>
        internal FormationAdvanceBatch
            ReturnKnockbackBoundaryCrossersAtTurnEnd()
        {
            Dictionary<int, FormationFootprint> before =
                CaptureFormationFootprints();
            _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);
            List<int> moved = EnemyFormationLayout.MoveToVisibleBoundary(
                _enemies,
                _knockbackBoundaryCrossers);
            for (int i = 0; i < moved.Count; i++)
                _knockbackBoundaryCrossers.Remove(moved[i]);
            return BuildBatch(
                FormationAdvanceCause.KnockbackBoundaryReturn,
                moved,
                before);
        }

        private Dictionary<int, FormationFootprint>
            CaptureFormationFootprints()
        {
            var result = new Dictionary<int, FormationFootprint>();
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy == null) continue;
                result.Add(i, FootprintOf(enemy));
            }
            return result;
        }

        private IReadOnlyList<FormationAdvanceMove> BuildMoves(
            IReadOnlyList<int> movedIndices,
            IReadOnlyDictionary<int, FormationFootprint> before)
        {
            if (movedIndices == null || movedIndices.Count == 0)
                return Array.Empty<FormationAdvanceMove>();

            var moves = new List<FormationAdvanceMove>(movedIndices.Count);
            for (int i = 0; i < movedIndices.Count; i++)
            {
                int formationIndex = movedIndices[i];
                if (formationIndex < 0 || formationIndex >= _enemies.Count ||
                    !before.TryGetValue(
                        formationIndex,
                        out FormationFootprint fromFootprint))
                {
                    continue;
                }

                EnemyStats enemy = _enemies[formationIndex];
                if (enemy == null) continue;
                FormationFootprint toFootprint = FootprintOf(enemy);
                if (fromFootprint == toFootprint) continue;

                moves.Add(new FormationAdvanceMove(
                    enemy.runtimeId,
                    formationIndex,
                    fromFootprint,
                    toFootprint));
            }
            return moves;
        }

        private FormationAdvanceBatch BuildBatch(
            FormationAdvanceCause cause,
            IReadOnlyList<int> movedIndices,
            IReadOnlyDictionary<int, FormationFootprint> before)
        {
            IReadOnlyList<FormationAdvanceMove> moves = BuildMoves(
                movedIndices,
                before);
            return moves.Count == 0
                ? null
                : new FormationAdvanceBatch(cause, moves);
        }

        private static FormationFootprint FootprintOf(EnemyStats enemy)
            => new FormationFootprint(
                enemy.row,
                enemy.column,
                enemy.GridWidth,
                enemy.GridDepth);

        private List<FormationAdvanceBatch> AdvancePendingOnce()
        {
            var batches = new List<FormationAdvanceBatch>();
            if (_pendingDefeats.Count == 0) return batches;

            var pending = new Dictionary<int, List<FormationFootprint>>(_pendingDefeats);
            var waves = new List<int>(pending.Keys);
            waves.Sort();
            _pendingDefeats.Clear();
            _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);

            // One selection and one movement pass for the entire resolution:
            // visible followers may belong to different spawn waves, but each
            // actor must advance at most once even after multi-wave defeats.
            Dictionary<int, FormationFootprint> backfillBefore =
                CaptureFormationFootprints();
            List<int> moved = EnemyFormationLayout.BackfillBehindDefeats(
                _enemies, pending, _knockbackBoundaryCrossers);
            FormationAdvanceBatch backfill = BuildBatch(
                FormationAdvanceCause.DefeatBackfill, moved, backfillBefore);
            if (backfill != null) batches.Add(backfill);
            var movedWaves = new HashSet<int>();
            foreach (int index in moved)
                movedWaves.Add(_enemies[index].spawnWaveIndex);
            var consideredDeployments = new HashSet<int>();

            for (int i = 0; i < waves.Count; i++)
            {
                int waveIndex = waves[i];
                if (HasLivingEnemyInWave(waveIndex)) continue;

                // Already visible waves have entered the active queue. Do not
                // deploy them a second time or drag unrelated columns forward.
                // Truly hidden reserves retain explicit whole-wave entry.
                int nextWave = FindNextLivingWave(waveIndex);
                if (nextWave >= 0 && consideredDeployments.Add(nextWave) &&
                    !movedWaves.Contains(nextWave) &&
                    !HasVisibleEnemyInWave(nextWave) &&
                    !HasPendingKnockbackInWave(nextWave))
                {
                    Dictionary<int, FormationFootprint> before =
                        CaptureFormationFootprints();
                    FormationAdvanceBatch deployment = BuildBatch(
                        FormationAdvanceCause.WaveDeploy,
                        EnemyFormationLayout.DeployWaveToFront(
                            _enemies,
                            nextWave),
                        before);
                    if (deployment != null)
                        batches.Add(deployment);
                }
            }
            return batches;
        }

        private bool HasVisibleEnemyInWave(int waveIndex)
        {
            foreach (EnemyStats enemy in _enemies)
                if (enemy != null && !enemy.IsDead &&
                    enemy.spawnWaveIndex == waveIndex &&
                    EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                    return true;
            return false;
        }

        private List<FormationAdvanceBatch>
            AdvanceWithdrawalVacancyOnce(
                int waveIndex,
                IReadOnlyCollection<int> columns)
        {
            var batches = new List<FormationAdvanceBatch>();
            if (columns == null || columns.Count == 0)
                return batches;
            _knockbackBoundaryCrossers.RemoveWhere(IsNoLongerPendingReturn);

            if (HasLivingEnemyInWave(waveIndex))
            {
                Dictionary<int, FormationFootprint> before =
                    CaptureFormationFootprints();
                FormationAdvanceBatch backfill = BuildBatch(
                    FormationAdvanceCause.WithdrawalBackfill,
                    EnemyFormationLayout.BackfillAfterDeaths(
                        _enemies,
                        waveIndex,
                        columns,
                        _knockbackBoundaryCrossers),
                    before);
                if (backfill != null)
                    batches.Add(backfill);
                return batches;
            }

            int nextWave = FindNextLivingWave(waveIndex);
            if (nextWave < 0 || HasPendingKnockbackInWave(nextWave))
                return batches;

            Dictionary<int, FormationFootprint> deploymentBefore =
                CaptureFormationFootprints();
            FormationAdvanceBatch deployment = BuildBatch(
                FormationAdvanceCause.WaveDeploy,
                EnemyFormationLayout.DeployWaveToFront(
                    _enemies,
                    nextWave),
                deploymentBefore);
            if (deployment != null)
                batches.Add(deployment);
            return batches;
        }

        private bool HasPendingKnockbackInWave(int waveIndex)
        {
            foreach (int index in _knockbackBoundaryCrossers)
                if (_enemies[index].spawnWaveIndex == waveIndex) return true;
            return false;
        }

        private bool HasLivingEnemyInWave(int waveIndex)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy != null && enemy.IsDeployed && !enemy.IsDead &&
                    enemy.spawnWaveIndex == waveIndex)
                {
                    return true;
                }
            }
            return false;
        }

        private int FindNextLivingWave(int clearedWaveIndex)
        {
            int nextWave = int.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    enemy.spawnWaveIndex <= clearedWaveIndex)
                {
                    continue;
                }
                nextWave = Math.Min(nextWave, enemy.spawnWaveIndex);
            }
            return nextWave == int.MaxValue ? -1 : nextWave;
        }

        private bool IsNoLongerPendingReturn(int enemyIndex)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemies.Count)
                return true;
            EnemyStats enemy = _enemies[enemyIndex];
            return enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                EnemyFormationLayout.IsFullyInsideVisibleRows(enemy);
        }

    }
}
