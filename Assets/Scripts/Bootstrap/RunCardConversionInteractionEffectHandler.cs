using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// ConvertCard 的规则解析与提交适配器。精确卡牌选择交给 Run 原子事务；
    /// “化为灵玉”沿用注入的地图经济账本，不让 Dungeon 依赖 Meta。
    /// </summary>
    public sealed class RunCardConversionInteractionEffectHandler :
        ILevelInteractionEffectHandler,
        ILevelInteractionTargetedEffectHandler,
        ILevelInteractionAtomicEffectHandler
    {
        private readonly DungeonRunSession _run;
        private readonly Func<IReadOnlyList<CardConfig>> _getCardPool;
        private readonly Func<string, RunCardConversionRuleProfile>
            _resolveProfile;
        private readonly Func<int> _getJade;
        private readonly Func<int, int> _grantJade;
        private readonly Func<int, int> _previewJade;
        private readonly IDungeonRunInteractionWallet _wallet;
        private readonly Action _persist;
        private readonly Dictionary<string, CashOutAttempt> _cashOutAttempts =
            new Dictionary<string, CashOutAttempt>(
                StringComparer.OrdinalIgnoreCase);

        private readonly struct CashOutAttempt
        {
            public int BaseAmount { get; }
            public int Before { get; }
            public int After { get; }
            public int AppliedAmount { get; }

            public CashOutAttempt(
                int baseAmount,
                int before,
                int after,
                int appliedAmount)
            {
                BaseAmount = baseAmount;
                Before = before;
                After = after;
                AppliedAmount = appliedAmount;
            }
        }

        public LevelInteractionEffectKind Kind =>
            LevelInteractionEffectKind.ConvertCard;

        /// <summary>Production constructor: both dispositions use the shared Run transaction.</summary>
        public RunCardConversionInteractionEffectHandler(
            DungeonRunSession run,
            Func<IReadOnlyList<CardConfig>> getCardPool,
            IDungeonRunInteractionWallet wallet,
            Func<string, RunCardConversionRuleProfile> resolveProfile = null,
            Action persist = null)
            : this(run, getCardPool, () => wallet.Balance,
                _ => throw new InvalidOperationException("Transactional conversion cannot bypass its wallet receipt."),
                resolveProfile, amount => wallet.Preview(amount).FinalJade)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _persist = persist;
        }

        /// <summary>Compatibility for old direct callers; scene composition uses the wallet constructor.</summary>
        public RunCardConversionInteractionEffectHandler(
            DungeonRunSession run,
            Func<IReadOnlyList<CardConfig>> getCardPool,
            Func<int> getJade,
            Func<int, int> grantJade,
            Func<string, RunCardConversionRuleProfile> resolveProfile = null,
            Func<int, int> previewJade = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _getCardPool = getCardPool ??
                throw new ArgumentNullException(nameof(getCardPool));
            _getJade = getJade ?? (() => 0);
            _grantJade = grantJade ??
                throw new ArgumentNullException(nameof(grantJade));
            _previewJade = previewJade ?? (amount => Math.Max(0, amount));
            _resolveProfile = resolveProfile ?? RunCardConversionRuleCatalog.Get;
        }

        public int PreviewCashOut(LevelInteractionEffect effect)
        {
            return TryResolve(effect, out RunCardConversionRuleProfile profile, out _) &&
                profile.AllowsCashOut
                    ? Math.Max(0, _previewJade(Math.Max(0, effect.amount))) : 0;
        }

        public int PreviewConversionAmount(RunCardConversionRule rule)
            => RunCardConversionChoices.GetEffectiveAmount(_run, rule);

        public bool TryGetCandidates(
            LevelInteractionEffect effect,
            out RunCardConversionRuleProfile profile,
            out List<CardConfig> candidates,
            out string error)
        {
            candidates = new List<CardConfig>();
            if (!TryResolve(effect, out profile, out error))
                return false;
            IReadOnlyList<CardConfig> pool = _getCardPool();
            return RunCardConversionChoices.TryGetCandidateCards(
                _run, effect, profile, pool, out candidates, out error);
        }

        public bool TryApplyTargeted(
            string nodeContentId,
            LevelInteractionEffect effect,
            int targetRuntimeInstanceId,
            out LevelInteractionReceipt receipt,
            out string error)
        {
            receipt = default;
            if (!TryResolve(effect, out RunCardConversionRuleProfile profile,
                    out error))
                return false;
            return RunCardConversionChoices.TryConvert(
                _run,
                nodeContentId,
                effect,
                targetRuntimeInstanceId,
                profile,
                _getCardPool(),
                out receipt,
                out error,
                _persist);
        }

        public bool TryApplyAtomic(string nodeContentId, LevelInteractionEffect effect,
            out LevelInteractionReceipt receipt, out string error)
        {
            receipt = default;
            if (_wallet == null) { error = "Atomic conversion cash-out requires the production wallet constructor."; return false; }
            if (!TryResolve(effect, out RunCardConversionRuleProfile profile, out error)) return false;
            return RunCardConversionChoices.TryCashOut(_run, nodeContentId, effect, profile,
                _getCardPool(), _wallet, out receipt, out error, _persist);
        }

        /// <summary>无精确目标时选择化玉；正式钱包构造会同时提交回执，旧直接调用保持兼容。</summary>
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
            if (_wallet != null)
            {
                if (!TryApplyAtomic(_run.CurrentFloor.ActiveInteractionNodeId, effect,
                        out LevelInteractionReceipt receipt, out error)) return false;
                valueBefore = receipt.ValueBefore;
                valueAfter = receipt.ValueAfter;
                appliedAmount = receipt.Amount;
                return true;
            }
            if (!TryResolve(effect, out RunCardConversionRuleProfile profile, out error))
                return false;
            if (!profile.AllowsCashOut)
            {
                error = "此交互需要选择一张牌转化为灵力。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(effect.effectId) &&
                _cashOutAttempts.TryGetValue(
                    effect.effectId, out CashOutAttempt previous))
            {
                if (previous.BaseAmount != effect.amount)
                {
                    error = $"归元化玉效果 {effect.effectId} 的金额在重试时发生变化。";
                    return false;
                }
                valueBefore = previous.Before;
                valueAfter = previous.After;
                appliedAmount = previous.AppliedAmount;
                return true;
            }
            valueAfter = _grantJade(effect.amount);
            appliedAmount = Math.Max(0, valueAfter - valueBefore);
            if (!string.IsNullOrWhiteSpace(effect.effectId))
            {
                _cashOutAttempts[effect.effectId] = new CashOutAttempt(
                    effect.amount,
                    valueBefore,
                    valueAfter,
                    appliedAmount);
            }
            return true;
        }

        private bool TryResolve(
            LevelInteractionEffect effect,
            out RunCardConversionRuleProfile profile,
            out string error)
        {
            profile = null;
            error = null;
            if (effect == null || effect.kind != Kind)
            {
                error = "归元处理器只接受 ConvertCard 效果。";
                return false;
            }
            try
            {
                profile = _resolveProfile(effect.targetContentId);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (profile == null)
            {
                error = $"找不到归元规则：{effect.targetContentId}。";
                return false;
            }
            return RunCardConversionChoices.TryValidateProfile(effect, profile, _getCardPool(), out error);
        }
    }
}
