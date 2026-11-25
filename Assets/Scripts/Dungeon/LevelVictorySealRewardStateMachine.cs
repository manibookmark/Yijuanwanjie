using System;
using System.Collections.Generic;

namespace WanjieScroll.Dungeon
{
    /// <summary>层主单枚灵印奖励尚未终结时的步骤。</summary>
    public enum LevelVictorySealRewardPhase
    {
        None,
        /// <summary>固定多枚灵印奖励尚未选定具体灵印。</summary>
        ChoosingSeal,
        ChoosingDisposition,
        AssigningSeal,
        Completed,
        /// <summary>随机池已合法解析为空，只能领取配置允许的化玉补偿。</summary>
        CashOutOnly
    }

    /// <summary>层主单枚灵印奖励的两个合法终态。</summary>
    public enum LevelVictorySealRewardDisposition
    {
        None,
        Assigned,
        CashedOut,
        CashedOutWithoutSeal
    }

    /// <summary>可恢复的待处理胜利灵印奖励。</summary>
    public readonly struct LevelVictorySealRewardOffer
    {
        public string NodeContentId { get; }
        public string RewardContentId { get; }
        /// <summary>本次奖励允许选择的稳定灵印 ID；顺序即作者配置顺序。</summary>
        public IReadOnlyList<string> SealContentIds { get; }
        /// <summary>已选择的灵印；处于 <see cref="LevelVictorySealRewardPhase.ChoosingSeal"/> 时为空。</summary>
        public string SelectedSealContentId { get; }
        /// <summary>旧单印调用点的兼容别名，等同于已选灵印。</summary>
        public string SealContentId => SelectedSealContentId;
        public string PresentationId { get; }
        public bool AllowsCashOut { get; }
        public LevelVictorySealRewardPhase Phase { get; }
        public bool IsCashOutOnly => Phase == LevelVictorySealRewardPhase.CashOutOnly;

        public LevelVictorySealRewardOffer(
            string nodeContentId,
            string rewardContentId,
            string sealContentId,
            string presentationId,
            LevelVictorySealRewardPhase phase)
            : this(
                nodeContentId,
                rewardContentId,
                new[] { sealContentId },
                sealContentId,
                presentationId,
                allowsCashOut: true,
                phase: phase)
        {
        }

        public LevelVictorySealRewardOffer(
            string nodeContentId,
            string rewardContentId,
            IReadOnlyList<string> sealContentIds,
            string selectedSealContentId,
            string presentationId,
            bool allowsCashOut,
            LevelVictorySealRewardPhase phase)
        {
            NodeContentId = nodeContentId;
            RewardContentId = rewardContentId;
            SealContentIds = CopySealContentIds(sealContentIds);
            SelectedSealContentId = selectedSealContentId;
            PresentationId = presentationId;
            AllowsCashOut = allowsCashOut;
            Phase = phase;
        }

        private static IReadOnlyList<string> CopySealContentIds(
            IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<string>();
            var result = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
                result[i] = source[i];
            return Array.AsReadOnly(result);
        }
    }

    /// <summary>胜利灵印奖励的稳定终态回执。</summary>
    public readonly struct LevelVictorySealRewardReceipt
    {
        public string NodeContentId { get; }
        public string RewardContentId { get; }
        public string SealContentId { get; }
        public LevelVictorySealRewardDisposition Disposition { get; }
        public int CardRuntimeInstanceId { get; }
        public int SlotIndex { get; }
        public int BaseJade { get; }
        public int FinalJade { get; }
        public bool NewlyResolved { get; }
        public bool IsCashOut => Disposition == LevelVictorySealRewardDisposition.CashedOut ||
            Disposition == LevelVictorySealRewardDisposition.CashedOutWithoutSeal;

        public LevelVictorySealRewardReceipt(
            string nodeContentId,
            string rewardContentId,
            string sealContentId,
            LevelVictorySealRewardDisposition disposition,
            int cardRuntimeInstanceId,
            int slotIndex,
            int baseJade,
            int finalJade,
            bool newlyResolved)
        {
            NodeContentId = nodeContentId;
            RewardContentId = rewardContentId;
            SealContentId = sealContentId;
            Disposition = disposition;
            CardRuntimeInstanceId = cardRuntimeInstanceId;
            SlotIndex = slotIndex;
            BaseJade = baseJade;
            FinalJade = finalJade;
            NewlyResolved = newlyResolved;
        }
    }

