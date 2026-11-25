using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Audio;
using WanjieScroll.Effects.Map;
using WanjieScroll.Effects.Transitions;
using WanjieScroll.UI.Daozang;
using WanjieScroll.UI.Maps;
using WanjieScroll.UI.Overlays;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 正式 LevelMap 场景装配入口。
    /// 优先使用 LevelRunSessionStore.Current；仅在编辑器直接打开场景且无会话时，
    /// 首层新建 Run，高层恢复已评审的标准入口 checkpoint。
    /// 玩家构建中无会话必须报错。
    /// 规模审查：神像只增加创建、依赖和订阅；计数/余额由 Dungeon 拥有，
    /// 独立输入与反馈生命周期由神像协调器拥有，本类不扩展其业务规则。
    /// F5 拒绝修复只消费既有入口预检结果并退出失败试玩；不复制验收规则，
    /// 不创建新的入场状态或放行未验收卡牌。
    /// 地图标题仍由独立UI模块持有动画；这里只在地图节点揭示时发出Show，
    /// 并让编辑器选关面板避让标题，不扩展地图或转场业务状态。
    /// 钱包布局与余额投影归共享UI预制体；本类只注入已有跨场景钱包读取。
    /// 本次仅收紧既有调试面板与预载订阅：悬停不启动整场构建，点击准备仍归
    /// LevelMapEntryCoordinator；不向本类增加卡组、预载或奖励状态所有权。
    /// 结算改动只组装导航、严格保存和测试出口说明；准备检查点由 Dungeon
    /// 单独持有，转场身份由 Core 持有，不在此复制重试状态机。
    /// </summary>
    public sealed class LevelMapBootstrap : MonoBehaviour
    {
        private static readonly ProfilerMarker MapBuildMarker =
            new ProfilerMarker("Wanjie.LevelMap.Build");
        private const string EditorDefaultSnapshot =
            "snapshot.xiyou.gaolaozhuang.f01.start";

        [Header("编辑器默认配置（仅无会话时使用）")]
        [SerializeField] private LevelRunProfile editorDefaultProfile;
        [SerializeField] private string editorDefaultSnapshotId = EditorDefaultSnapshot;

#if UNITY_EDITOR
        [Header("编辑器调试")]
        [Tooltip("按此键强制重播卷灵入场演出（忽略只播一次规则），用于调参")]
        [SerializeField] private KeyCode debugScrollSpiritReplayKey = KeyCode.F9;
        private static string _editorLevelOverrideContentId;
        private bool _editorDirectPlaySession;
        private bool _editorLevelPickerExpanded;
        private Vector2 _editorLevelPickerScroll;
        private Vector2 _editorStagePickerScroll;
        private string _editorSelectedDungeon;
        private LevelRunProfile[] _editorLevelProfiles;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEditorLevelOverrideOnPlayStart()
        {
            _editorLevelOverrideContentId = null;
        }
#endif

        private LevelRunSession _session;
        private MapPresentation _presentation;
        private LevelMapEntryCoordinator _entryCoordinator;
        private DungeonRunSideEventLifecycleCoordinator _sideEventLifecycle;
        private CharacterPresentationCoordinator _characterPresenter;
        private CombatScenePreloader _combatPreloader;
        private LevelVictoryFlowCoordinator _victoryCoordinator;
        private DungeonRunConclusionCoordinator _conclusionCoordinator;
        private RunSummaryPanel _runSummaryPanel;
        private FlowDestinationRouter _destinationRouter;
        private LevelMapTitleOverlay _titleOverlay;
        private WanjieScroll.UI.Views.TianjiStatusView _tianjiStatusView;
        private WanjieScroll.Meta.MetaProgression _meta;
        private WanjieScroll.Persistence.GameSaveService _saveService;
        private bool _built;
        private DungeonRunSession _residentRun;
        private LevelRunSession _residentFloor;
        private bool UseResidentCombat => FloorCombatSceneOwner.IsPilot(_session) &&
            ReferenceEquals(DungeonRunSessionStore.Current?.CurrentFloor, _session);
        private Coroutine _pendingReturnRoutine;
        private string _lastMapSequenceReturnKey;

        public bool IsBuilt => _built;
        public LevelRunSession Session => _session;
        public MapPresentation Presentation => _presentation;
        public LevelMapEntryCoordinator EntryCoordinator => _entryCoordinator;
        public CharacterPresentationCoordinator CharacterPresenter =>
            _characterPresenter;
        public CombatScenePreloader CombatPreloader => _combatPreloader;
        public LevelVictoryFlowCoordinator VictoryCoordinator => _victoryCoordinator;
        public FlowDestinationRouter DestinationRouter => _destinationRouter;

        private IEnumerator Start()
        {
            var entranceClock = System.Diagnostics.Stopwatch.StartNew();
            LevelRunProfile target = LevelRunSessionStore.Current?.Profile;
#if UNITY_EDITOR
            if (target == null)
            {
                if (!string.IsNullOrWhiteSpace(_editorLevelOverrideContentId))
                    target = LevelRunProfileCatalog.Get(_editorLevelOverrideContentId);
                else
                    target = editorDefaultProfile != null ? editorDefaultProfile :
                        Resources.Load<LevelRunProfile>("Levels/gaolaozhuang-g3f1");
            }
#endif
            StageResourceWarmupRuntime warmup = StageResourceWarmupRuntime.Ensure();
            // Only a new chapter pays the resource IO cost. A new floor in the
            // same chapter reuses immutable assets without another loading cover.
            string resourceScope = target != null && !string.IsNullOrWhiteSpace(target.dungeonContentId)
                ? target.dungeonContentId : target?.contentId;
            if (target != null && (!string.IsNullOrWhiteSpace(target.dungeonContentId) ||
                StageResourceManifestCatalog.TryGet(resourceScope, out _)))
            {
                SceneTransitionRuntimeBootstrap.Ensure();
                yield return warmup.EnsureReady(resourceScope);
                if (!string.IsNullOrEmpty(warmup.Error))
                {
                    Debug.LogError("[LevelMap] " + warmup.Error);
                    yield break;
                }
            }
            double readyMs = entranceClock.Elapsed.TotalMilliseconds;
            BuildNow();
            double builtMs = entranceClock.Elapsed.TotalMilliseconds;
            if (UseResidentCombat)
            {
                warmup.BeginMapPreparation(_session.Profile.displayName);
                bool mapInputWasEnabled = _presentation.IsInteractionEnabled;
                _presentation.SetInteractionEnabled(false);
                _residentRun = DungeonRunSessionStore.Current;
                _residentFloor = _session;
                FloorCombatSceneOwner resident = FloorCombatSceneOwner.Ensure();
                yield return resident.PrepareFloor(_residentRun);
                if (!string.IsNullOrEmpty(resident.Error) || resident.State != FloorCombatScenePhase.ShellReady)
                {
                    warmup.FailMapEntrance(resident.Error ?? "楼层战斗框架未能完成准备。");
                    yield break;
                }
                _presentation.SetInteractionEnabled(mapInputWasEnabled);
                BeginMapEntryPresentation();
                StartCoroutine(NotifyMapReadyAfterFirstFrame(
                    SceneTransitionRuntimeBootstrap.Ensure()));
            }
            double shellReadyMs = entranceClock.Elapsed.TotalMilliseconds;
            yield return null;
            warmup.CompleteMapEntrance();
            Debug.Log("[MapEntryTiming] floor=" + (_session?.Profile?.contentId ?? "unknown") +
                " prepare=" + readyMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "ms build=" + (builtMs - readyMs).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "ms shell=" + (shellReadyMs - builtMs).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "ms first-frame=" + (entranceClock.Elapsed.TotalMilliseconds - shellReadyMs).ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Input.GetKeyDown(debugScrollSpiritReplayKey) &&
                _session?.Profile != null &&
                !string.IsNullOrWhiteSpace(
                    _session.Profile.entryPresentationId) &&
                _characterPresenter != null &&
                _characterPresenter.TryPlayVariant(
                    _session.Profile.entryPresentationId,
                    _session.Profile.entryPresentationVariantId,
                    ignoreRepeatRules: true))
            {
                Debug.Log("[LevelMap] 调试重播当前关卡卷灵入场演出。");
            }
        }

        private void OnGUI()
        {
            // 两级选择仍属于同一调试面板状态；选择关只改变筛选，选择层才切场。
            // 所有结构变化均推迟到布局结束，避免 IMGUI 事件布局不匹配。
            if (!_editorDirectPlaySession || _session?.Profile == null ||
                _presentation == null || !_presentation.IsInteractionEnabled ||
                (_titleOverlay != null && _titleOverlay.IsPresenting))
                return;

            EnsureEditorLevelProfiles();
            if (_editorLevelProfiles == null ||
                _editorLevelProfiles.Length == 0)
                return;

            int currentIndex = FindEditorLevelIndex(
                _session.Profile.contentId);
            string stagedPreset =
                EditorLevelMapQuickPlayRuntime.LastStagedPresetDescription;
            bool hasStagedPreset = !string.IsNullOrWhiteSpace(stagedPreset);
            bool expanded = _editorLevelPickerExpanded;
            bool toggleExpanded = false;
            string requestedDungeon = null;
            if (_editorSelectedDungeon == null)
                _editorSelectedDungeon = _session.Profile.dungeonContentId ?? string.Empty;
            LevelRunProfile requestedLevel = null;
            // 顶部居中，给右上灵玉钱包留出独立空间；两级选择按需展开。
            // 原收起态的640×180面板会覆盖地图层主，且奖励窗开启时仍浮在上面。
            const float pickerTop = 16f;
            float width = Mathf.Min(expanded ? 560f : 340f, Screen.width - 32f);
            float height = Mathf.Min(Screen.height - pickerTop - 16f,
                expanded ? 440f : 40f);
            GUILayout.BeginArea(
                new Rect((Screen.width - width) * 0.5f, pickerTop, width, height),
                expanded ? "F5 关卡快速测试" : string.Empty,
                expanded ? GUI.skin.window : GUI.skin.box);
            if (!expanded)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{EditorStageLabel(_session.Profile.dungeonContentId)} · " +
                    $"F{_session.Profile.floorIndex} {_session.Profile.displayName}");
                if (GUILayout.Button("选关", GUILayout.Width(64f), GUILayout.Height(28f)))
                    toggleExpanded = true;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(
                    $"当前：{EditorStageLabel(_session.Profile.dungeonContentId)} · " +
                    $"{_session.Profile.floorIndex} 层 · {_session.Profile.displayName}");
                if (hasStagedPreset)
                    GUILayout.Label(
                        $"预设进度档：{stagedPreset}" +
                        $"（写入槽 {EditorLevelMapQuickPlayRuntime.QuickPlayTestSlot}）");
                GUILayout.Label("切关会重建目标关前测试数据；通关后设施验收请回仙域选进度。");
                GUILayout.BeginHorizontal();
                GUI.enabled = currentIndex > 0 && SameEditorDungeon(
                    _editorLevelProfiles[currentIndex - 1], _session.Profile);
                if (GUILayout.Button("上一层", GUILayout.Height(28f)))
                    requestedLevel = _editorLevelProfiles[currentIndex - 1];
                GUI.enabled = true;
                if (GUILayout.Button("收起选关", GUILayout.Height(28f)))
                    toggleExpanded = true;
                GUI.enabled = currentIndex >= 0 &&
                    currentIndex < _editorLevelProfiles.Length - 1 && SameEditorDungeon(
                        _editorLevelProfiles[currentIndex + 1], _session.Profile);
                if (GUILayout.Button("下一层", GUILayout.Height(28f)))
                    requestedLevel = _editorLevelProfiles[currentIndex + 1];
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            if (expanded)
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(190f));
                GUILayout.Label("① 选择关");
                _editorStagePickerScroll = GUILayout.BeginScrollView(_editorStagePickerScroll);
                string previousDungeon = null;
                foreach (LevelRunProfile candidate in _editorLevelProfiles)
                {
                    string dungeon = candidate.dungeonContentId ?? string.Empty;
                    if (string.Equals(previousDungeon, dungeon, StringComparison.OrdinalIgnoreCase))
                        continue;
                    previousDungeon = dungeon;
                    bool selected = string.Equals(_editorSelectedDungeon, dungeon,
                        StringComparison.OrdinalIgnoreCase);
                    if (GUILayout.Button((selected ? "▶ " : string.Empty) +
                        EditorStageLabel(dungeon), GUILayout.Height(32f)))
                        requestedDungeon = dungeon;
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.BeginVertical();
                GUILayout.Label("② 选择层 · " + EditorStageLabel(_editorSelectedDungeon));
                _editorLevelPickerScroll = GUILayout.BeginScrollView(
                    _editorLevelPickerScroll);
                for (int i = 0; i < _editorLevelProfiles.Length; i++)
                {
                    LevelRunProfile profile = _editorLevelProfiles[i];
                    if (!string.Equals(profile.dungeonContentId ?? string.Empty,
                        _editorSelectedDungeon, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var preset = EditorLevelMapQuickPlayRuntime.ResolvePresetForLevel(profile);
                    string hint = preset.HasValue
                        ? EditorLevelMapQuickPlayRuntime.DescribePreset(preset.Value)
                        : "未配置入口预设，沿用现有测试数据";
                    GUI.enabled = i != currentIndex;
                    if (GUILayout.Button(
                            new GUIContent($"{profile.floorIndex} 层 · {profile.displayName}" +
                                (i == currentIndex ? "（当前）" : string.Empty), hint),
                            GUILayout.Height(28f)))
                        requestedLevel = profile;
                }
                GUI.enabled = true;
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                // tooltip 主要在 Repaint 更新，不能据其有无增删布局控件。
                GUILayout.Label(GUI.tooltip ?? string.Empty, GUILayout.Height(36f));
            }
            GUILayout.EndArea();
            if (requestedDungeon != null)
            {
                _editorSelectedDungeon = requestedDungeon;
                _editorLevelPickerScroll = Vector2.zero;
                GUIUtility.ExitGUI();
            }
            // 结束全部布局组后再改变结构或切场，终止旧布局的本次事件。
            if (requestedLevel != null)
            {
                _editorLevelPickerExpanded = false;
                SwitchEditorLevel(requestedLevel);
                GUIUtility.ExitGUI();
            }
            if (toggleExpanded)
            {
                _editorLevelPickerExpanded = !expanded;
                GUIUtility.ExitGUI();
            }
        }

        private void EnsureEditorLevelProfiles()
        {
            if (_editorLevelProfiles != null)
                return;
            // 列出全部关卡（教程、G1 各层、G2…），支持跨地牢直选；
            // 排序：教学、G1、G2、G3；同关内按层号，稳定 ID 用于消除歧义。
            LevelRunProfile[] allProfiles =
                Resources.LoadAll<LevelRunProfile>("Levels");
            var levels = new List<LevelRunProfile>();
            for (int i = 0; i < allProfiles.Length; i++)
            {
                LevelRunProfile candidate = allProfiles[i];
                if (candidate != null && !string.IsNullOrWhiteSpace(
                        candidate.contentId))
                    levels.Add(candidate);
            }
            _editorLevelProfiles = levels.ToArray();
            Array.Sort(
                _editorLevelProfiles,
                (left, right) =>
                {
                    int byDungeon = string.Compare(
                        EditorStageLabel(left.dungeonContentId),
                        EditorStageLabel(right.dungeonContentId),
                        StringComparison.OrdinalIgnoreCase);
                    if (byDungeon != 0) return byDungeon;
                    byDungeon = string.Compare(
                        left.dungeonContentId ?? string.Empty,
                        right.dungeonContentId ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                    if (byDungeon != 0) return byDungeon;
                    int floor = left.floorIndex.CompareTo(right.floorIndex);
                    return floor != 0
                        ? floor
                        : string.Compare(
                            left.contentId,
                            right.contentId,
                            StringComparison.OrdinalIgnoreCase);
                });
        }

        private static bool SameEditorDungeon(LevelRunProfile left, LevelRunProfile right)
        {
            return string.Equals(left.dungeonContentId ?? string.Empty,
                right.dungeonContentId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string EditorStageLabel(string dungeonId)
        {
            // 展示名只用于菜单；筛选与入口仍使用资产中的稳定 dungeonContentId。
            switch ((dungeonId ?? string.Empty).ToLowerInvariant())
            {
                case "": return "G0 · 教学关";
                case "dungeon.xiyou.shuangchaling": return "G1 · 双叉岭";
                case "dungeon.xiyou.yingchoujian": return "G2 · 鹰愁涧";
                case "dungeon.xiyou.gaolaozhuang": return "G3 · 高老庄";
                default: return dungeonId;
            }
        }

        private int FindEditorLevelIndex(string contentId)
        {
            for (int i = 0; i < _editorLevelProfiles.Length; i++)
            {
                if (string.Equals(
                        _editorLevelProfiles[i].contentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void SwitchEditorLevel(LevelRunProfile profile)
        {
            if (profile == null)
                return;
            SceneTransitionRuntimeBootstrap transition =
                SceneTransitionRuntimeBootstrap.Ensure();
            if (transition.TryReloadMap(() =>
                {
                    _editorLevelOverrideContentId = profile.contentId;
                    LevelRunSessionStore.Clear();
                    SceneContext.Clear();
                }, out string error))
                _presentation.SetInteractionEnabled(false);
            else
                Debug.LogWarning("[LevelMapBootstrap] 暂时无法切换测试层：" + error);
        }
#endif

        private void OnEnable()
        {
            // 仅在已构建后（即从战斗返回重新激活地图时）处理返回。
            // 首次加载时 _built 为 false，由 Start → BuildNow 处理。
            if (!_built) return;

            // 重置预载器，使其能为下一场战斗预载
            if (_combatPreloader != null)
                _combatPreloader.ResetForReturn();

            // 更新转场请求 ID（新的返回请求由 CombatReturnCoordinator 设置到 SceneContext）
            string returnRequestId = SceneContext.Get<string>(
                SceneContextKeys.TransitionRequestId, null);
            if (_victoryCoordinator != null &&
                !string.IsNullOrEmpty(returnRequestId))
            {
                _victoryCoordinator.UpdateTransitionRequestId(returnRequestId);
            }

            // HiddenSceneReactivateHandle reactivates the whole map root at once.
            // Defer return processing by one frame so InfinityParticleSystem
            // children finish their OnEnable/rebuild before any Emit call.
            if (_pendingReturnRoutine != null)
                StopCoroutine(_pendingReturnRoutine);
            _pendingReturnRoutine = StartCoroutine(
                HandlePendingReturnAfterReactivation());
        }

        private IEnumerator HandlePendingReturnAfterReactivation()
        {
            yield return null;
            _pendingReturnRoutine = null;
            if (isActiveAndEnabled && _built)
                HandlePendingReturn();
        }

        public void BuildNow()
        {
            if (_built) return;
            using var buildScope = MapBuildMarker.Auto();
            var buildClock = System.Diagnostics.Stopwatch.StartNew();
            double lastStageMs = 0d;
            var buildStages = new List<string>();
            void MarkStage(string name)
            {
                double now = buildClock.Elapsed.TotalMilliseconds;
                buildStages.Add(name + "=" + (now - lastStageMs).ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms");
                lastStageMs = now;
            }

            bool editorDirectPlaySession = false;
#if UNITY_EDITOR
            editorDirectPlaySession =
                EditorLevelMapQuickPlayRuntime.IsTransientSession;
#endif
            _session = LevelRunSessionStore.Current;

            if (_session == null)
            {
#if UNITY_EDITOR
                // F5/直接打开 LevelMap 的临时会话允许重复遗珍节点，且不会
                // 写入正式存档；已有正式 Run 会话不享受该豁免。
                editorDirectPlaySession = true;
                if (!string.IsNullOrWhiteSpace(
                        _editorLevelOverrideContentId))
                {
                    editorDefaultProfile = LevelRunProfileCatalog.Get(
                        _editorLevelOverrideContentId);
                }
                else if (editorDefaultProfile == null)
                    // G2 has passed the current manual acceptance round.
                    // F5 now starts G3F1 with the G2-cleared test account;
                    // other floors remain selectable without changing this default.
                    editorDefaultProfile = Resources.Load<LevelRunProfile>(
                        "Levels/gaolaozhuang-g3f1");
                if (editorDefaultProfile == null)
                    throw new InvalidOperationException(
                        "找不到默认关卡配置 Levels/gaolaozhuang-g3f1。");
                string snapshotId = string.IsNullOrWhiteSpace(
                        _editorLevelOverrideContentId)
                    ? editorDefaultSnapshotId
                    : editorDefaultProfile.ResolveDefaultSnapshotId();
                if (!EditorLevelMapQuickPlayRuntime.TryInitializeMissingRun(
                        editorDefaultProfile,
                        snapshotId,
                        out _session,
                        out string entrySource,
                        out string entryError))
                {
                    // Expected admission denial is not a broken scene. Request
                    // Play exit; the existing exit hook restores the real slot.
                    // Keep feedback on this stack so domain reload cannot lose it.
                    Debug.LogWarning("[LevelMapBootstrap] F5 入口未开放：" + entryError);
                    UnityEditor.EditorApplication.isPlaying = false;
                    if (!Application.isBatchMode)
                        UnityEditor.EditorUtility.DisplayDialog(
                            "F5 入口未开放",
                            entryError + "\n\n若提示卡牌尚未验收，请先按 F7 完成验收，再按 F5。\n关闭提示后退出本次试玩。",
                            "知道了");
                    return;
                }
                Debug.Log(
                    $"[LevelMapBootstrap] F5 入口已初始化：" +
                    $"floor={_session.Profile.contentId}, source={entrySource}");
                // F5 预设进度档：按目标关卡自动生成阶段匹配的账号（写入专用
                // 测试槽），使叠势/五行等门禁与被验收的进度点一致。
                EditorLevelMapQuickPlayRuntime.ApplyPresetAccountForLevel(
                    _session.Profile);
#else
                throw new InvalidOperationException(
                    "玩家构建中必须有活动关卡会话。");
#endif
            }

#if UNITY_EDITOR
            _editorDirectPlaySession = editorDirectPlaySession;
#endif

            MapPreviewProfile mapProfile = MapPresentationCatalog.Get(
                _session.Profile.mapPresentationContentId);
            List<string> configurationErrors =
                LevelMapConfigurationValidator.Validate(
                    _session.Profile, mapProfile);
            ValidateSealContents(_session.Profile, configurationErrors);
            ValidateCharacterPresentations(
                _session.Profile, configurationErrors);
            if (configurationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "关卡与地图联合配置无效：" + Environment.NewLine +
                    string.Join(Environment.NewLine, configurationErrors));
            }

            MarkStage("validate");
            _saveService =
                WanjieScroll.Persistence.GameSaveService.CreateDefault();
            DungeonRunSession accountRun = DungeonRunSessionStore.Current;
            if (accountRun == null || !ReferenceEquals(accountRun.CurrentFloor, _session))
                accountRun = null;
            _meta = RunAccountScope.Resolve(accountRun, _saveService.SavePath, () =>
            {
                var loaded = new WanjieScroll.Meta.MetaProgression();
                _saveService.TryLoad(loaded, out _);
                return loaded;
            });
            if (!editorDirectPlaySession)
                _session.ReconcileOwnedUniqueRelicNodes(
                    _meta.IsRelicUnlocked);

            MarkStage("meta-load");
            var initialStates = new Dictionary<string, MapVfxState>();
            var monsterTiers =
                new Dictionary<string, MapMonsterVfxTier>();
            foreach (var node in _session.Nodes)
            {
                LevelNodeViewState viewState = _session.GetNodeState(node.contentId);
                initialStates[node.contentId] =
                    MapStateFromView(viewState);
                monsterTiers[node.contentId] = ResolveMonsterVfxTier(node);
            }

            // 确保常驻转场运行时可用（编辑器直接打开 LevelMap 也有可用运行时）
            SceneTransitionRuntimeBootstrap transitionRuntime =
                SceneTransitionRuntimeBootstrap.Ensure(
                    mapProfile.transitionProfileContentId);
            transitionRuntime.SetMapCover(
                mapProfile.background,
                mapProfile.backgroundTint);

            _presentation = gameObject.AddComponent<MapPresentation>();
            _presentation.Build(mapProfile, initialStates, monsterTiers,
                LevelMapExplorationBindings.Resolve(_session, mapProfile),
                MapRewardNodeStyleResolver.Resolve(_session.Nodes, mapProfile));
            // Hand off only after the map camera actually owns a live listener.
            SceneAudioListenerContinuity.Ensure().Reconcile();
            _presentation.ExecuteRequested += OnExecuteRequested;
            // Hover只读节点与提示框。整场Combat构建仍占主线程，不能把
            // 鼠标掠过当作加载命令；正式点击入口会独立Prepare并负责转场。

            MarkStage("presentation");
            _titleOverlay = gameObject.AddComponent<LevelMapTitleOverlay>();
            LevelMapWalletView.Create(transform, () => GameSession.Current.PlayerLingyu);
            _tianjiStatusView = WanjieScroll.UI.Views.TianjiStatusView.Create(transform,
                WanjieScroll.Progression.RunCombatRules.Create(_meta, _session,
                    DungeonRunSessionStore.Current), map: true,
                growth: DungeonRunSessionStore.Current?.RunProgression.CardPlayGrowth);

            var characterPresenterObject =
                new GameObject("CharacterPresentationCoordinator");
            characterPresenterObject.transform.SetParent(transform, false);
            _characterPresenter = characterPresenterObject.AddComponent<
                CharacterPresentationCoordinator>();
            _characterPresenter.Bind(
                _session,
                _presentation,
                _meta,
                EditorLevelMapQuickPlayRuntime.CanPersist
                    ? _saveService
                    : null,
                ignoreRepeatRulesForSession: false);

            MarkStage("hud-character");
            SceneTransitionProfile transitionProfile =
                SceneTransitionProfileCatalog.Get(
                    mapProfile.transitionProfileContentId);

            // 仅正式地图登记战斗加载器；整场构建由 LevelMapEntryCoordinator
            // 在玩家确认进入后触发，不在地图或奖励空闲时推测下一战。
            if (gameObject.scene.name == SceneNames.LevelMap && !UseResidentCombat)
            {
                _combatPreloader =
                    gameObject.AddComponent<CombatScenePreloader>();
            }

            DungeonRunSession dungeonRun = DungeonRunSessionStore.Current;
            if (dungeonRun != null)
            {
                dungeonRun.ConfigureSealRewardEligibility(() =>
                    WanjieScroll.Progression.SealRewardEligibility.CanReceiveSeals(_meta));
                // Account gating is resolved in Bootstrap; Dungeon only
                // receives an immutable pool snapshot for Finale rewards.
                dungeonRun.ConfigureVictorySealRewardPools(
                    DungeonRunInteractionCatalogAdapter.CreateSealRewardPools(
                        _meta.unlockedSeals));
            }
            RunProgressionState mapRunState = dungeonRun?.RunProgression ??
                WanjieScroll.Core.GameSession.Current.RunProgression;
            var economyStats = new PlayerStats();
            DaozangConfig.ApplyToPlayerStats(_meta, economyStats);
            var jadeLedger = new LevelMapJadeRewardLedger(
                _meta,
                WanjieScroll.Core.GameSession.Current,
                mapRunState,
                () => mapRunState.ProjectGreed(economyStats.lianYu));
            var exitLedger = new LevelMapJadeRewardLedger(
                _meta, GameSession.Current, mapRunState,
                () => mapRunState.ProjectGreed(economyStats.lianYu),
                transactionSource: JadeSource.Special);
            bool CommitFloorExit(string nodeId, out DungeonRunFloorExitResult result,
                out string error)
            {
                result = null;
                error = null;
                if (dungeonRun == null || !ReferenceEquals(
                        dungeonRun, DungeonRunSessionStore.Current))
                {
                    error = "当前地图不再属于活动 Run。";
                    return false;
                }
                return dungeonRun.FloorExits.TryCommit(
                    _session.Profile.contentId, nodeId, LevelRunProfileCatalog.Get,
                    exitLedger, out result, out error, PersistActiveRunOrThrow);
            }
            SealInteractionEffectHandler sealHandler = null;
            SealAssignmentCoordinator sealAssignmentCoordinator = null;
            SealChoiceVictoryRewardCoordinator
                sealChoiceVictoryRewardCoordinator = null;
            RunCardConversionCoordinator cardConversionCoordinator = null;
            IReadOnlyList<CardConfig> runCardPool = null;
            var externalEffectHandlers =
                new List<ILevelInteractionEffectHandler>
                {
                    new RelicInteractionEffectHandler(
                        _meta,
                        replayOwnedForSession: editorDirectPlaySession)
                };
            if (dungeonRun != null)
            {
                runCardPool =
                    ContentRuntimeFactory.CreateCardPool(
                        WanjieScroll.Core.GameSession.Current.GetVolumeId());
#if UNITY_EDITOR
                // 编辑器 F5 直达 G1F2 等含 GrantSeal 节点的非首层关卡时，Run 尚未经历
                // 任何战斗，_cards 为空——TryGetCurrentCards 会失败导致印台点击
                // 报"尚未建立可钤印的牌组实例"。只在当前楼层不是 Run 的首层时
                // 播种一份调试牌组（首层如 G1F1 必须让它自己走第一场战斗用正式
                // 牌组播种，不能在这里抢跑覆盖正式起始牌组）。
                bool isFirstFloor = string.Equals(
                    dungeonRun.CurrentFloorContentId,
                    dungeonRun.FirstFloorContentId,
                    StringComparison.OrdinalIgnoreCase);
                if (!isFirstFloor &&
                    !dungeonRun.TryGetCurrentCards(
                        runCardPool, out _, out _))
                {
                    // 与正式牌组同一过滤口径：只取可用于正式对局的伤害卡，
                    // 排除法杖等未确认/UI 未就绪的测试卡。
                    var seedDeck = new List<CardConfig>();
                    for (int i = 0;
                         i < runCardPool.Count && seedDeck.Count < 6;
                         i++)
                    {
                        CardConfig candidate = runCardPool[i];
                        if (candidate != null &&
                            candidate.baseDamage > 0 &&
                            WanjieScroll.Core.CardTestStatus.CanUseInFormalPlay(
                                candidate.contentId))
                            seedDeck.Add(candidate);
                    }
                    if (seedDeck.Count > 0)
                    {
                        dungeonRun.TryPrepareCombat(
                            new PlayerStats { maxHealth = 50, currentHealth = 50 },
                            seedDeck,
                            runCardPool,
                            out _,
                            out _);
                    }
                }
#endif
                sealHandler = new SealInteractionEffectHandler(
                    dungeonRun,
                    () => runCardPool,
                    getJade: () => jadeLedger.CurrentJade,
                    previewCashOut: jadeLedger.PreviewCashOut,
                    cashOut: jadeLedger.CashOut);
                externalEffectHandlers.Add(sealHandler);
                var cardConversionHandler =
                    new RunCardConversionInteractionEffectHandler(
                        dungeonRun,
                        () => runCardPool,
                        jadeLedger,
                        persist: PersistActiveRunOrThrow);
                externalEffectHandlers.Add(cardConversionHandler);
                externalEffectHandlers.Add(
                    new DungeonRunHealingEffectHandler(dungeonRun, persist: PersistActiveRunOrThrow));
                var sealCoordinatorObject =
                    new GameObject("SealAssignmentCoordinator");
                sealCoordinatorObject.transform.SetParent(transform, false);
                sealAssignmentCoordinator =
                    sealCoordinatorObject.AddComponent<
                        SealAssignmentCoordinator>();
                sealAssignmentCoordinator.Bind(
                    sealHandler,
                    () => AudioManager.PlaySfxSafe(SfxType.SealStamp),
                    () => _presentation != null &&
                          _presentation.IsInteractionEnabled,
                    enabled => _presentation?.SetInteractionEnabled(enabled));

                var sealChoiceCoordinatorObject =
                    new GameObject("SealChoiceVictoryRewardCoordinator");
                sealChoiceCoordinatorObject.transform.SetParent(
                    transform, false);
                sealChoiceVictoryRewardCoordinator =
                    sealChoiceCoordinatorObject.AddComponent<
                        SealChoiceVictoryRewardCoordinator>();
                sealChoiceVictoryRewardCoordinator.Bind(
                    new SealChoiceVictoryRewardRuntime(
                        _session,
                        dungeonRun,
                        () => runCardPool,
                        PersistActiveRunOrThrow),
                    sealHandler,
                    sealAssignmentCoordinator,
                    jadeLedger);

                var conversionCoordinatorObject =
                    new GameObject("RunCardConversionCoordinator");
                conversionCoordinatorObject.transform.SetParent(
                    transform, false);
                cardConversionCoordinator =
                    conversionCoordinatorObject.AddComponent<
                        RunCardConversionCoordinator>();
                cardConversionCoordinator.Bind(cardConversionHandler);
            }

            var interactionEffectExecutor =
                new LevelInteractionEffectExecutor(
                    _session,
                    () => jadeLedger.CurrentJade,
                    jadeLedger.Grant,
                    externalEffectHandlers);

            // 这里只装配跨模块输入。选择生命周期由协调器拥有，规则和待选状态
            // 由 Run 事务拥有；每次动作重新取得当前成长、天运和内容准入快照。
            var runInteractionObject =
                new GameObject("DungeonRunInteractionCoordinator");
            runInteractionObject.transform.SetParent(transform, false);
            var runInteractionCoordinator = runInteractionObject.AddComponent<
                DungeonRunInteractionCoordinator>();
            Func<DungeonRunInteractionCatalog> interactionCatalog =
                () => DungeonRunInteractionCatalogAdapter.Create(
                    dungeonRun,
                    runCardPool,
                    WanjieScroll.Core.GameSession.Current.GetVolumeId(),
                    jadeLedger,
                    economyStats,
                    _meta.unlockedSeals,
                    _meta);
            runInteractionCoordinator.Bind(
                dungeonRun,
                interactionCatalog,
                sealAssignmentCoordinator,
                () => PersistAcceptedLevel(runCardPool),
                () => WanjieScroll.UI.Views.CombatSceneBootstrap.CreateCombatRules(
                    _meta, dungeonRun.CurrentFloor, dungeonRun),
                persistPayment: PersistActiveRunOrThrow,
                persistGem: () => PersistAcceptedRewardOrThrow(dungeonRun, runCardPool));
            var arcanaObject = new GameObject("DungeonRunArcanaCoordinator");
            arcanaObject.transform.SetParent(transform, false);
            var arcanaCoordinator = arcanaObject.AddComponent<DungeonRunArcanaCoordinator>();
            Func<IReadOnlyList<WanjieScroll.Meta.ArcanaConfig>> arcanaConfigs =
                () => Resources.LoadAll<WanjieScroll.Meta.ArcanaConfig>("Content/Arcanas");
            Func<DungeonRunArcanaCatalog> arcanaCatalog = () =>
            {
                ContentCatalog.Current.TryGetRelic(
                    WanjieScroll.Progression.DungeonRunArcanaCatalogResolver.LegacyAcquisitionRelicContentId,
                    out RelicData acquisitionRelic);
                return WanjieScroll.Progression.DungeonRunArcanaCatalogResolver.ResolveFromContent(
                    _meta, arcanaConfigs(), acquisitionRelic);
            };
            arcanaCoordinator.Bind(dungeonRun, arcanaCatalog, arcanaConfigs, () =>
            {
                if (!ReferenceEquals(dungeonRun, DungeonRunSessionStore.Current) ||
                    !ReferenceEquals(dungeonRun?.CurrentFloor, _session))
                    throw new InvalidOperationException("天机选择不再属于当前地图 Run。");
                PersistActiveRunOrThrow();
            });
            var chickenObject = new GameObject("DungeonRunChickenOfferingCoordinator");
            chickenObject.transform.SetParent(transform, false);
            var chickenCoordinator = chickenObject.AddComponent<DungeonRunChickenOfferingCoordinator>();
            Func<bool> canPersistOffering = () => isActiveAndEnabled && gameObject.activeInHierarchy &&
                EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService) &&
                dungeonRun != null && _meta != null && _saveService != null &&
                ReferenceEquals(dungeonRun, DungeonRunSessionStore.Current) &&
                ReferenceEquals(dungeonRun.CurrentFloor, _session);
            if (dungeonRun != null)
                chickenCoordinator.Bind(dungeonRun,
                    new WanjieScroll.Progression.DungeonRunChickenOfferingContext(dungeonRun, _meta),
                    () => runCardPool, () =>
                    {
                        if (!canPersistOffering())
                            throw new InvalidOperationException("供奉需要当前地图的正式可保存 Run。");
                        PersistActiveRunOrThrow();
                    }, canPersistOffering);

            var cardStatObject = new GameObject("DungeonRunCardStatOfferingCoordinator");
            cardStatObject.transform.SetParent(transform, false);
            var cardStatCoordinator = cardStatObject.AddComponent<DungeonRunCardStatOfferingCoordinator>();
            if (dungeonRun != null)
                cardStatCoordinator.Bind(dungeonRun, () => runCardPool, jadeLedger, () =>
                {
                    if (!canPersistOffering())
                        throw new InvalidOperationException("献卡需要当前地图的正式可保存 Run。");
                    PersistActiveRunOrThrow();
                }, canPersistOffering);

            // The map assembles shared card-facility owners. Their rules and
            // durable journals remain in Dungeon; no floor-specific UI exists.
            var rubbingObject = new GameObject("DungeonRunDuplicateOfferingCoordinator");
            rubbingObject.transform.SetParent(transform, false);
            var rubbingCoordinator = rubbingObject.AddComponent<DungeonRunDuplicateOfferingCoordinator>();
            var invitationObject = new GameObject("DungeonRunSpiritInvitationCoordinator");
            invitationObject.transform.SetParent(transform, false);
            var invitationCoordinator = invitationObject.AddComponent<DungeonRunSpiritInvitationCoordinator>();
            if (dungeonRun != null)
            {
                Action persistFacility = () =>
                {
                    if (!canPersistOffering()) throw new InvalidOperationException("卡牌设施需要当前地图的正式可保存 Run。");
                    PersistActiveRunOrThrow();
                };
                rubbingCoordinator.Bind(dungeonRun, () => runCardPool,
                    () => _meta.EventUnlocks.QueryDuplicateOfferingEligibility(), persistFacility, canPersistOffering);
                var invitationContext = new WanjieScroll.Progression.DungeonRunInvitationContext(_meta);
                invitationCoordinator.Bind(dungeonRun, () => runCardPool,
                    () => invitationContext.Prepare(runCardPool), persistFacility, canPersistOffering,
                    invitationContext.CanAdmitCard);
            }

            // Assembly only: the destructible coordinator owns its input/feedback
            // lifecycle, and Dungeon remains the sole progress/balance owner.
            Func<bool> canPersistManaStatue = () => isActiveAndEnabled && gameObject.activeInHierarchy &&
                EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService) &&
                dungeonRun != null && _meta != null && _saveService != null &&
                ReferenceEquals(dungeonRun, DungeonRunSessionStore.Current) &&
                ReferenceEquals(dungeonRun.CurrentFloor, _session);
            DungeonRunManaStatueCoordinator manaStatueCoordinator = null;
            if (dungeonRun != null)
            {
                var manaStatueObject = new GameObject("DungeonRunManaStatueCoordinator");
                manaStatueObject.transform.SetParent(transform, false);
                manaStatueCoordinator = manaStatueObject.AddComponent<DungeonRunManaStatueCoordinator>();
                manaStatueCoordinator.Bind(dungeonRun, _presentation, _combatPreloader, () =>
                {
                    if (!canPersistManaStatue())
                        throw new InvalidOperationException("破坏物需要当前地图的正式可保存 Run。");
                    PersistActiveRunOrThrow();
                }, canPersistManaStatue);
                manaStatueCoordinator.MapStateCommitted += SyncNodeStatesWithSession;
                manaStatueCoordinator.InteractionFinished += OnDurableMapActionFinished;
            }
            DungeonRunCrystalCoordinator crystalCoordinator = null;
            DungeonRunManaPlatformCoordinator manaPlatformCoordinator = null;
            if (dungeonRun != null)
            {
                var platformObject = new GameObject("DungeonRunManaPlatformCoordinator");
                platformObject.transform.SetParent(transform, false);
                manaPlatformCoordinator = platformObject.AddComponent<DungeonRunManaPlatformCoordinator>();
                manaPlatformCoordinator.Bind(dungeonRun, _presentation, _combatPreloader,
                    PersistActiveRunOrThrow, canPersistManaStatue);
                manaPlatformCoordinator.MapStateCommitted += SyncNodeStatesWithSession;
                manaPlatformCoordinator.InteractionFinished += OnDurableMapActionFinished;
            }
            if (dungeonRun != null)
            {
                var crystalObject = new GameObject("DungeonRunCrystalCoordinator");
                crystalObject.transform.SetParent(transform, false);
                crystalCoordinator = crystalObject.AddComponent<DungeonRunCrystalCoordinator>();
                crystalCoordinator.Bind(dungeonRun, _presentation, _combatPreloader,
                    runInteractionCoordinator, interactionCatalog, () =>
                    {
                        if (!canPersistManaStatue())
                            throw new InvalidOperationException("晶体需要当前地图的正式可保存 Run。");
                        PersistActiveRunOrThrow();
                    }, () =>
                    {
                        if (!canPersistManaStatue())
                            throw new InvalidOperationException("晶体升级奖励需要当前地图的正式可保存 Run。");
                        PersistAcceptedRewardOrThrow(dungeonRun, runCardPool);
                    }, canPersistManaStatue);
                crystalCoordinator.MapStateCommitted += SyncNodeStatesWithSession;
                crystalCoordinator.InteractionFinished += OnDurableMapActionFinished;
            }
            var interactionPreviewObject = new GameObject("MapInteractionPreviewCoordinator");
            interactionPreviewObject.transform.SetParent(transform, false);
            interactionPreviewObject.AddComponent<MapInteractionPreviewCoordinator>()
                .Bind(_presentation, dungeonRun);

            var entryCoordinatorObject =
                new GameObject("LevelMapEntryCoordinator");
            entryCoordinatorObject.transform.SetParent(transform, false);
            _entryCoordinator =
                entryCoordinatorObject.AddComponent<LevelMapEntryCoordinator>();
            _entryCoordinator.Bind(
                _session,
                _presentation,
                transitionProfile,
                _combatPreloader,
                interactionEffectExecutor,
                _characterPresenter,
                sealAssignmentCoordinator,
                cardConversionCoordinator,
                runInteractionCoordinator,
                CommitFloorExit);
            _entryCoordinator.BindArcana(arcanaCoordinator);
            _entryCoordinator.BindChickenOffering(chickenCoordinator);
            _entryCoordinator.BindCardStatOffering(cardStatCoordinator);
            _entryCoordinator.BindCardFacilities(rubbingCoordinator, invitationCoordinator);
            _entryCoordinator.BindManaStatue(manaStatueCoordinator);
            _entryCoordinator.BindManaPlatform(manaPlatformCoordinator);
            _entryCoordinator.BindCrystal(crystalCoordinator);
            _entryCoordinator.InteractionCompleted +=
                OnInteractionCompleted;
            _entryCoordinator.DurableInteractionCompleted += OnDurableMapActionFinished;
            _entryCoordinator.EncounterStarted += OnEncounterStarted;
            _entryCoordinator.InteractionStarted += OnInteractionCheckpoint;
            _entryCoordinator.InteractionEffectsCommitted +=
                OnInteractionCheckpoint;
            _entryCoordinator.InteractionCancelled +=
                OnInteractionCheckpoint;
            _entryCoordinator.ExitCompleted += OnExitCompleted;

            MarkStage("coordinators");
            // 整局终局在角色奖励演出后进入共用 Results；只有玩家明确选择离场
            // 才清空活动 Run 并返回神域。
            _destinationRouter = new FlowDestinationRouter();
            _destinationRouter.Register(
                FlowDestinationIds.MetaHub,
                RouteToHub);

            if (dungeonRun != null)
            {
                _runSummaryPanel = RunSummaryPanel.CreateHosted(
                    transform, runCardPool);
                var conclusionGo = new GameObject(
                    "DungeonRunConclusionCoordinator");
                conclusionGo.transform.SetParent(transform, false);
                _conclusionCoordinator = conclusionGo.AddComponent<
                    DungeonRunConclusionCoordinator>();
                // Independent floor tests keep their real partial history. The
                // formal continuation gate is unchanged; do not offer an action
                // that this diagnostic entry cannot lawfully complete.
                LevelRunProfile firstProfile = LevelRunProfileCatalog.Get(dungeonRun.FirstFloorContentId);
                bool independentFloorTest = EditorLevelMapQuickPlayRuntime.IsTransientSession &&
                    firstProfile.floorIndex > 1;
                _conclusionCoordinator.Bind(
                    dungeonRun,
                    _runSummaryPanel,
                    _destinationRouter,
                    () =>
                    {
                        if (EditorLevelMapQuickPlayRuntime.CanPersist)
                            PersistActiveRunOrThrow();
                    },
                    enabled =>
                    {
                        _presentation?.SetInteractionEnabled(enabled);
                        if (enabled)
                            _titleOverlay?.Show(_session.Profile.displayName);
                        else
                            _titleOverlay?.Close();
                    },
                    TryRestartFromConclusion,
                    independentFloorTest ||
                    string.IsNullOrWhiteSpace(_session.Profile.victoryNextRunEntryContentId)
                        ? null : new DungeonRunContinueHandler(TryContinueToNextDungeon),
                    independentFloorTest
                        ? "当前为独立楼层测试；继续测试请使用楼层切换。"
                        : null);
            }

            // Create reward service (needs MetaProgression + save delegate)
            var rewardService = new LevelRewardGrantService(
                _meta,
                m =>
                {
                    if (EditorLevelMapQuickPlayRuntime.CanPersist)
                        _saveService.Save(m);
                },
                saveRun: (m, payload) =>
                {
                    if (!EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService))
                        throw new InvalidOperationException("当前存档绑定不能提交终局奖励事务。");
                    _saveService.SaveTransactional(m, payload);
                },
                gameSession: WanjieScroll.Core.GameSession.Current);

            MarkStage("summary");
            // Create victory flow coordinator
            var victoryGo = new GameObject("LevelVictoryFlowCoordinator");
            victoryGo.transform.SetParent(transform, false);
            _victoryCoordinator = victoryGo.AddComponent<LevelVictoryFlowCoordinator>();

            // Read the transition request ID from SceneContext (set by CombatReturnCoordinator)
            string returnRequestId = SceneContext.Get<string>(
                SceneContextKeys.TransitionRequestId, null);

            _victoryCoordinator.Bind(
                _session,
                _presentation,
                rewardService,
                returnRequestId,
                _characterPresenter,
                dungeonRun,
                sealChoiceVictoryRewardCoordinator,
                _conclusionCoordinator);
            _victoryCoordinator.VictoryPresentationCompleted +=
                OnVictoryPresentationCompleted;

            MarkStage("victory");
            // 公共入口统一装配，所有关卡配置自动获得同一套 ESC 行为。
            var pauseMenu = WanjieScroll.UI.Views.CombatPauseMenu.Create(transform);
            pauseMenu.ReturnToHubRequested += () =>
            {
                PersistActiveRun();
                pauseMenu.Close();
                SceneContext.Clear();
                SceneLoader.GoToHub();
            };
            pauseMenu.QuitRequested += () =>
            {
                PersistActiveRun();
                pauseMenu.Close();
                WanjieScroll.UI.Views.CombatPauseMenu.RequestApplicationQuit();
            };
            MarkStage("pause");
            _built = true;
            // Project independently saved pickups as well as the node progress;
            // this includes a repaired platform restored before its qi is taken.
            SyncNodeStatesWithSession();
            if (dungeonRun?.SideEvents.HasSourcePlan == true)
            {
                dungeonRun.BindSideEventContext(WanjieScroll.Progression.DungeonSideEventProductionResolver
                    .CreateRestoreContext(dungeonRun.SideEvents.Plan, _meta));
                _sideEventLifecycle = gameObject.AddComponent<DungeonRunSideEventLifecycleCoordinator>();
                _sideEventLifecycle.Bind(dungeonRun, _meta, _saveService,
                    () => EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService),
                    ReloadLevelMapAfterConclusion,
                    error => { _presentation.SetInteractionEnabled(false); Debug.LogError("[LevelMap] 来源阶段未能提交：" + error); },
                    _entryCoordinator, _victoryCoordinator);
            }

            if (!UseResidentCombat)
                BeginMapEntryPresentation();
            MarkStage("entry");

            Debug.Log(
                $"[LevelMapBootstrap] 地图已构建，会话 RunId={_session.RunId}。");
            Debug.Log("[MapBuildTiming] floor=" +
                (_session?.Profile?.contentId ?? "unknown") +
                " total=" + buildClock.Elapsed.TotalMilliseconds.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms " +
                string.Join(" ", buildStages));
            if (!UseResidentCombat)
            {
                // 组件未激活时协程不会启动；此时退回立即握手，避免转场空等超时。
                if (isActiveAndEnabled)
                    StartCoroutine(NotifyMapReadyAfterFirstFrame(transitionRuntime));
                else
                    transitionRuntime.NotifyMapBuilt();
            }
        }

        /// <summary>
        /// 遮幕仍盖着时，先让新地图完成排版并渲染一帧，再告诉转场"可以揭幕"。
        /// 直接在 BuildNow 尾部揭幕会把 Canvas 首帧排版、字形图集与粒子启动
        /// 全部压进撕纸动画那一帧，玩家看到的就是"切过去卡一下"。
        /// </summary>
        private IEnumerator NotifyMapReadyAfterFirstFrame(
            SceneTransitionRuntimeBootstrap transition)
        {
            yield return null;
            if (transition == null) yield break;
            Canvas.ForceUpdateCanvases();
            yield return null;
            transition.NotifyMapBuilt();
        }

        private void BeginMapEntryPresentation()
        {
            DungeonRunSession dungeonRun = DungeonRunSessionStore.Current;
            bool hasPendingReturn =
                _session.TryGetPendingReturn(out _);
            bool hasFrozenVictory = dungeonRun != null &&
                dungeonRun.TryGetConclusion(out DungeonRunResultSnapshot frozen) &&
                frozen.Status == DungeonRunResultStatus.Victory;
            bool playOpeningDialogue = true;
#if UNITY_EDITOR
            // F5 反复验收时跳过卷灵开场；正式入场与 F9 手动重播保持原流程。
            playOpeningDialogue = !_editorDirectPlaySession;
#endif
            if (playOpeningDialogue && !hasPendingReturn && !hasFrozenVictory &&
                !string.IsNullOrWhiteSpace(
                    _session.Profile.entryPresentationId))
            {
                // 配置可要求聚焦卷灵首句：演出期间收起全部节点，退场后按会话恢复。
                if (_session.Profile.hideMapNodesDuringEntryPresentation &&
                    _presentation != null)
                {
                    foreach (var node in _session.Nodes)
                    {
                        if (node == null) continue;
                        _presentation.SetGroupState(
                            node.contentId, MapVfxState.Hidden, immediate: true);
                    }
                }

                bool presentationStarted = _characterPresenter.TryPlayVariant(
                    _session.Profile.entryPresentationId,
                    _session.Profile.entryPresentationVariantId,
                    () =>
                    {
                        if (_session == null || _presentation == null)
                            return;
                        SyncNodeStatesWithSession();
                        ReplayMapEntrySequences();
                    });
                if (!presentationStarted &&
                    _session.Profile.hideMapNodesDuringEntryPresentation)
                {
                    // 演出未实际开播（已播过/配置缺失）时立刻恢复节点。
                    SyncNodeStatesWithSession();
                }
                if (!presentationStarted)
                    ReplayMapEntrySequences();
            }
            else if (!hasPendingReturn && !hasFrozenVictory)
            {
                ReplayMapEntrySequences();
            }

            // Handle pending return from previous battle
            HandlePendingReturn();
            if (!hasFrozenVictory && !string.IsNullOrWhiteSpace(
                    _session.ActiveInteractionNodeId))
            {
                if (dungeonRun != null && dungeonRun.CardConversions.HasPendingManaPickup(
                    dungeonRun.CurrentFloorContentId, _session.ActiveInteractionNodeId))
                    SyncNodeStatesWithSession(); // Legacy pending qi is resumed by its coordinator.
                else
                    TryExecuteNode(_session.ActiveInteractionNodeId);
            }

        }

        /// <summary>尝试执行某个节点（进入战斗）。委托给协调器。</summary>
        public bool TryExecuteNode(string nodeContentId)
        {
            if (!_built)
                BuildNow();
            if (_entryCoordinator == null) return false;
            if (_sideEventLifecycle != null && !_sideEventLifecycle.TryPrepareMapInput()) return false;
            return _entryCoordinator.TryEnterEncounter(nodeContentId);
        }

        private void OnExecuteRequested(string groupId)
        {
#if UNITY_EDITOR
            // IMGUI不参与uGUI射线：选关展开时不让点击穿透到地图节点。
            if (_editorDirectPlaySession && _editorLevelPickerExpanded)
                return;
#endif
            TryExecuteNode(groupId);
        }

        private void OnVictoryPresentationCompleted()
        {
            SyncNodeStatesWithSession();
            PersistActiveRun();
        }

        private void OnInteractionCompleted(
            IReadOnlyList<LevelInteractionReceipt> receipts)
        {
            DungeonRunSession run = DungeonRunSessionStore.Current;
            _tianjiStatusView?.RefreshRules(
                WanjieScroll.Progression.RunCombatRules.Create(_meta, _session, run),
                run?.RunProgression.CardPlayGrowth);
            PersistActiveRun();
        }

        private void OnDurableMapActionFinished()
        {
            // The owner has already saved this action. Feedback completion only
            // refreshes projections and releases pending map input. Combat
            // construction waits for a confirmed encounter entry.
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || _presentation == null ||
                !_presentation.isActiveAndEnabled || _session == null || run == null ||
                !ReferenceEquals(run.CurrentFloor, _session))
                return;
            _tianjiStatusView?.RefreshRules(
                WanjieScroll.Progression.RunCombatRules.Create(_meta, _session, run),
                run.RunProgression.CardPlayGrowth);
            if (_sideEventLifecycle != null && !_sideEventLifecycle.TryPrepareMapInput()) return;
        }

        private void OnEncounterStarted(LevelEncounterLaunch launch)
        {
            PersistActiveRun();
        }

        private void OnInteractionCheckpoint(string nodeContentId)
        {
            PersistActiveRun();
        }

        private void OnExitCompleted(string nodeContentId)
        {
            // The authoritative transaction has already saved and replaced
            // CurrentFloor. Never execute a second payout/advance here.
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null || ReferenceEquals(run.CurrentFloor, _session))
            {
                Debug.LogError(
                    $"[LevelMapBootstrap] 离层反馈缺少已提交的换层结果：{nodeContentId}");
                return;
            }

            if (!SceneTransitionRuntimeBootstrap.Ensure().TryReloadMap(
                    SceneContext.Clear, out string error))
                Debug.LogError("[LevelMapBootstrap] 无法加载下一层地图：" + error);
        }

        private void OnDestroy()
        {
            FloorCombatSceneOwner.Current?.ReleaseFloorScope(_residentRun, _residentFloor);
            if (_presentation != null)
            {
                _presentation.ExecuteRequested -= OnExecuteRequested;
            }
            if (_entryCoordinator != null)
                _entryCoordinator.InteractionCompleted -=
                    OnInteractionCompleted;
            if (_entryCoordinator != null)
            {
                _entryCoordinator.DurableInteractionCompleted -= OnDurableMapActionFinished;
                _entryCoordinator.EncounterStarted -= OnEncounterStarted;
                _entryCoordinator.InteractionStarted -=
                    OnInteractionCheckpoint;
                _entryCoordinator.InteractionEffectsCommitted -=
                    OnInteractionCheckpoint;
                _entryCoordinator.InteractionCancelled -=
                    OnInteractionCheckpoint;
                _entryCoordinator.ExitCompleted -= OnExitCompleted;
            }
            if (_victoryCoordinator != null)
            {
                _victoryCoordinator.VictoryPresentationCompleted -=
                    OnVictoryPresentationCompleted;
            }
            _characterPresenter?.CloseSilently();
            _runSummaryPanel?.Close();
            _titleOverlay?.Close();
        }

        private bool TryContinueToNextDungeon(out string error)
        {
            DungeonRunSession previous = DungeonRunSessionStore.Current;
            if (previous == null || !ReferenceEquals(previous.CurrentFloor, _session))
            {
                error = "当前探索已改变，请返回神域后重新进入。";
                return false;
            }
            if (!WanjieScroll.Progression.RunStartService.TryPrepareVictoryContinuation(
                    previous, _meta, GameSession.Current.SelectedCrawlerId,
                    DaozangConfig.ApplyToPlayerStats, out DungeonRunSession prepared, out string detail))
            {
                Debug.LogError("[LevelMapBootstrap] 下一关准入失败：" + detail);
                error = "下一关尚未满足进入条件，请返回神域查看解锁状态。";
                return false;
            }
            // A failed save leaves the G2 conclusion intact and retryable.
            // Prepared comes from the same formal admission used by the Hub.
            try
            {
                if (EditorLevelMapQuickPlayRuntime.CanPersist)
                    _saveService.SaveTransactional(_meta, prepared.Serialize());
                else if (!EditorLevelMapQuickPlayRuntime.TrySaveContinuationAccount(
                             _meta, prepared.Serialize(), out detail))
                    throw new InvalidOperationException(detail);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "进度未能保存，请稍后重试。";
                return false;
            }
            if (!DungeonRunSessionStore.TryAdoptPrepared(prepared, out detail))
            {
                Debug.LogError("[LevelMapBootstrap] 下一关接管失败：" + detail);
                error = "探索尚未准备完成，请返回神域后重试。";
                return false;
            }
            SceneContext.Clear();
            error = null;
            return true;
        }

        private bool TryRestartFromConclusion(out string error)
        {
            bool restarted = EditorLevelMapQuickPlayRuntime.IsTransientSession
                ? DungeonRunSessionStore.TryRestartAfterDefeat(out _, out string detail)
                : WanjieScroll.Progression.RunStartService.TryRestartActive(
                    _meta, GameSession.Current.SelectedCrawlerId,
                    DaozangConfig.ApplyToPlayerStats, out _, out detail);
            if (!restarted)
            {
                Debug.LogError("[LevelMapBootstrap] 重新挑战准入失败：" + detail);
                error = "暂时无法重新挑战，请返回神域后再试。";
                return false;
            }
            error = null;
            return true;
        }

        private bool RouteToHub()
        {
            if (EditorLevelMapQuickPlayRuntime.CanPersist)
            {
                try
                {
                    // This remains the existing navigation persistence boundary:
                    // only release the hosted Run after its settled account and
                    // empty active-Run payload are atomically durable.
                    if (_meta == null ||
                        !EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService))
                        throw new InvalidOperationException("当前存档绑定不能清除活动 Run。");
                    _saveService.SaveTransactional(_meta, string.Empty);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[LevelMapBootstrap] 清除活动 Run 存档失败：" +
                        exception.Message);
                    return false;
                }
            }
            LevelRunSessionStore.Clear();
            SceneContext.Clear();
            EditorLevelMapQuickPlayRuntime.EndTransientSession();
            SceneLoader.GoToHub();
            return true;
        }

        private static void ValidateCharacterPresentations(
            LevelRunProfile profile,
            ICollection<string> errors)
        {
            if (profile == null || errors == null)
                return;

            ValidatePresentationId(
                profile.entryPresentationId,
                profile.entryPresentationVariantId,
                $"关卡 {profile.contentId} 首次进图",
                errors);
            if (profile.nodes == null)
                return;

            foreach (LevelNode node in profile.nodes)
            {
                if (node == null)
                    continue;

                ValidatePresentationId(
                    node.completionPresentationId,
                    node.completionPresentationVariantId,
                    $"节点 {node.contentId}",
                    errors);

                if (node.victoryRewards == null)
                    continue;
                foreach (LevelRewardGrant grant in node.victoryRewards)
                {
                    if (grant == null)
                        continue;
                    if (grant.kind == LevelRewardKind.SealChoice)
                        continue;
                    ValidatePresentationId(
                        grant.presentationId,
                        null,
                        $"奖励 {grant.contentId}",
                        errors);
                }
            }
        }

        private static void ValidateSealContents(
            LevelRunProfile profile,
            ICollection<string> errors)
        {
            if (profile?.nodes == null || errors == null)
                return;
            foreach (string catalogError in
                     WanjieScroll.Meta.LingyinCatalog.ValidateAll())
                errors.Add(catalogError);
            for (int nodeIndex = 0;
                 nodeIndex < profile.nodes.Count;
                 nodeIndex++)
            {
                LevelNode node = profile.nodes[nodeIndex];
                if (node == null)
                    continue;
                int grantSealCount = 0;
                if (node.completionEffects != null)
                {
                    for (int effectIndex = 0;
                         effectIndex < node.completionEffects.Count;
                         effectIndex++)
                    {
                        LevelInteractionEffect effect =
                            node.completionEffects[effectIndex];
                        if (effect == null ||
                            effect.kind !=
                            LevelInteractionEffectKind.GrantSeal)
                            continue;
                        grantSealCount++;
                        if (!WanjieScroll.Meta.LingyinCatalog.TryGet(
                                effect.targetContentId, out _))
                        {
                            errors.Add(
                                $"节点 {node.contentId} 引用不存在的灵印：" +
                                $"{effect.targetContentId}。");
                        }
                    }
                }
                if (grantSealCount > 1)
                {
                    errors.Add(
                        $"节点 {node.contentId} 一次只能配置一个 GrantSeal 效果。");
                }

                if (node.victoryRewards == null)
                    continue;
                int sealChoiceCount = 0;
                for (int rewardIndex = 0;
                     rewardIndex < node.victoryRewards.Count;
                     rewardIndex++)
                {
                    LevelRewardGrant reward =
                        node.victoryRewards[rewardIndex];
                    if (reward == null ||
                        reward.kind != LevelRewardKind.SealChoice)
                        continue;
                    sealChoiceCount++;
                    if (!string.IsNullOrWhiteSpace(
                            reward.sealRewardPoolId))
                    {
                        // The account-resolved candidate list is supplied at
                        // Run start and frozen on victory; static LevelMap
                        // validation must not mistake a pool ID for a fixed
                        // authored seal contentId.
                        if (reward.sealChoiceCount <= 0 ||
                            reward.sealChoiceCountMax <
                            reward.sealChoiceCount ||
                            !string.IsNullOrWhiteSpace(
                                reward.targetContentId) ||
                            (reward.sealChoiceContentIds != null &&
                             reward.sealChoiceContentIds.Count > 0))
                        {
                            errors.Add(
                                $"节点 {node.contentId} 的随机胜利灵印池配置无效。");
                        }
                        continue;
                    }
                    IReadOnlyList<string> sealContentIds =
                        reward.sealChoiceContentIds != null &&
                        reward.sealChoiceContentIds.Count > 0
                            ? (IReadOnlyList<string>)reward.sealChoiceContentIds
                            : new[] { reward.targetContentId };
                    for (int sealIndex = 0;
                         sealIndex < sealContentIds.Count;
                         sealIndex++)
                    {
                        string sealContentId = sealContentIds[sealIndex];
                        if (!WanjieScroll.Meta.LingyinCatalog.TryGet(
                                sealContentId,
                                out WanjieScroll.Meta.LingyinConfig seal))
                        {
                            errors.Add(
                                $"节点 {node.contentId} 的胜利奖励引用不存在的灵印：" +
                                $"{sealContentId}。");
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(
                                reward.presentationId) &&
                            !string.Equals(
                                reward.presentationId,
                                seal.presentationId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add(
                                $"节点 {node.contentId} 的胜利灵印奖励表现 ID " +
                                $"{reward.presentationId} 与灵印配置 " +
                                $"{seal.presentationId} 不一致。");
                        }
                    }
                }
                if (sealChoiceCount > 1)
                {
                    errors.Add(
                        $"节点 {node.contentId} 一次只能配置一个 SealChoice 奖励。");
                }
            }
        }

        private static void ValidatePresentationId(
            string presentationId,
            string variantId,
            string owner,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(presentationId))
            {
                if (!string.IsNullOrWhiteSpace(variantId))
                {
                    errors.Add(
                        $"{owner} 配置了角色演出内容变体 {variantId}，" +
                        "但缺少角色演出 ID。");
                }
                return;
            }

            try
            {
                if (!CharacterPresentationCatalog.TryGet(
                        presentationId,
                        out CharacterPresentationProfile profile))
                {
                    errors.Add(
                        $"{owner} 引用不存在的角色演出：{presentationId}。");
                    return;
                }

                if (profile.HasVariants)
                {
                    if (string.IsNullOrWhiteSpace(variantId))
                    {
                        errors.Add(
                            $"{owner} 引用的角色演出 {presentationId} " +
                            "必须配置内容变体 ID。");
                    }
                    else if (!profile.TryGetVariant(variantId, out _))
                    {
                        errors.Add(
                            $"{owner} 引用的角色演出 {presentationId} " +
                            $"不存在内容变体：{variantId}。");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(variantId))
                {
                    errors.Add(
                        $"{owner} 引用的角色演出 {presentationId} " +
                        $"不接受内容变体：{variantId}。");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{owner} 的角色演出配置无效：{exception.Message}");
            }
        }

        /// <summary>查看未确认的返回结果并更新地图状态。</summary>
        public void ConsumeReturn()
        {
            HandlePendingReturn();
        }

        /// <summary>处理待处理的返回结果。</summary>
        private void HandlePendingReturn()
        {
            if (_session == null) return;

            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (!_session.TryGetPendingReturn(out _) &&
                run != null && ReferenceEquals(run.CurrentFloor, _session) &&
                run.TryGetConclusion(out DungeonRunResultSnapshot frozen) &&
                frozen.Status == DungeonRunResultStatus.Victory)
            {
                _entryCoordinator?.ResetEntering();
                SyncNodeStatesWithSession();
                _victoryCoordinator?.ProcessVictoryReturn();
                return;
            }

            if (_session.TryGetPendingReturn(out LevelEncounterReturn result))
            {
                string groupId = result.NodeContentId;
                Debug.Log(
                    $"[LevelMapBootstrap] HandlePendingReturn: node={groupId}, outcome={result.Outcome}");

                // Reset entry coordinator so map is interactive again
                if (_entryCoordinator != null)
                    _entryCoordinator.ResetEntering();

                if (result.Outcome == CombatOutcome.Victory)
                {
                    // 场景常驻模式下地图未重建，需要同步视觉状态与会话状态。
                    // 同步后 W1=Completed、W2=Available，揭幕后即可直接交互。
                    SyncNodeStatesWithSession();
                    ReplayMapEntrySequencesForReturn(result);
                    if (_victoryCoordinator != null)
                        _victoryCoordinator.ProcessVictoryReturn();
                    else
                        _presentation.SetGroupState(groupId, MapVfxState.Available);
                }
                else
                {
                    _presentation.SetInteractionEnabled(false);
                    string conclusionError = null;
                    if (_conclusionCoordinator == null ||
                        !_conclusionCoordinator.TryShowDefeat(
                            out conclusionError))
                    {
                        Debug.LogError(
                            "[LevelMapBootstrap] 无法显示战败结算：" +
                            (conclusionError ?? "未配置结算协调器。"));
                        _presentation.SetInteractionEnabled(true);
                    }
                }
            }
        }

        private static void ReloadLevelMapAfterConclusion()
        {
            if (!SceneTransitionRuntimeBootstrap.Ensure().TryReloadMap(
                    SceneContext.Clear, out string error))
                Debug.LogError("[LevelMapBootstrap] 无法重新加载地图：" + error);
        }

        private void ReplayMapEntrySequences()
        {
            if (_session?.Profile != null)
                _titleOverlay?.Show(_session.Profile.displayName);
            _presentation?.ReplayOneShotSpriteSequences();
        }

        private void ReplayMapEntrySequencesForReturn(
            LevelEncounterReturn result)
        {
            string key = $"{_session?.RunId}|{result.NodeContentId}|" +
                         result.Outcome;
            if (string.Equals(
                    key,
                    _lastMapSequenceReturnKey,
                    StringComparison.Ordinal))
            {
                return;
            }
            _lastMapSequenceReturnKey = key;
            ReplayMapEntrySequences();
        }

        private void PersistActiveRun()
        {
            if (!EditorLevelMapQuickPlayRuntime.CanPersist)
                return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null || _saveService == null)
                return;
            if (run.IsRewardCommitInProgress)
            {
                Debug.LogWarning("[LevelMapBootstrap] 奖励正在提交或钱包尚未恢复，暂不保存普通地图检查点。");
                return;
            }
            try
            {
                PersistActiveRunOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[LevelMapBootstrap] 保存活动 Run 失败：" +
                    exception.Message);
            }
        }

        // Transaction callbacks must observe persistence failures so Dungeon
        // can restore the same previous floor and all participating ledgers.
        private void PersistActiveRunOrThrow()
        {
            if (!EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService))
                throw new InvalidOperationException("当前存档绑定不能提交 Run 事务。");
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null || _saveService == null || _meta == null)
                throw new InvalidOperationException("活动 Run 或存档依赖缺失，不能提交离层。");
            if (!WanjieScroll.Progression.DungeonRunSettlement.TryCommitFactsAndPersist(run, _meta,
                () => _saveService.SaveTransactional(_meta, run.Serialize()), out string eventError))
                throw new InvalidOperationException(eventError);
        }

        // Crystal and source-Gem choices use the exact captured Run/floor and
        // strict account/Run save, including XP facts and normal card acceptance.
        private void PersistAcceptedRewardOrThrow(DungeonRunSession run, IReadOnlyList<CardConfig> cardPool)
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy ||
                !EditorLevelMapQuickPlayRuntime.CanPersistTransaction(_saveService) || run == null ||
                !ReferenceEquals(run, DungeonRunSessionStore.Current) ||
                !ReferenceEquals(run.CurrentFloor, _session) || _saveService == null || _meta == null)
                throw new InvalidOperationException("奖励绑定或正式存档依赖已改变。");
            var requirements = WanjieScroll.Progression.NaturalLevelUpRewardCatalog
                .ResolveUnlockRequirements(cardPool);
            var accepted = WanjieScroll.Progression.CardUnlockAcceptance.FromRun(run);
            Action acceptCardsAndPersist = () =>
            {
                if (!WanjieScroll.Progression.CardUnlockLevelTransaction.TryCommit(
                    _meta, accepted, requirements,
                    () => _saveService.SaveTransactional(_meta, run.Serialize()),
                    () => { }, out _, out string cardError))
                    throw new InvalidOperationException(cardError);
            };
            // XP is the outer account owner; card acceptance already owns the
            // event-facts transaction, so do not open that same owner twice.
            if (!_meta.ExperienceCollections.TryCommitRunFacts(accepted.RunId,
                run.ExperienceCollections.RunTotals, acceptCardsAndPersist, out string error))
                throw new InvalidOperationException(error);
        }

        private void PersistAcceptedLevel(IReadOnlyList<CardConfig> cardPool)
        {
            if (!EditorLevelMapQuickPlayRuntime.CanPersist) return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null || _saveService == null || _meta == null)
                throw new InvalidOperationException("地图提交缺少Run或存档依赖。");
            var requirements = WanjieScroll.Progression.NaturalLevelUpRewardCatalog
                .ResolveUnlockRequirements(cardPool);
            if (!WanjieScroll.Progression.CardUnlockLevelTransaction.TryCommit(
                _meta, WanjieScroll.Progression.CardUnlockAcceptance.FromRun(run), requirements,
                () => _saveService.SaveTransactional(_meta, run.Serialize()),
                () => { }, out _, out string error))
                throw new InvalidOperationException(error);
        }

        /// <summary>将所有节点的视觉状态同步为当前会话状态。</summary>
        private void SyncNodeStatesWithSession()
        {
            if (_session == null || _session.Profile == null || _presentation == null)
                return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            bool ownsRunFloor = run != null && ReferenceEquals(run.CurrentFloor, _session);

            foreach (var node in _session.Nodes)
            {
                if (node == null) continue;
                LevelNodeViewState viewState = _session.GetNodeState(node.contentId);
                MapVfxState mapState = MapStateFromView(viewState);
                // A restored CardStat draft is still Completing in Dungeon,
                // while its legal resume command must remain clickable on the map.
                if (ownsRunFloor && run.GetNodeAdmission(node.contentId) == DungeonRunNodeAdmission.ResumePendingChoice)
                    mapState = MapVfxState.Available;
                bool manaPickupAvailable = ownsRunFloor &&
                    run.CardConversions.HasPendingManaPickup(run.CurrentFloorContentId, node.contentId);
                if (manaPickupAvailable) mapState = MapVfxState.Completed;
                bool pickupAvailable = ownsRunFloor &&
                    (run.AbandonedCarts.HasPendingPickup(run.CurrentFloorContentId, node.contentId) ||
                     manaPickupAvailable);
                _presentation.SetGroupState(node.contentId, mapState, immediate: true,
                    pickupAvailable: pickupAvailable);
            }
        }

        private static MapMonsterVfxTier ResolveMonsterVfxTier(LevelNode node)
        {
            bool isCombatNode = node.nodeType == LevelNodeType.Encounter ||
                                node.nodeType == LevelNodeType.Finale;
            if (!isCombatNode)
                return MapMonsterVfxTier.None;

            BattleScenarioProfile scenario =
                BattleScenarioCatalog.Get(node.scenarioId);
            if (scenario == null ||
                string.IsNullOrWhiteSpace(scenario.levelBossContentId))
            {
                return MapMonsterVfxTier.Normal;
            }

            return scenario.levelBossRank switch
            {
                BossRank.Floor => MapMonsterVfxTier.FloorBoss,
                BossRank.Stage => MapMonsterVfxTier.StageBoss,
                _ => throw new InvalidOperationException(
                    $"Boss 遭遇 {scenario.scenarioId} 缺少有效的 BossRank。")
            };
        }

        private static MapVfxState MapStateFromView(LevelNodeViewState viewState)
        {
            return viewState switch
            {
                LevelNodeViewState.Hidden => MapVfxState.Hidden,
                LevelNodeViewState.Locked => MapVfxState.Locked,
                LevelNodeViewState.Available => MapVfxState.Available,
                LevelNodeViewState.Completing => MapVfxState.Completing,
                LevelNodeViewState.Completed => MapVfxState.Completed,
                _ => MapVfxState.Hidden
            };
        }
    }
}
