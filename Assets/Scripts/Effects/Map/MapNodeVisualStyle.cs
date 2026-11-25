using System;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// Resolved, presentation-only style. The composition root selects it;
    /// Effects never interprets reward kinds or changes a shared map asset.
    /// </summary>
    public sealed class MapNodeVisualStyle
    {
        public string ContentId { get; }
        public MapPreviewVisualKind VisualKind { get; }
        public Sprite Sprite { get; }
        public GameObject StatePrefab { get; }
        public float MaxWorldSize { get; }
        public MapRewardMotionKind Motion { get; }
        public bool NormalizedArtwork { get; }
        private readonly Vector2? _spriteHitSize;

        public MapNodeVisualStyle(string contentId, MapPreviewVisualKind visualKind,
            Sprite sprite, float maxWorldSize, MapRewardMotionKind motion,
            GameObject statePrefab = null, bool normalizedArtwork = false,
            bool fitHitToSprite = false)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Map style requires a stable ID.", nameof(contentId));
            if (visualKind == MapPreviewVisualKind.Sprite && sprite == null)
                throw new ArgumentException("Map sprite style requires artwork.", nameof(sprite));
            ContentId = contentId;
            VisualKind = visualKind;
            Sprite = sprite;
            MaxWorldSize = maxWorldSize;
            Motion = motion;
            StatePrefab = statePrefab;
            NormalizedArtwork = normalizedArtwork;
            if (fitHitToSprite && visualKind == MapPreviewVisualKind.Sprite && statePrefab == null)
                _spriteHitSize = ResolveSpriteHitSize();
        }

        /// <summary>Keep identity and placement; replacement artwork may own its hit envelope.</summary>
        public MapPreviewNode ResolveNode(MapPreviewNode source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            float scale = VisualKind != MapPreviewVisualKind.Sprite || NormalizedArtwork
                ? MaxWorldSize : MaxWorldSize / Mathf.Max(0.01f,
                    Mathf.Max(Sprite.bounds.size.x, Sprite.bounds.size.y));
            return new MapPreviewNode
            {
                contentId = source.contentId,
                interactionGroupId = source.interactionGroupId,
                enemyContentId = source.enemyContentId,
                normalizedPosition = source.normalizedPosition,
                visualOffset = source.visualOffset,
                initialState = source.initialState,
                sortingOrder = source.sortingOrder,
                hitOffset = _spriteHitSize.HasValue ? source.visualOffset : source.hitOffset,
                hitSize = _spriteHitSize ?? source.hitSize,
                visualKind = VisualKind,
                sprite = Sprite,
                visualScale = scale,
                stateVisualPrefab = StatePrefab,
                completionEffect = MapNodeCompletionEffect.None,
                battleForeground = source.battleForeground,
                battleForegroundTint = source.battleForegroundTint,
                battleForegroundOffset = source.battleForegroundOffset,
                battleForegroundScale = source.battleForegroundScale,
                battleFogIntensity = source.battleFogIntensity,
                postVictoryRevealSprite = source.postVictoryRevealSprite
            };
        }

        private Vector2 ResolveSpriteHitSize()
        {
            // Imported physics outlines are available without readable textures.
            // Measure once per resolved style, including disconnected silhouettes.
            var points = new System.Collections.Generic.List<Vector2>();
            Vector2 extent = Vector2.zero;
            for (int shape = 0; shape < Sprite.GetPhysicsShapeCount(); shape++)
            {
                Sprite.GetPhysicsShape(shape, points);
                foreach (Vector2 point in points)
                {
                    extent.x = Mathf.Max(extent.x, Mathf.Abs(point.x));
                    extent.y = Mathf.Max(extent.y, Mathf.Abs(point.y));
                }
            }
            if (extent.x <= 0f || extent.y <= 0f)
            {
                Bounds bounds = Sprite.bounds;
                extent = new Vector2(
                    Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)),
                    Mathf.Max(Mathf.Abs(bounds.min.y), Mathf.Abs(bounds.max.y)));
            }
            float scale = NormalizedArtwork ? MaxWorldSize : MaxWorldSize /
                Mathf.Max(0.01f, Mathf.Max(Sprite.bounds.size.x, Sprite.bounds.size.y));
            return extent * (2f * scale * 1.1f);
        }
    }
}
