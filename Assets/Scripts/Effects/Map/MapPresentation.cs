using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects;

namespace WanjieScroll.Effects.Map
{
    /// <summary>地图怪物节点的表现强度层级。</summary>
    public enum MapMonsterVfxTier
    {
        None = 0,
        Normal = 1,
        FloorBoss = 2,
        StageBoss = 3
    }

    /// <summary>
    /// 可复用的地图表现模块。负责相机、底图、节点、碰撞体、悬停、复合交互组、
    /// 完成检测、点击命中与节点图片序列的显式播放。它只表达
    /// "一个交互组的视觉状态"；具体节点与关卡规则
    /// (LevelRunSession) 由外部推导并喂给它。手指点击只会通过
    /// <see cref="ExecuteRequested"/> 上报，不会自行启动战斗。
    /// 本次阴影校准只在构建时解析并交给单节点 Halo owner，不新增表现状态
    /// 所有权；后续独立演出仍须先审查本文件的职责边界。
    /// None 收尾确认复用组现有视觉状态，不新增业务状态或独立演出生命周期。
    /// 节点局部几何仍由 Profile 拥有；偏移只在构建时应用，Halo 继续独占后续脚底补偿。
    /// </summary>
    public sealed class MapPresentation : MonoBehaviour
    {
        // 地图内容的逻辑舞台固定为 16:9。背景可以比核心舞台更宽，但节点的
        // normalizedPosition 永远只映射到这块核心区域，避免扩图后路线被横向摊开。
        private const float CoreStageAspect = 16f / 9f;

        private readonly Dictionary<string, MapGroup> _groups =
            new Dictionary<string, MapGroup>();
        private readonly List<ProceduralMapVfx> _effects =
            new List<ProceduralMapVfx>();
        private readonly List<MapSpriteSequenceAnimator> _spriteSequences =
            new List<MapSpriteSequenceAnimator>();
        private readonly Dictionary<string, Vector2> _nodePositions =
            new Dictionary<string, Vector2>(StringComparer.Ordinal);

        private Transform _presentationRoot;
        private Camera _camera;
        private Vector2 _mapWorldSize;
        private Vector2 _backgroundWorldSize;
        private bool _built;
        private MapPreviewProfile _profile;
        private bool _interactionEnabled = true;
        private string _focusedGroupId;
        private float _focusDuration = 0.30f;
        private float _overviewDuration = 0.35f;
        private float _purificationDuration = 1.10f;
        private Coroutine _focusCoroutine;
        private int _lastPointerExecutionFrame = -1;

        // Hover tooltip state
        private string _hoveredGroupId;
        private string _hoveredContentId;
        private MapVfxState _hoveredState;
        private Vector3 _hoveredWorldPos;

        /// <summary>用户点击某个交互组时触发，携带 groupId。</summary>
        public event Action<string> ExecuteRequested;

        /// <summary>
        /// 用户通过悬停或最终点击表达了对可执行交互组的意图。事件只携带稳定
        /// groupId；地图表现不判断它是否为战斗节点，也不拥有任何预载策略。
        /// </summary>
        public event Action<string> InteractionIntentSignaled;

        /// <summary>悬停组发生变化时发布稳定 groupId；null 表示离开或交互关闭。</summary>
        public event Action<string> HoverChanged;

        /// <summary>当前绑定的展示配置。</summary>
        public MapPreviewProfile Profile => _profile;

        /// <summary>逻辑地图舞台尺寸（固定 16:9 核心区）。</summary>
        public Vector2 MapWorldSize => _mapWorldSize;

        /// <summary>实际背景精灵覆盖的世界尺寸（包含横向扩图）。</summary>
        public Vector2 BackgroundWorldSize => _backgroundWorldSize;

        public bool IsBuilt => _built;

        public bool HasDeveloperHud => false;

        /// <summary>交互是否启用。</summary>
        public bool IsInteractionEnabled => _interactionEnabled;

        /// <summary>当前聚焦的组 ID。</summary>
        public string FocusedGroupId => _focusedGroupId;

        /// <summary>
        /// 构建地图表现。initialStates 允许外部（如关卡会话）覆盖资源里的默认状态。
        /// </summary>
        public void Build(
            MapPreviewProfile profile,
            IReadOnlyDictionary<string, MapVfxState> initialStates = null,
            IReadOnlyDictionary<string, MapMonsterVfxTier> monsterTiers = null,
            IReadOnlyDictionary<string, Vector2> nodePositions = null,
            IReadOnlyDictionary<string, MapNodeVisualStyle> nodeStyles = null)
        {
            if (_built)
                throw new InvalidOperationException(
                    "地图表现已构建，不能重复 Build。");
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (!profile.TryValidate(out string validationError))
                throw new InvalidOperationException(validationError);

            // Copy this view's positions, never mutate shared profile geometry.
            // Effects knows neither which guards were sampled nor gameplay rules.
            _nodePositions.Clear();
            if (nodePositions != null)
            {
                foreach (var pair in nodePositions)
                {
                    Vector2 position = pair.Value;
                    if (!profile.nodes.Exists(node => node.contentId == pair.Key) ||
                        float.IsNaN(position.x) || float.IsNaN(position.y) ||
                        position.x < 0f || position.x > 1f || position.y < 0f || position.y > 1f)
                        throw new InvalidOperationException("Invalid map position binding: " + pair.Key);
                    _nodePositions.Add(pair.Key, position);
                }
            }

            _profile = profile;

            var rootObject = new GameObject("MapPresentationContent");
            rootObject.transform.SetParent(transform, false);
            _presentationRoot = rootObject.transform;

            _camera = BuildCamera(profile.orthographicSize);
            BuildBackground(profile);

            var groupStates = new Dictionary<string, MapVfxState>();
            foreach (MapPreviewNode node in profile.nodes)
            {
                MapVfxState initialState = node.initialState;
                if (initialStates != null &&
                    initialStates.TryGetValue(
                        node.interactionGroupId, out MapVfxState overridden))
                {
                    initialState = overridden;
                }
                groupStates[node.interactionGroupId] = initialState;
            }

            foreach (MapPreviewNode node in profile.nodes)
            {
                MapMonsterVfxTier monsterTier = MapMonsterVfxTier.None;
                if (monsterTiers != null)
                {
                    monsterTiers.TryGetValue(
                                     node.interactionGroupId,
                                     out monsterTier);
                }
                MapNodeVisualStyle style = null;
                nodeStyles?.TryGetValue(node.contentId, out style);
                BuildNode(
                    style != null ? style.ResolveNode(node) : node,
                    groupStates[node.interactionGroupId],
                    monsterTier,
                    style?.Motion ?? MapRewardMotionKind.None);
            }

            foreach (MapGroup group in _groups.Values)
                group.CreateAvailabilityHalo();

            foreach (MapGroup group in _groups.Values)
                group.ApplyInitialState();

            _built = true;
        }

