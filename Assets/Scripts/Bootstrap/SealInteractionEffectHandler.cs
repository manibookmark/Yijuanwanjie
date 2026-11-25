using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// GrantSeal 的目标查询与原子提交适配器。Run 会话仍是卡牌实例状态的
    /// 唯一写入者；本类只解析稳定灵印内容并提供合法宿主快照。
    /// </summary>
    public sealed class SealInteractionEffectHandler :
        ILevelInteractionEffectHandler,
        ILevelInteractionTargetedEffectHandler
    {
        private readonly DungeonRunSession _run;
        private readonly Func<IReadOnlyList<CardConfig>> _getCardPool;
        private readonly Func<string, LingyinConfig> _resolveSeal;
        private readonly Func<int> _getJade;
        private readonly Func<int> _previewCashOut;
        private readonly Func<JadeGainResult> _cashOut;
        private readonly Dictionary<string, CashOutAttempt> _cashOutAttempts =
            new Dictionary<string, CashOutAttempt>(
                StringComparer.OrdinalIgnoreCase);

        private readonly struct CashOutAttempt
        {
            public int Before { get; }
            public int After { get; }
            public int AppliedAmount { get; }

            public CashOutAttempt(
                int before,
                int after,
                int appliedAmount)
            {
                Before = before;
                After = after;
                AppliedAmount = appliedAmount;
            }
        }

        public LevelInteractionEffectKind Kind =>
            LevelInteractionEffectKind.GrantSeal;
        public bool CanCashOut => _cashOut != null;

        public SealInteractionEffectHandler(
            DungeonRunSession run,
            Func<IReadOnlyList<CardConfig>> getCardPool,
            Func<string, LingyinConfig> resolveSeal = null,
            Func<int> getJade = null,
            Func<int> previewCashOut = null,
            Func<JadeGainResult> cashOut = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _getCardPool = getCardPool ??
                throw new ArgumentNullException(nameof(getCardPool));
            _resolveSeal = resolveSeal ?? ResolveFromCatalog;
            _getJade = getJade ?? (() => 0);
            _previewCashOut = previewCashOut ?? (() => 0);
            _cashOut = cashOut;
        }

        public int PreviewCashOut()
            => CanCashOut ? Math.Max(0, _previewCashOut()) : 0;

        public bool TryGetCandidates(
            LevelInteractionEffect effect,
            out LingyinConfig seal,
            out List<CardConfig> candidates,
            out string error)
        {
            seal = null;
            candidates = new List<CardConfig>();
            error = null;
            if (!TryResolve(effect, out seal, out error))
                return false;
            if (!TryBuildCandidates(seal, out candidates, out error))
                return false;
            if (candidates.Count == 0)
            {
                error = $"灵印 {seal.contentId} 没有合法宿主。";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 为地图 GrantSeal 取得灵印与合法宿主快照。零候选是有效结果，
        /// 由上层只保留“化为灵玉”；内容解析或 Run 牌组不可用才返回失败。
        /// </summary>
        public bool TryGetCandidateSnapshot(
            LevelInteractionEffect effect,
            out LingyinConfig seal,
            out List<CardConfig> candidates,
            out string error)
        {
            seal = null;
            candidates = new List<CardConfig>();
            error = null;
            if (!TryResolve(effect, out seal, out error))
                return false;
            return TryBuildCandidates(seal, out candidates, out error);
        }

        /// <summary>
        /// 为胜利奖励取得灵印与合法宿主快照。零候选是有效结果，由上层只保留
        /// “化为灵玉”；内容解析或 Run 牌组不可用才返回失败。
        /// </summary>
        public bool TryGetCandidateSnapshot(
            string sealContentId,
            out LingyinConfig seal,
            out List<CardConfig> candidates,
            out string error)
        {
            seal = null;
            candidates = new List<CardConfig>();
            error = null;
            if (!TryResolve(sealContentId, out seal, out error))
                return false;
            return TryBuildCandidates(seal, out candidates, out error);
        }

        private bool TryBuildCandidates(
            LingyinConfig seal,
            out List<CardConfig> candidates,
            out string error)
        {
            candidates = new List<CardConfig>();
            error = null;
            if (!_run.CanReceiveSealRewards)
                return true;
            IReadOnlyList<CardConfig> pool = _getCardPool();
            if (!_run.TryGetCurrentCards(pool, out List<CardConfig> cards,
                    out error))
                return false;
            CardSealSpec spec = seal.ToCardSealSpec();
            for (int i = 0; i < cards.Count; i++)
            {
                if (CardSealRules.CanAssign(cards[i], spec,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult))
                    candidates.Add(cards[i]);
            }
            return true;
        }

        public bool TryApplyTargeted(
            string nodeContentId,
            LevelInteractionEffect effect,
            int targetRuntimeInstanceId,
            out LevelInteractionReceipt receipt,
            out string error)
        {
            receipt = default;
            if (!TryResolve(effect, out LingyinConfig seal, out error))
                return false;
            return _run.TryAssignSeal(
                nodeContentId,
                effect,
                targetRuntimeInstanceId,
                seal.ToCardSealSpec(),
                _getCardPool(),
                out receipt,
                out error,
                isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult);
        }

        public bool TryApply(
            LevelInteractionEffect effect,
            out int valueBefore,
            out int valueAfter,
            out int appliedAmount,
            out string error)
        {
            valueBefore = _getJade();
            valueAfter = valueBefore;
            appliedAmount = 0;
            if (!TryResolve(effect, out _, out error))
                return false;
            if (effect.amount != 1)
            {
                error = $"即时灵印效果 {effect.effectId} 的 amount 必须为 1。";
                return false;
            }
            if (!CanCashOut)
            {
                error = "GrantSeal 必须先提供精确 cardInstanceId。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(effect.effectId) &&
                _cashOutAttempts.TryGetValue(
                    effect.effectId, out CashOutAttempt previous))
            {
                valueBefore = previous.Before;
                valueAfter = previous.After;
                appliedAmount = previous.AppliedAmount;
                return true;
            }

            JadeGainResult result = _cashOut();
            valueAfter = _getJade();
            appliedAmount = Math.Max(0, result.FinalJade);
            if (!string.IsNullOrWhiteSpace(effect.effectId))
            {
                _cashOutAttempts[effect.effectId] = new CashOutAttempt(
                    valueBefore,
                    valueAfter,
                    appliedAmount);
            }
            return true;
        }

        private bool TryResolve(
            LevelInteractionEffect effect,
            out LingyinConfig seal,
            out string error)
        {
            seal = null;
            error = null;
            if (effect == null || effect.kind != Kind)
            {
                error = "灵印处理器只接受 GrantSeal 效果。";
                return false;
            }
            return TryResolve(effect.targetContentId, out seal, out error);
        }

        private bool TryResolve(
            string sealContentId,
            out LingyinConfig seal,
            out string error)
        {
            seal = _resolveSeal(sealContentId);
            if (seal == null)
            {
                error = $"找不到灵印内容：{sealContentId}。";
                return false;
            }
            if (!seal.IsValid(out error))
                return false;
            return true;
        }

        private static LingyinConfig ResolveFromCatalog(string contentId)
            => LingyinCatalog.TryGet(contentId, out LingyinConfig config)
                ? config
                : null;
    }
}
