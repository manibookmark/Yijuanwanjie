using UnityEngine;
using UnityEngine.UI;
using SpiritEnergyColorPalette =
    WanjieScroll.Core.SpiritEnergyColorPalette;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 驻场英灵双股螺旋的一层绘制。只持有绘制快照，不推进时间；
    /// 前后两层共用 FeedbackView 的相位，按身体包络生成光粒与短尾。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrawlerResidentFootAuraGraphic : MaskableGraphic
    {
        private const float SpiralTurns = 2.35f;
        private const float TwoPi = Mathf.PI * 2f;

        private int _particleCount = 36;
        private float _radius = 64f;
        private float _particleSize = 5f;
        private Rect _bodyBounds = new Rect(0.2f, 0.06f, 0.6f, 0.88f);
        private bool _frontLayer;
        private Vector2 _offset;
        private float _intensity;
        private float _phase;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            enabled = false;
        }

        public void ApplySettings(CrawlerResidentAuraSettings settings, bool frontLayer)
        {
            int particleCount = Mathf.Clamp(
                settings.ParticleCount,
                CrawlerResidentLayoutProfile.MinAuraParticleCount,
                CrawlerResidentLayoutProfile.MaxAuraParticleCount);
            float radius = Mathf.Clamp(
                settings.Radius,
                CrawlerResidentLayoutProfile.MinAuraRadius,
                CrawlerResidentLayoutProfile.MaxAuraRadius);
            float particleSize = Mathf.Clamp(
                settings.ParticleSize,
                CrawlerResidentLayoutProfile.MinAuraParticleSize,
                CrawlerResidentLayoutProfile.MaxAuraParticleSize);
            Vector2 offset = new Vector2(
                Mathf.Clamp(
                    settings.Offset.x,
                    CrawlerResidentPresentationProfile.MinHoverAuraOffset,
                    CrawlerResidentPresentationProfile.MaxHoverAuraOffset),
                Mathf.Clamp(
                    settings.Offset.y,
                    CrawlerResidentPresentationProfile.MinHoverAuraOffset,
                    CrawlerResidentPresentationProfile.MaxHoverAuraOffset));

            if (_particleCount == particleCount &&
                Mathf.Approximately(_radius, radius) &&
                Mathf.Approximately(_particleSize, particleSize) &&
                _bodyBounds == settings.BodyBounds && _frontLayer == frontLayer &&
                Vector2.SqrMagnitude(_offset - offset) < 0.0001f)
            {
                return;
            }

            _particleCount = particleCount;
            _radius = radius;
            _particleSize = particleSize;
            _bodyBounds = settings.BodyBounds;
            _frontLayer = frontLayer;
            _offset = offset;
            if (enabled)
                SetVerticesDirty();
        }

        public void SetFrame(float phase, float intensity)
        {
            float clamped = Mathf.Clamp(intensity, 0f, 1.35f);
            if (Mathf.Approximately(_intensity, clamped) &&
                Mathf.Approximately(_phase, phase) &&
                enabled == (clamped > 0.001f))
            {
                return;
            }

            _intensity = clamped;
            _phase = phase;
            enabled = clamped > 0.001f;
            if (enabled)
                SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_intensity <= 0.001f || _particleCount <= 0)
                return;

            Rect rect = rectTransform.rect;
            float scale = rect.height / 260f;
            float bottom = rect.yMin + rect.height * _bodyBounds.yMin + _offset.y * scale;
            float top = rect.yMin + rect.height * _bodyBounds.yMax + _offset.y * scale;
            float height = Mathf.Max(1f, top - bottom);
            Vector2 center = new Vector2(
                rect.xMin + rect.width * _bodyBounds.center.x + _offset.x * scale,
                0f);
            // 半径滑杆保留：64 为身体适配基准，数值控制包络松紧；不再给所有体型同一像素半径。
            float radius = (rect.width * _bodyBounds.width * 0.5f + height * 0.05f) * (_radius / 64f);
            AddParticlePass(vertexHelper, center, bottom, height, radius, scale);
        }

        private void AddParticlePass(
            VertexHelper vertexHelper,
            Vector2 center,
            float bottom,
            float height,
            float radius,
            float scale)
        {
            for (int i = 0; i < _particleCount; i++)
            {
                int arm = i & 1;
                int indexOnArm = i / 2;
                int countOnArm = (_particleCount + (arm == 0 ? 1 : 0)) / 2;
                float baseProgress = (indexOnArm + 0.5f) /
                    Mathf.Max(1, countOnArm);
                float progress = Mathf.Repeat(baseProgress + _phase, 1f);
                float angle = progress * SpiralTurns * TwoPi + arm * Mathf.PI;
                float depth = Mathf.Cos(angle);
                bool isFront = depth >= 0f;
                if (isFront != _frontLayer)
                    continue;

                float depth01 = depth * 0.5f + 0.5f;
                float edgeFade = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(progress / 0.11f));
                edgeFade *= 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((progress - 0.84f) / 0.16f));
                float alpha = edgeFade * _intensity *
                    Mathf.Lerp(0.32f, 0.94f, depth01);
                // 头部前景减弱，不用明亮光粒遮住五官。
                if (_frontLayer)
                    alpha *= Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(0.72f, 0.96f, progress));
                float size = _particleSize * scale *
                    Mathf.Lerp(0.68f, 1.28f, depth01);
                Vector2 position = new Vector2(
                    center.x + Mathf.Sin(angle) * radius,
                    bottom + progress * height);
                Vector2 tangent = new Vector2(
                    Mathf.Cos(angle) * radius * SpiralTurns * TwoPi,
                    height).normalized;

                Color particleColor = Color.Lerp(
                    SpiritEnergyColorPalette.ParticleCore,
                    SpiritEnergyColorPalette.ParticleHighlight,
                    depth01);
                AddParticle(
                    vertexHelper,
                    position,
                    tangent,
                    size,
                    alpha,
                    particleColor);
            }
        }

        private static void AddParticle(
            VertexHelper vertexHelper,
            Vector2 position,
            Vector2 tangent,
            float size,
            float alpha,
            Color color)
        {
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float tailLength = size * 3.2f;
            float halfWidth = size * 0.34f;
            Vector2 tail = position - tangent * tailLength;
            Color headColor = WithAlpha(color, alpha * 0.42f);
            Color tailColor = WithAlpha(color, 0f);
            int baseIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, position + normal * halfWidth, headColor);
            AddVertex(vertexHelper, position - normal * halfWidth, headColor);
            AddVertex(vertexHelper, tail - normal * halfWidth * 0.25f, tailColor);
            AddVertex(vertexHelper, tail + normal * halfWidth * 0.25f, tailColor);
            vertexHelper.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vertexHelper.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);

            AddDiamond(
                vertexHelper,
                position,
                size * 1.85f,
                WithAlpha(
                    SpiritEnergyColorPalette.ParticleDeep,
                    alpha * 0.46f));
            AddDiamond(
                vertexHelper,
                position,
                size,
                WithAlpha(color, alpha * 0.72f));
            AddDiamond(
                vertexHelper,
                position,
                size * 0.38f,
                WithAlpha(Color.white, alpha));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static void AddDiamond(
            VertexHelper vertexHelper,
            Vector2 center,
            float size,
            Color color)
        {
            int baseIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center + Vector2.up * size, color);
            AddVertex(vertexHelper, center + Vector2.right * size * 0.62f, color);
            AddVertex(vertexHelper, center + Vector2.down * size, color);
            AddVertex(vertexHelper, center + Vector2.left * size * 0.62f, color);
            vertexHelper.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vertexHelper.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertexHelper.AddVert(vertex);
        }
    }
}