        /// <summary>
        /// 在地图内容真正揭示时重播全部一次性节点动作。场景装配只表达
        /// “地图已进入／已返回”，不接触 Animator 或序列时钟实现。
        /// </summary>
        public int ReplayOneShotSpriteSequences()
        {
            int replayed = 0;
            for (int i = 0; i < _spriteSequences.Count; i++)
            {
                MapSpriteSequenceAnimator sequence = _spriteSequences[i];
                if (sequence != null && sequence.PlayOnce())
                    replayed++;
            }
            return replayed;
        }

        private Camera BuildCamera(float orthographicSize)
        {
            var cameraObject = new GameObject(
                "MapPresentationCamera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.transform.SetParent(_presentationRoot, false);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.12f, 0.085f, 0.045f, 1f);
            SpriteCameraRenderingPolicy.Apply(camera);
            return camera;
        }

        private void BuildBackground(MapPreviewProfile profile)
        {
            var backgroundObject = new GameObject(
                "MapBackground", typeof(SpriteRenderer));
            backgroundObject.transform.SetParent(_presentationRoot, false);
            SpriteRenderer renderer =
                backgroundObject.GetComponent<SpriteRenderer>();
            renderer.sprite = profile.background;
            renderer.sortingOrder = -100;
            renderer.color = profile.backgroundTint;

            float worldHeight = _camera.orthographicSize * 2f;
            float scale = worldHeight / profile.background.bounds.size.y;
            backgroundObject.transform.localScale = Vector3.one * scale;
            _backgroundWorldSize = profile.background.bounds.size * scale;

            // 背景按高度 100% 等比绘制；节点仍以原始 16:9 核心区为坐标舞台。
            // 这样左右扩图只负责填充更宽画幅，不会改变路线、节点和命中区布局。
            _mapWorldSize = new Vector2(
                worldHeight * CoreStageAspect,
                worldHeight);

            float viewAspect = _camera != null
                ? _camera.aspect
                : CoreStageAspect;
            float viewWidth = worldHeight * viewAspect;
            if (_backgroundWorldSize.x + 0.01f < viewWidth)
            {
                Debug.LogWarning(
                    "[MapPresentation] 地图背景宽度不足以覆盖当前画幅；"
                    + "请为该楼层制作包含16:9核心区的横向扩图。"
                    + $" sprite={_backgroundWorldSize.x:F2}, view={viewWidth:F2}");
            }
        }

        private void BuildNode(
            MapPreviewNode definition,
            MapVfxState initialState,
            MapMonsterVfxTier monsterTier,
            MapRewardMotionKind rewardMotion)
        {
            if (!_groups.TryGetValue(
                    definition.interactionGroupId, out MapGroup group))
            {
                group = new MapGroup(definition.interactionGroupId, initialState);
                _groups.Add(group.Id, group);
            }

            var nodeObject = new GameObject(definition.contentId);
            nodeObject.transform.SetParent(_presentationRoot, false);
            nodeObject.transform.localPosition = NormalizedToWorld(
                ResolveNodePosition(definition));

            GameObject visualObject;
            IMapNodeStateVisual stateVisual = null;
            if (definition.stateVisualPrefab != null)
            {
                visualObject = Instantiate(
                    definition.stateVisualPrefab,
                    nodeObject.transform,
                    false);
                visualObject.name = "Visual";
                visualObject.transform.localPosition = Vector3.zero;
                visualObject.transform.localRotation = Quaternion.identity;
                // MapPreviewProfile exclusively owns map-space size. The
                // authored prefab may use another root scale in its sandbox,
                // so never multiply both scales in production.
                visualObject.transform.localScale =
                    Vector3.one * definition.visualScale;
                stateVisual = visualObject.GetComponent<
                    IMapNodeStateVisual>();
            }
            else
            {
                visualObject = new GameObject("Visual");
                visualObject.transform.SetParent(nodeObject.transform, false);
                visualObject.transform.localScale =
                    Vector3.one * definition.visualScale;
            }

            visualObject.transform.localPosition = definition.visualOffset;

            SpriteRenderer spriteRenderer = null;
            MapRewardNodeMotion motion = null;
            ProceduralMapVfx effect = null;
            Sprite resolvedSprite = definition.ResolveSprite();
            if (stateVisual == null)
            {
                switch (definition.visualKind)
                {
                    case MapPreviewVisualKind.Sprite:
                        GameObject spriteObject = visualObject;
                        if (rewardMotion != MapRewardMotionKind.None)
                        {
                            spriteObject = new GameObject("SubjectMotion");
                            spriteObject.transform.SetParent(visualObject.transform, false);
                            motion = spriteObject.AddComponent<MapRewardNodeMotion>();
                            motion.Initialize(rewardMotion,
                                definition.normalizedPosition.x * 7.3f + definition.normalizedPosition.y * 4.1f,
                                Mathf.Max(resolvedSprite.bounds.size.x, resolvedSprite.bounds.size.y));
                        }
                        spriteRenderer =
                            spriteObject.AddComponent<SpriteRenderer>();
                        spriteRenderer.sprite = resolvedSprite;
                        spriteRenderer.sortingOrder = definition.sortingOrder;
                        if (definition.spriteSequence != null &&
                            definition.spriteSequence.IsValid)
                        {
                            MapSpriteSequenceAnimator animator =
                                visualObject.AddComponent<
                                    MapSpriteSequenceAnimator>();
                            animator.Bind(
                                spriteRenderer,
                                definition.spriteSequence,
                                resolvedSprite);
                            _spriteSequences.Add(animator);
                        }
                        break;
                    case MapPreviewVisualKind.ManaPearl:
                        effect = visualObject.AddComponent<ProceduralMapVfx>();
                        effect.Initialize(MapVfxKind.ManaPearl, initialState,
                            definition.sortingOrder);
                        _effects.Add(effect);
                        break;
                    case MapPreviewVisualKind.ManaConversion:
                    case MapPreviewVisualKind.ManaPlatform:
                        var manaConversion = visualObject.AddComponent<ManaConversionMapEffect>();
                        manaConversion.Initialize(definition.sortingOrder, initialState);
                        stateVisual = manaConversion;
                        break;
                    case MapPreviewVisualKind.Seal:
                        effect = visualObject.AddComponent<ProceduralMapVfx>();
                        effect.Initialize(MapVfxKind.Seal, initialState,
                            definition.sortingOrder);
                        _effects.Add(effect);
                        break;
                }
            }

            Bounds auraBounds;
            float visibleBottomY;
            if (spriteRenderer != null && resolvedSprite != null)
            {
                // 序列动画只替换渲染帧；灵气尺寸仍使用静态 fallback，避免
                // 图集透明留白改变命中或妖气范围。个别图集可显式提供测得的
                // 脚底锚点，仅让接触阴影与实际动画脚底对齐。
                auraBounds = resolvedSprite.bounds;
                if (definition.spriteSequence != null &&
                    definition.spriteSequence.TryResolveGroundAnchorY(
                        resolvedSprite,
                        out float sequenceGroundAnchorY))
                {
                    visibleBottomY = sequenceGroundAnchorY;
                }
                else
                {
                    visibleBottomY =
                        MapMonsterGroundingSprites.ResolveVisibleBottom(
                            resolvedSprite);
                }
            }
            else
            {
                float visualScale = Mathf.Max(
                    0.01f, definition.visualScale);
                Vector2 localSize = definition.hitSize / visualScale;
                auraBounds = new Bounds(
                    Vector3.zero,
                    new Vector3(
                        Mathf.Max(0.35f, localSize.x),
                        Mathf.Max(0.35f, localSize.y),
                        0.01f));
                visibleBottomY = auraBounds.min.y;
            }

            var element = new MapElement(
                visualObject, spriteRenderer, effect, stateVisual,
                definition.visualScale,
                monsterTier != MapMonsterVfxTier.None,
                motion);
            group.Add(element);
            // 灵力节点本身已用共享 ManaSpiritInfinityEffect 表达可用、
            // 悬停与收取状态；不再叠加“其它交互节点”共用的光环，避免
            // 四套 Infinity 同时出现并破坏流动轮廓。
            if (definition.visualKind != MapPreviewVisualKind.ManaPearl &&
                definition.visualKind != MapPreviewVisualKind.ManaConversion &&
                definition.visualKind != MapPreviewVisualKind.ManaPlatform)
            {
                group.ConsiderAvailabilityAnchor(new MapAuraAnchor(
                    element,
                    visualObject.transform,
                    auraBounds,
                    visibleBottomY,
                    definition.sortingOrder,
                    monsterTier == MapMonsterVfxTier.None
                        ? MapNodeAuraKind.Standard
                        : MapNodeAuraKind.Monster,
                    monsterTier,
                    definition.visualScale,
                    spriteRenderer != null
                        ? 3
                        : stateVisual != null ? 2 : 1,
                    definition.contentId,
                    definition.contactShadow.TryResolve(
                        resolvedSprite, out Rect contactShadowRect)
                        ? contactShadowRect
                        : (Rect?)null));
            }

            BoxCollider2D collider = nodeObject.AddComponent<BoxCollider2D>();
            collider.size = definition.hitSize;
            collider.offset = definition.hitOffset;
            group.AddPointerTarget(collider);
            MapPointerTarget pointer =
                nodeObject.AddComponent<MapPointerTarget>();
            string groupId = definition.interactionGroupId;
            pointer.Initialize(
                groupId,
                definition.sortingOrder,
                () => SetGroupHovered(groupId, true),
                () => SetGroupHovered(groupId, false),
                () => TryExecuteAtScreenPosition(Input.mousePosition));
        }

        /// <summary>
        /// 统一解析一次地图点击。未来节点的碰撞框可能与当前节点重叠；Unity 的
        /// OnMouseDown 不认识 SpriteRenderer.sortingOrder，直接由命中的组件执行会让
        /// 被锁定节点抢走点击。这里重新收集光标下的所有地图节点，只在可执行组中
        /// 选择最近的节点，并用视觉层级打破距离相同的平局。
        /// </summary>
        private void TryExecuteAtScreenPosition(Vector3 screenPosition)
        {
            if (!_interactionEnabled || _camera == null ||
                _lastPointerExecutionFrame == Time.frameCount)
                return;

            Vector3 world = _camera.ScreenToWorldPoint(screenPosition);
            Collider2D[] hits = Physics2D.OverlapPointAll(
                new Vector2(world.x, world.y));

            string selectedGroupId = null;
            float selectedDistance = float.PositiveInfinity;
            int selectedSortingOrder = int.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                MapPointerTarget target =
                    hits[i].GetComponent<MapPointerTarget>();
                if (target == null ||
                    !_groups.TryGetValue(target.GroupId, out MapGroup group) ||
                    !group.CanExecute)
                    continue;

                Vector2 delta = (Vector2)hits[i].bounds.center -
                                new Vector2(world.x, world.y);
                float distance = delta.sqrMagnitude;
                bool isCloser = distance < selectedDistance - 0.0001f;
                bool winsTie = Mathf.Abs(distance - selectedDistance) <=
                               0.0001f &&
                               target.SortingOrder > selectedSortingOrder;
                if (!isCloser && !winsTie)
                    continue;

                selectedGroupId = target.GroupId;
                selectedDistance = distance;
                selectedSortingOrder = target.SortingOrder;
            }

            if (string.IsNullOrEmpty(selectedGroupId))
                return;

            _lastPointerExecutionFrame = Time.frameCount;
            InteractionIntentSignaled?.Invoke(selectedGroupId);
            ExecuteRequested?.Invoke(selectedGroupId);
        }

