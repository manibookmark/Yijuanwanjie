using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 一次敌方伤害的完整输入。调用方只描述来源与已算好的威力；护盾、生命、
    /// 事件字段和死亡回执的映射全部由 DamageSystem 原子完成。
    /// </summary>
    internal readonly struct EnemyDamageRequest
    {
        public int SourceCardInstanceId { get; }
        public int AttackInstanceIndex { get; }
        public int SubHitIndex { get; }
        public DamageKind Kind { get; }
        public int RequestedDamage { get; }
        public bool IsCritical { get; }
        public bool CanTriggerArea { get; }
        public bool CanTriggerSecondaryEffects { get; }

        public EnemyDamageRequest(
            int sourceCardInstanceId,
            int attackInstanceIndex,
            int subHitIndex,
            DamageKind kind,
            int requestedDamage,
            bool isCritical = false,
            bool canTriggerArea = false,
            bool canTriggerSecondaryEffects = false)
        {
            SourceCardInstanceId = Math.Max(0, sourceCardInstanceId);
            AttackInstanceIndex = attackInstanceIndex;
            SubHitIndex = subHitIndex;
            Kind = kind;
            RequestedDamage = Math.Max(0, requestedDamage);
            IsCritical = isCritical;
            CanTriggerArea = canTriggerArea;
            CanTriggerSecondaryEffects = canTriggerSecondaryEffects;
        }
    }

    /// <summary>
    /// 敌方伤害的原子回执。DamageEvent 与可选 EnemyDefeatResult 永远来自同一
    /// 次数值写入，调用方不再重复推导 Fatal、runtimeId 或字段映射。
    /// </summary>
    internal readonly struct EnemyDamageReceipt
    {
        public DamageEvent Event { get; }
        public bool HasDefeat => Event.IsFatal;

        internal EnemyDamageReceipt(DamageEvent damageEvent)
        {
            Event = damageEvent;
        }

        public bool TryGetDefeat(out EnemyDefeatResult defeat)
        {
            if (!HasDefeat)
            {
                defeat = default;
                return false;
            }
            defeat = new EnemyDefeatResult(
                Event.TargetEnemyRuntimeId,
                Event.TargetIndex,
                DefeatCause.Damage,
                false);
            return true;
        }
    }

    /// <summary>
    /// 一次伤害写入的不可变回执。RequestedDamage 是事件威力；AppliedDamage
    /// 只包含真实扣除的护盾与生命；合法 Boss 尸体伤害单独进入 OverkillDamage。
    /// </summary>
    public readonly struct DamageResolution
    {
        public int RequestedDamage { get; }
        public int AppliedDamage { get; }
        public int ShieldDamage { get; }
        public int HpDamage { get; }
        public int OverkillDamage { get; }
        public bool BecameFatal { get; }
        public int RemainingHealth { get; }

        public int ResolvedDamage => AppliedDamage + OverkillDamage;

        internal DamageResolution(
            int requestedDamage,
            int appliedDamage,
            int shieldDamage,
            int hpDamage,
            int overkillDamage,
            bool becameFatal,
            int remainingHealth)
        {
            RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage;
            ShieldDamage = shieldDamage;
            HpDamage = hpDamage;
            OverkillDamage = overkillDamage;
            BecameFatal = becameFatal;
            RemainingHealth = remainingHealth;
        }

        public override string ToString()
            => $"Dmg applied={AppliedDamage} overkill={OverkillDamage} " +
               $"(requested={RequestedDamage}, shield={ShieldDamage}, " +
               $"hp={HpDamage}, fatal={BecameFatal}, " +
               $"remain={RemainingHealth})";
    }

    /// <summary>
    /// 护盾、生命、伤害导致的死亡转变和合法 Boss 余威数值的唯一写入者。
    /// AttackResolver 只负责编排目标与事件元数据；状态和地形伤害也必须调用
    /// 本模块，禁止直接修改 currentHealth/armor。
    /// </summary>
    public static class DamageSystem
    {
        /// <summary>CritSpec 的默认概率，不是全局强制规则。</summary>
        public const float DefaultCritChance = 0.25f;

        /// <summary>CritSpec 的默认倍率，不是全局强制规则。</summary>
        public const float DefaultCritMultiplier = 1.5f;

        public static int RoundHalfUp(float value)
            => Mathf.FloorToInt(value + 0.5f);

        /// <summary>VC DamageEffect and target projection use midpoint-to-even, not half-up.</summary>
        public static int RoundToEven(double value)
            => checked((int)Math.Round(value, MidpointRounding.ToEven));

        public static int CalculateSplashDamage(
            double hitDamage,
            int area,
            float coefficient = 0.2f)
        {
            if (hitDamage <= 0 || area <= 0 || coefficient <= 0f)
                return 0;
            // Preserve the project's existing float-to-ceiling integer HP map.
            // Double precision belongs to native main-hit stages; widening the
            // float coefficient here would turn an exact 2 into ceil(2+epsilon).
            return Mathf.CeilToInt((float)hitDamage * (area / 100f) * coefficient);
        }

        public static DamageResolution ApplyToPlayer(
            PlayerStats player,
            int requestedDamage)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            int requested = Math.Max(0, requestedDamage);
            bool aliveBefore = player.currentHealth > 0;
            int shield = Math.Min(Math.Max(0, player.armor), requested);
            player.armor -= shield;
            int hp = Math.Min(
                Math.Max(0, player.currentHealth),
                requested - shield);
            player.currentHealth -= hp;

            return new DamageResolution(
                requested,
                shield + hp,
                shield,
                hp,
                overkillDamage: 0,
                becameFatal: aliveBefore && player.currentHealth <= 0,
                remainingHealth: player.currentHealth);
        }

        /// <summary>
        /// 对敌人应用已经完成 Might/Combo/Crit 等上游计算的确定伤害。
        /// 活体致命伤严格封顶到护盾+生命；只有已由 BossOverkillSession
        /// 激活的 Boss 尸体才把整次请求记入 OverkillDamage。
        /// </summary>
        private static DamageResolution ApplyToEnemy(
            EnemyStats target,
            int requestedDamage)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            int requested = Math.Max(0, requestedDamage);
            if (!target.IsDeployed || target.IsWithdrawn)
                return Empty(requested, target.currentHealth);

            bool aliveBefore = !target.IsDead;
            if (!aliveBefore)
            {
                int overkill = target.isBoss && target.isOverkillTarget
                    ? requested
                    : 0;
                return new DamageResolution(
                    requested,
                    appliedDamage: 0,
                    shieldDamage: 0,
                    hpDamage: 0,
                    overkillDamage: overkill,
                    becameFatal: false,
                    remainingHealth: target.currentHealth);
            }

            int shield = Math.Min(Math.Max(0, target.armor), requested);
            target.armor -= shield;
            int hp = Math.Min(
                Math.Max(0, target.currentHealth),
                requested - shield);
            target.currentHealth -= hp;

            return new DamageResolution(
                requested,
                shield + hp,
                shield,
                hp,
                overkillDamage: 0,
                becameFatal: target.IsDead,
                remainingHealth: target.currentHealth);
        }

        /// <summary>
        /// 应用敌方伤害并一次性产出事件与死亡回执。所有战斗编排模块必须调用
        /// 此入口，避免各自复制 DamageResolution 到事件协议的知识。
        /// </summary>
        internal static EnemyDamageReceipt ResolveEnemyDamage(
            System.Collections.Generic.IList<EnemyStats> enemies,
            int targetIndex,
            EnemyDamageRequest request)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            if (targetIndex < 0 || targetIndex >= enemies.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(targetIndex),
                    "伤害目标索引必须指向当前敌人集合中的实体。");

            EnemyStats target = enemies[targetIndex];
            if (target == null)
                throw new ArgumentException(
                    $"敌人集合第 {targetIndex} 项为空。",
                    nameof(enemies));

            DamageResolution damage = ApplyToEnemy(
                target,
                request.RequestedDamage);
            return new EnemyDamageReceipt(new DamageEvent(
                request.SourceCardInstanceId,
                target.runtimeId,
                targetIndex,
                request.AttackInstanceIndex,
                request.SubHitIndex,
                request.Kind,
                damage.RequestedDamage,
                damage.AppliedDamage,
                damage.ShieldDamage,
                damage.HpDamage,
                request.IsCritical,
                damage.BecameFatal,
                request.CanTriggerArea,
                request.CanTriggerSecondaryEffects,
                damage.OverkillDamage));
        }

        private static DamageResolution Empty(
            int requestedDamage,
            int remainingHealth)
            => new DamageResolution(
                requestedDamage,
                appliedDamage: 0,
                shieldDamage: 0,
                hpDamage: 0,
                overkillDamage: 0,
                becameFatal: false,
                remainingHealth: remainingHealth);
    }
}
