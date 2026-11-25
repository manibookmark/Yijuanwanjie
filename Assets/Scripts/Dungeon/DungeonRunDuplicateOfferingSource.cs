using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Owns the production rubbing subset and detached copy projection.
    /// Source evidence comes only from the adopted Run document and frozen
    /// original authors. Inventory, fresh IDs and persistence remain in the Run.</summary>
    public static class DungeonRunDuplicateOfferingSource
    {
        public static bool TryPrepare(DungeonRunSession run, string nodeId,
            IReadOnlyList<CardConfig> authorPool, DuplicateOfferingEligibility eligibility,
            out DuplicateOfferingPreparedContext prepared, out string error)
        {
            prepared = null; List<CardConfig> cards = null;
            try
            {
                if (run == null || authorPool == null || eligibility == null || !eligibility.IsUnlocked)
                    throw new InvalidOperationException("拓印处尚未解锁，或当前探索与作者目录不可用。");
                if (run.DuplicateOfferings.HasPendingChoice || run.DuplicateOfferings.IsCommitting)
                    throw new InvalidOperationException("请先恢复已经保存的拓印选择。");
                var effect = DungeonRunDuplicateOfferings.RequireEffect(run.CurrentFloor, nodeId);
                var document = run.SideEvents.Plan?.Document;
                if (!run.SideEvents.HasSourcePlan || document == null ||
                    effect.duplicateOffering.sourceFingerprint != document.Fingerprint)
                    throw new InvalidOperationException("拓印处需要本局已采用的完整来源计划。");
                DuplicateOfferingPreparedContext.RequireSpecification(document, effect.duplicateOffering.rootContentId);
                if (!run.TryGetCurrentCards(authorPool, out cards, out error)) return false;
                var references = DungeonRunCardCodec.CreateResolver(authorPool);
                var candidates = cards.Where(card => IsEligible(card, references)).ToArray();
                prepared = new DuplicateOfferingPreparedContext(document, effect.duplicateOffering.rootContentId,
                    DuplicateOfferingPreparedContext.ExplicitSubsetPolicyId, eligibility, candidates,
                    PersistentDuplicateLimitBreakPolicy.Reset, references);
                error = null; return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            { error = exception.Message; return false; }
            finally { if (cards != null) foreach (var card in cards) DungeonRunCardCodec.Release(card); }
        }

        /// <summary>Only original author admission distinguishes a summon card
        /// from an ordinary skill. A destroy-on-use keyword does not make an
        /// otherwise owned permanent card temporary or ineligible.</summary>
        internal static bool IsEligible(CardConfig card, ICardAuthorReferenceResolver references)
        {
            if (card == null || card.Lifetime != CardLifetime.Persistent || !card.HasKnownAuthorConfigurations) return false;
            // Core alone knows private encounter/destroyed instance state.
            try { CardInstanceFactory.CapturePersistentState(card); }
            catch (ArgumentException) { return false; }
            var admission = CardInstanceFactory.CaptureAuthorConfigurations(card, references).Base.ReadOfferingAdmission();
            if (admission.State == CardOfferingAdmissionState.Unknown || admission.SourceClass == "FccConfig") return false;
            DuplicateOfferingPreparedContext.RequireCandidate(DungeonRunCardCodec.Capture(card, references));
            return true;
        }

        public static bool TrySubmit(DungeonRunSession run, string nodeId, int instanceId,
            IReadOnlyList<CardConfig> authorPool, Action persist, out DuplicateOfferingReceipt receipt, out string error)
        {
            receipt = null;
            try { return run.DuplicateOfferings.TrySubmitAndComplete(nodeId, instanceId, out receipt,
                out error, persist, DungeonRunCardCodec.CreateResolver(authorPool)); }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Restores a detached snapshot of the same identity with the
        /// exact copy result. This never reserves an ID, creates an inventory
        /// card or changes the original; the caller releases it after display.</summary>
        public static CardConfig BuildPreview(CardConfig source, IReadOnlyList<CardConfig> authorPool,
            PersistentDuplicateLimitBreakPolicy policy)
        {
            var references = DungeonRunCardCodec.CreateResolver(authorPool);
            var saved = DungeonRunCardCodec.Capture(source, references);
            DuplicateOfferingPreparedContext.RequireCandidate(saved);
            if (policy != PersistentDuplicateLimitBreakPolicy.Reset && policy != PersistentDuplicateLimitBreakPolicy.Preserve)
                throw new ArgumentOutOfRangeException(nameof(policy));
            if (policy == PersistentDuplicateLimitBreakPolicy.Reset) saved.limitBreakCount = 0;
            return DungeonRunCardCodec.Restore(saved, references);
        }
    }
}
