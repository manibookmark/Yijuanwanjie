using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI.Battle;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 怪物区域封装组件：棋盘规格由 EnemyFormationLayout 统一定义，
    /// 棋盘几何与素材对齐读取表现配置。前排全灭后后排整体前移一行，
    /// 后备怪完整占格进入可见三排前始终不显示；抵达完整入场目标格后原地渐显。
    /// 独立顶部云雾只可作为环境装饰，绝不承担补位遮挡职责。
    /// 每个素材的落地点锚点和视觉缩放由 EnemyAreaPresentationProfile 配置。
    /// 棋盘线色是创建时注入的实例值，由本组件唯一写入；不修改共享配置。
    /// 此项仍属既有棋盘构建职责，不新增更新循环；动态主题动画出现时重新审查拆分。
    /// </summary>
    public sealed class EnemyAreaView : MonoBehaviour
    {
        private readonly List<EnemyActorView> _enemyActors =
            new List<EnemyActorView>();
        private readonly List<EnemyStatusEntry> _statusEntries =
            new List<EnemyStatusEntry>();
        private readonly List<LineRenderer> _gridLines =
            new List<LineRenderer>();
        private readonly List<DeferredBossPresentation> _deferredBosses =
            new List<DeferredBossPresentation>();
        private readonly Dictionary<int, BossPresentationWarmupSession>
            _bossWarmups =
                new Dictionary<int, BossPresentationWarmupSession>();
        private EnemyStatusView _enemyStatus;
        private Sprite _attackStatusIcon;
        private Sprite _defenseStatusIcon;
        private Sprite _batSprite;
        private SpriteRenderer _topFogRenderer;
        private TopFogDispersalView _topFogDispersal;
        private bool _topFogEnabled;
        private Transform _formationGrid;
        private Material _gridMaterial;
        private Color? _gridColorOverride;
        private EnemyAreaPresentationProfile _presentation;
        private CombatController _combat;
        private Canvas _tooltipCanvas;
        private EnemyFormationPresenter _formationPresenter;
        public event Action<EnemyActionPresentationSignal> ActionPresentationSignaled;

        private void ForwardActionSignal(EnemyActionPresentationSignal signal)
            => ActionPresentationSignaled?.Invoke(signal);

        private Action<BossAudioRequest> _bossAudioRequested;
        private bool _warnedMissingBossAudioSubscriber;
        private bool _pendingMissingBossAudioSubscriber;
        public event Action<BossAudioRequest> BossAudioRequested
        {
            add
            {
                _bossAudioRequested -= value;
                _bossAudioRequested += value;
                _warnedMissingBossAudioSubscriber = false;
                if (_bossAudioRequested != null) _pendingMissingBossAudioSubscriber = false;
                // Critical-ready can precede Bootstrap subscription. Replay intentions,
                // not renderer state or an AudioSource, so existing Boss idle remains audible.
                foreach (EnemyActorView actor in _enemyActors)
                {
                    actor?.Renderer?.GetComponent<BossSequenceAnimator>()?.ReplayAudioRequestTo(value);
                    actor?.Renderer?.GetComponent<BossAttackCloseupPresenter>()?.ReplayAudioRequestTo(value);
                }
            }
            remove { _bossAudioRequested -= value; }
        }

        private void ForwardBossAudio(BossAudioRequest request)
        {
            if (_bossAudioRequested != null) _bossAudioRequested(request);
            else if (request.Kind == BossAudioRequestKind.Play && !_warnedMissingBossAudioSubscriber)
            {
                _pendingMissingBossAudioSubscriber = true;
            }
        }
        private EnemyHoverPresenter _hoverPresenter;
        private Coroutine _deferredBossLoadRoutine;
        private Camera _closeupCamera;
        private SpriteRenderer _closeupBackground;
        private BossAttackCloseupProfile _closeupProfile;

        /// <summary>Inject the actual stage, without giving action presentation
        /// ownership of the formation, entrance root or main camera.</summary>
        public void ConfigureBossAttackCloseup(Camera camera, SpriteRenderer background)
        {
            _closeupCamera = camera;
            _closeupBackground = background;
            _closeupProfile = Resources.Load<BossAttackCloseupProfile>(
                BossAttackCloseupProfile.DefaultResourcePath);
            for (int i = 0; i < _enemyActors.Count; i++) AttachBossAttackCloseup(i);
        }

        private void AttachBossAttackCloseup(int enemyIndex)
        {
            if (_closeupProfile == null || _closeupCamera == null ||
                _closeupBackground == null || _combat == null || enemyIndex < 0 ||
                enemyIndex >= _enemyActors.Count || enemyIndex >= _combat.Enemies.Count ||
                !string.Equals(_combat.Enemies[enemyIndex]?.contentId, _closeupProfile.contentId,
                    StringComparison.Ordinal)) return;
            SpriteRenderer renderer = _enemyActors[enemyIndex]?.Renderer;
            if (renderer == null || renderer.GetComponent<BossAttackCloseupPresenter>() != null) return;
            var closeup = renderer.gameObject.AddComponent<BossAttackCloseupPresenter>();
            closeup.Bind(_closeupProfile, _closeupCamera, _closeupBackground, ForwardBossAudio);
        }

        public bool IsBossAttackCloseupActive(int enemyIndex)
            => enemyIndex >= 0 && enemyIndex < _enemyActors.Count &&
               _enemyActors[enemyIndex]?.Renderer != null &&
               _enemyActors[enemyIndex].Renderer.GetComponent<BossAttackCloseupPresenter>()?.IsPlaying == true;

        private readonly struct DeferredBossPresentation
        {
            public int EnemyIndex { get; }
            public string ContentId { get; }

            public DeferredBossPresentation(int enemyIndex, string contentId)
            {
                EnemyIndex = enemyIndex;
                ContentId = contentId;
            }
        }

        /// <summary>当前已构建的敌人表现节点数量。</summary>
        public int EnemyPresentationCount => _enemyActors.Count;
        public bool IsPresentationDeferred =>
            _formationPresenter != null &&
            _formationPresenter.IsPresentationDeferred;
        public bool IsEnemyActionPresentationActive =>
            _formationPresenter != null &&
            _formationPresenter.IsEnemyActionPresentationActive;
        /// <summary>
        /// True only after death holds, dependency tickets, formation motion
        /// and entry fades have all settled. Callers do not need to inspect
        /// the presenter's internal queues or coroutine counts.
        /// </summary>
        public bool IsFormationPresentationSettled =>
            _formationPresenter == null ||
            _formationPresenter.IsFormationPresentationSettled;
        public int EntryFadeCount =>
            _formationPresenter?.EntryFadeCount ?? 0;
        /// <summary>仅供人工表现诊断；生产等待逻辑使用 settled 查询。</summary>
        public int DefeatHoldCount =>
            _formationPresenter?.DefeatHoldCount ?? 0;

        public static EnemyAreaView Create(
            Transform parent,
            CombatController combat)
        {
            return Create(parent, combat, false);
        }

        public static EnemyAreaView Create(
            Transform parent,
            CombatController combat,
            bool topFogEnabled,
            Color? gridColorOverride = null)
        {
            var root = new GameObject("EnemyArea");
            root.transform.SetParent(parent, false);
            EnemyAreaView view = root.AddComponent<EnemyAreaView>();
            view._topFogEnabled = topFogEnabled;
            view._gridColorOverride = gridColorOverride;
            view.Bind(combat);
            return view;
        }

        public void ReleaseDefeatHold(int enemyRuntimeId)
        {
            _formationPresenter?.ReleaseDefeatHold(enemyRuntimeId);
        }

        /// <summary>Injects a source-scoped readiness query without importing the effects module.</summary>
        public void BindAttackEffectCompletionResolver(Func<int, Func<bool>> resolver)
            => _formationPresenter?.BindAttackEffectCompletionResolver(resolver);

        /// <summary>
        /// 注入怪物 Tooltip 唯一允许使用的战斗 HUD Canvas。
        /// 场景 Bootstrap 在完成 Canvas 构建后调用。
        /// </summary>
        public void ConfigureTooltipCanvas(Canvas canvas)
        {
            _tooltipCanvas = canvas;
            _hoverPresenter?.SetTooltipCanvas(canvas);
        }

        /// <summary>
        /// 为跨模块特殊入场申请角色表现保留。调用方只获得成功/失败，不接触
        /// Renderer 或 Transform；实际姿态仍由编队表现模块建立。
        /// </summary>
        public bool ReserveEnemyEntrance(int enemyIndex)
            => _formationPresenter != null &&
                _formationPresenter.ReserveEnemyEntrance(enemyIndex);

        public void ReleaseEnemyEntrance(int enemyIndex)
            => _formationPresenter?.ReleaseEnemyEntrance(enemyIndex);

        /// <summary>
        /// 播放已经异步准备好的 BOSS 挑衅段。素材尚未完成时返回 false，
        /// 调用方应直接使用静态回退，绝不能同步加载大图集。
        /// </summary>
        public bool TryPlayBossEntranceTaunt(
            int enemyIndex,
            Action<BossEntranceRuntimeDescriptor> onSpeechCue,
            Action onComplete)
        {
            if (!_bossWarmups.TryGetValue(
                    enemyIndex,
                    out BossPresentationWarmupSession warmup) ||
                !warmup.TryGetCritical(
                    out BossPreparedPresentation presentation) ||
                !presentation.HasEntrance ||
                enemyIndex < 0 || enemyIndex >= _enemyActors.Count)
            {
                return false;
            }

            SpriteRenderer renderer = _enemyActors[enemyIndex]?.Renderer;
            if (renderer == null)
                return false;
            BossEntranceTauntPlayer player =
                renderer.GetComponent<BossEntranceTauntPlayer>();
            if (player == null || !player.IsPrepared)
                return false;

            BossSequenceAnimator currentAnimator =
                renderer.GetComponent<BossSequenceAnimator>();
            if (currentAnimator != null)
            {
                currentAnimator.StopAll();
                currentAnimator.enabled = false;
            }
            Action prepareIdle = () =>
            {
                if (renderer == null)
                    return;
                BossSequenceAnimator animator =
                    renderer.GetComponent<BossSequenceAnimator>();
                if (animator == null)
                    return;
                animator.enabled = true;
                animator.EnterIdle();
            };
            return player.Play(
                () => onSpeechCue?.Invoke(
                    presentation.EntranceDescriptor),
                prepareIdle,
                onComplete);
        }

        /// <summary>关 BOSS 落地时把顶部云层向两侧震开并永久隐藏。</summary>
        public void DismissTopFogForStageBossEntrance(
            float duration,
            float distanceRatio)
        {
            _topFogDispersal?.Dismiss(duration, distanceRatio);
        }

        public void FadeTopFogUpwardForStageBossEntrance(
            float duration,
            float riseRatio)
        {
            _topFogDispersal?.DismissUpward(duration, riseRatio);
        }

        /// <summary>
        /// 返回棋盘四角的世界坐标值，供 HUD 投影使用，不暴露可修改的棋盘节点。
        /// </summary>
        public bool TryGetBoardWorldFrame(out Vector3 frontLeft, out Vector3 frontRight,
            out Vector3 backLeft, out Vector3 backRight)
        {
            frontLeft = frontRight = backLeft = backRight = default;
            if (_formationGrid == null || _presentation == null) return false;
            frontLeft = _formationGrid.TransformPoint(_presentation.GetGridPoint(0f, 0f));
            frontRight = _formationGrid.TransformPoint(_presentation.GetGridPoint(1f, 0f));
            backLeft = _formationGrid.TransformPoint(_presentation.GetGridPoint(0f, 1f));
            backRight = _formationGrid.TransformPoint(_presentation.GetGridPoint(1f, 1f));
            return true;
        }

        /// <summary>只读棋盘深度查询；不泄露Renderer或允许外部写敌人层级。</summary>
        public int GetBoardProjectileSortingOrder(float depth)
            => _presentation != null ? _presentation.GetProjectileSortingOrder(depth) : 0;

        /// <summary>区域静态构成与敌势汇总的只读快照。</summary>
        public EnemyAreaPresentationSnapshot GetAreaPresentation()
        {
            SpriteRenderer topFog = _topFogRenderer;
            EnemyStatusView status = _enemyStatus;
            EnemyAreaPresentationProfile presentation = _presentation;
            return new EnemyAreaPresentationSnapshot(
                topFog != null && topFog.sprite != null
                    ? topFog.sprite.name
                    : string.Empty,
                topFog != null ? topFog.color.a : 0f,
                topFog != null ? topFog.sortingOrder : 0,
                presentation != null ? presentation.topFogOpacity : 0f,
                presentation != null ? presentation.VisibleRows : 0,
                _formationGrid != null,
                _gridLines.Count,
                status != null,
                status != null ? status.VisibleEntryCount : 0,
                status != null ? status.ContentWidth : 0f);
        }

        /// <summary>
        /// 获取调用瞬间的只读表现快照；索引无效或节点已释放时返回默认值。
        /// 快照不会把 Renderer、GameObject 或 Transform 暴露给调用方。
        /// </summary>
        public EnemyPresentationSnapshot GetEnemyPresentation(int enemyIndex)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemyActors.Count)
                return default;

            EnemyActorView actor = _enemyActors[enemyIndex];
            SpriteRenderer renderer = actor?.Renderer;
            if (actor?.FormationTransform == null || renderer == null)
                return default;

            return new EnemyPresentationSnapshot(
                renderer.sprite != null ? renderer.sprite.name : string.Empty,
                transform.InverseTransformPoint(renderer.transform.position),
                renderer.bounds,
                renderer.sortingOrder,
                renderer.gameObject.activeSelf,
                renderer.gameObject.activeInHierarchy,
                renderer.enabled,
                renderer.color.a);
        }

        /// <summary>供场景组装读取阴影世界落点，保留阴影节点与编队位置的所有权。</summary>
        public bool TryGetEnemyGroundAnchor(int enemyIndex, out Vector3 worldAnchor)
        {
            worldAnchor = default;
            if (enemyIndex < 0 || enemyIndex >= _enemyActors.Count)
                return false;
            EnemyActorView actor = _enemyActors[enemyIndex];
            return actor != null && actor.TryGetGroundAnchor(out worldAnchor);
        }

        /// <summary>
        /// 供 Bootstrap 在场景组装阶段一次性注册特效目标。
        /// 回调依次收到命中特效目标与独立入场运动根。它们只用于场景组装期
        /// 注册；编队定位根不会跨模块暴露，Effects 也不得反向改写阵型位置。
        /// </summary>
        public void ConfigureEffectTargets(
            Action<int, Transform, Transform> configureTarget)
        {
            if (configureTarget == null) return;
            for (int i = 0; i < _enemyActors.Count; i++)
            {
                EnemyActorView actor = _enemyActors[i];
                Transform target = actor?.EffectTargetTransform;
                Transform entranceMotion = actor?.EntranceMotionTransform;
                if (target != null && entranceMotion != null)
                    configureTarget(i, target, entranceMotion);
            }
        }

        public void Bind(CombatController combat)
        {
            Unbind();
            _combat = combat;
            if (_combat == null) return;

            _topFogDispersal?.Restore();
            BuildEnemies(_combat.Enemies.Count);
            if (_hoverPresenter == null)
            {
                _hoverPresenter = GetComponent<EnemyHoverPresenter>();
                if (_hoverPresenter == null)
                    _hoverPresenter = gameObject.AddComponent<EnemyHoverPresenter>();
            }
            _hoverPresenter.SetTooltipCanvas(_tooltipCanvas);
            _hoverPresenter.Bind(_combat, _enemyActors);
            if (_formationPresenter == null)
            {
                _formationPresenter =
                    GetComponent<EnemyFormationPresenter>();
                if (_formationPresenter == null)
                {
                    _formationPresenter =
                        gameObject.AddComponent<EnemyFormationPresenter>();
                }
            }
            _formationPresenter.ActionPresentationSignaled -= ForwardActionSignal;
            _formationPresenter.ActionPresentationSignaled += ForwardActionSignal;
            _formationPresenter.Bind(
                _combat,
                _presentation,
                _enemyActors,
                Refresh,
                MarkEnemyTooltipDirty);
            _formationPresenter.InitializeActors();
            Refresh();
        }

        /// <summary>战斗层显示后启动普通小怪的纸片入场表现。</summary>
        public void PlayInitialEnemyEntrances()
            => _formationPresenter?.PlayInitialEnemyEntrances();

        public void Unbind()
        {
            CancelDeferredBossPresentationLoad();
            ReleaseBossWarmups();
            _hoverPresenter?.Unbind();
            ClearDamagePreview();
            _formationPresenter?.Unbind();
            if (_formationPresenter != null)
                _formationPresenter.ActionPresentationSignaled -= ForwardActionSignal;
            _combat = null;
        }

        /// <summary>Prepares the fixed board without creating enemies or rules.</summary>
        public void PrepareEmptyBoard()
        {
            if (_combat != null)
                throw new System.InvalidOperationException(
                    "已绑定战斗的敌人区域不能重新预建空棋盘。");
            BuildEnemies(0);
            _statusEntries.Clear();
            _enemyStatus?.SetEntries(_statusEntries);
            _topFogDispersal?.Restore();
        }

        /// <summary>
        /// Disposes the encounter's actors and transition tickets while keeping
        /// the floor board, status slots and fog available for another battle.
        /// </summary>
        public void ReleaseEncounter()
        {
            Unbind();
            foreach (EnemyActorView actor in _enemyActors)
            {
                actor?.HideForState();
                actor?.Release();
            }
            _enemyActors.Clear();
            _deferredBosses.Clear();
            _statusEntries.Clear();
            _enemyStatus?.SetEntries(_statusEntries);
            _topFogDispersal?.Restore();
        }

        /// <summary>
        /// 只能在战斗已显示后由 Bootstrap 显式启动。隐藏预载期间
        /// BuildEnemies 只建立轻量占位角色，不触碰任何 BOSS 大图集。
        /// </summary>
        public void BeginDeferredBossPresentationLoad()
        {
            if (_deferredBossLoadRoutine != null ||
                _bossWarmups.Count != 0 || _deferredBosses.Count == 0 ||
                !isActiveAndEnabled)
                return;
            _deferredBossLoadRoutine = StartCoroutine(
                LoadDeferredBossPresentations());
        }

        private IEnumerator LoadDeferredBossPresentations()
        {
            // 让战斗首帧先完整呈现，避免资源请求的完成回调与转场同帧。
            yield return null;
            for (int i = 0; i < _deferredBosses.Count; i++)
            {
                DeferredBossPresentation deferred = _deferredBosses[i];
                EnemySpriteAlignment visual = BuildBossVisual(
                    deferred.ContentId);
                Vector2 groundAnchor = visual != null
                    ? visual.groundAnchor
                    : new Vector2(0.5f, 0.05f);
                var warmup = new BossPresentationWarmupSession();
                _bossWarmups.Add(deferred.EnemyIndex, warmup);
                yield return warmup.PrepareAsync(
                    deferred.ContentId,
                    groundAnchor,
                    prepared => AttachPreparedBossPresentation(
                        deferred.EnemyIndex,
                        groundAnchor,
                        prepared));

                if (warmup.CriticalStatus !=
                    BossPresentationWarmupStatus.Ready)
                {
                    Debug.LogWarning(
                        $"[EnemyArea] BOSS 关键表现预热失败，" +
                        $"保留静态回退：{deferred.ContentId}，" +
                        warmup.CriticalError);
                    continue;
                }
                if (warmup.OptionalStatus ==
                    BossPresentationWarmupStatus.Failed)
                {
                    Debug.LogWarning(
                        $"[EnemyArea] BOSS 可选动作预热失败，" +
                        $"后续动作使用普通表现回退：" +
                        $"{deferred.ContentId}，{warmup.OptionalError}");
                }
                // 不要让下一张大图的后处理追在当前帧。
                yield return null;
            }
            _deferredBossLoadRoutine = null;
        }

        private void AttachPreparedBossPresentation(
            int enemyIndex,
            Vector2 groundAnchor,
            BossPreparedPresentation presentation)
        {
            if (_combat == null || presentation == null || enemyIndex < 0 ||
                enemyIndex >= _enemyActors.Count)
            {
                return;
            }

            EnemyActorView actor = _enemyActors[enemyIndex];
            SpriteRenderer renderer = actor?.Renderer;
            if (renderer == null)
                return;

            BossSequenceAnimator animator =
                renderer.GetComponent<BossSequenceAnimator>() ??
                renderer.gameObject.AddComponent<BossSequenceAnimator>();
            EnemyStats enemy = _combat.Enemies[enemyIndex];
            bool entranceReserved = _formationPresenter != null &&
                                    _formationPresenter
                                        .IsEnemyEntranceReserved(enemyIndex);
            bool startIdlePlayback = enemy != null && enemy.IsDeployed &&
                                     !entranceReserved;
            animator.BindAudioRequests(ForwardBossAudio);
            animator.BindPrepared(
                presentation,
                groundAnchor,
                startIdlePlayback);
            AttachBossAttackCloseup(enemyIndex);
            if (startIdlePlayback && renderer.sprite != null)
            {
                _formationPresenter?.RefreshEnemyGeometry(enemyIndex);
            }

            if (!presentation.HasEntrance)
                return;
            BossEntranceTauntPlayer taunt =
                renderer.GetComponent<BossEntranceTauntPlayer>() ??
                renderer.gameObject.AddComponent<BossEntranceTauntPlayer>();
            if (!taunt.ConfigurePrepared(presentation))
            {
                Debug.LogWarning(
                    $"[EnemyArea] BOSS 登场帧已预热但绑定失败：" +
                    presentation.ContentId);
            }
        }

        private void Update()
        {
            if (_pendingMissingBossAudioSubscriber)
            {
                _pendingMissingBossAudioSubscriber = false;
                if (_bossAudioRequested == null && !_warnedMissingBossAudioSubscriber)
                {
                    _warnedMissingBossAudioSubscriber = true;
                    Debug.LogWarning("BossAudioRequested has no Audio coordinator; current playback intent will replay when one subscribes.");
                }
            }
            if (_combat == null) return;
            _formationPresenter?.Tick();
        }

        private void MarkEnemyTooltipDirty()
        {
            _hoverPresenter?.MarkDirty();
        }

        /// <summary>
        /// 返回基于当前战斗快照生成的怪物 Tooltip 文本。
        /// </summary>
        public string GetEnemyTooltipText(int enemyIndex)
        {
            return _hoverPresenter?.GetTooltipText(enemyIndex) ??
                string.Empty;
        }
        private void OnDisable()
        {
            CancelDeferredBossPresentationLoad();
            _hoverPresenter?.Hide();
            _formationPresenter?.CancelPresentations();
        }

        private void CancelDeferredBossPresentationLoad()
        {
            if (_deferredBossLoadRoutine == null)
                return;
            StopCoroutine(_deferredBossLoadRoutine);
            _deferredBossLoadRoutine = null;
            foreach (BossPresentationWarmupSession warmup in
                     _bossWarmups.Values)
                warmup?.Cancel();
        }

        private void ReleaseBossWarmups()
        {
            for (int i = 0; i < _enemyActors.Count; i++)
            {
                SpriteRenderer renderer = _enemyActors[i]?.Renderer;
                if (renderer == null)
                    continue;
                renderer.GetComponent<BossSequenceAnimator>()?.BindPrepared(
                    null,
                    Vector2.zero,
                    false);
                renderer.GetComponent<BossEntranceTauntPlayer>()?
                    .ConfigurePrepared(null);
            }
            foreach (BossPresentationWarmupSession warmup in
                     _bossWarmups.Values)
                warmup?.Release();
            _bossWarmups.Clear();
        }

        /// <summary>
        /// Plays a short readable melee beat. The supplied impact callback is
        /// the boundary where Bootstrap asks Core to apply damage.
        /// </summary>
        public bool PlayAttackMotion(
            int enemyIndex,
            System.Action onImpact,
            System.Action onComplete)
        {
            return PlayAttackMotion(
                enemyIndex,
                null,
                onImpact,
                onComplete);
        }

        public bool PlayAttackMotion(
            int enemyIndex,
            string actionId,
            System.Action onImpact,
            System.Action onComplete,
            System.Action onInterrupted = null)
        {
            if (_formationPresenter != null)
            {
                return _formationPresenter.PlayAttackMotion(
                    enemyIndex,
                    actionId,
                    onImpact,
                    onComplete,
                    onInterrupted);
            }

            onImpact?.Invoke();
            onComplete?.Invoke();
            return false;
        }

        public bool PlayUtilityMotion(int enemyIndex, Action onImpact, Action onComplete)
            => PlayUtilityMotion(enemyIndex, null, onImpact, onComplete);

        public bool PlayUtilityMotion(int enemyIndex, string actionId,
            Action onImpact, Action onComplete, Action onInterrupted = null)
        {
            if (_formationPresenter != null)
                return _formationPresenter.PlayUtilityMotion(enemyIndex, actionId,
                    onImpact, onComplete, onInterrupted);
            onImpact?.Invoke();
            onComplete?.Invoke();
            return false;
        }

        public void BeginCombatPresentation()
        {
            _formationPresenter?.BeginCombatPresentation();
        }

        public void EndCombatPresentation()
        {
            _formationPresenter?.EndCombatPresentation();
        }

        private void BuildEnemies(int count)
        {
            foreach (EnemyActorView actor in _enemyActors)
                actor?.Release();
            _enemyActors.Clear();
            _deferredBosses.Clear();

            _batSprite = RuntimeArtLoader.GetPapercraftBat();
            if (_batSprite == null)
            {
                throw new System.InvalidOperationException(
                    "敌人区域缺少默认剪纸怪物素材：" +
                    "Sprites/Enemies/Papercraft/bat");
            }

            if (_presentation == null)
                _presentation = EnemyAreaPresentationProfile.LoadDefault();

            if (_formationGrid == null)
                _formationGrid = CreateFormationGrid(transform);

            if (!_topFogEnabled && _topFogRenderer != null)
            {
                Destroy(_topFogRenderer.gameObject);
                _topFogRenderer = null;
                _topFogDispersal = null;
            }
            if (_topFogEnabled && _topFogRenderer == null)
                _topFogRenderer = CreateTopFog(transform);
            if (_topFogRenderer != null && _topFogDispersal == null)
            {
                _topFogDispersal =
                    _topFogRenderer.GetComponent<TopFogDispersalView>() ??
                    _topFogRenderer.gameObject.AddComponent<
                        TopFogDispersalView>();
                _topFogDispersal.Bind(_topFogRenderer);
            }

            if (_enemyStatus == null)
            {
                _enemyStatus = EnemyStatusView.Create(transform);
                _enemyStatus.transform.localPosition =
                    _presentation.enemyStatusPosition;
                _attackStatusIcon = EnemyStatusView.LoadDefaultIcon(
                    EnemyStatusView.AttackStatusId);
                _defenseStatusIcon = EnemyStatusView.LoadDefaultIcon(
                    EnemyStatusView.DefenseStatusId);
            }
            _defenseStatusIcon ??=
                EnemyStatusView.LoadDefaultIcon(EnemyStatusView.DefenseStatusId);

            // 为所有敌人创建对象；与可见三排相交时才启用渲染。
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemyStats = _combat.Enemies[i];
                if (enemyStats != null && enemyStats.isBoss &&
                    BossAnimationRuntimeCatalog.HasManifest(
                        enemyStats.contentId))
                {
                    // 预载期只建立静态占位角色。真正图集在战斗显示后
                    // 由 BeginDeferredBossPresentationLoad 逐张异步准备。
                    EnemySpriteAlignment bossVisual =
                        BuildBossVisual(enemyStats.contentId);
                    EnemyActorView actor = EnemyActorView.Create(
                        transform,
                        i,
                        bossVisual?.sprite ?? SelectFallbackSprite(enemyStats),
                        bossVisual);
                    _enemyActors.Add(actor);
                    _deferredBosses.Add(new DeferredBossPresentation(
                        i,
                        enemyStats.contentId));
                    continue;
                }

                EnemySpriteAlignment visual =
                    SelectEnemyVisual(enemyStats);
                Sprite sprite = visual != null
                    ? visual.sprite
                    : SelectFallbackSprite(enemyStats);
                EnemyActorView actor2 = EnemyActorView.Create(
                    transform,
                    i,
                    sprite,
                    visual);
                _enemyActors.Add(actor2);
            }
        }

        /// <summary>
        /// 关卡 BOSS 优先消费统一敌人对齐配置；旧动画配置中的落点与缩放
        /// 只作为缺少对齐项时的兼容回退，避免两份配置互相覆盖。
        /// </summary>
        private EnemySpriteAlignment BuildBossVisual(string contentId)
        {
            if (_presentation != null &&
                _presentation.TryGetVisual(
                    contentId,
                    out EnemySpriteAlignment configured))
            {
                return configured;
            }

            BossAnimationRuntimeCatalog.TryGetManifest(
                contentId,
                out BossAnimationRuntimeManifest manifest);

            return new EnemySpriteAlignment
            {
                contentId = contentId,
                sprite = null,
                groundAnchor = manifest != null
                    ? manifest.groundAnchor
                    : new Vector2(0.5f, 0.05f),
                visualScale = manifest != null
                    ? manifest.visualScale
                    : 1f
            };
        }

        /// <summary>
        /// 在怪物身上显示按敌人聚合后的确定性伤害预览。
        /// 数值只读取 Core 的 EnemyDamagePreview，不在 UI 层重复计算伤害。
        /// </summary>
        public void ShowDamagePreview(
            IReadOnlyList<EnemyDamagePreview> previews)
        {
            ClearDamagePreview();
            if (previews == null) return;

            for (int i = 0; i < previews.Count; i++)
            {
                EnemyDamagePreview preview = previews[i];
                int targetIndex = preview.TargetIndex;
                if (targetIndex < 0 ||
                    targetIndex >= _enemyActors.Count ||
                    (preview.AppliedDamage <= 0 && preview.OverkillDamage <= 0 && !preview.IsErased))
                {
                    continue;
                }

                _enemyActors[targetIndex]?.ShowDamagePreview(preview);
            }
        }

        public void ClearDamagePreview()
        {
            // Hover 离开或战场刷新时，立即清除全部怪物的伤害预览。
            for (int i = 0; i < _enemyActors.Count; i++)
                _enemyActors[i]?.HideDamagePreview();
        }

        /// <summary>根据排和排内索引设置位置、缩放、层级</summary>
        private void Refresh()
        {
            if (_combat == null) return;
            _formationPresenter?.RefreshActors();

            if (_enemyStatus != null)
            {
                int totalAttack = 0;
                int totalDefense = 0;
                bool hasVisibleBoss = false;
                foreach (var enemy in _combat.Enemies)
                {
                    if (enemy.IsDead || !enemy.IsDeployed)
                        continue;
                    hasVisibleBoss |= enemy.isBoss;
                    if (enemy.row != 0) continue;
                    totalAttack += enemy.maxHit;
                    totalDefense += enemy.armor;
                }

                Vector3 statusPosition = _presentation.enemyStatusPosition;
                if (hasVisibleBoss)
                    statusPosition.y += _presentation.bossEnemyStatusYOffset;
                _enemyStatus.transform.localPosition = statusPosition;

                _statusEntries.Clear();

                // 汇总已冻结的敌人意图（只在前排存活敌人中统计）。
                // 敌方动作播放期间已进入 EnemyActing，但意图仍是本轮
                // 的权威快照；不能因阶段变化回退到 maxHit，否则
                // Defend 动作中会同时误显“3 攻击”。
                int intentAttack = 0;
                int intentDefend = 0;
                IReadOnlyList<EnemyIntent> intents =
                    _combat.State?.EnemyAi?.GetPlannedIntents();
                bool hasIntents = intents != null &&
                    intents.Count == _combat.Enemies.Count;

                if (hasIntents)
                {
                    for (int i = 0; i < intents.Count && i < _combat.Enemies.Count; i++)
                    {
                        if (_combat.Enemies[i].IsDead ||
                            !_combat.Enemies[i].IsDeployed ||
                            _combat.Enemies[i].row != 0) continue;
                        var intent = intents[i];
                        EnemyIntentType primaryType = intent.Type == EnemyIntentType.Compound
                            ? intent.PrimaryType : intent.Type;
                        if (primaryType == EnemyIntentType.Attack)
                            intentAttack += intent.Amount;
                        else if (primaryType == EnemyIntentType.Defend)
                            intentDefend += intent.Amount;
                        if (intent.Type == EnemyIntentType.Compound)
                        {
                            if (intent.SecondaryType == EnemyIntentType.Attack)
                                intentAttack += intent.SecondaryAmount;
                            else if (intent.SecondaryType == EnemyIntentType.Defend)
                                intentDefend += intent.SecondaryAmount;
                        }
                    }
                }

                // 只有尚未产生本轮意图时才回退到基础战力。
                int displayAttack = hasIntents
                    ? intentAttack
                    : totalAttack;
                int displayDefense = hasIntents
                    ? intentDefend
                    : totalDefense;

                if (displayAttack > 0)
                {
                    _statusEntries.Add(new EnemyStatusEntry(
                        EnemyStatusView.AttackStatusId,
                        _attackStatusIcon,
                        displayAttack));
                }
                if (displayDefense > 0)
                {
                    _statusEntries.Add(new EnemyStatusEntry(
                        EnemyStatusView.DefenseStatusId,
                        _defenseStatusIcon,
                        displayDefense));
                }
                _enemyStatus.SetEntries(_statusEntries);
            }
        }

        private EnemySpriteAlignment SelectEnemyVisual(
            EnemyStats enemy)
        {
            if (enemy != null &&
                _presentation.TryGetVisual(
                    enemy.contentId,
                    out EnemySpriteAlignment configured))
            {
                return configured;
            }

            Sprite sprite = SelectFallbackSprite(enemy);
            if (sprite != null &&
                _presentation.TryGetAlignment(
                    sprite,
                    out Vector2 groundAnchor,
                    out Vector2 visualOffset,
                    out float visualScale))
            {
                return new EnemySpriteAlignment
                {
                    sprite = sprite,
                    groundAnchor = groundAnchor,
                    visualOffset = visualOffset,
                    visualScale = visualScale
                };
            }
            return null;
        }

        private Sprite SelectFallbackSprite(
            EnemyStats enemy)
        {
            if (enemy != null && enemy.enemyArt != null)
                return enemy.enemyArt;

            // 未配置的新敌人只使用统一占位素材，不用显示名称推断资源。
            return _batSprite;
        }

        private SpriteRenderer CreateTopFog(Transform parent)
        {
            Sprite fog = RuntimeArtLoader.GetPapercraftTopFog();
            if (fog == null)
            {
                throw new System.InvalidOperationException(
                    "敌人区域缺少顶部云雾素材：" +
                    "Sprites/Enemies/Papercraft/top-fog");
            }

            var fogObject = new GameObject(
                "TopFog",
                typeof(SpriteRenderer));
            fogObject.transform.SetParent(parent, false);
            fogObject.transform.localPosition =
                _presentation.topFogPosition;
            SpriteRenderer renderer =
                fogObject.GetComponent<SpriteRenderer>();
            renderer.sprite = fog;
            renderer.color = new Color(
                1f,
                1f,
                1f,
                _presentation.topFogOpacity);
            // 覆盖后两排敌人的上缘，同时保持在前排敌人与敌势组件后方。
            renderer.sortingOrder = 25;

            float spriteWidth = fog.bounds.size.x;
            if (spriteWidth > 0f)
            {
                float scale = _presentation.topFogWidth / spriteWidth;
                fogObject.transform.localScale =
                    new Vector3(scale, scale, 1f);
            }
            return renderer;
        }

        private Transform CreateFormationGrid(Transform parent)
        {
            var root = new GameObject("FormationGrid");
            root.transform.SetParent(parent, false);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "无法创建敌人棋盘格：缺少 Sprites/Default Shader。");
            }

            _gridMaterial = new Material(shader)
            {
                name = "EnemyFormationGrid_Runtime"
            };
            _gridLines.Clear();

            for (int rowBoundary = 0;
                 rowBoundary <= _presentation.VisibleRows;
                 rowBoundary++)
            {
                float depth =
                    rowBoundary / (float)_presentation.VisibleRows;
                AddGridLine(
                    root.transform,
                    $"Row_{rowBoundary}",
                    _presentation.GetGridPoint(0f, depth),
                    _presentation.GetGridPoint(1f, depth));
            }

            for (int columnBoundary = 0;
                 columnBoundary <= _presentation.Columns;
                 columnBoundary++)
            {
                float column =
                    columnBoundary / (float)_presentation.Columns;
                AddGridLine(
                    root.transform,
                    $"Column_{columnBoundary}",
                    _presentation.GetGridPoint(column, 0f),
                    _presentation.GetGridPoint(column, 1f));
            }

            return root.transform;
        }

        private void AddGridLine(
            Transform parent,
            string objectName,
            Vector3 start,
            Vector3 end)
        {
            var lineObject = new GameObject(
                objectName,
                typeof(LineRenderer));
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.sharedMaterial = _gridMaterial;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = _presentation.gridLineWidth;
            line.numCapVertices = 2;
            line.startColor = _gridColorOverride ?? _presentation.gridColor;
            line.endColor = line.startColor;
            line.sortingOrder = -6;
            _gridLines.Add(line);
        }

        private void OnDestroy()
        {
            Unbind();
            if (_gridMaterial != null)
                Destroy(_gridMaterial);
        }
    }
}
