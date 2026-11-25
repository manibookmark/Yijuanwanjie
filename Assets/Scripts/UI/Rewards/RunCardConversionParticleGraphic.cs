using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 归元专用的单 Mesh 程序化粒子系统。粒子使用中心高亮、边缘透明的
    /// 菱形三角扇，不读取纸片/火花 Sprite，也不创建逐粒子 GameObject，
    /// 从结构上避免贴图方块在碑芯叠加。
    /// </summary>
    public sealed class RunCardConversionParticleGraphic : MaskableGraphic
    {
        private enum EffectMode
        {
            None,
            CardConvergence,
            JadeConvergence
        }

        private const float CardDuration = 1.52f;
        private const float JadeDuration = 1.16f;
        private const int CardParticleCount = 184;
        private const int JadeParticleCount = 112;

        public bool UseManaPalette { get; set; }
        private Color Jade => UseManaPalette
            ? SpiritEnergyColorPalette.ManaFull : SpiritEnergyColorPalette.Energy;
        private Color JadeCore => UseManaPalette
            ? SpiritEnergyColorPalette.ManaFull : SpiritEnergyColorPalette.ParticleCore;
        private Color JadeHighlight => UseManaPalette
            ? SpiritEnergyColorPalette.ManaHighlight : SpiritEnergyColorPalette.ParticleHighlight;
        private static readonly Color PaperWarm =
            new Color(0.88f, 0.76f, 0.55f, 1f);

        private readonly Vector2[] _sourceCorners = new Vector2[4];
        private EffectMode _mode;
        private Vector2 _destination;
        private float _elapsed = -1f;

        public float Duration => _mode == EffectMode.JadeConvergence
            ? JadeDuration
            : CardDuration;

        public void PlayCardConvergence(
            RectTransform source,
            RectTransform destination)
        {
            if (source == null || destination == null)
            {
                StopEffect();
                return;
            }

            CaptureSource(source);
            _destination = CaptureCentre(destination);
            Begin(EffectMode.CardConvergence);
        }

        public void PlayJadeConvergence(RectTransform destination)
        {
            if (destination == null)
            {
                StopEffect();
                return;
            }

            _destination = CaptureCentre(destination);
            Begin(EffectMode.JadeConvergence);
        }

        public void StopEffect()
        {
            _mode = EffectMode.None;
            _elapsed = -1f;
            enabled = false;
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            color = Color.white;
            raycastTarget = false;
            enabled = false;
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
            if (_elapsed >= Duration)
            {
                StopEffect();
                return;
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_elapsed < 0f || _mode == EffectMode.None)
                return;

            float progress = Mathf.Clamp01(_elapsed / Duration);
            if (_mode == EffectMode.CardConvergence)
                PopulateCardConvergence(helper, progress);
            else
                PopulateJadeConvergence(helper, progress);
            PopulateCoreAbsorption(helper, progress);
        }

        private void PopulateCardConvergence(
            VertexHelper helper,
            float globalProgress)
        {
            int particleCount = UseManaPalette ? 96 : CardParticleCount;
            for (int index = 0; index < particleCount; index++)
            {
                float delay = Hash01(index * 47 + 11) * 0.25f;
                float finish = Mathf.Lerp(
                    0.72f,
                    0.92f,
                    Hash01(index * 59 + 23));
                float progress = LocalProgress(
                    globalProgress, delay, finish);
                if (progress < 0f || progress >= 1f)
                    continue;

                Vector2 start = BilinearSourcePoint(
                    Hash01(index * 71 + 5),
                    Hash01(index * 83 + 17));
                Vector2 toCore = _destination - start;
                Vector2 tangent = Perpendicular(toCore.normalized);
                float bend = Mathf.Lerp(
                    -170f,
                    170f,
                    Hash01(index * 97 + 29));
                Vector2 control = Vector2.Lerp(
                    start, _destination, 0.52f) + tangent * bend +
                    Vector2.up * Mathf.Lerp(
                        18f, 86f, Hash01(index * 109 + 31));
                Vector2 end = _destination + new Vector2(
                    Mathf.Lerp(-9f, 9f, Hash01(index * 127 + 37)),
                    Mathf.Lerp(-7f, 7f, Hash01(index * 139 + 41)));
                Vector2 position = Quadratic(
                    start, control, end, EaseInOut(progress));
                Vector2 velocity = QuadraticDerivative(
                    start, control, end, EaseInOut(progress)).normalized;
                if (velocity.sqrMagnitude < 0.001f)
                    velocity = toCore.normalized;

                float seed = Hash01(index * 151 + 43);
                float length = Mathf.Lerp(15f, 3.2f, progress) *
                    Mathf.Lerp(0.72f, 1.32f, seed);
                float width = Mathf.Lerp(2.4f, 0.65f, progress) *
                    Mathf.Lerp(0.68f, 1.18f,
                        Hash01(index * 163 + 47));
                Color startColor = index % 8 == 0
                    ? JadeHighlight
                    : index % 3 == 0 ? JadeCore : PaperWarm;
                float jadeTransition = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.18f, 0.92f, progress));
                Color tint = Color.Lerp(
                    startColor,
                    index % 5 == 0 ? JadeHighlight : Jade,
                    jadeTransition);
                tint.a = ParticleEnvelope(progress) *
                    Mathf.Lerp(0.54f, 0.92f, seed);
                AddSoftKite(
                    helper,
                    position,
                    velocity * length,
                    Perpendicular(velocity) * width,
                    tint);

                if (index % 9 == 0)
                {
                    Color dust = JadeHighlight;
                    dust.a = tint.a * 0.54f;
                    AddSoftKite(
                        helper,
                        position - velocity * 9f,
                        velocity * length * 0.48f,
                        Perpendicular(velocity) * width * 1.7f,
                        dust);
                }
            }
        }

        private void PopulateJadeConvergence(
            VertexHelper helper,
            float globalProgress)
        {
            for (int index = 0; index < JadeParticleCount; index++)
            {
                float delay = Hash01(index * 37 + 7) * 0.22f;
                float finish = Mathf.Lerp(
                    0.70f,
                    0.91f,
                    Hash01(index * 43 + 13));
                float progress = LocalProgress(
                    globalProgress, delay, finish);
                if (progress < 0f || progress >= 1f)
                    continue;

                float angle = Hash01(index * 53 + 17) * Mathf.PI * 2f;
                float radius = Mathf.Lerp(
                    95f,
                    290f,
                    Hash01(index * 61 + 19));
                Vector2 radial = new Vector2(
                    Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 start = _destination + radial * radius;
                Vector2 tangent = Perpendicular(radial) *
                    Mathf.Lerp(-1f, 1f, Hash01(index * 67 + 23));
                Vector2 control = _destination + radial * radius * 0.48f +
                    tangent * radius * 0.62f;
                Vector2 end = _destination;
                Vector2 position = Quadratic(
                    start, control, end, EaseInOut(progress));
                Vector2 velocity = QuadraticDerivative(
                    start, control, end, EaseInOut(progress)).normalized;
                float length = Mathf.Lerp(12f, 2.8f, progress);
                float width = Mathf.Lerp(2.2f, 0.55f, progress);
                Color tint = index % 6 == 0
                    ? JadeHighlight
                    : index % 2 == 0 ? JadeCore : Jade;
                tint.a = ParticleEnvelope(progress) * 0.82f;
                AddSoftKite(
                    helper,
                    position,
                    velocity * length,
                    Perpendicular(velocity) * width,
                    tint);
            }
        }

        private void PopulateCoreAbsorption(
            VertexHelper helper,
            float globalProgress)
        {
            float convergence = Mathf.Clamp01(
                Mathf.InverseLerp(0.46f, 0.92f, globalProgress));
            if (convergence <= 0f)
                return;

            float ringEnvelope = Mathf.Sin(convergence * Mathf.PI);
            float radius = Mathf.Lerp(
                96f, 8f, EaseInOut(convergence));
            for (int index = 0; index < 28; index++)
            {
                float angle = index / 28f * Mathf.PI * 2f +
                    convergence * 0.75f;
                Vector2 radial = new Vector2(
                    Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 tangent = Perpendicular(radial);
                Color ring = index % 4 == 0
                    ? JadeHighlight : JadeCore;
                ring.a = ringEnvelope * 0.44f;
                AddSoftKite(
                    helper,
                    _destination + radial * radius,
                    tangent * 5.5f,
                    radial * 1.8f,
                    ring);
            }

            float flash = Mathf.Sin(Mathf.Clamp01(
                Mathf.InverseLerp(0.70f, 1f, globalProgress)) * Mathf.PI);
            for (int index = 0; index < 14; index++)
            {
                float angle = index / 14f * Mathf.PI * 2f;
                Vector2 radial = new Vector2(
                    Mathf.Cos(angle), Mathf.Sin(angle));
                Color ray = index % 3 == 0 ? JadeHighlight : Jade;
                ray.a = flash * 0.52f;
                AddSoftKite(
                    helper,
                    _destination + radial * flash * 24f,
                    radial * Mathf.Lerp(6f, 34f, flash),
                    Perpendicular(radial) * 1.4f,
                    ray);
            }

            Color core = JadeHighlight;
            core.a = Mathf.Max(ringEnvelope * 0.36f, flash * 0.78f);
            AddSoftKite(
                helper,
                _destination,
                Vector2.right * Mathf.Lerp(6f, 19f, flash),
                Vector2.up * Mathf.Lerp(6f, 19f, flash),
                core);
        }

        private void CaptureSource(RectTransform source)
        {
            Vector3[] worldCorners = new Vector3[4];
            source.GetWorldCorners(worldCorners);
            for (int index = 0; index < worldCorners.Length; index++)
            {
                _sourceCorners[index] =
                    rectTransform.InverseTransformPoint(worldCorners[index]);
            }
        }

        private Vector2 CaptureCentre(RectTransform target)
        {
            Vector3 world = target.TransformPoint(target.rect.center);
            return rectTransform.InverseTransformPoint(world);
        }

        private Vector2 BilinearSourcePoint(float x, float y)
        {
            Vector2 bottom = Vector2.Lerp(
                _sourceCorners[0], _sourceCorners[3], x);
            Vector2 top = Vector2.Lerp(
                _sourceCorners[1], _sourceCorners[2], x);
            return Vector2.Lerp(bottom, top, y);
        }

        private static float LocalProgress(
            float globalProgress,
            float start,
            float finish)
        {
            if (globalProgress < start)
                return -1f;
            return Mathf.Clamp01(
                (globalProgress - start) /
                Mathf.Max(0.01f, finish - start));
        }

        private static float ParticleEnvelope(float progress)
        {
            float fadeIn = Mathf.Clamp01(progress / 0.09f);
            float fadeOut = Mathf.Clamp01((1f - progress) / 0.16f);
            return fadeIn * fadeOut;
        }

        private static float EaseInOut(float progress)
        {
            progress = Mathf.Clamp01(progress);
            return progress * progress * (3f - 2f * progress);
        }

        private static Vector2 Quadratic(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start +
                   2f * inverse * progress * control +
                   progress * progress * end;
        }

        private static Vector2 QuadraticDerivative(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            return 2f * (1f - progress) * (control - start) +
                   2f * progress * (end - control);
        }

        private static Vector2 Perpendicular(Vector2 direction)
        {
            return new Vector2(-direction.y, direction.x);
        }

        private static void AddSoftKite(
            VertexHelper helper,
            Vector2 centre,
            Vector2 axisAlong,
            Vector2 axisAcross,
            Color tint)
        {
            int start = helper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = centre;
            vertex.color = tint;
            vertex.uv0 = new Vector2(0.5f, 0.5f);
            helper.AddVert(vertex);

            Color edge = tint;
            edge.a = 0f;
            AddVertex(helper, centre - axisAlong, edge, new Vector2(0f, 0.5f));
            AddVertex(helper, centre + axisAcross, edge, new Vector2(0.5f, 1f));
            AddVertex(helper, centre + axisAlong, edge, new Vector2(1f, 0.5f));
            AddVertex(helper, centre - axisAcross, edge, new Vector2(0.5f, 0f));

            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
            helper.AddTriangle(start, start + 3, start + 4);
            helper.AddTriangle(start, start + 4, start + 1);
        }

        private static void AddVertex(
            VertexHelper helper,
            Vector2 position,
            Color color,
            Vector2 uv)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = uv;
            helper.AddVert(vertex);
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
