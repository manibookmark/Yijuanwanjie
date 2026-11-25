using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 跨场景持有当前完整 Run，并集中处理“开始、读档、战败重开、换层”。
    /// LevelRunSessionStore 只保留为旧调用点的兼容门面。
    /// </summary>
    public static class DungeonRunSessionStore
    {
        private static DungeonRunSession _current;
        private static Func<string, CommonEncounterSourceContract> _commonEncounterSources;
        private static Func<LevelRunProfile, bool> _requiresSourceAdmission;
        public static void ConfigureSideEventStartGuard(Func<LevelRunProfile, bool> requiresSourceAdmission)
            => _requiresSourceAdmission = requiresSourceAdmission;

        /// <summary>Bootstrap supplies content lookup without a Dungeon to Content dependency.
        /// Existing Runs retain the provider captured on their creation.</summary>
        public static void ConfigureCommonEncounterSources(Func<string, CommonEncounterSourceContract> sources)
            => _commonEncounterSources = sources;

        // A combat is intentionally not a normal save transaction.  The
        // checkpoint is kept in memory until the combat result reaches the
        // return boundary; this prevents a reroll (and every other Run-owned
        // combat mutation) from becoming durable merely because the scene was
        // entered or a pause action was pressed.
        private static string _combatRollbackPayload;
        private static string _combatRollbackRunId;

        public static DungeonRunSession Current => _current;

        /// <summary>
        /// True while an active map encounter owns an uncommitted combat
        /// checkpoint.  The checkpoint is deliberately not serialized as a
        /// second save format; it is an in-process transaction boundary.
        /// </summary>
        public static bool HasCombatRollbackCheckpoint =>
            !string.IsNullOrWhiteSpace(_combatRollbackPayload);

        /// <summary>
        /// Captures the complete Run immediately before the level session marks
        /// an encounter as resolving.  The caller must invoke this before
        /// <see cref="LevelRunSession.TryBeginEncounter"/>.
        /// </summary>
        public static bool TryBeginCombatRollbackCheckpoint(
            DungeonRunSession run,
            string nodeContentId,
            out string error)
        {
            error = null;
            if (run == null || !ReferenceEquals(_current, run))
            {
                error = "当前活动 Run 与战斗入口不一致。";
                return false;
            }
            if (HasCombatRollbackCheckpoint)
            {
                error = "已有未提交的战斗回滚检查点。";
                return false;
            }
            if (run.CurrentFloor == null ||
                run.CurrentFloor.ActiveEncounter.HasValue)
            {
                error = "只能为尚未开始遭遇的 Run 建立战斗检查点。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(nodeContentId))
            {
                error = "战斗节点 ID 不能为空。";
                return false;
            }
            LevelNode node = run.CurrentFloor.FindNode(nodeContentId);
            if (node == null ||
                (node.nodeType != LevelNodeType.Encounter &&
                 node.nodeType != LevelNodeType.Finale))
            {
                error = $"节点 {nodeContentId} 不是可建立战斗检查点的遭遇节点。";
                return false;
            }
            if (run.GetNodeAdmission(nodeContentId) !=
                DungeonRunNodeAdmission.Allowed ||
                run.CurrentFloor.GetNodeState(nodeContentId) !=
                LevelNodeViewState.Available)
            {
                error = $"节点 {nodeContentId} 当前不满足战斗检查点准入。";
                return false;
            }

            try
            {
                string payload = run.Serialize();
                if (string.IsNullOrWhiteSpace(payload))
                {
                    error = "无法为战斗检查点生成 Run 载荷。";
                    return false;
                }

                _combatRollbackPayload = payload;
                _combatRollbackRunId = run.RunId;
                return true;
            }
            catch (Exception exception)
            {
                error = $"建立战斗回滚检查点失败：{exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// During a combat transaction, normal save callers must persist the
        /// pre-combat payload.  This keeps scene-preload saves and economy
        /// metadata saves from accidentally turning a partial combat into a
        /// resumable checkpoint.
        /// </summary>
        public static bool TryGetCombatPersistencePayload(out string payload)
        {
            payload = HasCombatRollbackCheckpoint
                ? _combatRollbackPayload
                : _current?.Serialize();
            return !string.IsNullOrWhiteSpace(payload);
        }

        /// <summary>
        /// Returns the live Run payload for the formal "leave combat and
        /// resume" boundary.  This is intentionally separate from
        /// <see cref="TryGetCombatPersistencePayload"/>: scene construction
        /// may still need the pre-combat rollback payload, while an explicit
        /// player pause in a production build must retain the active encounter
        /// so the Hub portal can route back to Combat.
        /// </summary>
        public static bool TryGetCombatResumePayload(out string payload)
        {
            payload = null;
            DungeonRunSession run = _current;
            if (run == null || run.CurrentFloor == null ||
                !run.CurrentFloor.ActiveEncounter.HasValue)
                return false;

            try
            {
                payload = run.Serialize();
            }
            catch (Exception)
            {
                payload = null;
            }
            return !string.IsNullOrWhiteSpace(payload);
        }

        /// <summary>Commits the active combat at the normal result boundary.</summary>
        public static void CommitCombatRollbackCheckpoint()
        {
            ClearCombatRollbackCheckpoint();
        }

        /// <summary>
        /// Drops a checkpoint when encounter start never completed.  No restore
        /// is needed because the level session has not been allowed to mutate
        /// the Run beyond its own interrupted-entry cleanup.
        /// </summary>
        public static void DiscardCombatRollbackCheckpoint()
        {
            ClearCombatRollbackCheckpoint();
        }

        /// <summary>
        /// Restores the complete pre-combat Run and replaces the cross-scene
        /// host.  The resolver overload is internal so pure domain tests can
        /// use in-memory profiles without registering Unity Resources assets.
        /// </summary>
        public static bool TryRollbackCombat(
            out DungeonRunSession restored,
            out string error)
            => TryRollbackCombat(ResolveProfile, out restored, out error);

        internal static bool TryRollbackCombat(
            Func<string, LevelRunProfile> resolveProfile,
            out DungeonRunSession restored,
            out string error)
        {
            restored = null;
            error = null;
            if (!HasCombatRollbackCheckpoint)
            {
                error = "当前没有未提交的战斗回滚检查点。";
                return false;
            }
            if (_current == null ||
                !string.Equals(_current.RunId, _combatRollbackRunId,
                    StringComparison.Ordinal))
            {
                error = "活动 Run 已变化，不能套用旧战斗回滚检查点。";
                return false;
            }

            if (!DungeonRunSession.TryRestore(
                    _combatRollbackPayload,
                    resolveProfile,
                    out restored,
                    out error, commonEncounterSources: _current.CommonEncounterSources,
                    sideEventContext: _current.SideEventContext))
            {
                return false;
            }
            if (restored.CurrentFloor.ActiveEncounter.HasValue ||
                !string.Equals(
                    restored.RunId,
                    _combatRollbackRunId,
                    StringComparison.Ordinal))
            {
                restored = null;
                error = "战斗回滚载荷未恢复到同一、无活动遭遇的 Run。";
                return false;
            }

            AdoptRestored(restored);
            ClearCombatRollbackCheckpoint();
            return true;
        }

        /// <summary>
        /// Adopt a fully prepared new Run without resetting the previous Run.
        /// Admission/loadout policy belongs to Progression; this host validates
        /// the lifecycle boundary, then swaps references with no fallible work.
        /// </summary>
        public static bool TryAdoptPrepared(DungeonRunSession prepared, out string error)
        {
            error = null;
            if (prepared == null || prepared.CurrentFloor == null || !prepared.HasPlayerState ||
                prepared.RunCardCount == 0 || prepared.CurrentFloor.Profile.floorIndex > 1 ||
                !string.Equals(prepared.CurrentFloorContentId, prepared.FirstFloorContentId,
                    StringComparison.OrdinalIgnoreCase) ||
                prepared.CurrentFloor.ActiveEncounter.HasValue ||
                !string.IsNullOrWhiteSpace(prepared.CurrentFloor.ActiveInteractionNodeId) ||
                prepared.TryGetConclusion(out _))
            {
                error = "只能接管已完整装载、尚未进入战斗或交互的新 Run。";
                return false;
            }
            GameSession.Current.BindRunProgression(prepared.RunProgression);
            _current = prepared;
            ClearCombatRollbackCheckpoint();
            return true;
        }

        public static DungeonRunSession Begin(
            LevelRunProfile firstFloorProfile,
            string snapshotId,
            string runId = null,
            int? runSeed = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            DungeonRunSideEventPlan sideEventPlan = null, IDungeonRunSideEventContext sideEventContext = null)
            => Begin(
                firstFloorProfile,
                snapshotId,
                runId,
                DefaultInstanceIdGenerator.Instance,
                runSeed, commonEncounterSources, sideEventPlan, sideEventContext);

#if UNITY_EDITOR
        /// <summary>Publish a detached high-floor editor entry without round-tripping
        /// through a different global content resolver. Player builds have no such entry.</summary>
        public static bool TryAdoptQuickPlayRepresentative(DungeonRunSession prepared, out string error)
        {
            error = null;
            if (prepared?.CurrentFloor == null || prepared.CurrentFloor.Profile.floorIndex <= 1 ||
                !prepared.HasPlayerState || prepared.RunCardCount == 0 || prepared.CurrentHealth <= 0 ||
                prepared.PlayerBaselineOrigin != DungeonRunPlayerBaselineOrigin.RunEntry ||
                prepared.FirstFloorContentId != prepared.CurrentFloorContentId ||
                prepared.CaptureCompletedFloorContentIds().Count != 0 || prepared.SideEvents.HasSourcePlan ||
                prepared.CurrentFloor.ActiveEncounter.HasValue ||
                !string.IsNullOrWhiteSpace(prepared.CurrentFloor.ActiveInteractionNodeId) ||
                prepared.CurrentFloor.TryGetPendingReturn(out _) || prepared.TryGetConclusion(out _))
            {
                error = "只能接管静止、存活、有牌组和正式基线的独立高层代表入口。";
                return false;
            }
            GameSession.Current.BindRunProgression(prepared.RunProgression);
            _current = prepared;
            ClearCombatRollbackCheckpoint();
            return true;
        }
#endif

        /// <summary>
        /// Starts the globally hosted Run with a caller-owned instance ID source.
        /// Deterministic headless tooling uses this overload so serialized card
        /// identities do not depend on other Runs created in the editor process.
        /// </summary>
        public static DungeonRunSession Begin(
            LevelRunProfile firstFloorProfile,
            string snapshotId,
            string runId,
            IInstanceIdGenerator instanceIds,
            int? runSeed = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            DungeonRunSideEventPlan sideEventPlan = null, IDungeonRunSideEventContext sideEventContext = null)
        {
            if (sideEventPlan == null && (_requiresSourceAdmission?.Invoke(firstFloorProfile) ?? false))
                throw new InvalidOperationException("This formal entry requires full source admission with its actual account.");
            if (instanceIds == null)
                throw new ArgumentNullException(nameof(instanceIds));
            DungeonRunSession candidate = DungeonRunSession.Begin(
                firstFloorProfile,
                snapshotId,
                runId,
                GameSession.Current.RunProgression,
                instanceIds,
                runSeed, commonEncounterSources: commonEncounterSources ?? _commonEncounterSources,
                sideEventPlan: sideEventPlan, sideEventContext: sideEventContext);
            _current = candidate;
            ClearCombatRollbackCheckpoint();
            return _current;
        }

        public static DungeonRunSession BeginConfigured(
            string firstFloorContentId,
            string snapshotId = null,
            string runId = null,
            int? runSeed = null)
        {
            LevelRunProfile profile =
                LevelRunProfileCatalog.Get(firstFloorContentId);
            List<string> validationErrors = profile.Validate();
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"关卡 {firstFloorContentId} 配置无效：" +
                    string.Join(" | ", validationErrors));
            }
            string resolvedSnapshot = string.IsNullOrWhiteSpace(snapshotId)
                ? profile.ResolveDefaultSnapshotId()
                : snapshotId;
            if (string.IsNullOrWhiteSpace(resolvedSnapshot))
            {
                throw new InvalidOperationException(
                    $"关卡 {firstFloorContentId} 没有可用的启动快照。");
            }
            return Begin(profile, resolvedSnapshot, runId, runSeed);
        }

        public static bool TryRestore(string payload, out string error,
            Func<DungeonRunSideEventPlan, IDungeonRunSideEventContext> sideEventContexts = null)
            => TryRestore(payload, ResolveProfile, out error, sideEventContexts);

        /// <summary>Resolve and validate the entire payload in detached state.
        /// A malformed late field must not mutate the currently hosted Run.</summary>
        internal static bool TryRestore(string payload,
            Func<string, LevelRunProfile> resolveProfile, out string error,
            Func<DungeonRunSideEventPlan, IDungeonRunSideEventContext> sideEventContexts = null)
        {
            if (!DungeonRunSession.TryRestore(
                    payload,
                    resolveProfile,
                    out DungeonRunSession restored,
                    out error, commonEncounterSources: _commonEncounterSources))
            {
                return false;
            }
            if (restored.SideEvents.HasSourcePlan)
            {
                try
                {
                    if (sideEventContexts == null)
                        throw new InvalidOperationException("Restoring a source Run requires its actual admitted account context.");
                    var context = sideEventContexts(restored.SideEvents.Plan);
                    if (context == null) throw new InvalidOperationException("Saved source plan cannot fall back to an empty context.");
                    // Bind only after the entire payload and external authority validate.
                    restored.BindSideEventContext(context);
                }
                catch (Exception exception) { error = exception.Message; return false; }
            }
            AdoptRestored(restored);
            ClearCombatRollbackCheckpoint();
            return true;
        }

        /// <summary>
        /// Restores the active-encounter snapshot captured before result acceptance.
        /// Unlike a normal load or an abort, this retains the pre-combat checkpoint
        /// and the Run progression object already bound to the live CombatController.
        /// Only a successfully saved result may discard the pre-combat checkpoint.
        /// </summary>
        public static bool TryRestoreCombatSubmission(string payload, out string error)
            => TryRestoreCombatSubmission(payload, ResolveProfile, out error);

        internal static bool TryRestoreCombatSubmission(string payload,
            Func<string, LevelRunProfile> resolveProfile, out string error)
        {
            error = null;
            DungeonRunSession previous = _current;
            if (previous == null)
            {
                error = "No active Run owns the failed combat submission.";
                return false;
            }
            // Validate detached first: a valid but foreign snapshot must not rewrite
            // the progression owner that the current combat still references.
            if (!DungeonRunSession.TryRestore(payload, resolveProfile,
                    out DungeonRunSession detached, out error,
                    commonEncounterSources: previous.CommonEncounterSources,
                    sideEventContext: previous.SideEventContext)) return false;
            if (!string.Equals(detached.RunId, previous.RunId, StringComparison.Ordinal) ||
                !string.Equals(detached.CurrentFloorContentId, previous.CurrentFloorContentId, StringComparison.Ordinal) ||
                !string.Equals(detached.DungeonContentId, previous.DungeonContentId, StringComparison.Ordinal) ||
                !detached.CurrentFloor.ActiveEncounter.HasValue || detached.TryGetConclusion(out _) ||
                (HasCombatRollbackCheckpoint && !string.Equals(_combatRollbackRunId, previous.RunId, StringComparison.Ordinal)))
            {
                error = "Combat submission rollback requires the same Run, floor and an unaccepted active encounter.";
                return false;
            }
            if (previous.CurrentFloor.ActiveEncounter.HasValue &&
                !string.Equals(previous.CurrentFloor.ActiveEncounter.Value.NodeContentId,
                    detached.CurrentFloor.ActiveEncounter.Value.NodeContentId, StringComparison.Ordinal))
            {
                error = "Combat submission rollback belongs to a different encounter.";
                return false;
            }
            // The still-live CombatController will retry with its existing
            // physical cards. Preserve their original borrowed author resolver
            // only after the detached checkpoint proves the same combat.
            if (!detached.TryRetainCombatAuthorBindingsFrom(previous, out error)) return false;
            if (!DungeonRunSession.TryRestore(payload, resolveProfile, previous.RunProgression,
                    out DungeonRunSession restored, out error,
                    commonEncounterSources: previous.CommonEncounterSources,
                    sideEventContext: previous.SideEventContext)) return false;
            if (!restored.TryRetainCombatAuthorBindingsFrom(detached, out error)) return false;
            // Combat also holds the previous experience object. Result retries do
            // not re-enter gameplay, but its saved values must still be restored.
            previous.CombatProgression.Restore(detached.CombatProgression.CurrentLevel,
                detached.CombatProgression.CurrentExperience, detached.CombatProgression.XpFractionCarryUnits);
            AdoptRestored(restored);
            return true;
        }

        public static bool TryRestartAfterDefeat(
            out DungeonRunSession restarted,
            out string error)
        {
            restarted = null;
            error = null;
            if (_current == null)
            {
                error = "没有可重开的活动 Run。";
                return false;
            }
            if (!_current.TryGetConclusion(
                    out DungeonRunResultSnapshot conclusion) ||
                conclusion.Status != DungeonRunResultStatus.Defeat)
            {
                error = "只有已经冻结的战败 Run 才能重新挑战。";
                return false;
            }

            try
            {
                restarted = _current.CreateRestarted(new RunProgressionState());
                AdoptRestored(restarted);
                ClearCombatRollbackCheckpoint();
                return true;
            }
            catch (Exception exception)
            {
                error = $"战败重开 Run 失败：{exception.Message}";
                return false;
            }
        }

        public static bool CanAdvanceCurrentFloor(out string error)
        {
            if (_current == null)
            {
                error = "没有活动 Run。";
                return false;
            }
            return _current.CanAdvanceCurrentFloor(
                ResolveProfile, out _, out error);
        }

        public static bool TryAdvanceCurrentFloor(out string error)
        {
            if (_current == null)
            {
                error = "没有活动 Run。";
                return false;
            }
            return _current.TryAdvanceCurrentFloor(ResolveProfile, out error);
        }

        public static void Clear()
        {
            _current = null;
            ClearCombatRollbackCheckpoint();
        }

        private static void AdoptRestored(DungeonRunSession restored)
        {
            GameSession.Current.BindRunProgression(restored.RunProgression);
            _current = restored;
        }

        private static void ClearCombatRollbackCheckpoint()
        {
            _combatRollbackPayload = null;
            _combatRollbackRunId = null;
        }

        private static LevelRunProfile ResolveProfile(string contentId)
        {
            try
            {
                return LevelRunProfileCatalog.Get(contentId);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
