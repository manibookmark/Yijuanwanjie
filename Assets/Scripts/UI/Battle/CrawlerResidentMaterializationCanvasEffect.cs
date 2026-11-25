using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 战斗驻场英灵首帧碎片的可调参数。只供驻灵轨道使用，不能传给
    /// 群英殿、卷灵或其他角色显化流程。
    /// </summary>
    public readonly struct CrawlerResidentMaterializationSettings
    {
        public int ParticleCount { get; }
        public float ShardScale { get; }

        public CrawlerResidentMaterializationSettings(
            int particleCount,
            float shardScale)
        {
            ParticleCount = particleCount;
            ShardScale = shardScale;
        }
    }

    /// <summary>
    /// 战斗驻场英灵专用的首帧碎片聚散。每个碎片直接引用待机图集首帧
    /// 的局部 UV：入场时从左侧屏幕外聚回人物轮廓，退场时沿反向轨迹打散。
    /// 本组件不参与卷灵显化，也不拥有规则层的召唤或凝神状态。
    /// </summary>
    public sealed class CrawlerResidentMaterializationCanvasEffect :
        MonoBehaviour
    {
        private sealed class Shard
        {
            public RectTransform Rect;
            public RawImage Image;
            public Vector2 Settled;
            public Vector2 Scattered;
            public float ScatteredScale;
        }

        private const int DefaultParticleCount = 96;
        // 2026-08-18 F7 人工确认后的左侧出屏散布，移除高级滑杆后固定。
        private const float ApprovedLeftScatterScale = 0.91078424f;
        private readonly List<Shard> _shards = new List<Shard>();

        private RectTransform _layer;
        private float _elapsed;
        private float _duration;
        private bool _reverse;
        private bool _playing;

        public bool IsPlaying => _playing;

        public void Build(
            RectTransform root,
            RectTransform actor,
            Texture frameAtlas,
            Rect frameUv,
            CrawlerResidentMaterializationSettings settings)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));
            if (frameAtlas == null)
                throw new ArgumentNullException(nameof(frameAtlas));

            Clear();
            Canvas.ForceUpdateCanvases();

            int count = Mathf.Clamp(
                settings.ParticleCount > 0
                    ? settings.ParticleCount
                    : DefaultParticleCount,
                CrawlerResidentLayoutProfile.MinMaterializationParticleCount,
                CrawlerResidentLayoutProfile.MaxMaterializationParticleCount);
            float shardScale = Mathf.Clamp(
                settings.ShardScale,
                CrawlerResidentLayoutProfile.MinMaterializationSizeScale,
                CrawlerResidentLayoutProfile.MaxMaterializationSizeScale);

            var layerObject = new GameObject(
                "CrawlerResidentMaterialization",
                typeof(RectTransform));
            layerObject.transform.SetParent(root, false);
            _layer = layerObject.GetComponent<RectTransform>();
            Stretch(_layer);
            _layer.SetSiblingIndex(Mathf.Min(
                actor.GetSiblingIndex() + 1,
                root.childCount - 1));

            Vector2 rootSize = root.rect.size;
            if (rootSize.x <= 0f || rootSize.y <= 0f)
                rootSize = new Vector2(1920f, 1080f);

            Vector2 target = new Vector2(
                (actor.anchorMin.x - 0.5f) * rootSize.x +
                actor.anchoredPosition.x,
                (actor.anchorMin.y - 0.5f) * rootSize.y +
                actor.anchoredPosition.y);

            Vector2 actorSize = actor.rect.size;
            if (actorSize.x <= 0f || actorSize.y <= 0f)
                actorSize = actor.sizeDelta;
            actorSize.x = Mathf.Max(1f, actorSize.x);
            actorSize.y = Mathf.Max(1f, actorSize.y);
            float actorAspect = actorSize.x / actorSize.y;
            int columns = Mathf.Max(
                2,
                Mathf.RoundToInt(Mathf.Sqrt(count * actorAspect)));
            int rows = Mathf.Max(2, Mathf.CeilToInt(count / (float)columns));
            Vector2 cellSize = new Vector2(
                actorSize.x / columns,
                actorSize.y / rows);

            Rect safeFrameUv = frameUv.width > 0f && frameUv.height > 0f
                ? frameUv
                : new Rect(0f, 0f, 1f, 1f);
            float insetU = 0.5f / Mathf.Max(1f, frameAtlas.width);
            float insetV = 0.5f / Mathf.Max(1f, frameAtlas.height);

            var random = new System.Random(0x484C53);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    var shardObject = new GameObject(
                        $"CharacterShard_{index:000}",
                        typeof(RectTransform),
                        typeof(RawImage));
                    shardObject.transform.SetParent(_layer, false);

                    RectTransform rect =
                        shardObject.GetComponent<RectTransform>();
                    rect.sizeDelta = cellSize * shardScale;

                    RawImage image = shardObject.GetComponent<RawImage>();
                    image.texture = frameAtlas;
                    float uvWidth = safeFrameUv.width / columns;
                    float uvHeight = safeFrameUv.height / rows;
                    image.uvRect = new Rect(
                        safeFrameUv.x + column * uvWidth + insetU,
                        safeFrameUv.y + row * uvHeight + insetV,
                        Mathf.Max(0.0001f, uvWidth - insetU * 2f),
                        Mathf.Max(0.0001f, uvHeight - insetV * 2f));
                    image.color = new Color(1f, 1f, 1f, 0f);
                    image.raycastTarget = false;

                    float normalizedX = (column + 0.5f) / columns;
                    float normalizedY = (row + 0.5f) / rows;
                    Vector2 actorLocal = new Vector2(
                        (normalizedX - actor.pivot.x) * actorSize.x,
                        (normalizedY - actor.pivot.y) * actorSize.y);
                    Vector2 settled = target + actorLocal;

                    // 战斗驻场英灵从左侧屏幕外进入。散布参数同时控制
                    // 出屏深度与纵向离散，保证最小值时也不会在屏内起步。
                    float outsideDepth = Mathf.Lerp(
                        40f,
                        260f,
                        Next01(random)) * ApprovedLeftScatterScale;
                    float verticalJitter = Mathf.Lerp(
                        -actorSize.y * 0.95f,
                        actorSize.y * 0.95f,
                        Next01(random)) * ApprovedLeftScatterScale;
                    Vector2 scattered = new Vector2(
                        -rootSize.x * 0.5f - outsideDepth,
                        Mathf.Clamp(
                            target.y + actorLocal.y * 0.55f +
                            verticalJitter,
                            -rootSize.y * 0.48f,
                            rootSize.y * 0.48f));
                    _shards.Add(new Shard
                    {
                        Rect = rect,
                        Image = image,
                        Settled = settled,
                        Scattered = scattered,
                        ScatteredScale = Mathf.Lerp(
                            0.55f,
                            0.9f,
                            Next01(random))
                    });
                }
            }

            _layer.gameObject.SetActive(false);
        }

        public void PlayEnter(float duration)
        {
            Begin(false, duration);
        }

        public void PlayExit(float duration)
        {
            Begin(true, duration);
        }

        private void Begin(bool reverse, float duration)
        {
            if (_layer == null || _shards.Count == 0)
                return;

            _reverse = reverse;
            _duration = Mathf.Max(0.05f, duration);
            _elapsed = 0f;
            _playing = true;
            _layer.gameObject.SetActive(true);
            UpdateShards(0f);
        }

        private void Update()
        {
            if (!_playing)
                return;

            _elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(_elapsed / _duration);
            UpdateShards(normalized);
            if (normalized < 1f)
                return;

            _playing = false;
            if (_layer != null)
                _layer.gameObject.SetActive(false);
        }

        private void UpdateShards(float normalized)
        {
            for (int i = 0; i < _shards.Count; i++)
            {
                Shard shard = _shards[i];
                float local = Mathf.Clamp01(normalized);
                float eased = local * local * (3f - 2f * local);
                Vector2 from = _reverse
                    ? shard.Settled
                    : shard.Scattered;
                Vector2 to = _reverse
                    ? shard.Scattered
                    : shard.Settled;
                shard.Rect.anchoredPosition = Vector2.LerpUnclamped(
                    from,
                    to,
                    eased);
                float scale = _reverse
                    ? Mathf.Lerp(1f, shard.ScatteredScale, eased)
                    : Mathf.Lerp(shard.ScatteredScale, 1f, eased);
                shard.Rect.localScale = Vector3.one * scale;

                float fadeIn = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(local / 0.1f));
                float fadeOutStart = _reverse ? 0.56f : 0.76f;
                float fadeOut = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(fadeOutStart, 1f, local));
                Color color = shard.Image.color;
                color.a = fadeIn * fadeOut;
                shard.Image.color = color;
            }
        }

        public void Clear()
        {
            _playing = false;
            _shards.Clear();
            if (_layer != null)
                Destroy(_layer.gameObject);
            _layer = null;
        }

        private static float Next01(System.Random random)
        {
            return (float)random.NextDouble();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
