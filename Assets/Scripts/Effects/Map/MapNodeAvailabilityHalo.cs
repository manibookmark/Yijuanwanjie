using System.Collections;
using System.Collections.Generic;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;

namespace WanjieScroll.Effects.Map
{
    /// <summary>地图节点可用提示只区分怪物与其它节点。</summary>
    public enum MapNodeAuraKind
    {
        Standard = 0,
        Monster = 1
    }

    /// <summary>
    /// 地图节点的独立灵气粒子提示。怪物使用黑紫上涌妖气，其它节点使用
    /// 青玉外缘内聚灵气；两类效果都只在 Available / Hovered 状态显示。
    /// 本类统一拥有节点灵气的布局、贴地层和原生就绪状态；生命周期修复
    /// 不增加独立职责。若新增另一类演出，应重新审查此单节点表现边界。
    /// 可选阴影矩形是构建时的局部几何快照，仅本 owner 写入阴影 Renderer；
    /// 不改变其它贴地层或 Infinity 生命周期。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapNodeAvailabilityHalo : MonoBehaviour
    {
        private const string MonsterPrefabResourcePath =
            "Effects/Map/MonsterSpiritAura";
        private const string StandardPrefabResourcePath =
            "Effects/Map/StandardSpiritAura";
        // 与 MonsterSpiritAuraAssetConfigurator 的两套源资源发射率一致。
        // 保留托管侧基准，避免读取 Infinity 1.5.1 不稳定的原生曲线 getter。
        private const float MonsterAuthoredEmissionRate = 12f;
        private const float StandardAuthoredEmissionRate = 11f;

        /// <summary>
        /// 怪物地图立绘统一倍率与悬停倍率的唯一真源。本组件只由 Create 在
        /// 运行时创建，没有预制件资产承载序列化值，因此字段默认值即实际生效值。
        /// 工具侧（如 export-map-layout-review.csx 的作者合成图）必须引用这些
        /// 常量，不得各写一份硬编码，否则验收合成图会与运行时静默漂移。
        /// </summary>
        public const float DefaultNormalMonsterSizeMultiplier = 1.10f;
        public const float NormalMonsterHoverVisualScale = 1f;
        public const float DefaultFloorBossSizeMultiplier = 1.25f;
        public const float DefaultStageBossSizeMultiplier = 1.35f;
        public const float MonsterHoverVisualScale = 1.08f;
        // playOnAwake 的粒子在就绪前停放到地图相机裁剪范围外，避免锁定
        // 节点闪光；只移动 Transform，不通过禁用对象重建 Infinity 批次。
        private const float RuntimeWarmupDepth = 100000f;
        private static readonly HashSet<MapNodeAvailabilityHalo>
            InitializedHalos = new HashSet<MapNodeAvailabilityHalo>();

        [Header("简易调节")]
        [SerializeField, Range(0.25f, 4f),
         Tooltip("全地图粒子数量倍率；Inspector 保存后两类节点共同使用。")]
        private float particleAmountMultiplier = 1.35f;

        [SerializeField, Range(0.75f, 1.4f),
         Tooltip("灵气覆盖范围的整体倍率，不影响脚底阴影。")]
        private float auraCoverageMultiplier = 1f;

        [SerializeField, Range(0.8f, 2f),
         Tooltip("所有层 BOSS 地图立绘相对各节点 visualScale 的统一倍率。")]
        private float floorBossSizeMultiplier = DefaultFloorBossSizeMultiplier;

        [SerializeField, Range(0.8f, 2f),
         Tooltip("所有关 BOSS 地图立绘相对各节点 visualScale 的统一倍率。")]
        private float stageBossSizeMultiplier = DefaultStageBossSizeMultiplier;

        [Header("怪物图片自适应")]
        [SerializeField, Tooltip("以怪物 Sprite 宽高为单位的局部偏移。Y 为负时灵气下移。")]
        private Vector2 normalizedOffset = new Vector2(0f, -0.04f);

        [SerializeField, Tooltip("灵气最终宽高相对怪物 Sprite 宽高的倍率。")]
        private Vector2 spriteSizeMultiplier = new Vector2(0.58f, 0.72f);

        [SerializeField, Min(0.01f), Tooltip("极小怪物图片使用的最小世界尺寸。")]
        private float minimumWorldSize = 0.1f;

        [Header("可用状态变化")]
        [SerializeField, Min(0f), Tooltip("默认关闭规律呼吸；保留参数用于后续人工尝试。")]
        private float pulseSpeed;

        [SerializeField, Min(0.01f), Tooltip("可用状态呼吸时的最小缩放倍率。")]
        private float pulseMinimumScale = 1f;

        [SerializeField, Min(0.01f), Tooltip("可用状态呼吸时的最大缩放倍率。")]
        private float pulseMaximumScale = 1f;

        [SerializeField, Min(0.01f), Tooltip("鼠标悬停时相对基础尺寸的缩放倍率。")]
        private float hoveredScale = 1.015f;

        [Header("渲染")]
        [SerializeField, Tooltip("相对怪物 Sprite sortingOrder 的偏移。")]
        private int sortingOrderOffset = 1;

        [Header("怪物脚底贴合层")]
        [SerializeField, Tooltip("关闭后怪物只保留灵气粒子，便于在 Play 模式快速对比。")]
        private bool showGroundingLayers;

        [SerializeField, Tooltip("为所有仍显示的怪物节点保留柔和接触阴影。")]
        private bool showContactShadow = true;

        [SerializeField, Tooltip("地面墨迹中心相对怪物脚底的位置，单位为 Sprite 宽高。")]
        private Vector2 groundStainOffsetFromFeet = new Vector2(0f, 0.015f);

        [SerializeField, Tooltip("地面墨迹宽高相对怪物 Sprite 宽高的倍率。")]
        private Vector2 groundStainSize = new Vector2(0.84f, 0.16f);

        [SerializeField]
        private Color groundStainColor =
            new Color(0.10f, 0.025f, 0.14f, 0.42f);

        [SerializeField, Tooltip("接触阴影中心相对怪物脚底的位置，单位为 Sprite 宽高。")]
        private Vector2 contactShadowOffsetFromFeet = new Vector2(0f, 0.01f);

        [SerializeField, Tooltip("脚底接触阴影宽高相对怪物 Sprite 宽高的倍率。")]
        private Vector2 contactShadowSize = new Vector2(0.54f, 0.075f);

        [SerializeField]
        private Color contactShadowColor =
            new Color(0.015f, 0.012f, 0.02f, 0.58f);

        [SerializeField, Tooltip("脚部薄雾中心相对怪物脚底的位置，单位为 Sprite 宽高。")]
        private Vector2 footVeilOffsetFromFeet = new Vector2(0f, 0.07f);

        [SerializeField, Tooltip("脚部薄雾宽高相对怪物 Sprite 宽高的倍率。")]
        private Vector2 footVeilSize = new Vector2(0.72f, 0.13f);

        [SerializeField]
        private Color footVeilColor =
            new Color(0.22f, 0.20f, 0.24f, 0.30f);

        [SerializeField, Tooltip("地面墨迹相对怪物的渲染排序偏移。")]
        private int groundStainSortingOffset = -1;

        [SerializeField, Tooltip("接触阴影相对怪物的渲染排序偏移。")]
        private int contactShadowSortingOffset = -1;

        [SerializeField, Tooltip("脚部薄雾相对怪物的渲染排序偏移；正值会遮住脚部。")]
        private int footVeilSortingOffset = 1;

        private InfinityParticleSystem _particles;
        private Transform _groundingRoot;
        private SpriteRenderer _groundStain;
        private SpriteRenderer _contactShadow;
        private SpriteRenderer _footVeil;
        private Bounds _targetBounds;
        private Rect? _contactShadowRect;
        private float _visibleBottomY;
        private float _tierScale = 1f;
        private float _authoredVisualScale = 1f;
        private Vector3 _authoredVisualPosition;
        private InfinityMinMaxCurve _baseEmissionCurve;
        private bool _hasBaseEmissionCurve;
        private MapMonsterVfxTier _tier;
        private MapNodeAuraKind _auraKind;
        private int _targetSortingLayerId;
        private int _targetSortingOrder;
        private int _appliedParticleSortingOrder = int.MinValue;
        private float _appliedParticleAmountMultiplier = float.NaN;
        private Vector3 _baseScale;
        private float _phase;
        private MapVfxState _state;
        private bool _initialized;
        private bool _runtimeReady;
        private Coroutine _runtimeActivation;

        public static MapNodeAvailabilityHalo Create(
            Transform parent,
            Bounds targetBounds,
            float visibleBottomY,
            int sortingOrder,
            MapNodeAuraKind auraKind,
            MapMonsterVfxTier tier,
            float authoredVisualScale,
            Rect? contactShadowRect = null)
        {
            if (parent == null || targetBounds.size.sqrMagnitude <= 0.0001f)
                return null;

            string prefabPath = auraKind == MapNodeAuraKind.Monster
                ? MonsterPrefabResourcePath
                : StandardPrefabResourcePath;
            GameObject prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    $"找不到地图节点灵气资源 {prefabPath}。");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.name = auraKind == MapNodeAuraKind.Monster
                ? "MonsterSpiritAura"
                : "NodeSpiritAura";
            MapNodeAvailabilityHalo halo =
                instance.GetComponent<MapNodeAvailabilityHalo>();
            if (halo == null)
            {
                Object.Destroy(instance);
                Debug.LogError(
                    "怪物节点灵气资源缺少 MapNodeAvailabilityHalo。");
                return null;
            }

            if (!halo.Initialize(
                    targetBounds,
                    visibleBottomY,
                    sortingOrder,
                    auraKind,
                    tier,
                    authoredVisualScale,
                    contactShadowRect))
            {
                Object.Destroy(instance);
                return null;
            }

            return halo;
        }

