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
    public enum CartStage { Intact = 0, AwaitingPickup = 1, Collected = 2 }

    [Serializable]
    public sealed class LevelInteractionAbandonedCartContract
    {
        public int version;
        public string sourceEventId;
        public int requiredAcceptedBumps;
        public int drawCountBonus;
        public string pickupContentId;
        public bool IsSpecified => version != 0 || !string.IsNullOrEmpty(sourceEventId) ||
            requiredAcceptedBumps != 0 || drawCountBonus != 0 || !string.IsNullOrEmpty(pickupContentId);
    }

    public sealed class DungeonRunCartOffer
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public string PickupContentId => DungeonRunAbandonedCarts.BracerContentId;
        public int AcceptedBumps { get; }
        public CartStage Stage { get; }
        public bool CanBump => Stage == CartStage.Intact;
        public bool CanCollect => Stage == CartStage.AwaitingPickup;
        internal DungeonRunCartOffer(string floor, string node, string effect, DungeonRunCartEntry entry)
        {
            FloorContentId = floor; NodeContentId = node; EffectId = effect;
            AcceptedBumps = entry?.acceptedBumps ?? 0;
            Stage = entry?.collected == true ? CartStage.Collected :
                AcceptedBumps == DungeonRunAbandonedCarts.RequiredAcceptedBumps ? CartStage.AwaitingPickup : CartStage.Intact;
        }
    }

    /// <summary>The independent pickup decision; the floor receipt continues to prove destruction only.</summary>
    public readonly struct CartPickupReceipt
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public string PickupContentId => DungeonRunAbandonedCarts.BracerContentId;
        public int RequestedAmount => DungeonRunAbandonedCarts.DrawCountBonus;
        public int ActualAmount => ValueAfter - ValueBefore;
        public int ValueBefore { get; }
        public int ValueAfter { get; }
        public bool NewlyCollected { get; }
        internal CartPickupReceipt(DungeonRunCartEntry entry, bool newlyCollected)
        {
            FloorContentId = entry.floorContentId; NodeContentId = entry.nodeContentId;
            EffectId = entry.effectId; ValueBefore = entry.handBefore; ValueAfter = entry.handAfter;
            NewlyCollected = newlyCollected;
        }
    }

    /// <summary>
    /// Owns ordinary-cart collision revisions, destruction and the separate bracer
    /// pickup journal. The fourth accepted bump clears the floor node without
    /// granting a bonus; only pickup changes the Run-owned standard draw modifier.
    /// No timer, turn cost, account gate or AlwaysMana variant is inferred.
    /// Intermediate checkpoints, replay and rollback are project guarantees.
    /// Persist is a pure atomic save: returning publishes, throwing publishes nothing.
    /// </summary>
    public sealed class DungeonRunAbandonedCarts
    {
        public const string SourceEventId = "event.xiyou.abandoned-cart.v1";
        public const string BracerContentId = "pickup.xiyou.bracer.draw-count.v1";
        public const int RequiredAcceptedBumps = 4;
        public const int DrawCountBonus = 1;
        private readonly DungeonRunSession _run;
        private DungeonRunCartSaveData _state;
        internal bool IsCommitting { get; private set; }
        internal DungeonRunAbandonedCarts(DungeonRunSession run, DungeonRunCartSaveData saved = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = saved == null ? Empty() : Clone(saved); ValidateLocal(_state);
        }

        /// <summary>Read-only coverage survives battle, a floor transition and Run conclusion.</summary>
        public bool HasPendingPickup(string floorContentId, string nodeContentId) => _state.entries.Any(e =>
            e.floorContentId == floorContentId && e.nodeContentId == nodeContentId &&
            e.acceptedBumps == RequiredAcceptedBumps && !e.collected);

        public bool TryInspect(string nodeId, out DungeonRunCartOffer offer, out string error)
        {
            offer = null;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var entry = Find(nodeId); RequireCheckpoint(nodeId, effect, entry);
                offer = Offer(nodeId, effect, entry); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>The previous accepted count is a revision; retries cannot invent another impact.</summary>
        public bool TryBump(string nodeId, int expectedPreviousBumps,
            out DungeonRunCartOffer offer, out string error, Action persist)
        {
            offer = null;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var previous = Find(nodeId); RequireCheckpoint(nodeId, effect, previous);
                if (expectedPreviousBumps < 0 || expectedPreviousBumps >= RequiredAcceptedBumps)
                    throw Invalid("Collision revision must be between zero and three.");
                int bumps = previous?.acceptedBumps ?? 0;
                if (bumps == expectedPreviousBumps + 1)
                { offer = Offer(nodeId, effect, previous); return true; }
                if (bumps != expectedPreviousBumps || previous?.collected == true)
                    throw Invalid("Collision revision is stale or skips an accepted action.");
                var next = Clone(_state);
                var item = next.entries.SingleOrDefault(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
                if (item == null)
                {
                    item = new DungeonRunCartEntry { floorContentId = _run.CurrentFloorContentId,
                        nodeContentId = nodeId, effectId = effect.effectId, sourceEventId = SourceEventId, pickupContentId = BracerContentId };
                    next.entries.Add(item);
                }
                item.acceptedBumps++;
                Commit(next, () =>
                {
                    if (item.acceptedBumps != RequiredAcceptedBumps) return;
                    if (!_run.CurrentFloor.TryBeginInteraction(nodeId, out string failure) ||
                        !_run.CurrentFloor.TryRecordExternalEffect(nodeId, effect, RequiredAcceptedBumps - 1,
                            RequiredAcceptedBumps, 0, out _, out failure) ||
                        !_run.CurrentFloor.TryCompleteInteraction(nodeId, out failure)) throw Invalid(failure);
                }, persist, () => RequireCheckpoint(nodeId, effect, item));
                offer = Offer(nodeId, effect, Find(nodeId)); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool TryCollect(string nodeId, out CartPickupReceipt receipt, out string error, Action persist)
        {
            receipt = default;
            if (!Guard(out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var previous = Find(nodeId); RequireCheckpoint(nodeId, effect, previous);
                if (previous?.collected == true) { receipt = new CartPickupReceipt(previous, false); return true; }
                if (previous?.acceptedBumps != RequiredAcceptedBumps)
                    throw Invalid("Four accepted collisions must release the bracer before pickup.");
                var next = Clone(_state);
                var item = next.entries.Single(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
                Commit(next, () =>
                {
                    item.handBefore = _run.HandLimitBonus;
                    _run.ApplyCartDrawPickup();
                    item.handAfter = _run.HandLimitBonus; item.collected = true;
                }, persist, () => RequireCheckpoint(nodeId, effect, item));
                receipt = new CartPickupReceipt(item, true); return true;
            }
            catch (Exception ex) { receipt = default; error = ex.Message; return false; }
        }

        /// <summary>Leaving does not erase impacts or consume a pending pickup, and publishes no new save.</summary>
        public bool TryLeave(string nodeId, out string error)
        {
            if (!Guard(out error)) return false;
            try { var effect = RequireEffect(_run.CurrentFloor, nodeId); RequireCheckpoint(nodeId, effect, Find(nodeId)); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private void Commit(DungeonRunCartSaveData next, Action apply, Action persist, Action requireCheckpoint)
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
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.Crystals.HasPendingEvent) { error = "Cart transaction is already committing."; return false; }
            if (_run.Interactions.HasPendingChoice || _run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy))
            { error = "Complete the current Run choice first."; return false; }
            if (_run.TryGetConclusion(out _)) { error = "A concluded Run cannot operate an abandoned cart."; return false; }
            return true;
        }

        private void RequireCheckpoint(string nodeId, LevelInteractionEffect effect, DungeonRunCartEntry entry)
        {
            if (entry != null && (entry.floorContentId != _run.CurrentFloorContentId || entry.nodeContentId != nodeId ||
                entry.effectId != effect.effectId || entry.sourceEventId != effect.abandonedCart.sourceEventId ||
                entry.pickupContentId != effect.abandonedCart.pickupContentId))
                throw Invalid("Cart identity changed from its accepted collision checkpoint.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId))
                throw Invalid("Cart requires an idle map checkpoint.");
            bool broken = entry?.acceptedBumps == RequiredAcceptedBumps;
            if (floor.GetNodeState(nodeId) != (broken ? LevelNodeViewState.Completed : LevelNodeViewState.Available))
                throw Invalid("Cart node state disagrees with its destruction proof.");
            bool hasReceipt = floor.TryGetInteractionReceipt(effect.effectId, out var receipt);
            if (hasReceipt != broken || (broken && !MatchesBreakReceipt(receipt, effect)))
                throw Invalid("Cart break receipt is missing or inconsistent.");
            if (entry?.collected == true && _run.HandLimitBonus < entry.handAfter)
                throw Invalid("Run draw modifier is below the saved pickup proof.");
        }

        private static bool MatchesBreakReceipt(LevelInteractionReceipt r, LevelInteractionEffect effect)
            => r.Kind == LevelInteractionEffectKind.AbandonedCart && r.Amount == 0 &&
                r.ValueBefore == RequiredAcceptedBumps - 1 && r.ValueAfter == RequiredAcceptedBumps &&
                r.TargetRuntimeInstanceId == 0 && string.IsNullOrEmpty(r.TargetContentId) &&
                (r.PresentationId ?? string.Empty) == (effect.presentationId ?? string.Empty);
        private DungeonRunCartEntry Find(string nodeId) => _state.entries.SingleOrDefault(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
        private DungeonRunCartOffer Offer(string nodeId, LevelInteractionEffect effect, DungeonRunCartEntry entry)
            => new DungeonRunCartOffer(_run.CurrentFloorContentId, nodeId, effect.effectId, entry);
        internal DungeonRunCartSaveData CaptureSaveData() => Clone(_state);

        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            var c = effect?.abandonedCart;
            if (effect == null || effect.kind != LevelInteractionEffectKind.AbandonedCart || !Stable(effect.effectId) ||
                c == null || c.version != 1 || c.sourceEventId != SourceEventId || c.requiredAcceptedBumps != RequiredAcceptedBumps ||
                c.drawCountBonus != DrawCountBonus || c.pickupContentId != BracerContentId || effect.amount != 0 || effect.amountMax != 0 ||
                !string.IsNullOrEmpty(effect.targetContentId) || effect.HasManaStatueContract || effect.HasChickenOfferingContract ||
                effect.HasManaOfferingContract || effect.HasArcanaChoiceContract || effect.HasDoubleKeyPayment || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract ||
                effect.HasTargetLevelResultContract || !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cashOutJade != 0 ||
                effect.cardChoices?.Count > 0 || effect.sealChoices?.Count > 0)
                errors.Add("AbandonedCart requires only its canonical four collisions / separate bracer draw-count +1 contract.");
        }

        private static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
            => RequireNodeEffect(floor.FindNode(nodeId));

        private static LevelInteractionEffect RequireNodeEffect(LevelNode node)
        {
            var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            var errors = new List<string>(); ValidateEffect(effect, errors);
            if (node?.nodeType != LevelNodeType.Interaction || errors.Count > 0) throw Invalid("Node is not a canonical ordinary abandoned cart. " + string.Join(" ", errors));
            return effect;
        }

        internal static DungeonRunCartSaveData ValidateRestored(DungeonRunSaveData data, IReadOnlyList<LevelRunSession> visited, string payload)
        {
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
            if (data.currentFloor != null) floors.Add(data.currentFloor);
            if (data.schemaVersion < 28)
            {
                if (data.hasAbandonedCartState || data.abandonedCarts?.entries?.Count > 0 ||
                    floors.Any(f => f.interactionReceipts?.Any(r => r.kind == LevelInteractionEffectKind.AbandonedCart) == true) ||
                    visited.Any(p => p.Nodes.Where(n => p.IsNodeEnabled(n.contentId)).Any(n => n?.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.AbandonedCart) == true &&
                        floors.Single(f => f.profileContentId == p.Profile.contentId).nodeStates?.Any(s => s.nodeContentId == n.contentId && s.progress == LevelNodeProgress.Cleared) == true)))
                    throw Invalid("Legacy Run cannot contain cart destruction or pickup proof.");
                return Empty();
            }
            RequireWire(data, payload); ValidateLocal(data.abandonedCarts);
            var state = Clone(data.abandonedCarts);
            foreach (var profile in visited)
            {
                var floor = floors.Single(f => f.profileContentId == profile.Profile.contentId);
                foreach (var node in profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)).Where(n => n?.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.AbandonedCart) == true))
                {
                    var effect = RequireEffect(profile, node.contentId);
                    var entry = state.entries.SingleOrDefault(e => e.floorContentId == profile.Profile.contentId && e.nodeContentId == node.contentId);
                    var receipt = floor.interactionReceipts?.SingleOrDefault(r => r.effectId == effect.effectId);
                    bool broken = entry?.acceptedBumps == RequiredAcceptedBumps;
                    bool cleared = floor.nodeStates?.SingleOrDefault(n => n.nodeContentId == node.contentId)?.progress == LevelNodeProgress.Cleared;
                    if (entry != null && !broken &&
                        (floor.nodeStates?.SingleOrDefault(n => n.nodeContentId == node.contentId)?.progress != LevelNodeProgress.Available ||
                         floor.activeInteractionNodeContentId == node.contentId))
                        throw Invalid("A partial cart checkpoint must leave its own node available.");
                    if (broken != cleared || broken != (receipt != null)) throw Invalid("Cart destruction/node/receipt disagrees.");
                    if (receipt != null && (receipt.kind != LevelInteractionEffectKind.AbandonedCart || receipt.amount != 0 ||
                        receipt.valueBefore != RequiredAcceptedBumps - 1 || receipt.valueAfter != RequiredAcceptedBumps ||
                        receipt.targetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(receipt.targetContentId) ||
                        (receipt.presentationId ?? string.Empty) != (effect.presentationId ?? string.Empty))) throw Invalid("Cart break receipt is inconsistent.");
                    if (entry != null && (entry.effectId != effect.effectId || entry.sourceEventId != effect.abandonedCart.sourceEventId ||
                        entry.pickupContentId != effect.abandonedCart.pickupContentId)) throw Invalid("Cart identity changed from its saved contract.");
                }
                foreach (var receipt in floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>())
                    if (receipt.kind == LevelInteractionEffectKind.AbandonedCart &&
                        !profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)).Any(n => n?.completionEffects?.Any(e => e?.effectId == receipt.effectId && e.kind == LevelInteractionEffectKind.AbandonedCart) == true))
                        throw Invalid("Orphan cart break receipt.");
            }
            foreach (var entry in state.entries)
            {
                var profile = visited.SingleOrDefault(p => p.Profile.contentId == entry.floorContentId) ?? throw Invalid("Cart floor was not visited.");
                var effect = RequireEffect(profile, entry.nodeContentId);
                if (effect.effectId != entry.effectId) throw Invalid("Cart effect identity changed.");
            }
            var collected = state.entries.Where(e => e.collected).ToArray();
            if (data.handLimitBonus < 0 || collected.Any(e => data.handLimitBonus < e.handAfter) ||
                (long)data.handLimitBonus < collected.Sum(e => (long)e.handAfter - e.handBefore))
                throw Invalid("Run draw modifier is below the completed pickup proof.");
            return state;
        }

        private static void RequireWire(DungeonRunSaveData data, string payload)
        {
            if (!data.hasAbandonedCartState || data.abandonedCarts == null ||
                !JsonObjectShape.TryGetField(payload, "hasAbandonedCartState", out var present) || present != "true" ||
                !JsonObjectShape.TryGetField(payload, "abandonedCarts", out var raw) || raw == "null" ||
                !JsonObjectShape.TryGetField(raw, "version", out var version) || version != "1" ||
                !JsonObjectShape.TryGetField(raw, "entries", out var entries) || !JsonObjectShape.TryGetArrayItems(entries, out var items) ||
                !JsonObjectShape.TryGetField(raw, "ledgerFingerprint", out var fingerprint) || !WireString(fingerprint) ||
                data.abandonedCarts.entries == null || items.Count != data.abandonedCarts.entries.Count) throw Invalid("Explicit cart wire proof is missing or malformed.");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i]; var entry = data.abandonedCarts.entries[i];
                if (entry == null) throw Invalid("Cart list contains null.");
                foreach (string field in new[] { "floorContentId", "nodeContentId", "effectId", "sourceEventId", "pickupContentId" })
                    if (!JsonObjectShape.TryGetField(item, field, out var text) || !WireString(text)) throw Invalid("Cart wire identity is malformed: " + field);
                if (!JsonObjectShape.TryGetField(item, "collected", out var collected) || collected != (entry.collected ? "true" : "false")) throw Invalid("Cart wire collected flag is invalid.");
                string[] names = { "acceptedBumps", "handBefore", "handAfter" };
                int[] values = { entry.acceptedBumps, entry.handBefore, entry.handAfter };
                for (int n = 0; n < names.Length; n++)
                    if (!JsonObjectShape.TryGetField(item, names[n], out var text) || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value != values[n])
                        throw Invalid("Cart numeric wire field is invalid: " + names[n]);
            }
        }

        private static void ValidateLocal(DungeonRunCartSaveData state)
        {
            if (state.version != 1 || state.entries == null || state.ledgerFingerprint != Fingerprint(state.entries)) throw Invalid("Cart ledger shape/fingerprint is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in state.entries)
            {
                if (e == null || !Stable(e.floorContentId) || !Stable(e.nodeContentId) || !Stable(e.effectId) || e.sourceEventId != SourceEventId ||
                    e.pickupContentId != BracerContentId || !ids.Add(e.floorContentId + "|" + e.nodeContentId) ||
                    e.acceptedBumps < 1 || e.acceptedBumps > RequiredAcceptedBumps) throw Invalid("Cart collision proof is invalid.");
                if (e.collected)
                {
                    if (e.acceptedBumps != RequiredAcceptedBumps || e.handBefore < 0 ||
                        e.handAfter != (int)Math.Min(int.MaxValue, (long)e.handBefore + DrawCountBonus)) throw Invalid("Cart pickup values are invalid.");
                }
                else if (e.handBefore != 0 || e.handAfter != 0) throw Invalid("Uncollected cart contains pickup values.");
            }
        }
        private static DungeonRunCartSaveData Empty()
        { var state = new DungeonRunCartSaveData { version = 1, entries = new List<DungeonRunCartEntry>() }; state.ledgerFingerprint = Fingerprint(state.entries); return state; }
        private static DungeonRunCartSaveData Clone(DungeonRunCartSaveData state) => JsonUtility.FromJson<DungeonRunCartSaveData>(JsonUtility.ToJson(state));
        private static string Fingerprint(List<DungeonRunCartEntry> entries)
        {
            if (entries == null || entries.Any(e => e == null)) throw Invalid("Cart list contains null.");
            var text = new StringBuilder("abandoned-cart-v1\n");
            foreach (var e in entries) text.Append(e.floorContentId).Append('|').Append(e.nodeContentId).Append('|').Append(e.effectId).Append('|')
                .Append(e.sourceEventId).Append('|').Append(e.pickupContentId).Append('|').Append(e.acceptedBumps.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(e.collected ? '1' : '0').Append('|').Append(e.handBefore.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(e.handAfter.ToString(CultureInfo.InvariantCulture)).Append('\n');
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }
        private static bool WireString(string value) => value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"';
        private static bool Stable(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '|');
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Abandoned cart: " + message);
    }

    [Serializable]
    internal sealed class DungeonRunCartSaveData
    {
        public int version;
        public string ledgerFingerprint;
        public List<DungeonRunCartEntry> entries;
    }
    [Serializable]
    internal sealed class DungeonRunCartEntry
    {
        public string floorContentId, nodeContentId, effectId, sourceEventId, pickupContentId;
        public int acceptedBumps;
        public bool collected;
        public int handBefore, handAfter;
    }
}
