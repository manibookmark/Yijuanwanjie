using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Draws either one flowing particle divider or one particle button frame
    /// as a single UI mesh. In frame mode it uniquely owns hover/press energy
    /// and label-colour feedback without changing layout bounds.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class HubEnergyParticleGraphic : MaskableGraphic,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        private Texture2D _softParticleTexture;
        private bool _frameMode;
        private bool _interactionEnabled;
        private int _particleCount = 64;
        private float _flowSpeed = 0.10f;
        private float _idleIntensity = 0.55f;
        private float _hoverIntensity = 1f;
        private float _currentIntensity = 0.55f;
        private float _targetIntensity = 0.55f;
        private Color _particleColour = new Color(0.43f, 1f, 0.91f, 1f);
        private Color _normalLabelColour = Color.white;
        private Color _hoverLabelColour = Color.white;
        private Color _pressedLabelColour = Color.white;
        private Color _disabledLabelColour = Color.gray;
        private Button _button;
        private Text _label;

        public override Texture mainTexture
        {
            get
            {
                EnsureSoftParticleTexture();
                return _softParticleTexture != null
                    ? _softParticleTexture
                    : Texture2D.whiteTexture;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            EnsureSoftParticleTexture();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureSoftParticleTexture();
            SetVerticesDirty();
        }

        private void Update()
        {
            float previous = _currentIntensity;
            _currentIntensity = Mathf.MoveTowards(
                _currentIntensity,
                _targetIntensity,
                Time.unscaledDeltaTime * 5.8f);
            if (_currentIntensity > 0.005f ||
                !Mathf.Approximately(previous, _currentIntensity))
            {
                SetVerticesDirty();
            }
        }

        protected override void OnDestroy()
        {
            if (_softParticleTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(_softParticleTexture);
                else
                    DestroyImmediate(_softParticleTexture);
            }
            base.OnDestroy();
        }

        public void ConfigureDivider(
            Color particleColour,
            int particleCount,
            float flowSpeed,
            float intensity)
        {
            _frameMode = false;
            _interactionEnabled = false;
            _particleColour = particleColour;
            _particleCount = Mathf.Clamp(particleCount, 16, 160);
            _flowSpeed = Mathf.Clamp(flowSpeed, 0.01f, 0.4f);
            _idleIntensity = Mathf.Clamp01(intensity);
            _hoverIntensity = _idleIntensity;
            _currentIntensity = _targetIntensity = _idleIntensity;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void ConfigureButtonFrame(
            Color particleColour,
            int particleCount,
            float flowSpeed,
            float idleIntensity,
            float hoverIntensity)
        {
            _frameMode = true;
            _interactionEnabled = true;
            _particleColour = particleColour;
            _particleCount = Mathf.Clamp(particleCount, 24, 200);
            _flowSpeed = Mathf.Clamp(flowSpeed, 0.01f, 0.4f);
            _idleIntensity = Mathf.Clamp01(idleIntensity);
            _hoverIntensity = Mathf.Clamp(hoverIntensity, _idleIntensity, 1.4f);
            _currentIntensity = _targetIntensity = _idleIntensity;
            raycastTarget = true;
            SetVerticesDirty();
        }

        public void BindInteraction(
            Button button,
            Text label,
            Color normalLabelColour,
            Color hoverLabelColour,
            Color pressedLabelColour,
            Color disabledLabelColour)
        {
            _button = button;
            _label = label;
            _normalLabelColour = normalLabelColour;
            _hoverLabelColour = hoverLabelColour;
            _pressedLabelColour = pressedLabelColour;
            _disabledLabelColour = disabledLabelColour;
            if (_label != null)
                _label.color = _normalLabelColour;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            raycastTarget = enabled;
            _targetIntensity = enabled ? _idleIntensity : 0.05f;
            if (_label != null)
                _label.color = enabled
                    ? _normalLabelColour
                    : _disabledLabelColour;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (!CanInteract())
                return;
            _targetIntensity = _hoverIntensity;
            if (_label != null)
                _label.color = _hoverLabelColour;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_interactionEnabled)
                return;
            _targetIntensity = _idleIntensity;
            if (_label != null)
                _label.color = _normalLabelColour;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanInteract())
                return;
            _targetIntensity = Mathf.Min(1.4f, _hoverIntensity + 0.24f);
            if (_label != null)
                _label.color = _pressedLabelColour;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!CanInteract())
                return;
            _targetIntensity = _hoverIntensity;
            if (_label != null)
                _label.color = _hoverLabelColour;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_currentIntensity <= 0.005f || _particleCount <= 0)
                return;

            Rect rect = rectTransform.rect;
            float time = Time.unscaledTime;
            float densityProgress = _frameMode
                ? Mathf.InverseLerp(
                    _idleIntensity,
                    Mathf.Max(_idleIntensity + 0.01f, _hoverIntensity),
                    _currentIntensity)
                : 1f;
            float frameDensity = Mathf.Lerp(
                0.38f,
                1f,
                Mathf.SmoothStep(0f, 1f, densityProgress));
            for (int index = 0; index < _particleCount; index++)
            {
                float seed = Fraction((index + 1) * 0.61803398875f);
                float seed2 = Fraction((index + 1) * 0.754877666f);
                float densitySeed = Fraction((index + 1) * 0.569840296f);
                float densityVisibility = 1f;
                if (_frameMode && frameDensity < 0.999f)
                {
                    densityVisibility = 1f - Mathf.SmoothStep(
                        frameDensity,
                        Mathf.Min(1f, frameDensity + 0.08f),
                        densitySeed);
                    if (densityVisibility <= 0.001f)
                        continue;
                }
                float phase = Mathf.Repeat(
                    (index + 0.5f) / _particleCount +
                    time * _flowSpeed * Mathf.Lerp(0.78f, 1.22f, seed),
                    1f);
                Vector2 position;
                Vector2 tangent;
                if (_frameMode)
                {
                    position = PointOnFrame(rect.size, phase, 6f);
                    Vector2 next = PointOnFrame(rect.size, phase + 0.002f, 6f);
                    tangent = (next - position).normalized;
                }
                else
                {
                    position = new Vector2(
                        Mathf.Lerp(rect.xMin, rect.xMax, phase),
                        Mathf.Sin(time * 1.8f + seed * 27f) * 2.2f);
                    tangent = Vector2.right;
                }

                float pulse = 0.72f + 0.28f * Mathf.Sin(
                    time * (1.4f + seed * 1.6f) + seed2 * 31f);
                Color particleColour = _particleColour;
                particleColour.a = Mathf.Clamp01(
                    Mathf.Lerp(0.28f, 0.82f, seed2) *
                    pulse * _currentIntensity * densityVisibility);
                float size = Mathf.Lerp(2.4f, 6.8f, seed) *
                             Mathf.Lerp(0.82f, 1.12f, _currentIntensity);
                float stretch = _frameMode
                    ? Mathf.Lerp(1.15f, 2.2f, seed2)
                    : Mathf.Lerp(1.4f, 3.2f, seed2);
                AddQuad(
                    vertexHelper,
                    position,
                    new Vector2(size * stretch, size),
                    Mathf.Atan2(tangent.y, tangent.x),
                    particleColour);
            }
        }

        private bool CanInteract()
        {
            return _interactionEnabled &&
                   (_button == null || _button.IsInteractable());
        }

        private void EnsureSoftParticleTexture()
        {
            if (_softParticleTexture != null)
                return;

            const int size = 32;
            _softParticleTexture = new Texture2D(
                size, size, TextureFormat.RGBA32, false)
            {
                name = "HubUiEnergySoftParticle",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float centre = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f - centre) / centre;
                    float ny = (y + 0.5f - centre) / centre;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    _softParticleTexture.SetPixel(
                        x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _softParticleTexture.Apply();
        }

        private static Vector2 PointOnFrame(Vector2 size, float phase, float inset)
        {
            float halfWidth = Mathf.Max(8f, size.x * 0.5f - inset);
            float halfHeight = Mathf.Max(8f, size.y * 0.5f - inset);
            float width = halfWidth * 2f;
            float height = halfHeight * 2f;
            float perimeter = (width + height) * 2f;
            float distance = Mathf.Repeat(phase, 1f) * perimeter;
            if (distance < width)
                return new Vector2(-halfWidth + distance, -halfHeight);
            distance -= width;
            if (distance < height)
                return new Vector2(halfWidth, -halfHeight + distance);
            distance -= height;
            if (distance < width)
                return new Vector2(halfWidth - distance, halfHeight);
            distance -= width;
            return new Vector2(-halfWidth, halfHeight - distance);
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            Vector2 centre,
            Vector2 size,
            float rotation,
            Color colour)
        {
            float cosine = Mathf.Cos(rotation);
            float sine = Mathf.Sin(rotation);
            Vector2 right = new Vector2(cosine, sine) * (size.x * 0.5f);
            Vector2 up = new Vector2(-sine, cosine) * (size.y * 0.5f);
            int firstVertex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, centre - right - up, new Vector2(0f, 0f), colour);
            AddVertex(vertexHelper, centre - right + up, new Vector2(0f, 1f), colour);
            AddVertex(vertexHelper, centre + right + up, new Vector2(1f, 1f), colour);
            AddVertex(vertexHelper, centre + right - up, new Vector2(1f, 0f), colour);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex + 2, firstVertex + 3, firstVertex);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Vector2 uv,
            Color colour)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.uv0 = uv;
            vertex.color = colour;
            vertexHelper.AddVert(vertex);
        }

        private static float Fraction(float value)
        {
            return value - Mathf.Floor(value);
        }
    }
}
