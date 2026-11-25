using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// 手牌 Wild 卡的常驻沿边灵光。组件唯一拥有圆角网格、双相配色与低频
    /// 呼吸节拍；不创建材质、不参与射线，并由 CardView 在牌池换牌时开关。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WildCardAmbientGlowGraphic : MaskableGraphic
    {
        private const string ObjectName = "WildHandAmbientGlow";
        private const int PerimeterSegments = 48;
        private const float TwoPi = Mathf.PI * 2f;
        private const float PulseInterval = 1f / 12f;
        private const float PulsePeriod = 6.8f;

        // 軟翠 (ruan cui) #006D87：阴侧的沉静支撑色。
        private static readonly Color SoftJade =
            new Color32(0x00, 0x6D, 0x87, 0xFF);

        // 庫金 (ku jin) #E18A3B：阳侧的克制高光色。
        private static readonly Color TreasuryGold =
            new Color32(0xE1, 0x8A, 0x3B, 0xFF);

        private float _cornerRadius = 12f;
        private float _phaseOffset;
        private float _nextPulseTime;

        /// <summary>
        /// 在视觉根下取得或创建唯一实例。插在费用与文字层之前，灵光只沿边
        /// 可见，且不会压住卡名、费用、描述或太极费用标识。
        /// </summary>
        public static WildCardAmbientGlowGraphic Ensure(
            RectTransform visualRoot,
            Transform insertBefore)
        {
            if (visualRoot == null) return null;

            Transform existing = visualRoot.Find(ObjectName);
            WildCardAmbientGlowGraphic graphic = existing != null
                ? existing.GetComponent<WildCardAmbientGlowGraphic>()
                : null;
            if (graphic == null)
            {
                GameObject glowObject = existing != null
                    ? existing.gameObject
                    : new GameObject(
                        ObjectName,
                        typeof(RectTransform),
                        typeof(CanvasRenderer));
                glowObject.SetActive(false);
                glowObject.transform.SetParent(visualRoot, false);

                RectTransform rect = glowObject.transform as RectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                graphic = glowObject.AddComponent<WildCardAmbientGlowGraphic>();
            }

            if (insertBefore != null &&
                insertBefore.parent == visualRoot)
            {
                int targetIndex = insertBefore.GetSiblingIndex();
                if (graphic.transform.GetSiblingIndex() < targetIndex)
                    targetIndex--;
                graphic.transform.SetSiblingIndex(targetIndex);
            }

            graphic.raycastTarget = false;
            return graphic;
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            _phaseOffset = Mathf.Repeat(
                unchecked((uint)GetInstanceID()) * 0.61803398875f,
                1f);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            _nextPulseTime = 0f;
            ApplyPulse(Time.unscaledTime);
            SetVerticesDirty();
        }

        public void Configure(float cornerRadius)
        {
            float clamped = Mathf.Max(0f, cornerRadius);
            if (Mathf.Approximately(_cornerRadius, clamped)) return;

            _cornerRadius = clamped;
            if (isActiveAndEnabled)
                SetVerticesDirty();
        }

        public void SetAmbientVisible(bool visible)
        {
            if (gameObject.activeSelf == visible) return;
            gameObject.SetActive(visible);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextPulseTime) return;

            _nextPulseTime = now + PulseInterval;
            ApplyPulse(now);
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            AddRoundedRing(
                vertexHelper,
                Expand(rect, 4f),
                Inset(rect, 2.2f),
                _cornerRadius + 4f,
                Mathf.Max(0f, _cornerRadius - 2.2f),
                0f,
                0.14f);
            AddRoundedRing(
                vertexHelper,
                Expand(rect, 0.8f),
                Inset(rect, 1.4f),
                _cornerRadius + 0.8f,
                Mathf.Max(0f, _cornerRadius - 1.4f),
                0.11f,
                0.32f);
        }

        private void ApplyPulse(float time)
        {
            float phase = time * (TwoPi / PulsePeriod) +
                _phaseOffset * TwoPi;
            float pulse = 0.78f + 0.22f *
                (Mathf.Sin(phase) * 0.5f + 0.5f);
            canvasRenderer.SetAlpha(pulse);
        }

        private void AddRoundedRing(
            VertexHelper vertexHelper,
            Rect outerRect,
            Rect innerRect,
            float outerRadius,
            float innerRadius,
            float outerAlpha,
            float innerAlpha)
        {
            for (int index = 0; index < PerimeterSegments; index++)
            {
                float from = index / (float)PerimeterSegments;
                float to = (index + 1) / (float)PerimeterSegments;
                Vector2 outerFrom = PointOnRoundedRect(
                    outerRect, outerRadius, from);
                Vector2 outerTo = PointOnRoundedRect(
                    outerRect, outerRadius, to);
                Vector2 innerFrom = PointOnRoundedRect(
                    innerRect, innerRadius, from);
                Vector2 innerTo = PointOnRoundedRect(
                    innerRect, innerRadius, to);

                Color outerFromColor = EdgeColor(from, outerAlpha);
                Color outerToColor = EdgeColor(to, outerAlpha);
                Color innerFromColor = EdgeColor(from, innerAlpha);
                Color innerToColor = EdgeColor(to, innerAlpha);
                AddQuad(
                    vertexHelper,
                    outerFrom,
                    innerFrom,
                    innerTo,
                    outerTo,
                    outerFromColor,
                    innerFromColor,
                    innerToColor,
                    outerToColor);
            }
        }

        private Color EdgeColor(float progress, float alpha)
        {
            float wave = Mathf.Sin(
                (progress + _phaseOffset) * TwoPi) * 0.5f + 0.5f;
            float blend = Mathf.SmoothStep(0f, 1f, wave);
            Color result = Color.Lerp(SoftJade, TreasuryGold, blend);
            float lobe = 0.82f + 0.18f * Mathf.Cos(
                (progress * 2f + _phaseOffset) * TwoPi);
            result.a = Mathf.Clamp01(alpha * lobe);
            return result;
        }

        private static Vector2 PointOnRoundedRect(
            Rect rect,
            float radius,
            float progress)
        {
            float resolvedRadius = Mathf.Clamp(
                radius,
                0.01f,
                Mathf.Max(0.01f, Mathf.Min(rect.width, rect.height) * 0.5f));
            float horizontal = Mathf.Max(0f, rect.width - resolvedRadius * 2f);
            float vertical = Mathf.Max(0f, rect.height - resolvedRadius * 2f);
            float arc = resolvedRadius * Mathf.PI * 0.5f;
            float perimeter = (horizontal + vertical) * 2f + arc * 4f;
            float distance = Mathf.Repeat(progress, 1f) * perimeter;

            if (distance < horizontal)
                return new Vector2(
                    rect.xMin + resolvedRadius + distance,
                    rect.yMin);
            distance -= horizontal;
            if (distance < arc)
                return CornerPoint(
                    new Vector2(
                        rect.xMax - resolvedRadius,
                        rect.yMin + resolvedRadius),
                    resolvedRadius,
                    -Mathf.PI * 0.5f + distance / resolvedRadius);
            distance -= arc;
            if (distance < vertical)
                return new Vector2(
                    rect.xMax,
                    rect.yMin + resolvedRadius + distance);
            distance -= vertical;
            if (distance < arc)
                return CornerPoint(
                    new Vector2(
                        rect.xMax - resolvedRadius,
                        rect.yMax - resolvedRadius),
                    resolvedRadius,
                    distance / resolvedRadius);
            distance -= arc;
            if (distance < horizontal)
                return new Vector2(
                    rect.xMax - resolvedRadius - distance,
                    rect.yMax);
            distance -= horizontal;
            if (distance < arc)
                return CornerPoint(
                    new Vector2(
                        rect.xMin + resolvedRadius,
                        rect.yMax - resolvedRadius),
                    resolvedRadius,
                    Mathf.PI * 0.5f + distance / resolvedRadius);
            distance -= arc;
            if (distance < vertical)
                return new Vector2(
                    rect.xMin,
                    rect.yMax - resolvedRadius - distance);
            distance -= vertical;
            return CornerPoint(
                new Vector2(
                    rect.xMin + resolvedRadius,
                    rect.yMin + resolvedRadius),
                resolvedRadius,
                Mathf.PI + distance / resolvedRadius);
        }

        private static Vector2 CornerPoint(
            Vector2 center,
            float radius,
            float radians)
        {
            return center + new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians)) * radius;
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static Rect Inset(Rect rect, float amount)
        {
            float x = Mathf.Min(amount, rect.width * 0.5f - 0.01f);
            float y = Mathf.Min(amount, rect.height * 0.5f - 0.01f);
            return new Rect(
                rect.xMin + x,
                rect.yMin + y,
                Mathf.Max(0.02f, rect.width - x * 2f),
                Mathf.Max(0.02f, rect.height - y * 2f));
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            Vector2 outerFrom,
            Vector2 innerFrom,
            Vector2 innerTo,
            Vector2 outerTo,
            Color outerFromColor,
            Color innerFromColor,
            Color innerToColor,
            Color outerToColor)
        {
            int first = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, outerFrom, outerFromColor);
            AddVertex(vertexHelper, innerFrom, innerFromColor);
            AddVertex(vertexHelper, innerTo, innerToColor);
            AddVertex(vertexHelper, outerTo, outerToColor);
            vertexHelper.AddTriangle(first, first + 1, first + 2);
            vertexHelper.AddTriangle(first, first + 2, first + 3);
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
