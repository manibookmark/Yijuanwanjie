using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using WanjieScroll.Core;
using WanjieScroll.Effects.Projectiles;
using WanjieScroll.Effects.Terrain;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 战斗视觉特效中央管理器
    /// 统一协调投射物、击中粒子、伤害数字、角色反馈、顿帧
    /// 支持 VfxProfile 驱动的逐卡特效配置
    ///
    /// 规模审查：本存量门面已超过 1200 行，因此 BOSS 预热的资源请求、取消
    /// 代次与 Infinity 原生生命周期全部下沉到独立深模块；本类只保留 Begin、
    /// Ready 与 Prepare 三个组合入口，不拥有入场状态。后续新增特效必须继续
    /// 独立封装，本类只保留窄入口。
    /// </summary>
    public class VisualEffectManager : MonoBehaviour
    {
        public static VisualEffectManager Instance { get; private set; }

        [Header("场景引用")]
        [Tooltip("玩家卡牌攻击与敌方命中反馈的统一世界坐标起点；不是驻场英灵位置。")]
        [FormerlySerializedAs("heroTransform")]
        public Transform playerAttackOrigin;
        public List<Transform> enemyTransforms = new List<Transform>();
        private readonly List<Transform> _enemyEntranceMotionRoots =
            new List<Transform>();
        public Transform projectileContainer;
        public Transform effectContainer;
        public TerrainEffectPresenter terrainEffectPresenter;

        [Header("投射物默认参数")]
        public Sprite defaultProjectileSprite;
        public float defaultFlightDuration = 0.35f;
        public float defaultProjectileScale = 0.1f;
        public float multiProjectileDelay = 0.06f;

        [Header("击中粒子参数（默认/回退用）")]
        public Sprite particleTexture;
        [Tooltip("多发投射物逐发命中的火花纹理。")]
        public Sprite sparkParticleTexture;
        public int hitParticleCount = 20;
        public float hitParticleSpeed = 2f;
        public float hitParticleDuration = 0.5f;
        public float hitParticleStartSize = 0.06f;

        [Header("伤害数字参数")]
        public float damageNumberOffset = 0f;
        public float damageNumberLifetime = 0.9f;

        [Header("角色反馈")]
        public float punchScaleIntensity = 0.2f;
        public float punchScaleDuration = 0.12f;

        [Header("顿帧")]
        public float hitStopDuration = 0.05f;
        public float hitStopTimeScale = 0.05f;

        // 对象池
        private Stack<GameObject> _projectilePool = new Stack<GameObject>();
        private Stack<GameObject> _hitEffectPool = new Stack<GameObject>();
        private Queue<DamageNumber> _damageNumberPool = new Queue<DamageNumber>();
        private Dictionary<Transform, Vector3> _originalScales = new Dictionary<Transform, Vector3>();
        private bool _hitStopActive;
        private bool _levelUpPaused;
        private System.IDisposable _hitStopLease;
        private System.IDisposable _levelUpLease;
        private Coroutine _hitStopRoutine;

        private static Material _hitParticleMaterial;
        private static Material _sparkParticleMaterial;

        // VfxProfile 驱动的动态粒子系统缓存
        private Dictionary<Sprite, ParticleSystem> _profileHitPS = new Dictionary<Sprite, ParticleSystem>();
        private Dictionary<Sprite, ParticleSystem> _profileSecondaryPS = new Dictionary<Sprite, ParticleSystem>();
        private Dictionary<Sprite, Material> _profileMaterials = new Dictionary<Sprite, Material>();

        // 命中音效缓存（path → clip）
        private static Dictionary<string, AudioClip> _sfxCache = new Dictionary<string, AudioClip>();
        private IEffectOneShotAudio _oneShotAudio;
        private BossInfinityParticleWarmup _bossEntranceParticleWarmup;
        private BossEntranceEffectPreset _waterEntrancePreset;
        private CardSelfEffectPlayer _cardSelfEffectPlayer;
        public void SetOneShotAudio(IEffectOneShotAudio oneShotAudio)
        {
            _oneShotAudio = oneShotAudio;
            terrainEffectPresenter?.SetOneShotAudio(oneShotAudio);
        }

        /// <summary>
        /// 播放非伤害卡牌的程序化自作用演出。具体几何、动画和清理由独立
        /// CardSelfEffectPlayer 拥有，本管理器只负责场景生命周期与落点。
        /// </summary>
        public void PlayCardSelfEffect(
            Vector3 worldPosition,
            VfxProfile profile,
            string gainLabel,
            IReadOnlyList<Vector3> destinations = null,
            int manaGained = 0)
        {
            if (profile == null ||
                profile.selfEffectStyle == CardSelfEffectStyle.None)
            {
                return;
            }
            if (_cardSelfEffectPlayer == null)
            {
                var host = new GameObject("CardSelfEffectPlayer");
                host.transform.SetParent(effectContainer, false);
                _cardSelfEffectPlayer =
                    host.AddComponent<CardSelfEffectPlayer>();
            }
            _cardSelfEffectPlayer.Play(worldPosition, profile, gainLabel, destinations, manaGained);
        }

        /// <summary>Manifest one support artifact, then hand its actual origin
        /// to the injected HUD receipt. This module never knows UI components.</summary>
        public bool PlaySupportArtifact(Vector3 worldPosition, VfxProfile profile,
            int manaGained, System.Action<Vector3> onManifested)
        {
            if (!isActiveAndEnabled || profile == null) return false;
            if (_cardSelfEffectPlayer == null)
            {
                var host = new GameObject("CardSelfEffectPlayer");
                host.transform.SetParent(effectContainer != null ? effectContainer : transform, false);
                _cardSelfEffectPlayer = host.AddComponent<CardSelfEffectPlayer>();
            }
            return _cardSelfEffectPlayer.PlayArtifact(worldPosition, profile, manaGained, onManifested);
        }

        /// <summary>
        /// Retains the checkpoint's pause intent. Core composes its clock lease
        /// with hit-stop and Esc, so releasing one owner cannot resume another.
        /// </summary>
        public void SetLevelUpPaused(bool paused)
        {
            _levelUpPaused = paused;
            if (paused && isActiveAndEnabled)
                _levelUpLease ??= PresentationTimeScale.Shared.Hold(0f);
            else
                ReleaseLevelUpLease();
        }

        private void Awake()
        {
            Instance = this;
            if (projectileContainer == null)
            {
                var go = new GameObject("ProjectilePool");
                go.transform.SetParent(transform, false);
                projectileContainer = go.transform;
            }
            if (effectContainer == null)
            {
                var go = new GameObject("EffectPool");
                go.transform.SetParent(transform, false);
                effectContainer = go.transform;
            }
            if (terrainEffectPresenter == null)
                terrainEffectPresenter = gameObject.AddComponent<TerrainEffectPresenter>();
            terrainEffectPresenter.SetVisualRoot(effectContainer);

            // 程序化填充粒子纹理
            if (particleTexture == null)
                particleTexture = RuntimeArtLoader.GetBloodParticle();
            if (sparkParticleTexture == null)
                sparkParticleTexture = RuntimeArtLoader.GetSparkParticle();
            CreatePooledParticleSystems();
        }

        // ==================== 投射物 ====================

        /// <summary>
        /// 发射单个投射物到指定敌人（VfxProfile 驱动）
        /// </summary>
        public void FireProjectile(int targetIndex, System.Action onArrive, VfxProfile profile)
        {
            Transform target = GetEnemyTransform(targetIndex);
            if (target == null || playerAttackOrigin == null)
            {
                onArrive?.Invoke();
                return;
            }

            var proj = CreateProjectile(profile);
            var controller = proj.GetComponent<ProjectileController>();
            Vector3 end = target.position + new Vector3(-0.3f, 0, 0);

            float duration = profile != null ? profile.flightDuration : defaultFlightDuration;
            bool arc = profile != null && profile.flightArc;
            float arcHeight = profile != null ? profile.flightArcHeight : 0f;

            StartCoroutine(controller.Fly(
                playerAttackOrigin.position, end, duration,
                arc: arc, arcHeight: arcHeight,
                onArrive: onArrive
            ));
        }

        /// <summary>
        /// Fires toward a position captured when Core accepted the card. This
        /// keeps projectile aim stable while the live formation immediately
        /// backfills and remains available to later card plays.
        /// </summary>
        public void FireProjectileAtPosition(
            int targetIndex,
            Vector3 targetPosition,
            System.Action onArrive,
            VfxProfile profile,
            AttackEffectCompletion completion = null)
        {
            if (playerAttackOrigin == null || targetIndex < 0)
            {
                onArrive?.Invoke();
                return;
            }

            var proj = CreateProjectile(profile);
            var controller = proj.GetComponent<ProjectileController>();
            Vector3 end = targetPosition + new Vector3(-0.3f, 0f, 0f);
            float duration = profile != null
                ? profile.flightDuration
                : defaultFlightDuration;
            bool arc = profile != null && profile.flightArc;
            float arcHeight = profile != null
                ? profile.flightArcHeight
                : 0f;
            StartCoroutine(controller.Fly(
                playerAttackOrigin.position,
                end,
                duration,
                arc,
                arcHeight,
                onArrive, completion));
        }

        /// <summary>
        /// Fires one pooled projectile between two recorded world positions.
        /// Authoritative Direct→Bounce presentation uses this narrow primitive
        /// so chain timing stays outside the manager while pooling remains here.
        /// </summary>
        internal void FireProjectileBetweenPositions(
            Vector3 startPosition,
            Vector3 targetPosition,
            VfxProfile profile,
            bool isBounce,
            System.Action onArrive,
            AttackEffectCompletion completion = null)
        {
            float configuredBounceScale = profile != null &&
                profile.bounceScaleMultiplier > 0f
                    ? profile.bounceScaleMultiplier
                    : 0.72f;
            float scaleMultiplier = isBounce
                ? configuredBounceScale
                : 1f;
            GameObject projectile = CreateProjectile(
                profile,
                scaleMultiplier);
            ProjectileController controller =
                projectile.GetComponent<ProjectileController>();
            float baseDuration = profile != null
                ? profile.flightDuration
                : defaultFlightDuration;
            float duration = Mathf.Max(
                0.04f,
                baseDuration * (isBounce ? 0.62f : 1f));
            bool arc = profile != null && profile.flightArc;
            float arcHeight = profile != null
                ? profile.flightArcHeight * (isBounce ? 0.56f : 1f)
                : 0f;

            StartCoroutine(controller.Fly(
                startPosition,
                targetPosition,
                duration,
                arc,
                arcHeight,
                () =>
                {
                    PlaySmallHitEffect(targetPosition, profile, completion: completion);
                    onArrive?.Invoke();
                }, completion));
        }

        /// <summary>
        /// 多发齐射（VfxProfile 驱动）
        /// </summary>
        public void FireMultiShot(int targetIndex, int count, VfxProfile profile, System.Action onAllArrived)
        {
            StartCoroutine(FireMultiShotRoutine(targetIndex, count, profile, onAllArrived));
        }

        /// <summary>
        /// 多发齐射（分布式目标，VfxProfile 驱动）
        /// onEachArrive: 每发到达时回调（参数=目标索引）
        /// onAllArrived: 全部到达后回调
        /// </summary>
        public void FireMultiShot(int[] targetIndices, VfxProfile profile,
            System.Action<int> onEachArrive, System.Action onAllArrived)
        {
            StartCoroutine(FireMultiShotDistributedRoutine(targetIndices, profile, onEachArrive, onAllArrived));
        }

        public void FireMultiShotAtPositions(
            int[] targetIndices,
            Vector3[] targetPositions,
            VfxProfile profile,
            System.Action<int> onEachArrive,
            System.Action onAllArrived,
            AttackEffectCompletion completion = null)
        {
            StartCoroutine(FireMultiShotAtPositionsRoutine(
                targetIndices,
                targetPositions,
                profile,
                onEachArrive,
                onAllArrived, completion));
        }

        /// <summary>
        /// 分组弧线连投。每个分组代表一次攻击，只生成一把投射物；抵达后对本组全部
        /// Direct Target 播放命中反馈。用于“攻击 N 次、每次命中 M 个目标”的重型武器。
        /// </summary>
        public void FireArcVolley(IReadOnlyList<int[]> targetGroups,
            VfxProfile profile, System.Action<int> onEachTargetArrive,
            System.Action onAllArrived,
            AttackEffectCompletion completion = null)
        {
            StartCoroutine(FireArcVolleyRoutine(
                targetGroups, null, profile,
                onEachTargetArrive, onAllArrived, completion));
        }

        /// <summary>
        /// Fires a grouped arc volley at the committed positions captured from
        /// authoritative damage events. Target groups remain authoritative for
        /// impact callbacks; fixed landing positions prevent death/backfill
        /// presentation from moving an already committed projectile.
        /// </summary>
        public void FireArcVolley(
            IReadOnlyList<int[]> targetGroups,
            IReadOnlyList<Vector3> landingPositions,
            VfxProfile profile,
            System.Action<int> onEachTargetArrive,
            System.Action onAllArrived,
            AttackEffectCompletion completion = null)
        {
            StartCoroutine(FireArcVolleyRoutine(
                targetGroups, landingPositions, profile,
                onEachTargetArrive, onAllArrived, completion));
        }

        // 旧签名兼容（回退）
        public void FireProjectile(int targetIndex, System.Action onArrive, Color projectileColor)
            => FireProjectile(targetIndex, onArrive, (VfxProfile)null);

        public void FireMultiShot(int targetIndex, int count, Color projectileColor, System.Action onAllArrived)
            => FireMultiShot(targetIndex, count, (VfxProfile)null, onAllArrived);

        public void FireMultiShot(int[] targetIndices, Color projectileColor, System.Action onAllArrived)
            => FireMultiShot(targetIndices, (VfxProfile)null, null, onAllArrived);

        private IEnumerator FireMultiShotDistributedRoutine(int[] targets, VfxProfile profile,
            System.Action<int> onEachArrive, System.Action onAllArrived)
        {
            int count = targets.Length;
            int arrived = 0;
            int fired = 0;
            float delay = profile != null ? profile.multiProjectileDelay : 0.06f;

            Vector3 origin = playerAttackOrigin != null
                ? playerAttackOrigin.position
                : Vector3.zero;
            float flightTime = profile != null ? profile.flightDuration : 0.22f;

            for (int i = 0; i < count; i++)
            {
                Transform target = GetEnemyTransform(targets[i]);
                if (target == null)
                    continue;

                var proj = CreateProjectile(profile);
                var controller = proj.GetComponent<ProjectileController>();

                float spreadAngle = count <= 1 ? 0f : Mathf.Lerp(-25f, 25f, (float)i / (count - 1));
                float spreadRad = spreadAngle * Mathf.Deg2Rad;
                Vector3 startOffset = new Vector3(
                    Mathf.Sin(spreadRad) * 0.3f,
                    Mathf.Cos(spreadRad) * 0.1f - 0.1f, 0);
                Vector3 start = origin + startOffset;

                Vector3 end = target.position + new Vector3(
                    Random.Range(-0.15f, 0.15f),
                    Random.Range(-0.15f, 0.15f), 0);

                fired++;
                int capturedTargetIndex = targets[i];
                VfxProfile capturedProfile = profile;

                StartCoroutine(controller.Fly(start, end, flightTime,
                    arc: true, arcHeight: 0.4f, onArrive: () =>
                    {
                        // 每发到达时立即对该目标播放命中效果
                        PlaySmallHitEffect(end, capturedProfile);
                        onEachArrive?.Invoke(capturedTargetIndex);
                        arrived++;
                        if (arrived >= fired)
                            onAllArrived?.Invoke();
                    }));

                // 发射间隔（演出表节奏：0/45/120/165/220ms）
                if (i < count - 1)
                    yield return new WaitForSeconds(delay);
            }

            if (fired == 0)
                onAllArrived?.Invoke();
        }

        private IEnumerator FireMultiShotAtPositionsRoutine(
            int[] targets,
            Vector3[] positions,
            VfxProfile profile,
            System.Action<int> onEachArrive,
            System.Action onAllArrived,
            AttackEffectCompletion completion)
        {
            int count = targets?.Length ?? 0;
            if (count == 0 || playerAttackOrigin == null)
            {
                onAllArrived?.Invoke();
                yield break;
            }

            int arrived = 0;
            float delay = profile != null
                ? profile.multiProjectileDelay
                : 0.06f;
            float flightTime = profile != null
                ? profile.flightDuration
                : 0.22f;
            Vector3 origin = playerAttackOrigin.position;
            for (int i = 0; i < count; i++)
            {
                if (completion != null && completion.IsCancelled) yield break;
                float spreadAngle = count <= 1
                    ? 0f
                    : Mathf.Lerp(-25f, 25f, (float)i / (count - 1));
                float spreadRad = spreadAngle * Mathf.Deg2Rad;
                Vector3 start = origin + new Vector3(
                    Mathf.Sin(spreadRad) * 0.3f,
                    Mathf.Cos(spreadRad) * 0.1f - 0.1f,
                    0f);
                Vector3 basePosition = positions != null && i < positions.Length
                    ? positions[i]
                    : Vector3.zero;
                Vector3 end = basePosition + new Vector3(
                    Random.Range(-0.15f, 0.15f),
                    Random.Range(-0.15f, 0.15f),
                    0f);
                var projectile = CreateProjectile(profile);
                var controller = projectile.GetComponent<ProjectileController>();
                int capturedTarget = targets[i];
                Vector3 capturedEnd = end;
                VfxProfile capturedProfile = profile;
                StartCoroutine(controller.Fly(
                    start,
                    end,
                    flightTime,
                    arc: true,
                    arcHeight: 0.4f,
                    onArrive: () =>
                    {
                        PlaySmallHitEffect(capturedEnd, capturedProfile, completion: completion);
                        onEachArrive?.Invoke(capturedTarget);
                        arrived++;
                        if (arrived >= count)
                            onAllArrived?.Invoke();
                    }, completion));
                if (i < count - 1)
                    yield return new WaitForSeconds(delay);
            }
        }

        private IEnumerator FireMultiShotRoutine(int targetIndex, int count, VfxProfile profile, System.Action onAllArrived)
        {
            int arrived = 0;
            int fired = 0;
            float delay = profile != null ? profile.multiProjectileDelay : multiProjectileDelay;

            for (int i = 0; i < count; i++)
            {
                Transform target = GetEnemyTransform(targetIndex);
                if (target == null || playerAttackOrigin == null)
                    break;

                var proj = CreateProjectile(profile);
                var controller = proj.GetComponent<ProjectileController>();

                Vector3 start = playerAttackOrigin.position +
                    new Vector3(0, Random.Range(-0.1f, 0.1f), 0);
                Vector3 end = target.position + new Vector3(
                    -0.3f + Random.Range(-0.1f, 0.1f),
                    Random.Range(-0.2f, 0.2f), 0);

                fired++;
                Vector3 capturedEnd = end;
                VfxProfile capturedProfile = profile;

                float duration = profile != null ? profile.flightDuration : defaultFlightDuration;

                StartCoroutine(controller.Fly(start, end, duration,
                    arc: false, arcHeight: 0f, onArrive: () =>
                    {
                        PlaySmallHitEffect(capturedEnd, capturedProfile);
                        arrived++;
                        if (arrived >= count)
                            onAllArrived?.Invoke();
                    }));

                if (i < count - 1)
                    yield return new WaitForSeconds(delay);
            }

            if (fired == 0)
                onAllArrived?.Invoke();
        }

        private IEnumerator FireArcVolleyRoutine(
            IReadOnlyList<int[]> targetGroups,
            IReadOnlyList<Vector3> landingPositions,
            VfxProfile profile,
            System.Action<int> onEachTargetArrive,
            System.Action onAllArrived,
            AttackEffectCompletion completion)
        {
            if (targetGroups == null || targetGroups.Count == 0 ||
                playerAttackOrigin == null)
            {
                onAllArrived?.Invoke();
                yield break;
            }

            int validGroupCount = 0;
            for (int i = 0; i < targetGroups.Count; i++)
            {
                int[] group = targetGroups[i];
                if (group == null) continue;
                for (int j = 0; j < group.Length; j++)
                {
                    if (GetEnemyTransform(group[j]) == null) continue;
                    validGroupCount++;
                    break;
                }
            }

            if (validGroupCount == 0)
            {
                onAllArrived?.Invoke();
                yield break;
            }

            int arrivedGroups = 0;
            float delay = profile != null
                ? profile.multiProjectileDelay : multiProjectileDelay;
            float duration = profile != null
                ? profile.flightDuration : defaultFlightDuration;
            float arcHeight = profile != null && profile.flightArc
                ? profile.flightArcHeight : 0.9f;

            for (int groupIndex = 0; groupIndex < targetGroups.Count; groupIndex++)
            {
                if (completion != null && completion.IsCancelled) yield break;
                int[] sourceGroup = targetGroups[groupIndex];
                var validTargets = new List<int>();
                Vector3 firstTargetPosition = Vector3.zero;
                if (sourceGroup != null)
                {
                    for (int i = 0; i < sourceGroup.Length; i++)
                    {
                        int targetIndex = sourceGroup[i];
                        Transform target = GetEnemyTransform(targetIndex);
                        if (target == null || validTargets.Contains(targetIndex)) continue;
                        if (validTargets.Count == 0)
                            firstTargetPosition = target.position;
                        validTargets.Add(targetIndex);
                    }
                }
                if (validTargets.Count == 0) continue;

                Vector3 landingPosition =
                    landingPositions != null &&
                    groupIndex < landingPositions.Count
                        ? landingPositions[groupIndex]
                        : firstTargetPosition;
                Vector3 start = playerAttackOrigin.position + new Vector3(
                    0f, (groupIndex % 2 == 0 ? -0.08f : 0.08f), 0f);
                Vector3 end = landingPosition +
                    new Vector3(-0.12f, 0.12f, 0f);
                var projectile = CreateProjectile(profile);
                var controller = projectile.GetComponent<ProjectileController>();
                int[] capturedTargets = validTargets.ToArray();
                VfxProfile capturedProfile = profile;

                StartCoroutine(controller.Fly(start, end, duration,
                    arc: true, arcHeight: arcHeight,
                    onArrive: () =>
                    {
                        for (int i = 0; i < capturedTargets.Length; i++)
                        {
                            int targetIndex = capturedTargets[i];
                            Transform target = GetEnemyTransform(targetIndex);
                            if (target != null)
                                PlaySmallHitEffect(target.position, capturedProfile, completion: completion);
                            onEachTargetArrive?.Invoke(targetIndex);
                        }

                        arrivedGroups++;
                        if (arrivedGroups >= validGroupCount)
                            onAllArrived?.Invoke();
                    }, completion));

                if (groupIndex < targetGroups.Count - 1)
                    yield return new WaitForSeconds(delay);
            }
        }

        /// <summary>
        /// 发射弧线投射物（斧头等弧线武器，VfxProfile 驱动）
        /// </summary>
        public void FireArcProjectile(int targetIndex, float arcHeight, System.Action onArrive, VfxProfile profile)
        {
            Transform target = GetEnemyTransform(targetIndex);
            if (target == null || playerAttackOrigin == null)
            {
                onArrive?.Invoke();
                return;
            }

            var proj = CreateProjectile(profile);
            var controller = proj.GetComponent<ProjectileController>();
            Vector3 end = target.position + new Vector3(-0.2f, 0.3f, 0);

            float duration = profile != null ? profile.flightDuration * 1.4f : defaultFlightDuration * 1.4f;

            StartCoroutine(controller.Fly(
                playerAttackOrigin.position, end, duration,
                arc: true, arcHeight: arcHeight,
                onArrive: onArrive
            ));
        }

        // 旧签名兼容
        public void FireArcProjectile(int targetIndex, float arcHeight, System.Action onArrive, Color projectileColor)
            => FireArcProjectile(targetIndex, arcHeight, onArrive, (VfxProfile)null);

        private GameObject CreateProjectile(
            VfxProfile profile,
            float scaleMultiplier = 1f)
        {
            GameObject go;

            if (_projectilePool.Count > 0)
            {
                go = _projectilePool.Pop();
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("Projectile", typeof(SpriteRenderer), typeof(ProjectileController));
                go.transform.SetParent(projectileContainer, false);
            }

            go.transform.position = playerAttackOrigin.position;

            var controller = go.GetComponent<ProjectileController>();

            // 从 profile 读取配置，null 时用默认值
            float scale = (profile != null
                ? profile.projectileScale
                : defaultProjectileScale) * Mathf.Max(0.01f, scaleMultiplier);
            Color trailColor = profile != null ? profile.trailColor : Color.white;
            bool enableTrail = profile == null || profile.enableTrail;
            float trailWidth = profile != null ? profile.trailWidth : 0.1f;
            float trailTime = profile != null ? profile.trailTime : 0.15f;
            string visualType = profile != null ? profile.visualType : "Sprite";

            // 通过工厂创建投射物外观
            var visual = ProjectileVisualFactory.Create(visualType);
            if (visual != null)
            {
                // 只有 Sprite 外观需要默认图片；程序化外观不得在运行时污染配置。
                if (profile != null && visualType == "Sprite" &&
                    profile.projectileSprite == null)
                    profile.projectileSprite = defaultProjectileSprite;
                visual.Initialize(go, profile, scale);
                controller.SetVisual(visual);
            }
            else
            {
                // 最终回退：直接设 scale
                go.transform.localScale = Vector3.one * scale;
            }

            controller.ConfigureTrail(enableTrail, trailColor, trailWidth, trailTime);

            return go;
        }

        /// <summary>
        /// 回收投射物到对象池
        /// </summary>
        public void ReturnProjectile(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _projectilePool.Push(go);
        }

        // ==================== 击中粒子 ====================

        /// <summary>
        /// 在指定位置播放主层击中粒子（VfxProfile 驱动）。
        /// 次层碎屑由 PlaySmallHitEffect 在同一命中帧播放，避免主层重复发射。
        /// </summary>
        public void PlayHitEffect(Vector3 pos, VfxProfile profile, bool isCrit,
            AttackEffectCompletion completion = null)
        {
            if (completion != null && completion.IsCancelled) return;
            if (profile != null && profile.visualType == ProceduralWaterDragonVisual.VisualTypeName)
                TrackContactEffect(completion, WaterDragonImpact.Play(effectContainer, pos));
            if (isCrit && profile != null)
            {
                if (profile.knifeCriticalAccent)
                    TrackContactEffect(completion, KnifeCriticalImpact.Play(effectContainer, pos));
                if (!string.IsNullOrWhiteSpace(profile.critSfxPath))
                {
                    if (!_sfxCache.TryGetValue(profile.critSfxPath, out AudioClip clip) || clip == null)
                    {
                        clip = Resources.Load<AudioClip>(profile.critSfxPath);
                        if (clip != null) _sfxCache[profile.critSfxPath] = clip;
                    }
                    if (clip != null) _oneShotAudio?.Play(clip, GetImpactAudioKind(profile), 1f, profile.critSfxGain);
                }
            }
            if (profile != null &&
                profile.visualType == ProceduralAxeVisual.VisualTypeName)
            {
                TrackContactEffect(completion, ProceduralAxeImpactVisual.PlayImpact(
                    effectContainer, pos, profile, isCrit));
                return;
            }
            if (profile != null &&
                profile.visualType == RuyiStaffParticleVisual.VisualTypeName)
            {
                TrackContactEffect(completion, RuyiStaffParticleImpactVisual.PlayImpact(
                    effectContainer, pos, profile, isCrit));
                return;
            }

            if (profile == null)
            {
                PlayHitEffect(pos, new Color(0.7f, 0.1f, 0.1f));
                TrackContactBurst(completion, _hitEffectPS);
                return;
            }

            // 主层数量始终由 Profile 决定；纹理为空只表示复用公共纹理。
            if (profile.hitParticleCount > 0)
            {
                Sprite texture = profile.hitParticleTexture != null
                    ? profile.hitParticleTexture
                    : particleTexture;
                var ps = GetOrCreateProfilePS(texture, _profileHitPS,
                    profile.hitParticleCount, profile.hitParticleSize,
                    profile.hitParticleSpeed, profile.hitParticleLifetime, 30);
                ps.transform.position = pos;
                var main = ps.main;
                main.startColor = profile.hitParticleColor;
                EmitConfiguredBurst(ps, profile.hitParticleCount);
                TrackContactBurst(completion, ps);
            }
        }

        /// <summary>
        /// 小型击中火花（每发投射物命中时播放，VfxProfile 驱动）
        /// </summary>
        public void PlaySmallHitEffect(Vector3 pos, VfxProfile profile,
            bool playSound = true, bool emitParticles = true,
            AttackEffectCompletion completion = null)
        {
            if (completion != null && completion.IsCancelled) return;
            if (playSound) PlayHitSfx(pos, profile);
            if (!emitParticles) return;

            if (profile != null &&
                profile.visualType == ProceduralAxeVisual.VisualTypeName)
            {
                TrackContactEffect(completion, ProceduralAxeImpactVisual.PlaySparks(
                    effectContainer, pos, profile));
                return;
            }
            if (profile != null &&
                profile.visualType == RuyiStaffParticleVisual.VisualTypeName)
            {
                TrackContactEffect(completion, RuyiStaffParticleImpactVisual.PlayContact(
                    effectContainer, pos, profile));
                return;
            }

            if (profile == null)
            {
                if (_smallHitPS != null)
                {
                    _smallHitPS.transform.position = pos;
                    _smallHitPS.Emit(5);
                    TrackContactBurst(completion, _smallHitPS);
                }
                return;
            }

            // 次层碎屑使用自己的显式数量；无专用纹理时复用公共火花纹理。
            if (profile.secondaryParticleCount > 0)
            {
                Sprite texture = profile.secondaryParticleTexture != null
                    ? profile.secondaryParticleTexture
                    : (sparkParticleTexture != null
                        ? sparkParticleTexture
                        : particleTexture);
                var ps = GetOrCreateProfilePS(texture, _profileSecondaryPS,
                    profile.secondaryParticleCount,
                    profile.secondaryParticleSize,
                    profile.secondaryParticleSpeed,
                    profile.secondaryParticleLifetime, 29);
                ps.transform.position = pos;
                var main = ps.main;
                main.startColor = profile.secondaryParticleColor;
                EmitConfiguredBurst(ps, profile.secondaryParticleCount);
                TrackContactBurst(completion, ps);
            }
        }

        // 旧签名兼容
        // Each receipt follows only the actual one-shot root, or this burst's
        // captured lifetime. IsAlive on a shared PS would couple unrelated casts.
        private static void TrackContactEffect(AttackEffectCompletion completion, GameObject effect)
        {
            if (completion == null || effect == null) return;
            completion.ObserveUntil(() => effect == null || !effect.activeInHierarchy);
        }

        private static void TrackContactBurst(AttackEffectCompletion completion, ParticleSystem particles)
        {
            if (completion == null || particles == null) return;
            var main = particles.main;
            bool unscaled = main.useUnscaledTime;
            float endsAt = (unscaled ? Time.unscaledTime : Time.time) +
                main.startLifetime.constantMax / Mathf.Max(0.0001f, main.simulationSpeed);
            completion.ObserveUntil(() => particles == null || !particles.gameObject.activeInHierarchy ||
                (unscaled ? Time.unscaledTime : Time.time) >= endsAt);
        }

        public void PlayHitEffect(Vector3 pos, Color color)
        {
            if (_hitEffectPS == null) return;
            _hitEffectPS.transform.position = pos;
            var main = _hitEffectPS.main;
            main.startColor = color;
            _hitEffectPS.Emit(hitParticleCount);
        }

        public void PlaySmallHitEffect(Vector3 pos, Color color)
        {
            if (_smallHitPS == null) return;
            _smallHitPS.transform.position = pos;
            var main = _smallHitPS.main;
            main.startColor = color;
            _smallHitPS.Emit(5);
        }

        /// <summary>
        /// Begins the battle-scoped Boss entrance particle warmup. The manager
        /// remains a narrow composition facade; resource requests and native
        /// lifecycle state are wholly owned by the dedicated warmup module.
        /// </summary>
        public void BeginBossEntranceWarmup(BossEntranceEffectMode mode)
        {
            BossInfinityParticleWarmup warmup = GetOrCreateBossEntranceWarmup(mode);
            if (mode == BossEntranceEffectMode.WaterConvergence)
            {
                // 水登场只有小型预设与程序化材质，不创建原生粒子宿主。
                BossEntranceEffectPresetCatalog.TryGetDefault(
                    out _waterEntrancePreset, out _);
                return;
            }
            warmup?.Begin();
        }

        public bool IsBossEntranceWarmupReady =>
            _waterEntrancePreset != null ||
            (_bossEntranceParticleWarmup != null &&
             _bossEntranceParticleWarmup.IsReady);

        /// <summary>
        /// 按已注册的敌人索引同步捕获并隐藏 Boss，确保首帧不会闪出最终姿态。
        /// 场景组装层无需读取 SpriteRenderer；目标查找和渲染器类型由
        /// Effects 模块封装。
        /// </summary>
        public BossEntranceEffect PrepareBossEntrance(
            int enemyIndex,
            Camera presentationCamera,
            BossEntranceEffectMode mode = BossEntranceEffectMode.ImpactDrop)
        {
            Transform target = GetEnemyTransform(enemyIndex);
            SpriteRenderer renderer = target != null
                ? target.GetComponent<SpriteRenderer>()
                : null;
            Transform motionRoot = GetEnemyEntranceMotionRoot(enemyIndex);
            BossInfinityParticleWarmup warmup =
                GetOrCreateBossEntranceWarmup(mode);
            if (mode == BossEntranceEffectMode.WaterConvergence)
                return BossEntranceEffect.Prepare(
                    renderer, motionRoot, effectContainer,
                    presentationCamera, _waterEntrancePreset, mode);
            return BossEntranceEffect.PrepareWithWarmup(
                renderer,
                motionRoot,
                effectContainer,
                presentationCamera,
                null,
                mode,
                warmup);
        }

        private BossInfinityParticleWarmup GetOrCreateBossEntranceWarmup(
            BossEntranceEffectMode mode)
        {
            if (mode != BossEntranceEffectMode.WaterConvergence)
                _waterEntrancePreset = null;
            if (_bossEntranceParticleWarmup != null &&
                _bossEntranceParticleWarmup.Mode == mode)
            {
                return _bossEntranceParticleWarmup;
            }

            if (_bossEntranceParticleWarmup != null)
            {
                GameObject stale = _bossEntranceParticleWarmup.gameObject;
                _bossEntranceParticleWarmup = null;
                if (Application.isPlaying)
                    Destroy(stale);
                else
                    DestroyImmediate(stale);
            }

            if (mode == BossEntranceEffectMode.WaterConvergence)
                return null;

            _bossEntranceParticleWarmup =
                BossInfinityParticleWarmup.Create(effectContainer, mode);
            return _bossEntranceParticleWarmup;
        }

        /// <summary>
        /// Plays the boss-only foreground cleave toward the player's side of
        /// the screen. The camera is explicit so presentation never depends
        /// on whichever tagged camera happens to be active.
        /// </summary>
        public GameObject PlayBossForegroundSlash(
            Camera presentationCamera,
            Vector3 playerAnchor)
        {
            GameObject slash = BossForegroundSlashEffect.Play(
                effectContainer,
                presentationCamera,
                playerAnchor);
            if (slash == null) return null;

            HitStop(0.055f);
            ScreenShake shake = presentationCamera.GetComponent<ScreenShake>();
            if (shake != null)
                shake.Shake(0.16f, 0.10f);
            return slash;
        }

        private void PlayHitSfx(Vector3 pos, VfxProfile profile)
        {
            string path = profile != null ? profile.hitSfxPath : null;
            if (string.IsNullOrEmpty(path)) return;

            if (!_sfxCache.TryGetValue(path, out var clip))
            {
                clip = Resources.Load<AudioClip>(path);
                _sfxCache[path] = clip;
            }
            if (clip != null)
            {
                _oneShotAudio?.Play(
                    clip,
                    GetImpactAudioKind(profile),
                    1f,
                    profile.hitSfxGain);
            }
        }

        private static EffectOneShotKind GetImpactAudioKind(VfxProfile profile)
        {
            if (profile.denseImpactAudio) return EffectOneShotKind.DenseImpactHit;
            return profile.periodicFireCrow
                ? EffectOneShotKind.PeriodicAttackHit
                : EffectOneShotKind.ProjectileHit;
        }

        /// <summary>
        /// 播放 per-card 出牌音效（在英雄位置播放，叠加在全局翻纸声之上）。
        /// </summary>
        public void PlayCardSfx(VfxProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[CardSfx] 卡牌没有 VfxProfile，无法读取专属出牌音。");
                return;
            }
            AudioClip clip = profile.playSfxClip;
            if (clip == null)
            {
                string path = profile.playSfxPath;
                if (string.IsNullOrEmpty(path))
                    return;
                if (!_sfxCache.TryGetValue(path, out clip))
                {
                    clip = Resources.Load<AudioClip>(path);
                    _sfxCache[path] = clip;
                }
                if (clip == null)
                {
                    Debug.LogWarning(
                        $"[CardSfx] Resources.Load 失败：{path}，profile={profile.contentId}。");
                    return;
                }
            }
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (_oneShotAudio == null)
            {
                Debug.LogWarning(
                    $"[CardSfx] 缺少一次性音频端口，无法播放 {clip.name}。");
                return;
            }
            _oneShotAudio.Play(
                clip,
                EffectOneShotKind.CardPlay,
                1f,
                profile.playSfxGain);
            Debug.Log(
                $"[CardSfx] play={clip.name} state={clip.loadState} " +
                $"profile={profile.contentId}");
        }

        // ==================== 伤害数字 ====================

        public void ShowDamageNumber(Vector3 worldPos, int damage, bool isCrit)
        {
            var dn = GetDamageNumber();
            Vector3 pos = worldPos + Vector3.up * damageNumberOffset;
            dn.transform.position = pos;
            dn.Show(damage, isCrit);
            StartCoroutine(ReturnDamageNumberAfter(dn, damageNumberLifetime));
        }

        public void ShowDamageNumber(
            Vector3 worldPos,
            DamageEvent damageEvent,
            int sequenceOrdinal = 0)
        {
            var dn = GetDamageNumber();
            Vector3 pos = worldPos + Vector3.up * damageNumberOffset;
            dn.transform.position = pos;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[DamageTrace][DamageNumber] " +
                    $"runtime={damageEvent.TargetEnemyRuntimeId} " +
                    $"index={damageEvent.TargetIndex} " +
                    $"kind={damageEvent.Kind} " +
                    $"applied={damageEvent.AppliedDamage} " +
                    $"crit={damageEvent.IsCritical} " +
                    $"world=({pos.x:F2},{pos.y:F2},{pos.z:F2})");
            dn.Show(damageEvent, sequenceOrdinal);
            StartCoroutine(ReturnDamageNumberAfter(
                dn,
                damageEvent.Kind == DamageKind.Splash
                    ? damageNumberLifetime * 0.78f
                    : damageNumberLifetime));
        }

        private DamageNumber GetDamageNumber()
        {
            if (_damageNumberPool.Count > 0)
            {
                var pooled = _damageNumberPool.Dequeue();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            var go = new GameObject("DamageNumber", typeof(TextMesh));
            go.transform.SetParent(effectContainer, false);
            var tm = go.GetComponent<TextMesh>();
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.102f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sortingOrder = 40;
            return go.AddComponent<DamageNumber>();
        }

        private IEnumerator ReturnDamageNumberAfter(DamageNumber dn, float delay)
        {
            yield return new WaitForSeconds(delay);
            dn.gameObject.SetActive(false);
            _damageNumberPool.Enqueue(dn);
        }

        // ==================== 角色反馈 ====================

        public void PunchScale(Transform target, float intensity = -1f)
        {
            if (target == null) return;
            if (!_originalScales.ContainsKey(target))
                _originalScales[target] = target.localScale;

            float i = intensity < 0 ? punchScaleIntensity : intensity;
            StartCoroutine(PunchScaleRoutine(target, i));
        }

        private IEnumerator PunchScaleRoutine(Transform target, float intensity)
        {
            Vector3 original = _originalScales.GetValueOrDefault(target, target.localScale);
            Vector3 punched = original * (1f + intensity);

            float timer = 0f;
            float half = punchScaleDuration * 0.5f;

            while (timer < half)
            {
                timer += Time.deltaTime;
                float t = timer / half;
                target.localScale = Vector3.Lerp(original, punched, t);
                yield return null;
            }

            timer = 0f;
            while (timer < half)
            {
                timer += Time.deltaTime;
                float t = timer / half;
                target.localScale = Vector3.Lerp(punched, original, t);
                yield return null;
            }

            target.localScale = original;
        }

        // ==================== 顿帧 ====================

        public void HitStop(float duration = -1f)
        {
            if (!isActiveAndEnabled || _levelUpPaused) return;
            float d = duration < 0 ? hitStopDuration : duration;
            if (!_hitStopActive && d > 0f)
            {
                _hitStopRoutine = StartCoroutine(HitStopRoutine(d));
            }
        }

        private IEnumerator HitStopRoutine(float duration)
        {
            _hitStopActive = true;
            System.IDisposable lease = PresentationTimeScale.Shared.Hold(Mathf.Max(0f, hitStopTimeScale));
            _hitStopLease = lease;
            try
            {
                yield return new WaitForSecondsRealtime(duration);
            }
            finally
            {
                lease.Dispose();
                if (ReferenceEquals(_hitStopLease, lease))
                {
                    _hitStopLease = null;
                    _hitStopRoutine = null;
                    _hitStopActive = false;
                }
            }
        }

        private void CancelHitStop()
        {
            Coroutine old = _hitStopRoutine;
            System.IDisposable lease = _hitStopLease;
            _hitStopRoutine = null;
            _hitStopLease = null;
            _hitStopActive = false;
            if (old != null) StopCoroutine(old);
            lease?.Dispose();
        }

        private void ReleaseLevelUpLease()
        {
            System.IDisposable lease = _levelUpLease;
            _levelUpLease = null;
            lease?.Dispose();
        }

        private void OnEnable()
        {
            if (_levelUpPaused && _levelUpLease == null)
                _levelUpLease = PresentationTimeScale.Shared.Hold(0f);
        }

        private void OnDisable()
        {
            CancelHitStop();
            ReleaseLevelUpLease();
        }

        private void OnDestroy()
        {
            CancelHitStop();
            ReleaseLevelUpLease();
            _levelUpPaused = false;
            if (Instance == this)
                Instance = null;
        }

        // ==================== 工具 ====================

        public Transform GetEnemyTransform(int index)
        {
            if (index >= 0 && index < enemyTransforms.Count)
                return enemyTransforms[index];
            return null;
        }

        /// <summary>
        /// 注册角色自身特效目标和独立入场运动根。普通测试目标可以省略运动根，
        /// 但该目标将不具备 BOSS 入场能力。
        /// </summary>
        public void RegisterEnemy(
            int index,
            Transform target,
            Transform entranceMotionRoot = null)
        {
            while (enemyTransforms.Count <= index)
                enemyTransforms.Add(null);
            while (_enemyEntranceMotionRoots.Count <= index)
                _enemyEntranceMotionRoots.Add(null);
            enemyTransforms[index] = target;
            _enemyEntranceMotionRoots[index] = entranceMotionRoot;
        }

        private Transform GetEnemyEntranceMotionRoot(int index)
        {
            if (index >= 0 && index < _enemyEntranceMotionRoots.Count)
                return _enemyEntranceMotionRoots[index];
            return null;
        }

        /// <summary>
        /// 敌人死亡特效（VfxProfile 驱动）
        /// </summary>
        public void PlayDeathEffect(
            Transform target,
            VfxProfile profile,
            System.Action onComplete = null)
        {
            PlayDeathEffect(target, profile, null, onComplete);
        }

        /// <summary>
        /// 敌人死亡特效：主体腾空后即可补位，完整结束后再结算表现尾声。
        /// </summary>
        public void PlayDeathEffect(
            Transform target,
            VfxProfile profile,
            System.Action onSubjectVacated,
            System.Action onComplete,
            DeathEffectType? deathEffectOverride = null)
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX] PlayDeathEffect target={(target != null ? target.name : "null")} " +
                    $"type={(profile != null ? profile.deathEffect.ToString() : "DefaultFadeOut")}");
            if (target == null)
            {
                onSubjectVacated?.Invoke();
                onComplete?.Invoke();
                return;
            }

            DeathEffectType deathType = deathEffectOverride ??
                (profile != null
                    ? profile.deathEffect
                    : DeathEffectType.FadeOut);

            switch (deathType)
            {
                case DeathEffectType.SealErase:
                    StartCoroutine(ErasedEnemyEffect.Play(target, effectContainer,
                        profile != null ? profile.hitParticleColor : new Color(1f, 0.84f, 0.35f),
                        onSubjectVacated, onComplete));
                    break;
                case DeathEffectType.RowAnnihilation:
                    StartCoroutine(ErasedEnemyEffect.Play(target, effectContainer,
                        profile != null ? profile.hitParticleColor : new Color(1f, 0.84f, 0.35f),
                        onSubjectVacated,
                        onComplete,
                        annihilation: true,
                        rowWidth: profile != null
                            ? profile.rowAnnihilationWidth
                            : 10.8f,
                        intensity: profile != null
                            ? profile.rowAnnihilationIntensity
                            : 1.35f,
                        bandHeight: profile != null
                            ? profile.rowAnnihilationBandHeight
                            : 0.72f));
                    break;
                case DeathEffectType.PaperCut:
                    var sr = target.GetComponent<SpriteRenderer>();
                    Sprite enemySprite = sr != null ? sr.sprite : null;
                    Color enemyTint = sr != null ? sr.color : Color.white;
                    PaperCutDeath.Play(
                        target,
                        enemySprite,
                        enemyTint,
                        effectContainer,
                        onSubjectVacated,
                        onComplete);
                    break;
                case DeathEffectType.BossDisperse:
                    var bossRenderer = target.GetComponent<SpriteRenderer>();
                    Color bossTint = bossRenderer != null
                        ? bossRenderer.color
                        : Color.white;
                    PaperCutDeath.PlayBossDisperse(
                        target,
                        bossTint,
                        effectContainer,
                        onSubjectVacated,
                        onComplete);
                    break;
                case DeathEffectType.InkDisperse:
                    // 后续实现，暂时回退到 FadeOut
                    goto case DeathEffectType.FadeOut;
                case DeathEffectType.FadeOut:
                default:
                    PlayHitEffect(target.position, new Color(0.8f, 0.2f, 0.2f));
                    StartCoroutine(
                        DeathFadeRoutine(
                            target,
                            onSubjectVacated,
                            onComplete));
                    break;
            }
        }

        // 旧签名兼容
        public void PlayDeathEffect(Transform target, Color color)
        {
            if (target == null) return;
            PlayHitEffect(target.position, color);
            StartCoroutine(DeathFadeRoutine(target, null, null));
        }

        private IEnumerator DeathFadeRoutine(
            Transform target,
            System.Action onSubjectVacated,
            System.Action onComplete)
        {
            var sr = target.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                target.gameObject.SetActive(false);
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[VFX] DeathFadeRoutine srNull target={target.name}");
                onSubjectVacated?.Invoke();
                onComplete?.Invoke();
                yield break;
            }

            float timer = 0f;
            float duration = 0.3f;
            Vector3 startScale = target.localScale;
            Color startColor = sr.color;
            bool vacancySignaled = false;

            while (timer < duration)
            {
                if (target == null || sr == null)
                    break;
                timer += Time.deltaTime;
                float t = timer / duration;
                target.localScale = Vector3.Lerp(startScale, startScale * 0.5f, t);
                var c = startColor;
                c.a = Mathf.Lerp(1f, 0f, t);
                sr.color = c;
                if (!vacancySignaled && timer >= 0.08f)
                {
                    vacancySignaled = true;
                    if (CombatLogger.EnableVerboseDiagnostics)
                        Debug.Log(
                            $"[VFX] DeathFadeRoutine vacancySignaled target={target.name}");
                    onSubjectVacated?.Invoke();
                }
                yield return null;
            }

            if (!vacancySignaled)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[VFX] DeathFadeRoutine vacancySignaledLate target={target.name}");
                onSubjectVacated?.Invoke();
            }
            if (target != null)
                target.gameObject.SetActive(false);
            onComplete?.Invoke();
        }

        // ==================== 预创建粒子系统 ====================

        private ParticleSystem _hitEffectPS;
        private ParticleSystem _smallHitPS;
        private bool _particleSystemsReady;

        private void CreatePooledParticleSystems()
        {
            if (_particleSystemsReady) return;

            _hitEffectPS = CreateParticleSystem(
                "HitEffectPS", hitParticleCount, hitParticleStartSize,
                hitParticleSpeed, hitParticleDuration, 30,
                particleTexture, ref _hitParticleMaterial);

            _smallHitPS = CreateParticleSystem(
                "SmallHitPS", 5, hitParticleStartSize * 0.6f,
                hitParticleSpeed * 0.5f, 0.25f, 28,
                sparkParticleTexture != null ? sparkParticleTexture : particleTexture,
                ref _sparkParticleMaterial);

            _particleSystemsReady = true;
        }

        private ParticleSystem CreateParticleSystem(
            string name, int maxParticles, float startSize,
            float startSpeed, float startLifetime, int sortingOrder,
            Sprite texture, ref Material cachedMaterial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(effectContainer, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startSize = startSize;
            main.startSpeed = startSpeed;
            main.startLifetime = startLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            shape.angle = 180f;

            var emission = ps.emission;
            emission.rateOverTime = 0;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var mat = CreateParticleMaterial(texture, ref cachedMaterial);
            if (renderer.material != mat) renderer.material = mat;
            renderer.sortingOrder = sortingOrder;

            return ps;
        }

        private static Material CreateParticleMaterial(
            Sprite texture,
            ref Material cachedMaterial)
        {
            if (cachedMaterial != null) return cachedMaterial;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            cachedMaterial = new Material(shader);

            if (texture != null)
                cachedMaterial.mainTexture = texture.texture;

            return cachedMaterial;
        }

        // ==================== VfxProfile 动态粒子系统 ====================

        /// <summary>
        /// 按 Sprite 纹理获取或创建专属 ParticleSystem。
        /// 修复 Bug4: 缓存命中时也更新参数，避免不同 Profile 复用同一纹理时继承先创建者的速度/大小/寿命。
        /// </summary>
        private ParticleSystem GetOrCreateProfilePS(
            Sprite texture,
            Dictionary<Sprite, ParticleSystem> cache,
            int maxParticles, float startSize,
            float startSpeed, float startLifetime,
            int sortingOrder)
        {
            if (texture == null) return _hitEffectPS;

            if (cache.TryGetValue(texture, out var ps) && ps != null)
            {
                // 缓存命中：更新参数（材质/纹理不变，只改运动参数）
                var main = ps.main;
                main.maxParticles = Mathf.Max(
                    main.maxParticles,
                    Mathf.Max(maxParticles, 10));
                main.startSize = startSize;
                main.startSpeed = startSpeed;
                main.startLifetime = startLifetime;
                return ps;
            }

            // 为这个纹理创建独立的材质和 PS
            Material mat;
            if (!_profileMaterials.TryGetValue(texture, out mat) || mat == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader);
                mat.mainTexture = texture.texture;
                _profileMaterials[texture] = mat;
            }

            string goName = $"ProfilePS_{texture.name}";
            ps = CreateParticleSystemWithMaterial(
                goName, maxParticles, startSize,
                startSpeed, startLifetime, sortingOrder, mat);

            cache[texture] = ps;
            return ps;
        }

        private static void EmitConfiguredBurst(
            ParticleSystem particleSystem, int count)
        {
            if (particleSystem == null || count <= 0) return;

            // maxParticles 只是容量，不是发射数量。连续命中重叠时按需扩容，
            // 防止后续一次 Emit 被前一批仍存活的粒子静默截断。
            var main = particleSystem.main;
            int requiredCapacity = particleSystem.particleCount + count;
            if (main.maxParticles < requiredCapacity)
                main.maxParticles = requiredCapacity;
            particleSystem.Emit(count);
        }

        private ParticleSystem CreateParticleSystemWithMaterial(
            string name, int maxParticles, float startSize,
            float startSpeed, float startLifetime, int sortingOrder,
            Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(effectContainer, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(maxParticles, 10);
            main.startSize = startSize;
            main.startSpeed = startSpeed;
            main.startLifetime = startLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            shape.angle = 180f;

            var emission = ps.emission;
            emission.rateOverTime = 0;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.sortingOrder = sortingOrder;

            return ps;
        }
    }
}
