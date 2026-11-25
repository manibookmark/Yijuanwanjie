using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Sole writer of the Run's exploration seed and frozen reward-to-guard mappings.
    /// Preparation is detached and commits one complete floor only after validation.
    /// Callers clone this owner before attempting a larger floor-transition transaction.
    /// No asset, battle RNG, node progress, economy or receipt is mutated here.
    /// </summary>
    internal sealed class DungeonRunExplorationState
    {
        private const int SaveVersion = 1;
        private readonly Dictionary<string, LevelExplorationGuardView> _floors =
            new Dictionary<string, LevelExplorationGuardView>(StringComparer.Ordinal);

        private DungeonRunExplorationState(int seed) { Seed = seed; }
        public int Seed { get; }

        public static DungeonRunExplorationState Create(string runId, int? seed = null)
        {
            RequireRunId(runId);
            return new DungeonRunExplorationState(seed ?? SeedFor("run-seed/v1", runId));
        }

        public DungeonRunExplorationState Clone()
        {
            var copy = new DungeonRunExplorationState(Seed);
            foreach (var pair in _floors) copy._floors.Add(pair.Key, pair.Value.Clone());
            return copy;
        }

        /// <summary>
        /// Freeze once. Contract drift is an error, never permission to reroll.
        /// Rewards and candidates are sorted before using a separate versioned stream
        /// for each floor/group; preparing other floors or groups cannot consume it.
        /// </summary>
        public LevelExplorationGuardView PrepareFloor(LevelRunProfile profile)
        {
            string hash = ContractHash(profile);
            if (_floors.TryGetValue(profile.contentId, out LevelExplorationGuardView existing))
            {
                existing.RequireMatches(profile);
                return existing;
            }
            if (LevelExplorationGuardRule.Rules(profile).Count == 0) return LevelExplorationGuardView.Empty;
            var saved = new LevelExplorationFloorSaveData { floorContentId = profile.contentId, contractHash = hash };
            foreach (LevelExplorationGuardRule rule in LevelExplorationGuardRule.Rules(profile)
                .OrderBy(rule => rule.ruleId, StringComparer.Ordinal))
            {
                var candidates = rule.candidateEncounterNodeIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
                var random = new SeededRandomSource(SeedFor("exploration-guards/v1",
                    Seed.ToString(CultureInfo.InvariantCulture), profile.contentId,
                    rule.ruleId, rule.revision.ToString(CultureInfo.InvariantCulture)));
                random.Shuffle(candidates);
                var group = new LevelExplorationGroupSaveData { ruleId = rule.ruleId, revision = rule.revision };
                int index = 0;
                foreach (string reward in rule.rewardNodeIds.OrderBy(id => id, StringComparer.Ordinal))
                    group.bindings.Add(new LevelExplorationBindingSaveData
                    { rewardNodeId = reward, guardNodeId = candidates[index++] });
                saved.groups.Add(group);
            }
            var prepared = new LevelExplorationGuardView(profile, saved);
            _floors.Add(profile.contentId, prepared);
            return prepared;
        }

        /// <summary>
        /// Read a visited floor without ever creating bindings. Restoration and
        /// acknowledgement retries must fail closed if a frozen mapping is missing.
        /// </summary>
        internal LevelExplorationGuardView ReadFloor(LevelRunProfile profile)
        {
            ContractHash(profile);
            if (_floors.TryGetValue(profile.contentId, out LevelExplorationGuardView view))
            {
                view.RequireMatches(profile);
                return view;
            }
            LevelExplorationGuardView.Empty.RequireMatches(profile);
            return LevelExplorationGuardView.Empty;
        }

        /// <summary>Canonical deep copy. Editing a DTO cannot edit this owner or an issued view.</summary>
        public DungeonRunExplorationSaveData Capture()
        {
            var saved = new DungeonRunExplorationSaveData { schemaVersion = SaveVersion, hasSeed = true, seed = Seed };
            foreach (var pair in _floors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                saved.floors.Add(pair.Value.Capture());
            return saved;
        }

        /// <summary>
        /// Restores authoritative bindings without rerolling, even when the caller later
        /// overrides the Run ID. Missing legacy state is safe only when no visited floor
        /// has exploration rules. New schemas require state even for a rule-free Run.
        /// No partially restored owner escapes on failure.
        /// </summary>
        public static DungeonRunExplorationState Restore(DungeonRunExplorationSaveData saved,
            string originalRunId, IReadOnlyList<LevelRunProfile> visitedFloors, bool requireState)
        {
            RequireRunId(originalRunId);
            if (visitedFloors == null) throw new ArgumentNullException(nameof(visitedFloors));
            var visited = new Dictionary<string, LevelRunProfile>(StringComparer.Ordinal);
            var expected = new Dictionary<string, LevelRunProfile>(StringComparer.Ordinal);
            foreach (LevelRunProfile profile in visitedFloors)
            {
                ContractHash(profile); // Validate every visited contract before accepting any saved mapping.
                if (visited.ContainsKey(profile.contentId)) throw Invalid("已访问楼层重复：" + profile.contentId);
                visited.Add(profile.contentId, profile);
                if (LevelExplorationGuardRule.Rules(profile).Count > 0) expected.Add(profile.contentId, profile);
            }
            // JsonUtility materializes absent inline classes as default objects in
            // legacy payloads. Only an entirely empty pre-v15 shell means "missing";
            // any seed/version/binding evidence remains subject to strict validation.
            // New-schema empty shells must never bypass the required-state contract.
            bool missingLegacyInlineState = !requireState && saved != null &&
                saved.schemaVersion == 0 && !saved.hasSeed && saved.seed == 0 &&
                (saved.floors == null || saved.floors.Count == 0);
            if (saved == null || missingLegacyInlineState)
            {
                if (requireState || expected.Count > 0) throw Invalid("存档缺少必须的探索守卫状态，禁止补抽。");
                return Create(originalRunId);
            }
            if (saved.schemaVersion != SaveVersion || !saved.hasSeed || saved.floors == null)
                throw Invalid("探索存档版本、Seed 标记或楼层集合无效。");
            var restored = new DungeonRunExplorationState(saved.seed);
            foreach (LevelExplorationFloorSaveData floor in saved.floors)
            {
                if (floor == null || string.IsNullOrWhiteSpace(floor.floorContentId) ||
                    !expected.TryGetValue(floor.floorContentId, out LevelRunProfile profile))
                    throw Invalid("探索存档含空、多余、未访问或无规则楼层。");
                if (restored._floors.ContainsKey(floor.floorContentId)) throw Invalid("探索存档楼层重复：" + floor.floorContentId);
                restored._floors.Add(floor.floorContentId, new LevelExplorationGuardView(profile, floor));
            }
            if (restored._floors.Count != expected.Count) throw Invalid("已访问的探索楼层缺少完整绑定，禁止补抽。");
            return restored;
        }

        internal static string ContractHash(LevelRunProfile profile)
            => BitConverter.ToString(Hash("exploration-contract/v1", LevelExplorationGuardRule.ValidatedContract(profile)))
                .Replace("-", string.Empty).ToLowerInvariant();

        private static int SeedFor(string domain, params string[] parts)
        {
            byte[] bytes = Hash(domain, parts);
            // Explicit little-endian decoding: do not depend on platform endianness or GetHashCode.
            uint value = (uint)bytes[0] | ((uint)bytes[1] << 8) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
            return (int)(value & int.MaxValue);
        }

        private static byte[] Hash(string domain, params string[] parts)
        {
            var text = new StringBuilder(domain).Append('|');
            foreach (string part in parts)
                text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        private static void RequireRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("探索状态需要原始稳定 runId。", nameof(runId));
        }

        private static InvalidOperationException Invalid(string message) => new InvalidOperationException(message);
    }

    /// <summary>
    /// Immutable, floor-bound dependency view. Contains no mutable profile reference.
    /// Level construction must call RequireMatches before deriving node availability.
    /// Empty is valid only for a profile without exploration rules, never a fallback.
    /// </summary>
    public sealed class LevelExplorationGuardView
    {
        public static LevelExplorationGuardView Empty { get; } = new LevelExplorationGuardView();
        private readonly LevelExplorationFloorSaveData _saved;
        private readonly Dictionary<string, string> _bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        private LevelExplorationGuardView() { }
        internal LevelExplorationGuardView(LevelRunProfile profile, LevelExplorationFloorSaveData saved)
        {
            ValidateSaved(profile, saved);
            _saved = Copy(saved);
            foreach (LevelExplorationGroupSaveData group in _saved.groups)
                foreach (LevelExplorationBindingSaveData binding in group.bindings)
                    _bindings.Add(binding.rewardNodeId, binding.guardNodeId);
        }

        private LevelExplorationGuardView(LevelExplorationGuardView source)
        {
            _saved = source._saved == null ? null : Copy(source._saved);
            foreach (var pair in source._bindings) _bindings.Add(pair.Key, pair.Value);
        }

        public bool TryGetGuard(string rewardId, out string guardId)
        {
            guardId = null;
            return rewardId != null && _bindings.TryGetValue(rewardId, out guardId);
        }

        /// <summary>Pure validation; wrong ownership, contract drift or missing bindings must fail closed.</summary>
        internal void RequireMatches(LevelRunProfile profile)
        {
            if (_saved != null) { ValidateSaved(profile, _saved); return; }
            LevelExplorationGuardRule.ValidatedContract(profile);
            if (LevelExplorationGuardRule.Rules(profile).Count != 0)
                throw new InvalidOperationException("启用探索守卫的楼层不能使用 Empty 视图。");
        }

        internal LevelExplorationGuardView Clone() => new LevelExplorationGuardView(this);
        internal LevelExplorationFloorSaveData Capture() => _saved == null ? null : Copy(_saved);

        private static void ValidateSaved(LevelRunProfile profile, LevelExplorationFloorSaveData saved)
        {
            string hash = DungeonRunExplorationState.ContractHash(profile);
            IReadOnlyList<LevelExplorationGuardRule> rules = LevelExplorationGuardRule.Rules(profile);
            if (saved == null || rules.Count == 0 || saved.floorContentId != profile.contentId ||
                saved.contractHash != hash || saved.groups == null || saved.groups.Count != rules.Count)
                throw new InvalidOperationException("探索视图所属楼层、合同或规则组不匹配。");
            var byId = rules.ToDictionary(rule => rule.ruleId, StringComparer.Ordinal);
            var seenRules = new HashSet<string>(StringComparer.Ordinal);
            var seenGuards = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelExplorationGroupSaveData group in saved.groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.ruleId) || !seenRules.Add(group.ruleId) ||
                    !byId.TryGetValue(group.ruleId, out LevelExplorationGuardRule rule) ||
                    group.revision != rule.revision || group.bindings == null || group.bindings.Count != rule.rewardNodeIds.Count)
                    throw new InvalidOperationException("探索存档包含缺失、多余、重复或不兼容的规则绑定。");
                var rewards = new HashSet<string>(rule.rewardNodeIds, StringComparer.Ordinal);
                var candidates = new HashSet<string>(rule.candidateEncounterNodeIds, StringComparer.Ordinal);
                foreach (LevelExplorationBindingSaveData binding in group.bindings)
                    if (binding == null || string.IsNullOrWhiteSpace(binding.rewardNodeId) ||
                        string.IsNullOrWhiteSpace(binding.guardNodeId) || !rewards.Remove(binding.rewardNodeId) ||
                        !candidates.Contains(binding.guardNodeId) || !seenGuards.Add(binding.guardNodeId))
                        throw new InvalidOperationException("探索存档奖励/守卫为空、重复、越界或不属于该规则。");
                if (rewards.Count != 0) throw new InvalidOperationException("探索存档奖励绑定不完整。");
            }
        }

        private static LevelExplorationFloorSaveData Copy(LevelExplorationFloorSaveData source)
        {
            var copy = new LevelExplorationFloorSaveData { floorContentId = source.floorContentId, contractHash = source.contractHash };
            foreach (LevelExplorationGroupSaveData group in source.groups.OrderBy(group => group.ruleId, StringComparer.Ordinal))
                copy.groups.Add(new LevelExplorationGroupSaveData
                {
                    ruleId = group.ruleId, revision = group.revision,
                    bindings = group.bindings.OrderBy(binding => binding.rewardNodeId, StringComparer.Ordinal)
                        .Select(binding => new LevelExplorationBindingSaveData
                        { rewardNodeId = binding.rewardNodeId, guardNodeId = binding.guardNodeId }).ToList()
                });
            return copy;
        }
    }

    // Mutable wire DTOs only. Capture/restore copy them at the owner boundary; they are
    // never exposed as runtime state. hasSeed distinguishes a real zero from missing data.
    [Serializable]
    internal sealed class DungeonRunExplorationSaveData
    {
        public int schemaVersion;
        public bool hasSeed;
        public int seed;
        public List<LevelExplorationFloorSaveData> floors = new List<LevelExplorationFloorSaveData>();
    }

    [Serializable]
    internal sealed class LevelExplorationFloorSaveData
    {
        public string floorContentId;
        public string contractHash;
        public List<LevelExplorationGroupSaveData> groups = new List<LevelExplorationGroupSaveData>();
    }

    [Serializable]
    internal sealed class LevelExplorationGroupSaveData
    {
        public string ruleId;
        public int revision;
        public List<LevelExplorationBindingSaveData> bindings = new List<LevelExplorationBindingSaveData>();
    }

    [Serializable]
    internal sealed class LevelExplorationBindingSaveData
    {
        public string rewardNodeId;
        public string guardNodeId;
    }
}
