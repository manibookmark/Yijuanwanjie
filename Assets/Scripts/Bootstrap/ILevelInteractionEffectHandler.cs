using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Bootstrap 注入的外部交互效果处理器。地图与 Dungeon 无需了解遗物、
    /// 灵印或其它持久化系统；新增外部奖励只需注册对应 kind 的处理器。
    /// </summary>
    public interface ILevelInteractionEffectHandler
    {
        LevelInteractionEffectKind Kind { get; }

        /// <summary>
        /// 提交效果。appliedAmount 是本次实际生效数量（随机发放时与配置
        /// amount 可能不同），会进入回执供奖励反馈展示。
        /// </summary>
        bool TryApply(
            LevelInteractionEffect effect,
            out int valueBefore,
            out int valueAfter,
            out int appliedAmount,
            out string error);
    }

    /// <summary>
    /// An untargeted choice whose rule owner commits state and receipt together.
    /// The executor must preserve that first receipt, including NewlyApplied,
    /// instead of registering a second receipt after the account was credited.
    /// </summary>
    public interface ILevelInteractionAtomicEffectHandler
    {
        bool TryApplyAtomic(string nodeContentId, LevelInteractionEffect effect,
            out LevelInteractionReceipt receipt, out string error);
    }
}
