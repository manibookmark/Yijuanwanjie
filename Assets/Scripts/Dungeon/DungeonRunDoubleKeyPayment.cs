using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the paid-box ledger and two-key eligibility. Both cards are checked
    /// on a detached Run draft before either is removed. Payment opens the
    /// existing seal-choice checkpoint; reward disposition stays in Interactions.
    /// The Run owns deck/assignment rollback, while this owner rolls back its
    /// ledger and the newly opened checkpoint if the atomic save fails.
    /// </summary>
    public sealed class DungeonRunDoubleKeyPayment
    {
        private readonly DungeonRunSession _run;
        private readonly DungeonRunDoubleKeyPaymentSaveData _state;
        internal bool IsCommitting { get; private set; }

        internal DungeonRunDoubleKeyPayment(DungeonRunSession run, DungeonRunDoubleKeyPaymentSaveData saved = null)
        {
            _run = run;
            _state = saved == null
                ? new DungeonRunDoubleKeyPaymentSaveData { version = 1, payments = new List<DungeonRunDoubleKeyPaymentEntry>() }
                : Clone(saved);
        }

        public bool RequiresPayment(string nodeId)
        {
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            return node?.completionEffects?.Count == 1 && node.completionEffects[0]?.HasDoubleKeyPayment == true &&
                Find(_run.CurrentFloorContentId, nodeId) == null;
        }

        /// <summary>Inspect never reserves a card, opens a checkpoint or writes a save.</summary>
        public bool TryInspect(string nodeId, IReadOnlyList<CardConfig> cardPool,
            out DungeonRunDoubleKeyPaymentOffer offer, out string error)
        {
            offer = null;
            if (!TryRequireEffect(nodeId, out LevelInteractionEffect effect, out error)) return false;
            var first = new List<DungeonRunDoubleKeyCandidate>();
            var second = new List<DungeonRunDoubleKeyCandidate>();
            bool paid = Find(_run.CurrentFloorContentId, nodeId) != null;
            if (!paid)
            {
                if (!TryRequireAvailable(nodeId, out error)) return false;
                foreach (DungeonRunCardSaveData saved in _run.CaptureInteractionDraft().Cards)
                {
                    CardConfig source = null;
                    try
                    {
                        source = DungeonRunCardCodec.Restore(saved, DungeonRunCardCodec.CreateResolver(cardPool));
                        if (!IsLegalSource(source)) continue;
                        if (source.color == effect.doubleKeyPayment.firstColor)
                            first.Add(new DungeonRunDoubleKeyCandidate(saved));
                        if (source.color == effect.doubleKeyPayment.secondColor)
                            second.Add(new DungeonRunDoubleKeyCandidate(saved));
                    }
                    catch (Exception exception) { error = exception.Message; return false; }
                    finally { DungeonRunCardCodec.Release(source); }
                }
            }
            offer = new DungeonRunDoubleKeyPaymentOffer(effect.doubleKeyPayment, first, second, paid);
            return true;
        }

        /// <summary>
        /// IDs correspond to the first/second authored native colors. A repeated
        /// exact request returns its receipt, including after reward completion
        /// or leaving the floor; a different pair can never repay that box.
        /// Production callers supply an atomic persist callback: it must throw
        /// iff nothing was written. In-memory sessions may omit persistence.
        /// </summary>
        public bool TryPay(string expectedFloorId, string nodeId, int firstInstanceId, int secondInstanceId,
            IReadOnlyList<CardConfig> cardPool, out DungeonRunDoubleKeyPaymentReceipt receipt,
            out string error, Action persist = null)
        {
            receipt = default; error = null;
            if (IsCommitting || _run.Interactions.IsCommitting || _run.GemChests.IsCommitting || _run.VictorySealRewards.IsCommitting ||
                _run.FloorExits.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || _run.Crystals.HasPendingEvent || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)))
            { error = "地图事务正在提交。"; return false; }
            DungeonRunDoubleKeyPaymentEntry prior = Find(expectedFloorId, nodeId);
            if (prior != null)
            {
                if (prior.firstRuntimeInstanceId != firstInstanceId || prior.secondRuntimeInstanceId != secondInstanceId)
                { error = "此印匣已经使用另一对实例开启，不能再次支付。"; return false; }
                receipt = new DungeonRunDoubleKeyPaymentReceipt(prior, false);
                return true;
            }
            if (!string.Equals(expectedFloorId, _run.CurrentFloorContentId, StringComparison.Ordinal))
            { error = "双钥支付不属于当前楼层。"; return false; }
            if (!TryRequireEffect(nodeId, out LevelInteractionEffect effect, out error) ||
                !TryRequireAvailable(nodeId, out error)) return false;
            if (_run.CurrentFloor.ActiveInteractionNodeId != nodeId)
            { error = "双钥支付需要匹配的活动交互。"; return false; }
            if (firstInstanceId <= 0 || secondInstanceId <= 0 || firstInstanceId == secondInstanceId)
            { error = "双钥需要两个不同的持久卡牌实例。"; return false; }

            DungeonRunInteractionDraft draft = _run.CaptureInteractionDraft();
            if (!TryRequireCard(draft, firstInstanceId, effect.doubleKeyPayment.firstColor, cardPool, out var first, out error) ||
                !TryRequireCard(draft, secondInstanceId, effect.doubleKeyPayment.secondColor, cardPool, out var second, out error))
                return false;
            var entry = new DungeonRunDoubleKeyPaymentEntry
            {
                floorContentId = expectedFloorId, nodeContentId = nodeId, effectId = effect.effectId,
                firstColor = effect.doubleKeyPayment.firstColor, secondColor = effect.doubleKeyPayment.secondColor,
                firstRuntimeInstanceId = firstInstanceId, secondRuntimeInstanceId = secondInstanceId,
                firstCardContentId = first.contentId, secondCardContentId = second.contentId
            };
            draft.Cards.Remove(first);
            draft.Cards.Remove(second);
            if (draft.ForcedOpeningCardId == firstInstanceId || draft.ForcedOpeningCardId == secondInstanceId)
                draft.ForcedOpeningCardId = 0;

            var previousChoice = _run.Interactions.CaptureSaveData();
            IsCommitting = true;
            try
            {
                _state.payments.Add(entry);
                _run.Interactions.OpenPaidSealChoice(nodeId, effect.effectId);
                _run.CommitInteractionDraft(draft, persist);
                receipt = new DungeonRunDoubleKeyPaymentReceipt(entry, true);
                return true;
            }
            catch (Exception exception)
            {
                _state.payments.Remove(entry);
                _run.Interactions.RestorePaymentCheckpoint(previousChoice);
                error = "双钥支付未保存，已回滚：" + exception.Message;
                return false;
            }
            finally { IsCommitting = false; }
        }

        internal bool WasConsumed(int instanceId) => _state.payments.Exists(item =>
            item.firstRuntimeInstanceId == instanceId || item.secondRuntimeInstanceId == instanceId);

        internal bool HasPaidEffect(string effectId) => _state.payments.Exists(item =>
            item.floorContentId == _run.CurrentFloorContentId && item.effectId == effectId);

        internal DungeonRunDoubleKeyPaymentSaveData CaptureSaveData() => Clone(_state);

        private DungeonRunDoubleKeyPaymentEntry Find(string floorId, string nodeId)
            => _state.payments.Find(item => item.floorContentId == floorId && item.nodeContentId == nodeId);

        private bool TryRequireEffect(string nodeId, out LevelInteractionEffect effect, out string error)
        {
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            error = null;
            if (node?.nodeType != LevelNodeType.Interaction || effect?.HasDoubleKeyPayment != true ||
                string.IsNullOrWhiteSpace(effect.effectId))
            { error = "节点未配置双钥印匣合同。"; return false; }
            var errors = new List<string>();
            DungeonRunInteractionTransaction.ValidateEffect(effect, errors);
            if (errors.Count == 0) return true;
            error = string.Join("；", errors);
            return false;
        }

        private bool TryRequireAvailable(string nodeId, out string error)
        {
            error = null;
            LevelNodeViewState state = _run.CurrentFloor.GetNodeState(nodeId);
            string effectId = _run.CurrentFloor.FindNode(nodeId).completionEffects[0].effectId;
            if (IsCommitting || _run.Interactions.IsCommitting || _run.GemChests.IsCommitting || _run.VictorySealRewards.IsCommitting ||
                _run.FloorExits.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || _run.Crystals.HasPendingEvent || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)) || _run.TryGetConclusion(out _) ||
                (_run.Interactions.HasPendingChoice || (_run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy))) || _run.CurrentFloor.ActiveEncounter.HasValue ||
                _run.CurrentFloor.TryGetPendingReturn(out _) ||
                _run.CurrentFloor.TryGetInteractionReceipt(effectId, out _) ||
                (state != LevelNodeViewState.Available && state != LevelNodeViewState.Completing) ||
                (!string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId) && _run.CurrentFloor.ActiveInteractionNodeId != nodeId))
            { error = "请先完成活动战斗、返回或地图奖励；此时不能支付双钥。"; return false; }
            return true;
        }

        private static bool TryRequireCard(DungeonRunInteractionDraft draft, int id, CardColor color,
            IReadOnlyList<CardConfig> pool, out DungeonRunCardSaveData saved, out string error)
        {
            saved = null; error = null;
            foreach (var card in draft.Cards)
            {
                if (card.runtimeInstanceId != id) continue;
                if (saved != null) { error = "Run 中存在重复实例身份。"; return false; }
                saved = card;
            }
            if (saved == null) { error = "双钥目标不是当前 Run 持有的持久实例。"; return false; }
            CardConfig physical = null;
            try
            {
                physical = DungeonRunCardCodec.Restore(saved, DungeonRunCardCodec.CreateResolver(pool));
                if (!IsLegalSource(physical) || physical.color != color)
                { error = "双钥目标不是要求本色的持久非英灵牌。"; return false; }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { DungeonRunCardCodec.Release(physical); }
        }

        private static bool IsLegalSource(CardConfig source)
            => !source.IsEncounterTemporary && source.color != CardColor.Jin && !RunCardConversionRuleProfile.IsHeroCard(source);

        /// <summary>
        /// Validate the ledger against both live and completed floors before a
        /// Run is exposed. Missing payment proof is never inferred from a reward
        /// checkpoint/receipt. Pre-17 saves can only migrate unopened keyed boxes.
        /// </summary>
        internal static DungeonRunDoubleKeyPaymentSaveData ValidateRestored(
            DungeonRunSaveData save, IReadOnlyList<LevelRunSession> profiles)
        {
            var state = save.doubleKeyPayments;
            // JsonUtility may materialize a null inline class as an empty shell
            // when old DTOs are reserialized. Only pre-17 empty shells migrate.
            if (save.schemaVersion < 17 && state != null && state.version == 0 &&
                (state.payments == null || state.payments.Count == 0)) state = null;
            if (state == null && save.schemaVersion >= 17)
                throw Invalid("schema 17 缺少支付账本。");
            if (state != null && (state.version != 1 || state.payments == null))
                throw Invalid("支付账本版本或记录集合缺失。");
            var entries = state?.payments ?? new List<DungeonRunDoubleKeyPaymentEntry>();
            if (entries.Count > 0 && !save.hasPlayerState) throw Invalid("支付记录缺少已建立的 Run 牌组基线。");
            var keys = new HashSet<(string Floor, string Node)>();
            var consumed = new HashSet<int>();
            var liveIds = new HashSet<int>((save.cards ?? new List<DungeonRunCardSaveData>()).Select(card => card.runtimeInstanceId));
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.floorContentId) || string.IsNullOrWhiteSpace(entry.nodeContentId) ||
                    string.IsNullOrWhiteSpace(entry.effectId) || string.IsNullOrWhiteSpace(entry.firstCardContentId) ||
                    string.IsNullOrWhiteSpace(entry.secondCardContentId) || !keys.Add((entry.floorContentId, entry.nodeContentId)) ||
                    entry.firstRuntimeInstanceId <= 0 || entry.secondRuntimeInstanceId <= 0 ||
                    !consumed.Add(entry.firstRuntimeInstanceId) || !consumed.Add(entry.secondRuntimeInstanceId) ||
                    liveIds.Contains(entry.firstRuntimeInstanceId) || liveIds.Contains(entry.secondRuntimeInstanceId))
                    throw Invalid("存在空、重复或仍在牌组中的支付实例。");
                var profile = profiles.FirstOrDefault(item => item.Profile.contentId == entry.floorContentId);
                var node = profile?.FindNode(entry.nodeContentId);
                var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
                if (node?.nodeType != LevelNodeType.Interaction || effect?.HasDoubleKeyPayment != true || effect.effectId != entry.effectId ||
                    effect.doubleKeyPayment.firstColor != entry.firstColor || effect.doubleKeyPayment.secondColor != entry.secondColor)
                    throw Invalid("支付记录与作者合同不符。");
            }

            int pendingCount = 0;
            foreach (var profile in profiles)
            {
                LevelRunSaveData floor = profile.Profile.contentId == save.currentFloorContentId ? save.currentFloor :
                    save.completedFloorStates.Find(item => item.profileContentId == profile.Profile.contentId);
                foreach (var node in profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)))
                {
                    var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
                    if (effect?.HasDoubleKeyPayment != true) continue;
                    var errors = new List<string>();
                    DungeonRunInteractionTransaction.ValidateEffect(effect, errors);
                    if (errors.Count > 0) throw Invalid(string.Join("；", errors));
                    bool paid = entries.Exists(item => item.floorContentId == profile.Profile.contentId && item.nodeContentId == node.contentId);
                    var reward = floor.interactionReceipts?.Find(item => item.effectId == effect.effectId);
                    bool pending = profile.Profile.contentId == save.currentFloorContentId && save.interaction?.effectId == effect.effectId &&
                        save.interaction.nodeContentId == node.contentId;
                    bool cleared = floor.nodeStates.Exists(item => item.nodeContentId == node.contentId && item.progress == LevelNodeProgress.Cleared);
                    if (!paid && (reward != null || pending || cleared)) throw Invalid("奖励已有进度但没有双钥支付凭据。");
                    if (!paid) continue;
                    if (reward != null)
                    {
                        if (reward.kind != LevelInteractionEffectKind.ChooseSeal || pending)
                            throw Invalid("已领奖支付记录与奖励回执冲突。");
                    }
                    else if (!pending || cleared || ++pendingCount > 1 || floor.hasActiveEncounter || floor.hasPendingReturn ||
                        (!string.IsNullOrEmpty(floor.activeInteractionNodeContentId) && floor.activeInteractionNodeContentId != node.contentId))
                        throw Invalid("已开启印匣缺少唯一合法的待领奖检查点。");
                }
            }
            return state;
        }

        private static InvalidOperationException Invalid(string message)
            => new InvalidOperationException("双钥存档无效：" + message);
        private static DungeonRunDoubleKeyPaymentSaveData Clone(DungeonRunDoubleKeyPaymentSaveData state)
            => JsonUtility.FromJson<DungeonRunDoubleKeyPaymentSaveData>(JsonUtility.ToJson(state));
    }
}
