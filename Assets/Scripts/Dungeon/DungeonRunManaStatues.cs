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
    public enum ManaStatueStage { Dormant = 0, AwaitingPickup = 1, Collected = 2 }
    [Serializable]
    public sealed class LevelInteractionManaStatueContract
    {
        public int version;
        public string sourceEventId;
        public int requiredAcceptedBumps;
        public int manaAmount;
        public bool IsSpecified => version != 0 || !string.IsNullOrEmpty(sourceEventId) || requiredAcceptedBumps != 0 || manaAmount != 0;
    }
    public sealed class DungeonRunManaStatueOffer
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public int AcceptedBumps { get; }
        public ManaStatueStage Stage { get; }
        public bool CanBump => Stage == ManaStatueStage.Dormant;
        public bool CanCollect => Stage == ManaStatueStage.AwaitingPickup;
        internal DungeonRunManaStatueOffer(string floor, string node, string effect, DungeonRunManaStatueEntry entry)
        {
            FloorContentId = floor; NodeContentId = node; EffectId = effect;
            AcceptedBumps = entry?.acceptedBumps ?? 0;
            Stage = entry?.collected == true ? ManaStatueStage.Collected : AcceptedBumps == 3 ? ManaStatueStage.AwaitingPickup : ManaStatueStage.Dormant;
        }
    }
    /// <summary>
    /// Owns accepted-collision revisions, released pickups and exact durable Mana
    /// statue decisions. PlayerState owns the map balance; SharedState owns the
    /// base bonus; their same-owner restoration belongs to the Run aggregate.
    /// Collisions are explicit accepted actions, never elapsed turns or automatic
    /// node entry. They leave the node available and never force three actions in
    /// succession. Persist is a pure atomic save: return means durable publication;
    /// a throw means nothing was published. Partial-collision checkpoints and
    /// receipt recovery are project guarantees, not observed VC save behavior.
    /// </summary>
    public sealed class DungeonRunManaStatues
    {
        public const string SourceEventId = "event.xiyou.mana-statue.v1";
        public const int RequiredAcceptedBumps = 3;
        public const int ManaAmount = 1;
        public const int SourceMaximumMana = 1000000;
        private readonly DungeonRunSession _run;
        private DungeonRunManaStatueSaveData _state;
        internal bool IsCommitting { get; private set; }
        internal DungeonRunManaStatues(DungeonRunSession run, DungeonRunManaStatueSaveData saved = null)
        { _run = run ?? throw new ArgumentNullException(nameof(run)); _state = saved == null ? Empty() : Clone(saved); ValidateLocal(_state); }

        public bool TryInspect(string nodeId, out DungeonRunManaStatueOffer offer, out string error)
        {
            offer = null;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var entry = Find(nodeId);
                if (entry?.collected == true) RequireReceipt(nodeId, effect, entry);
                else RequireAvailable(nodeId);
                offer = Offer(nodeId, effect, entry); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        /// <summary>The expected prior count is the collision revision; stale callbacks cannot add another bump.</summary>
        public bool TryBump(string nodeId, int expectedPreviousBumps,
            out DungeonRunManaStatueOffer offer, out string error, Action persist)
        {
            offer = null;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var entry = Find(nodeId);
                if (expectedPreviousBumps < 0 || expectedPreviousBumps >= RequiredAcceptedBumps)
                    throw Invalid("Collision revision must be 0, 1 or 2.");
                int bumps = entry?.acceptedBumps ?? 0;
                if (bumps == expectedPreviousBumps + 1)
                {
                    if (entry.collected) RequireReceipt(nodeId, effect, entry); else RequireAvailable(nodeId);
                    offer = Offer(nodeId, effect, entry); return true;
                }
                if (bumps != expectedPreviousBumps || entry?.collected == true)
                    throw Invalid("Collision revision is stale or skips an accepted action.");
                RequireAvailable(nodeId);
                var next = Clone(_state);
                var item = next.entries.SingleOrDefault(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
                if (item == null)
                {
                    item = new DungeonRunManaStatueEntry { floorContentId = _run.CurrentFloorContentId,
                        nodeContentId = nodeId, effectId = effect.effectId, sourceEventId = SourceEventId };
                    next.entries.Add(item);
                }
                item.acceptedBumps++;
                Commit(next, () => { }, persist, () => RequireAvailable(nodeId));
                offer = Offer(nodeId, effect, Find(nodeId)); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public bool TryCollectAndComplete(string nodeId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
        {
            receipt = default;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var previous = Find(nodeId);
                if (previous?.collected == true)
                {
                    var prior = RequireReceipt(nodeId, effect, previous);
                    receipt = new LevelInteractionReceipt(prior.EffectId, prior.Kind, prior.TargetContentId, prior.PresentationId,
                        prior.Amount, prior.ValueBefore, prior.ValueAfter, false, prior.SummaryText, prior.DetailText, prior.Rarity, prior.TargetRuntimeInstanceId);
                    return true;
                }
                RequireAvailable(nodeId);
                if (previous?.acceptedBumps != RequiredAcceptedBumps) throw Invalid("Three accepted collisions must release the pickup first.");
                if (!_run.HasManaState) throw Invalid("Map Mana balance is unknown; establish the Run player baseline first.");
                var next = Clone(_state);
                var item = next.entries.Single(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
                LevelInteractionReceipt applied = default;
                Commit(next, () =>
                {
                    if (!_run.CurrentFloor.TryBeginInteraction(nodeId, out string failure)) throw Invalid(failure);
                    item.currentBefore = _run.MapCurrentMana; item.baseBonusBefore = _run.BaseManaBonus;
                    _run.ApplyPersistentManaPickup();
                    item.currentAfter = _run.MapCurrentMana; item.baseBonusAfter = _run.BaseManaBonus; item.collected = true;
                    if (!_run.CurrentFloor.TryRecordExternalEffect(nodeId, effect, item.currentBefore, item.currentAfter,
                        ManaAmount, out applied, out failure)) throw Invalid(failure);
                    if (!_run.CurrentFloor.TryCompleteInteraction(nodeId, out failure)) throw Invalid(failure);
                }, persist, () => RequireCompletedNode(nodeId));
                receipt = applied; return true;
            }
            catch (Exception ex) { receipt = default; error = ex.Message; return false; }
        }
        /// <summary>Leaving preserves collisions/released pickup. No active interaction, receipt or save is created.</summary>
        public bool TryLeave(string nodeId, out string error)
        {
            if (!Guard(out error)) return false;
            try { RequireEffect(_run.CurrentFloor, nodeId); RequireAvailable(nodeId); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        private void Commit(DungeonRunManaStatueSaveData next, Action apply, Action persist, Action requireCheckpoint)
        {
            if (persist == null) throw Invalid("Strict atomic persistence is required before mutation.");
            var previous = _state; IsCommitting = true;
            try
            {
                _run.CommitRewardState(() => { apply(); next.ledgerFingerprint = Fingerprint(next.entries); _state = next; requireCheckpoint(); },
                    () => { persist(); requireCheckpoint(); });
            }
            catch { _state = previous; throw; }
            finally { IsCommitting = false; }
        }
        private bool Guard(out string error)
        {
            error = null;
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.Crystals.HasPendingEvent) { error = "Mana statue transaction is already committing."; return false; }
            if (_run.Interactions.HasPendingChoice || _run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy))
            { error = "Complete the current Run choice first."; return false; }
            if (_run.TryGetConclusion(out _)) { error = "A concluded Run cannot operate a Mana statue."; return false; }
            return true;
        }
        private void RequireAvailable(string nodeId)
        {
            var floor = _run.CurrentFloor;
            if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || floor.ActiveEncounter.HasValue ||
                floor.TryGetPendingReturn(out _) || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)) throw Invalid("Statue is not currently available.");
        }
        private void RequireCompletedNode(string nodeId)
        {
            var floor = _run.CurrentFloor;
            if (floor.GetNodeState(nodeId) != LevelNodeViewState.Completed || floor.ActiveEncounter.HasValue ||
                floor.TryGetPendingReturn(out _) || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)) throw Invalid("Completed pickup checkpoint was changed.");
        }
        private LevelInteractionReceipt RequireReceipt(string nodeId, LevelInteractionEffect effect, DungeonRunManaStatueEntry entry)
        {
            if (!_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out var receipt) ||
                receipt.Kind != LevelInteractionEffectKind.ManaStatue || receipt.Amount != ManaAmount ||
                receipt.ValueBefore != entry.currentBefore || receipt.ValueAfter != entry.currentAfter ||
                receipt.TargetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(receipt.TargetContentId) ||
                (receipt.PresentationId ?? string.Empty) != (effect.presentationId ?? string.Empty)) throw Invalid("Pickup receipt disagrees with its owner proof.");
            RequireCompletedNode(nodeId); return receipt;
        }
        private DungeonRunManaStatueEntry Find(string nodeId) => _state.entries.SingleOrDefault(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
        private DungeonRunManaStatueOffer Offer(string nodeId, LevelInteractionEffect effect, DungeonRunManaStatueEntry entry)
            => new DungeonRunManaStatueOffer(_run.CurrentFloorContentId, nodeId, effect.effectId, entry);
        internal DungeonRunManaStatueSaveData CaptureSaveData() => Clone(_state);
        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            var c = effect?.manaStatue;
            if (effect == null || effect.kind != LevelInteractionEffectKind.ManaStatue || !Stable(effect.effectId) ||
                c == null || c.version != 1 || c.sourceEventId != SourceEventId || c.requiredAcceptedBumps != RequiredAcceptedBumps || c.manaAmount != ManaAmount ||
                effect.amount != 0 || effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId) ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract || effect.HasAbandonedCartContract || effect.HasArcanaChoiceContract || effect.HasDoubleKeyPayment ||
                !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cashOutJade != 0 || effect.cardChoices?.Count > 0 ||
                effect.sealChoices?.Count > 0 || effect.HasTargetLevelResultContract || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract)
                errors.Add("ManaStatue requires its canonical 3 accepted collisions / 1 Mana typed contract only.");
        }
        private static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
            => RequireNodeEffect(floor.FindNode(nodeId));

        private static LevelInteractionEffect RequireNodeEffect(LevelNode node)
        {
            var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            var errors = new List<string>(); ValidateEffect(effect, errors);
            if (node?.nodeType != LevelNodeType.Interaction || errors.Count > 0) throw Invalid("Node is not a canonical Mana statue. " + string.Join(" ", errors));
            return effect;
        }
        internal static DungeonRunManaStatueSaveData ValidateRestored(DungeonRunSaveData data, IReadOnlyList<LevelRunSession> visited, string payload)
        {
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
            if (data.currentFloor != null) floors.Add(data.currentFloor);
            if (data.schemaVersion < 26)
            {
                if (data.hasManaStatueState || data.manaStatues?.entries?.Count > 0 ||
                    floors.Any(f => f.interactionReceipts?.Any(r => r.kind == LevelInteractionEffectKind.ManaStatue) == true)) throw Invalid("Legacy Run cannot contain statue proof.");
                return Empty();
            }
            if (!data.hasManaStatueState || data.manaStatues == null || !JsonObjectShape.TryGetField(payload,"hasManaStatueState",out var present) || present != "true" ||
                !JsonObjectShape.TryGetField(payload,"manaStatues",out var raw) || raw == "null" ||
                !JsonObjectShape.TryGetField(raw,"version",out _) || !JsonObjectShape.TryGetField(raw,"entries",out var entries) ||
                !JsonObjectShape.TryGetArrayItems(entries,out var items) || !JsonObjectShape.TryGetField(raw,"ledgerFingerprint",out _)) throw Invalid("Explicit statue wire proof is missing.");
            if (data.manaStatues.entries == null || items.Count != data.manaStatues.entries.Count) throw Invalid("Statue wire list differs.");
            if (!JsonObjectShape.TryGetField(raw,"version",out var wireVersion) || wireVersion != "1") throw Invalid("Statue wire version is invalid.");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i]; var entry = data.manaStatues.entries[i];
                if (entry == null) throw Invalid("Statue list contains null.");
                foreach (string field in new[] { "floorContentId","nodeContentId","effectId","sourceEventId" })
                    if (!JsonObjectShape.TryGetField(item,field,out var value) || value.Length < 2 || value[0] != '"' || value[value.Length-1] != '"')
                        throw Invalid("Statue identity wire field is invalid: " + field);
                if (!JsonObjectShape.TryGetField(item,"collected",out var collected) || collected != (entry.collected ? "true" : "false")) throw Invalid("Statue boolean wire field is invalid.");
                string[] names = { "acceptedBumps","currentBefore","currentAfter","baseBonusBefore","baseBonusAfter" };
                int[] values = { entry.acceptedBumps,entry.currentBefore,entry.currentAfter,entry.baseBonusBefore,entry.baseBonusAfter };
                for (int n = 0; n < names.Length; n++)
                    if (!JsonObjectShape.TryGetField(item,names[n],out var value) || !int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int actual) || actual != values[n])
                        throw Invalid("Statue numeric wire field is invalid: " + names[n]);
            }
            ValidateLocal(data.manaStatues);
            var state = Clone(data.manaStatues);
            foreach (var entry in state.entries)
            {
                var profile = visited.SingleOrDefault(p => p.Profile.contentId == entry.floorContentId) ?? throw Invalid("Statue floor was not visited.");
                var effect = RequireEffect(profile,entry.nodeContentId);
                var floor = floors.SingleOrDefault(f => f.profileContentId == entry.floorContentId) ?? throw Invalid("Statue floor checkpoint is missing.");
                var receipt = floor.interactionReceipts?.SingleOrDefault(r => r.effectId == entry.effectId);
                bool cleared = floor.nodeStates?.SingleOrDefault(n => n.nodeContentId == entry.nodeContentId)?.progress == LevelNodeProgress.Cleared;
                if (effect.effectId != entry.effectId || effect.manaStatue.sourceEventId != entry.sourceEventId || entry.collected != cleared || entry.collected != (receipt != null)) throw Invalid("Statue configuration/node/receipt disagrees.");
                if (entry.collected && (receipt.kind != LevelInteractionEffectKind.ManaStatue || receipt.amount != ManaAmount || receipt.valueBefore != entry.currentBefore ||
                    receipt.valueAfter != entry.currentAfter || receipt.targetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(receipt.targetContentId) ||
                    (receipt.presentationId ?? string.Empty) != (effect.presentationId ?? string.Empty))) throw Invalid("Statue receipt is inconsistent.");
            }
            foreach (var profile in visited)
            {
                var floor = floors.Single(f => f.profileContentId == profile.Profile.contentId);
                foreach (var node in profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)).Where(n => n.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.ManaStatue) == true))
                {
                    var effect = RequireEffect(profile,node.contentId);
                    bool collected = state.entries.Any(e => e.floorContentId == profile.Profile.contentId && e.nodeContentId == node.contentId && e.collected);
                    if (collected != (floor.interactionReceipts?.Any(r => r.effectId == effect.effectId) == true) ||
                        collected != (floor.nodeStates?.Any(n => n.nodeContentId == node.contentId && n.progress == LevelNodeProgress.Cleared) == true)) throw Invalid("Statue owner proof is missing or extra.");
                }
            }
            // Every typed receipt needs an owner decision, including orphan receipts
            // whose effect ID does not occur on any canonical statue node.
            foreach (var floor in floors)
                foreach (var receipt in floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>())
                {
                    if (receipt.kind != LevelInteractionEffectKind.ManaStatue) continue;
                    var profile = visited.Single(p => p.Profile.contentId == floor.profileContentId);
                    var nodes = profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)).Where(n => n.completionEffects?.Any(e => e?.effectId == receipt.effectId) == true).ToList();
                    if (nodes.Count != 1) throw Invalid("Orphan Mana statue receipt.");
                    var effect = RequireEffect(profile,nodes[0].contentId);
                    if (!state.entries.Any(e => e.floorContentId == profile.Profile.contentId && e.nodeContentId == nodes[0].contentId && e.effectId == effect.effectId && e.collected))
                        throw Invalid("Mana statue receipt has no collected owner proof.");
                }
            int count = state.entries.Count(e => e.collected);
            var facts = DungeonRunCardUnlockFactsSaveData.Read(data,payload).Counts;
            long mana = facts.Where(c => c.Metric == CardUnlockMetric.ManaCollected).Sum(c => (long)c.Count);
            if (data.baseManaBonus < count || mana < count || (count > 0 && !data.hasManaState)) throw Invalid("Mana bonus/world facts are below completed pickup proof.");
            return state;
        }
        private static void ValidateLocal(DungeonRunManaStatueSaveData state)
        {
            if (state.version != 1 || state.entries == null || state.ledgerFingerprint != Fingerprint(state.entries)) throw Invalid("Statue ledger shape/fingerprint is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in state.entries)
            {
                if (e == null || !Stable(e.floorContentId) || !Stable(e.nodeContentId) || !Stable(e.effectId) || e.sourceEventId != SourceEventId ||
                    !ids.Add(e.floorContentId + "/" + e.nodeContentId) || e.acceptedBumps < 1 || e.acceptedBumps > RequiredAcceptedBumps) throw Invalid("Statue collision proof is invalid.");
                if (e.collected)
                {
                    if (e.acceptedBumps != RequiredAcceptedBumps || e.currentBefore < 0 || e.currentAfter != (int)Math.Min(SourceMaximumMana,(long)e.currentBefore + ManaAmount) ||
                        e.baseBonusBefore < 0 || e.baseBonusAfter != (int)Math.Min(int.MaxValue,(long)e.baseBonusBefore + ManaAmount)) throw Invalid("Statue pickup values are invalid.");
                }
                else if (e.currentBefore != 0 || e.currentAfter != 0 || e.baseBonusBefore != 0 || e.baseBonusAfter != 0) throw Invalid("Uncollected statue contains reward values.");
            }
        }
        private static DungeonRunManaStatueSaveData Empty()
        { var state = new DungeonRunManaStatueSaveData { version = 1, entries = new List<DungeonRunManaStatueEntry>() }; state.ledgerFingerprint = Fingerprint(state.entries); return state; }
        private static DungeonRunManaStatueSaveData Clone(DungeonRunManaStatueSaveData state) => JsonUtility.FromJson<DungeonRunManaStatueSaveData>(JsonUtility.ToJson(state));
        private static string Fingerprint(List<DungeonRunManaStatueEntry> entries)
        {
            if (entries == null || entries.Any(e => e == null)) throw Invalid("Statue list contains null.");
            var text = new StringBuilder("mana-statue-v1\n");
            foreach (var e in entries) text.Append(e.floorContentId).Append('|').Append(e.nodeContentId).Append('|').Append(e.effectId).Append('|').Append(e.sourceEventId).Append('|')
                .Append(e.acceptedBumps.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.collected ? '1' : '0').Append('|')
                .Append(e.currentBefore.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.currentAfter.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(e.baseBonusBefore.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.baseBonusAfter.ToString(CultureInfo.InvariantCulture)).Append('\n');
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-",string.Empty).ToLowerInvariant();
        }
        private static bool Stable(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '|');
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Mana statue: " + message);
    }
    [Serializable]
    internal sealed class DungeonRunManaStatueSaveData
    {
        public int version;
        public string ledgerFingerprint;
        public List<DungeonRunManaStatueEntry> entries;
    }
    [Serializable]
    internal sealed class DungeonRunManaStatueEntry
    {
        public string floorContentId, nodeContentId, effectId, sourceEventId;
        public int acceptedBumps;
        public bool collected;
        public int currentBefore, currentAfter, baseBonusBefore, baseBonusAfter;
    }
}
