using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 进化成果揭示的独立 Canvas 光场：拥有聚能、爆发、上升碎光的网格、
    /// 材质和阶段时钟，只读取卡牌尺寸，不修改卡牌 Transform 或结算数据。
    /// 位于成果卡后方，不遮盖卡面；结束仅清空自身，不收牌、不调用业务提交。
    /// </summary>
    public sealed class EvolutionCardPresentation : MaskableGraphic
    {
        internal const float ResultHoldDuration = 1.4f;
        private const float ChargeDuration = 0.65f;
        private const int RingSegments = 96;
        private enum Phase { None, Charge, Reveal }

        private Phase _phase;
        private float _elapsed;
        private float _chargeDelay;
        private Vector2 _centre;
        private Vector2 _cardHalfSize;
        private Material _ownedMaterial;

        internal static EvolutionCardPresentation Create(RectTransform parent)
        {
            var instance = new GameObject(
                "EvolutionCardLightField",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(EvolutionCardPresentation));
            instance.transform.SetParent(parent, false);
            RectTransform rect = instance.GetComponent<RectTransform>();
            RewardPanelFactory.Stretch(rect);
            var graphic = instance.GetComponent<EvolutionCardPresentation>();
            graphic.raycastTarget = false;
            Shader shader = Resources.Load<Shader>("Shaders/SealStampParticle");
            if (shader != null)
            {
                graphic._ownedMaterial = new Material(shader)
                {
                    name = "EvolutionCardLightFieldMaterial"
                };
                graphic.material = graphic._ownedMaterial;
            }
            return graphic;
        }

        internal void BeginCharge(RectTransform card, float revealDelay)
        {
            _centre = rectTransform.rect.center;
            _cardHalfSize = new Vector2(150f, 220f);
            if (card != null)
            {
                var corners = new Vector3[4];
                card.GetWorldCorners(corners);
                Vector2 min = rectTransform.InverseTransformPoint(corners[0]);
                Vector2 max = rectTransform.InverseTransformPoint(corners[2]);
                _cardHalfSize = new Vector2(
                    Mathf.Max(40f, Mathf.Abs(max.x - min.x) * 0.5f),
                    Mathf.Max(60f, Mathf.Abs(max.y - min.y) * 0.5f));
                // The card remains the final opaque layer. This field never writes it.
                transform.SetSiblingIndex(card.GetSiblingIndex());
            }
            _chargeDelay = Mathf.Max(0f, revealDelay - ChargeDuration);
            _elapsed = 0f;
            _phase = Phase.Charge;
            SetVerticesDirty();
        }

        internal void Reveal()
        {
            _phase = Phase.Reveal;
            _elapsed = 0f;
            SetVerticesDirty();
        }

        internal void StopPresentation()
        {
            _phase = Phase.None;
            _elapsed = 0f;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (_phase == Phase.None)
                return;
            _elapsed += Time.unscaledDeltaTime;
            if (_phase == Phase.Reveal && _elapsed >= ResultHoldDuration)
            {
                StopPresentation();
                return;
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_phase == Phase.Charge && _elapsed >= _chargeDelay)
                PopulateCharge(helper);
            else if (_phase == Phase.Reveal)
                PopulateReveal(helper);
        }

        private void PopulateCharge(VertexHelper helper)
        {
            float progress = Mathf.Clamp01((_elapsed - _chargeDelay) / ChargeDuration);
            Vector2 radius = _cardHalfSize + Vector2.one * Mathf.Lerp(110f, 28f, progress);
            AddRing(helper, radius, 3.5f, 0.55f * progress, progress * 0.7f);
            for (int i = 0; i < 64; i++)
            {
                float cycle = Mathf.Repeat(progress * 1.25f + Noise(i * 13 + 1), 1f);
                float angle = i * 2.399963f + progress * 0.4f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 orbit = _cardHalfSize + Vector2.one * Mathf.Lerp(170f, 24f, cycle);
                Vector2 point = _centre + Vector2.Scale(direction, orbit);
                Color tint = SpiritEnergyColorPalette.ParticleCore;
                tint.a = Mathf.Sin(cycle * Mathf.PI) * progress * 0.85f;
                AddQuad(helper, point, direction * 7f,
                    new Vector2(-direction.y, direction.x) * 1.7f, tint);
            }
        }

        private void PopulateReveal(VertexHelper helper)
        {
            float progress = Mathf.Clamp01(_elapsed / ResultHoldDuration);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, progress);
            float flash = Mathf.Exp(-_elapsed * 6f);
            // A soft backlight, never a full-screen white flash.
            Color halo = SpiritEnergyColorPalette.ParticleHighlight;
            halo.a = flash * 0.72f;
            AddQuad(helper, _centre,
                Vector2.right * (_cardHalfSize.x + 70f),
                Vector2.up * (_cardHalfSize.y + 65f), halo);

            for (int ring = 0; ring < 3; ring++)
            {
                float local = Mathf.Clamp01((_elapsed - ring * 0.09f) / 0.85f);
                if (_elapsed < ring * 0.09f || local >= 1f)
                    continue;
                float eased = 1f - Mathf.Pow(1f - local, 3f);
                Vector2 radius = _cardHalfSize + Vector2.one * Mathf.Lerp(18f, 180f + ring * 25f, eased);
                AddRing(helper, radius, Mathf.Lerp(8f, 1.5f, local),
                    (1f - local) * 0.85f, ring * 0.2f);
            }

            // Tall columns stay along the card edges so the new illustration is legible.
            for (int beam = 0; beam < 10; beam++)
            {
                float side = beam % 2 == 0 ? -1f : 1f;
                float x = side * (_cardHalfSize.x + 18f + Noise(beam * 11) * 64f);
                float height = (_cardHalfSize.y + 100f) * Mathf.Lerp(0.8f, 1.4f, Noise(beam * 7));
                Color tint = SpiritEnergyColorPalette.ParticleCore;
                tint.a = fade * 0.24f;
                AddQuad(helper, _centre + new Vector2(x, progress * 70f),
                    Vector2.right * (5f + Noise(beam * 23) * 11f),
                    Vector2.up * height, tint);
            }

            for (int i = 0; i < 110; i++)
            {
                float delay = Noise(i * 17 + 5) * 0.25f;
                float local = Mathf.Clamp01((_elapsed - delay) / (ResultHoldDuration - delay));
                if (_elapsed < delay || local >= 1f)
                    continue;
                float angle = i * 2.399963f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float outward = (1f - Mathf.Pow(1f - local, 3f)) * (75f + Noise(i * 31) * 150f);
                Vector2 point = _centre + Vector2.Scale(direction, _cardHalfSize + Vector2.one * 12f)
                    + direction * outward + Vector2.up * local * local * 180f;
                float size = Mathf.Lerp(2.2f, 4.8f, Noise(i * 43));
                Color tint = i % 3 == 0
                    ? SpiritEnergyColorPalette.ParticleHighlight
                    : SpiritEnergyColorPalette.ParticleCore;
                tint.a = Mathf.Sin(Mathf.Min(1f, local * 5f) * Mathf.PI * 0.5f)
                    * (1f - local) * 0.92f;
                AddQuad(helper, point, Vector2.right * size,
                    Vector2.up * size * Mathf.Lerp(2.8f, 1f, local), tint);
            }
        }

        private void AddRing(VertexHelper helper, Vector2 radius,
            float width, float alpha, float rotation)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / RingSegments + rotation;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 tangent = new Vector2(-radial.y * radius.x, radial.x * radius.y);
                Color tint = SpiritEnergyColorPalette.ParticleHighlight;
                tint.a = alpha;
                AddQuad(helper, _centre + Vector2.Scale(radial, radius),
                    tangent * (Mathf.PI / RingSegments * 1.45f), radial * width, tint);
            }
        }

        private static void AddQuad(VertexHelper helper, Vector2 centre,
            Vector2 axisX, Vector2 axisY, Color tint)
        {
            int start = helper.currentVertCount;
            helper.AddVert(centre - axisX - axisY, tint, Vector2.zero);
            helper.AddVert(centre - axisX + axisY, tint, Vector2.up);
            helper.AddVert(centre + axisX + axisY, tint, Vector2.one);
            helper.AddVert(centre + axisX - axisY, tint, Vector2.right);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }

        private static float Noise(int seed)
            => Mathf.Repeat(Mathf.Sin(seed * 12.9898f + 4.1414f) * 43758.5453f, 1f);

        protected override void OnDisable()
        {
            StopPresentation();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_ownedMaterial != null)
                Destroy(_ownedMaterial);
            _ownedMaterial = null;
            base.OnDestroy();
        }
    }
}
