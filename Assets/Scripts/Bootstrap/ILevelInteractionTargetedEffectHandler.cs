using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 需要玩家先选定具体运行时目标的地图交互处理器。实现必须同时提交业务
    /// 状态和 LevelInteractionReceipt，避免普通外部效果的两步登记窗口。
    /// </summary>
    public interface ILevelInteractionTargetedEffectHandler
    {
        bool TryApplyTargeted(
            string nodeContentId,
            LevelInteractionEffect effect,
            int targetRuntimeInstanceId,
            out LevelInteractionReceipt receipt,
            out string error);
    }

    public readonly struct LevelInteractionExecutionContext
    {
        public string EffectId { get; }
        public int TargetRuntimeInstanceId { get; }
        public bool UseUntargetedHandler { get; }

        public LevelInteractionExecutionContext(
            string effectId,
            int targetRuntimeInstanceId,
            bool useUntargetedHandler = false)
        {
            EffectId = effectId;
            TargetRuntimeInstanceId = targetRuntimeInstanceId;
            UseUntargetedHandler = useUntargetedHandler;
        }

        public bool ShouldUseUntargetedHandler(
            LevelInteractionEffect effect)
        {
            return UseUntargetedHandler && effect != null &&
                   !string.IsNullOrWhiteSpace(EffectId) &&
                   string.Equals(
                       EffectId,
                       effect.effectId,
                       System.StringComparison.Ordinal);
        }

        public bool TryGetTarget(
            LevelInteractionEffect effect,
            out int targetRuntimeInstanceId)
        {
            targetRuntimeInstanceId = 0;
            if (effect == null || TargetRuntimeInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(EffectId) ||
                !string.Equals(EffectId, effect.effectId,
                    System.StringComparison.Ordinal))
                return false;
            targetRuntimeInstanceId = TargetRuntimeInstanceId;
            return targetRuntimeInstanceId > 0;
        }
    }
}
