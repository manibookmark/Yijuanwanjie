using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 钤印流程唯一的程序化粒子层：命中时向两侧挤出灵气，收卡时从
    /// 卡面范围析出碎光并随风消散。只生成 UI Mesh，也永不参与射线。
    /// </summary>
    public sealed class SealStampBurstGraphic : MaskableGraphic
    {
        private enum EffectMode { None, Impact, CardDissolve }

        private const float FallbackImpactDuration = 0.72f;
        private const float FallbackDissolveDuration = 0.82f;

        private SealStampVfxProfile _profile;
        private EffectMode _mode;
        private float _elapsed = -1f;
        private readonly Vector2[] _cardCorners = new Vector2[4];

        internal float DissolveDuration => _profile != null
            ? _profile.DissolveDuration
            : FallbackDissolveDuration;

        internal void Configure(SealStampVfxProfile profile)
        {
            _profile = profile;
            SetVerticesDirty();
        }

        public void PlayBurst()
        {
            Begin(EffectMode.Impact);
        }

        public void PlayDissolve(RectTransform card)
        {
            if (card == null)
                return;
            var worldCorners = new Vector3[4];
            card.GetWorldCorners(worldCorners);
            for (int index = 0; index < worldCorners.Length; index++)
                _cardCorners[index] = rectTransform.InverseTransformPoint(
                    worldCorners[index]);
            Begin(EffectMode.CardDissolve);
        }

        public void StopBurst()
        {
            _mode = EffectMode.None;
            _elapsed = -1f;
            enabled = false;
            SetVerticesDirty();
        }

        private void Begin(EffectMode mode)
        {
            _mode = mode;
            _elapsed = 0f;
            gameObject.SetActive(true);
            enabled = true;
            transform.SetAsLastSibling();
            SetVerticesDirty();
        }

        private void Update()
        {
            if (_elapsed < 0f)
                return;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= CurrentDuration())
            {
                StopBurst();
                return;
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_elapsed < 0f || _elapsed >= CurrentDuration())
                return;
            if (_mode == EffectMode.Impact)
                PopulateImpact(helper);
            else if (_mode == EffectMode.CardDissolve)
                PopulateDissolve(helper);
        }

        private void PopulateImpact(VertexHelper helper)
        {
            Vector2 centre = rectTransform.rect.center;
            int shards = _profile != null ? _profile.ImpactShardCount : 96;
            int dust = _profile != null ? _profile.ImpactDustCount : 180;
            for (int index = 0; index < shards; index++)
                AddImpactShard(helper, centre, index);
            for (int index = 0; index < dust; index++)
                AddImpactDust(helper, centre, index);
        }

        private void AddImpactShard(
            VertexHelper helper,
            Vector2 centre,
            int index)
        {
            float delay = Hash01(index * 17 + 3) * GetImpactDelay();
            float progress = LocalProgress(CurrentDuration(), delay);
            if (progress < 0f || progress >= 1f)
                return;

            // 多数碎光向左右双叶喷出，少数保留斜向呼吸，形成“被压出”。
            float side = index % 2 == 0 ? 1f : -1f;
            float spread = Mathf.Lerp(-0.72f, 0.72f,
                Hash01(index * 29 + 11));
            if (index % 5 == 0)
                spread *= 1.8f;
            float compression = _profile != null
                ? _profile.ImpactVerticalCompression
                : 0.55f;
            Vector2 radial = new Vector2(side, spread * compression);
            Vector2 direction = radial.normalized;
            Vector2 tangent = new Vector2(-direction.y, direction.x);
            float start = _profile != null
                ? _profile.ImpactStartRadius
                : 58f;
            float end = (_profile != null
                ? _profile.ImpactEndRadius
                : 310f) * Mathf.Lerp(
                    0.82f, 1.12f, Hash01(index * 53 + 19));
            float radius = Mathf.Lerp(start, end, EaseOut(progress));
            float baseLength = _profile != null
                ? _profile.ImpactShardLength
                : 20f;
            float length = baseLength * Mathf.Lerp(
                0.55f, 1.25f, Hash01(index * 67 + 5)) *
                Mathf.Lerp(1f, 0.18f, progress);
            float baseWidth = _profile != null
                ? _profile.ImpactShardWidth
                : 0.85f;
            float width = baseWidth * Mathf.Lerp(1f, 0.28f, progress);
            float driftAmount = _profile != null
                ? _profile.ImpactTangentialDrift
                : 16f;
            Vector2 drift = tangent * Mathf.Sin(progress * Mathf.PI) *
                Mathf.Lerp(-driftAmount, driftAmount,
                    Hash01(index * 71 + 23));
            Color tint = index % 5 == 0
                ? SpiritEnergyColorPalette.ParticleHighlight
                : SpiritEnergyColorPalette.ParticleCore;
            tint.a = FadeAlpha(progress) * GetImpactAlpha();
            AddQuad(helper, centre + radial * radius + drift,
                direction * (length * 0.5f),
                tangent * (width * 0.5f), tint);
        }

        private void AddImpactDust(
            VertexHelper helper,
            Vector2 centre,
            int index)
        {
            float delay = GetImpactDelay() * Mathf.Lerp(
                0.25f, 1.35f, Hash01(index * 31 + 13));
            float progress = LocalProgress(CurrentDuration(), delay);
            if (progress < 0f || progress >= 1f)
                return;

            float angle = Hash01(index * 43 + 17) * Mathf.PI * 2f;
            float compression = _profile != null
                ? _profile.ImpactVerticalCompression
                : 0.55f;
            Vector2 radial = new Vector2(
                Mathf.Cos(angle), Mathf.Sin(angle) * compression);
            float start = (_profile != null
                ? _profile.ImpactStartRadius
                : 58f) * 1.15f;
            float end = (_profile != null
                ? _profile.ImpactEndRadius
                : 310f) * Mathf.Lerp(
                    0.58f, 0.95f, Hash01(index * 59 + 29));
            float minSize = _profile != null
                ? _profile.ImpactDustMinSize
                : 0.6f;
            float maxSize = _profile != null
                ? _profile.ImpactDustMaxSize
                : 2.2f;
            float size = Mathf.Lerp(
                Mathf.Lerp(minSize, maxSize,
                    Hash01(index * 73 + 37)),
                minSize * 0.35f,
                progress);
            Color tint = index % 4 == 0
                ? SpiritEnergyColorPalette.ParticleDeep
                : SpiritEnergyColorPalette.ParticleHighlight;
            tint.a = FadeAlpha(progress) * GetImpactAlpha() * 0.68f;
            AddQuad(helper,
                centre + radial * Mathf.Lerp(start, end, EaseOut(progress)),
                Vector2.right * size,
                Vector2.up * size * 0.62f,
                tint);
        }

        private void PopulateDissolve(VertexHelper helper)
        {
            int count = _profile != null
                ? _profile.DissolveParticleCount
                : 280;
            float duration = CurrentDuration();
            float delaySpread = _profile != null
                ? _profile.DissolveDelaySpread
                : 0.22f;
            Vector2 wind = _profile != null
                ? _profile.DissolveWind
                : new Vector2(175f, 58f);
            float turbulence = _profile != null
                ? _profile.DissolveTurbulence
                : 34f;
            float minSize = _profile != null
                ? _profile.DissolveParticleMinSize
                : 0.65f;
            float maxSize = _profile != null
                ? _profile.DissolveParticleMaxSize
                : 2.6f;
            float maxAlpha = _profile != null
                ? _profile.DissolveAlpha
                : 0.86f;
            Vector2 direction = wind.sqrMagnitude > 0.01f
                ? wind.normalized
                : Vector2.right;
            Vector2 normal = new Vector2(-direction.y, direction.x);

            for (int index = 0; index < count; index++)
            {
                float delay = Hash01(index * 47 + 7) * delaySpread;
                float progress = LocalProgress(duration, delay);
                if (progress < 0f || progress >= 1f)
                    continue;
                Vector2 source = BilinearCardPoint(
                    Hash01(index * 61 + 11),
                    Hash01(index * 79 + 19));
                float phase = Hash01(index * 97 + 31) * Mathf.PI * 2f;
                float wobble = Mathf.Sin(
                    progress * Mathf.PI * 2.5f + phase) * turbulence *
                    Mathf.Sin(progress * Mathf.PI);
                Vector2 position = source + wind * EaseOut(progress) +
                    normal * wobble + Vector2.up *
                    (Mathf.Sin(progress * Mathf.PI) * turbulence * 0.28f);
                float size = Mathf.Lerp(minSize, maxSize,
                    Hash01(index * 103 + 37)) *
                    Mathf.Lerp(1f, 0.28f, progress);
                float streak = Mathf.Lerp(1f, 2.8f, progress) *
                    Mathf.Lerp(0.75f, 1.25f,
                        Hash01(index * 109 + 41));
                Color tint = index % 9 == 0
                    ? SpiritEnergyColorPalette.ParticleHighlight
                    : index % 4 == 0
                        ? SpiritEnergyColorPalette.ParticleDeep
                        : SpiritEnergyColorPalette.ParticleCore;
                tint.a = FadeAlpha(progress) * maxAlpha;
                AddQuad(helper, position, direction * size * streak,
                    normal * size, tint);
            }
        }

        private Vector2 BilinearCardPoint(float x, float y)
        {
            Vector2 bottom = Vector2.Lerp(_cardCorners[0], _cardCorners[3], x);
            Vector2 top = Vector2.Lerp(_cardCorners[1], _cardCorners[2], x);
            return Vector2.Lerp(bottom, top, y);
        }

        private float LocalProgress(float duration, float delay)
        {
            if (_elapsed < delay)
                return -1f;
            return Mathf.Clamp01(
                (_elapsed - delay) / Mathf.Max(0.01f, duration - delay));
        }

        private float CurrentDuration()
        {
            if (_mode == EffectMode.CardDissolve)
                return DissolveDuration;
            return _profile != null
                ? _profile.ImpactDuration
                : FallbackImpactDuration;
        }

        private float GetImpactDelay()
        {
            return _profile != null ? _profile.ImpactDelaySpread : 0.10f;
        }

        private float GetImpactAlpha()
        {
            return _profile != null ? _profile.ImpactAlpha : 0.78f;
        }

        private static void AddQuad(VertexHelper helper, Vector2 centre,
            Vector2 axisA, Vector2 axisB, Color color)
        {
            int start = helper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = centre - axisA - axisB;
            vertex.uv0 = new Vector2(0f, 0f);
            helper.AddVert(vertex);
            vertex.position = centre - axisA + axisB;
            vertex.uv0 = new Vector2(0f, 1f);
            helper.AddVert(vertex);
            vertex.position = centre + axisA + axisB;
            vertex.uv0 = new Vector2(1f, 1f);
            helper.AddVert(vertex);
            vertex.position = centre + axisA - axisB;
            vertex.uv0 = new Vector2(1f, 0f);
            helper.AddVert(vertex);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }

        private static float FadeAlpha(float progress)
        {
            float fadeIn = Mathf.Clamp01(progress / 0.08f);
            return fadeIn * (1f - progress) * (1f - progress);
        }

        private static float EaseOut(float progress)
        {
            float inverse = 1f - progress;
            return 1f - inverse * inverse * inverse;
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
