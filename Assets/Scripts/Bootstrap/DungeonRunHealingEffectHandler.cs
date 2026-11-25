using System;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 把地图回血交给 Dungeon 的原子事务；满血也正常领取并保存拾取事实。
    /// 实际恢复量由回执提供，已提交回执只读重放，规则不在表现层复制。
    /// </summary>
    public sealed class DungeonRunHealingEffectHandler :
        ILevelInteractionEffectHandler, ILevelInteractionAtomicEffectHandler
    {
        private readonly DungeonRunSession _run;
        private readonly Action _persist;

        public LevelInteractionEffectKind Kind =>
            LevelInteractionEffectKind.HealCurrentHealth;

        public DungeonRunHealingEffectHandler(DungeonRunSession run, Action persist = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _persist = persist;
        }

        public bool TryApplyAtomic(string nodeContentId, LevelInteractionEffect effect,
            out LevelInteractionReceipt receipt, out string error)
        {
            receipt = default; error = null;
            // Queries return detached copies. Validate the complete canonical payload, not object identity or ID alone.
            if (effect == null || effect.kind != Kind ||
                !_run.CurrentFloor.MatchesConfiguredEffect(nodeContentId, effect))
            { error = "回血处理器需要当前节点的原配置效果。"; return false; }
            return _run.TryHealCurrentHealth(nodeContentId, effect.effectId, out receipt, out error, _persist);
        }

        // Legacy direct consumers explicitly retain memory-only behavior, without a receipt/save.
        public bool TryApply(
            LevelInteractionEffect effect,
            out int valueBefore,
            out int valueAfter,
            out int appliedAmount,
            out string error)
        {
            valueBefore = _run.CurrentHealth;
            valueAfter = valueBefore;
            appliedAmount = 0;
            if (_persist != null)
            { error = "严格保存的回血必须使用绑定节点的原子入口。"; return false; }
            if (effect == null || effect.kind != Kind)
            {
                error = "回血处理器只接受 HealCurrentHealth 效果。";
                return false;
            }
            if (!_run.TryHealCurrentHealth(
                    effect.amount,
                    out valueBefore,
                    out valueAfter,
                    out error))
                return false;
            appliedAmount = Math.Max(0, valueAfter - valueBefore);
            return true;
        }
    }
}
