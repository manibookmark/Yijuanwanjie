using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the isolation boundary between mutable author nodes and a floor's
    /// effective contracts. Author mode reads the current author without exporting
    /// its objects; frozen mode retains a full detached floor and permits only
    /// Interaction payload replacements and immutable side-slot activation.
    /// Queries always return detached copies. Disabled slots retain their topology;
    /// generation/placement reasons and attempts belong to the source Run owner.
    /// This module owns copying, payload validation, topology identity and the
    /// canonical fingerprint, not random selection, progress or persistence.
    /// Reassess this single contract boundary when adding another payload type;
    /// serialized field mappings are deliberately kept together rather than split
    /// into pass-through helpers or a runtime ScriptableObject lifecycle.
    /// </summary>
    internal sealed class LevelRunNodeDirectory
    {
        private readonly LevelRunProfile _author;
        private readonly string _authorId;
        private readonly List<LevelNode> _topology;
        private readonly List<LevelNode> _frozen;
        private readonly HashSet<string> _disabled;
        private readonly IReadOnlyList<string> _disabledIds;

        public bool HasFrozenInteractions => _frozen != null;
        internal string AuthorContentId => _authorId;
        public string Fingerprint { get; }
        public IReadOnlyList<string> DisabledInteractionNodeIds => _disabledIds;
        public IReadOnlyList<LevelNode> Nodes
            => new ReadOnlyCollection<LevelNode>(CopyNodes(HasFrozenInteractions ? _frozen : _author.nodes));

        private LevelRunNodeDirectory(LevelRunProfile author, List<LevelNode> topology,
            List<LevelNode> frozen, List<string> disabledIds)
        {
            _author = author ?? throw new ArgumentNullException(nameof(author));
            _authorId = author.contentId;
            _topology = topology;
            _frozen = frozen;
            _disabled = new HashSet<string>(disabledIds, StringComparer.Ordinal);
            _disabledIds = new ReadOnlyCollection<string>(disabledIds);
            Fingerprint = frozen == null ? string.Empty : FingerprintOfFloor(frozen, disabledIds);
        }

        public static LevelRunNodeDirectory ForAuthor(LevelRunProfile profile)
            => new LevelRunNodeDirectory(profile, null, null, new List<string>());

        public static LevelRunNodeDirectory FreezeInteractions(LevelRunProfile profile,
            IReadOnlyList<LevelNode> replacements)
            => FreezeInteractions(profile, replacements, Array.Empty<string>());

        /// <summary>Freezes payloads and enablement together; unused slots cannot be completed as empty events.</summary>
        public static LevelRunNodeDirectory FreezeInteractions(LevelRunProfile profile,
            IReadOnlyList<LevelNode> replacements, IReadOnlyList<string> disabledInteractionNodeIds)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (replacements == null) throw new ArgumentNullException(nameof(replacements));
            RequireIdentifier(profile.contentId, "Author floor");
            List<LevelNode> topology = CopyNodes(profile.nodes);
            List<LevelNode> effective = CopyNodes(topology);
            var indices = IndexNodes(topology);
            List<string> disabledIds = ValidateDisabledNodes(topology, indices, disabledInteractionNodeIds);
            var disabled = new HashSet<string>(disabledIds, StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelNode replacement in replacements)
            {
                if (replacement == null) throw Invalid("A frozen replacement cannot be null.");
                RequireIdentifier(replacement.contentId, "Replacement node");
                if (!changed.Add(replacement.contentId) || !indices.TryGetValue(replacement.contentId, out int index))
                    throw Invalid("Replacement nodes must be unique existing author nodes.");
                if (disabled.Contains(replacement.contentId))
                    throw Invalid("A disabled side slot cannot also receive a generated replacement.");
                if (topology[index].nodeType != LevelNodeType.Interaction ||
                    replacement.nodeType != LevelNodeType.Interaction ||
                    !SameTopology(topology[index], replacement))
                    throw Invalid("Only an existing Interaction's effects and completion presentation may be replaced.");
                effective[index] = CopyNode(replacement);
            }
            ValidateFrozen(effective, disabled);
            return new LevelRunNodeDirectory(profile, topology, effective, disabledIds);
        }

        /// <summary>
        /// Reconstructs a saved complete floor without reapplying replacements to today's
        /// payloads. Topology must still match the author; every saved effect, activation
        /// and presentation is validated and fingerprinted before a directory is returned.
        /// This does not restore Run source qualification, attempts or progress.
        /// </summary>
        public static LevelRunNodeDirectory RestoreFrozen(LevelRunProfile profile,
            IReadOnlyList<LevelNode> completeFrozenNodes, IReadOnlyList<string> disabledInteractionNodeIds,
            string expectedFingerprint)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            RequireIdentifier(profile.contentId, "Author floor");
            List<LevelNode> topology = CopyNodes(profile.nodes);
            List<LevelNode> frozen = CopyNodes(completeFrozenNodes);
            var indices = IndexNodes(topology);
            IndexNodes(frozen);
            if (frozen.Count != topology.Count) throw Invalid("A saved frozen floor must contain every author node.");
            for (int i = 0; i < frozen.Count; i++)
                if (!SameTopology(topology[i], frozen[i]))
                    throw Invalid("A saved frozen floor changed node topology or author order.");
            List<string> disabledIds = ValidateDisabledNodes(frozen, indices, disabledInteractionNodeIds);
            ValidateFrozen(frozen, new HashSet<string>(disabledIds, StringComparer.Ordinal));
            var restored = new LevelRunNodeDirectory(profile, topology, frozen, disabledIds);
            if (!string.Equals(restored.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
                throw Invalid("The saved complete floor or its activation contract differs from its fingerprint.");
            return restored;
        }

        public bool IsNodeEnabled(string nodeContentId)
            => FindSourceNode(nodeContentId) != null && !_disabled.Contains(nodeContentId);

        /// <summary>
        /// Continues a same-floor source phase from the previous COMPLETE effective
        /// floor. Today's author supplies topology only: earlier interactions,
        /// rewards and Exit payloads retain their saved values. Run progress and
        /// the authority to activate a previously disabled slot belong to the Run
        /// source owner and are checked before it publishes this directory.
        /// </summary>
        internal static LevelRunNodeDirectory RefreezeInteractions(LevelRunProfile profile,
            LevelRunNodeDirectory previous, IReadOnlyList<LevelNode> replacements,
            IReadOnlyList<string> disabledInteractionNodeIds)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            if (replacements == null) throw new ArgumentNullException(nameof(replacements));
            previous.RequireMatches(profile);
            RequireIdentifier(profile.contentId, "Author floor");
            List<LevelNode> topology = CopyNodes(profile.nodes);
            List<LevelNode> effective = CopyNodes(previous.Nodes);
            var indices = IndexNodes(topology);
            List<string> disabledIds = ValidateDisabledNodes(topology, indices, disabledInteractionNodeIds);
            var disabled = new HashSet<string>(disabledIds, StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelNode replacement in replacements)
            {
                if (replacement == null) throw Invalid("A source phase replacement cannot be null.");
                RequireIdentifier(replacement.contentId, "Replacement node");
                if (!changed.Add(replacement.contentId) || !indices.TryGetValue(replacement.contentId, out int index) ||
                    disabled.Contains(replacement.contentId) || topology[index].nodeType != LevelNodeType.Interaction ||
                    replacement.nodeType != LevelNodeType.Interaction || !SameTopology(topology[index], replacement))
                    throw Invalid("Source phase replacements must be unique enabled author Interaction slots with unchanged topology.");
                effective[index] = CopyNode(replacement);
            }
            ValidateFrozen(effective, disabled);
            return new LevelRunNodeDirectory(profile, topology, effective, disabledIds);
        }

        public LevelNode FindNode(string nodeContentId)
        {
            LevelNode source = FindSourceNode(nodeContentId);
            return source == null ? null : CopyNode(source);
        }

        private LevelNode FindSourceNode(string nodeContentId)
        {
            IReadOnlyList<LevelNode> nodes = HasFrozenInteractions ? _frozen : _author.nodes;
            if (nodes == null) throw Invalid("Author nodes cannot be null.");
            foreach (LevelNode node in nodes)
            {
                if (node == null) throw Invalid("A node collection cannot contain null members.");
                if (string.Equals(node.contentId, nodeContentId, StringComparison.Ordinal)) return node;
            }
            return null;
        }

        internal bool MatchesEffect(string nodeId, LevelInteractionEffect supplied)
        {
            if (!IsNodeEnabled(nodeId) || supplied == null || string.IsNullOrWhiteSpace(supplied.effectId)) return false;
            LevelNode node = FindNode(nodeId);
            LevelInteractionEffect configured = node?.completionEffects?.Find(effect =>
                effect != null && string.Equals(effect.effectId, supplied.effectId, StringComparison.Ordinal));
            if (configured == null) return false;
            try { return SameCanonicalValue(configured, supplied); }
            catch (EncoderFallbackException) { return false; }
        }

        private static Dictionary<string, int> IndexNodes(IReadOnlyList<LevelNode> nodes)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                RequireIdentifier(nodes[i].contentId, "Node");
                if (indices.ContainsKey(nodes[i].contentId)) throw Invalid("Nodes contain duplicate identities.");
                indices.Add(nodes[i].contentId, i);
            }
            return indices;
        }

        private static List<string> ValidateDisabledNodes(IReadOnlyList<LevelNode> nodes,
            IReadOnlyDictionary<string, int> indices, IReadOnlyList<string> disabledIds)
        {
            if (disabledIds == null) throw Invalid("A frozen floor requires an explicit activation contract.");
            var disabled = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in disabledIds)
            {
                RequireIdentifier(id, "Disabled side slot");
                if (!disabled.Add(id) || !indices.TryGetValue(id, out int index) ||
                    nodes[index].nodeType != LevelNodeType.Interaction)
                    throw Invalid("Disabled side slots must be unique existing Interaction nodes.");
            }
            var ordered = new List<string>(disabled.Count);
            var disabledPrerequisites = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase);
            foreach (LevelNode node in nodes)
            {
                if (disabled.Contains(node.contentId)) { ordered.Add(node.contentId); continue; }
                foreach (string prerequisite in node.prerequisiteNodeIds ?? new List<string>())
                    if (disabledPrerequisites.Contains(prerequisite))
                        throw Invalid("An enabled node cannot require a disabled side slot; its route must be resolved by the author.");
            }
            return ordered;
        }

        public void RequireMatches(LevelRunProfile profile)
        {
            if (profile == null || !string.Equals(profile.contentId, _authorId, StringComparison.Ordinal))
                throw Invalid("The node directory belongs to another author floor.");
            if (!HasFrozenInteractions) return;
            if (profile.nodes == null || profile.nodes.Count != _topology.Count)
                throw Invalid("Frozen floor topology changed its node collection.");
            for (int i = 0; i < _topology.Count; i++)
                if (profile.nodes[i] == null || !SameTopology(_topology[i], profile.nodes[i]))
                    throw Invalid("Frozen floor topology changed a node or its author order.");
        }

        private static List<LevelNode> CopyNodes(IReadOnlyList<LevelNode> nodes)
        {
            if (nodes == null) throw Invalid("A node collection cannot be null.");
            var copy = new List<LevelNode>(nodes.Count);
            foreach (LevelNode node in nodes)
            {
                if (node == null) throw Invalid("A node collection cannot contain null members.");
                copy.Add(CopyNode(node));
            }
            return copy;
        }

        private static LevelNode CopyNode(LevelNode source)
            => new LevelNode
            {
                contentId = source.contentId,
                nodeType = source.nodeType,
                encounterRole = source.encounterRole,
                scenarioId = source.scenarioId,
                commonEncounter = source.commonEncounter == null ? null : new LevelCommonEncounterBinding
                {
                    datasetContentId = source.commonEncounter.datasetContentId,
                    difficultyContentId = source.commonEncounter.difficultyContentId,
                    hasTileDifficulty = source.commonEncounter.hasTileDifficulty,
                    tileDifficulty = source.commonEncounter.tileDifficulty
                },
                prerequisiteNodeIds = CopyStrings(source.prerequisiteNodeIds),
                victoryRewards = CopyList(source.victoryRewards, CopyReward),
                completionEffects = CopyList(source.completionEffects, CopyEffect),
                completionPresentationId = source.completionPresentationId,
                completionPresentationVariantId = source.completionPresentationVariantId
            };

        private static LevelRewardGrant CopyReward(LevelRewardGrant source)
            => new LevelRewardGrant
            {
                contentId = source.contentId, kind = source.kind, targetContentId = source.targetContentId,
                sealChoiceContentIds = CopyStrings(source.sealChoiceContentIds),
                sealRewardPoolId = source.sealRewardPoolId, sealChoiceCount = source.sealChoiceCount,
                sealChoiceCountMax = source.sealChoiceCountMax, allowCashOut = source.allowCashOut,
                presentationId = source.presentationId
            };

        private static LevelInteractionEffect CopyEffect(LevelInteractionEffect source)
            => new LevelInteractionEffect
            {
                effectId = source.effectId, kind = source.kind, amount = source.amount,
                amountMax = source.amountMax, targetContentId = source.targetContentId,
                cardChoices = CopyStrings(source.cardChoices), presentationId = source.presentationId,
                rarity = source.rarity, summaryText = source.summaryText, detailText = source.detailText,
                sealChoices = CopyList(source.sealChoices, seal => seal.Clone()),
                sealRewardPoolId = source.sealRewardPoolId, cashOutJade = source.cashOutJade,
                fixedEvolution = source.fixedEvolution == null ? null : new LevelInteractionFixedEvolutionContract
                {
                    recipeContentId = source.fixedEvolution.recipeContentId,
                    primaryCardContentId = source.fixedEvolution.primaryCardContentId,
                    secondaryCardContentId = source.fixedEvolution.secondaryCardContentId,
                    resultCardContentId = source.fixedEvolution.resultCardContentId
                },
                targetLevelResult = source.targetLevelResult == null ? null : new LevelInteractionTargetLevelContract
                {
                    referenceLoadoutContentId = source.targetLevelResult.referenceLoadoutContentId,
                    targetLevel = source.targetLevelResult.targetLevel
                },
                doubleKeyPayment = source.doubleKeyPayment == null ? null : new LevelInteractionDoubleKeyContract
                {
                    version = source.doubleKeyPayment.version, firstColor = source.doubleKeyPayment.firstColor,
                    secondColor = source.doubleKeyPayment.secondColor
                },
                arcanaChoice = source.arcanaChoice == null ? null : new LevelInteractionArcanaChoiceContract
                {
                    poolContentId = source.arcanaChoice.poolContentId,
                    candidateCount = source.arcanaChoice.candidateCount
                },
                duplicateOffering = source.duplicateOffering == null ? null : new LevelInteractionDuplicateOfferingContract
                {
                    sourceEventId = source.duplicateOffering.sourceEventId, rootContentId = source.duplicateOffering.rootContentId,
                    sourceFingerprint = source.duplicateOffering.sourceFingerprint, poolPolicyId = source.duplicateOffering.poolPolicyId
                },
                crawlerInvitation = source.crawlerInvitation == null ? null : new LevelInteractionInvitationContract { policyId = source.crawlerInvitation.policyId },
                gunForHire = source.gunForHire == null ? null : new LevelInteractionGunForHireContract
                {
                    sourceEventId = source.gunForHire.sourceEventId, rootContentId = source.gunForHire.rootContentId,
                    sourceFingerprint = source.gunForHire.sourceFingerprint, poolPolicyId = source.gunForHire.poolPolicyId
                },
                cardStatOffering = source.cardStatOffering == null ? null : new LevelInteractionCardStatOfferingContract
                {
                    sourceEventId = source.cardStatOffering.sourceEventId, rootContentId = source.cardStatOffering.rootContentId,
                    sourceFingerprint = source.cardStatOffering.sourceFingerprint, poolPolicyId = source.cardStatOffering.poolPolicyId
                },
                chickenOffering = source.chickenOffering == null ? null : new LevelInteractionChickenOfferingContract
                    { sourceEventId = source.chickenOffering.sourceEventId },
                manaOffering = source.manaOffering == null ? null : new LevelInteractionManaOfferingContract
                    { sourceEventId = source.manaOffering.sourceEventId },
                manaStatue = source.manaStatue == null ? null : new LevelInteractionManaStatueContract
                {
                    version = source.manaStatue.version, sourceEventId = source.manaStatue.sourceEventId,
                    requiredAcceptedBumps = source.manaStatue.requiredAcceptedBumps,
                    manaAmount = source.manaStatue.manaAmount
                },
                crystalThrowing = source.crystalThrowing == null ? null : new LevelInteractionCrystalContract { version = source.crystalThrowing.version, sourceEventId = source.crystalThrowing.sourceEventId },
                abandonedCart = source.abandonedCart == null ? null : new LevelInteractionAbandonedCartContract
                {
                    version = source.abandonedCart.version, sourceEventId = source.abandonedCart.sourceEventId,
                    requiredAcceptedBumps = source.abandonedCart.requiredAcceptedBumps,
                    drawCountBonus = source.abandonedCart.drawCountBonus,
                    pickupContentId = source.abandonedCart.pickupContentId
                }
            };

        private static List<string> CopyStrings(IReadOnlyList<string> source)
        {
            if (source == null) return null;
            var copy = new List<string>(source.Count);
            foreach (string item in source)
            {
                if (item == null) throw Invalid("A configured string collection cannot contain null members.");
                copy.Add(item);
            }
            return copy;
        }

        private static List<T> CopyList<T>(IReadOnlyList<T> source, Func<T, T> copyItem) where T : class
        {
            if (source == null) return null;
            var copy = new List<T>(source.Count);
            foreach (T item in source)
            {
                if (item == null) throw Invalid("A configured payload collection cannot contain null members.");
                copy.Add(copyItem(item));
            }
            return copy;
        }

        private static bool SameTopology(LevelNode first, LevelNode second)
            => first.contentId == second.contentId && first.nodeType == second.nodeType &&
                first.encounterRole == second.encounterRole && first.scenarioId == second.scenarioId &&
                SameCanonicalValue(first.commonEncounter, second.commonEncounter) &&
                SameCanonicalValue(first.prerequisiteNodeIds, second.prerequisiteNodeIds) &&
                SameCanonicalValue(first.victoryRewards, second.victoryRewards);

        private static void ValidateFrozen(IReadOnlyList<LevelNode> nodes, ISet<string> disabled)
        {
            var effects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (LevelNode node in nodes)
            {
                if (!Enum.IsDefined(typeof(LevelNodeType), node.nodeType) ||
                    !Enum.IsDefined(typeof(LevelEncounterRole), node.encounterRole))
                    errors.Add("Frozen node has an unknown type or encounter role.");
                if (node.encounterRole != LevelEncounterRole.Unspecified &&
                    node.nodeType != LevelNodeType.Encounter && node.nodeType != LevelNodeType.Finale)
                    errors.Add("A noncombat node cannot carry a combat role.");
                node.commonEncounter?.Validate(node, errors);
                if ((node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale) &&
                    string.IsNullOrWhiteSpace(node.scenarioId)) errors.Add("A combat node requires its Scenario.");
                if (string.IsNullOrWhiteSpace(node.completionPresentationId) &&
                    !string.IsNullOrWhiteSpace(node.completionPresentationVariantId))
                    errors.Add("A completion presentation variant requires its presentation ID.");
                bool interaction = node.nodeType == LevelNodeType.Interaction || node.nodeType == LevelNodeType.Exit;
                int count = node.completionEffects?.Count ?? 0;
                if (!interaction && count > 0) errors.Add("Only Interaction or Exit nodes may contain completion effects.");
                if (node.nodeType == LevelNodeType.Interaction && count == 0 && !disabled.Contains(node.contentId))
                    errors.Add("A frozen Interaction requires its complete effect contract.");
                foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                {
                    RequireIdentifier(effect.effectId, "Effect");
                    if (!effects.Add(effect.effectId)) errors.Add("Effect IDs must be unique across the entire frozen floor.");
                    ValidateEffect(effect, count, errors);
                }
            }
            if (errors.Count != 0) throw Invalid(string.Join(" ", errors));
        }

        private static void ValidateEffect(LevelInteractionEffect effect, int effectCount, List<string> errors)
        {
            if (!Enum.IsDefined(typeof(LevelInteractionEffectKind), effect.kind) ||
                !Enum.IsDefined(typeof(RewardRarity), effect.rarity))
                errors.Add("Frozen effect has an unknown kind or rarity.");
            if (effect.amount < 0 || effect.amountMax < 0 ||
                (effect.amountMax != 0 && effect.amountMax < effect.amount) || effect.cashOutJade < 0)
                errors.Add("Frozen effect has an invalid integer range.");
            if ((effect.HasFixedEvolutionContract && effect.kind != LevelInteractionEffectKind.RefineCard) ||
                (effect.HasTargetLevelResultContract && effect.kind != LevelInteractionEffectKind.ReachLevel) ||
                (effect.HasDoubleKeyPayment && effect.kind != LevelInteractionEffectKind.ChooseSeal) ||
                (effect.HasArcanaChoiceContract && effect.kind != LevelInteractionEffectKind.ChooseArcana) ||
                (effect.HasDuplicateOfferingContract && effect.kind != LevelInteractionEffectKind.DuplicateOffering) ||
                (effect.HasCrawlerInvitationContract && effect.kind != LevelInteractionEffectKind.CrawlerInvitation) ||
                (effect.HasGunForHireContract && effect.kind != LevelInteractionEffectKind.GunForHire) ||
                (effect.HasCardStatOfferingContract && effect.kind != LevelInteractionEffectKind.CardStatOffering) ||
                (effect.HasChickenOfferingContract && effect.kind != LevelInteractionEffectKind.ChickenOffering) ||
                (effect.HasManaOfferingContract && effect.kind != LevelInteractionEffectKind.ManaOffering) ||
                (effect.HasManaStatueContract && effect.kind != LevelInteractionEffectKind.ManaStatue) ||
                (effect.HasCrystalThrowingContract && effect.kind != LevelInteractionEffectKind.CrystalThrowing) ||
                (effect.HasAbandonedCartContract && effect.kind != LevelInteractionEffectKind.AbandonedCart))
                errors.Add("Frozen effect contains another kind's typed contract.");
            if ((effect.cardChoices?.Count > 0 && effect.kind != LevelInteractionEffectKind.ChooseCard) ||
                (effect.sealChoices?.Count > 0 && effect.kind != LevelInteractionEffectKind.ChooseSeal) ||
                (!string.IsNullOrEmpty(effect.sealRewardPoolId) && effect.kind != LevelInteractionEffectKind.ChooseSeal && effect.kind != LevelInteractionEffectKind.GemChest) ||
                (effect.cashOutJade != 0 && effect.kind != LevelInteractionEffectKind.ChooseSeal && effect.kind != LevelInteractionEffectKind.GemChest))
                errors.Add("Frozen effect contains another kind's selection payload.");

            if (effect.kind == LevelInteractionEffectKind.GemChest &&
                (effect.cardChoices?.Count > 0 || effect.HasDoubleKeyPayment ||
                 effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract))
                errors.Add("Gem Chest cannot carry an unrelated selection or payment contract.");
            bool exclusive = false;
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.DuplicateOffering:
                    DungeonRunDuplicateOfferings.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.CrawlerInvitation:
                    DungeonRunInvitations.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.GunForHire:
                    DungeonRunGunForHire.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.CardStatOffering:
                    DungeonRunCardStatOfferings.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.CrystalThrowing:
                    CrystalEventContracts.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.AbandonedCart:
                    DungeonRunAbandonedCarts.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.ManaStatue:
                    DungeonRunManaStatues.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.ChickenOffering:
                case LevelInteractionEffectKind.ManaOffering:
                    DungeonRunChickenOffering.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.ChooseArcana:
                    DungeonRunArcanas.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.GrantCard:
                case LevelInteractionEffectKind.GrantExperience:
                case LevelInteractionEffectKind.RefineCard:
                case LevelInteractionEffectKind.GemChest:
                case LevelInteractionEffectKind.ChooseSeal:
                case LevelInteractionEffectKind.ReachLevel:
                case LevelInteractionEffectKind.ChooseCard:
                    DungeonRunInteractionTransaction.ValidateEffect(effect, errors); exclusive = true; break;
                case LevelInteractionEffectKind.ConvertCard:
                    exclusive = true;
                    goto case LevelInteractionEffectKind.GrantRelic;
                case LevelInteractionEffectKind.GrantRelic:
                case LevelInteractionEffectKind.GrantSeal:
                case LevelInteractionEffectKind.RecordStoryClue:
                    if (string.IsNullOrWhiteSpace(effect.targetContentId)) errors.Add("Frozen targeted effect lacks its stable target.");
                    if (effect.kind == LevelInteractionEffectKind.RecordStoryClue && (effect.amount != 1 || effect.amountMax != 0))
                        errors.Add("A story clue requires a fixed amount of one.");
                    goto case LevelInteractionEffectKind.GrantJade;
                case LevelInteractionEffectKind.AddBaseMana:
                case LevelInteractionEffectKind.AddBaseArmor:
                case LevelInteractionEffectKind.AddHandLimit:
                case LevelInteractionEffectKind.AddMight:
                case LevelInteractionEffectKind.AddArea:
                case LevelInteractionEffectKind.AddRewardReroll:
                    if (effect.amountMax != 0) errors.Add("A Run modifier requires a fixed amount.");
                    goto case LevelInteractionEffectKind.GrantJade;
                case LevelInteractionEffectKind.HealCurrentHealth:
                    if (effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId))
                        errors.Add("A recovery effect requires a fixed targetless amount.");
                    goto case LevelInteractionEffectKind.GrantJade;
                case LevelInteractionEffectKind.GrantJade:
                    if (effect.amount <= 0) errors.Add("A fixed or bounded reward requires a positive amount.");
                    break;
                case LevelInteractionEffectKind.Unavailable:
                    break;
                default:
                    errors.Add("Frozen effect kind is unsupported."); break;
            }
            if (exclusive && effectCount != 1) errors.Add("A typed selection or conversion node requires one canonical effect.");
        }

        private static void RequireIdentifier(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw Invalid(name + " identity is missing or padded.");
            try { new UTF8Encoding(false, true).GetByteCount(value); }
            catch (EncoderFallbackException exception) { throw new InvalidOperationException(name + " identity has invalid Unicode.", exception); }
        }

        private static bool SameCanonicalValue(object first, object second)
            => FingerprintOf(first) == FingerprintOf(second);

        private static string FingerprintOfFloor(List<LevelNode> nodes, List<string> disabledIds)
        {
            // Preserve the preceding all-enabled contract revision. Activation is
            // a separate framed suffix only when present; author order canonicalizes
            // the set without changing source occurrence order in its own Run owner.
            if (disabledIds.Count == 0) return FingerprintOf(nodes);
            return FingerprintOf(nodes, disabledIds);
        }

        private static string FingerprintOf(object value, List<string> disabledIds = null)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), leaveOpen: true))
            {
                writer.Write("level-run-node-contract/v1");
                WriteCanonical(writer, value);
                if (disabledIds != null)
                {
                    writer.Write("disabled-interaction-slots/v1");
                    WriteCanonical(writer, disabledIds);
                }
            }
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", string.Empty).ToLowerInvariant();
        }

        // This private codec covers serialized fields only, never derived Has*
        // properties. Field names/types are framed; lists retain order, and null
        // differs from an empty list/string/object. Binary32 bits preserve -0.
        private static void WriteCanonical(BinaryWriter writer, object value)
        {
            writer.Write(value != null);
            if (value == null) return;
            Type type = value.GetType();
            writer.Write(CanonicalTypeName(type));
            if (value is string text) { writer.Write(text); return; }
            if (value is int integer) { writer.Write(integer); return; }
            if (value is bool boolean) { writer.Write(boolean); return; }
            if (value is float number) { writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(number), 0)); return; }
            if (type.IsEnum) { writer.Write(Convert.ToInt32(value)); return; }
            if (value is IList items)
            {
                writer.Write(items.Count);
                foreach (object item in items) WriteCanonical(writer, item);
                return;
            }
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            // The only nullable v1 extension: absence preserves all preceding
            // frozen-directory fingerprints. A specified Crystal contract is hashed
            // in full; its validator rejects missing/unknown payloads on Crystal kind.
            if (value is LevelInteractionEffect effect && !effect.HasCrystalThrowingContract)
                fields = Array.FindAll(fields, field => field.Name != "crystalThrowing");
            if (value is LevelInteractionEffect statEffect && !statEffect.HasCardStatOfferingContract)
                fields = Array.FindAll(fields, field => field.Name != "cardStatOffering");
            if (value is LevelInteractionEffect duplicateEffect && !duplicateEffect.HasDuplicateOfferingContract)
                fields = Array.FindAll(fields, field => field.Name != "duplicateOffering");
            if (value is LevelInteractionEffect invitationEffect && !invitationEffect.HasCrawlerInvitationContract)
                fields = Array.FindAll(fields, field => field.Name != "crawlerInvitation");
            if (value is LevelInteractionEffect hireEffect && !hireEffect.HasGunForHireContract)
                fields = Array.FindAll(fields, field => field.Name != "gunForHire");
            writer.Write(fields.Length);
            foreach (FieldInfo field in fields)
            {
                writer.Write(field.Name); writer.Write(CanonicalTypeName(field.FieldType));
                WriteCanonical(writer, field.GetValue(value));
            }
        }

        private static string CanonicalTypeName(Type type)
        {
            if (!type.IsGenericType) return type.FullName;
            // Type.FullName on a closed List includes assembly-qualified generic
            // arguments. CLR/library version labels are not node payload facts.
            Type[] arguments = type.GetGenericArguments();
            return type.GetGenericTypeDefinition().FullName + "<" +
                string.Join(",", Array.ConvertAll(arguments, CanonicalTypeName)) + ">";
        }

        private static InvalidOperationException Invalid(string message)
            => new InvalidOperationException(message);
    }
}
