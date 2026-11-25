using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>Controls how many map visuals may represent one logical interaction.</summary>
    public enum MapInteractionVisualPolicy
    {
        CompositeVisuals = 0,
        SingleRepresentative = 1
    }

    public enum MapPreviewVisualKind
    {
        Sprite,
        ManaPearl,
        Seal,
        // Append-only: previously serialized Sprite / ManaPearl / Seal values remain stable.
        ManaConversion,
        ManaPlatform
    }

    /// <summary>Sprite 节点的完成演出风格。</summary>
    public enum MapNodeCompletionEffect
    {
        Purification,        // 普通净化：纸屑+墨滴+朱砂消散
        RelicCollection,     // 旧遗珍采集兼容：暖金收束+本体上浮归藏
        None                 // 立即完成，不在地图场景生成任何表现对象
    }

    /// <summary>
    /// 地图 Sprite 节点可选的无音频序列。静态 sprite 仍是尺寸、pivot、
    /// 灵气范围和无效配置时的回退真相；序列只替换渲染帧。
    /// </summary>
    [Serializable]
    public sealed class MapSpriteSequence
    {
        public Texture2D frameAtlas;
        [Min(1)] public int frameCount = 48;
        [Min(1)] public int atlasColumns = 8;
        [Min(1)] public int atlasRows = 6;
        [Min(0.1f)] public float framesPerSecond = 12f;
        [Range(-1f, 1f)]
        [Tooltip(
            "序列帧脚底距帧底边的归一化高度；负数时沿用静态回退 Sprite 的脚底锚点。")]
        public float groundAnchorNormalizedY = -1f;
        [Tooltip("播放一次后回到第 0 帧并保持；适合地图挑战动作。")]
        public bool playOnceThenHoldFirstFrame;
        [Min(0f)]
        [Tooltip("只用于编辑器审核页：单次动作结束后隔多久自动重播。运行地图不会重播。")]
        public float reviewReplayDelaySeconds = 1.5f;

        public bool IsConfigured => frameAtlas != null;
        public float DurationSeconds => IsValid
            ? frameCount / framesPerSecond
            : 0f;
        public bool IsValid => IsConfigured && frameCount > 0 &&
                               atlasColumns > 0 && atlasRows > 0 &&
                               frameCount <= atlasColumns * atlasRows &&
                               framesPerSecond > 0f &&
                               IsFiniteNonNegative(reviewReplayDelaySeconds) &&
                               IsOptionalNormalizedUnitInterval(
                                   groundAnchorNormalizedY) &&
                               frameAtlas.width % atlasColumns == 0 &&
                               frameAtlas.height % atlasRows == 0;

        public int GetFrameIndex(float elapsedSeconds)
        {
            if (!IsValid)
                return 0;
            float elapsed = Mathf.Max(0f, elapsedSeconds);
            float actionDuration = DurationSeconds;
            if (playOnceThenHoldFirstFrame && elapsed >= actionDuration)
                return 0;
            return Mathf.FloorToInt(elapsed * framesPerSecond) % frameCount;
        }

        /// <summary>
        /// 审核页会周期重播一次性地图动作，避免打开页面较晚时永远只看到待机帧。
        /// 运行地图仍严格使用 GetFrameIndex 的一次播放语义。
        /// </summary>
        public int GetReviewFrameIndex(float elapsedSeconds)
        {
            if (!IsValid || !playOnceThenHoldFirstFrame)
                return GetFrameIndex(elapsedSeconds);

            float actionDuration = DurationSeconds;
            float cycleDuration = actionDuration + reviewReplayDelaySeconds;
            if (cycleDuration <= 0f)
                return 0;
            float phase = Mathf.Max(0f, elapsedSeconds) % cycleDuration;
            return phase >= actionDuration
                ? 0
                : Mathf.Min(
                    frameCount - 1,
                    Mathf.FloorToInt(phase * framesPerSecond));
        }

        public Rect GetFrameUv(int frameIndex)
        {
            if (!IsValid)
                return new Rect(0f, 0f, 1f, 1f);
            int frame = Mathf.Clamp(frameIndex, 0, frameCount - 1);
            int column = frame % atlasColumns;
            int rowFromTop = frame / atlasColumns;
            return new Rect(
                column / (float)atlasColumns,
                1f - (rowFromTop + 1f) / atlasRows,
                1f / atlasColumns,
                1f / atlasRows);
        }

        /// <summary>
        /// 由静态回退 Sprite 统一推导序列的 pivot、PPU 与本地完整帧边界。
        /// 运行时切片与编辑器预览共用，杜绝把窄图集帧拉伸进宽静态框。
        /// </summary>
        public bool TryGetFrameLayout(
            Sprite fallback,
            out Vector2 normalizedPivot,
            out float pixelsPerUnit,
            out Rect localFrameBounds)
        {
            normalizedPivot = new Vector2(0.5f, 0.5f);
            pixelsPerUnit = 100f;
            localFrameBounds = default;
            if (!IsValid || fallback == null || fallback.pixelsPerUnit <= 0f)
                return false;

            Vector2 fallbackSize = fallback.rect.size;
            if (fallbackSize.x <= 0f || fallbackSize.y <= 0f)
                return false;

            normalizedPivot = new Vector2(
                fallback.pivot.x / fallbackSize.x,
                fallback.pivot.y / fallbackSize.y);
            int frameWidth = frameAtlas.width / atlasColumns;
            int frameHeight = frameAtlas.height / atlasRows;
            float desiredWorldHeight = fallbackSize.y / fallback.pixelsPerUnit;
            pixelsPerUnit = frameHeight / Mathf.Max(0.001f, desiredWorldHeight);
            Vector2 localSize = new Vector2(
                frameWidth / pixelsPerUnit,
                frameHeight / pixelsPerUnit);
            Vector2 localMin = -Vector2.Scale(normalizedPivot, localSize);
            localFrameBounds = new Rect(localMin, localSize);
            return true;
        }

        /// <summary>
        /// Resolves an explicitly authored frame-foot anchor. This stays opt-in:
        /// legacy sequences continue to derive their ground from the fallback
        /// Sprite, while an atlas with different transparent bottom padding can
        /// keep its contact shadow attached to the visible feet.
        /// </summary>
        public bool TryResolveGroundAnchorY(
            Sprite fallback,
            out float localY)
        {
            localY = 0f;
            if (groundAnchorNormalizedY < 0f ||
                !TryGetFrameLayout(
                    fallback,
                    out _,
                    out _,
                    out Rect frameBounds))
            {
                return false;
            }

            localY = frameBounds.yMin +
                     frameBounds.height * groundAnchorNormalizedY;
            return true;
        }

        public bool TryValidate(string ownerId, out string error)
        {
            if (!IsConfigured)
            {
                error = null;
                return true;
            }
            if (frameCount <= 0 || atlasColumns <= 0 || atlasRows <= 0 ||
                frameCount > atlasColumns * atlasRows ||
                framesPerSecond <= 0f ||
                !IsFiniteNonNegative(reviewReplayDelaySeconds) ||
                !IsOptionalNormalizedUnitInterval(
                    groundAnchorNormalizedY))
            {
                error = $"{ownerId} 的地图序列帧数、网格或帧率无效。";
                return false;
            }
            if (frameAtlas.width % atlasColumns != 0 ||
                frameAtlas.height % atlasRows != 0)
            {
                error = $"{ownerId} 的地图序列图集不能被网格整除。";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= 0f;
        }

        private static bool IsOptionalNormalizedUnitInterval(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= -1f && value <= 1f;
        }
    }

    /// <summary>
    /// Owns optional contact-shadow geometry in the static Sprite's full rectangle.
    /// It resolves an immutable local rectangle; renderers, aura geometry and hit
    /// regions remain owned by their existing presentation modules.
    /// </summary>
    [Serializable]
    public struct MapContactShadowCalibration
    {
        public bool isConfigured;
        [Tooltip("阴影中心相对静态 Sprite 完整矩形的位置；左下为 (0,0)，右上为 (1,1)。")]
        public Vector2 normalizedCenter;
        [Tooltip("阴影椭圆宽高相对静态 Sprite 完整宽高的比例；不包含节点或 Boss 缩放。")]
        public Vector2 normalizedSize;

        public bool IsValid => !isConfigured ||
            (IsUnitValue(normalizedCenter.x) &&
             IsUnitValue(normalizedCenter.y) &&
             IsUnitValue(normalizedSize.x) && normalizedSize.x > 0f &&
             IsUnitValue(normalizedSize.y) && normalizedSize.y > 0f);

        /// <summary>
        /// Uses rect and pivot rather than tight mesh bounds. A sequence keeps
        /// this static calibration across frames; parent transforms apply scale.
        /// </summary>
        public bool TryResolve(Sprite sprite, out Rect localRect)
        {
            localRect = default;
            if (!isConfigured || !IsValid || sprite == null ||
                sprite.pixelsPerUnit <= 0f)
                return false;

            Vector2 canvasSize = sprite.rect.size / sprite.pixelsPerUnit;
            if (canvasSize.x <= 0f || canvasSize.y <= 0f)
                return false;

            Vector2 center = Vector2.Scale(canvasSize, normalizedCenter) -
                             sprite.pivot / sprite.pixelsPerUnit;
            Vector2 size = Vector2.Scale(canvasSize, normalizedSize);
            localRect = new Rect(center - size * 0.5f, size);
            return true;
        }

        private static bool IsUnitValue(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) &&
               value >= 0f && value <= 1f;
    }

    /// <summary>
    /// Describes one visible element in a map preview snapshot. Several
    /// elements may share an interactionGroupId so a monster and the seal it
    /// guards respond as one node without either visual knowing map rules.
    /// </summary>
    [Serializable]
    public sealed class MapPreviewNode
    {
        public string contentId;
        public string interactionGroupId;
        [Tooltip(
            "Optional stable enemy contentId represented by this visual. " +
            "This is distinct from interactionGroupId: several visuals may " +
            "belong to one encounter while representing different enemies.")]
        public string enemyContentId;
        public MapPreviewVisualKind visualKind;
        public Sprite sprite;
        [Tooltip(
            "可选 Resources 相对路径；用于既有不透明 GUID 素材，避免把该 GUID " +
            "手写进 YAML。sprite 有值时优先使用对象引用。")]
        public string spriteResourcePath;
        [Tooltip("可选：地图专用、无音频序列；静态 sprite 继续负责尺寸与回退。")]
        public MapSpriteSequence spriteSequence = new MapSpriteSequence();
        [Tooltip("可选静态脚底阴影校准；不改变灵气、污渍、脚雾或交互命中范围。")]
        public MapContactShadowCalibration contactShadow;
        [Tooltip("可选：由 Prefab 完整接管节点状态表现；sprite 仅作静态预览与回退素材。")]
        public GameObject stateVisualPrefab;
        public Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        [Tooltip("视觉相对节点锚点的局部世界偏移；独立于 visualScale，不改变逻辑坐标。")]
        public Vector2 visualOffset;
        [Min(0.01f)] public float visualScale = 1f;
        [Tooltip("点击框相对节点锚点的局部世界偏移；独立于视觉缩放。")]
        public Vector2 hitOffset;
        public Vector2 hitSize = Vector2.one;
        public int sortingOrder = 20;
        public MapVfxState initialState = MapVfxState.Locked;
        [Header("完成演出风格")]
        public MapNodeCompletionEffect completionEffect =
            MapNodeCompletionEffect.Purification;

        [Header("战斗环境点缀（可选）")]
        public Sprite battleForeground;
        public Color battleForegroundTint = Color.white;
        public Vector2 battleForegroundOffset;
        [Min(0.01f)] public float battleForegroundScale = 1f;
        [Range(0f, 1f)] public float battleFogIntensity;
        public Sprite postVictoryRevealSprite;

        /// <summary>
        /// 统一解析静态图。遗留不透明 GUID 只保留在原始 .meta 中，文本资产可用
        /// Resources 路径引用，避免生成编辑器无法提取的 YAML GUID。
        /// </summary>
        public Sprite ResolveSprite()
        {
            if (sprite != null)
                return sprite;
            return string.IsNullOrWhiteSpace(spriteResourcePath)
                ? null
                : Resources.Load<Sprite>(spriteResourcePath.Trim());
        }
    }

    /// <summary>One logical dependency's chosen visual endpoints, stored by stable IDs.</summary>
    [Serializable]
    public sealed class MapDependencyVisualAnchor
    {
        public string prerequisiteGroupId;
        public string dependentGroupId;
        public string prerequisiteVisualId;
        public string dependentVisualId;
    }

    /// <summary>
    /// An authored empty support position, not a node, reward or random choice.
    /// The Run supplies the selected guard IDs; unused slots render nothing.
    /// </summary>
    [Serializable]
    public sealed class MapExplorationRewardSlot
    {
        public string guardGroupId;
        public Vector2 normalizedPosition;
    }

    /// <summary>
    /// A configurable, self-validating visual snapshot for the F5 map
    /// sandbox. It owns framing and visual placement only; production map
    /// progression remains responsible for deriving semantic node states.
    /// </summary>
    [CreateAssetMenu(
        fileName = "map-preview-profile",
        menuName = "万界长卷/Map/Preview Profile")]
    public sealed class MapPreviewProfile : ScriptableObject
    {
        public string contentId;
        public Sprite background;
        [Min(0.1f)] public float orthographicSize = 5.4f;
        public Color backgroundTint = Color.white;
        public string debugFocusGroupId;
        [Tooltip("复合表现允许同组多个图像；单代表节点要求每个交互组只有一个地图入口。")]
        public MapInteractionVisualPolicy interactionVisualPolicy =
            MapInteractionVisualPolicy.CompositeVisuals;

        public List<MapPreviewNode> nodes = new List<MapPreviewNode>();
        public List<MapDependencyVisualAnchor> dependencyVisualAnchors =
            new List<MapDependencyVisualAnchor>();
        public List<MapExplorationRewardSlot> explorationRewardSlots =
            new List<MapExplorationRewardSlot>();

        [Header("楼层战斗背景配对")]
        public Sprite battleBackground;
        public Color battleBackgroundTint = Color.white;
        [Tooltip("随本层战斗背景使用经过人工校准的棋盘线色；关闭时沿用全局默认，不改变棋盘几何。")]
        public bool overrideBattleGridColor;
        public Color battleGridColor = new Color(0.42f, 0.18f, 0.1f, 0.34f);
        [Tooltip(
            "是否在战斗场景叠加独立的顶部云雾素材；背景图自身的自然云层不受影响。")]
        public bool battleTopFogEnabled;
        public string transitionProfileContentId;

        /// <summary>
        /// 节点位置/点击区最近一次写入的来源标记。由布局工具写入，供写回守卫判断
        /// 「这块坐标是谁定的」——避免旧的逐层 Configurator 静默覆盖已经人工或 AI
        /// 标注定稿的布局。留空表示未知来源（旧资产），不阻塞首次写回。
        /// 取值：hand / ai-annotation / configurator。
        /// </summary>
        [Header("节点布局来源（工具写入）")]
        public string layoutSource;
        /// <summary>写入轮次标识，例如 g2f5-r3。</summary>
        public string layoutRoundId;
        /// <summary>写入时间（UTC，ISO 8601）。</summary>
        public string layoutAppliedUtc;
        /// <summary>写入所用候选 JSON 的 SHA256。</summary>
        public string layoutCandidateSha256;

        /// <summary>
        /// Resolves the visuals that represent one enemy inside an encounter.
        /// Explicit stable bindings win; legacy groups without any explicit
        /// match retain their former all-visual behavior during migration.
        /// </summary>
        public List<MapPreviewNode> FindEnemyVisuals(
            string interactionGroupId,
            string enemyContentId)
        {
            var groupVisuals = new List<MapPreviewNode>();
            var explicitVisuals = new List<MapPreviewNode>();
            if (nodes == null)
                return groupVisuals;

            foreach (MapPreviewNode node in nodes)
            {
                if (node == null ||
                    !string.Equals(
                        node.interactionGroupId,
                        interactionGroupId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                groupVisuals.Add(node);
                if (string.Equals(
                        node.enemyContentId,
                        enemyContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    explicitVisuals.Add(node);
                }
            }

            return explicitVisuals.Count > 0
                ? explicitVisuals
                : groupVisuals;
        }

        /// <summary>
        /// Rejects ambiguous preview data before it reaches the scene. Stable
        /// IDs and normalized coordinates are part of the profile contract.
        /// </summary>
        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "Map preview profile requires a stable contentId.";
                return false;
            }

            if (interactionVisualPolicy != MapInteractionVisualPolicy.CompositeVisuals &&
                interactionVisualPolicy != MapInteractionVisualPolicy.SingleRepresentative)
            {
                error = $"{contentId} has an unsupported interactionVisualPolicy " +
                        $"value {(int)interactionVisualPolicy}.";
                return false;
            }

            if (background == null)
            {
                error = $"{contentId} has no map background.";
                return false;
            }

            if (orthographicSize <= 0f)
            {
                error = $"{contentId} has an invalid orthographic size.";
                return false;
            }

            if (nodes == null || nodes.Count == 0)
            {
                error = $"{contentId} contains no map preview nodes.";
                return false;
            }

            var nodeIds = new HashSet<string>();
            var groupStates = new Dictionary<string, MapVfxState>();
            var groupVisualIds = new Dictionary<string, string>();
            bool focusGroupExists = string.IsNullOrWhiteSpace(debugFocusGroupId);
            foreach (MapPreviewNode node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.contentId))
                {
                    error = $"{contentId} contains a node without contentId.";
                    return false;
                }

                if (!nodeIds.Add(node.contentId))
                {
                    error = $"{contentId} repeats node ID {node.contentId}.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(node.interactionGroupId))
                {
                    error = $"{node.contentId} requires an interactionGroupId.";
                    return false;
                }

                if (interactionVisualPolicy ==
                    MapInteractionVisualPolicy.SingleRepresentative)
                {
                    if (groupVisualIds.TryGetValue(
                            node.interactionGroupId, out string firstVisualId))
                    {
                        error = $"{contentId} requires one representative per interaction group: " +
                                $"{node.interactionGroupId} repeats visuals " +
                                $"{firstVisualId} and {node.contentId}.";
                        return false;
                    }
                    groupVisualIds.Add(node.interactionGroupId, node.contentId);
                }

                if (node.normalizedPosition.x < 0f ||
                    node.normalizedPosition.x > 1f ||
                    node.normalizedPosition.y < 0f ||
                    node.normalizedPosition.y > 1f)
                {
                    error = $"{node.contentId} lies outside normalized map bounds.";
                    return false;
                }

                if (node.visualScale <= 0f ||
                    node.hitSize.x <= 0f || node.hitSize.y <= 0f)
                {
                    error = $"{node.contentId} has a non-positive visual or hit size.";
                    return false;
                }

                if (float.IsNaN(node.visualOffset.x) || float.IsInfinity(node.visualOffset.x) ||
                    float.IsNaN(node.visualOffset.y) || float.IsInfinity(node.visualOffset.y) ||
                    float.IsNaN(node.hitOffset.x) || float.IsInfinity(node.hitOffset.x) ||
                    float.IsNaN(node.hitOffset.y) || float.IsInfinity(node.hitOffset.y))
                {
                    error = $"{node.contentId} has a non-finite visual or hit offset.";
                    return false;
                }

                if (node.visualKind == MapPreviewVisualKind.Sprite &&
                    node.ResolveSprite() == null)
                {
                    error = $"{node.contentId} is a sprite node without a resolvable sprite.";
                    return false;
                }

                if (!node.contactShadow.IsValid ||
                    (node.contactShadow.isConfigured &&
                     node.visualKind != MapPreviewVisualKind.Sprite))
                {
                    error = $"{node.contentId} 的接触阴影校准需要 Sprite、归一化中心及 (0,1] 范围的宽高。";
                    return false;
                }

                if (node.visualKind != MapPreviewVisualKind.Sprite &&
                    node.spriteSequence != null &&
                    node.spriteSequence.IsConfigured)
                {
                    error = $"{node.contentId} 不是 Sprite 节点，不能配置序列图集。";
                    return false;
                }
                if (node.spriteSequence != null &&
                    !node.spriteSequence.TryValidate(
                        node.contentId,
                        out error))
                {
                    return false;
                }

                if (node.stateVisualPrefab != null &&
                    node.stateVisualPrefab.GetComponent<
                        IMapNodeStateVisual>() == null)
                {
                    error = $"{node.contentId} state visual prefab must " +
                            $"implement {nameof(IMapNodeStateVisual)}.";
                    return false;
                }

                if (groupStates.TryGetValue(
                        node.interactionGroupId, out MapVfxState groupState) &&
                    groupState != node.initialState)
                {
                    error = $"{node.interactionGroupId} contains conflicting states.";
                    return false;
                }

                groupStates[node.interactionGroupId] = node.initialState;
                if (node.interactionGroupId == debugFocusGroupId)
                    focusGroupExists = true;
            }

            if (!focusGroupExists)
            {
                error = $"{contentId} cannot find debug focus group " +
                        $"{debugFocusGroupId}.";
                return false;
            }

            var slotGuards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MapExplorationRewardSlot slot in explorationRewardSlots ??
                     new List<MapExplorationRewardSlot>())
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.guardGroupId) ||
                    !slotGuards.Add(slot.guardGroupId) || !groupStates.ContainsKey(slot.guardGroupId) ||
                    float.IsNaN(slot.normalizedPosition.x) || float.IsNaN(slot.normalizedPosition.y) ||
                    slot.normalizedPosition.x < 0f || slot.normalizedPosition.x > 1f ||
                    slot.normalizedPosition.y < 0f || slot.normalizedPosition.y > 1f)
                {
                    error = $"{contentId} has a duplicate, unbound or out-of-bounds exploration slot.";
                    return false;
                }
            }

            // Battle background validation
            if (battleBackground == null)
            {
                error = $"{contentId} requires a battleBackground sprite.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(transitionProfileContentId))
            {
                error = $"{contentId} requires a transitionProfileContentId.";
                return false;
            }

            // Validate node battle fields
            var revealSprites = new Dictionary<string, List<Sprite>>();
            foreach (MapPreviewNode node in nodes)
            {
                if (node.battleForegroundScale <= 0f)
                {
                    error = $"{node.contentId} has non-positive battleForegroundScale.";
                    return false;
                }

                if (node.postVictoryRevealSprite != null)
                {
                    if (!revealSprites.TryGetValue(
                            node.interactionGroupId, out var list))
                    {
                        list = new List<Sprite>();
                        revealSprites[node.interactionGroupId] = list;
                    }
                    list.Add(node.postVictoryRevealSprite);
                }
            }

            foreach (var kvp in revealSprites)
            {
                if (kvp.Value.Count > 1)
                {
                    var distinct = new HashSet<Sprite>();
                    foreach (var s in kvp.Value)
                        distinct.Add(s);
                    if (distinct.Count > 1)
                    {
                        error = $"{kvp.Key} has conflicting postVictoryRevealSprite.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>按 groupId 查找第一个匹配的节点配置。</summary>
        public MapPreviewNode FindFirstNodeByGroupId(string groupId)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].interactionGroupId == groupId)
                    return nodes[i];
            }
            return null;
        }

        /// <summary>获取某组的唯一 postVictoryRevealSprite。无配置时返回 null。</summary>
        public Sprite GetPostVictoryRevealSprite(string groupId)
        {
            Sprite found = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].interactionGroupId != groupId)
                    continue;
                if (nodes[i].postVictoryRevealSprite == null)
                    continue;
                if (found != null && found != nodes[i].postVictoryRevealSprite)
                    return null; // conflicting — validation should have caught this
                found = nodes[i].postVictoryRevealSprite;
            }
            return found;
        }
    }
}
