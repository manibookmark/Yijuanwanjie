using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Displays one authored video facility with its mask and exposes the
    /// same click boundary used by the existing Hub facility navigation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(RawImage), typeof(VideoPlayer))]
    public sealed class HubFacilityVideoArtwork : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        ICanvasRaycastFilter
    {
        private static HubFacilityVideoArtwork _currentHover;

        public event System.Action Clicked;
        public event System.Action<HubFacilityVideoArtwork> Ready;

        private RawImage _image;
        private VideoPlayer _player;
        private Texture2D _hitMask;
        private bool _interactionEnabled;
        private bool _interactionRequested;
        private bool _visible;
        private bool _ready;
        private bool _hovered;
        private bool _leaving;
        private bool _holdAtEnd;
        private RenderTexture _renderTexture;
        private Material _runtimeMaterial;

        public string RegistryEntryId { get; private set; }

        public void Initialize(
            HubFacilityVideoOverlayProfile.Facility facility,
            Vector2 canvasSize,
            Transform parent)
        {
            if (facility == null || facility.Video == null || facility.Material == null ||
                facility.HitMask == null || canvasSize.x <= 0f || canvasSize.y <= 0f)
            {
                return;
            }

            RegistryEntryId = facility.RegistryEntryId;
            _holdAtEnd = RegistryEntryId == "tianlu.entry.facility.tianlu.v1";
            _image = GetComponent<RawImage>();
            _player = GetComponent<VideoPlayer>();
            _hitMask = facility.HitMask;
            transform.SetParent(parent, false);

            _runtimeMaterial = new Material(facility.Material)
            {
                name = facility.Material.name + "_Runtime"
            };
            _image.material = _runtimeMaterial;
            _image.raycastTarget = false;
            _image.color = Color.clear;
            _hovered = false;

            Rect artRect = facility.ArtRect;
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(artRect.xMin / canvasSize.x,
                1f - artRect.yMax / canvasSize.y);
            rect.anchorMax = new Vector2(artRect.xMax / canvasSize.x,
                1f - artRect.yMin / canvasSize.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            int width = Mathf.Max(2, (int)facility.Video.width);
            int height = Mathf.Max(2, (int)facility.Video.height);
            _renderTexture = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32)
            {
                name = facility.RegistryEntryId + "_RuntimeVideo",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();

            _image.texture = _renderTexture;
            _player.clip = facility.Video;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _renderTexture;
            _player.playOnAwake = false;
            _player.sendFrameReadyEvents = true;
            // Match Tianlu's hover semantics: play the authored clip once,
            // then hold its last frame until the pointer leaves.
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.playbackSpeed = facility.PlaybackSpeed;
            if (facility.Video.audioTrackCount > 0)
            {
                // The embedded video track is the hover sound source. Keep it
                // silent until the pointer enters the prepared artwork.
                _player.audioOutputMode = VideoAudioOutputMode.Direct;
                _player.SetDirectAudioVolume(0, facility.AudioVolume);
                _player.SetDirectAudioMute(0, true);
            }
            else
            {
                _player.audioOutputMode = VideoAudioOutputMode.None;
            }
            _interactionRequested = true;
            _visible = false;
            _ready = false;
            _player.prepareCompleted -= HandlePrepared;
            _player.prepareCompleted += HandlePrepared;
            _player.frameReady -= HandleFrameReady;
            _player.frameReady += HandleFrameReady;
            _player.loopPointReached -= HandlePlaybackFinished;
            _player.loopPointReached += HandlePlaybackFinished;
            _player.Prepare();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionRequested = enabled;
            if (!enabled)
                SetHovered(false);
            RefreshPresentation();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible)
                SetHovered(false);
            RefreshPresentation();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (!_interactionEnabled)
                return;

            if (_currentHover != null && _currentHover != this)
                _currentHover.SetHovered(false);
            _currentHover = this;
            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_hovered)
                return;
            _hovered = false;
            _leaving = true;
            if (_currentHover == this)
                _currentHover = null;
            SetVideoAudioMuted(true);
            if (_ready && _visible)
            {
                if (_holdAtEnd)
                    ResetToFirstFrame();
                else
                    _player.Play();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_interactionEnabled)
                Clicked?.Invoke();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!_interactionEnabled)
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
            if (_hitMask == null || !_hitMask.isReadable)
                return true;
            return _hitMask.GetPixelBilinear(u, v).r >= 0.15f;
        }

        private void OnDestroy()
        {
            if (_currentHover == this)
                _currentHover = null;
            if (_player != null)
            {
                _player.prepareCompleted -= HandlePrepared;
                _player.loopPointReached -= HandlePlaybackFinished;
                _player.frameReady -= HandleFrameReady;
                SetVideoAudioMuted(true);
                _player.Stop();
                _player.targetTexture = null;
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                if (Application.isPlaying)
                    Destroy(_renderTexture);
                else
                    DestroyImmediate(_renderTexture);
            }
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeMaterial);
                else
                    DestroyImmediate(_runtimeMaterial);
            }
        }

        private void HandlePrepared(VideoPlayer player)
        {
            if (_ready)
                return;
            ResetToFirstFrame();
            StartCoroutine(RevealAfterPreparedFrame(player));
        }

        private System.Collections.IEnumerator RevealAfterPreparedFrame(VideoPlayer player)
        {
            yield return null;
            if (!_ready && player == _player && _player.isPrepared)
                HandleFrameReady(player, 0);
        }

        private void HandleFrameReady(VideoPlayer player, long frameIdx)
        {
            if (player != _player || _ready || !_player.isPrepared)
                return;
            _ready = true;
            RefreshPresentation();
            Ready?.Invoke(this);
        }

        private void HandlePlaybackFinished(VideoPlayer player)
        {
            if (player != _player || !_ready)
                return;
            player.Pause();
            if (_leaving)
                ResetToFirstFrame();
            else if (player.frameCount > 0)
                player.frame = (long)(player.frameCount / 2);
            SetVideoAudioMuted(true);
            _leaving = false;
        }

        private void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
                return;

            _hovered = hovered;
            _leaving = false;
            if (!_hovered && _currentHover == this)
                _currentHover = null;
            SetVideoAudioMuted(!_hovered);
            if (_ready)
            {
                if (_hovered && _visible && _interactionRequested)
                {
                    ResetToFirstFrame();
                    _player.Play();
                }
                else if (_hovered)
                    ResetToFirstFrame();
            }
        }

        private void Update()
        {
            if (_player == null || !_ready || !_hovered || !_player.isPlaying ||
                _player.length <= 0d)
                return;
            double stopTime = _holdAtEnd ? Mathf.Max(0f, (float)_player.length - 0.05f) : _player.length * 0.5d;
            if (_player.time >= stopTime)
            {
                _player.Pause();
                _player.time = stopTime;
                SetVideoAudioMuted(true);
            }
        }

        private void ResetToFirstFrame()
        {
            if (_player == null || !_player.isPrepared)
                return;
            _player.Pause();
            _player.time = 0d;
            _player.frame = 0;
        }

        private void SetVideoAudioMuted(bool muted)
        {
            if (_player == null || _player.clip == null ||
                _player.clip.audioTrackCount == 0 ||
                _player.audioOutputMode != VideoAudioOutputMode.Direct)
                return;
            _player.SetDirectAudioMute(0, muted);
        }

        private void RefreshPresentation()
        {
            bool shown = _visible && _ready;
            _interactionEnabled = shown && _interactionRequested;
            if (_image != null)
            {
                // 首帧准备完成前不让材质参与绘制，避免 RenderTexture 初始黑帧闪现。
                _image.enabled = shown;
                if (!shown)
                {
                    _image.color = Color.clear;
                }
                else
                {
                    // Preserve the video's authored first-frame brightness;
                    // hover changes playback and sound, not the artwork tint.
                    _image.color = Color.white;
                }
                _image.raycastTarget = _interactionEnabled;
            }
        }
    }
}
