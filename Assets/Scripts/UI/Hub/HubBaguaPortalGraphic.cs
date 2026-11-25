using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using SpiritEnergyColorPalette =
    WanjieScroll.Core.SpiritEnergyColorPalette;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Owns the complete visual state of the Hub's procedural Bagua portal.
    /// Callers choose only a semantic state; exact trigram geometry, animation
    /// timing, material parameters and transition easing remain hidden here.
    /// Perspective is supplied by the RectTransform as one affine plane, so the
    /// Taiji, rings and all eight trigrams can never drift apart independently.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class HubBaguaPortalGraphic : MaskableGraphic,
        IPointerEnterHandler,
        IPointerMoveHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler
    {
        public enum PortalState
        {
            Dormant,
            Idle,
            Hover,
            Opening,
            Awakening
        }

        private const string ShaderName = "WanjieScroll/UI/HubBaguaPortal";
        private const string ShaderResourcePath = "Shaders/HubBaguaPortal";
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int OpeningId = Shader.PropertyToID("_Opening");
        private static readonly int JadeColorId = Shader.PropertyToID("_JadeColor");
        private static readonly int EnergyColorId = Shader.PropertyToID("_EnergyColor");
        private static readonly int HoverAngleId = Shader.PropertyToID("_HoverAngle");
        private static readonly int IdleFlowId = Shader.PropertyToID("_IdleFlow");
        private static readonly int IdleFlowSpeedId = Shader.PropertyToID("_IdleFlowSpeed");
        private static readonly int SpinAngleId = Shader.PropertyToID("_SpinAngle");
        private static readonly int SpinSinkId = Shader.PropertyToID("_SpinSink");

        [Header("State")]
        [SerializeField] private PortalState _state = PortalState.Idle;
        [SerializeField, Min(0.05f)] private float _transitionDuration = 0.28f;
        [SerializeField] private bool _previewPointerStates = true;
        [SerializeField] private UnityEvent _openingRequested = new UnityEvent();

        [Header("Idle Energy Breath")]
        [SerializeField, Range(0f, 0.45f)] private float _idleBaseFlowStrength = 0.18f;
        [Tooltip("两次静默能量流之间的总周期；脉冲结束后会重新归于近黑。")]
        [SerializeField, Min(2f)] private float _idleFlowInterval = 6.8f;
        [Tooltip("每次能量沿阵纹流动的持续时间。")]
        [SerializeField, Range(0.35f, 2.5f)] private float _idleFlowDuration = 1.18f;
        [Tooltip("Idle 能量流的最高亮度，不影响 Hover 和 Opening。")]
        [SerializeField, Range(0f, 1f)] private float _idleFlowStrength = 0.74f;
        [SerializeField, Range(0.1f, 2f)] private float _idleFlowSpeed = 0.66f;

        [Header("Hover Rotation")]
        [SerializeField, Range(1f, 24f)] private float _hoverSpinSpeedDegrees = 7.5f;
        [SerializeField, Range(4f, 90f)] private float _hoverReturnSpeedDegrees = 30f;

        [Header("Opening Spin")]
        [SerializeField, Range(0.2f, 1.2f)] private float _openingSpinDuration = 0.48f;
        [SerializeField, Range(0.25f, 2f)] private float _openingSpinTurns = 1f;
        [SerializeField, Range(0f, 0.18f)] private float _openingSinkStrength = 0.085f;

        [Header("Scene Palette")]
        [SerializeField] private Color _jadeColor = new Color(0.015f, 0.022f, 0.024f, 0.98f);
        [SerializeField] private Color _energyColor =
            SpiritEnergyColorPalette.Energy;

        private Material _runtimeMaterial;
        private float _currentIntensity;
        private float _currentOpening;
        private float _targetIntensity;
        private float _targetOpening;
        private bool _pointerInside;
        private bool _pointerDown;
        private bool _pressStartedInside;
        private float _hoverAngle;
        private float _hoverSpinAngleDegrees;
        private float _currentIdleFlow;
        private float _openingSpinProgress;

        public PortalState State => _state;

        /// <summary>
        /// Raised once after a valid press/release on the formation. The Hub
        /// bootstrap may bind one scene-level transition effect without knowing
        /// any shader or pointer-state details.
        /// </summary>
        public UnityEvent OpeningRequested => _openingRequested;

        public override Texture mainTexture => Texture2D.whiteTexture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = true;
            EnsureMaterial();
            ApplyStateImmediately(_state);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
            ResolveTargets();
            SetMaterialDirty();
        }

        protected override void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }
            }

            base.OnDestroy();
        }

        private void Update()
        {
            ResolveTargets();
            _currentIdleFlow = ResolveIdleFlow(Time.unscaledTime);
            float deltaTime = Time.unscaledDeltaTime;
            bool hovering = _state == PortalState.Hover ||
                            (_previewPointerStates &&
                             _state == PortalState.Idle &&
                             _pointerInside);
            if (_state != PortalState.Opening)
            {
                if (hovering)
                {
                    _hoverSpinAngleDegrees = NormalizeSignedAngle(
                        _hoverSpinAngleDegrees + _hoverSpinSpeedDegrees * deltaTime);
                }
                else
                {
                    _hoverSpinAngleDegrees = Mathf.MoveTowardsAngle(
                        _hoverSpinAngleDegrees,
                        0f,
                        _hoverReturnSpeedDegrees * deltaTime);
                }
            }
            _openingSpinProgress = _state == PortalState.Opening
                ? Mathf.MoveTowards(
                    _openingSpinProgress,
                    1f,
                    deltaTime / Mathf.Max(0.2f, _openingSpinDuration))
                : 0f;
            float speed = 1f / Mathf.Max(0.05f, _transitionDuration);
            _currentIntensity = Mathf.MoveTowards(
                _currentIntensity,
                _targetIntensity,
                speed * deltaTime);
            _currentOpening = Mathf.MoveTowards(
                _currentOpening,
                _targetOpening,
                speed * 0.72f * deltaTime);
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            Rect rectangle = GetPixelAdjustedRect();
            vertexHelper.Clear();

            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = new Vector3(rectangle.xMin, rectangle.yMin);
            vertex.uv0 = new Vector2(0f, 0f);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rectangle.xMin, rectangle.yMax);
            vertex.uv0 = new Vector2(0f, 1f);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rectangle.xMax, rectangle.yMax);
            vertex.uv0 = new Vector2(1f, 1f);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rectangle.xMax, rectangle.yMin);
            vertex.uv0 = new Vector2(1f, 0f);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(0, 1, 2);
            vertexHelper.AddTriangle(2, 3, 0);
        }

        public override Material materialForRendering
        {
            get
            {
                EnsureMaterial();
                UpdateMaterialParameters();
                return base.materialForRendering;
            }
        }

        /// <summary>
        /// Changes the portal's semantic state. The visual transition and all
        /// shader details are intentionally internal to this component.
        /// </summary>
        public void SetState(PortalState state, bool immediate = false)
        {
            if (state == PortalState.Opening && _state != PortalState.Opening)
                _openingSpinProgress = 0f;
            else if (state != PortalState.Opening)
                _openingSpinProgress = 0f;

            _state = state;
            _pointerDown = false;
            if (immediate)
            {
                ApplyStateImmediately(state);
            }
            else
            {
                ResolveTargets();
            }
        }

        public void ConfigureMotion(HubPortalMotionSettings settings)
        {
            _idleBaseFlowStrength = Mathf.Clamp(settings.IdleBaseFlowStrength, 0f, 0.45f);
            _idleFlowDuration = Mathf.Clamp(settings.IdleFlowDuration, 0.35f, 2.5f);
            _idleFlowInterval = Mathf.Max(
                _idleFlowDuration + 0.6f,
                settings.IdleFlowInterval);
            _idleFlowStrength = Mathf.Clamp01(settings.IdleFlowStrength);
            _idleFlowSpeed = Mathf.Clamp(settings.IdleFlowSpeed, 0.1f, 2f);
            _hoverSpinSpeedDegrees = Mathf.Clamp(settings.HoverSpinSpeedDegrees, 1f, 24f);
            _hoverReturnSpeedDegrees = Mathf.Clamp(settings.HoverReturnSpeedDegrees, 4f, 90f);
            _openingSpinDuration = Mathf.Clamp(settings.OpeningSpinDuration, 0.2f, 1.2f);
            _openingSpinTurns = Mathf.Clamp(settings.OpeningSpinTurns, 0.25f, 2f);
            _openingSinkStrength = Mathf.Clamp(settings.OpeningSinkStrength, 0f, 0.18f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _pointerInside = true;
            UpdateHoverAngle(eventData);
            if (_previewPointerStates && _state != PortalState.Dormant && _state != PortalState.Opening)
            {
                ResolveTargets();
            }
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            UpdateHoverAngle(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            _pointerDown = false;
            _pressStartedInside = false;
            if (_previewPointerStates)
            {
                ResolveTargets();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_state == PortalState.Dormant ||
                _state == PortalState.Awakening)
            {
                return;
            }

            _pointerDown = true;
            _pressStartedInside = true;
            ResolveTargets();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pointerDown = false;
            ResolveTargets();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_pressStartedInside || _state == PortalState.Dormant ||
                _state == PortalState.Awakening ||
                _state == PortalState.Opening)
            {
                _pressStartedInside = false;
                return;
            }

            _pressStartedInside = false;
            SetState(PortalState.Opening);
            _openingRequested.Invoke();
        }

        public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
        {
            if (!base.Raycast(screenPoint, eventCamera)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    screenPoint,
                    eventCamera,
                    out Vector2 localPoint))
            {
                return false;
            }

            Rect rectangle = rectTransform.rect;
            if (rectangle.width <= 0.01f || rectangle.height <= 0.01f)
            {
                return false;
            }

            Vector2 normalized = new Vector2(
                (localPoint.x - rectangle.center.x) / (rectangle.width * 0.5f),
                (localPoint.y - rectangle.center.y) / (rectangle.height * 0.5f));
            return normalized.sqrMagnitude <= 1f;
        }

        private void EnsureMaterial()
        {
            if (_runtimeMaterial != null)
            {
                return;
            }

            // The Resources reference keeps this runtime-created material's
            // shader from being stripped from player builds.
            Shader shader = Resources.Load<Shader>(ShaderResourcePath) ?? Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[{nameof(HubBaguaPortalGraphic)}] Missing shader '{ShaderName}'.", this);
                return;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = "Hub Bagua Portal (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            material = _runtimeMaterial;
        }

        private void ResolveTargets()
        {
            switch (_state)
            {
                case PortalState.Dormant:
                    _targetIntensity = 0f;
                    _targetOpening = 0f;
                    break;
                case PortalState.Awakening:
                    _targetIntensity = 1.18f;
                    _targetOpening = 0.16f;
                    break;
                case PortalState.Hover:
                    _targetIntensity = 1.22f;
                    _targetOpening = 0f;
                    break;
                case PortalState.Opening:
                    _targetIntensity = 1.50f;
                    _targetOpening = 1f;
                    break;
                default:
                    // Idle is a carved near-black formation, not a glowing
                    // portal. Energy only becomes readable on pointer hover.
                    _targetIntensity = 0f;
                    _targetOpening = 0f;
                    break;
            }

            if (_previewPointerStates && _state == PortalState.Idle && _pointerInside)
            {
                _targetIntensity = _pointerDown ? 1.42f : 1.22f;
                _targetOpening = 0f;
            }
        }

        private float ResolveIdleFlow(float time)
        {
            if (_state != PortalState.Idle || _pointerInside || _pointerDown ||
                (_idleBaseFlowStrength <= 0.001f && _idleFlowStrength <= 0.001f))
            {
                return 0f;
            }

            float interval = Mathf.Max(
                _idleFlowDuration + 0.6f,
                _idleFlowInterval);
            float duration = Mathf.Min(_idleFlowDuration, interval - 0.3f);
            float cycle = Mathf.Repeat(time, interval);
            float pulseStart = interval - duration;
            if (cycle < pulseStart)
            {
                return _idleBaseFlowStrength;
            }

            float progress = Mathf.InverseLerp(pulseStart, interval, cycle);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            return Mathf.Clamp01(
                _idleBaseFlowStrength + envelope * envelope * _idleFlowStrength);
        }

        private void ApplyStateImmediately(PortalState state)
        {
            _state = state;
            if (state == PortalState.Dormant || state == PortalState.Idle)
                _hoverSpinAngleDegrees = 0f;
            ResolveTargets();
            _currentIntensity = _targetIntensity;
            _currentOpening = _targetOpening;
            SetMaterialDirty();
        }

        private void UpdateMaterialParameters()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            _runtimeMaterial.SetFloat(IntensityId, _currentIntensity);
            _runtimeMaterial.SetFloat(OpeningId, _currentOpening);
            _runtimeMaterial.SetColor(JadeColorId, _jadeColor);
            _runtimeMaterial.SetColor(EnergyColorId, _energyColor);
            _runtimeMaterial.SetFloat(HoverAngleId, _hoverAngle);
            _runtimeMaterial.SetFloat(IdleFlowId, _currentIdleFlow);
            _runtimeMaterial.SetFloat(IdleFlowSpeedId, _idleFlowSpeed);
            float spinProgress = Mathf.SmoothStep(0f, 1f, _openingSpinProgress);
            _runtimeMaterial.SetFloat(
                SpinAngleId,
                _hoverSpinAngleDegrees * Mathf.Deg2Rad +
                spinProgress * _openingSpinTurns * Mathf.PI * 2f);
            _runtimeMaterial.SetFloat(
                SpinSinkId,
                Mathf.Sin(_openingSpinProgress * Mathf.PI) * _openingSinkStrength);
        }

        private void UpdateHoverAngle(PointerEventData eventData)
        {
            if (eventData == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rectangle = rectTransform.rect;
            Vector2 normalized = new Vector2(
                (localPoint.x - rectangle.center.x) /
                Mathf.Max(1f, rectangle.width * 0.5f),
                (localPoint.y - rectangle.center.y) /
                Mathf.Max(1f, rectangle.height * 0.5f));
            _hoverAngle = Mathf.Atan2(normalized.y, normalized.x);
            SetMaterialDirty();
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _transitionDuration = Mathf.Max(0.05f, _transitionDuration);
            _idleBaseFlowStrength = Mathf.Clamp(_idleBaseFlowStrength, 0f, 0.45f);
            _idleFlowDuration = Mathf.Clamp(_idleFlowDuration, 0.35f, 2.5f);
            _idleFlowInterval = Mathf.Max(
                _idleFlowDuration + 0.6f,
                _idleFlowInterval);
            _idleFlowStrength = Mathf.Clamp01(_idleFlowStrength);
            _idleFlowSpeed = Mathf.Clamp(_idleFlowSpeed, 0.1f, 2f);
            _hoverSpinSpeedDegrees = Mathf.Clamp(_hoverSpinSpeedDegrees, 1f, 24f);
            _hoverReturnSpeedDegrees = Mathf.Clamp(_hoverReturnSpeedDegrees, 4f, 90f);
            _openingSpinDuration = Mathf.Clamp(_openingSpinDuration, 0.2f, 1.2f);
            _openingSpinTurns = Mathf.Clamp(_openingSpinTurns, 0.25f, 2f);
            _openingSinkStrength = Mathf.Clamp(_openingSinkStrength, 0f, 0.18f);
            EnsureMaterial();
            ApplyStateImmediately(_state);
            SetVerticesDirty();
        }
#endif
    }
}
