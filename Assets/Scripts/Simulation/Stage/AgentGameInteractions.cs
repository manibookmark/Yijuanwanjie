using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Owns the non-UI offer/commit adaptation and temporary content for
    /// one floor. Dungeon alone owns choice progress, cards and effect receipts.
    /// Inspection never opens a transaction; only Begin/explicit commands commit.
    /// No scoring, default candidate or random reward reroll lives here. Scale
    /// review: dedicated source lifecycles are delegated, not added as local
    /// mutable drafts; this facade retains the shared floor/catalog boundary.</summary>
    internal sealed class AgentGameInteractions : IDisposable
    {
        private readonly DungeonRunSession _run;
        private readonly DungeonRunInteractionCatalog _catalog;
        private readonly RunAcceptanceEconomy _economy;
        private readonly IRandomSource _random;
        private readonly bool _ownsPool;
        private readonly Func<string, LingyinConfig> _resolveSealConfig;
        private readonly IChickenOfferingContext _offeringContext;
        private readonly Action _persistOffering;
        private readonly AgentGameSourceInteractions _sources;
        private readonly string _rewardVolumeId;
        internal PlayerStats BasePlayer { get; }
        internal IReadOnlyList<CardConfig> CardPool => _catalog.Cards;
        internal Func<string, bool> IsEligible => _catalog.IsEligible;
        internal Func<string, bool> IsEvolutionResultEligible => _catalog.CanEvolveTo;

        internal static AgentGameInteractions Open(DungeonRunSession run, RunAcceptanceEconomy economy, IRandomSource random)
        {
            run.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(economy.Account));
            BattleScenarioProfile scenario = RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor);
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try
            {
                PlayerStats player = scenario.CreatePlayer();
                LevelUpRewardRuleResolution rewards = StageEncounterAssembler.ResolveRewardRule(
                    run.CurrentFloor.Profile, scenario, pool, economy.Account,
                    new CardUnlockRunContext(run.RunId,
                        () => run.RunProgression.CardUnlockFacts.Capture(), run.LeaderCrawlerContentId));
                var recipes = new List<DungeonRunEvolutionRecipe>();
                foreach (string volume in new[] { "common", scenario.volumeId }.Distinct())
                    foreach (EvolutionData recipe in ContentCatalog.Current.GetVolume(volume).evolutions)
                        recipes.Add(new DungeonRunEvolutionRecipe { ContentId = recipe.contentId,
                            PrimaryCardContentId = recipe.primaryCardContentId, SecondaryCardContentIds = recipe.secondaryCardContentIds,
                            ResultCardContentId = recipe.resultCardContentId });
                var catalog = new DungeonRunInteractionCatalog {
                    Cards = pool, Recipes = recipes, RewardCards = rewards.Cards, RewardConfig = rewards.Config,
                    GemChestOfferResolver = new DungeonRunGemChestOfferResolver(run, economy.Account, ContentCatalog.Current, allowSilverRunDeckPayment: true),
                    NaturalRewardPool = rewards.NaturalPool,
                    CanReceiveSeals = SealRewardEligibility.CanReceiveSeals(economy.Account),
                    IsEligible = CardTestStatus.CanUseInFormalPlay, IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                    SealRewardPools = CreateSealRewardPools(
                        economy.Account?.unlockedSeals ?? LingyinCatalog.CreateDefaultUnlockedSealContentIds()),
                    LevelRewardOfferResolver = LevelUpOfferScheduleAdapter.ResolveForFloor(run.CurrentFloor.Profile, pool, CardTestStatus.IsReady,
                        canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(economy.Account))
                };
                run.ConfigureVictorySealRewardPools(catalog.SealRewardPools);
                return new AgentGameInteractions(run, catalog, player, economy, random, true,
                    rewardVolumeId: scenario.volumeId);
            }
            catch { DestroyCards(pool); throw; }
        }

        internal AgentGameInteractions(DungeonRunSession run, DungeonRunInteractionCatalog catalog, PlayerStats basePlayer,
            RunAcceptanceEconomy economy, IRandomSource random, bool ownsPool = false,
            Func<string, LingyinConfig> resolveSealConfig = null,
            IChickenOfferingContext offeringContext = null, Action persistOffering = null,
            AgentGameSourceInputs sourceInputs = null, string rewardVolumeId = null)
        {
            _run = run; _catalog = catalog; BasePlayer = basePlayer;
            _rewardVolumeId = rewardVolumeId;
            _run.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(economy.Account));
            _run.TryProjectPlayerBaseline(BasePlayer);
            _resolveSealConfig = resolveSealConfig ?? FindSealConfig;
            _offeringContext = offeringContext ?? new DungeonRunChickenOfferingContext(run, economy.Account);
            _persistOffering = persistOffering ?? (() =>
            {
                // The session's private account is the memory publication boundary.
                // Accept actual Run facts during the same domain transaction,
                // before any later combat or terminal report is required.
                Require(DungeonRunSettlement.TryCommitFactsAndPersist(_run, economy.Account,
                    () => { }, out string error), error);
            });
            _economy = economy; _random = random; _ownsPool = ownsPool;
            _sources = new AgentGameSourceInteractions(run, CardPool, _persistOffering,
                () => _economy.ForGreed(_run.RunProgression.ProjectGreed(BasePlayer.lianYu)) as IDungeonRunFlatCashOutWallet
                    ?? throw new InvalidOperationException("Source cash-out requires the formal flat wallet."),
                sourceInputs ?? AgentGameSourceInputs.Production(run, CardPool, economy.Account));
            AfterCommit();
        }

        internal static IReadOnlyList<DungeonRunSealRewardPool>
            CreateSealRewardPools(IEnumerable<string> unlockedSealContentIds)
        {
            var unlocked = (unlockedSealContentIds ?? Array.Empty<string>())
                .ToArray();
            IReadOnlyList<CardSealSpec> candidates =
                LingyinCatalog.GetUnlockedCopperChestPool(unlocked);
            return new[]
            {
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedCopperChestPoolId,
                    Candidates = candidates
                },
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedSilverChestPoolId,
                    Candidates = candidates,
                    PreferredCandidateCount = 1,
                    PreferredCandidateContentIds =
                        LingyinCatalog.GetUnlockedSilverChestPreferredContentIds(
                            unlocked)
                },
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedHighTierChestPoolId,
                    Candidates = LingyinCatalog.GetUnlockedHighTierChestPool(
                        unlocked),
                    IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                    PreferredCandidateCount = 1,
                    PreferredCandidateContentIds =
                        LingyinCatalog.GetUnlockedHighTierChestPreferredContentIds(
                            unlocked)
                }
            };
        }

        internal void AfterCommit()
        { RefreshCatalog(); _catalog.Wallet = _economy.ForGreed(_run.RunProgression.ProjectGreed(BasePlayer.lianYu)); }

        // Updating this adapter's projection does not mutate gameplay or consume RNG.
        private void RefreshCatalog()
        {
            bool canReceive = _run.CanReceiveSealRewards;
            if (_catalog.CanReceiveSeals != canReceive && _rewardVolumeId != null)
            {
                LevelUpRewardRuleResolution rewards = NaturalLevelUpRewardCatalog.ResolveGlobal(
                    CardPool, _rewardVolumeId, _economy.Account, CardTestStatus.IsReady,
                    runContext: new CardUnlockRunContext(_run.RunId,
                        () => _run.RunProgression.CardUnlockFacts.Capture(), _run.LeaderCrawlerContentId));
                _catalog.RewardCards = rewards.Cards;
                _catalog.RewardConfig = rewards.Config;
                _catalog.NaturalRewardPool = rewards.NaturalPool;
            }
            _catalog.CanReceiveSeals = canReceive;
            _catalog.GrowthPercent = _run.RunProgression.ProjectGrowth(BasePlayer.mingWu);
            _catalog.LuckPercent = _run.RunProgression.ProjectLuck(BasePlayer.tianYun);
        }

        internal string Availability(LevelNode node, out string reason)
        {
            RefreshCatalog();
            reason = null;
            if (_run.GetNodeAdmission(node.contentId) == DungeonRunNodeAdmission.PendingChoiceOutstanding)
            { reason = "Resume the outstanding map reward before another node."; return "ConditionUnavailable"; }
            if (node.completionEffects == null) return null;
            if (node.completionEffects.Any(e => e == null || e.kind == LevelInteractionEffectKind.Unavailable))
            { reason = "Author-declared interaction is not implemented; no substitute effect is executed."; return "Unimplemented"; }
            if (node.nodeType != LevelNodeType.Interaction && node.nodeType != LevelNodeType.Exit) return null;
            foreach (LevelInteractionEffect effect in node.completionEffects)
                if (!Enum.IsDefined(typeof(LevelInteractionEffectKind), effect.kind))
                { reason = "No executable interaction route: " + effect.kind; return "Unimplemented"; }
            if (_run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Available &&
                _run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Completing) return null;
            foreach (LevelInteractionEffect effect in node.completionEffects)
                if (!CanRoute(node, effect))
                { reason = "No executable interaction route: " + effect.kind; return "Unimplemented"; }
            if (AgentGameCrystals.Handles(node))
                return AgentGameCrystals.Availability(_run, node, out reason);
            if (AgentGameCardOfferings.Handles(node))
                return AgentGameCardOfferings.Availability(_run, node, _offeringContext, out reason);
            if (_sources.Handles(node)) return _sources.Availability(node, out reason);
            if (node.completionEffects.Count == 1 && DungeonRunInteractionTransaction.Handles(node.completionEffects[0].kind))
            {
                RefreshCatalog();
                if (_run.DoubleKeyPayments.RequiresPayment(node.contentId))
                {
                    if (!_run.DoubleKeyPayments.TryInspect(node.contentId, CardPool, out _, out reason)) return "ConditionUnavailable";
                    return null; // The unpaid menu always permits leaving without a cost.
                }
                if (!_run.Interactions.TryInspect(node.contentId, _catalog, out DungeonRunInteractionOffer offer, out reason)) return "ConditionUnavailable";
                if (offer.Phase == DungeonRunInteractionPhase.Ready &&
                    node.completionEffects[0].kind == LevelInteractionEffectKind.GemChest &&
                    node.completionEffects[0].sealRewardPoolId == DungeonRunGemChests.SilverOfferContractId &&
                    _catalog.GemChestOfferResolver is DungeonRunGemChestOfferResolver productionGems &&
                    !productionGems.IsSilverRunDeckPaymentAuthorized)
                { reason = "Silver whole-Run payment scope is not explicitly authorized."; return "ConditionUnavailable"; }
                if (offer.Phase == DungeonRunInteractionPhase.Unavailable) { reason = offer.Message; return "ConditionUnavailable"; }
            }
            // A map seal always has its formal cash-out disposition, including
            // before Yuyin. No-host does not make the reward node unavailable.
            return null;
        }

        internal void Begin(LevelNode node)
        {
            if (node.nodeType == LevelNodeType.Exit)
                throw new InvalidOperationException("Exit must use the atomic floor transaction.");
            if (Availability(node, out string unavailable) != null) throw new InvalidOperationException(unavailable);
            if (AgentGameCardOfferings.Handles(node))
            { AgentGameCardOfferings.Begin(_run, node, _offeringContext, _persistOffering); AfterCommit(); return; }
            if (_sources.Handles(node)) { _sources.Begin(node); AfterCommit(); return; }
            if (AgentGameCrystals.Handles(node))
            {
                Require(_run.Crystals.TryBeginSource(node.contentId, _persistOffering, out string crystalError), crystalError);
                AfterCommit(); return;
            }
            Require(_run.CurrentFloor.TryBeginInteraction(node.contentId, out string error), error);
            AfterCommit();
            AdvanceAutomatic();
        }

        internal DungeonRunFloorExitResult CommitExit(string expectedFloorId, string nodeId,
            Func<string, LevelRunProfile> resolveFloor)
        {
            IDungeonRunInteractionWallet wallet = _economy.ForGreed(
                _run.RunProgression.ProjectGreed(BasePlayer.lianYu), JadeSource.Special);
            Require(_run.FloorExits.TryCommit(expectedFloorId, nodeId,
                resolveFloor ?? LevelRunProfileCatalog.Get, wallet,
                out DungeonRunFloorExitResult result, out string error), error);
            return result;
        }

        internal AgentGameOffer ObservePending()
        {
            if (_run.CurrentFloor.TryGetPendingVictorySealReward(out LevelVictorySealRewardOffer victory))
                return VictoryOffer(victory);
            if (_run.CardOfferings.HasPendingChoice)
                return AgentGameCardOfferings.Observe(_run, _offeringContext, CardPool);
            string nodeId = _run.CurrentFloor.ActiveInteractionNodeId;
            if (string.IsNullOrEmpty(nodeId)) return null;
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            if (_sources.Handles(node)) return _sources.Observe(node);
            LevelInteractionEffect effect = PendingEffect(node);
            if (effect == null) return null;
            RefreshCatalog();
            if (_run.DoubleKeyPayments.RequiresPayment(nodeId))
                return ObservePayment(nodeId);
            if (AgentGameCrystals.Handles(node) && !_run.Interactions.HasPendingChoice)
                return AgentGameCrystals.Observe(_run, nodeId);
            if (DungeonRunInteractionTransaction.Handles(effect.kind) ||
                (effect.kind == LevelInteractionEffectKind.CrystalThrowing && _run.Interactions.HasPendingChoice))
            {
                Require(_run.Interactions.TryInspect(nodeId, _catalog, out DungeonRunInteractionOffer source, out string error), error);
                var result = new AgentGameOffer { kind = "Map", nodeContentId = nodeId, phase = source.Phase.ToString(),
                    title = source.Title, message = source.Message, selectedSeal = AgentGameSeal.Copy(source.SelectedSeal) };
                result.options.AddRange(source.Options.Select(o => new AgentGameOption { choiceId = o.Id,
                    title = source.Phase == DungeonRunInteractionPhase.SealChoice && o.Seal != null ? SealName(o.Seal) : o.Title,
                    description = SealDescription(o.Seal, o.Description), contentId = o.CardContentId, runtimeInstanceId = o.CardRuntimeInstanceId,
                    secondaryRuntimeInstanceId = o.SecondaryRuntimeInstanceId, seal = AgentGameSeal.Copy(o.Seal),
                    card = AgentGameCard.Copy(CardPool.FirstOrDefault(c => c != null && c.contentId == o.CardContentId)) }));
                if (source.SelectedSeal != null)
                {
                    result.title = SealName(source.SelectedSeal);
                    result.message = SealDescription(source.SelectedSeal, source.Message);
                }
                if (source.Phase == DungeonRunInteractionPhase.SealTarget && !result.options.Any(o => o.choiceId == "back"))
                    result.options.Add(new AgentGameOption { choiceId = "back", kind = "Back", title = "返回选印" });
                if ((source.Phase != DungeonRunInteractionPhase.SealTarget ||
                    DungeonRunGemChests.SupportsDurableLeave(effect)) &&
                    !effect.HasDoubleKeyPayment && effect.kind != LevelInteractionEffectKind.CrystalThrowing)
                    result.options.Add(new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap", title = "返回地图（保留待选奖励）" });
                return result;
            }
            if (effect.kind == LevelInteractionEffectKind.ConvertCard)
            {
                RunCardConversionRuleProfile profile = RunCardConversionRuleCatalog.Get(effect.targetContentId);
                Require(RunCardConversionChoices.TryInspect(_run, nodeId, effect, profile, CardPool, out RunCardConversionOffer source, out string error), error);
                var result = new AgentGameOffer { kind = "Conversion", nodeContentId = nodeId, phase = "ChooseDisposition", title = effect.summaryText };
                result.options.AddRange(source.Candidates.Select(c => new AgentGameOption { choiceId = "convert/" + c.RuntimeInstanceId,
                    contentId = c.ContentId, runtimeInstanceId = c.RuntimeInstanceId, kind = c.Outcome.ToString(), amount = c.Amount, title = "归元" }));
                if (source.AllowsCashOut)
                    result.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut", amount = source.CashOutBaseJade, title = "化玉" });
                if (source.AllowsLeave)
                    result.options.Add(new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap", title = "离开（不消耗卡牌，不获得属性）" });
                return result;
            }
            if (effect.kind == LevelInteractionEffectKind.GrantSeal)
            {
                CardSealSpec seal = ResolveSeal(effect.targetContentId);
                var result = new AgentGameOffer { kind = "GrantSeal", nodeContentId = nodeId, phase = "SealTarget",
                    title = SealName(seal), message = SealDescription(seal), selectedSeal = AgentGameSeal.Copy(seal) };
                result.options.AddRange(SealHosts(seal).Select(c => new AgentGameOption { choiceId = "host/" + c.runtimeInstanceId,
                    runtimeInstanceId = c.runtimeInstanceId, contentId = c.contentId, card = c, title = c.name,
                    description = SealDescription(seal) }));
                result.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut",
                    amount = _catalog.Wallet.Preview(new EconomyConfig().cashOutJade).FinalJade, title = "化玉" });
                result.options.Add(new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap", title = "返回地图" });
                return result;
            }
            throw new InvalidOperationException("Automatic effect did not finish before observation.");
        }

        private AgentGameOffer ObservePayment(string nodeId)
        {
            Require(_run.DoubleKeyPayments.TryInspect(nodeId, CardPool, out var source, out string error), error);
            var result = new AgentGameOffer { kind = "DoubleKey", nodeContentId = nodeId, phase = "Payment",
                title = "双钥印匣", message = "选择两张本色实例一起移除（含附印）；未支付可离开，支付后须结清奖励。" };
            foreach (var first in source.FirstCandidates)
                foreach (var second in source.SecondCandidates)
                    result.options.Add(new AgentGameOption {
                        choiceId = "pay/" + first.RuntimeInstanceId + "/" + second.RuntimeInstanceId,
                        kind = "PayDoubleKey", runtimeInstanceId = first.RuntimeInstanceId,
                        secondaryRuntimeInstanceId = second.RuntimeInstanceId, contentId = first.ContentId,
                        title = "移除 " + first.RuntimeInstanceId + " + " + second.RuntimeInstanceId,
                        description = first.ContentId + " + " + second.ContentId });
            result.options.Add(new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap", title = "离开（尚未支付）" });
            return result;
        }

        internal List<AgentGameCommand> Commands()
        {
            AgentGameOffer offer = ObservePending();
            if (offer == null) return new List<AgentGameCommand>();
            if (offer.kind == "Crystal")
                return AgentGameCrystals.Commands(_run, offer.nodeContentId, _catalog, _persistOffering, AfterCommit);
            return offer.options.Select(option => new AgentGameCommand(new AgentGameAction {
                actionId = offer.kind + "/" + option.choiceId, kind = offer.kind, nodeContentId = offer.nodeContentId,
                choiceId = option.choiceId, contentId = option.contentId, runtimeInstanceId = option.runtimeInstanceId,
                secondaryRuntimeInstanceId = option.secondaryRuntimeInstanceId, title = option.title, description = option.description
            }, () => Commit(option))).ToList();
        }

        private void Commit(AgentGameOption option)
        {
            if (_run.CardOfferings.HasPendingChoice)
            { AgentGameCardOfferings.Commit(_run, _offeringContext, option, _persistOffering); AfterCommit(); return; }
            LevelNode sourceNode = _run.CurrentFloor.FindNode(_run.CurrentFloor.ActiveInteractionNodeId);
            if (_sources.Handles(sourceNode))
            { _sources.Commit(sourceNode, option); _economy.Synchronize(); AfterCommit(); return; }
            // Wallet creation/synchronization is confined to an actual action,
            // never to observation. Dungeon performs exactly-once validation.
            _catalog.Wallet = _economy.ForGreed(_run.RunProgression.ProjectGreed(BasePlayer.lianYu));
            if (_run.CurrentFloor.TryGetPendingVictorySealReward(out LevelVictorySealRewardOffer victory))
            { CommitVictory(victory, option); _economy.Synchronize(); return; }
            string nodeId = _run.CurrentFloor.ActiveInteractionNodeId;
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            LevelInteractionEffect effect = PendingEffect(node);
            if (option.choiceId == "return-map")
            {
                if (effect?.HasDoubleKeyPayment == true)
                    Require(_run.DoubleKeyPayments.RequiresPayment(nodeId), "Paid double-key rewards must be settled before leaving.");
                if (effect?.kind == LevelInteractionEffectKind.ConvertCard)
                {
                    RunCardConversionRuleProfile profile = RunCardConversionRuleCatalog.Get(effect.targetContentId);
                    Require(profile.AllowsLeave, "This conversion requires a committed disposition before leaving.");
                }
                if (DungeonRunGemChests.SupportsDurableLeave(effect))
                    Require(_run.GemChests.TryCommit(nodeId, "leave", _catalog, out _, out string leaveError, _persistOffering), leaveError);
                _run.CurrentFloor.RecoverInterruptedEncounter();
                return;
            }
            string error;
            if (option.kind == "PayDoubleKey")
                Require(_run.DoubleKeyPayments.TryPay(_run.CurrentFloorContentId, nodeId,
                    option.runtimeInstanceId, option.secondaryRuntimeInstanceId, CardPool, out _, out error), error);
            else if (DungeonRunInteractionTransaction.Handles(effect.kind) ||
                (effect.kind == LevelInteractionEffectKind.CrystalThrowing && _run.Interactions.HasPendingChoice))
                Require(_run.Interactions.TryCommit(nodeId, option.choiceId, _catalog, out _, out error, _persistOffering), error);
            else if (effect.kind == LevelInteractionEffectKind.ConvertCard)
            {
                RunCardConversionRuleProfile profile = RunCardConversionRuleCatalog.Get(effect.targetContentId);
                bool success = option.choiceId == "cash-out"
                    ? RunCardConversionChoices.TryCashOut(_run, nodeId, effect, profile, CardPool, _catalog.Wallet, out _, out error)
                    : RunCardConversionChoices.TryConvert(_run, nodeId, effect, option.runtimeInstanceId, profile, CardPool, out _, out error);
                Require(success, error);
            }
            else if (effect.kind == LevelInteractionEffectKind.GrantSeal && option.choiceId == "cash-out")
                Require(_run.TryCashOutSealReward(nodeId, effect, new EconomyConfig().cashOutJade,
                    _catalog.Wallet, out _, out error, _persistOffering), error);
            else Require(_run.TryAssignSeal(nodeId, effect, option.runtimeInstanceId, ResolveSeal(effect.targetContentId),
                CardPool, out _, out error, isEvolutionResultEligible: IsEvolutionResultEligible), error);
            _economy.Synchronize();
            AdvanceAutomatic();
        }

        private void AdvanceAutomatic()
        {
            string nodeId = _run.CurrentFloor.ActiveInteractionNodeId;
            if (string.IsNullOrEmpty(nodeId)) return;
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            while (true)
            {
                if (_run.CurrentFloor.GetNodeState(nodeId) == LevelNodeViewState.Completed) return;
                LevelInteractionEffect effect = PendingEffect(node);
                if (effect == null) { Require(_run.CurrentFloor.TryCompleteInteraction(nodeId, out string completedError), completedError); return; }
                if (_run.DoubleKeyPayments.RequiresPayment(nodeId)) return;
                if (AgentGameCardOfferings.Handles(node) || AgentGameCrystals.Handles(node) || _sources.Handles(node)) return;
                if (DungeonRunInteractionTransaction.Handles(effect.kind))
                {
                    RefreshCatalog();
                    Require(_run.Interactions.TryInspect(nodeId, _catalog, out DungeonRunInteractionOffer offer, out string error), error);
                    if (offer.Phase == DungeonRunInteractionPhase.Unavailable) throw new InvalidOperationException(offer.Message);
                    if (offer.Phase != DungeonRunInteractionPhase.Ready) return;
                    Require(_run.Interactions.TryCommit(nodeId, string.Empty, _catalog, out _, out error, _persistOffering), error);
                }
                else if (effect.kind == LevelInteractionEffectKind.ConvertCard || effect.kind == LevelInteractionEffectKind.GrantSeal) return;
                else (ResolveAutomatic(effect.kind) ?? throw new InvalidOperationException("No executable interaction route: " + effect.kind))(node, effect);
                _economy.Synchronize();
            }
        }

        // The handler itself is the capability: availability cannot advertise
        // a kind that later falls through a separate execution whitelist.
        private bool CanRoute(LevelNode node, LevelInteractionEffect effect)
        {
            if (_sources.Handles(node) || AgentGameCardOfferings.Handles(node) || AgentGameCrystals.Handles(node) ||
                DungeonRunInteractionTransaction.Handles(effect.kind) || effect.kind == LevelInteractionEffectKind.ConvertCard ||
                effect.kind == LevelInteractionEffectKind.GrantSeal || ResolveAutomatic(effect.kind) != null) return true;
            if (node.completionEffects.Count != 1) return false;
            var commands = new List<AgentGameCommand>();
            // These owners run directly on the map, outside modal interactions.
            return AgentGameAbandonedCarts.TryAppend(_run, node, commands, _persistOffering) ||
                AgentGameManaStatues.TryAppend(_run, node, commands, _persistOffering);
        }

        private Action<LevelNode, LevelInteractionEffect> ResolveAutomatic(LevelInteractionEffectKind kind)
        {
            switch (kind)
            {
                case LevelInteractionEffectKind.AddBaseMana:
                case LevelInteractionEffectKind.AddBaseArmor:
                case LevelInteractionEffectKind.AddHandLimit:
                case LevelInteractionEffectKind.AddMight:
                case LevelInteractionEffectKind.AddArea:
                case LevelInteractionEffectKind.AddRewardReroll:
                case LevelInteractionEffectKind.RecordStoryClue:
                    return (node, effect) => Require(_run.CurrentFloor.TryApplyRunEffect(node.contentId, effect, out _, out var error), error);
                case LevelInteractionEffectKind.GrantJade:
                    return (node, effect) => {
                    int rolled = effect.amountMax > effect.amount ? _random.Range(effect.amount, checked(effect.amountMax + 1)) : effect.amount;
                    int before = _run.RunProgression.RunJadeEarned;
                    var economy = new EconomySystem(new EconomyConfig(), _run.RunProgression);
                    economy.Grant(rolled, _run.RunProgression.ProjectGreed(BasePlayer.lianYu), JadeSource.Special, true);
                    int after = _run.RunProgression.RunJadeEarned;
                    Require(_run.CurrentFloor.TryRecordExternalEffect(node.contentId, effect, before, after, after - before, out _, out var error), error); };
                case LevelInteractionEffectKind.HealCurrentHealth:
                    return (node, effect) => {
                    Require(_run.TryHealCurrentHealth(effect.amount, out int hpBefore, out int hpAfter, out var error), error);
                    Require(_run.CurrentFloor.TryRecordExternalEffect(node.contentId, effect, hpBefore, hpAfter, hpAfter - hpBefore, out _, out error), error); };
                case LevelInteractionEffectKind.GrantRelic:
                    return (node, effect) => {
                    if (!ContentCatalog.Current.TryGetRelic(effect.targetContentId, out RelicData relic) || !Enum.IsDefined(typeof(RelicType), relic.relicType))
                        throw new InvalidOperationException("Missing or invalid granted relic: " + effect.targetContentId);
                    int prior = _economy.Account.IsRelicUnlocked(relic.contentId) ? 1 : 0;
                    bool changed = _economy.Account.TryUnlockRelic(relic.contentId, (RelicType)relic.relicType, relic.name, relic.description);
                    Require(_run.CurrentFloor.TryRecordExternalEffect(node.contentId, effect, prior, 1, changed ? effect.amount : 0, out _, out var error), error); };
                default: return null;
            }
        }

        private AgentGameOffer VictoryOffer(LevelVictorySealRewardOffer offer)
        {
            CardSealSpec selected = string.IsNullOrEmpty(offer.SealContentId) ? null : ResolveSeal(offer.SealContentId);
            var result = new AgentGameOffer { kind = "VictorySeal", nodeContentId = offer.NodeContentId, phase = offer.Phase.ToString(),
                title = selected == null ? "胜利灵印" : SealName(selected), message = SealDescription(selected),
                selectedSeal = AgentGameSeal.Copy(selected) };
            if (offer.Phase == LevelVictorySealRewardPhase.ChoosingSeal)
            {
                foreach (string id in offer.SealContentIds)
                {
                    CardSealSpec seal = ResolveSeal(id);
                    if (offer.AllowsCashOut || SealHosts(seal).Count > 0)
                        result.options.Add(new AgentGameOption { choiceId = "seal/" + id, contentId = id,
                            seal = AgentGameSeal.Copy(seal), title = SealName(seal), description = SealDescription(seal) });
                }
                if (offer.AllowsCashOut)
                    result.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut",
                        title = "化玉", amount = new EconomyConfig().cashOutJade });
            }
            else if (offer.IsCashOutOnly)
                result.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut",
                    title = "领取灵玉", amount = new EconomyConfig().cashOutJade });
            else if (offer.Phase == LevelVictorySealRewardPhase.ChoosingDisposition)
            {
                if (SealHosts(selected).Count > 0)
                    result.options.Add(new AgentGameOption { choiceId = "assign", kind = "Assign", title = "选择宿主",
                        description = SealDescription(selected) });
                if (offer.AllowsCashOut) result.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut", title = "化玉", amount = new EconomyConfig().cashOutJade });
                if (offer.SealContentIds.Count > 1) result.options.Add(new AgentGameOption { choiceId = "back-choice", kind = "Back", title = "返回选印" });
            }
            else if (offer.Phase == LevelVictorySealRewardPhase.AssigningSeal)
            {
                result.options.AddRange(SealHosts(selected).Select(c => new AgentGameOption {
                    choiceId = "host/" + c.runtimeInstanceId, runtimeInstanceId = c.runtimeInstanceId, contentId = c.contentId,
                    card = c, title = c.name, description = SealDescription(selected) }));
                result.options.Add(new AgentGameOption { choiceId = "back", kind = "Back", title = "返回处理方式" });
            }
            return result;
        }

        private void CommitVictory(LevelVictorySealRewardOffer offer, AgentGameOption option)
        {
            string error;
            if (option.choiceId == "back") Require(_run.CurrentFloor.TryReturnToVictorySealDisposition(offer.RewardContentId, out error), error);
            else if (option.choiceId == "back-choice") Require(_run.CurrentFloor.TryReturnToVictorySealChoice(offer.RewardContentId, out error), error);
            else if (option.choiceId == "assign") Require(_run.CurrentFloor.TryBeginVictorySealAssignment(offer.RewardContentId, out error), error);
            else if (option.choiceId == "cash-out") Require(_run.TryCashOutVictorySealReward(offer.RewardContentId,
                () => _catalog.Wallet.Credit(new EconomyConfig().cashOutJade), out _, out error), error);
            else if (offer.Phase == LevelVictorySealRewardPhase.ChoosingSeal)
                Require(_run.CurrentFloor.TryChooseVictorySeal(offer.RewardContentId, option.contentId, out error), error);
            else Require(_run.TryAssignVictorySealReward(offer.RewardContentId, option.runtimeInstanceId, ResolveSeal(offer.SealContentId),
                CardPool, out _, out error, isEvolutionResultEligible: IsEvolutionResultEligible), error);
        }

        internal List<CardConfig> ReadDeck()
        { Require(_run.TryGetCurrentCards(CardPool, out List<CardConfig> cards, out string error), error); return cards; }
        private List<AgentGameCard> SealHosts(CardSealSpec seal)
        {
            if (!_run.CanReceiveSealRewards) return new List<AgentGameCard>();
            List<CardConfig> cards = ReadDeck();
            try { return cards.Where(c => CardSealRules.CanAssign(c, seal, isEvolutionResultEligible: IsEvolutionResultEligible)).Select(c => AgentGameCard.Copy(c)).ToList(); }
            finally { DestroyCards(cards); }
        }
        private LevelInteractionEffect PendingEffect(LevelNode node)
            => node?.completionEffects?.FirstOrDefault(e => e != null && !_run.CurrentFloor.TryGetInteractionReceipt(e.effectId, out _));
        private static LingyinConfig FindSealConfig(string id)
            => LingyinCatalog.TryGet(id, out LingyinConfig seal) ? seal : null;
        private CardSealSpec ResolveSeal(string id)
            => _resolveSealConfig(id)?.ToCardSealSpec() ?? throw new InvalidOperationException("Missing seal: " + id);
        private string SealName(CardSealSpec seal)
            => _resolveSealConfig(seal.contentId)?.displayName ?? seal.contentId;
        private string SealDescription(CardSealSpec seal, string transactionDescription = null)
        {
            if (seal == null) return transactionDescription;
            LingyinConfig config = _resolveSealConfig(seal.contentId);
            // The transaction may deliberately offer a different value. Keep its
            // exact payment/multiplier description instead of substituting the
            // authored default. Presentation never changes the offered spec.
            if (config == null || config.effectType != seal.effect || config.effectValue != seal.value ||
                string.IsNullOrWhiteSpace(config.description)) return transactionDescription;
            if (string.IsNullOrWhiteSpace(transactionDescription)) return config.description;
            return config.description == transactionDescription ? transactionDescription :
                config.description + "\n" + transactionDescription;
        }
        internal static void Require(bool success, string error) { if (!success) throw new InvalidOperationException(error ?? "Formal transaction rejected."); }
        internal static void DestroyCards(IEnumerable<CardConfig> cards)
        { if (cards != null) foreach (CardConfig card in cards) if (card != null) UnityEngine.Object.DestroyImmediate(card); }
        public void Dispose() { if (_ownsPool) DestroyCards(CardPool); }
    }
}