    /// <summary>
    /// 层主单枚灵印奖励的唯一状态所有者。只保存步骤与终态回执，不读取 UI、
    /// 卡牌、经济或地图关系；调用方在提交外部变更前完成对应规则校验。
    /// </summary>
    public sealed class LevelVictorySealRewardStateMachine
    {
        private readonly Dictionary<string, LevelVictorySealRewardReceipt>
            _receipts = new Dictionary<string,
                LevelVictorySealRewardReceipt>(StringComparer.Ordinal);
        private LevelVictorySealRewardOffer? _pending;

        public bool HasPending => _pending.HasValue;
        internal void RestrictPendingToCashOut()
        {
            if (!_pending.HasValue) return;
            var pending = _pending.Value;
            _pending = new LevelVictorySealRewardOffer(pending.NodeContentId, pending.RewardContentId,
                Array.Empty<string>(), null, pending.PresentationId, true, LevelVictorySealRewardPhase.CashOutOnly);
        }

        public bool TryGetPending(out LevelVictorySealRewardOffer offer)
        {
            if (_pending.HasValue)
            {
                offer = _pending.Value;
                return true;
            }
            offer = default;
            return false;
        }

        public LevelVictorySealRewardPhase GetPhase(string rewardContentId)
        {
            if (string.IsNullOrWhiteSpace(rewardContentId))
                return LevelVictorySealRewardPhase.None;
            if (_receipts.ContainsKey(rewardContentId))
                return LevelVictorySealRewardPhase.Completed;
            if (_pending.HasValue && string.Equals(
                    _pending.Value.RewardContentId,
                    rewardContentId,
                    StringComparison.Ordinal))
            {
                return _pending.Value.Phase;
            }
            return LevelVictorySealRewardPhase.None;
        }

        /// <summary>
        /// 兼容旧的单印奖励定义。单候选无需额外“选印”步骤，直接进入
        /// “钤印／化玉”的处理页。
        /// </summary>
        public bool TryBegin(
            string nodeContentId,
            string rewardContentId,
            string sealContentId,
            string presentationId,
            out LevelVictorySealRewardOffer offer,
            out string error)
            => TryBegin(
                nodeContentId,
                rewardContentId,
                new[] { sealContentId },
                presentationId,
                true,
                out offer,
                out error);

        /// <summary>
        /// 开始固定灵印候选奖励。候选集合属于奖励事务，而不是 UI；多候选
        /// 必须先由调用方提交精确的稳定 ID，再决定宿主或（若允许）化玉。
        /// </summary>
        public bool TryBegin(
            string nodeContentId,
            string rewardContentId,
            IReadOnlyList<string> sealContentIds,
            string presentationId,
            bool allowsCashOut,
            out LevelVictorySealRewardOffer offer,
            out string error)
            => TryBeginCore(nodeContentId, rewardContentId, sealContentIds,
                presentationId, allowsCashOut, false, out offer, out error);

        /// <summary>合法空池的显式出口；不伪造灵印、不提前发放灵玉。</summary>
        public bool TryBeginCashOutOnly(string nodeContentId, string rewardContentId,
            string presentationId, out LevelVictorySealRewardOffer offer, out string error)
            => TryBeginCore(nodeContentId, rewardContentId, Array.Empty<string>(),
                presentationId, true, true, out offer, out error);