        private Vector3 NormalizedToWorld(Vector2 normalized)
        {
            return new Vector3(
                (normalized.x - 0.5f) * _mapWorldSize.x,
                (0.5f - normalized.y) * _mapWorldSize.y,
                0f);
        }

        /// <summary>设置某个交互组的状态，供外部按关卡会话驱动。</summary>
        public void SetGroupState(
            string groupId,
            MapVfxState state,
            bool immediate = true,
            bool pickupAvailable = false)
        {
            if (!_groups.TryGetValue(groupId, out MapGroup group)) return;
            // A completed gameplay node can retain a separate pickup. This is
            // a presentation projection only; its authoritative state stays Completed.
            group.SetBaseState(state, immediate, pickupAvailable);
            if (_hoveredGroupId == groupId && !group.CanPresentHover)
            {
                _hoveredGroupId = null;
                HoverChanged?.Invoke(null);
            }
        }

        /// <summary>播放某个交互组的破封完成演出；仅当可执行时返回 true。</summary>
        public bool TryPlayCompletion(string groupId)
        {
            // Legacy alias: primary completion only; outstanding pickups do not purify again.
            if (!_built || !_groups.TryGetValue(groupId, out MapGroup group) ||
                !group.CanComplete)
                return false;
            return TryPlayVictoryPurification(groupId, null);
        }

