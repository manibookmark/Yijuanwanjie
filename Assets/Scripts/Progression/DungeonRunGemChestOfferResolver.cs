using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Compiles ordinary/Silver/Evo/Gold source-Gem inputs from explicit project account and physical-card evidence.
    /// Core owns count/weighted/fallback math; GemChests alone owns the durable lifecycle. This provider
    /// owns no mutable reward/account state and never opens, saves, installs, cashes out or rerolls an offer.
    /// The versioned project policy uses unlockedSeals and CardSealRules, including zero-benefit normal
    /// hosts and single-primary evolution with no materials. Native Jeweller, sealed/banished Gems and
    /// native tag exclusion predicates are not adopted. Their project projections are explicitly frozen.
    /// A color-only Gem needs native global-FCC evidence which the project does not own: that candidate
    /// is retained in the input and locally rejected, never granted by a card color or current crawler.
    /// The ordered 20-object native database subsequence and source frequency/weight bits are preserved;
    /// neither registry author order nor project grades determine selection. The per-effect LCG stream
    /// is a project adaptation and does not claim native same-seed equivalence. Back/restore uses the
    /// GemChests checkpoint and does not call this provider. Silver adds an explicit T2 configuration
    /// and exact source-card type projection within this same stateless evidence boundary. Content
    /// owns the immutable author evidence directory; unmapped cards are not presumed non-FCC. Payment
    /// eligibility never uses project colors, Wild costs or the Offering flag. The whole-Run payment
    /// scope requires explicit construction-time authorization. Reassess before another chest policy
    /// or a live native achievement/tag system is introduced; no journal state is owned here.
    /// Evo adds source guarantee/complete-five-frequency filtering within this same evidence boundary;
    /// its approved single-primary and return/cash-out adaptation is explicit in the frozen input.
    /// Gold uses the captured ChestT3 Epic/Legendary frequencies and conditional evolution guarantee,
    /// retaining ordinary chest disposition and the project's fixed-200 cash-out policy. It does not
    /// infer a live native chest-tier roll from an enemy mask or own another reward journal.
    /// </summary>
    public sealed class DungeonRunGemChestOfferResolver : IDungeonRunGemChestOfferResolver, IDungeonRunGemChestPaymentResolver
    {
        public const string ContractId = DungeonRunGemChests.OrdinaryOfferContractId;
        public const string SilverContractId = DungeonRunGemChests.SilverOfferContractId;
        public const string EvoStatueContractId = DungeonRunGemChests.EvoStatueOfferContractId;
        public const string GoldContractId = DungeonRunGemChests.GoldOfferContractId;
        public const string SourceRegistryContentId = "gemsource.xiyou.vc.v1";
        private const string HostTag = "project.gem.cardsealrules.v2";
        private readonly DungeonRunSession _run;
        private readonly MetaProgression _account;
        private readonly ContentCatalog _content;
        private readonly bool _allowSilverRunDeckPayment;

        /// <summary>Read-only entry capability. Querying it never prepares payment or samples its requirements.</summary>
        public bool IsSilverRunDeckPaymentAuthorized => _allowSilverRunDeckPayment;

        // Explicit stable-ID/native-object mapping from GemDatabase's _assetList, not m_Name or grade.
        private static readonly string[] OrderedSealIds = {
            "seal.xiyou.benglie.common.v1", "seal.xiyou.armor.common.v1", "seal.xiyou.double-damage.common.v1",
            "seal.xiyou.might.common.v1", "seal.xiyou.mug.common.v1", "seal.xiyou.copy.very-rare.v1",
            "seal.xiyou.leader.very-rare.v1", "seal.xiyou.midas.very-rare.v1", "seal.xiyou.armor-strike.very-rare.v1",
            "seal.xiyou.mana-rebate.ultra-rare.v1", "seal.common.huaxing.immortal.v1", "seal.xiyou.amount.common.v1",
            "seal.xiyou.draw.common.v1", "seal.xiyou.reverse-combo.rare.v1", "seal.xiyou.triple-damage.rare.v1",
            "seal.xiyou.yellow-trigger.uncommon.v1", "seal.xiyou.nduja.uncommon.v1", "seal.xiyou.refund.uncommon.v1",
            "seal.xiyou.retain.uncommon.v1", "seal.xiyou.return.rare.v1" };
        private static readonly string[] OrderedAssetIds = {
            "GemConfig_Area", "GemConfig_Armor", "GemConfig_DoubleDamage", "GemConfig_Might", "GemConfig_Steal",
            "GemConfig_CloneCard", "GemConfig_Leader", "GemConfig_Midas", "GemConfig_Spike", "GemConfig_Arcane",
            "GemConfig_Evolve", "GemConfig_Amount", "GemConfig_Draw", "GemConfig_ReverseCombo", "GemConfig_TripleDamage",
            "GemConfig_AddType_Special", "GemConfig_Nduja", "GemConfig_Refresh", "GemConfig_Retain", "GemConfig_ReturnToHand" };
        private static readonly string[] OrderedFrequencyIds = {
            "sidefrequency.xiyou.g4_alpha", "sidefrequency.xiyou.g4_beta", "sidefrequency.xiyou.g4_gamma",
            "sidefrequency.xiyou.g4_delta", "sidefrequency.xiyou.g4_epsilon" };

        public DungeonRunGemChestOfferResolver(DungeonRunSession run, MetaProgression account, ContentCatalog content,
            bool allowSilverRunDeckPayment = false)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _allowSilverRunDeckPayment = allowSilverRunDeckPayment;
        }

        public bool TryPrepare(string runId, LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            out GemChestPreparedOffer offer, out string error)
        {
            offer = null; error = null; List<CardConfig> cards = null;
            try
            {
                Require(runId == _run.RunId && catalog != null && effect != null, "Wrong Run or missing catalog/effect.");
                var contractErrors = new List<string>(); DungeonRunGemChests.ValidateEffect(effect, contractErrors);
                Require(contractErrors.Count == 0, string.Join("; ", contractErrors));
                bool silver = effect.sealRewardPoolId == SilverContractId;
                bool evoStatue = effect.sealRewardPoolId == EvoStatueContractId;
                bool gold = effect.sealRewardPoolId == GoldContractId;
                bool guaranteeEvo = evoStatue || gold;
                Require(DungeonSideEventQualificationResolver.ResolveGemHammerPermission(_account, _content) ==
                    SideEventRequirementEligibility.Eligible, "Gem rewards require the exact unlocked GemHammer relic permission.");
                string resolverContract = silver ? SilverContractId : evoStatue ? EvoStatueContractId : gold ? GoldContractId : ContractId;
                Require(effect.kind == LevelInteractionEffectKind.GemChest && effect.amount == 3 && effect.amountMax == 0 &&
                    effect.sealRewardPoolId == resolverContract && effect.cashOutJade == 200 &&
                    string.IsNullOrEmpty(effect.targetContentId) && (effect.sealChoices == null || effect.sealChoices.Count == 0),
                    "Gem chest requires its exact versioned three-choice/fixed-200 contract.");
                RequireId(effect.effectId);
                var nodes = _run.CurrentFloor.Nodes.Where(node => node.nodeType == LevelNodeType.Interaction &&
                    node.completionEffects != null && node.completionEffects.Count == 1 &&
                    node.completionEffects[0].effectId == effect.effectId).ToList();
                Require(nodes.Count == 1 && _run.CurrentFloor.MatchesConfiguredEffect(nodes[0].contentId, effect),
                    "Gem effect must be the enabled current floor's complete canonical single effect.");
                Require(_content.TryGetGemSourceDocument(SourceRegistryContentId, out var document), "Complete Gem source registry is missing.");
                var registry = document.Definition;
                var ordered = OrderedEntries(registry);
                var frequencies = OrderedFrequencies(registry);
                if (silver || evoStatue || gold) RequireCapturedFrequencies(frequencies);
                Require(_account.unlockedSeals != null, "Project account Gem qualification is missing.");
                var unlocked = new HashSet<string>(_account.unlockedSeals, StringComparer.OrdinalIgnoreCase);
                foreach (string id in unlocked) RequireId(id);
                var specs = ResolveSpecifications(catalog);
                foreach (var entry in ordered.Where(entry => unlocked.Contains(entry.ProjectSealContentId)))
                {
                    Require(specs.TryGetValue(entry.ProjectSealContentId, out var spec), "Unlocked Gem has no resolved project specification.");
                    Require((spec.effect == CardSealEffect.Evolution) == entry.IsEvolution,
                        "Project specification contradicts source evolution classification.");
                }
                Require(_run.TryGetCurrentCards(catalog.Cards, out cards, out string cardError), cardError ?? "Physical Run cards are unavailable.");
                var releases = cards.Where(card => card.evolutionTarget != null).Select(card => card.evolutionTarget.contentId)
                    .Distinct(StringComparer.Ordinal).ToDictionary(id => id, catalog.CanEvolveTo, StringComparer.Ordinal);
                Func<string, bool> canEvolve = id => releases.TryGetValue(id, out bool eligible) && eligible;
                var input = Snapshot(document, ordered, cards, specs, unlocked, catalog, effect, nodes[0].contentId, canEvolve);
                if (silver)
                {
                    input.policy = "gem-silver-project-eligibility/v1";
                    input.randomPolicy = "gem-silver-reward/project-lcg-v1/no-native-seed-equivalence";
                    input.configuredFrequencyIds = OrderedFrequencyIds.Skip(2).ToList();
                    input.randomSeed = SeedWithDomain("gem-silver-reward/project-lcg-v1", input.runId,
                        input.runSeed.ToString(CultureInfo.InvariantCulture), input.floorId, input.effectId, SilverContractId);
                }
                if (evoStatue)
                {
                    input.policy = "gem-evo-statue-project-single-primary/v1";
                    input.randomPolicy = "gem-evo-statue/project-lcg-v1/no-native-seed-equivalence";
                    input.sourceCallPolicy = "evo-statue/guarantee-evo/three-false-source-flags";
                    input.sourceCanCashOut = false; input.allowFallbacks = false; input.ensureNonEvo = false;
                    input.guaranteeEvo = true;
                    input.noTargetPolicy = "approved-project-return-or-200-Greed-cash-out/no-component-card-or-BagOfCoins";
                    input.configuredFrequencyIds = OrderedFrequencyIds.ToList();
                    input.randomSeed = SeedWithDomain("gem-evo-statue/project-lcg-v1", input.runId,
                        input.runSeed.ToString(CultureInfo.InvariantCulture), input.floorId, input.effectId, EvoStatueContractId);
                }
                if (gold)
                {
                    input.policy = "gem-gold-project-single-primary/v1";
                    input.randomPolicy = "gem-gold/project-lcg-v1/no-native-seed-equivalence";
                    input.sourceCallPolicy = "gem-chest-view/ChestT3/guarantee-evo/default-offer-flags";
                    input.guaranteeEvo = true;
                    input.configuredFrequencyIds = OrderedFrequencyIds.Skip(3).ToList();
                    input.randomSeed = SeedWithDomain("gem-gold/project-lcg-v1", input.runId,
                        input.runSeed.ToString(CultureInfo.InvariantCulture), input.floorId, input.effectId, GoldContractId);
                }
                var bases = EvolutionBases(cards, specs, ordered, unlocked, canEvolve);
                // These are declared project predicates, not guessed native flags or tag-group membership.
                var context = new SideEventGemEligibilityContext(new SideEventOptionalBool(true),
                    new[] { HostTag }, Array.Empty<string>(), bases);
                var evidence = new List<SideEventGemCandidateEvidence>();
                foreach (var entry in ordered)
                {
                    bool available = unlocked.Contains(entry.ProjectSealContentId);
                    var hosts = new List<SideEventGemHostEvidence>();
                    if (available)
                        foreach (var card in cards)
                            if (CardSealRules.CanAssign(card, specs[entry.ProjectSealContentId],
                                    isEvolutionResultEligible: canEvolve))
                                hosts.Add(new SideEventGemHostEvidence(card.runtimeInstanceId,
                                    CardSealRules.GetSlotCapacity(card) - CardSealRules.GetOccupiedSlotCount(card),
                                    InstalledSourceIds(card, registry), Array.Empty<string>(), Array.Empty<string>(),
                                    Array.Empty<string>(), new SideEventOptionalBool(false)));
                    evidence.Add(new SideEventGemCandidateEvidence(entry.ProjectSealContentId, entry.SourceAssetId, entry.OwnWeight,
                        entry.IsEvolution, entry.IsUnique, new SideEventOptionalBool(available), new SideEventOptionalBool(false),
                        new SideEventOptionalBool(false), new SideEventGemFrequencyEvidence(entry.BaseFrequencyContentId,
                            entry.BaseFrequencyContentId, SideEventGemFrequencyShift.Unchanged), new[] { HostTag },
                        new SideEventOptionalBool(entry.HasNonColorEffects), Array.Empty<string>(), hosts));
                }
                var configuredFrequencies = evoStatue ? frequencies.ToArray() : gold ? frequencies.Skip(3).ToArray() :
                    silver ? frequencies.Skip(2).ToArray() : frequencies.Take(3).ToArray();
                var source = new SideEventGemOffer(3, 3, guaranteeEvo, configuredFrequencies,
                    new SideEventOptionalBool(!evoStatue), new SideEventOptionalBool(!evoStatue), new SideEventOptionalBool(!evoStatue),
                    new SideEventOptionalInt(200), null, SideEventOptionalInt.Unknown);
                var completeSource = new SideEventGemOffer(3, 3, guaranteeEvo, frequencies, new SideEventOptionalBool(!evoStatue),
                    new SideEventOptionalBool(!evoStatue), new SideEventOptionalBool(!evoStatue), new SideEventOptionalInt(200), null, SideEventOptionalInt.Unknown);
                input.rejectedCandidates = SideEventGemSelectionRules.Filter(completeSource, context, evidence).Rejected
                    .Select(item => new RejectedSnapshot { gemContentId = item.ContentId, reason = item.Reason.ToString() }).ToList();
                var random = new SeededRandomSource(input.randomSeed);
                float luck = (float)(1f + catalog.LuckPercent / 100f);
                var count = SideEventGemChoiceCountRules.Resolve(3, luck, new SideEventGemChoiceCountConfiguration(5, .1f), random);
                input.luckMultiplierBits = Bits(luck); input.countThresholdBits = Bits(count.Threshold);
                input.countDrawBits = Bits(count.Draw); input.countRollPassed = count.LuckRollPassed;
                input.resolvedCount = count.ResolvedCount;
                var result = SideEventGemOfferGenerationRules.Generate(new SideEventGemOfferGenerationRequest(source, context,
                    evidence, frequencies, count.ResolvedCount, Array.Empty<SideEventGemChoice>()), random);
                var choices = new List<GemChestPreparedChoice>();
                foreach (var choice in result.Choices)
                {
                    var spec = specs[choice.GemContentId];
                    var entry = ordered.Single(item => item.ProjectSealContentId == choice.GemContentId);
                    var hostIds = cards.Where(card => CardSealRules.CanAssign(card, spec,
                            isEvolutionResultEligible: canEvolve) &&
                        (!entry.IsUnique || !InstalledSourceIds(card, registry).Contains(entry.SourceAssetId)) &&
                        (choice.EvolutionOutcomeContentId == null || card.evolutionTarget?.contentId == choice.EvolutionOutcomeContentId))
                        .Select(card => card.runtimeInstanceId).ToArray();
                    Require(hostIds.Length > 0, "Generated Gem has no frozen lawful physical host.");
                    choices.Add(new GemChestPreparedChoice(choice, spec, hostIds));
                }
                offer = new GemChestPreparedOffer(resolverContract, registry.ContentId, registry.Fingerprint,
                    JsonUtility.ToJson(input), result, choices);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally
            {
                if (cards != null) foreach (var card in cards) if (card != null)
                { if (Application.isPlaying) UnityEngine.Object.Destroy(card); else UnityEngine.Object.DestroyImmediate(card); }
            }
        }

        public bool TryPreparePayment(string runId, LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            out GemChestPreparedPayment payment, out string error)
        {
            payment = null; error = null; List<CardConfig> cards = null;
            try
            {
                Require(_allowSilverRunDeckPayment, "Silver whole-Run payment scope is not explicitly authorized.");
                Require(runId == _run.RunId && effect != null && catalog != null, "Wrong Run or missing Silver catalog/effect.");
                Require(DungeonSideEventQualificationResolver.ResolveGemHammerPermission(_account, _content) ==
                    SideEventRequirementEligibility.Eligible, "Silver payment requires the exact unlocked GemHammer relic permission.");
                var errors = new List<string>(); DungeonRunGemChests.ValidateEffect(effect, errors);
                Require(errors.Count == 0, string.Join("; ", errors));
                Require(effect.kind == LevelInteractionEffectKind.GemChest && effect.amount == 3 && effect.amountMax == 0 &&
                    effect.sealRewardPoolId == SilverContractId && effect.cashOutJade == 200,
                    "Silver payment requires its exact versioned T2/fixed-200 contract.");
                var nodes = _run.CurrentFloor.Nodes.Where(node => node.nodeType == LevelNodeType.Interaction &&
                    node.completionEffects != null && node.completionEffects.Count == 1 &&
                    node.completionEffects[0].effectId == effect.effectId).ToArray();
                Require(nodes.Length == 1 && _run.CurrentFloor.MatchesConfiguredEffect(nodes[0].contentId, effect),
                    "Silver payment effect must match the complete enabled current node.");
                Require(_content.TryGetGemSourceDocument(SourceRegistryContentId, out var document), "Complete Gem source registry is missing.");
                Require(_run.TryGetCurrentCards(catalog.Cards, out cards, out string cardError), cardError ?? "Physical Run cards are unavailable.");
                Require(cards.All(card => card != null && card.runtimeInstanceId > 0) &&
                    cards.Select(card => card.runtimeInstanceId).Distinct().Count() == cards.Count,
                    "Silver payment requires unique real physical instances.");
                var input = new PaymentInputSnapshot {
                    runId = runId, runSeed = _run.RunSeed, floorId = _run.CurrentFloor.Profile.contentId,
                    nodeId = nodes[0].contentId, effectId = effect.effectId,
                    sourceCanonicalJson = document.CanonicalJson, sourceFingerprint = document.Fingerprint,
                    seed = SeedWithDomain("gem-silver-payment/project-lcg-v1", runId,
                        _run.RunSeed.ToString(CultureInfo.InvariantCulture), _run.CurrentFloor.Profile.contentId, effect.effectId, SilverContractId)
                };
                var candidates = new List<SilverGemChestPaymentCard>();
                foreach (var card in cards)
                {
                    var proof = ProjectPaymentCard(card, document.Definition);
                    input.cards.Add(proof);
                    if (proof.diagnostic.Length == 0 && proof.paymentTypes.Count > 0)
                        candidates.Add(new SilverGemChestPaymentCard(card.runtimeInstanceId, proof.paymentTypes, false));
                }
                input.requirements = SideEventSilverGemChestPaymentRules.GenerateRequirements(new SeededRandomSource(input.seed)).ToList();
                payment = new GemChestPreparedPayment(SilverContractId, JsonUtility.ToJson(input), input.requirements, candidates);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally
            {
                if (cards != null) foreach (var card in cards) if (card != null)
                { if (Application.isPlaying) UnityEngine.Object.Destroy(card); else UnityEngine.Object.DestroyImmediate(card); }
            }
        }

        private PaymentCardSnapshot ProjectPaymentCard(CardConfig card, GemSourceRegistryDefinition registry)
        {
            var admission = card.OfferingAdmission;
            var proof = new PaymentCardSnapshot {
                runtimeInstanceId = card.runtimeInstanceId, cardContentId = card.contentId,
                projectColor = card.color.ToString(), projectCostType = card.costType.ToString(),
                sourceConfigId = admission.SourceConfigId, sourceClass = admission.SourceClass,
                rawOfferingFlag = admission.RawAllowInOfferingTable,
                slotCapacity = CardSealRules.GetSlotCapacity(card), limitBreakCount = card.LimitBreakCount,
                seals = (card.seals ?? new List<CardSealSpec>()).Select(seal => seal?.Clone()).ToList()
            };
            string admissionError = admission.Validate();
            if (admissionError != null) { proof.diagnostic = "InvalidSourceIdentityEvidence:" + admissionError; return proof; }
            if (!_content.TryGetCardSourceIdentity(card.contentId, out CardSourceIdentity identity))
            {
                if (admission.SourceClass == "FccConfig")
                { proof.isFccKnown = true; proof.isFcc = true; proof.diagnostic = "SourceFccExcluded"; }
                else proof.diagnostic = "UnknownSourceIdentityOrClass";
                return proof;
            }
            if (admission.State != CardOfferingAdmissionState.Unknown &&
                (admission.SourceConfigId != identity.SourceConfigId || admission.SourceClass != identity.SourceClass))
            { proof.diagnostic = "ContradictoryPhysicalCardSourceIdentity"; return proof; }
            proof.sourceConfigId = identity.SourceConfigId; proof.sourceClass = identity.SourceClass;
            proof.sourceAlias = identity.SourceAlias; proof.sourceFile = identity.SourceFile;
            proof.sourceCaptureSha256 = identity.SourceCaptureSha256;
            proof.sourceObjectPathId = identity.SourcePathId;
            proof.sourceCardTypeFile = identity.CardTypeSourceFile;
            proof.sourceCardTypePathId = identity.CardTypePathId;
            proof.sourceCardTypeFileIndex = identity.CardTypeFileIndex;
            proof.isFccKnown = true; proof.isFcc = identity.IsFcc;
            if (identity.IsFcc)
            { proof.isFccKnown = true; proof.isFcc = true; proof.diagnostic = "SourceFccExcluded"; return proof; }
            proof.nativeCardTypes.Add(identity.CardTypeAssetId);
            foreach (var seal in proof.seals)
            {
                if (seal == null || !Enum.IsDefined(typeof(CardSealEffect), seal.effect) || seal.value <= 0)
                { proof.diagnostic = "InvalidInstalledSealEvidence"; return proof; }
                if (seal.effect != CardSealEffect.YellowTrigger) continue;
                if (seal.contentId != "seal.xiyou.yellow-trigger.uncommon.v1" ||
                    !registry.TryGetBySealId(seal.contentId, out var entry) || entry.SourceAssetId != "GemConfig_AddType_Special" ||
                    !entry.IsEffectClassificationKnown || entry.HasNonColorEffects || entry.ColorTriggerReferences.Count != 1)
                { proof.diagnostic = "UnknownInstalledSourceCardTypeOverride"; return proof; }
                var reference = entry.ColorTriggerReferences[0];
                if (reference.Provenance.Alias != "vc-local-full-copy" ||
                    reference.Provenance.SourceFile != "Vampire Crawlers_Data/globalgamemanagers.assets" ||
                    reference.Provenance.PathId != 7554 || reference.Pointer.PathId != 7554 || reference.Pointer.FileIndex != 1)
                { proof.diagnostic = "ContradictoryYellowSourceCardTypeReference"; return proof; }
                // One native effect appends once, independent of Gem value. Retain installed order/repeats.
                proof.nativeCardTypes.Add(SideEventSilverGemChestPaymentRules.SpecialSourceAssetId);
            }
            proof.paymentTypes = proof.nativeCardTypes.Where(type =>
                SideEventSilverGemChestPaymentRules.SourceCardTypePool.Contains(type)).ToList();
            if (proof.paymentTypes.Count == 0) proof.diagnostic = "KnownNativeTypesOutsideSilverThreeColorPool";
            return proof;
        }

        private static void RequireCapturedFrequencies(IReadOnlyList<SideEventGemFrequency> frequencies)
        {
            float[] weights = { 160f, 120f, 80f, 60f, 40f };
            long[] sourcePaths = { 19652, 19656, 19655, 19653, 19654 };
            for (int i = 0; i < weights.Length; i++)
            {
                Require(Bits(frequencies[i].ChestWeight) == Bits(weights[i]),
                    "Source frequency weights must preserve the captured complete five-frequency binary32 values.");
                Require(frequencies[i].Provenance.Alias == "vc-local-full-copy" &&
                    frequencies[i].Provenance.SourceFile == "Vampire Crawlers_Data/sharedassets0.assets" &&
                    frequencies[i].Provenance.PathId == sourcePaths[i],
                    "Source frequency provenance must bind its actual five native objects.");
            }
        }

        private static IReadOnlyList<GemSourceEntry> OrderedEntries(GemSourceRegistryDefinition registry)
        {
            Require(registry.Entries.Count == OrderedSealIds.Length, "The versioned native database mapping requires all 20 entries.");
            var result = new List<GemSourceEntry>();
            for (int i = 0; i < OrderedSealIds.Length; i++)
            {
                Require(registry.TryGetBySealId(OrderedSealIds[i], out var entry) && entry.SourceAssetId == OrderedAssetIds[i],
                    "Native Gem database stable-ID mapping is incomplete or contradictory.");
                Require(entry.IsEffectClassificationKnown, "Native Gem effect classification is unknown.");
                Require(entry.IsEvolution == (i == 10), "Native Gem evolution identity changed.");
                // Only the captured Yellow object is intentionally color-only in this 20-object projection.
                Require(entry.HasNonColorEffects || entry.SourceAssetId == "GemConfig_AddType_Special",
                    "Unsupported color-only Gem source.");
                result.Add(entry);
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<SideEventGemFrequency> OrderedFrequencies(GemSourceRegistryDefinition registry)
        {
            Require(registry.Frequencies.Count == 5, "Complete five-frequency source database is required.");
            return Array.AsReadOnly(OrderedFrequencyIds.Select(id =>
            {
                var item = registry.Frequencies.SingleOrDefault(value => value.ContentId == id);
                Require(item != null, "Source frequency identity is missing.");
                return new SideEventGemFrequency(item.ContentId, item.Provenance, item.ChestWeight, item.LevelUpWeight, item.LevelUpLuckFactor);
            }).ToArray());
        }

        private static Dictionary<string, CardSealSpec> ResolveSpecifications(DungeonRunInteractionCatalog catalog)
        {
            var result = new Dictionary<string, CardSealSpec>(StringComparer.OrdinalIgnoreCase);
            var pools = (catalog.SealRewardPools ?? Array.Empty<DungeonRunSealRewardPool>()).ToList();
            if (catalog.SealRewardPool != null) pools.Add(catalog.SealRewardPool);
            foreach (var pool in pools)
            {
                Require(pool != null && pool.Candidates != null, "Resolved project Gem pool is missing.");
                foreach (var spec in pool.Candidates)
                {
                    Require(spec != null, "Null project Gem specification."); RequireId(spec.contentId);
                    Require(Enum.IsDefined(typeof(CardSealEffect), spec.effect) && spec.value > 0, "Invalid project Gem specification.");
                    if (result.TryGetValue(spec.contentId, out var prior))
                        Require(prior.effect == spec.effect && prior.value == spec.value && prior.comboScalable == spec.comboScalable,
                            "Conflicting resolved specifications for one Gem identity.");
                    else result.Add(spec.contentId, spec.Clone());
                }
            }
            return result;
        }

        private static IReadOnlyList<SideEventGemEvolutionEvidence> EvolutionBases(IReadOnlyList<CardConfig> cards,
            Dictionary<string, CardSealSpec> specs, IReadOnlyList<GemSourceEntry> entries, HashSet<string> unlocked,
            Func<string, bool> canEvolve)
        {
            var evolution = entries.Single(item => item.IsEvolution);
            if (!unlocked.Contains(evolution.ProjectSealContentId)) return Array.Empty<SideEventGemEvolutionEvidence>();
            return cards.Where(card => CardSealRules.CanAssign(card, specs[evolution.ProjectSealContentId],
                    isEvolutionResultEligible: canEvolve))
                .GroupBy(card => card.contentId, StringComparer.Ordinal).Select(group =>
                    new SideEventGemEvolutionEvidence(group.Key, group.First().evolutionTarget.contentId,
                        new SideEventOptionalBool(true), new SideEventOptionalBool(true), Array.Empty<int?>(),
                        new SideEventOptionalBool(true))).ToArray();
        }

        private static IReadOnlyList<string> InstalledSourceIds(CardConfig card, GemSourceRegistryDefinition registry)
        {
            var ids = new List<string>();
            foreach (var seal in card.seals ?? new List<CardSealSpec>())
            {
                Require(seal != null, "Null installed Gem specification.");
                if (registry.TryGetBySealId(seal.contentId, out var entry)) ids.Add(entry.SourceAssetId);
                // Built-in Luck/Greed are project seals outside this native Gem registry.
                else { RequireId(seal.contentId); ids.Add(seal.contentId); }
            }
            return ids.AsReadOnly();
        }

        private InputSnapshot Snapshot(GemSourceRegistryDocument document, IReadOnlyList<GemSourceEntry> ordered,
            IReadOnlyList<CardConfig> cards, Dictionary<string, CardSealSpec> specs, HashSet<string> unlocked,
            DungeonRunInteractionCatalog catalog, LevelInteractionEffect effect, string nodeId, Func<string, bool> canEvolve)
        {
            var input = new InputSnapshot {
                runId = _run.RunId, runSeed = _run.RunSeed, floorId = _run.CurrentFloor.Profile.contentId,
                nodeId = nodeId, effectId = effect.effectId, sourceCanonicalJson = document.CanonicalJson,
                sourceFingerprint = document.Fingerprint, luckPercent = catalog.LuckPercent,
                accountUnlockedSealIds = unlocked.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                orderedGemIds = ordered.Select(entry => entry.ProjectSealContentId).ToList(),
                orderedFrequencyIds = OrderedFrequencyIds.ToList(), configuredFrequencyIds = OrderedFrequencyIds.Take(3).ToList(),
                resolvedSpecifications = specs.Values.OrderBy(spec => spec.contentId, StringComparer.Ordinal).Select(spec => spec.Clone()).ToList()
            };
            input.randomSeed = Seed(input.runId, input.runSeed.ToString(CultureInfo.InvariantCulture), input.floorId, input.effectId, ContractId);
            foreach (var card in cards) input.hosts.Add(new HostSnapshot {
                runtimeInstanceId = card.runtimeInstanceId, cardContentId = card.contentId,
                slotCapacity = CardSealRules.GetSlotCapacity(card), limitBreakCount = card.LimitBreakCount,
                installedSeals = (card.seals ?? new List<CardSealSpec>()).Select(seal => seal.Clone()).ToList(),
                evolutionTargetContentId = card.evolutionTarget?.contentId ?? string.Empty,
                evolutionTargetPublished = card.evolutionTarget != null && canEvolve(card.evolutionTarget.contentId)
            });
            return input;
        }

        private static int Seed(params string[] parts)
            => SeedWithDomain("gem-ordinary/project-lcg-v1", parts);
        private static int SeedWithDomain(string domain, params string[] parts)
        {
            var text = new StringBuilder(domain);
            foreach (string part in parts) text.Append('|').Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
            using (var sha = SHA256.Create())
            { byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())); return unchecked(hash[0] | hash[1] << 8 | hash[2] << 16 | hash[3] << 24); }
        }
        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        private static void RequireId(string id)
        {
            Require(!string.IsNullOrEmpty(id) && !id.Any(char.IsWhiteSpace) && !id.Any(char.IsControl), "Invalid project qualification identity.");
            new UTF8Encoding(false, true).GetByteCount(id);
        }

        // Closed frozen project projection. Float evidence is binary32 bits; the complete source document
        // remains its own canonical wire rather than another copied Gem/Odin schema.
        [Serializable] private sealed class InputSnapshot
        {
            public string policy = "gem-project-eligibility/v1";
            public string accountPolicy = "project-unlocked-seals/no-native-achievement-equivalence";
            public string hostPolicy = "project-cardsealrules-v2/native-tags-not-adopted/ordinary-zero-benefit";
            public string evolutionPolicy = "project-single-primary/published-target/no-component-count/no-sacrifice/retain-instance";
            public string jewellerPolicy = "not-adopted/base-frequency-only";
            public string sealedBanishPolicy = "not-adopted/no-project-sealed-or-banished-gem-state";
            public string colorPolicy = "yellow-native-global-fcc-unmapped/color-only-candidate-rejected";
            public string randomPolicy = "gem-ordinary/project-lcg-v1/no-native-seed-equivalence";
            public string sourceCanonicalJson, sourceFingerprint, runId, floorId, nodeId, effectId;
            public int runSeed, randomSeed, luckPercent, luckMultiplierBits, countThresholdBits, countDrawBits, resolvedCount;
            public int baseCount = 3, maximumChoices = 5, luckChanceModifierBits = Bits(.1f), cashOutAmount = 200;
            public bool canCashOut = true, sourceCanCashOut = true, allowFallbacks = true, ensureNonEvo = true, guaranteeEvo = false, countRollPassed;
            public string sourceCallPolicy = "ordinary-or-Silver/three-true-source-flags";
            public string noTargetPolicy = "project-return-or-200-Greed-cash-out";
            public List<string> accountUnlockedSealIds, orderedGemIds, orderedFrequencyIds, configuredFrequencyIds;
            public List<CardSealSpec> resolvedSpecifications;
            public List<HostSnapshot> hosts = new List<HostSnapshot>();
            public List<RejectedSnapshot> rejectedCandidates;
        }
        [Serializable] private sealed class HostSnapshot
        {
            public int runtimeInstanceId, slotCapacity, limitBreakCount;
            public string cardContentId, evolutionTargetContentId;
            public bool evolutionTargetPublished;
            public List<CardSealSpec> installedSeals;
        }
        [Serializable] private sealed class RejectedSnapshot { public string gemContentId, reason; }
        [Serializable] private sealed class PaymentInputSnapshot
        {
            public string policy = "silver-exact-source-card-types/author-identity-directory-v2";
            public string scope = "explicitly-authorized-whole-Run/next-combat-regroups-project-adaptation";
            public string unknownPolicy = "unmapped-identity-or-class-excluded-with-diagnostic/not-presumed-non-FCC";
            public string flagPolicy = "Offering-flag-not-required-by-Silver-source";
            public string colorPolicy = "project-color-and-Wild-cost-not-native-CardTypes/Yellow-appends-Special-in-order";
            public string populationLimit = "only-author-configured-verified-native-identities/unknown-excluded";
            public string sourceAlias = "vc-local-full-copy";
            public string sourceCapture = ".analysis/AllCardWeaponAssets.json";
            public string randomPolicy = "two-Range-0-3-with-replacement/project-independent-LCG/no-native-seed-equivalence";
            public string resolverContractId = SilverContractId;
            public string runId, floorId, nodeId, effectId, sourceCanonicalJson, sourceFingerprint;
            public int runSeed, seed;
            public List<string> requirements;
            public List<PaymentCardSnapshot> cards = new List<PaymentCardSnapshot>();
        }
        [Serializable] private sealed class PaymentCardSnapshot
        {
            public int runtimeInstanceId, slotCapacity, limitBreakCount, rawOfferingFlag, sourceObjectPathId, sourceCardTypePathId, sourceCardTypeFileIndex;
            public string cardContentId, projectColor, projectCostType, sourceConfigId, sourceClass, diagnostic = string.Empty;
            public string sourceAlias, sourceFile, sourceCaptureSha256, sourceCardTypeFile;
            public bool isFccKnown, isFcc;
            public List<CardSealSpec> seals;
            public List<string> nativeCardTypes = new List<string>(), paymentTypes = new List<string>();
        }
    }
}