        private bool TryBeginCore(string nodeContentId, string rewardContentId,
            IReadOnlyList<string> sealContentIds, string presentationId,
            bool allowsCashOut, bool cashOutOnly,
            out LevelVictorySealRewardOffer offer, out string error)
        {
            offer = default;
            error = null;
            if (string.IsNullOrWhiteSpace(nodeContentId) ||
                string.IsNullOrWhiteSpace(rewardContentId) ||
                (!cashOutOnly && !TryValidateSealContentIds(sealContentIds, out error)))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "胜利灵印奖励缺少稳定节点或奖励 contentId。";
                return false;
            }

            if (_receipts.TryGetValue(
                    rewardContentId,
                    out LevelVictorySealRewardReceipt completed))
            {
                if (!Matches(
                        completed,
                        nodeContentId,
                        rewardContentId,
                        sealContentIds))
                {
                    error = $"胜利奖励 {rewardContentId} 的既有回执与配置不匹配。";
                    return false;
                }
                error = $"胜利奖励 {rewardContentId} 已经完成。";
                return false;
            }
            if (_pending.HasValue)
            {
                LevelVictorySealRewardOffer pending = _pending.Value;
                if (!Matches(
                        pending,
                        nodeContentId,
                        rewardContentId,
                        sealContentIds,
                        allowsCashOut))
                {
                    error = $"仍有未处理的胜利奖励：{pending.RewardContentId}。";
                    return false;
                }
                offer = pending;
                return true;
            }

            string selectedSealContentId = sealContentIds.Count == 1
                ? sealContentIds[0]
                : null;
            LevelVictorySealRewardPhase phase =
                cashOutOnly ? LevelVictorySealRewardPhase.CashOutOnly : selectedSealContentId == null
                    ? LevelVictorySealRewardPhase.ChoosingSeal
                    : LevelVictorySealRewardPhase.ChoosingDisposition;
            offer = new LevelVictorySealRewardOffer(
                nodeContentId,
                rewardContentId,
                sealContentIds,
                selectedSealContentId,
                presentationId,
                allowsCashOut,
                phase);
            _pending = offer;
            return true;
        }

        /// <summary>从固定候选中选择一枚灵印。重复提交同一选择是幂等的。</summary>
        public bool TryChooseSeal(
            string rewardContentId,
            string sealContentId,
            out LevelVictorySealRewardOffer offer,
            out string error)
        {
            offer = default;
            error = null;
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
            {
                return false;
            }
            if (!ContainsSealContentId(pending.SealContentIds, sealContentId))
            {
                error = $"胜利奖励 {rewardContentId} 不包含灵印 {sealContentId}。";
                return false;
            }
            if (pending.Phase ==
                LevelVictorySealRewardPhase.ChoosingDisposition &&
                string.Equals(
                    pending.SelectedSealContentId,
                    sealContentId,
                    StringComparison.Ordinal))
            {
                offer = pending;
                return true;
            }
            if (pending.Phase != LevelVictorySealRewardPhase.ChoosingSeal)
            {
                error = $"胜利奖励 {rewardContentId} 当前不能选择灵印。";
                return false;
            }

            offer = WithSelection(
                pending,
                sealContentId,
                LevelVictorySealRewardPhase.ChoosingDisposition);
            _pending = offer;
            return true;
        }

