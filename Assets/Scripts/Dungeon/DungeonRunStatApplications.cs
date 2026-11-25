using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Ordered project application of the eleven captured source stat IDs. Existing
    /// Run owners retain all state; this module owns mapping, full-batch preflight
    /// and receipts. Integer Base grants are exact, Growth projection caps at 1000,
    /// and Revival uses the admitted reserve capacity. These are project policies,
    /// not a reproduction of native binary32 Minimum/observer behavior. Payment,
    /// event completion and durable idempotence belong to the offering transaction.
    /// </summary>
    internal sealed class DungeonRunStatApplications
    {
        internal const string PolicyVersion = "run-stat-applications-integer-v1";
        private readonly DungeonRunSharedState _shared;
        private readonly DungeonRunPlayerState _playerState;

        internal DungeonRunStatApplications(DungeonRunSharedState shared,
            DungeonRunPlayerState playerState)
        {
            _shared = shared ?? throw new ArgumentNullException(nameof(shared));
            _playerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
        }

        internal DungeonRunPreparedStatApplications Prepare(SideEventCardStatOfferingResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var rewards = new List<DungeonRunStatApplicationRequest>();
            foreach (var reward in result.Rewards) rewards.Add(DungeonRunStatApplicationRequest.Capture(reward));
            return PrepareRequests(rewards);
        }

        // Live preparation and durable mathematical replay share this sole ordered
        // application kernel. Proof parsing never adopts its detached state into a Run.
        private DungeonRunPreparedStatApplications PrepareRequests(IReadOnlyList<DungeonRunStatApplicationRequest> rewards)
        {
            var baseline = RequireBaseline();
            var before = DungeonRunStatApplicationState.Capture(_shared);
            var detached = _shared.CloneForFloorPreparation();
            var receipts = new List<DungeonRunStatApplicationReceipt>();
            int previousIndex = -1;
            foreach (var reward in rewards)
            {
                if (reward == null || reward.ModifiedStatIndex <= previousIndex)
                    throw new InvalidOperationException("Original source tuple indices must be strictly increasing.");
                reward.ValidateArithmetic();
                previousIndex = reward.ModifiedStatIndex;
                var mode = SideEventStatApplicationRules.DefaultMode(reward.SourceStat);
                int ownedBefore = Owned(detached, reward.SourceStat);
                int? effectiveBefore = Effective(detached, baseline, reward.SourceStat);
                Grant(detached, reward.SourceStat, reward.RequestedAmount);
                int ownedAfter = Owned(detached, reward.SourceStat);
                int? effectiveAfter = Effective(detached, baseline, reward.SourceStat);
                receipts.Add(new DungeonRunStatApplicationReceipt(reward, mode,
                    ownedBefore, ownedAfter, effectiveBefore, effectiveAfter));
            }
            if (receipts.Count == 0) throw new InvalidOperationException("An empty grant batch is invalid.");
            var after = DungeonRunStatApplicationState.Capture(detached);
            var proof = new DungeonRunStatApplicationProof(baseline, before, after, rewards, receipts);
            return new DungeonRunPreparedStatApplications(this, before,
                after, DungeonRunStatApplicationProof.WriteBaseline(baseline),
                _shared.RunProgression.AdditionalStats, _shared.RunProgression.RevivalReserve, receipts, proof);
        }

        /// <summary>Captures evidence without applying a grant or authorizing a payment.</summary>
        internal DungeonRunStatApplicationProof CaptureProof(SideEventCardStatOfferingResult result)
            => Prepare(result).CaptureProof();

        /// <summary>Validates closed canonical wire and replays mathematics on fresh
        /// detached owners. Baseline authenticity, source identity, Run association
        /// and durable idempotence require the future transaction journal.</summary>
        internal static DungeonRunStatApplicationProof ParseAndValidateProof(string canonicalJson)
        {
            try
            {
                var proof = DungeonRunStatApplicationProof.Read(canonicalJson);
                var shared = new DungeonRunSharedState(null, new RunProgressionState());
                var playerState = new DungeonRunPlayerState();
                var entry = new PlayerStats();
                proof.Baseline.ToSaveData().ApplyTo(entry);
                playerState.Prepare(entry, shared.RunProgression);
                proof.Before.Restore(shared);
                var replay = new DungeonRunStatApplications(shared, playerState)
                    .PrepareRequests(proof.OrderedRewards).CaptureProof();
                if (!string.Equals(replay.Serialize(), canonicalJson, StringComparison.Ordinal))
                    throw new InvalidOperationException("Saved stat application does not match ordered mathematical replay.");
                return replay;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is FormatException)
            { throw new InvalidOperationException("Invalid stat application mathematical proof.", exception); }
        }

        /// <summary>Accepts only this owner's still-current preparation. No callbacks
        /// run between validation and snapshot adoption. All overflow and reserve
        /// proof checks have already completed on the detached owners.</summary>
        internal IReadOnlyList<DungeonRunStatApplicationReceipt> Apply(DungeonRunPreparedStatApplications prepared)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            prepared.AdoptInto(this, _shared, DungeonRunStatApplicationProof.WriteBaseline(RequireBaseline()));
            return prepared.Receipts;
        }

        internal IReadOnlyList<DungeonRunStatApplicationReceipt> Apply(SideEventCardStatOfferingResult result)
            => Apply(Prepare(result));

        private DungeonRunPlayerBaselineSaveData RequireBaseline()
            => _playerState.CaptureBaseline() ?? throw new InvalidOperationException(
                "The immutable Run entry baseline must be established before a stat grant.");

        private static int Owned(DungeonRunSharedState state, int stat)
        {
            var run = state.RunProgression;
            switch (stat)
            {
                case 0: return state.AreaBonus;
                case 1: return run.AdditionalStats.AmountBonus;
                case 2: return state.BaseArmorPerTurnBonus;
                case 4: return state.MightBonus;
                case 5: return run.RunGrowthBonus;
                case 6: return run.RunGreedBonus;
                case 7: return run.RunLuckBonus;
                case 10: return state.HandLimitBonus;
                case 11: return run.AdditionalStats.MagnetBonus;
                case 13: return run.RevivalReserve.RemainingPercent;
                case 17: return run.AdditionalStats.CurseBonus;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        private static int? Effective(DungeonRunSharedState state,
            DungeonRunPlayerBaselineSaveData baseline, int stat)
        {
            var run = state.RunProgression;
            switch (stat)
            {
                case 0: return Project(baseline.bengLie, state.AreaBonus);
                case 1: return run.AdditionalStats.ProjectAmount(baseline.amount);
                // The map does not persist Current armor. Base must not fabricate it.
                case 2: return null;
                case 4: return Project(baseline.fengRui, state.MightBonus);
                case 5: return run.ProjectGrowth(baseline.mingWu);
                case 6: return run.ProjectGreed(baseline.lianYu);
                case 7: return run.ProjectLuck(baseline.tianYun);
                // Combat hand limit retains its existing minimum of one.
                case 10: return Math.Max(1, Project(baseline.shouPai, state.HandLimitBonus));
                case 11: return run.AdditionalStats.ProjectMagnet(baseline.yinJuan);
                case 13: return run.RevivalReserve.RemainingPercent;
                case 17: return run.AdditionalStats.ProjectCurse(baseline.zhaoSha);
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        private static int Project(int baseline, int bonus)
            => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)baseline + bonus));

        private static void Grant(DungeonRunSharedState state, int stat, int amount)
        {
            var run = state.RunProgression;
            switch (stat)
            {
                case 0: state.AddSourceArea(amount); break;
                case 1: run.AdditionalStats.AddSourceAmount(amount); break;
                case 2: state.AddSourceBaseArmorPerTurn(amount); break;
                case 4: state.AddSourceMight(amount); break;
                case 5: run.AddSourceGrowth(amount); break;
                case 6: run.AddSourceGreed(amount); break;
                case 7: run.AddSourceLuck(amount); break;
                case 10: state.AddSourceHandLimit(amount); break;
                case 11: run.AdditionalStats.AddSourceMagnet(amount); break;
                case 13: run.RevivalReserve.AddCharge(amount); break;
                case 17: run.AdditionalStats.AddSourceCurse(amount); break;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }
    }

    internal sealed class DungeonRunStatApplicationReceipt
    {
        public int ModifiedStatIndex { get; }
        public int SourceStat { get; }
        public SideEventStatModifierMode Mode { get; }
        public int RequestedAmount { get; }
        public int OwnedBefore { get; }
        public int OwnedAfter { get; }
        public int ActualOwnedDelta => OwnedAfter - OwnedBefore;
        public int? EffectiveBefore { get; }
        public int? EffectiveAfter { get; }
        public int? AcceptedEffectiveDelta => EffectiveAfter - EffectiveBefore;
        public int BaseDelta => Mode == SideEventStatModifierMode.Current ? 0 : ActualOwnedDelta;
        // Unknown Current armor is deliberately distinct from an observed zero.
        public int? CurrentDelta => AcceptedEffectiveDelta;
        public string PolicyVersion => DungeonRunStatApplications.PolicyVersion;

        internal DungeonRunStatApplicationReceipt(DungeonRunStatApplicationRequest reward,
            SideEventStatModifierMode mode, int ownedBefore, int ownedAfter,
            int? effectiveBefore, int? effectiveAfter)
        {
            ModifiedStatIndex = reward.ModifiedStatIndex; SourceStat = reward.SourceStat;
            Mode = mode; RequestedAmount = reward.RequestedAmount;
            OwnedBefore = ownedBefore; OwnedAfter = ownedAfter;
            EffectiveBefore = effectiveBefore; EffectiveAfter = effectiveAfter;
        }
    }

    /// <summary>Immutable batch; no detached mutable owner escapes preparation.</summary>
    internal sealed class DungeonRunPreparedStatApplications
    {
        private readonly DungeonRunStatApplications _owner;
        private readonly DungeonRunStatApplicationState _before;
        private readonly DungeonRunStatApplicationState _after;
        private readonly string _baselineJson;
        private readonly RunAdditionalStatModifiers _additionalOwner;
        private readonly RunRevivalReserve _revivalOwner;
        private readonly DungeonRunStatApplicationProof _proof;
        public IReadOnlyList<DungeonRunStatApplicationReceipt> Receipts { get; }

        internal DungeonRunPreparedStatApplications(DungeonRunStatApplications owner,
            DungeonRunStatApplicationState before, DungeonRunStatApplicationState after,
            string baselineJson, RunAdditionalStatModifiers additionalOwner,
            RunRevivalReserve revivalOwner, List<DungeonRunStatApplicationReceipt> receipts,
            DungeonRunStatApplicationProof proof)
        {
            _owner = owner; _before = before; _after = after; _baselineJson = baselineJson;
            _additionalOwner = additionalOwner; _revivalOwner = revivalOwner;
            _proof = proof;
            Receipts = Array.AsReadOnly(receipts.ToArray());
        }

        // This value remains readable after adoption/rollback; reading evidence
        // neither rebinds the preparation nor makes it applicable to another Run.
        internal DungeonRunStatApplicationProof CaptureProof() => _proof;

        private bool IsBoundTo(DungeonRunStatApplications owner, DungeonRunSharedState shared, string baselineJson)
            => ReferenceEquals(_owner, owner) &&
                ReferenceEquals(_additionalOwner, shared.RunProgression.AdditionalStats) &&
                ReferenceEquals(_revivalOwner, shared.RunProgression.RevivalReserve) &&
                _before.Matches(shared) && string.Equals(_baselineJson, baselineJson, StringComparison.Ordinal);

        internal void AdoptInto(DungeonRunStatApplications owner, DungeonRunSharedState shared, string baselineJson)
        {
            if (!IsBoundTo(owner, shared, baselineJson))
                throw new InvalidOperationException("Stat preparation is foreign or stale; no grant was applied.");
            _after.Restore(shared);
        }
    }

    /// <summary>Only the four existing stat owners' immutable values; no new ledger.</summary>
    internal readonly struct DungeonRunStatApplicationState
    {
        private readonly DungeonRunModifierSnapshot _modifiers;
        private readonly RunProgressionSnapshot _progression;
        private readonly RunAdditionalStatSnapshot _additional;
        private readonly RunRevivalSnapshot _revival;
        public DungeonRunModifierSnapshot Modifiers => _modifiers;
        public RunProgressionSnapshot Progression => _progression;
        public RunAdditionalStatSnapshot Additional => _additional;
        public RunRevivalSnapshot Revival => _revival;

        internal DungeonRunStatApplicationState(DungeonRunModifierSnapshot modifiers,
            RunProgressionSnapshot progression, RunAdditionalStatSnapshot additional, RunRevivalSnapshot revival)
        { _modifiers = modifiers; _progression = progression; _additional = additional; _revival = revival; }

        private DungeonRunStatApplicationState(DungeonRunSharedState shared)
        {
            _modifiers = shared.CaptureModifiers(); _progression = shared.RunProgression.Capture();
            _additional = shared.RunProgression.AdditionalStats.Capture();
            _revival = shared.RunProgression.RevivalReserve.Capture();
        }
        internal static DungeonRunStatApplicationState Capture(DungeonRunSharedState shared)
            => new DungeonRunStatApplicationState(shared);
        internal bool Matches(DungeonRunSharedState shared)
            => _modifiers.Equals(shared.CaptureModifiers()) &&
                _progression.Equals(shared.RunProgression.Capture()) &&
                _additional.Equals(shared.RunProgression.AdditionalStats.Capture()) &&
                _revival.Equals(shared.RunProgression.RevivalReserve.Capture());
        internal void Restore(DungeonRunSharedState shared)
        {
            shared.RestoreModifiers(_modifiers); shared.RunProgression.Restore(_progression);
            shared.RunProgression.AdditionalStats.Restore(_additional);
            shared.RunProgression.RevivalReserve.Restore(_revival);
        }
    }
}