        /// <summary>读取某个交互组当前状态。</summary>
        public MapVfxState GetGroupState(string groupId)
        {
            if (!_groups.TryGetValue(groupId, out MapGroup group))
                return MapVfxState.Hidden;
            return group.CurrentState;
        }

        /// <summary>
        /// Plays a state-visual-owned completion after authoritative gameplay
        /// commit. Returns false when this group uses only ordinary map art.
        /// </summary>
        public bool TryPlayStateVisualCompletion(
            string groupId,
            Action onComplete)
        {
            if (!_built || !_groups.TryGetValue(groupId, out MapGroup group))
                return false;
            return group.TryPlayStateVisualCompletion(onComplete);
        }

        public bool TryPlayPickupCompletion(string groupId, Action onComplete)
        {
            return _built && _groups.TryGetValue(groupId, out MapGroup group) &&
                group.TryPlayPickupCompletion(onComplete);
        }

        public bool TryCaptureInteractionScreenAnchor(
            string groupId,
            out Vector2 screenPosition)
        {
            screenPosition = default;
            if (!_built || _camera == null ||
                !_groups.TryGetValue(groupId, out MapGroup group) ||
                !group.TryGetInteractionWorldAnchor(out Vector3 worldPosition))
                return false;

            Vector3 projected = _camera.WorldToScreenPoint(worldPosition);
            if (projected.z <= 0f)
                return false;
            screenPosition = new Vector2(projected.x, projected.y);
            return true;
        }

        /// <summary>设置交互是否启用。关闭时不响应点击。</summary>
        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            if (!enabled && !string.IsNullOrEmpty(_hoveredGroupId))
                SetGroupHovered(_hoveredGroupId, false);
        }

        /// <summary>聚焦某个组。immediate=true 时不播动画。</summary>
        public bool TryFocusGroup(string groupId, bool immediate = false, Action onComplete = null)
        {
            if (!_built || !_groups.ContainsKey(groupId))
                return false;

            _focusedGroupId = groupId;

            if (immediate)
            {
                Vector3 center = GetGroupWorldCenter(groupId);
                if (_camera != null)
                    _camera.transform.position = new Vector3(
                        center.x, center.y, -10f);
                onComplete?.Invoke();
                return true;
            }

            if (_focusCoroutine != null)
                StopCoroutine(_focusCoroutine);
            _focusCoroutine = StartCoroutine(FocusRoutine(groupId, onComplete));
            return true;
        }

        /// <summary>准备胜利返回：将节点覆盖为 Available。</summary>
        public bool TryPrepareVictoryReturn(string groupId)
        {
            if (!_built || !_groups.ContainsKey(groupId))
                return false;
            SetGroupState(groupId, MapVfxState.Available, immediate: true);
            return true;
        }

        /// <summary>播放节点配置的完成演出。</summary>
        public bool TryPlayVictoryPurification(string groupId, Action onComplete)
        {
            if (!_built || !_groups.TryGetValue(groupId, out MapGroup group))
                return false;

            MapNodeCompletionEffect flavor = MapNodeCompletionEffect.Purification;
            foreach (var node in _profile.nodes)
            {
                if (node.interactionGroupId != groupId) continue;
                flavor = node.completionEffect;
                break;
            }

            if (!group.CanComplete)
            {
                // Session synchronization may have already finished a sprite
                // group while its victory reward was open. None has no pending
                // external effect to replay; acknowledge this visual completion
                // without restoring click availability or starting another VFX.
                if (flavor != MapNodeCompletionEffect.None ||
                    !group.HasOnlySpriteElements ||
                    (group.CurrentState != MapVfxState.Completing &&
                     group.CurrentState != MapVfxState.Completed))
                    return false;
                SetGroupState(groupId, MapVfxState.Completed, immediate: true);
                onComplete?.Invoke();
                return true;
            }

            if (group.TryPlayStateVisualCompletion(onComplete))
                return true;

            bool isSpriteGroup = group.HasSpriteElements;
            if (!isSpriteGroup)
            {
                group.PlayCompletion();
                // Seal groups: wait for the seal-break animation to complete
                // (ProceduralMapVfx plays ~1.46s of breaking/releasing/fade)
                StartCoroutine(WaitForSealCompletion(group, onComplete));
                return true;
            }

            SpriteRenderer targetRenderer = null;
            foreach (var node in _profile.nodes)
            {
                if (node.interactionGroupId == groupId)
                {
                    var nodeGo = _presentationRoot.Find(node.contentId);
                    if (nodeGo != null)
                    {
                        var visual = nodeGo.Find("Visual");
                        if (visual != null)
                            targetRenderer = visual.GetComponent<SpriteRenderer>();
                    }
                    break;
                }
            }

            if (flavor == MapNodeCompletionEffect.None)
            {
                group.PlayCompletion();
                onComplete?.Invoke();
                return true;
            }

            // Sprite group — play the node-type-specific completion effect
            var purificationGo = new GameObject("MapNodeCompletionEffect");
            purificationGo.transform.SetParent(_presentationRoot, false);
            purificationGo.transform.position = GetGroupWorldCenter(groupId);

            // 普通战斗节点由完成逻辑立即隐藏本体；遗珍必须保留本体，交给
            // 收取演出做“灵光收束—提起归藏”，否则粒子刚开始本体就消失。
            group.PlayCompletion(
                hideSpriteElements:
                    flavor != MapNodeCompletionEffect.RelicCollection);

            if (flavor == MapNodeCompletionEffect.RelicCollection)
            {
                var relic =
                    purificationGo.AddComponent<MapNodeRelicCollectionEffect>();
                relic.Play(targetRenderer, _purificationDuration, () =>
                {
                    onComplete?.Invoke();
                    if (purificationGo != null)
                        Destroy(purificationGo);
                });
            }
            else
            {
                var purify =
                    purificationGo.AddComponent<MapNodePurificationEffect>();
                purify.Play(targetRenderer, _purificationDuration, () =>
                {
                    onComplete?.Invoke();
                    if (purificationGo != null)
                        Destroy(purificationGo);
                });
            }
            return true;
        }

