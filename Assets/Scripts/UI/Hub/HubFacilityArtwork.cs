using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Owns one facility's static body, silhouette hit test and hover glow.
    /// All layers share the background's coordinate space. This view neither
    /// mutates progression nor implies that a facility's interior is available.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public sealed class HubFacilityArtwork : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        ICanvasRaycastFilter
    {
        /// <summary>Raised after a valid click on this facility's silhouette.</summary>
        public event System.Action Clicked;

        private Image _raycastImage;
        private RectTransform _visualRoot;
        private Image _body;
        private Image _emission;
        private Texture2D _hitMask;
        private Color _glowColor;
        private Sprite _restBody;
        private Sprite[] _hoverFrames;
        private float _hoverDuration;
        private bool _loopHover;
        private HubFacilityArtProfile.HoverPlaybackMode _hoverPlaybackMode;
        private float _hoverExitDuration;
        private float _hoverPlaybackTime;
        private float _splitProgress;
        private bool _playingHoverExit;
        private bool _hoverEnabled = true;
        private bool _hovered;
        private float _hoverAmount;
        private HubFacilityArtProfile.Facility _art;
        private Vector2 _canvasSize;

        /// <summary>Initializes once under the actual background rectangle.</summary>
        public void Initialize(HubFacilityArtProfile.Facility art, Vector2 canvasSize)
        {
            if (_body != null)
                return;

            _art = art;
            _canvasSize = canvasSize;

            _raycastImage = GetComponent<Image>();
            _raycastImage.sprite = art.Body;
            _raycastImage.color = new Color(1f, 1f, 1f, 0f);
            _raycastImage.raycastTarget = true;

            var visualObject = new GameObject("VisualRoot",
                typeof(RectTransform));
            visualObject.transform.SetParent(transform, false);
            _visualRoot = (RectTransform)visualObject.transform;
            Stretch(_visualRoot);

            var bodyObject = new GameObject("Body",
                typeof(RectTransform), typeof(Image));
            bodyObject.transform.SetParent(_visualRoot, false);
            Stretch((RectTransform)bodyObject.transform);
            _body = bodyObject.GetComponent<Image>();
            _body.sprite = art.Body;
            _body.raycastTarget = false;
            _restBody = art.Body;
            _hoverFrames = art.LoadHoverFrames();
            _hoverDuration = art.HoverDuration;
            _loopHover = art.LoopHover;
            _hoverPlaybackMode = art.PlaybackMode;
            _hoverExitDuration = art.HoverExitDuration;
            _hitMask = art.HitMask;
            _glowColor = art.GlowColor;

            RefreshPlacement();

            var glowObject = new GameObject("HoverEmission",
                typeof(RectTransform), typeof(Image));
            glowObject.transform.SetParent(_visualRoot, false);
            Stretch((RectTransform)glowObject.transform);
            _emission = glowObject.GetComponent<Image>();
            _emission.sprite = art.Emission;
            _emission.raycastTarget = false;

            ApplyHover();
        }

        /// <summary>
        /// 从阶段建筑配置资产实时重读位置与显示缩放。播放中直接在
        /// Inspector 修改该资产即可实时生效并持久保存
        /// （与万界阵/天箓同一调参模式）。
        /// </summary>
        public void RefreshPlacement()
        {
            if (_art == null)
                return;
            ApplyPlacement(_art.ArtRect, _art.VisualScale, _canvasSize);
        }

        /// <summary>
        /// 按艺术坐标写入显示锚点：以建筑中心应用显示缩放，可重复调用；
        /// 辉光子层自动跟随。
        /// </summary>
        private void ApplyPlacement(
            Rect artRect,
            float visualScale,
            Vector2 canvasSize)
        {
            visualScale = Mathf.Max(0.05f, visualScale);
            Vector2 size = artRect.size * visualScale;
            Rect scaled = new Rect(artRect.center - size * 0.5f, size);
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(scaled.xMin / canvasSize.x,
                1f - scaled.yMax / canvasSize.y);
            rect.anchorMax = new Vector2(scaled.xMax / canvasSize.x,
                1f - scaled.yMin / canvasSize.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public void SetHoverEnabled(bool enabled)
        {
            _hoverEnabled = enabled;
            if (_raycastImage != null)
                _raycastImage.raycastTarget = enabled;
            if (!enabled)
                ResetHover();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (!_hoverEnabled)
                return;
            if (!_hovered)
            {
                _hoverPlaybackTime = 0f;
                _splitProgress = 0f;
                _playingHoverExit = false;
            }
            _hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            if (_hoverPlaybackMode ==
                    HubFacilityArtProfile.HoverPlaybackMode.SplitEnterExit &&
                _hoverFrames != null && _hoverFrames.Length >= 2)
            {
                _splitProgress = Mathf.Max(0.5f, _splitProgress);
                _playingHoverExit = true;
                ApplyHoverFrame(_splitProgress);
                return;
            }

            // 标准序列与天箓保持同一交互语义：移出后从当前帧平滑
            // 回放到首帧，而不是瞬间跳回或继续循环造成视觉抖动。
            if (_hoverFrames != null && _hoverFrames.Length >= 2)
            {
                _playingHoverExit = true;
                return;
            }
            ResetHoverAnimation();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_hoverEnabled)
                Clicked?.Invoke();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!_hoverEnabled || _hitMask == null)
                return false;
            RectTransform rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, screenPoint, eventCamera, out Vector2 local))
                return false;

            Rect bounds = rect.rect;
            if (bounds.width <= 0f || bounds.height <= 0f || !bounds.Contains(local))
                return false;
            float u = (local.x - bounds.xMin) / bounds.width;
            float v = (local.y - bounds.yMin) / bounds.height;
            return _hitMask.GetPixelBilinear(u, v).r >= 0.15f;
        }

        private void Update()
        {
            float target = _hovered ? 1f : 0f;
            if (!Mathf.Approximately(_hoverAmount, target))
            {
                _hoverAmount = Mathf.MoveTowards(_hoverAmount, target,
                    Time.unscaledDeltaTime / (_hovered ? 0.28f : 0.2f));
                ApplyHover();
            }
            UpdateHoverAnimation();
        }

        private void OnDisable()
        {
            ResetHover();
        }

        private void ResetHover()
        {
            _hovered = false;
            _hoverAmount = 0f;
            ResetHoverAnimation();
            ApplyHover();
        }

        private void ResetHoverAnimation()
        {
            _hoverPlaybackTime = 0f;
            _splitProgress = 0f;
            _playingHoverExit = false;
            if (_body != null)
                _body.sprite = _restBody;
            if (_visualRoot != null)
                _visualRoot.localScale = Vector3.one;
        }

        private void UpdateHoverAnimation()
        {
            if (_body == null || _hoverFrames == null ||
                _hoverFrames.Length < 2)
                return;

            if (_hoverPlaybackMode ==
                HubFacilityArtProfile.HoverPlaybackMode.SplitEnterExit)
            {
                UpdateSplitHoverAnimation();
                return;
            }

            if (_hovered)
            {
                _playingHoverExit = false;
                _hoverPlaybackTime += Time.unscaledDeltaTime;
            }
            else if (_playingHoverExit)
            {
                _hoverPlaybackTime -= Time.unscaledDeltaTime *
                    _hoverDuration / Mathf.Max(0.1f, _hoverExitDuration);
                if (_hoverPlaybackTime <= 0f)
                {
                    ResetHoverAnimation();
                    return;
                }
            }
            else
            {
                return;
            }

            float progress = _loopHover && _hovered
                ? Mathf.Repeat(_hoverPlaybackTime / _hoverDuration, 1f)
                : Mathf.Clamp01(_hoverPlaybackTime / _hoverDuration);
            ApplyHoverFrame(progress);
        }

        private void UpdateSplitHoverAnimation()
        {
            if (_hovered && _splitProgress < 0.5f)
            {
                _splitProgress = Mathf.Min(0.5f,
                    _splitProgress + Time.unscaledDeltaTime *
                    0.5f / _hoverDuration);
                ApplyHoverFrame(_splitProgress);
                return;
            }

            if (!_playingHoverExit)
                return;

            _splitProgress = Mathf.Min(1f,
                _splitProgress + Time.unscaledDeltaTime *
                0.5f / _hoverExitDuration);
            if (_splitProgress >= 1f)
            {
                ResetHoverAnimation();
                return;
            }
            ApplyHoverFrame(_splitProgress);
        }

        private void ApplyHoverFrame(float progress)
        {
            int index = Mathf.RoundToInt(
                progress * (_hoverFrames.Length - 1));
            _body.sprite = _hoverFrames[index];

            // Hover 只切换等尺寸序列帧，根节点始终保持 1:1；
            // 不再做任何缩放补偿，命中范围与建筑显示大小都不会变化。
            if (_visualRoot != null)
                _visualRoot.localScale = Vector3.one;
        }

        private void ApplyHover()
        {
            if (_emission != null)
            {
                Color color = _glowColor;
                color.a *= _hoverFrames != null && _hoverFrames.Length >= 2
                    ? 0f
                    : Mathf.SmoothStep(0f, 1f, _hoverAmount);
                _emission.color = color;
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
