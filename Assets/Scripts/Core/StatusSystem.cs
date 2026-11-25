using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 状态效果的触发时机。
    /// </summary>
    public enum StatusTiming
    {
        OnApplied,      // 施加时立即触发
        TurnStarted,    // 回合开始时
        TurnEnded,      // 回合结束时
        BeforeDamage,   // 伤害结算前
        AfterDamage,    // 伤害结算后
        UnitDied        // 单位死亡时
    }

    /// <summary>
    /// 状态效果静态定义。描述"这种状态是什么"。
    /// </summary>
    [Serializable]
    public class StatusDefinition
    {
        public StatusEffect effectType;
        public string displayName;
        public bool canStack = true;
        public int maxStacks = 99;
        public int defaultDuration = 0; // 0=永久，>0=N回合
        public StatusTiming triggerTiming = StatusTiming.TurnEnded;
    }

    /// <summary>
    /// 状态效果运行时实例。描述"这个单位身上当前有几层、还剩几回合"。
    /// </summary>
    public class StatusInstance
    {
        public StatusEffect effectType;
        public int stacks;
        public int remainingTurns;
        public int sourceId; // 施加者索引（-1=系统）

        public bool IsExpired => remainingTurns == 0;
        public bool IsPermanent => remainingTurns < 0;

        public StatusInstance(StatusEffect effect, int stacks = 1, int duration = 0, int sourceId = -1)
        {
            effectType = effect;
            this.stacks = stacks;
            // duration=0 表示永久，用 -1 标记
            remainingTurns = duration == 0 ? -1 : duration;
            this.sourceId = sourceId;
        }

        public void Tick()
        {
            if (remainingTurns > 0)
                remainingTurns--;
        }
    }

    /// <summary>
    /// 持续效果系统。只管理绑定玩家或单位的状态；绑定战场格的效果
    /// 由 TerrainEffectSystem 管理，避免两种生命周期相互泄漏。
    /// <para>
    /// 职责：
    /// <list type="bullet">
    /// <item>施加状态（叠层或覆盖）</item>
    /// <item>按时机触发状态效果</item>
    /// <item>回合结束倒计时和过期清理</item>
    /// </list>
    /// </para>
    /// <para>
    /// 当前实现第一期：
    /// <list type="bullet">
    /// <item>ZhenTui（击退）— 由 CombatController 直接处理，不经过此系统</item>
    /// <item>ZhuoShao（灼烧）— 回合结束造成 stacks 点伤害</item>
    /// <item>BingFeng（冰封）— 跳过敌人下次行动（预留，当前无敌人 AI）</item>
    /// <item>JiaoXie（缴械）— 预留</item>
    /// <item>BaoJi（暴击）— 预留（当前暴击由 DamageSystem 处理）</item>
    /// </list>
    /// </para>
    /// </summary>
    public class StatusSystem
    {
        public event Action<DamageEvent> DamageResolved;
        /// <summary>
        /// Complete immutable damage/death facts before legacy callbacks. The consumer
        /// can prepare every death, then publish hits and kills without retaining a batch.
        /// </summary>
        public event Action<IReadOnlyList<DamageEvent>, IReadOnlyList<EnemyDefeatResult>> DamageBatchResolved;
        public event Action<EnemyDefeatResult> EnemyDefeated;
        private readonly CombatState _state;
        // key = EnemyRuntimeId；索引只是当次 roster 定位，绝不是第二套身份。
        private readonly Dictionary<int, List<StatusInstance>> _enemyStatuses = new();
        private readonly List<StatusInstance> _playerStatuses = new();

        /// <summary>按场内唯一的 EnemyRuntimeId 存储。</summary>
        public IReadOnlyDictionary<int, List<StatusInstance>> EnemyStatuses => _enemyStatuses;
        public IReadOnlyList<StatusInstance> PlayerStatuses => _playerStatuses;

        public StatusSystem(CombatState state)
        {
            _state = state;
        }

        // ── 施加状态 ──

        /// <summary>对敌人施加状态。</summary>
        public void ApplyToEnemy(int enemyIndex, StatusEffect effect, int stacks = 1, int duration = 0, int sourceId = 0)
        {
            if (enemyIndex < 0 || enemyIndex >= _state.Enemies.Count)
                return;
            EnemyStats enemy = _state.Enemies[enemyIndex];
            if (enemy == null || enemy.IsDead || !enemy.IsDeployed)
                return;

            int enemyKey = GetEnemyKey(enemyIndex);
            if (!_enemyStatuses.TryGetValue(enemyKey, out var list))
            {
                list = new List<StatusInstance>();
                _enemyStatuses[enemyKey] = list;
            }

            // 检查是否已有同类型状态
            var existing = list.Find(s => s.effectType == effect);
            if (existing != null)
            {
                existing.stacks = Math.Min(existing.stacks + stacks, 99);
                if (duration > 0)
                    existing.remainingTurns = Math.Max(existing.remainingTurns, duration);
                if (sourceId != 0) existing.sourceId = sourceId;
            }
            else
            {
                list.Add(new StatusInstance(effect, stacks, duration, sourceId));
            }
        }

        /// <summary>对玩家施加状态。</summary>
        public void ApplyToPlayer(StatusEffect effect, int stacks = 1, int duration = 0)
        {
            var existing = _playerStatuses.Find(s => s.effectType == effect);
            if (existing != null)
            {
                existing.stacks = Math.Min(existing.stacks + stacks, 99);
                if (duration > 0)
                    existing.remainingTurns = Math.Max(existing.remainingTurns, duration);
            }
            else
            {
                _playerStatuses.Add(new StatusInstance(effect, stacks, duration));
            }
        }

        // ── 查询 ──

        /// <summary>敌人是否有指定状态。</summary>
        public bool EnemyHasStatus(int enemyIndex, StatusEffect effect)
        {
            if (!_enemyStatuses.TryGetValue(GetEnemyKey(enemyIndex), out var list))
                return false;
            return list.Exists(s => s.effectType == effect);
        }

        /// <summary>获取敌人指定状态的层数。</summary>
        public int GetEnemyStacks(int enemyIndex, StatusEffect effect)
        {
            if (!_enemyStatuses.TryGetValue(GetEnemyKey(enemyIndex), out var list))
                return 0;
            var inst = list.Find(s => s.effectType == effect);
            return inst?.stacks ?? 0;
        }

        /// <summary>移除敌人的指定状态。</summary>
        public void RemoveFromEnemy(int enemyIndex, StatusEffect effect)
        {
            if (_enemyStatuses.TryGetValue(GetEnemyKey(enemyIndex), out var list))
                list.RemoveAll(s => s.effectType == effect);
        }

        /// <summary>移除玩家的指定状态。</summary>
        public void RemoveFromPlayer(StatusEffect effect)
        {
            _playerStatuses.RemoveAll(s => s.effectType == effect);
        }

        /// <summary>清空敌人死亡时所有状态。</summary>
        public void ClearEnemy(int enemyIndex)
        {
            _enemyStatuses.Remove(GetEnemyKey(enemyIndex));
        }

        /// <summary>清空全部状态（战斗结束时）。</summary>
        public void ClearAll()
        {
            _enemyStatuses.Clear();
            _playerStatuses.Clear();
        }

        // ── 时机触发 ──

        /// <summary>回合开始时触发。倒计时由 TurnEnded 处理。</summary>
        public void OnTurnStarted()
        {
            // 冰封检查：如果有冰封状态，移除一层并标记跳过
            // 当前无敌人 AI，预留
        }

        /// <summary>回合结束时触发。执行灼烧等伤害并倒计时。</summary>
        public void OnTurnEnded()
        {
            // 灼烧：每个有灼烧状态的敌人受 stacks 点伤害
            var enemies = _state.Enemies;
            var pendingDamage = new List<DamageEvent>();
            var pendingDefeats = new List<EnemyDefeatResult>();
            foreach (var kvp in _enemyStatuses)
            {
                int idx = FindEnemyIndex(kvp.Key);
                if (idx < 0 || idx >= enemies.Count) continue;
                var enemy = enemies[idx];
                if (enemy.IsDead || !enemy.IsDeployed) continue;

                foreach (var status in kvp.Value)
                {
                    if (status.effectType == StatusEffect.ZhuoShao && status.stacks > 0)
                    {
                        EnemyDamageReceipt receipt =
                            DamageSystem.ResolveEnemyDamage(
                                _state.EnemyRoster,
                            idx,
                            new EnemyDamageRequest(
                                status.sourceId,
                                -1,
                                -1,
                                DamageKind.DoT,
                                status.stacks));
                        pendingDamage.Add(receipt.Event);
                        if (receipt.TryGetDefeat(
                                out EnemyDefeatResult defeat))
                            pendingDefeats.Add(defeat);
                    }
                }
            }

            // 倒计时
            foreach (var kvp in _enemyStatuses)
            {
                for (int i = kvp.Value.Count - 1; i >= 0; i--)
                {
                    kvp.Value[i].Tick();
                    if (kvp.Value[i].IsExpired && !kvp.Value[i].IsPermanent)
                        kvp.Value.RemoveAt(i);
                }
            }

            for (int i = _playerStatuses.Count - 1; i >= 0; i--)
            {
                _playerStatuses[i].Tick();
                if (_playerStatuses[i].IsExpired && !_playerStatuses[i].IsPermanent)
                    _playerStatuses.RemoveAt(i);
            }

            if (pendingDamage.Count > 0)
                DamageBatchResolved?.Invoke(pendingDamage.AsReadOnly(), pendingDefeats.AsReadOnly());
            for (int i = 0; i < pendingDamage.Count; i++) DamageResolved?.Invoke(pendingDamage[i]);
            for (int i = 0; i < pendingDefeats.Count; i++) EnemyDefeated?.Invoke(pendingDefeats[i]);
        }

        private int GetEnemyKey(int enemyIndex)
        {
            if (enemyIndex < 0 || enemyIndex >= _state.Enemies.Count) return int.MinValue;
            EnemyStats enemy = _state.Enemies[enemyIndex];
            if (enemy == null || enemy.runtimeId <= 0)
            {
                throw new InvalidOperationException(
                    $"状态系统要求敌人拥有大于 0 的稳定 runtimeId：" +
                    $"index={enemyIndex}。");
            }
            return enemy.runtimeId;
        }

        private int FindEnemyIndex(int enemyKey)
        {
            for (int i = 0; i < _state.Enemies.Count; i++)
                if (GetEnemyKey(i) == enemyKey) return i;
            return -1;
        }

    }
}
