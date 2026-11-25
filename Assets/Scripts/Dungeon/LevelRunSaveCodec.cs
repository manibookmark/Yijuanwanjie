using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 楼层状态的存档边界。它负责验证和 DTO 转换，不执行节点规则，也不持有
    /// 当前会话；因此 LevelRunSession 仍是节点状态的唯一可变所有者。
    /// </summary>
    internal static class LevelRunSaveCodec
    {
        internal static void RequireLegacyInvitationAdmission(DungeonRunSaveData data, IReadOnlyList<LevelRunProfile> visited)
        {
            if (data.schemaVersion >= 40) return;
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>()); floors.Add(data.currentFloor);
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i] ?? throw new InvalidOperationException("Legacy invitation floor is missing.");
                if ((floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>()).Exists(r => r?.kind == LevelInteractionEffectKind.CrawlerInvitation))
                    throw new InvalidOperationException("Legacy Run contains a future invitation receipt.");
                if (!string.IsNullOrEmpty(floor.frozenNodeDirectoryJson))
                {
                    var directory = LevelRunNodeDirectoryCheckpoint.Parse(floor.frozenNodeDirectoryJson);
                    // Inspect the registered mutable DTOs instead of scanning log text or consulting current source plans.
                    if (!JsonObjectShape.TryGetField(directory.CanonicalJson, "nodes", out var rawNodes) || !JsonObjectShape.TryGetArrayItems(rawNodes, out var nodes))
                        throw new InvalidOperationException("Legacy frozen invitation nodes are missing.");
                    foreach (string node in nodes)
                    {
                        if (!JsonObjectShape.TryGetField(node, "completionEffects", out var rawEffects) || !JsonObjectShape.TryGetArrayItems(rawEffects, out var effects))
                            throw new InvalidOperationException("Legacy frozen invitation effects are missing.");
                        foreach (string effect in effects)
                            if (DungeonRunCrystalEvents.ReadFieldNames(effect).Any(name => name.Equals("crawlerInvitation", StringComparison.OrdinalIgnoreCase)) ||
                                JsonObjectShape.TryGetField(effect, "kind", out var kind) && kind == ((int)LevelInteractionEffectKind.CrawlerInvitation).ToString(System.Globalization.CultureInfo.InvariantCulture))
                                throw new InvalidOperationException("Legacy directory contains a future invitation contract.");
                    }
                }
                else
                    foreach (var node in visited[i].nodes ?? new List<LevelNode>())
                        foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                            if (effect?.kind == LevelInteractionEffectKind.CrawlerInvitation || effect?.HasCrawlerInvitationContract == true)
                                throw new InvalidOperationException("Legacy floor contains a future invitation contract.");
            }
        }
        // A Run36 has no CardStat owner. Nullable omission preserves preceding
        // directory-v1 bytes, but no future contract/kind/receipt may be admitted
        // through a legacy Run, even with an empty nullable contract shell.
        internal static void RequireLegacyCardStatAdmission(DungeonRunSaveData data,
            IReadOnlyList<LevelRunProfile> visited)
        {
            if (data.schemaVersion >= 37) return;
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
            floors.Add(data.currentFloor);
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (floor == null) throw new InvalidOperationException("Legacy floor is missing.");
                foreach (var receipt in floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>())
                    if (receipt?.kind == LevelInteractionEffectKind.CardStatOffering)
                        throw new InvalidOperationException("Legacy Run contains a future CardStat receipt.");
                if (!string.IsNullOrEmpty(floor.frozenNodeDirectoryJson))
                {
                    LevelRunNodeDirectoryCheckpoint.Parse(floor.frozenNodeDirectoryJson);
                    if (!JsonObjectShape.TryGetField(floor.frozenNodeDirectoryJson, "nodes", out string rawNodes) ||
                        !JsonObjectShape.TryGetArrayItems(rawNodes, out var nodes))
                        throw new InvalidOperationException("Legacy frozen nodes are missing.");
                    foreach (string node in nodes)
                    {
                        if (!JsonObjectShape.TryGetField(node, "completionEffects", out string rawEffects) ||
                            !JsonObjectShape.TryGetArrayItems(rawEffects, out var effects))
                            throw new InvalidOperationException("Legacy frozen effects are missing.");
                        foreach (string effect in effects)
                        {
                            foreach (string field in DungeonRunCrystalEvents.ReadFieldNames(effect))
                                if (field.Equals("cardStatOffering", StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException("Legacy directory contains a future CardStat contract.");
                            if (JsonObjectShape.TryGetField(effect, "kind", out string kind) &&
                                kind == ((int)LevelInteractionEffectKind.CardStatOffering).ToString(System.Globalization.CultureInfo.InvariantCulture))
                                throw new InvalidOperationException("Legacy directory contains a future CardStat kind.");
                        }
                    }
                }
                else
                    foreach (LevelNode node in visited[i].nodes ?? new List<LevelNode>())
                        foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                            if (effect?.kind == LevelInteractionEffectKind.CardStatOffering || effect?.HasCardStatOfferingContract == true)
                                throw new InvalidOperationException("Legacy author floor contains a future CardStat contract.");
            }
        }

        // An older Run cannot smuggle future contracts through a valid directory-v1 shell.
        internal static void RequireLegacyAdditionalEventAdmission(DungeonRunSaveData data,
            IReadOnlyList<LevelRunProfile> visited)
        {
            if (data.schemaVersion >= 39) return;
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
            floors.Add(data.currentFloor);
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i] ?? throw new InvalidOperationException("Legacy floor is missing.");
                foreach (var receipt in floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>())
                    if (receipt?.kind == LevelInteractionEffectKind.DuplicateOffering || receipt?.kind == LevelInteractionEffectKind.GunForHire)
                        throw new InvalidOperationException("Legacy Run contains a future interaction receipt.");
                if (!string.IsNullOrEmpty(floor.frozenNodeDirectoryJson))
                {
                    LevelRunNodeDirectoryCheckpoint.Parse(floor.frozenNodeDirectoryJson);
                    if (!JsonObjectShape.TryGetField(floor.frozenNodeDirectoryJson, "nodes", out string rawNodes) ||
                        !JsonObjectShape.TryGetArrayItems(rawNodes, out var nodes))
                        throw new InvalidOperationException("Legacy frozen nodes are missing.");
                    foreach (string node in nodes)
                    {
                        if (!JsonObjectShape.TryGetField(node, "completionEffects", out string rawEffects) ||
                            !JsonObjectShape.TryGetArrayItems(rawEffects, out var effects))
                            throw new InvalidOperationException("Legacy frozen effects are missing.");
                        foreach (string effect in effects)
                        {
                            foreach (string field in DungeonRunCrystalEvents.ReadFieldNames(effect))
                                if (field.Equals("duplicateOffering", StringComparison.OrdinalIgnoreCase) || field.Equals("gunForHire", StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException("Legacy directory contains a future interaction contract.");
                            if (JsonObjectShape.TryGetField(effect, "kind", out string kind) &&
                                (kind == ((int)LevelInteractionEffectKind.DuplicateOffering).ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                                 kind == ((int)LevelInteractionEffectKind.GunForHire).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                                throw new InvalidOperationException("Legacy directory contains a future interaction kind.");
                        }
                    }
                }
                else
                    foreach (LevelNode node in visited[i].nodes ?? new List<LevelNode>())
                        foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                            if (effect?.kind == LevelInteractionEffectKind.DuplicateOffering || effect?.kind == LevelInteractionEffectKind.GunForHire ||
                                effect?.HasDuplicateOfferingContract == true || effect?.HasGunForHireContract == true)
                                throw new InvalidOperationException("Legacy author floor contains a future interaction contract.");
            }
        }

        internal static LevelRunNodeDirectory RestoreDirectory(LevelRunProfile profile,
            LevelRunSaveData data, LevelRunNodeDirectory supplied = null)
        {
            if (data == null) throw new InvalidOperationException("Floor checkpoint is missing.");
            if (data.hasNodeDirectoryCheckpointState)
            {
                if (data.hasFrozenInteractionContract)
                {
                    if (string.IsNullOrEmpty(data.frozenNodeDirectoryJson))
                        throw new InvalidOperationException("Frozen floor lacks its complete directory checkpoint.");
                    var recovered = LevelRunNodeDirectoryCheckpoint.Parse(data.frozenNodeDirectoryJson).Restore(profile);
                    if (recovered.Fingerprint != data.frozenInteractionContractFingerprint)
                        throw new InvalidOperationException("Frozen directory and floor fingerprint disagree.");
                    // Prepared transitions keep the exact same directory identity;
                    // saved bytes are still fully validated before using that reference.
                    if (supplied != null)
                    {
                        supplied.RequireMatches(profile);
                        if (!supplied.HasFrozenInteractions || supplied.Fingerprint != recovered.Fingerprint)
                            throw new InvalidOperationException("Supplied directory differs from the complete checkpoint.");
                        return supplied;
                    }
                    return recovered;
                }
                if (data.frozenNodeDirectoryJson != string.Empty)
                    throw new InvalidOperationException("Author floor must explicitly carry an empty directory checkpoint.");
            }
            else if (!string.IsNullOrEmpty(data.frozenNodeDirectoryJson))
                throw new InvalidOperationException("Directory checkpoint lacks its explicit presence flag.");
            // Legacy author data retains its original compatibility path. A legacy
            // frozen marker needs an explicitly supplied complete contract; no author fallback.
            return supplied ?? LevelRunNodeDirectory.ForAuthor(profile);
        }

        internal static void RequireRunDirectoryWire(DungeonRunSaveData data, string payload)
        {
            if (data.schemaVersion < 29)
            {
                if (data.currentFloor?.hasFrozenInteractionContract == true ||
                    data.completedFloorStates?.Exists(f => f?.hasFrozenInteractionContract == true) == true)
                    throw new InvalidOperationException("Legacy Run cannot carry a frozen directory checkpoint.");
                return;
            }
            RequirePlainWireKeys(payload);
            if (!JsonObjectShape.TryGetField(payload, "currentFloor", out var current))
                throw new InvalidOperationException("Run lacks its current floor wire.");
            RequireFloorDirectoryWire(data.currentFloor, current);
            if (!JsonObjectShape.TryGetField(payload, "completedFloorStates", out var history) ||
                !JsonObjectShape.TryGetArrayItems(history, out var items) ||
                data.completedFloorStates == null || items.Count != data.completedFloorStates.Count)
                throw new InvalidOperationException("Run lacks explicit completed-floor history.");
            for (int i = 0; i < items.Count; i++) RequireFloorDirectoryWire(data.completedFloorStates[i], items[i]);
        }

        // Run v29 uses the literal DTO member names written by our serializer.
        // Check the immediate object's names before the shape scanner, which
        // intentionally ignores escaped names. Embedded JSON string values are
        // skipped whole; their own closed checkpoint parser remains authoritative.
        private static void RequirePlainWireKeys(string raw)
        {
            int depth = 0;
            for (int index = 0; index < raw.Length; index++)
            {
                char token = raw[index];
                if (token == '{' || token == '[') { depth++; continue; }
                if (token == '}' || token == ']') { depth--; continue; }
                if (token != '"') continue;
                bool escaped = false, ended = false;
                while (++index < raw.Length)
                {
                    if (raw[index] == '\\') { escaped = true; index++; continue; }
                    if (raw[index] == '"') { ended = true; break; }
                }
                if (!ended) throw new InvalidOperationException("Malformed directory wire string.");
                int next = index + 1;
                while (next < raw.Length && char.IsWhiteSpace(raw[next])) next++;
                if (depth == 1 && escaped && next < raw.Length && raw[next] == ':')
                    throw new InvalidOperationException("Run v29 directory wire requires literal unescaped member names.");
            }
        }

        private static void RequireFloorDirectoryWire(LevelRunSaveData data, string raw)
        {
            RequirePlainWireKeys(raw);
            if (data == null || !data.hasNodeDirectoryCheckpointState ||
                !JsonObjectShape.TryGetField(raw, "hasNodeDirectoryCheckpointState", out var present) || present != "true" ||
                !JsonObjectShape.TryGetField(raw, "hasFrozenInteractionContract", out var frozen) ||
                frozen != (data.hasFrozenInteractionContract ? "true" : "false") ||
                !JsonObjectShape.TryGetField(raw, "frozenInteractionContractFingerprint", out var fingerprint) ||
                fingerprint.Length < 2 || fingerprint[0] != '"' || fingerprint[fingerprint.Length - 1] != '"' ||
                !JsonObjectShape.TryGetField(raw, "frozenNodeDirectoryJson", out var directory) ||
                directory.Length < 2 || directory[0] != '"' || directory[directory.Length - 1] != '"')
                throw new InvalidOperationException("Run v29 requires explicit complete floor directory wire.");
        }

        public static LevelRunSaveData Capture(LevelRunSaveSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var data = new LevelRunSaveData
            {
                profileContentId = source.ProfileContentId,
                hasNodeDirectoryCheckpointState = true,
                frozenNodeDirectoryJson = source.FrozenNodeDirectoryJson,
                hasFrozenInteractionContract = source.HasFrozenInteractionContract,
                frozenInteractionContractFingerprint = source.FrozenInteractionContractFingerprint,
                snapshotId = source.SnapshotId,
                activeInteractionNodeContentId =
                    source.ActiveInteractionNodeContentId,
                returnConsumed = source.ReturnConsumed,
                encounterCount = source.EncounterCount,
                victoryCount = source.VictoryCount,
                victorySealRewards = source.VictorySealRewards,
                completedPresentationIds = source.CompletedPresentationIds != null
                    ? new List<string>(source.CompletedPresentationIds)
                    : new List<string>()
            };

            if (source.NodeStates != null)
            {
                foreach (KeyValuePair<string, LevelNodeProgress> item in
                         source.NodeStates)
                {
                    data.nodeStates.Add(new LevelNodeSaveData
                    {
                        nodeContentId = item.Key,
                        progress = item.Value
                    });
                }
                data.nodeStates.Sort((left, right) =>
                    string.CompareOrdinal(
                        left.nodeContentId, right.nodeContentId));
            }

            if (source.InteractionReceipts != null)
            {
                foreach (LevelInteractionReceipt receipt in
                         source.InteractionReceipts.Values)
                {
                    data.interactionReceipts.Add(
                        new LevelInteractionReceiptSaveData
                        {
                            effectId = receipt.EffectId,
                            kind = receipt.Kind,
                            targetContentId = receipt.TargetContentId,
                            presentationId = receipt.PresentationId,
                            summaryText = receipt.SummaryText,
                            detailText = receipt.DetailText,
                            rarity = receipt.Rarity,
                            amount = receipt.Amount,
                            valueBefore = receipt.ValueBefore,
                            valueAfter = receipt.ValueAfter,
                            targetRuntimeInstanceId =
                                receipt.TargetRuntimeInstanceId
                        });
                }
                data.interactionReceipts.Sort((left, right) =>
                    string.CompareOrdinal(left.effectId, right.effectId));
            }

            if (source.ActiveEncounter.HasValue)
            {
                data.hasActiveEncounter = true;
                data.activeEncounterNodeContentId =
                    source.ActiveEncounter.Value.NodeContentId;
                data.activeEncounterScenarioId =
                    source.ActiveEncounter.Value.ScenarioId;
            }
            if (source.PendingReturn.HasValue)
            {
                data.hasPendingReturn = true;
                data.pendingReturnNodeContentId =
                    source.PendingReturn.Value.NodeContentId;
                data.pendingReturnOutcome =
                    source.PendingReturn.Value.Outcome;
            }
            return data;
        }

        public static LevelRunRestoredState Restore(
            LevelRunProfile profile,
            LevelRunSaveData data,
            string runId,
            LevelRunNodeDirectory nodeDirectory = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (data == null)
                throw new InvalidOperationException(
                    "Run 存档缺少当前楼层状态。");
            nodeDirectory ??= LevelRunNodeDirectory.ForAuthor(profile);
            nodeDirectory.RequireMatches(profile);
            if (data.hasFrozenInteractionContract != nodeDirectory.HasFrozenInteractions ||
                !string.Equals(data.frozenInteractionContractFingerprint ?? string.Empty,
                    nodeDirectory.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Frozen node contracts are missing or do not match this floor checkpoint.");
            if (!string.Equals(
                    profile.contentId,
                    data.profileContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"楼层存档与配置不匹配：{data.profileContentId} / " +
                    $"{profile.contentId}。");
            }
            LevelSnapshot snapshot = profile.FindSnapshot(data.snapshotId);
            if (snapshot == null)
            {
                throw new InvalidOperationException(
                    $"楼层存档引用不存在的快照：{data.snapshotId}。");
            }

            var result = new LevelRunRestoredState
            {
                NodeStates = RestoreNodeStates(
                    profile, snapshot, data.nodeStates, nodeDirectory),
                InteractionReceipts = RestoreReceipts(
                    data.interactionReceipts, nodeDirectory),
                ActiveInteractionNodeContentId =
                    data.activeInteractionNodeContentId,
                ReturnConsumed = data.returnConsumed,
                EncounterCount = Math.Max(0, data.encounterCount),
                VictoryCount = Math.Max(0, data.victoryCount),
                VictorySealRewards =
                    LevelVictorySealRewardStateMachine.Restore(
                        data.victorySealRewards),
                CompletedPresentationIds = data.completedPresentationIds ??
                    new List<string>()
            };

            if (data.hasActiveEncounter)
            {
                ValidateResolvingNode(
                    result.NodeStates,
                    data.activeEncounterNodeContentId,
                    "活动遭遇");
                LevelNode node = nodeDirectory.FindNode(
                    data.activeEncounterNodeContentId);
                if (node == null ||
                    !string.Equals(
                        node.scenarioId,
                        data.activeEncounterScenarioId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "活动遭遇与当前节点配置不匹配。");
                }
                result.ActiveEncounter = new LevelEncounterLaunch(
                    data.activeEncounterNodeContentId,
                    data.activeEncounterScenarioId,
                    runId);
            }

            if (!string.IsNullOrWhiteSpace(
                    result.ActiveInteractionNodeContentId))
            {
                ValidateResolvingNode(
                    result.NodeStates,
                    result.ActiveInteractionNodeContentId,
                    "活动交互");
            }

            if (data.hasPendingReturn)
            {
                if (!result.NodeStates.ContainsKey(
                        data.pendingReturnNodeContentId) ||
                    (nodeDirectory.HasFrozenInteractions && !nodeDirectory.IsNodeEnabled(data.pendingReturnNodeContentId)))
                {
                    throw new InvalidOperationException(
                        "待确认返回引用不存在的节点。");
                }
                result.PendingReturn = new LevelEncounterReturn(
                    data.pendingReturnNodeContentId,
                    data.pendingReturnOutcome);
            }

            if (result.VictorySealRewards.TryGetPending(
                    out LevelVictorySealRewardOffer pendingReward))
            {
                if (pendingReward.IsCashOutOnly)
                    ValidateUnselectedCashOutReward(nodeDirectory, pendingReward.NodeContentId,
                        pendingReward.RewardContentId, allowFixedChoices: true);
                ValidateResolvingNode(
                    result.NodeStates,
                    pendingReward.NodeContentId,
                    "待处理胜利灵印奖励");
                if (!result.PendingReturn.HasValue ||
                    result.PendingReturn.Value.Outcome !=
                        CombatOutcome.Victory ||
                    !string.Equals(
                        result.PendingReturn.Value.NodeContentId,
                        pendingReward.NodeContentId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "待处理胜利灵印奖励缺少匹配的胜利返回结果。");
                }
            }
            foreach (LevelVictorySealRewardReceiptSaveData receipt in
                     data.victorySealRewards?.receipts ?? new List<LevelVictorySealRewardReceiptSaveData>())
            {
                if (nodeDirectory.HasFrozenInteractions &&
                    (receipt == null || !nodeDirectory.IsNodeEnabled(receipt.nodeContentId)))
                    throw new InvalidOperationException("A frozen victory receipt cannot reference a disabled or unknown node.");
                if (receipt.disposition == LevelVictorySealRewardDisposition.CashedOutWithoutSeal)
                    ValidateUnselectedCashOutReward(nodeDirectory, receipt.nodeContentId, receipt.rewardContentId,
                        allowFixedChoices: true);
            }
            return result;
        }

        private static void ValidateUnselectedCashOutReward(LevelRunNodeDirectory nodeDirectory,
            string nodeContentId, string rewardContentId, bool allowFixedChoices = false)
        {
            LevelNode node = nodeDirectory.FindNode(nodeContentId);
            foreach (LevelRewardGrant grant in node?.victoryRewards ?? new List<LevelRewardGrant>())
                if (grant != null && grant.contentId == rewardContentId &&
                    grant.kind == LevelRewardKind.SealChoice && (grant.allowCashOut || allowFixedChoices) &&
                    (!string.IsNullOrWhiteSpace(grant.sealRewardPoolId) ||
                     (allowFixedChoices && ((grant.sealChoiceContentIds?.Count ?? 0) > 0 ||
                         !string.IsNullOrWhiteSpace(grant.targetContentId))))) return;
            throw new InvalidOperationException("未选印化玉存档缺少允许化玉的多候选或随机灵印奖励配置。");
        }

        private static Dictionary<string, LevelNodeProgress>
            RestoreNodeStates(
                LevelRunProfile profile,
                LevelSnapshot snapshot,
                IReadOnlyList<LevelNodeSaveData> savedStates,
                LevelRunNodeDirectory nodeDirectory)
        {
            var configured = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < profile.nodes.Count; i++)
            {
                LevelNode node = profile.nodes[i];
                if (node != null)
                    configured.Add(node.contentId);
            }
            if (savedStates == null)
                throw new InvalidOperationException(
                    "楼层存档缺少节点状态。");

            // 先以当前快照补齐配置节点，再用存档中的稳定 ID
            // 覆盖旧状态。这允许内容更新追加可选节点，仍拒绝未知 ID。
            var restored = new Dictionary<string, LevelNodeProgress>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshot.nodeStates.Count; i++)
            {
                LevelNodeState configuredState = snapshot.nodeStates[i];
                if (configuredState == null ||
                    !configured.Contains(configuredState.nodeContentId) ||
                    restored.ContainsKey(configuredState.nodeContentId))
                {
                    throw new InvalidOperationException(
                        "当前楼层快照包含无效或重复节点状态。");
                }
                restored.Add(
                    configuredState.nodeContentId,
                    configuredState.progress);
            }
            if (restored.Count != configured.Count)
                throw new InvalidOperationException(
                    "当前楼层快照节点状态不完整。");

            var savedNodeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < savedStates.Count; i++)
            {
                LevelNodeSaveData saved = savedStates[i];
                if (saved == null ||
                    !configured.Contains(saved.nodeContentId) ||
                    !savedNodeIds.Add(saved.nodeContentId) ||
                    (nodeDirectory.HasFrozenInteractions &&
                     !Enum.IsDefined(typeof(LevelNodeProgress), saved.progress)))
                {
                    throw new InvalidOperationException(
                        $"楼层存档包含无效或重复节点：" +
                        $"{saved?.nodeContentId ?? "<null>"}。");
                }
                restored[saved.nodeContentId] = saved.progress;
            }
            if (savedNodeIds.Count == 0 && configured.Count > 0)
                throw new InvalidOperationException(
                    "楼层存档节点状态不完整。");
            if (nodeDirectory.HasFrozenInteractions)
            {
                if (savedNodeIds.Count != configured.Count)
                    throw new InvalidOperationException("A frozen floor checkpoint requires complete node progress.");
                foreach (LevelNode node in nodeDirectory.Nodes)
                    if (!nodeDirectory.IsNodeEnabled(node.contentId) &&
                        restored[node.contentId] != LevelNodeProgress.Locked)
                        throw new InvalidOperationException("A disabled side slot cannot have available, resolving or completed saved progress.");
            }
            return restored;
        }

        private static Dictionary<string, LevelInteractionReceipt>
            RestoreReceipts(
                IReadOnlyList<LevelInteractionReceiptSaveData> savedReceipts,
                LevelRunNodeDirectory nodeDirectory)
        {
            var restored = new Dictionary<string, LevelInteractionReceipt>(
                StringComparer.OrdinalIgnoreCase);
            if (savedReceipts == null)
                return restored;
            IReadOnlyList<LevelNode> frozenNodes = nodeDirectory.HasFrozenInteractions
                ? nodeDirectory.Nodes : null;
            for (int i = 0; i < savedReceipts.Count; i++)
            {
                LevelInteractionReceiptSaveData saved = savedReceipts[i];
                if (saved == null ||
                    string.IsNullOrWhiteSpace(saved.effectId) ||
                    restored.ContainsKey(saved.effectId))
                {
                    throw new InvalidOperationException(
                        "楼层存档包含无效或重复交互回执。");
                }
                if (nodeDirectory.HasFrozenInteractions)
                {
                    LevelInteractionEffect canonical = null;
                    foreach (LevelNode node in frozenNodes)
                    {
                        if (!nodeDirectory.IsNodeEnabled(node.contentId)) continue;
                        foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                            if (effect.effectId == saved.effectId) canonical = effect;
                    }
                    if (canonical == null || canonical.kind != saved.kind)
                        throw new InvalidOperationException("Frozen floor receipt is orphaned or differs from its effect contract.");
                    if (!string.Equals(canonical.presentationId ?? string.Empty, saved.presentationId ?? string.Empty, StringComparison.Ordinal) ||
                        !string.Equals(canonical.summaryText ?? string.Empty, saved.summaryText ?? string.Empty, StringComparison.Ordinal) ||
                        !string.Equals(canonical.detailText ?? string.Empty, saved.detailText ?? string.Empty, StringComparison.Ordinal) ||
                        canonical.rarity != saved.rarity)
                        throw new InvalidOperationException("Frozen floor receipt presentation differs from its effect contract.");
                    switch (canonical.kind)
                    {
                        case LevelInteractionEffectKind.DuplicateOffering:
                            if (!DungeonRunDuplicateOfferings.IsValidFloorSummary(saved.valueBefore, saved.valueAfter, saved.amount,
                                saved.targetRuntimeInstanceId, saved.targetContentId))
                                throw new InvalidOperationException("Frozen DuplicateOffering summary differs from its durable branch.");
                            break;
                        case LevelInteractionEffectKind.CrawlerInvitation:
                            if (!DungeonRunInvitations.IsValidFloorSummary(saved.valueBefore, saved.valueAfter, saved.amount,
                                saved.targetRuntimeInstanceId, saved.targetContentId))
                                throw new InvalidOperationException("Frozen invitation summary differs from its grant.");
                            break;
                        case LevelInteractionEffectKind.GunForHire:
                            if (!DungeonRunGunForHire.IsValidFloorSummary(saved.valueBefore, saved.valueAfter, saved.amount,
                                saved.targetRuntimeInstanceId, saved.targetContentId))
                                throw new InvalidOperationException("Frozen GunForHire summary differs from its durable branch.");
                            break;
                        case LevelInteractionEffectKind.CardStatOffering:
                            if (!DungeonRunCardStatOfferings.IsValidFloorSummary(saved.valueBefore, saved.valueAfter, saved.amount,
                                saved.targetRuntimeInstanceId, saved.targetContentId))
                                throw new InvalidOperationException("Frozen CardStat summary differs from its branch or physical target.");
                            break;
                        case LevelInteractionEffectKind.CrystalThrowing:
                            if (saved.valueBefore != 0 || saved.amount < 0 || saved.valueAfter != saved.amount ||
                                saved.targetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(saved.targetContentId))
                                throw new InvalidOperationException("Frozen Crystal summary differs from actual XP receipt.");
                            break;
                        case LevelInteractionEffectKind.AddBaseMana:
                        case LevelInteractionEffectKind.AddBaseArmor:
                        case LevelInteractionEffectKind.AddHandLimit:
                        case LevelInteractionEffectKind.AddMight:
                        case LevelInteractionEffectKind.AddArea:
                        case LevelInteractionEffectKind.AddRewardReroll:
                        case LevelInteractionEffectKind.RecordStoryClue:
                            long expectedAfter = canonical.kind == LevelInteractionEffectKind.RecordStoryClue
                                ? canonical.amount : Math.Min(int.MaxValue, (long)saved.valueBefore + canonical.amount);
                            if (saved.amount != canonical.amount || saved.valueBefore < 0 ||
                                saved.valueAfter != expectedAfter ||
                                (canonical.kind == LevelInteractionEffectKind.RecordStoryClue && saved.valueBefore != 0) ||
                                saved.targetRuntimeInstanceId != 0 ||
                                !string.Equals(saved.targetContentId ?? string.Empty, canonical.targetContentId ?? string.Empty, StringComparison.Ordinal))
                                throw new InvalidOperationException("Frozen Run modifier receipt differs from its fixed effect contract.");
                            break;
                    }
                }
                restored.Add(
                    saved.effectId,
                    new LevelInteractionReceipt(
                        saved.effectId,
                        saved.kind,
                        saved.targetContentId,
                        saved.presentationId,
                        saved.amount,
                        saved.valueBefore,
                        saved.valueAfter,
                        newlyApplied: false,
                        saved.summaryText,
                        saved.detailText,
                        saved.rarity,
                        saved.targetRuntimeInstanceId));
            }
            return restored;
        }

        private static void ValidateResolvingNode(
            IReadOnlyDictionary<string, LevelNodeProgress> states,
            string nodeContentId,
            string owner)
        {
            if (!states.TryGetValue(
                    nodeContentId, out LevelNodeProgress progress) ||
                progress != LevelNodeProgress.Resolving)
            {
                throw new InvalidOperationException(
                    $"{owner}没有对应的 Resolving 节点。");
            }
        }
    }

    internal sealed class LevelRunSaveSource
    {
        public string ProfileContentId;
        public bool HasFrozenInteractionContract;
        public string FrozenInteractionContractFingerprint;
        public string FrozenNodeDirectoryJson;
        public string SnapshotId;
        public IReadOnlyDictionary<string, LevelNodeProgress> NodeStates;
        public IReadOnlyDictionary<string, LevelInteractionReceipt>
            InteractionReceipts;
        public LevelEncounterLaunch? ActiveEncounter;
        public string ActiveInteractionNodeContentId;
        public LevelEncounterReturn? PendingReturn;
        public bool ReturnConsumed;
        public int EncounterCount;
        public int VictoryCount;
        public LevelVictorySealRewardStateSaveData VictorySealRewards;
        public IReadOnlyList<string> CompletedPresentationIds;
    }

    internal sealed class LevelRunRestoredState
    {
        public Dictionary<string, LevelNodeProgress> NodeStates;
        public Dictionary<string, LevelInteractionReceipt>
            InteractionReceipts;
        public LevelEncounterLaunch? ActiveEncounter;
        public string ActiveInteractionNodeContentId;
        public LevelEncounterReturn? PendingReturn;
        public bool ReturnConsumed;
        public int EncounterCount;
        public int VictoryCount;
        public LevelVictorySealRewardStateMachine VictorySealRewards;
        public IReadOnlyList<string> CompletedPresentationIds;
    }

    [Serializable]
    internal sealed class LevelRunSaveData
    {
        // Run v29 explicitly carries the complete frozen directory as strict
        // canonical JSON. This is a payload checkpoint, not source placement proof.
        public bool hasNodeDirectoryCheckpointState;
        public string frozenNodeDirectoryJson;
        public bool hasFrozenInteractionContract;
        public string frozenInteractionContractFingerprint;
        public string profileContentId;
        public string snapshotId;
        public List<LevelNodeSaveData> nodeStates =
            new List<LevelNodeSaveData>();
        public List<LevelInteractionReceiptSaveData> interactionReceipts =
            new List<LevelInteractionReceiptSaveData>();
        public bool hasActiveEncounter;
        public string activeEncounterNodeContentId;
        public string activeEncounterScenarioId;
        public string activeInteractionNodeContentId;
        public bool hasPendingReturn;
        public string pendingReturnNodeContentId;
        public CombatOutcome pendingReturnOutcome;
        public bool returnConsumed;
        public int encounterCount;
        public int victoryCount;
        public LevelVictorySealRewardStateSaveData victorySealRewards;
        public List<string> completedPresentationIds = new List<string>();
    }

    [Serializable]
    internal sealed class LevelNodeSaveData
    {
        public string nodeContentId;
        public LevelNodeProgress progress;
    }

    [Serializable]
    internal sealed class LevelInteractionReceiptSaveData
    {
        public string effectId;
        public LevelInteractionEffectKind kind;
        public string targetContentId;
        public string presentationId;
        public string summaryText;
        public string detailText;
        public RewardRarity rarity;
        public int amount;
        public int valueBefore;
        public int valueAfter;
        public int targetRuntimeInstanceId;
    }
}
