using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// A field-serialized replay artifact. The authoritative entry plus exact
    /// requests and typed random history reconstruct the encounter; compact
    /// visible-state projections make its before/after evidence inspectable.
    /// No readonly protocol property is passed to JsonUtility implicitly.
    /// </summary>
    [Serializable]
    public sealed class RunAcceptanceTrace
    {
        public const int CurrentSchemaVersion = 3;
        public int schemaVersion = CurrentSchemaVersion;
        public string protocolVersion = AgentProtocol.Version;
        public string strategy = PlanningCombatAgent.Id;
        public int strategyRevision = PlanningCombatAgent.Revision;
        public string entryIdentity, initialRunPayload, scenarioId;
        public int nextInstanceId, plannerSeed, entryExperienceGrant;
        public CombatRuleSet rules;
        public MetaProgression rewardAccount;
        // JsonUtility does not serialize Meta's private permanent-fact owner.
        // This detached field belongs to the replay artifact, never the account.
        public int rewardHighestPlayerLevel;
        public CardUnlockProgressSaveData rewardUnlockProgress;
        public RunAcceptanceSettings budget;
        public CombatPlanningStatistics statistics;
        public RunAcceptanceObservation initial, final;
        public List<RunAcceptanceAction> actions = new List<RunAcceptanceAction>();
        public List<CombatRandomDecision> randomHistory = new List<CombatRandomDecision>();
        public List<RunAcceptanceRewardChoice> rewards = new List<RunAcceptanceRewardChoice>();
        public List<RunAcceptanceCardUsage> cardUsage = new List<RunAcceptanceCardUsage>();

        internal void CaptureRewardAccount(MetaProgression account)
        {
            rewardAccount = StageEncounterAssembler.FreezeRewardAccount(account);
            rewardHighestPlayerLevel = account?.CardUnlocks.HighestPlayerLevel ?? 0;
            rewardUnlockProgress = CardUnlockProgressSaveData.Capture(account?.CardUnlocks.Capture() ?? default);
        }

        public static RunAcceptanceTrace Deserialize(string json)
        {
            RunAcceptanceTrace trace = JsonUtility.FromJson<RunAcceptanceTrace>(json)
                ?? throw new InvalidOperationException("Replay trace could not be decoded.");
            if (trace.schemaVersion == CurrentSchemaVersion)
            {
                if (!JsonObjectShape.TryGetField(json, "rewardUnlockProgress", out string rawProgress))
                    throw new InvalidOperationException("Replay reward-account complete progress is missing.");
                CardUnlockProgressSaveData.ValidateSerializedProgress(rawProgress);
            }
            trace.RestoreRewardAccount();
            return trace;
        }

        /// <summary>Decode the exported qualification facts into a new account owner.
        /// Schemas 1/2 contain no behavior evidence; schema 1 also lacks a peak.
        /// They never infer missing facts
        /// from an unlocked card, starting deck or current Run checkpoint.</summary>
        internal MetaProgression RestoreRewardAccount()
        {
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported reward-account replay schema.");
            int highestLevel = schemaVersion == 1 ? 0 : rewardHighestPlayerLevel;
            if (highestLevel < 0 || (rewardAccount == null && highestLevel != 0))
                throw new InvalidOperationException("Replay reward-account permanent facts are invalid.");
            CardUnlockProgressSnapshot progress = new CardUnlockProgressSnapshot(highestLevel);
            if (schemaVersion == CurrentSchemaVersion)
            {
                if (rewardUnlockProgress == null ||
                    (rewardUnlockProgress.schemaVersion != 2 &&
                     rewardUnlockProgress.schemaVersion != CardUnlockProgressSaveData.CurrentSchemaVersion))
                    throw new InvalidOperationException("Replay reward-account complete progress is missing.");
                progress = rewardUnlockProgress.Read();
                if (progress.HighestPlayerLevel != highestLevel ||
                    (rewardAccount == null && (progress.LifetimeFacts.Counts.Count != 0 || progress.AcceptedRuns.Count != 0)))
                    throw new InvalidOperationException("Replay reward-account progress disagrees with its account.");
            }
            MetaProgression restored = StageEncounterAssembler.FreezeRewardAccount(rewardAccount);
            restored?.CardUnlocks.Restore(progress);
            return restored;
        }

        public void ValidateReplay()
        {
            if ((schemaVersion < 1 || schemaVersion > CurrentSchemaVersion) ||
                (protocolVersion != AgentProtocol.Version && protocolVersion != AgentProtocol.LegacyVersion &&
                    protocolVersion != AgentProtocol.SealAssignmentVersion) || final == null ||
                string.IsNullOrEmpty(initialRunPayload) || actions.Any(a => !a.accepted))
                throw new InvalidOperationException("Trace is incomplete or uses an unsupported replay schema.");
            var factory = new StageEncounterAssembler().CaptureBranchFactory(initialRunPayload,
                BattleScenarioCatalog.Get(scenarioId), nextInstanceId, rules,
                rewardAccount: RestoreRewardAccount());
            var requests = actions.Select(a => a.ToLegalAction()).ToList();
            ReplayTrace replay = CombatPlanningSession.ReplayRecorded(factory, randomHistory, requests, entryExperienceGrant);
            if (JsonUtility.ToJson(RunAcceptanceObservation.Capture(replay.FinalObservation)) != JsonUtility.ToJson(final))
                throw new InvalidOperationException("Exported replay did not reproduce the recorded final visible state.");
            if (replay.Steps.Count != actions.Count) throw new InvalidOperationException("Replay command count differs.");
            for (int i = 0; i < actions.Count; i++)
                if (JsonUtility.ToJson(RunAcceptanceObservation.Capture(replay.Steps[i].After)) !=
                    JsonUtility.ToJson(actions[i].after))
                    throw new InvalidOperationException("Exported replay diverged after command " + actions[i].sequence);
        }

        internal void Finish(CombatPlanningOptions options, EncounterMetrics metrics)
        {
            statistics = options.Statistics;
            CombatPlanningTrace trace = options.Trace;
            if (trace != null)
            {
                entryIdentity = trace.EntryIdentity;
                initial = RunAcceptanceObservation.Capture(trace.Actions.InitialObservation);
                final = RunAcceptanceObservation.Capture(trace.Actions.FinalObservation);
                foreach (ReplayTrace.Step step in trace.Actions.Steps)
                    actions.Add(new RunAcceptanceAction
                    {
                        sequence = step.Sequence, kind = step.RequestedAction.Type,
                        cardInstanceId = step.RequestedAction.CardRuntimeInstanceId,
                        rewardIndex = step.RequestedAction.RewardIndex,
                        accepted = step.Result.Success, failureCode = step.Result.FailureCode,
                        coreErrorCode = step.Result.CoreErrorCode, explanation = step.DecisionSummary,
                        before = RunAcceptanceObservation.Capture(step.Before),
                        after = RunAcceptanceObservation.Capture(step.After)
                    });
                randomHistory.AddRange(trace.RandomHistory);
            }
            if (metrics == null) return;
            foreach (RewardSelectionMetric reward in metrics.RewardSelections)
                rewards.Add(new RunAcceptanceRewardChoice
                {
                    selectedIndex = reward.SelectedIndex,
                    candidates = reward.Candidates.Select(c => new RunAcceptanceRewardCandidate
                    { index = c.Index, kind = c.Type, cardContentId = c.CardContentId,
                        stat = c.StatType, amount = c.StatAmount }).ToList()
                });
            foreach (CardUsageMetric usage in metrics.CardUsage)
                cardUsage.Add(new RunAcceptanceCardUsage { contentId = usage.ContentId,
                    plays = usage.Plays, damage = usage.Damage, armor = usage.Armor,
                    healing = usage.Healing, manaSpent = usage.ManaSpent,
                    manaGained = usage.ManaGained, directKills = usage.DirectKills });
        }
    }

    [Serializable]
    public sealed class RunAcceptanceAction
    {
        public int sequence, cardInstanceId, rewardIndex;
        public LegalActionType kind;
        public bool accepted;
        public StepFailureCode failureCode;
        public string coreErrorCode, explanation;
        public RunAcceptanceObservation before, after;

        /// <summary>Decode the typed request only; explanation text never drives replay.</summary>
        public LegalAction ToLegalAction() => kind switch
        {
            LegalActionType.PlayCard => LegalAction.PlayCard(cardInstanceId),
            LegalActionType.EndTurn => LegalAction.EndTurn(),
            LegalActionType.SelectReward => LegalAction.SelectReward(rewardIndex),
            LegalActionType.AssignRewardSeal => LegalAction.AssignRewardSeal(cardInstanceId),
            LegalActionType.CancelRewardSeal => LegalAction.CancelRewardSeal(),
            _ => throw new InvalidOperationException("Unknown action in exported trace.")
        };
    }

    [Serializable]
    public sealed class RunAcceptanceObservation
    {
        public int turn, health, maxHealth, armor, mana, temporaryMana, combo, level, experience;
        public int drawPileCount, discardPileCount;
        public BattlePhase phase;
        public string outcome;
        public bool awaitingReward;
        public bool sealAssignmentPending;
        public bool cardPlayGrowthEnabled;
        public long totalRealPlays;
        public int cardPlayGrowthCycles, cardPlayGrowthBaseManaBonus;
        public List<int> handInstanceIds = new List<int>();
        public List<RunAcceptanceVisibleEnemy> enemies = new List<RunAcceptanceVisibleEnemy>();
        internal static RunAcceptanceObservation Capture(BattleObservation observation)
            => new RunAcceptanceObservation
            {
                turn = observation.Turn, phase = observation.Phase, outcome = observation.Outcome?.ToString(),
                health = observation.Player.CurrentHealth, maxHealth = observation.Player.MaxHealth,
                armor = observation.Player.Armor, mana = observation.Player.CurrentMana,
                temporaryMana = observation.Player.TemporaryMana, combo = observation.Player.ComboCount,
                level = observation.Player.Level, experience = observation.Player.Experience,
                drawPileCount = observation.DrawPileCount, discardPileCount = observation.DiscardPileCount,
                awaitingReward = observation.AwaitingRewardSelection,
                sealAssignmentPending = observation.SealAssignmentPending,
                cardPlayGrowthEnabled = observation.Player.CardPlayGrowth.Enabled,
                totalRealPlays = observation.Player.CardPlayGrowth.TotalRealPlays,
                cardPlayGrowthCycles = observation.Player.CardPlayGrowth.SettledCycles,
                cardPlayGrowthBaseManaBonus = observation.Player.CardPlayGrowth.BaseManaBonus,
                handInstanceIds = observation.Hand.Select(c => c.RuntimeInstanceId).ToList(),
                enemies = observation.VisibleEnemies.Select(e => new RunAcceptanceVisibleEnemy {
                    runtimeId = e.RuntimeId, contentId = e.ContentId, health = e.CurrentHealth,
                    armor = e.Armor, intent = e.Intent.Type, intentAmount = e.Intent.Amount }).ToList()
            };
    }

    [Serializable]
    public sealed class RunAcceptanceVisibleEnemy
    {
        public int runtimeId, health, armor, intentAmount;
        public string contentId;
        public EnemyIntentType intent;
    }

    [Serializable]
    public sealed class RunAcceptanceRewardChoice
    {
        public int selectedIndex;
        public List<RunAcceptanceRewardCandidate> candidates = new List<RunAcceptanceRewardCandidate>();
    }

    [Serializable]
    public sealed class RunAcceptanceRewardCandidate
    {
        public int index, amount;
        public string cardContentId;
        public LevelUpSystem.RewardType kind;
        public StatType stat;
    }

    [Serializable]
    public sealed class RunAcceptanceCardUsage
    {
        public string contentId;
        public int plays, damage, armor, healing, manaSpent, manaGained, directKills;
    }
}