        public bool TryBeginAssignment(
            string rewardContentId,
            out string error)
        {
            error = null;
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (pending.Phase == LevelVictorySealRewardPhase.AssigningSeal)
                return true;
            if (pending.Phase !=
                LevelVictorySealRewardPhase.ChoosingDisposition)
            {
                error = $"胜利奖励 {rewardContentId} 当前不能进入钤印步骤。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(pending.SelectedSealContentId))
            {
                error = $"胜利奖励 {rewardContentId} 尚未选择灵印。";
                return false;
            }
            _pending = WithPhase(
                pending, LevelVictorySealRewardPhase.AssigningSeal);
            return true;
        }

        public bool TryReturnToDisposition(
            string rewardContentId,
            out string error)
        {
            error = null;
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (pending.Phase ==
                LevelVictorySealRewardPhase.ChoosingDisposition)
                return true;
            if (pending.Phase != LevelVictorySealRewardPhase.AssigningSeal)
            {
                error = $"胜利奖励 {rewardContentId} 当前不能返回处理页。";
                return false;
            }
            _pending = WithPhase(
                pending,
                LevelVictorySealRewardPhase.ChoosingDisposition);
            return true;
        }

        /// <summary>
        /// 放弃当前尚未结算的已选灵印，返回候选页。仅多候选奖励允许此操作，
        /// 避免为旧单印奖励制造不存在的额外步骤。
        /// </summary>
        public bool TryReturnToSealChoice(
            string rewardContentId,
            out string error)
        {
            error = null;
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
            {
                return false;
            }
            if (pending.SealContentIds == null ||
                pending.SealContentIds.Count <= 1)
            {
                error = $"胜利奖励 {rewardContentId} 没有可返回的多印候选。";
                return false;
            }
            if (pending.Phase !=
                    LevelVictorySealRewardPhase.ChoosingDisposition &&
                pending.Phase != LevelVictorySealRewardPhase.AssigningSeal)
            {
                error = $"胜利奖励 {rewardContentId} 当前不能返回灵印候选。";
                return false;
            }

            _pending = WithSelection(
                pending,
                selectedSealContentId: null,
                phase: LevelVictorySealRewardPhase.ChoosingSeal);
            return true;
        }

        public bool TryCompleteAssignment(
            string rewardContentId,
            int cardRuntimeInstanceId,
            int slotIndex,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;
            if (_receipts.TryGetValue(
                    rewardContentId,
                    out LevelVictorySealRewardReceipt completed))
            {
                if (completed.Disposition !=
                        LevelVictorySealRewardDisposition.Assigned ||
                    completed.CardRuntimeInstanceId != cardRuntimeInstanceId ||
                    completed.SlotIndex != slotIndex)
                {
                    error = $"胜利奖励 {rewardContentId} 已以其它方式结算。";
                    return false;
                }
                receipt = AsRetry(completed);
                return true;
            }
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (pending.Phase != LevelVictorySealRewardPhase.AssigningSeal)
            {
                error = $"胜利奖励 {rewardContentId} 尚未进入钤印步骤。";
                return false;
            }
            if (cardRuntimeInstanceId <= 0 || slotIndex < 0)
            {
                error = "钤印终态缺少有效卡牌实例或印位。";
                return false;
            }

            receipt = new LevelVictorySealRewardReceipt(
                pending.NodeContentId,
                pending.RewardContentId,
                pending.SelectedSealContentId,
                LevelVictorySealRewardDisposition.Assigned,
                cardRuntimeInstanceId,
                slotIndex,
                0,
                0,
                newlyResolved: true);
            _receipts.Add(rewardContentId, receipt);
            _pending = null;
            return true;
        }

        public bool TryCompleteCashOut(
            string rewardContentId,
            int baseJade,
            int finalJade,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;
            if (_receipts.TryGetValue(
                    rewardContentId,
                    out LevelVictorySealRewardReceipt completed))
            {
                if (!completed.IsCashOut ||
                    completed.BaseJade != baseJade ||
                    completed.FinalJade != finalJade)
                {
                    error = $"胜利奖励 {rewardContentId} 已以其它方式结算。";
                    return false;
                }
                receipt = AsRetry(completed);
                return true;
            }
            if (!TryRequirePending(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            bool withoutSelection = pending.IsCashOutOnly ||
                pending.Phase == LevelVictorySealRewardPhase.ChoosingSeal;
            if (pending.Phase !=
                LevelVictorySealRewardPhase.ChoosingDisposition && !withoutSelection)
            {
                error = $"胜利奖励 {rewardContentId} 正在选择钤印宿主，" +
                        "需先返回处理页。";
                return false;
            }
            if (baseJade < 0 || finalJade < 0)
            {
                error = "化玉回执不能包含负数。";
                return false;
            }
            if (!pending.AllowsCashOut)
            {
                error = $"胜利奖励 {rewardContentId} 不允许化玉。";
                return false;
            }
            if (!withoutSelection && string.IsNullOrWhiteSpace(pending.SelectedSealContentId))
            {
                error = $"胜利奖励 {rewardContentId} 尚未选择灵印。";
                return false;
            }

            receipt = new LevelVictorySealRewardReceipt(
                pending.NodeContentId,
                pending.RewardContentId,
                pending.SelectedSealContentId,
                withoutSelection ? LevelVictorySealRewardDisposition.CashedOutWithoutSeal :
                    LevelVictorySealRewardDisposition.CashedOut,
                0,
                -1,
                baseJade,
                finalJade,
                newlyResolved: true);
            _receipts.Add(rewardContentId, receipt);
            _pending = null;
            return true;
        }

        public bool TryGetReceipt(
            string rewardContentId,
            out LevelVictorySealRewardReceipt receipt)
        {
            if (!string.IsNullOrWhiteSpace(rewardContentId) &&
                _receipts.TryGetValue(
                    rewardContentId,
                    out LevelVictorySealRewardReceipt completed))
            {
                receipt = AsRetry(completed);
                return true;
            }
            receipt = default;
            return false;
        }

        internal LevelVictorySealRewardStateSaveData CaptureSaveData()
        {
            var data = new LevelVictorySealRewardStateSaveData();
            if (_pending.HasValue)
            {
                LevelVictorySealRewardOffer pending = _pending.Value;
                data.hasPending = true;
                data.pendingNodeContentId = pending.NodeContentId;
                data.pendingRewardContentId = pending.RewardContentId;
                data.pendingSealContentIds = new List<string>(
                    pending.SealContentIds ?? Array.Empty<string>());
                data.pendingSelectedSealContentId =
                    pending.SelectedSealContentId;
                // 保留旧字段，旧版读档与人工诊断仍可读取单印状态。
                data.pendingSealContentId = pending.SealContentId;
                data.pendingPresentationId = pending.PresentationId;
                data.pendingAllowsCashOut = pending.AllowsCashOut;
                data.pendingPhase = pending.Phase;
            }
            foreach (LevelVictorySealRewardReceipt receipt in
                     _receipts.Values)
            {
                data.receipts.Add(ToSaveData(receipt));
            }
            data.receipts.Sort((left, right) => string.CompareOrdinal(
                left.rewardContentId, right.rewardContentId));
            return data;
        }

        internal static LevelVictorySealRewardStateMachine Restore(
            LevelVictorySealRewardStateSaveData data)
        {
            var restored = new LevelVictorySealRewardStateMachine();
            if (data == null)
                return restored;
            if (data.receipts != null)
            {
                for (int i = 0; i < data.receipts.Count; i++)
                {
                    LevelVictorySealRewardReceiptSaveData saved =
                        data.receipts[i];
                    LevelVictorySealRewardReceipt receipt =
                        FromSaveData(saved);
                    if (restored._receipts.ContainsKey(
                            receipt.RewardContentId))
                    {
                        throw new InvalidOperationException(
                            "胜利灵印奖励存档包含重复回执。");
                    }
                    restored._receipts.Add(
                        receipt.RewardContentId, receipt);
                }
            }
            if (data.hasPending)
            {
                if (data.pendingPhase == LevelVictorySealRewardPhase.CashOutOnly)
                {
                    ValidateStableIds(data.pendingNodeContentId, data.pendingRewardContentId,
                        data.pendingSealContentId, allowNoSeal: true);
                    if (!data.pendingAllowsCashOut ||
                        (data.pendingSealContentIds != null && data.pendingSealContentIds.Count != 0) ||
                        !string.IsNullOrWhiteSpace(data.pendingSealContentId) ||
                        !string.IsNullOrWhiteSpace(data.pendingSelectedSealContentId) ||
                        restored._receipts.ContainsKey(data.pendingRewardContentId))
                        throw new InvalidOperationException("胜利灵印奖励存档的空池化玉步骤不合法。");
                    restored._pending = new LevelVictorySealRewardOffer(data.pendingNodeContentId,
                        data.pendingRewardContentId, Array.Empty<string>(), null,
                        data.pendingPresentationId, true, LevelVictorySealRewardPhase.CashOutOnly);
                    return restored;
                }
                List<string> sealContentIds =
                    data.pendingSealContentIds != null &&
                    data.pendingSealContentIds.Count > 0
                        ? new List<string>(data.pendingSealContentIds)
                        : new List<string> { data.pendingSealContentId };
                if (!TryValidateSealContentIds(
                        sealContentIds,
                        out string validationError))
                {
                    throw new InvalidOperationException(
                        "胜利灵印奖励存档候选无效：" + validationError);
                }
                ValidateStableIds(
                    data.pendingNodeContentId,
                    data.pendingRewardContentId,
                    sealContentIds[0]);
                if (data.pendingPhase !=
                        LevelVictorySealRewardPhase.ChoosingSeal &&
                    data.pendingPhase !=
                        LevelVictorySealRewardPhase.ChoosingDisposition &&
                    data.pendingPhase !=
                        LevelVictorySealRewardPhase.AssigningSeal)
                {
                    throw new InvalidOperationException(
                        "胜利灵印奖励存档包含无效待处理步骤。");
                }
                if (restored._receipts.ContainsKey(
                        data.pendingRewardContentId))
                {
                    throw new InvalidOperationException(
                        "同一胜利灵印奖励不能同时处于待处理和已完成状态。");
                }
                string selectedSealContentId =
                    data.pendingSelectedSealContentId;
                if (string.IsNullOrWhiteSpace(selectedSealContentId) &&
                    sealContentIds.Count == 1 &&
                    data.pendingPhase !=
                        LevelVictorySealRewardPhase.ChoosingSeal)
                {
                    // 旧存档只保存 pendingSealContentId，等同单候选且已选中。
                    selectedSealContentId = sealContentIds[0];
                }
                if (data.pendingPhase ==
                    LevelVictorySealRewardPhase.ChoosingSeal)
                {
                    if (sealContentIds.Count <= 1 ||
                        !string.IsNullOrWhiteSpace(selectedSealContentId))
                    {
                        throw new InvalidOperationException(
                            "胜利灵印奖励存档的选印步骤不合法。");
                    }
                }
                else if (!ContainsSealContentId(
                             sealContentIds,
                             selectedSealContentId))
                {
                    throw new InvalidOperationException(
                        "胜利灵印奖励存档缺少已选候选。" );
                }
                restored._pending = new LevelVictorySealRewardOffer(
                    data.pendingNodeContentId,
                    data.pendingRewardContentId,
                    sealContentIds,
                    selectedSealContentId,
                    data.pendingPresentationId,
                    data.pendingAllowsCashOut,
                    data.pendingPhase);
            }
            return restored;
        }

        private bool TryRequirePending(
            string rewardContentId,
            out LevelVictorySealRewardOffer pending,
            out string error)
        {
            pending = default;
            error = null;
            if (!_pending.HasValue)
            {
                error = "当前没有待处理的胜利灵印奖励。";
                return false;
            }
            pending = _pending.Value;
            if (!string.Equals(
                    pending.RewardContentId,
                    rewardContentId,
                    StringComparison.Ordinal))
            {
                error = $"待处理奖励不匹配：{rewardContentId} / " +
                        $"{pending.RewardContentId}。";
                return false;
            }
            return true;
        }

        private static LevelVictorySealRewardOffer WithPhase(
            LevelVictorySealRewardOffer offer,
            LevelVictorySealRewardPhase phase)
        {
            return new LevelVictorySealRewardOffer(
                offer.NodeContentId,
                offer.RewardContentId,
                offer.SealContentIds,
                offer.SelectedSealContentId,
                offer.PresentationId,
                offer.AllowsCashOut,
                phase);
        }

        private static LevelVictorySealRewardOffer WithSelection(
            LevelVictorySealRewardOffer offer,
            string selectedSealContentId,
            LevelVictorySealRewardPhase phase)
        {
            return new LevelVictorySealRewardOffer(
                offer.NodeContentId,
                offer.RewardContentId,
                offer.SealContentIds,
                selectedSealContentId,
                offer.PresentationId,
                offer.AllowsCashOut,
                phase);
        }

        private static bool Matches(
            LevelVictorySealRewardOffer offer,
            string nodeContentId,
            string rewardContentId,
            IReadOnlyList<string> sealContentIds,
            bool allowsCashOut)
        {
            return string.Equals(
                       offer.NodeContentId,
                       nodeContentId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       offer.RewardContentId,
                       rewardContentId,
                       StringComparison.Ordinal) &&
                   offer.AllowsCashOut == allowsCashOut &&
                   SameSealContentIds(
                       offer.SealContentIds,
                       sealContentIds);
        }

        private static bool Matches(
            LevelVictorySealRewardReceipt receipt,
            string nodeContentId,
            string rewardContentId,
            IReadOnlyList<string> sealContentIds)
        {
            return string.Equals(
                       receipt.NodeContentId,
                       nodeContentId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       receipt.RewardContentId,
                       rewardContentId,
                       StringComparison.Ordinal) &&
                   ContainsSealContentId(
                       sealContentIds,
                       receipt.SealContentId);
        }

        private static bool TryValidateSealContentIds(
            IReadOnlyList<string> sealContentIds,
            out string error)
        {
            error = null;
            if (sealContentIds == null || sealContentIds.Count == 0)
            {
                error = "胜利灵印奖励至少需要一个稳定灵印 contentId。";
                return false;
            }
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < sealContentIds.Count; i++)
            {
                string contentId = sealContentIds[i];
                if (string.IsNullOrWhiteSpace(contentId))
                {
                    error = "胜利灵印奖励包含空灵印 contentId。";
                    return false;
                }
                if (!known.Add(contentId))
                {
                    error = $"胜利灵印奖励包含重复灵印：{contentId}。";
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsSealContentId(
            IReadOnlyList<string> sealContentIds,
            string sealContentId)
        {
            if (sealContentIds == null ||
                string.IsNullOrWhiteSpace(sealContentId))
            {
                return false;
            }
            for (int i = 0; i < sealContentIds.Count; i++)
            {
                if (string.Equals(
                        sealContentIds[i],
                        sealContentId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameSealContentIds(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(
                        left[i],
                        right[i],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static LevelVictorySealRewardReceipt AsRetry(
            LevelVictorySealRewardReceipt receipt)
        {
            return new LevelVictorySealRewardReceipt(
                receipt.NodeContentId,
                receipt.RewardContentId,
                receipt.SealContentId,
                receipt.Disposition,
                receipt.CardRuntimeInstanceId,
                receipt.SlotIndex,
                receipt.BaseJade,
                receipt.FinalJade,
                newlyResolved: false);
        }

        private static LevelVictorySealRewardReceiptSaveData ToSaveData(
            LevelVictorySealRewardReceipt receipt)
        {
            return new LevelVictorySealRewardReceiptSaveData
            {
                nodeContentId = receipt.NodeContentId,
                rewardContentId = receipt.RewardContentId,
                sealContentId = receipt.SealContentId,
                disposition = receipt.Disposition,
                cardRuntimeInstanceId = receipt.CardRuntimeInstanceId,
                slotIndex = receipt.SlotIndex,
                baseJade = receipt.BaseJade,
                finalJade = receipt.FinalJade
            };
        }

        private static LevelVictorySealRewardReceipt FromSaveData(
            LevelVictorySealRewardReceiptSaveData data)
        {
            if (data == null)
                throw new InvalidOperationException(
                    "胜利灵印奖励存档包含空回执。");
            ValidateStableIds(
                data.nodeContentId,
                data.rewardContentId,
                data.sealContentId,
                allowNoSeal: data.disposition == LevelVictorySealRewardDisposition.CashedOutWithoutSeal);
            if (data.disposition !=
                    LevelVictorySealRewardDisposition.Assigned &&
                data.disposition !=
                    LevelVictorySealRewardDisposition.CashedOut &&
                data.disposition != LevelVictorySealRewardDisposition.CashedOutWithoutSeal)
            {
                throw new InvalidOperationException(
                    "胜利灵印奖励存档包含无效终态。");
            }
            if (data.disposition ==
                LevelVictorySealRewardDisposition.Assigned)
            {
                if (data.cardRuntimeInstanceId <= 0 ||
                    data.slotIndex < 0)
                {
                    throw new InvalidOperationException(
                        "已钤印回执缺少有效卡牌实例或印位。");
                }
                if (data.baseJade != 0 || data.finalJade != 0)
                {
                    throw new InvalidOperationException(
                        "已钤印回执不能同时包含化玉结果。");
                }
            }
            else
            {
                if (data.disposition == LevelVictorySealRewardDisposition.CashedOutWithoutSeal &&
                    !string.IsNullOrWhiteSpace(data.sealContentId))
                    throw new InvalidOperationException("未选印化玉回执不能包含灵印 ID。");
                if (data.cardRuntimeInstanceId != 0 ||
                    data.slotIndex != -1)
                {
                    throw new InvalidOperationException(
                        "化玉回执不能同时包含钤印目标。");
                }
                if (data.baseJade < 0 || data.finalJade < 0)
                {
                    throw new InvalidOperationException(
                        "化玉回执不能包含负数。");
                }
            }
            return new LevelVictorySealRewardReceipt(
                data.nodeContentId,
                data.rewardContentId,
                data.sealContentId,
                data.disposition,
                data.cardRuntimeInstanceId,
                data.slotIndex,
                data.baseJade,
                data.finalJade,
                newlyResolved: false);
        }

        private static void ValidateStableIds(
            string nodeContentId,
            string rewardContentId,
            string sealContentId, bool allowNoSeal = false)
        {
            if (string.IsNullOrWhiteSpace(nodeContentId) ||
                string.IsNullOrWhiteSpace(rewardContentId) ||
                (!allowNoSeal && string.IsNullOrWhiteSpace(sealContentId)))
            {
                throw new InvalidOperationException(
                    "胜利灵印奖励存档缺少稳定 contentId。");
            }
        }
    }

    [Serializable]
    internal sealed class LevelVictorySealRewardStateSaveData
    {
        public bool hasPending;
        public string pendingNodeContentId;
        public string pendingRewardContentId;
        /// <summary>v2+ 的全部固定候选；缺省时回退到旧单印字段。</summary>
        public List<string> pendingSealContentIds = new List<string>();
        /// <summary>多印候选中已经确认的稳定 ID；选印阶段为空。</summary>
        public string pendingSelectedSealContentId;
        /// <summary>旧单印存档兼容字段。</summary>
        public string pendingSealContentId;
        public string pendingPresentationId;
        public bool pendingAllowsCashOut = true;
        public LevelVictorySealRewardPhase pendingPhase;
        public List<LevelVictorySealRewardReceiptSaveData> receipts =
            new List<LevelVictorySealRewardReceiptSaveData>();
    }

    [Serializable]
    internal sealed class LevelVictorySealRewardReceiptSaveData
    {
        public string nodeContentId;
        public string rewardContentId;
        public string sealContentId;
        public LevelVictorySealRewardDisposition disposition;
        public int cardRuntimeInstanceId;
        public int slotIndex;
        public int baseJade;
        public int finalJade;
    }
}
