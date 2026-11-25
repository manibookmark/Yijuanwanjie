using System;
using System.Collections;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// 只负责把已经异步准备好的入口图集播放一次，并在指定手势帧发出
    /// 台词 cue。它不加载资源、不认识音频，也不拥有 BOSS 战斗状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossEntranceTauntPlayer : MonoBehaviour
    {
        private const float ReturnToIdleBlendDuration = 0.1f;

        private SpriteRenderer _renderer;
        private BossEntranceRuntimeDescriptor _descriptor;
        private Sprite[] _sprites;
        private Coroutine _routine;
        private Sprite _capturedSprite;
        private Color _capturedColor;
        private Vector3 _capturedLocalPosition;
        private Vector3 _capturedLocalScale;
        private bool _hasCapturedPose;
        private bool _ownsSprites;
        private int _playVersion;
        private SpriteRenderer _outgoingFrameGhost;
        private Color _blendRendererColor;
        private bool _hasBlendRendererColor;

        public bool IsPrepared => _descriptor != null &&
                                  _sprites != null && _sprites.Length > 0;
        public bool IsPlaying => _routine != null;

        public bool Configure(BossEntranceRuntimePresentation presentation)
        {
            StopAndRestore();
            DestroySprites();
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null || presentation == null ||
                !presentation.IsReady ||
                !presentation.Descriptor.TryValidate(out _))
            {
                return false;
            }

            _descriptor = presentation.Descriptor;
            Vector2 entranceAnchor = new Vector2(
                _descriptor.entranceGroundAnchorX,
                _descriptor.entranceGroundAnchorY);
            Texture2D atlas = presentation.FrameAtlas;
            int frameWidth = atlas.width / _descriptor.atlasColumns;
            int frameHeight = atlas.height / _descriptor.atlasRows;
            _sprites = new Sprite[_descriptor.playbackFrameCount];
            _ownsSprites = true;
            for (int i = 0; i < _sprites.Length; i++)
            {
                int sourceFrame = _descriptor.firstFrame + i;
                int column = sourceFrame % _descriptor.atlasColumns;
                int rowFromTop = sourceFrame / _descriptor.atlasColumns;
                Rect rect = new Rect(
                    column * frameWidth,
                    atlas.height - (rowFromTop + 1) * frameHeight,
                    frameWidth,
                    frameHeight);
                _sprites[i] = Sprite.Create(
                    atlas,
                    rect,
                    entranceAnchor,
                    frameHeight);
            }
            return true;
        }

        /// <summary>
        /// Borrows the already sliced entrance frames from the encounter
        /// warmup owner. No loading or Sprite.Create is permitted here.
        /// </summary>
        internal bool ConfigurePrepared(BossPreparedPresentation presentation)
        {
            StopAndRestore();
            DestroySprites();
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null || presentation == null ||
                !presentation.HasEntrance ||
                !presentation.EntranceDescriptor.TryValidate(out _))
            {
                return false;
            }

            _descriptor = presentation.EntranceDescriptor;
            _sprites = presentation.EntranceSprites;
            _ownsSprites = false;
            return true;
        }

        public bool Play(
            Action onSpeechCue,
            Action prepareIdle,
            Action onComplete)
        {
            if (!IsPrepared || _renderer == null || !isActiveAndEnabled)
                return false;
            StopAndRestore();
            int version = _playVersion;
            _capturedSprite = _renderer.sprite;
            _capturedColor = _renderer.color;
            _capturedLocalPosition = transform.localPosition;
            _capturedLocalScale = transform.localScale;
            _hasCapturedPose = true;
            float visualScale = Mathf.Max(
                0.01f,
                _descriptor.visualScaleRelativeToIdle);
            transform.localScale = new Vector3(
                _capturedLocalScale.x * visualScale,
                _capturedLocalScale.y * visualScale,
                _capturedLocalScale.z);
            Coroutine started = StartCoroutine(
                PlayRoutine(onSpeechCue, prepareIdle, onComplete, version));
            if (version == _playVersion)
                _routine = started;
            return true;
        }

        private IEnumerator PlayRoutine(
            Action onSpeechCue,
            Action prepareIdle,
            Action onComplete,
            int version)
        {
            float elapsed = 0f;
            float duration = _sprites.Length /
                             Mathf.Max(0.1f, _descriptor.framesPerSecond);
            bool speechFired = false;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                int localFrame = Mathf.Clamp(
                    Mathf.FloorToInt(
                        elapsed * _descriptor.framesPerSecond),
                    0,
                    _sprites.Length - 1);
                ApplyFrame(localFrame);
                int sourceFrame = _descriptor.firstFrame + localFrame;
                if (!speechFired && sourceFrame >= _descriptor.speechFrame)
                {
                    speechFired = true;
                    onSpeechCue?.Invoke();
                    if (version != _playVersion) yield break;
                }
                yield return null;
            }

            if (!speechFired)
                onSpeechCue?.Invoke();
            if (version != _playVersion) yield break;
            ApplyFrame(_sprites.Length - 1);
            Sprite finalTauntFrame = _renderer != null
                ? _renderer.sprite
                : null;
            SpriteRenderer outgoing = CreateOutgoingFrameGhost();
            RestoreCapturedTransform();
            if (outgoing != null)
            {
                float visualScale = Mathf.Max(
                    0.01f,
                    _descriptor.visualScaleRelativeToIdle);
                outgoing.transform.localScale = new Vector3(
                    visualScale,
                    visualScale,
                    1f);
            }
            if (prepareIdle != null)
                prepareIdle();
            if (version != _playVersion) yield break;
            if (_renderer != null && _renderer.sprite == finalTauntFrame)
                RestoreCapturedFrame();

            if (outgoing != null)
            {
                Color outgoingStart = outgoing.color;
                Color idleTarget = _renderer != null
                    ? _renderer.color
                    : Color.white;
                _blendRendererColor = idleTarget;
                _hasBlendRendererColor = _renderer != null;
                if (_renderer != null)
                {
                    Color transparentIdle = idleTarget;
                    transparentIdle.a = 0f;
                    _renderer.color = transparentIdle;
                }
                float blendElapsed = 0f;
                while (blendElapsed < ReturnToIdleBlendDuration)
                {
                    blendElapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(
                        blendElapsed / ReturnToIdleBlendDuration);
                    Color nextOutgoing = outgoingStart;
                    nextOutgoing.a = outgoingStart.a * (1f - progress);
                    outgoing.color = nextOutgoing;
                    if (_renderer != null)
                    {
                        Color nextIdle = idleTarget;
                        nextIdle.a = idleTarget.a * progress;
                        _renderer.color = nextIdle;
                    }
                    yield return null;
                }
            }

            if (version != _playVersion) yield break;
            ClearReturnBlend();
            _routine = null;
            _capturedSprite = null;
            onComplete?.Invoke();
        }

        private void ApplyFrame(int index)
        {
            if (_renderer == null || _sprites == null || _sprites.Length == 0)
                return;
            Sprite sprite = _sprites[Mathf.Clamp(index, 0, _sprites.Length - 1)];
            if (sprite != null)
                _renderer.sprite = sprite;
            Color color = _renderer.color;
            color.a = 1f;
            _renderer.color = color;
        }

        private SpriteRenderer CreateOutgoingFrameGhost()
        {
            if (_renderer == null || _renderer.sprite == null)
                return null;
            var host = new GameObject("BossEntranceTauntEndBlend");
            host.transform.SetParent(_renderer.transform, false);
            var ghost = host.AddComponent<SpriteRenderer>();
            _outgoingFrameGhost = ghost;
            ghost.sprite = _renderer.sprite;
            ghost.color = _renderer.color;
            ghost.flipX = _renderer.flipX;
            ghost.flipY = _renderer.flipY;
            ghost.sharedMaterial = _renderer.sharedMaterial;
            ghost.sortingLayerID = _renderer.sortingLayerID;
            ghost.sortingOrder = _renderer.sortingOrder + 1;
            ghost.maskInteraction = _renderer.maskInteraction;
            return ghost;
        }

        private void StopAndRestore()
        {
            unchecked { _playVersion++; }
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            ClearReturnBlend();
            RestoreCapturedFrame();
        }

        private void ClearReturnBlend()
        {
            SpriteRenderer ghost = _outgoingFrameGhost;
            _outgoingFrameGhost = null;
            if (ghost != null)
            {
                ghost.gameObject.SetActive(false);
                Destroy(ghost.gameObject);
            }
            if (_hasBlendRendererColor)
            {
                _hasBlendRendererColor = false;
                if (_renderer != null)
                    _renderer.color = _blendRendererColor;
            }
        }

        private void RestoreCapturedFrame()
        {
            RestoreCapturedTransform();
            if (_renderer != null && _capturedSprite != null)
            {
                _renderer.sprite = _capturedSprite;
                _renderer.color = _capturedColor;
            }
            _capturedSprite = null;
        }

        private void RestoreCapturedTransform()
        {
            if (!_hasCapturedPose)
                return;
            transform.localPosition = _capturedLocalPosition;
            transform.localScale = _capturedLocalScale;
            _hasCapturedPose = false;
        }

        private void DestroySprites()
        {
            if (_sprites != null && _ownsSprites)
            {
                for (int i = 0; i < _sprites.Length; i++)
                {
                    if (_sprites[i] == null)
                        continue;
                    if (Application.isPlaying)
                        Destroy(_sprites[i]);
                    else
                        DestroyImmediate(_sprites[i]);
                }
            }
            _sprites = null;
            _ownsSprites = false;
            _descriptor = null;
        }

        private void OnDisable()
        {
            StopAndRestore();
        }

        private void OnDestroy()
        {
            StopAndRestore();
            DestroySprites();
        }
    }
}
