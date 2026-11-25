using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using static WanjieScroll.Dungeon.DungeonRunInvitationSaveCodec;

namespace WanjieScroll.Dungeon
{
    /// <summary>Single owner of free project invitation: frozen choices, durable one-card birth and next-encounter opening guarantee.
    /// Permanent unlock qualification is supplied explicitly. Inventory remains Session-owned; no wallet, account unlock or resident is mutated.</summary>
    public sealed class DungeonRunInvitations
    {
        public const string PolicyId = "interaction.crawler-invitation.free-choice.v1";
        public const int MaximumChoices = 3, MaximumRunCrawlers = 3;
        private readonly DungeonRunSession _run;
        private InvitationSaveData _state;
        public bool IsCommitting { get; private set; }
        public bool HasPendingChoice => _state.entries.Any(e => !e.finished);
        public string PendingNodeContentId => _state.entries.FirstOrDefault(e => !e.finished)?.nodeId;
        public IReadOnlyList<string> OwnedCrawlerContentIds
        {
            get
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(_run.LeaderCrawlerContentId)) ids.Add(_run.LeaderCrawlerContentId);
                foreach (string id in _run.UsedCrawlerContentIds) ids.Add(id);
                foreach (var card in _run.CaptureInteractionDraft().Cards)
                    if (DungeonRunCardCodec.IsKnown(card))
                        foreach (string id in CardAuthorDocument.Parse(card.currentAuthorJson).ReadSummonedCrawlerContentIds()) ids.Add(id);
                foreach (var e in _state.entries.Where(e => e.finished)) ids.Add(e.candidates[e.selectedIndex].crawlerId);
                return ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }
        }
        public IReadOnlyList<int> PendingOpeningHandCardInstanceIds => _state.entries.Where(e => e.finished && !e.openingConsumed)
            .Select(e => e.grantedId).ToArray();
        internal DungeonRunInvitations(DungeonRunSession run, string savedJson = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run)); _state = savedJson == null ? Empty(run.RunId) : Read(savedJson);
            if (_state.runId != run.RunId) throw Bad("Journal belongs to another Run.");
        }
        public static bool CanGenerateFromOwned(IReadOnlyList<string> ownedCrawlerIds, IReadOnlyList<InvitationCandidate> unlockedCandidates)
        {
            if (ownedCrawlerIds == null || unlockedCandidates == null) return false;
            var owned = new HashSet<string>(ownedCrawlerIds, StringComparer.Ordinal);
            return owned.Count < MaximumRunCrawlers && unlockedCandidates.Any(c => c != null && c.PermanentlyUnlocked && !owned.Contains(c.CrawlerContentId));
        }
        public bool CanGenerate(InvitationPreparedContext context) => context != null && context.EventAdmitted &&
            CanGenerateFromOwned(OwnedCrawlerContentIds, context.Candidates) && Eligible(context).Count > 0;
        private List<InvitationCandidate> Eligible(InvitationPreparedContext context)
        {
            var owned = OwnedCrawlerContentIds; var cards = _run.CaptureInteractionDraft().Cards;
            return context.Candidates.Where(c => c.PermanentlyUnlocked && !owned.Contains(c.CrawlerContentId) &&
                !cards.Any(card => card.contentId == c.Author.ContentId)).OrderBy(c => c.CrawlerContentId, StringComparer.Ordinal).ToList();
        }
        public string CaptureCanonicalJson() => Write(_state);
        internal bool ReservesInstanceId(int id) => id > 0 && _state.entries.Any(e => e.grantedId == id ||
            e.openingCards.Concat(e.afterCards).Any(c => DungeonRunCardCodec.Parse(c.value).runtimeInstanceId == id));
        internal bool HasGeneratedInstanceId(int id) => id > 0 && _state.entries.Any(e => e.finished && e.grantedId == id);
        internal void ConsumeOpeningGuarantees() { foreach (var e in _state.entries.Where(e => e.finished)) e.openingConsumed = true; }
        public bool CanHandleLegacy(string nodeId) => TryCanAdoptLegacy(nodeId, out _);
        /// <summary>Only an unopened legacy node, or this owner's existing adoption, is eligible. Native quotations are never overwritten.</summary>
        public bool TryCanAdoptLegacy(string nodeId, out string error)
        {
            try
            {
                var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId);
                if (effect.kind != LevelInteractionEffectKind.GunForHire) throw Bad("节点不是旧版请灵。");
                RequireNoNativeJournal(floor.Profile.contentId, nodeId);
                var own = Find(nodeId);
                if (own != null) { RequireEntry(own, effect); RequireLive(own, floor); }
                else if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || floor.TryGetInteractionReceipt(effect.effectId, out _))
                    throw Bad("旧版请灵节点不是尚未打开的可用状态。");
                error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        private void RequireNoNativeJournal(string floorId, string nodeId)
        {
            if (_run.GunForHire.HasJournalEntry(floorId, nodeId))
                throw Bad("旧版请灵已有报价或完成记录，已保留原记录，不能自动覆盖为免费请灵。请先诊断该旧状态。");
        }
        public bool TryInspect(string nodeId, out InvitationOffer offer, out string error)
        {
            offer = null;
            try { Guard(nodeId); var effect = RequireEffect(_run.CurrentFloor, nodeId); var entry = Find(nodeId) ?? throw Bad("Invitation is not opened.");
                RequireEntry(entry, effect); RequireLive(entry, _run.CurrentFloor); offer = new InvitationOffer(entry, entry.finished ? ReadReceipt(entry, _run.CurrentFloor) : null); error = null; return true; }
            catch (Exception e) { error = e.Message; return false; }
        }
        public bool TryOpen(string nodeId, InvitationPreparedContext context, out InvitationOffer offer, out string error, Action persist)
        {
            offer = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId);
                if (old != null) { RequireEntry(old, effect); RequireLive(old, floor); offer = new InvitationOffer(old, old.finished ? ReadReceipt(old, floor) : null); error = null; return true; }
                if (persist == null || !CanGenerate(context)) throw Bad("Invitation requires an admitted event, an available unlocked crawler and explicit persistence.");
                if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Node is unavailable or has an orphan receipt.");
                bool legacy = effect.kind == LevelInteractionEffectKind.GunForHire;
                SideEventSourceWireDocument legacyDocument = null;
                if (legacy)
                {
                    RequireNoNativeJournal(floor.Profile.contentId, nodeId);
                    legacyDocument = _run.SideEvents.HasSourcePlan ? _run.SideEvents.Plan.Document : context.LegacySourceDocument;
                    RequireLegacySource(legacyDocument, effect);
                }
                var draft = _run.CaptureInteractionDraft(); var eligible = Eligible(context);
                string seedText = Hash(PolicyId + "\n" + _run.RunId + "\n" + floor.Profile.contentId + "\n" + effect.effectId);
                var random = new SeededRandomSource(unchecked((int)Convert.ToUInt32(seedText.Substring(0, 8), 16)));
                var picked = new List<InvitationCandidateData>();
                while (picked.Count < MaximumChoices && eligible.Count > 0)
                { int index = random.Range(0, eligible.Count); picked.Add(CaptureCandidate(eligible[index])); eligible.RemoveAt(index); }
                var entry = new InvitationEntryData { floorId = floor.Profile.contentId, nodeId = nodeId, effectId = effect.effectId,
                    effectFingerprint = EffectFingerprint(effect), eventAdmitted = true, candidates = picked, openingCards = CaptureRoster(draft.Cards),
                    legacyAdoption = legacy, legacySourceDocumentJson = legacyDocument?.CanonicalJson ?? string.Empty,
                    legacyRootContentId = legacy ? effect.gunForHire.rootContentId : string.Empty,
                    afterCards = new List<InvitationStringData>(), ownedBefore = CaptureStrings(OwnedCrawlerContentIds), jadeBefore = _run.RunProgression.RunJadeEarned,
                    forcedBefore = draft.ForcedOpeningCardId, selectedIndex = -1 };
                var next = Clone(_state); next.entries.Add(entry);
                Commit(next, () => { if (!floor.TryBeginInteraction(nodeId, out var failure)) throw Bad(failure); }, persist);
                offer = new InvitationOffer(entry); error = null; return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
        public bool TryChooseAndComplete(string nodeId, string summonCardContentId, IReadOnlyList<CardConfig> authorCatalog,
            out InvitationReceipt receipt, out string error, Action persist)
        {
            receipt = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId);
                var old = Find(nodeId) ?? throw Bad("Open and save invitation before selecting."); RequireEntry(old, effect); RequireLive(old, floor);
                if (old.finished)
                { if (old.grantedContentId != summonCardContentId) throw Bad("Invitation already selected another crawler."); receipt = ReadReceipt(old, floor); error = null; return true; }
                if (persist == null || authorCatalog == null) throw Bad("Explicit persistence and author references are required.");
                int selected = old.candidates.FindIndex(c => CardAuthorDocument.Parse(c.authorJson).ContentId == summonCardContentId);
                if (selected < 0) throw Bad("Selection is outside the frozen offer.");
                var next = Clone(_state); var entry = next.entries.Single(e => e.floorId == old.floorId && e.nodeId == nodeId);
                LevelInteractionReceipt applied = default;
                Commit(next, () =>
                {
                    string checkpoint = _run.SerializeForDeterministicComparison(); var draft = _run.CaptureInteractionDraft();
                    var author = RestoreCandidate(entry.candidates[selected]).Author; var resolver = DungeonRunCardCodec.CreateResolver(authorCatalog);
                    CardConfig resolved = null; DungeonRunCardSaveData reward;
                    try { resolved = author.Instantiate(resolver); entry.grantedId = _run.AllocateInteractionCardId(); reward = DungeonRunCardCodec.CaptureNew(resolved, entry.grantedId, resolver); }
                    finally { DungeonRunCardCodec.Release(resolved); }
                    if (_run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Author references or allocator changed the Run checkpoint.");
                    entry.grantedContentId = author.ContentId; entry.selectedIndex = selected; entry.finished = true;
                    draft.Cards.Add(reward); draft.Cards.Sort((a,b) => a.runtimeInstanceId.CompareTo(b.runtimeInstanceId)); entry.afterCards = CaptureRoster(draft.Cards);
                    _run.CommitInteractionDraft(draft);
                    if (!floor.TryRecordExternalEffect(nodeId, effect, 0, 1, 1, out applied, out var failure, entry.grantedId, entry.grantedContentId) || !applied.NewlyApplied) throw Bad(failure ?? "Invitation already recorded.");
                    if (!floor.TryCompleteInteraction(nodeId, out failure)) throw Bad(failure);
                }, persist);
                receipt = new InvitationReceipt(entry, applied); error = null; return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
        private void Commit(InvitationSaveData next, Action apply, Action persist)
        {
            var previous = _state; IsCommitting = true;
            try
            {
                _run.CommitRewardState(() => { apply(); Validate(next); _state = next; RequireVisitedFloors(new[] { _run.CurrentFloor }, currentOnly: true); }, () =>
                { string checkpoint = _run.SerializeForDeterministicComparison(); persist();
                    if (_run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Strict save changed the Run checkpoint.");
                    RequireVisitedFloors(new[] { _run.CurrentFloor }, currentOnly: true); });
            }
            catch { _state = previous; throw; } finally { IsCommitting = false; }
        }
        private void Guard(string nodeId)
        {
            RequireId(nodeId);
            if (_run.CurrentFloor.FindNode(nodeId)?.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.GunForHire) == true)
                RequireNoNativeJournal(_run.CurrentFloorContentId, nodeId);
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted ||
                _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice || _run.GemChests.HasPendingChoice ||
                _run.Crystals.HasPendingEvent || _run.CardStatOfferings.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice || _run.GunForHire.HasPendingChoice ||
                HasPendingChoice && PendingNodeContentId != nodeId) throw Bad("Resolve the active Run reward transaction first.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId) throw Bad("Invitation requires its matching idle map node.");
        }
        private InvitationEntryData Find(string nodeId) => _state.entries.SingleOrDefault(e => e.floorId == _run.CurrentFloorContentId && e.nodeId == nodeId);
        private void RequireLive(InvitationEntryData entry, LevelRunSession floor)
        {
            if (entry.finished) { ReadReceipt(entry, floor); return; }
            var draft = _run.CaptureInteractionDraft();
            if (!ReferenceEquals(floor, _run.CurrentFloor) || floor.ActiveInteractionNodeId != entry.nodeId || floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Completing ||
                floor.TryGetInteractionReceipt(entry.effectId, out _) || !SameStrings(entry.openingCards, CaptureRoster(draft.Cards)) ||
                entry.forcedBefore != draft.ForcedOpeningCardId || entry.jadeBefore != _run.RunProgression.RunJadeEarned ||
                !ReadStrings(entry.ownedBefore).SequenceEqual(OwnedCrawlerContentIds)) throw Bad("Pending invitation checkpoint differs.");
        }
        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> floors, bool currentOnly = false)
        {
            Validate(_state);
            foreach (var entry in _state.entries)
            {
                var floor = floors.SingleOrDefault(f => f.Profile.contentId == entry.floorId);
                if (floor == null) { if (currentOnly) continue; throw Bad("Invitation refers to an unvisited floor."); }
                RequireEntry(entry, RequireEffect(floor, entry.nodeId)); RequireLive(entry, floor);
                if (entry.finished && (_run.DuplicateOfferings.HasGeneratedInstanceId(entry.grantedId) || _run.GunForHire.HasGeneratedInstanceId(entry.grantedId))) throw Bad("Invitation birth conflicts with another owner.");
            }
            foreach (var floor in floors)
                foreach (var node in floor.Nodes)
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.CrawlerInvitation &&
                            (floor.GetNodeState(node.contentId) == LevelNodeViewState.Completed || floor.TryGetInteractionReceipt(effect.effectId, out _)) &&
                            !_state.entries.Any(e => e.finished && e.floorId == floor.Profile.contentId && e.nodeId == node.contentId && e.effectId == effect.effectId)) throw Bad("Completed invitation lacks its journal.");
            if (!currentOnly && HasPendingChoice && (_run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice ||
                _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent || _run.CardStatOfferings.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice || _run.GunForHire.HasPendingChoice)) throw Bad("Invitation overlaps another active owner.");
            if (!currentOnly)
                foreach (int id in PendingOpeningHandCardInstanceIds)
                    if (!_run.CaptureInteractionDraft().Cards.Any(c => c.runtimeInstanceId == id)) throw Bad("Pending invited summon is absent from the roster.");
        }
        /// <summary>Joint restoration accepts a legacy receipt from exactly one owner; native price proofs are never synthesized.</summary>
        internal bool HasLegacyCompletionProof(LevelRunSession floor, string effectId)
        {
            var entry = _state.entries.SingleOrDefault(e => e.floorId == floor.Profile.contentId && e.effectId == effectId && e.legacyAdoption && e.finished);
            if (entry == null) return false;
            RequireEntry(entry, RequireEffect(floor, entry.nodeId)); ReadReceipt(entry, floor); return true;
        }
        private static InvitationReceipt ReadReceipt(InvitationEntryData e, LevelRunSession floor)
        {
            var effect = RequireEffect(floor, e.nodeId);
            if (!e.finished || floor.GetNodeState(e.nodeId) != LevelNodeViewState.Completed || floor.ActiveInteractionNodeId == e.nodeId ||
                !floor.TryGetInteractionReceipt(e.effectId, out var receipt) || receipt.Kind != (e.legacyAdoption ? LevelInteractionEffectKind.GunForHire : LevelInteractionEffectKind.CrawlerInvitation) ||
                !IsValidFloorSummary(receipt.ValueBefore, receipt.ValueAfter, receipt.Amount, receipt.TargetRuntimeInstanceId, receipt.TargetContentId) ||
                receipt.TargetRuntimeInstanceId != e.grantedId || receipt.TargetContentId != e.grantedContentId || receipt.Rarity != effect.rarity ||
                (receipt.PresentationId ?? "") != (effect.presentationId ?? "") || (receipt.SummaryText ?? "") != (effect.summaryText ?? "") ||
                (receipt.DetailText ?? "") != (effect.detailText ?? "")) throw Bad("Invitation receipt differs from selected summon.");
            return new InvitationReceipt(e, receipt);
        }
        internal static bool IsValidFloorSummary(int before, int after, int amount, int id, string content) => before == 0 && after == 1 && amount == 1 && id > 0 && !string.IsNullOrEmpty(content);
        private void RequireEntry(InvitationEntryData entry, LevelInteractionEffect effect)
        {
            if (entry.effectId != effect.effectId || entry.effectFingerprint != EffectFingerprint(effect) ||
                entry.legacyAdoption != (effect.kind == LevelInteractionEffectKind.GunForHire)) throw Bad("Journal differs from canonical effect.");
            if (entry.legacyAdoption)
            {
                RequireNoNativeJournal(entry.floorId, entry.nodeId);
                var document = SideEventSourceWireDocument.Parse(entry.legacySourceDocumentJson); RequireLegacySource(document, effect);
                if (entry.legacyRootContentId != effect.gunForHire.rootContentId ||
                    _run.SideEvents.HasSourcePlan && _run.SideEvents.Plan.Document.CanonicalJson != document.CanonicalJson)
                    throw Bad("Legacy adoption differs from the frozen full Run source.");
            }
        }
        private static void RequireLegacySource(SideEventSourceWireDocument document, LevelInteractionEffect effect)
        {
            if (document == null || document.Fingerprint != effect.gunForHire.sourceFingerprint) throw Bad("旧版请灵缺少匹配的完整冻结来源，未自动改写。");
            GunForHirePreparedContext.RequireSpecification(document, effect.gunForHire.rootContentId);
        }
        internal static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        {
            var node = floor.FindNode(nodeId);
            if (node == null || !floor.IsNodeEnabled(nodeId) || node.nodeType != LevelNodeType.Interaction || node.completionEffects?.Count != 1) throw Bad("Invitation requires one enabled exact interaction.");
            var effect = node.completionEffects[0]; var errors = new List<string>();
            if (effect?.kind == LevelInteractionEffectKind.GunForHire) DungeonRunGunForHire.ValidateEffect(effect, errors);
            else ValidateEffect(effect, errors);
            if (errors.Count != 0) throw Bad(string.Join("; ", errors)); return effect;
        }
        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (effect == null || effect.kind != LevelInteractionEffectKind.CrawlerInvitation || effect.crawlerInvitation?.policyId != PolicyId)
            { errors.Add("Invitation requires its project free-choice policy."); return; }
            try { RequireId(effect.effectId); } catch (Exception e) { errors.Add(e.Message); }
            if (effect.amount != 0 || effect.amountMax != 0 || effect.cashOutJade != 0 || !string.IsNullOrEmpty(effect.targetContentId) ||
                !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cardChoices?.Count > 0 || effect.sealChoices?.Count > 0 || effect.HasArcanaChoiceContract ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract || effect.HasManaStatueContract || effect.HasAbandonedCartContract ||
                effect.HasCrystalThrowingContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract || effect.HasCardStatOfferingContract ||
                effect.HasDuplicateOfferingContract || effect.HasGunForHireContract || effect.doubleKeyPayment?.IsSpecified == true)
                errors.Add("Invitation cannot mix price, cash-out, fixed candidate or another effect contract.");
        }
        internal static string EffectFingerprint(LevelInteractionEffect effect) => effect.kind == LevelInteractionEffectKind.GunForHire
            ? DungeonRunGunForHire.EffectFingerprint(effect)
            : Hash(new ExactJsonWire(typeof(InvitationEffectData), typeof(LevelInteractionInvitationContract)).Write(
            new InvitationEffectData { effectId = effect.effectId, kind = (int)effect.kind, policy = effect.crawlerInvitation, presentation = effect.presentationId,
                rarity = (int)effect.rarity, summary = effect.summaryText, detail = effect.detailText }));
        internal static bool SameContract(LevelInteractionEffect a, LevelInteractionEffect b) => a != null && b != null && EffectFingerprint(a) == EffectFingerprint(b);
        private sealed class InvitationEffectData { public string effectId; public int kind; public LevelInteractionInvitationContract policy; public string presentation; public int rarity; public string summary, detail; }
    }
}
