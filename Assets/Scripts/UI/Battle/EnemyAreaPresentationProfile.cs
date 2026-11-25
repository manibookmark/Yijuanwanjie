using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 单个敌人素材的表现参数。contentId 负责稳定关联，Sprite 引用用于
    /// 编辑器预览；素材脚底、整体偏移和视觉缩放由美术人工校准。
    /// 阴影校准只提供局部几何，不持有渲染状态；EnemyActorView 继续唯一写入阴影。
    /// </summary>
    [Serializable]
    public sealed class EnemySpriteAlignment
    {
        [Tooltip("稳定内容 ID；禁止使用敌人显示名称作为关联键")]
        public string contentId;
        public Sprite sprite;
        [Range(0f, 1f)]
        public Vector2 groundAnchor = new Vector2(0.5f, 0f);
        [Tooltip("相对素材显示尺寸的整体偏移；本体和动作共用")]
        public Vector2 visualOffset = Vector2.zero;
        [Min(0.1f)] public float visualScale = 1f;
        [Tooltip("启用后按已测量的可见主体大小显示；不再由规则占格放大。关 Boss 继续使用独立合同。")]
        public bool hasVisibleBodySizing;
        [Tooltip("alpha 可见主体相对静态 Sprite 完整矩形的范围；左下 (0,0)，右上 (1,1)。")]
        public Rect visibleBodyNormalizedRect = new Rect(0f, 0f, 1f, 1f);
        [Min(0.01f), Tooltip("首排可见主体的目标最大边；层 Boss 使用统一目标，visualScale 只提供人工微调。")]
        public float visibleBodyTargetMaxWorldSize = 1.8f;
        [Tooltip("启用后只覆盖战斗脚底阴影的中心和尺寸，不改变角色对齐或体型。")]
        public bool hasContactShadowCalibration;
        [Tooltip("阴影中心相对静态 Sprite 完整矩形的位置；左下 (0,0)，右上 (1,1)。")]
        public Vector2 contactShadowNormalizedCenter;
        [Tooltip("阴影宽高相对静态 Sprite 完整宽高的比例；不包含整体视觉缩放。")]
        public Vector2 contactShadowNormalizedSize;

        public bool IsVisibleBodySizingValid =>
            !hasVisibleBodySizing ||
            (IsUnitValue(visibleBodyNormalizedRect.x) &&
             IsUnitValue(visibleBodyNormalizedRect.y) &&
             IsUnitValue(visibleBodyNormalizedRect.width) &&
             visibleBodyNormalizedRect.width > 0f &&
             IsUnitValue(visibleBodyNormalizedRect.height) &&
             visibleBodyNormalizedRect.height > 0f &&
             visibleBodyNormalizedRect.xMax <= 1f &&
             visibleBodyNormalizedRect.yMax <= 1f &&
             !float.IsNaN(visibleBodyTargetMaxWorldSize) &&
             !float.IsInfinity(visibleBodyTargetMaxWorldSize) &&
             visibleBodyTargetMaxWorldSize > 0f);

        public bool IsContactShadowCalibrationValid =>
            !hasContactShadowCalibration ||
            (IsUnitValue(contactShadowNormalizedCenter.x) &&
             IsUnitValue(contactShadowNormalizedCenter.y) &&
             IsUnitValue(contactShadowNormalizedSize.x) &&
             contactShadowNormalizedSize.x > 0f &&
             IsUnitValue(contactShadowNormalizedSize.y) &&
             contactShadowNormalizedSize.y > 0f);

        /// <summary>
        /// Converts the measured alpha envelope to unscaled Sprite-local geometry.
        /// Uses the full Sprite rectangle and pivot, so transparent padding and
        /// Tight mesh generation do not change the authored visible-body size.
        /// </summary>
        public bool TryGetVisibleBodyRect(Sprite targetSprite, out Rect localRect)
        {
            localRect = default;
            if (!hasVisibleBodySizing || !IsVisibleBodySizingValid ||
                targetSprite == null || targetSprite.pixelsPerUnit <= 0f)
                return false;

            Vector2 canvasSize =
                targetSprite.rect.size / targetSprite.pixelsPerUnit;
            if (canvasSize.x <= 0f || canvasSize.y <= 0f)
                return false;

            Vector2 minimum =
                Vector2.Scale(canvasSize, visibleBodyNormalizedRect.min) -
                targetSprite.pivot / targetSprite.pixelsPerUnit;
            Vector2 size =
                Vector2.Scale(canvasSize, visibleBodyNormalizedRect.size);
            localRect = new Rect(minimum, size);
            return true;
        }

        /// <summary>
        /// Resolves the authored static-Sprite rectangle without reading tight
        /// mesh bounds or animation frames. Parent transforms apply visual scale.
        /// </summary>
        public bool TryGetContactShadowRect(Sprite targetSprite, out Rect localRect)
        {
            localRect = default;
            if (!hasContactShadowCalibration ||
                !IsContactShadowCalibrationValid || targetSprite == null ||
                targetSprite.pixelsPerUnit <= 0f)
                return false;

            Vector2 canvasSize =
                targetSprite.rect.size / targetSprite.pixelsPerUnit;
            if (canvasSize.x <= 0f || canvasSize.y <= 0f)
                return false;

            Vector2 center =
                Vector2.Scale(canvasSize, contactShadowNormalizedCenter) -
                targetSprite.pivot / targetSprite.pixelsPerUnit;
            Vector2 size =
                Vector2.Scale(canvasSize, contactShadowNormalizedSize);
            localRect = new Rect(center - size * 0.5f, size);
            return true;
        }

        private static bool IsUnitValue(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) &&
               value >= 0f && value <= 1f;
    }

    /// <summary>
    /// 敌人区域表现配置。占格尺寸来自 EnemyData/EnemyStats；本资产负责
    /// 棋盘几何、素材绑定、落地点与视觉缩放。
    /// 文件达到规模审查阈值，但仍只持有作者配置与校验；局部几何换算无运行时
    /// 状态。后续新增动画或生命周期职责时须重新审查并独立封装。
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyAreaPresentation",
        menuName = "万界长卷/UI/敌人区域表现配置")]
    public sealed class EnemyAreaPresentationProfile : ScriptableObject
    {
        /// <summary>
        /// 未迁移可见主体校准的两格层 BOSS 兼容倍率；新校准不叠加此倍率。
        /// </summary>
        public const float TwoCellFloorBossScale = 1.2f;

        /// <summary>已校准层 BOSS 首排可见主体的统一最大边，与规则占格无关。</summary>
        public const float FloorBossVisibleBodyMaxWorldSize = 2.8f;

        public const int CurrentSchemaVersion = 2;
        public const string DefaultResourcePath =
            "UI/Battle/EnemyAreaPresentation";

        [Header("身份与版本")]
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "ui.enemy-area.huaguoshan.v1";

        [Header("棋盘")]
        public float gridFrontY = -1.9f;
        public float gridBackY = 2.25f;
        [Min(0.1f)] public float gridFrontHalfWidth = 6.3f;
        [Min(0.1f)] public float gridBackHalfWidth = 5.2f;
        [Range(0.1f, 1f)] public float cellWidthUsage = 0.82f;
        [Min(0f)] public float visualDepthPadding = 0.3f;
        [Range(0.1f, 1f)] public float backRowScale = 0.82f;
        [Min(0.001f)] public float gridLineWidth = 0.025f;
        public Color gridColor =
            new Color(0.42f, 0.18f, 0.1f, 0.34f);

        [Header("附属元素")]
        public Vector3 topFogPosition = new Vector3(0f, 2.9f, 0f);
        [Min(0.1f)] public float topFogWidth = 14.5f;
        [Range(0f, 1f)] public float topFogOpacity = 0.6f;
        public Vector3 enemyStatusPosition = new Vector3(0f, 3.7f, 0f);
        [Min(0f)] public float bossEnemyStatusYOffset = 0.55f;

        [Header("敌人素材")]
        [SerializeField]
        private List<EnemySpriteAlignment> entries =
            new List<EnemySpriteAlignment>();

        private Dictionary<Sprite, EnemySpriteAlignment> _bySprite;
        private Dictionary<string, EnemySpriteAlignment> _byContentId;
        private static EnemyAreaPresentationProfile _runtimeFallback;

        public IReadOnlyList<EnemySpriteAlignment> Entries => entries;
        public int Columns => EnemyFormationLayout.Columns;
        public int VisibleRows => EnemyFormationLayout.VisibleRows;

        public static EnemyAreaPresentationProfile LoadDefault()
        {
            EnemyAreaPresentationProfile profile =
                Resources.Load<EnemyAreaPresentationProfile>(
                    DefaultResourcePath);
            return profile != null ? profile : RuntimeFallback;
        }

        public bool TryGetVisual(
            string contentId,
            out EnemySpriteAlignment alignment)
        {
            alignment = null;
            if (string.IsNullOrWhiteSpace(contentId))
                return false;

            EnsureLookup();
            return _byContentId.TryGetValue(contentId, out alignment);
        }

        /// <summary>
        /// 兼容编辑器预览和显式 enemyArt：按 Sprite 引用查找人工对齐值。
        /// 需要可见主体等完整校准时，使用返回 EnemySpriteAlignment 的重载。
        /// </summary>
        public bool TryGetAlignment(
            Sprite sprite,
            out Vector2 groundAnchor,
            out Vector2 visualOffset,
            out float visualScale)
        {
            if (TryGetAlignment(sprite, out EnemySpriteAlignment entry))
            {
                groundAnchor = entry.groundAnchor;
                visualOffset = entry.visualOffset;
                visualScale = entry.visualScale;
                return true;
            }

            groundAnchor = new Vector2(0.5f, 0f);
            visualOffset = Vector2.zero;
            visualScale = 1f;
            return false;
        }

        public bool TryGetAlignment(
            Sprite sprite,
            out EnemySpriteAlignment alignment)
        {
            alignment = null;
            if (sprite == null)
                return false;

            EnsureLookup();
            return _bySprite.TryGetValue(sprite, out alignment);
        }

        public EnemySpriteAlignment GetOrCreateEntry(Sprite sprite)
        {
            if (sprite == null) return null;
            EnsureLookup();
            if (_bySprite.TryGetValue(sprite, out EnemySpriteAlignment entry))
                return entry;

            entry = new EnemySpriteAlignment { sprite = sprite };
            entries.Add(entry);
            InvalidateLookup();
            return entry;
        }

        public void RemoveEntry(Sprite sprite)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].sprite == sprite)
                    entries.RemoveAt(i);
            }
            InvalidateLookup();
        }

        public int GetCellSortingOrder(int visibleRow, int column)
            => 30 - Mathf.Clamp(visibleRow, 0, VisibleRows - 1) * 10 + column;

        /// <summary>Flying bodies sit behind all actors in their current board row.</summary>
        public int GetProjectileSortingOrder(float depth)
        {
            int row = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(depth) * VisibleRows), 0, VisibleRows - 1);
            return GetCellSortingOrder(row, 0) - 1;
        }

        public Vector3 GetGridPoint(float column, float depth)
        {
            float halfWidth = Mathf.Lerp(
                gridFrontHalfWidth,
                gridBackHalfWidth,
                Mathf.Clamp01(depth));
            return new Vector3(
                Mathf.Lerp(-halfWidth, halfWidth, column),
                Mathf.Lerp(gridFrontY, gridBackY, depth),
                0f);
        }

        /// <summary>
        /// 返回矩形占格最靠前一排的落地点。纵深不改变已经人工校准的素材锚点；
        /// 隐藏排保留真实队列坐标；表现层负责在首次进入可见区时切换到第三排落点渐显。
        /// </summary>
        public Vector3 GetFootprintGroundAnchor(
            int row,
            int column,
            int gridWidth)
        {
            float rowT =
                (Mathf.Max(0, row) + 0.5f) /
                VisibleRows;
            float halfWidth = Mathf.Lerp(
                gridFrontHalfWidth,
                gridBackHalfWidth,
                Mathf.Clamp01(rowT));
            float cellWidth = halfWidth * 2f / Columns;
            float x = -halfWidth +
                cellWidth * (column + Mathf.Max(1, gridWidth) * 0.5f);
            float y = Mathf.LerpUnclamped(
                gridFrontY,
                gridBackY,
                rowT);
            return new Vector3(x, y, 0f);
        }

        public void GetVisualBounds(
            int row,
            int gridWidth,
            int gridDepth,
            out float maxWidth,
            out float maxHeight)
        {
            float rowT =
                (Mathf.Clamp(row, 0, VisibleRows - 1) + 0.5f) /
                VisibleRows;
            float halfWidth = Mathf.Lerp(
                gridFrontHalfWidth,
                gridBackHalfWidth,
                rowT);
            float cellWidth = halfWidth * 2f / Columns;
            float cellHeight =
                (gridBackY - gridFrontY) / VisibleRows;
            float depthScale = Mathf.Lerp(1f, backRowScale, rowT);
            maxWidth = cellWidth *
                Mathf.Max(1, gridWidth) *
                cellWidthUsage;
            maxHeight = cellHeight *
                (Mathf.Max(1, gridDepth) + visualDepthPadding) *
                depthScale;
        }

        public float GetSpriteScale(
            Sprite sprite,
            EnemySpriteAlignment visual,
            int row,
            int gridWidth,
            int gridDepth,
            BossRank bossRank = BossRank.None)
        {
            if (sprite == null) return 1f;

            if (bossRank != BossRank.Stage && visual != null &&
                visual.TryGetVisibleBodyRect(sprite, out Rect visibleBodyRect))
            {
                float targetSize = bossRank == BossRank.Floor
                    ? FloorBossVisibleBodyMaxWorldSize
                    : visual.visibleBodyTargetMaxWorldSize;
                float rowT =
                    (Mathf.Clamp(row, 0, VisibleRows - 1) + 0.5f) /
                    VisibleRows;
                float frontRowT = 0.5f / VisibleRows;
                float perspectiveScale = Mathf.Lerp(1f, backRowScale, rowT) /
                    Mathf.Lerp(1f, backRowScale, frontRowT);
                return targetSize /
                    Mathf.Max(visibleBodyRect.width, visibleBodyRect.height) *
                    perspectiveScale * Mathf.Max(0.1f, visual.visualScale);
            }

            GetVisualBounds(
                row,
                gridWidth,
                gridDepth,
                out float maxWidth,
                out float maxHeight);
            Vector2 size = sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f)
                return 1f;

            float fitted = Mathf.Min(
                maxWidth / size.x,
                maxHeight / size.y);
            float multiplier = visual != null
                ? Mathf.Max(0.1f, visual.visualScale)
                : 1f;
            return fitted * multiplier * GetFootprintScaleMultiplier(
                bossRank,
                gridWidth,
                gridDepth);
        }

        public float GetFootprintScaleMultiplier(
            BossRank bossRank,
            int gridWidth,
            int gridDepth)
        {
            int footprintArea = Mathf.Max(1, gridWidth) *
                                Mathf.Max(1, gridDepth);
            return bossRank == BossRank.Floor && footprintArea == 2
                ? TwoCellFloorBossScale
                : 1f;
        }

        /// <summary>
        /// 将 F10 的归一化整体偏移换算到战斗世界坐标。偏移跟随最终缩放，
        /// 不改变历史 groundAnchor 对素材脚底的校准语义。
        /// </summary>
        public Vector3 GetPlacementGroundAnchor(
            Vector3 cellAnchor,
            Sprite sprite,
            EnemySpriteAlignment visual,
            float spriteScale)
        {
            if (sprite == null)
                return cellAnchor;

            Vector2 size = sprite.bounds.size;
            Vector2 visualOffset = visual != null
                ? visual.visualOffset
                : Vector2.zero;
            return cellAnchor + new Vector3(
                visualOffset.x * size.x * spriteScale,
                visualOffset.y * size.y * spriteScale,
                0f);
        }

        public Vector3 GetAlignedPivotPosition(
            Vector3 cellAnchor,
            Sprite sprite,
            Vector2 groundAnchor,
            float spriteScale)
        {
            if (sprite == null) return cellAnchor;

            Vector2 pivot = new Vector2(
                sprite.pivot.x / sprite.rect.width,
                sprite.pivot.y / sprite.rect.height);
            Vector2 size = sprite.bounds.size;
            Vector2 offset = new Vector2(
                (pivot.x - groundAnchor.x) * size.x * spriteScale,
                (pivot.y - groundAnchor.y) * size.y * spriteScale);
            return cellAnchor + new Vector3(offset.x, offset.y, 0f);
        }

        public List<string> ValidateProfile()
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(profileId))
                errors.Add("profileId 不能为空。");
            if (gridBackY <= gridFrontY)
                errors.Add("棋盘后沿必须高于前沿。");
            if (topFogOpacity < 0f || topFogOpacity > 1f)
                errors.Add("顶部云雾透明度必须在 0 到 1 之间。");

            var ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (EnemySpriteAlignment entry in entries)
            {
                if (entry == null)
                {
                    errors.Add("敌人素材列表不能包含 null。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.contentId))
                    errors.Add("敌人素材绑定缺少 contentId。");
                else if (!ids.Add(entry.contentId))
                    errors.Add($"敌人素材 contentId 重复：{entry.contentId}。");
                if (entry.sprite == null)
                    errors.Add($"敌人素材绑定缺少图片：{entry.contentId}。");
                if (!entry.IsVisibleBodySizingValid)
                {
                    errors.Add($"敌人可见主体校准需要完整归一化范围内的正尺寸及有限正目标最大边：{entry.contentId}。");
                }
                if (!entry.IsContactShadowCalibrationValid)
                {
                    errors.Add($"敌人阴影校准需要归一化中心及 (0,1] 范围的宽高：{entry.contentId}。");
                }
                if (entry.groundAnchor.x < 0f ||
                    entry.groundAnchor.x > 1f ||
                    entry.groundAnchor.y < 0f ||
                    entry.groundAnchor.y > 1f)
                {
                    errors.Add($"敌人落地点超出图片范围：{entry.contentId}。");
                }
                if (entry.visualScale <= 0f)
                    errors.Add($"敌人视觉缩放必须大于 0：{entry.contentId}。");
                if (Mathf.Abs(entry.visualOffset.x) > 1f ||
                    Mathf.Abs(entry.visualOffset.y) > 1f)
                {
                    errors.Add($"敌人整体偏移超出一倍素材尺寸：{entry.contentId}。");
                }
            }
            return errors;
        }

        private void EnsureLookup()
        {
            if (_bySprite != null && _byContentId != null)
                return;

            _bySprite = new Dictionary<Sprite, EnemySpriteAlignment>();
            _byContentId = new Dictionary<string, EnemySpriteAlignment>(
                StringComparer.OrdinalIgnoreCase);
            foreach (EnemySpriteAlignment entry in entries)
            {
                if (entry == null) continue;
                if (entry.sprite != null && !_bySprite.ContainsKey(entry.sprite))
                    _bySprite.Add(entry.sprite, entry);
                if (!string.IsNullOrWhiteSpace(entry.contentId) &&
                    !_byContentId.ContainsKey(entry.contentId))
                {
                    _byContentId.Add(entry.contentId, entry);
                }
            }
        }

        private void InvalidateLookup()
        {
            _bySprite = null;
            _byContentId = null;
        }

        private void OnValidate()
        {
            InvalidateLookup();
        }

        private static EnemyAreaPresentationProfile RuntimeFallback
        {
            get
            {
                if (_runtimeFallback != null)
                    return _runtimeFallback;

                _runtimeFallback =
                    CreateInstance<EnemyAreaPresentationProfile>();
                _runtimeFallback.name =
                    "RuntimeEnemyAreaPresentationFallback";
                _runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
                _runtimeFallback.entries.Add(
                    new EnemySpriteAlignment
                    {
                        contentId = "legacy.enemy.common.b83e679a",
                        sprite = RuntimeArtLoader.GetPapercraftBat(),
                        groundAnchor = new Vector2(0.5f, 0.15f),
                        visualScale = 1f
                    });
                _runtimeFallback.entries.Add(
                    new EnemySpriteAlignment
                    {
                        contentId = "enemy.common.snake_elite",
                        sprite = RuntimeArtLoader.GetPapercraftSnake(),
                        groundAnchor = new Vector2(0.5f, 0.08f),
                        visualScale = 1f
                    });
                return _runtimeFallback;
            }
        }

#if UNITY_EDITOR
        public EnemySpriteAlignment GetOrCreateEntryEditor(
            string contentId,
            Sprite sprite)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            EnsureLookup();
            if (_byContentId.TryGetValue(
                    contentId,
                    out EnemySpriteAlignment existing))
            {
                if (existing.sprite == null && sprite != null)
                    existing.sprite = sprite;
                return existing;
            }

            var entry = new EnemySpriteAlignment
            {
                contentId = contentId,
                sprite = sprite
            };
            entries.Add(entry);
            InvalidateLookup();
            return entry;
        }

        public void SetDirtyEditor()
        {
            InvalidateLookup();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