        public void SetState(MapVfxState state)
        {
            _state = state;
            ApplyMonsterVisualScale();
            bool particleVisible = state == MapVfxState.Available ||
                                   state == MapVfxState.Hovered;
            bool monsterVisualVisible = state != MapVfxState.Hidden &&
                                        state != MapVfxState.Completed;
            bool groundingVisible = showGroundingLayers && particleVisible &&
                                    _auraKind == MapNodeAuraKind.Monster;
            bool contactShadowVisible = showContactShadow &&
                                        monsterVisualVisible &&
                                        _auraKind == MapNodeAuraKind.Monster;
            ApplyLayout();
            SetGroundingVisible(
                groundingVisible,
                contactShadowVisible);
            if (_runtimeReady)
                ApplyRuntimeState();
            else
                ScheduleRuntimeActivation();
            UpdatePulse(Time.unscaledTime);
        }

        private void ApplyRuntimeState()
        {
            if (!_runtimeReady || !isActiveAndEnabled || _particles == null)
                return;

            if (_state == MapVfxState.Available ||
                _state == MapVfxState.Hovered)
            {
                ApplyParticleSorting();
                ApplyParticleAmount();
                if (!_particles.isPlaying)
                    _particles.Play();
            }
            else if (!_particles.isStopped)
            {
                _particles.Stop(
                    true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        public void UpdatePulse(float time)
        {
            if (!_initialized || !gameObject.activeSelf)
                return;

            ApplyLayout();
            if (_state != MapVfxState.Available &&
                _state != MapVfxState.Hovered)
            {
                transform.localScale = _baseScale;
                return;
            }

            float wave = 0.5f +
                         Mathf.Sin(time * pulseSpeed + _phase) * 0.5f;
            float stateScale = Mathf.Lerp(
                pulseMinimumScale,
                pulseMaximumScale,
                wave);
            if (_state == MapVfxState.Hovered)
                stateScale *= hoveredScale;
            transform.localScale = _baseScale * stateScale;
        }

        private bool Initialize(
            Bounds targetBounds,
            float visibleBottomY,
            int sortingOrder,
            MapNodeAuraKind auraKind,
            MapMonsterVfxTier tier,
            float authoredVisualScale,
            Rect? contactShadowRect)
        {
            _particles = GetComponent<InfinityParticleSystem>();
            if (_particles == null)
            {
                Debug.LogError("怪物节点灵气资源缺少 InfinityParticleSystem。");
                return false;
            }

            _targetBounds = targetBounds;
            _contactShadowRect = contactShadowRect;
            _visibleBottomY = visibleBottomY;
            _auraKind = auraKind;
            _tier = tier;
            _authoredVisualScale = Mathf.Max(0.01f, authoredVisualScale);
            _authoredVisualPosition = transform.parent != null
                ? transform.parent.localPosition : Vector3.zero;
            _tierScale = 1f + 0.08f *
                (Mathf.Clamp((int)tier, 1, 3) - 1);
            float authoredEmissionRate =
                auraKind == MapNodeAuraKind.Monster
                    ? MonsterAuthoredEmissionRate
                    : StandardAuthoredEmissionRate;
            _baseEmissionCurve =
                new InfinityMinMaxCurve(authoredEmissionRate);
            _hasBaseEmissionCurve = true;
            SpriteRenderer targetRenderer =
                transform.parent != null
                    ? transform.parent.GetComponent<SpriteRenderer>()
                    : null;
            _targetSortingLayerId = targetRenderer != null
                ? targetRenderer.sortingLayerID
                : 0;
            _targetSortingOrder = sortingOrder;
            CreateGroundingLayers();
            SetGroundingVisible(false, false);
            ApplyLayout();

            Transform parent = transform.parent;
            Vector3 nodePosition = parent != null && parent.parent != null
                ? parent.parent.localPosition
                : parent != null ? parent.localPosition : transform.localPosition;
            _phase = Mathf.Repeat(
                nodePosition.x * 0.83f + nodePosition.y * 0.57f,
                Mathf.PI * 2f);
            _initialized = true;
            InitializedHalos.Add(this);
            // 包含锁定节点在内都保持 activeSelf，隐藏请求由就绪后的 owner
            // 执行 Stop/Clear；不在 Instantiate/OnEnable 同栈访问原生模块。
            ScheduleRuntimeActivation();
            return true;
        }

        private void ApplyLayout()
        {
            Vector3 size = _targetBounds.size;
            transform.localPosition = _targetBounds.center + new Vector3(
                size.x * normalizedOffset.x,
                size.y * normalizedOffset.y,
                _runtimeReady ? 0f : RuntimeWarmupDepth);
            _baseScale = new Vector3(
                Mathf.Max(
                    minimumWorldSize,
                    size.x * Mathf.Max(0.01f, spriteSizeMultiplier.x)) *
                    _tierScale * auraCoverageMultiplier,
                Mathf.Max(
                    minimumWorldSize,
                    size.y * Mathf.Max(0.01f, spriteSizeMultiplier.y)) *
                    _tierScale * auraCoverageMultiplier,
                1f);

            ApplyLayerLayout(
                _groundStain,
                groundStainOffsetFromFeet,
                groundStainSize,
                groundStainColor,
                groundStainSortingOffset);
            ApplyContactShadowLayout();
            ApplyLayerLayout(
                _footVeil,
                footVeilOffsetFromFeet,
                footVeilSize,
                footVeilColor,
                footVeilSortingOffset);

        }

        private void ApplyContactShadowLayout()
        {
            if (!_contactShadowRect.HasValue)
            {
                ApplyLayerLayout(
                    _contactShadow,
                    contactShadowOffsetFromFeet,
                    contactShadowSize,
                    contactShadowColor,
                    contactShadowSortingOffset);
                return;
            }

            if (_contactShadow == null || _contactShadow.sprite == null)
                return;

            Rect rect = _contactShadowRect.Value;
            _contactShadow.transform.localPosition =
                new Vector3(rect.center.x, rect.center.y, 0f);
            Vector2 spriteSize = _contactShadow.sprite.bounds.size;
            _contactShadow.transform.localScale = new Vector3(
                rect.width / Mathf.Max(0.001f, spriteSize.x),
                rect.height / Mathf.Max(0.001f, spriteSize.y),
                1f);
            _contactShadow.color = MapMonsterGroundingSprites.WithStateAlpha(
                contactShadowColor, _state);
            _contactShadow.sortingLayerID = _targetSortingLayerId;
            _contactShadow.sortingOrder =
                _targetSortingOrder + contactShadowSortingOffset;
        }

        private void ApplyParticleSorting()
        {
            if (!_runtimeReady || _particles == null ||
                !_particles.gameObject.activeInHierarchy)
                return;

            int finalSortingOrder =
                _targetSortingOrder + sortingOrderOffset;
            if (_appliedParticleSortingOrder == finalSortingOrder)
                return;

            var renderer = _particles.renderer;
            renderer.sortingOrder = finalSortingOrder;
            renderer.customBatchKey = finalSortingOrder;
            _appliedParticleSortingOrder = finalSortingOrder;
        }

        private void CreateGroundingLayers()
        {
            if (_groundingRoot != null)
                return;

            if (_auraKind != MapNodeAuraKind.Monster)
                return;

            Transform parent = transform.parent;
            if (parent == null)
                return;

            var rootObject = new GameObject("MonsterAuraGroundingRoot");
            rootObject.transform.SetParent(parent, false);
            _groundingRoot = rootObject.transform;
            _groundStain = CreateLayer(
                "GroundInkStain", MapMonsterGroundingSprites.SoftInk);
            _contactShadow = CreateLayer(
                "ContactShadow", MapMonsterGroundingSprites.SoftInk);
            _footVeil = CreateLayer(
                "FootVeil", MapMonsterGroundingSprites.FootVeil);
        }

        private SpriteRenderer CreateLayer(string name, Sprite sprite)
        {
            var layerObject = new GameObject(name, typeof(SpriteRenderer));
            layerObject.transform.SetParent(_groundingRoot, false);
            SpriteRenderer renderer =
                layerObject.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }

        private void ApplyLayerLayout(
            SpriteRenderer renderer,
            Vector2 offsetFromFeet,
            Vector2 relativeSize,
            Color baseColor,
            int sortingOffset)
        {
            if (renderer == null || renderer.sprite == null)
                return;

            Vector3 targetSize = _targetBounds.size;
            renderer.transform.localPosition = new Vector3(
                _targetBounds.center.x +
                targetSize.x * offsetFromFeet.x,
                _visibleBottomY +
                targetSize.y * offsetFromFeet.y,
                0f);

            Vector2 spriteSize = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(
                targetSize.x * relativeSize.x /
                Mathf.Max(0.001f, spriteSize.x),
                targetSize.y * relativeSize.y /
                Mathf.Max(0.001f, spriteSize.y),
                1f);
            renderer.color = MapMonsterGroundingSprites.WithStateAlpha(
                baseColor, _state);
            renderer.sortingLayerID = _targetSortingLayerId;
            renderer.sortingOrder = _targetSortingOrder + sortingOffset;
        }

        /// <summary>供简易 Inspector 在 Play 中即时刷新，不会重启粒子。</summary>
        public void ApplyInspectorTuning(bool updateParticleAmount = true)
        {
            if (!_initialized)
                return;

            ApplyLayout();
            if (updateParticleAmount)
                ApplyParticleAmount();
            ApplyMonsterVisualScale();
            UpdatePulse(Time.unscaledTime);
        }

        /// <summary>
        /// Play 模式 Inspector 的全局调节入口。尚未就绪的节点只更新托管
        /// 参数，后续由生命周期 owner 安全应用；不临时启停 Infinity。
        /// </summary>
        public static void ApplyGlobalInspectorTuning(
            MapNodeAvailabilityHalo source,
            bool updateParticleAmount = true)
        {
            if (source == null)
                return;

            var snapshot = new List<MapNodeAvailabilityHalo>(
                InitializedHalos);
            foreach (MapNodeAvailabilityHalo halo in snapshot)
            {
                if (halo == null)
                {
                    InitializedHalos.Remove(halo);
                    continue;
                }

                halo.particleAmountMultiplier =
                    source.particleAmountMultiplier;
                halo.auraCoverageMultiplier =
                    source.auraCoverageMultiplier;
                halo.hoveredScale = source.hoveredScale;
                halo.floorBossSizeMultiplier =
                    source.floorBossSizeMultiplier;
                halo.stageBossSizeMultiplier =
                    source.stageBossSizeMultiplier;
                halo.ApplyInspectorTuning(updateParticleAmount);
            }
        }

        public float MonsterVisualScaleMultiplier => _auraKind != MapNodeAuraKind.Monster
            ? 1f : _tier switch
        {
            MapMonsterVfxTier.Normal => DefaultNormalMonsterSizeMultiplier,
            MapMonsterVfxTier.FloorBoss => floorBossSizeMultiplier,
            MapMonsterVfxTier.StageBoss => stageBossSizeMultiplier,
            _ => 1f
        };

        private void ApplyMonsterVisualScale()
        {
            if (transform.parent == null)
                return;

            float hoverMultiplier = _state != MapVfxState.Hovered ? 1f :
                _auraKind == MapNodeAuraKind.Monster && _tier == MapMonsterVfxTier.Normal
                    ? NormalMonsterHoverVisualScale : MonsterHoverVisualScale;
            float multiplier = MonsterVisualScaleMultiplier * hoverMultiplier;
            transform.parent.localScale = Vector3.one * _authoredVisualScale * multiplier;
            // Ordinary sprite enlargement pivots around the authored foot,
            // leaving the logical node and its collider exactly where they are.
            // Bosses retain their existing calibrated centre-based scaling.
            if (_auraKind == MapNodeAuraKind.Monster && _tier == MapMonsterVfxTier.Normal)
                transform.parent.localPosition = _authoredVisualPosition +
                    Vector3.up * (_visibleBottomY * _authoredVisualScale * (1f - multiplier));
        }

        private void ApplyParticleAmount()
        {
            if (!_runtimeReady || _particles == null ||
                !_hasBaseEmissionCurve ||
                !gameObject.activeInHierarchy)
                return;

            float tierMultiplier = _tier switch
            {
                MapMonsterVfxTier.FloorBoss => 1.4f,
                MapMonsterVfxTier.StageBoss => 1.8f,
                _ => 1f
            };
            float finalMultiplier =
                Mathf.Clamp(particleAmountMultiplier, 0.25f, 4f) *
                tierMultiplier;
            if (_appliedParticleAmountMultiplier == finalMultiplier)
                return;
            var emission = _particles.emission;
            InfinityMinMaxCurve curve = _baseEmissionCurve;
            curve.constantMin =
                _baseEmissionCurve.constantMin * finalMultiplier;
            curve.constantMax =
                _baseEmissionCurve.constantMax * finalMultiplier;
            emission.rateOverTime = curve;
            _appliedParticleAmountMultiplier = finalMultiplier;
        }

        private void SetGroundingVisible(
            bool decorativeLayersVisible,
            bool contactShadowVisible)
        {
            if (_groundingRoot == null)
                return;

            _groundingRoot.gameObject.SetActive(
                decorativeLayersVisible || contactShadowVisible);
            if (_groundStain != null)
                _groundStain.gameObject.SetActive(decorativeLayersVisible);
            if (_contactShadow != null)
                _contactShadow.gameObject.SetActive(contactShadowVisible);
            if (_footVeil != null)
                _footVeil.gameObject.SetActive(decorativeLayersVisible);
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                ApplyLayout();
                ScheduleRuntimeActivation();
            }
        }

        private void OnDisable()
        {
            if (_runtimeActivation != null)
            {
                StopCoroutine(_runtimeActivation);
                _runtimeActivation = null;
            }
            _runtimeReady = false;
            _appliedParticleSortingOrder = int.MinValue;
            _appliedParticleAmountMultiplier = float.NaN;
        }

        private void ScheduleRuntimeActivation()
        {
            if (!Application.isPlaying || !_initialized || _runtimeReady ||
                !isActiveAndEnabled || _runtimeActivation != null)
                return;
            _runtimeActivation = StartCoroutine(ActivateRuntimeAfterEnable());
        }

        private IEnumerator ActivateRuntimeAfterEnable()
        {
            yield return null;
            yield return null;
            _runtimeActivation = null;
            if (!isActiveAndEnabled || _particles == null)
                yield break;

            _runtimeReady = true;
            // 清除 playOnAwake 在停放位置发出的粒子，再回到节点位置。
            // 等待期间最后一次 SetState 的意图才是当前目标状态。
            _particles.Stop(
                true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ApplyLayout();
            ApplyRuntimeState();
            UpdatePulse(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            InitializedHalos.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInitializedHalos()
        {
            InitializedHalos.Clear();
        }

        private void OnValidate()
        {
            minimumWorldSize = Mathf.Max(0.01f, minimumWorldSize);
            particleAmountMultiplier = Mathf.Clamp(
                particleAmountMultiplier, 0.25f, 4f);
            auraCoverageMultiplier = Mathf.Clamp(
                auraCoverageMultiplier, 0.75f, 1.4f);
            floorBossSizeMultiplier = Mathf.Clamp(
                floorBossSizeMultiplier, 0.8f, 2f);
            stageBossSizeMultiplier = Mathf.Clamp(
                stageBossSizeMultiplier, 0.8f, 2f);
            spriteSizeMultiplier.x = Mathf.Max(
                0.01f, spriteSizeMultiplier.x);
            spriteSizeMultiplier.y = Mathf.Max(
                0.01f, spriteSizeMultiplier.y);
            pulseSpeed = Mathf.Max(0f, pulseSpeed);
            pulseMinimumScale = Mathf.Max(0.01f, pulseMinimumScale);
            pulseMaximumScale = Mathf.Max(
                pulseMinimumScale, pulseMaximumScale);
            hoveredScale = Mathf.Max(0.01f, hoveredScale);
            groundStainSize.x = Mathf.Max(0.01f, groundStainSize.x);
            groundStainSize.y = Mathf.Max(0.01f, groundStainSize.y);
            contactShadowSize.x = Mathf.Max(0.01f, contactShadowSize.x);
            contactShadowSize.y = Mathf.Max(0.01f, contactShadowSize.y);
            footVeilSize.x = Mathf.Max(0.01f, footVeilSize.x);
            footVeilSize.y = Mathf.Max(0.01f, footVeilSize.y);

            if (Application.isPlaying && _initialized)
                SetState(_state);
        }
    }
}
