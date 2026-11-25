using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.Persistence;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Owns one isolated formal Run and its pause-at-player-choice
    /// lifecycle. Commands never pick other commands. Core/Dungeon alone write
    /// gameplay state; this owner caches detached observations after commits,
    /// disposes encounter resources, and keeps coverage separate from victory.
    /// Size review: this file also contains resolved entry assembly. Session still
    /// solely owns its formal pause/command lifecycle and detached checkpoints;
    /// settlement wire knowledge is held by AgentGameSettlementObservation.
    /// Re-review before adding another lifecycle or a new independently mutable owner.</summary>
    internal sealed class AgentGameSession : IAgentGameSession
    {
        // Full-node G3 already reached 2021 commands before its final boss body.
        // Keep a finite full-run budget, with receipt/recovery counts derived here.
        internal const int MaximumActions = 4096;
        private readonly AgentGameEntry _entry;
        private readonly DungeonRunSession _run;
        private readonly IRandomSource _random;
        private readonly IInstanceIdGenerator _identities;
        private readonly RunAcceptanceEconomy _economy;
        private readonly AgentGameTrace _trace;
        private readonly Dictionary<string, List<AgentGameNode>> _pastCoverage = new Dictionary<string, List<AgentGameNode>>();
        private readonly Dictionary<string, (string status, string reason)> _lastAvailability = new Dictionary<string, (string status, string reason)>();
        private AgentGameInteractions _interactions;
        private StageEncounterAssembly _assembly;
        private HeadlessCombatSession _combatSession;
        private CombatRuleSet _combatRules;
        private LevelNode _activeNode;
        private AgentGameObservation _observation;
        private List<AgentGameCommand> _commands = new List<AgentGameCommand>();
        private string _outcome = "InProgress", _reason;
        private bool _disposed;
        private int _actionStartStep;
        public int Revision { get; private set; }
        public bool IsTerminal => _outcome != "InProgress";
        public string ContentFingerprint => _entry.ContentFingerprint;
        internal string FrozenEncounterFingerprint => _run.FrozenEncounterFingerprint;

        internal AgentGameSession(AgentGameEntry entry, int seed, string actorId = null, string agentVersion = null, string promptVersion = null, string runIdSuffix = null)
        {
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _random = new SeededRandomSource(seed); _identities = new SeededInstanceIdGenerator(1);
            // Session/request/actor IDs deliberately never enter gameplay RNG.
            string runId = "agent-game/" + entry.TestCaseId + "/" + seed.ToString(CultureInfo.InvariantCulture) + (runIdSuffix == null ? string.Empty : "/" + runIdSuffix);
            _trace = new AgentGameTrace(new AgentGameTraceDocument { testCaseId = entry.TestCaseId, seed = seed,
                actorId = actorId, agentVersion = agentVersion, promptVersion = promptVersion,
                contentFingerprint = entry.ContentFingerprint, startingAccount = entry.StartingAccount });
            try
            {
                _run = entry.PrepareRun != null
                    ? entry.PrepareRun(seed, runId, _identities)
                    : DungeonRunSession.Begin(entry.FirstFloor, entry.FirstFloor.ResolveDefaultSnapshotId(), runId, _identities,
                        runSeed: seed, cardPlayGrowthEnabled: RunStartService.CaptureCardPlayGrowthAdmission(entry.Account),
                        leaderCrawlerContentId: entry.LeaderCrawlerContentId,
                        commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
                if (_run == null) throw new InvalidOperationException("Entry preparation did not return a Run.");
                _economy = new RunAcceptanceEconomy(_run.RunProgression, entry.Account);
                if (entry.PrepareRun == null) entry.Initialize(_run);
                _interactions = entry.OpenInteractions(_run, _economy, _random);
                Refresh(); _trace.SetInitial(_observation);
            }
            catch { Dispose(); throw; }
        }

        public AgentGameObservation Observe() { ThrowIfDisposed(); return AgentGameTrace.Clone(_observation); }
        public List<AgentGameAction> LegalActions()
        { ThrowIfDisposed(); return _commands.Select(c => AgentGameTrace.Clone(c.View)).ToList(); }
        public AgentGameTraceDocument ExportTrace() { ThrowIfDisposed(); return _trace.Export(); }

        // Private recovery evidence, never exported through observation/legalActions.
        // Run and subsystem owners remain authoritative. This is a deterministic
        // verification projection, not a battle restore format or a new state owner.
        public string CaptureRecoveryDigest()
        {
            var evidence = new System.Text.StringBuilder();
            void Add(string value) => evidence.Append(value?.Length ?? 0).Append(':').Append(value).Append('\n');
            Add(_run.SerializeForDeterministicComparison());
            Add(FrozenEncounterFingerprint);
            Add(JsonUtility.ToJson(_entry.Account));
            Add(JsonUtility.ToJson(CardUnlockProgressSaveData.Capture(_entry.Account.CardUnlocks.Capture())));
            Add(JsonUtility.ToJson(EventUnlockProgressSaveData.Capture(_entry.Account.EventUnlocks.Capture())));
            Add(_entry.Account.ExperienceCollections.Serialize());
            foreach (var upgrade in _entry.Account.powerUpLevels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { Add(upgrade.Key); Add(upgrade.Value.ToString(CultureInfo.InvariantCulture)); }
            Add(((SeededRandomSource)_random).DeterministicState.ToString(CultureInfo.InvariantCulture));
            Add(((SeededInstanceIdGenerator)_identities).NextInstanceId.ToString(CultureInfo.InvariantCulture));
            Add(_economy.CreditedRunIncome.ToString(CultureInfo.InvariantCulture));
            CombatController combat = _assembly?.Combat;
            if (combat != null)
            {
                foreach (var pile in new[] { combat.Hand, combat.Deck, combat.Discard })
                {
                    Add(pile.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (CardConfig card in pile)
                    {
                        if (card == null) { Add(null); continue; }
                        Add(card.contentId);
                        Add(FormattableString.Invariant($"{card.runtimeInstanceId},{card.LimitBreakCount},{card.Lifetime},{card.manaCost},{card.baseDamage},{card.armor},{card.heal},{card.manaGain},{card.MustDestroyOnUse},{card.hasReverse},{card.isFreeToPlay}"));
                        Add(JsonUtility.ToJson(new RecoverySeals { seals = card.seals }));
                    }
                }
                foreach (EnemyStats enemy in combat.Enemies)
                {
                    if (enemy == null) { Add(null); continue; }
                    Add(enemy.contentId);
                    Add(FormattableString.Invariant($"{enemy.runtimeId},{enemy.baseHealth},{enemy.currentHealth},{enemy.armor},{enemy.row},{enemy.column},{enemy.gridWidth},{enemy.gridDepth},{enemy.IsDeployed},{enemy.IsWithdrawn},{enemy.isDisarmed},{enemy.isOverkillTarget},{enemy.justFilledFrontRow},{enemy.spawnWaveIndex},{enemy.behaviorActionIndex},{enemy.behaviorPhaseIndex},{enemy.behaviorLegalActionCount}"));
                    if (combat.State.EnemyAi.TryGetHealthBehaviorSnapshot(enemy.runtimeId,
                            out EnemyHealthBehaviorSnapshot healthSelection))
                        Add(FormattableString.Invariant($"health-choice:{healthSelection.CurrentPhaseIndex},{healthSelection.ChoicePhaseIndex},{healthSelection.ChoiceIndex}"));
                }
                Add(FormattableString.Invariant($"{combat.State.Combo.ComboLength},{combat.State.Combo.OutstandingWuxingBonus},{combat.State.Combo.LastComboValue},{combat.State.Combo.AllowReverseNext},{combat.LastPlayedCardColor},{combat.PendingExperienceAwardCount},{combat.PendingEnemyActionCount},{combat.PendingLevelUpRewardRevision}"));
            }
            return AgentGameTrace.Hash(evidence.ToString());
        }

        [Serializable] private sealed class RecoverySeals { public List<CardSealSpec> seals; }

        public bool TryAct(string actionId, string summary, out string code, out string message)
        {
            ThrowIfDisposed(); code = null; message = null;
            _actionStartStep = _trace.StepCount;
            AgentGameCommand command = _commands.FirstOrDefault(c => c.View.actionId == actionId);
            if (command == null || IsTerminal) { code = "InvalidAction"; message = "Action is not present in the current legalActions."; return false; }
            if (Revision >= MaximumActions || !_trace.HasActionCapacity)
            { Terminate("BudgetExceeded", "External action or evidence safety budget reached."); code = "BudgetExceeded"; message = _reason; return false; }
            AgentGameObservation before = _observation;
            var settlementBoundary = new AgentGameSettlementBoundary(_run);
            try
            {
                command.Execute();
                AdvanceFormalBoundary();
                _economy.Synchronize();
                _interactions.AfterCommit();
                Revision++;
                Refresh(settlementBoundary);
                if (!_trace.Add(Revision, command.View, summary, before, _observation))
                {
                    Terminate("BudgetExceeded", "Evidence safety budget reached after the committed action; it was not rolled back.");
                    code = "BudgetExceeded"; message = _reason; return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                // A formal command may already have committed effects. Never
                // claim rollback or allow another command against partial state.
                Revision++; _outcome = "ExecutionError"; _reason = exception.GetType().Name + ": " + exception.Message;
                _commands.Clear();
                try { Refresh(); }
                catch { _observation = AgentGameTrace.Clone(before); _observation.phase = _outcome;
                    _observation.outcome = _outcome; _observation.terminationReason = _reason; }
                _trace.Add(Revision, command.View, summary, before, _observation, "ExecutionError", _reason);
                ReleaseCombat();
                code = "ExecutionError"; message = "Session terminated after an execution error; effects were not declared rolled back. " + _reason;
                return false;
            }
        }

        public void Terminate(string outcome, string reason)
        {
            ThrowIfDisposed();
            if (IsTerminal) return;
            AgentGameObservation before = _observation;
            _outcome = outcome; _reason = reason; Revision++;
            try { Refresh(); }
            catch
            {
                _commands.Clear(); _observation = AgentGameTrace.Clone(before);
                _observation.phase = outcome; _observation.outcome = outcome; _observation.terminationReason = reason;
            }
            _trace.Add(Revision, new AgentGameAction { actionId = "administrative/" + outcome, kind = outcome }, null,
                before, _observation, outcome);
            ReleaseCombat();
        }

        private void Enter(LevelNode node)
        {
            _activeNode = node;
            if (node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale)
            {
                Require(_run.CurrentFloor.TryBeginEncounter(node.contentId, out _, out string error), error);
                _combatRules = RunCombatRules.Create(_entry.Account, _run.CurrentFloor, _run);
                _assembly = _entry.CreateEncounter(_run, node, _random, _identities, _combatRules);
                _combatSession = new HeadlessCombatSession(_assembly.Combat);
                _trace.Attach(_assembly.Combat);
                _assembly.Combat.StartTurn();
            }
            else if (node.nodeType == LevelNodeType.Exit)
            {
                LevelRunSession departed = _run.CurrentFloor;
                DungeonRunFloorExitResult exit = _interactions.CommitExit(
                    departed.Profile.contentId, node.contentId, _entry.ResolveFloor);
                if (exit.NewlyCommitted)
                {
                    // The transaction completes the old session before replacing
                    // CurrentFloor. Preserve that floor's actual coverage.
                    _pastCoverage[exit.FloorContentId] = CurrentNodes(false, departed);
                    _interactions.Dispose();
                    _interactions = _entry.OpenInteractions(_run, _economy, _random);
                }
                _activeNode = null;
            }
            else _interactions.Begin(node);
        }

        private void AdvanceFormalBoundary()
        {
            if (_assembly != null)
            {
                CombatController combat = _assembly.Combat;
                if (combat.AwaitingRewardSelection || combat.HasPendingExperienceAwards) return;
                if (combat.State.CurrentPhase == BattlePhase.BattleDefeat)
                {
                    Require(_run.TryCompleteDefeat(combat, out _, out string error), error);
                    _economy.Synchronize();
                    Require(DungeonRunSettlement.TrySettleDefeat(_run, out _, out error), error);
                    RunAcceptanceSession.CommitAcceptedUnlocks(_entry.Account, _run, _assembly.UnlockRequirements);
                    _outcome = "Defeat"; ReleaseCombat(); return;
                }
                if (combat.State.CurrentPhase != BattlePhase.BattleVictory) return;
                Require(_run.TryCompleteVictory(combat, out _, out string victoryError), victoryError);
                RunAcceptanceSession.CommitAcceptedUnlocks(_entry.Account, _run, _assembly.UnlockRequirements);
                if (_activeNode.nodeType != LevelNodeType.Finale)
                    new LevelRewardGrantRules(_entry.Account).Apply(LevelRewardGrantRules.GetAutomaticGrants(_activeNode));
                ReleaseCombat();
            }
            if (_run.CurrentFloor.TryGetPendingVictorySealReward(out _)) return;
            if (_run.CurrentFloor.TryGetPendingReturn(out LevelEncounterReturn pending))
            {
                if (_activeNode?.nodeType == LevelNodeType.Finale)
                {
                    _economy.Synchronize();
                    Require(DungeonRunSettlement.TrySettleVictory(_run, _entry.Account, _entry.CompletionUnlocks, null, out _, out string error, persist: () => { }), error); // Isolated account memory boundary.
                    _outcome = "Victory"; return;
                }
                Require(_run.CurrentFloor.TryAcknowledgeReturn(pending.NodeContentId), "Cannot acknowledge encounter return.");
                _activeNode = null;
            }
            if (!string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId)) return;
            // This isolated formal host accepts account state in memory. The
            // durable gateway archive subsequently commits this whole command.
            var sourcePhase = DungeonRunSideEventActivation.TryActivateReady(_run,
                () => { }, out string sourceError);
            Require(sourcePhase != DungeonRunSideEventActivationResult.Failed, sourceError);
            _activeNode = null;
        }

        private void Refresh(AgentGameSettlementBoundary? settlementBoundary = null)
        {
            string phase = IsTerminal ? _outcome : _assembly != null ? "Combat" :
                _run.CurrentFloor.TryGetPendingVictorySealReward(out _) ? "VictoryReward" :
                _run.CardOfferings.HasPendingChoice || !string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId) ? "Interaction" : "Map";
            _commands = IsTerminal ? new List<AgentGameCommand>() : BuildCommands(phase);
            foreach (AgentGameCommand command in _commands) command.View.actionId = "r" + Revision + "/" + command.View.actionId;
            var currentNodes = CurrentNodes(!IsTerminal && phase == "Map");
            _observation = new AgentGameObservation { testCaseId = _entry.TestCaseId, floorContentId = _run.CurrentFloorContentId,
                activeNodeContentId = _activeNode?.contentId, phase = phase, outcome = _outcome, terminationReason = _reason,
                frozenEncounterFingerprint = FrozenEncounterFingerprint,
                player = Player(), nodes = currentNodes, offer = _assembly == null ? _interactions.ObservePending() : BattleOffer(),
                settlement = (_outcome == "Victory" || _outcome == "Defeat")
                    ? settlementBoundary.HasValue
                        ? AgentGameSettlementObservation.Copy(_run, settlementBoundary.Value)
                        : _observation?.settlement
                    : null };
            foreach (List<AgentGameNode> nodes in _pastCoverage.Values) _observation.coverage.AddRange(nodes);
            _observation.coverage.AddRange(currentNodes);
            if (IsTerminal)
                foreach (LevelRunProfile floor in _entry.Floors)
                    if (floor.contentId != _run.CurrentFloorContentId && !_pastCoverage.ContainsKey(floor.contentId))
                        foreach (LevelNode node in floor.nodes.Where(n => n != null && n.nodeType != LevelNodeType.Start))
                            _observation.coverage.Add(new AgentGameNode { floorContentId = floor.contentId, nodeContentId = node.contentId,
                                kind = node.nodeType.ToString(), state = "NotReached", coverage = "Unreachable", reason = "Run ended before reaching this floor." });
            if (_assembly != null)
            {
                _observation.battle = AgentGameBattle.Copy(_assembly.Combat, _combatSession);
                _observation.battle.cashOutAllowed = CanCashOutCombatReward();
                _observation.deck = CombatCards().OrderBy(c => c.runtimeInstanceId).Select(c => AgentGameCard.Copy(c)).ToList();
            }
            else
            {
                List<CardConfig> cards = _interactions.ReadDeck();
                try { _observation.deck = cards.OrderBy(c => c.runtimeInstanceId).Select(c => AgentGameCard.Copy(c)).ToList(); }
                finally { AgentGameInteractions.DestroyCards(cards); }
            }
        }

        private List<AgentGameCommand> BuildCommands(string phase)
        {
            if (phase == "Interaction" || phase == "VictoryReward") return _interactions.Commands();
            var commands = new List<AgentGameCommand>();
            if (phase == "Map")
            {
                foreach (LevelNode node in _run.CurrentFloor.Nodes)
                {
                    if (node == null || node.nodeType == LevelNodeType.Start) continue;
                    if (AgentGameAbandonedCarts.TryAppend(_run, node, commands, () => { })) continue; // Isolated memory checkpoint.
                    if (_run.CurrentFloor.GetNodeState(node.contentId) == LevelNodeViewState.Available && _interactions.Availability(node, out _) == null)
                    {
                        if (AgentGameManaStatues.TryAppend(_run, node, commands, () => { })) continue; // Isolated memory checkpoint.
                        commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "node/" + node.contentId,
                            kind = "EnterNode", nodeContentId = node.contentId, title = node.nodeType.ToString() }, () => Enter(node)));
                    }
                }
                return commands;
            }
            CombatController combat = _assembly.Combat;
            if (combat.CanRerollPendingRewards)
            {
                commands.Add(new AgentGameCommand(new AgentGameAction
                {
                    actionId = "combat/RerollRewards",
                    kind = "RerollRewards",
                    title = "重选",
                    description =
                        $"消耗 1 次重选；剩余 {combat.RewardRerollsRemaining} 次。"
                }, () =>
                {
                    bool success = combat.TryRerollPendingRewards(
                        out string error);
                    Require(success, error);
                }));
            }
            if (combat.HasPendingLevelUpSealAssignment)
            {
                foreach (CardConfig card in combat.GetPendingLevelUpSealCandidates())
                {
                    int id = card.runtimeInstanceId;
                    commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "combat/seal-host/" + id,
                        kind = "AssignRewardSeal", runtimeInstanceId = id, contentId = card.contentId, title = card.cardName },
                        () => Require(combat.TryAssignPendingLevelUpSeal(id, out string error), error)));
                }
                commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "combat/seal-back", kind = "BackToRewards", title = "返回奖励" },
                    () => Require(combat.CancelPendingLevelUpSealSelection(), "Cannot return to pending rewards.")));
                return commands;
            }
            foreach (LegalAction action in _combatSession.GetLegalActions())
            {
                if (action.Type == LegalActionType.SelectReward)
                {
                    LevelUpSystem.Reward reward = combat.PendingLevelUpRewards[action.RewardIndex];
                    if (reward.type == LevelUpSystem.RewardType.Gem && (reward.seal == null ||
                        !CombatCards().Any(c => CardSealRules.CanAssign(c, reward.seal, _combatRules, _interactions.IsEvolutionResultEligible)))) continue;
                }
                commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "combat/" + action,
                    kind = action.Type.ToString(), runtimeInstanceId = action.CardRuntimeInstanceId, rewardIndex = action.RewardIndex }, () => {
                        StepResult result = _combatSession.Step(action); Require(result.Success, result.Message);
                    }));
            }
            if (CanCashOutCombatReward())
                commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "combat/reward-cash-out", kind = "CashOutReward", title = "化玉" },
                    () => Require(combat.TryCashOutPendingRewards(out _), "Cannot cash out pending rewards.")));
            return commands;
        }

        private bool CanCashOutCombatReward() => _assembly?.Combat.CanCashOutPendingRewards == true &&
            _entry.Account.rewardAbandonUnlocked && (_entry.RewardUtilitiesAllowed?.Invoke(_activeNode) ?? true);

        private List<CardConfig> CombatCards() => _assembly.Combat.Deck.Concat(_assembly.Combat.Hand)
            .Concat(_assembly.Combat.Discard).Where(c => c != null).GroupBy(c => c.runtimeInstanceId).Select(g => g.First()).ToList();

        private AgentGameOffer BattleOffer()
        {
            CombatController combat = _assembly.Combat;
            if (!combat.AwaitingRewardSelection) return null;
            var result = new AgentGameOffer { kind = "CombatReward", nodeContentId = _activeNode.contentId,
                phase = combat.HasPendingLevelUpSealAssignment ? "SealTarget" : "RewardChoice",
                selectedSeal = AgentGameSeal.Copy(combat.PendingLevelUpSeal) };
            if (combat.HasPendingLevelUpSealAssignment)
                result.options.AddRange(combat.GetPendingLevelUpSealCandidates().Select(c => new AgentGameOption {
                    choiceId = "host/" + c.runtimeInstanceId, runtimeInstanceId = c.runtimeInstanceId, contentId = c.contentId, card = AgentGameCard.Copy(c) }));
            else for (int i = 0; i < combat.PendingLevelUpRewards.Count; i++)
            {
                LevelUpSystem.Reward reward = combat.PendingLevelUpRewards[i];
                result.options.Add(new AgentGameOption { choiceId = i.ToString(CultureInfo.InvariantCulture), kind = reward.type.ToString(),
                    title = reward.name, description = reward.description, contentId = reward.card?.contentId ?? reward.seal?.contentId,
                    card = AgentGameCard.Copy(reward.card), seal = AgentGameSeal.Copy(reward.seal), amount = reward.statAmount });
            }
            return result;
        }

        private List<AgentGameNode> CurrentNodes(bool allowExecution, LevelRunSession floor = null)
        {
            floor ??= _run.CurrentFloor;
            var result = new List<AgentGameNode>();
            foreach (LevelNode node in floor.Nodes.Where(n => n != null && n.nodeType != LevelNodeType.Start))
            {
                LevelNodeViewState state = floor.GetNodeState(node.contentId);
                if (state == LevelNodeViewState.Hidden) continue;
                string unavailable = null, reason = null;
                // When another transaction is active, inspecting unrelated offers
                // is prohibited by Dungeon. Classify their authored placeholder
                // status without attempting an offer inspection.
                if (node.completionEffects?.Any(e => e == null || e.kind == LevelInteractionEffectKind.Unavailable) == true)
                { unavailable = "Unimplemented"; reason = "Author-declared effect is not implemented."; }
                else if (allowExecution) unavailable = _interactions.Availability(node, out reason);
                else if (_lastAvailability.TryGetValue(floor.Profile.contentId + "/" + node.contentId, out var known))
                { unavailable = known.status; reason = known.reason; }
                if (allowExecution)
                    _lastAvailability[floor.Profile.contentId + "/" + node.contentId] = (unavailable, reason);
                bool completed = state == LevelNodeViewState.Completed;
                if (completed && AgentGameAbandonedCarts.IsCart(node) && _run.AbandonedCarts.HasPendingPickup(floor.Profile.contentId, node.contentId))
                {
                    completed = false;
                    reason = "弃车已击碎，护腕尚未领取。";
                }
                bool cartCanAct = false;
                if (allowExecution && ReferenceEquals(floor, _run.CurrentFloor) && AgentGameAbandonedCarts.IsCart(node))
                {
                    AgentGameAbandonedCarts.InspectCoverage(_run, node, out cartCanAct, out bool cartComplete, out string cartReason);
                    completed = cartComplete;
                    reason = cartReason;
                }
                result.Add(new AgentGameNode { floorContentId = floor.Profile.contentId, nodeContentId = node.contentId,
                    kind = node.nodeType.ToString(), state = state.ToString(), executable = allowExecution && (cartCanAct || state == LevelNodeViewState.Available && unavailable == null),
                    coverage = completed ? "Completed" : unavailable ?? (IsTerminal && state == LevelNodeViewState.Locked ? "Unreachable" : "Unvisited"),
                    reason = completed ? null : reason,
                    knownEffects = node.completionEffects?.Where(e => e != null).Select(e => e.summaryText ?? e.kind.ToString()).ToList() ?? new List<string>(),
                    effects = node.completionEffects?.Where(e => e != null).Select(e => new AgentGameEffect { kind = e.kind.ToString(),
                        summary = e.summaryText, detail = e.detailText, amount = e.amount, maximumAmount = e.amountMax,
                        targetContentId = e.targetContentId }).ToList() ?? new List<AgentGameEffect>() });
            }
            return result;
        }

        private AgentGamePlayer Player()
        {
            CombatController combat = _assembly?.Combat;
            PlayerStats p = combat?.Player ?? _interactions.BasePlayer;
            LevelRunModifierSnapshot mods = _run.CurrentFloor.Modifiers;
            // Run bonuses are nonnegative; widen sums without adding new map-only lower bounds.
            return new AgentGamePlayer { health = combat != null ? p.currentHealth : _run.CurrentHealth,
                maximumHealth = combat != null ? p.maxHealth : _run.MaxHealth, armor = combat != null ? p.armor : 0,
                mana = combat?.State.Mana.CurrentMana ?? (_run.HasManaState ? _run.MapCurrentMana : 0), temporaryMana = combat?.State.Mana.TemporaryMana ?? 0,
                baseMana = combat != null ? combat.State.Mana.BaseMana
                    : _run.RunProgression.CardPlayGrowth.ProjectBaseMana(
                        (int)Math.Min(int.MaxValue, (long)p.faLi + mods.BaseManaBonus)),
                level = combat?.CurrentLevel ?? _run.CombatProgression.CurrentLevel,
                experience = combat?.CurrentXP ?? _run.CombatProgression.CurrentExperience,
                requiredExperience = combat?.RequiredXP ?? _run.CombatProgression.GetRequiredExperience(_run.CombatProgression.CurrentLevel),
                might = combat != null ? p.fengRui
                    : (int)Math.Min(int.MaxValue, (long)p.fengRui + mods.MightBonus), amount = p.amount,
                area = combat != null ? p.bengLie
                    : (int)Math.Min(int.MaxValue, (long)p.bengLie + mods.AreaBonus),
                duration = combat?.GetCurrentStatValue(StatType.NingShen)
                    ?? _run.RunProgression.CrawlerDurationReserve.PendingBonus,
                standardDraw = combat != null ? p.shouPai
                    : (int)Math.Min(int.MaxValue, (long)p.shouPai + mods.HandLimitBonus), drawBonus = p.yinJuan,
                luck = combat?.CurrentLuck ?? _run.RunProgression.ProjectLuck(p.tianYun),
                greed = combat?.CurrentGreed ?? _run.RunProgression.ProjectGreed(p.lianYu),
                growth = combat?.CurrentGrowth ?? _run.RunProgression.ProjectGrowth(p.mingWu),
                revival = p.hunHun, curse = p.zhaoSha, jade = _entry.Account.lingyu,
                baseManaBonus = mods.BaseManaBonus, mightBonus = mods.MightBonus, areaBonus = mods.AreaBonus,
                baseArmorPerTurnBonus = mods.BaseArmorPerTurnBonus,
                standardDrawBonus = mods.HandLimitBonus,
                rewardRerollsRemaining = _run.RewardRerollsRemaining,
                cardPlayGrowthEnabled = _run.RunProgression.CardPlayGrowth.Enabled,
                totalRealPlays = _run.RunProgression.CardPlayGrowth.TotalRealPlays,
                cardPlayGrowthCycles = _run.RunProgression.CardPlayGrowth.SettledCycles,
                cardPlayGrowthBaseManaBonus = _run.RunProgression.CardPlayGrowth.BaseManaBonus,
                unlockedRelics = new List<string>(_entry.Account.unlockedRelics) };
        }

        internal List<AgentGameEvent> LastActionEvents => _trace.EventsAfter(_actionStartStep);

        private void ReleaseCombat()
        { _trace.Detach(); _combatSession?.Dispose(); _combatSession = null; _assembly?.Dispose(); _assembly = null; }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(AgentGameSession)); }
        private static void Require(bool success, string error) => AgentGameInteractions.Require(success, error);
        public void Dispose()
        { if (_disposed) return; _disposed = true; ReleaseCombat(); _interactions?.Dispose(); _trace.Dispose(); }
    }

}
