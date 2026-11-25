using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    public sealed class RunManaRepairPickupOffer
    {
        public int Amount { get; }
        public bool Collected { get; }
        public bool CanCollect => !Collected;
        internal RunManaRepairPickupOffer(RunManaRepairPickupEntry entry)
        { Amount = entry.amount; Collected = entry.collected; }
    }

    /// <summary>Location of a paid, uncollected reward in a historical Run save.</summary>
    public sealed class RunLegacyManaPickup
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public int Amount { get; }
        internal RunLegacyManaPickup(RunManaRepairPickupEntry entry)
        { FloorContentId = entry.floorContentId; NodeContentId = entry.nodeContentId; Amount = entry.amount; }
    }

    /// <summary>
    /// Legacy schema-41 proof for already-paid Mana platforms. New exchanges
    /// apply card removal and Mana in one transaction and create no entries.
    /// CardConversions alone settles old entries inside its commit boundary.
    /// </summary>
    internal sealed class RunManaRepairPickupJournal
    {
        private readonly RunManaRepairPickupSaveData _state;
        internal RunManaRepairPickupJournal(RunManaRepairPickupSaveData state = null)
        { _state = state == null ? Empty() : Clone(state); ValidateLocal(_state); }
        internal RunManaRepairPickupSaveData Capture() => Clone(_state);
        internal RunManaRepairPickupEntry Find(string floor, string node)
            => _state.entries.SingleOrDefault(e => e.floorContentId == floor && e.nodeContentId == node);
        internal bool HasPending(string floor, string node) => Find(floor, node)?.collected == false;
        internal IReadOnlyList<string> PendingNodeIds(string floor)
            => _state.entries.Where(e => e.floorContentId == floor && !e.collected)
                .Select(e => e.nodeContentId).ToList().AsReadOnly();
        internal IReadOnlyList<RunLegacyManaPickup> PendingPickups()
            => _state.entries.Where(e => !e.collected).Select(e => new RunLegacyManaPickup(e)).ToList().AsReadOnly();
        internal RunManaRepairPickupJournal WithRepair(string floor, string node, LevelInteractionEffect effect,
            int cardId, int amount)
        {
            var next = Capture();
            if (Find(floor, node) != null) throw Invalid("Platform already has a repair proof.");
            next.entries.Add(new RunManaRepairPickupEntry { floorContentId = floor, nodeContentId = node,
                effectId = effect.effectId, profileContentId = effect.targetContentId,
                cardRuntimeInstanceId = cardId, amount = amount });
            next.fingerprint = Fingerprint(next.entries);
            return new RunManaRepairPickupJournal(next);
        }
        internal RunManaRepairPickupJournal WithCollection(string floor, string node, int before, int after)
        {
            var next = Capture();
            var entry = next.entries.Single(e => e.floorContentId == floor && e.nodeContentId == node);
            entry.collected = true; entry.baseBefore = before; entry.baseAfter = after;
            next.fingerprint = Fingerprint(next.entries);
            return new RunManaRepairPickupJournal(next);
        }
        internal static bool MatchesRepair(LevelInteractionReceipt receipt, RunManaRepairPickupEntry entry)
            => receipt.Kind == LevelInteractionEffectKind.ConvertCard && receipt.Amount == 0 &&
                receipt.ValueBefore == 0 && receipt.ValueAfter == 1 &&
                receipt.TargetRuntimeInstanceId == entry.cardRuntimeInstanceId &&
                receipt.TargetContentId == entry.profileContentId && receipt.EffectId == entry.effectId;
        internal static LevelInteractionReceipt PickupReceipt(RunManaRepairPickupEntry entry, bool newlyCollected)
            => new LevelInteractionReceipt(entry.effectId + ".mana-pickup", LevelInteractionEffectKind.AddBaseMana,
                null, null, entry.baseAfter - entry.baseBefore, entry.baseBefore, entry.baseAfter, newlyCollected,
                "基础灵力 +" + (entry.baseAfter - entry.baseBefore).ToString(CultureInfo.InvariantCulture),
                "每回合基础灵力增加。", RewardRarity.Common);

        internal static RunManaRepairPickupSaveData ValidateRestored(DungeonRunSaveData data,
            IReadOnlyList<LevelRunSession> visited, string payload)
        {
            if (data.schemaVersion < 41)
            {
                // JsonUtility may instantiate a default nested DTO even when
                // the historical wire has no such field. Only raw presence is
                // evidence of a forbidden future field, including false/null.
                if (JsonObjectShape.TryGetField(payload, "hasManaRepairPickupState", out _) ||
                    JsonObjectShape.TryGetField(payload, "manaRepairPickups", out _))
                    throw Invalid("Legacy Run cannot contain a deferred Mana repair journal.");
                // Legacy conversion receipts already granted their modifier.
                // They are deliberately never inferred as new released pickups.
                return Empty();
            }
            if (!data.hasManaRepairPickupState || data.manaRepairPickups == null ||
                !JsonObjectShape.TryGetField(payload, "hasManaRepairPickupState", out string present) || present != "true" ||
                !JsonObjectShape.TryGetField(payload, "manaRepairPickups", out string raw) ||
                !JsonObjectShape.TryGetField(raw, "version", out string version) || version != "1" ||
                !JsonObjectShape.TryGetField(raw, "fingerprint", out _) ||
                !JsonObjectShape.TryGetField(raw, "entries", out string entries) ||
                !JsonObjectShape.TryGetArrayItems(entries, out var items) ||
                data.manaRepairPickups.entries == null || items.Count != data.manaRepairPickups.entries.Count)
                throw Invalid("Explicit pickup journal wire shape is missing.");
            var state = data.manaRepairPickups;
            ValidateLocal(state);
            for (int i = 0; i < items.Count; i++)
            {
                var entry = state.entries[i];
                string[] fields = { "floorContentId", "nodeContentId", "effectId", "profileContentId",
                    "cardRuntimeInstanceId", "amount", "collected", "baseBefore", "baseAfter" };
                foreach (string field in fields)
                    if (!JsonObjectShape.TryGetField(items[i], field, out _)) throw Invalid("Pickup entry field is missing: " + field);
                foreach (var pair in new[] {
                    new KeyValuePair<string,int>("cardRuntimeInstanceId",entry.cardRuntimeInstanceId),
                    new KeyValuePair<string,int>("amount",entry.amount),
                    new KeyValuePair<string,int>("baseBefore",entry.baseBefore),
                    new KeyValuePair<string,int>("baseAfter",entry.baseAfter) })
                    if (!JsonObjectShape.TryGetField(items[i], pair.Key, out string number) ||
                        !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value != pair.Value)
                        throw Invalid("Pickup numeric field is invalid.");
                if (!JsonObjectShape.TryGetField(items[i], "collected", out string collected) ||
                    collected != (entry.collected ? "true" : "false")) throw Invalid("Pickup collected flag is invalid.");
                var floor = visited.SingleOrDefault(f => f.Profile.contentId == entry.floorContentId)
                    ?? throw Invalid("Pickup floor was not visited.");
                var node = floor.FindNode(entry.nodeContentId);
                var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
                if (node?.nodeType != LevelNodeType.Interaction || !floor.IsNodeEnabled(entry.nodeContentId) ||
                    effect == null || effect.kind != LevelInteractionEffectKind.ConvertCard ||
                    effect.effectId != entry.effectId || effect.targetContentId != entry.profileContentId ||
                    !floor.TryGetInteractionReceipt(entry.effectId, out var receipt) || !MatchesRepair(receipt, entry) ||
                    (floor.GetNodeState(entry.nodeContentId) != LevelNodeViewState.Completed &&
                        floor.GetNodeState(entry.nodeContentId) != LevelNodeViewState.Available &&
                        !(floor.GetNodeState(entry.nodeContentId) == LevelNodeViewState.Completing && floor.ActiveInteractionNodeId == entry.nodeContentId)) ||
                    // Historical repair saves could release the paid node to
                    // Available before leaving the floor. Recovering that owed
                    // reward does not reopen or rewrite historical node state.
                    (entry.collected && entry.floorContentId == data.currentFloorContentId &&
                        floor.GetNodeState(entry.nodeContentId) != LevelNodeViewState.Completed))
                    throw Invalid("Pickup does not match its repaired platform receipt.");
                if (data.cards?.Any(c => c.runtimeInstanceId == entry.cardRuntimeInstanceId) == true)
                    throw Invalid("Consumed repair card still exists in Run deck.");
            }
            foreach (var floor in visited)
                foreach (var node in floor.Nodes.Where(n => floor.IsNodeEnabled(n.contentId)))
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.ConvertCard &&
                            floor.TryGetInteractionReceipt(effect.effectId, out var receipt) &&
                            receipt.TargetRuntimeInstanceId > 0 && receipt.Amount == 0 &&
                            receipt.ValueBefore == 0 && receipt.ValueAfter == 1 &&
                            !state.entries.Any(e => e.floorContentId == floor.Profile.contentId && e.nodeContentId == node.contentId))
                            throw Invalid("Deferred repair receipt has no pickup owner proof.");
            if (data.baseManaBonus < state.entries.Where(e => e.collected).Sum(e => (long)e.baseAfter - e.baseBefore) ||
                state.entries.Any(e => e.collected && data.baseManaBonus < e.baseAfter))
                throw Invalid("Run Mana bonus is below completed pickup proof.");
            return Clone(state);
        }

        private static void ValidateLocal(RunManaRepairPickupSaveData state)
        {
            if (state.version != 1 || state.entries == null || state.fingerprint != Fingerprint(state.entries))
                throw Invalid("Journal version or fingerprint is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cards = new HashSet<int>();
            foreach (var entry in state.entries)
                if (entry == null || !Stable(entry.floorContentId) || !Stable(entry.nodeContentId) ||
                    !Stable(entry.effectId) || !Stable(entry.profileContentId) ||
                    !ids.Add(entry.floorContentId + "|" + entry.nodeContentId) ||
                    entry.cardRuntimeInstanceId <= 0 || !cards.Add(entry.cardRuntimeInstanceId) || entry.amount <= 0 ||
                    (entry.collected ? entry.baseBefore < 0 || entry.baseAfter < entry.baseBefore ||
                        entry.baseAfter != Math.Min(int.MaxValue, (long)entry.baseBefore + entry.amount) :
                        entry.baseBefore != 0 || entry.baseAfter != 0))
                    throw Invalid("Journal entry is invalid or duplicated.");
        }
        private static RunManaRepairPickupSaveData Empty()
        { var state = new RunManaRepairPickupSaveData { version = 1, entries = new List<RunManaRepairPickupEntry>() }; state.fingerprint = Fingerprint(state.entries); return state; }
        private static RunManaRepairPickupSaveData Clone(RunManaRepairPickupSaveData state)
            => JsonUtility.FromJson<RunManaRepairPickupSaveData>(JsonUtility.ToJson(state));
        private static string Fingerprint(List<RunManaRepairPickupEntry> entries)
        {
            if (entries == null || entries.Any(e => e == null)) throw Invalid("Pickup list contains null.");
            var text = new StringBuilder("mana-repair-pickups-v1\n");
            foreach (var e in entries) text.Append(e.floorContentId).Append('|').Append(e.nodeContentId).Append('|').Append(e.effectId).Append('|')
                .Append(e.profileContentId).Append('|').Append(e.cardRuntimeInstanceId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(e.amount.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.collected ? '1' : '0').Append('|')
                .Append(e.baseBefore.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.baseAfter.ToString(CultureInfo.InvariantCulture)).Append('\n');
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }
        private static bool Stable(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '|');
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Mana repair pickup: " + message);
    }
    [Serializable]
    internal sealed class RunManaRepairPickupSaveData
    { public int version; public string fingerprint; public List<RunManaRepairPickupEntry> entries; }
    [Serializable]
    internal sealed class RunManaRepairPickupEntry
    {
        public string floorContentId, nodeContentId, effectId, profileContentId;
        public int cardRuntimeInstanceId, amount;
        public bool collected;
        public int baseBefore, baseAfter;
    }
}
