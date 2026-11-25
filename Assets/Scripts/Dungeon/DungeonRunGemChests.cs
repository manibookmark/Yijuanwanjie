using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Sole durable source-Gem chest lifecycle: ordered occurrences, full seal specifications,
    /// Silver color-payment checkpoints, selection/Back cache, exact physical-host commit and fixed-200 Greed cash-out. Core owns
    /// generation math, the injected resolver owns project qualification/source projection,
    /// and SealAssignmentRules owns card construction. This journal never reparses old pool IDs,
    /// rerolls on Back/restore, executes UI or changes legacy ChooseSeal semantics.
    /// Scale review: lifecycle, closed checkpoint and receipt cross-proof are one state boundary;
    /// the larger module still owns one chest lifecycle and one journal. Payment is its opening prerequisite,
    /// not a second reward/selection owner. Core owns color matching; the supplier owns source/project evidence.
    /// EvoStatue reuses this same Gem lifecycle with an explicit policy ID: dormant candidates
    /// remain frozen, but retired or changed physical hosts cannot be selected on reopening.
    /// V3 adds full physical lineage and actual frozen host snapshots; old validated
    /// V1/V2 evidence remains explicitly legacy and never acquires author provenance.
    /// Reassess before another event family or independent account/economy protocol is added.
    /// </summary>
    public sealed class DungeonRunGemChests
    {
        public const string OrdinaryOfferContractId = "pool.gem.xiyou.vc-ordinary.project-eligibility.v1";
        public const string SilverOfferContractId = "pool.gem.xiyou.vc-silver.project-eligibility.v1";
        public const string EvoStatueOfferContractId = "pool.gem.xiyou.vc-evo-statue.project-single-primary.v1";
        public const string GoldOfferContractId = "pool.gem.xiyou.vc-gold.project-single-primary.v1";

        /// <summary>Only adopted source policies own a durable leave/reopen cache.
        /// Presentation close for other Gem policies keeps their existing behavior.</summary>
        public static bool SupportsDurableLeave(LevelInteractionEffect effect)
            => effect?.kind == LevelInteractionEffectKind.GemChest &&
                (effect.sealRewardPoolId == SilverOfferContractId || effect.sealRewardPoolId == EvoStatueOfferContractId);
        private readonly DungeonRunSession _run;
        private GemChestRunSaveData _state;
        private bool _committing;
        private IDungeonRunInteractionWallet _unrestoredWallet;
        private int _unrestoredBalance;
        internal bool IsCommitting => _committing || _unrestoredWallet != null;
        internal long ProvenJadeIncome => _state.entries.Where(entry => entry.finished && entry.disposition == 2).Sum(entry => (long)entry.amount);
        public bool HasPendingChoice => _state.entries.Any(entry => entry.active && !entry.finished);
        public string PendingNodeContentId => _state.entries.FirstOrDefault(entry => entry.active && !entry.finished)?.nodeId;

        internal DungeonRunGemChests(DungeonRunSession run, string saved = null, int requiredVersion = 3)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = saved == null ? new GemChestRunSaveData { runId = run.RunId } : GemChestRunCodec.Read(saved, requiredVersion);
            // Headless restore may replace a fresh Run identity. An empty, integrity-checked
            // journal owns no source draw or physical-host proof to transplant.
            if (_state.runId != run.RunId)
            {
                if (_state.entries.Count != 0) throw Bad("Journal belongs to another Run.");
                _state.runId = run.RunId;
            }
            // Core constructors retain their lawful ArgumentException contract. At this boundary,
            // those arguments came from saved evidence and mean corrupt checkpoint semantics.
            try { ValidateLocal(); }
            catch (ArgumentException exception) when (saved != null)
            { throw new InvalidOperationException("Run Gem chest: Invalid saved payment semantics.", exception); }
            Seal(); // Other Run owners are not yet assembled here.
        }

        /// <summary>Consumed IDs remain reserved across floors and every entry in this journal.</summary>
        public bool HasConsumed(int runtimeInstanceId) => runtimeInstanceId > 0 && _state.entries.Any(entry =>
            entry.payment?.paid == true && entry.payment.selectedIds.Contains(runtimeInstanceId));

        /// <summary>Strictly leaves Silver or Evo activity. Unpaid Silver choices and the selected Evo
        /// occurrence are cleared without consuming cards; complete generated candidates stay frozen.</summary>
        public bool TryCancel(string nodeId, DungeonRunInteractionCatalog catalog, out string error, Action persist = null)
            => TryCommit(nodeId, "leave", catalog, out _, out error, persist);

        public bool TryInspect(string nodeId, DungeonRunInteractionCatalog catalog,
            out DungeonRunInteractionOffer offer, out string error)
        {
            offer = null; error = null; bool ownsBoundary = false;
            try
            {
                RequirePublicBoundary(nodeId); RequireConsumedProof(); var effect = RequireEffect(_run.CurrentFloor, nodeId);
                _committing = true; ownsBoundary = true;
                var entry = Find(nodeId);
                if (entry == null)
                {
                    if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out var lockedReceipt) &&
                        DungeonRunSealRewardPolicy.IsLockedCashOut(effect, lockedReceipt))
                    { offer = Completed(effect, _run.CurrentFloor); return true; }
                    if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Orphan Gem completion receipt.");
                    RequireAvailable(nodeId);
                    offer = !_run.CanReceiveSealRewards
                        ? LockedOffer(effect, catalog)
                        : new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Ready, Title = effect.summaryText };
                }
                else
                { RequireFloorEntry(_run.CurrentFloor, entry); offer = BuildOffer(effect, entry, catalog, _run.CaptureInteractionDraft()); }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { if (ownsBoundary) _committing = false; }
        }

        /// <summary>Null opens once. gem/N chooses an occurrence, back preserves the entire offer,
        /// host/N/physicalId installs it, cash-out grants 200 with Greed. Every new transition requires
        /// an atomic throwing persistence callback. Successful replay neither generates nor saves.</summary>
        public bool TryCommit(string nodeId, string choiceId, DungeonRunInteractionCatalog catalog,
            out DungeonRunInteractionOffer offer, out string error, Action persist = null)
        {
            offer = null; error = null; var previous = _state; bool succeeded = false, walletTouched = false, ownsBoundary = false;
            IDungeonRunInteractionWallet wallet = null; int balance = 0;
            try
            {
                RequirePublicBoundary(nodeId, allowRecovery: true); RequireConsumedProof();
                if (persist == null) throw Bad("Strict Gem persistence is required.");
                var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId);
                var entry = Find(nodeId); choiceId ??= string.Empty;
                if (entry == null && floor.TryGetInteractionReceipt(effect.effectId, out var lockedReceipt) &&
                    DungeonRunSealRewardPolicy.IsLockedCashOut(effect, lockedReceipt))
                {
                    if (choiceId.Length != 0 && choiceId != "cash-out") throw Bad("Gem reward already cashed out.");
                    offer = Completed(effect, floor); return true;
                }
                if (entry == null && (!_run.CanReceiveSealRewards))
                {
                    RequireAvailable(nodeId);
                    if (choiceId.Length == 0) { offer = LockedOffer(effect, catalog); return true; }
                    if (choiceId != "cash-out") throw Bad("取得玉印后才能钤印。");
                    if (!_run.TryCashOutSealReward(nodeId, effect, 200, catalog?.Wallet, out _, out error, persist)) return false;
                    offer = Completed(effect, floor); return true;
                }
                if (entry != null && entry.finished)
                {
                    RequireFloorEntry(floor, entry);
                    if (choiceId.Length > 0 && choiceId != entry.decisionId) throw Bad("Gem chest already has another disposition.");
                    offer = Completed(effect, floor); return true;
                }
                if (entry != null && !_run.CanReceiveSealRewards && entry.payment != null && !entry.payment.paid && choiceId != "leave")
                {
                    if (choiceId.Length == 0) { offer = LockedOffer(effect, catalog); return true; }
                    if (choiceId != "cash-out") throw Bad("取得玉印后才能钤印。");
                    // An old unpaid cache contains no consumed cards. Replace it with the explicit
                    // permission-locked floor disposition, never counterfeit a paid/source-generation proof.
                    var withoutUnpaid = GemChestRunCodec.Clone(_state);
                    withoutUnpaid.entries.RemoveAll(item => item.floorId == floor.Profile.contentId && item.nodeId == nodeId);
                    _state = withoutUnpaid; Seal();
                    if (!_run.TryCashOutSealReward(nodeId, effect, 200, catalog?.Wallet, out _, out error, persist))
                    { _state = previous; return false; }
                    offer = Completed(effect, floor); succeeded = true; return true;
                }
                if (entry != null && !_run.CanReceiveSealRewards && choiceId.Length > 0 &&
                    choiceId != "cash-out" && choiceId != "leave") throw Bad("取得玉印后才能钤印。");
                RequireAvailable(nodeId); _committing = true; ownsBoundary = true;
                var next = GemChestRunCodec.Clone(_state); var draft = _run.CaptureInteractionDraft();
                var pending = next.entries.FirstOrDefault(item => item.floorId == _run.CurrentFloorContentId && item.nodeId == nodeId);
                if (pending == null)
                {
                    // Leaving an untouched Silver chest must not draw its locks or prepare its T2 offer.
                    if (choiceId == "leave" && SupportsDurableLeave(effect))
                    {
                        if (floor.ActiveInteractionNodeId == nodeId)
                            _run.CommitRewardState(() => { if (!floor.TryCancelUncommittedInteraction(nodeId, out string cancelError)) throw Bad(cancelError); },
                                StrictPersist(persist, floor, nodeId, effect));
                        offer = new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Ready, Title = effect.summaryText };
                        succeeded = true; return true;
                    }
                    if (choiceId.Length != 0) throw Bad("Open the source chest before selecting a result.");
                    if (effect.sealRewardPoolId == SilverOfferContractId)
                    {
                        offer = OpenSilver(floor, nodeId, effect, catalog, draft, next, persist);
                        succeeded = true; return true;
                    }
                    if (catalog?.GemChestOfferResolver == null) throw Bad("No explicit Gem source resolver is available.");
                    DungeonRunInteractionOffer preparedOffer = null;
                    _run.CommitRewardState(() =>
                    {
                        string before = _run.SerializeForDeterministicComparison();
                        if (!catalog.GemChestOfferResolver.TryPrepare(_run.RunId, effect, catalog, out var prepared, out string prepareError) || prepared == null)
                            throw Bad(prepareError ?? "Gem source qualification is unresolved.");
                        if (_run.SerializeForDeterministicComparison() != before) throw Bad("Resolver mutated Run state.");
                        RequireSameEffect(floor, nodeId, effect);
                        if (prepared.ResolverContractId != effect.sealRewardPoolId) throw Bad("Resolver policy differs from the authored contract.");
                        pending = new GemChestEntryData { floorId = _run.CurrentFloorContentId, nodeId = nodeId,
                            effectId = effect.effectId, effectFingerprint = EffectFingerprint(effect), prepared = ClonePrepared(prepared.Data),
                            physicalProofVersion = 2 };
                        CaptureHosts(pending, draft);
                        RequireHostsInRoster(pending, draft); next.entries.Add(pending);
                        if (!floor.TryBeginInteraction(nodeId, out string beginError)) throw Bad(beginError);
                        _state = next; Seal(); preparedOffer = BuildOffer(effect, pending, catalog, draft);
                    }, StrictPersist(persist, floor, nodeId, effect));
                    offer = preparedOffer; succeeded = true; return true;
                }
                RequireFloorEntry(floor, pending);
                if (pending.payment == null && effect.sealRewardPoolId == EvoStatueOfferContractId &&
                    (choiceId == "leave" || !pending.active))
                {
                    offer = CommitEvoActivity(floor, nodeId, effect, catalog, draft, next, pending, choiceId, persist);
                    succeeded = true; return true;
                }
                if (pending.payment != null && (choiceId == "leave" || !pending.active || !pending.payment.paid || choiceId.StartsWith("pay/", StringComparison.Ordinal)))
                {
                    offer = CommitSilver(floor, nodeId, effect, catalog, draft, next, pending, choiceId, persist);
                    succeeded = true; return true;
                }
                if (choiceId.Length == 0 || choiceId == "back" && pending.selectedOccurrence < 0 ||
                    pending.selectedOccurrence >= 0 && choiceId == GemKey(pending.selectedOccurrence))
                { offer = BuildOffer(effect, pending, catalog, draft); succeeded = true; return true; }
                if (choiceId == "back")
                {
                    pending.selectedOccurrence = -1;
                    var preparedOffer = BuildOffer(effect, pending, catalog, draft);
                    CommitCheckpoint(floor, nodeId, effect, next, persist);
                    offer = preparedOffer; succeeded = true; return true;
                }
                if (pending.selectedOccurrence < 0 && TryGemIndex(choiceId, pending.prepared.choices.Count, out int index))
                {
                    pending.selectedOccurrence = index;
                    var preparedOffer = BuildOffer(effect, pending, catalog, draft);
                    CommitCheckpoint(floor, nodeId, effect, next, persist);
                    offer = preparedOffer; succeeded = true; return true;
                }
                if (choiceId == "cash-out")
                {
                    wallet = catalog?.Wallet ?? throw Bad("Gem cash-out requires its real economy participant.");
                    balance = wallet.Balance; if (balance < 0) throw Bad("Negative economy balance.");
                    _run.CommitRewardState(() =>
                    {
                        EnsureActive(floor, nodeId, effect);
                        int runJade = _run.RunProgression.RunJadeEarned;
                        walletTouched = true; var expected = wallet.Preview(200); RequireGain(expected);
                        RequireSameEffect(floor, nodeId, effect); var actual = wallet.Credit(200); RequireGain(actual);
                        RequireSameEffect(floor, nodeId, effect);
                        if (actual.FinalJade != expected.FinalJade || (long)wallet.Balance - balance != actual.FinalJade ||
                            (long)_run.RunProgression.RunJadeEarned - runJade != actual.FinalJade)
                            throw Bad("Cash-out participants did not commit the exact Greed-adjusted amount.");
                        pending.before = balance; pending.after = wallet.Balance; pending.amount = actual.FinalJade;
                        pending.disposition = 2; Complete(floor, nodeId, effect, pending, next, choiceId, persist: null);
                    }, StrictPersist(persist, floor, nodeId, effect, wallet));
                }
                else
                {
                    if (!_run.CanReceiveSealRewards)
                        throw Bad("取得玉印后才能钤印。");
                    if (pending.selectedOccurrence < 0) throw Bad("Choose a Gem occurrence first.");
                    var selected = pending.prepared.choices[pending.selectedOccurrence];
                    int physicalId = ParseHost(choiceId, pending.selectedOccurrence);
                    if (!selected.hostIds.Contains(physicalId)) throw Bad("Host was not in the frozen project-policy projection.");
                    int cardIndex = draft.Cards.FindIndex(card => card.runtimeInstanceId == physicalId);
                    if (cardIndex < 0) throw Bad("Physical Gem host is missing.");
                    if (!MatchesHostLineage(pending, draft.Cards[cardIndex])) throw Bad("Frozen Gem host author lineage changed.");
                    _run.CommitRewardState(() =>
                    {
                        EnsureActive(floor, nodeId, effect);
                        var savedCard = draft.Cards[cardIndex]; var source = catalog?.FindCard(savedCard.contentId);
                        if (!DungeonRunSealAssignmentRules.TryPrepare(savedCard, source, selected.seal,
                            id => id == selected.evolutionOutcomeContentId && catalog.CanEvolveTo(id),
                            out var replacement, out _, out string assignError,
                            references: DungeonRunCardCodec.CreateResolver(catalog.Cards))) throw Bad(assignError);
                        if (selected.seal.effect == CardSealEffect.Evolution && replacement.contentId != selected.evolutionOutcomeContentId)
                            throw Bad("Evolution must match this frozen occurrence's exact outcome.");
                        RequireSameEffect(floor, nodeId, effect);
                        pending.cardBefore = CaptureCard(savedCard); pending.cardAfter = CaptureCard(replacement);
                        // A migrated legacy card may omit its capacity override; freeze its actual effective value.
                        if (!pending.cardBefore.hasGemSlotCapacity)
                        { pending.cardBefore.hasGemSlotCapacity = true; pending.cardBefore.gemSlotCapacity = CardSealRules.GetSlotCapacity(source); }
                        draft.Cards[cardIndex] = replacement; draft.ForcedOpeningCardId = physicalId;
                        pending.before = 0; pending.after = 1; pending.amount = 1; pending.targetId = physicalId;
                        pending.targetContentId = selected.seal.contentId; pending.disposition = 1;
                        _run.CommitInteractionDraft(draft); Complete(floor, nodeId, effect, pending, next, choiceId, persist: null);
                    }, StrictPersist(persist, floor, nodeId, effect));
                }
                RequireConsumedProof(); offer = Completed(effect, floor); succeeded = true; return true;
            }
            catch (Exception exception) { _state = previous; error = "Gem transaction was not committed: " + exception.Message; return false; }
            finally
            {
                if (!succeeded && walletTouched && !RestoreWallet(wallet, balance, out string restoreError))
                { _unrestoredWallet = wallet; _unrestoredBalance = balance; error += " Economy recovery is incomplete: " + restoreError; }
                if (ownsBoundary) _committing = false;
            }
        }

        // This is the existing Gem journal activity boundary, not a separate evolution owner.
        // Generation is never called while leaving or reopening this cache.
        private DungeonRunInteractionOffer CommitEvoActivity(LevelRunSession floor, string nodeId,
            LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft,
            GemChestRunSaveData next, GemChestEntryData entry, string command, Action persist)
        {
            if (command == "leave")
            {
                if (entry.active)
                {
                    entry.active = false; entry.selectedOccurrence = -1;
                    _run.CommitRewardState(() =>
                    {
                        RequireSameEffect(floor, nodeId, effect);
                        if (!floor.TryCancelUncommittedInteraction(nodeId, out string error)) throw Bad(error);
                        _state = next; ValidateLocal(); Seal();
                    }, StrictPersist(persist, floor, nodeId, effect));
                }
                return new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Ready, Title = effect.summaryText };
            }
            if (command.Length != 0) throw Bad("Reopen the dormant Evo cache before selecting.");
            DungeonRunInteractionOffer offer = null;
            _run.CommitRewardState(() =>
            {
                RequireSameEffect(floor, nodeId, effect); entry.active = true;
                EnsureActive(floor, nodeId, effect); _state = next; ValidateLocal(); Seal();
                offer = BuildOffer(effect, entry, catalog, draft);
            }, StrictPersist(persist, floor, nodeId, effect));
            return offer;
        }

        private DungeonRunInteractionOffer OpenSilver(LevelRunSession floor, string nodeId, LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft, GemChestRunSaveData next, Action persist)
        {
            var resolver = catalog?.GemChestOfferResolver as IDungeonRunGemChestPaymentResolver;
            if (resolver == null) throw Bad("No explicit Silver physical-card projection is available.");
            DungeonRunInteractionOffer result = null;
            _run.CommitRewardState(() =>
            {
                string before = _run.SerializeForDeterministicComparison();
                if (!resolver.TryPreparePayment(_run.RunId, effect, catalog, out var prepared, out string error) || prepared == null)
                    throw Bad(error ?? "Silver payment source is unresolved.");
                if (_run.SerializeForDeterministicComparison() != before) throw Bad("Payment resolver mutated Run state.");
                RequireSameEffect(floor, nodeId, effect);
                if (prepared.ResolverContractId != SilverOfferContractId) throw Bad("Silver payment policy differs from this chest.");
                var payment = new GemChestPaymentData { resolverContractId = prepared.ResolverContractId,
                    canonicalInputJson = prepared.CanonicalInputJson, inputFingerprint = prepared.InputFingerprint,
                    requirements = prepared.Requirements.ToList(), remainingRequirements = prepared.Requirements.ToList(),
                    openingRoster = draft.Cards.Select(CaptureCard).ToList() };
                foreach (var evidence in prepared.Cards)
                {
                    var physical = draft.Cards.SingleOrDefault(card => card.runtimeInstanceId == evidence.RuntimeInstanceId)
                        ?? throw Bad("Payment candidate is outside the physical Run roster.");
                    if (HasConsumed(physical.runtimeInstanceId) || WasConsumedElsewhere(physical.runtimeInstanceId))
                        throw Bad("A consumed physical instance cannot become Silver evidence.");
                    var card = CaptureCard(physical);
                    payment.candidates.Add(new GemChestPaymentCardData { card = card,
                        cardFingerprint = GemChestRunCodec.Hash(GemChestRunCodec.Write(card)), isFcc = evidence.IsFcc,
                        sourceCardTypes = evidence.SourceCardTypes.ToList() });
                }
                var entry = new GemChestEntryData { floorId = _run.CurrentFloorContentId, nodeId = nodeId,
                    effectId = effect.effectId, effectFingerprint = EffectFingerprint(effect), prepared = null, payment = payment,
                    physicalProofVersion = 2 };
                next.entries.Add(entry); EnsureActive(floor, nodeId, effect); _state = next; ValidateLocal(); Seal();
                result = BuildPaymentOffer(effect, entry, draft);
            }, StrictPersist(persist, floor, nodeId, effect));
            return result;
        }

        private DungeonRunInteractionOffer CommitSilver(LevelRunSession floor, string nodeId, LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft, GemChestRunSaveData next,
            GemChestEntryData entry, string command, Action persist)
        {
            var payment = entry.payment;
            if (command == "leave")
            {
                if (entry.active)
                {
                    entry.active = false;
                    if (!payment.paid) { payment.selectedIds.Clear(); payment.remainingRequirements = payment.requirements.ToList(); }
                    _run.CommitRewardState(() =>
                    {
                        RequireSameEffect(floor, nodeId, effect);
                        if (!floor.TryCancelUncommittedInteraction(nodeId, out string error)) throw Bad(error);
                        _state = next; ValidateLocal(); Seal();
                    }, StrictPersist(persist, floor, nodeId, effect));
                }
                return new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Ready, Title = effect.summaryText };
            }
            if (!entry.active)
            {
                if (command.Length != 0) throw Bad("Reopen the dormant Silver cache before selecting.");
                entry.active = true; RequireSelectedCards(payment, draft);
                CommitCheckpoint(floor, nodeId, effect, next, persist);
                return BuildOffer(effect, entry, catalog, draft);
            }
            if (command.Length == 0) return BuildOffer(effect, entry, catalog, draft);
            int id = ParsePayment(command);
            if (payment.selectedIds.Contains(id)) return BuildOffer(effect, entry, catalog, draft);
            if (payment.paid) throw Bad("Silver payment is already complete.");
            var candidate = payment.candidates.SingleOrDefault(item => item.card.runtimeInstanceId == id)
                ?? throw Bad("Physical card is outside the frozen Silver projection.");
            RequireSelectedCards(payment, draft); RequirePhysicalCandidate(candidate, draft);
            if (!ReplayPayment(payment).TrySelect(ToEvidence(candidate), out var selected, out string selectError)) throw Bad(selectError);
            payment.selectedIds.Add(id); payment.remainingRequirements = selected.RemainingRequirements.ToList();
            if (!selected.IsComplete)
            {
                CommitCheckpoint(floor, nodeId, effect, next, persist);
                return BuildPaymentOffer(effect, entry, draft);
            }
            // The final selection removes exact instances and then prepares from that real post-payment
            // roster within the same aggregate rollback boundary. No observer sees a paid empty cache.
            payment.rosterBeforePayment = draft.Cards.Select(CaptureCard).ToList();
            payment.forcedOpeningBefore = draft.ForcedOpeningCardId;
            draft.Cards.RemoveAll(card => payment.selectedIds.Contains(card.runtimeInstanceId));
            if (payment.selectedIds.Contains(draft.ForcedOpeningCardId)) draft.ForcedOpeningCardId = 0;
            payment.rosterAfterPayment = draft.Cards.Select(CaptureCard).ToList(); payment.forcedOpeningAfter = draft.ForcedOpeningCardId;
            DungeonRunInteractionOffer result = null;
            _run.CommitRewardState(() =>
            {
                EnsureActive(floor, nodeId, effect); _run.CommitInteractionDraft(draft);
                string before = _run.SerializeForDeterministicComparison();
                if (catalog?.GemChestOfferResolver == null) throw Bad("Post-payment T2 source is unresolved.");
                if (!catalog.GemChestOfferResolver.TryPrepare(_run.RunId, effect, catalog,
                    out var prepared, out string prepareError) || prepared == null) throw Bad(prepareError ?? "Post-payment T2 source is unresolved.");
                if (_run.SerializeForDeterministicComparison() != before) throw Bad("T2 resolver mutated the post-payment Run.");
                RequireSameEffect(floor, nodeId, effect);
                if (prepared.ResolverContractId != SilverOfferContractId) throw Bad("T2 resolver changed the Silver contract.");
                entry.prepared = ClonePrepared(prepared.Data); payment.paid = true; CaptureHosts(entry, draft); RequireHostsInRoster(entry, draft);
                _state = next; ValidateLocal(); RequireConsumedProof(); Seal(); result = BuildOffer(effect, entry, catalog, draft);
            }, StrictPersist(persist, floor, nodeId, effect));
            return result;
        }

        private static int ParsePayment(string command)
        {
            if (!command.StartsWith("pay/", StringComparison.Ordinal) || !int.TryParse(command.Substring(4), out int id) ||
                id <= 0 || command != "pay/" + id) throw Bad("Invalid exact Silver payment command.");
            return id;
        }
        private DungeonRunInteractionOffer BuildPaymentOffer(LevelInteractionEffect effect, GemChestEntryData entry, DungeonRunInteractionDraft draft)
        {
            var payment = entry.payment; var frozen = ReplayPayment(payment); RequireSelectedCards(payment, draft);
            var options = new List<DungeonRunInteractionOption>();
            foreach (var candidate in payment.candidates)
            {
                if (!frozen.CanSelect(ToEvidence(candidate))) continue;
                var card = draft.Cards.SingleOrDefault(item => item.runtimeInstanceId == candidate.card.runtimeInstanceId);
                if (card == null || GemChestRunCodec.Write(CaptureCard(card)) != GemChestRunCodec.Write(candidate.card) ||
                    HasConsumed(card.runtimeInstanceId) || WasConsumedElsewhere(card.runtimeInstanceId)) continue;
                options.Add(new DungeonRunInteractionOption { Id = "pay/" + card.runtimeInstanceId,
                    CardRuntimeInstanceId = card.runtimeInstanceId, CardContentId = card.contentId });
            }
            return new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.SilverPayment, Title = effect.summaryText,
                Options = options.AsReadOnly(), PaymentRequirements = Array.AsReadOnly(payment.requirements.ToArray()),
                PaymentRemainingRequirements = Array.AsReadOnly(payment.remainingRequirements.ToArray()),
                PaymentSelectedRuntimeInstanceIds = Array.AsReadOnly(payment.selectedIds.ToArray()),
                Message = options.Count == 0 ? "No known physical card satisfies the remaining Silver locks; leave is available." : null };
        }
        private static SilverGemChestPaymentCard ToEvidence(GemChestPaymentCardData card)
            => new SilverGemChestPaymentCard(card.card.runtimeInstanceId, card.sourceCardTypes, card.isFcc);
        private static SideEventSilverGemChestPayment ReplayPayment(GemChestPaymentData payment)
        {
            var frozen = SideEventSilverGemChestPaymentRules.CreateFrozen(payment.requirements);
            foreach (int id in payment.selectedIds)
            {
                var card = payment.candidates.SingleOrDefault(item => item.card.runtimeInstanceId == id)
                    ?? throw Bad("Selected Silver instance has no frozen evidence.");
                if (!frozen.TrySelect(ToEvidence(card), out var next, out string error)) throw Bad(error); frozen = next;
            }
            return frozen;
        }
        private bool WasConsumedElsewhere(int id) => _run.DoubleKeyPayments.WasConsumed(id) || _run.ChickenOfferings.WasConsumed(id) || (_run.CardStatOfferings?.HasConsumed(id) ?? false);
        private void RequirePhysicalCandidate(GemChestPaymentCardData candidate, DungeonRunInteractionDraft draft)
        {
            int id = candidate.card.runtimeInstanceId;
            var actual = draft.Cards.SingleOrDefault(item => item.runtimeInstanceId == id);
            if (actual == null || HasConsumed(id) || WasConsumedElsewhere(id) ||
                GemChestRunCodec.Write(CaptureCard(actual)) != GemChestRunCodec.Write(candidate.card))
                throw Bad("Frozen Silver physical card changed or was consumed.");
        }
        private void RequireSelectedCards(GemChestPaymentData payment, DungeonRunInteractionDraft draft)
        {
            if (payment.paid) return;
            foreach (int id in payment.selectedIds)
                RequirePhysicalCandidate(payment.candidates.Single(item => item.card.runtimeInstanceId == id), draft);
        }
        private void RequireConsumedProof()
        {
            var ids = new HashSet<int>(_run.CaptureInteractionDraft().Cards.Select(card => card.runtimeInstanceId));
            foreach (var entry in _state.entries)
                if (entry.payment?.paid == true)
                    foreach (int id in entry.payment.selectedIds)
                        if (ids.Contains(id) || WasConsumedElsewhere(id)) throw Bad("Silver consumed-ID proof conflicts with the Run or another owner.");
        }
        private static void RequirePayment(GemChestEntryData entry)
        {
            var p = entry.payment;
            if (p.resolverContractId != SilverOfferContractId || p.requirements == null || p.remainingRequirements == null || p.candidates == null ||
                p.selectedIds == null || p.openingRoster == null || p.rosterBeforePayment == null || p.rosterAfterPayment == null ||
                p.forcedOpeningBefore < 0 || p.forcedOpeningAfter < 0) throw Bad("Invalid complete Silver checkpoint.");
            GemChestRunCodec.ReadFields(p.canonicalInputJson); GemChestRunCodec.RequireHash(p.inputFingerprint);
            if (GemChestRunCodec.Hash(p.canonicalInputJson) != p.inputFingerprint) throw Bad("Silver input evidence changed.");
            RequireRoster(p.openingRoster); var ids = new HashSet<int>();
            foreach (var candidate in p.candidates)
            {
                if (candidate == null || !ids.Add(candidate.card?.runtimeInstanceId ?? 0)) throw Bad("Duplicate/null Silver candidate.");
                RequireRosterCard(candidate.card); GemChestRunCodec.RequireHash(candidate.cardFingerprint);
                if (candidate.cardFingerprint != GemChestRunCodec.Hash(GemChestRunCodec.Write(candidate.card)) ||
                    GemChestRunCodec.Write(p.openingRoster.SingleOrDefault(card => card.runtimeInstanceId == candidate.card.runtimeInstanceId)) != GemChestRunCodec.Write(candidate.card))
                    throw Bad("Silver candidate is not its full opening physical snapshot.");
                ToEvidence(candidate);
            }
            var replay = ReplayPayment(p);
            if (!replay.RemainingRequirements.SequenceEqual(p.remainingRequirements) || p.paid != replay.IsComplete || !entry.active && !p.paid && p.selectedIds.Count != 0)
                throw Bad("Silver selected instances and ordered remaining locks disagree.");
            if (!p.paid)
            {
                if (entry.prepared != null || entry.finished || p.rosterBeforePayment.Count != 0 || p.rosterAfterPayment.Count != 0 ||
                    p.forcedOpeningBefore != 0 || p.forcedOpeningAfter != 0) throw Bad("Unpaid Silver carries a payment or Gem proof.");
                return;
            }
            GemChestRunCodec.ValidatePrepared(entry.prepared);
            if (entry.prepared.resolverContractId != SilverOfferContractId) throw Bad("Silver T2 contract changed.");
            RequireRoster(p.rosterBeforePayment); RequireRoster(p.rosterAfterPayment);
            foreach (int id in p.selectedIds)
            {
                var candidate = p.candidates.Single(item => item.card.runtimeInstanceId == id);
                if (GemChestRunCodec.Write(p.rosterBeforePayment.SingleOrDefault(card => card.runtimeInstanceId == id)) != GemChestRunCodec.Write(candidate.card))
                    throw Bad("Paid Silver changed its exact selected physical snapshot.");
            }
            var after = p.rosterBeforePayment.Where(card => !p.selectedIds.Contains(card.runtimeInstanceId)).ToList();
            if (GemChestRunCodec.Write(after) != GemChestRunCodec.Write(p.rosterAfterPayment) ||
                p.forcedOpeningBefore != 0 && !p.rosterBeforePayment.Any(card => card.runtimeInstanceId == p.forcedOpeningBefore) ||
                p.forcedOpeningAfter != (p.selectedIds.Contains(p.forcedOpeningBefore) ? 0 : p.forcedOpeningBefore))
                throw Bad("Silver removal/forced-hand proof is not the exact ordered subtraction.");
            var hosts = new HashSet<int>(p.rosterAfterPayment.Select(card => card.runtimeInstanceId));
            if (entry.prepared.choices.Any(choice => choice.hostIds.Any(id => !hosts.Contains(id)))) throw Bad("T2 frozen hosts include a paid-away instance.");
        }
        private static void RequireRoster(IReadOnlyList<GemChestCardData> cards)
        {
            if (cards == null || cards.Select(card => card?.runtimeInstanceId ?? 0).Distinct().Count() != cards.Count) throw Bad("Duplicate physical roster proof.");
            foreach (var card in cards) RequireRosterCard(card);
        }
        private static void RequireRosterCard(GemChestCardData card)
        {
            if (card == null || card.runtimeInstanceId <= 0 || card.limitBreakCount < 0 || card.gemSlotCapacity < 0 || card.seals == null ||
                card.hasGemSlotCapacity && card.seals.Count > card.gemSlotCapacity || !card.hasGemSlotCapacity && card.gemSlotCapacity != 0)
                throw Bad("Invalid full Silver physical card snapshot.");
            GemChestRunCodec.RequireId(card.contentId); foreach (var seal in card.seals) GemChestRunCodec.RequireId(seal?.contentId);
            DungeonRunCardCodec.Validate(GemChestRunCodec.RestoreCard(card));
        }

        internal string CaptureCanonicalJson()
        {
            if (_unrestoredWallet != null) throw Bad("Cannot save an unrecovered Gem economy participant.");
            ValidateLocal(); Seal(); return GemChestRunCodec.Write(_state);
        }

        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (effect == null || effect.kind != LevelInteractionEffectKind.GemChest || effect.amount <= 0 || effect.amountMax != 0 ||
                string.IsNullOrWhiteSpace(effect.sealRewardPoolId) || effect.cashOutJade != 200 || !string.IsNullOrEmpty(effect.targetContentId) ||
                effect.sealChoices == null || effect.sealChoices.Count != 0 || effect.cardChoices == null || effect.cardChoices.Count != 0 ||
                effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract || effect.HasDoubleKeyPayment || effect.HasArcanaChoiceContract ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract || effect.HasManaStatueContract || effect.HasCrystalThrowingContract || effect.HasAbandonedCartContract)
                errors.Add("GemChest requires an explicit resolver contract, positive configured base count, fixed 200 cash-out and no mixed payload.");
            else if (SupportsDurableLeave(effect) && effect.amount != 3)
                errors.Add("Silver and Evo source policies require the captured base count three.");
            else try { GemChestRunCodec.RequireId(effect.effectId); GemChestRunCodec.RequireId(effect.sealRewardPoolId); }
            catch (Exception exception) { errors.Add(exception.Message); }
        }

        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> floors)
        {
            ValidateLocal(); if (floors == null) throw Bad("Missing visited floors.");
            foreach (var entry in _state.entries)
            {
                var floor = floors.SingleOrDefault(item => item?.Profile.contentId == entry.floorId) ?? throw Bad("Gem journal references an unvisited floor.");
                RequireFloorEntry(floor, entry);
                if (!entry.finished && entry.physicalProofVersion == 2)
                {
                    foreach (var card in _run.CaptureInteractionDraft().Cards)
                    {
                        var frozen = entry.hostCards.SingleOrDefault(host => host.runtimeInstanceId == card.runtimeInstanceId);
                        if (frozen != null && frozen.contentId == card.contentId && !MatchesHostLineage(entry, card))
                            throw Bad("Saved Gem host author lineage differs from the frozen offer.");
                    }
                }
                if (entry.active && !entry.finished && !ReferenceEquals(floor, _run.CurrentFloor)) throw Bad("Pending Gem belongs to another floor.");
            }
            foreach (var floor in floors)
                foreach (var node in floor.Nodes)
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.GemChest && floor.TryGetInteractionReceipt(effect.effectId, out var receipt) &&
                            !DungeonRunSealRewardPolicy.IsLockedCashOut(effect, receipt) &&
                            !_state.entries.Any(entry => entry.floorId == floor.Profile.contentId && entry.nodeId == node.contentId && entry.finished))
                            throw Bad("Gem receipt has no durable generation and disposition proof.");
            if (HasPendingChoice)
            {
                if (_run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted ||
                    _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy) || _run.Crystals.HasPendingEvent)
                    throw Bad("Pending Gem conflicts with another reward or terminal Run.");
                var active = _state.entries.Single(entry => entry.active && !entry.finished);
                if (active.payment == null)
                {
                    // An Evo cache can outlive lawful deck changes made after leaving it.
                    // Its occurrence/host IDs remain frozen; BuildOffer and commit recheck each current host.
                    if (active.prepared.resolverContractId != EvoStatueOfferContractId)
                        RequireHostsInRoster(active, _run.CaptureInteractionDraft());
                }
                else if (!active.payment.paid) RequireSelectedCards(active.payment, _run.CaptureInteractionDraft());
            }
            RequireConsumedProof();
        }

        private void RequirePublicBoundary(string nodeId, bool allowRecovery = false)
        {
            GemChestRunCodec.RequireId(nodeId);
            if (_committing) throw Bad("Gem transaction is already committing.");
            if (allowRecovery && !_run.TryRecoverSealRewardWallet(out string policyRecoveryError))
                throw Bad(policyRecoveryError);
            if (_unrestoredWallet != null)
            {
                if (!allowRecovery) throw Bad("Prior economy recovery is incomplete.");
                _committing = true;
                try
                {
                    if (!RestoreWallet(_unrestoredWallet, _unrestoredBalance, out string failure)) throw Bad("Prior economy recovery is incomplete: " + failure);
                    _unrestoredWallet = null;
                }
                finally { _committing = false; }
            }
            if (_run.IsRewardCommitInProgress || _run.TryGetConclusion(out _) ||
                _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted || _run.Interactions.HasOwnPendingChoice ||
                _run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy) || _run.Crystals.HasPendingEvent ||
                HasPendingChoice && PendingNodeContentId != nodeId) throw Bad("Resolve the active Run reward boundary first.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId)
                throw Bad("Gem requires its matching idle map interaction.");
        }
        private void CommitCheckpoint(LevelRunSession floor, string nodeId, LevelInteractionEffect effect, GemChestRunSaveData next, Action persist)
        {
            _run.CommitRewardState(() => { EnsureActive(floor, nodeId, effect); _state = next; Seal(); }, StrictPersist(persist, floor, nodeId, effect));
        }
        private Action StrictPersist(Action persist, LevelRunSession floor, string nodeId, LevelInteractionEffect effect,
            IDungeonRunInteractionWallet wallet = null)
            => () =>
            {
                string checkpoint = _run.SerializeForDeterministicComparison(); int balance = wallet?.Balance ?? 0;
                persist();
                if (!ReferenceEquals(floor, _run.CurrentFloor) || EffectFingerprint(RequireEffect(floor, nodeId)) != EffectFingerprint(effect) ||
                    _run.SerializeForDeterministicComparison() != checkpoint || wallet != null && wallet.Balance != balance)
                    throw Bad("Persistence callback changed the prepared Run/economy checkpoint.");
            };
        private void Complete(LevelRunSession floor, string nodeId, LevelInteractionEffect effect, GemChestEntryData entry,
            GemChestRunSaveData next, string decisionId, Action persist)
        {
            if (!floor.TryRecordExternalEffect(nodeId, effect, entry.before, entry.after, entry.amount,
                out var receipt, out string error, entry.targetId, entry.targetContentId) || !receipt.NewlyApplied)
                throw Bad(error ?? "Gem receipt changed during commit.");
            if (!floor.TryCompleteInteraction(nodeId, out error)) throw Bad(error);
            entry.finished = true; entry.active = false; entry.decisionId = decisionId; _state = next; Seal(); persist?.Invoke();
        }
        private DungeonRunInteractionOffer BuildOffer(LevelInteractionEffect effect, GemChestEntryData entry,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft)
        {
            if (entry.finished) return Completed(effect, _run.CurrentFloor);
            if (!_run.CanReceiveSealRewards) return LockedOffer(effect, catalog);
            if (!entry.active && entry.payment == null)
                return new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Ready, Title = effect.summaryText };
            if (entry.payment != null && !entry.payment.paid) return BuildPaymentOffer(effect, entry, draft);
            var options = new List<DungeonRunInteractionOption>(); CardSealSpec selectedSeal = null;
            if (entry.selectedOccurrence < 0)
            {
                for (int i = 0; i < entry.prepared.choices.Count; i++) options.Add(new DungeonRunInteractionOption {
                    Id = GemKey(i), Seal = entry.prepared.choices[i].seal.Clone(), CardContentId = entry.prepared.choices[i].evolutionOutcomeContentId });
            }
            else
            {
                var selected = entry.prepared.choices[entry.selectedOccurrence]; selectedSeal = selected.seal.Clone();
                foreach (var saved in draft.Cards)
                {
                    if (!selected.hostIds.Contains(saved.runtimeInstanceId) || !MatchesHostLineage(entry, saved)) continue;
                    if (DungeonRunSealAssignmentRules.TryPrepare(saved, catalog?.FindCard(saved.contentId), selected.seal,
                        id => id == selected.evolutionOutcomeContentId && catalog.CanEvolveTo(id), out _, out _, out _,
                        references: DungeonRunCardCodec.CreateResolver(catalog.Cards)))
                        options.Add(new DungeonRunInteractionOption { Id = HostKey(entry.selectedOccurrence, saved.runtimeInstanceId),
                            CardContentId = saved.contentId, CardRuntimeInstanceId = saved.runtimeInstanceId });
                }
            }
            if (catalog?.Wallet != null) options.Add(new DungeonRunInteractionOption { Id = "cash-out",
                Title = $"化为灵玉 · +{catalog.Wallet.Preview(200).FinalJade}", Description = "按当前敛玉结算灵玉。" });
            return new DungeonRunInteractionOffer { Phase = entry.selectedOccurrence < 0 ? DungeonRunInteractionPhase.SealChoice : DungeonRunInteractionPhase.SealTarget,
                Title = string.IsNullOrWhiteSpace(effect.summaryText) ? "选择灵印" : effect.summaryText,
                SelectedSeal = selectedSeal, Options = options.AsReadOnly(), Message = options.Count == 0 ? "没有合法宿主或化玉账本。" : null };
        }
        private static DungeonRunInteractionOffer LockedOffer(LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog)
            => new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.SealChoice, Title = effect.summaryText,
                Options = catalog?.Wallet == null ? Array.Empty<DungeonRunInteractionOption>() :
                    new[] { new DungeonRunInteractionOption { Id = "cash-out", Title = $"化为灵玉 · +{catalog.Wallet.Preview(200).FinalJade}" } } };

        private static DungeonRunInteractionOffer Completed(LevelInteractionEffect effect, LevelRunSession floor)
        {
            if (!floor.TryGetInteractionReceipt(effect.effectId, out var receipt)) throw Bad("Missing completed Gem receipt.");
            return new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Completed, Title = effect.summaryText, Receipt = receipt };
        }
        private GemChestEntryData Find(string nodeId) => _state.entries.FirstOrDefault(entry => entry.floorId == _run.CurrentFloorContentId && entry.nodeId == nodeId);
        private static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        {
            var node = floor.FindNode(nodeId); var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            var errors = new List<string>(); ValidateEffect(effect, errors);
            if (node?.nodeType != LevelNodeType.Interaction || !floor.IsNodeEnabled(nodeId) || errors.Count != 0) throw Bad("Not a canonical enabled single Gem chest.");
            return effect;
        }
        private void RequireAvailable(string nodeId)
        {
            var floor = _run.CurrentFloor; var state = floor.GetNodeState(nodeId);
            if (state != LevelNodeViewState.Available && !(state == LevelNodeViewState.Completing && floor.ActiveInteractionNodeId == nodeId))
                throw Bad("Gem node is not available.");
        }
        private void EnsureActive(LevelRunSession floor, string nodeId, LevelInteractionEffect effect)
        { RequireSameEffect(floor, nodeId, effect); if (!floor.TryBeginInteraction(nodeId, out string error)) throw Bad(error); }
        private void RequireSameEffect(LevelRunSession floor, string nodeId, LevelInteractionEffect effect)
        {
            if (!ReferenceEquals(floor, _run.CurrentFloor) || EffectFingerprint(RequireEffect(floor, nodeId)) != EffectFingerprint(effect) ||
                floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Gem floor/effect changed during a participant callback.");
        }
        private static string EffectFingerprint(LevelInteractionEffect effect)
        {
            // Unity's absent optional-class shells and optional display strings are equivalent
            // at this execution boundary. The full floor-directory fingerprint separately keeps
            // every authored null/presence distinction; this proof binds the effective Gem contract.
            var canonical = new LevelInteractionEffect { effectId = effect.effectId, kind = effect.kind,
                amount = effect.amount, amountMax = effect.amountMax, targetContentId = effect.targetContentId ?? string.Empty,
                presentationId = effect.presentationId ?? string.Empty, rarity = effect.rarity,
                summaryText = effect.summaryText ?? string.Empty, detailText = effect.detailText ?? string.Empty,
                sealRewardPoolId = effect.sealRewardPoolId, cashOutJade = effect.cashOutJade };
            return GemChestRunCodec.Hash(GemChestRunCodec.Write(canonical));
        }
        private static string GemKey(int occurrence) => "gem/" + occurrence;
        private static string HostKey(int occurrence, int physicalId) => "host/" + occurrence + "/" + physicalId;
        private static bool TryGemIndex(string text, int count, out int value)
        { value = -1; return text.StartsWith("gem/", StringComparison.Ordinal) && int.TryParse(text.Substring(4), out value) && value >= 0 && value < count && text == GemKey(value); }
        private static int ParseHost(string text, int occurrence)
        {
            string prefix = "host/" + occurrence + "/";
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(text.Substring(prefix.Length), out int id) || id <= 0 || text != HostKey(occurrence, id))
                throw Bad("Invalid exact Gem host command."); return id;
        }
        private static GemChestPreparedData ClonePrepared(GemChestPreparedData data)
            => UnityEngine.JsonUtility.FromJson<GemChestPreparedData>(GemChestRunCodec.Write(data));
        private static GemChestCardData CaptureCard(DungeonRunCardSaveData card) => GemChestRunCodec.CaptureCard(card);
        private static void CaptureHosts(GemChestEntryData entry, DungeonRunInteractionDraft draft)
        {
            var ids = new HashSet<int>(entry.prepared.choices.SelectMany(choice => choice.hostIds));
            entry.hostCards = draft.Cards.Where(card => ids.Contains(card.runtimeInstanceId)).Select(CaptureCard).ToList();
            if (entry.hostCards.Count != ids.Count) throw Bad("Frozen host is outside the real roster.");
        }
        private static bool MatchesHostLineage(GemChestEntryData entry, DungeonRunCardSaveData card)
        {
            if (entry.physicalProofVersion == 1) return true;
            var frozen = entry.hostCards.SingleOrDefault(host => host.runtimeInstanceId == card.runtimeInstanceId);
            return frozen != null && frozen.contentId == card.contentId && frozen.lineageVersion == card.lineageVersion &&
                frozen.lineageState == card.lineageState && frozen.baseAuthorJson == card.baseAuthorJson && frozen.currentAuthorJson == card.currentAuthorJson;
        }
        private void Seal() => _state.fingerprint = GemChestRunCodec.Fingerprint(_state);
        private static void RequireGain(JadeGainResult gain)
        { if (gain.Source != JadeSource.CashOut || gain.BaseJade != 200 || gain.FinalJade < 0 || !gain.AffectedByGreed) throw Bad("Invalid fixed-200 Greed cash-out receipt."); }
        private static bool RestoreWallet(IDungeonRunInteractionWallet wallet, int balance, out string error)
        {
            error = null; try { wallet.RestoreBalance(balance); if (wallet.Balance == balance) return true; error = "Wrong restored balance."; }
            catch (Exception exception) { error = exception.Message; } return false;
        }
        private static void RequireHostsInRoster(GemChestEntryData entry, DungeonRunInteractionDraft draft)
        {
            var ids = new HashSet<int>(draft.Cards.Select(card => card.runtimeInstanceId));
            if (entry.prepared.choices.Any(choice => choice.hostIds.Any(id => !ids.Contains(id)))) throw Bad("Frozen Gem host is outside the real Run roster.");
            foreach (var card in draft.Cards.Where(card => entry.prepared.choices.Any(choice => choice.hostIds.Contains(card.runtimeInstanceId))))
                if (!MatchesHostLineage(entry, card)) throw Bad("Frozen Gem host lineage changed.");
        }
        private static void RequireFloorEntry(LevelRunSession floor, GemChestEntryData entry)
        {
            var effect = RequireEffect(floor, entry.nodeId);
            if (entry.floorId != floor.Profile.contentId || entry.effectId != effect.effectId || entry.effectFingerprint != EffectFingerprint(effect) ||
                (entry.payment?.resolverContractId ?? entry.prepared?.resolverContractId) != effect.sealRewardPoolId) throw Bad("Gem journal and effective authored contract disagree.");
            bool has = floor.TryGetInteractionReceipt(effect.effectId, out var receipt); var state = floor.GetNodeState(entry.nodeId);
            if (entry.finished)
            {
                if (!has || state != LevelNodeViewState.Completed || receipt.Kind != effect.kind || receipt.ValueBefore != entry.before ||
                    receipt.ValueAfter != entry.after || receipt.Amount != entry.amount || receipt.TargetRuntimeInstanceId != entry.targetId ||
                    (receipt.TargetContentId ?? string.Empty) != entry.targetContentId || (receipt.PresentationId ?? string.Empty) != (effect.presentationId ?? string.Empty) ||
                    (receipt.SummaryText ?? string.Empty) != (effect.summaryText ?? string.Empty) || (receipt.DetailText ?? string.Empty) != (effect.detailText ?? string.Empty) ||
                    receipt.Rarity != effect.rarity) throw Bad("Finished Gem lacks its exact floor receipt.");
            }
            else if (!entry.active)
            {
                if (entry.payment == null && entry.prepared?.resolverContractId != EvoStatueOfferContractId ||
                    has || state != LevelNodeViewState.Available || floor.ActiveInteractionNodeId == entry.nodeId)
                    throw Bad("Dormant adopted Gem cache must be an available, unsubmitted node.");
            }
            else if (has || state != LevelNodeViewState.Available && state != LevelNodeViewState.Completing ||
                state == LevelNodeViewState.Completing && floor.ActiveInteractionNodeId != entry.nodeId ||
                state == LevelNodeViewState.Available && !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) ||
                floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _)) throw Bad("Pending Gem and floor lifecycle disagree.");
        }
        private void ValidateLocal()
        {
            if (_state.version != 3 || _state.runId != _run.RunId || _state.entries == null || _state.entries.Count(entry => entry.active && !entry.finished) > 1)
                throw Bad("Invalid Gem journal version or pending count.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var consumed = new HashSet<int>();
            foreach (var entry in _state.entries)
            {
                if (entry == null) throw Bad("Null Gem journal entry.");
                GemChestRunCodec.RequireId(entry.floorId); GemChestRunCodec.RequireId(entry.nodeId); GemChestRunCodec.RequireId(entry.effectId);
                if (!identities.Add(entry.floorId + "\n" + entry.nodeId)) throw Bad("Duplicate Gem floor/node identity.");
                GemChestRunCodec.RequireHash(entry.effectFingerprint);
                if (entry.hostCards == null || entry.physicalProofVersion < 1 || entry.physicalProofVersion > 2)
                    throw Bad("Missing physical proof version or host snapshot.");
                RequireRoster(entry.hostCards);
                if (entry.physicalProofVersion == 1 && entry.hostCards.Count != 0)
                    throw Bad("Legacy offer cannot invent frozen hosts.");
                if (entry.physicalProofVersion == 1)
                {
                    var historicalCards = new[] { entry.cardBefore, entry.cardAfter }.Where(card => card != null);
                    if (entry.payment != null)
                        historicalCards = historicalCards.Concat(entry.payment.openingRoster ?? Enumerable.Empty<GemChestCardData>())
                            .Concat(entry.payment.rosterBeforePayment ?? Enumerable.Empty<GemChestCardData>())
                            .Concat(entry.payment.rosterAfterPayment ?? Enumerable.Empty<GemChestCardData>())
                            .Concat(entry.payment.candidates?.Select(candidate => candidate?.card) ?? Enumerable.Empty<GemChestCardData>());
                    if (historicalCards.Any(card => card == null || card.lineageState != (int)CardAuthorProvenance.LegacyUnknown))
                        throw Bad("Legacy Gem proof cannot invent frozen known authors.");
                }
                if (entry.physicalProofVersion == 2)
                {
                    var hostIds = new HashSet<int>(entry.prepared?.choices.SelectMany(choice => choice.hostIds) ?? Enumerable.Empty<int>());
                    if (!hostIds.SetEquals(entry.hostCards.Select(card => card.runtimeInstanceId)))
                        throw Bad("Full host snapshots differ from the exact frozen host subset.");
                }
                if (entry.payment == null)
                {
                    GemChestRunCodec.ValidatePrepared(entry.prepared);
                    if (entry.prepared.resolverContractId == SilverOfferContractId ||
                        !entry.finished && !entry.active && entry.prepared.resolverContractId != EvoStatueOfferContractId)
                        throw Bad("Only the adopted Evo policy can carry an unpaid dormant Gem cache.");
                    if (!entry.finished && !entry.active && entry.selectedOccurrence != -1)
                        throw Bad("Dormant Evo cache cannot retain a selected occurrence.");
                }
                else
                {
                    RequirePayment(entry);
                    if (entry.payment.paid)
                        foreach (int id in entry.payment.selectedIds)
                            if (!consumed.Add(id)) throw Bad("Silver entries consumed the same physical instance.");
                }
                if (entry.finished && entry.active) throw Bad("Finished Gem still owns active activity.");
                int choiceCount = entry.prepared?.choices.Count ?? 0;
                if (entry.selectedOccurrence < -1 || entry.selectedOccurrence >= choiceCount || entry.disposition < 0 || entry.disposition > 2 ||
                    entry.before < 0 || entry.after < 0 || entry.amount < 0 || entry.targetId < 0) throw Bad("Invalid Gem disposition values.");
                if (!entry.finished)
                {
                    if (entry.disposition != 0 || entry.decisionId != string.Empty || entry.before != 0 || entry.after != 0 || entry.amount != 0 ||
                        entry.targetId != 0 || entry.targetContentId != string.Empty || entry.cardBefore != null || entry.cardAfter != null)
                        throw Bad("Pending Gem carries a terminal proof.");
                }
                else if (entry.disposition == 2)
                {
                    if (entry.decisionId != "cash-out" || entry.targetId != 0 || entry.targetContentId != string.Empty ||
                        (long)entry.after - entry.before != entry.amount || entry.cardBefore != null || entry.cardAfter != null)
                        throw Bad("Invalid Gem cash-out proof.");
                }
                else if (entry.disposition == 1) RequireAssignmentProof(entry);
                else throw Bad("Finished Gem lacks a disposition.");
            }
        }
        private static void RequireAssignmentProof(GemChestEntryData entry)
        {
            if (entry.selectedOccurrence < 0 || entry.before != 0 || entry.after != 1 || entry.amount != 1 || entry.targetId <= 0 ||
                entry.decisionId != HostKey(entry.selectedOccurrence, entry.targetId)) throw Bad("Invalid Gem assignment decision.");
            var selected = entry.prepared.choices[entry.selectedOccurrence]; var before = entry.cardBefore; var after = entry.cardAfter;
            RequireCard(before); RequireCard(after);
            if (!selected.hostIds.Contains(entry.targetId) || before.runtimeInstanceId != entry.targetId || after.runtimeInstanceId != entry.targetId ||
                !before.hasGemSlotCapacity || !after.hasGemSlotCapacity || before.seals.Count >= before.gemSlotCapacity ||
                entry.targetContentId != selected.seal.contentId) throw Bad("Invalid physical Gem assignment proof.");
            bool evo = selected.seal.effect == CardSealEffect.Evolution;
            var expected = before.seals.Select(seal => seal.Clone()).ToList(); if (!evo) expected.Add(selected.seal.Clone());
            if (GemChestRunCodec.Write(expected) != GemChestRunCodec.Write(after.seals) ||
                after.contentId != (evo ? selected.evolutionOutcomeContentId : before.contentId) ||
                after.limitBreakCount != (evo ? 0 : before.limitBreakCount) ||
                after.gemSlotCapacity != before.gemSlotCapacity - (evo ? 1 : 0)) throw Bad("Gem card proof violates exact host/primary-only evolution policy.");
            var savedBefore = GemChestRunCodec.RestoreCard(before); var savedAfter = GemChestRunCodec.RestoreCard(after);
            if (evo) DungeonRunCardCodec.ValidateEvolutionTransition(savedBefore, savedAfter, selected.evolutionOutcomeContentId);
            else if (before.lineageVersion != after.lineageVersion || before.lineageState != after.lineageState ||
                before.baseAuthorJson != after.baseAuthorJson || before.currentAuthorJson != after.currentAuthorJson)
                throw Bad("Ordinary seal assignment changed author lineage.");
            if (entry.physicalProofVersion == 2)
            {
                var frozen = entry.hostCards.SingleOrDefault(card => card.runtimeInstanceId == entry.targetId);
                if (frozen == null || frozen.contentId != before.contentId || frozen.lineageVersion != before.lineageVersion ||
                    frozen.lineageState != before.lineageState || frozen.baseAuthorJson != before.baseAuthorJson || frozen.currentAuthorJson != before.currentAuthorJson)
                    throw Bad("Assignment author lineage differs from its frozen host.");
            }
        }
        private static void RequireCard(GemChestCardData card)
        {
            if (card == null || card.runtimeInstanceId <= 0 || card.limitBreakCount < 0 || card.gemSlotCapacity < 0 || card.seals == null ||
                card.seals.Count > card.gemSlotCapacity) throw Bad("Invalid Gem physical card snapshot.");
            GemChestRunCodec.RequireId(card.contentId); foreach (var seal in card.seals) GemChestRunCodec.RequireId(seal?.contentId);
            DungeonRunCardCodec.Validate(GemChestRunCodec.RestoreCard(card));
        }
        private static InvalidOperationException Bad(string message) => GemChestRunCodec.Bad(message);
    }
}
