using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Projects the actual account and approved logical placements at the requested
    /// source time. The Run journal alone freezes these results. This service never caches
    /// future qualifications, writes the account or treats a missing placement as a false proof.</summary>
    public sealed class DungeonSideEventProductionContext : IDungeonRunSideEventContext, IDungeonRunSideEventRosterContext
    {
        private readonly DungeonRunSideEventPlan _plan;
        private readonly MetaProgression _account;
        private readonly ContentCatalog _content;
        private readonly Func<MetaProgression, DungeonRunArcanaCatalog> _arcanas;
        private readonly Func<string, bool> _invitationEligible;

        internal DungeonSideEventProductionContext(DungeonRunSideEventPlan plan, MetaProgression account,
            ContentCatalog content, Func<MetaProgression, DungeonRunArcanaCatalog> arcanas,
            Func<string, bool> invitationEligible = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _arcanas = arcanas ?? throw new ArgumentNullException(nameof(arcanas));
            _invitationEligible = invitationEligible ?? CardTestStatus.CanUseInFormalPlay;
        }

        public SideEventQualificationSnapshot CaptureQualification(SideEventSourceWireDocument document,
            string sourceLayerContentId, string runId, IReadOnlyList<DungeonRunArcanaDefinition> activeArcanas)
        {
            RequireIdentity(runId);
            if (document == null || document.Fingerprint != _plan.Document.Fingerprint)
                throw new InvalidOperationException("Source capture must use the admitted complete document.");
            bool bound = false;
            foreach (var phase in _plan.Phases) bound |= phase.SourceLayerContentId == sourceLayerContentId;
            if (!bound) throw new InvalidOperationException("Source capture requires an admitted timeline phase.");
            if (!_plan.IsSourceLayerApproved(sourceLayerContentId))
                throw new InvalidOperationException("Source capture cannot execute an unapproved later phase.");
            return DungeonSideEventQualificationResolver.Capture(document.Definition, sourceLayerContentId,
                _account, _content, _arcanas(_account), activeArcanas);
        }

        public bool CanPlace(DungeonRunSideEventPlacementRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireIdentity(request.RunId);
            foreach (var phase in _plan.Phases)
            {
                if (phase.PhaseContentId != request.Phase?.PhaseContentId) continue;
                if (!_plan.IsSourceLayerApproved(phase.SourceLayerContentId))
                    throw new InvalidOperationException("Source placement cannot execute an unapproved later phase.");
                if (phase.FloorContentId != request.Phase.FloorContentId ||
                    phase.SourceLayerContentId != request.Phase.SourceLayerContentId)
                    throw new InvalidOperationException("Placement phase differs from the admitted timeline.");
                foreach (var slot in phase.Slots)
                    if (slot.SourceSlotContentId == request.Slot?.SourceSlotContentId)
                    {
                        var node = request.Node;
                        if (slot.NodeContentId != request.Slot.NodeContentId || slot.EffectId != request.Slot.EffectId ||
                            request.Attempt?.Selection?.Slot?.Source.ContentId != slot.SourceSlotContentId ||
                            node?.contentId != slot.NodeContentId || node.nodeType != LevelNodeType.Interaction ||
                            node.completionEffects?.Count != 1 || node.completionEffects[0]?.effectId != slot.EffectId)
                            throw new InvalidOperationException("Placement must use its exact approved logical slot and effect.");
                        return true;
                    }
                throw new InvalidOperationException("Placement references an unapproved source slot.");
            }
            throw new InvalidOperationException("Placement references an unapproved source phase.");
        }

        public SideEventQualificationSnapshot CaptureQualificationForRoster(SideEventSourceWireDocument document,
            string layerId, string runId, IReadOnlyList<DungeonRunArcanaDefinition> activeArcanas,
            IReadOnlyList<string> ownedCrawlerIds)
        {
            var snapshot = CaptureQualification(document, layerId, runId, activeArcanas);
            bool hasCandidates = new DungeonRunInvitationContext(_account, () => _content, _invitationEligible).HasCandidates(ownedCrawlerIds);
            var decisions = new List<SideEventRequirementDecision>();
            foreach (var decision in snapshot.Decisions)
            {
                document.Definition.TryGetRoot(decision.RootContentId, out var root);
                // Keep the original permanent 200-success gate. A full team or
                // empty legal catalog excludes this occurrence before RNG.
                var eligibility = root.Kind == SideEventSourceKind.GunForHire && !hasCandidates
                    ? SideEventRequirementEligibility.Ineligible : decision.Eligibility;
                decisions.Add(new SideEventRequirementDecision(decision.RootContentId, eligibility));
            }
            return new SideEventQualificationSnapshot(document.Definition, layerId, decisions, snapshot.UnequippedArcanaCount);
        }

        private static void RequireIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new InvalidOperationException("Production source context requires an exact Run identity.");
        }
    }
}
