using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 地图交互完成效果的类型化执行器。Dungeon 只保存状态和 Run 修正，
    /// 外部经济写入由 Bootstrap 注入，因此新增节点无需改地图表现代码。
    /// </summary>
    public sealed class LevelInteractionEffectExecutor
    {
        private readonly LevelRunSession _session;
        private readonly Dictionary<LevelInteractionEffectKind,
            ILevelInteractionEffectHandler> _externalHandlers =
                new Dictionary<LevelInteractionEffectKind,
                    ILevelInteractionEffectHandler>();

        public LevelInteractionEffectExecutor(
            LevelRunSession session,
            Func<int> getJade,
            Func<int, int> grantJade,
            IRandomSource randomSource = null)
            : this(session, getJade, grantJade, null, randomSource)
        {
        }

        public LevelInteractionEffectExecutor(
            LevelRunSession session,
            Func<int> getJade,
            Func<int, int> grantJade,
            IEnumerable<ILevelInteractionEffectHandler> externalHandlers,
            IRandomSource randomSource = null)
            : this(
                session,
                BuildHandlers(getJade, grantJade, externalHandlers,
                    randomSource))
        {
        }

        public LevelInteractionEffectExecutor(
            LevelRunSession session,
            IEnumerable<ILevelInteractionEffectHandler> externalHandlers)
        {
            _session = session ??
                throw new ArgumentNullException(nameof(session));
            if (externalHandlers == null) return;
            foreach (ILevelInteractionEffectHandler handler in externalHandlers)
            {
                if (handler == null) continue;
                if (!_externalHandlers.TryAdd(handler.Kind, handler))
                {
                    throw new ArgumentException(
                        $"交互效果处理器重复：{handler.Kind}。",
                        nameof(externalHandlers));
                }
            }
        }

        public bool TryExecute(
            string nodeContentId,
            out IReadOnlyList<LevelInteractionReceipt> receipts,
            out string error)
            => TryExecute(
                nodeContentId,
                default,
                out receipts,
                out error);

        public bool TryExecute(
            string nodeContentId,
            LevelInteractionExecutionContext context,
            out IReadOnlyList<LevelInteractionReceipt> receipts,
            out string error)
        {
            var applied = new List<LevelInteractionReceipt>();
            receipts = applied;
            error = null;

            LevelNode node = _session.FindNode(nodeContentId);
            if (node == null ||
                (node.nodeType != LevelNodeType.Interaction &&
                 node.nodeType != LevelNodeType.Exit))
            {
                error = $"节点不是可执行交互：{nodeContentId}。";
                return false;
            }

            if (node.completionEffects == null ||
                node.completionEffects.Count == 0)
            {
                if (node.nodeType == LevelNodeType.Exit)
                    return true;
                error = $"交互节点没有完成效果：{nodeContentId}。";
                return false;
            }

            // Reject the whole Cart operation before applying any preceding effect.
            // Destruction and pickup have separate durable receipts and owners.
            foreach (var candidate in node.completionEffects)
            {
                if (candidate?.kind != LevelInteractionEffectKind.AbandonedCart) continue;
                error = "弃车需要逐次撞击与独立护腕拾取，不能通过普通完成效果执行。";
                return false;
            }

            for (int i = 0; i < node.completionEffects.Count; i++)
            {
                LevelInteractionEffect effect = node.completionEffects[i];
                if (effect == null)
                {
                    error = $"交互节点 {nodeContentId} 包含空效果。";
                    return false;
                }

                if (effect.kind == LevelInteractionEffectKind.ManaStatue)
                {
                    error = "灵气神像需要逐次撞击与独立拾取，不能通过普通完成效果执行。";
                    return false;
                }

                if (effect.kind == LevelInteractionEffectKind.ChickenOffering ||
                    effect.kind == LevelInteractionEffectKind.ManaOffering)
                {
                    error = "供奉需要选择本局卡牌实例并通过供奉事务保存，不能执行普通完成效果。";
                    return false;
                }

                if (effect.kind == LevelInteractionEffectKind.ConvertCard &&
                    context.UseUntargetedHandler && context.TargetRuntimeInstanceId != 0)
                {
                    error = "Conversion cash-out cannot include a card target.";
                    return false;
                }

                if (effect.kind == LevelInteractionEffectKind.HealCurrentHealth &&
                    (context.TargetRuntimeInstanceId != 0 ||
                     (context.EffectId != null && context.EffectId != effect.effectId) ||
                     (context.UseUntargetedHandler && context.EffectId == null)))
                {
                    error = "回血效果不接受卡牌目标或不匹配的 effectId。";
                    return false;
                }

                if (_session.TryGetInteractionReceipt(
                        effect.effectId,
                        out LevelInteractionReceipt retryReceipt))
                {
                    if (effect.kind == LevelInteractionEffectKind.ConvertCard)
                    {
                        // Resume a saved Completing checkpoint without asking for the consumed card again.
                        // Explicit intent must still match; only the genuine default context can be reconstructed.
                        LevelInteractionExecutionContext replayContext = context;
                        if (context.EffectId == null && context.TargetRuntimeInstanceId == 0 && !context.UseUntargetedHandler)
                            replayContext = new LevelInteractionExecutionContext(effect.effectId,
                                retryReceipt.TargetRuntimeInstanceId, retryReceipt.TargetRuntimeInstanceId == 0);
                        bool cashOut = replayContext.ShouldUseUntargetedHandler(effect);
                        bool hasTarget = replayContext.TryGetTarget(effect, out int targetId);
                        if ((cashOut && retryReceipt.TargetRuntimeInstanceId != 0) ||
                            (!cashOut && (!hasTarget || targetId != retryReceipt.TargetRuntimeInstanceId)))
                        {
                            error = "The conversion receipt is already bound to a different disposition or card instance.";
                            return false;
                        }
                        // The owner validates replay and recovery guards, without saving/paying/consuming again.
                        if (!TryApplyExternalEffect(nodeContentId, effect, replayContext, out retryReceipt, out error))
                            return false;
                    }
                    if (effect.kind == LevelInteractionEffectKind.HealCurrentHealth &&
                        !TryApplyExternalEffect(nodeContentId, effect, context, out retryReceipt, out error))
                        return false;
                    applied.Add(retryReceipt);
                    continue;
                }

                bool succeeded;
                LevelInteractionReceipt receipt;
                switch (effect.kind)
                {
                    case LevelInteractionEffectKind.AddBaseMana:
                    case LevelInteractionEffectKind.AddBaseArmor:
                    case LevelInteractionEffectKind.AddHandLimit:
                    case LevelInteractionEffectKind.AddMight:
                    case LevelInteractionEffectKind.AddArea:
                    case LevelInteractionEffectKind.AddRewardReroll:
                    case LevelInteractionEffectKind.RecordStoryClue:
                        succeeded = _session.TryApplyRunEffect(
                            nodeContentId,
                            effect,
                            out receipt,
                            out error);
                        break;
                    default:
                        succeeded = TryApplyExternalEffect(
                            nodeContentId,
                            effect,
                            context,
                            out receipt,
                            out error);
                        break;
                }

                if (!succeeded)
                    return false;
                applied.Add(receipt);
            }

            return true;
        }

        private bool TryApplyExternalEffect(
            string nodeContentId,
            LevelInteractionEffect effect,
            LevelInteractionExecutionContext context,
            out LevelInteractionReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;
            if (effect.amount <= 0)
            {
                error = $"交互效果 {effect.effectId} 的 amount 必须大于 0。";
                return false;
            }

            if (!_externalHandlers.TryGetValue(
                    effect.kind, out ILevelInteractionEffectHandler handler))
            {
                error = $"未注册交互效果处理器：{effect.kind} " +
                        $"({effect.effectId})。";
                return false;
            }

            // Targetless healing always uses the Run owner, including the genuine default context.
            if (effect.kind == LevelInteractionEffectKind.HealCurrentHealth &&
                !(handler is ILevelInteractionAtomicEffectHandler))
            { error = "地图回血需要原子交互处理器。"; return false; }
            if ((effect.kind == LevelInteractionEffectKind.HealCurrentHealth ||
                 context.ShouldUseUntargetedHandler(effect)) &&
                handler is ILevelInteractionAtomicEffectHandler atomic)
            {
                try { return atomic.TryApplyAtomic(nodeContentId, effect, out receipt, out error); }
                catch (Exception exception)
                {
                    error = $"原子交互效果 {effect.effectId} 提交失败：{exception.Message}";
                    return false;
                }
            }

            if (handler is ILevelInteractionTargetedEffectHandler targeted &&
                !context.ShouldUseUntargetedHandler(effect))
            {
                if (!context.TryGetTarget(
                        effect, out int targetRuntimeInstanceId))
                {
                    error = $"交互效果 {effect.effectId} 缺少运行时目标。";
                    return false;
                }
                try
                {
                    return targeted.TryApplyTargeted(
                        nodeContentId,
                        effect,
                        targetRuntimeInstanceId,
                        out receipt,
                        out error);
                }
                catch (Exception exception)
                {
                    error = $"目标效果 {effect.effectId} 提交失败：" +
                            exception.Message;
                    return false;
                }
            }

            int before;
            int after;
            int appliedAmount;
            try
            {
                if (!handler.TryApply(
                        effect, out before, out after,
                        out appliedAmount, out error))
                    return false;
            }
            catch (Exception exception)
            {
                error = $"外部效果 {effect.effectId} 提交失败：" +
                        exception.Message;
                return false;
            }

            if (after < before)
            {
                error = $"外部效果 {effect.effectId} 返回了无效结果：" +
                        $"{before} -> {after}。";
                return false;
            }
            if (appliedAmount < 0)
            {
                error = $"外部效果 {effect.effectId} 返回了无效数量：" +
                        appliedAmount + "。";
                return false;
            }

            return _session.TryRecordExternalEffect(
                nodeContentId,
                effect,
                before,
                after,
                appliedAmount,
                out receipt,
                out error);
        }

        private static IEnumerable<ILevelInteractionEffectHandler>
            BuildHandlers(
                Func<int> getJade,
                Func<int, int> grantJade,
                IEnumerable<ILevelInteractionEffectHandler> externalHandlers,
                IRandomSource randomSource)
        {
            var handlers = new List<ILevelInteractionEffectHandler>
            {
                new JadeEffectHandler(getJade, grantJade, randomSource)
            };
            if (externalHandlers != null)
            {
                foreach (ILevelInteractionEffectHandler handler in
                         externalHandlers)
                {
                    if (handler != null)
                        handlers.Add(handler);
                }
            }
            return handlers;
        }

        private sealed class JadeEffectHandler : ILevelInteractionEffectHandler
        {
            private readonly Func<int> _getJade;
            private readonly Func<int, int> _grantJade;
            private readonly IRandomSource _random;

            public LevelInteractionEffectKind Kind =>
                LevelInteractionEffectKind.GrantJade;

            public JadeEffectHandler(
                Func<int> getJade,
                Func<int, int> grantJade,
                IRandomSource randomSource)
            {
                _getJade = getJade ?? (() => 0);
                _grantJade = grantJade ??
                    throw new ArgumentNullException(nameof(grantJade));
                _random = randomSource ?? DefaultRandomSource.Instance;
            }

            public bool TryApply(
                LevelInteractionEffect effect,
                out int valueBefore,
                out int valueAfter,
                out int appliedAmount,
                out string error)
            {
                error = null;
                int rolled = effect.amountMax > effect.amount
                    ? _random.Range(effect.amount, effect.amountMax + 1)
                    : effect.amount;
                valueBefore = _getJade();
                valueAfter = _grantJade(rolled);
                appliedAmount = Math.Max(0, valueAfter - valueBefore);
                return true;
            }
        }
    }
}
