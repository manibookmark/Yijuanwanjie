using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    public enum MapRewardMotionKind { None, LifePulse, QuietFloat }

    /// <summary>
    /// Owns only a sprite's child motion channel. MapElement still owns the
    /// parent hover scale, visibility and tint; no native particle state here.
    /// </summary>
    public sealed class MapRewardNodeMotion : MonoBehaviour
    {
        private MapRewardMotionKind _kind;
        private MapVfxState _state;
        private float _phase;
        private float _floatAmplitude;

        public void Initialize(MapRewardMotionKind kind, float phase, float subjectSize)
        {
            _kind = kind;
            _phase = phase;
            _floatAmplitude = subjectSize * 0.025f;
        }

        public void SetState(MapVfxState state)
        {
            _state = state;
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (_kind == MapRewardMotionKind.None ||
                (_state != MapVfxState.Available && _state != MapVfxState.Hovered))
                return;
            float time = Time.unscaledTime + _phase;
            if (_kind == MapRewardMotionKind.LifePulse)
            {
                float pulse = 1f + Mathf.Sin(time * 2.2f) * 0.014f;
                transform.localScale = new Vector3(pulse, 1f / pulse, 1f);
            }
            else
            {
                // Local displacement is relative to the subject size, never
                // to the source image's pixel dimensions or node position.
                transform.localPosition = new Vector3(0f,
                    Mathf.Sin(time * 1.4f) * _floatAmplitude, 0f);
            }
        }
    }
}