        /// <summary>回到全图视图并突出下一可用节点。</summary>
        public void ReturnToOverview(string highlightedGroupId, Action onComplete = null)
        {
            if (!_built)
            {
                onComplete?.Invoke();
                return;
            }

            _focusedGroupId = null;

            if (_camera != null)
            {
                if (_focusCoroutine != null)
                    StopCoroutine(_focusCoroutine);
                _focusCoroutine = StartCoroutine(OverviewRoutine(onComplete));
            }
            else
            {
                onComplete?.Invoke();
            }
        }

        private IEnumerator WaitForSealCompletion(MapGroup group, Action onComplete)
        {
            // Wait for the seal break animation to reach Completed state
            float timeout = 5f;
            float elapsed = 0f;
            while (group.CurrentState != MapVfxState.Completed && elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            onComplete?.Invoke();
        }

        private IEnumerator FocusRoutine(string groupId, Action onComplete)
        {
            Vector3 center = GetGroupWorldCenter(groupId);
            Vector3 startPos = _camera.transform.position;
            Vector3 endPos = new Vector3(center.x, center.y, -10f);
            float elapsed = 0f;
            while (elapsed < _focusDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _focusDuration);
                float eased = 1f - (1f - t) * (1f - t);
                _camera.transform.position = Vector3.Lerp(startPos, endPos, eased);
                yield return null;
            }
            _camera.transform.position = endPos;
            onComplete?.Invoke();
        }

