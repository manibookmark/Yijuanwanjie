using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Drives Dungeon's published map choices without owning their rules or
    /// checkpoints. Temporary content, reward-policy context and exact-instance
    /// targeting live here; only Run.Interactions may grant or transform cards.
    /// An unavailable refinement remains an unconsumed optional map node.
    /// </summary>
    internal sealed class StageSimulationInteractionRunner
    {
        private readonly DungeonRunSession _run;
        private readonly StageSimulationStep _step;
        private readonly DungeonRunInteractionCatalog _catalog;
        private readonly PlayerStats _basePlayer;
        private readonly ISimulationRewardPolicy _rewardPolicy;
        private readonly HashSet<string> _attemptedSeals = new HashSet<string>(StringComparer.Ordinal);

        private StageSimulationInteractionRunner(DungeonRunSession run,
            StageSimulationStep step, DungeonRunInteractionCatalog catalog,
            PlayerStats basePlayer, ISimulationRewardPolicy rewardPolicy)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _basePlayer = basePlayer;
            _rewardPolicy = rewardPolicy ?? throw new ArgumentNullException(nameof(rewardPolicy));
        }

        internal static bool Execute(
            DungeonRunSession run,
            StageSimulationStep step,
            ISimulationRewardPolicy rewardPolicy,
            MetaProgression rewardAccount = null)
        {
            run.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(rewardAccount));
            BattleScenarioProfile scenario = ResolveScenario(run.CurrentFloor);
            List<CardConfig> cards = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try
            {
                PlayerStats player = scenario.CreatePlayer();
                LevelUpRewardRuleResolution rewards =
                    StageEncounterAssembler.ResolveRewardRule(
                        run.CurrentFloor.Profile, scenario, cards, rewardAccount,
                        new CardUnlockRunContext(run.RunId,
                            () => run.RunProgression.CardUnlockFacts.Capture(), run.LeaderCrawlerContentId));
                var recipes = new List<DungeonRunEvolutionRecipe>();
                if (!string.Equals(scenario.volumeId, "common", StringComparison.OrdinalIgnoreCase))
                    AddRecipes("common", recipes);
                AddRecipes(scenario.volumeId, recipes);
                var catalog = new DungeonRunInteractionCatalog
                {
                    Cards = cards,
                    GemChestOfferResolver = new DungeonRunGemChestOfferResolver(run, rewardAccount, ContentCatalog.Current, allowSilverRunDeckPayment: true),
                    Recipes = recipes,
                    RewardCards = rewards.Cards,
                    RewardConfig = rewards.Config,
                    NaturalRewardPool = rewards.NaturalPool,
                    CanReceiveSeals = SealRewardEligibility.CanReceiveSeals(rewardAccount),
                    SealRewardPools = AgentGameInteractions.CreateSealRewardPools(
                        rewardAccount?.unlockedSeals ?? LingyinCatalog.CreateDefaultUnlockedSealContentIds()),
                    LevelRewardOfferResolver = LevelUpOfferScheduleAdapter.ResolveForFloor(
                        run.CurrentFloor.Profile, cards, CardTestStatus.IsReady,
                        canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(rewardAccount)),
                    IsEligible = CardTestStatus.CanUseInFormalPlay, IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                    GrowthPercent = run.RunProgression.ProjectGrowth(player.mingWu),
                    LuckPercent = run.RunProgression.ProjectLuck(player.tianYun),
                    Wallet = new RunWallet(run.RunProgression,
                        run.RunProgression.ProjectGreed(player.lianYu))
                };
                run.ConfigureVictorySealRewardPools(catalog.SealRewardPools);
                return Execute(run, step, catalog, player, rewardPolicy);
            }
            finally { DestroyCards(cards); }
        }

        /// <summary>In-memory content boundary for non-UI contract tests.</summary>
        internal static bool Execute(
            DungeonRunSession run,
            StageSimulationStep step,
            DungeonRunInteractionCatalog catalog,
            PlayerStats basePlayer,
            ISimulationRewardPolicy rewardPolicy)
            => new StageSimulationInteractionRunner(run, step, catalog, basePlayer, rewardPolicy).Run();

        private bool Run()
        {
            if (_run.DoubleKeyPayments.RequiresPayment(_step.NodeContentId))
            {
                if (!_run.DoubleKeyPayments.TryInspect(_step.NodeContentId, _catalog.Cards,
                        out var payment, out string paymentError)) throw Failure(_step, paymentError);
                if (!payment.CanPay) return false; // Optional box remains untouched.
                if (!_run.CurrentFloor.TryBeginInteraction(_step.NodeContentId, out paymentError))
                    throw Failure(_step, paymentError);
                // A declared simulation policy, not an automatic live-player sacrifice:
                // deterministic lowest instance of each authored native color.
                int first = payment.FirstCandidates.Min(c => c.RuntimeInstanceId);
                int second = payment.SecondCandidates.Min(c => c.RuntimeInstanceId);
                if (!_run.DoubleKeyPayments.TryPay(_run.CurrentFloorContentId, _step.NodeContentId,
                        first, second, _catalog.Cards, out _, out paymentError)) throw Failure(_step, paymentError);
            }
            if (!_run.Interactions.TryInspect(_step.NodeContentId, _catalog,
                    out DungeonRunInteractionOffer offer, out string error))
                throw Failure(_step, error);
            if (offer.Phase == DungeonRunInteractionPhase.Unavailable)
            {
                LevelNode node = _run.CurrentFloor.FindNode(_step.NodeContentId);
                if (node.completionEffects[0].kind == LevelInteractionEffectKind.RefineCard)
                    return false;
                throw Failure(_step, offer.Message);
            }
            if (offer.Phase == DungeonRunInteractionPhase.Completed &&
                _run.CurrentFloor.GetNodeState(_step.NodeContentId) == LevelNodeViewState.Completed)
                return true;
            if (_run.CurrentFloor.ActiveInteractionNodeId != _step.NodeContentId &&
                !_run.CurrentFloor.TryBeginInteraction(_step.NodeContentId, out error))
                throw Failure(_step, error);

            var visitedOffers = new HashSet<string>(StringComparer.Ordinal);
            while (offer.Phase != DungeonRunInteractionPhase.Completed)
            {
                string signature = offer.Phase + "/" + offer.SelectedSeal?.contentId +
                    "/" + _attemptedSeals.Count + "/" +
                    string.Join("|", offer.Options.Select(option => option.Id));
                if (!visitedOffers.Add(signature))
                    throw Failure(_step, "The interaction did not advance after a committed choice.");
                string choice = Choose(offer);
                if (!_run.Interactions.TryCommit(_step.NodeContentId, choice, _catalog,
                        out offer, out error, _run.CurrentFloor.FindNode(_step.NodeContentId).completionEffects[0].kind == LevelInteractionEffectKind.GemChest
                            ? () => { _run.SerializeForDeterministicComparison(); } : (Action)null))
                    throw Failure(_step, error);
            }
            if (_run.CurrentFloor.GetNodeState(_step.NodeContentId) != LevelNodeViewState.Completed &&
                !_run.CurrentFloor.TryCompleteInteraction(_step.NodeContentId, out error))
                throw Failure(_step, error);
            return true;
        }

        private string Choose(DungeonRunInteractionOffer offer)
        {
            switch (offer.Phase)
            {
                case DungeonRunInteractionPhase.SilverPayment:
                    return offer.Options.FirstOrDefault(option => option.CardRuntimeInstanceId > 0)?.Id
                        ?? throw Failure(_step, "No proven physical card can satisfy the remaining Silver locks.");
                case DungeonRunInteractionPhase.Ready:
                    return string.Empty;
                case DungeonRunInteractionPhase.RewardChoice:
                    return ChooseReward(offer);
                case DungeonRunInteractionPhase.EvolutionChoice:
                    return ChooseHost(offer).Id;
                case DungeonRunInteractionPhase.SealChoice:
                    foreach (DungeonRunInteractionOption option in offer.Options)
                        // Source Gem occurrences can share the same seal but name different outcomes.
                        if (option.Seal != null && _attemptedSeals.Add(option.Id))
                            return option.Id;
                    DungeonRunInteractionOption cashOut = offer.Options.FirstOrDefault(
                        option => option.Id == "cash-out");
                    if (cashOut != null && string.IsNullOrWhiteSpace(_step.TargetCardContentId))
                        return cashOut.Id;
                    throw Failure(_step, "No offered seal has a legal requested host or cash-out disposition.");
                case DungeonRunInteractionPhase.SealTarget:
                    DungeonRunInteractionOption host = ChooseHost(offer, required: false);
                    return host?.Id ?? "back";
                default:
                    throw Failure(_step, offer.Message ?? "Unsupported interaction phase " + offer.Phase + ".");
            }
        }

        private DungeonRunInteractionOption ChooseHost(
            DungeonRunInteractionOffer offer,
            bool required = true)
        {
            int requestedId = 0;
            if (!string.IsNullOrWhiteSpace(_step.TargetCardContentId))
            {
                if (!_run.TryGetCurrentCards(_catalog.Cards, out List<CardConfig> cards, out string error))
                    throw Failure(_step, error);
                try
                {
                    CardConfig requested = cards
                        .Where(card => string.Equals(card.contentId, _step.TargetCardContentId, StringComparison.Ordinal))
                        .OrderBy(card => card.runtimeInstanceId)
                        .Skip(_step.TargetCardOccurrence).FirstOrDefault();
                    if (requested == null)
                        throw Failure(_step, "The requested stable card occurrence is absent from the Run.");
                    requestedId = requested.runtimeInstanceId;
                }
                finally { DestroyCards(cards); }
            }
            DungeonRunInteractionOption selected = offer.Options
                // Utility dispositions (cash-out/back) are not physical hosts.
                // Their zero ID must not sort ahead of every eligible card.
                .Where(option => option.CardRuntimeInstanceId > 0)
                .Where(option => requestedId == 0 || option.CardRuntimeInstanceId == requestedId)
                .OrderBy(option => option.CardRuntimeInstanceId)
                .ThenBy(option => option.SecondaryRuntimeInstanceId)
                .ThenBy(option => option.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (required && selected == null)
                throw Failure(_step, "The requested card has no legal offered transformation.");
            return selected;
        }

        private string ChooseReward(DungeonRunInteractionOffer offer)
        {
            if (!_run.TryGetCurrentCards(_catalog.Cards, out List<CardConfig> cards, out string error))
                throw new InvalidOperationException("Cannot inspect map reward deck: " + error);
            CombatController context = null;
            try
            {
                PlayerStats player = JsonUtility.FromJson<PlayerStats>(
                    JsonUtility.ToJson(_basePlayer ?? new PlayerStats()));
                player.maxHealth = _run.MaxHealth;
                player.currentHealth = _run.CurrentHealth;
                player.faLi = SumStat(player.faLi, _run.CurrentFloor.Modifiers.BaseManaBonus);
                player.fengRui = SumStat(player.fengRui, _run.CurrentFloor.Modifiers.MightBonus);
                player.bengLie = SumStat(player.bengLie, _run.CurrentFloor.Modifiers.AreaBonus);
                player.shouPai = SumStat(player.shouPai, _run.CurrentFloor.Modifiers.HandLimitBonus);
                var detachedProgression = new RunProgressionState(_run.RunProgression.CardPlayGrowth.Enabled);
                detachedProgression.Restore(_run.RunProgression.Capture());
                detachedProgression.CardPlayGrowth.Restore(_run.RunProgression.CardPlayGrowth.Capture());
                detachedProgression.CardUnlockFacts.Restore(_run.RunProgression.CardUnlockFacts.Capture());
                // The policy sees a detached deck/player context. Constructing
                // it does not prepare a combat, advance turns or touch Run state.
                context = new CombatController(player, cards, new List<EnemyStats>(),
                    runState: detachedProgression,
                    progression: new CombatProgressionState(_run.CombatProgression.Rules,
                        _run.CombatProgression.CurrentLevel, _run.CombatProgression.CurrentExperience,
                        _run.CombatProgression.XpFractionCarryUnits),
                    deckInitialization: CardDeckInitialization.RestoreInstances);
                var rewards = new List<LevelUpSystem.Reward>();
                foreach (DungeonRunInteractionOption option in offer.Options)
                {
                    if (option.Seal != null)
                    {
                        rewards.Add(new LevelUpSystem.Reward
                        {
                            type = LevelUpSystem.RewardType.Gem, seal = option.Seal.Clone(),
                            name = option.Title, description = option.Description
                        });
                        continue;
                    }
                    CardConfig card = _catalog.Cards.First(candidate => candidate != null &&
                        string.Equals(candidate.contentId, option.CardContentId, StringComparison.OrdinalIgnoreCase));
                    rewards.Add(new LevelUpSystem.Reward
                    {
                        type = LevelUpSystem.RewardType.NewCard,
                        card = card,
                        name = card.cardName,
                        description = card.description
                    });
                }
                int chosen = _rewardPolicy.ChooseReward(context, rewards);
                if (chosen < 0 || chosen >= offer.Options.Count)
                    throw new InvalidOperationException("Map reward policy returned an invalid offered index.");
                return offer.Options[chosen].Id;
            }
            finally
            {
                if (context != null)
                {
                    DestroyCards(context.Deck);
                    DestroyCards(context.Hand);
                    DestroyCards(context.Discard);
                }
                DestroyCards(cards);
            }
        }

        private static BattleScenarioProfile ResolveScenario(LevelRunSession floor)
        {
            foreach (LevelNode node in floor.Nodes)
            {
                if (node == null || (node.nodeType != LevelNodeType.Encounter && node.nodeType != LevelNodeType.Finale))
                    continue;
                BattleScenarioProfile scenario = BattleScenarioCatalog.Get(node.scenarioId);
                if (scenario != null && !string.IsNullOrWhiteSpace(scenario.volumeId)) return scenario;
            }
            throw new InvalidOperationException("The floor has no production scenario for map interaction content.");
        }

        private static void AddRecipes(string volumeId, List<DungeonRunEvolutionRecipe> recipes)
        {
            foreach (EvolutionData recipe in ContentCatalog.Current.GetVolume(volumeId).evolutions)
                recipes.Add(new DungeonRunEvolutionRecipe
                {
                    ContentId = recipe.contentId,
                    PrimaryCardContentId = recipe.primaryCardContentId,
                    SecondaryCardContentIds = recipe.secondaryCardContentIds,
                    ResultCardContentId = recipe.resultCardContentId
                });
        }

        private static int SumStat(int first, int second)
            => (int)Math.Min(int.MaxValue, Math.Max(0L, (long)first + second));

        private static InvalidOperationException Failure(StageSimulationStep step, string error)
            => new InvalidOperationException($"Cannot execute map interaction {step.NodeContentId}: {error}");

        private static void DestroyCards(IReadOnlyList<CardConfig> cards)
        {
            if (cards == null) return;
            foreach (CardConfig card in cards)
                if (card != null) UnityEngine.Object.DestroyImmediate(card);
        }

        // Exit shares the existing headless economy boundary, but is not a
        // card/reward cash-out. Never reconstruct the rounding formula here.
        internal static IDungeonRunInteractionWallet CreateExitWallet(DungeonRunSession run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var player = new PlayerStats();
            run.TryProjectPlayerBaseline(player);
            return new RunWallet(run.RunProgression,
                run.RunProgression.ProjectGreed(player.lianYu), JadeSource.Special);
        }

        internal static IDungeonRunInteractionWallet CreateSealCashOutWallet(DungeonRunSession run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var player = new PlayerStats();
            run.TryProjectPlayerBaseline(player);
            return new RunWallet(run.RunProgression,
                run.RunProgression.ProjectGreed(player.lianYu));
        }

        private sealed class RunWallet : IDungeonRunFlatCashOutWallet
        {
            private readonly EconomySystem _economy;
            private readonly int _greed;
            private readonly JadeSource _source;
            private int _balanceBefore;
            private bool _canRollback;
            public int Balance { get; private set; }

            internal RunWallet(RunProgressionState run, int greed, JadeSource source = JadeSource.CashOut)
            {
                Balance = run.RunJadeEarned;
                _greed = greed;
                _source = source;
                _economy = new EconomySystem(new EconomyConfig(), run,
                    amount => Balance = checked(Balance + amount));
            }

            public JadeGainResult Preview(int amount) => new JadeGainResult(
                _source, amount, EconomySystem.CalculateFinalJade(amount, _greed, true), true);
            public JadeGainResult Credit(int amount)
                => CreditWallet(amount, _source, affectedByGreed: true);
            public JadeGainResult PreviewFlatCashOut(int baseAmount)
            {
                if (baseAmount < 0) throw new ArgumentOutOfRangeException(nameof(baseAmount));
                return new JadeGainResult(JadeSource.CashOut, baseAmount, baseAmount, false);
            }
            public JadeGainResult CreditFlatCashOut(int baseAmount)
                => CreditWallet(baseAmount, JadeSource.CashOut, affectedByGreed: false);
            private JadeGainResult CreditWallet(int amount, JadeSource source, bool affectedByGreed)
            {
                _canRollback = false;
                if (!affectedByGreed && amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                int final = EconomySystem.CalculateFinalJade(amount, _greed, affectedByGreed);
                if (final < 0) throw new InvalidOperationException("Reward exceeds the supported jade range.");
                checked { _ = Balance + final; _ = _economy.RunState.RunJadeEarned + final; }
                _balanceBefore = Balance;
                _canRollback = true;
                return _economy.Grant(amount, affectedByGreed ? _greed : 0,
                    source, affectedByGreed, recordInRun: true);
            }
            public void RestoreBalance(int balance)
            {
                if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));
                if (!_canRollback || balance != _balanceBefore)
                {
                    if (balance == Balance) { _canRollback = false; return; }
                    throw new InvalidOperationException("Wallet rollback does not match its most recent transaction.");
                }
                Balance = _balanceBefore;
                _canRollback = false;
            }
        }
    }
}
