using System;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// 地图 Sprite 序列的无音频生命周期适配器。它唯一拥有运行时切片和帧时钟，
    /// 并从静态回退 Sprite 推导 pivot 与世界尺寸，换帧不会改变地图位置或大小。
    /// </summary>
    public sealed class MapSpriteSequenceAnimator : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private MapSpriteSequence _sequence;
        private Sprite _fallback;
        private Sprite[] _frames = Array.Empty<Sprite>();
        private float _elapsed;

        public int CurrentFrameIndex { get; private set; }
        public bool IsOneShot => _sequence != null &&
                                 _sequence.playOnceThenHoldFirstFrame;

        public void Bind(
            SpriteRenderer target,
            MapSpriteSequence sequence,
            Sprite fallback)
        {
            ReleaseFrames();
            _renderer = target;
            _sequence = sequence;
            _fallback = fallback;
            _elapsed = 0f;
            CurrentFrameIndex = 0;
            if (_renderer == null || _fallback == null ||
                _sequence == null || !_sequence.IsValid)
            {
                if (_renderer != null)
                    _renderer.sprite = _fallback;
                enabled = false;
                return;
            }

            _frames = SliceFrames(_sequence, _fallback);
            enabled = _frames.Length > 0 && !IsOneShot;
            ApplyFrame(0);
        }

        /// <summary>
        /// 从第 0 帧显式播放一次地图挑战动作。调用者决定“地图真正可见”的
        /// 语义时点，避免组件启用或节点临时隐藏时把动作提前消耗掉。
        /// </summary>
        public bool PlayOnce()
        {
            if (!IsOneShot || _renderer == null || _frames.Length == 0)
                return false;
            _elapsed = 0f;
            CurrentFrameIndex = 0;
            ApplyFrame(0);
            enabled = true;
            return true;
        }

        private void Update()
        {
            Advance(Time.unscaledDeltaTime);
        }

        /// <summary>Shared clock entry for runtime Update and an isolated Editor preview.</summary>
        public void Advance(float deltaSeconds)
        {
            if (!enabled || _sequence == null || !_sequence.IsValid ||
                _frames.Length == 0)
            {
                return;
            }

            _elapsed += Mathf.Max(0f, deltaSeconds);
            if (IsOneShot && _elapsed >= _sequence.DurationSeconds)
            {
                ApplyFrame(0);
                enabled = false;
                return;
            }
            int frame = _sequence.GetFrameIndex(_elapsed);
            if (frame == CurrentFrameIndex)
                return;
            ApplyFrame(frame);
        }

        private void ApplyFrame(int frame)
        {
            if (_renderer == null || _frames.Length == 0)
                return;
            CurrentFrameIndex = Mathf.Clamp(frame, 0, _frames.Length - 1);
            _renderer.sprite = _frames[CurrentFrameIndex];
        }

        private static Sprite[] SliceFrames(
            MapSpriteSequence sequence,
            Sprite fallback)
        {
            Texture2D atlas = sequence.frameAtlas;
            int frameWidth = atlas.width / sequence.atlasColumns;
            int frameHeight = atlas.height / sequence.atlasRows;
            if (!sequence.TryGetFrameLayout(
                    fallback,
                    out Vector2 pivot,
                    out float pixelsPerUnit,
                    out _))
            {
                return Array.Empty<Sprite>();
            }
            var frames = new Sprite[sequence.frameCount];
            for (int i = 0; i < frames.Length; i++)
            {
                int column = i % sequence.atlasColumns;
                int rowFromTop = i / sequence.atlasColumns;
                Rect rect = new Rect(
                    column * frameWidth,
                    atlas.height - (rowFromTop + 1) * frameHeight,
                    frameWidth,
                    frameHeight);
                frames[i] = Sprite.Create(
                    atlas,
                    rect,
                    pivot,
                    pixelsPerUnit,
                    0u,
                    SpriteMeshType.FullRect);
                frames[i].name = $"{atlas.name}_map_{i + 1:D2}";
            }
            return frames;
        }

        private void ReleaseFrames()
        {
            if (_frames == null)
                return;
            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i] == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(_frames[i]);
                else
                    DestroyImmediate(_frames[i]);
            }
            _frames = Array.Empty<Sprite>();
        }

        private void OnDestroy()
        {
            ReleaseFrames();
        }
    }
}