        private IEnumerator OverviewRoutine(Action onComplete)
        {
            Vector3 startPos = _camera.transform.position;
            Vector3 endPos = new Vector3(0f, 0f, -10f);
            float elapsed = 0f;
            while (elapsed < _overviewDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _overviewDuration);
                float eased = 1f - (1f - t) * (1f - t);
                _camera.transform.position = Vector3.Lerp(startPos, endPos, eased);
                yield return null;
            }
            _camera.transform.position = endPos;
            onComplete?.Invoke();
        }

        private Vector3 GetGroupWorldCenter(string groupId)
        {
            if (_profile == null) return Vector3.zero;
            foreach (var node in _profile.nodes)
            {
                if (node.interactionGroupId == groupId)
                    return NormalizedToWorld(ResolveNodePosition(node));
            }
            return Vector3.zero;
        }

        public void Clear()
        {
            _hoveredGroupId = null;
            HoverChanged?.Invoke(null);
            _groups.Clear();
            _effects.Clear();
            _spriteSequences.Clear();
            _nodePositions.Clear();
            _built = false;
            foreach (Transform child in transform)
                Destroy(child.gameObject);
        }

        private static void DrawGUIColor(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void Update()
        {
            if (!_built) return;

            float time = Time.unscaledTime;
            foreach (var kvp in _groups)
            {
                kvp.Value.CheckCompletion();
                kvp.Value.UpdatePulse(time);
            }
            if (!string.IsNullOrEmpty(_hoveredGroupId) &&
                _groups.TryGetValue(_hoveredGroupId, out MapGroup hovered) &&
                !hovered.CanPresentHover)
            {
                _hoveredGroupId = null;
                HoverChanged?.Invoke(null);
            }
        }

        // private void OnGUI()
        // {
        //     if (!_built || string.IsNullOrEmpty(_hoveredGroupId))
        //         return;
        //     if (_camera == null) return;
        //
        //     // World → screen position
        //     Vector3 screenPos = _camera.WorldToScreenPoint(_hoveredWorldPos);
        //     // Convert to GUI coordinates (Y flipped)
        //     Vector2 guiPos = new Vector2(
        //         screenPos.x,
        //         Screen.height - screenPos.y);
        //
        //     // Build label text
        //     string displayName = _hoveredContentId;
        //     if (!string.IsNullOrEmpty(displayName))
        //     {
        //         int lastDot = displayName.LastIndexOf('.');
        //         if (lastDot >= 0 && lastDot < displayName.Length - 1)
        //             displayName = displayName.Substring(lastDot + 1);
        //     }
        //     string stateLabel = _hoveredState switch
        //     {
        //         MapVfxState.Available => "可进入",
        //         MapVfxState.Locked => "未解锁",
        //         MapVfxState.Completed => "已完成",
        //         MapVfxState.Hovered => "可进入",
        //         _ => _hoveredState.ToString()
        //     };
        //     string text = $"{displayName}\n{stateLabel}";
        //
        //     // Draw tooltip with background
        //     var style = new GUIStyle(GUI.skin.label)
        //     {
        //         fontSize = 20,
        //         fontStyle = FontStyle.Bold,
        //         normal = { textColor = Color.white },
        //         alignment = TextAnchor.UpperLeft
        //     };
        //     Vector2 size = style.CalcSize(new GUIContent(text));
        //     size.x += 12f;
        //     size.y += 8f;
        //
        //     // Offset above the marker
        //     float labelX = guiPos.x - size.x * 0.5f;
        //     float labelY = guiPos.y - size.y - 20f;
        //
        //     // Clamp to screen
        //     labelX = Mathf.Clamp(labelX, 4f, Screen.width - size.x - 4f);
        //     labelY = Mathf.Clamp(labelY, 4f, Screen.height - size.y - 4f);
        //
        //     Rect bgRect = new Rect(labelX, labelY, size.x, size.y);
        //     // Background
        //     DrawGUIColor(bgRect, new Color(0.05f, 0.04f, 0.03f, 0.85f));
        //     // Border (4 sides)
        //     DrawGUIColor(new Rect(bgRect.x, bgRect.y, bgRect.width, 1f),
        //         new Color(0.9f, 0.8f, 0.7f, 0.6f));
        //     DrawGUIColor(new Rect(bgRect.x, bgRect.yMax - 1f, bgRect.width, 1f),
        //         new Color(0.9f, 0.8f, 0.7f, 0.6f));
        //     DrawGUIColor(new Rect(bgRect.x, bgRect.y, 1f, bgRect.height),
        //         new Color(0.9f, 0.8f, 0.7f, 0.6f));
        //     DrawGUIColor(new Rect(bgRect.xMax - 1f, bgRect.y, 1f, bgRect.height),
        //         new Color(0.9f, 0.8f, 0.7f, 0.6f));
        //     Rect textRect = new Rect(
        //         labelX + 6f, labelY + 4f, size.x - 12f, size.y - 8f);
        //     GUI.Label(textRect, text, style);
        // }

        private void SetGroupHovered(string groupId, bool hovered)
        {
            if (!_groups.TryGetValue(groupId, out MapGroup group)) return;
            // Spent one-use nodes lose the visual and the hover target together.
            // Only explicitly retained landmarks can still show a read-back tooltip.
            if (hovered && !group.CanPresentHover)
                return;
            bool signalsNewIntent = _interactionEnabled &&
                                    hovered &&
                                    group.CanExecute &&
                                    !string.Equals(
                                        _hoveredGroupId,
                                        groupId,
                                        StringComparison.Ordinal);
            // SetHovered 会先把 CurrentState 改成 Hovered，因此 tooltip 必须读 BaseState，
            // 否则所有节点都显示"可进入"。Completed 节点只记录悬停供回看，
            // 不改变视觉；Locked/Hidden 已在上方统一拦截。
            group.SetHovered(
                hovered,
                allowVisualChange: group.CanExecute);

            if (hovered)
            {
                bool hoverChanged = !string.Equals(
                    _hoveredGroupId, groupId, StringComparison.Ordinal);
                _hoveredGroupId = groupId;
                _hoveredState = group.BaseState;
                // Find the first element's contentId for display
                _hoveredContentId = null;
                foreach (var node in _profile.nodes)
                {
                    if (node.interactionGroupId == groupId)
                    {
                        _hoveredContentId = node.contentId;
                        _hoveredWorldPos = NormalizedToWorld(
                            ResolveNodePosition(node));
                        break;
                    }
                }

                if (signalsNewIntent)
                    InteractionIntentSignaled?.Invoke(groupId);
                if (_interactionEnabled && hoverChanged)
                    HoverChanged?.Invoke(groupId);
            }
            else if (_hoveredGroupId == groupId)
            {
                _hoveredGroupId = null;
                HoverChanged?.Invoke(null);
            }
        }

        private Vector2 ResolveNodePosition(MapPreviewNode node)
            => _nodePositions.TryGetValue(node.contentId, out Vector2 position)
                ? position : node.normalizedPosition;

        /// <summary>
        /// 复合交互组唯一拥有组内表现状态；每只怪物各自拥有灵气，
        /// 非怪物复合节点仍只选一个提示锚点。本次仅修复既有提示的
        /// 分配基数，不扩展模块职责；新增独立演出时应重新审查边界。
        /// Completed 的主体和指针投影也由本组统一拥有，不新增业务状态；
        /// 今后新增独立演出状态机或运行规则时须先拆分职责。
        /// </summary>
        private sealed class MapGroup
        {
            private readonly List<MapElement> _elements =
                new List<MapElement>();
            private MapAuraAnchor _availabilityAnchor;
            private readonly List<MapAuraAnchor> _monsterAuraAnchors =
                new List<MapAuraAnchor>();
            private readonly List<MapNodeAvailabilityHalo> _availabilityHalos =
                new List<MapNodeAvailabilityHalo>();
            private readonly List<Collider2D> _pointerTargets = new List<Collider2D>();

            public MapGroup(string id, MapVfxState initialState)
            {
                Id = id;
                BaseState = initialState;
                CurrentState = initialState;
            }

            public string Id { get; }
            public MapVfxState BaseState { get; private set; }
            public MapVfxState CurrentState { get; private set; }
            private bool _pickupAvailable;

            public bool CanPresentHover =>
                BaseState == MapVfxState.Available ||
                (BaseState == MapVfxState.Completed &&
                 (_pickupAvailable || RetainsCompletedVisual));

            private bool RetainsCompletedVisual
            {
                get
                {
                    foreach (MapElement element in _elements)
                        if (element.StateVisual != null &&
                            element.StateVisual.RetainsCompletedVisual)
                            return true;
                    return false;
                }
            }

            public bool CanComplete => BaseState == MapVfxState.Available &&
                                        CurrentState != MapVfxState.Completing &&
                                        CurrentState != MapVfxState.Completed;

            public bool CanExecute => CanComplete ||
                (BaseState == MapVfxState.Completed && _pickupAvailable);

            /// <summary>组内是否有纯 Sprite 元素（非粒子效果）。</summary>
            public bool HasSpriteElements
            {
                get
                {
                    foreach (var el in _elements)
                        if (el.Effect == null)
                            return true;
                    return false;
                }
            }

            // StateVisual and procedural elements own completion callbacks;
            // they must finish themselves even when external VFX is None.
            public bool HasOnlySpriteElements
            {
                get
                {
                    if (_elements.Count == 0) return false;
                    foreach (MapElement element in _elements)
                        if (element.StateVisual != null || element.Effect != null)
                            return false;
                    return true;
                }
            }

            public void Add(MapElement element)
            {
                _elements.Add(element);
            }

            public void AddPointerTarget(Collider2D target)
            {
                _pointerTargets.Add(target);
            }

            public void ConsiderAvailabilityAnchor(MapAuraAnchor candidate)
            {
                if (candidate == null)
                    return;

                if (candidate.IsMonsterSprite)
                    _monsterAuraAnchors.Add(candidate);

                if (_availabilityAnchor == null ||
                    candidate.IsPreferredOver(_availabilityAnchor))
                {
                    _availabilityAnchor = candidate;
                }
            }

            public void CreateAvailabilityHalo()
            {
                if (_availabilityHalos.Count > 0)
                    return;

                if (_monsterAuraAnchors.Count > 0)
                {
                    foreach (MapAuraAnchor anchor in _monsterAuraAnchors)
                        CreateHalo(anchor);
                }
                else if (_availabilityAnchor != null)
                {
                    CreateHalo(_availabilityAnchor);
                }
            }

            private void CreateHalo(MapAuraAnchor anchor)
            {
                MapNodeAvailabilityHalo halo = anchor.CreateHalo();
                if (halo == null)
                    return;
                anchor.Element.BindAvailabilityHalo(halo);
                _availabilityHalos.Add(halo);
            }

            private void SetHaloState(MapVfxState state)
            {
                foreach (MapNodeAvailabilityHalo halo in _availabilityHalos)
                    halo.SetState(state);
            }

            public void ApplyInitialState()
            {
                ApplyState(BaseState);
            }

            public void SetBaseState(MapVfxState state, bool immediate, bool pickupAvailable)
            {
                BaseState = state;
                _pickupAvailable = state == MapVfxState.Completed && pickupAvailable;
                ApplyState(state);
            }

            public void SetHovered(bool hovered, bool allowVisualChange = true)
            {
                if (_pickupAvailable)
                {
                    ApplyState(BaseState, pickupHovered: hovered && allowVisualChange);
                    return;
                }
                if (CurrentState == MapVfxState.Completing ||
                    CurrentState == MapVfxState.Completed)
                    return;
                // Locked/Completed 节点仅显示 tooltip，不改变视觉（颜色/缩放）
                if (hovered && !allowVisualChange) return;
                ApplyState(hovered ? MapVfxState.Hovered : BaseState);
            }

            public void PlayCompletion(bool hideSpriteElements = true)
            {
                if (!CanComplete) return;
                CurrentState = MapVfxState.Completing;
                SetHaloState(MapVfxState.Completing);
                foreach (MapElement element in _elements)
                    element.PlayCompletion(hideSpriteElements);
            }

            public bool TryPlayStateVisualCompletion(Action onComplete)
            {
                if (!CanComplete)
                    return false;

                foreach (MapElement element in _elements)
                {
                    if (!element.TryPlayStateVisualCompletion(onComplete))
                        continue;
                    CurrentState = MapVfxState.Completing;
                    SetHaloState(MapVfxState.Completing);
                    return true;
                }

                return false;
            }

            public bool TryPlayPickupCompletion(Action onComplete)
            {
                if (!_pickupAvailable || !CanExecute) return false;
                foreach (MapElement element in _elements)
                {
                    if (!element.TryPlayStateVisualCompletion(onComplete)) continue;
                    CurrentState = MapVfxState.Completing;
                    SetHaloState(MapVfxState.Completing);
                    return true;
                }
                return false;
            }

            public bool TryGetInteractionWorldAnchor(out Vector3 worldPosition)
            {
                foreach (MapElement element in _elements)
                {
                    if (element.TryGetInteractionWorldAnchor(out worldPosition))
                        return true;
                }
                worldPosition = default;
                return false;
            }

            /// <summary>检查所有粒子效果是否已完成，如是则标记为 Completed。</summary>
            public void CheckCompletion()
            {
                if (CurrentState != MapVfxState.Completing)
                    return;

                foreach (MapElement element in _elements)
                {
                    if (element.StateVisual != null &&
                        element.StateVisual.State != MapVfxState.Completed)
                        return;
                    if (element.Effect == null)
                        continue;
                    if (element.Effect.State != MapVfxState.Completed)
                        return;
                }

                CurrentState = MapVfxState.Completed;
                BaseState = MapVfxState.Completed;
                SetHaloState(MapVfxState.Completed);
                UpdatePointerTargets(MapVfxState.Completed);
                // Sprite collection owns its fade until its callback returns.
                // Do not interrupt that animation merely because particle
                // children have already finished; the saved state sync performs
                // the final Completed projection.
            }

            private void UpdatePointerTargets(MapVfxState state)
            {
                bool hasVisibleTarget = state != MapVfxState.Hidden &&
                    (state != MapVfxState.Completed ||
                     _pickupAvailable || RetainsCompletedVisual);
                foreach (Collider2D target in _pointerTargets)
                    target.enabled = hasVisibleTarget;
            }

            private void ApplyState(MapVfxState state, bool pickupHovered = false)
            {
                CurrentState = state;
                UpdatePointerTargets(state);
                MapVfxState visualState = _pickupAvailable
                    ? (pickupHovered ? MapVfxState.Hovered : MapVfxState.Available)
                    : state;
                bool cueVisible = visualState == MapVfxState.Available ||
                                  visualState == MapVfxState.Hovered;
                if (!cueVisible)
                    SetHaloState(visualState);
                foreach (MapElement element in _elements)
                    element.ApplyState(visualState, _pickupAvailable);
                if (cueVisible)
                    SetHaloState(visualState);
            }

            public void UpdatePulse(float time)
            {
                foreach (MapNodeAvailabilityHalo halo in _availabilityHalos)
                    halo.UpdatePulse(time);
            }
        }

        /// <summary>
        /// 单个视觉元素的稳定灵气锚点。怪物 Sprite 各自承载妖气；
        /// 其它复合节点择一承载公共提示，Sprite 优先于完整状态 Prefab，
        /// 再优先于程序化主体，避免同一非怪物主体重复叠加灵气。
        /// </summary>
        private sealed class MapAuraAnchor
        {
            private readonly Transform _parent;
            private readonly Bounds _bounds;
            private readonly float _visibleBottomY;
            private readonly int _sortingOrder;
            private readonly MapNodeAuraKind _kind;
            private readonly MapMonsterVfxTier _tier;
            private readonly float _visualScale;
            private readonly int _priority;
            private readonly string _contentId;
            private readonly Rect? _contactShadowRect;

            public MapAuraAnchor(
                MapElement element,
                Transform parent,
                Bounds bounds,
                float visibleBottomY,
                int sortingOrder,
                MapNodeAuraKind kind,
                MapMonsterVfxTier tier,
                float visualScale,
                int priority,
                string contentId,
                Rect? contactShadowRect)
            {
                Element = element;
                _parent = parent;
                _bounds = bounds;
                _visibleBottomY = visibleBottomY;
                _sortingOrder = sortingOrder;
                _kind = kind;
                _tier = tier;
                _visualScale = visualScale;
                _priority = priority;
                _contentId = contentId ?? string.Empty;
                _contactShadowRect = contactShadowRect;
            }

            public MapElement Element { get; }
            public bool IsMonsterSprite =>
                _kind == MapNodeAuraKind.Monster && _priority == 3;

            public bool IsPreferredOver(MapAuraAnchor other)
            {
                if (_priority != other._priority)
                    return _priority > other._priority;
                if (_sortingOrder != other._sortingOrder)
                    return _sortingOrder > other._sortingOrder;
                return string.CompareOrdinal(_contentId, other._contentId) < 0;
            }

            public MapNodeAvailabilityHalo CreateHalo()
            {
                return MapNodeAvailabilityHalo.Create(
                    _parent,
                    _bounds,
                    _visibleBottomY,
                    _sortingOrder,
                    _kind,
                    _tier,
                    _visualScale,
                    _contactShadowRect);
            }
        }

        /// <summary>组内的单个视觉元素。</summary>
        private sealed class MapElement
        {
            private readonly GameObject _visualObject;
            private readonly SpriteRenderer _spriteRenderer;
            private readonly IMapNodeStateVisual _stateVisual;
            private readonly float _baseScale;
            private readonly bool _isMonster;
            private readonly MapRewardNodeMotion _rewardMotion;
            private MapNodeAvailabilityHalo _availabilityHalo;

            public MapElement(
                GameObject visualObject,
                SpriteRenderer spriteRenderer,
                ProceduralMapVfx effect,
                IMapNodeStateVisual stateVisual,
                float baseScale,
                bool isMonster,
                MapRewardNodeMotion rewardMotion)
            {
                _visualObject = visualObject;
                _spriteRenderer = spriteRenderer;
                Effect = effect;
                _stateVisual = stateVisual;
                _baseScale = baseScale;
                _isMonster = isMonster;
                _rewardMotion = rewardMotion;
            }

            public ProceduralMapVfx Effect { get; }
            public IMapNodeStateVisual StateVisual => _stateVisual;

            public void BindAvailabilityHalo(
                MapNodeAvailabilityHalo availabilityHalo)
            {
                _availabilityHalo = availabilityHalo;
            }

            public void ApplyState(MapVfxState state, bool pickupAvailable = false)
            {
                _rewardMotion?.SetState(state);
                if (_stateVisual != null)
                {
                    if (_stateVisual is IMapNodePickupStateVisual pickup)
                        pickup.SetPickupAvailable(pickupAvailable);
                    // Keep native particle hierarchies active; their owner
                    // interprets Hidden and safely clears a spent one-use node.
                    MapVfxState ownedState = state == MapVfxState.Completed &&
                        !_stateVisual.RetainsCompletedVisual
                        ? MapVfxState.Hidden : state;
                    _stateVisual.SetState(ownedState, immediate: true);
                    return;
                }

                // Once bound, the Halo owns this visual's scale and foot offset.
                // The group still owns visibility/tint; it must not undo that pose.
                if (_availabilityHalo == null)
                    _visualObject.transform.localScale = Vector3.one *
                        (Effect == null && state == MapVfxState.Hovered
                            ? _baseScale * MapNodeAvailabilityHalo.MonsterHoverVisualScale
                            : _baseScale);

                if (Effect != null)
                {
                    _visualObject.SetActive(true);
                    Effect.SetState(state, true);
                    return;
                }

                bool visible = state != MapVfxState.Hidden &&
                               state != MapVfxState.Completed;
                _visualObject.SetActive(visible);
                if (!visible || _spriteRenderer == null) return;

                switch (state)
                {
                    case MapVfxState.Locked:
                        _spriteRenderer.color = _isMonster
                            ? new Color(0.34f, 0.34f, 0.34f, 0.40f)
                            : new Color(0.30f, 0.25f, 0.22f, 0.40f);
                        break;
                    case MapVfxState.Available:
                        // 怪物是否可交互已由黑紫灵气表达；主体保持原色，
                        // 避免暖黄 tint 再次破坏与战斗动作统一后的色板。
                        _spriteRenderer.color = _isMonster
                            ? Color.white
                            : new Color(1f, 0.92f, 0.78f, 1f);
                        break;
                    case MapVfxState.Hovered:
                        _spriteRenderer.color = Color.white;
                        break;
                    default:
                        _spriteRenderer.color = _isMonster
                            ? new Color(0.78f, 0.78f, 0.78f, 0.80f)
                            : new Color(0.85f, 0.75f, 0.65f, 0.80f);
                        break;
                }
            }

            public void PlayCompletion(bool hideSpriteElement)
            {
                if (_stateVisual != null)
                    _stateVisual.PlayCompletion();
                else if (Effect != null)
                    Effect.PlayCompletion();
                else if (hideSpriteElement)
                    _visualObject.SetActive(false);
            }

            public bool TryPlayStateVisualCompletion(Action onComplete)
            {
                return _stateVisual != null &&
                       _stateVisual.PlayCompletion(onComplete);
            }

            public bool TryGetInteractionWorldAnchor(out Vector3 worldPosition)
            {
                if (_stateVisual is IMapNodeInteractionAnchor anchor &&
                    anchor.TryGetInteractionWorldAnchor(out worldPosition))
                    return true;
                worldPosition = default;
                return false;
            }
        }
    }

    /// <summary>
    /// 地图点击命中适配器。只上报点击，不直接启动战斗或粒子。
    /// </summary>
    internal sealed class MapPointerTarget : MonoBehaviour
    {
        private Action _onEnter;
        private Action _onExit;
        private Action _onClick;

        public string GroupId { get; private set; }
        public int SortingOrder { get; private set; }

        public void Initialize(
            string groupId,
            int sortingOrder,
            Action onEnter,
            Action onExit,
            Action onClick)
        {
            GroupId = groupId;
            SortingOrder = sortingOrder;
            _onEnter = onEnter;
            _onExit = onExit;
            _onClick = onClick;
        }

        private void OnMouseEnter() => _onEnter?.Invoke();
        private void OnMouseExit() => _onExit?.Invoke();
        private void OnMouseDown() => _onClick?.Invoke();
    }
}
