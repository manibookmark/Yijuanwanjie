using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Builds the production CardStat subset from this Run's real persistent
    /// physical cards and its admitted source document. Core owns original-stat
    /// projection; the existing CardStat owner freezes and pays the decision.
    /// This module owns neither publication/unlock approval nor a guessed source
    /// mapping. It also exposes validated applied facts without leaking the
    /// private persistence proof to presentation consumers.
    /// </summary>
    public static class DungeonRunCardStatOfferingSource
    {
        public static bool TryPrepare(DungeonRunSession run, string nodeId,
            IReadOnlyList<CardConfig> authorPool, out CardStatOfferingPreparedContext prepared,
            out string error)
        {
            prepared = null;
            List<CardConfig> cards = null;
            try
            {
                if (run == null || authorPool == null)
                    throw new ArgumentException("属性供奉缺少当前探索或卡牌作者目录。");
                if (run.CardStatOfferings.HasPendingChoice || run.CardStatOfferings.IsCommitting)
                    throw new InvalidOperationException("已有属性供奉选择，应恢复已保存的候选，不能重新生成。");
                var effect = DungeonRunCardStatOfferings.RequireEffect(run.CurrentFloor, nodeId);
                var document = run.SideEvents.Plan?.Document;
                if (!run.SideEvents.HasSourcePlan || document == null)
                    throw new InvalidOperationException("属性供奉需要本局已采用的完整来源计划。");
                var contract = effect.cardStatOffering;
                if (contract.sourceEventId != DungeonRunCardStatOfferings.SourceEventId ||
                    contract.sourceFingerprint != document.Fingerprint ||
                    contract.poolPolicyId != CardStatOfferingPreparedContext.ExplicitSubsetPolicyId)
                    throw new InvalidOperationException("属性供奉节点与本局来源合同不一致。");
                var specification = CardStatOfferingPreparedContext.RequireSpecification(document, contract.rootContentId);
                if (!run.TryGetCurrentCards(authorPool, out cards, out error)) return false;

                var candidates = new List<CardConfig>();
                var references = DungeonRunCardCodec.CreateResolver(authorPool);
                var supported = new HashSet<int>(specification.StatSettings.Select(setting => setting.SourceStat));
                foreach (var card in cards)
                {
                    // Unknown authors, FCC and unverified source effects cannot
                    // invent a grant. Failure to project is an exclusion, not an
                    // inferred native stat, card color or damage conversion.
                    if (card == null || card.Lifetime != CardLifetime.Persistent || !card.HasKnownAuthorConfigurations)
                        continue;
                    var original = CardInstanceFactory.CaptureAuthorConfigurations(card, references).Base;
                    var admission = original.ReadOfferingAdmission();
                    if (admission.State == CardOfferingAdmissionState.Unknown || admission.SourceClass == "FccConfig") continue;
                    // The original class is known here. A contradictory known
                    // projection is damaged evidence, not proof of an empty
                    // legal pool. Do not parse diagnostic text into policy.
                    if (!SideEventCardStatOfferingProjection.TryProject(card, out var projection, out var projectionError))
                        throw new InvalidOperationException("已知卡牌的属性供奉来源证据不完整：" + projectionError);
                    if (projection.IsFcc != false || projection.ModifiedStats.Count == 0) continue;
                    if (!projection.ModifiedStats.Any(tuple => tuple.SourceStat.HasValue && supported.Contains(tuple.SourceStat.Value)))
                        continue;
                    // Validate the same ordered arithmetic before admitting a
                    // physical candidate. Malformed supported evidence fails the
                    // entire preparation rather than becoming an empty cash pool.
                    SideEventCardStatOfferingRules.Prepare(specification, projection.IsFcc,
                        projection.RawAllowInOfferingTable == 1, projection.ModifiedStats);
                    candidates.Add(card);
                }
                prepared = new CardStatOfferingPreparedContext(document, contract.rootContentId,
                    CardStatOfferingPreparedContext.ExplicitSubsetPolicyId, candidates,
                    references);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            { error = exception.Message; return false; }
            finally
            {
                if (cards != null)
                    foreach (var card in cards) DungeonRunCardCodec.Release(card);
            }
        }

        /// <summary>Actual owned deltas from the same validated durable proof,
        /// never the requested source amount or a reread of today's card.</summary>
        public static IReadOnlyList<CardStatOfferingStatChange> ReadAppliedChanges(CardStatOfferingReceipt receipt)
        {
            if (receipt == null || receipt.Disposition != CardStatOfferingDisposition.Stats)
                throw new ArgumentException("属性收益需要已提交的献牌回执。", nameof(receipt));
            var proof = DungeonRunStatApplications.ParseAndValidateProof(receipt.StatApplicationProofJson);
            return Array.AsReadOnly(proof.Receipts.Select(value =>
                new CardStatOfferingStatChange(value.SourceStat, value.ActualOwnedDelta)).ToArray());
        }
    }

    /// <summary>A detached presentation fact; all writes remain in the Run owner.</summary>
    public readonly struct CardStatOfferingStatChange
    {
        public int SourceStat { get; }
        public int Amount { get; }
        internal CardStatOfferingStatChange(int sourceStat, int amount)
        { SourceStat = sourceStat; Amount = amount; }
    }
}
